using System;
using System.Collections.Generic;
using System.Globalization;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFlowOptimizer.Patches
{
    internal enum SmartTransportWaitPhase
    {
        PlanningAnchor,
        ValidatingCandidate,
        MovingToWait,
        Waiting,
        ReturningToPatient
    }

    internal sealed class SmartTransportWaitContext
    {
        internal StretcherComponent Stretcher;
        internal Entity Patient;
        internal Entity Transporter;
        internal TileObject ExaminationTransportPoint;
        internal Room ExaminationRoom;
        internal TileObject MovingSide;
        internal TileObject PatientSide;
        internal Vector2f OriginalStretcherPosition;
        internal Direction OriginalStretcherOrientation;
        internal int OriginalStretcherFloor;
        internal int WaitingFloor;
        internal Vector2i Anchor;
        internal Vector2i WaitPosition;
        internal float StartedAt;
        internal float WaitStartedAt;
        internal float WaitEndedAt;
        internal float RetentionEndedAt;
        internal bool HasStartedWaiting;
        internal bool HasEndedWaiting;
        internal bool HasEndedRetention;
        internal int EffectiveMaxProcedureMinutes;
        internal SmartTransportWaitPhase Phase;
        internal PathfinderJob PlanningJob;
        internal PathfinderJob CandidateJob;
        internal List<Vector2i> Candidates;
        internal int CandidateIndex;
        internal bool ShowingWaitingBubble;
        internal long LastPressureRescueSequence;
        internal readonly HashSet<Entity> EvaluatedPressureRescueHpoTargets =
            new HashSet<Entity>();
    }

    internal static class SmartTransportWaitRuntime
    {
        private const int PathfinderStepLimit = 9216;
        private const int PublicAreaRadius = 5;
        private const float PublicAreaIdealDistance = 3.5f;
        private const float PublicAreaMaximumRouteDistance = 7f;
        private const float PublicAreaNearDoorDistance = 2f;
        private const float PublicAreaNearDoorPenalty = 50f;
        private const float PublicAreaAnyDoorPenalty = 100f;
        private const float PublicAreaWallPreferenceBonus = 10f;
        private const int TransportWaitBubbleIndex = 1;
        private const float PlanningTimeoutSeconds = 5f;

        private static readonly Dictionary<StretcherComponent, SmartTransportWaitContext> Contexts =
            new Dictionary<StretcherComponent, SmartTransportWaitContext>();

        private static readonly System.Reflection.FieldInfo StretcherEntityField =
            AccessTools.Field(typeof(StretcherComponent), "m_entity");

        internal static bool HandleReleasing(StretcherComponent stretcher)
        {
            if (stretcher == null || stretcher.m_state == null)
            {
                return true;
            }

            // A direct autonomous continuation still drives the source
            // ReleasingMovingCharacter state through the normal release hook.
            // Do not create a second Smart Transport Wait context over it.
            if (SmartTransportContinuationRuntime
                    .HasActiveContinuation(stretcher))
            {
                return true;
            }

            // HPO Pressure Rescue already accepted a real preflight target.
            // The previous tick ended HFO's Wait context. Let this tick run
            // vanilla UpdateStateReleasingMovingCharacter so the real
            // ReleaseMovingCharacter() occurs and HPO can create its true
            // ReturnJob/window. Never recreate a fresh Smart Wait here.
            if (SmartTransportContinuationRuntime
                    .ShouldReleaseForHpoPressureRescue(stretcher))
            {
                return true;
            }

            if (!FlowOptimizerConfig.SmartTransportWait)
            {
                return true;
            }

            SmartTransportWaitContext context;
            if (!Contexts.TryGetValue(stretcher, out context))
            {
                if (stretcher.m_state.m_timeInState <= 1f)
                {
                    return true;
                }

                context = TryCreateContext(stretcher);
                if (context == null)
                {
                    return true;
                }

                Contexts[stretcher] = context;
                Log(context, "planning outside wait position");
                return false;
            }

            if (!IsContextUsable(context))
            {
                return Abandon(context, "context no longer valid");
            }

            float elapsedSeconds =
                stretcher.m_state.m_timeInState - context.StartedAt;

            if (elapsedSeconds > PlanningTimeoutSeconds &&
                (context.Phase == SmartTransportWaitPhase.PlanningAnchor ||
                 context.Phase == SmartTransportWaitPhase.ValidatingCandidate))
            {
                return Abandon(context, "wait-position planning timed out");
            }

            float actualWaitingSeconds =
                context.HasStartedWaiting
                    ? stretcher.m_state.m_timeInState -
                      context.WaitStartedAt
                    : 0f;

            if (context.Phase == SmartTransportWaitPhase.Waiting &&
                HasExceededMaximumActualWait(
                    context,
                    actualWaitingSeconds))
            {
                return Abandon(
                    context,
                    "maximum actual wait exceeded; releasing transporter",
                    false);
            }

            if (context.Phase == SmartTransportWaitPhase.PlanningAnchor)
            {
                return UpdateAnchorPlanning(context);
            }

            if (context.Phase == SmartTransportWaitPhase.ValidatingCandidate)
            {
                return UpdateCandidateValidation(context);
            }

            if (context.Phase == SmartTransportWaitPhase.MovingToWait)
            {
                return UpdateMovingToWait(context);
            }

            if (context.Phase == SmartTransportWaitPhase.Waiting ||
                context.Phase == SmartTransportWaitPhase.ReturningToPatient)
            {
                if (!CanKeepWaiting(context))
                {
                    return Abandon(context, "patient or transporter can no longer wait");
                }

                if (context.Phase == SmartTransportWaitPhase.Waiting)
                {
                    if (TryPressureRescue(context))
                    {
                        return false;
                    }

                    ShowWaitingBubble(context);
                }

                return false;
            }

            return Abandon(context, "unknown wait phase");
        }

        internal static bool TryBeginRequestedTransport(
            StretcherComponent stretcher,
            ref Entity movingCharacter,
            ref Vehicle requestedVehicle,
            ref bool result,
            out SmartTransportWaitContext state)
        {
            state = null;

            SmartTransportWaitContext context;
            if (stretcher == null || !Contexts.TryGetValue(stretcher, out context))
            {
                return true;
            }

            if (!IsContextUsable(context))
            {
                LogFinalOutcome(
                    context,
                    "invalidated",
                    "context no longer valid during chained request");
                RemoveContext(context);
                result = false;
                return false;
            }

            MarkWaitingEnded(context);
            HideWaitingBubble(context);
            movingCharacter = context.Transporter;

            bool retainedWheelchair = context.PatientSide.HasTag("wheelchair_patient");
            if (requestedVehicle == Vehicle.STRETCHER && retainedWheelchair)
            {
                LogFinalOutcome(
                    context,
                    "invalidated",
                    "retained wheelchair is incompatible with requested stretcher");
                RemoveContext(context);
                SmartTransportContinuationRuntime.SuppressNextRelease(stretcher);
                stretcher.ReleaseMovingCharacter();
                result = false;
                return false;
            }

            if (requestedVehicle == Vehicle.WHEELCHAIR && !retainedWheelchair)
            {
                requestedVehicle = Vehicle.STRETCHER;
            }

            WalkComponent walk = context.Transporter.GetComponent<WalkComponent>();
            if (walk == null || context.ExaminationTransportPoint == null)
            {
                LogFinalOutcome(
                    context,
                    "invalidated",
                    "transporter or examination transport point lost during chained request");
                RemoveContext(context);
                result = false;
                return false;
            }

            Vector2i pickup = context.ExaminationTransportPoint.GetDefaultStretcherTile();
            int pickupFloor = context.ExaminationTransportPoint.GetFloorIndex();

            if (walk.GetFloorIndex() != pickupFloor ||
                walk.GetCurrentTile() != pickup ||
                walk.IsBusy())
            {
                if (context.Phase != SmartTransportWaitPhase.ReturningToPatient ||
                    !walk.IsBusy())
                {
                    walk.SetDestination(pickup, pickupFloor, MovementType.WALKING);
                    context.Phase = SmartTransportWaitPhase.ReturningToPatient;
                    Log(context, "returning to patient before chained transport");
                }

                result = false;
                return false;
            }

            context.Phase = SmartTransportWaitPhase.ReturningToPatient;
            MarkRetentionEnded(context);
            state = context;
            return true;
        }

        internal static void CompleteRequestedTransport(
            StretcherComponent stretcher,
            SmartTransportWaitContext context,
            bool result)
        {
            if (stretcher == null || context == null || !result)
            {
                return;
            }

            stretcher.m_state.m_originalStretcherPosition = context.OriginalStretcherPosition;
            stretcher.m_state.m_originalStretcherOrientation = context.OriginalStretcherOrientation;
            stretcher.m_state.m_originalStretcherFloor = context.OriginalStretcherFloor;

            Behavior behavior = context.Transporter == null
                ? null
                : context.Transporter.GetComponent<Behavior>();
            if (behavior != null)
            {
                behavior.ReceiveMessage(new Message(Messages.OVERRIDE_BY_MOVING_STRETCHER));
            }

            MarkWaitingEnded(context);
            LogFinalOutcome(
                context,
                "resumed",
                "patient transport resumed with retained vehicle");
            RemoveContext(context);
        }

        private static bool TryPressureRescue(
            SmartTransportWaitContext context)
        {
            if (context == null ||
                context.Phase != SmartTransportWaitPhase.Waiting ||
                !FlowOptimizerConfig.SmartTransportPressureRescue)
            {
                return false;
            }

            long evaluatedSequence;
            Entity targetPatient;
            SmartTransportPressureRescueResult result =
                SmartTransportContinuationRuntime
                    .TryStartPressureRescue(
                        context.Stretcher,
                        context.Patient,
                        context.LastPressureRescueSequence,
                        context.EvaluatedPressureRescueHpoTargets,
                        out evaluatedSequence,
                        out targetPatient);

            if (evaluatedSequence > 0L)
            {
                context.LastPressureRescueSequence =
                    evaluatedSequence;
            }

            if (result !=
                SmartTransportPressureRescueResult.Started)
            {
                return false;
            }

            MarkWaitingEnded(context);
            MarkRetentionEnded(context);
            HideWaitingBubble(context);
            LogFinalOutcome(
                context,
                "pressure-rescue",
                "retained transporter released to another valid Patient mission" +
                "; target=" +
                HfoDiagnostics.EntityName(targetPatient));
            RemoveContext(context);
            return true;
        }

        private static bool HasExceededMaximumActualWait(
            SmartTransportWaitContext context,
            float elapsedSeconds)
        {
            if (context == null ||
                elapsedSeconds < 0f ||
                DayTime.Instance == null)
            {
                return false;
            }

            float maximumSeconds =
                DayTime.Instance.IngameTimeHoursToRealTimeSeconds(
                    (float)context.EffectiveMaxProcedureMinutes / 60f);

            return maximumSeconds > 0f &&
                   elapsedSeconds > maximumSeconds;
        }

        internal static bool ShouldReportLogicalIdle(
            StretcherComponent stretcher)
        {
            SmartTransportWaitContext context;
            if (stretcher == null ||
                !Contexts.TryGetValue(stretcher, out context) ||
                context == null ||
                context.Stretcher != stretcher ||
                context.Phase == SmartTransportWaitPhase.PlanningAnchor ||
                context.Phase == SmartTransportWaitPhase.ValidatingCandidate)
            {
                return false;
            }

            return stretcher.m_state != null &&
                   stretcher.m_state.m_stretcherState ==
                       StretcherComponentState.ReleasingMovingCharacter &&
                   IsContextUsable(context);
        }

        internal static void Clear()
        {
            List<SmartTransportWaitContext> contexts =
                new List<SmartTransportWaitContext>(Contexts.Values);
            for (int i = 0; i < contexts.Count; i++)
            {
                HideWaitingBubble(contexts[i]);
                AbortJob(contexts[i].PlanningJob);
                AbortJob(contexts[i].CandidateJob);
            }

            Contexts.Clear();
        }

        internal static bool TryGetWaitingPatient(
            Entity transporter,
            out Entity patient)
        {
            patient = null;
            if (transporter == null)
            {
                return false;
            }

            foreach (SmartTransportWaitContext context in Contexts.Values)
            {
                if (context != null &&
                    context.Transporter == transporter &&
                    context.Patient != null &&
                    context.Phase == SmartTransportWaitPhase.Waiting)
                {
                    patient = context.Patient;
                    return true;
                }
            }

            return false;
        }

        private static SmartTransportWaitContext TryCreateContext(StretcherComponent stretcher)
        {
            if (stretcher.m_state.m_stretcherState !=
                    StretcherComponentState.ReleasingMovingCharacter ||
                stretcher.m_state.m_stretcherUse != StretcherUse.RETURN_STRETCHER)
            {
                return null;
            }

            Entity patient = GetStretcherEntity(stretcher);
            HospitalizationComponent hospitalization = patient == null
                ? null
                : patient.GetComponent<HospitalizationComponent>();
            if (hospitalization == null || hospitalization.m_state == null ||
                hospitalization.m_state.m_hospitalizationState !=
                    HospitalizationState.BeingTransportedToExamination ||
                hospitalization.m_state.m_bed == null ||
                hospitalization.m_state.m_bed.GetEntity() == null)
            {
                return null;
            }

            Entity transporter = stretcher.m_state.m_movingCharacter == null
                ? null
                : stretcher.m_state.m_movingCharacter.GetEntity();
            BehaviorNurse nurse = transporter == null
                ? null
                : transporter.GetComponent<BehaviorNurse>();
            EmployeeComponent employee = transporter == null
                ? null
                : transporter.GetComponent<EmployeeComponent>();
            WalkComponent walk = transporter == null
                ? null
                : transporter.GetComponent<WalkComponent>();

            TileObject movingSide = stretcher.m_state.m_stretcherMovingCharacterSide == null
                ? null
                : stretcher.m_state.m_stretcherMovingCharacterSide.GetEntity();
            TileObject patientSide = stretcher.m_state.m_stretcherPatientSide == null
                ? null
                : stretcher.m_state.m_stretcherPatientSide.GetEntity();
            TileObject transportPoint = stretcher.m_state.m_destinationBed == null
                ? null
                : stretcher.m_state.m_destinationBed.GetEntity();

            if (nurse == null || employee == null || walk == null ||
                movingSide == null || patientSide == null || transportPoint == null ||
                !movingSide.IsAttachedToCharacter() ||
                !patientSide.IsAttachedToCharacter() ||
                movingSide.User != transporter ||
                patientSide.User != transporter)
            {
                return null;
            }

            Room examinationRoom = MapScriptInterface.Instance.GetRoomAt(
                transportPoint.m_state.m_position,
                transportPoint.GetFloorIndex());
            if (!IsPatientProcedureRoom(examinationRoom))
            {
                return null;
            }

            if (!IsEmployeeStillUsable(employee))
            {
                return null;
            }

            string examinationId;
            float estimatedProcedureSeconds;
            string durationSource;
            bool hasDurationEstimate =
                TryGetEstimatedProcedureSeconds(
                    patient,
                    out examinationId,
                    out estimatedProcedureSeconds,
                    out durationSource);

            if (!hasDurationEstimate &&
                !TryGetWaitCompatibleUnestimatedProcedure(
                    patient,
                    out examinationId,
                    out durationSource))
            {
                LogDecision(
                    patient,
                    transporter,
                    "fallback to vanilla: procedure duration is not safely estimable " +
                    "and script is not a known retained-wait-compatible native procedure");
                return null;
            }

            if (DayTime.Instance == null)
            {
                LogDecision(
                    patient,
                    transporter,
                    "fallback to vanilla: DayTime is unavailable for duration conversion");
                return null;
            }

            int effectiveMaxProcedureMinutes =
                GetStableWaitLimitMinutes(patient, examinationId);
            float maxProcedureSeconds =
                DayTime.Instance.IngameTimeHoursToRealTimeSeconds(
                    (float)effectiveMaxProcedureMinutes / 60f);

            if (hasDurationEstimate)
            {
                float estimatedProcedureMinutes =
                    DayTime.Instance.RealTimeSecondsToIngameTimeHours(
                        estimatedProcedureSeconds) * 60f;

                if (estimatedProcedureSeconds > maxProcedureSeconds)
                {
                    LogDecision(
                        patient,
                        transporter,
                        "fallback to vanilla: examination=" + examinationId +
                        "; estimatedProcedureMinutes=" +
                        estimatedProcedureMinutes.ToString("0.0") +
                        "; maxProcedureMinutes=" +
                        effectiveMaxProcedureMinutes +
                        "; baseMaxProcedureMinutes=" +
                        FlowOptimizerConfig.SmartTransportWaitMaxProcedureMinutes +
                        "; variationMinutes=+/-" +
                        FlowOptimizerConfig
                            .SmartTransportWaitMaxProcedureVariationMinutes +
                        "; durationSource=" + durationSource);
                    return null;
                }

                LogDecision(
                    patient,
                    transporter,
                    "duration accepted: examination=" + examinationId +
                    "; estimatedProcedureMinutes=" +
                    estimatedProcedureMinutes.ToString("0.0") +
                    "; maxProcedureMinutes=" +
                    effectiveMaxProcedureMinutes +
                    "; baseMaxProcedureMinutes=" +
                    FlowOptimizerConfig.SmartTransportWaitMaxProcedureMinutes +
                    "; variationMinutes=+/-" +
                    FlowOptimizerConfig
                        .SmartTransportWaitMaxProcedureVariationMinutes +
                    "; durationSource=" + durationSource);
            }
            else
            {
                LogDecision(
                    patient,
                    transporter,
                    "duration unbounded but wait-compatible: examination=" +
                    examinationId +
                    "; maxRetainedWaitMinutes=" +
                    effectiveMaxProcedureMinutes +
                    "; baseMaxProcedureMinutes=" +
                    FlowOptimizerConfig.SmartTransportWaitMaxProcedureMinutes +
                    "; variationMinutes=+/-" +
                    FlowOptimizerConfig
                        .SmartTransportWaitMaxProcedureVariationMinutes +
                    "; durationSource=" + durationSource);
            }

            SmartTransportWaitContext context = new SmartTransportWaitContext();
            context.Stretcher = stretcher;
            context.Patient = patient;
            context.Transporter = transporter;
            context.ExaminationTransportPoint = transportPoint;
            context.ExaminationRoom = examinationRoom;
            context.MovingSide = movingSide;
            context.PatientSide = patientSide;
            context.OriginalStretcherPosition = stretcher.m_state.m_originalStretcherPosition;
            context.OriginalStretcherOrientation = stretcher.m_state.m_originalStretcherOrientation;
            context.OriginalStretcherFloor = stretcher.m_state.m_originalStretcherFloor;
            context.WaitingFloor = walk.GetFloorIndex();
            context.StartedAt = stretcher.m_state.m_timeInState;
            context.EffectiveMaxProcedureMinutes =
                effectiveMaxProcedureMinutes;
            context.Phase = SmartTransportWaitPhase.PlanningAnchor;

            Vector2i start = walk.GetCurrentTile();
            if (walk.Floor == null)
            {
                return null;
            }

            Room currentRoom = walk.Floor.GetRoomTileSafe(start.m_x, start.m_y);
            if (currentRoom != examinationRoom)
            {
                context.Anchor = start;
                context.Candidates = CollectCandidates(context);
                if (context.Candidates.Count == 0)
                {
                    return null;
                }

                StartCandidateValidation(context);
                return context;
            }

            TileObject homeBed = hospitalization.m_state.m_bed.GetEntity();
            Vector2i target = homeBed.GetDefaultStretcherTile();
            int targetFloor = homeBed.GetFloorIndex();
            if (targetFloor != context.WaitingFloor)
            {
                KeyValuePair<List<WalkMidpoint>, float> waypoints =
                    GridMap.GetInstance().GetWaypoints(
                        context.WaitingFloor,
                        start,
                        targetFloor,
                        target,
                        nurse.GetAccessRights());
                if (waypoints.Key == null || waypoints.Key.Count == 0)
                {
                    return null;
                }

                target = waypoints.Key[0].m_destination.ToVector2i();
            }

            if (target == start)
            {
                return null;
            }

            context.PlanningJob = CreatePathfinderJob(
                start,
                target,
                walk.Floor,
                nurse);
            if (context.PlanningJob == null)
            {
                return null;
            }

            context.PlanningJob.TryToStart(ThreadedJob.THREAD_CATEGORY_PATHFINDING);
            return context;
        }

        private static bool UpdateAnchorPlanning(SmartTransportWaitContext context)
        {
            PathfinderJob job = context.PlanningJob;
            if (job == null)
            {
                return Abandon(context, "missing approach pathfinder");
            }

            if (!job.IsDone)
            {
                if (!job.IsRunning)
                {
                    job.TryToStart(ThreadedJob.THREAD_CATEGORY_PATHFINDING);
                }
                return false;
            }

            if (!HasRoute(job))
            {
                return Abandon(context, "no route from examination toward home");
            }

            Vector2i anchor;
            if (!TryFindOutsideAnchor(
                    context,
                    job.m_result.m_route,
                    out anchor))
            {
                return Abandon(context, "route does not expose a safe outside anchor");
            }

            context.Anchor = anchor;
            context.PlanningJob = null;
            context.Candidates = CollectCandidates(context);
            if (context.Candidates.Count == 0)
            {
                return Abandon(context, "no nearby public waiting tile");
            }

            StartCandidateValidation(context);
            return false;
        }

        private static bool UpdateCandidateValidation(SmartTransportWaitContext context)
        {
            PathfinderJob job = context.CandidateJob;
            if (job == null)
            {
                return Abandon(context, "missing candidate pathfinder");
            }

            if (!job.IsDone)
            {
                if (!job.IsRunning)
                {
                    job.TryToStart(ThreadedJob.THREAD_CATEGORY_PATHFINDING);
                }
                return false;
            }

            if (!HasRoute(job) ||
                !IsVehicleFootprintSafe(
                    context,
                    context.Candidates[context.CandidateIndex],
                    job.m_result.m_route))
            {
                context.CandidateIndex++;
                if (context.CandidateIndex >= context.Candidates.Count)
                {
                    return Abandon(context, "no candidate fits attached transport equipment");
                }

                StartCandidateValidation(context);
                return false;
            }

            int roll = UnityEngine.Random.Range(0, 100);
            if (roll >= FlowOptimizerConfig.SmartTransportWaitChance)
            {
                return Abandon(
                    context,
                    "valid wait skipped by chance roll " +
                    roll + "/" + FlowOptimizerConfig.SmartTransportWaitChance);
            }

            context.WaitPosition = context.Candidates[context.CandidateIndex];
            WalkComponent walk = context.Transporter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return Abandon(context, "transporter lost WalkComponent");
            }

            walk.SetDestination(
                context.WaitPosition,
                context.WaitingFloor,
                MovementType.WALKING);
            context.Phase = SmartTransportWaitPhase.MovingToWait;
            context.CandidateJob = null;
            Log(
                context,
                "wait accepted at " +
                context.WaitPosition.m_x + "," + context.WaitPosition.m_y +
                "; chanceRoll=" + roll);
            return false;
        }

        private static bool UpdateMovingToWait(SmartTransportWaitContext context)
        {
            WalkComponent walk = context.Transporter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return Abandon(context, "transporter lost WalkComponent while staging");
            }

            if (walk.IsBusy())
            {
                return false;
            }

            Floor floor = walk.Floor;
            if (walk.GetFloorIndex() != context.WaitingFloor ||
                walk.GetCurrentTile() != context.WaitPosition ||
                floor == null ||
                !IsBaseCandidateSafe(context, floor, context.WaitPosition) ||
                !IsCurrentVehicleFootprintSafe(context))
            {
                return Abandon(context, "staging destination became unsafe");
            }

            context.Phase = SmartTransportWaitPhase.Waiting;
            context.WaitStartedAt =
                GetCurrentStretcherStateTime(context);
            context.HasStartedWaiting = true;
            context.HasEndedWaiting = false;
            ShowWaitingBubble(context);
            Log(
                context,
                "transporter waiting outside examination room" +
                "; retainedMinutes=" +
                ToIngameMinutes(
                    GetRetainedSeconds(context)).ToString(
                        "0.0",
                        CultureInfo.InvariantCulture));
            return false;
        }

        private static void StartCandidateValidation(SmartTransportWaitContext context)
        {
            WalkComponent walk = context.Transporter.GetComponent<WalkComponent>();
            Behavior behavior = context.Transporter.GetComponent<Behavior>();
            if (walk == null || walk.Floor == null || behavior == null ||
                context.Candidates == null ||
                context.CandidateIndex < 0 ||
                context.CandidateIndex >= context.Candidates.Count)
            {
                context.CandidateJob = null;
                return;
            }

            context.CandidateJob = CreatePathfinderJob(
                walk.GetCurrentTile(),
                context.Candidates[context.CandidateIndex],
                walk.Floor,
                behavior);
            context.Phase = SmartTransportWaitPhase.ValidatingCandidate;
            if (context.CandidateJob != null)
            {
                context.CandidateJob.TryToStart(
                    ThreadedJob.THREAD_CATEGORY_PATHFINDING);
            }
        }

        private static PathfinderJob CreatePathfinderJob(
            Vector2i start,
            Vector2i end,
            Floor floor,
            Behavior behavior)
        {
            if (floor == null || behavior == null)
            {
                return null;
            }

            PathfinderFlags flags =
                SettingsManager.Instance.m_gameSettings.m_8DirMovement
                    ? PathfinderFlags.DIAGONALS
                    : (PathfinderFlags)0;

            return new PathfinderJob(
                start,
                end,
                floor,
                PathfinderStepLimit,
                flags,
                (int)behavior.GetAccessRights(),
                (int)behavior.GetDefaultAccessRights(),
                behavior.GetLookAheadDistance());
        }

        private static bool TryFindOutsideAnchor(
            SmartTransportWaitContext context,
            PathfinderRoute route,
            out Vector2i anchor)
        {
            anchor = Vector2i.ZERO_VECTOR;
            if (route == null || route.Nodes == null || route.Nodes.Count == 0 ||
                context.ExaminationRoom == null ||
                context.WaitingFloor < 0 ||
                Hospital.Instance == null ||
                context.WaitingFloor >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[context.WaitingFloor];
            bool sawExaminationRoom = false;
            for (int i = 0; i < route.Nodes.Count; i++)
            {
                Vector2i position = route.Nodes[i].Position;
                Room room = floor.GetRoomTileSafe(position.m_x, position.m_y);
                if (room == context.ExaminationRoom)
                {
                    sawExaminationRoom = true;
                    continue;
                }

                if (sawExaminationRoom)
                {
                    anchor = position;
                    return true;
                }
            }

            return false;
        }

        private static List<Vector2i> CollectCandidates(
            SmartTransportWaitContext context)
        {
            List<CandidateScore> scored = new List<CandidateScore>();
            if (Hospital.Instance == null ||
                context.WaitingFloor < 0 ||
                context.WaitingFloor >= Hospital.Instance.m_floors.Count)
            {
                return new List<Vector2i>();
            }

            Floor floor = Hospital.Instance.m_floors[context.WaitingFloor];
            Behavior behavior = context.Transporter.GetComponent<Behavior>();
            GridMap gridMap = GridMap.GetInstance();
            if (floor == null || behavior == null || gridMap == null)
            {
                return new List<Vector2i>();
            }

            for (int dx = -PublicAreaRadius; dx <= PublicAreaRadius; dx++)
            {
                for (int dy = -PublicAreaRadius; dy <= PublicAreaRadius; dy++)
                {
                    Vector2i candidate =
                        new Vector2i(context.Anchor.m_x + dx, context.Anchor.m_y + dy);
                    if (!IsBaseCandidateSafe(
                            context,
                            floor,
                            candidate))
                    {
                        continue;
                    }

                    float routeDistance = gridMap.GetDistance(
                        context.WaitingFloor,
                        context.Anchor,
                        context.WaitingFloor,
                        candidate,
                        behavior.GetAccessRights());
                    if (routeDistance < 0f ||
                        routeDistance >= float.MaxValue ||
                        routeDistance > PublicAreaMaximumRouteDistance)
                    {
                        continue;
                    }

                    float score =
                        Math.Abs(routeDistance - PublicAreaIdealDistance) +
                        GetCandidateJitter(context.Patient, candidate);
                    if (routeDistance < PublicAreaNearDoorDistance)
                    {
                        score += PublicAreaNearDoorPenalty;
                    }

                    if (HasNearbyDoor(floor, candidate))
                    {
                        score += PublicAreaAnyDoorPenalty;
                    }
                    else if (HasAdjacentWall(floor, candidate))
                    {
                        score -= PublicAreaWallPreferenceBonus;
                    }

                    CandidateScore item = new CandidateScore();
                    item.Position = candidate;
                    item.Score = score;
                    scored.Add(item);
                }
            }

            scored.Sort(delegate(CandidateScore left, CandidateScore right)
            {
                return left.Score.CompareTo(right.Score);
            });

            List<Vector2i> result = new List<Vector2i>();
            for (int i = 0; i < scored.Count; i++)
            {
                result.Add(scored[i].Position);
            }

            return result;
        }

        private sealed class CandidateScore
        {
            internal Vector2i Position;
            internal float Score;
        }

        private static bool IsBaseCandidateSafe(
            SmartTransportWaitContext context,
            Floor floor,
            Vector2i candidate)
        {
            if (!IsInBounds(floor, candidate))
            {
                return false;
            }

            Room room = floor.GetRoomTileSafe(candidate.m_x, candidate.m_y);
            if (room == context.ExaminationRoom || !IsAllowedPublicRoom(room))
            {
                return false;
            }

            if (floor.IsAnyObjectAt(candidate))
            {
                return false;
            }

            Entity reserved =
                floor.m_mapPersistentData.m_tiles[candidate.m_x, candidate.m_y]
                    .m_user.GetEntity();
            return reserved == null;
        }

        private static bool IsVehicleFootprintSafe(
            SmartTransportWaitContext context,
            Vector2i transporterPosition,
            PathfinderRoute route)
        {
            if (route == null || route.Nodes == null || route.Nodes.Count < 2)
            {
                return false;
            }

            Direction transporterDirection =
                route.Nodes[route.Nodes.Count - 2].Direction.GetOppositeDirection();
            return IsVehicleFootprintSafe(
                context,
                new Vector2f(transporterPosition),
                transporterDirection);
        }

        private static bool IsCurrentVehicleFootprintSafe(
            SmartTransportWaitContext context)
        {
            WalkComponent walk = context.Transporter.GetComponent<WalkComponent>();
            AnimModelComponent animation =
                context.Transporter.GetComponent<AnimModelComponent>();
            if (walk == null || animation == null)
            {
                return false;
            }

            return IsVehicleFootprintSafe(
                context,
                walk.m_state.m_currentPosition,
                animation.m_state.m_direction);
        }

        private static bool IsVehicleFootprintSafe(
            SmartTransportWaitContext context,
            Vector2f transporterPosition,
            Direction transporterDirection)
        {
            if (Hospital.Instance == null ||
                context.WaitingFloor < 0 ||
                context.WaitingFloor >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[context.WaitingFloor];
            if (floor == null)
            {
                return false;
            }

            if (!IsAttachedObjectPositionSafe(
                    context,
                    floor,
                    context.MovingSide,
                    transporterPosition,
                    transporterDirection))
            {
                return false;
            }

            if (context.PatientSide != context.MovingSide &&
                !IsAttachedObjectPositionSafe(
                    context,
                    floor,
                    context.PatientSide,
                    transporterPosition,
                    transporterDirection))
            {
                return false;
            }

            return true;
        }

        private static bool IsAttachedObjectPositionSafe(
            SmartTransportWaitContext context,
            Floor floor,
            TileObject attachedObject,
            Vector2f transporterPosition,
            Direction transporterDirection)
        {
            if (attachedObject == null)
            {
                return false;
            }

            Vector2f offset = TileObject.TransformOffsetForDirection(
                attachedObject.GetCharacterAttachmentOffset(),
                transporterDirection);
            Vector2i transporterTile =
                transporterPosition.ToVector2i();
            Vector2i tile =
                transporterPosition.add(offset).ToVector2i();

            if (!IsInBounds(floor, transporterTile) ||
                !IsInBounds(floor, tile) ||
                floor.IsAnyObjectAt(tile))
            {
                return false;
            }

            Room footprintRoom =
                floor.GetRoomTileSafe(tile.m_x, tile.m_y);
            if (footprintRoom == context.ExaminationRoom ||
                !IsAllowedPublicRoom(footprintRoom))
            {
                return false;
            }

            if (tile != transporterTile &&
                !floor.IsAccessibleIgnoreDoors(
                    transporterTile,
                    tile))
            {
                return false;
            }

            Entity reserved =
                floor.m_mapPersistentData
                    .m_tiles[tile.m_x, tile.m_y]
                    .m_user.GetEntity();
            return reserved == null ||
                   reserved == context.Transporter;
        }

        internal static bool TryGetEstimatedProcedureSeconds(
            Entity patient,
            out string examinationId,
            out float estimatedSeconds,
            out string source)
        {
            examinationId = "<unknown>";
            estimatedSeconds = 0f;
            source = "unknown";

            ProcedureComponent procedure = patient == null
                ? null
                : patient.GetComponent<ProcedureComponent>();
            ProcedureScript script =
                procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_currentProcedureScript == null
                    ? null
                    : procedure.m_state.m_currentProcedureScript.GetEntity();

            if (script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_examination == null ||
                script.m_stateData.m_examination.Entry == null)
            {
                return false;
            }

            GameDBExamination examination =
                script.m_stateData.m_examination.Entry;
            examinationId = examination.DatabaseID.ToString();

            float declaredSeconds = 0f;
            if (examination.DurationHours > 0f && DayTime.Instance != null)
            {
                declaredSeconds =
                    DayTime.Instance.IngameTimeHoursToRealTimeSeconds(
                        examination.DurationHours);
            }

            float scriptSeconds;
            string scriptSource;
            if (!TryGetScriptEstimatedSeconds(
                    script,
                    out scriptSeconds,
                    out scriptSource))
            {
                if (declaredSeconds <= 0f)
                {
                    return false;
                }

                estimatedSeconds = declaredSeconds;
                source = "GameDBExamination.DurationHours";
                return true;
            }

            estimatedSeconds =
                scriptSeconds > declaredSeconds
                    ? scriptSeconds
                    : declaredSeconds;
            source =
                declaredSeconds > scriptSeconds
                    ? "GameDBExamination.DurationHours"
                    : scriptSource;
            return estimatedSeconds > 0f;
        }

        private static bool TryGetScriptEstimatedSeconds(
            ProcedureScript script,
            out float estimatedSeconds,
            out string source)
        {
            estimatedSeconds = 0f;
            source = "unknown";

            if (script is ProcedureScriptExaminationVisualTest)
            {
                estimatedSeconds = script.GetParam(2) + 5f;
                source = "VisualTest action+overhead";
                return IsFinitePositive(estimatedSeconds);
            }

            if (script is ProcedureScriptExaminationAngiography)
            {
                estimatedSeconds = script.GetParam(1) * 2f + 15f;
                source = "Angiography two actions+overhead";
                return IsFinitePositive(estimatedSeconds);
            }

            if (script is ProcedureScriptExaminationRadiology)
            {
                estimatedSeconds = script.GetParam(2) * 2f + 25f;
                source = "Radiology two actions+overhead";
                return IsFinitePositive(estimatedSeconds);
            }

            if (script is ProcedureScriptExaminationXray)
            {
                estimatedSeconds = script.GetParam(2) * 2f + 20f;
                source = "Xray two actions+overhead";
                return IsFinitePositive(estimatedSeconds);
            }

            if (script is ProcedureScriptExaminationGeneral)
            {
                estimatedSeconds = script.GetParam(2) + 12f;
                source = "General examination action+overhead";
                return IsFinitePositive(estimatedSeconds);
            }

            if (script is ProcedureScriptExaminationSmallEquipment)
            {
                estimatedSeconds = script.GetParam(2) + 10f;
                source = "SmallEquipment action+overhead";
                return IsFinitePositive(estimatedSeconds);
            }

            if (script is ProcedureScriptExaminationSpecialEquipment)
            {
                estimatedSeconds = script.GetParam(2) + 12f;
                source = "SpecialEquipment action+overhead";
                return IsFinitePositive(estimatedSeconds);
            }

            if (script is ProcedureScriptExaminationGeneralEquipment)
            {
                estimatedSeconds = script.GetParam(2) + 12f;
                source = "GeneralEquipment action+overhead";
                return IsFinitePositive(estimatedSeconds);
            }

            if (script is ProcedureScriptExaminationOptionalEquipment)
            {
                GameDBTweakableFloat multiplier =
                    Database.Instance == null
                        ? null
                        : Database.Instance.GetEntry<GameDBTweakableFloat>(
                            "TWEAKABLE_OPTIONAL_EQUIPMENT_MULTIPLIER");
                if (multiplier == null)
                {
                    return false;
                }

                estimatedSeconds =
                    script.GetParam(2) * multiplier.Value + 12f;
                source = "OptionalEquipment action+overhead";
                return IsFinitePositive(estimatedSeconds);
            }

            if (script is ProcedureScriptExaminationPhysicalChest)
            {
                if (script.m_stateData.m_procedureScene == null ||
                    script.m_stateData.m_procedureScene.Doctor == null ||
                    script.m_stateData.m_procedureScene.Doctor.GetEntity() == null)
                {
                    return false;
                }

                Entity doctor =
                    script.m_stateData.m_procedureScene.Doctor.GetEntity();
                EmployeeComponent employee =
                    doctor.GetComponent<EmployeeComponent>();
                if (employee == null)
                {
                    return false;
                }

                float skill =
                    employee.GetSkillLevel(
                        "SKILL_DOC_QUALIF_GENERAL_MEDICINE");
                float activeSeconds = 7f - skill;
                if (activeSeconds < 1f)
                {
                    activeSeconds = 1f;
                }

                estimatedSeconds = activeSeconds + 12f;
                source = "PhysicalChest native skill duration+overhead";
                return IsFinitePositive(estimatedSeconds);
            }

            // Some native scripts have variable duration and are admitted separately
            // by TryGetWaitCompatibleUnestimatedProcedure(), where the runtime hard
            // cap remains authoritative. Unknown/custom scripts stay conservative.
            return false;
        }

        private static int GetStableWaitLimitMinutes(
            Entity patient,
            string examinationId)
        {
            int baseMinutes =
                FlowOptimizerConfig.SmartTransportWaitMaxProcedureMinutes;
            int variation =
                FlowOptimizerConfig
                    .SmartTransportWaitMaxProcedureVariationMinutes;

            if (variation <= 0 || patient == null)
            {
                return baseMinutes;
            }

            uint hash = 2166136261u;
            MixWaitLimitHash(ref hash, patient.GetEntityID());
            MixWaitLimitHash(ref hash, examinationId);

            int span = variation * 2 + 1;
            int offset = (int)(hash % (uint)span) - variation;
            int result = baseMinutes + offset;

            if (result < 1)
            {
                return 1;
            }

            return result > 240 ? 240 : result;
        }

        private static void MixWaitLimitHash(
            ref uint hash,
            uint value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 16777619u;
            }
        }

        private static void MixWaitLimitHash(
            ref uint hash,
            string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                MixWaitLimitHash(ref hash, 0u);
                return;
            }

            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }
            }
        }

        private static bool TryGetWaitCompatibleUnestimatedProcedure(
            Entity patient,
            out string examinationId,
            out string source)
        {
            examinationId = "<unknown>";
            source = "unknown";

            ProcedureComponent procedure = patient == null
                ? null
                : patient.GetComponent<ProcedureComponent>();
            ProcedureScript script =
                procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_currentProcedureScript == null
                    ? null
                    : procedure.m_state.m_currentProcedureScript.GetEntity();

            if (script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_examination == null ||
                script.m_stateData.m_examination.Entry == null)
            {
                return false;
            }

            examinationId =
                script.m_stateData.m_examination.Entry.DatabaseID.ToString();

            if (script is ProcedureScriptExaminationDoctorsInterview)
            {
                source =
                    "DoctorsInterview variable symptom loop; runtime wait cap";
                return true;
            }

            if (script is ProcedureScriptExaminationUrineAnalysis)
            {
                source =
                    "UrineAnalysis hospitalized workflow; runtime wait cap";
                return true;
            }

            return false;
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f &&
                   !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static bool IsContextUsable(SmartTransportWaitContext context)
        {
            if (context == null ||
                context.Stretcher == null ||
                context.Stretcher.m_state == null ||
                context.Patient == null ||
                context.Transporter == null ||
                context.MovingSide == null ||
                context.PatientSide == null ||
                context.ExaminationTransportPoint == null)
            {
                return false;
            }

            if (context.Stretcher.m_state.m_movingCharacter == null ||
                context.Stretcher.m_state.m_movingCharacter.GetEntity() !=
                    context.Transporter ||
                context.Stretcher.m_state.m_stretcherMovingCharacterSide == null ||
                context.Stretcher.m_state.m_stretcherMovingCharacterSide.GetEntity() !=
                    context.MovingSide ||
                context.Stretcher.m_state.m_stretcherPatientSide == null ||
                context.Stretcher.m_state.m_stretcherPatientSide.GetEntity() !=
                    context.PatientSide ||
                !context.MovingSide.IsAttachedToCharacter() ||
                !context.PatientSide.IsAttachedToCharacter() ||
                context.MovingSide.User != context.Transporter ||
                context.PatientSide.User != context.Transporter)
            {
                return false;
            }

            EmployeeComponent employee =
                context.Transporter.GetComponent<EmployeeComponent>();
            return IsEmployeeStillUsable(employee);
        }

        private static bool CanKeepWaiting(SmartTransportWaitContext context)
        {
            HospitalizationComponent hospitalization =
                context.Patient.GetComponent<HospitalizationComponent>();
            if (hospitalization == null || hospitalization.m_state == null)
            {
                return false;
            }

            HospitalizationState state =
                hospitalization.m_state.m_hospitalizationState;
            return state == HospitalizationState.BeingTransportedToExamination ||
                   state == HospitalizationState.BeingExamined ||
                   state == HospitalizationState.AfterExaminationCheck ||
                   state == HospitalizationState.BeingTreated ||
                   state == HospitalizationState.AfterTreatmentCheck ||
                   state == HospitalizationState.Interrupted;
        }

        private static bool IsEmployeeStillUsable(EmployeeComponent employee)
        {
            return employee != null &&
                   employee.m_state != null &&
                   DayTime.Instance != null &&
                   !employee.IsFired() &&
                   !employee.ShouldGoToTraining() &&
                   employee.m_state.m_shift == DayTime.Instance.GetShift();
        }

        private static bool IsPatientProcedureRoom(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null)
            {
                return false;
            }

            GameDBRoomType type = room.m_roomPersistentData.m_roomType.Entry;
            return type != null && type.AccessRights == AccessRights.PATIENT_PROCEDURE;
        }

        private static bool IsAllowedPublicRoom(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null ||
                Database.Instance == null)
            {
                return false;
            }

            GameDBRoomType type = room.m_roomPersistentData.m_roomType.Entry;
            if (type == null)
            {
                return false;
            }

            return type == Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_CORRIDOR") ||
                   type == Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_WAITING") ||
                   type == Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_RECEPTION");
        }

        private static bool HasNearbyDoor(
            Floor floor,
            Vector2i position)
        {
            if (floor == null)
            {
                return false;
            }

            for (int x = position.m_x - 1;
                 x <= position.m_x + 1;
                 x++)
            {
                for (int y = position.m_y - 1;
                     y <= position.m_y + 1;
                     y++)
                {
                    Vector2i tile =
                        new Vector2i(x, y);
                    if (!IsInBounds(floor, tile))
                    {
                        continue;
                    }

                    TileWalls walls =
                        floor.GetWallsSafe(x, y);
                    if (walls != null &&
                        (walls.m_doorSW != null ||
                         walls.m_doorSE != null))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasAdjacentWall(
            Floor floor,
            Vector2i position)
        {
            if (!IsInBounds(floor, position))
            {
                return false;
            }

            TileWalls current =
                floor.GetWallsSafe(
                    position.m_x,
                    position.m_y);
            TileWalls north =
                position.m_y + 1 < floor.Size.m_y
                    ? floor.GetWallsSafe(
                        position.m_x,
                        position.m_y + 1)
                    : null;
            TileWalls east =
                position.m_x + 1 < floor.Size.m_x
                    ? floor.GetWallsSafe(
                        position.m_x + 1,
                        position.m_y)
                    : null;

            return
                (current != null &&
                 (current.m_wallSW != null ||
                  current.m_wallSE != null)) ||
                (north != null &&
                 north.m_wallSW != null) ||
                (east != null &&
                 east.m_wallSE != null);
        }

        private static bool IsInBounds(Floor floor, Vector2i position)
        {
            return floor != null &&
                   position.m_x >= 0 &&
                   position.m_y >= 0 &&
                   position.m_x < floor.Size.m_x &&
                   position.m_y < floor.Size.m_y;
        }

        private static bool HasRoute(PathfinderJob job)
        {
            return job != null &&
                   job.m_result != null &&
                   job.m_result.m_state ==
                       PathfinderResult.PathfinderState.FOUND_ROUTE &&
                   job.m_result.m_route != null &&
                   job.m_result.m_route.Nodes != null &&
                   job.m_result.m_route.Nodes.Count >= 2;
        }

        private static float GetCandidateJitter(
            Entity patient,
            Vector2i position)
        {
            int seed = patient == null ? 0 : patient.GetHashCode();
            unchecked
            {
                seed = seed * 397 ^ position.m_x;
                seed = seed * 397 ^ position.m_y;
            }

            int normalized = seed % 1000;
            if (normalized < 0)
            {
                normalized = -normalized;
            }

            return ((float)normalized / 1000f) * 0.15f;
        }

        private static bool Abandon(
            SmartTransportWaitContext context,
            string reason)
        {
            return Abandon(
                context,
                reason,
                true);
        }

        private static bool Abandon(
            SmartTransportWaitContext context,
            string reason,
            bool suppressContinuation)
        {
            if (context != null &&
                context.HasStartedWaiting)
            {
                string outcome =
                    reason != null &&
                    reason.IndexOf(
                        "maximum actual wait exceeded",
                        StringComparison.Ordinal) >= 0
                        ? "hard-cap"
                        : "invalidated";

                LogFinalOutcome(
                    context,
                    outcome,
                    "fallback to vanilla: " + reason);
            }
            else
            {
                Log(
                    context,
                    "fallback to vanilla: " + reason);
            }

            if (suppressContinuation &&
                context != null &&
                context.Stretcher != null)
            {
                SmartTransportContinuationRuntime.SuppressNextRelease(
                    context.Stretcher);
            }
            RemoveContext(context);
            return true;
        }

        private static void RemoveContext(SmartTransportWaitContext context)
        {
            if (context == null)
            {
                return;
            }

            HideWaitingBubble(context);
            AbortJob(context.PlanningJob);
            AbortJob(context.CandidateJob);
            if (context.Stretcher != null)
            {
                Contexts.Remove(context.Stretcher);
            }
        }

        private static void ShowWaitingBubble(
            SmartTransportWaitContext context)
        {
            if (context == null ||
                context.Transporter == null ||
                context.ShowingWaitingBubble)
            {
                return;
            }

            SpeechComponent speech =
                context.Transporter.GetComponent<SpeechComponent>();
            if (speech == null || speech.IsActive())
            {
                return;
            }

            speech.SetBubble(TransportWaitBubbleIndex);
            context.ShowingWaitingBubble = true;
        }

        private static void HideWaitingBubble(
            SmartTransportWaitContext context)
        {
            if (context == null ||
                context.Transporter == null ||
                !context.ShowingWaitingBubble)
            {
                return;
            }

            SpeechComponent speech =
                context.Transporter.GetComponent<SpeechComponent>();
            SpeechComponentPersistentData state =
                speech == null
                    ? null
                    : speech.GetPersistentData() as
                        SpeechComponentPersistentData;

            if (state != null &&
                state.m_speechBubble == null &&
                state.m_bubbleTextureIndex ==
                    TransportWaitBubbleIndex &&
                state.m_bubbleIconIndex == -1 &&
                state.m_bubbleIconAssetID == null)
            {
                speech.HideBubble();
            }

            context.ShowingWaitingBubble = false;
        }

        private static void AbortJob(PathfinderJob job)
        {
            if (job == null || !job.IsRunning)
            {
                return;
            }

            try
            {
                job.Abort();
            }
            catch
            {
            }
        }

        private static Entity GetStretcherEntity(StretcherComponent component)
        {
            return component == null || StretcherEntityField == null
                ? null
                : StretcherEntityField.GetValue(component) as Entity;
        }

        private static void MarkWaitingEnded(
            SmartTransportWaitContext context)
        {
            if (context == null ||
                !context.HasStartedWaiting ||
                context.HasEndedWaiting)
            {
                return;
            }

            context.WaitEndedAt =
                GetCurrentStretcherStateTime(context);
            context.HasEndedWaiting = true;
        }

        private static void MarkRetentionEnded(
            SmartTransportWaitContext context)
        {
            if (context == null ||
                context.HasEndedRetention)
            {
                return;
            }

            context.RetentionEndedAt =
                GetCurrentStretcherStateTime(context);
            context.HasEndedRetention = true;
        }

        private static float GetCurrentStretcherStateTime(
            SmartTransportWaitContext context)
        {
            return context == null ||
                context.Stretcher == null ||
                context.Stretcher.m_state == null
                    ? 0f
                    : context.Stretcher.m_state.m_timeInState;
        }

        private static float GetRetainedSeconds(
            SmartTransportWaitContext context)
        {
            if (context == null)
            {
                return 0f;
            }

            float current =
                context.HasEndedRetention
                    ? context.RetentionEndedAt
                    : GetCurrentStretcherStateTime(context);
            float seconds =
                current - context.StartedAt;
            return seconds > 0f
                ? seconds
                : 0f;
        }

        private static float GetActualWaitingSeconds(
            SmartTransportWaitContext context)
        {
            if (context == null ||
                !context.HasStartedWaiting)
            {
                return 0f;
            }

            float end =
                context.HasEndedWaiting
                    ? context.WaitEndedAt
                    : GetCurrentStretcherStateTime(context);
            float seconds =
                end - context.WaitStartedAt;
            return seconds > 0f
                ? seconds
                : 0f;
        }

        private static float ToIngameMinutes(
            float seconds)
        {
            if (seconds <= 0f ||
                DayTime.Instance == null)
            {
                return 0f;
            }

            return DayTime.Instance.RealTimeSecondsToIngameTimeHours(
                seconds) * 60f;
        }

        private static void LogFinalOutcome(
            SmartTransportWaitContext context,
            string outcome,
            string message)
        {
            if (!FlowOptimizerConfig.TransportDiagnosticLogging ||
                Plugin.Log == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[Diagnostic][Transport/Wait] patient=" +
                HfoDiagnostics.EntityName(
                    context == null
                        ? null
                        : context.Patient) +
                "; transporter=" +
                HfoDiagnostics.EntityName(
                    context == null
                        ? null
                        : context.Transporter) +
                "; " + message +
                "; outcome=" + outcome +
                "; retainedMinutes=" +
                ToIngameMinutes(
                    GetRetainedSeconds(context)).ToString(
                        "0.0",
                        CultureInfo.InvariantCulture) +
                "; actualWaitingMinutes=" +
                ToIngameMinutes(
                    GetActualWaitingSeconds(context)).ToString(
                        "0.0",
                        CultureInfo.InvariantCulture) +
                "; effectiveMaxProcedureMinutes=" +
                (context == null
                    ? 0
                    : context.EffectiveMaxProcedureMinutes));
        }

        private static void LogDecision(
            Entity patient,
            Entity transporter,
            string message)
        {
            if (!FlowOptimizerConfig.TransportDiagnosticLogging ||
                Plugin.Log == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[Diagnostic][Transport/Wait] patient=" +
                HfoDiagnostics.EntityName(patient) +
                "; transporter=" +
                HfoDiagnostics.EntityName(transporter) +
                "; " + message);
        }

        private static void Log(
            SmartTransportWaitContext context,
            string message)
        {
            if (!FlowOptimizerConfig.TransportDiagnosticLogging ||
                Plugin.Log == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[Diagnostic][Transport/Wait] patient=" +
                HfoDiagnostics.EntityName(context == null ? null : context.Patient) +
                "; transporter=" +
                HfoDiagnostics.EntityName(context == null ? null : context.Transporter) +
                "; " + message);
        }
    }

    [HarmonyPatch(
        typeof(Hospital),
        nameof(Hospital.GetPatientsOfADoctor),
        new Type[]
        {
            typeof(Entity),
            typeof(bool),
            typeof(bool),
            typeof(bool)
        })]
    internal static class SmartTransportWaitPatientPanelPatch
    {
        private static void Postfix(
            Entity employee,
            bool includeCurrentUnassigned,
            ref PatientsOfADoctor __result)
        {
            if (!includeCurrentUnassigned ||
                __result == null)
            {
                return;
            }

            Entity patient;
            if (!SmartTransportWaitRuntime.TryGetWaitingPatient(
                    employee,
                    out patient) ||
                patient == null ||
                __result.m_currentPatients.Contains(patient))
            {
                return;
            }

            __result.m_currentPatients.Insert(0, patient);
        }
    }

    [HarmonyPatch(
        typeof(StretcherComponent),
        nameof(StretcherComponent.UpdateStateReleasingMovingCharacter),
        new Type[] { })]
    internal static class SmartTransportWaitReleasePatch
    {
        private static bool Prefix(StretcherComponent __instance)
        {
            return SmartTransportWaitRuntime.HandleReleasing(__instance);
        }
    }

    [HarmonyPatch(
        typeof(StretcherComponent),
        nameof(StretcherComponent.SetDestination),
        new Type[]
        {
            typeof(TileObject),
            typeof(LyingState),
            typeof(StretcherUse),
            typeof(BedReservation),
            typeof(Entity),
            typeof(MovementType),
            typeof(Vehicle)
        })]
    internal static class SmartTransportWaitReusePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            StretcherComponent __instance,
            ref Entity movingCharacter,
            ref Vehicle vehicle,
            ref bool __result,
            out SmartTransportWaitContext __state)
        {
            return SmartTransportWaitRuntime.TryBeginRequestedTransport(
                __instance,
                ref movingCharacter,
                ref vehicle,
                ref __result,
                out __state);
        }

        private static void Postfix(
            StretcherComponent __instance,
            bool __result,
            SmartTransportWaitContext __state)
        {
            SmartTransportWaitRuntime.CompleteRequestedTransport(
                __instance,
                __state,
                __result);
        }
    }
}
