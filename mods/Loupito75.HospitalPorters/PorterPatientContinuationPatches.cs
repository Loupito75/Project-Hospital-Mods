using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    internal sealed class PorterPatientStationReturnSnapshot
    {
        internal Entity Porter;
        internal bool ContinuationDeclined;
    }

    internal sealed class PorterPatientVehicleContinuationSnapshot
    {
        internal Entity Porter;
        internal Entity Patient;
        internal TileObject Destination;
        internal LyingState LyingState;
        internal StretcherUse StretcherUse;
        internal BedReservation BedReservation;
        internal MovementType MovementType;
        internal Vehicle RequestedVehicle;
        internal bool AwaitingNativeReplay;
    }

    // Patient delivery follows the same HPO-owned post-delivery return
    // principle as Samples. Native code returns the physical wheelchair /
    // stretcher first; HPO then locks the walk back to the Porter Station.
    // The optional continuation API remains callable WHILE returning:
    // no pause, timer, automatic selection or dependency on another mod.
    internal static class PorterPatientContinuationRuntime
    {
        private enum ReturnPhase
        {
            ReturningEquipment,
            ReturningToStation
        }

        private sealed class ReturnJob
        {
            internal Entity Porter;
            internal Entity SourcePatient;
            internal ReturnPhase Phase;
            internal TileObject PatientSide;
            internal TileObject MovingSide;
            internal Vector2f OriginalPosition;
            internal Direction OriginalOrientation;
            internal int OriginalFloor;
            internal bool CanReuseVehicle;
            internal bool ContinuationDeclined;
            internal Vector2i StationTarget;
            internal int StationFloor;
            internal bool HasStationTarget;
        }

        private sealed class ActiveVehicleContinuation
        {
            internal Entity Porter;
            internal Entity Patient;
            internal TileObject PatientSide;
            internal TileObject MovingSide;
            internal Vector2f OriginalPosition;
            internal Direction OriginalOrientation;
            internal int OriginalFloor;
            internal TileObject Destination;
            internal LyingState LyingState;
            internal StretcherUse StretcherUse;
            internal BedReservation BedReservation;
            internal MovementType MovementType;
            internal Vehicle RequestedVehicle;
            internal bool AwaitingNativeReplay;
        }

        private static readonly FieldInfo StretcherEntityField =
            AccessTools.Field(typeof(StretcherComponent), "m_entity");

        private static readonly Dictionary<Entity, ReturnJob> Jobs =
            new Dictionary<Entity, ReturnJob>();

        private static readonly Dictionary<Entity, ActiveVehicleContinuation>
            ActiveVehicleContinuations =
                new Dictionary<Entity, ActiveVehicleContinuation>();

        internal static void Clear()
        {
            Jobs.Clear();
            ActiveVehicleContinuations.Clear();
        }

        internal static bool IsBusy(Entity porter)
        {
            return porter != null && Jobs.ContainsKey(porter);
        }

        internal static bool IsWindowOpen(Entity porter)
        {
            ReturnJob job;
            return porter != null &&
                Jobs.TryGetValue(porter, out job) &&
                !job.ContinuationDeclined &&
                IsReturnJobUsable(job);
        }

        internal static Entity[] GetCandidates(Entity porter)
        {
            if (!IsWindowOpen(porter))
            {
                return new Entity[0];
            }

            ReturnJob job = Jobs[porter];
            List<Entity> result = new List<Entity>();
            List<PorterPatientFallbackSnapshot> requests =
                StretcherDestinationPatch.GetPatientFallbackStateSnapshot();
            for (int i = 0; i < requests.Count; i++)
            {
                Entity patient = requests[i].Patient;
                if (CanContinue(job, patient) &&
                    !result.Contains(patient))
                {
                    result.Add(patient);
                }
            }
            return result.ToArray();
        }

        internal static Entity[] GetPreflightCandidates(
            Entity porter,
            Entity sourcePatient)
        {
            ReturnJob preflight;
            if (!TryCreatePreflightJob(
                    porter,
                    sourcePatient,
                    out preflight))
            {
                return new Entity[0];
            }

            List<Entity> result = new List<Entity>();
            List<PorterPatientFallbackSnapshot> requests =
                StretcherDestinationPatch.GetPatientFallbackStateSnapshot();
            for (int i = 0; i < requests.Count; i++)
            {
                Entity patient = requests[i].Patient;
                if (CanContinuePreflight(preflight, patient) &&
                    !result.Contains(patient))
                {
                    result.Add(patient);
                }
            }
            return result.ToArray();
        }

        internal static bool TryStart(Entity porter, Entity patient)
        {
            if (!IsWindowOpen(porter))
            {
                return false;
            }

            ReturnJob job = Jobs[porter];
            if (!CanContinue(job, patient))
            {
                return false;
            }

            TileObject destination;
            LyingState lyingState;
            StretcherUse use;
            BedReservation reservation;
            MovementType movement;
            Vehicle requestedVehicle;
            bool waited;
            if (!StretcherDestinationPatch.TryGetPatientContinuationRequest(
                    patient, out destination, out lyingState, out use,
                    out reservation, out movement, out requestedVehicle,
                    out waited))
            {
                return false;
            }

            bool started = job.Phase == ReturnPhase.ReturningEquipment
                ? TryReuseVehicle(
                    job, patient, destination, lyingState, use,
                    reservation, movement, requestedVehicle)
                : TryStartWithNativeVehicle(
                    job, patient, destination, lyingState, use,
                    reservation, movement, requestedVehicle);

            if (started)
            {
                Jobs.Remove(porter);
                StretcherDestinationPatch.ClearPatientContinuationFallback(
                    patient);
                PorterDiagnostics.Log(
                    "patient continuation started during return: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; patient=" +
                    (patient.Name ?? string.Empty).Trim() +
                    ".");
            }
            return started;
        }

        internal static bool FinishWindow(Entity porter)
        {
            ReturnJob job;
            if (!IsWindowOpen(porter) ||
                !Jobs.TryGetValue(porter, out job))
            {
                return false;
            }

            // Do not stop or accelerate the return: just decline any
            // further API continuation for this particular return.
            job.ContinuationDeclined = true;
            return true;
        }

        // Called after the ORIGINAL native ReleaseMovingCharacter.
        // Never intercept or delay native ReturnStretcher.
        internal static void OnNativeRelease(
            StretcherComponent source, Entity porter)
        {
            if (source == null ||
                source.m_state == null)
            {
                return;
            }

            // The reused-vehicle chain ends at the real native release.
            // Its preserved origin has already been consumed by vanilla
            // ReturnStretcher and is copied below into the new ReturnJob.
            ClearActiveVehicleContinuation(porter, source);

            if (!PorterIdentity.IsPorter(porter) ||
                PorterSampleTransportRuntime.IsBusy(porter))
            {
                return;
            }

            BehaviorNurse nurse =
                porter.GetComponent<BehaviorNurse>();
            if (nurse == null ||
                nurse.m_state == null ||
                nurse.m_state.m_nurseState !=
                    NurseState.OverridenReturningStretcher)
            {
                return;
            }

            TileObject patientSide =
                nurse.m_state.m_stretcherPatientSide == null
                    ? null
                    : nurse.m_state.m_stretcherPatientSide.GetEntity();
            TileObject movingSide =
                nurse.m_state.m_stretcherNurseSide == null
                    ? null
                    : nurse.m_state.m_stretcherNurseSide.GetEntity();
            if (patientSide == null || movingSide == null)
            {
                return;
            }

            ReturnJob job = new ReturnJob();
            job.Porter = porter;
            job.SourcePatient = StretcherEntityField == null
                ? null
                : StretcherEntityField.GetValue(source) as Entity;
            job.Phase = ReturnPhase.ReturningEquipment;
            job.PatientSide = patientSide;
            job.MovingSide = movingSide;
            job.OriginalPosition =
                source.m_state.m_originalStretcherPosition;
            job.OriginalOrientation =
                source.m_state.m_originalStretcherOrientation;
            job.OriginalFloor =
                source.m_state.m_originalStretcherFloor;
            job.CanReuseVehicle = true;
            Jobs[porter] = job;
        }

        // The nurse's vanilla update must run while it is physically
        // returning the equipment. HPO takes control only after that.
        internal static bool UpdateActiveReturn(
            BehaviorNurse nurse,
            Entity porter)
        {
            if (porter == null)
            {
                return false;
            }

            ReturnJob job;
            if (!Jobs.TryGetValue(porter, out job))
            {
                // Loading a save made during vanilla equipment return:
                // recover the station-return obligation without inventing
                // an origin for a reusable vehicle.
                if (nurse != null &&
                    nurse.m_state != null &&
                    nurse.m_state.m_nurseState ==
                        NurseState.OverridenReturningStretcher &&
                    !PorterSampleTransportRuntime.IsBusy(porter))
                {
                    WalkComponent walk =
                        porter.GetComponent<WalkComponent>();
                    TileObject patientSide =
                        nurse.m_state.m_stretcherPatientSide == null
                            ? null
                            : nurse.m_state.m_stretcherPatientSide.GetEntity();
                    TileObject movingSide =
                        nurse.m_state.m_stretcherNurseSide == null
                            ? null
                            : nurse.m_state.m_stretcherNurseSide.GetEntity();

                    job = new ReturnJob();
                    job.Porter = porter;
                    job.Phase = ReturnPhase.ReturningEquipment;
                    job.PatientSide = patientSide;
                    job.MovingSide = movingSide;
                    if (walk != null && walk.m_state != null)
                    {
                        // ReturnStretcher stores the original equipment
                        // position as the native Walk destination.
                        job.OriginalPosition =
                            walk.m_state.m_destination;
                        job.OriginalFloor =
                            walk.m_state.m_destinationFloor;
                    }
                    job.OriginalOrientation =
                        nurse.m_state.m_stretcherOrientation;
                    job.CanReuseVehicle =
                        walk != null &&
                        walk.m_state != null &&
                        patientSide != null &&
                        movingSide != null;
                    Jobs[porter] = job;
                }
                return false;
            }

            if (job.Phase == ReturnPhase.ReturningEquipment)
            {
                if (nurse.m_state != null &&
                    nurse.m_state.m_nurseState ==
                        NurseState.OverridenReturningStretcher)
                {
                    return false; // Native equipment restitution.
                }

                if (nurse.m_state != null &&
                    nurse.m_state.m_nurseState == NurseState.Idle)
                {
                    BeginStationReturn(porter, nurse, job);
                    return Jobs.ContainsKey(porter);
                }

                Jobs.Remove(porter); // Interrupted by another native state.
                return false;
            }

            return UpdateStationReturn(porter, nurse, job);
        }

        internal static void OnNurseUpdated(
            BehaviorNurse nurse,
            Entity porter)
        {
            ReturnJob job;
            if (porter == null ||
                !Jobs.TryGetValue(porter, out job) ||
                job.Phase != ReturnPhase.ReturningEquipment ||
                nurse == null || nurse.m_state == null)
            {
                return;
            }

            if (nurse.m_state.m_nurseState == NurseState.Idle)
            {
                BeginStationReturn(porter, nurse, job);
            }
            else if (nurse.m_state.m_nurseState !=
                     NurseState.OverridenReturningStretcher)
            {
                Jobs.Remove(porter);
            }
        }

        internal static List<PorterPatientStationReturnSnapshot>
            GetStationReturnSnapshot()
        {
            List<PorterPatientStationReturnSnapshot> returns =
                new List<PorterPatientStationReturnSnapshot>();
            foreach (KeyValuePair<Entity, ReturnJob> pair in Jobs)
            {
                if (pair.Value == null ||
                    pair.Value.Phase != ReturnPhase.ReturningToStation)
                {
                    continue;
                }

                PorterPatientStationReturnSnapshot snapshot =
                    new PorterPatientStationReturnSnapshot();
                snapshot.Porter = pair.Key;
                snapshot.ContinuationDeclined =
                    pair.Value.ContinuationDeclined;
                returns.Add(snapshot);
            }
            return returns;
        }

        internal static bool RestoreStationReturn(
            Entity porter,
            bool continuationDeclined)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                PorterSampleTransportRuntime.IsBusy(porter) ||
                Jobs.ContainsKey(porter))
            {
                return false;
            }

            BehaviorNurse nurse = porter.GetComponent<BehaviorNurse>();
            if (nurse == null ||
                nurse.m_state == null ||
                nurse.m_state.m_nurseState != NurseState.Idle)
            {
                return false;
            }

            ReturnJob job = new ReturnJob();
            job.Porter = porter;
            job.Phase = ReturnPhase.ReturningToStation;
            job.ContinuationDeclined = continuationDeclined;
            Jobs[porter] = job;
            return true;
        }

        private static bool IsPorterOperational(Entity porter)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                PorterSampleTransportRuntime.IsBusy(porter) ||
                DayTime.Instance == null ||
                Database.Instance == null)
            {
                return false;
            }

            BehaviorNurse nurse =
                porter.GetComponent<BehaviorNurse>();
            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            GameDBEmployeeRole role =
                Database.Instance.GetEntry<GameDBEmployeeRole>(
                    PorterIds.PatientTransportRole);
            Room home = employee == null ||
                employee.m_state == null ||
                employee.m_state.m_homeRoom == null
                    ? null
                    : employee.m_state.m_homeRoom.GetEntity();
            return nurse != null &&
                nurse.m_state != null &&
                employee != null &&
                employee.m_state != null &&
                !employee.IsFired() &&
                !employee.ShouldGoToTraining() &&
                employee.m_state.m_shift == DayTime.Instance.GetShift() &&
                role != null &&
                employee.HasRole(role) &&
                home != null &&
                home.GetEquipmentOk() &&
                home.m_roomPersistentData.m_roomType != null &&
                PorterStationRegistry.IsPorterStation(
                    home.m_roomPersistentData.m_roomType.Entry);
        }

        private static bool TryCreatePreflightJob(
            Entity porter,
            Entity sourcePatient,
            out ReturnJob job)
        {
            job = null;
            if (porter == null ||
                sourcePatient == null ||
                Jobs.ContainsKey(porter) ||
                !IsPorterOperational(porter))
            {
                return false;
            }

            BehaviorNurse nurse =
                porter.GetComponent<BehaviorNurse>();
            StretcherComponent source =
                sourcePatient.GetComponent<StretcherComponent>();
            if (nurse == null ||
                nurse.m_state == null ||
                nurse.m_state.m_nurseState !=
                    NurseState.OverridenMovingStretcher ||
                !IsPreflightSourceState(source) ||
                source.m_state.m_movingCharacter == null ||
                source.m_state.m_movingCharacter.GetEntity() !=
                    porter)
            {
                return false;
            }

            job = new ReturnJob();
            job.Porter = porter;
            job.SourcePatient = sourcePatient;
            job.Phase = ReturnPhase.ReturningEquipment;
            job.PatientSide =
                source.m_state.m_stretcherPatientSide == null
                    ? null
                    : source.m_state.m_stretcherPatientSide.GetEntity();
            job.MovingSide =
                source.m_state.m_stretcherMovingCharacterSide == null
                    ? null
                    : source.m_state.m_stretcherMovingCharacterSide.GetEntity();
            job.OriginalPosition =
                source.m_state.m_originalStretcherPosition;
            job.OriginalOrientation =
                source.m_state.m_originalStretcherOrientation;
            job.OriginalFloor =
                source.m_state.m_originalStretcherFloor;
            job.CanReuseVehicle = true;
            return IsPreflightJobUsable(job);
        }

        private static bool IsPreflightSourceState(
            StretcherComponent source)
        {
            if (source == null ||
                source.m_state == null ||
                source.m_state.m_stretcherState !=
                    StretcherComponentState.ReleasingMovingCharacter)
            {
                return false;
            }

            return source.m_state.m_stretcherUse ==
                    StretcherUse.RETURN_STRETCHER ||
                source.m_state.m_stretcherUse ==
                    StretcherUse.WAIT_WITH_PATIENT_RETURN_STRETCHER;
        }

        private static bool IsPreflightJobUsable(ReturnJob job)
        {
            if (job == null ||
                !job.CanReuseVehicle ||
                !IsPorterOperational(job.Porter) ||
                Jobs.ContainsKey(job.Porter) ||
                MapScriptInterface.Instance == null ||
                GridMap.GetInstance() == null ||
                job.SourcePatient == null ||
                job.PatientSide == null ||
                job.MovingSide == null ||
                job.PatientSide.m_state == null ||
                job.MovingSide.m_state == null ||
                job.PatientSide.m_state.m_error > 0 ||
                job.MovingSide.m_state.m_error > 0)
            {
                return false;
            }

            BehaviorNurse nurse =
                job.Porter.GetComponent<BehaviorNurse>();
            StretcherComponent source =
                job.SourcePatient.GetComponent<StretcherComponent>();
            if (nurse == null ||
                nurse.m_state == null ||
                nurse.m_state.m_nurseState !=
                    NurseState.OverridenMovingStretcher ||
                !IsPreflightSourceState(source) ||
                source.m_state.m_movingCharacter == null ||
                source.m_state.m_movingCharacter.GetEntity() !=
                    job.Porter ||
                source.m_state.m_stretcherPatientSide == null ||
                source.m_state.m_stretcherMovingCharacterSide == null ||
                source.m_state.m_stretcherPatientSide.GetEntity() !=
                    job.PatientSide ||
                source.m_state.m_stretcherMovingCharacterSide.GetEntity() !=
                    job.MovingSide)
            {
                return false;
            }

            return job.PatientSide.IsAttachedToCharacter() &&
                job.MovingSide.IsAttachedToCharacter() &&
                job.PatientSide.User == job.Porter &&
                job.MovingSide.User == job.Porter;
        }

        private static bool IsReturnJobUsable(ReturnJob job)
        {
            if (job == null ||
                !IsPorterOperational(job.Porter) ||
                MapScriptInterface.Instance == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            BehaviorNurse nurse =
                job.Porter.GetComponent<BehaviorNurse>();
            if (job.Phase == ReturnPhase.ReturningToStation)
            {
                return nurse.m_state.m_nurseState == NurseState.Idle &&
                    nurse.m_state.m_currentPatient == null;
            }

            if (!job.CanReuseVehicle ||
                job.PatientSide == null ||
                job.MovingSide == null ||
                nurse.m_state.m_nurseState !=
                    NurseState.OverridenReturningStretcher ||
                nurse.m_state.m_stretcherPatientSide == null ||
                nurse.m_state.m_stretcherNurseSide == null ||
                nurse.m_state.m_stretcherPatientSide.GetEntity() !=
                    job.PatientSide ||
                nurse.m_state.m_stretcherNurseSide.GetEntity() !=
                    job.MovingSide ||
                job.PatientSide.m_state == null ||
                job.MovingSide.m_state == null ||
                job.PatientSide.m_state.m_error > 0 ||
                job.MovingSide.m_state.m_error > 0)
            {
                return false;
            }

            return job.PatientSide.IsAttachedToCharacter() &&
                job.MovingSide.IsAttachedToCharacter() &&
                job.PatientSide.User == job.Porter &&
                job.MovingSide.User == job.Porter;
        }

        private static bool CanContinue(ReturnJob job, Entity patient)
        {
            return IsReturnJobUsable(job) &&
                CanContinueCore(job, patient, false);
        }

        private static bool CanContinuePreflight(
            ReturnJob job,
            Entity patient)
        {
            return IsPreflightJobUsable(job) &&
                CanContinueCore(job, patient, true);
        }

        private static bool CanContinueCore(
            ReturnJob job,
            Entity patient,
            bool preflight)
        {
            if (job == null ||
                patient == null ||
                patient == job.SourcePatient)
            {
                return false;
            }

            TileObject destination;
            LyingState lyingState;
            StretcherUse use;
            BedReservation bedReservation;
            MovementType movement;
            Vehicle requestedVehicle;
            bool waited;
            if (!StretcherDestinationPatch.TryGetPatientContinuationRequest(
                    patient, out destination, out lyingState, out use,
                    out bedReservation, out movement,
                    out requestedVehicle, out waited) ||
                destination == null ||
                destination.m_state == null ||
                !destination.HasTag("transport_point"))
            {
                return false;
            }

            StretcherComponent target =
                patient.GetComponent<StretcherComponent>();
            if (target == null ||
                target.m_state == null ||
                target.m_state.m_stretcherState !=
                    StretcherComponentState.Idle ||
                target.m_state.m_movingCharacter != null ||
                target.m_state.m_stretcherPatientSide != null ||
                target.m_state.m_stretcherMovingCharacterSide != null)
            {
                return false;
            }

            if (job.Phase == ReturnPhase.ReturningEquipment)
            {
                bool wheelchair =
                    job.PatientSide.HasTag("wheelchair_patient");
                if ((wheelchair &&
                     requestedVehicle != Vehicle.WHEELCHAIR) ||
                    (!wheelchair &&
                     requestedVehicle != Vehicle.WHEELCHAIR &&
                     requestedVehicle != Vehicle.STRETCHER))
                {
                    return false;
                }
            }

            Vector2i pickup;
            int pickupFloor;
            if (!TryGetPickup(patient, out pickup, out pickupFloor))
            {
                return false;
            }

            WalkComponent porterWalk =
                job.Porter.GetComponent<WalkComponent>();
            EmployeeComponent employee =
                job.Porter.GetComponent<EmployeeComponent>();
            BehaviorPatient patientBehavior =
                patient.GetComponent<BehaviorPatient>();
            Department targetDepartment =
                patientBehavior == null
                    ? null
                    : patientBehavior.GetDepartment();
            Department porterDepartment =
                employee == null ||
                employee.m_state == null ||
                employee.m_state.m_department == null
                    ? null
                    : employee.m_state.m_department.GetEntity();
            if (porterWalk == null ||
                employee == null ||
                targetDepartment == null ||
                porterDepartment == null)
            {
                return false;
            }

            float route = GridMap.GetInstance().GetDistance(
                porterWalk.GetFloorIndex(),
                porterWalk.GetCurrentTile(),
                pickupFloor,
                pickup,
                AccessRights.STAFF);
            if (route < 0f)
            {
                return false;
            }

            if (job.Phase == ReturnPhase.ReturningToStation &&
                !StretcherDestinationPatch.HasFreePatientTransportEquipment(
                    patient, porterDepartment, requestedVehicle))
            {
                return false;
            }

            bool pendingLocalSampleWork = false;
            if (!preflight)
            {
                // Preserve Patient API v1 behavior exactly. During the real
                // return window the Porter is already registered in this
                // Patient ReturnJob, so the Sample availability check
                // short-circuits before scanning. Running that scan here,
                // before ReleaseMovingCharacter, would produce a different
                // answer and can update Sample fallback housekeeping.
                pendingLocalSampleWork =
                    PorterSampleTransportRuntime.HasPendingLocalSampleWork(
                        job.Porter);
            }

            if (porterDepartment != targetDepartment &&
                (StretcherDestinationPatch.HasAvailableLocalPatientPorter(
                     targetDepartment) ||
                 pendingLocalSampleWork ||
                 !PorterPatientHelp.ShouldAccept(
                     job.Porter, patient, destination,
                     requestedVehicle, route, waited)))
            {
                return false;
            }

            Vector2i tile = destination.GetDefaultStretcherTile();
            int floor = destination.GetFloorIndex();
            return
                MapScriptInterface.Instance.GetCenterObjectAt(
                    tile, floor) == null &&
                MapScriptInterface.Instance.GetTileReservedBy(
                    tile, floor) == null;
        }

        private static bool TryGetPickup(
            Entity patient,
            out Vector2i pickup,
            out int floor)
        {
            pickup = Vector2i.ZERO_VECTOR;
            floor = -1;
            WalkComponent walk =
                patient == null
                    ? null
                    : patient.GetComponent<WalkComponent>();
            TileObject bed =
                walk == null ||
                walk.m_state == null ||
                walk.m_state.m_objectSittingOn == null
                    ? null
                    : walk.m_state.m_objectSittingOn.GetEntity();
            if (bed == null)
            {
                return false;
            }

            pickup = bed.GetDefaultStretcherTile();
            floor = bed.GetFloorIndex();
            return floor >= 0;
        }

        private static void RegisterActiveVehicleContinuation(
            ReturnJob job,
            Entity patient,
            TileObject destination,
            LyingState lyingState,
            StretcherUse use,
            BedReservation reservation,
            MovementType movement,
            Vehicle requestedVehicle,
            StretcherComponent target)
        {
            if (job == null ||
                job.Porter == null ||
                patient == null ||
                target == null ||
                target.m_state == null ||
                job.PatientSide == null ||
                job.MovingSide == null)
            {
                return;
            }

            ActiveVehicleContinuation continuation =
                new ActiveVehicleContinuation();
            continuation.Porter = job.Porter;
            continuation.Patient = patient;
            continuation.PatientSide = job.PatientSide;
            continuation.MovingSide = job.MovingSide;
            continuation.OriginalPosition = job.OriginalPosition;
            continuation.OriginalOrientation = job.OriginalOrientation;
            continuation.OriginalFloor = job.OriginalFloor;
            continuation.Destination = destination;
            continuation.LyingState = lyingState;
            continuation.StretcherUse = use;
            continuation.BedReservation = reservation;
            continuation.MovementType = movement;
            continuation.RequestedVehicle = requestedVehicle;
            continuation.AwaitingNativeReplay = true;
            ActiveVehicleContinuations[job.Porter] = continuation;

            PorterDiagnostics.Log(
                "patient continuation vehicle origin armed: porter=" +
                (job.Porter.Name ?? string.Empty).Trim() +
                "; patient=" +
                (patient.Name ?? string.Empty).Trim() +
                "; origin=" +
                DescribeOrigin(
                    continuation.OriginalPosition,
                    continuation.OriginalFloor,
                    continuation.OriginalOrientation) +
                "; vehicle={" +
                DescribeVehicleState(job.MovingSide, job.Porter) +
                "}.");
        }

        internal static bool TryAcknowledgeNativeContinuationReplay(
            StretcherComponent target,
            TileObject destination,
            LyingState lyingState,
            StretcherUse use,
            BedReservation reservation,
            MovementType movement,
            Vehicle requestedVehicle)
        {
            if (target == null ||
                target.m_state == null ||
                target.m_state.m_movingCharacter == null)
            {
                return false;
            }

            Entity porter =
                target.m_state.m_movingCharacter.GetEntity();
            ActiveVehicleContinuation continuation;
            if (porter == null ||
                !ActiveVehicleContinuations.TryGetValue(
                    porter, out continuation) ||
                continuation == null ||
                !continuation.AwaitingNativeReplay)
            {
                return false;
            }

            if (!IsActiveVehicleContinuationUsable(
                    continuation, target))
            {
                ActiveVehicleContinuations.Remove(porter);
                return false;
            }

            continuation.AwaitingNativeReplay = false;
            bool sameRequest =
                SameTileObject(destination, continuation.Destination) &&
                lyingState == continuation.LyingState &&
                use == continuation.StretcherUse &&
                reservation == continuation.BedReservation &&
                movement == continuation.MovementType &&
                requestedVehicle == continuation.RequestedVehicle;

            PorterDiagnostics.Log(
                sameRequest
                    ? "patient continuation native SetDestination replay " +
                      "acknowledged without replaying vanilla: porter=" +
                      (porter.Name ?? string.Empty).Trim() +
                      "; patient=" +
                      (continuation.Patient.Name ?? string.Empty).Trim() +
                      "."
                    : "patient continuation native request changed before " +
                      "replay; vanilla retarget remains allowed while HPO " +
                      "keeps the vehicle origin: porter=" +
                      (porter.Name ?? string.Empty).Trim() +
                      "; patient=" +
                      (continuation.Patient.Name ?? string.Empty).Trim() +
                      ".");

            return sameRequest;
        }

        internal static void RestoreActiveVehicleContinuationOrigin(
            StretcherComponent target,
            string stage)
        {
            if (target == null ||
                target.m_state == null ||
                target.m_state.m_movingCharacter == null)
            {
                return;
            }

            Entity porter =
                target.m_state.m_movingCharacter.GetEntity();
            ActiveVehicleContinuation continuation;
            if (porter == null ||
                !ActiveVehicleContinuations.TryGetValue(
                    porter, out continuation) ||
                continuation == null)
            {
                return;
            }

            if (!IsActiveVehicleContinuationUsable(
                    continuation, target))
            {
                ActiveVehicleContinuations.Remove(porter);
                PorterDiagnostics.Log(
                    "patient continuation vehicle origin guard cleared " +
                    "after vehicle/state mismatch: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; stage=" + stage + ".");
                return;
            }

            bool changed =
                target.m_state.m_originalStretcherPosition.m_x !=
                    continuation.OriginalPosition.m_x ||
                target.m_state.m_originalStretcherPosition.m_y !=
                    continuation.OriginalPosition.m_y ||
                target.m_state.m_originalStretcherFloor !=
                    continuation.OriginalFloor ||
                target.m_state.m_originalStretcherOrientation !=
                    continuation.OriginalOrientation;
            if (changed)
            {
                PorterDiagnostics.Log(
                    "patient continuation vehicle origin corrected: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; patient=" +
                    (continuation.Patient.Name ?? string.Empty).Trim() +
                    "; stage=" + stage +
                    "; overwritten=" +
                    DescribeOrigin(
                        target.m_state.m_originalStretcherPosition,
                        target.m_state.m_originalStretcherFloor,
                        target.m_state.m_originalStretcherOrientation) +
                    "; restored=" +
                    DescribeOrigin(
                        continuation.OriginalPosition,
                        continuation.OriginalFloor,
                        continuation.OriginalOrientation) +
                    "; vehicle={" +
                    DescribeVehicleState(
                        continuation.MovingSide,
                        porter) +
                    "}.");
            }

            target.m_state.m_originalStretcherPosition =
                continuation.OriginalPosition;
            target.m_state.m_originalStretcherOrientation =
                continuation.OriginalOrientation;
            target.m_state.m_originalStretcherFloor =
                continuation.OriginalFloor;
        }

        private static bool IsActiveVehicleContinuationUsable(
            ActiveVehicleContinuation continuation,
            StretcherComponent target)
        {
            if (continuation == null ||
                target == null ||
                target.m_state == null ||
                continuation.Porter == null ||
                continuation.Patient == null ||
                continuation.PatientSide == null ||
                continuation.MovingSide == null ||
                target.m_state.m_movingCharacter == null ||
                target.m_state.m_movingCharacter.GetEntity() !=
                    continuation.Porter ||
                target.m_state.m_stretcherPatientSide == null ||
                target.m_state.m_stretcherMovingCharacterSide == null ||
                target.m_state.m_stretcherPatientSide.GetEntity() !=
                    continuation.PatientSide ||
                target.m_state.m_stretcherMovingCharacterSide.GetEntity() !=
                    continuation.MovingSide)
            {
                return false;
            }

            Entity targetPatient =
                StretcherEntityField == null
                    ? null
                    : StretcherEntityField.GetValue(target) as Entity;
            return targetPatient == continuation.Patient &&
                PorterIdentity.IsPorter(continuation.Porter) &&
                continuation.PatientSide.IsAttachedToCharacter() &&
                continuation.MovingSide.IsAttachedToCharacter() &&
                continuation.PatientSide.User == continuation.Porter &&
                continuation.MovingSide.User == continuation.Porter;
        }

        private static void ClearActiveVehicleContinuation(
            Entity porter,
            StretcherComponent source)
        {
            ActiveVehicleContinuation continuation;
            if (porter == null ||
                !ActiveVehicleContinuations.TryGetValue(
                    porter, out continuation) ||
                continuation == null)
            {
                return;
            }

            Entity sourcePatient =
                source == null ||
                StretcherEntityField == null
                    ? null
                    : StretcherEntityField.GetValue(source) as Entity;
            if (sourcePatient == null ||
                sourcePatient == continuation.Patient)
            {
                ActiveVehicleContinuations.Remove(porter);
            }
        }

        internal static List<PorterPatientVehicleContinuationSnapshot>
            GetActiveVehicleContinuationSnapshot()
        {
            List<PorterPatientVehicleContinuationSnapshot> result =
                new List<PorterPatientVehicleContinuationSnapshot>();
            foreach (KeyValuePair<Entity, ActiveVehicleContinuation> pair in
                ActiveVehicleContinuations)
            {
                ActiveVehicleContinuation continuation = pair.Value;
                StretcherComponent target =
                    continuation == null ||
                    continuation.Patient == null
                        ? null
                        : continuation.Patient
                            .GetComponent<StretcherComponent>();
                if (!IsActiveVehicleContinuationUsable(
                        continuation, target) ||
                    continuation.Destination == null)
                {
                    continue;
                }

                PorterPatientVehicleContinuationSnapshot snapshot =
                    new PorterPatientVehicleContinuationSnapshot();
                snapshot.Porter = continuation.Porter;
                snapshot.Patient = continuation.Patient;
                snapshot.Destination = continuation.Destination;
                snapshot.LyingState = continuation.LyingState;
                snapshot.StretcherUse = continuation.StretcherUse;
                snapshot.BedReservation = continuation.BedReservation;
                snapshot.MovementType = continuation.MovementType;
                snapshot.RequestedVehicle =
                    continuation.RequestedVehicle;
                snapshot.AwaitingNativeReplay =
                    continuation.AwaitingNativeReplay;
                result.Add(snapshot);
            }
            return result;
        }

        internal static bool RestoreActiveVehicleContinuation(
            Entity porter,
            Entity patient,
            TileObject destination,
            LyingState lyingState,
            StretcherUse use,
            BedReservation reservation,
            MovementType movement,
            Vehicle requestedVehicle,
            bool awaitingNativeReplay)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                patient == null ||
                destination == null)
            {
                return false;
            }

            StretcherComponent target =
                patient.GetComponent<StretcherComponent>();
            if (target == null ||
                target.m_state == null ||
                target.m_state.m_movingCharacter == null ||
                target.m_state.m_movingCharacter.GetEntity() != porter ||
                target.m_state.m_stretcherPatientSide == null ||
                target.m_state.m_stretcherMovingCharacterSide == null)
            {
                return false;
            }

            ActiveVehicleContinuation continuation =
                new ActiveVehicleContinuation();
            continuation.Porter = porter;
            continuation.Patient = patient;
            continuation.PatientSide =
                target.m_state.m_stretcherPatientSide.GetEntity();
            continuation.MovingSide =
                target.m_state.m_stretcherMovingCharacterSide.GetEntity();
            continuation.OriginalPosition =
                target.m_state.m_originalStretcherPosition;
            continuation.OriginalOrientation =
                target.m_state.m_originalStretcherOrientation;
            continuation.OriginalFloor =
                target.m_state.m_originalStretcherFloor;
            continuation.Destination = destination;
            continuation.LyingState = lyingState;
            continuation.StretcherUse = use;
            continuation.BedReservation = reservation;
            continuation.MovementType = movement;
            continuation.RequestedVehicle = requestedVehicle;
            continuation.AwaitingNativeReplay = awaitingNativeReplay;
            if (!IsActiveVehicleContinuationUsable(
                    continuation, target))
            {
                return false;
            }

            ActiveVehicleContinuations[porter] = continuation;
            PorterDiagnostics.Log(
                "load: restored patient continuation vehicle origin: " +
                "porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; patient=" +
                (patient.Name ?? string.Empty).Trim() +
                "; origin=" +
                DescribeOrigin(
                    continuation.OriginalPosition,
                    continuation.OriginalFloor,
                    continuation.OriginalOrientation) +
                "; awaitingNativeReplay=" +
                awaitingNativeReplay +
                ".");
            return true;
        }

        private static bool SameTileObject(
            TileObject left,
            TileObject right)
        {
            return left == right ||
                (left != null &&
                 right != null &&
                 left.GetEntityID() == right.GetEntityID());
        }

        private static string DescribeOrigin(
            Vector2f position,
            int floor,
            Direction orientation)
        {
            return "[" + position.m_x + "," + position.m_y +
                "] F" + floor + " " + orientation;
        }

        private static string DescribeVehicleState(
            TileObject vehicle,
            Entity porter)
        {
            if (vehicle == null || vehicle.m_state == null)
            {
                return "missing";
            }

            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            Entity user = vehicle.User;
            return
                "position=[" +
                vehicle.m_state.m_position.m_x + "," +
                vehicle.m_state.m_position.m_y +
                "]; movePosition=[" +
                vehicle.m_state.m_movePosition.m_x + "," +
                vehicle.m_state.m_movePosition.m_y +
                "]; floorIndex=" +
                vehicle.m_state.m_floorIndex +
                "; originalFloorIndex=" +
                vehicle.m_state.m_originalFloorIndex +
                "; attached=" +
                vehicle.IsAttachedToCharacter() +
                "; user=" +
                (user == null
                    ? "null"
                    : (user.Name ?? string.Empty).Trim()) +
                "; porterTile=" +
                (walk == null
                    ? "UNKNOWN"
                    : "[" +
                      walk.GetCurrentTile().m_x + "," +
                      walk.GetCurrentTile().m_y + "]") +
                "; porterFloor=" +
                (walk == null ? -1 : walk.GetFloorIndex());
        }

        private static bool TryReuseVehicle(
            ReturnJob job,
            Entity patient,
            TileObject destination,
            LyingState lyingState,
            StretcherUse use,
            BedReservation reservation,
            MovementType movement,
            Vehicle requestedVehicle)
        {
            if (!IsReturnJobUsable(job))
            {
                return false;
            }

            StretcherComponent target =
                patient.GetComponent<StretcherComponent>();
            BehaviorNurse nurse =
                job.Porter.GetComponent<BehaviorNurse>();
            if (target == null ||
                nurse == null ||
                nurse.m_state == null)
            {
                return false;
            }

            Vector2i pickup;
            int pickupFloor;
            WalkComponent porterWalk =
                job.Porter.GetComponent<WalkComponent>();
            if (porterWalk == null ||
                !TryGetPickup(
                    patient,
                    out pickup,
                    out pickupFloor))
            {
                return false;
            }

            Vector2i tile =
                destination.GetDefaultStretcherTile();
            int floor = destination.GetFloorIndex();
            bool started = false;
            try
            {
                // The vehicle is already attached to the Porter. Reuse
                // the vanilla StretcherComponent transport lifecycle.
                MapScriptInterface.Instance.ReserveTile(
                    tile, patient, floor);
                target.m_state.m_movingCharacter = job.Porter;
                target.m_state.m_stretcherPatientSide =
                    job.PatientSide;
                nurse.SwitchState(NurseState.Idle);

                // Vanilla normally assigns the route from the newly attached
                // vehicle to the waiting patient in
                // UpdateStateMovingCharacterWalkingToStretcher(). Reuse
                // skips that state because HPO keeps the vehicle attached,
                // so replace the active ReturnStretcher route explicitly.
                porterWalk.SetDestination(
                    pickup,
                    pickupFloor,
                    movement);

                Vehicle actualVehicle =
                    job.PatientSide.HasTag("wheelchair_patient")
                        ? Vehicle.WHEELCHAIR
                        : Vehicle.STRETCHER;
                started = target.SetDestination(
                    destination, lyingState, use, reservation,
                    job.Porter, movement, actualVehicle);
                if (started)
                {
                    target.m_state.m_originalStretcherPosition =
                        job.OriginalPosition;
                    target.m_state.m_originalStretcherOrientation =
                        job.OriginalOrientation;
                    target.m_state.m_originalStretcherFloor =
                        job.OriginalFloor;
                    RegisterActiveVehicleContinuation(
                        job,
                        patient,
                        destination,
                        lyingState,
                        use,
                        reservation,
                        movement,
                        requestedVehicle,
                        target);
                    // The native return is cancelled only after native
                    // SetDestination has accepted the new transport.
                    nurse.m_state.m_stretcherNurseSide = null;
                    nurse.m_state.m_stretcherPatientSide = null;
                    job.Porter.GetComponent<Behavior>().ReceiveMessage(
                        new Message(Messages.OVERRIDE_BY_MOVING_STRETCHER));
                }
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning(
                    "Patient continuation vehicle reuse failed: " +
                    exception.Message);
            }
            finally
            {
                if (!started)
                {
                    target.m_state.m_movingCharacter = null;
                    target.m_state.m_stretcherPatientSide = null;
                    target.m_state.m_stretcherMovingCharacterSide = null;
                    if (MapScriptInterface.Instance != null &&
                        MapScriptInterface.Instance.GetTileReservedBy(
                            tile, floor) == patient)
                    {
                        MapScriptInterface.Instance.FreeTile(
                            tile, floor);
                    }
                    // Native FreeStretcher released ownership before
                    // this continuation attempt. Restore that free state.
                    job.PatientSide.Owner = null;
                    job.MovingSide.Owner = null;
                    nurse.ReturnStretcher(
                        job.OriginalPosition,
                        job.OriginalOrientation,
                        job.OriginalFloor,
                        job.PatientSide,
                        job.MovingSide);
                }
            }

            return started;
        }

        private static bool TryStartWithNativeVehicle(
            ReturnJob job,
            Entity patient,
            TileObject destination,
            LyingState lyingState,
            StretcherUse use,
            BedReservation reservation,
            MovementType movement,
            Vehicle requestedVehicle)
        {
            StretcherComponent target =
                patient.GetComponent<StretcherComponent>();
            if (target == null)
            {
                return false;
            }

            // The previous vehicle has already been physically restored.
            // Native selection safely obtains equipment for the new job.
            return target.SetDestination(
                destination, lyingState, use, reservation,
                job.Porter, movement, requestedVehicle);
        }

        private static void BeginStationReturn(
            Entity porter,
            BehaviorNurse nurse,
            ReturnJob job)
        {
            if (job == null ||
                !IsPorterOperational(porter) ||
                !HasValidStation(porter))
            {
                Jobs.Remove(porter);
                return;
            }

            job.Phase = ReturnPhase.ReturningToStation;
            job.CanReuseVehicle = false;
            job.PatientSide = null;
            job.MovingSide = null;
            job.HasStationTarget = false;
            UpdateStationReturn(porter, nurse, job);
        }

        private static bool HasValidStation(Entity porter)
        {
            EmployeeComponent employee;
            Room station;
            return PorterIdleResting.TryGetPorterStation(
                    porter, out employee, out station) &&
                station != null &&
                MapScriptInterface.Instance != null;
        }

        private static bool TryPrepareStationTarget(
            Entity porter,
            ReturnJob job,
            out Room station)
        {
            station = null;
            EmployeeComponent employee;
            if (!PorterIdleResting.TryGetPorterStation(
                    porter, out employee, out station) ||
                station == null ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            Vector2i target =
                MapScriptInterface.Instance.GetRandomFreePosition(
                    station, AccessRights.STAFF);
            if (target == Vector2i.ZERO_VECTOR &&
                employee != null &&
                employee.m_state != null &&
                employee.m_state.m_workPlacePosition !=
                    Vector2i.ZERO_VECTOR &&
                station.IsPositionInRoom(
                    employee.m_state.m_workPlacePosition))
            {
                target = employee.m_state.m_workPlacePosition;
            }
            if (target == Vector2i.ZERO_VECTOR)
            {
                return false;
            }

            job.StationTarget = target;
            job.StationFloor = station.GetFloorIndex();
            job.HasStationTarget = true;
            return true;
        }

        private static bool UpdateStationReturn(
            Entity porter,
            BehaviorNurse nurse,
            ReturnJob job)
        {
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (walk == null ||
                nurse == null ||
                nurse.m_state == null ||
                nurse.m_state.m_nurseState != NurseState.Idle ||
                nurse.m_state.m_currentPatient != null ||
                !IsPorterOperational(porter))
            {
                Jobs.Remove(porter);
                return false;
            }

            EmployeeComponent employee;
            Room station;
            if (!PorterIdleResting.TryGetPorterStation(
                    porter, out employee, out station) ||
                station == null ||
                MapScriptInterface.Instance == null)
            {
                Jobs.Remove(porter);
                return false;
            }

            if (walk.IsBusy())
            {
                return true;
            }

            Room current =
                MapScriptInterface.Instance.GetRoomAt(walk);
            if (current == station)
            {
                Jobs.Remove(porter);
                PorterDiagnostics.Log(
                    "patient station return completed: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    ".");
                return false;
            }

            if (!job.HasStationTarget &&
                !TryPrepareStationTarget(
                    porter, job, out station))
            {
                // Keep the obligation without preventing a fresh attempt.
                return true;
            }

            // An interrupted / inaccessible route gets a fresh station
            // target on the next native update, rather than a timer.
            Vector2i target = job.StationTarget;
            int floor = job.StationFloor;
            job.HasStationTarget = false;
            walk.SetDestination(target, floor);
            return true;
        }
    }

    [HarmonyPatch(
        typeof(StretcherComponent),
        nameof(StretcherComponent.ReleaseMovingCharacter),
        new Type[] { })]
    internal static class PorterPatientContinuationReleasePatch
    {
        private static void Prefix(
            StretcherComponent __instance,
            ref Entity __state)
        {
            PorterPatientContinuationRuntime
                .RestoreActiveVehicleContinuationOrigin(
                    __instance,
                    "ReleaseMovingCharacter");

            __state = null;
            if (__instance == null ||
                __instance.m_state == null ||
                __instance.m_state.m_movingCharacter == null ||
                (__instance.m_state.m_stretcherUse !=
                     StretcherUse.RETURN_STRETCHER &&
                 __instance.m_state.m_stretcherUse !=
                     StretcherUse.WAIT_WITH_PATIENT_RETURN_STRETCHER))
            {
                return;
            }

            __state =
                __instance.m_state.m_movingCharacter.GetEntity();
        }

        private static void Postfix(
            StretcherComponent __instance,
            Entity __state)
        {
            try
            {
                PorterPatientContinuationRuntime.OnNativeRelease(
                    __instance, __state);
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning(
                    "Patient return initialization failed: " +
                    exception.Message);
            }
        }
    }
}
