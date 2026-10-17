using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalPorters
{
    internal enum PorterSampleTransportPhase
    {
        GoingToCart,
        GoingToSampleParking,
        GoingToSample,
        TakingSample,
        ReturningToCartAfterSample,
        WaitingForFridge,
        GoingToFridgeParking,
        GoingToFridge,
        StoringSample,
        ReturningToCartAfterFridge,
        ReturningCart,
        RecoveringCartAfterCancel,
        WaitingForCartReturnSlot,
        ReturningToStation
    }

    internal sealed class PorterSampleTransportJob
    {
        internal LabProcedure Procedure;
        internal readonly List<LabProcedure> CollectedProcedures =
            new List<LabProcedure>();
        internal Room StatLab;
        internal TileObject Cart;
        internal Room CartHomeRoom;
        internal Vector2i CartHomeTile;
        internal int CartHomeFloorIndex;
        internal Direction CartHomeOrientation;
        internal bool NativeTimeout;
        internal int RouteRecoveryAttempts;
        internal int CartRestoreRetryCount;
        internal float NextCartRestoreRetryAt;
        internal Vector2i CartReturnTarget;
        internal bool HasCartReturnTarget;
        internal Vector2i StationReturnTarget;
        internal int StationReturnFloorIndex;
        internal bool HasStationReturnTarget;
        internal Vector2i CartParkingTarget;
        internal int CartParkingFloorIndex;
        internal bool HasCartParkingTarget;
        internal Vector2i SampleApproachTarget;
        internal int SampleApproachFloorIndex;
        internal bool HasSampleApproachTarget;
        internal Vector2i SampleRoomWaitTarget;
        internal int SampleRoomWaitFloorIndex;
        internal bool HasSampleRoomWaitTarget;
        // Diagnostics only: logged once when a Porter actually stops at the wait tile.
        internal bool LoggedSampleRoomWaitArrival;
        internal int SampleRoomWaitDeadlineMinute;
        // Independent rescue deadline before the Porter reaches the outdoor wait tile.
        internal int SampleRoomStagingDeadlineMinute;
        internal bool RecoveringParkedCartForSampleRoomWait;
        internal bool ShowingSampleRoomWaitingBubble;
        internal bool FridgeDepositSoundPlayed;
        internal bool PostDeliveryReturn;
        internal bool ContinuationDeclined;
        // WalkComponent builds seated routes from the chair's physical tile,
        // not from the character's actual use position. If the cart use tile
        // matches that chair tile, vanilla can produce a one-node route and
        // remain seated. Stage the native stand-up before routing to the cart.
        internal bool WaitingForStandUpBeforeCart;
        internal PorterSampleTransportPhase Phase;
    }

    internal struct PorterSampleBundleKey :
        IEquatable<PorterSampleBundleKey>
    {
        internal readonly uint PatientId;
        internal readonly uint SourceId;

        internal PorterSampleBundleKey(
            uint patientId,
            uint sourceId)
        {
            PatientId = patientId;
            SourceId = sourceId;
        }

        public bool Equals(PorterSampleBundleKey other)
        {
            return PatientId == other.PatientId &&
                SourceId == other.SourceId;
        }

        public override bool Equals(object obj)
        {
            return obj is PorterSampleBundleKey &&
                Equals((PorterSampleBundleKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)PatientId * 397) ^
                    (int)SourceId;
            }
        }
    }

    internal sealed class PorterSampleFallbackWait
    {
        internal LabProcedure Procedure;
        internal int RetryCount;
        internal int NextRetryGameMinute;
        internal int SuppressedFallbackChecks;
        internal int SuppressedIdleScans;
    }

    internal sealed class PorterSampleFallbackSnapshot
    {
        internal LabProcedure Procedure;
        internal int RetryCount;
        internal int NextRetryGameMinute;
        internal bool Released;
    }

    internal static class PorterSampleTransportRuntime
    {
        private const int CartRestoreRetryMinutes = 2;
        // Housekeeping only: do not throttle mission assignment or reservations.
        private const float OrphanedCartRecoveryIntervalSeconds = 1f;

        // Cross-department Sample help is intentionally probabilistic only for
        // the FIRST foreign pickup. Local work remains deterministic and
        // always keeps its established priority. The stable roll/personality
        // values below are derived from entity IDs: no reroll loop, save field
        // or Unity Random state is introduced.
        private const int ForeignSampleHelpMinimumChance = 5;
        private const int ForeignSampleHelpMaximumChance = 95;
        private const int ForeignSampleHelpDistancePenaltyCap = 25;
        private const float ForeignSampleHelpDistancePenaltyDivisor = 4f;
        private const int ForeignSampleHelpPersonalityRange = 8;
        private const int ForeignSampleHelpMissionVariationRange = 5;
        private const uint ForeignSampleHelpPersonalitySalt = 2654435761u;
        private const uint ForeignSampleHelpMissionSalt = 2246822519u;
        private const uint ForeignSampleHelpRollSalt = 3266489917u;
        private const uint PreBlockedRoomTimeoutSalt = 668265263u;
        private const uint SampleFallbackRetrySalt = 374761393u;
        private const int SamplePorterCapacityWindowMinutes = 24 * 60;
        private const int SamplePorterCapacityFallbackThreshold = 3;

        // Cooldown range is configured in PorterTransportConfig.
        private const int SampleRoomWaitBubbleIndex = 24;
        private const string SamplePickupSoundEvent = "SFX_ENV_PLASTIC_PROP_PICKUP";
        private const string SampleFridgeDepositSoundEvent = "SFX_ENV_DOOR_FRIDGE_CLOSE";
        private const int SampleRoomWaitRadius = 5;
        private const float SampleRoomWaitIdealDistance = 3.5f;
        private const float SampleRoomWaitMaximumDistance = 7f;
        private const float SampleRoomWaitNearDoorDistance = 2f;
        private const float SampleRoomWaitNearDoorPenalty = 50f;
        private const float SampleRoomWaitAnyDoorPenalty = 100f;
        private const float SampleRoomWaitWallPreferenceBonus = 10f;

        private static readonly FieldInfo NurseEntityField =
            AccessTools.Field(typeof(BehaviorNurse), "m_entity");

        private static readonly Dictionary<Entity, PorterSampleTransportJob> Jobs =
            new Dictionary<Entity, PorterSampleTransportJob>();

        // Unity unscaled realtime; never serialized. The first idle check,
        // including after loading, always executes immediately.
        private static readonly Dictionary<Entity, float>
            NextOrphanedCartRecoveryTimes =
                new Dictionary<Entity, float>();

        private static readonly Dictionary<BehaviorLabSpecialist, LabProcedure>
            NativeLabSampleBundles =
                new Dictionary<BehaviorLabSpecialist, LabProcedure>();

        private static readonly Dictionary<PorterSampleBundleKey, PorterSampleFallbackWait>
            FallbackWaits =
                new Dictionary<PorterSampleBundleKey, PorterSampleFallbackWait>();

        private static readonly Dictionary<PorterSampleBundleKey, LabProcedure>
            ReleasedFallbacks =
                new Dictionary<PorterSampleBundleKey, LabProcedure>();

        private static readonly Dictionary<Department, List<int>>
            SamplePorterCapacityFallbackEvents =
                new Dictionary<Department, List<int>>();

        // Native-style anti-spam: at most one staffing popup per department
        // per in-game day. Department.OnDayStart clears this flag.
        private static readonly HashSet<Department>
            SamplePorterCapacityNotifications =
                new HashSet<Department>();

        // Independent of the standard pre-assignment fallback retries.
        // 1 = the first Porter wait expired and one more HPO job may be assigned.
        private static readonly Dictionary<LabProcedure, int>
            RoomWaitRetryCounts = new Dictionary<LabProcedure, int>();

        // Absolute in-game minute when this physical bundle becomes eligible
        // for its single second Porter attempt.
        private static readonly Dictionary<LabProcedure, int>
            RoomWaitRetryAvailableMinutes =
                new Dictionary<LabProcedure, int>();

        // Once both outside-room waits expire, only native lab staff may
        // collect this physical sample bundle.
        private static readonly HashSet<LabProcedure>
            RoomWaitReleasedToLab = new HashSet<LabProcedure>();

        // Capacity warnings are scoped to the Porter's actual home station.
        // Multiple Porter Stations in one department must not mask each
        // other's cart shortages.
        private static readonly HashSet<Room>
            MissingCartNotifications =
                new HashSet<Room>();

        private static readonly HashSet<LabProcedure>
            BlockedSamplePickupRooms =
                new HashSet<LabProcedure>();

        // Only for diagnostics: a pre-assignment room block can defer native
        // lab selection without starting a Porter's 40-minute mission clock.
        private static readonly HashSet<LabProcedure>
            LoggedPreAssignmentRoomDeferrals =
                new HashSet<LabProcedure>();

        // The choice is attached to the physical sample bundle, not to a
        // particular Porter or per-frame availability scan.
        private static readonly Dictionary<LabProcedure, bool>
            PreBlockedRoomWaitDecisions =
                new Dictionary<LabProcedure, bool>();

        internal static void Reset()
        {
            Jobs.Clear();
            NextOrphanedCartRecoveryTimes.Clear();
            NativeLabSampleBundles.Clear();
            FallbackWaits.Clear();
            ReleasedFallbacks.Clear();
            SamplePorterCapacityFallbackEvents.Clear();
            SamplePorterCapacityNotifications.Clear();
            RoomWaitRetryCounts.Clear();
            RoomWaitRetryAvailableMinutes.Clear();
            RoomWaitReleasedToLab.Clear();
            MissingCartNotifications.Clear();
            BlockedSamplePickupRooms.Clear();
            LoggedPreAssignmentRoomDeferrals.Clear();
            PreBlockedRoomWaitDecisions.Clear();
        }

        internal static bool IsBusy(Entity porter)
        {
            return porter != null && Jobs.ContainsKey(porter);
        }

        internal static bool IsSampleContinuationWindowOpen(
            Entity porter)
        {
            PorterSampleTransportJob job;
            return porter != null &&
                Jobs.TryGetValue(porter, out job) &&
                job != null &&
                job.PostDeliveryReturn &&
                !job.ContinuationDeclined &&
                (job.Phase ==
                    PorterSampleTransportPhase.ReturningCart ||
                 job.Phase ==
                    PorterSampleTransportPhase.WaitingForCartReturnSlot ||
                 job.Phase ==
                    PorterSampleTransportPhase.ReturningToStation);
        }

        internal static LabProcedure[] GetSampleContinuationCandidates(
            Entity porter)
        {
            if (!IsSampleContinuationWindowOpen(porter) ||
                !IsAvailablePorter(
                    porter,
                    null,
                    allowPostDeliveryReturn: true))
            {
                return new LabProcedure[0];
            }

            List<LabProcedure> free =
                CollectSampleContinuationCandidates(
                    porter,
                    blockedOnly: false);
            if (free.Count > 0)
            {
                return free.ToArray();
            }

            return CollectSampleContinuationCandidates(
                porter,
                blockedOnly: true).ToArray();
        }

        internal static bool FinishSampleContinuationWindow(
            Entity porter)
        {
            PorterSampleTransportJob job;
            if (!IsSampleContinuationWindowOpen(porter) ||
                !Jobs.TryGetValue(porter, out job) ||
                job == null)
            {
                return false;
            }

            // Keep the current cart/station return untouched. This flag only
            // closes API continuation for this post-delivery obligation.
            job.ContinuationDeclined = true;
            return true;
        }

        private static List<LabProcedure>
            CollectSampleContinuationCandidates(
                Entity porter,
                bool blockedOnly)
        {
            List<LabProcedure> result =
                new List<LabProcedure>();
            List<LabProcedure> procedures =
                LabProcedureManager.Instance == null
                    ? null
                    : LabProcedureManager.Instance.m_labProcedures;
            if (procedures == null)
            {
                return result;
            }

            HashSet<PorterSampleBundleKey> seenBundles =
                new HashSet<PorterSampleBundleKey>();

            for (int i = 0; i < procedures.Count; i++)
            {
                LabProcedure procedure = procedures[i];
                if (procedure == null ||
                    procedure.m_state == null ||
                    procedure.m_state.m_labProcedureState !=
                        LabProcedureState.SamplingFinished)
                {
                    continue;
                }

                Room sourceRoom =
                    GetSampleSourceRoom(procedure);
                PorterSampleBundleKey bundleKey;
                if (!TryGetSampleBundleKey(
                        procedure,
                        sourceRoom,
                        out bundleKey) ||
                    seenBundles.Contains(bundleKey))
                {
                    continue;
                }

                if (ReleasedFallbacks.ContainsKey(bundleKey))
                {
                    continue;
                }

                PorterSampleFallbackWait fallbackWait;
                if (FallbackWaits.TryGetValue(
                        bundleKey,
                        out fallbackWait) &&
                    fallbackWait != null &&
                    GetSampleFallbackClockMinute() <
                        fallbackWait.NextRetryGameMinute)
                {
                    continue;
                }

                float distance;
                if (!CanTakeProcedure(
                        porter,
                        procedure,
                        allowForeignDepartment: true,
                        porterAvailabilityAlreadyValidated: true,
                        allowPreBlockedRoom: true,
                        out distance))
                {
                    continue;
                }

                bool blocked =
                    IsSamplePickupRoomBlocked(procedure);
                if (blocked != blockedOnly)
                {
                    continue;
                }

                if (blocked &&
                    !HasRoomWaitRetryForBundle(procedure) &&
                    !ShouldWaitForPreBlockedRoom(procedure))
                {
                    continue;
                }

                seenBundles.Add(bundleKey);
                result.Add(procedure);
            }

            return result;
        }

        internal static bool TryStartSampleContinuation(
            Entity porter,
            LabProcedure procedure)
        {
            if (porter == null ||
                procedure == null ||
                !IsSampleContinuationWindowOpen(porter) ||
                !IsAvailablePorter(
                    porter,
                    procedure,
                    allowPostDeliveryReturn: true))
            {
                return false;
            }

            LabProcedure[] candidates =
                GetSampleContinuationCandidates(porter);
            bool candidateFound = false;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] == procedure)
                {
                    candidateFound = true;
                    break;
                }
            }
            if (!candidateFound)
            {
                return false;
            }

            PorterSampleTransportJob job;
            if (!Jobs.TryGetValue(porter, out job) ||
                job == null)
            {
                return false;
            }

            BehaviorNurse nurse =
                porter.GetComponent<BehaviorNurse>();
            UseComponent use =
                porter.GetComponent<UseComponent>();
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            TileObject source =
                procedure.m_state == null ||
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment.GetEntity();
            Room statLab =
                procedure.m_state == null ||
                procedure.m_state.m_statLab == null
                    ? null
                    : procedure.m_state.m_statLab.GetEntity();
            if (nurse == null ||
                use == null ||
                walk == null ||
                source == null ||
                statLab == null)
            {
                return false;
            }

            bool hasCart = job.Cart != null;
            if (hasCart &&
                (job.Cart.User != porter ||
                 !job.Cart.m_state.m_moving ||
                 !job.Cart.IsAttachedToCharacter()))
            {
                return false;
            }

            bool blocked =
                IsSamplePickupRoomBlocked(procedure);
            Vector2i parkingTile =
                Vector2i.ZERO_VECTOR;

            ClearCartParkingTarget(job);
            if (hasCart &&
                !blocked &&
                !PorterSampleCartRuntime.TryGetInteractionParkingTile(
                    porter,
                    job,
                    source,
                    out parkingTile))
            {
                return false;
            }

            if (use.m_state.m_reservedObject != null ||
                use.IsUsing())
            {
                use.Interrupt();
            }

            use.ReserveObject(source);
            if (source.User != porter)
            {
                ClearCartParkingTarget(job);
                return false;
            }

            if (walk.IsBusy())
            {
                walk.Stop();
            }

            nurse.CancelBrowsing();
            nurse.SwitchState(NurseState.Idle);

            HideSampleRoomWaitingBubble(porter, job);
            job.Procedure = procedure;
            job.CollectedProcedures.Clear();
            job.StatLab = statLab;
            job.NativeTimeout = false;
            job.PostDeliveryReturn = false;
            job.ContinuationDeclined = false;
            job.RouteRecoveryAttempts = 0;
            job.CartRestoreRetryCount = 0;
            job.NextCartRestoreRetryAt = 0f;
            job.HasCartReturnTarget = false;
            job.HasStationReturnTarget = false;
            job.HasSampleApproachTarget = false;
            job.HasSampleRoomWaitTarget = false;
            job.RecoveringParkedCartForSampleRoomWait = false;
            job.FridgeDepositSoundPlayed = false;
            ResetSampleRoomWaitTimeout(job);

            ClearFallbackTracking(procedure);
            ShowSampleBubble(porter, procedure);

            if (hasCart)
            {
                job.Phase =
                    PorterSampleTransportPhase.GoingToSampleParking;

                if (blocked)
                {
                    HandleBlockedSampleRoomApproach(
                        porter,
                        nurse,
                        walk,
                        job);
                }
                else
                {
                    SetSampleApproachTarget(
                        job,
                        parkingTile,
                        source.GetFloorIndex());
                    walk.SetDestination(
                        parkingTile,
                        source.GetFloorIndex());
                }
            }
            else
            {
                Vector2i sampleTarget =
                    ToTile(source.GetDefaultUsePosition());
                job.Phase =
                    PorterSampleTransportPhase.GoingToSample;
                SetSampleApproachTarget(
                    job,
                    sampleTarget,
                    source.GetFloorIndex());
                walk.SetDestination(
                    sampleTarget,
                    source.GetFloorIndex());

                if (blocked)
                {
                    HandleBlockedSampleRoomApproach(
                        porter,
                        nurse,
                        walk,
                        job);
                }
            }

            PorterDiagnostics.Log(
                "sample continuation accepted: porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; procedureId=" +
                procedure.GetEntityID() +
                "; cart=" +
                hasCart +
                "; sourceFloor=" +
                source.GetFloorIndex() +
                "; labFloor=" +
                statLab.GetFloorIndex() +
                ".");

            return true;
        }

        internal static List<KeyValuePair<LabProcedure, bool>>
            GetPreBlockedRoomWaitDecisionsSnapshot()
        {
            List<KeyValuePair<LabProcedure, bool>> snapshot =
                new List<KeyValuePair<LabProcedure, bool>>();
            foreach (KeyValuePair<LabProcedure, bool> pair in
                PreBlockedRoomWaitDecisions)
            {
                if (pair.Key != null && pair.Key.m_state != null &&
                    pair.Key.m_state.m_labProcedureState ==
                        LabProcedureState.SamplingFinished)
                {
                    snapshot.Add(pair);
                }
            }
            return snapshot;
        }

        internal static bool RestorePreBlockedRoomWaitDecision(
            LabProcedure procedure, bool wait)
        {
            if (procedure == null || procedure.m_state == null ||
                procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SamplingFinished)
            {
                return false;
            }
            PreBlockedRoomWaitDecisions[procedure] = wait;
            return true;
        }

        private static void ClearPreBlockedRoomWaitDecision(
            LabProcedure procedure)
        {
            if (procedure == null ||
                PreBlockedRoomWaitDecisions.Count == 0)
            {
                return;
            }
            List<LabProcedure> matches = new List<LabProcedure>();
            foreach (KeyValuePair<LabProcedure, bool> pair in
                PreBlockedRoomWaitDecisions)
            {
                if (AreSameSampleBundle(pair.Key, procedure))
                {
                    matches.Add(pair.Key);
                }
            }
            for (int i = 0; i < matches.Count; i++)
            {
                PreBlockedRoomWaitDecisions.Remove(matches[i]);
            }
        }

        private static bool ShouldWaitForPreBlockedRoom(
            LabProcedure procedure)
        {
            bool previousChoice;
            if (PreBlockedRoomWaitDecisions.TryGetValue(
                    procedure, out previousChoice))
            {
                return previousChoice;
            }

            foreach (KeyValuePair<LabProcedure, bool> pair in
                PreBlockedRoomWaitDecisions)
            {
                if (AreSameSampleBundle(pair.Key, procedure))
                {
                    return pair.Value;
                }
            }

            int chance =
                PorterTransportConfig.SampleOccupiedRoomPreAssignmentWaitChance;
            int roll = UnityEngine.Random.Range(0, 100);
            bool wait = roll < chance;
            PreBlockedRoomWaitDecisions[procedure] = wait;

            PorterDiagnostics.Log(
                "sample pre-block wait decision: patient=" +
                GetPatientName(procedure) +
                "; procedureId=" + procedure.GetEntityID() +
                "; chance=" + chance +
                "; roll=" + roll +
                "; decision=" + (wait ? "wait" : "skip") +
                "; note=one-choice-per-physical-bundle-until-room-free.");
            return wait;
        }

        internal static List<KeyValuePair<LabProcedure, int>>
            GetRoomWaitRetrySnapshot()
        {
            List<KeyValuePair<LabProcedure, int>> snapshot =
                new List<KeyValuePair<LabProcedure, int>>();

            foreach (KeyValuePair<LabProcedure, int> pair in
                RoomWaitRetryCounts)
            {
                if (pair.Key != null &&
                    pair.Key.m_state != null &&
                    (pair.Key.m_state.m_labProcedureState ==
                        LabProcedureState.SamplingFinished ||
                     pair.Key.m_state.m_labProcedureState ==
                        LabProcedureState.SampleBeingDelivered))
                {
                    snapshot.Add(pair);
                }
            }

            foreach (LabProcedure procedure in RoomWaitReleasedToLab)
            {
                if (procedure != null &&
                    procedure.m_state != null &&
                    (procedure.m_state.m_labProcedureState ==
                        LabProcedureState.SamplingFinished ||
                     procedure.m_state.m_labProcedureState ==
                        LabProcedureState.SampleBeingDelivered))
                {
                    snapshot.Add(
                        new KeyValuePair<LabProcedure, int>(
                            procedure,
                            2));
                }
            }

            return snapshot;
        }

        internal static List<KeyValuePair<LabProcedure, int>>
            GetRoomWaitRetryCooldownSnapshot()
        {
            List<KeyValuePair<LabProcedure, int>> snapshot =
                new List<KeyValuePair<LabProcedure, int>>();

            foreach (KeyValuePair<LabProcedure, int> pair in
                RoomWaitRetryAvailableMinutes)
            {
                if (pair.Key != null &&
                    pair.Key.m_state != null &&
                    pair.Value > 0 &&
                    (pair.Key.m_state.m_labProcedureState ==
                        LabProcedureState.SamplingFinished ||
                     pair.Key.m_state.m_labProcedureState ==
                        LabProcedureState.SampleBeingDelivered))
                {
                    snapshot.Add(pair);
                }
            }

            return snapshot;
        }

        internal static bool RestoreRoomWaitRetryCooldown(
            LabProcedure procedure,
            int availableMinute)
        {
            if (procedure == null ||
                procedure.m_state == null ||
                availableMinute <= 0 ||
                (procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SamplingFinished &&
                 procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SampleBeingDelivered))
            {
                return false;
            }

            RoomWaitRetryCounts[procedure] = 1;
            RoomWaitRetryAvailableMinutes[procedure] =
                availableMinute;

            PorterDiagnostics.Log(
                "load: sample room Porter retry cooldown restored: patient=" +
                GetPatientName(procedure) +
                "; procedureId=" +
                procedure.GetEntityID() +
                "; nextAttempt=2/2" +
                "; availableMinute=" +
                availableMinute +
                ".");
            return true;
        }

        internal static bool RestoreRoomWaitRetry(
            LabProcedure procedure,
            int stage)
        {
            if (procedure == null ||
                procedure.m_state == null ||
                (procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SamplingFinished &&
                 procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SampleBeingDelivered))
            {
                return false;
            }

            if (stage == 1)
            {
                RoomWaitRetryCounts[procedure] = 1;
                PorterDiagnostics.Log(
                    "load: sample room outside wait restored: patient=" +
                    GetPatientName(procedure) +
                    "; procedureId=" +
                    procedure.GetEntityID() +
                    "; waitStage=2/2.");
                return true;
            }

            if (stage == 2)
            {
                RoomWaitRetryCounts.Remove(procedure);
                RoomWaitReleasedToLab.Add(procedure);
                PorterDiagnostics.Log(
                    "load: sample room native handoff restored: patient=" +
                    GetPatientName(procedure) +
                    "; procedureId=" +
                    procedure.GetEntityID() +
                    "; state=" +
                    procedure.m_state.m_labProcedureState +
                    ".");
                return true;
            }

            return false;
        }

        private static bool IsRoomWaitReleasedToLab(
            LabProcedure procedure)
        {
            if (procedure == null)
            {
                return false;
            }

            foreach (LabProcedure released in RoomWaitReleasedToLab)
            {
                if (AreSameSampleBundle(released, procedure))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasRoomWaitRetryForBundle(
            LabProcedure procedure)
        {
            if (procedure == null)
            {
                return false;
            }

            foreach (KeyValuePair<LabProcedure, int> pair in
                RoomWaitRetryCounts)
            {
                if (pair.Value > 0 &&
                    AreSameSampleBundle(pair.Key, procedure))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ClearRoomWaitRetryForBundle(
            LabProcedure procedure)
        {
            if (procedure == null)
            {
                return;
            }

            List<LabProcedure> matching =
                new List<LabProcedure>();
            foreach (KeyValuePair<LabProcedure, int> pair in
                RoomWaitRetryCounts)
            {
                if (AreSameSampleBundle(pair.Key, procedure))
                {
                    matching.Add(pair.Key);
                }
            }

            for (int i = 0; i < matching.Count; i++)
            {
                RoomWaitRetryCounts.Remove(matching[i]);
            }

            matching.Clear();
            foreach (KeyValuePair<LabProcedure, int> pair in
                RoomWaitRetryAvailableMinutes)
            {
                if (AreSameSampleBundle(pair.Key, procedure))
                {
                    matching.Add(pair.Key);
                }
            }

            for (int i = 0; i < matching.Count; i++)
            {
                RoomWaitRetryAvailableMinutes.Remove(matching[i]);
            }
        }

        private static bool IsRoomWaitRetryCoolingDown(
            LabProcedure procedure)
        {
            if (procedure == null ||
                RoomWaitRetryAvailableMinutes.Count == 0)
            {
                return false;
            }

            int availableMinute = 0;
            bool found = false;
            foreach (KeyValuePair<LabProcedure, int> pair in
                RoomWaitRetryAvailableMinutes)
            {
                if (!AreSameSampleBundle(pair.Key, procedure))
                {
                    continue;
                }

                found = true;
                if (pair.Value > availableMinute)
                {
                    availableMinute = pair.Value;
                }
            }

            if (!found)
            {
                return false;
            }

            if (DayTime.Instance == null)
            {
                return true;
            }

            int nowMinute =
                (int)GetCartRestoreClockMinutes();
            if (nowMinute < availableMinute)
            {
                return true;
            }

            List<LabProcedure> matching =
                new List<LabProcedure>();
            foreach (KeyValuePair<LabProcedure, int> pair in
                RoomWaitRetryAvailableMinutes)
            {
                if (AreSameSampleBundle(pair.Key, procedure))
                {
                    matching.Add(pair.Key);
                }
            }

            for (int i = 0; i < matching.Count; i++)
            {
                RoomWaitRetryAvailableMinutes.Remove(matching[i]);
            }

            PorterDiagnostics.Log(
                "sample room Porter retry available: patient=" +
                GetPatientName(procedure) +
                "; procedureId=" +
                procedure.GetEntityID() +
                "; nextAttempt=2/2" +
                "; currentMinute=" +
                nowMinute +
                ".");
            return false;
        }

        private static void ClearRoomWaitTracking(
            LabProcedure procedure)
        {
            if (procedure == null)
            {
                return;
            }

            RoomWaitRetryCounts.Remove(procedure);
            RoomWaitRetryAvailableMinutes.Remove(procedure);
            RoomWaitReleasedToLab.Remove(procedure);
        }

        private static string GetDepartmentDiagnosticId(
            Department department)
        {
            GameDBDepartment departmentType =
                department == null
                    ? null
                    : department.GetDepartmentType();
            return departmentType == null
                ? "NONE"
                : departmentType.DatabaseID.ToString();
        }

        private static string GetRoomTypeDiagnosticId(
            Room room)
        {
            GameDBRoomType roomType =
                room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null
                    ? null
                    : room.m_roomPersistentData.m_roomType.Entry;
            return roomType == null
                ? "NONE"
                : roomType.DatabaseID.ToString();
        }

        // One event per SamplingFinished transition, never once per Update tick.
        // A sample can be finished as an outpatient and thus outside HPO's
        // hospitalized-only scope, regardless of the room's type.
        internal static void LogSamplePickupEligibilityAtSamplingFinished(
            LabProcedure procedure,
            LabProcedureState previousState)
        {
            if (!PorterTransportConfig.EnableDiagnostics ||
                procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SamplingFinished)
            {
                return;
            }

            Entity patient = procedure.m_state.m_patient == null
                ? null
                : procedure.m_state.m_patient.GetEntity();
            HospitalizationComponent hospitalization =
                patient == null
                    ? null
                    : patient.GetComponent<HospitalizationComponent>();
            TileObject source = procedure.m_state.m_usedEquipment == null
                ? null
                : procedure.m_state.m_usedEquipment.GetEntity();
            Room sourceRoom = GetSampleSourceRoom(procedure);

            PorterDiagnostics.Log(
                "sample pickup eligibility snapshot: patient=" +
                GetPatientName(procedure) +
                "; procedureId=" + procedure.GetEntityID() +
                "; previousState=" + previousState +
                "; state=SamplingFinished" +
                "; hospitalizedAtSamplingFinished=" +
                (hospitalization != null &&
                 hospitalization.IsHospitalized()) +
                "; sourceRoomType=" +
                GetRoomTypeDiagnosticId(sourceRoom) +
                "; sourceFloor=" +
                (source == null ? -1 : source.GetFloorIndex()) +
                "; sourceExists=" + (source != null) +
                "; sourceUser=" +
                (source == null || source.User == null
                    ? "NONE"
                    : (source.User.Name ?? string.Empty).Trim()) +
                "; sourceOwnerPresent=" +
                (source != null && source.Owner != null) +
                "; sourceDepartment=" +
                GetDepartmentDiagnosticId(
                    GetProcedureDepartment(procedure)) +
                "; patientDepartment=" +
                GetDepartmentDiagnosticId(
                    GetPatientDepartment(procedure)) +
                "; porterEligibleByHospitalization=" +
                (hospitalization != null &&
                 hospitalization.IsHospitalized()) +
                "; note=other-eligibility-conditions-not-yet-evaluated.");
        }

        internal static void LogLabProcedureDelivered(
            LabProcedure procedure,
            string transition)
        {
            if (procedure == null || procedure.m_state == null ||
                procedure.m_state.m_labProcedureState !=
                    LabProcedureState.DeliveredToStatLab)
            {
                return;
            }

            Entity deliveryPatient =
                procedure.m_state.m_patient == null
                    ? null
                    : procedure.m_state.m_patient.GetEntity();
            HospitalizationComponent deliveryHospitalization =
                deliveryPatient == null
                    ? null
                    : deliveryPatient.GetComponent<HospitalizationComponent>();
            bool hospitalizedAtDelivery =
                deliveryHospitalization != null &&
                deliveryHospitalization.IsHospitalized();

            string handler =
                hospitalizedAtDelivery
                    ? "vanilla-or-untracked"
                    : "patient self-delivery";
            if (IsProcedureClaimed(procedure))
            {
                handler = "HPO Porter";
            }
            else
            {
                foreach (KeyValuePair<BehaviorLabSpecialist, LabProcedure> pair in
                    NativeLabSampleBundles)
                {
                    if (AreSameSampleBundle(pair.Value, procedure))
                    {
                        handler = "native Lab Specialist";
                        break;
                    }
                }
            }

            bool roomWaitExhausted =
                IsRoomWaitReleasedToLab(procedure);
            Room sourceRoom = GetSampleSourceRoom(procedure);
            Department patientDepartment =
                GetPatientDepartment(procedure);
            Department missionDepartment =
                GetProcedureDepartment(procedure);
            Department sourceRoomDepartment =
                sourceRoom == null ||
                sourceRoom.m_roomPersistentData == null ||
                sourceRoom.m_roomPersistentData.m_department == null
                    ? null
                    : sourceRoom.m_roomPersistentData.m_department.GetEntity();
            Room labRoom =
                procedure.m_state.m_statLab == null
                    ? null
                    : procedure.m_state.m_statLab.GetEntity();

            PorterDiagnostics.Log(
                "sample delivery outcome: patient=" +
                GetPatientName(procedure) +
                "; examination=" +
                (procedure.m_state.m_examination == null ||
                 procedure.m_state.m_examination.Entry == null
                    ? "UNKNOWN"
                    : procedure.m_state.m_examination.Entry.DatabaseID.ToString()) +
                "; procedureId=" +
                procedure.GetEntityID() +
                "; patientDepartment=" +
                GetDepartmentDiagnosticId(
                    patientDepartment) +
                "; missionDepartment=" +
                GetDepartmentDiagnosticId(
                    missionDepartment) +
                "; sourceRoomDepartment=" +
                GetDepartmentDiagnosticId(
                    sourceRoomDepartment) +
                "; sourceRoomType=" +
                GetRoomTypeDiagnosticId(sourceRoom) +
                "; state=DeliveredToStatLab" +
                "; hospitalizedAtDelivery=" +
                hospitalizedAtDelivery +
                "; handler=" +
                handler +
                "; transition=" +
                transition +
                "; roomWaitExhausted=" +
                roomWaitExhausted +
                "; sourceFloor=" +
                (sourceRoom == null ? -1 : sourceRoom.GetFloorIndex()) +
                "; labFloor=" +
                (labRoom == null ? -1 : labRoom.GetFloorIndex()) +
                ".");

            ClearRoomWaitTracking(procedure);
        }

        internal static void AppendCurrentPatients(
            Entity porter,
            List<Entity> patients)
        {
            if (porter == null || patients == null)
            {
                return;
            }

            PorterSampleTransportJob job;
            if (!Jobs.TryGetValue(porter, out job) ||
                job == null)
            {
                return;
            }

            AppendProcedurePatient(
                job.Procedure,
                patients);

            for (int i = 0;
                i < job.CollectedProcedures.Count;
                i++)
            {
                AppendProcedurePatient(
                    job.CollectedProcedures[i],
                    patients);
            }
        }

        private static void AppendProcedurePatient(
            LabProcedure procedure,
            List<Entity> patients)
        {
            if (procedure == null ||
                procedure.m_state == null)
            {
                return;
            }

            LabProcedureState state =
                procedure.m_state.m_labProcedureState;
            if (state != LabProcedureState.SamplingFinished &&
                state != LabProcedureState.SampleBeingDelivered)
            {
                return;
            }

            Entity patient =
                procedure.m_state.m_patient == null
                    ? null
                    : procedure.m_state.m_patient.GetEntity();

            if (patient != null &&
                patient.GetComponent<BehaviorPatient>() != null &&
                !patients.Contains(patient))
            {
                patients.Add(patient);
            }
        }

        internal static bool IsCartInActiveJob(TileObject cart)
        {
            if (cart == null)
            {
                return false;
            }

            foreach (KeyValuePair<Entity, PorterSampleTransportJob> pair in Jobs)
            {
                PorterSampleTransportJob job = pair.Value;
                if (job != null && job.Cart == cart)
                {
                    return true;
                }
            }

            return false;
        }

        internal static List<KeyValuePair<Entity, PorterSampleTransportJob>>
            GetActiveJobsSnapshot()
        {
            return new List<KeyValuePair<Entity, PorterSampleTransportJob>>(
                Jobs);
        }

        internal static List<KeyValuePair<Entity, LabProcedure>>
            GetNativeLabSampleBundlesSnapshot()
        {
            List<KeyValuePair<Entity, LabProcedure>> result =
                new List<KeyValuePair<Entity, LabProcedure>>();
            if (Hospital.Instance == null ||
                Hospital.Instance.m_characters == null)
            {
                return result;
            }

            foreach (Entity character in
                Hospital.Instance.m_characters)
            {
                BehaviorLabSpecialist lab =
                    character == null
                        ? null
                        : character.GetComponent<BehaviorLabSpecialist>();
                LabProcedure procedure;
                if (lab != null &&
                    NativeLabSampleBundles.TryGetValue(
                        lab,
                        out procedure) &&
                    procedure != null)
                {
                    result.Add(
                        new KeyValuePair<Entity, LabProcedure>(
                            character,
                            procedure));
                }
            }

            return result;
        }

        internal static bool RestoreNativeLabSampleBundle(
            Entity labEntity,
            LabProcedure procedure)
        {
            BehaviorLabSpecialist lab =
                labEntity == null
                    ? null
                    : labEntity.GetComponent<BehaviorLabSpecialist>();
            if (lab == null ||
                lab.m_state == null ||
                procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SampleBeingDelivered ||
                lab.m_state.m_currentPatient == null ||
                lab.m_state.m_currentPatient.GetEntity() !=
                    procedure.m_state.m_patient.GetEntity() ||
                !IsNativeLabTransportState(
                    lab.m_state.m_labSpecialistState))
            {
                return false;
            }

            NativeLabSampleBundles[lab] =
                procedure;
            return true;
        }

        private static bool IsNativeLabTransportState(
            LabSpecialistState state)
        {
            return
                state ==
                    LabSpecialistState
                        .GoingForSampleFromHospitalizedPatient ||
                state ==
                    LabSpecialistState
                        .GoingToStoreSampleFromPatient ||
                state ==
                    LabSpecialistState
                        .StoringSampleFromPatient ||
                state ==
                    LabSpecialistState
                        .SampleFromPatientStored;
        }

        internal static bool TryReserveCartParkingTarget(
            Entity porter,
            PorterSampleTransportJob job,
            Vector2i target,
            int floorIndex)
        {
            if (porter == null ||
                job == null ||
                target == Vector2i.ZERO_VECTOR)
            {
                return false;
            }

            foreach (KeyValuePair<Entity, PorterSampleTransportJob> pair in Jobs)
            {
                if (pair.Key == porter)
                {
                    continue;
                }

                PorterSampleTransportJob other =
                    pair.Value;
                if (other != null &&
                    other.HasCartParkingTarget &&
                    other.CartParkingFloorIndex == floorIndex &&
                    other.CartParkingTarget == target)
                {
                    return false;
                }
            }

            job.CartParkingTarget = target;
            job.CartParkingFloorIndex = floorIndex;
            job.HasCartParkingTarget = true;
            return true;
        }

        internal static bool IsCartParkingTargetClaimed(
            Entity porter,
            Vector2i target,
            int floorIndex)
        {
            foreach (KeyValuePair<Entity, PorterSampleTransportJob> pair in Jobs)
            {
                if (pair.Key == porter)
                {
                    continue;
                }

                PorterSampleTransportJob other =
                    pair.Value;
                if (other != null &&
                    other.HasCartParkingTarget &&
                    other.CartParkingFloorIndex == floorIndex &&
                    other.CartParkingTarget == target)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void ClearCartParkingTarget(
            PorterSampleTransportJob job)
        {
            if (job == null)
            {
                return;
            }

            job.CartParkingTarget =
                Vector2i.ZERO_VECTOR;
            job.CartParkingFloorIndex =
                -1;
            job.HasCartParkingTarget =
                false;
        }

        internal static bool RestorePersistedJob(
            Entity porter,
            PorterSampleTransportJob job)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                job == null ||
                job.StatLab == null ||
                porter.GetComponent<BehaviorNurse>() == null ||
                Jobs.ContainsKey(porter))
            {
                return false;
            }

            if (job.Cart != null)
            {
                if (job.CartHomeRoom == null ||
                    IsCartInActiveJob(job.Cart) ||
                    (job.Cart.User != null &&
                     job.Cart.User != porter))
                {
                    return false;
                }

                job.Cart.User = porter;
                job.Cart.m_state.m_originalFloorIndex =
                    job.CartHomeFloorIndex;
            }

            Jobs[porter] = job;
            NextOrphanedCartRecoveryTimes.Remove(porter);

            // WaitingForStandUpBeforeCart is intentionally transient. Rebuild
            // it from vanilla's persisted seat state so a save made during a
            // seated GoingToCart departure cannot restore the one-node route
            // loop that this guard prevents.
            WalkComponent restoredWalk =
                porter.GetComponent<WalkComponent>();
            if (job.Phase ==
                    PorterSampleTransportPhase.GoingToCart &&
                job.Cart != null &&
                restoredWalk != null &&
                restoredWalk.IsSitting())
            {
                job.WaitingForStandUpBeforeCart = true;

                PorterDiagnostics.Log(
                    "sample departure stand-up restored after load: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; current=" +
                    restoredWalk.GetCurrentTile() +
                    "; cartUseTile=" +
                    ToTile(job.Cart.GetDefaultUsePosition()) +
                    "; walkState=" +
                    (restoredWalk.m_state == null
                        ? "UNKNOWN"
                        : restoredWalk.m_state.m_walkState.ToString()) +
                    ".");
            }

            if (job.Procedure != null &&
                job.Phase !=
                    PorterSampleTransportPhase.ReturningCart &&
                job.Phase !=
                    PorterSampleTransportPhase.WaitingForCartReturnSlot)
            {
                ShowSampleBubble(
                    porter,
                    job.Procedure);
            }

            return true;
        }

        internal static void RestoreActiveCartDepartments()
        {
            foreach (KeyValuePair<Entity, PorterSampleTransportJob> pair in Jobs)
            {
                PorterSampleTransportJob job = pair.Value;
                if (job == null || job.Cart == null)
                {
                    continue;
                }

                PorterSampleCartRuntime.SyncCartDepartmentForState(
                    job.Cart,
                    job.CartHomeRoom);
            }
        }

        internal static bool UpdateActiveMission(BehaviorNurse nurse)
        {
            Entity porter = GetEntity(nurse);
            if (!PorterIdentity.IsPorter(porter))
            {
                return false;
            }

            PorterSampleTransportJob job;
            if (!Jobs.TryGetValue(porter, out job))
            {
                // Scanning every department object on every Nurse.Update is
                // unnecessary while this Porter has no sample mission.
                // Keep a periodic safety check for orphan carts caused by
                // interrupted work or external state transitions.
                float now = Time.realtimeSinceStartup;
                float nextScan;
                if (!NextOrphanedCartRecoveryTimes.TryGetValue(
                        porter, out nextScan) ||
                    now >= nextScan)
                {
                    NextOrphanedCartRecoveryTimes[porter] =
                        now + OrphanedCartRecoveryIntervalSeconds;
                    PorterSampleCartRuntime.RecoverOrphanedCartForPorter(
                        porter);
                }

                // Usage reservations still receive their original checks.
                RecoverOrphanedUseReservation(porter, nurse);
                return false;
            }

            UpdateJob(porter, nurse, job);
            return true;
        }

        internal static void TryAssignFromIdlePorter(BehaviorNurse nurse)
        {
            Entity porter = GetEntity(nurse);
            if (!IsAvailablePorter(porter, null))
            {
                return;
            }

            LabProcedure localFree;
            LabProcedure foreignFree;
            List<PorterBlockedSampleCandidate> blockedCandidates;
            ScanSampleCandidatesForPorter(
                porter, 2, true,
                out localFree, out foreignFree, out blockedCandidates);

            // Same priority as 1.0.2.41, but only ONE scan: local free,
            // foreign free, local occupied, foreign occupied.
            if (localFree != null &&
                StartJob(porter, nurse, localFree, false))
            {
                return;
            }
            if (foreignFree != null &&
                StartJob(porter, nurse, foreignFree, true))
            {
                return;
            }

            LabProcedure bestBlocked =
                SelectBestBlockedCandidate(porter, blockedCandidates, false);
            if (bestBlocked != null &&
                StartJob(porter, nurse, bestBlocked, false, true))
            {
                return;
            }

            bestBlocked =
                SelectBestBlockedCandidate(porter, blockedCandidates, true);
            if (bestBlocked != null)
            {
                StartJob(porter, nurse, bestBlocked, true, true);
            }
        }

        internal static bool ShouldDeferLabFallback(LabProcedure procedure)
        {
            if (IsProcedureClaimed(procedure))
            {
                ClearFallbackTracking(procedure);
                return true;
            }
            if (!IsHospitalizedSampleWaiting(procedure))
            {
                // SelectNextLabProcedureStep also revisits older idle lab
                // procedures (DeliveredToStatLab/ReadyForTesting/
                // ProcessingResults). They may still share the same
                // patient/source-room bundle key with a newer waiting sample,
                // so they must not clear another procedure's fallback wait.
                ClearFallbackTrackingIfOwnedByProcedure(procedure);
                return false;
            }
            if (IsRoomWaitReleasedToLab(procedure))
            {
                ClearFallbackTracking(procedure);
                return false;
            }
            if (IsRoomWaitRetryCoolingDown(procedure))
            {
                RemoveFallbackWaitForBundle(procedure);
                return true;
            }
            if (IsFallbackReleasedForBundle(procedure)) return false;

            PorterSampleFallbackWait wait;
            bool hasFallbackWait = TryGetFallbackWaitForBundle(procedure, out wait);
            int fallbackNowMinute =
                GetSampleFallbackClockMinute();
            if (hasFallbackWait &&
                fallbackNowMinute < wait.NextRetryGameMinute)
            {
                wait.SuppressedFallbackChecks++;
                return true;
            }

            bool isRetryAttempt =
                hasFallbackWait && wait != null;
            int scheduledRetryMinute =
                isRetryAttempt
                    ? wait.NextRetryGameMinute
                    : 0;
            bool assigned =
                TryAssignAvailablePorter(procedure);

            if (isRetryAttempt)
            {
                PorterDiagnostics.Log(
                    "sample fallback retry attempt: patient=" +
                    GetPatientName(procedure) +
                    "; examination=" +
                    procedure.m_state.m_examination.Entry.DatabaseID +
                    "; retry=" +
                    (wait.RetryCount + 1) +
                    "/" +
                    PorterTransportConfig.SampleFallbackRetries +
                    "; scheduledMinute=" +
                    scheduledRetryMinute +
                    "; nowMinute=" +
                    fallbackNowMinute +
                    "; suppressedFallbackChecks=" +
                    wait.SuppressedFallbackChecks +
                    "; suppressedIdleScans=" +
                    wait.SuppressedIdleScans +
                    "; result=" +
                    (assigned
                        ? "porter-assigned"
                        : "no-porter") +
                    ".");
            }

            if (assigned)
            {
                ClearFallbackTracking(procedure);
                return true;
            }
            if (!HasConfiguredReachableSamplePorter(procedure))
            {
                ClearFallbackTracking(procedure);
                return false;
            }

            if (IsSamplePickupRoomBlocked(procedure) &&
                !HasRoomWaitRetryForBundle(procedure))
            {
                bool waitForPorter = ShouldWaitForPreBlockedRoom(procedure);
                if (waitForPorter)
                {
                    RemoveFallbackWaitForBundle(procedure);
                    int timeoutMinutes =
                        GetStablePreAssignmentBlockedRoomTimeoutMinutes(procedure);
                    float timeoutSeconds = timeoutMinutes <= 0 || DayTime.Instance == null
                        ? 0f
                        : DayTime.Instance.IngameTimeHoursToRealTimeSeconds(timeoutMinutes / 60f);
                    if (timeoutMinutes <= 0 ||
                        (DayTime.Instance != null &&
                         procedure.m_state.m_timeInState >= timeoutSeconds))
                    {
                        ClearRoomWaitRetryForBundle(procedure);
                        RoomWaitReleasedToLab.Add(procedure);
                        LoggedPreAssignmentRoomDeferrals.Remove(procedure);
                        PorterDiagnostics.Log(
                            "sample pre-assignment occupied-room wait released to native lab: patient=" +
                            GetPatientName(procedure) + "; procedureId=" + procedure.GetEntityID() +
                            "; sourceRoomType=" + GetRoomTypeDiagnosticId(GetSampleSourceRoom(procedure)) +
                            "; timeoutMinutes=" + timeoutMinutes + "; porterClaimed=False.");
                        return false;
                    }
                    if (PorterTransportConfig.EnableDiagnostics &&
                        LoggedPreAssignmentRoomDeferrals.Add(procedure))
                    {
                        PorterDiagnostics.Log(
                            "sample native lab deferred by bounded pre-assignment room wait: patient=" +
                            GetPatientName(procedure) + "; procedureId=" + procedure.GetEntityID() +
                            "; sourceRoomType=" + GetRoomTypeDiagnosticId(GetSampleSourceRoom(procedure)) +
                            "; porterRoomWaitStarted=False; timeoutMinutes=" + timeoutMinutes +
                            "; elapsedRealSeconds=" + procedure.m_state.m_timeInState.ToString("0.0") +
                            "; note=stable occupied-room wait decision; Lab Specialist releases if no Porter claims before deadline.");
                    }
                    return true;
                }
                LoggedPreAssignmentRoomDeferrals.Remove(procedure);
            }
            else
            {
                LoggedPreAssignmentRoomDeferrals.Remove(procedure);
            }

            int maxFallbackRetries = PorterTransportConfig.SampleFallbackRetries;
            if (maxFallbackRetries <= 0)
            {
                ClearFallbackTracking(procedure);
                return false;
            }

            PorterSampleBundleKey fallbackKey;
            if (!TryGetSampleBundleKey(
                    procedure,
                    out fallbackKey))
            {
                ClearFallbackTracking(procedure);
                return false;
            }

            int nowMinute = fallbackNowMinute;
            if (!hasFallbackWait || wait == null)
            {
                int firstDelay = GetStableSampleFallbackRetryMinutes(procedure, 1);
                wait = new PorterSampleFallbackWait
                {
                    Procedure = procedure,
                    RetryCount = 0,
                    NextRetryGameMinute = nowMinute + firstDelay
                };
                FallbackWaits[fallbackKey] = wait;
                PorterDiagnostics.Log(
                    "sample fallback initial attempt failed: patient=" +
                    GetPatientName(procedure) +
                    "; examination=" +
                    procedure.m_state.m_examination.Entry.DatabaseID +
                    "; nowMinute=" +
                    nowMinute +
                    "; retry=1/" +
                    maxFallbackRetries +
                    "; delayMinutes=" +
                    firstDelay +
                    "; deadlineMinute=" +
                    wait.NextRetryGameMinute +
                    "; note=no Porter assignment before deadline.");
                return true;
            }

            wait.RetryCount++;
            if (wait.RetryCount >= maxFallbackRetries)
            {
                RemoveFallbackWaitForBundle(procedure);
                ReleasedFallbacks[fallbackKey] =
                    procedure;
                PorterDiagnostics.Log(
                    "sample fallback: patient=" + GetPatientName(procedure) +
                    "; examination=" + procedure.m_state.m_examination.Entry.DatabaseID +
                    "; retries=" + maxFallbackRetries +
                    "; native Lab Specialist released.");
                return false;
            }

            int nextRetryOrdinal = wait.RetryCount + 1;
            int retryDelayMinutes =
                GetStableSampleFallbackRetryMinutes(procedure, nextRetryOrdinal);
            wait.NextRetryGameMinute = nowMinute + retryDelayMinutes;
            wait.SuppressedFallbackChecks = 0;
            wait.SuppressedIdleScans = 0;
            PorterDiagnostics.Log(
                "sample fallback next retry scheduled: patient=" +
                GetPatientName(procedure) +
                "; examination=" +
                procedure.m_state.m_examination.Entry.DatabaseID +
                "; retry=" +
                nextRetryOrdinal +
                "/" +
                maxFallbackRetries +
                "; nowMinute=" +
                nowMinute +
                "; delayMinutes=" +
                retryDelayMinutes +
                "; deadlineMinute=" +
                wait.NextRetryGameMinute +
                "; note=no Porter assignment before deadline.");
            return true;
        }

        internal static void CleanupFallbackTracking(LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null ||
                procedure.m_state.m_labProcedureState != LabProcedureState.SamplingFinished)
            {
                if (procedure != null)
                {
                    BlockedSamplePickupRooms.Remove(
                        procedure);
                    LoggedPreAssignmentRoomDeferrals.Remove(
                        procedure);
                    if (procedure.m_state == null ||
                        procedure.m_state.m_labProcedureState !=
                            LabProcedureState.SampleBeingDelivered)
                    {
                        ClearPreBlockedRoomWaitDecision(procedure);
                    }
                    if (procedure.m_state == null ||
                        (procedure.m_state.m_labProcedureState !=
                            LabProcedureState.SampleBeingDelivered &&
                         procedure.m_state.m_labProcedureState !=
                            LabProcedureState.SamplingFinished))
                    {
                        ClearRoomWaitTracking(procedure);
                    }
                }
                // Cleanup runs for every LabProcedure update. A historical
                // procedure from the same patient/source room can share the
                // physical-bundle key with a newer waiting sample. Only the
                // procedure that OWNS the current fallback entry may clear it.
                // Explicit claim/release paths still use ClearFallbackTracking
                // to clear the whole active physical bundle intentionally.
                ClearFallbackTrackingIfOwnedByProcedure(
                    procedure);
            }
        }

        internal static bool TryAssignAvailablePorter(LabProcedure procedure)
        {
            if (IsRoomWaitReleasedToLab(procedure) ||
                IsFallbackReleasedForBundle(procedure) ||
                IsOrdinaryFallbackCoolingDown(procedure) ||
                !IsHospitalizedSampleWaiting(procedure))
            {
                return false;
            }

            Department department =
                GetProcedureDepartment(procedure);
            if (department == null ||
                department.m_departmentPersistentData.m_nurses == null)
            {
                return false;
            }

            Entity best = null;
            BehaviorNurse bestNurse = null;
            float bestDistance = float.MaxValue;

            foreach (EntityIDPointer<Entity> pointer in
                department.m_departmentPersistentData.m_nurses)
            {
                Entity candidate = pointer.GetEntity();
                float distance;
                if (!CanTakeProcedure(
                        candidate,
                        procedure,
                        allowForeignDepartment: false,
                        out distance))
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                    bestNurse = candidate.GetComponent<BehaviorNurse>();
                }
            }

            if (best != null && bestNurse != null)
            {
                return StartJob(
                    best,
                    bestNurse,
                    procedure,
                    allowForeignDepartment: false);
            }

            if (!TryFindForeignSampleHelper(
                    procedure,
                    department,
                    out best,
                    out bestNurse))
            {
                return false;
            }

            return StartJob(
                best,
                bestNurse,
                procedure,
                allowForeignDepartment: true);
        }

        internal static bool HasPendingLocalSampleWork(Entity porter)
        {
            LabProcedure procedure;
            return TryFindBestProcedureForPorter(
                porter,
                foreignDepartmentOnly: false,
                out procedure);
        }

        internal static void RecoverOrphanedDelivery(LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null ||
                procedure.m_state.m_labProcedureState != LabProcedureState.SampleBeingDelivered)
            {
                return;
            }

            Entity patient = procedure.m_state.m_patient.GetEntity();
            if (patient == null ||
                patient.GetComponent<HospitalizationComponent>() == null ||
                !patient.GetComponent<HospitalizationComponent>().IsHospitalized() ||
                IsProcedureClaimed(procedure) ||
                IsHandledByLabSpecialist(procedure))
            {
                return;
            }

            TileObject source = procedure.m_state.m_usedEquipment == null
                ? null
                : procedure.m_state.m_usedEquipment.GetEntity();
            if (source != null && source.User != null &&
                PorterIdentity.IsPorter(source.User))
            {
                Entity stalePorter = source.User;
                UseComponent use = stalePorter.GetComponent<UseComponent>();
                if (use != null &&
                    (use.m_state.m_reservedObject != null || use.IsUsing()))
                {
                    use.Interrupt();
                }
                if (source.User == stalePorter)
                {
                    source.User = null;
                }
            }

            procedure.SwitchState(LabProcedureState.SamplingFinished);
            ClearFallbackTracking(procedure);
            PorterDiagnostics.Log(
                "sample recovery: patient=" +
                patient.Name +
                "; examination=" +
                procedure.m_state.m_examination.Entry.DatabaseID +
                "; action=requeued orphaned delivery.");
        }

        private static Entity GetEntity(BehaviorNurse nurse)
        {
            return nurse == null || object.ReferenceEquals(NurseEntityField, null)
                ? null
                : NurseEntityField.GetValue(nurse) as Entity;
        }

        private static bool HasConfiguredReachableSamplePorter(
            LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null ||
                Database.Instance == null || GridMap.GetInstance() == null)
            {
                return false;
            }

            Department department =
                GetProcedureDepartment(procedure);
            TileObject source = procedure.m_state.m_usedEquipment == null
                ? null
                : procedure.m_state.m_usedEquipment.GetEntity();
            if (department == null || source == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_departments == null)
            {
                return false;
            }

            if (procedure.m_state.m_statLab == null ||
                procedure.m_state.m_statLab.GetEntity() == null)
            {
                procedure.FindRoom(onlyFree: false);
            }

            Room statLab = procedure.m_state.m_statLab == null
                ? null
                : procedure.m_state.m_statLab.GetEntity();
            if (statLab == null ||
                statLab.m_roomPersistentData.m_roomType == null ||
                !statLab.m_roomPersistentData.m_roomType.Entry.HasTag("stat_lab"))
            {
                return false;
            }

            foreach (Department porterDepartment in
                Hospital.Instance.m_departments)
            {
                if (porterDepartment == null ||
                    porterDepartment.m_departmentPersistentData == null ||
                    porterDepartment.m_departmentPersistentData.m_nurses == null)
                {
                    continue;
                }

                foreach (EntityIDPointer<Entity> pointer in
                    porterDepartment.m_departmentPersistentData.m_nurses)
                {
                    Entity candidate = pointer.GetEntity();
                    if (!IsConfiguredSamplePorter(
                            candidate,
                            porterDepartment))
                    {
                        continue;
                    }

                    bool foreign =
                        porterDepartment != department;
                    if (foreign &&
                        !PorterTransportConfig.SampleCrossDepartmentHelp)
                    {
                        continue;
                    }

                    WalkComponent walk =
                        candidate.GetComponent<WalkComponent>();
                    if (walk == null)
                    {
                        continue;
                    }

                    float routeDistance =
                        GridMap.GetInstance().GetDistance(
                            walk.GetFloorIndex(),
                            walk.GetCurrentTile(),
                            source.GetFloorIndex(),
                            source.m_state.m_position,
                            AccessRights.STAFF);
                    if (routeDistance >= 0f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsConfiguredSamplePorter(
            Entity porter,
            Department department)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                Database.Instance == null || department == null)
            {
                return false;
            }

            EmployeeComponent employee = porter.GetComponent<EmployeeComponent>();
            GameDBEmployeeRole role =
                Database.Instance.GetEntry<GameDBEmployeeRole>(
                    PorterIds.SampleTransportRole);
            Room homeRoom = employee == null || employee.m_state.m_homeRoom == null
                ? null
                : employee.m_state.m_homeRoom.GetEntity();

            return employee != null &&
                role != null &&
                !employee.IsFired() &&
                employee.m_state.m_shift == DayTime.Instance.GetShift() &&
                employee.m_state.m_department.GetEntity() == department &&
                employee.HasRole(role) &&
                homeRoom != null &&
                homeRoom.GetEquipmentOk() &&
                homeRoom.m_roomPersistentData.m_roomType != null &&
                PorterStationRegistry.IsPorterStation(
                    homeRoom.m_roomPersistentData.m_roomType.Entry);
        }

        private static Room GetPorterHomeStation(
            Entity porter)
        {
            EmployeeComponent employee =
                porter == null
                    ? null
                    : porter.GetComponent<EmployeeComponent>();
            Room station =
                employee == null ||
                employee.m_state.m_homeRoom == null
                    ? null
                    : employee.m_state.m_homeRoom.GetEntity();
            return station != null &&
                   station.m_roomPersistentData.m_roomType != null &&
                   PorterStationRegistry.IsPorterStation(
                       station.m_roomPersistentData.m_roomType.Entry)
                ? station
                : null;
        }

        private static Department GetPorterHomeDepartment(
            Entity porter)
        {
            EmployeeComponent employee =
                porter == null
                    ? null
                    : porter.GetComponent<EmployeeComponent>();
            return employee == null ||
                   employee.m_state.m_department == null
                ? null
                : employee.m_state.m_department.GetEntity();
        }

        private static void NotifySampleCartCapacity(
            Entity porter)
        {
            Room station = GetPorterHomeStation(porter);
            Department department =
                GetPorterHomeDepartment(porter);
            if (station == null ||
                department == null ||
                MissingCartNotifications.Contains(station))
            {
                return;
            }

            MissingCartNotifications.Add(station);

            GameDBObject cartEntry =
                Database.Instance.GetEntry<GameDBObject>(
                    PorterIds.SampleCart);
            string iconAssetId =
                cartEntry == null ||
                cartEntry.CustomIconAssetRef == null
                    ? null
                    : cartEntry.CustomIconAssetRef.XmlID;
            int icon =
                string.IsNullOrEmpty(iconAssetId) &&
                cartEntry != null
                    ? cartEntry.IconIndex
                    : 0;
            string departmentName =
                StringTable.GetInstance().GetLocalizedText(
                    department.m_departmentPersistentData.m_departmentType.Entry);

            NotificationManager.GetInstance().AddMessage(
                null,
                PorterIds.SampleCartNotification,
                departmentName,
                string.Empty,
                string.Empty,
                icon,
                0,
                0,
                0,
                iconAssetId);

            PorterDiagnostics.Log(
                "sample cart capacity notification: department=" +
                department.m_departmentPersistentData.m_departmentType.Entry.DatabaseID +
                "; stationId=" + station.GetEntityID() +
                "; stationFloor=" + station.GetFloorIndex() +
                ".");
        }

        private static bool WouldAdditionalSampleCartHelp(
            Entity porter,
            Department workloadDepartment)
        {
            Room station = GetPorterHomeStation(porter);
            Department homeDepartment =
                GetPorterHomeDepartment(porter);
            if (station == null ||
                homeDepartment == null ||
                homeDepartment.m_departmentPersistentData.m_nurses == null ||
                workloadDepartment == null ||
                LabProcedureManager.Instance == null ||
                LabProcedureManager.Instance.m_labProcedures == null)
            {
                return false;
            }

            // One physical pickup may contain several LabProcedures.
            // Counting exams as separate samples inflates cart demand.
            List<LabProcedure> waitingBundles =
                new List<LabProcedure>();
            List<LabProcedure> procedures =
                LabProcedureManager.Instance.m_labProcedures;
            for (int i = 0; i < procedures.Count; i++)
            {
                LabProcedure procedure = procedures[i];
                if (!IsHospitalizedSampleWaiting(procedure) ||
                    GetProcedureDepartment(procedure) != workloadDepartment ||
                    IsProcedureClaimed(procedure) ||
                    IsRoomWaitReleasedToLab(procedure) ||
                    IsRoomWaitRetryCoolingDown(procedure))
                {
                    continue;
                }

                bool alreadyCounted = false;
                for (int j = 0; j < waitingBundles.Count; j++)
                {
                    if (AreSameSampleBundle(
                            waitingBundles[j],
                            procedure))
                    {
                        alreadyCounted = true;
                        break;
                    }
                }

                if (!alreadyCounted)
                {
                    waitingBundles.Add(procedure);
                }
            }

            int waitingSamples = waitingBundles.Count;
            if (waitingSamples <= 1)
            {
                return false;
            }

            int availablePorters = 0;
            int staffedPorters = 0;
            HashSet<TileObject> freeCarts =
                new HashSet<TileObject>();

            foreach (EntityIDPointer<Entity> pointer in
                homeDepartment.m_departmentPersistentData.m_nurses)
            {
                Entity candidate = pointer.GetEntity();
                if (GetPorterHomeStation(candidate) != station)
                {
                    continue;
                }

                if (IsConfiguredSamplePorter(
                        candidate,
                        homeDepartment))
                {
                    staffedPorters++;
                }

                if (!IsAvailablePorter(candidate, null))
                {
                    continue;
                }

                availablePorters++;
                PorterSampleCartRuntime.CollectFreeCarts(
                    candidate,
                    freeCarts);
            }

            // Supply is station-scoped, matching the actual cart selector.
            // An active cart that originated from this station still counts
            // as installed even while it is physically away.
            int installedCarts =
                CountSampleCartsAssignedToStation(station);
            if (availablePorters <= 0 ||
                freeCarts.Count >= availablePorters ||
                (staffedPorters > 0 &&
                 installedCarts >= staffedPorters))
            {
                return false;
            }

            int maxSamples =
                PorterTransportConfig.SampleTourMaxSamples;
            if (maxSamples <= 1)
            {
                return false;
            }

            int currentUnserved;
            int currentPorters;
            EvaluateSampleTransportCapacity(
                waitingSamples,
                availablePorters,
                freeCarts.Count,
                maxSamples,
                out currentUnserved,
                out currentPorters);

            int improvedUnserved;
            int improvedPorters;
            EvaluateSampleTransportCapacity(
                waitingSamples,
                availablePorters,
                freeCarts.Count + 1,
                maxSamples,
                out improvedUnserved,
                out improvedPorters);

            return improvedUnserved < currentUnserved ||
                (improvedUnserved == currentUnserved &&
                 improvedPorters < currentPorters);
        }

        private static int CountSampleCartsAssignedToStation(
            Room station)
        {
            if (station == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return 0;
            }

            HashSet<TileObject> carts =
                new HashSet<TileObject>();
            int floorIndex = station.GetFloorIndex();
            if (floorIndex >= 0 &&
                floorIndex < Hospital.Instance.m_floors.Count)
            {
                Floor floor =
                    Hospital.Instance.m_floors[floorIndex];
                for (int x =
                        station.m_roomPersistentData.m_positionBottom.m_x;
                    x <=
                        station.m_roomPersistentData.m_positionTop.m_x;
                    x++)
                {
                    for (int y =
                            station.m_roomPersistentData.m_positionBottom.m_y;
                        y <=
                            station.m_roomPersistentData.m_positionTop.m_y;
                        y++)
                    {
                        Vector2i position = new Vector2i(x, y);
                        if (!station.IsPositionInRoom(position))
                        {
                            continue;
                        }

                        List<TileObject> objects =
                            floor.m_tileObjects[x, y].GetAllObjects();
                        for (int i = 0; i < objects.Count; i++)
                        {
                            TileObject cart = objects[i];
                            if (PorterSampleCartRuntime.IsSampleCart(cart))
                            {
                                carts.Add(cart);
                            }
                        }
                    }
                }
            }

            foreach (KeyValuePair<Entity, PorterSampleTransportJob>
                pair in Jobs)
            {
                PorterSampleTransportJob job = pair.Value;
                if (job != null &&
                    job.Cart != null &&
                    job.CartHomeRoom == station)
                {
                    carts.Add(job.Cart);
                }
            }

            return carts.Count;
        }

        private static void EvaluateSampleTransportCapacity(
            int waitingSamples,
            int availablePorters,
            int availableCarts,
            int maxSamplesPerCart,
            out int unservedSamples,
            out int portersNeeded)
        {
            int cartPorters =
                Math.Min(availablePorters, availableCarts);
            int capacity =
                cartPorters * maxSamplesPerCart +
                (availablePorters - cartPorters);

            unservedSamples =
                Math.Max(0, waitingSamples - capacity);
            if (unservedSamples > 0)
            {
                portersNeeded = availablePorters;
                return;
            }

            if (waitingSamples <=
                cartPorters * maxSamplesPerCart)
            {
                portersNeeded =
                    (waitingSamples + maxSamplesPerCart - 1) /
                    maxSamplesPerCart;
                return;
            }

            portersNeeded =
                cartPorters +
                waitingSamples -
                cartPorters * maxSamplesPerCart;
        }

        internal static void ResetMissingCartNotification(
            Department department)
        {
            if (department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_rooms == null)
            {
                return;
            }

            foreach (EntityIDPointer<Room> pointer in
                department.m_departmentPersistentData.m_rooms)
            {
                Room room = pointer.GetEntity();
                if (room != null)
                {
                    MissingCartNotifications.Remove(room);
                }
            }
        }

        private static string GetPatientName(LabProcedure procedure)
        {
            Entity patient = procedure == null || procedure.m_state == null
                ? null
                : procedure.m_state.m_patient.GetEntity();
            return patient == null ? "UNKNOWN" : patient.Name;
        }

        private static bool TryGetSampleBundleKey(
            LabProcedure procedure,
            out PorterSampleBundleKey key)
        {
            Room sourceRoom = GetSampleSourceRoom(procedure);
            return TryGetSampleBundleKey(
                procedure,
                sourceRoom,
                out key);
        }

        private static bool TryGetSampleBundleKey(
            LabProcedure procedure,
            Room sourceRoom,
            out PorterSampleBundleKey key)
        {
            key = new PorterSampleBundleKey();
            if (procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_patient == null)
            {
                return false;
            }

            Entity patient =
                procedure.m_state.m_patient.GetEntity();
            if (patient == null)
            {
                return false;
            }

            // Preserve AreSameSampleBundle semantics exactly:
            // same patient + same non-null source room form one physical
            // bundle. Without a room, only the procedure itself matches.
            uint sourceId =
                sourceRoom != null
                    ? sourceRoom.GetEntityID()
                    : procedure.GetEntityID();
            key = new PorterSampleBundleKey(
                patient.GetEntityID(),
                sourceId);
            return sourceId != 0u;
        }

        private static bool TryGetFallbackWaitForBundle(
            LabProcedure procedure,
            out PorterSampleFallbackWait wait)
        {
            PorterSampleBundleKey key;
            wait = null;
            return TryGetSampleBundleKey(procedure, out key) &&
                FallbackWaits.TryGetValue(key, out wait) &&
                wait != null;
        }

        private static void RemoveFallbackWaitForBundle(
            LabProcedure procedure)
        {
            PorterSampleBundleKey key;
            if (TryGetSampleBundleKey(procedure, out key))
            {
                FallbackWaits.Remove(key);
            }
        }

        private static bool IsFallbackReleasedForBundle(
            LabProcedure procedure)
        {
            PorterSampleBundleKey key;
            return TryGetSampleBundleKey(procedure, out key) &&
                ReleasedFallbacks.ContainsKey(key);
        }

        private static bool IsOrdinaryFallbackCoolingDown(
            LabProcedure procedure)
        {
            PorterSampleFallbackWait wait;
            return TryGetFallbackWaitForBundle(procedure, out wait) &&
                GetSampleFallbackClockMinute() <
                    wait.NextRetryGameMinute;
        }

        private static int GetSampleFallbackClockMinute()
        {
            if (DayTime.Instance == null) return 0;
            return (int)(DayTime.Instance.GetDay() * 1440f +
                DayTime.Instance.GetDayTimeHours() * 60f);
        }

        private static int GetStableSampleFallbackRetryMinutes(
            LabProcedure procedure, int retryOrdinal)
        {
            int baseMinutes = PorterTransportConfig.SampleFallbackRetryMinutes;
            int randomness = PorterTransportConfig.SampleFallbackRetryRandomnessMinutes;
            if (randomness <= 0) return baseMinutes;
            int minimum = Math.Max(1, baseMinutes - randomness);
            int maximum = Math.Min(60, baseMinutes + randomness);
            if (maximum <= minimum) return minimum;
            uint hash = GetPhysicalSampleBundleStableHash(procedure);
            hash = StableHashStep(hash, SampleFallbackRetrySalt);
            hash = StableHashStep(hash, (uint)Math.Max(1, retryOrdinal));
            return minimum + (int)(hash % (uint)(maximum - minimum + 1));
        }

        internal static List<PorterSampleFallbackSnapshot>
            GetFallbackStateSnapshot()
        {
            List<PorterSampleFallbackSnapshot> snapshot =
                new List<PorterSampleFallbackSnapshot>();
            foreach (KeyValuePair<PorterSampleBundleKey, PorterSampleFallbackWait> pair in
                FallbackWaits)
            {
                PorterSampleFallbackWait wait = pair.Value;
                LabProcedure procedure =
                    wait == null ? null : wait.Procedure;
                if (procedure == null ||
                    procedure.m_state == null ||
                    procedure.m_state.m_labProcedureState !=
                        LabProcedureState.SamplingFinished)
                {
                    continue;
                }

                PorterSampleFallbackSnapshot entry =
                    new PorterSampleFallbackSnapshot();
                entry.Procedure = procedure;
                entry.RetryCount = wait.RetryCount;
                entry.NextRetryGameMinute =
                    wait.NextRetryGameMinute;
                entry.Released = false;
                snapshot.Add(entry);
            }

            foreach (KeyValuePair<PorterSampleBundleKey, LabProcedure> pair in
                ReleasedFallbacks)
            {
                LabProcedure procedure = pair.Value;
                if (procedure == null ||
                    procedure.m_state == null ||
                    procedure.m_state.m_labProcedureState !=
                        LabProcedureState.SamplingFinished)
                {
                    continue;
                }

                PorterSampleFallbackSnapshot entry =
                    new PorterSampleFallbackSnapshot();
                entry.Procedure = procedure;
                entry.RetryCount =
                    PorterTransportConfig.SampleFallbackRetries;
                entry.NextRetryGameMinute = 0;
                entry.Released = true;
                snapshot.Add(entry);
            }

            return snapshot;
        }

        internal static bool RestoreFallbackState(
            LabProcedure procedure,
            int retryCount,
            int nextRetryGameMinute,
            bool released)
        {
            if (procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SamplingFinished)
            {
                return false;
            }

            PorterSampleBundleKey key;
            if (!TryGetSampleBundleKey(procedure, out key))
            {
                return false;
            }

            if (released)
            {
                ReleasedFallbacks[key] = procedure;
                return true;
            }

            if (retryCount < 0 ||
                nextRetryGameMinute <= 0)
            {
                return false;
            }

            FallbackWaits[key] =
                new PorterSampleFallbackWait
                {
                    Procedure = procedure,
                    RetryCount = retryCount,
                    NextRetryGameMinute =
                        nextRetryGameMinute
                };
            return true;
        }

        internal static List<KeyValuePair<Department, int>>
            GetSamplePorterCapacityFallbackHistorySnapshot()
        {
            TrimSamplePorterCapacityFallbackHistory();
            List<KeyValuePair<Department, int>> snapshot =
                new List<KeyValuePair<Department, int>>();
            foreach (KeyValuePair<Department, List<int>> pair in
                SamplePorterCapacityFallbackEvents)
            {
                if (pair.Key == null || pair.Value == null)
                {
                    continue;
                }

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    snapshot.Add(
                        new KeyValuePair<Department, int>(
                            pair.Key,
                            pair.Value[i]));
                }
            }

            return snapshot;
        }

        internal static List<Department>
            GetSamplePorterCapacityNotificationsSnapshot()
        {
            return new List<Department>(
                SamplePorterCapacityNotifications);
        }

        internal static bool RestoreSamplePorterCapacityFallbackEvent(
            Department department,
            int gameMinute)
        {
            if (department == null || gameMinute < 0)
            {
                return false;
            }

            int now = GetSampleFallbackClockMinute();
            if (now - gameMinute >
                SamplePorterCapacityWindowMinutes)
            {
                return false;
            }

            List<int> events;
            if (!SamplePorterCapacityFallbackEvents.TryGetValue(
                    department, out events))
            {
                events = new List<int>();
                SamplePorterCapacityFallbackEvents[department] =
                    events;
            }

            events.Add(gameMinute);
            return true;
        }

        internal static bool RestoreSamplePorterCapacityNotification(
            Department department)
        {
            return department != null &&
                SamplePorterCapacityNotifications.Add(
                    department);
        }

        internal static void ResetSamplePorterCapacityNotification(
            Department department)
        {
            if (department != null)
            {
                SamplePorterCapacityNotifications.Remove(
                    department);
            }
        }

        private static void TrimSamplePorterCapacityFallbackHistory()
        {
            int now = GetSampleFallbackClockMinute();
            List<Department> empty =
                new List<Department>();

            foreach (KeyValuePair<Department, List<int>> pair in
                SamplePorterCapacityFallbackEvents)
            {
                List<int> events = pair.Value;
                if (events == null)
                {
                    empty.Add(pair.Key);
                    continue;
                }

                for (int i = events.Count - 1; i >= 0; i--)
                {
                    if (now - events[i] >
                        SamplePorterCapacityWindowMinutes)
                    {
                        events.RemoveAt(i);
                    }
                }

                if (events.Count == 0)
                {
                    empty.Add(pair.Key);
                }
            }

            for (int i = 0; i < empty.Count; i++)
            {
                SamplePorterCapacityFallbackEvents.Remove(
                    empty[i]);
            }
        }

        private static void RecordSamplePorterCapacityFallback(
            LabProcedure procedure)
        {
            Department department =
                GetProcedureDepartment(procedure);
            if (department == null)
            {
                return;
            }

            TrimSamplePorterCapacityFallbackHistory();

            List<int> events;
            if (!SamplePorterCapacityFallbackEvents.TryGetValue(
                    department, out events))
            {
                events = new List<int>();
                SamplePorterCapacityFallbackEvents[department] =
                    events;
            }

            events.Add(GetSampleFallbackClockMinute());
            if (events.Count <
                    SamplePorterCapacityFallbackThreshold ||
                SamplePorterCapacityNotifications.Contains(
                    department))
            {
                return;
            }

            NotifySamplePorterCapacity(
                department,
                events.Count);
            SamplePorterCapacityNotifications.Add(
                department);
        }

        private static void NotifySamplePorterCapacity(
            Department department,
            int fallbackCount)
        {
            if (department == null || department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_departmentType == null ||
                department.m_departmentPersistentData.m_departmentType.Entry == null ||
                NotificationManager.GetInstance() == null) return;
            StringTable strings = StringTable.GetInstance();
            string departmentName = strings == null
                ? department.m_departmentPersistentData.m_departmentType.Entry.DatabaseID.ToString()
                : strings.GetLocalizedText(department.m_departmentPersistentData.m_departmentType.Entry);
            NotificationManager.GetInstance().AddMessage(
                null, PorterIds.SamplePorterCapacityNotification,
                departmentName, string.Empty, string.Empty, 0, 0, 0, 0,
                PorterVisuals.CategoryAsset);
            PorterDiagnostics.Log(
                "sample Porter capacity notification: department=" +
                department.m_departmentPersistentData.m_departmentType.Entry.DatabaseID +
                "; fallbacks=" + fallbackCount +
                "; windowMinutes=" + SamplePorterCapacityWindowMinutes + ".");
        }

        private static void ClearFallbackTracking(
            LabProcedure procedure)
        {
            PorterSampleBundleKey key;
            if (!TryGetSampleBundleKey(procedure, out key))
            {
                return;
            }

            FallbackWaits.Remove(key);
            ReleasedFallbacks.Remove(key);
        }

        private static void ClearFallbackTrackingIfOwnedByProcedure(
            LabProcedure procedure)
        {
            PorterSampleBundleKey key;
            if (!TryGetSampleBundleKey(procedure, out key))
            {
                return;
            }

            PorterSampleFallbackWait wait;
            if (FallbackWaits.TryGetValue(key, out wait) &&
                wait != null &&
                wait.Procedure == procedure)
            {
                FallbackWaits.Remove(key);
            }

            LabProcedure released;
            if (ReleasedFallbacks.TryGetValue(key, out released) &&
                released == procedure)
            {
                ReleasedFallbacks.Remove(key);
            }
        }

        // Only occupied candidates need a local deferred list. Everything else
        // is scored in a SINGLE pass over the vanilla LabProcedure list.
        private struct PorterBlockedSampleCandidate
        {
            internal LabProcedure Procedure;
            internal bool Foreign;
        }

        private static void ScanSampleCandidatesForPorter(
            Entity porter,
            int departmentScope,
            bool includeBlocked,
            out LabProcedure localFree,
            out LabProcedure foreignFree,
            out List<PorterBlockedSampleCandidate> blockedCandidates)
        {
            ScanSampleCandidatesForPorter(
                porter, departmentScope, includeBlocked, false,
                out localFree, out foreignFree, out blockedCandidates);
        }

        private static void ScanSampleCandidatesForPorter(
            Entity porter,
            int departmentScope,
            bool includeBlocked,
            bool stopOnFirstFree,
            out LabProcedure localFree,
            out LabProcedure foreignFree,
            out List<PorterBlockedSampleCandidate> blockedCandidates)
        {
            localFree = null;
            foreignFree = null;
            blockedCandidates = null;
            if (!IsAvailablePorter(porter, null))
            {
                return;
            }

            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            Department homeDepartment =
                employee == null || employee.m_state.m_department == null
                    ? null
                    : employee.m_state.m_department.GetEntity();
            List<LabProcedure> procedures =
                LabProcedureManager.Instance == null
                    ? null
                    : LabProcedureManager.Instance.m_labProcedures;
            if (homeDepartment == null || procedures == null)
            {
                return;
            }

            float bestLocalFreeDistance = float.MaxValue;
            float bestForeignFreeDistance = float.MaxValue;
            WalkComponent porterWalk =
                porter.GetComponent<WalkComponent>();
            for (int i = 0; i < procedures.Count; i++)
            {
                LabProcedure procedure = procedures[i];
                // Vanilla's manager contains procedures in many other states.
                // Check sample readiness before department/room/path lookups.
                if (procedure == null ||
                    procedure.m_state == null ||
                    procedure.m_state.m_labProcedureState !=
                        LabProcedureState.SamplingFinished ||
                    !IsHospitalizedSampleWaiting(procedure))
                {
                    continue;
                }

                // Resolve the source room once and reuse it both for the
                // physical-bundle key and the existing department/room work.
                Room sourceRoom = GetSampleSourceRoom(procedure);
                PorterSampleBundleKey bundleKey;
                PorterSampleFallbackWait fallbackWait;
                if (TryGetSampleBundleKey(
                        procedure,
                        sourceRoom,
                        out bundleKey))
                {
                    if (ReleasedFallbacks.ContainsKey(bundleKey))
                    {
                        continue;
                    }

                    if (FallbackWaits.TryGetValue(
                            bundleKey,
                            out fallbackWait) &&
                        fallbackWait != null &&
                        GetSampleFallbackClockMinute() <
                            fallbackWait.NextRetryGameMinute)
                    {
                        fallbackWait.SuppressedIdleScans++;
                        continue;
                    }
                }

                // Preserve vanilla/HPO's fallback to patient department when
                // the equipment has no room or no owning department.
                Department sourceDepartment =
                    sourceRoom == null ||
                    sourceRoom.m_roomPersistentData == null ||
                    sourceRoom.m_roomPersistentData.m_department == null
                        ? null
                        : sourceRoom.m_roomPersistentData.m_department.GetEntity();
                if (sourceDepartment == null)
                {
                    sourceDepartment = GetPatientDepartment(procedure);
                }
                if (sourceDepartment == null)
                {
                    continue;
                }

                bool foreign = sourceDepartment != homeDepartment;
                if ((departmentScope == 0 && foreign) ||
                    (departmentScope == 1 && !foreign) ||
                    (foreign &&
                     !PorterTransportConfig.SampleCrossDepartmentHelp))
                {
                    continue;
                }

                // Phase 1: all normal eligibility checks EXCEPT pathfinding.
                // A blocked room cannot cause a costly route calculation
                // while a free-room job may still be assigned.
                float ignoredDistance;
                bool validatedForeign;
                if (!CanTakeProcedure(
                        porter,
                        procedure,
                        allowForeignDepartment: true,
                        porterAvailabilityAlreadyValidated: true,
                        allowPreBlockedRoom: true,
                        out ignoredDistance,
                        out validatedForeign,
                        sourceDepartment,
                        skipRouteDistance: true))
                {
                    continue;
                }

                foreign = validatedForeign;
                if (foreign &&
                    !CanForeignSampleHelpPassBestCase(
                        porter, procedure))
                {
                    continue;
                }
                bool blocked = IsSamplePickupRoomBlocked(
                    procedure, sourceRoom);
                if (blocked)
                {
                    if (includeBlocked)
                    {
                        if (blockedCandidates == null)
                        {
                            blockedCandidates =
                                new List<PorterBlockedSampleCandidate>();
                        }
                        PorterBlockedSampleCandidate candidate =
                            new PorterBlockedSampleCandidate();
                        candidate.Procedure = procedure;
                        candidate.Foreign = foreign;
                        blockedCandidates.Add(candidate);
                    }
                    continue;
                }

                // Phase 2: do not calculate a real route when the
                // minimum attainable score cannot beat the best mission
                // in THIS category. On the same floor this is the straight
                // line; otherwise only the existing floor penalty is used.
                TileObject source = procedure.m_state.m_usedEquipment.GetEntity();
                float bestInCategory = foreign
                    ? bestForeignFreeDistance
                    : bestLocalFreeDistance;
                if (CannotBeatSamplePickupDistance(
                        porterWalk, source, bestInCategory))
                {
                    continue;
                }
                float distance =
                    GetSamplePickupRouteDistance(porterWalk, source);
                if (distance < 0f)
                {
                    continue;
                }

                if (foreign &&
                    !ShouldAcceptForeignSampleHelp(
                        porter, procedure, distance))
                {
                    continue;
                }

                if (!foreign)
                {
                    if (distance < bestLocalFreeDistance)
                    {
                        bestLocalFreeDistance = distance;
                        localFree = procedure;
                    }
                }
                else if (distance < bestForeignFreeDistance)
                {
                    bestForeignFreeDistance = distance;
                    foreignFree = procedure;
                }

                // Existence checks do not need the nearest candidate.
                // Preserve the full ranking scan for actual assignments.
                if (stopOnFirstFree &&
                    (localFree != null || foreignFree != null))
                {
                    break;
                }
            }
        }

        private static bool HasAnyFreeSampleWork(Entity porter)
        {
            LabProcedure local;
            LabProcedure foreign;
            List<PorterBlockedSampleCandidate> ignored;
            ScanSampleCandidatesForPorter(
                porter, 2, false, true,
                out local, out foreign, out ignored);
            return local != null || foreign != null;
        }

        private static bool TryFindBestProcedureForPorter(
            Entity porter,
            bool foreignDepartmentOnly,
            out LabProcedure best)
        {
            LabProcedure local;
            LabProcedure foreign;
            List<PorterBlockedSampleCandidate> ignored;
            ScanSampleCandidatesForPorter(
                porter,
                foreignDepartmentOnly ? 1 : 0,
                false,
                true,
                out local,
                out foreign,
                out ignored);
            best = foreignDepartmentOnly ? foreign : local;
            return best != null;
        }

        private static LabProcedure SelectBestBlockedCandidate(
            Entity porter,
            List<PorterBlockedSampleCandidate> candidates,
            bool foreignOnly)
        {
            if (candidates == null)
            {
                return null;
            }

            LabProcedure best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                PorterBlockedSampleCandidate candidate = candidates[i];
                if (candidate.Foreign != foreignOnly)
                {
                    continue;
                }

                // Free-room assignment failed. Only NOW evaluate paths for
                // occupied-room candidates, retaining true route ordering.
                float distance;
                if (!CanTakeProcedure(
                        porter,
                        candidate.Procedure,
                        allowForeignDepartment: true,
                        porterAvailabilityAlreadyValidated: true,
                        allowPreBlockedRoom: true,
                        out distance))
                {
                    continue;
                }

                // Keep the one-time 30% decision after reachability and
                // without drawing again for a cooldown retry.
                if (!HasRoomWaitRetryForBundle(candidate.Procedure) &&
                    !ShouldWaitForPreBlockedRoom(candidate.Procedure))
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    best = candidate.Procedure;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static bool TryFindForeignSampleHelper(
            LabProcedure procedure,
            Department targetDepartment,
            out Entity best,
            out BehaviorNurse bestNurse)
        {
            best = null;
            bestNurse = null;
            if (procedure == null ||
                targetDepartment == null ||
                !PorterTransportConfig.SampleCrossDepartmentHelp ||
                Hospital.Instance == null ||
                Hospital.Instance.m_departments == null)
            {
                return false;
            }

            // All helpers are competing for the SAME sample. Resolve
            // its source once, and reject impossible pickups before any
            // helper scans its entire local sample workload.
            TileObject sampleSource =
                procedure.m_state == null ||
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment.GetEntity();
            if (sampleSource == null || sampleSource.m_state == null ||
                sampleSource.User != null || sampleSource.Owner != null)
            {
                return false;
            }
            float bestDistance = float.MaxValue;
            foreach (Department department in
                Hospital.Instance.m_departments)
            {
                if (department == null ||
                    department == targetDepartment ||
                    department.m_departmentPersistentData.m_nurses == null)
                {
                    continue;
                }

                foreach (EntityIDPointer<Entity> pointer in
                    department.m_departmentPersistentData.m_nurses)
                {
                    Entity candidate = pointer.GetEntity();
                    EmployeeComponent employee =
                        candidate == null
                            ? null
                            : candidate.GetComponent<EmployeeComponent>();
                    if (employee == null ||
                        employee.m_state.m_department.GetEntity() != department ||
                        !IsAvailablePorter(candidate, null))
                    {
                        continue;
                    }

                    // Keep the cheap lower-bound pruning from 1.0.2.47
                    // before the nested local-work scan. Station floor is no
                    // longer an admissibility veto; it contributes to the
                    // stable willingness score after exact reachability.
                    if (CannotBeatSamplePickupDistance(
                            candidate.GetComponent<WalkComponent>(),
                            sampleSource,
                            bestDistance) ||
                        !CanForeignSampleHelpPassBestCase(
                            candidate, procedure))
                    {
                        continue;
                    }

                    // Only a helper that could actually win the current
                    // target AND whose stable willingness roll can still pass
                    // with a perfect route should run this nested priority
                    // check. Its own local work is still checked with exact
                    // native routes.
                    if (HasPendingLocalSampleWork(candidate))
                    {
                        continue;
                    }

                    float distance;
                    if (!CanTakeProcedure(
                            candidate,
                            procedure,
                            allowForeignDepartment: true,
                            porterAvailabilityAlreadyValidated: true,
                            out distance))
                    {
                        continue;
                    }

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                        bestNurse =
                            candidate.GetComponent<BehaviorNurse>();
                    }
                }
            }

            return best != null && bestNurse != null;
        }

        private static Room GetSampleSourceRoom(
            LabProcedure procedure)
        {
            TileObject source =
                procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment.GetEntity();
            return source == null ||
                MapScriptInterface.Instance == null
                    ? null
                    : MapScriptInterface.Instance.GetRoomAt(
                        source.m_state.m_position,
                        source.GetFloorIndex());
        }

        private static bool AreSameSampleBundle(
            LabProcedure first,
            LabProcedure second)
        {
            if (first == null ||
                second == null ||
                first.m_state == null ||
                second.m_state == null)
            {
                return false;
            }

            if (first == second)
            {
                return true;
            }

            Entity firstPatient =
                first.m_state.m_patient == null
                    ? null
                    : first.m_state.m_patient.GetEntity();
            Entity secondPatient =
                second.m_state.m_patient == null
                    ? null
                    : second.m_state.m_patient.GetEntity();
            if (firstPatient == null ||
                secondPatient == null ||
                firstPatient != secondPatient)
            {
                return false;
            }

            Room firstRoom =
                GetSampleSourceRoom(first);
            Room secondRoom =
                GetSampleSourceRoom(second);
            return firstRoom != null &&
                secondRoom != null &&
                firstRoom == secondRoom;
        }

        private static bool IsProcedureClaimedByOtherJob(
            LabProcedure procedure,
            PorterSampleTransportJob currentJob)
        {
            if (procedure == null)
            {
                return false;
            }

            foreach (KeyValuePair<Entity, PorterSampleTransportJob> pair in Jobs)
            {
                PorterSampleTransportJob job =
                    pair.Value;
                if (job == null ||
                    job == currentJob)
                {
                    continue;
                }

                if (AreSameSampleBundle(
                        job.Procedure,
                        procedure))
                {
                    return true;
                }

                for (int i = 0;
                    i < job.CollectedProcedures.Count;
                    i++)
                {
                    if (AreSameSampleBundle(
                            job.CollectedProcedures[i],
                            procedure))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int CountCollectedSampleBundles(
            PorterSampleTransportJob job)
        {
            if (job == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0;
                i < job.CollectedProcedures.Count;
                i++)
            {
                LabProcedure procedure =
                    job.CollectedProcedures[i];
                if (procedure == null)
                {
                    continue;
                }

                bool alreadyCounted = false;
                for (int j = 0; j < i; j++)
                {
                    if (AreSameSampleBundle(
                            procedure,
                            job.CollectedProcedures[j]))
                    {
                        alreadyCounted = true;
                        break;
                    }
                }

                if (!alreadyCounted)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CollectCurrentSampleBundle(
            PorterSampleTransportJob job)
        {
            if (job == null ||
                job.Procedure == null ||
                job.Procedure.m_state == null ||
                LabProcedureManager.Instance == null ||
                LabProcedureManager.Instance.m_labProcedures == null)
            {
                return 0;
            }

            LabProcedure primary =
                job.Procedure;
            int added = 0;

            for (int i = 0;
                i <
                    LabProcedureManager.Instance
                        .m_labProcedures.Count;
                i++)
            {
                LabProcedure candidate =
                    LabProcedureManager.Instance
                        .m_labProcedures[i];
                if (!AreSameSampleBundle(
                        primary,
                        candidate) ||
                    candidate == null ||
                    candidate.m_state == null ||
                    IsProcedureClaimedByOtherJob(
                        candidate,
                        job) ||
                    IsSampleBundleHandledByLabSpecialist(
                        candidate))
                {
                    continue;
                }

                if (candidate != primary)
                {
                    if (candidate.m_state.m_labProcedureState !=
                            LabProcedureState.SamplingFinished)
                    {
                        continue;
                    }

                    TileObject source =
                        candidate.m_state.m_usedEquipment == null
                            ? null
                            : candidate.m_state
                                .m_usedEquipment.GetEntity();
                    if (source == null ||
                        source.User != null ||
                        source.Owner != null)
                    {
                        continue;
                    }

                    candidate.SwitchState(
                        LabProcedureState.SampleBeingDelivered);
                }

                if (candidate.m_state.m_labProcedureState !=
                        LabProcedureState.SampleBeingDelivered ||
                    job.CollectedProcedures.Contains(
                        candidate))
                {
                    continue;
                }

                job.CollectedProcedures.Add(
                    candidate);
                ClearFallbackTracking(
                    candidate);
                added++;
            }

            if (added > 0)
            {
                Room room =
                    GetSampleSourceRoom(primary);
                PorterDiagnostics.Log(
                    "sample bundle collected: patient=" +
                    GetPatientName(primary) +
                    "; roomType=" +
                    (room == null ||
                     room.m_roomPersistentData == null ||
                     room.m_roomPersistentData.m_roomType == null ||
                     room.m_roomPersistentData.m_roomType.Entry == null
                        ? "NONE"
                        : room.m_roomPersistentData
                            .m_roomType.Entry.DatabaseID.ToString()) +
                    "; procedures=" +
                    added +
                    "; physicalSamples=1.");
            }

            return added;
        }

        internal static bool IsNativeLabSamplePickupCandidate(
            LabProcedure procedure)
        {
            if (procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SamplingFinished)
            {
                return false;
            }

            Entity patient =
                procedure.m_state.m_patient == null
                    ? null
                    : procedure.m_state.m_patient.GetEntity();
            HospitalizationComponent hospitalization =
                patient == null
                    ? null
                    : patient.GetComponent<HospitalizationComponent>();
            return hospitalization != null &&
                hospitalization.IsHospitalized();
        }

        internal static bool IsNativeLabSampleBundleClaimedByOther(
            BehaviorLabSpecialist lab,
            LabProcedure procedure)
        {
            if (lab == null ||
                procedure == null)
            {
                return false;
            }

            foreach (KeyValuePair<BehaviorLabSpecialist, LabProcedure> pair in
                NativeLabSampleBundles)
            {
                if (pair.Key != lab &&
                    AreSameSampleBundle(
                        pair.Value,
                        procedure))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void RegisterNativeLabSampleBundle(
            BehaviorLabSpecialist lab,
            LabProcedure primary)
        {
            if (lab == null ||
                primary == null ||
                primary.m_state == null ||
                LabProcedureManager.Instance == null ||
                LabProcedureManager.Instance.m_labProcedures == null)
            {
                return;
            }

            if (IsFallbackReleasedForBundle(primary))
            {
                RecordSamplePorterCapacityFallback(primary);
            }

            NativeLabSampleBundles[lab] =
                primary;

            int procedures = 0;
            for (int i = 0;
                i <
                    LabProcedureManager.Instance
                        .m_labProcedures.Count;
                i++)
            {
                LabProcedure candidate =
                    LabProcedureManager.Instance
                        .m_labProcedures[i];
                if (!AreSameSampleBundle(
                        primary,
                        candidate) ||
                    candidate == null ||
                    candidate.m_state == null ||
                    IsProcedureClaimed(candidate))
                {
                    continue;
                }

                if (candidate == primary)
                {
                    if (candidate.m_state.m_labProcedureState ==
                        LabProcedureState.SampleBeingDelivered)
                    {
                        procedures++;
                    }
                    continue;
                }

                if (candidate.m_state.m_labProcedureState !=
                    LabProcedureState.SamplingFinished ||
                    IsNativeLabSampleBundleClaimedByOther(
                        lab,
                        candidate))
                {
                    continue;
                }

                TileObject source =
                    candidate.m_state.m_usedEquipment == null
                        ? null
                        : candidate.m_state
                            .m_usedEquipment.GetEntity();
                if (source == null ||
                    source.User != null ||
                    source.Owner != null)
                {
                    continue;
                }

                candidate.SwitchState(
                    LabProcedureState.SampleBeingDelivered);
                ClearFallbackTracking(
                    candidate);
                procedures++;
            }

            Room room =
                GetSampleSourceRoom(primary);
            PorterDiagnostics.Log(
                "native lab sample bundle claimed: patient=" +
                GetPatientName(primary) +
                "; procedureId=" +
                primary.GetEntityID() +
                "; roomWaitReleasedToLab=" +
                IsRoomWaitReleasedToLab(primary) +
                "; roomType=" +
                (room == null ||
                 room.m_roomPersistentData == null ||
                 room.m_roomPersistentData.m_roomType == null ||
                 room.m_roomPersistentData.m_roomType.Entry == null
                    ? "NONE"
                    : room.m_roomPersistentData
                        .m_roomType.Entry.DatabaseID.ToString()) +
                "; procedures=" +
                procedures +
                "; physicalSamples=1.");
        }

        internal static bool CompleteNativeLabSampleBundle(
            BehaviorLabSpecialist lab)
        {
            if (lab == null)
            {
                return false;
            }

            LabProcedure primary;
            if (!NativeLabSampleBundles.TryGetValue(
                    lab,
                    out primary))
            {
                return false;
            }

            Entity patient =
                lab.m_state == null ||
                lab.m_state.m_currentPatient == null
                    ? null
                    : lab.m_state.m_currentPatient.GetEntity();
            if (patient == null)
            {
                NativeLabSampleBundles.Remove(
                    lab);
                return false;
            }

            bool wasReleasedFromOutsideWait =
                IsRoomWaitReleasedToLab(primary);
            int delivered = 0;
            if (LabProcedureManager.Instance != null &&
                LabProcedureManager.Instance.m_labProcedures != null)
            {
                for (int i = 0;
                    i <
                        LabProcedureManager.Instance
                            .m_labProcedures.Count;
                    i++)
                {
                    LabProcedure candidate =
                        LabProcedureManager.Instance
                            .m_labProcedures[i];
                    if (!AreSameSampleBundle(
                            primary,
                            candidate) ||
                        candidate == null ||
                        candidate.m_state == null ||
                        candidate.m_state.m_labProcedureState !=
                            LabProcedureState.SampleBeingDelivered ||
                        IsProcedureClaimed(candidate))
                    {
                        continue;
                    }

                    candidate.SwitchState(
                        LabProcedureState.DeliveredToStatLab);
                    ClearFallbackTracking(
                        candidate);
                    delivered++;
                }
            }

            NativeLabSampleBundles.Remove(
                lab);
            lab.m_state.m_currentPatient = null;
            lab.GoToWorkplace();

            PorterDiagnostics.Log(
                "native lab sample bundle delivered: patient=" +
                (patient.Name ?? string.Empty).Trim() +
                "; procedureId=" +
                primary.GetEntityID() +
                "; roomWaitReleasedToLab=" +
                wasReleasedFromOutsideWait +
                "; procedures=" +
                delivered +
                "; physicalSamples=1.");
            return true;
        }

        internal static void CleanupNativeLabSampleBundle(
            BehaviorLabSpecialist lab)
        {
            if (lab == null ||
                lab.m_state == null)
            {
                return;
            }

            LabProcedure primary;
            if (!NativeLabSampleBundles.TryGetValue(
                    lab,
                    out primary))
            {
                return;
            }

            LabSpecialistState state =
                lab.m_state.m_labSpecialistState;
            if (IsNativeLabTransportState(
                    state))
            {
                return;
            }

            NativeLabSampleBundles.Remove(
                lab);

            int requeued = 0;
            if (LabProcedureManager.Instance != null &&
                LabProcedureManager.Instance.m_labProcedures != null)
            {
                for (int i = 0;
                    i <
                        LabProcedureManager.Instance
                            .m_labProcedures.Count;
                    i++)
                {
                    LabProcedure candidate =
                        LabProcedureManager.Instance
                            .m_labProcedures[i];
                    if (!AreSameSampleBundle(
                            primary,
                            candidate) ||
                        candidate == null ||
                        candidate.m_state == null ||
                        candidate.m_state.m_labProcedureState !=
                            LabProcedureState.SampleBeingDelivered ||
                        IsProcedureClaimed(candidate))
                    {
                        continue;
                    }

                    candidate.SwitchState(
                        LabProcedureState.SamplingFinished);
                    ClearFallbackTracking(
                        candidate);
                    requeued++;
                }
            }

            PorterDiagnostics.Log(
                "native lab sample bundle released: patient=" +
                GetPatientName(primary) +
                "; procedures=" +
                requeued +
                "; state=" +
                state +
                ".");
        }

        private static bool IsSampleBundleHandledByLabSpecialist(
            LabProcedure procedure)
        {
            if (procedure == null)
            {
                return false;
            }

            foreach (KeyValuePair<BehaviorLabSpecialist, LabProcedure> pair in
                NativeLabSampleBundles)
            {
                if (AreSameSampleBundle(
                        pair.Value,
                        procedure))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetSampleApproachTarget(
            PorterSampleTransportJob job,
            Vector2i target,
            int floorIndex)
        {
            if (job == null)
            {
                return;
            }

            job.SampleApproachTarget = target;
            job.SampleApproachFloorIndex = floorIndex;
            job.HasSampleApproachTarget = true;
        }

        private static bool IsAllowedSampleRoomWaitRoom(
            Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null ||
                room.m_roomPersistentData.m_roomType.Entry == null ||
                Database.Instance == null)
            {
                return false;
            }

            GameDBRoomType roomType =
                room.m_roomPersistentData.m_roomType.Entry;
            GameDBRoomType corridor =
                Database.Instance.GetEntry<GameDBRoomType>(
                    "ROOM_TYPE_CORRIDOR");
            GameDBRoomType waiting =
                Database.Instance.GetEntry<GameDBRoomType>(
                    "ROOM_TYPE_WAITING");
            GameDBRoomType reception =
                Database.Instance.GetEntry<GameDBRoomType>(
                    "ROOM_TYPE_RECEPTION");

            return roomType == corridor ||
                roomType == waiting ||
                roomType == reception;
        }

        private static bool IsValidSampleRoomWaitTile(
            Entity porter,
            Floor floor,
            Room sourceRoom,
            Vector2i candidate)
        {
            if (porter == null ||
                floor == null ||
                sourceRoom == null ||
                candidate.m_x < 0 ||
                candidate.m_y < 0 ||
                candidate.m_x >= floor.Size.m_x ||
                candidate.m_y >= floor.Size.m_y ||
                sourceRoom.IsPositionInRoom(candidate) ||
                floor.IsAnyObjectAt(candidate) ||
                !GridMap.GetInstance().IsInGrid(
                    candidate,
                    floor.m_floorIndex))
            {
                return false;
            }

            Entity tileUser =
                floor.m_mapPersistentData
                    .m_tiles[
                        candidate.m_x,
                        candidate.m_y]
                    .m_user == null
                        ? null
                        : floor.m_mapPersistentData
                            .m_tiles[
                                candidate.m_x,
                                candidate.m_y]
                            .m_user.GetEntity();

            return tileUser == null ||
                tileUser == porter;
        }

        private static bool HasNearbySampleRoomWaitDoor(
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
                    if (x < 0 ||
                        y < 0 ||
                        x >= floor.Size.m_x ||
                        y >= floor.Size.m_y)
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

        private static bool HasAdjacentSampleRoomWaitWall(
            Floor floor,
            Vector2i position)
        {
            if (floor == null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= floor.Size.m_x ||
                position.m_y >= floor.Size.m_y)
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

        private static bool IsSampleRoomWaitCartDirectionSafe(
            Entity porter,
            PorterSampleTransportJob job,
            Floor floor,
            Room sourceRoom,
            Vector2i porterPosition,
            Direction direction)
        {
            TileObject cart =
                job == null
                    ? null
                    : job.Cart;
            if (cart == null ||
                cart.m_state == null ||
                !cart.m_state.m_moving ||
                !cart.IsAttachedToCharacter() ||
                cart.User != porter)
            {
                return true;
            }

            if (floor == null ||
                sourceRoom == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            Vector2f offset =
                TileObject.TransformOffsetForDirection(
                    cart.GetCharacterAttachmentOffset(),
                    direction);
            Vector2i cartTile =
                new Vector2f(porterPosition)
                    .add(offset)
                    .ToVector2i();

            if (cartTile.m_x < 0 ||
                cartTile.m_y < 0 ||
                cartTile.m_x >= floor.Size.m_x ||
                cartTile.m_y >= floor.Size.m_y ||
                sourceRoom.IsPositionInRoom(cartTile) ||
                !PorterSampleCartRuntime.IsCartPlacementCompletelyFree(
                    cart,
                    floor,
                    cartTile) ||
                !GridMap.GetInstance().IsInGrid(
                    cartTile,
                    floor.m_floorIndex))
            {
                return false;
            }

            Room cartRoom =
                floor.GetRoomTileSafe(
                    cartTile.m_x,
                    cartTile.m_y);
            if (!IsAllowedSampleRoomWaitRoom(cartRoom))
            {
                return false;
            }

            Entity tileUser =
                floor.m_mapPersistentData
                    .m_tiles[
                        cartTile.m_x,
                        cartTile.m_y]
                    .m_user == null
                        ? null
                        : floor.m_mapPersistentData
                            .m_tiles[
                                cartTile.m_x,
                                cartTile.m_y]
                            .m_user.GetEntity();
            if (tileUser != null &&
                tileUser != porter)
            {
                return false;
            }

            return cartTile == porterPosition ||
                floor.IsAccessibleIgnoreDoors(
                    porterPosition,
                    cartTile);
        }

        private static bool TryGetSampleRoomWaitCartDirection(
            Entity porter,
            PorterSampleTransportJob job,
            Floor floor,
            Room sourceRoom,
            Vector2i porterPosition,
            Direction preferredDirection,
            out Direction direction)
        {
            direction = Direction.None;

            TileObject cart =
                job == null
                    ? null
                    : job.Cart;
            if (cart == null ||
                cart.m_state == null ||
                !cart.m_state.m_moving ||
                !cart.IsAttachedToCharacter() ||
                cart.User != porter)
            {
                direction = preferredDirection;
                return true;
            }

            if (preferredDirection != Direction.None &&
                IsSampleRoomWaitCartDirectionSafe(
                    porter,
                    job,
                    floor,
                    sourceRoom,
                    porterPosition,
                    preferredDirection))
            {
                direction = preferredDirection;
                return true;
            }

            if (IsSampleRoomWaitCartDirectionSafe(
                    porter,
                    job,
                    floor,
                    sourceRoom,
                    porterPosition,
                    Direction.NW))
            {
                direction = Direction.NW;
                return true;
            }

            if (IsSampleRoomWaitCartDirectionSafe(
                    porter,
                    job,
                    floor,
                    sourceRoom,
                    porterPosition,
                    Direction.NE))
            {
                direction = Direction.NE;
                return true;
            }

            if (IsSampleRoomWaitCartDirectionSafe(
                    porter,
                    job,
                    floor,
                    sourceRoom,
                    porterPosition,
                    Direction.SE))
            {
                direction = Direction.SE;
                return true;
            }

            if (IsSampleRoomWaitCartDirectionSafe(
                    porter,
                    job,
                    floor,
                    sourceRoom,
                    porterPosition,
                    Direction.SW))
            {
                direction = Direction.SW;
                return true;
            }

            return false;
        }

        private static bool CanStageAttachedSampleCartAtWaitTile(
            Entity porter,
            PorterSampleTransportJob job,
            Floor floor,
            Room sourceRoom,
            Vector2i porterPosition)
        {
            Direction direction;
            return TryGetSampleRoomWaitCartDirection(
                porter,
                job,
                floor,
                sourceRoom,
                porterPosition,
                Direction.None,
                out direction);
        }

        private static bool EnsureSampleRoomWaitCartFootprint(
            Entity porter,
            PorterSampleTransportJob job,
            Floor floor,
            Room sourceRoom,
            Vector2i porterPosition)
        {
            TileObject cart =
                job == null
                    ? null
                    : job.Cart;
            if (cart == null ||
                cart.m_state == null ||
                !cart.m_state.m_moving ||
                !cart.IsAttachedToCharacter() ||
                cart.User != porter)
            {
                return true;
            }

            AnimModelComponent animation =
                porter == null
                    ? null
                    : porter.GetComponent<AnimModelComponent>();
            if (animation == null)
            {
                return false;
            }

            Direction safeDirection;
            if (!TryGetSampleRoomWaitCartDirection(
                    porter,
                    job,
                    floor,
                    sourceRoom,
                    porterPosition,
                    animation.m_state.m_direction,
                    out safeDirection))
            {
                return false;
            }

            if (safeDirection != Direction.None &&
                animation.m_state.m_direction != safeDirection)
            {
                animation.SetDirection(safeDirection);
            }

            return true;
        }

        private static bool TryFindSampleRoomWaitTile(
            Entity porter,
            PorterSampleTransportJob job,
            Room sourceRoom,
            out Vector2i waitTile,
            out Vector2i doorwayAnchor,
            out bool usedDoorFallback)
        {
            waitTile = Vector2i.ZERO_VECTOR;
            doorwayAnchor = Vector2i.ZERO_VECTOR;
            usedDoorFallback = false;

            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            if (walk == null ||
                sourceRoom == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                GridMap.GetInstance() == null ||
                MapScriptInterface.Instance == null ||
                sourceRoom.GetFloorIndex() < 0 ||
                sourceRoom.GetFloorIndex() >=
                    Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            int floorIndex =
                sourceRoom.GetFloorIndex();
            Floor floor =
                Hospital.Instance.m_floors[floorIndex];
            Vector2i current =
                walk.GetCurrentTile();

            float bestAnchorDistance =
                float.MaxValue;
            int anchorMinX =
                Math.Max(
                    0,
                    sourceRoom.m_roomPersistentData
                        .m_positionBottom.m_x - 2);
            int anchorMaxX =
                Math.Min(
                    floor.Size.m_x - 1,
                    sourceRoom.m_roomPersistentData
                        .m_positionTop.m_x + 2);
            int anchorMinY =
                Math.Max(
                    0,
                    sourceRoom.m_roomPersistentData
                        .m_positionBottom.m_y - 2);
            int anchorMaxY =
                Math.Min(
                    floor.Size.m_y - 1,
                    sourceRoom.m_roomPersistentData
                        .m_positionTop.m_y + 2);

            for (int x = anchorMinX; x <= anchorMaxX; x++)
            {
                for (int y = anchorMinY; y <= anchorMaxY; y++)
                {
                    // Only on the same floor: Euclidean distance is a
                    // lower bound on native walking distance. Once a closer
                    // reachable anchor exists, an impossible competitor
                    // needs neither room validation nor pathfinding.
                    if (walk.GetFloorIndex() == floorIndex &&
                        bestAnchorDistance < float.MaxValue)
                    {
                        float dx = x - current.m_x;
                        float dy = y - current.m_y;
                        if (dx * dx + dy * dy >=
                            bestAnchorDistance * bestAnchorDistance)
                        {
                            continue;
                        }
                    }

                    Vector2i candidate =
                        new Vector2i(x, y);
                    if (!IsValidSampleRoomWaitTile(
                            porter,
                            floor,
                            sourceRoom,
                            candidate))
                    {
                        continue;
                    }

                    float distance =
                        GridMap.GetInstance().GetDistance(
                            walk.GetFloorIndex(),
                            current,
                            floorIndex,
                            candidate,
                            AccessRights.STAFF);
                    if (distance < 0f ||
                        distance >= bestAnchorDistance)
                    {
                        continue;
                    }

                    bestAnchorDistance = distance;
                    doorwayAnchor = candidate;
                }
            }

            if (bestAnchorDistance == float.MaxValue)
            {
                return false;
            }

            float bestScore =
                float.MaxValue;
            float bestRouteDistance =
                float.MaxValue;
            int minX =
                Math.Max(
                    0,
                    doorwayAnchor.m_x -
                        SampleRoomWaitRadius);
            int maxX =
                Math.Min(
                    floor.Size.m_x - 1,
                    doorwayAnchor.m_x +
                        SampleRoomWaitRadius);
            int minY =
                Math.Max(
                    0,
                    doorwayAnchor.m_y -
                        SampleRoomWaitRadius);
            int maxY =
                Math.Min(
                    floor.Size.m_y - 1,
                    doorwayAnchor.m_y +
                        SampleRoomWaitRadius);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Vector2i candidate =
                        new Vector2i(x, y);
                    if (!IsValidSampleRoomWaitTile(
                            porter,
                            floor,
                            sourceRoom,
                            candidate))
                    {
                        continue;
                    }

                    Room candidateRoom =
                        floor.GetRoomTileSafe(
                            candidate.m_x,
                            candidate.m_y);
                    if (!IsAllowedSampleRoomWaitRoom(
                            candidateRoom))
                    {
                        continue;
                    }

                    if (!CanStageAttachedSampleCartAtWaitTile(
                            porter,
                            job,
                            floor,
                            sourceRoom,
                            candidate))
                    {
                        continue;
                    }

                    // A candidate farther than the native max in a
                    // straight line cannot pass the exact route limit.
                    float dx = x - doorwayAnchor.m_x;
                    float dy = y - doorwayAnchor.m_y;
                    if (dx * dx + dy * dy >
                        SampleRoomWaitMaximumDistance *
                        SampleRoomWaitMaximumDistance)
                    {
                        continue;
                    }

                    float routeDistance =
                        GridMap.GetInstance().GetDistance(
                            floorIndex,
                            doorwayAnchor,
                            floorIndex,
                            candidate,
                            AccessRights.STAFF);
                    if (routeDistance < 0f ||
                        routeDistance >
                            SampleRoomWaitMaximumDistance)
                    {
                        continue;
                    }

                    // Score depends on the door route, not the Porter
                    // route. Skip the second expensive route when this
                    // position cannot improve the chosen wait tile.
                    float score =
                        Math.Abs(
                            routeDistance -
                            SampleRoomWaitIdealDistance);
                    if (routeDistance <
                        SampleRoomWaitNearDoorDistance)
                    {
                        score +=
                            SampleRoomWaitNearDoorPenalty;
                    }

                    if (HasNearbySampleRoomWaitDoor(
                            floor,
                            candidate))
                    {
                        score +=
                            SampleRoomWaitAnyDoorPenalty;
                    }
                    else if (HasAdjacentSampleRoomWaitWall(
                                 floor,
                                 candidate))
                    {
                        score -=
                            SampleRoomWaitWallPreferenceBonus;
                    }

                    if (score > bestScore ||
                        (score == bestScore &&
                         routeDistance >= bestRouteDistance))
                    {
                        continue;
                    }

                    float fromPorter =
                        GridMap.GetInstance().GetDistance(
                            walk.GetFloorIndex(),
                            current,
                            floorIndex,
                            candidate,
                            AccessRights.STAFF);
                    if (fromPorter < 0f)
                    {
                        continue;
                    }

                    bestScore = score;
                    bestRouteDistance = routeDistance;
                    waitTile = candidate;
                }
            }

            if (bestScore < float.MaxValue)
            {
                return true;
            }

            if (!CanStageAttachedSampleCartAtWaitTile(
                    porter,
                    job,
                    floor,
                    sourceRoom,
                    doorwayAnchor))
            {
                return false;
            }

            waitTile = doorwayAnchor;
            usedDoorFallback = true;
            return true;
        }

        private static void ReleaseSampleSourceReservation(
            Entity porter,
            PorterSampleTransportJob job)
        {
            UseComponent use =
                porter == null
                    ? null
                    : porter.GetComponent<UseComponent>();
            TileObject source =
                job == null ||
                job.Procedure == null ||
                job.Procedure.m_state == null ||
                job.Procedure.m_state.m_usedEquipment == null
                    ? null
                    : job.Procedure.m_state.m_usedEquipment.GetEntity();
            if (use == null ||
                source == null ||
                source.User != porter)
            {
                return;
            }

            if (use.IsUsing() ||
                use.m_state.m_reservedObject != null)
            {
                use.Interrupt();
            }

            if (source.User == porter)
            {
                source.User = null;
            }
        }

        private static bool EnsureSampleSourceReserved(
            Entity porter,
            PorterSampleTransportJob job,
            TileObject source)
        {
            UseComponent use =
                porter == null
                    ? null
                    : porter.GetComponent<UseComponent>();
            if (use == null ||
                source == null)
            {
                return false;
            }

            if (source.User == porter)
            {
                return true;
            }

            if (source.User != null)
            {
                return false;
            }

            if (use.m_state.m_reservedObject != null ||
                use.IsUsing())
            {
                use.Interrupt();
            }

            use.ReserveObject(source);
            return source.User == porter;
        }

        private static void ResetSampleRoomWaitTimeout(
            PorterSampleTransportJob job)
        {
            if (job == null)
            {
                return;
            }

            job.SampleRoomWaitDeadlineMinute = 0;
            job.SampleRoomStagingDeadlineMinute = 0;
        }

        private static bool ShouldReleaseBlockedSample(
            Entity porter,
            PorterSampleTransportJob job,
            bool allowOutsideRoomRetry,
            bool awaitingWaitTarget)
        {
            if (job == null)
            {
                return false;
            }

            int configuredTimeoutMinutes =
                PorterTransportConfig.SampleOccupiedRoomTimeoutMinutes;
            if (configuredTimeoutMinutes <= 0)
            {
                return true;
            }

            if (DayTime.Instance == null)
            {
                return false;
            }

            int nowMinute =
                (int)GetCartRestoreClockMinutes();

            int deadlineMinute = awaitingWaitTarget
                ? job.SampleRoomStagingDeadlineMinute
                : job.SampleRoomWaitDeadlineMinute;
            if (deadlineMinute <= 0)
            {
                int timeoutMinutes =
                    GetRandomizedSampleRoomTimeoutMinutes(
                        configuredTimeoutMinutes);

                deadlineMinute = nowMinute + timeoutMinutes;
                if (awaitingWaitTarget)
                {
                    job.SampleRoomStagingDeadlineMinute = deadlineMinute;
                }
                else
                {
                    job.SampleRoomWaitDeadlineMinute = deadlineMinute;
                }

                PorterDiagnostics.Log(
                    (awaitingWaitTarget
                        ? "sample room staging rescue timeout started: porter="
                        : "sample room timeout started: porter=") +
                    (porter == null
                        ? "UNKNOWN"
                        : (porter.Name ?? string.Empty).Trim()) +
                    "; patient=" +
                    GetPatientName(job.Procedure) +
                    "; procedureId=" +
                    (job.Procedure == null
                        ? "NONE"
                        : job.Procedure.GetEntityID().ToString()) +
                    "; waitStage=" +
                    (HasRoomWaitRetryForBundle(job.Procedure)
                        ? "2/2"
                        : "1/2") +
                    "; timeoutMinutes=" +
                    timeoutMinutes +
                    "; baseMinutes=" +
                    configuredTimeoutMinutes +
                    "; randomnessMinutes=" +
                    PorterTransportConfig
                        .SampleOccupiedRoomTimeoutRandomnessMinutes +
                    "; deadlineMinute=" +
                    deadlineMinute +
                    ".");
                return false;
            }

            if (nowMinute < deadlineMinute)
            {
                return false;
            }

            if (allowOutsideRoomRetry &&
                job.Procedure != null &&
                !IsRoomWaitReleasedToLab(job.Procedure))
            {
                if (!HasRoomWaitRetryForBundle(job.Procedure))
                {
                    RoomWaitRetryCounts[job.Procedure] = 1;
                    int retryCooldownMinutes =
                        UnityEngine.Random.Range(
                            PorterTransportConfig.SampleOccupiedRoomRetryCooldownMinMinutes,
                            PorterTransportConfig.SampleOccupiedRoomRetryCooldownMaxMinutes + 1);
                    int retryAvailableMinute =
                        nowMinute + retryCooldownMinutes;
                    RoomWaitRetryAvailableMinutes[job.Procedure] =
                        retryAvailableMinute;

                    PorterDiagnostics.Log(
                        "sample room Porter retry queued: porter=" +
                        (porter == null
                            ? "UNKNOWN"
                            : (porter.Name ?? string.Empty).Trim()) +
                        "; patient=" +
                        GetPatientName(job.Procedure) +
                        "; examination=" +
                        job.Procedure.m_state.m_examination.Entry.DatabaseID +
                        "; procedureId=" +
                        job.Procedure.GetEntityID() +
                        "; nextAttempt=2/2" +
                        "; cooldownMinutes=" +
                        retryCooldownMinutes +
                        "; availableMinute=" +
                        retryAvailableMinute +
                        "; action=release-current-Porter-and-requeue-sample.");
                }
                else
                {
                    ClearRoomWaitRetryForBundle(job.Procedure);
                    RoomWaitReleasedToLab.Add(job.Procedure);

                    PorterDiagnostics.Log(
                        "sample room released to native lab: porter=" +
                        (porter == null
                            ? "UNKNOWN"
                            : (porter.Name ?? string.Empty).Trim()) +
                        "; patient=" +
                        GetPatientName(job.Procedure) +
                        "; examination=" +
                        job.Procedure.m_state.m_examination.Entry.DatabaseID +
                        "; procedureId=" +
                        job.Procedure.GetEntityID() +
                        "; attemptsExhausted=2/2" +
                        "; state=" +
                        job.Procedure.m_state.m_labProcedureState +
                        ".");
                }
            }

            PorterDiagnostics.Log(
                (awaitingWaitTarget
                    ? "sample room staging rescue timeout expired: porter="
                    : "sample room timeout expired: porter=") +
                (porter == null
                    ? "UNKNOWN"
                    : (porter.Name ?? string.Empty).Trim()) +
                "; patient=" +
                GetPatientName(job.Procedure) +
                "; procedureId=" +
                (job.Procedure == null
                    ? "NONE"
                    : job.Procedure.GetEntityID().ToString()) +
                "; baseMinutes=" +
                configuredTimeoutMinutes +
                "; randomnessMinutes=" +
                PorterTransportConfig
                    .SampleOccupiedRoomTimeoutRandomnessMinutes +
                "; deadlineMinute=" +
                deadlineMinute +
                "; currentMinute=" +
                nowMinute +
                ".");
            return true;
        }

        private static int GetStablePreAssignmentBlockedRoomTimeoutMinutes(
            LabProcedure procedure)
        {
            int baseMinutes =
                PorterTransportConfig.SampleOccupiedRoomTimeoutMinutes;
            if (baseMinutes <= 0)
            {
                return 0;
            }

            int randomnessMinutes =
                PorterTransportConfig
                    .SampleOccupiedRoomTimeoutRandomnessMinutes;
            if (randomnessMinutes <= 0)
            {
                return baseMinutes;
            }

            int minimum =
                Math.Max(
                    1,
                    baseMinutes - randomnessMinutes);
            int maximum =
                Math.Min(
                    240,
                    baseMinutes + randomnessMinutes);
            if (maximum <= minimum)
            {
                return minimum;
            }

            uint hash =
                StableHashStep(
                    GetPhysicalSampleBundleStableHash(procedure),
                    PreBlockedRoomTimeoutSalt);
            uint width =
                (uint)(maximum - minimum + 1);
            return minimum + (int)(hash % width);
        }

        private static int GetRandomizedSampleRoomTimeoutMinutes(
            int baseMinutes)
        {
            if (baseMinutes <= 0)
            {
                return 0;
            }

            int randomnessMinutes =
                PorterTransportConfig
                    .SampleOccupiedRoomTimeoutRandomnessMinutes;
            if (randomnessMinutes <= 0)
            {
                return baseMinutes;
            }

            int minimum =
                Math.Max(
                    1,
                    baseMinutes - randomnessMinutes);
            int maximum =
                Math.Min(
                    240,
                    baseMinutes + randomnessMinutes);

            if (maximum <= minimum)
            {
                return minimum;
            }

            return UnityEngine.Random.Range(
                minimum,
                maximum + 1);
        }

        private static bool IsCartParkedInsideProcedureRoom(
            TileObject cart,
            LabProcedure procedure)
        {
            if (cart == null ||
                cart.m_state.m_moving ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            Room sourceRoom =
                GetSampleSourceRoom(procedure);
            Room cartRoom =
                MapScriptInterface.Instance.GetRoomAt(
                    cart.m_state.m_position,
                    cart.GetFloorIndex());

            return sourceRoom != null &&
                cartRoom == sourceRoom;
        }

        private static void AbandonBlockedRoomTourWithParkedCart(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            if (job == null)
            {
                return;
            }

            int requeued =
                RequeueUndeliveredSamples(
                    job,
                    includeCurrent: false);
            job.CollectedProcedures.Clear();

            TileObject cart =
                job.Cart;
            if (cart != null)
            {
                cart.User = null;
                cart.SetAttachedToCharacter(
                    attached: false);
                cart.m_state.m_originalFloorIndex =
                    -1;
            }

            PorterDiagnostics.Log(
                "sample room fallback released parked cart: porter=" +
                (porter == null
                    ? "UNKNOWN"
                    : (porter.Name ?? string.Empty).Trim()) +
                "; requeuedCollected=" +
                requeued +
                "; cartPosition=" +
                (cart == null
                    ? "NONE"
                    : cart.m_state.m_position.ToString()) +
                "; cartFloor=" +
                (cart == null
                    ? -1
                    : cart.GetFloorIndex()) +
                ".");

            FinalizeJob(
                porter,
                nurse,
                job,
                timedOut: false);
        }

        private static void ReleaseBlockedSampleAndContinue(
            Entity porter,
            BehaviorNurse nurse,
            WalkComponent walk,
            PorterSampleTransportJob job)
        {
            if (job == null)
            {
                return;
            }

            HideSampleRoomWaitingBubble(porter, job);
            LabProcedure releasedProcedure =
                job.Procedure;

            ReleaseSampleSourceReservation(
                porter,
                job);

            if (releasedProcedure != null &&
                releasedProcedure.m_state != null &&
                releasedProcedure.m_state.m_labProcedureState ==
                    LabProcedureState.SampleBeingDelivered)
            {
                releasedProcedure.SwitchState(
                    LabProcedureState.SamplingFinished);
            }

            ClearFallbackTracking(
                releasedProcedure);

            SpeechComponent speech =
                porter == null
                    ? null
                    : porter.GetComponent<SpeechComponent>();
            if (speech != null)
            {
                speech.HideBubble();
            }

            job.Procedure = null;
            job.HasSampleApproachTarget = false;
            job.HasSampleRoomWaitTarget = false;
            ResetSampleRoomWaitTimeout(job);

            PorterDiagnostics.Log(
                "sample room timeout fallback: porter=" +
                (porter == null
                    ? "UNKNOWN"
                    : (porter.Name ?? string.Empty).Trim()) +
                "; releasedPatient=" +
                GetPatientName(releasedProcedure) +
                "; procedureId=" +
                (releasedProcedure == null
                    ? "NONE"
                    : releasedProcedure.GetEntityID().ToString()) +
                "; releasedToLab=" +
                IsRoomWaitReleasedToLab(releasedProcedure) +
                "; collected=" +
                job.CollectedProcedures.Count +
                "; cart=" +
                (job.Cart != null) +
                ".");

            if (IsCartParkedInsideProcedureRoom(
                    job.Cart,
                    releasedProcedure))
            {
                AbandonBlockedRoomTourWithParkedCart(
                    porter,
                    nurse,
                    job);
                return;
            }

            if (job.CollectedProcedures.Count <= 0)
            {
                CancelJob(
                    porter,
                    nurse,
                    job,
                    revertSample: false);
                return;
            }

            if (job.Cart == null)
            {
                job.Phase =
                    PorterSampleTransportPhase.WaitingForFridge;
                TryGoToFridge(
                    porter,
                    nurse,
                    job);
                return;
            }

            if (job.Cart.m_state.m_moving &&
                job.Cart.IsAttachedToCharacter() &&
                job.Cart.User == porter)
            {
                if (TryContinueSampleTour(
                        porter,
                        nurse,
                        job))
                {
                    return;
                }

                job.Phase =
                    PorterSampleTransportPhase.WaitingForFridge;
                TryGoToFridge(
                    porter,
                    nurse,
                    job);
                return;
            }

            if (!job.Cart.m_state.m_moving)
            {
                if (!PorterSampleCartRuntime.TryWalkToParkedCart(
                        porter,
                        job))
                {
                    CancelJob(
                        porter,
                        nurse,
                        job,
                        revertSample: true);
                    return;
                }

                job.Phase =
                    PorterSampleTransportPhase.ReturningToCartAfterSample;
                return;
            }

            CancelJob(
                porter,
                nurse,
                job,
                revertSample: true);
        }

        private static bool RecoverParkedCartBeforeSampleRoomWait(
            Entity porter,
            WalkComponent walk,
            PorterSampleTransportJob job,
            Room sourceRoom)
        {
            TileObject cart =
                job == null
                    ? null
                    : job.Cart;
            if (porter == null ||
                walk == null ||
                job == null ||
                sourceRoom == null ||
                cart == null ||
                cart.m_state == null ||
                cart.m_state.m_moving ||
                !IsCartParkedInsideProcedureRoom(
                    cart,
                    job.Procedure))
            {
                if (job != null)
                {
                    job.RecoveringParkedCartForSampleRoomWait =
                        false;
                }
                return false;
            }

            Vector2i current =
                walk.GetCurrentTile();
            Vector2i cartUseTile =
                ToTile(
                    cart.GetDefaultUsePosition());
            bool insideRoom =
                walk.GetFloorIndex() ==
                    sourceRoom.GetFloorIndex() &&
                sourceRoom.IsPositionInRoom(
                    current);

            if (!insideRoom &&
                !job.RecoveringParkedCartForSampleRoomWait &&
                current != cartUseTile)
            {
                return false;
            }

            job.RecoveringParkedCartForSampleRoomWait =
                true;
            ResetSampleRoomWaitTimeout(job);
            job.HasSampleRoomWaitTarget = false;

            if (walk.GetFloorIndex() ==
                    cart.GetFloorIndex() &&
                current == cartUseTile &&
                !walk.IsBusy())
            {
                if (PorterSampleCartRuntime.TryAttachCart(
                        porter,
                        cart,
                        job.CartHomeRoom))
                {
                    job.RecoveringParkedCartForSampleRoomWait =
                        false;

                    PorterDiagnostics.Log(
                        "sample room evacuation cart recovered: porter=" +
                        (porter.Name ?? string.Empty).Trim() +
                        "; cartPosition=" +
                        cart.m_state.m_position +
                        "; floor=" +
                        cart.GetFloorIndex() +
                        ".");
                    return false;
                }

                PorterDiagnostics.Log(
                    "sample room evacuation cart attach deferred: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; cartPosition=" +
                    cart.m_state.m_position +
                    "; cartUseTile=" +
                    cartUseTile +
                    "; floor=" +
                    cart.GetFloorIndex() +
                    ".");
                return true;
            }

            if (!walk.IsBusy() ||
                walk.GetDestinationTile() !=
                    cartUseTile)
            {
                walk.SetDestination(
                    cartUseTile,
                    cart.GetFloorIndex());

                PorterDiagnostics.Log(
                    "sample room evacuation returning to cart: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; current=" +
                    current +
                    "; cartPosition=" +
                    cart.m_state.m_position +
                    "; cartUseTile=" +
                    cartUseTile +
                    "; floor=" +
                    cart.GetFloorIndex() +
                    ".");
            }

            return true;
        }

        private static bool HandleBlockedSampleRoomApproach(
            Entity porter,
            BehaviorNurse nurse,
            WalkComponent walk,
            PorterSampleTransportJob job)
        {
            if (job == null ||
                walk == null)
            {
                return false;
            }

            bool blocked =
                IsSamplePickupRoomBlocked(
                    job.Procedure);
            if (!blocked)
            {
                HideSampleRoomWaitingBubble(porter, job);
                ResetSampleRoomWaitTimeout(job);
                job.RecoveringParkedCartForSampleRoomWait =
                    false;

                if (!job.HasSampleRoomWaitTarget)
                {
                    return false;
                }

                TileObject source =
                    job.Procedure == null ||
                    job.Procedure.m_state == null ||
                    job.Procedure.m_state.m_usedEquipment == null
                        ? null
                        : job.Procedure.m_state
                            .m_usedEquipment.GetEntity();
                if (source == null)
                {
                    CancelJob(
                        porter,
                        nurse,
                        job,
                        revertSample: false);
                    return true;
                }

                // Stay on the valid staging tile until the sample equipment
                // reservation is really ours again. Starting a route first and
                // stopping it on the next tick caused a visible walk-in-place
                // pause before route recovery restarted the approach.
                if (!EnsureSampleSourceReserved(
                        porter,
                        job,
                        source))
                {
                    return true;
                }

                job.HasSampleRoomWaitTarget = false;

                TileObject cart =
                    job.Cart;
                if (cart != null &&
                    cart.m_state != null &&
                    cart.m_state.m_moving &&
                    cart.IsAttachedToCharacter() &&
                    cart.User == porter)
                {
                    Vector2i parkingTile;
                    if (!PorterSampleCartRuntime.TryGetInteractionParkingTile(
                            porter,
                            job,
                            source,
                            out parkingTile))
                    {
                        CancelJob(
                            porter,
                            nurse,
                            job,
                            revertSample: false);
                        return true;
                    }

                    SetSampleApproachTarget(
                        job,
                        parkingTile,
                        source.GetFloorIndex());
                    job.Phase =
                        PorterSampleTransportPhase.GoingToSampleParking;
                    walk.SetDestination(
                        parkingTile,
                        source.GetFloorIndex());

                    PorterDiagnostics.Log(
                        "sample room approach resumed with cart: porter=" +
                        (porter.Name ?? string.Empty).Trim() +
                        "; parkingTarget=" +
                        parkingTile +
                        "; floor=" +
                        source.GetFloorIndex() +
                        ".");
                    return true;
                }

                if (job.HasSampleApproachTarget)
                {
                    walk.SetDestination(
                        job.SampleApproachTarget,
                        job.SampleApproachFloorIndex);

                    PorterDiagnostics.Log(
                        "sample room approach resumed: porter=" +
                        (porter.Name ?? string.Empty).Trim() +
                        "; target=" +
                        job.SampleApproachTarget +
                        "; floor=" +
                        job.SampleApproachFloorIndex +
                        ".");
                    return true;
                }

                return false;
            }

            ReleaseSampleSourceReservation(
                porter,
                job);
            ClearCartParkingTarget(job);

            Room sourceRoom =
                GetSampleSourceRoom(job.Procedure);
            if (sourceRoom == null)
            {
                HideSampleRoomWaitingBubble(porter, job);
                if (walk.IsBusy())
                {
                    walk.Stop();
                }

                if (ShouldReleaseBlockedSample(
                        porter, job,
                        allowOutsideRoomRetry: false,
                        awaitingWaitTarget:
                            job.SampleRoomWaitDeadlineMinute <= 0))
                {
                    ReleaseBlockedSampleAndContinue(
                        porter, nurse, walk, job);
                }
                return true;
            }

            // The normal room-wait countdown begins only after reaching a valid
            // wait tile and completing movement. Staging has its own bounded
            // rescue clock, including cross-floor movement.

            if (RecoverParkedCartBeforeSampleRoomWait(
                    porter,
                    walk,
                    job,
                    sourceRoom))
            {
                return true;
            }

            Vector2i current =
                walk.GetCurrentTile();
            // Preserve an already-running wait deadline if the tile is
            // invalidated, changed, or restored from a saved game.
            if (job.SampleRoomWaitDeadlineMinute > 0 &&
                ShouldReleaseBlockedSample(
                    porter, job,
                    allowOutsideRoomRetry: true,
                    awaitingWaitTarget: false))
            {
                ReleaseBlockedSampleAndContinue(
                    porter, nurse, walk, job);
                return true;
            }

            Floor floor =
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                sourceRoom.GetFloorIndex() < 0 ||
                sourceRoom.GetFloorIndex() >=
                    Hospital.Instance.m_floors.Count
                    ? null
                    : Hospital.Instance.m_floors[
                        sourceRoom.GetFloorIndex()];

            if (job.HasSampleRoomWaitTarget &&
                (job.SampleRoomWaitFloorIndex !=
                    sourceRoom.GetFloorIndex() ||
                 floor == null ||
                 !IsValidSampleRoomWaitTile(
                     porter,
                     floor,
                     sourceRoom,
                     job.SampleRoomWaitTarget) ||
                 !CanStageAttachedSampleCartAtWaitTile(
                     porter,
                     job,
                     floor,
                     sourceRoom,
                     job.SampleRoomWaitTarget)))
            {
                Vector2i invalidTarget =
                    job.SampleRoomWaitTarget;
                if (walk.IsBusy())
                {
                    walk.Stop();
                }
                job.HasSampleRoomWaitTarget = false;
                job.LoggedSampleRoomWaitArrival = false;
                HideSampleRoomWaitingBubble(porter, job);

                PorterDiagnostics.Log(
                    "sample room wait target invalidated: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; current=" +
                    current +
                    "; previousTarget=" +
                    invalidTarget +
                    "; floor=" +
                    walk.GetFloorIndex() +
                    ".");
            }

            if (!job.HasSampleRoomWaitTarget)
            {
                Vector2i waitTile;
                Vector2i doorwayAnchor;
                bool usedDoorFallback;
                if (!TryFindSampleRoomWaitTile(
                        porter,
                        job,
                        sourceRoom,
                        out waitTile,
                        out doorwayAnchor,
                        out usedDoorFallback))
                {
                    if (walk.IsBusy())
                    {
                        walk.Stop();
                    }

                    PorterDiagnostics.Log(
                        "sample room evacuation unavailable: porter=" +
                        (porter.Name ?? string.Empty).Trim() +
                        "; current=" +
                        walk.GetCurrentTile() +
                        "; floor=" +
                        walk.GetFloorIndex() +
                        "; fallback=bounded-room-timeout.");
                    // Even if no safe tile is found (including across
                    // floors), never strand the assigned Porter forever.
                    if (ShouldReleaseBlockedSample(
                            porter, job,
                            allowOutsideRoomRetry: true,
                            awaitingWaitTarget:
                                job.SampleRoomWaitDeadlineMinute <= 0))
                    {
                        ReleaseBlockedSampleAndContinue(
                            porter, nurse, walk, job);
                    }
                    return true;
                }

                job.SampleRoomWaitTarget =
                    waitTile;
                job.SampleRoomWaitFloorIndex =
                    sourceRoom.GetFloorIndex();
                job.HasSampleRoomWaitTarget =
                    true;
                job.LoggedSampleRoomWaitArrival = false;

                PorterDiagnostics.Log(
                    "sample room wait target: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; current=" +
                    walk.GetCurrentTile() +
                    "; doorwayAnchor=" +
                    doorwayAnchor +
                    "; waitTarget=" +
                    waitTile +
                    "; floor=" +
                    sourceRoom.GetFloorIndex() +
                    "; doorFallback=" +
                    usedDoorFallback +
                    ".");
            }

            if (current ==
                    job.SampleRoomWaitTarget &&
                walk.GetFloorIndex() ==
                    job.SampleRoomWaitFloorIndex)
            {
                // Let WalkComponent finish its route natively. Calling Stop()
                // here switches to Idle without playing stand_idle, which can
                // leave the Porter visually walking in place.
                if (!walk.IsBusy())
                {
                    if (!EnsureSampleRoomWaitCartFootprint(
                            porter,
                            job,
                            floor,
                            sourceRoom,
                            current))
                    {
                        job.HasSampleRoomWaitTarget = false;
                        job.LoggedSampleRoomWaitArrival = false;
                        HideSampleRoomWaitingBubble(porter, job);
                        return true;
                    }

                    // Stage complete: only now start the full waiting window.
                    job.SampleRoomStagingDeadlineMinute = 0;
                    if (job.SampleRoomWaitDeadlineMinute <= 0 &&
                        ShouldReleaseBlockedSample(
                            porter, job,
                            allowOutsideRoomRetry: true,
                            awaitingWaitTarget: false))
                    {
                        ReleaseBlockedSampleAndContinue(
                            porter, nurse, walk, job);
                        return true;
                    }

                    if (!job.LoggedSampleRoomWaitArrival)
                    {
                        job.LoggedSampleRoomWaitArrival = true;
                        PorterDiagnostics.Log(
                            "sample room wait reached: porter=" +
                            (porter.Name ?? string.Empty).Trim() +
                            "; patient=" +
                            GetPatientName(job.Procedure) +
                            "; procedureId=" +
                            (job.Procedure == null
                                ? "NONE"
                                : job.Procedure.GetEntityID().ToString()) +
                            "; position=" + current +
                            "; floor=" + walk.GetFloorIndex() +
                            "; deadlineMinute=" +
                            job.SampleRoomWaitDeadlineMinute +
                            "; cartAttached=" +
                            (job.Cart != null &&
                             job.Cart.IsAttachedToCharacter()) +
                            ".");
                    }
                    ShowSampleRoomWaitingBubble(porter, job);
                }
                else if (job.SampleRoomWaitDeadlineMinute <= 0 &&
                    ShouldReleaseBlockedSample(
                        porter, job,
                        allowOutsideRoomRetry: true,
                        awaitingWaitTarget: true))
                {
                    // A route can still report busy on its destination tile.
                    ReleaseBlockedSampleAndContinue(
                        porter, nurse, walk, job);
                }
                return true;
            }

            HideSampleRoomWaitingBubble(porter, job);

            // A route that never reaches the wait tile must not hold a
            // sample forever; this timeout is NOT the outside-room wait.
            if (job.SampleRoomWaitDeadlineMinute <= 0 &&
                ShouldReleaseBlockedSample(
                    porter, job,
                    allowOutsideRoomRetry: true,
                    awaitingWaitTarget: true))
            {
                ReleaseBlockedSampleAndContinue(
                    porter, nurse, walk, job);
                return true;
            }

            if (!walk.IsBusy() ||
                walk.GetDestinationTile() !=
                    job.SampleRoomWaitTarget ||
                walk.m_state.m_destinationFloor !=
                    job.SampleRoomWaitFloorIndex)
            {
                walk.SetDestination(
                    job.SampleRoomWaitTarget,
                    job.SampleRoomWaitFloorIndex);

                string action =
                    walk.GetFloorIndex() == sourceRoom.GetFloorIndex() &&
                    sourceRoom.IsPositionInRoom(current)
                        ? "sample room evacuation"
                        : "sample room approach staged outside";
                PorterDiagnostics.Log(
                    action +
                    ": porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; current=" +
                    current +
                    "; waitTarget=" +
                    job.SampleRoomWaitTarget +
                    "; floor=" +
                    job.SampleRoomWaitFloorIndex +
                    "; cartAttached=" +
                    (job.Cart != null &&
                     job.Cart.m_state != null &&
                     job.Cart.m_state.m_moving &&
                     job.Cart.IsAttachedToCharacter()) +
                    ".");
            }

            return true;
        }

        private static bool IsSamplePickupRoomBlocked(
            LabProcedure procedure)
        {
            return IsSamplePickupRoomBlocked(
                procedure,
                GetSampleSourceRoom(procedure));
        }

        private static bool IsSamplePickupRoomBlocked(
            LabProcedure procedure,
            Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null ||
                room.m_roomPersistentData.m_roomType.Entry == null)
            {
                ClearPreBlockedRoomWaitDecision(procedure);
                UpdateSamplePickupRoomDiagnostic(
                    procedure,
                    room,
                    blocked: false,
                    reason: "no-room");
                return false;
            }

            GameDBRoomType roomType =
                room.m_roomPersistentData.m_roomType.Entry;

            if (roomType.HasTag("hospitalization"))
            {
                ClearPreBlockedRoomWaitDecision(procedure);
                UpdateSamplePickupRoomDiagnostic(
                    procedure,
                    room,
                    blocked: false,
                    reason: "hospitalization-room");
                return false;
            }

            Entity procedureOwner =
                room.m_roomPersistentData.m_currentProcedureOwner == null
                    ? null
                    : room.m_roomPersistentData
                        .m_currentProcedureOwner.GetEntity();
            Entity reservedBy =
                room.m_roomPersistentData.m_reservedByCharacter == null
                    ? null
                    : room.m_roomPersistentData
                        .m_reservedByCharacter.GetEntity();
            if (procedureOwner != null ||
                reservedBy is ProcedureScript)
            {
                UpdateSamplePickupRoomDiagnostic(
                    procedure,
                    room,
                    blocked: true,
                    reason: "procedure-room-reserved");
                return true;
            }

            // Room's native lying-patient pointer is a cheap positive
            // signal. It is NOT a complete room-occupancy index: a standing
            // patient may be inside without setting this pointer.
            Entity lyingPatient =
                room.m_roomPersistentData.m_currentLyingPatient == null
                    ? null
                    : room.m_roomPersistentData
                        .m_currentLyingPatient.GetEntity();
            // Room.Update can clear/reassign this pointer. It is only
            // proof of occupancy while the patient is STILL physically here.
            if (lyingPatient != null &&
                lyingPatient.GetComponent<BehaviorPatient>() != null &&
                IsEntityPhysicallyInsideRoom(lyingPatient, room))
            {
                UpdateSamplePickupRoomDiagnostic(
                    procedure,
                    room,
                    blocked: true,
                    reason: "lying-patient");
                return true;
            }

            // A procedure reservation already establishes the room block.
            // Only search patients if the native room fields were inconclusive.
            Entity patientInside =
                FindPatientInsideRoom(room);
            if (patientInside != null)
            {
                UpdateSamplePickupRoomDiagnostic(
                    procedure,
                    room,
                    blocked: true,
                    reason: "patient-inside");
                return true;
            }

            ClearPreBlockedRoomWaitDecision(procedure);

            UpdateSamplePickupRoomDiagnostic(
                procedure,
                room,
                blocked: false,
                reason: "room-free");
            return false;
        }

        private static bool IsEntityPhysicallyInsideRoom(
            Entity entity,
            Room room)
        {
            if (entity == null || room == null)
            {
                return false;
            }

            WalkComponent walk =
                entity.GetComponent<WalkComponent>();
            return walk != null &&
                walk.GetFloorIndex() == room.GetFloorIndex() &&
                room.IsPositionInRoom(
                    walk.GetCurrentTile());
        }

        private static Entity FindPatientInsideRoom(
            Room room)
        {
            if (room == null || room.m_roomPersistentData == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return null;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Grid spatialGrid = floor == null ? null : floor.m_grid;
            if (spatialGrid == null ||
                spatialGrid.m_objects == null)
            {
                // The native spatial index may be absent during load.
                // Retain the previous exhaustive check ONLY in that case.
                return FindPatientInsideRoomBeforeGridReady(room);
            }

            // The game updates this spatial index in
            // Hospital.UpdateCharacters(). Query covered room tiles, not
            // every patient in every department. Never cache this result.
            Vector2i bottom =
                room.m_roomPersistentData.m_positionBottom;
            Vector2i top =
                room.m_roomPersistentData.m_positionTop;
            for (int x = Math.Max(0, bottom.m_x);
                 x <= Math.Min(floor.Size.m_x - 1, top.m_x);
                 x++)
            {
                for (int y = Math.Max(0, bottom.m_y);
                     y <= Math.Min(floor.Size.m_y - 1, top.m_y);
                     y++)
                {
                    // The native grid has an empty bucket on most tiles.
                    // Avoid position construction and room-shape checks for
                    // those buckets, retaining exact irregular-room bounds.
                    List<GridObject> occupants =
                        spatialGrid.m_objects[x, y];
                    if (occupants == null || occupants.Count == 0 ||
                        !room.IsPositionInRoom(new Vector2i(x, y)))
                    {
                        continue;
                    }

                    foreach (GridObject occupant in occupants)
                    {
                        GridComponent gridComponent =
                            occupant as GridComponent;
                        Entity entity = gridComponent == null
                            ? null : gridComponent.m_entity;
                        if (entity != null &&
                            entity.GetComponent<BehaviorPatient>() != null &&
                            IsEntityPhysicallyInsideRoom(entity, room))
                        {
                            return entity;
                        }
                    }
                }
            }

            return null;
        }

        private static Entity FindPatientInsideRoomBeforeGridReady(
            Room room)
        {
            if (Hospital.Instance == null ||
                Hospital.Instance.m_departments == null)
            {
                return null;
            }

            foreach (Department department in
                Hospital.Instance.m_departments)
            {
                if (department == null ||
                    department.m_departmentPersistentData == null ||
                    department.m_departmentPersistentData.m_patients == null)
                {
                    continue;
                }

                foreach (EntityIDPointer<Entity> pointer in
                    department.m_departmentPersistentData.m_patients)
                {
                    Entity patient = pointer == null
                        ? null : pointer.GetEntity();
                    if (patient != null &&
                        patient.GetComponent<BehaviorPatient>() != null &&
                        IsEntityPhysicallyInsideRoom(patient, room))
                    {
                        return patient;
                    }
                }
            }

            return null;
        }

        private static void UpdateSamplePickupRoomDiagnostic(
            LabProcedure procedure,
            Room room,
            bool blocked,
            string reason)
        {
            if (procedure == null)
            {
                return;
            }

            bool wasBlocked =
                BlockedSamplePickupRooms.Contains(procedure);
            if (blocked)
            {
                if (wasBlocked)
                {
                    return;
                }

                BlockedSamplePickupRooms.Add(procedure);
            }
            else
            {
                if (!wasBlocked)
                {
                    return;
                }

                BlockedSamplePickupRooms.Remove(procedure);
                LoggedPreAssignmentRoomDeferrals.Remove(procedure);
            }

            // Diagnostic state is updated above, but heavy enrichment is opt-in.
            if (!PorterTransportConfig.EnableDiagnostics)
            {
                return;
            }

            Entity patient =
                procedure.m_state == null ||
                procedure.m_state.m_patient == null
                    ? null
                    : procedure.m_state.m_patient.GetEntity();
            TileObject source =
                procedure.m_state == null ||
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment.GetEntity();
            GameDBRoomType roomType =
                room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null
                    ? null
                    : room.m_roomPersistentData.m_roomType.Entry;

            Entity patientInside =
                FindPatientInsideRoom(room);

            PorterDiagnostics.Log(
                "sample room " +
                (blocked ? "blocked" : "released") +
                ": samplePatient=" +
                (patient == null
                    ? "UNKNOWN"
                    : (patient.Name ?? string.Empty).Trim()) +
                "; patientInside=" +
                (patientInside == null
                    ? "NONE"
                    : (patientInside.Name ?? string.Empty).Trim()) +
                "; roomType=" +
                (roomType == null
                    ? "NONE"
                    : roomType.DatabaseID.ToString()) +
                "; floor=" +
                (room == null
                    ? -1
                    : room.GetFloorIndex()) +
                "; sourcePosition=" +
                (source == null
                    ? "NONE"
                    : source.m_state.m_position.ToString()) +
                "; sourceFloor=" +
                (source == null
                    ? -1
                    : source.GetFloorIndex()) +
                "; reason=" +
                reason +
                "; pickupStage=" +
                (IsProcedureClaimed(procedure)
                    ? "mission-already-assigned"
                    : "before-Porter-assignment") +
                "; sampleHospitalized=" +
                (patient != null &&
                 patient.GetComponent<HospitalizationComponent>() != null &&
                 patient.GetComponent<HospitalizationComponent>().IsHospitalized()) +
                "; procedureState=" +
                (procedure.m_state == null
                    ? "UNKNOWN"
                    : procedure.m_state.m_labProcedureState.ToString()) +
                ".");
        }

        private static Vector2i ToTile(
            Vector2f position)
        {
            return new Vector2i(
                (int)(position.m_x + 0.5f),
                (int)(position.m_y + 0.5f));
        }

        private static bool EnsureReachedDestination(
            Entity porter,
            WalkComponent walk,
            PorterSampleTransportJob job,
            Vector2i expectedTile,
            int expectedFloor,
            string stage)
        {
            if (walk == null || walk.IsBusy())
            {
                return false;
            }

            Vector2i current =
                walk.GetCurrentTile();
            Vector2i destination =
                walk.GetDestinationTile();
            bool reached =
                walk.GetFloorIndex() == expectedFloor &&
                current == expectedTile;

            if (reached)
            {
                job.RouteRecoveryAttempts = 0;
                return true;
            }

            job.RouteRecoveryAttempts++;
            TileObject cart =
                job == null
                    ? null
                    : job.Cart;
            TileObject seat =
                walk.m_state == null ||
                walk.m_state.m_objectSittingOn == null
                    ? null
                    : walk.m_state.m_objectSittingOn.GetEntity();
            AnimModelComponent animation =
                porter == null
                    ? null
                    : porter.GetComponent<AnimModelComponent>();

            PorterDiagnostics.Log(
                "sample route mismatch: porter=" +
                (porter == null
                    ? "UNKNOWN"
                    : (porter.Name ?? string.Empty).Trim()) +
                "; phase=" +
                (job == null
                    ? "NONE"
                    : job.Phase.ToString()) +
                "; stage=" +
                stage +
                "; current=" +
                current +
                "; destination=" +
                destination +
                "; expected=" +
                expectedTile +
                "; currentFloor=" +
                walk.GetFloorIndex() +
                "; expectedFloor=" +
                expectedFloor +
                "; walkState=" +
                (walk.m_state == null
                    ? "UNKNOWN"
                    : walk.m_state.m_walkState.ToString()) +
                "; seated=" +
                (seat != null) +
                "; seatPosition=" +
                (seat == null
                    ? "NONE"
                    : seat.m_state.m_position.ToString()) +
                "; seatUseTile=" +
                (seat == null
                    ? "NONE"
                    : ToTile(
                        seat.GetDefaultUsePosition()).ToString()) +
                "; animation=" +
                (animation == null ||
                 animation.m_state == null
                    ? "NONE"
                    : animation.m_state.m_currentAnimationID) +
                "; cartPosition=" +
                (cart == null
                    ? "NONE"
                    : cart.m_state.m_position.ToString()) +
                "; cartFloor=" +
                (cart == null
                    ? -1
                    : cart.GetFloorIndex()) +
                "; cartMoving=" +
                (cart != null && cart.m_state.m_moving) +
                "; cartAttached=" +
                (cart != null && cart.IsAttachedToCharacter()) +
                "; retry=" +
                job.RouteRecoveryAttempts +
                ".");

            if (job.RouteRecoveryAttempts < 3)
            {
                walk.SetDestination(
                    expectedTile,
                    expectedFloor);
            }

            return false;
        }

        private static Department GetPatientDepartment(
            LabProcedure procedure)
        {
            Entity patient =
                procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_patient == null
                    ? null
                    : procedure.m_state.m_patient.GetEntity();
            return patient == null
                ? null
                : patient.GetComponent<BehaviorPatient>()?.GetDepartment();
        }

        private static Department GetProcedureDepartment(
            LabProcedure procedure)
        {
            Room sourceRoom =
                GetSampleSourceRoom(procedure);
            Department sourceDepartment =
                sourceRoom == null ||
                sourceRoom.m_roomPersistentData == null ||
                sourceRoom.m_roomPersistentData.m_department == null
                    ? null
                    : sourceRoom.m_roomPersistentData.m_department.GetEntity();

            return sourceDepartment ??
                GetPatientDepartment(procedure);
        }

        private static bool TryContinueSampleTour(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            if (porter == null ||
                nurse == null ||
                job == null ||
                job.StatLab == null)
            {
                return false;
            }

            int collectedBundles =
                CountCollectedSampleBundles(job);
            int collectedProcedures =
                job.CollectedProcedures == null
                    ? 0
                    : job.CollectedProcedures.Count;
            int maxBundles =
                PorterTransportConfig.SampleTourMaxSamples;

            if (collectedBundles >= maxBundles)
            {
                PorterDiagnostics.Log(
                    "sample tour stop: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; reason=bundle-limit" +
                    "; bundles=" +
                    collectedBundles +
                    "; procedures=" +
                    collectedProcedures +
                    "; maxBundles=" +
                    maxBundles +
                    ".");
                return false;
            }

            LabProcedure next;
            float routeDistance;
            if (!TryFindNextTourProcedure(
                    porter,
                    job,
                    out next,
                    out routeDistance))
            {
                PorterDiagnostics.Log(
                    "sample tour stop: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; reason=no-compatible-candidate" +
                    "; bundles=" +
                    collectedBundles +
                    "; procedures=" +
                    collectedProcedures +
                    "; maxBundles=" +
                    maxBundles +
                    ".");
                return false;
            }

            TileObject source =
                next.m_state.m_usedEquipment.GetEntity();
            UseComponent use =
                porter.GetComponent<UseComponent>();
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (source == null ||
                use == null ||
                walk == null)
            {
                return false;
            }

            use.ReserveObject(source);
            if (source.User != porter)
            {
                return false;
            }

            Vector2i parkingTile;
            if (!PorterSampleCartRuntime.TryGetInteractionParkingTile(
                    porter,
                    job,
                    source,
                    out parkingTile))
            {
                use.Interrupt();
                return false;
            }

            job.Procedure = next;
            job.Phase =
                PorterSampleTransportPhase.GoingToSampleParking;
            SetSampleApproachTarget(
                job,
                parkingTile,
                source.GetFloorIndex());

            ClearFallbackTracking(next);
            ShowSampleBubble(porter, next);
            walk.SetDestination(
                parkingTile,
                source.GetFloorIndex());

            PorterDiagnostics.Log(
                "sample tour continue: porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; bundlesBeforePickup=" +
                collectedBundles +
                "; proceduresBeforePickup=" +
                collectedProcedures +
                "; nextPatient=" +
                GetPatientName(next) +
                "; nextProcedureId=" +
                next.GetEntityID() +
                "; routeDistance=" +
                routeDistance.ToString("0.0") +
                "; maxBundles=" +
                maxBundles +
                ".");

            return true;
        }

        private static bool TryFindNextTourProcedure(
            Entity porter,
            PorterSampleTransportJob job,
            out LabProcedure best,
            out float bestRouteDistance)
        {
            best = null;
            bestRouteDistance = float.MaxValue;

            EmployeeComponent employee =
                porter == null
                    ? null
                    : porter.GetComponent<EmployeeComponent>();
            Department homeDepartment =
                employee == null
                    ? null
                    : employee.m_state.m_department.GetEntity();
            List<LabProcedure> procedures =
                LabProcedureManager.Instance == null
                    ? null
                    : LabProcedureManager.Instance.m_labProcedures;
            if (homeDepartment == null ||
                procedures == null ||
                job == null ||
                job.StatLab == null)
            {
                return false;
            }

            // One synchronous tour selection: the Porter, destination lab
            // and fridge routes are the same for all candidate samples.
            // NaN means not calculated; no state survives this selection.
            //
            // Once the first physical pickup is complete, do not make another
            // foreign-help roll. Preserve local work first, then only continue
            // helping the department of the FIRST collected bundle. A tour
            // that started locally therefore remains local and goes to the lab
            // when no more compatible local pickup exists.
            float directLabDistance = float.NaN;
            if (TryFindNextTourProcedure(
                    porter,
                    job,
                    procedures,
                    homeDepartment,
                    ref directLabDistance,
                    out best,
                    out bestRouteDistance))
            {
                return true;
            }

            Department assistedDepartment =
                GetInitialTourAssistedDepartment(
                    job, homeDepartment);
            if (assistedDepartment == null)
            {
                return false;
            }

            return TryFindNextTourProcedure(
                porter,
                job,
                procedures,
                assistedDepartment,
                ref directLabDistance,
                out best,
                out bestRouteDistance);
        }

        private static Department GetInitialTourAssistedDepartment(
            PorterSampleTransportJob job,
            Department homeDepartment)
        {
            if (job == null || homeDepartment == null)
            {
                return null;
            }

            // CollectedProcedures is appended in pickup order and that order
            // is preserved by the public V2 save payload. The first surviving
            // procedure therefore identifies the first physical bundle after
            // save/load without adding a new persistence field.
            for (int i = 0;
                i < job.CollectedProcedures.Count;
                i++)
            {
                LabProcedure collected =
                    job.CollectedProcedures[i];
                Department department =
                    GetProcedureDepartment(collected);
                if (department != null)
                {
                    return department == homeDepartment
                        ? null
                        : department;
                }
            }

            // Safety for a transient phase immediately around first pickup.
            Department currentDepartment =
                GetProcedureDepartment(job.Procedure);
            return currentDepartment == null ||
                   currentDepartment == homeDepartment
                ? null
                : currentDepartment;
        }

        private static bool TryFindNextTourProcedure(
            Entity porter,
            PorterSampleTransportJob job,
            List<LabProcedure> procedures,
            Department requiredDepartment,
            ref float directLabDistance,
            out LabProcedure best,
            out float bestRouteDistance)
        {
            best = null;
            bestRouteDistance = float.MaxValue;

            for (int i = 0; i < procedures.Count; i++)
            {
                LabProcedure procedure = procedures[i];
                // Most manager entries cannot join a sample tour. Check
                // native state BEFORE room and department lookups.
                if (procedure == null || procedure.m_state == null ||
                    procedure.m_state.m_labProcedureState !=
                        LabProcedureState.SamplingFinished)
                {
                    continue;
                }

                Department procedureDepartment =
                    GetProcedureDepartment(procedure);
                if (procedureDepartment == null)
                {
                    continue;
                }

                if (procedureDepartment != requiredDepartment)
                {
                    continue;
                }

                float routeDistance;
                if (!CanAddProcedureToTour(
                        porter,
                        job,
                        procedure,
                        ref directLabDistance,
                        out routeDistance))
                {
                    continue;
                }

                if (routeDistance < bestRouteDistance)
                {
                    bestRouteDistance = routeDistance;
                    best = procedure;
                }
            }

            return best != null;
        }

        private static bool CanAddProcedureToTour(
            Entity porter,
            PorterSampleTransportJob job,
            LabProcedure procedure,
            ref float directLabDistance,
            out float routeDistance)
        {
            routeDistance = float.MaxValue;

            if (porter == null ||
                job == null ||
                procedure == null ||
                procedure == job.Procedure ||
                IsProcedureClaimed(procedure) ||
                IsRoomWaitRetryCoolingDown(procedure) ||
                !IsHospitalizedSampleWaiting(procedure))
            {
                return false;
            }

            if (procedure.m_state.m_statLab == null ||
                procedure.m_state.m_statLab.GetEntity() == null)
            {
                procedure.FindRoom(onlyFree: false);
            }

            Room statLab =
                procedure.m_state.m_statLab == null
                    ? null
                    : procedure.m_state.m_statLab.GetEntity();
            if (statLab == null ||
                statLab != job.StatLab)
            {
                return false;
            }

            TileObject source =
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment.GetEntity();
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (source == null ||
                source.User != null ||
                source.Owner != null ||
                IsSamplePickupRoomBlocked(procedure) ||
                walk == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            // After the first pickup, only extend the tour on the floor
            // the Porter is currently working on. The configured search distance
            // and detour guard apply from this point onward; the lab itself may
            // still be on any reachable floor.
            if (source.GetFloorIndex() != walk.GetFloorIndex())
            {
                return false;
            }

            float directToLab;
            float routeViaSample;
            float distanceToSample;
            if (!TryGetTourRouteDistances(
                    porter,
                    source,
                    job.StatLab,
                    ref directLabDistance,
                    out directToLab,
                    out routeViaSample,
                    out distanceToSample))
            {
                return false;
            }

            if (distanceToSample >
                PorterTransportConfig.SampleTourSearchDistance)
            {
                return false;
            }

            float detourAllowance =
                Math.Max(8f, directToLab * 0.5f);
            if (routeViaSample >
                directToLab + detourAllowance)
            {
                return false;
            }

            routeDistance = routeViaSample;
            return true;
        }

        private static bool TryGetTourRouteDistances(
            Entity porter,
            TileObject source,
            Room statLab,
            ref float directLabDistance,
            out float directToLab,
            out float routeViaSample,
            out float distanceToSample)
        {
            directToLab = float.MaxValue;
            routeViaSample = float.MaxValue;
            distanceToSample = float.MaxValue;

            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            Department labDepartment =
                statLab == null ||
                statLab.m_roomPersistentData.m_department == null
                    ? null
                    : statLab.m_roomPersistentData.m_department.GetEntity();
            if (walk == null ||
                source == null ||
                labDepartment == null ||
                labDepartment.m_departmentPersistentData.m_objects == null ||
                MapScriptInterface.Instance == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            // A tour already has an explicit maximum pickup distance.
            // Cheap geometric lower bounds can reject impossible candidates
            // BEFORE running graph distance calculations. No cutoff is added
            // to normal patient or sample assignments.
            int floorDifference =
                Math.Abs(walk.GetFloorIndex() - source.GetFloorIndex());
            int maximumTourDistance =
                PorterTransportConfig.SampleTourSearchDistance;
            if (floorDifference * 100 > maximumTourDistance)
            {
                return false;
            }
            if (floorDifference == 0 &&
                Vector2f.distance(
                    new Vector2f(walk.GetCurrentTile()),
                    new Vector2f(source.m_state.m_position)) >
                    maximumTourDistance)
            {
                return false;
            }

            float toSource = GetRouteDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                source.GetFloorIndex(),
                source.m_state.m_position);
            if (toSource < 0f ||
                toSource > maximumTourDistance)
            {
                // The caller already rejects this candidate. Do not
                // calculate two paths per refrigerator before rejecting it.
                return false;
            }
            distanceToSample = toSource;

            // A direct Porter -> fridge route is independent of the
            // candidate sample. Calculate it only for the FIRST eligible
            // sample and reuse this local scalar across both passes.
            bool calculateDirect = float.IsNaN(directLabDistance);
            float bestDirect =
                calculateDirect ? float.MaxValue : directLabDistance;
            foreach (EntityIDPointer<TileObject> pointer in
                labDepartment.m_departmentPersistentData.m_objects)
            {
                TileObject fridge = pointer.GetEntity();
                if (fridge == null ||
                    !fridge.HasTag("medical_fridge") ||
                    !fridge.IsValid() ||
                    fridge.IsBroken() ||
                    MapScriptInterface.Instance.GetRoomAt(
                        fridge.m_state.m_position,
                        fridge.GetFloorIndex()) != statLab)
                {
                    continue;
                }

                Vector2i fridgePosition =
                    fridge.m_state.m_position;

                if (calculateDirect)
                {
                    float direct = GetRouteDistance(
                        walk.GetFloorIndex(),
                        walk.GetCurrentTile(),
                        fridge.GetFloorIndex(),
                        fridgePosition);
                    if (direct >= 0f && direct < bestDirect)
                    {
                        bestDirect = direct;
                    }
                }

                float sourceToLab = GetRouteDistance(
                    source.GetFloorIndex(),
                    source.m_state.m_position,
                    fridge.GetFloorIndex(),
                    fridgePosition);
                if (sourceToLab >= 0f &&
                    toSource + sourceToLab <
                        routeViaSample)
                {
                    routeViaSample =
                        toSource + sourceToLab;
                }
            }

            if (calculateDirect)
            {
                directLabDistance = bestDirect;
            }
            directToLab = directLabDistance;
            return
                directToLab < float.MaxValue &&
                routeViaSample < float.MaxValue;
        }

        private static float GetRouteDistance(
            int fromFloor,
            Vector2i from,
            int toFloor,
            Vector2i to)
        {
            float distance =
                GridMap.GetInstance().GetDistance(
                    fromFloor,
                    from,
                    toFloor,
                    to,
                    AccessRights.STAFF);
            if (distance < 0f)
            {
                return -1f;
            }

            return distance +
                Math.Abs(fromFloor - toFloor) * 100f;
        }

        private struct ForeignSampleHelpDecision
        {
            internal bool Accepted;
            internal int Chance;
            internal int Roll;
            internal int StationFloorBonus;
            internal int CurrentFloorBonus;
            internal int WaitingBonus;
            internal int DistancePenalty;
            internal int Personality;
            internal int MissionVariation;
        }

        private static bool CanForeignSampleHelpPassBestCase(
            Entity porter,
            LabProcedure procedure)
        {
            if (porter == null ||
                procedure == null ||
                procedure.m_state == null)
            {
                return false;
            }

            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            TileObject source =
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment.GetEntity();
            if (walk == null || source == null)
            {
                return false;
            }

            // Evaluate the maximum chance available under CURRENT
            // circumstances by using a perfect native route (zero distance
            // penalty) while retaining the existing +100-per-floor ranking
            // term. If the stable roll still fails, a real route can only
            // lower the chance, so room/path/local-work work can be skipped.
            float bestCaseRankedDistance =
                Math.Abs(
                    walk.GetFloorIndex() -
                    source.GetFloorIndex()) * 100f;
            return EvaluateForeignSampleHelp(
                porter,
                procedure,
                bestCaseRankedDistance).Accepted;
        }

        private static bool ShouldAcceptForeignSampleHelp(
            Entity porter,
            LabProcedure procedure,
            float rankedDistance)
        {
            return EvaluateForeignSampleHelp(
                porter,
                procedure,
                rankedDistance).Accepted;
        }

        private static ForeignSampleHelpDecision
            EvaluateForeignSampleHelp(
                Entity porter,
                LabProcedure procedure,
                float rankedDistance)
        {
            ForeignSampleHelpDecision decision =
                new ForeignSampleHelpDecision();

            if (!PorterTransportConfig.SampleCrossDepartmentHelp ||
                porter == null ||
                procedure == null ||
                procedure.m_state == null)
            {
                return decision;
            }

            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            TileObject source =
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment.GetEntity();
            if (walk == null || source == null)
            {
                return decision;
            }

            int chance =
                PorterTransportConfig.SampleCrossDepartmentHelpBaseChance;

            if (GetPorterZoneFloor(porter) ==
                source.GetFloorIndex())
            {
                decision.StationFloorBonus =
                    PorterTransportConfig
                        .SampleCrossDepartmentStationFloorBonus;
                chance += decision.StationFloorBonus;
            }

            if (walk.GetFloorIndex() == source.GetFloorIndex())
            {
                decision.CurrentFloorBonus =
                    PorterTransportConfig
                        .SampleCrossDepartmentCurrentFloorBonus;
                chance += decision.CurrentFloorBonus;
            }

            if (HasForeignSampleWaitingPressure(procedure))
            {
                decision.WaitingBonus =
                    PorterTransportConfig
                        .SampleCrossDepartmentWaitingBonus;
                chance += decision.WaitingBonus;
            }

            // The ranking distance already contains HPO's existing +100 per
            // floor. Do not charge that artificial ranking penalty again in
            // the human willingness score: use only the native STAFF route.
            float nativeRouteDistance =
                rankedDistance -
                Math.Abs(
                    walk.GetFloorIndex() -
                    source.GetFloorIndex()) * 100f;
            if (nativeRouteDistance < 0f)
            {
                nativeRouteDistance = 0f;
            }

            decision.DistancePenalty =
                (int)(nativeRouteDistance /
                    ForeignSampleHelpDistancePenaltyDivisor);
            if (decision.DistancePenalty >
                ForeignSampleHelpDistancePenaltyCap)
            {
                decision.DistancePenalty =
                    ForeignSampleHelpDistancePenaltyCap;
            }
            chance -= decision.DistancePenalty;

            uint bundleHash =
                GetPhysicalSampleBundleStableHash(procedure);
            decision.Personality =
                GetStableSignedOffset(
                    porter.GetEntityID(),
                    ForeignSampleHelpPersonalitySalt,
                    ForeignSampleHelpPersonalityRange);
            decision.MissionVariation =
                GetStableSignedOffset(
                    bundleHash,
                    ForeignSampleHelpMissionSalt,
                    ForeignSampleHelpMissionVariationRange);
            chance += decision.Personality;
            chance += decision.MissionVariation;

            if (chance < ForeignSampleHelpMinimumChance)
            {
                chance = ForeignSampleHelpMinimumChance;
            }
            else if (chance > ForeignSampleHelpMaximumChance)
            {
                chance = ForeignSampleHelpMaximumChance;
            }

            decision.Chance = chance;
            decision.Roll =
                GetStableForeignHelpRoll(
                    porter.GetEntityID(),
                    bundleHash);
            decision.Accepted =
                decision.Roll < decision.Chance;
            return decision;
        }

        private static bool HasForeignSampleWaitingPressure(
            LabProcedure procedure)
        {
            if (procedure == null)
            {
                return false;
            }

            // A second occupied-room Porter attempt is already a clearly
            // delayed physical sample and deserves the same pressure bonus.
            if (HasRoomWaitRetryForBundle(procedure))
            {
                return true;
            }

            PorterSampleFallbackWait wait;
            return TryGetFallbackWaitForBundle(
                    procedure,
                    out wait) &&
                wait != null &&
                wait.RetryCount > 0;
        }

        private static uint GetPhysicalSampleBundleStableHash(
            LabProcedure procedure)
        {
            uint hash = 2166136261u;
            if (procedure == null || procedure.m_state == null)
            {
                return hash;
            }

            Entity patient =
                procedure.m_state.m_patient == null
                    ? null
                    : procedure.m_state.m_patient.GetEntity();
            Room sourceRoom =
                GetSampleSourceRoom(procedure);
            TileObject source =
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment.GetEntity();

            hash = StableHashStep(
                hash,
                patient == null
                    ? 0u
                    : patient.GetEntityID());
            hash = StableHashStep(
                hash,
                sourceRoom != null
                    ? sourceRoom.GetEntityID()
                    : source == null
                        ? 0u
                        : source.GetEntityID());
            return hash;
        }

        private static int GetStableForeignHelpRoll(
            uint porterId,
            uint bundleHash)
        {
            uint hash = 2166136261u;
            hash = StableHashStep(hash, porterId);
            hash = StableHashStep(hash, bundleHash);
            hash = StableHashStep(
                hash,
                ForeignSampleHelpRollSalt);
            return (int)(hash % 100u);
        }

        private static int GetStableSignedOffset(
            uint seed,
            uint salt,
            int range)
        {
            uint hash = 2166136261u;
            hash = StableHashStep(hash, seed);
            hash = StableHashStep(hash, salt);
            uint width =
                (uint)(range * 2 + 1);
            return (int)(hash % width) - range;
        }

        private static uint StableHashStep(
            uint hash,
            uint value)
        {
            unchecked
            {
                return (hash ^ value) * 16777619u;
            }
        }

        private static int GetPorterZoneFloor(
            Entity porter)
        {
            EmployeeComponent employee =
                porter == null
                    ? null
                    : porter.GetComponent<EmployeeComponent>();
            Room homeRoom =
                employee == null ||
                employee.m_state.m_homeRoom == null
                    ? null
                    : employee.m_state.m_homeRoom.GetEntity();
            if (homeRoom != null)
            {
                return homeRoom.GetFloorIndex();
            }

            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            return walk == null ? -1 : walk.GetFloorIndex();
        }

        private static bool CanTakeProcedure(
            Entity porter,
            LabProcedure procedure,
            bool allowForeignDepartment,
            out float distance)
        {
            // StartJob and direct assignment always perform full validation.
            return CanTakeProcedure(
                porter, procedure, allowForeignDepartment,
                false, false, out distance);
        }

        private static bool CanTakeProcedure(
            Entity porter,
            LabProcedure procedure,
            bool allowForeignDepartment,
            bool porterAvailabilityAlreadyValidated,
            out float distance)
        {
            return CanTakeProcedure(
                porter, procedure, allowForeignDepartment,
                porterAvailabilityAlreadyValidated, false, out distance);
        }

        private static bool CanTakeProcedure(
            Entity porter,
            LabProcedure procedure,
            bool allowForeignDepartment,
            bool porterAvailabilityAlreadyValidated,
            bool allowPreBlockedRoom,
            out float distance)
        {
            bool foreign;
            return CanTakeProcedure(
                porter,
                procedure,
                allowForeignDepartment,
                porterAvailabilityAlreadyValidated,
                allowPreBlockedRoom,
                out distance,
                out foreign);
        }

        private static bool CanTakeProcedure(
            Entity porter,
            LabProcedure procedure,
            bool allowForeignDepartment,
            bool porterAvailabilityAlreadyValidated,
            bool allowPreBlockedRoom,
            out float distance,
            out bool foreign)
        {
            return CanTakeProcedure(
                porter, procedure, allowForeignDepartment,
                porterAvailabilityAlreadyValidated, allowPreBlockedRoom,
                out distance, out foreign, null);
        }

        private static bool CanTakeProcedure(
            Entity porter,
            LabProcedure procedure,
            bool allowForeignDepartment,
            bool porterAvailabilityAlreadyValidated,
            bool allowPreBlockedRoom,
            out float distance,
            out bool foreign,
            Department knownProcedureDepartment)
        {
            return CanTakeProcedure(
                porter, procedure, allowForeignDepartment,
                porterAvailabilityAlreadyValidated, allowPreBlockedRoom,
                out distance, out foreign, knownProcedureDepartment,
                skipRouteDistance: false);
        }

        // Restricted to candidate classification. Never use it to assign
        // a mission: StartJob always calls full path validation.
        private static bool CanTakeProcedure(
            Entity porter,
            LabProcedure procedure,
            bool allowForeignDepartment,
            bool porterAvailabilityAlreadyValidated,
            bool allowPreBlockedRoom,
            out float distance,
            out bool foreign,
            Department knownProcedureDepartment,
            bool skipRouteDistance)
        {
            distance = float.MaxValue;
            foreign = false;
            // Reject cheap failures before bundle comparisons and routing.
            if ((!porterAvailabilityAlreadyValidated &&
                 !IsAvailablePorter(porter, procedure)) ||
                !IsHospitalizedSampleWaiting(procedure) ||
                IsRoomWaitReleasedToLab(procedure) ||
                IsRoomWaitRetryCoolingDown(procedure) ||
                IsProcedureClaimed(procedure) ||
                IsSampleBundleHandledByLabSpecialist(procedure))
            {
                return false;
            }

            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            Department homeDepartment =
                employee == null
                    ? null
                    : employee.m_state.m_department.GetEntity();
            Department procedureDepartment =
                knownProcedureDepartment ??
                GetProcedureDepartment(procedure);
            if (homeDepartment == null ||
                procedureDepartment == null)
            {
                return false;
            }

            foreign = procedureDepartment != homeDepartment;
            if (foreign &&
                (!allowForeignDepartment ||
                 !PorterTransportConfig.SampleCrossDepartmentHelp))
            {
                return false;
            }

            TileObject source = procedure.m_state.m_usedEquipment == null
                ? null
                : procedure.m_state.m_usedEquipment.GetEntity();
            if (source == null ||
                source.User != null ||
                source.Owner != null)
            {
                return false;
            }

            // Room occupancy is deliberately checked LAST, after the
            // inexpensive sample/employee/source checks, lab validation
            // and reachability. Scanning every patient in every department
            // for an invalid or unreachable procedure is wasted work.
            if (procedure.m_state.m_statLab == null ||
                procedure.m_state.m_statLab.GetEntity() == null)
            {
                procedure.FindRoom(onlyFree: false);
            }
            Room statLab = procedure.m_state.m_statLab == null
                ? null
                : procedure.m_state.m_statLab.GetEntity();
            if (statLab == null ||
                statLab.m_roomPersistentData.m_roomType == null ||
                !statLab.m_roomPersistentData.m_roomType.Entry.HasTag("stat_lab"))
            {
                return false;
            }

            WalkComponent walk = porter.GetComponent<WalkComponent>();
            if (walk == null || GridMap.GetInstance() == null)
            {
                return false;
            }

            // Own-department Sample work is deterministic on any reachable
            // floor. Foreign help is no longer forbidden by station floor:
            // station/current floor become willingness bonuses after an exact
            // STAFF route exists. Classification still avoids pathfinding.
            if (skipRouteDistance)
            {
                // The caller will perform the exact route calculation only
                // after classifying this candidate's room.
                return true;
            }

            float routeDistance =
                GetSamplePickupRouteDistance(walk, source);
            if (routeDistance < 0f)
            {
                return false;
            }

            if (foreign &&
                !ShouldAcceptForeignSampleHelp(
                    porter, procedure, routeDistance))
            {
                return false;
            }

            if (!allowPreBlockedRoom &&
                IsSamplePickupRoomBlocked(procedure))
            {
                if (!HasRoomWaitRetryForBundle(procedure))
                {
                    return false;
                }

                // Native-triggered second attempts must not jump ahead of
                // an already available free-room sample.
                if (HasAnyFreeSampleWork(porter))
                {
                    return false;
                }
            }

            distance = routeDistance;
            return true;
        }

        // Conservative lower bound for the HPO score (native STAFF
        // route distance + 100 per floor). Reuse the actual best distance,
        // never invent a maximum mission radius. Same-floor geometry is
        // not used to exclude candidates from different floors.
        private static bool CannotBeatSamplePickupDistance(
            WalkComponent walk,
            TileObject source,
            float bestDistance)
        {
            if (walk == null || source == null || source.m_state == null ||
                bestDistance >= float.MaxValue)
            {
                return false;
            }

            int floorDifference =
                Math.Abs(walk.GetFloorIndex() - source.GetFloorIndex());
            float floorPenalty = floorDifference * 100f;
            if (floorPenalty >= bestDistance)
            {
                return true;
            }
            if (floorDifference != 0)
            {
                return false;
            }

            Vector2i origin = walk.GetCurrentTile();
            Vector2i target = source.m_state.m_position;
            float dx = origin.m_x - target.m_x;
            float dy = origin.m_y - target.m_y;
            return dx * dx + dy * dy >= bestDistance * bestDistance;
        }

        private static float GetSamplePickupRouteDistance(
            WalkComponent walk,
            TileObject source)
        {
            if (walk == null || source == null ||
                GridMap.GetInstance() == null)
            {
                return -1f;
            }

            float routeDistance = GridMap.GetInstance().GetDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                source.GetFloorIndex(),
                source.m_state.m_position,
                AccessRights.STAFF);
            return routeDistance < 0f
                ? -1f
                : routeDistance +
                    Math.Abs(walk.GetFloorIndex() -
                             source.GetFloorIndex()) * 100f;
        }

        private static bool IsAvailablePorter(
            Entity porter,
            LabProcedure procedure)
        {
            return IsAvailablePorter(
                porter,
                procedure,
                allowPostDeliveryReturn: false);
        }

        private static bool IsAvailablePorter(
            Entity porter,
            LabProcedure procedure,
            bool allowPostDeliveryReturn)
        {
            bool busy = IsBusy(porter);
            if (!PorterIdentity.IsPorter(porter) ||
                PorterPatientContinuationRuntime.IsBusy(porter) ||
                (busy &&
                 !(allowPostDeliveryReturn &&
                   IsSampleContinuationWindowOpen(porter))) ||
                Database.Instance == null)
            {
                return false;
            }

            BehaviorNurse nurse =
                porter.GetComponent<BehaviorNurse>();
            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            GameDBEmployeeRole role =
                Database.Instance.GetEntry<GameDBEmployeeRole>(
                    PorterIds.SampleTransportRole);
            if (nurse == null ||
                employee == null ||
                walk == null ||
                role == null ||
                employee.IsFired() ||
                employee.ShouldGoToTraining() ||
                employee.m_state.m_shift != DayTime.Instance.GetShift() ||
                employee.m_state.m_homeRoom == null ||
                !PorterStationRegistry.IsPorterStation(
                    employee.GetHomeRoomType()) ||
                !employee.HasRole(role))
            {
                return false;
            }

            NurseState state = nurse.m_state.m_nurseState;
            return
                (state == NurseState.Idle ||
                 state == NurseState.GoingToWorkplace ||
                 state == NurseState.FinishedProcedure) &&
                nurse.m_state.m_currentPatient == null;
        }

        private static bool IsHospitalizedSampleWaiting(LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null ||
                procedure.m_state.m_labProcedureState != LabProcedureState.SamplingFinished ||
                procedure.m_state.m_usedEquipment == null ||
                !procedure.IsIdle())
            {
                return false;
            }

            Entity patient = procedure.m_state.m_patient.GetEntity();
            HospitalizationComponent hospitalization = patient == null
                ? null
                : patient.GetComponent<HospitalizationComponent>();
            return hospitalization != null && hospitalization.IsHospitalized();
        }

        private static bool StartJob(
            Entity porter,
            BehaviorNurse nurse,
            LabProcedure procedure,
            bool allowForeignDepartment)
        {
            return StartJob(
                porter, nurse, procedure, allowForeignDepartment, false);
        }

        private static bool StartJob(
            Entity porter,
            BehaviorNurse nurse,
            LabProcedure procedure,
            bool allowForeignDepartment,
            bool allowPreBlockedRoom)
        {
            float distance;
            if (!CanTakeProcedure(
                    porter,
                    procedure,
                    allowForeignDepartment,
                    false,
                    allowPreBlockedRoom,
                    out distance))
            {
                return false;
            }

            TileObject source =
                procedure.m_state.m_usedEquipment.GetEntity();
            Room statLab =
                procedure.m_state.m_statLab == null
                    ? null
                    : procedure.m_state.m_statLab.GetEntity();
            UseComponent use =
                porter.GetComponent<UseComponent>();
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (source == null ||
                statLab == null ||
                use == null ||
                walk == null)
            {
                return false;
            }

            TileObject cart;
            Room cartHomeRoom;
            bool hasCart =
                PorterSampleCartRuntime.TryReserveCart(
                    porter,
                    out cart,
                    out cartHomeRoom);

            Department targetDepartment =
                GetProcedureDepartment(procedure);
            Department patientDepartment =
                GetPatientDepartment(procedure);
            bool notifyCartCapacity =
                !hasCart &&
                WouldAdditionalSampleCartHelp(
                    porter,
                    targetDepartment);

            nurse.CancelBrowsing();
            nurse.SwitchState(NurseState.Idle);

            use.ReserveObject(source);
            if (source.User != porter)
            {
                PorterSampleCartRuntime.ReleaseReservedCart(
                    cart,
                    porter);
                return false;
            }

            PorterSampleTransportJob job =
                new PorterSampleTransportJob
                {
                    Procedure = procedure,
                    StatLab = statLab,
                    Cart = cart,
                    CartHomeRoom = cartHomeRoom,
                    CartHomeTile =
                        hasCart
                            ? cart.m_state.m_position
                            : Vector2i.ZERO_VECTOR,
                    CartHomeFloorIndex =
                        hasCart
                            ? cart.GetFloorIndex()
                            : -1,
                    CartHomeOrientation =
                        hasCart
                            ? cart.Orientation
                            : Direction.None,
                    Phase =
                        hasCart
                            ? PorterSampleTransportPhase.GoingToCart
                            : PorterSampleTransportPhase.GoingToSample
                };

            ClearFallbackTracking(procedure);
            Jobs[porter] = job;
            NextOrphanedCartRecoveryTimes.Remove(porter);

            ShowSampleBubble(porter, procedure);
            if (hasCart)
            {
                TileObject seat =
                    walk.m_state == null ||
                    walk.m_state.m_objectSittingOn == null
                        ? null
                        : walk.m_state.m_objectSittingOn.GetEntity();

                if (seat != null)
                {
                    job.WaitingForStandUpBeforeCart = true;

                    AnimModelComponent animation =
                        porter.GetComponent<AnimModelComponent>();
                    PorterDiagnostics.Log(
                        "sample departure staged from seat: porter=" +
                        (porter.Name ?? string.Empty).Trim() +
                        "; current=" +
                        walk.GetCurrentTile() +
                        "; seatPosition=" +
                        seat.m_state.m_position +
                        "; seatUseTile=" +
                        ToTile(seat.GetDefaultUsePosition()) +
                        "; cartUseTile=" +
                        ToTile(cart.GetDefaultUsePosition()) +
                        "; walkState=" +
                        (walk.m_state == null
                            ? "UNKNOWN"
                            : walk.m_state.m_walkState.ToString()) +
                        "; animation=" +
                        (animation == null ||
                         animation.m_state == null
                            ? "NONE"
                            : animation.m_state.m_currentAnimationID) +
                        ".");

                    // Keep ownership in vanilla WalkComponent. It releases the
                    // seat User in UpdateStandingUp(); HPO only waits until the
                    // native transition has fully completed before routing.
                    // If another native movement transition is already active,
                    // do not restart it; UpdateGoingToCart will wait for it.
                    if (!walk.IsBusy())
                    {
                        walk.StandUp();
                    }
                }
                else
                {
                    walk.SetDestination(
                        cart.GetDefaultUsePosition(),
                        cart.GetFloorIndex());
                }
            }
            else
            {
                Vector2i sampleTarget =
                    ToTile(source.GetDefaultUsePosition());
                SetSampleApproachTarget(
                    job,
                    sampleTarget,
                    source.GetFloorIndex());
                walk.SetDestination(
                    sampleTarget,
                    source.GetFloorIndex());

                if (allowPreBlockedRoom &&
                    IsSamplePickupRoomBlocked(procedure))
                {
                    // Do not enter an occupied room, even without a cart.
                    HandleBlockedSampleRoomApproach(
                        porter, nurse, walk, job);
                }

                if (notifyCartCapacity)
                {
                    NotifySampleCartCapacity(porter);
                }
            }

            Entity patient =
                procedure.m_state.m_patient.GetEntity();
            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            Department homeDepartment =
                employee == null
                    ? null
                    : employee.m_state.m_department.GetEntity();
            bool helpingOtherDepartment =
                homeDepartment != null &&
                targetDepartment != null &&
                homeDepartment != targetDepartment;
            ForeignSampleHelpDecision foreignHelpDecision =
                new ForeignSampleHelpDecision();
            if (helpingOtherDepartment)
            {
                foreignHelpDecision =
                    EvaluateForeignSampleHelp(
                        porter,
                        procedure,
                        distance);
            }
            HospitalizationComponent hospitalization =
                patient == null
                    ? null
                    : patient.GetComponent<HospitalizationComponent>();
            BehaviorPatient patientBehavior =
                patient == null
                    ? null
                    : patient.GetComponent<BehaviorPatient>();
            Room sourceRoom =
                GetSampleSourceRoom(procedure);
            Department sourceDepartment =
                sourceRoom == null ||
                sourceRoom.m_roomPersistentData == null ||
                sourceRoom.m_roomPersistentData.m_department == null
                    ? null
                    : sourceRoom.m_roomPersistentData.m_department.GetEntity();

            PorterDiagnostics.Log(
                "sample assigned: porter=" +
                porter.Name +
                "; patient=" +
                (patient == null ? "UNKNOWN" : patient.Name) +
                "; examination=" +
                procedure.m_state.m_examination.Entry.DatabaseID +
                "; procedureId=" +
                procedure.GetEntityID() +
                "; patientDepartment=" +
                GetDepartmentDiagnosticId(patientDepartment) +
                "; missionDepartment=" +
                GetDepartmentDiagnosticId(targetDepartment) +
                "; porterDepartment=" +
                GetDepartmentDiagnosticId(homeDepartment) +
                "; sourceRoomDepartment=" +
                GetDepartmentDiagnosticId(sourceDepartment) +
                "; isHospitalized=" +
                (hospitalization != null && hospitalization.IsHospitalized()) +
                "; hospitalizationState=" +
                (hospitalization == null || hospitalization.m_state == null
                    ? "NONE"
                    : hospitalization.m_state.m_hospitalizationState.ToString()) +
                "; patientState=" +
                (patientBehavior == null || patientBehavior.m_state == null
                    ? "NONE"
                    : patientBehavior.m_state.m_patientState.ToString()) +
                "; samplePorterAttempt=" +
                (HasRoomWaitRetryForBundle(procedure)
                    ? "2/2"
                    : "1/2") +
                "; sourceRoomType=" +
                GetRoomTypeDiagnosticId(
                    GetSampleSourceRoom(procedure)) +
                "; floors=" +
                source.GetFloorIndex() +
                "->" +
                statLab.GetFloorIndex() +
                "; cart=" +
                hasCart +
                "; crossDepartment=" +
                helpingOtherDepartment +
                (helpingOtherDepartment
                    ? "; foreignHelpChance=" +
                      foreignHelpDecision.Chance +
                      "; foreignHelpRoll=" +
                      foreignHelpDecision.Roll +
                      "; foreignHelpStationFloorBonus=" +
                      foreignHelpDecision.StationFloorBonus +
                      "; foreignHelpCurrentFloorBonus=" +
                      foreignHelpDecision.CurrentFloorBonus +
                      "; foreignHelpWaitingBonus=" +
                      foreignHelpDecision.WaitingBonus +
                      "; foreignHelpDistancePenalty=" +
                      foreignHelpDecision.DistancePenalty +
                      "; foreignHelpPersonality=" +
                      foreignHelpDecision.Personality +
                      "; foreignHelpMissionVariation=" +
                      foreignHelpDecision.MissionVariation
                    : string.Empty) +
                "; preBlockedRoomWait=" + allowPreBlockedRoom +
                ".");
            return true;
        }

        private static void UpdateJob(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            if (job == null)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            if (job.Phase == PorterSampleTransportPhase.ReturningCart)
            {
                UpdateReturningCart(porter, nurse, job);
                return;
            }

            if (job.Phase ==
                    PorterSampleTransportPhase.ReturningToStation)
            {
                UpdateReturningToStation(
                    porter,
                    nurse,
                    job);
                return;
            }

            if (job.Phase ==
                    PorterSampleTransportPhase.WaitingForCartReturnSlot)
            {
                UpdateWaitingForCartReturnSlot(
                    porter,
                    nurse,
                    job);
                return;
            }

            if (job.Phase ==
                    PorterSampleTransportPhase.RecoveringCartAfterCancel)
            {
                UpdateRecoveringCartAfterCancel(
                    porter,
                    nurse,
                    job);
                return;
            }

            if (job.Procedure == null || job.Procedure.m_state == null)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            LabProcedure procedure = job.Procedure;
            LabProcedureState state = procedure.m_state.m_labProcedureState;
            if (state == LabProcedureState.Finished)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            if (job.Phase == PorterSampleTransportPhase.GoingToCart)
            {
                if (state != LabProcedureState.SamplingFinished)
                {
                    CancelJob(porter, nurse, job, revertSample: false);
                    return;
                }
                UpdateGoingToCart(porter, nurse, job);
                return;
            }

            if (job.Phase ==
                    PorterSampleTransportPhase.GoingToSampleParking)
            {
                if (state != LabProcedureState.SamplingFinished)
                {
                    CancelJob(porter, nurse, job, revertSample: false);
                    return;
                }
                UpdateGoingToSampleParking(porter, nurse, job);
                return;
            }

            if (job.Phase == PorterSampleTransportPhase.GoingToSample)
            {
                if (state != LabProcedureState.SamplingFinished)
                {
                    CancelJob(porter, nurse, job, revertSample: false);
                    return;
                }
                UpdateGoingToSample(porter, nurse, job);
                return;
            }

            if (state == LabProcedureState.DeliveredToStatLab)
            {
                DeliverCollectedProcedures(job);
                CompleteJob(porter, nurse, job, timedOut: true);
                return;
            }

            if (state != LabProcedureState.SampleBeingDelivered)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            switch (job.Phase)
            {
                case PorterSampleTransportPhase.TakingSample:
                    UpdateTakingSample(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.ReturningToCartAfterSample:
                    UpdateReturningToCartAfterSample(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.WaitingForFridge:
                    TryGoToFridge(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.GoingToFridgeParking:
                    UpdateGoingToFridgeParking(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.GoingToFridge:
                    UpdateGoingToFridge(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.StoringSample:
                    UpdateStoringSample(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.ReturningToCartAfterFridge:
                    UpdateReturningToCartAfterFridge(porter, nurse, job);
                    break;
            }
        }

        private static void UpdateGoingToCart(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            TileObject source =
                job.Procedure.m_state.m_usedEquipment == null
                    ? null
                    : job.Procedure.m_state.m_usedEquipment.GetEntity();
            if (walk == null ||
                source == null ||
                job.Cart == null ||
                job.Cart.User != porter)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            Vector2i cartUseTile =
                ToTile(job.Cart.GetDefaultUsePosition());

            if (job.WaitingForStandUpBeforeCart)
            {
                // StandingUp remains busy and keeps m_objectSittingOn until
                // vanilla finishes the exit animation. Once vanilla has fully
                // cleared the seat and settled its old seat destination, start
                // the cart route from the Porter's real standing tile.
                if (walk.IsSitting())
                {
                    if (!walk.IsBusy())
                    {
                        walk.StandUp();
                    }
                    return;
                }

                if (walk.IsBusy())
                {
                    return;
                }

                job.WaitingForStandUpBeforeCart = false;
                job.RouteRecoveryAttempts = 0;
                walk.SetDestination(
                    cartUseTile,
                    job.Cart.GetFloorIndex());

                PorterDiagnostics.Log(
                    "sample departure resumed after stand-up: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; current=" +
                    walk.GetCurrentTile() +
                    "; cartUseTile=" +
                    cartUseTile +
                    "; floor=" +
                    job.Cart.GetFloorIndex() +
                    ".");
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            if (!EnsureReachedDestination(
                    porter,
                    walk,
                    job,
                    cartUseTile,
                    job.Cart.GetFloorIndex(),
                    "cart-pickup"))
            {
                if (job.RouteRecoveryAttempts >= 3)
                {
                    CancelJob(
                        porter,
                        nurse,
                        job,
                        revertSample: false);
                }
                return;
            }

            // If the room is still blocked, the Porter must not idle
            // indefinitely at the cart. Attach it and approach outside.
            bool blockedBeforeApproach =
                IsSamplePickupRoomBlocked(job.Procedure);

            Vector2i parkingTile = Vector2i.ZERO_VECTOR;
            if (!blockedBeforeApproach &&
                !PorterSampleCartRuntime.TryGetInteractionParkingTile(
                    porter,
                    job,
                    source,
                    out parkingTile))
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            if (!PorterSampleCartRuntime.TryAttachCart(
                    porter,
                    job.Cart,
                    job.CartHomeRoom))
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            job.Phase =
                PorterSampleTransportPhase.GoingToSampleParking;
            if (blockedBeforeApproach)
            {
                HandleBlockedSampleRoomApproach(
                    porter, nurse, walk, job);
                return;
            }

            SetSampleApproachTarget(
                job,
                parkingTile,
                source.GetFloorIndex());
            walk.SetDestination(
                parkingTile,
                source.GetFloorIndex());

        }

        private static void UpdateGoingToSampleParking(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            TileObject source =
                job.Procedure.m_state.m_usedEquipment.GetEntity();
            if (walk == null ||
                source == null)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            if (HandleBlockedSampleRoomApproach(
                    porter,
                    nurse,
                    walk,
                    job))
            {
                return;
            }

            if (!EnsureSampleSourceReserved(
                    porter,
                    job,
                    source))
            {
                if (walk.IsBusy())
                {
                    walk.Stop();
                }
                return;
            }

            if (!job.HasSampleApproachTarget)
            {
                Vector2i parkingTile;
                if (!PorterSampleCartRuntime.TryGetInteractionParkingTile(
                        porter,
                        job,
                        source,
                        out parkingTile))
                {
                    CancelJob(
                        porter,
                        nurse,
                        job,
                        revertSample: false);
                    return;
                }

                SetSampleApproachTarget(
                    job,
                    parkingTile,
                    source.GetFloorIndex());
            }

            if (walk.IsBusy())
            {
                return;
            }

            if (!EnsureReachedDestination(
                    porter,
                    walk,
                    job,
                    job.SampleApproachTarget,
                    job.SampleApproachFloorIndex,
                    "sample-parking"))
            {
                if (job.RouteRecoveryAttempts >= 3)
                {
                    CancelJob(
                        porter,
                        nurse,
                        job,
                        revertSample: false);
                }
                return;
            }

            if (!PorterSampleCartRuntime.TryParkCartAtCurrentTile(
                    porter,
                    job))
            {
                Vector2i replacementParkingTile;
                if (PorterSampleCartRuntime.TryGetInteractionParkingTile(
                        porter,
                        job,
                        source,
                        out replacementParkingTile))
                {
                    SetSampleApproachTarget(
                        job,
                        replacementParkingTile,
                        source.GetFloorIndex());
                    job.RouteRecoveryAttempts = 0;

                    if (walk.GetCurrentTile() != replacementParkingTile ||
                        walk.GetFloorIndex() != source.GetFloorIndex())
                    {
                        walk.SetDestination(
                            replacementParkingTile,
                            source.GetFloorIndex());
                    }

                    PorterDiagnostics.Log(
                        "sample cart parking replanned: porter=" +
                        (porter.Name ?? string.Empty).Trim() +
                        "; target=" +
                        replacementParkingTile +
                        "; floor=" +
                        source.GetFloorIndex() +
                        ".");
                    return;
                }

                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            Vector2i sampleTarget =
                ToTile(source.GetDefaultUsePosition());
            SetSampleApproachTarget(
                job,
                sampleTarget,
                source.GetFloorIndex());
            walk.SetDestination(
                sampleTarget,
                source.GetFloorIndex());
            job.Phase =
                PorterSampleTransportPhase.GoingToSample;
        }

        private static void UpdateGoingToSample(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            UseComponent use = porter.GetComponent<UseComponent>();
            TileObject source = job.Procedure.m_state.m_usedEquipment.GetEntity();
            if (walk == null || use == null || source == null)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            if (HandleBlockedSampleRoomApproach(
                    porter,
                    nurse,
                    walk,
                    job))
            {
                return;
            }

            if (!EnsureSampleSourceReserved(
                    porter,
                    job,
                    source))
            {
                if (walk.IsBusy())
                {
                    walk.Stop();
                }
                return;
            }

            if (!job.HasSampleApproachTarget)
            {
                SetSampleApproachTarget(
                    job,
                    ToTile(source.GetDefaultUsePosition()),
                    source.GetFloorIndex());
            }

            if (walk.IsBusy())
            {
                return;
            }

            if (!EnsureReachedDestination(
                    porter,
                    walk,
                    job,
                    job.SampleApproachTarget,
                    job.SampleApproachFloorIndex,
                    "sample-pickup"))
            {
                if (job.RouteRecoveryAttempts >= 3)
                {
                    CancelJob(
                        porter,
                        nurse,
                        job,
                        revertSample: false);
                }
                return;
            }

            job.Procedure.SwitchState(LabProcedureState.SampleBeingDelivered);
            use.Activate(UseComponentMode.SINGLE_USE);
            job.Phase = PorterSampleTransportPhase.TakingSample;
        }

        private static void UpdateTakingSample(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            UseComponent use = porter.GetComponent<UseComponent>();
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (use == null ||
                walk == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            if (IsSamplePickupRoomBlocked(job.Procedure))
            {
                TileObject source =
                    job.Procedure == null ||
                    job.Procedure.m_state == null ||
                    job.Procedure.m_state.m_usedEquipment == null
                        ? null
                        : job.Procedure.m_state
                            .m_usedEquipment.GetEntity();

                ReleaseSampleSourceReservation(
                    porter,
                    job);

                if (job.Procedure != null &&
                    job.Procedure.m_state != null &&
                    job.Procedure.m_state.m_labProcedureState ==
                        LabProcedureState.SampleBeingDelivered)
                {
                    job.Procedure.SwitchState(
                        LabProcedureState.SamplingFinished);
                }

                job.Phase =
                    PorterSampleTransportPhase.GoingToSample;
                if (source != null)
                {
                    SetSampleApproachTarget(
                        job,
                        ToTile(source.GetDefaultUsePosition()),
                        source.GetFloorIndex());
                }

                string interruptedPatient =
                    GetPatientName(job.Procedure);

                HandleBlockedSampleRoomApproach(
                    porter,
                    nurse,
                    walk,
                    job);

                PorterDiagnostics.Log(
                    "sample pickup interrupted by occupied room: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; patient=" +
                    interruptedPatient +
                    ".");
                return;
            }

            if (use.IsBusy())
            {
                return;
            }

            CollectCurrentSampleBundle(job);
            PlaySampleInteractionSound(porter, SamplePickupSoundEvent);

            if (job.Cart == null)
            {
                job.Phase =
                    PorterSampleTransportPhase.WaitingForFridge;
                TryGoToFridge(porter, nurse, job);
                return;
            }

            if (!PorterSampleCartRuntime.TryWalkToParkedCart(
                    porter,
                    job))
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            job.Phase =
                PorterSampleTransportPhase.ReturningToCartAfterSample;
        }

        private static void UpdateReturningToCartAfterSample(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            Vector2i cartUseTile =
                ToTile(job.Cart.GetDefaultUsePosition());
            if (!EnsureReachedDestination(
                    porter,
                    walk,
                    job,
                    cartUseTile,
                    job.Cart.GetFloorIndex(),
                    "sample-cart-repickup"))
            {
                if (job.RouteRecoveryAttempts >= 3)
                {
                    CancelJob(
                        porter,
                        nurse,
                        job,
                        revertSample: true);
                }
                return;
            }

            if (!PorterSampleCartRuntime.TryAttachCart(
                    porter,
                    job.Cart,
                    job.CartHomeRoom))
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            if (TryContinueSampleTour(porter, nurse, job))
            {
                return;
            }

            job.Phase =
                PorterSampleTransportPhase.WaitingForFridge;
            TryGoToFridge(porter, nurse, job);
        }

        private static void TryGoToFridge(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            Room statLab = job == null
                ? null
                : job.StatLab;
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            UseComponent use = porter.GetComponent<UseComponent>();
            if (statLab == null || walk == null || use == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            TileObject fridge = MapScriptInterface.Instance.FindClosestFreeObjectWithTag(
                porter,
                null,
                walk.GetCurrentTile(),
                statLab,
                "medical_fridge",
                AccessRights.STAFF);
            if (fridge == null)
            {
                return;
            }

            use.ReserveObject(fridge);
            if (fridge.User != porter)
            {
                return;
            }

            if (job.Cart == null)
            {
                walk.SetDestination(
                    fridge.GetDefaultUsePosition(),
                    fridge.GetFloorIndex());
                job.Phase =
                    PorterSampleTransportPhase.GoingToFridge;
                return;
            }

            Vector2i parkingTile;
            if (!PorterSampleCartRuntime.TryGetInteractionParkingTile(
                    porter,
                    job,
                    fridge,
                    out parkingTile))
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            walk.SetDestination(
                parkingTile,
                fridge.GetFloorIndex());
            job.Phase =
                PorterSampleTransportPhase.GoingToFridgeParking;
        }

        private static void UpdateGoingToFridgeParking(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            UseComponent use =
                porter.GetComponent<UseComponent>();
            TileObject fridge =
                use == null ||
                use.m_state.m_reservedObject == null
                    ? null
                    : use.m_state.m_reservedObject.GetEntity();
            if (walk == null ||
                use == null ||
                fridge == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            Vector2i expectedParkingTile =
                job.HasCartParkingTarget
                    ? job.CartParkingTarget
                    : walk.GetDestinationTile();

            if (!EnsureReachedDestination(
                    porter,
                    walk,
                    job,
                    expectedParkingTile,
                    fridge.GetFloorIndex(),
                    "fridge-parking"))
            {
                if (job.RouteRecoveryAttempts >= 3)
                {
                    CancelJob(
                        porter,
                        nurse,
                        job,
                        revertSample: true);
                }
                return;
            }

            if (!PorterSampleCartRuntime.TryParkCartAtCurrentTile(
                    porter,
                    job))
            {
                Vector2i replacementParkingTile;
                if (PorterSampleCartRuntime.TryGetInteractionParkingTile(
                        porter,
                        job,
                        fridge,
                        out replacementParkingTile))
                {
                    job.RouteRecoveryAttempts = 0;

                    if (walk.GetCurrentTile() != replacementParkingTile ||
                        walk.GetFloorIndex() != fridge.GetFloorIndex())
                    {
                        walk.SetDestination(
                            replacementParkingTile,
                            fridge.GetFloorIndex());
                    }

                    PorterDiagnostics.Log(
                        "fridge cart parking replanned: porter=" +
                        (porter.Name ?? string.Empty).Trim() +
                        "; target=" +
                        replacementParkingTile +
                        "; floor=" +
                        fridge.GetFloorIndex() +
                        ".");
                    return;
                }

                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            walk.SetDestination(
                fridge.GetDefaultUsePosition(),
                fridge.GetFloorIndex());
            job.Phase =
                PorterSampleTransportPhase.GoingToFridge;
        }

        private static void UpdateGoingToFridge(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            UseComponent use = porter.GetComponent<UseComponent>();
            if (walk == null || use == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }
            if (walk.IsBusy())
            {
                return;
            }

            TileObject fridge =
                use.m_state.m_reservedObject == null
                    ? null
                    : use.m_state.m_reservedObject.GetEntity();
            if (fridge == null)
            {
                CancelJob(
                    porter,
                    nurse,
                    job,
                    revertSample: true);
                return;
            }

            Vector2i fridgeUseTile =
                ToTile(fridge.GetDefaultUsePosition());
            if (!EnsureReachedDestination(
                    porter,
                    walk,
                    job,
                    fridgeUseTile,
                    fridge.GetFloorIndex(),
                    "fridge-use"))
            {
                if (job.RouteRecoveryAttempts >= 3)
                {
                    CancelJob(
                        porter,
                        nurse,
                        job,
                        revertSample: true);
                }
                return;
            }

            use.Activate(UseComponentMode.SINGLE_USE);
            job.Phase = PorterSampleTransportPhase.StoringSample;
        }

        private static void UpdateStoringSample(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            UseComponent use = porter.GetComponent<UseComponent>();
            if (use == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }
            if (use.IsBusy())
            {
                return;
            }

            if (!job.FridgeDepositSoundPlayed)
            {
                job.FridgeDepositSoundPlayed = true;
                PlaySampleInteractionSound(porter, SampleFridgeDepositSoundEvent);
            }

            if (job.Cart == null)
            {
                int deliveredSamples =
                    CountCollectedSampleBundles(job);
                int deliveredProcedures =
                    DeliverCollectedProcedures(job);
                PorterExperience.AwardWorkExperience(
                    porter,
                    deliveredSamples);
                PorterDiagnostics.Log(
                    "sample completed: porter=" +
                    porter.Name +
                    "; samples=" +
                    deliveredSamples +
                    "; procedures=" +
                    deliveredProcedures +
                    "; labFloor=" +
                    job.StatLab.GetFloorIndex() +
                    "; cart=false.");

                CompleteJob(
                    porter,
                    nurse,
                    job,
                    timedOut: false);
                return;
            }

            if (!PorterSampleCartRuntime.TryWalkToParkedCart(
                    porter,
                    job))
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            job.Phase =
                PorterSampleTransportPhase.ReturningToCartAfterFridge;
        }

        private static void UpdateReturningToCartAfterFridge(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            Vector2i cartUseTile =
                ToTile(job.Cart.GetDefaultUsePosition());
            if (!EnsureReachedDestination(
                    porter,
                    walk,
                    job,
                    cartUseTile,
                    job.Cart.GetFloorIndex(),
                    "fridge-cart-repickup"))
            {
                if (job.RouteRecoveryAttempts >= 3)
                {
                    CancelJob(
                        porter,
                        nurse,
                        job,
                        revertSample: true);
                }
                return;
            }

            if (!PorterSampleCartRuntime.TryAttachCart(
                    porter,
                    job.Cart,
                    job.CartHomeRoom))
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            int deliveredSamples =
                CountCollectedSampleBundles(job);
            int deliveredProcedures =
                DeliverCollectedProcedures(job);
            PorterExperience.AwardWorkExperience(
                porter,
                deliveredSamples);
            PorterDiagnostics.Log(
                "sample completed: porter=" +
                porter.Name +
                "; samples=" +
                deliveredSamples +
                "; procedures=" +
                deliveredProcedures +
                "; labFloor=" +
                job.StatLab.GetFloorIndex() +
                ".");

            CompleteJob(porter, nurse, job, timedOut: false);
        }

        private static void UpdateRecoveringCartAfterCancel(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            TileObject cart =
                job == null
                    ? null
                    : job.Cart;
            if (walk == null ||
                cart == null ||
                cart.User != porter)
            {
                PorterDiagnostics.Log(
                    "cart cancel recovery aborted: porter=" +
                    (porter == null
                        ? "UNKNOWN"
                        : (porter.Name ?? string.Empty).Trim()) +
                    "; cart=" +
                    (cart == null ? "NONE" : "present") +
                    "; userMatches=" +
                    (cart != null && cart.User == porter) +
                    ".");

                if (cart != null && cart.User == porter)
                {
                    cart.User = null;
                }

                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    timedOut: false);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            Vector2i cartUseTile =
                ToTile(cart.GetDefaultUsePosition());
            if (!EnsureReachedDestination(
                    porter,
                    walk,
                    job,
                    cartUseTile,
                    cart.GetFloorIndex(),
                    "cancel-cart-repickup"))
            {
                if (job.RouteRecoveryAttempts >= 3)
                {
                    PorterDiagnostics.Log(
                        "cart cancel recovery released parked cart after repeated route failure: porter=" +
                        (porter.Name ?? string.Empty).Trim() +
                        "; cartPosition=" +
                        cart.m_state.m_position +
                        "; cartFloor=" +
                        cart.GetFloorIndex() +
                        ".");

                    cart.User = null;
                    cart.SetAttachedToCharacter(
                        attached: false);
                    FinalizeJob(
                        porter,
                        nurse,
                        job,
                        timedOut: false);
                }
                return;
            }

            if (!PorterSampleCartRuntime.TryAttachCart(
                    porter,
                    cart,
                    job.CartHomeRoom))
            {
                PorterDiagnostics.Log(
                    "cart cancel recovery attach failed; cart left in place and released: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; cartPosition=" +
                    cart.m_state.m_position +
                    "; cartFloor=" +
                    cart.GetFloorIndex() +
                    ".");

                cart.User = null;
                cart.SetAttachedToCharacter(
                    attached: false);
                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    timedOut: false);
                return;
            }

            BeginCartReturnOrWait(
                porter,
                nurse,
                job,
                timedOut: false);

            PorterDiagnostics.Log(
                "cart cancel recovery after reattach: porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; home=" +
                job.CartHomeTile +
                "; homeFloor=" +
                job.CartHomeFloorIndex +
                "; phase=" +
                job.Phase +
                ".");
        }

        private static void ScheduleCartReturnSlotRetry(
            Entity porter,
            PorterSampleTransportJob job)
        {
            if (job == null)
            {
                return;
            }

            float now =
                GetCartRestoreClockMinutes();

            job.CartRestoreRetryCount++;
            job.NextCartRestoreRetryAt =
                DayTime.Instance == null
                    ? 0f
                    : now + CartRestoreRetryMinutes;

            if (job.CartRestoreRetryCount == 1)
            {
                Plugin.Log?.LogWarning(
                    "Porter sample cart has no safe return position; the Porter will wait away from the station and retry every 2 in-game minutes.");
            }

            if (job.CartRestoreRetryCount == 1 ||
                job.CartRestoreRetryCount % 5 == 0)
            {
                PorterDiagnostics.Log(
                    "cart return slot wait: porter=" +
                    (porter == null
                        ? "UNKNOWN"
                        : (porter.Name ?? string.Empty).Trim()) +
                    "; attempt=" +
                    job.CartRestoreRetryCount +
                    "; retryMinutes=" +
                    CartRestoreRetryMinutes +
                    "; cartPosition=" +
                    (job.Cart == null
                        ? "NONE"
                        : job.Cart.m_state.m_position.ToString()) +
                    "; cartFloor=" +
                    (job.Cart == null
                        ? -1
                        : job.Cart.GetFloorIndex()) +
                    "; attached=" +
                    (job.Cart != null &&
                        job.Cart.IsAttachedToCharacter()) +
                    "; " +
                    PorterSampleCartRuntime.GetSafeReturnPlacementDiagnostic(
                        job) +
                    ".");
            }
        }

        private static bool TryPrepareCartReturnTarget(
            Entity porter,
            PorterSampleTransportJob job)
        {
            if (job == null)
            {
                return false;
            }

            Vector2i placement;
            Direction orientation;
            if (!PorterSampleCartRuntime.TryGetSafeReturnPlacement(
                    job,
                    porter,
                    out placement,
                    out orientation))
            {
                job.HasCartReturnTarget = false;
                return false;
            }

            job.CartReturnTarget = placement;
            job.HasCartReturnTarget = true;

            PorterDiagnostics.Log(
                "cart return target selected: porter=" +
                (porter == null
                    ? "UNKNOWN"
                    : (porter.Name ?? string.Empty).Trim()) +
                "; target=" +
                placement +
                "; orientation=" +
                orientation +
                "; home=" +
                job.CartHomeTile +
                "; usedFallback=" +
                (placement != job.CartHomeTile) +
                ".");
            return true;
        }

        private static void BeginCartReturnOrWait(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job,
            bool timedOut)
        {
            ClearCartParkingTarget(job);
            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            if (job == null ||
                walk == null ||
                job.Cart == null ||
                !job.Cart.m_state.m_moving ||
                job.Cart.User != porter)
            {
                return;
            }

            job.NativeTimeout = timedOut;
            job.RouteRecoveryAttempts = 0;
            job.CartRestoreRetryCount = 0;
            job.NextCartRestoreRetryAt = 0f;
            job.HasCartReturnTarget = false;

            if (TryPrepareCartReturnTarget(
                    porter,
                    job))
            {
                job.Phase =
                    PorterSampleTransportPhase.ReturningCart;
                walk.SetDestination(
                    job.CartReturnTarget,
                    job.CartHomeFloorIndex);

                PorterDiagnostics.Log(
                    "cart return started: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; target=" +
                    job.CartReturnTarget +
                    "; home=" +
                    job.CartHomeTile +
                    "; homeFloor=" +
                    job.CartHomeFloorIndex +
                    ".");
                return;
            }

            walk.Stop();
            nurse.SwitchState(NurseState.Idle);
            job.Phase =
                PorterSampleTransportPhase.WaitingForCartReturnSlot;
            ScheduleCartReturnSlotRetry(
                porter,
                job);
        }

        private static void UpdateWaitingForCartReturnSlot(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            if (job == null ||
                job.Cart == null ||
                job.Cart.User != porter ||
                !job.Cart.m_state.m_moving ||
                !job.Cart.IsAttachedToCharacter())
            {
                CancelJob(
                    porter,
                    nurse,
                    job,
                    revertSample: false);
                return;
            }

            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return;
            }

            if (walk.IsBusy())
            {
                walk.Stop();
            }

            float now =
                GetCartRestoreClockMinutes();
            if (DayTime.Instance != null &&
                job.NextCartRestoreRetryAt > now)
            {
                return;
            }

            if (!TryPrepareCartReturnTarget(
                    porter,
                    job))
            {
                ScheduleCartReturnSlotRetry(
                    porter,
                    job);
                return;
            }

            job.CartRestoreRetryCount = 0;
            job.NextCartRestoreRetryAt = 0f;
            job.RouteRecoveryAttempts = 0;
            job.Phase =
                PorterSampleTransportPhase.ReturningCart;
            walk.SetDestination(
                job.CartReturnTarget,
                job.CartHomeFloorIndex);

            PorterDiagnostics.Log(
                "cart return slot available: porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; target=" +
                job.CartReturnTarget +
                "; home=" +
                job.CartHomeTile +
                "; homeFloor=" +
                job.CartHomeFloorIndex +
                ".");
        }

        private static bool TryPrepareStationReturnTarget(
            Entity porter,
            PorterSampleTransportJob job,
            out Room station)
        {
            station = null;
            if (porter == null ||
                job == null ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            EmployeeComponent employee;
            if (!PorterIdleResting.TryGetPorterStation(
                    porter,
                    out employee,
                    out station) ||
                station == null)
            {
                return false;
            }

            Vector2i target =
                MapScriptInterface.Instance.GetRandomFreePosition(
                    station,
                    AccessRights.STAFF);

            if (target == Vector2i.ZERO_VECTOR &&
                employee != null &&
                employee.m_state != null &&
                employee.m_state.m_workPlacePosition !=
                    Vector2i.ZERO_VECTOR &&
                station.IsPositionInRoom(
                    employee.m_state.m_workPlacePosition))
            {
                target =
                    employee.m_state.m_workPlacePosition;
            }

            if (target == Vector2i.ZERO_VECTOR)
            {
                return false;
            }

            job.StationReturnTarget = target;
            job.StationReturnFloorIndex =
                station.GetFloorIndex();
            job.HasStationReturnTarget = true;
            return true;
        }

        private static void BeginStationReturn(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job,
            bool timedOut)
        {
            if (porter == null ||
                nurse == null ||
                job == null)
            {
                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    timedOut);
                return;
            }

            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    timedOut);
                return;
            }

            job.NativeTimeout = timedOut;
            job.PostDeliveryReturn = true;
            job.RouteRecoveryAttempts = 0;
            job.HasStationReturnTarget = false;
            job.Phase =
                PorterSampleTransportPhase.ReturningToStation;
            nurse.SwitchState(NurseState.Idle);

            EmployeeComponent employee;
            Room station;
            if (!PorterIdleResting.TryGetPorterStation(
                    porter,
                    out employee,
                    out station) ||
                station == null)
            {
                Plugin.Log?.LogWarning(
                    "Porter sample return could not resolve a valid Porter Station; releasing the completed job.");
                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    timedOut);
                return;
            }

            Room currentRoom =
                MapScriptInterface.Instance == null
                    ? null
                    : MapScriptInterface.Instance.GetRoomAt(walk);
            if (!walk.IsBusy() &&
                currentRoom == station)
            {
                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    timedOut);
                return;
            }

            Room preparedStation;
            if (!TryPrepareStationReturnTarget(
                    porter,
                    job,
                    out preparedStation))
            {
                if (walk.IsBusy())
                {
                    walk.Stop();
                }
                return;
            }

            walk.SetDestination(
                job.StationReturnTarget,
                job.StationReturnFloorIndex);

            PorterDiagnostics.Log(
                "sample station return started: porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; cart=" +
                (job.Cart != null) +
                "; target=" +
                job.StationReturnTarget +
                "; floor=" +
                job.StationReturnFloorIndex +
                ".");
        }

        private static void UpdateReturningToStation(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            if (walk == null ||
                job == null)
            {
                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    job != null && job.NativeTimeout);
                return;
            }

            EmployeeComponent employee;
            Room station;
            if (!PorterIdleResting.TryGetPorterStation(
                    porter,
                    out employee,
                    out station) ||
                station == null)
            {
                Plugin.Log?.LogWarning(
                    "Porter Station disappeared during completed Sample return; releasing the job.");
                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    job.NativeTimeout);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            Room currentRoom =
                MapScriptInterface.Instance == null
                    ? null
                    : MapScriptInterface.Instance.GetRoomAt(walk);
            if (currentRoom == station)
            {
                PorterDiagnostics.Log(
                    "sample station return completed: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; cart=" +
                    (job.Cart != null) +
                    ".");
                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    job.NativeTimeout);
                return;
            }

            if (!job.HasStationReturnTarget)
            {
                Room preparedStation;
                if (!TryPrepareStationReturnTarget(
                        porter,
                        job,
                        out preparedStation))
                {
                    return;
                }
            }

            if (!EnsureReachedDestination(
                    porter,
                    walk,
                    job,
                    job.StationReturnTarget,
                    job.StationReturnFloorIndex,
                    "porter-station-return"))
            {
                if (job.RouteRecoveryAttempts >= 3)
                {
                    job.RouteRecoveryAttempts = 0;
                    job.HasStationReturnTarget = false;
                }
                return;
            }

            PorterDiagnostics.Log(
                "sample station return completed at target: porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; cart=" +
                (job.Cart != null) +
                ".");
            FinalizeJob(
                porter,
                nurse,
                job,
                job.NativeTimeout);
        }

        private static float GetCartRestoreClockMinutes()
        {
            if (DayTime.Instance == null)
            {
                return 0f;
            }

            return DayTime.Instance.GetDay() * 1440f +
                DayTime.Instance.GetDayTimeHours() * 60f;
        }

        private static bool TryRestoreCartWithRetry(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            if (job == null)
            {
                return true;
            }

            float now =
                GetCartRestoreClockMinutes();

            if (DayTime.Instance != null &&
                job.NextCartRestoreRetryAt > now)
            {
                return false;
            }

            if (PorterSampleCartRuntime.RestoreCart(
                    job,
                    porter))
            {
                if (job.CartRestoreRetryCount > 0)
                {
                    PorterDiagnostics.Log(
                        "cart restore retry succeeded: porter=" +
                        (porter == null
                            ? "UNKNOWN"
                            : (porter.Name ?? string.Empty).Trim()) +
                        "; attempts=" +
                        job.CartRestoreRetryCount +
                        ".");
                }

                job.CartRestoreRetryCount = 0;
                job.NextCartRestoreRetryAt = 0f;
                return true;
            }

            job.CartRestoreRetryCount++;

            job.NextCartRestoreRetryAt =
                DayTime.Instance == null
                    ? 0f
                    : now + CartRestoreRetryMinutes;

            if (job.CartRestoreRetryCount == 1)
            {
                Plugin.Log?.LogWarning(
                    "Porter sample cart return is blocked; the Porter will keep the cart and retry when a safe station position becomes available.");
            }

            if (job.CartRestoreRetryCount == 1 ||
                job.CartRestoreRetryCount % 5 == 0)
            {
                PorterDiagnostics.Log(
                    "cart restore retry scheduled: porter=" +
                    (porter == null
                        ? "UNKNOWN"
                        : (porter.Name ?? string.Empty).Trim()) +
                    "; attempt=" +
                    job.CartRestoreRetryCount +
                    "; retryMinutes=" +
                    CartRestoreRetryMinutes +
                    "; cartMoving=" +
                    (job.Cart != null &&
                        job.Cart.m_state.m_moving) +
                    "; cartAttached=" +
                    (job.Cart != null &&
                        job.Cart.IsAttachedToCharacter()) +
                    ".");
            }

            return false;
        }

        private static void UpdateReturningCart(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                if (!TryRestoreCartWithRetry(
                        porter,
                        nurse,
                        job))
                {
                    return;
                }

                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    job.NativeTimeout);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            if (!job.HasCartReturnTarget &&
                !TryPrepareCartReturnTarget(
                    porter,
                    job))
            {
                walk.Stop();
                nurse.SwitchState(NurseState.Idle);
                job.Phase =
                    PorterSampleTransportPhase.WaitingForCartReturnSlot;
                ScheduleCartReturnSlotRetry(
                    porter,
                    job);
                return;
            }

            if (!EnsureReachedDestination(
                    porter,
                    walk,
                    job,
                    job.CartReturnTarget,
                    job.CartHomeFloorIndex,
                    "cart-home-return"))
            {
                if (job.RouteRecoveryAttempts >= 3)
                {
                    job.RouteRecoveryAttempts = 0;
                    walk.SetDestination(
                        job.CartReturnTarget,
                        job.CartHomeFloorIndex);
                }
                return;
            }

            if (!TryRestoreCartWithRetry(
                    porter,
                    nurse,
                    job))
            {
                job.HasCartReturnTarget = false;
                job.Phase =
                    PorterSampleTransportPhase.WaitingForCartReturnSlot;
                return;
            }

            FinalizeJob(
                porter,
                nurse,
                job,
                job.NativeTimeout);
        }

        private static int DeliverCollectedProcedures(PorterSampleTransportJob job)
        {
            if (job == null)
            {
                return 0;
            }

            int delivered = 0;
            for (int i = 0; i < job.CollectedProcedures.Count; i++)
            {
                LabProcedure procedure = job.CollectedProcedures[i];
                if (procedure == null ||
                    procedure.m_state == null ||
                    procedure.m_state.m_labProcedureState != LabProcedureState.SampleBeingDelivered)
                {
                    continue;
                }

                procedure.SwitchState(LabProcedureState.DeliveredToStatLab);
                ClearFallbackTracking(procedure);
                delivered++;
            }

            if (job.Procedure != null &&
                job.Procedure.m_state != null &&
                job.Procedure.m_state.m_labProcedureState == LabProcedureState.SampleBeingDelivered &&
                !job.CollectedProcedures.Contains(job.Procedure))
            {
                job.Procedure.SwitchState(LabProcedureState.DeliveredToStatLab);
                ClearFallbackTracking(job.Procedure);
                delivered++;
            }

            return delivered;
        }

        private static void CompleteJob(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job,
            bool timedOut)
        {
            HideSampleRoomWaitingBubble(porter, job);
            SpeechComponent speech = porter.GetComponent<SpeechComponent>();
            if (speech != null)
            {
                speech.HideBubble();
            }

            if (job == null)
            {
                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    timedOut);
                return;
            }

            job.PostDeliveryReturn = true;
            // A completed delivery creates a new return obligation. Never
            // carry a decline decision from an older continuation window.
            job.ContinuationDeclined = false;
            job.NativeTimeout = timedOut;
            job.Procedure = null;
            job.CollectedProcedures.Clear();
            job.HasSampleApproachTarget = false;
            job.HasSampleRoomWaitTarget = false;
            ResetSampleRoomWaitTimeout(job);

            WalkComponent walk = porter.GetComponent<WalkComponent>();
            if (job.Cart != null &&
                walk != null &&
                job.Cart.m_state.m_moving &&
                job.Cart.User == porter)
            {
                BeginCartReturnOrWait(
                    porter,
                    nurse,
                    job,
                    timedOut);
                return;
            }

            if (!TryRestoreCartWithRetry(
                    porter,
                    nurse,
                    job))
            {
                return;
            }

            BeginStationReturn(
                porter,
                nurse,
                job,
                timedOut);
        }

        private static void FinalizeJob(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job,
            bool timedOut)
        {
            HideSampleRoomWaitingBubble(porter, job);
            ClearCartParkingTarget(job);
            if (job != null)
            {
                job.PostDeliveryReturn = false;
                job.HasStationReturnTarget = false;
            }
            Jobs.Remove(porter);
            NextOrphanedCartRecoveryTimes.Remove(porter);
            if (nurse != null)
            {
                nurse.SwitchState(NurseState.Idle);
            }

        }

        private static void CancelJob(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job,
            bool revertSample)
        {
            HideSampleRoomWaitingBubble(porter, job);
            ClearCartParkingTarget(job);
            if (job != null)
            {
                job.PostDeliveryReturn = false;
                job.HasStationReturnTarget = false;
            }

            UseComponent use =
                porter == null
                    ? null
                    : porter.GetComponent<UseComponent>();
            if (use != null &&
                (use.m_state.m_reservedObject != null ||
                 use.IsUsing()))
            {
                use.Interrupt();
            }

            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            if (walk != null)
            {
                walk.Stop();
            }

            SpeechComponent speech =
                porter == null
                    ? null
                    : porter.GetComponent<SpeechComponent>();
            if (speech != null)
            {
                speech.HideBubble();
            }

            int requeued =
                RequeueUndeliveredSamples(
                    job,
                    revertSample);

            TileObject cart =
                job == null
                    ? null
                    : job.Cart;

            PorterDiagnostics.Log(
                "sample cancelled: porter=" +
                (porter == null
                    ? "UNKNOWN"
                    : (porter.Name ?? string.Empty).Trim()) +
                "; phase=" +
                (job == null
                    ? "NONE"
                    : job.Phase.ToString()) +
                "; requeued=" +
                requeued +
                "; cart=" +
                (cart != null) +
                "; cartPosition=" +
                (cart == null
                    ? "NONE"
                    : cart.m_state.m_position.ToString()) +
                "; cartFloor=" +
                (cart == null
                    ? -1
                    : cart.GetFloorIndex()) +
                "; cartMoving=" +
                (cart != null && cart.m_state.m_moving) +
                "; cartAttached=" +
                (cart != null && cart.IsAttachedToCharacter()) +
                "; userMatches=" +
                (cart != null && cart.User == porter) +
                ".");

            if (job == null || cart == null)
            {
                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    timedOut: false);
                return;
            }

            if (cart.User != porter)
            {
                FinalizeJob(
                    porter,
                    nurse,
                    job,
                    timedOut: false);
                return;
            }

            if (walk == null)
            {
                if (TryRestoreCartWithRetry(
                        porter,
                        nurse,
                        job))
                {
                    FinalizeJob(
                        porter,
                        nurse,
                        job,
                        timedOut: false);
                }
                return;
            }

            if (cart.m_state.m_moving &&
                cart.IsAttachedToCharacter())
            {
                BeginCartReturnOrWait(
                    porter,
                    nurse,
                    job,
                    timedOut: false);

                PorterDiagnostics.Log(
                    "cart cancel recovery started from attached cart: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; home=" +
                    job.CartHomeTile +
                    "; homeFloor=" +
                    job.CartHomeFloorIndex +
                    "; phase=" +
                    job.Phase +
                    ".");
                return;
            }

            if (!cart.m_state.m_moving)
            {
                bool alreadyHome =
                    cart.GetFloorIndex() ==
                        job.CartHomeFloorIndex &&
                    cart.m_state.m_position ==
                        job.CartHomeTile;

                if (alreadyHome)
                {
                    if (TryRestoreCartWithRetry(
                            porter,
                            nurse,
                            job))
                    {
                        FinalizeJob(
                            porter,
                            nurse,
                            job,
                            timedOut: false);
                        return;
                    }

                    job.RouteRecoveryAttempts = 0;
                    job.Phase =
                        PorterSampleTransportPhase.ReturningCart;
                    walk.SetDestination(
                        job.CartHomeTile,
                        job.CartHomeFloorIndex);
                    return;
                }

                job.RouteRecoveryAttempts = 0;
                job.Phase =
                    PorterSampleTransportPhase
                        .RecoveringCartAfterCancel;
                walk.SetDestination(
                    ToTile(cart.GetDefaultUsePosition()),
                    cart.GetFloorIndex());

                PorterDiagnostics.Log(
                    "cart cancel recovery walking back to parked cart: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; cartPosition=" +
                    cart.m_state.m_position +
                    "; cartFloor=" +
                    cart.GetFloorIndex() +
                    ".");
                return;
            }

            BeginCartReturnOrWait(
                porter,
                nurse,
                job,
                timedOut: false);

            PorterDiagnostics.Log(
                "cart cancel recovery returning moving cart: porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; home=" +
                job.CartHomeTile +
                "; homeFloor=" +
                job.CartHomeFloorIndex +
                "; phase=" +
                job.Phase +
                ".");
        }

        private static int RequeueUndeliveredSamples(
            PorterSampleTransportJob job,
            bool includeCurrent)
        {
            if (job == null)
            {
                return 0;
            }

            HashSet<LabProcedure> procedures = new HashSet<LabProcedure>();
            for (int i = 0; i < job.CollectedProcedures.Count; i++)
            {
                if (job.CollectedProcedures[i] != null)
                {
                    procedures.Add(job.CollectedProcedures[i]);
                }
            }
            if (includeCurrent && job.Procedure != null)
            {
                procedures.Add(job.Procedure);
            }

            int requeued = 0;
            foreach (LabProcedure procedure in procedures)
            {
                if (procedure == null ||
                    procedure.m_state == null ||
                    procedure.m_state.m_labProcedureState != LabProcedureState.SampleBeingDelivered)
                {
                    continue;
                }

                procedure.SwitchState(LabProcedureState.SamplingFinished);
                ClearFallbackTracking(procedure);
                requeued++;
            }
            return requeued;
        }

        private static void RecoverOrphanedUseReservation(
            Entity porter,
            BehaviorNurse nurse)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                nurse.m_state.m_nurseState != NurseState.Idle)
            {
                return;
            }

            UseComponent use = porter.GetComponent<UseComponent>();
            if (use == null ||
                use.m_state == null ||
                (use.m_state.m_reservedObject == null && !use.IsUsing()))
            {
                return;
            }

            // Inspect ownership before touching vanilla UseComponent state.
            // UseComponent.Interrupt() unconditionally clears the User of its
            // reserved object, so it must never be called while this Porter's
            // stale pointer references an object now owned by another entity.
            TileObject reservedObject =
                use.m_state.m_reservedObject == null
                    ? null
                    : use.m_state.m_reservedObject.GetEntity();
            TileObject activeObject =
                use.m_state.m_object == null
                    ? null
                    : use.m_state.m_object.GetEntity();
            Entity reservedUser =
                reservedObject == null
                    ? null
                    : reservedObject.User;
            Entity activeUser =
                activeObject == null
                    ? null
                    : activeObject.User;

            string reservedId =
                reservedObject == null ||
                reservedObject.m_state == null ||
                reservedObject.m_state.m_gameDBObject == null ||
                reservedObject.m_state.m_gameDBObject.Entry == null
                    ? "NONE"
                    : reservedObject.m_state.m_gameDBObject.Entry.DatabaseID.ToString();
            string activeId =
                activeObject == null ||
                activeObject.m_state == null ||
                activeObject.m_state.m_gameDBObject == null ||
                activeObject.m_state.m_gameDBObject.Entry == null
                    ? "NONE"
                    : activeObject.m_state.m_gameDBObject.Entry.DatabaseID.ToString();
            string useState =
                use.m_state.m_useComponentState.ToString();

            bool reservedObjectWasPorter =
                reservedObject != null &&
                reservedUser == porter;
            bool activeObjectWasPorter =
                activeObject != null &&
                activeUser == porter;
            bool staleForeignOrUnownedReservation =
                reservedObject != null &&
                reservedUser != porter;

            if (staleForeignOrUnownedReservation)
            {
                string previousReservedUser =
                    reservedUser == null
                        ? "NONE"
                        : reservedUser.GetType().Name + ":" +
                          (reservedUser.Name ?? string.Empty).Trim() +
                          "#" + reservedUser.GetEntityID();

                // Clear only this Porter's stale persistent pointer. Do not
                // mutate TileObject.User: it may belong to another employee.
                use.m_state.m_reservedObject = null;

                bool interruptedOwnActiveUse = false;
                bool foreignActiveUsePreserved =
                    activeObject != null &&
                    activeUser != null &&
                    activeUser != porter;

                if (use.IsUsing() &&
                    !foreignActiveUsePreserved)
                {
                    // Safe after detaching the stale reserved pointer: the
                    // remaining active object is either ours or absent.
                    use.Interrupt();
                    interruptedOwnActiveUse = true;
                }

                PorterDiagnostics.Log(
                    "sample recovery: porter=" +
                    (porter.Name ?? string.Empty).Trim() +
                    "; action=cleared stale reservation pointer" +
                    "; preservedReservedObjectUser=True" +
                    "; previousUseState=" + useState +
                    "; reservedObject=" + reservedId +
                    "; reservedObjectUser=" +
                    previousReservedUser +
                    "; activeObject=" + activeId +
                    "; activeObjectUserWasPorter=" +
                    activeObjectWasPorter +
                    "; interruptedOwnActiveUse=" +
                    interruptedOwnActiveUse +
                    "; foreignActiveUsePreserved=" +
                    foreignActiveUsePreserved +
                    ".");
                return;
            }

            // The reservation is still owned by this Porter (or only an active
            // use remains), so vanilla Interrupt() is safe and remains the
            // preferred cleanup path.
            use.Interrupt();

            PorterDiagnostics.Log(
                "sample recovery: porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; action=cleared owned orphaned use state" +
                "; previousUseState=" + useState +
                "; reservedObject=" + reservedId +
                "; activeObject=" + activeId +
                "; previousReservedObjectUserWasPorter=" +
                reservedObjectWasPorter +
                "; previousActiveObjectUserWasPorter=" +
                activeObjectWasPorter +
                ".");
        }

        private static bool IsProcedureClaimed(LabProcedure procedure)
        {
            if (procedure == null)
            {
                return false;
            }

            foreach (KeyValuePair<Entity, PorterSampleTransportJob> pair in Jobs)
            {
                PorterSampleTransportJob job =
                    pair.Value;
                if (job == null)
                {
                    continue;
                }

                if (AreSameSampleBundle(
                        job.Procedure,
                        procedure))
                {
                    return true;
                }

                for (int i = 0;
                    i < job.CollectedProcedures.Count;
                    i++)
                {
                    if (AreSameSampleBundle(
                            job.CollectedProcedures[i],
                            procedure))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static float GetDeliveryTimeoutHours(LabProcedure procedure)
        {
            return IsProcedureClaimed(procedure) ||
                IsSampleBundleHandledByLabSpecialist(
                    procedure)
                ? float.MaxValue
                : 4f;
        }

        private static bool IsHandledByLabSpecialist(LabProcedure procedure)
        {
            if (IsSampleBundleHandledByLabSpecialist(
                    procedure))
            {
                return true;
            }

            if (Hospital.Instance == null ||
                Hospital.Instance.m_characters == null)
            {
                return false;
            }

            foreach (Entity character in Hospital.Instance.m_characters)
            {
                BehaviorLabSpecialist lab =
                    character == null
                        ? null
                        : character.GetComponent<BehaviorLabSpecialist>();
                if (lab != null &&
                    lab.m_state.m_currentLabProcedure != null &&
                    lab.m_state.m_currentLabProcedure.GetEntity() == procedure)
                {
                    return true;
                }
            }
            return false;
        }

        private static void PlaySampleInteractionSound(
            Entity porter,
            string soundEvent)
        {
            if (porter == null || string.IsNullOrEmpty(soundEvent))
            {
                return;
            }

            SoundSourceComponent sound =
                porter.GetComponent<SoundSourceComponent>();
            if (sound != null)
            {
                sound.PlaySoundEvent(soundEvent, SoundEventCategory.SFX);
            }
        }

        private static bool IsOwnSampleRoomWaitingBubble(
            SpeechComponent speech)
        {
            SpeechComponentPersistentData state =
                speech == null
                    ? null
                    : speech.GetPersistentData() as SpeechComponentPersistentData;

            return state != null &&
                state.m_speechBubble == null &&
                state.m_bubbleTextureIndex == SampleRoomWaitBubbleIndex &&
                state.m_bubbleIconIndex == -1 &&
                state.m_bubbleIconAssetID == null;
        }

        private static void ShowSampleRoomWaitingBubble(
            Entity porter,
            PorterSampleTransportJob job)
        {
            if (porter == null || job == null)
            {
                return;
            }

            SpeechComponent speech = porter.GetComponent<SpeechComponent>();
            if (speech == null)
            {
                return;
            }

            if (job.ShowingSampleRoomWaitingBubble &&
                IsOwnSampleRoomWaitingBubble(speech))
            {
                return;
            }

            // Never replace native speech. Resume after its bubble expires.
            job.ShowingSampleRoomWaitingBubble = false;
            if (speech.IsActive())
            {
                return;
            }

            speech.SetBubble(SampleRoomWaitBubbleIndex);
            job.ShowingSampleRoomWaitingBubble = true;
        }

        private static void HideSampleRoomWaitingBubble(
            Entity porter,
            PorterSampleTransportJob job)
        {
            if (job == null || !job.ShowingSampleRoomWaitingBubble)
            {
                return;
            }

            SpeechComponent speech =
                porter == null ? null : porter.GetComponent<SpeechComponent>();
            if (IsOwnSampleRoomWaitingBubble(speech))
            {
                speech.HideBubble();
            }

            job.ShowingSampleRoomWaitingBubble = false;
        }

        private static void ShowSampleBubble(Entity porter, LabProcedure procedure)
        {
            if (porter == null || procedure == null || procedure.m_state == null)
            {
                return;
            }

            GameDBExamination testing =
                procedure.m_state.m_examination.Entry.LabTestingExaminationRef.Entry;
            SpeechComponent speech = porter.GetComponent<SpeechComponent>();
            if (testing == null || speech == null)
            {
                return;
            }

            if (testing.CustomIconBigAssetRef != null)
            {
                speech.SetBubble(
                    "BUBBLE_EMPTY",
                    testing.CustomIconBigAssetRef.XmlID,
                    4f);
            }
            else
            {
                speech.SetBubble("BUBBLE_EMPTY", testing.IconIndex, 4f);
            }
        }
    }

    [HarmonyPatch(
        typeof(Department),
        nameof(Department.OnDayStart),
        new Type[] { })]
    internal static class PorterSampleCartNotificationDayResetPatch
    {
        private static void Postfix(Department __instance)
        {
            PorterSampleTransportRuntime.ResetMissingCartNotification(
                __instance);
            PorterSampleTransportRuntime
                .ResetSamplePorterCapacityNotification(
                    __instance);
            StretcherDestinationPatch
                .ResetPatientPorterCapacityNotification(
                    __instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.Update),
        new Type[] { typeof(float) })]
    internal static class PorterSampleTransportNurseUpdatePatch
    {
        private static bool Prefix(
            BehaviorNurse __instance,
            ref bool __state)
        {
            __state =
                PorterPatientContinuationRuntime.UpdateActiveReturn(__instance) ||
                PorterSampleTransportRuntime.UpdateActiveMission(__instance);
            return !__state;
        }

        private static void Postfix(
            BehaviorNurse __instance,
            bool __state)
        {
            PorterPatientContinuationRuntime.OnNurseUpdated(__instance);
            if (!__state)
            {
                if (PorterNeedsRuntime.TryStartNativeIdleActivity(
                        __instance))
                {
                    return;
                }

                PorterSampleTransportRuntime.TryAssignFromIdlePorter(
                    __instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(BehaviorLabSpecialist),
        "SelectNextLabProcedureStep",
        new Type[] { typeof(LabProcedure) })]
    internal static class PorterSampleTransportLabFallbackPatch
    {
        private static bool Prefix(
            BehaviorLabSpecialist __instance,
            LabProcedure procedure,
            ref bool __result,
            ref bool __state)
        {
            __state = false;

            if (PorterSampleTransportRuntime
                    .IsNativeLabSampleBundleClaimedByOther(
                        __instance,
                        procedure))
            {
                __result = false;
                return false;
            }

            if (PorterSampleTransportRuntime
                    .ShouldDeferLabFallback(
                        procedure))
            {
                __result = false;
                return false;
            }

            __state =
                PorterSampleTransportRuntime
                    .IsNativeLabSamplePickupCandidate(
                        procedure);
            return true;
        }

        private static void Postfix(
            BehaviorLabSpecialist __instance,
            LabProcedure procedure,
            bool __result,
            bool __state)
        {
            if (!__state ||
                !__result)
            {
                return;
            }

            PorterSampleTransportRuntime
                .RegisterNativeLabSampleBundle(
                    __instance,
                    procedure);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorLabSpecialist),
        "UpdateStateSampleFromPatientStored",
        new Type[] { })]
    internal static class PorterNativeLabSampleStoredPatch
    {
        private static bool Prefix(
            BehaviorLabSpecialist __instance)
        {
            if (__instance == null)
            {
                return true;
            }

            UseComponent use =
                __instance.GetComponent<UseComponent>();
            if (use == null ||
                use.IsBusy())
            {
                return true;
            }

            return !PorterSampleTransportRuntime
                .CompleteNativeLabSampleBundle(
                    __instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorLabSpecialist),
        nameof(BehaviorLabSpecialist.Update),
        new Type[] { typeof(float) })]
    internal static class PorterNativeLabSampleBundleCleanupPatch
    {
        private static void Postfix(
            BehaviorLabSpecialist __instance)
        {
            PorterSampleTransportRuntime
                .CleanupNativeLabSampleBundle(
                    __instance);
        }
    }

    [HarmonyPatch(
        typeof(LabProcedure),
        nameof(LabProcedure.SwitchState),
        new Type[] { typeof(LabProcedureState) })]
    internal static class PorterSampleDeliveryOutcomePatch
    {
        private static void Prefix(
            LabProcedure __instance,
            ref LabProcedureState __state)
        {
            __state = __instance == null ||
                __instance.m_state == null
                    ? LabProcedureState.Finished
                    : __instance.m_state.m_labProcedureState;
        }

        private static void Postfix(
            LabProcedure __instance,
            LabProcedureState __state)
        {
            if (__instance != null &&
                __instance.m_state != null &&
                __state != LabProcedureState.SamplingFinished &&
                __instance.m_state.m_labProcedureState ==
                    LabProcedureState.SamplingFinished)
            {
                PorterSampleTransportRuntime
                    .LogSamplePickupEligibilityAtSamplingFinished(
                        __instance,
                        __state);
            }

            if (__instance != null &&
                __instance.m_state != null &&
                __state != LabProcedureState.DeliveredToStatLab &&
                __instance.m_state.m_labProcedureState ==
                    LabProcedureState.DeliveredToStatLab)
            {
                PorterSampleTransportRuntime
                    .LogLabProcedureDelivered(
                        __instance,
                        "SwitchState");
            }
        }
    }

    [HarmonyPatch(
        typeof(LabProcedure),
        nameof(LabProcedure.Update),
        new Type[] { typeof(int), typeof(float) })]
    internal static class PorterSampleTransportRecoveryPatch
    {
        private static void Prefix(
            LabProcedure __instance,
            ref LabProcedureState __state)
        {
            PorterSampleTransportRuntime.RecoverOrphanedDelivery(__instance);
            __state = __instance == null ||
                __instance.m_state == null
                    ? LabProcedureState.Finished
                    : __instance.m_state.m_labProcedureState;
        }

        private static void Postfix(
            LabProcedure __instance,
            LabProcedureState __state)
        {
            if (__instance != null &&
                __instance.m_state != null &&
                __state == LabProcedureState.SampleBeingDelivered &&
                __instance.m_state.m_labProcedureState ==
                    LabProcedureState.DeliveredToStatLab)
            {
                PorterSampleTransportRuntime
                    .LogLabProcedureDelivered(
                        __instance,
                        "vanilla-update-timeout");
            }

            PorterSampleTransportRuntime.CleanupFallbackTracking(__instance);
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo conversion = AccessTools.Method(
                typeof(DayTime),
                nameof(DayTime.IngameTimeHoursToRealTimeSeconds),
                new Type[] { typeof(float) });
            MethodInfo timeoutHelper = AccessTools.Method(
                typeof(PorterSampleTransportRuntime),
                nameof(PorterSampleTransportRuntime.GetDeliveryTimeoutHours),
                new Type[] { typeof(LabProcedure) });

            if (conversion == null || timeoutHelper == null)
            {
                Plugin.Log?.LogWarning(
                    "Porter sample timeout patch could not resolve the native conversion/helper; vanilla 4-hour timeout remains active.");
                return codes;
            }

            int matchIndex = -1;
            int matches = 0;
            for (int i = 0; i + 1 < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldc_R4 ||
                    !(codes[i].operand is float) ||
                    (float)codes[i].operand != 4f)
                {
                    continue;
                }

                MethodInfo called = codes[i + 1].operand as MethodInfo;
                if (called != conversion)
                {
                    continue;
                }

                matchIndex = i;
                matches++;
            }

            if (matches != 1)
            {
                Plugin.Log?.LogWarning(
                    "Porter sample timeout patch expected one native 4-hour delivery threshold but found " +
                    matches + "; vanilla timeout remains unchanged.");
                return codes;
            }

            CodeInstruction loadProcedure = new CodeInstruction(OpCodes.Ldarg_0);
            loadProcedure.labels.AddRange(codes[matchIndex].labels);
            codes[matchIndex].labels.Clear();
            codes[matchIndex] = loadProcedure;
            codes.Insert(matchIndex + 1, new CodeInstruction(OpCodes.Call, timeoutHelper));

            return codes;
        }
    }


}
