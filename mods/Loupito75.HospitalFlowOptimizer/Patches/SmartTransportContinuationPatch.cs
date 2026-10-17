using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFlowOptimizer.Patches
{
    internal sealed class SmartTransportContinuationRequest
    {
        internal StretcherComponent Stretcher;
        internal Entity Patient;
        internal TileObject Destination;
        internal LyingState LyingState;
        internal StretcherUse StretcherUse;
        internal BedReservation BedReservation;
        internal MovementType MovementType;
        internal Vehicle RequestedVehicle;
        internal long Sequence;
    }

    internal sealed class SmartTransportContinuationContext
    {
        internal StretcherComponent SourceStretcher;
        internal Entity SourcePatient;
        internal Entity Transporter;
        internal TileObject MovingSide;
        internal TileObject PatientSide;
        internal Vector2f OriginalPosition;
        internal Direction OriginalOrientation;
        internal int OriginalFloor;
        internal SmartTransportContinuationRequest Target;
        internal Vector2i Pickup;
        internal int PickupFloor;
        internal bool ReadyForTarget;
        internal bool HandoffFailed;
    }

    internal sealed class SmartTransportContinuationSetDestinationState
    {
        internal SmartTransportContinuationContext Context;
        internal NurseState PreviousNurseState;
        internal Vector2i DestinationTile;
        internal int DestinationFloor;
        internal bool ReservedDestination;
    }

    internal sealed class SmartTransportContinuationReleaseState
    {
        internal Entity HpoPorter;
        internal Entity HpoPressureRescueTarget;
        internal bool SuppressHpoContinuation;
    }

    internal enum SmartTransportPressureRescueResult
    {
        None,
        Declined,
        Started
    }

    internal static class HospitalPortersContinuationCompatibility
    {
        private const string ApiTypeName =
            "HospitalPorters.HospitalPortersApi";
        private const string HpoPluginGuid =
            "loupito75.HospitalPorters";
        private const string LegacyPorterIdentityTypeName =
            "HospitalPorters.PorterIdentity";

        private static Type ApiType;
        private static MethodInfo GetPatientContinuationApiVersionMethod;
        private static MethodInfo IsPorterMethod;
        private static MethodInfo LegacyIsPorterMethod;
        private static MethodInfo IsPatientContinuationWindowOpenMethod;
        private static MethodInfo GetPatientContinuationCandidatesMethod;
        private static MethodInfo GetPatientContinuationPreflightCandidatesMethod;
        private static MethodInfo TryStartPatientContinuationMethod;
        private static MethodInfo FinishPatientContinuationWindowMethod;
        private static int PatientApiVersion;
        private static int ResolvedAssemblyCount = -1;

        internal static int GetApiVersion()
        {
            return Resolve()
                ? PatientApiVersion
                : 0;
        }

        internal static bool IsManagedPorter(Entity porter)
        {
            if (porter == null || !Resolve())
            {
                return false;
            }

            MethodInfo method =
                IsPorterMethod ??
                LegacyIsPorterMethod;
            if (method == null)
            {
                return false;
            }

            try
            {
                object result =
                    method.Invoke(
                        null,
                        new object[] { porter });
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsWindowOpen(Entity porter)
        {
            if (porter == null || !Resolve())
            {
                return false;
            }

            try
            {
                object result =
                    IsPatientContinuationWindowOpenMethod.Invoke(
                        null,
                        new object[] { porter });
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        internal static Entity[] GetCandidates(Entity porter)
        {
            if (porter == null || !Resolve())
            {
                return new Entity[0];
            }

            try
            {
                Entity[] result =
                    GetPatientContinuationCandidatesMethod.Invoke(
                        null,
                        new object[] { porter }) as Entity[];
                return result ?? new Entity[0];
            }
            catch
            {
                return new Entity[0];
            }
        }

        internal static Entity[] GetPreflightCandidates(
            Entity porter,
            Entity sourcePatient)
        {
            if (porter == null ||
                sourcePatient == null ||
                !Resolve() ||
                PatientApiVersion < 2 ||
                GetPatientContinuationPreflightCandidatesMethod == null)
            {
                return new Entity[0];
            }

            try
            {
                Entity[] result =
                    GetPatientContinuationPreflightCandidatesMethod.Invoke(
                        null,
                        new object[] { porter, sourcePatient }) as Entity[];
                return result ?? new Entity[0];
            }
            catch
            {
                return new Entity[0];
            }
        }

        internal static bool TryStart(
            Entity porter,
            Entity targetPatient)
        {
            if (porter == null ||
                targetPatient == null ||
                !Resolve())
            {
                return false;
            }

            try
            {
                object result =
                    TryStartPatientContinuationMethod.Invoke(
                        null,
                        new object[] { porter, targetPatient });
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        internal static bool FinishWindow(Entity porter)
        {
            if (porter == null || !Resolve())
            {
                return false;
            }

            try
            {
                object result =
                    FinishPatientContinuationWindowMethod.Invoke(
                        null,
                        new object[] { porter });
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private static bool Resolve()
        {
            if (!Chainloader.PluginInfos.ContainsKey(HpoPluginGuid))
            {
                return false;
            }

            if (ApiType != null &&
                GetPatientContinuationApiVersionMethod != null &&
                (IsPorterMethod != null ||
                 LegacyIsPorterMethod != null) &&
                IsPatientContinuationWindowOpenMethod != null &&
                GetPatientContinuationCandidatesMethod != null &&
                TryStartPatientContinuationMethod != null &&
                FinishPatientContinuationWindowMethod != null)
            {
                return true;
            }

            Assembly[] assemblies =
                AppDomain.CurrentDomain.GetAssemblies();
            if (assemblies == null ||
                assemblies.Length == ResolvedAssemblyCount)
            {
                return false;
            }

            ResolvedAssemblyCount = assemblies.Length;
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type apiType =
                    assemblies[i].GetType(
                        ApiTypeName,
                        false);
                if (apiType == null)
                {
                    continue;
                }

                MethodInfo versionMethod =
                    apiType.GetMethod(
                        "GetPatientContinuationApiVersion",
                        BindingFlags.Public |
                        BindingFlags.Static);
                MethodInfo publicIsPorterMethod =
                    apiType.GetMethod(
                        "IsPorter",
                        BindingFlags.Public |
                        BindingFlags.Static);
                MethodInfo isWindowOpenMethod =
                    apiType.GetMethod(
                        "IsPatientContinuationWindowOpen",
                        BindingFlags.Public |
                        BindingFlags.Static);
                MethodInfo getCandidatesMethod =
                    apiType.GetMethod(
                        "GetPatientContinuationCandidates",
                        BindingFlags.Public |
                        BindingFlags.Static);
                MethodInfo getPreflightCandidatesMethod =
                    apiType.GetMethod(
                        "GetPatientContinuationPreflightCandidates",
                        BindingFlags.Public |
                        BindingFlags.Static);
                MethodInfo tryStartMethod =
                    apiType.GetMethod(
                        "TryStartPatientContinuation",
                        BindingFlags.Public |
                        BindingFlags.Static);
                MethodInfo finishWindowMethod =
                    apiType.GetMethod(
                        "FinishPatientContinuationWindow",
                        BindingFlags.Public |
                        BindingFlags.Static);

                int version;
                try
                {
                    object versionResult =
                        versionMethod == null
                            ? null
                            : versionMethod.Invoke(null, null);
                    if (!(versionResult is int))
                    {
                        continue;
                    }

                    version = (int)versionResult;
                    if (version < 1)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                MethodInfo legacyIsPorterMethod = null;
                if (publicIsPorterMethod == null)
                {
                    Type legacyPorterIdentityType =
                        assemblies[i].GetType(
                            LegacyPorterIdentityTypeName,
                            false);
                    legacyIsPorterMethod =
                        legacyPorterIdentityType == null
                            ? null
                            : legacyPorterIdentityType.GetMethod(
                                "IsPorter",
                                BindingFlags.NonPublic |
                                BindingFlags.Static);
                }

                if (versionMethod == null ||
                    (publicIsPorterMethod == null &&
                     legacyIsPorterMethod == null) ||
                    isWindowOpenMethod == null ||
                    getCandidatesMethod == null ||
                    tryStartMethod == null ||
                    finishWindowMethod == null)
                {
                    return false;
                }

                ApiType = apiType;
                PatientApiVersion = version;
                GetPatientContinuationApiVersionMethod =
                    versionMethod;
                IsPorterMethod =
                    publicIsPorterMethod;
                LegacyIsPorterMethod =
                    legacyIsPorterMethod;
                IsPatientContinuationWindowOpenMethod =
                    isWindowOpenMethod;
                GetPatientContinuationCandidatesMethod =
                    getCandidatesMethod;
                GetPatientContinuationPreflightCandidatesMethod =
                    getPreflightCandidatesMethod;
                TryStartPatientContinuationMethod =
                    tryStartMethod;
                FinishPatientContinuationWindowMethod =
                    finishWindowMethod;
                return true;
            }

            return false;
        }
    }

    internal static class SmartTransportContinuationRuntime
    {
        private const int DiagnosticSummaryReleaseInterval = 20;

        private static int DiagnosticReleaseChecks;
        private static int DiagnosticReleaseChecksWithoutPending;
        private static int DiagnosticReleaseChecksWithPending;
        private static int DiagnosticPendingCreated;
        private static int DiagnosticPendingRetries;
        private static int DiagnosticSourceRejected;
        private static int DiagnosticRejectedInvalidTarget;
        private static int DiagnosticRejectedDepartment;
        private static int DiagnosticRejectedVehicle;
        private static int DiagnosticRejectedRoute;
        private static int DiagnosticRejectedPickup;
        private static int DiagnosticSelections;
        private static int DiagnosticReady;
        private static int DiagnosticHandoffsStarted;
        private static int DiagnosticHandoffRejected;
        private static int DiagnosticFallbacks;

        private static readonly Dictionary<StretcherComponent, SmartTransportContinuationRequest>
            PendingRequests =
                new Dictionary<StretcherComponent, SmartTransportContinuationRequest>();

        private static readonly Dictionary<StretcherComponent, SmartTransportContinuationContext>
            ActiveContexts =
                new Dictionary<StretcherComponent, SmartTransportContinuationContext>();

        private static readonly Dictionary<StretcherComponent, SmartTransportContinuationContext>
            ReadyTargets =
                new Dictionary<StretcherComponent, SmartTransportContinuationContext>();

        private static readonly HashSet<StretcherComponent> ClaimedTargets =
            new HashSet<StretcherComponent>();

        private static readonly HashSet<StretcherComponent> SuppressedReleases =
            new HashSet<StretcherComponent>();

        private static readonly HashSet<Entity> PendingHpoPorters =
            new HashSet<Entity>();

        private static readonly Dictionary<Entity, Entity>
            PendingHpoPressureRescueTargets =
                new Dictionary<Entity, Entity>();

        private static readonly FieldInfo StretcherEntityField =
            AccessTools.Field(typeof(StretcherComponent), "m_entity");

        private static long RequestSequence;

        internal static void SuppressNextRelease(
            StretcherComponent stretcher)
        {
            if (stretcher != null)
            {
                SuppressedReleases.Add(stretcher);
            }
        }

        internal static bool ShouldReportLogicalIdle(
            StretcherComponent stretcher)
        {
            SmartTransportContinuationContext context;
            if (stretcher == null ||
                !ActiveContexts.TryGetValue(stretcher, out context) ||
                context == null ||
                context.HandoffFailed)
            {
                return false;
            }

            return stretcher.m_state != null &&
                   stretcher.m_state.m_stretcherState ==
                       StretcherComponentState.ReleasingMovingCharacter &&
                   IsSourceUsable(context);
        }

        internal static void TrackRequest(
            StretcherComponent stretcher,
            TileObject bed,
            LyingState lyingState,
            StretcherUse stretcherUse,
            BedReservation bedReservation,
            MovementType movementType,
            Vehicle vehicle,
            bool result)
        {
            if (stretcher == null)
            {
                return;
            }

            if (result)
            {
                PendingRequests.Remove(stretcher);
                return;
            }

            // A target already claimed by an active continuation keeps using
            // vanilla's retry loop, but those retries must not recreate a
            // competing pending request while the retained carrier is en route.
            if (ClaimedTargets.Contains(stretcher))
            {
                return;
            }

            if ((!FlowOptimizerConfig.SmartTransportContinuation &&
                 !FlowOptimizerConfig.SmartTransportPressureRescue) ||
                stretcher.m_state == null ||
                bed == null ||
                stretcherUse != StretcherUse.RETURN_STRETCHER)
            {
                return;
            }

            Entity patient = GetPatient(stretcher);
            if (patient == null ||
                patient.GetComponent<HospitalizationComponent>() == null ||
                patient.GetComponent<BehaviorPatient>() == null ||
                patient.GetComponent<WalkComponent>() == null ||
                !IsPotentialProcedureTransportRequest(
                    patient,
                    bed))
            {
                return;
            }

            SmartTransportContinuationRequest request =
                new SmartTransportContinuationRequest();
            request.Stretcher = stretcher;
            request.Patient = patient;
            request.Destination = bed;
            request.LyingState = lyingState;
            request.StretcherUse = stretcherUse;
            request.BedReservation = bedReservation;
            request.MovementType = movementType;
            request.RequestedVehicle = vehicle;

            SmartTransportContinuationRequest existing;
            bool samePendingRequest =
                PendingRequests.TryGetValue(
                    stretcher,
                    out existing) &&
                existing != null &&
                existing.Destination == bed;

            request.Sequence =
                samePendingRequest
                    ? existing.Sequence
                    : ++RequestSequence;

            PendingRequests[stretcher] = request;

            if (FlowOptimizerConfig.TransportDiagnosticLogging)
            {
                if (samePendingRequest)
                {
                    DiagnosticPendingRetries++;
                }
                else
                {
                    DiagnosticPendingCreated++;
                    LogPendingRequest(
                        request,
                        "pending request tracked after native SetDestination=false");
                }
            }
        }

        internal static bool HandleRelease(
            StretcherComponent source,
            out SmartTransportContinuationReleaseState releaseState)
        {
            releaseState = null;

            bool suppressContinuation =
                source != null &&
                SuppressedReleases.Remove(source);

            Entity transporter =
                source == null ||
                source.m_state == null ||
                source.m_state.m_movingCharacter == null
                    ? null
                    : source.m_state.m_movingCharacter.GetEntity();

            Entity hpoPressureTarget = null;
            bool hasHpoPressureTarget =
                transporter != null &&
                PendingHpoPressureRescueTargets.TryGetValue(
                    transporter,
                    out hpoPressureTarget);

            if (HospitalPortersContinuationCompatibility
                    .IsManagedPorter(transporter))
            {
                if (!FlowOptimizerConfig.SmartTransportContinuation &&
                    !hasHpoPressureTarget)
                {
                    return true;
                }

                releaseState =
                    new SmartTransportContinuationReleaseState();
                releaseState.HpoPorter = transporter;
                releaseState.SuppressHpoContinuation =
                    suppressContinuation;
                releaseState.HpoPressureRescueTarget =
                    hpoPressureTarget;

                // HPO owns the Porter + vehicle lifecycle. Always let the
                // original release run so HPO can open its real continuation
                // window from the completed native return transition.
                return true;
            }

            if (suppressContinuation)
            {
                return true;
            }

            if (source == null ||
                source.m_state == null)
            {
                return true;
            }

            SmartTransportContinuationContext active;
            if (ActiveContexts.TryGetValue(source, out active))
            {
                return ContinueActive(active);
            }

            if (!FlowOptimizerConfig.SmartTransportContinuation)
            {
                return true;
            }

            if (FlowOptimizerConfig.TransportDiagnosticLogging)
            {
                DiagnosticReleaseChecks++;
                if (PendingRequests.Count == 0)
                {
                    DiagnosticReleaseChecksWithoutPending++;
                }
                else
                {
                    DiagnosticReleaseChecksWithPending++;
                }
            }

            SmartTransportContinuationContext context =
                TryCreateContext(source);
            if (context == null)
            {
                MaybeLogDiagnosticSummary(false);
                return true;
            }

            int chanceRoll;
            if (!PassesChance(
                    FlowOptimizerConfig.SmartTransportContinuationChance,
                    out chanceRoll))
            {
                Log(
                    context,
                    "continuation declined by HFO chance; chance=" +
                    FlowOptimizerConfig.SmartTransportContinuationChance +
                    "; roll=" + chanceRoll);
                MaybeLogDiagnosticSummary(false);
                return true;
            }

            if (!StartAutonomousContext(
                    context,
                    "continuation selected; chanceRoll=" +
                    chanceRoll))
            {
                return true;
            }

            return false;
        }

        internal static bool HasActiveContinuation(
            StretcherComponent source)
        {
            return source != null &&
                   ActiveContexts.ContainsKey(source);
        }

        internal static bool ShouldReleaseForHpoPressureRescue(
            StretcherComponent source)
        {
            if (source == null ||
                source.m_state == null ||
                source.m_state.m_movingCharacter == null)
            {
                return false;
            }

            Entity transporter =
                source.m_state.m_movingCharacter.GetEntity();
            return transporter != null &&
                   PendingHpoPressureRescueTargets.ContainsKey(
                       transporter);
        }

        internal static SmartTransportPressureRescueResult
            TryStartPressureRescue(
                StretcherComponent source,
                Entity sourcePatient,
                long lastEvaluatedSequence,
                HashSet<Entity> evaluatedHpoTargets,
                out long evaluatedSequence,
                out Entity targetPatient)
        {
            evaluatedSequence = 0L;
            targetPatient = null;

            if (!FlowOptimizerConfig.SmartTransportPressureRescue ||
                source == null ||
                source.m_state == null ||
                ActiveContexts.ContainsKey(source))
            {
                return SmartTransportPressureRescueResult.None;
            }

            Entity transporter =
                source.m_state.m_movingCharacter == null
                    ? null
                    : source.m_state.m_movingCharacter.GetEntity();

            if (HospitalPortersContinuationCompatibility
                    .IsManagedPorter(transporter))
            {
                Entity[] candidates =
                    HospitalPortersContinuationCompatibility
                        .GetPreflightCandidates(
                            transporter,
                            sourcePatient);
                Entity target =
                    SelectBestHpoCandidate(
                        transporter,
                        candidates);
                if (target == null ||
                    evaluatedHpoTargets == null ||
                    evaluatedHpoTargets.Contains(target))
                {
                    return SmartTransportPressureRescueResult.None;
                }

                evaluatedHpoTargets.Add(target);
                targetPatient = target;

                int hpoChanceRoll;
                if (!PassesChance(
                        FlowOptimizerConfig
                            .SmartTransportPressureRescueChance,
                        out hpoChanceRoll))
                {
                    LogHpoPressureRescue(
                        transporter,
                        target,
                        "declined by HFO chance",
                        hpoChanceRoll);
                    return SmartTransportPressureRescueResult.Declined;
                }

                PendingHpoPressureRescueTargets[transporter] =
                    target;
                LogHpoPressureRescue(
                    transporter,
                    target,
                    "accepted; waiting for native/HPO release",
                    hpoChanceRoll);
                return SmartTransportPressureRescueResult.Started;
            }

            SmartTransportContinuationContext context =
                TryCreateContext(source);
            if (context == null ||
                context.Target == null)
            {
                return SmartTransportPressureRescueResult.None;
            }

            evaluatedSequence =
                context.Target.Sequence;
            targetPatient =
                context.Target.Patient;

            if (evaluatedSequence <= 0L ||
                evaluatedSequence == lastEvaluatedSequence)
            {
                return SmartTransportPressureRescueResult.None;
            }

            int chanceRoll;
            if (!PassesChance(
                    FlowOptimizerConfig.SmartTransportPressureRescueChance,
                    out chanceRoll))
            {
                Log(
                    context,
                    "pressure rescue declined by HFO chance; chance=" +
                    FlowOptimizerConfig.SmartTransportPressureRescueChance +
                    "; roll=" + chanceRoll);
                return SmartTransportPressureRescueResult.Declined;
            }

            if (!StartAutonomousContext(
                    context,
                    "pressure rescue selected; chanceRoll=" +
                    chanceRoll))
            {
                return SmartTransportPressureRescueResult.None;
            }

            return SmartTransportPressureRescueResult.Started;
        }

        private static bool StartAutonomousContext(
            SmartTransportContinuationContext context,
            string selectionMessage)
        {
            if (context == null ||
                context.SourceStretcher == null ||
                context.Target == null ||
                context.Target.Stretcher == null ||
                context.Transporter == null)
            {
                return false;
            }

            WalkComponent walk =
                context.Transporter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return false;
            }

            if (FlowOptimizerConfig.TransportDiagnosticLogging)
            {
                DiagnosticSelections++;
            }

            ActiveContexts[context.SourceStretcher] =
                context;
            ClaimedTargets.Add(
                context.Target.Stretcher);
            PendingRequests.Remove(
                context.Target.Stretcher);
            MaybeLogDiagnosticSummary(true);

            if (walk.GetFloorIndex() == context.PickupFloor &&
                walk.GetCurrentTile() == context.Pickup &&
                !walk.IsBusy())
            {
                bool releaseSource =
                    MarkReady(context);
                return !releaseSource;
            }

            walk.SetDestination(
                context.Pickup,
                context.PickupFloor,
                MovementType.WALKING);

            Log(
                context,
                selectionMessage +
                "; moving to next patient; pickup=" +
                context.Pickup.m_x + "," +
                context.Pickup.m_y +
                "; floor=" +
                context.PickupFloor);
            return true;
        }

        private static void LogHpoPressureRescue(
            Entity porter,
            Entity target,
            string outcome,
            int chanceRoll)
        {
            if (!FlowOptimizerConfig.TransportDiagnosticLogging ||
                Plugin.Log == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[TransportPressureRescue] HPO API " +
                outcome +
                "; chance=" +
                FlowOptimizerConfig
                    .SmartTransportPressureRescueChance +
                "; roll=" +
                chanceRoll +
                "; porter=" +
                HfoDiagnostics.EntityName(porter) +
                "; target=" +
                HfoDiagnostics.EntityName(target));
        }

        private static bool PassesChance(
            int chance,
            out int roll)
        {
            if (chance <= 0)
            {
                roll = 100;
                return false;
            }

            if (chance >= 100)
            {
                roll = 0;
                return true;
            }

            roll =
                UnityEngine.Random.Range(
                    0,
                    100);
            return roll < chance;
        }

        internal static void CompleteRelease(
            SmartTransportContinuationReleaseState releaseState)
        {
            if (releaseState == null ||
                releaseState.HpoPorter == null)
            {
                return;
            }

            Entity porter = releaseState.HpoPorter;
            PendingHpoPorters.Remove(porter);

            Entity pressureTarget =
                releaseState.HpoPressureRescueTarget;
            if (pressureTarget != null)
            {
                PendingHpoPressureRescueTargets.Remove(porter);

                if (releaseState.SuppressHpoContinuation ||
                    !HospitalPortersContinuationCompatibility
                        .IsWindowOpen(porter))
                {
                    return;
                }

                bool pressureStarted =
                    HospitalPortersContinuationCompatibility
                        .TryStart(
                            porter,
                            pressureTarget);

                if (FlowOptimizerConfig.TransportDiagnosticLogging &&
                    Plugin.Log != null)
                {
                    Plugin.Log.LogInfo(
                        "[TransportPressureRescue] HPO API " +
                        (pressureStarted
                            ? "continuation started after release"
                            : "target rejected after release; HPO return continues") +
                        "; porter=" +
                        HfoDiagnostics.EntityName(porter) +
                        "; target=" +
                        HfoDiagnostics.EntityName(pressureTarget));
                }

                // The 70% Pressure Rescue roll already made the optimization
                // decision. Never apply the normal 65% continuation gate to
                // this same release, even if HPO revalidation rejected the
                // preflight snapshot.
                return;
            }

            if (!HospitalPortersContinuationCompatibility
                    .IsWindowOpen(porter))
            {
                return;
            }

            if (releaseState.SuppressHpoContinuation)
            {
                HospitalPortersContinuationCompatibility
                    .FinishWindow(porter);
                return;
            }

            PendingHpoPorters.Add(porter);
            TryContinueHpoPorter(porter);
        }

        internal static void UpdateHpoContinuation(
            BehaviorNurse nurse)
        {
            if (!FlowOptimizerConfig.SmartTransportContinuation ||
                nurse == null)
            {
                return;
            }

            EmployeeComponent employee =
                nurse.GetComponent<EmployeeComponent>();
            Entity porter =
                employee == null
                    ? null
                    : employee.m_entity;
            if (porter == null ||
                !PendingHpoPorters.Contains(porter))
            {
                return;
            }

            if (!HospitalPortersContinuationCompatibility
                    .IsWindowOpen(porter))
            {
                PendingHpoPorters.Remove(porter);
                return;
            }

            TryContinueHpoPorter(porter);
        }

        private static void TryContinueHpoPorter(
            Entity porter)
        {
            Entity[] candidates =
                HospitalPortersContinuationCompatibility
                    .GetCandidates(porter);
            Entity target =
                SelectBestHpoCandidate(
                    porter,
                    candidates);
            if (target == null)
            {
                return;
            }

            int chanceRoll;
            if (!PassesChance(
                    FlowOptimizerConfig.SmartTransportContinuationChance,
                    out chanceRoll))
            {
                PendingHpoPorters.Remove(porter);
                HospitalPortersContinuationCompatibility
                    .FinishWindow(porter);

                if (FlowOptimizerConfig.TransportDiagnosticLogging)
                {
                    Plugin.Log?.LogInfo(
                        "[TransportContinuation] HPO API continuation declined by HFO chance" +
                        "; chance=" +
                        FlowOptimizerConfig.SmartTransportContinuationChance +
                        "; roll=" +
                        chanceRoll +
                        "; porter=" +
                        HfoDiagnostics.EntityName(porter) +
                        "; target=" +
                        HfoDiagnostics.EntityName(target));
                }
                return;
            }

            bool started =
                HospitalPortersContinuationCompatibility
                    .TryStart(porter, target);
            PendingHpoPorters.Remove(porter);

            if (!started)
            {
                // HPO revalidates atomically. A failed handoff ends this
                // particular optimization opportunity and preserves the
                // normal equipment/station return.
                HospitalPortersContinuationCompatibility
                    .FinishWindow(porter);
            }

            if (FlowOptimizerConfig.TransportDiagnosticLogging)
            {
                Plugin.Log?.LogInfo(
                    "[TransportContinuation] HPO API " +
                    (started
                        ? "continuation started"
                        : "continuation rejected by HPO revalidation") +
                    "; chanceRoll=" +
                    chanceRoll +
                    "; porter=" +
                    HfoDiagnostics.EntityName(porter) +
                    "; target=" +
                    HfoDiagnostics.EntityName(target));
            }
        }

        private static Entity SelectBestHpoCandidate(
            Entity porter,
            Entity[] candidates)
        {
            if (porter == null ||
                candidates == null ||
                candidates.Length == 0)
            {
                return null;
            }

            WalkComponent porterWalk =
                porter.GetComponent<WalkComponent>();
            GridMap gridMap =
                GridMap.GetInstance();

            Entity best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < candidates.Length; i++)
            {
                Entity candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                float distance = float.MaxValue;
                WalkComponent patientWalk =
                    candidate.GetComponent<WalkComponent>();
                if (porterWalk != null &&
                    patientWalk != null &&
                    gridMap != null)
                {
                    Vector2i pickup =
                        patientWalk.GetCurrentTile();
                    int pickupFloor =
                        patientWalk.GetFloorIndex();

                    if (patientWalk.m_state != null &&
                        patientWalk.m_state.m_objectSittingOn != null)
                    {
                        TileObject bed =
                            patientWalk.m_state
                                .m_objectSittingOn
                                .GetEntity();
                        if (bed != null)
                        {
                            pickup =
                                bed.GetDefaultStretcherTile();
                            pickupFloor =
                                bed.GetFloorIndex();
                        }
                    }

                    float route =
                        gridMap.GetDistance(
                            porterWalk.GetFloorIndex(),
                            porterWalk.GetCurrentTile(),
                            pickupFloor,
                            pickup,
                            AccessRights.STAFF);
                    if (route >= 0f &&
                        route < float.MaxValue)
                    {
                        distance = route;
                    }
                }

                if (best == null ||
                    distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        internal static bool TryPrepareRequestedTransport(
            StretcherComponent stretcher,
            TileObject bed,
            StretcherUse stretcherUse,
            ref Entity movingCharacter,
            ref Vehicle vehicle,
            ref bool result,
            out SmartTransportContinuationSetDestinationState state)
        {
            state = null;

            if (stretcher == null)
            {
                return true;
            }

            if (!FlowOptimizerConfig.SmartTransportContinuation &&
                !ReadyTargets.ContainsKey(stretcher) &&
                !ClaimedTargets.Contains(stretcher))
            {
                return true;
            }

            SmartTransportContinuationContext context;
            if (!ReadyTargets.TryGetValue(stretcher, out context))
            {
                if (!TryGetClaimedContext(stretcher, out context))
                {
                    return true;
                }

                if (context.HandoffFailed ||
                    !IsSourceUsable(context) ||
                    !IsClaimedTargetUsable(context.Target, context.SourcePatient))
                {
                    FailHandoff(
                        context,
                        "claimed continuation became invalid before pickup");
                    return true;
                }

                if (context.Target == null ||
                    context.Target.Destination != bed ||
                    stretcherUse != StretcherUse.RETURN_STRETCHER ||
                    !IsVehicleCompatible(context.PatientSide, vehicle))
                {
                    FailHandoff(
                        context,
                        "native retry changed while continuation was claimed");
                    return true;
                }

                // Keep vanilla's own transport request alive without allowing
                // another carrier to steal a target HFO has already claimed.
                // Once MarkReady() publishes the context in ReadyTargets, the
                // next native retry falls through to the normal handoff below.
                result = false;
                return false;
            }

            if (context == null ||
                context.HandoffFailed ||
                !context.ReadyForTarget ||
                context.Target == null ||
                context.Target.Stretcher != stretcher ||
                context.Target.Destination != bed ||
                stretcherUse != StretcherUse.RETURN_STRETCHER ||
                !IsSourceUsable(context) ||
                !IsClaimedTargetUsable(context.Target, context.SourcePatient))
            {
                FailHandoff(context, "native retry no longer matches retained target");
                return true;
            }

            WalkComponent transporterWalk =
                context.Transporter.GetComponent<WalkComponent>();
            if (transporterWalk == null ||
                transporterWalk.IsBusy() ||
                transporterWalk.GetFloorIndex() != context.PickupFloor ||
                transporterWalk.GetCurrentTile() != context.Pickup)
            {
                FailHandoff(context, "transporter left the retained pickup position");
                return true;
            }

            if (!IsVehicleCompatible(context.PatientSide, vehicle))
            {
                FailHandoff(context, "retained vehicle became incompatible");
                return true;
            }

            Vector2i destinationTile =
                bed.GetDefaultStretcherTile();
            int destinationFloor =
                bed.GetFloorIndex();

            if (MapScriptInterface.Instance == null ||
                MapScriptInterface.Instance.GetCenterObjectAt(
                    destinationTile,
                    destinationFloor) != null ||
                MapScriptInterface.Instance.GetTileReservedBy(
                    destinationTile,
                    destinationFloor) != null)
            {
                FailHandoff(context, "target transport point became occupied");
                return true;
            }

            BehaviorNurse nurse =
                context.Transporter.GetComponent<BehaviorNurse>();
            if (nurse == null || nurse.m_state == null)
            {
                FailHandoff(context, "transporter lost BehaviorNurse");
                return true;
            }

            SmartTransportContinuationSetDestinationState handoff =
                new SmartTransportContinuationSetDestinationState();
            handoff.Context = context;
            handoff.PreviousNurseState =
                nurse.m_state.m_nurseState;
            handoff.DestinationTile = destinationTile;
            handoff.DestinationFloor = destinationFloor;
            handoff.ReservedDestination = true;

            MapScriptInterface.Instance.ReserveTile(
                destinationTile,
                context.Target.Patient,
                destinationFloor);

            movingCharacter = context.Transporter;
            vehicle =
                context.PatientSide.HasTag("wheelchair_patient")
                    ? Vehicle.WHEELCHAIR
                    : Vehicle.STRETCHER;

            stretcher.m_state.m_movingCharacter =
                context.Transporter;
            stretcher.m_state.m_stretcherPatientSide =
                context.PatientSide;

            // Native SetDestination always emits GOING_TO_STRETCHER for
            // BehaviorNurse, even when the equipment is already attached.
            // Presenting the retained carrier as Idle for this one synchronous
            // call avoids CheckOverrides reporting a false broken-stretcher
            // transition. The postfix immediately restores the moving state.
            nurse.m_state.m_nurseState = NurseState.Idle;

            state = handoff;
            return true;
        }

        internal static void CompleteRequestedTransport(
            StretcherComponent stretcher,
            SmartTransportContinuationSetDestinationState state,
            bool result)
        {
            if (state == null || state.Context == null)
            {
                return;
            }

            SmartTransportContinuationContext context =
                state.Context;
            BehaviorNurse nurse =
                context.Transporter == null
                    ? null
                    : context.Transporter.GetComponent<BehaviorNurse>();

            if (!result)
            {
                if (nurse != null && nurse.m_state != null)
                {
                    nurse.m_state.m_nurseState =
                        state.PreviousNurseState;
                }

                if (stretcher != null && stretcher.m_state != null)
                {
                    stretcher.m_state.m_movingCharacter = null;
                    stretcher.m_state.m_stretcherPatientSide = null;
                    stretcher.m_state.m_stretcherMovingCharacterSide = null;
                }

                if (state.ReservedDestination &&
                    MapScriptInterface.Instance != null &&
                    MapScriptInterface.Instance.GetTileReservedBy(
                        state.DestinationTile,
                        state.DestinationFloor) ==
                        context.Target.Patient)
                {
                    MapScriptInterface.Instance.FreeTile(
                        state.DestinationTile,
                        state.DestinationFloor);
                }

                if (context.MovingSide != null)
                {
                    context.MovingSide.Owner =
                        context.SourcePatient;
                }
                if (context.PatientSide != null)
                {
                    context.PatientSide.Owner =
                        context.SourcePatient;
                }

                FailHandoff(context, "native SetDestination retry failed");
                return;
            }

            stretcher.m_state.m_originalStretcherPosition =
                context.OriginalPosition;
            stretcher.m_state.m_originalStretcherOrientation =
                context.OriginalOrientation;
            stretcher.m_state.m_originalStretcherFloor =
                context.OriginalFloor;

            Behavior behavior =
                context.Transporter == null
                    ? null
                    : context.Transporter.GetComponent<Behavior>();
            if (behavior != null)
            {
                behavior.ReceiveMessage(
                    new Message(Messages.OVERRIDE_BY_MOVING_STRETCHER));
            }

            context.SourceStretcher.m_state.m_movingCharacter = null;
            context.SourceStretcher.m_state.m_stretcherMovingCharacterSide = null;
            context.SourceStretcher.m_state.m_stretcherPatientSide = null;
            context.SourceStretcher.SwitchState(
                StretcherComponentState.Idle);

            if (FlowOptimizerConfig.TransportDiagnosticLogging)
            {
                DiagnosticHandoffsStarted++;
            }

            CleanupContext(context);
            PendingRequests.Remove(stretcher);

            Log(
                context,
                "continuation started from native retry; vehicle=" +
                stretcher.m_state.m_vehicle +
                "; originalFloor=" + context.OriginalFloor);
            MaybeLogDiagnosticSummary(true);
        }

        internal static void Clear()
        {
            MaybeLogDiagnosticSummary(true);

            PendingRequests.Clear();
            ActiveContexts.Clear();
            ReadyTargets.Clear();
            ClaimedTargets.Clear();
            SuppressedReleases.Clear();
            PendingHpoPorters.Clear();
            PendingHpoPressureRescueTargets.Clear();
            RequestSequence = 0;

            DiagnosticReleaseChecks = 0;
            DiagnosticReleaseChecksWithoutPending = 0;
            DiagnosticReleaseChecksWithPending = 0;
            DiagnosticPendingCreated = 0;
            DiagnosticPendingRetries = 0;
            DiagnosticSourceRejected = 0;
            DiagnosticRejectedInvalidTarget = 0;
            DiagnosticRejectedDepartment = 0;
            DiagnosticRejectedVehicle = 0;
            DiagnosticRejectedRoute = 0;
            DiagnosticRejectedPickup = 0;
            DiagnosticSelections = 0;
            DiagnosticReady = 0;
            DiagnosticHandoffsStarted = 0;
            DiagnosticHandoffRejected = 0;
            DiagnosticFallbacks = 0;
        }

        private static bool ContinueActive(
            SmartTransportContinuationContext context)
        {
            if (context == null)
            {
                return true;
            }

            if (context.HandoffFailed)
            {
                return Abandon(context, "handoff attempt failed");
            }

            if (!IsSourceUsable(context) ||
                !IsClaimedTargetUsable(context.Target, context.SourcePatient))
            {
                return Abandon(
                    context,
                    "source or target no longer valid");
            }

            if (context.ReadyForTarget)
            {
                return false;
            }

            WalkComponent walk =
                context.Transporter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return Abandon(
                    context,
                    "transporter lost WalkComponent");
            }

            if (walk.IsBusy())
            {
                return false;
            }

            if (walk.GetFloorIndex() != context.PickupFloor ||
                walk.GetCurrentTile() != context.Pickup)
            {
                return Abandon(
                    context,
                    "route to next patient did not complete");
            }

            return MarkReady(context);
        }

        private static bool MarkReady(
            SmartTransportContinuationContext context)
        {
            if (!IsSourceUsable(context) ||
                !IsClaimedTargetUsable(context.Target, context.SourcePatient))
            {
                return Abandon(
                    context,
                    "target became invalid at pickup");
            }

            Vector2i destinationTile =
                context.Target.Destination.GetDefaultStretcherTile();
            int destinationFloor =
                context.Target.Destination.GetFloorIndex();

            if (MapScriptInterface.Instance == null ||
                MapScriptInterface.Instance.GetCenterObjectAt(
                    destinationTile,
                    destinationFloor) != null ||
                MapScriptInterface.Instance.GetTileReservedBy(
                    destinationTile,
                    destinationFloor) != null)
            {
                return Abandon(
                    context,
                    "target transport point unavailable at pickup");
            }

            context.ReadyForTarget = true;
            ReadyTargets[context.Target.Stretcher] = context;
            if (FlowOptimizerConfig.TransportDiagnosticLogging)
            {
                DiagnosticReady++;
            }

            Log(
                context,
                "waiting for target patient's native transport retry");
            return false;
        }

        private static SmartTransportContinuationContext TryCreateContext(
            StretcherComponent source)
        {
            if (source.m_state.m_stretcherState !=
                    StretcherComponentState.ReleasingMovingCharacter ||
                source.m_state.m_stretcherUse !=
                    StretcherUse.RETURN_STRETCHER ||
                source.m_state.m_movingCharacter == null ||
                source.m_state.m_stretcherMovingCharacterSide == null ||
                source.m_state.m_stretcherPatientSide == null)
            {
                if (FlowOptimizerConfig.TransportDiagnosticLogging &&
                    PendingRequests.Count > 0)
                {
                    DiagnosticSourceRejected++;
                }
                return null;
            }

            Entity sourcePatient = GetPatient(source);
            Entity transporter =
                source.m_state.m_movingCharacter.GetEntity();
            TileObject movingSide =
                source.m_state.m_stretcherMovingCharacterSide.GetEntity();
            TileObject patientSide =
                source.m_state.m_stretcherPatientSide.GetEntity();

            if (sourcePatient == null ||
                transporter == null ||
                movingSide == null ||
                patientSide == null ||
                !IsTransporterUsable(transporter) ||
                !movingSide.IsAttachedToCharacter() ||
                !patientSide.IsAttachedToCharacter() ||
                movingSide.User != transporter ||
                patientSide.User != transporter)
            {
                if (FlowOptimizerConfig.TransportDiagnosticLogging &&
                    PendingRequests.Count > 0)
                {
                    DiagnosticSourceRejected++;
                }
                return null;
            }

            SmartTransportContinuationRequest target =
                FindBestRequest(
                    source,
                    sourcePatient,
                    transporter,
                    patientSide);
            if (target == null)
            {
                return null;
            }

            Vector2i pickup;
            int pickupFloor;
            if (!TryGetPickup(
                    target.Patient,
                    out pickup,
                    out pickupFloor))
            {
                if (FlowOptimizerConfig.TransportDiagnosticLogging)
                {
                    DiagnosticRejectedPickup++;
                }
                PendingRequests.Remove(target.Stretcher);
                return null;
            }

            SmartTransportContinuationContext context =
                new SmartTransportContinuationContext();
            context.SourceStretcher = source;
            context.SourcePatient = sourcePatient;
            context.Transporter = transporter;
            context.MovingSide = movingSide;
            context.PatientSide = patientSide;
            context.OriginalPosition =
                source.m_state.m_originalStretcherPosition;
            context.OriginalOrientation =
                source.m_state.m_originalStretcherOrientation;
            context.OriginalFloor =
                source.m_state.m_originalStretcherFloor;
            context.Target = target;
            context.Pickup = pickup;
            context.PickupFloor = pickupFloor;
            return context;
        }

        private static SmartTransportContinuationRequest FindBestRequest(
            StretcherComponent source,
            Entity sourcePatient,
            Entity transporter,
            TileObject retainedPatientSide)
        {
            if (sourcePatient == null ||
                transporter == null ||
                retainedPatientSide == null ||
                GridMap.GetInstance() == null)
            {
                return null;
            }

            BehaviorPatient sourceBehavior =
                sourcePatient.GetComponent<BehaviorPatient>();
            WalkComponent transporterWalk =
                transporter.GetComponent<WalkComponent>();
            if (sourceBehavior == null ||
                transporterWalk == null)
            {
                return null;
            }

            Department sourceDepartment =
                sourceBehavior.GetDepartment();
            List<SmartTransportContinuationRequest> snapshot =
                new List<SmartTransportContinuationRequest>(
                    PendingRequests.Values);

            SmartTransportContinuationRequest best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < snapshot.Count; i++)
            {
                SmartTransportContinuationRequest candidate =
                    snapshot[i];
                if (candidate == null ||
                    candidate.Stretcher == source)
                {
                    continue;
                }

                if (candidate.Stretcher != null &&
                    ClaimedTargets.Contains(candidate.Stretcher))
                {
                    continue;
                }

                if (!IsTargetUsable(
                        candidate,
                        sourcePatient))
                {
                    if (FlowOptimizerConfig.TransportDiagnosticLogging)
                    {
                        DiagnosticRejectedInvalidTarget++;
                    }
                    if (candidate.Stretcher != null)
                    {
                        PendingRequests.Remove(
                            candidate.Stretcher);
                    }
                    continue;
                }

                BehaviorPatient targetBehavior =
                    candidate.Patient.GetComponent<BehaviorPatient>();
                WalkComponent targetWalk =
                    candidate.Patient.GetComponent<WalkComponent>();
                if (targetBehavior == null ||
                    targetWalk == null ||
                    targetBehavior.GetDepartment() != sourceDepartment)
                {
                    if (FlowOptimizerConfig.TransportDiagnosticLogging)
                    {
                        DiagnosticRejectedDepartment++;
                    }
                    continue;
                }

                if (!IsVehicleCompatible(
                        retainedPatientSide,
                        candidate.RequestedVehicle))
                {
                    if (FlowOptimizerConfig.TransportDiagnosticLogging)
                    {
                        DiagnosticRejectedVehicle++;
                    }
                    continue;
                }

                float distance =
                    GridMap.GetInstance().GetDistance(
                        transporterWalk.GetFloorIndex(),
                        transporterWalk.GetCurrentTile(),
                        targetWalk.GetFloorIndex(),
                        targetWalk.GetCurrentTile(),
                        AccessRights.STAFF);
                if (distance < 0f)
                {
                    if (FlowOptimizerConfig.TransportDiagnosticLogging)
                    {
                        DiagnosticRejectedRoute++;
                    }
                    continue;
                }

                if (best == null ||
                    distance < bestDistance ||
                    (Math.Abs(distance - bestDistance) < 0.001f &&
                     candidate.Sequence < best.Sequence))
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static bool TryGetClaimedContext(
            StretcherComponent target,
            out SmartTransportContinuationContext context)
        {
            context = null;
            if (target == null ||
                !ClaimedTargets.Contains(target))
            {
                return false;
            }

            foreach (SmartTransportContinuationContext candidate in
                ActiveContexts.Values)
            {
                if (candidate != null &&
                    candidate.Target != null &&
                    candidate.Target.Stretcher == target)
                {
                    context = candidate;
                    return true;
                }
            }

            // Heal a stale claim defensively. A live context is authoritative.
            ClaimedTargets.Remove(target);
            return false;
        }

        private static bool IsPotentialProcedureTransportRequest(
            Entity patient,
            TileObject destination)
        {
            if (patient == null ||
                destination == null)
            {
                return false;
            }

            ProcedureComponent procedure =
                patient.GetComponent<ProcedureComponent>();
            if (procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_reservedProcedureScript == null ||
                procedure.m_state.m_reservedProcedureScript.GetEntity() == null)
            {
                return false;
            }

            try
            {
                TileObject transportPoint =
                    procedure.m_state.m_reservedProcedureScript
                        .GetEntity()
                        .GetEquipment(0)
                        .GetPartWithTag("transport_point");
                return transportPoint == destination;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTargetUsable(
            SmartTransportContinuationRequest request,
            Entity sourcePatient)
        {
            if (!IsClaimedTargetUsable(
                    request,
                    sourcePatient))
            {
                return false;
            }

            HospitalizationComponent hospitalization =
                request.Patient.GetComponent<HospitalizationComponent>();
            return hospitalization != null &&
                   hospitalization.m_state != null &&
                   IsWaitingForProcedureTransport(
                       hospitalization.m_state
                           .m_procedureReservationStatus);
        }

        private static bool IsClaimedTargetUsable(
            SmartTransportContinuationRequest request,
            Entity sourcePatient)
        {
            if (request == null ||
                request.Stretcher == null ||
                request.Stretcher.m_state == null ||
                request.Patient == null ||
                request.Patient == sourcePatient ||
                request.Destination == null ||
                request.Destination.m_state == null ||
                request.StretcherUse !=
                    StretcherUse.RETURN_STRETCHER ||
                request.Stretcher.m_state.m_stretcherState !=
                    StretcherComponentState.Idle ||
                request.Stretcher.m_state.m_movingCharacter != null ||
                request.Stretcher.m_state.m_stretcherPatientSide != null ||
                request.Stretcher.m_state.m_stretcherMovingCharacterSide != null)
            {
                return false;
            }

            ProcedureComponent procedure =
                request.Patient.GetComponent<ProcedureComponent>();
            if (procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_reservedProcedureScript == null ||
                procedure.m_state.m_reservedProcedureScript.GetEntity() == null)
            {
                return false;
            }

            try
            {
                TileObject transportPoint =
                    procedure.m_state.m_reservedProcedureScript
                        .GetEntity()
                        .GetEquipment(0)
                        .GetPartWithTag("transport_point");
                return transportPoint ==
                    request.Destination;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWaitingForProcedureTransport(
            ProcedureReservationStatus status)
        {
            return status ==
                       ProcedureReservationStatus.WAITING_FOR_TRANSPORT_EXM ||
                   status ==
                       ProcedureReservationStatus.WAITING_FOR_TRANSPORT_TRT ||
                   status ==
                       ProcedureReservationStatus.WAITING_FOR_TRANSPORT_SURG;
        }

        private static bool IsVehicleCompatible(
            TileObject retainedPatientSide,
            Vehicle requested)
        {
            bool retainedWheelchair =
                retainedPatientSide != null &&
                retainedPatientSide.HasTag("wheelchair_patient");

            if (retainedWheelchair)
            {
                return requested == Vehicle.WHEELCHAIR;
            }

            return requested == Vehicle.STRETCHER ||
                   requested == Vehicle.WHEELCHAIR;
        }

        private static bool TryGetPickup(
            Entity patient,
            out Vector2i pickup,
            out int floor)
        {
            pickup = Vector2i.ZERO_VECTOR;
            floor = -1;

            if (patient == null)
            {
                return false;
            }

            WalkComponent walk =
                patient.GetComponent<WalkComponent>();
            if (walk == null ||
                walk.m_state == null ||
                walk.m_state.m_objectSittingOn == null ||
                walk.m_state.m_objectSittingOn.GetEntity() == null)
            {
                return false;
            }

            TileObject currentBed =
                walk.m_state.m_objectSittingOn.GetEntity();
            pickup =
                currentBed.GetDefaultStretcherTile();
            floor =
                currentBed.GetFloorIndex();
            return floor >= 0;
        }

        private static bool IsSourceUsable(
            SmartTransportContinuationContext context)
        {
            if (context == null ||
                context.SourceStretcher == null ||
                context.SourceStretcher.m_state == null ||
                context.SourcePatient == null ||
                context.Transporter == null ||
                context.MovingSide == null ||
                context.PatientSide == null ||
                context.SourceStretcher.m_state.m_stretcherState !=
                    StretcherComponentState.ReleasingMovingCharacter ||
                context.SourceStretcher.m_state.m_movingCharacter == null ||
                context.SourceStretcher.m_state.m_movingCharacter.GetEntity() !=
                    context.Transporter ||
                context.SourceStretcher.m_state.m_stretcherMovingCharacterSide == null ||
                context.SourceStretcher.m_state.m_stretcherMovingCharacterSide.GetEntity() !=
                    context.MovingSide ||
                context.SourceStretcher.m_state.m_stretcherPatientSide == null ||
                context.SourceStretcher.m_state.m_stretcherPatientSide.GetEntity() !=
                    context.PatientSide ||
                !context.MovingSide.IsAttachedToCharacter() ||
                !context.PatientSide.IsAttachedToCharacter() ||
                context.MovingSide.User !=
                    context.Transporter ||
                context.PatientSide.User !=
                    context.Transporter)
            {
                return false;
            }

            return IsTransporterUsable(
                context.Transporter);
        }

        private static bool IsTransporterUsable(
            Entity transporter)
        {
            if (transporter == null)
            {
                return false;
            }

            BehaviorNurse nurse =
                transporter.GetComponent<BehaviorNurse>();
            EmployeeComponent employee =
                transporter.GetComponent<EmployeeComponent>();

            return nurse != null &&
                   nurse.m_state != null &&
                   nurse.m_state.m_nurseState ==
                       NurseState.OverridenMovingStretcher &&
                   employee != null &&
                   employee.m_state != null &&
                   DayTime.Instance != null &&
                   !employee.IsFired() &&
                   !employee.ShouldGoToTraining() &&
                   employee.m_state.m_shift ==
                       DayTime.Instance.GetShift();
        }

        private static void FailHandoff(
            SmartTransportContinuationContext context,
            string reason)
        {
            if (context == null)
            {
                return;
            }

            if (!context.HandoffFailed &&
                FlowOptimizerConfig.TransportDiagnosticLogging)
            {
                DiagnosticHandoffRejected++;
            }

            context.HandoffFailed = true;
            context.ReadyForTarget = false;

            if (context.Target != null &&
                context.Target.Stretcher != null)
            {
                ReadyTargets.Remove(
                    context.Target.Stretcher);
            }

            Log(
                context,
                "handoff rejected: " + reason);
        }

        private static bool Abandon(
            SmartTransportContinuationContext context,
            string reason)
        {
            if (context != null)
            {
                if (FlowOptimizerConfig.TransportDiagnosticLogging)
                {
                    DiagnosticFallbacks++;
                }

                CleanupContext(context);

                if (context.Target != null &&
                    context.Target.Stretcher != null &&
                    IsTargetUsable(
                        context.Target,
                        context.SourcePatient))
                {
                    PendingRequests[
                        context.Target.Stretcher] =
                            context.Target;
                }

                Log(
                    context,
                    "fallback to vanilla: " + reason);
            }

            return true;
        }

        private static void CleanupContext(
            SmartTransportContinuationContext context)
        {
            if (context == null)
            {
                return;
            }

            if (context.SourceStretcher != null)
            {
                ActiveContexts.Remove(
                    context.SourceStretcher);
            }

            if (context.Target != null &&
                context.Target.Stretcher != null)
            {
                ReadyTargets.Remove(
                    context.Target.Stretcher);
                ClaimedTargets.Remove(
                    context.Target.Stretcher);
            }
        }

        private static Entity GetPatient(
            StretcherComponent component)
        {
            return component == null ||
                   StretcherEntityField == null
                ? null
                : StretcherEntityField.GetValue(
                    component) as Entity;
        }

        private static void LogPendingRequest(
            SmartTransportContinuationRequest request,
            string message)
        {
            if (!FlowOptimizerConfig.TransportDiagnosticLogging ||
                Plugin.Log == null ||
                request == null)
            {
                return;
            }

            HospitalizationComponent hospitalization =
                request.Patient == null
                    ? null
                    : request.Patient.GetComponent<HospitalizationComponent>();

            Plugin.Log.LogInfo(
                "[TransportContinuation] patient=" +
                HfoDiagnostics.EntityName(request.Patient) +
                "; " + message +
                "; vehicle=" + request.RequestedVehicle +
                "; reservationStatusAtReturn=" +
                (hospitalization == null ||
                 hospitalization.m_state == null
                    ? "unknown"
                    : hospitalization.m_state
                        .m_procedureReservationStatus.ToString()) +
                "; destination=F" +
                (request.Destination == null
                    ? -1
                    : request.Destination.GetFloorIndex()) +
                ":" +
                (request.Destination == null ||
                 request.Destination.m_state == null
                    ? "none"
                    : request.Destination.m_state.m_position.ToString()) +
                "; sequence=" + request.Sequence);
        }

        private static void MaybeLogDiagnosticSummary(
            bool force)
        {
            if (!FlowOptimizerConfig.TransportDiagnosticLogging ||
                Plugin.Log == null ||
                DiagnosticReleaseChecks <= 0)
            {
                return;
            }

            if (!force &&
                DiagnosticReleaseChecks %
                    DiagnosticSummaryReleaseInterval != 0)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[TransportContinuationSummary]" +
                " releaseChecks=" +
                DiagnosticReleaseChecks +
                "; noPending=" +
                DiagnosticReleaseChecksWithoutPending +
                "; withPending=" +
                DiagnosticReleaseChecksWithPending +
                "; pendingCreated=" +
                DiagnosticPendingCreated +
                "; pendingRetries=" +
                DiagnosticPendingRetries +
                "; sourceRejected=" +
                DiagnosticSourceRejected +
                "; rejectedInvalidTarget=" +
                DiagnosticRejectedInvalidTarget +
                "; rejectedDepartment=" +
                DiagnosticRejectedDepartment +
                "; rejectedVehicle=" +
                DiagnosticRejectedVehicle +
                "; rejectedRoute=" +
                DiagnosticRejectedRoute +
                "; rejectedPickup=" +
                DiagnosticRejectedPickup +
                "; selected=" +
                DiagnosticSelections +
                "; ready=" +
                DiagnosticReady +
                "; handoffsStarted=" +
                DiagnosticHandoffsStarted +
                "; handoffRejected=" +
                DiagnosticHandoffRejected +
                "; fallbacks=" +
                DiagnosticFallbacks +
                "; livePending=" +
                PendingRequests.Count +
                "; liveActive=" +
                ActiveContexts.Count +
                "; liveReady=" +
                ReadyTargets.Count);
        }

        private static void Log(
            SmartTransportContinuationContext context,
            string message)
        {
            if (!FlowOptimizerConfig.TransportDiagnosticLogging ||
                Plugin.Log == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[TransportContinuation] patient=" +
                HfoDiagnostics.EntityName(
                    context == null
                        ? null
                        : context.SourcePatient) +
                "; nextPatient=" +
                HfoDiagnostics.EntityName(
                    context == null ||
                    context.Target == null
                        ? null
                        : context.Target.Patient) +
                "; transporter=" +
                HfoDiagnostics.EntityName(
                    context == null
                        ? null
                        : context.Transporter) +
                "; " + message);
        }
    }

    [HarmonyPatch(
        typeof(StretcherComponent),
        nameof(StretcherComponent.IsIdle),
        new Type[] { })]
    internal static class SmartTransportLogicalIdlePatch
    {
        private static void Postfix(
            StretcherComponent __instance,
            ref bool __result)
        {
            if (__result)
            {
                return;
            }

            if (SmartTransportWaitRuntime.ShouldReportLogicalIdle(
                    __instance) ||
                SmartTransportContinuationRuntime.ShouldReportLogicalIdle(
                    __instance))
            {
                __result = true;
            }
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
    internal static class SmartTransportContinuationRequestPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            StretcherComponent __instance,
            TileObject bed,
            StretcherUse stretcherUse,
            ref Entity movingCharacter,
            ref Vehicle vehicle,
            ref bool __result,
            out SmartTransportContinuationSetDestinationState __state)
        {
            return SmartTransportContinuationRuntime
                .TryPrepareRequestedTransport(
                    __instance,
                    bed,
                    stretcherUse,
                    ref movingCharacter,
                    ref vehicle,
                    ref __result,
                    out __state);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            StretcherComponent __instance,
            TileObject bed,
            LyingState lyingState,
            StretcherUse stretcherUse,
            BedReservation freeBed,
            MovementType movementType,
            Vehicle vehicle,
            bool __result,
            SmartTransportContinuationSetDestinationState __state)
        {
            SmartTransportContinuationRuntime
                .CompleteRequestedTransport(
                    __instance,
                    __state,
                    __result);

            SmartTransportContinuationRuntime.TrackRequest(
                __instance,
                bed,
                lyingState,
                stretcherUse,
                freeBed,
                movementType,
                vehicle,
                __result);
        }
    }

    [HarmonyPatch(
        typeof(StretcherComponent),
        nameof(StretcherComponent.ReleaseMovingCharacter),
        new Type[] { })]
    [HarmonyAfter("Loupito75:HospitalPorters")]
    internal static class SmartTransportContinuationReleasePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            StretcherComponent __instance,
            out SmartTransportContinuationReleaseState __state)
        {
            return SmartTransportContinuationRuntime
                .HandleRelease(
                    __instance,
                    out __state);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            SmartTransportContinuationReleaseState __state)
        {
            SmartTransportContinuationRuntime
                .CompleteRelease(__state);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.Update),
        new Type[] { typeof(float) })]
    [HarmonyAfter("Loupito75:HospitalPorters")]
    internal static class SmartTransportContinuationHpoUpdatePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            BehaviorNurse __instance)
        {
            SmartTransportContinuationRuntime
                .UpdateHpoContinuation(__instance);
        }
    }
}
