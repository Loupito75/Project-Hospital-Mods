using System;
using System.Collections.Generic;
using System.Globalization;
using GLib;
using Lopital;

namespace HospitalShiftHandover
{
    internal static class PreShiftLockerInteractionTest
    {
        private const bool Enabled = true;
        private const float MinimumSlackMinutes = 8f;

        private const int StableDecisionArrivalDressing = 1;
        private const int StableDecisionOutgoingDressing = 2;
        private const int StableDecisionCivilianStyle = 3;

        private static readonly string[] CasualStyleIds =
        {
            "CLTHSTL_MALE_CASUAL",
            "CLTHSTL_FEMALE_CASUAL"
        };

        private static readonly string[] BusinessStyleIds =
        {
            "CLTHSTL_MALE_BUSINESS",
            "CLTHSTL_FEMALE_BUSINESS"
        };

        private enum TestPhase
        {
            GoingToLocker,
            OpeningLocker,
            UsingLocker,
            ClosingLocker,
            WaitingForAccessClear
        }

        private sealed class TestState
        {
            internal int Stamp;
            internal TileObject Locker;
            internal Vector2i AccessPosition;
            internal TestPhase Phase;
        }

        private enum OutgoingPhase
        {
            GoingToCommonRoom,
            GoingToLocker,
            OpeningLocker,
            UsingLocker,
            ClosingLocker,
            WaitingForAccessClear
        }

        private sealed class OutgoingState
        {
            internal int Stamp;
            internal Room CommonRoom;
            internal TileObject Locker;
            internal Vector2i AccessPosition;
            internal OutgoingPhase Phase;
        }

        private sealed class CivilianState
        {
            internal int Stamp;
            internal string Family;
            internal string DefaultStyleId;
            internal bool FromOutgoing;
        }

        private static readonly Dictionary<EmployeeComponent, TestState> States =
            new Dictionary<EmployeeComponent, TestState>();

        private static readonly Dictionary<EmployeeComponent, CivilianState> CivilianStates =
            new Dictionary<EmployeeComponent, CivilianState>();

        private static readonly Dictionary<EmployeeComponent, OutgoingState> OutgoingStates =
            new Dictionary<EmployeeComponent, OutgoingState>();

        private static readonly Dictionary<EmployeeComponent, int> OutgoingCompletedStamps =
            new Dictionary<EmployeeComponent, int>();


        private static readonly Dictionary<EmployeeComponent, int> CompletedStamps =
            new Dictionary<EmployeeComponent, int>();

        private static readonly Dictionary<EmployeeComponent, int> NoLockerLoggedStamps =
            new Dictionary<EmployeeComponent, int>();

        private static readonly Dictionary<EmployeeComponent, int> WorkplaceFallbackStamps =
            new Dictionary<EmployeeComponent, int>();

        private static EmployeeComponent ExplicitProfessionalRestoreEmployee;

        internal static bool ShouldAllowRevertToDefaultClothes(
            AnimModelComponent anim,
            bool colorsOnly)
        {
            if (!Enabled ||
                !ShiftHandoverConfig.DressingEnabled ||
                colorsOnly ||
                anim == null ||
                anim.m_state == null ||
                anim.m_entity == null ||
                DayTime.Instance == null)
            {
                return true;
            }

            EmployeeComponent employee =
                anim.m_entity.GetComponent<EmployeeComponent>();
            if (employee == null ||
                employee == ExplicitProfessionalRestoreEmployee)
            {
                return true;
            }

            CivilianState state;
            if (!CivilianStates.TryGetValue(employee, out state) ||
                state == null ||
                state.Stamp != GetStamp(employee))
            {
                return true;
            }

            if (WorldEventManager.Instance != null &&
                WorldEventManager.Instance.HasEventForcingBiohazardClothes())
            {
                if (state.FromOutgoing)
                {
                    CivilianStates.Remove(employee);
                }
                else
                {
                    AbandonForForcedBiohazardClothes(employee);
                }
                return true;
            }

            WalkComponent walk =
                anim.m_entity.GetComponent<WalkComponent>();
            if (walk != null && walk.IsOnBiohazard())
            {
                if (state.FromOutgoing)
                {
                    CivilianStates.Remove(employee);
                }
                return true;
            }

            string currentStyleId =
                GetClothingStyleId(anim.m_state.m_clothes);
            if (GetCivilianFamily(currentStyleId) == null)
            {
                if (state.FromOutgoing)
                {
                    CivilianStates.Remove(employee);
                }
                return true;
            }

            Log(
                employee,
                "CIVILIAN_REVERT_BLOCKED",
                "style=" + currentStyleId +
                " | source=vanilla-revert");
            return false;
        }

        internal static bool PrepareCivilianArrival(
            Behavior behavior,
            EmployeeComponent employee,
            Room destinationCommonRoom)
        {
            if (!Enabled ||
                behavior == null ||
                employee == null ||
                employee.m_state == null ||
                destinationCommonRoom == null ||
                DayTime.Instance == null)
            {
                return false;
            }

            int stamp = GetStamp(employee);
            if (HasResolvedDressingForCurrentShift(employee))
            {
                return false;
            }

            CivilianState existing;
            if (CivilianStates.TryGetValue(employee, out existing))
            {
                if (existing != null && existing.Stamp == stamp)
                {
                    if (IsCivilianStateVisible(employee, existing))
                    {
                        return true;
                    }

                    AbandonLostCivilianState(
                        employee,
                        stamp,
                        "prepare");
                    return false;
                }

                CivilianStates.Remove(employee);
            }

            if (TryRecoverCivilianState(employee, stamp))
            {
                return true;
            }

            if (!RoomHasUsableLocker(destinationCommonRoom))
            {
                return false;
            }

            WalkComponent walk = behavior.GetComponent<WalkComponent>();
            AnimModelComponent anim = behavior.GetComponent<AnimModelComponent>();
            if (walk == null ||
                anim == null ||
                anim.m_state == null ||
                anim.m_state.m_defaultClothes == null ||
                WorldEventManager.Instance == null ||
                WorldEventManager.Instance.HasEventForcingBiohazardClothes() ||
                walk.IsOnBiohazard())
            {
                return false;
            }

            int civilianStyleRoll =
                GetStableDecisionValue(
                    employee,
                    StableDecisionCivilianStyle,
                    100);
            bool useCasual =
                civilianStyleRoll <
                ShiftHandoverConfig.CasualClothesPercent;
            LogStableDecision(
                employee,
                "style",
                civilianStyleRoll,
                ShiftHandoverConfig.CasualClothesPercent,
                useCasual ? "casual" : "business");
            string[] styles = useCasual ? CasualStyleIds : BusinessStyleIds;

            if (!ClothingStylesExist(styles))
            {
                Log(
                    employee,
                    "CLOTHING_SKIP",
                    "reason=civilian-style-missing");
                return false;
            }

            string defaultStyleId =
                GetClothingStyleId(anim.m_state.m_defaultClothes);

            anim.ForceClothingStyle(styles, null);

            string appliedStyleId =
                GetClothingStyleId(anim.m_state.m_clothes);
            if (string.IsNullOrEmpty(appliedStyleId))
            {
                anim.RevertToDefaultClothes();
                Log(
                    employee,
                    "CLOTHING_SKIP",
                    "reason=civilian-style-not-applied");
                return false;
            }

            CivilianStates[employee] = new CivilianState
            {
                Stamp = stamp,
                Family = useCasual ? "casual" : "business",
                DefaultStyleId = defaultStyleId
            };

            Log(
                employee,
                "CIVILIAN",
                "family=" + (useCasual ? "casual" : "business") +
                " | style=" + appliedStyleId +
                " | default=" + defaultStyleId);
            return true;
        }

        internal static bool TryHandleOutgoing(Behavior behavior)
        {
            if (!Enabled ||
                !ShiftHandoverConfig.DressingEnabled ||
                behavior == null ||
                DayTime.Instance == null)
            {
                return false;
            }

            EmployeeComponent employee =
                behavior.GetComponent<EmployeeComponent>();
            if (employee == null ||
                employee.m_state == null ||
                employee.m_entity == null ||
                employee.IsFired())
            {
                return false;
            }

            int stamp = GetStamp(employee);

            // Incoming employees already inside during the pre-shift hold window are
            // also technically off-shift. They belong to the arrival pipeline, never
            // to the outgoing dressing pipeline.
            if (HandoverRules.ShouldHoldBeforeShift(employee))
            {
                return false;
            }

            if (DayTime.Instance.GetShift() == employee.m_state.m_shift)
            {
                OutgoingCompletedStamps.Remove(employee);
                return false;
            }

            BehaviorDoctor doctor = behavior as BehaviorDoctor;
            if (doctor != null && doctor.CurrentPatient != null)
            {
                return false;
            }

            ProcedureComponent procedure =
                behavior.GetComponent<ProcedureComponent>();
            if (procedure != null && procedure.IsBusy())
            {
                return false;
            }

            int completedStamp;
            if (OutgoingCompletedStamps.TryGetValue(
                    employee,
                    out completedStamp))
            {
                if (completedStamp == stamp)
                {
                    return false;
                }

                OutgoingCompletedStamps.Remove(employee);
            }

            OutgoingState state;
            if (OutgoingStates.TryGetValue(employee, out state) &&
                (state == null || state.Stamp != stamp))
            {
                CancelOutgoing(employee, state, "stamp-changed");
                state = null;
            }

            WalkComponent walk =
                behavior.GetComponent<WalkComponent>();
            if (walk == null || walk.Floor == null)
            {
                MarkOutgoingComplete(
                    employee,
                    state,
                    "OUT_SKIP",
                    "reason=walk-unavailable");
                return false;
            }

            bool forcedBiohazard =
                WorldEventManager.Instance != null &&
                WorldEventManager.Instance.HasEventForcingBiohazardClothes();
            if (forcedBiohazard || walk.IsOnBiohazard())
            {
                CivilianState outgoingCivilian;
                if (CivilianStates.TryGetValue(
                        employee,
                        out outgoingCivilian) &&
                    outgoingCivilian != null &&
                    outgoingCivilian.FromOutgoing)
                {
                    CivilianStates.Remove(employee);
                }

                MarkOutgoingComplete(
                    employee,
                    state,
                    "OUT_SKIP",
                    forcedBiohazard
                        ? "reason=forced-biohazard-clothes"
                        : "reason=biohazard-tile");
                return false;
            }

            if (state == null)
            {
                Room commonRoom;
                Vector2i commonAreaPosition;
                int commonAreaFloor;
                if (!TrySelectOutgoingCommonRoom(
                        behavior,
                        employee,
                        out commonRoom,
                        out commonAreaPosition,
                        out commonAreaFloor))
                {
                    OutgoingCompletedStamps[employee] = stamp;
                    LogOutgoing(
                        employee,
                        "OUT_SKIP",
                        "reason=no-common-room");
                    return false;
                }

                if (!RoomHasUsableLocker(commonRoom))
                {
                    OutgoingCompletedStamps[employee] = stamp;
                    LogOutgoing(
                        employee,
                        "OUT_SKIP",
                        "reason=no-locker-in-selected-room" +
                        " | floor=" +
                        commonAreaFloor.ToString(
                            CultureInfo.InvariantCulture));
                    return false;
                }

                if (!ShouldUseOutgoingDressing(employee))
                {
                    OutgoingCompletedStamps[employee] = stamp;
                    LogOutgoing(
                        employee,
                        "OUT_SKIP",
                        "reason=dressing-chance");
                    return false;
                }

                walk.SetDestination(
                    new Vector2f(
                        commonAreaPosition.m_x,
                        commonAreaPosition.m_y),
                    commonAreaFloor);

                state = new OutgoingState
                {
                    Stamp = stamp,
                    CommonRoom = commonRoom,
                    Phase = OutgoingPhase.GoingToCommonRoom
                };
                OutgoingStates[employee] = state;

                LogOutgoing(
                    employee,
                    "OUT_ROUTE",
                    "target=" +
                    commonAreaPosition.m_x.ToString(
                        CultureInfo.InvariantCulture) +
                    "," +
                    commonAreaPosition.m_y.ToString(
                        CultureInfo.InvariantCulture) +
                    " | floor=" +
                    commonAreaFloor.ToString(
                        CultureInfo.InvariantCulture));
                return true;
            }

            if (state.Phase == OutgoingPhase.WaitingForAccessClear)
            {
                return false;
            }

            if (state.Phase == OutgoingPhase.GoingToCommonRoom)
            {
                if (walk.IsBusy())
                {
                    return true;
                }

                Room currentRoom =
                    MapScriptInterface.Instance != null
                        ? MapScriptInterface.Instance.GetRoomAt(
                            walk.GetCurrentTile(),
                            walk.GetFloorIndex())
                        : null;
                if (currentRoom != state.CommonRoom)
                {
                    MarkOutgoingComplete(
                        employee,
                        state,
                        "OUT_SKIP",
                        "reason=common-room-route-ended-away");
                    return false;
                }

                TileObject locker = FindClosestFreeLocker(walk);
                if (locker == null)
                {
                    bool roomStillHasLocker =
                        RoomHasUsableLocker(currentRoom);
                    MarkOutgoingComplete(
                        employee,
                        state,
                        roomStillHasLocker
                            ? "OUT_LOCKER_BUSY"
                            : "OUT_SKIP",
                        roomStillHasLocker
                            ? "reason=no-free-locker-no-wait"
                            : "reason=locker-removed");
                    return false;
                }

                UseComponent use =
                    behavior.GetComponent<UseComponent>();
                AnimModelComponent anim =
                    behavior.GetComponent<AnimModelComponent>();
                Entity owner = employee.m_entity;
                if (use == null ||
                    anim == null ||
                    owner == null ||
                    use.IsBusy())
                {
                    MarkOutgoingComplete(
                        employee,
                        state,
                        "OUT_SKIP",
                        "reason=locker-runtime-unavailable");
                    return false;
                }

                Vector2i accessPosition;
                if (!TryGetLockerAccessPosition(
                        locker,
                        out accessPosition) ||
                    !locker.CanBeAccessedFrom(
                        accessPosition,
                        walk.Floor) ||
                    !HasLockerUseAnimations(locker))
                {
                    MarkOutgoingComplete(
                        employee,
                        state,
                        "OUT_SKIP",
                        "reason=locker-interaction-data-unavailable");
                    return false;
                }

                use.ReserveObject(locker);
                if (!ReserveAccessTile(
                        owner,
                        accessPosition,
                        locker.GetFloorIndex()))
                {
                    use.Interrupt();
                    MarkOutgoingComplete(
                        employee,
                        state,
                        "OUT_LOCKER_BUSY",
                        "reason=access-tile-reserved-no-wait");
                    return false;
                }

                walk.SetDestination(
                    accessPosition,
                    locker.GetFloorIndex());
                state.Locker = locker;
                state.AccessPosition = accessPosition;
                state.Phase = OutgoingPhase.GoingToLocker;

                LogOutgoing(
                    employee,
                    "OUT_START",
                    "locker=" + GetObjectId(locker) +
                    " | access=" +
                    accessPosition.m_x.ToString(
                        CultureInfo.InvariantCulture) +
                    "," +
                    accessPosition.m_y.ToString(
                        CultureInfo.InvariantCulture) +
                    " | floor=" +
                    locker.GetFloorIndex().ToString(
                        CultureInfo.InvariantCulture));
                return true;
            }

            TileObject activeLocker = state.Locker;
            UseComponent activeUse =
                behavior.GetComponent<UseComponent>();
            AnimModelComponent activeAnim =
                behavior.GetComponent<AnimModelComponent>();
            Entity activeOwner = employee.m_entity;

            if (activeLocker == null ||
                !activeLocker.IsValid() ||
                activeUse == null ||
                activeAnim == null ||
                activeOwner == null)
            {
                MarkOutgoingComplete(
                    employee,
                    state,
                    "OUT_FAIL",
                    "reason=invalid-runtime-state");
                return false;
            }

            if (activeLocker.User != activeOwner)
            {
                MarkOutgoingComplete(
                    employee,
                    state,
                    "OUT_FAIL",
                    "reason=locker-reservation-lost");
                return false;
            }

            if (state.Phase == OutgoingPhase.GoingToLocker)
            {
                if (walk.IsBusy())
                {
                    return true;
                }

                if (walk.GetFloorIndex() !=
                        activeLocker.GetFloorIndex() ||
                    walk.GetCurrentTile() !=
                        state.AccessPosition)
                {
                    MarkOutgoingComplete(
                        employee,
                        state,
                        "OUT_FAIL",
                        "reason=path-ended-away-from-locker");
                    return false;
                }

                activeAnim.SetDirection(
                    activeLocker.GetAccessOrientation(
                        state.AccessPosition));
                activeLocker.PlayStartUseSound();
                if (!PlayLockerCharacterAnimation(
                        behavior,
                        activeLocker.m_state.m_gameDBObject.Entry
                            .DefaultUseStartAnimations,
                        false))
                {
                    MarkOutgoingComplete(
                        employee,
                        state,
                        "OUT_FAIL",
                        "reason=locker-start-animation-unavailable");
                    return false;
                }

                state.Phase = OutgoingPhase.OpeningLocker;
                LogOutgoing(
                    employee,
                    "OUT_ACTIVATE",
                    "locker=" + GetObjectId(activeLocker));
                return true;
            }

            if (state.Phase == OutgoingPhase.OpeningLocker)
            {
                if (!HasCurrentAnimationFinished(activeAnim))
                {
                    return true;
                }

                ForceLockerFrame(activeLocker, 1);
                if (!PlayLockerCharacterAnimation(
                        behavior,
                        activeLocker.m_state.m_gameDBObject.Entry
                            .DefaultUseAnimations,
                        false))
                {
                    MarkOutgoingComplete(
                        employee,
                        state,
                        "OUT_FAIL",
                        "reason=locker-use-animation-unavailable");
                    return false;
                }

                state.Phase = OutgoingPhase.UsingLocker;
                LogOutgoing(
                    employee,
                    "OUT_OPEN",
                    "locker=" + GetObjectId(activeLocker));
                return true;
            }

            if (state.Phase == OutgoingPhase.UsingLocker)
            {
                if (!HasCurrentAnimationFinished(activeAnim))
                {
                    return true;
                }

                ApplyOutgoingCivilianClothes(
                    behavior,
                    employee);

                ForceLockerFrame(activeLocker, 0);
                activeLocker.PlayEndUseSound();
                if (!PlayLockerCharacterAnimation(
                        behavior,
                        activeLocker.m_state.m_gameDBObject.Entry
                            .DefaultUseEndAnimations,
                        false))
                {
                    MarkOutgoingComplete(
                        employee,
                        state,
                        "OUT_FAIL",
                        "reason=locker-end-animation-unavailable");
                    return false;
                }

                state.Phase = OutgoingPhase.ClosingLocker;
                LogOutgoing(
                    employee,
                    "OUT_CLOSE",
                    "locker=" + GetObjectId(activeLocker));
                return true;
            }

            if (!HasCurrentAnimationFinished(activeAnim))
            {
                return true;
            }

            activeAnim.PlayAnimation("stand_idle");
            DetachLockerFromUseComponent(
                activeOwner,
                activeLocker);
            state.Phase =
                OutgoingPhase.WaitingForAccessClear;
            OutgoingCompletedStamps[employee] = stamp;

            LogOutgoing(
                employee,
                "OUT_END",
                "locker=" + GetObjectId(activeLocker) +
                " | access-held=yes");
            return false;
        }

        internal static bool ShouldRunVanillaOutgoingDeparture(
            Behavior behavior)
        {
            if (!Enabled ||
                behavior == null ||
                DayTime.Instance == null)
            {
                return false;
            }

            EmployeeComponent employee =
                behavior.GetComponent<EmployeeComponent>();
            if (employee == null ||
                employee.m_state == null ||
                employee.IsFired() ||
                HandoverRules.ShouldHoldBeforeShift(employee) ||
                DayTime.Instance.GetShift() ==
                    employee.m_state.m_shift)
            {
                return false;
            }

            int completedStamp;
            return OutgoingCompletedStamps.TryGetValue(
                       employee,
                       out completedStamp) &&
                   completedStamp == GetStamp(employee);
        }

        internal static void RestoreProfessionalAtHomeIfDressingUnavailable(
            EmployeeComponent employee)
        {
            if (!Enabled ||
                employee == null ||
                employee.m_state == null ||
                employee.m_entity == null ||
                DayTime.Instance == null ||
                (WorldEventManager.Instance != null &&
                 WorldEventManager.Instance
                     .HasEventForcingBiohazardClothes()) ||
                !IsEmployeeAtHome(employee))
            {
                return;
            }

            AnimModelComponent anim =
                employee.m_entity.GetComponent<AnimModelComponent>();
            if (anim == null ||
                anim.m_state == null ||
                anim.m_state.m_clothes == null ||
                anim.m_state.m_defaultClothes == null)
            {
                return;
            }

            string currentStyleId =
                GetClothingStyleId(anim.m_state.m_clothes);
            string defaultStyleId =
                GetClothingStyleId(anim.m_state.m_defaultClothes);
            if (currentStyleId == defaultStyleId ||
                GetCivilianFamily(currentStyleId) == null)
            {
                return;
            }

            RestoreProfessionalClothes(
                employee,
                "next-shift-no-locker");
        }

        internal static float MinimumRequiredSlackMinutes
        {
            get { return MinimumSlackMinutes; }
        }

        internal static bool ShouldUseArrivalDressing(
            EmployeeComponent employee)
        {
            if (!ShiftHandoverConfig.DressingEnabled)
            {
                return false;
            }

            int roll =
                GetStableDecisionValue(
                    employee,
                    StableDecisionArrivalDressing,
                    100);
            bool selected =
                roll <
                ShiftHandoverConfig.ArrivalDressingChancePercent;
            LogStableDecision(
                employee,
                "arrival",
                roll,
                ShiftHandoverConfig.ArrivalDressingChancePercent,
                selected ? "dressing" : "uniform");
            return selected;
        }

        private static bool ShouldUseOutgoingDressing(
            EmployeeComponent employee)
        {
            if (!ShiftHandoverConfig.DressingEnabled)
            {
                return false;
            }

            int roll =
                GetStableDecisionValue(
                    employee,
                    StableDecisionOutgoingDressing,
                    100);
            bool selected =
                roll <
                ShiftHandoverConfig.OutgoingDressingChancePercent;
            LogStableDecision(
                employee,
                "outgoing",
                roll,
                ShiftHandoverConfig.OutgoingDressingChancePercent,
                selected ? "dressing" : "uniform");
            return selected;
        }

        internal static bool IsOutgoingDressingActive(
            EmployeeComponent employee)
        {
            if (!Enabled ||
                employee == null ||
                employee.m_state == null ||
                DayTime.Instance == null)
            {
                return false;
            }

            OutgoingState state;
            return OutgoingStates.TryGetValue(
                       employee,
                       out state) &&
                   state != null &&
                   state.Stamp == GetStamp(employee) &&
                   state.Phase !=
                       OutgoingPhase.WaitingForAccessClear;
        }

        internal static bool HasActiveInteraction(
            EmployeeComponent employee)
        {
            if (!Enabled ||
                employee == null ||
                employee.m_state == null ||
                DayTime.Instance == null)
            {
                return false;
            }

            TestState state;
            if (!States.TryGetValue(employee, out state) ||
                state == null ||
                state.Stamp != GetStamp(employee))
            {
                return false;
            }

            return state.Phase != TestPhase.WaitingForAccessClear;
        }

        internal static bool HasPendingCivilianClothes(
            EmployeeComponent employee)
        {
            if (!Enabled ||
                employee == null ||
                employee.m_state == null ||
                DayTime.Instance == null)
            {
                return false;
            }

            if (WorldEventManager.Instance != null &&
                WorldEventManager.Instance.HasEventForcingBiohazardClothes())
            {
                AbandonForForcedBiohazardClothes(employee);
                return false;
            }

            int stamp = GetStamp(employee);
            CivilianState state;
            if (CivilianStates.TryGetValue(employee, out state))
            {
                if (state != null && state.Stamp == stamp)
                {
                    if (IsCivilianStateVisible(employee, state))
                    {
                        return true;
                    }

                    AbandonLostCivilianState(
                        employee,
                        stamp,
                        "pending-check");
                    return false;
                }

                CivilianStates.Remove(employee);
            }

            return TryRecoverCivilianState(employee, stamp);
        }

        internal static bool HasResolvedDressingForCurrentShift(
            EmployeeComponent employee)
        {
            if (!Enabled ||
                employee == null ||
                employee.m_state == null ||
                DayTime.Instance == null)
            {
                return false;
            }

            int stamp = GetStamp(employee);

            int completedStamp;
            if (CompletedStamps.TryGetValue(employee, out completedStamp))
            {
                if (completedStamp == stamp)
                {
                    return true;
                }

                CompletedStamps.Remove(employee);
            }

            if (IsWorkplaceFallbackActive(employee, stamp))
            {
                return true;
            }

            TestState state;
            return States.TryGetValue(employee, out state) &&
                   state != null &&
                   state.Stamp == stamp &&
                   (state.Phase == TestPhase.ClosingLocker ||
                    state.Phase == TestPhase.WaitingForAccessClear);
        }

        private static void AbandonForForcedBiohazardClothes(
            EmployeeComponent employee)
        {
            if (employee == null ||
                employee.m_state == null ||
                DayTime.Instance == null)
            {
                return;
            }

            int stamp = GetStamp(employee);
            bool hadDressingState = CivilianStates.ContainsKey(employee);

            TestState state;
            if (States.TryGetValue(employee, out state) &&
                state != null &&
                state.Stamp == stamp)
            {
                hadDressingState = true;

                Entity owner = employee.m_entity;
                WalkComponent walk =
                    owner != null
                        ? owner.GetComponent<WalkComponent>()
                        : null;

                if (walk != null &&
                    state.Phase == TestPhase.GoingToLocker)
                {
                    walk.Stop();
                }

                RestoreLockerVisual(owner, state.Locker);
                ReleaseAccessTileReservation(
                    owner,
                    state.AccessPosition,
                    state.Locker != null
                        ? state.Locker.GetFloorIndex()
                        : (walk != null ? walk.GetFloorIndex() : 0));
                ReleaseLockerReservation(owner, state.Locker);
                States.Remove(employee);
            }

            int fallbackStamp;
            if (WorkplaceFallbackStamps.TryGetValue(employee, out fallbackStamp) &&
                fallbackStamp == stamp)
            {
                hadDressingState = true;
                WorkplaceFallbackStamps.Remove(employee);
            }

            if (!hadDressingState)
            {
                return;
            }

            CivilianStates.Remove(employee);
            CompletedStamps[employee] = stamp;
            NoLockerLoggedStamps.Remove(employee);

            Log(
                employee,
                "BIOHAZARD_ABORT",
                "reason=forced-biohazard-clothes");
        }

        internal static void RecordCommonRoomArrivalPreserved(
            EmployeeComponent employee)
        {
            Log(
                employee,
                "CIVILIAN_PRESERVED",
                "trigger=common-room-arrival");
        }

        internal static void AbandonForWorkplaceFallback(
            EmployeeComponent employee,
            string reason)
        {
            if (!Enabled ||
                employee == null ||
                employee.m_state == null ||
                employee.m_entity == null ||
                DayTime.Instance == null)
            {
                return;
            }

            int stamp = GetStamp(employee);

            TestState active;
            if (States.TryGetValue(employee, out active) &&
                active != null &&
                active.Stamp == stamp &&
                active.Phase != TestPhase.WaitingForAccessClear)
            {
                return;
            }

            if (!HasPendingCivilianClothes(employee))
            {
                return;
            }

            CivilianStates.Remove(employee);
            CompletedStamps[employee] = stamp;
            WorkplaceFallbackStamps[employee] = stamp;

            Log(
                employee,
                "FALLBACK_WORKPLACE",
                "reason=" + reason +
                " | clothes=kept-until-vanilla-workplace");
        }

        internal static bool TryHandle(
            Behavior behavior,
            EmployeeComponent employee,
            float activitySlackMinutes)
        {
            if (!Enabled ||
                behavior == null ||
                employee == null ||
                employee.m_state == null ||
                DayTime.Instance == null)
            {
                return false;
            }

            if (WorldEventManager.Instance != null &&
                WorldEventManager.Instance.HasEventForcingBiohazardClothes())
            {
                AbandonForForcedBiohazardClothes(employee);
                return false;
            }

            int stamp = GetStamp(employee);
            int completedStamp;
            if (CompletedStamps.TryGetValue(employee, out completedStamp))
            {
                if (completedStamp == stamp)
                {
                    return false;
                }

                CompletedStamps.Remove(employee);
            }

            TestState state;
            if (States.TryGetValue(employee, out state))
            {
                if (state == null || state.Stamp != stamp)
                {
                    CancelActive(employee, "stamp-changed");
                    state = null;
                }
            }

            if (state == null)
            {
                if (activitySlackMinutes < MinimumSlackMinutes)
                {
                    return false;
                }

                WalkComponent walk = behavior.GetComponent<WalkComponent>();
                UseComponent use = behavior.GetComponent<UseComponent>();
                AnimModelComponent anim = behavior.GetComponent<AnimModelComponent>();
                if (walk == null || use == null || anim == null || walk.Floor == null ||
                    walk.IsBusy() || walk.IsSitting() || use.IsBusy())
                {
                    return false;
                }

                bool hasCivilianClothes =
                    HasPendingCivilianClothes(employee);

                if (!hasCivilianClothes)
                {
                    if (!PreShiftArrival.ShouldPlanLockerDressing(employee))
                    {
                        return false;
                    }

                    Room currentRoom = MapScriptInterface.Instance.GetRoomAt(
                        walk.GetCurrentTile(),
                        walk.GetFloorIndex());
                    if (!PrepareCivilianArrival(
                            behavior,
                            employee,
                            currentRoom))
                    {
                        return false;
                    }
                }

                TileObject locker = FindClosestFreeLocker(walk);
                if (locker == null)
                {
                    Room room = MapScriptInterface.Instance != null
                        ? MapScriptInterface.Instance.GetRoomAt(
                            walk.GetCurrentTile(),
                            walk.GetFloorIndex())
                        : null;

                    bool roomHasLocker = room != null && RoomHasUsableLocker(room);
                    int loggedStamp;
                    if (!NoLockerLoggedStamps.TryGetValue(employee, out loggedStamp) ||
                        loggedStamp != stamp)
                    {
                        NoLockerLoggedStamps[employee] = stamp;
                        Log(
                            employee,
                            roomHasLocker ? "LOCKER_BUSY" : "NO_LOCKER",
                            roomHasLocker
                                ? "All usable lockers in the current common room are currently reserved."
                                : "No usable OBJECT_LOCKER/ui_locker+dressing object was found in the current common room.");
                    }

                    if (!roomHasLocker)
                    {
                        AbandonForWorkplaceFallback(
                            employee,
                            "no-locker");
                    }
                    return false;
                }

                Entity owner = employee.m_entity;
                if (owner == null)
                {
                    return false;
                }

                Vector2i accessPosition;
                if (!TryGetLockerAccessPosition(locker, out accessPosition) ||
                    !locker.CanBeAccessedFrom(accessPosition, walk.Floor) ||
                    !HasLockerUseAnimations(locker))
                {
                    Log(
                        employee,
                        "FAIL",
                        "reason=locker-interaction-data-unavailable" +
                        " | locker=" + GetObjectId(locker));
                    AbandonForWorkplaceFallback(
                        employee,
                        "invalid-locker-data");
                    return false;
                }


                use.ReserveObject(locker);
                if (!ReserveAccessTile(owner, accessPosition, locker.GetFloorIndex()))
                {
                    use.Interrupt();
                    Log(
                        employee,
                        "LOCKER_BUSY",
                        "reason=access-tile-reserved" +
                        " | locker=" + GetObjectId(locker));
                    return false;
                }

                walk.SetDestination(accessPosition, locker.GetFloorIndex());

                state = new TestState
                {
                    Stamp = stamp,
                    Locker = locker,
                    AccessPosition = accessPosition,
                    Phase = TestPhase.GoingToLocker
                };
                States[employee] = state;

                Vector2i lockerTile = locker.m_state.m_position;
                Log(
                    employee,
                    "START",
                    "locker=" + GetObjectId(locker) +
                    " | lockerTile=" + lockerTile.m_x.ToString(CultureInfo.InvariantCulture) +
                    "," + lockerTile.m_y.ToString(CultureInfo.InvariantCulture) +
                    " | access=" + accessPosition.m_x.ToString(CultureInfo.InvariantCulture) +
                    "," + accessPosition.m_y.ToString(CultureInfo.InvariantCulture) +
                    " | floor=" + locker.GetFloorIndex().ToString(CultureInfo.InvariantCulture) +
                    " | slack=" + activitySlackMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");
                return true;
            }

            TileObject activeLocker = state.Locker;
            WalkComponent activeWalk = behavior.GetComponent<WalkComponent>();
            UseComponent activeUse = behavior.GetComponent<UseComponent>();
            AnimModelComponent activeAnim = behavior.GetComponent<AnimModelComponent>();
            Entity activeOwner = employee.m_entity;

            if (activeLocker == null || !activeLocker.IsValid() ||
                activeWalk == null || activeUse == null || activeAnim == null || activeOwner == null)
            {
                FailActive(employee, "invalid-runtime-state");
                return false;
            }

            if (activeLocker.User != activeOwner)
            {
                FailActive(employee, "locker-reservation-lost");
                return false;
            }

            if (state.Phase == TestPhase.WaitingForAccessClear)
            {
                return false;
            }

            if (state.Phase == TestPhase.GoingToLocker)
            {
                if (activeWalk.IsBusy())
                {
                    return true;
                }

                Vector2i accessPosition = state.AccessPosition;
                if (activeWalk.GetFloorIndex() != activeLocker.GetFloorIndex() ||
                    activeWalk.GetCurrentTile() != accessPosition)
                {
                    FailActive(
                        employee,
                        "path-ended-away-from-locker" +
                        " | current=" + activeWalk.GetCurrentTile().m_x.ToString(CultureInfo.InvariantCulture) +
                        "," + activeWalk.GetCurrentTile().m_y.ToString(CultureInfo.InvariantCulture) +
                        " | expected=" + accessPosition.m_x.ToString(CultureInfo.InvariantCulture) +
                        "," + accessPosition.m_y.ToString(CultureInfo.InvariantCulture));
                    return false;
                }

                activeAnim.SetDirection(activeLocker.GetAccessOrientation(accessPosition));
                activeLocker.PlayStartUseSound();
                if (!PlayLockerCharacterAnimation(
                        behavior,
                        activeLocker.m_state.m_gameDBObject.Entry.DefaultUseStartAnimations,
                        false))
                {
                    FailActive(employee, "locker-start-animation-unavailable");
                    return false;
                }

                state.Phase = TestPhase.OpeningLocker;
                Log(
                    employee,
                    "ACTIVATE",
                    "locker=" + GetObjectId(activeLocker) +
                    " | mode=manual-access-tile");
                return true;
            }

            if (state.Phase == TestPhase.OpeningLocker)
            {
                if (!HasCurrentAnimationFinished(activeAnim))
                {
                    return true;
                }

                ForceLockerFrame(activeLocker, 1);

                if (!PlayLockerCharacterAnimation(
                        behavior,
                        activeLocker.m_state.m_gameDBObject.Entry.DefaultUseAnimations,
                        false))
                {
                    FailActive(employee, "locker-use-animation-unavailable");
                    return false;
                }

                state.Phase = TestPhase.UsingLocker;
                Log(
                    employee,
                    "OPEN",
                    "locker=" + GetObjectId(activeLocker) +
                    " | forcedFrame=1");
                return true;
            }

            if (state.Phase == TestPhase.UsingLocker)
            {
                if (!HasCurrentAnimationFinished(activeAnim))
                {
                    return true;
                }

                RestoreProfessionalClothes(
                    employee,
                    "locker-use-complete");
                ForceLockerFrame(activeLocker, 0);
                activeLocker.PlayEndUseSound();
                if (!PlayLockerCharacterAnimation(
                        behavior,
                        activeLocker.m_state.m_gameDBObject.Entry.DefaultUseEndAnimations,
                        false))
                {
                    FailActive(employee, "locker-end-animation-unavailable");
                    return false;
                }

                state.Phase = TestPhase.ClosingLocker;
                Log(
                    employee,
                    "CLOSE",
                    "locker=" + GetObjectId(activeLocker) +
                    " | forcedFrame=0");
                return true;
            }

            if (!HasCurrentAnimationFinished(activeAnim))
            {
                return true;
            }

            activeAnim.PlayAnimation("stand_idle");
            DetachLockerFromUseComponent(activeOwner, activeLocker);
            state.Phase = TestPhase.WaitingForAccessClear;
            Log(
                employee,
                "END",
                "locker=" + GetObjectId(activeLocker) +
                " | access-held=yes");
            return false;
        }

        internal static void Cancel(EmployeeComponent employee, string reason)
        {
            if (employee == null)
            {
                return;
            }

            TestState state;
            if (States.TryGetValue(employee, out state))
            {
                if (state != null &&
                    state.Phase == TestPhase.WaitingForAccessClear)
                {
                    return;
                }

                CancelActive(employee, reason);
                return;
            }

            if (TryRecoverCivilianState(employee, GetStamp(employee)) ||
                CivilianStates.ContainsKey(employee))
            {
                RestoreProfessionalClothes(employee, reason);
            }
        }

        internal static void Clear(EmployeeComponent employee)
        {
            if (employee == null)
            {
                return;
            }

            if (IsWorkplaceFallbackActive(
                    employee,
                    GetStamp(employee)))
            {
                NoLockerLoggedStamps.Remove(employee);
                return;
            }

            TestState state;
            if (States.TryGetValue(employee, out state))
            {
                if (state == null ||
                    state.Phase != TestPhase.WaitingForAccessClear)
                {
                    CancelActive(employee, "employee-cleared");
                }
            }
            else if (TryRecoverCivilianState(employee, GetStamp(employee)) ||
                     CivilianStates.ContainsKey(employee))
            {
                RestoreProfessionalClothes(employee, "employee-cleared");
            }

            CompletedStamps.Remove(employee);
            NoLockerLoggedStamps.Remove(employee);
        }

        internal static void UpdatePostUseReservations()
        {
            PruneWorkplaceFallbacks();
            UpdateOutgoingPostUseReservations();

            if (States.Count == 0)
            {
                return;
            }

            List<EmployeeComponent> employees =
                new List<EmployeeComponent>(States.Keys);
            for (int i = 0; i < employees.Count; i++)
            {
                EmployeeComponent employee = employees[i];
                TestState state;
                if (employee == null ||
                    !States.TryGetValue(employee, out state) ||
                    state == null ||
                    state.Phase != TestPhase.WaitingForAccessClear)
                {
                    continue;
                }

                TileObject locker = state.Locker;
                Entity owner = employee.m_entity;
                WalkComponent walk =
                    owner != null
                        ? owner.GetComponent<WalkComponent>()
                        : null;

                bool accessCleared =
                    locker == null ||
                    walk == null ||
                    walk.GetFloorIndex() != locker.GetFloorIndex() ||
                    walk.GetCurrentTile() != state.AccessPosition;

                if (!accessCleared)
                {
                    continue;
                }

                int floorIndex =
                    locker != null
                        ? locker.GetFloorIndex()
                        : (walk != null ? walk.GetFloorIndex() : 0);

                ReleaseAccessTileReservation(
                    owner,
                    state.AccessPosition,
                    floorIndex);
                ReleaseLockerReservation(owner, locker);

                bool lockerReleased =
                    locker == null || locker.User == null;
                bool accessReleased =
                    owner == null ||
                    MapScriptInterface.Instance == null ||
                    MapScriptInterface.Instance.GetTileReservedBy(
                        state.AccessPosition,
                        floorIndex) != owner;

                CompleteActive(
                    employee,
                    lockerReleased && accessReleased,
                    "locker=" + GetObjectId(locker) +
                    " | released=" + (lockerReleased ? "yes" : "no") +
                    " | accessReleased=" + (accessReleased ? "yes" : "no"));
            }
        }

        private static void UpdateOutgoingPostUseReservations()
        {
            if (OutgoingStates.Count == 0)
            {
                return;
            }

            List<EmployeeComponent> employees =
                new List<EmployeeComponent>(
                    OutgoingStates.Keys);
            for (int i = 0; i < employees.Count; i++)
            {
                EmployeeComponent employee = employees[i];
                OutgoingState state;
                if (employee == null ||
                    !OutgoingStates.TryGetValue(
                        employee,
                        out state) ||
                    state == null ||
                    state.Phase !=
                        OutgoingPhase.WaitingForAccessClear)
                {
                    continue;
                }

                TileObject locker = state.Locker;
                Entity owner = employee.m_entity;
                WalkComponent walk =
                    owner != null
                        ? owner.GetComponent<WalkComponent>()
                        : null;

                bool accessCleared =
                    locker == null ||
                    walk == null ||
                    walk.GetFloorIndex() !=
                        locker.GetFloorIndex() ||
                    walk.GetCurrentTile() !=
                        state.AccessPosition;
                if (!accessCleared)
                {
                    continue;
                }

                int floorIndex =
                    locker != null
                        ? locker.GetFloorIndex()
                        : (walk != null
                            ? walk.GetFloorIndex()
                            : 0);

                ReleaseAccessTileReservation(
                    owner,
                    state.AccessPosition,
                    floorIndex);
                ReleaseLockerReservation(owner, locker);

                bool lockerReleased =
                    locker == null || locker.User == null;
                bool accessReleased =
                    owner == null ||
                    MapScriptInterface.Instance == null ||
                    MapScriptInterface.Instance
                        .GetTileReservedBy(
                            state.AccessPosition,
                            floorIndex) != owner;

                LogOutgoing(
                    employee,
                    lockerReleased && accessReleased
                        ? "OUT_SUCCESS"
                        : "OUT_FAIL",
                    "locker=" + GetObjectId(locker) +
                    " | released=" +
                    (lockerReleased ? "yes" : "no") +
                    " | accessReleased=" +
                    (accessReleased ? "yes" : "no"));
                OutgoingStates.Remove(employee);
            }
        }

        internal static void Shutdown()
        {
            List<EmployeeComponent> activeEmployees =
                new List<EmployeeComponent>(States.Keys);
            for (int i = 0; i < activeEmployees.Count; i++)
            {
                CancelActive(activeEmployees[i], "shutdown");
            }

            List<EmployeeComponent> civilianEmployees =
                new List<EmployeeComponent>(CivilianStates.Keys);
            for (int i = 0; i < civilianEmployees.Count; i++)
            {
                RestoreProfessionalClothes(
                    civilianEmployees[i],
                    "shutdown");
            }

            List<EmployeeComponent> outgoingEmployees =
                new List<EmployeeComponent>(
                    OutgoingStates.Keys);
            for (int i = 0;
                 i < outgoingEmployees.Count;
                 i++)
            {
                EmployeeComponent employee =
                    outgoingEmployees[i];
                OutgoingState state;
                OutgoingStates.TryGetValue(
                    employee,
                    out state);
                CancelOutgoing(
                    employee,
                    state,
                    "shutdown");
            }

            States.Clear();
            OutgoingStates.Clear();
            OutgoingCompletedStamps.Clear();
            CivilianStates.Clear();
            CompletedStamps.Clear();
            NoLockerLoggedStamps.Clear();
            WorkplaceFallbackStamps.Clear();
        }

        private static bool TrySelectOutgoingCommonRoom(
            Behavior behavior,
            EmployeeComponent employee,
            out Room selectedCommonRoom,
            out Vector2i selectedPosition,
            out int selectedFloor)
        {
            selectedCommonRoom = null;
            selectedPosition = Vector2i.ZERO_VECTOR;
            selectedFloor = -1;

            if (behavior == null ||
                employee == null ||
                employee.m_state == null ||
                Database.Instance == null ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            Vector2i workplaceTarget;
            int workplaceFloor;
            if (!WorkplaceTravelEstimator.TryGetWorkplaceTarget(
                    employee,
                    out workplaceTarget,
                    out workplaceFloor))
            {
                return false;
            }

            Department department =
                behavior.GetDepartment();
            GameDBRoomType commonRoomType =
                Database.Instance.GetEntry<GameDBRoomType>(
                    "ROOM_TYPE_COMMON_ROOM");
            if (department == null || commonRoomType == null)
            {
                return false;
            }

            List<Room> departmentCommonRooms =
                MapScriptInterface.Instance.FindValidRoomsWithType(
                    commonRoomType,
                    department);
            if (departmentCommonRooms == null)
            {
                departmentCommonRooms = new List<Room>();
            }

            int bestDistanceSquared = int.MaxValue;
            for (int i = 0;
                 i < departmentCommonRooms.Count;
                 i++)
            {
                Room commonRoom =
                    departmentCommonRooms[i];
                if (commonRoom == null ||
                    commonRoom.GetFloorIndex() !=
                        workplaceFloor)
                {
                    continue;
                }

                Vector2i candidatePosition =
                    MapScriptInterface.Instance
                        .GetRandomFreePosition(
                            commonRoom,
                            behavior.GetAccessRights());
                if (candidatePosition ==
                    Vector2i.ZERO_VECTOR)
                {
                    continue;
                }

                int deltaX =
                    candidatePosition.m_x -
                    workplaceTarget.m_x;
                int deltaY =
                    candidatePosition.m_y -
                    workplaceTarget.m_y;
                int distanceSquared =
                    deltaX * deltaX +
                    deltaY * deltaY;
                if (distanceSquared <
                    bestDistanceSquared)
                {
                    bestDistanceSquared =
                        distanceSquared;
                    selectedCommonRoom =
                        commonRoom;
                    selectedPosition =
                        candidatePosition;
                    selectedFloor =
                        workplaceFloor;
                }
            }

            if (selectedCommonRoom != null)
            {
                return true;
            }

            WalkComponent walk =
                behavior.GetComponent<WalkComponent>();
            GridMap gridMap = GridMap.GetInstance();
            if (walk == null ||
                walk.Floor == null ||
                gridMap == null)
            {
                return false;
            }

            List<Room> fallbackCommonRooms =
                GetOutgoingHospitalCommonRooms(
                    commonRoomType,
                    departmentCommonRooms);
            float bestRouteScore = float.MaxValue;
            int currentFloor = walk.GetFloorIndex();
            Vector2i currentTile =
                walk.GetCurrentTile();

            for (int i = 0;
                 i < fallbackCommonRooms.Count;
                 i++)
            {
                Room commonRoom =
                    fallbackCommonRooms[i];
                if (commonRoom == null)
                {
                    continue;
                }

                int candidateFloor =
                    commonRoom.GetFloorIndex();
                Vector2i candidatePosition =
                    MapScriptInterface.Instance
                        .GetRandomFreePosition(
                            commonRoom,
                            behavior.GetAccessRights());
                if (candidatePosition ==
                    Vector2i.ZERO_VECTOR)
                {
                    continue;
                }

                float arrivalDistance =
                    gridMap.GetDistance(
                        currentFloor,
                        currentTile,
                        candidateFloor,
                        candidatePosition,
                        behavior.GetAccessRights());
                if (arrivalDistance < 0f ||
                    arrivalDistance >= float.MaxValue)
                {
                    continue;
                }

                float workplaceDistance =
                    gridMap.GetDistance(
                        candidateFloor,
                        candidatePosition,
                        workplaceFloor,
                        workplaceTarget,
                        behavior.GetAccessRights());
                if (workplaceDistance < 0f ||
                    workplaceDistance >= float.MaxValue)
                {
                    continue;
                }

                float routeScore =
                    arrivalDistance +
                    workplaceDistance;
                if (routeScore < bestRouteScore)
                {
                    bestRouteScore = routeScore;
                    selectedCommonRoom = commonRoom;
                    selectedPosition =
                        candidatePosition;
                    selectedFloor =
                        candidateFloor;
                }
            }

            return selectedCommonRoom != null &&
                   selectedPosition !=
                       Vector2i.ZERO_VECTOR &&
                   selectedFloor >= 0;
        }

        private static List<Room> GetOutgoingHospitalCommonRooms(
            GameDBRoomType commonRoomType,
            List<Room> preferredRooms)
        {
            List<Room> rooms = new List<Room>();
            AddOutgoingUniqueRooms(
                rooms,
                preferredRooms);

            if (commonRoomType == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_departments == null)
            {
                return rooms;
            }

            for (int i = 0;
                 i < Hospital.Instance.m_departments.Count;
                 i++)
            {
                Department hospitalDepartment =
                    Hospital.Instance.m_departments[i];
                if (hospitalDepartment == null)
                {
                    continue;
                }

                List<Room> departmentRooms =
                    MapScriptInterface.Instance
                        .FindValidRoomsWithType(
                            commonRoomType,
                            hospitalDepartment);
                AddOutgoingUniqueRooms(
                    rooms,
                    departmentRooms);
            }

            return rooms;
        }

        private static void AddOutgoingUniqueRooms(
            List<Room> target,
            List<Room> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                Room room = source[i];
                if (room != null &&
                    !target.Contains(room))
                {
                    target.Add(room);
                }
            }
        }

        private static bool ApplyOutgoingCivilianClothes(
            Behavior behavior,
            EmployeeComponent employee)
        {
            if (behavior == null ||
                employee == null ||
                employee.m_state == null ||
                employee.m_entity == null ||
                DayTime.Instance == null)
            {
                return false;
            }

            WalkComponent walk =
                behavior.GetComponent<WalkComponent>();
            AnimModelComponent anim =
                behavior.GetComponent<AnimModelComponent>();
            if (walk == null ||
                anim == null ||
                anim.m_state == null ||
                anim.m_state.m_defaultClothes == null ||
                (WorldEventManager.Instance != null &&
                 WorldEventManager.Instance
                     .HasEventForcingBiohazardClothes()) ||
                walk.IsOnBiohazard())
            {
                return false;
            }

            int civilianStyleRoll =
                GetStableDecisionValue(
                    employee,
                    StableDecisionCivilianStyle,
                    100);
            bool useCasual =
                civilianStyleRoll <
                ShiftHandoverConfig.CasualClothesPercent;
            LogStableDecision(
                employee,
                "style",
                civilianStyleRoll,
                ShiftHandoverConfig.CasualClothesPercent,
                useCasual ? "casual" : "business");
            string[] styles =
                useCasual
                    ? CasualStyleIds
                    : BusinessStyleIds;
            if (!ClothingStylesExist(styles))
            {
                LogOutgoing(
                    employee,
                    "OUT_CLOTHING_SKIP",
                    "reason=civilian-style-missing");
                return false;
            }

            string defaultStyleId =
                GetClothingStyleId(
                    anim.m_state.m_defaultClothes);
            anim.ForceClothingStyle(styles, null);

            string appliedStyleId =
                GetClothingStyleId(
                    anim.m_state.m_clothes);
            string family =
                GetCivilianFamily(appliedStyleId);
            if (family == null)
            {
                LogOutgoing(
                    employee,
                    "OUT_CLOTHING_SKIP",
                    "reason=civilian-style-not-applied");
                return false;
            }

            CivilianStates[employee] =
                new CivilianState
                {
                    Stamp = GetStamp(employee),
                    Family = family,
                    DefaultStyleId =
                        defaultStyleId,
                    FromOutgoing = true
                };

            LogOutgoing(
                employee,
                "OUT_CIVILIAN",
                "family=" + family +
                " | style=" + appliedStyleId +
                " | default=" + defaultStyleId);
            return true;
        }

        private static bool IsEmployeeAtHome(
            EmployeeComponent employee)
        {
            if (employee == null ||
                employee.m_entity == null)
            {
                return false;
            }

            BehaviorDoctor doctor =
                employee.m_entity
                    .GetComponent<BehaviorDoctor>();
            if (doctor != null &&
                doctor.m_state != null)
            {
                return doctor.m_state.m_doctorState ==
                    DoctorState.AtHome;
            }

            BehaviorNurse nurse =
                employee.m_entity
                    .GetComponent<BehaviorNurse>();
            if (nurse != null &&
                nurse.m_state != null)
            {
                return nurse.m_state.m_nurseState ==
                    NurseState.AtHome;
            }

            BehaviorLabSpecialist lab =
                employee.m_entity
                    .GetComponent<BehaviorLabSpecialist>();
            return lab != null &&
                   lab.m_state != null &&
                   lab.m_state.m_labSpecialistState ==
                       LabSpecialistState.AtHome;
        }

        private static void MarkOutgoingComplete(
            EmployeeComponent employee,
            OutgoingState state,
            string step,
            string details)
        {
            if (employee == null)
            {
                return;
            }

            Entity owner = employee.m_entity;
            if (state != null)
            {
                RestoreLockerVisual(
                    owner,
                    state.Locker);

                if (state.Locker != null)
                {
                    ReleaseAccessTileReservation(
                        owner,
                        state.AccessPosition,
                        state.Locker.GetFloorIndex());
                    ReleaseLockerReservation(
                        owner,
                        state.Locker);
                }

                OutgoingStates.Remove(employee);
            }

            OutgoingCompletedStamps[employee] =
                GetStamp(employee);
            LogOutgoing(
                employee,
                step,
                details);
        }

        private static void CancelOutgoing(
            EmployeeComponent employee,
            OutgoingState state,
            string reason)
        {
            MarkOutgoingComplete(
                employee,
                state,
                "OUT_CANCEL",
                "reason=" + reason);
        }

        private static void LogOutgoing(
            EmployeeComponent employee,
            string step,
            string details)
        {
            if (Plugin.Log == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[DRESSING_OUT] " +
                GetTimestamp() +
                " | " +
                GetCharacterName(
                    employee != null
                        ? employee.m_entity
                        : null) +
                " | " +
                step +
                (string.IsNullOrEmpty(details)
                    ? string.Empty
                    : " | " + details));
        }

        private static TileObject FindClosestFreeLocker(WalkComponent walk)
        {
            if (walk == null || walk.Floor == null || MapScriptInterface.Instance == null)
            {
                return null;
            }

            Room room = MapScriptInterface.Instance.GetRoomAt(
                walk.GetCurrentTile(),
                walk.GetFloorIndex());
            if (!IsCommonRoom(room))
            {
                return null;
            }

            List<TileObject> objects = room.GetAllObjects(walk.Floor);
            if (objects == null || objects.Count == 0)
            {
                return null;
            }

            Vector2i currentTile = walk.GetCurrentTile();
            TileObject best = null;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < objects.Count; i++)
            {
                TileObject candidate = objects[i];
                if (!IsUsableLocker(candidate) ||
                    candidate.User != null ||
                    candidate.Owner != null)
                {
                    continue;
                }

                Vector2i accessPosition;
                if (!TryGetLockerAccessPosition(candidate, out accessPosition) ||
                    !candidate.CanBeAccessedFrom(accessPosition, walk.Floor) ||
                    !IsAccessTileAvailable(
                        accessPosition,
                        candidate.GetFloorIndex()))
                {
                    continue;
                }

                int distance =
                    Math.Abs(accessPosition.m_x - currentTile.m_x) +
                    Math.Abs(accessPosition.m_y - currentTile.m_y);

                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }

        internal static bool RoomHasUsableLocker(Room room)
        {
            if (!IsCommonRoom(room) ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return false;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null)
            {
                return false;
            }

            List<TileObject> objects = room.GetAllObjects(floor);
            if (objects == null)
            {
                return false;
            }

            for (int i = 0; i < objects.Count; i++)
            {
                if (IsUsableLocker(objects[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCommonRoom(Room room)
        {
            if (room == null || room.m_roomPersistentData == null)
            {
                return false;
            }

            GameDBRoomType commonRoomType =
                Database.Instance.GetEntry<GameDBRoomType>(
                    "ROOM_TYPE_COMMON_ROOM");
            return commonRoomType != null &&
                room.m_roomPersistentData.m_roomType.Entry ==
                    commonRoomType;
        }

        private static bool IsUsableLocker(TileObject locker)
        {
            return locker != null &&
                locker.IsValid() &&
                locker.HasTag("ui_locker") &&
                locker.HasTag("dressing") &&
                HasLockerUseAnimations(locker);
        }

        private static bool TryGetLockerAccessPosition(
            TileObject locker,
            out Vector2i accessPosition)
        {
            accessPosition = Vector2i.ZERO_VECTOR;
            if (locker == null ||
                locker.m_state == null ||
                locker.m_state.m_gameDBObject.Entry == null)
            {
                return false;
            }

            Vector2i[] accessPositions =
                locker.m_state.m_gameDBObject.Entry.AccessPositions;
            if (accessPositions == null || accessPositions.Length == 0)
            {
                return false;
            }

            accessPosition = locker.GetAccessTile(0);
            return true;
        }

        private static bool IsAccessTileAvailable(
            Vector2i accessPosition,
            int floorIndex)
        {
            return MapScriptInterface.Instance != null &&
                MapScriptInterface.Instance.GetTileReservedBy(
                    accessPosition,
                    floorIndex) == null;
        }

        private static bool ReserveAccessTile(
            Entity owner,
            Vector2i accessPosition,
            int floorIndex)
        {
            if (owner == null ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            Entity reservedBy =
                MapScriptInterface.Instance.GetTileReservedBy(
                    accessPosition,
                    floorIndex);
            if (reservedBy != null && reservedBy != owner)
            {
                return false;
            }

            if (reservedBy == null)
            {
                MapScriptInterface.Instance.ReserveTile(
                    accessPosition,
                    owner,
                    floorIndex);
            }
            return true;
        }

        private static void ReleaseAccessTileReservation(
            Entity owner,
            Vector2i accessPosition,
            int floorIndex)
        {
            if (owner == null ||
                MapScriptInterface.Instance == null)
            {
                return;
            }

            if (MapScriptInterface.Instance.GetTileReservedBy(
                    accessPosition,
                    floorIndex) == owner)
            {
                MapScriptInterface.Instance.FreeTile(
                    accessPosition,
                    floorIndex);
            }
        }

        private static bool HasCurrentAnimationFinished(
            AnimModelComponent anim)
        {
            if (anim == null)
            {
                return true;
            }

            float position =
                anim.GetCurrentAnimationPlaybackPosition();
            float duration =
                anim.GetCurrentAnimationDuration();

            if (position < 0f || duration <= 0f)
            {
                return anim.IsIdle();
            }

            return position >= duration;
        }

        private static bool HasLockerUseAnimations(TileObject locker)
        {
            if (locker == null ||
                locker.m_state == null ||
                locker.m_state.m_gameDBObject.Entry == null)
            {
                return false;
            }

            GameDBObject entry = locker.m_state.m_gameDBObject.Entry;
            return entry.DefaultUseStartAnimations != null &&
                entry.DefaultUseStartAnimations.Length > 0 &&
                entry.DefaultUseAnimations != null &&
                entry.DefaultUseAnimations.Length > 0 &&
                entry.DefaultUseEndAnimations != null &&
                entry.DefaultUseEndAnimations.Length > 0 &&
                locker.GetComponent<AnimatedObjectComponent>() != null;
        }

        private static bool PlayLockerCharacterAnimation(
            Behavior behavior,
            string[] animations,
            bool looping)
        {
            if (behavior == null || animations == null || animations.Length == 0)
            {
                return false;
            }

            AnimModelComponent anim = behavior.GetComponent<AnimModelComponent>();
            if (anim == null)
            {
                return false;
            }

            string animationId =
                behavior.GetSpecificAnimation(animations[0]);
            if (string.IsNullOrEmpty(animationId))
            {
                return false;
            }

            anim.PlayAnimation(animationId, looping);
            return true;
        }

        private static void ForceLockerFrame(
            TileObject locker,
            int frame)
        {
            if (locker == null)
            {
                return;
            }

            AnimatedObjectComponent animatedObject =
                locker.GetComponent<AnimatedObjectComponent>();
            if (animatedObject != null)
            {
                animatedObject.ForceFrame(frame);
            }
        }

        private static void DetachLockerFromUseComponent(
            Entity owner,
            TileObject locker)
        {
            if (owner == null || locker == null)
            {
                return;
            }

            UseComponent use = owner.GetComponent<UseComponent>();
            if (use == null ||
                use.m_state == null ||
                use.m_state.m_reservedObject.GetEntity() != locker)
            {
                return;
            }

            use.m_state.m_reservedObject = null;
        }

        private static void ReleaseLockerReservation(
            Entity owner,
            TileObject locker)
        {
            if (owner == null ||
                locker == null ||
                locker.User != owner)
            {
                return;
            }

            UseComponent use = owner.GetComponent<UseComponent>();
            if (use != null &&
                use.m_state != null &&
                use.m_state.m_reservedObject.GetEntity() == locker)
            {
                use.Interrupt();
            }

            if (locker.User == owner)
            {
                locker.User = null;
            }
        }

        private static void RestoreLockerVisual(
            Entity owner,
            TileObject locker)
        {
            ForceLockerFrame(locker, 0);

            if (owner == null)
            {
                return;
            }

            AnimModelComponent anim =
                owner.GetComponent<AnimModelComponent>();
            WalkComponent walk =
                owner.GetComponent<WalkComponent>();
            if (anim != null &&
                (walk == null || !walk.IsSitting()))
            {
                anim.PlayAnimation("stand_idle");
            }
        }

        private static bool ClothingStylesExist(string[] styleIds)
        {
            if (styleIds == null || styleIds.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < styleIds.Length; i++)
            {
                if (Database.Instance.GetEntry<GameDBClothingStyle>(
                        styleIds[i]) == null)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryRecoverCivilianState(
            EmployeeComponent employee,
            int stamp)
        {
            if (employee == null || employee.m_entity == null)
            {
                return false;
            }

            if (IsWorkplaceFallbackActive(employee, stamp))
            {
                return false;
            }

            AnimModelComponent anim =
                employee.m_entity.GetComponent<AnimModelComponent>();
            if (anim == null ||
                anim.m_state == null ||
                anim.m_state.m_clothes == null ||
                anim.m_state.m_defaultClothes == null)
            {
                return false;
            }

            string currentStyleId =
                GetClothingStyleId(anim.m_state.m_clothes);
            string defaultStyleId =
                GetClothingStyleId(anim.m_state.m_defaultClothes);

            if (string.IsNullOrEmpty(currentStyleId) ||
                currentStyleId == defaultStyleId)
            {
                return false;
            }

            string family = GetCivilianFamily(currentStyleId);
            if (family == null)
            {
                return false;
            }

            CivilianStates[employee] = new CivilianState
            {
                Stamp = stamp,
                Family = family,
                DefaultStyleId = defaultStyleId
            };
            return true;
        }

        private static bool IsWorkplaceFallbackActive(
            EmployeeComponent employee,
            int stamp)
        {
            int fallbackStamp;
            if (!WorkplaceFallbackStamps.TryGetValue(
                    employee,
                    out fallbackStamp))
            {
                return false;
            }

            if (fallbackStamp == stamp)
            {
                return true;
            }

            WorkplaceFallbackStamps.Remove(employee);
            return false;
        }

        private static void PruneWorkplaceFallbacks()
        {
            if (WorkplaceFallbackStamps.Count == 0)
            {
                return;
            }

            List<EmployeeComponent> employees =
                new List<EmployeeComponent>(
                    WorkplaceFallbackStamps.Keys);

            for (int i = 0; i < employees.Count; i++)
            {
                EmployeeComponent employee = employees[i];
                if (employee == null ||
                    employee.m_state == null ||
                    employee.m_entity == null ||
                    DayTime.Instance == null)
                {
                    WorkplaceFallbackStamps.Remove(employee);
                    continue;
                }

                int stamp = GetStamp(employee);
                if (!IsWorkplaceFallbackActive(employee, stamp))
                {
                    continue;
                }

                AnimModelComponent anim =
                    employee.m_entity.GetComponent<AnimModelComponent>();
                if (anim == null ||
                    anim.m_state == null ||
                    anim.m_state.m_clothes == null ||
                    anim.m_state.m_defaultClothes == null)
                {
                    continue;
                }

                string currentStyleId =
                    GetClothingStyleId(anim.m_state.m_clothes);
                string defaultStyleId =
                    GetClothingStyleId(anim.m_state.m_defaultClothes);

                if (currentStyleId == defaultStyleId)
                {
                    WorkplaceFallbackStamps.Remove(employee);
                    CompletedStamps.Remove(employee);
                    Log(
                        employee,
                        "FALLBACK_COMPLETE",
                        "trigger=vanilla-workplace");
                }
            }
        }

        private static bool IsCivilianStateVisible(
            EmployeeComponent employee,
            CivilianState state)
        {
            if (employee == null ||
                employee.m_entity == null ||
                state == null)
            {
                return false;
            }

            AnimModelComponent anim =
                employee.m_entity.GetComponent<AnimModelComponent>();
            if (anim == null || anim.m_state == null)
            {
                return false;
            }

            string currentStyleId =
                GetClothingStyleId(anim.m_state.m_clothes);
            string currentFamily =
                GetCivilianFamily(currentStyleId);

            return !string.IsNullOrEmpty(currentFamily) &&
                   currentFamily == state.Family;
        }

        private static void AbandonLostCivilianState(
            EmployeeComponent employee,
            int stamp,
            string source)
        {
            if (employee == null)
            {
                return;
            }

            string currentStyleId = "Unknown";
            if (employee.m_entity != null)
            {
                AnimModelComponent anim =
                    employee.m_entity.GetComponent<AnimModelComponent>();
                if (anim != null && anim.m_state != null)
                {
                    currentStyleId =
                        GetClothingStyleId(anim.m_state.m_clothes);
                }
            }

            CivilianStates.Remove(employee);
            CompletedStamps[employee] = stamp;

            Log(
                employee,
                "CIVILIAN_LOST",
                "style=" + currentStyleId +
                " | source=" + source +
                " | locker=skipped");
        }

        private static string GetCivilianFamily(string styleId)
        {
            if (styleId == "CLTHSTL_MALE_CASUAL" ||
                styleId == "CLTHSTL_FEMALE_CASUAL")
            {
                return "casual";
            }

            if (styleId == "CLTHSTL_MALE_BUSINESS" ||
                styleId == "CLTHSTL_FEMALE_BUSINESS")
            {
                return "business";
            }

            return null;
        }

        private static void RestoreProfessionalClothes(
            EmployeeComponent employee,
            string reason)
        {
            if (employee == null || employee.m_entity == null)
            {
                return;
            }

            CivilianState state;
            if (!CivilianStates.TryGetValue(employee, out state))
            {
                if (!TryRecoverCivilianState(employee, GetStamp(employee)) ||
                    !CivilianStates.TryGetValue(employee, out state))
                {
                    return;
                }
            }

            Entity owner = employee.m_entity;
            AnimModelComponent anim =
                owner.GetComponent<AnimModelComponent>();
            WalkComponent walk =
                owner.GetComponent<WalkComponent>();
            if (anim == null || anim.m_state == null)
            {
                CivilianStates.Remove(employee);
                return;
            }

            if (walk != null && walk.IsOnBiohazard())
            {
                Log(
                    employee,
                    "CLOTHING_SKIP",
                    "reason=biohazard-tile" +
                    " | trigger=" + reason);
                return;
            }

            EmployeeComponent previousExplicitRestore =
                ExplicitProfessionalRestoreEmployee;
            ExplicitProfessionalRestoreEmployee = employee;
            try
            {
                anim.RevertToDefaultClothes();
            }
            finally
            {
                ExplicitProfessionalRestoreEmployee =
                    previousExplicitRestore;
            }

            string restoredStyleId =
                GetClothingStyleId(anim.m_state.m_clothes);
            string defaultStyleId =
                GetClothingStyleId(anim.m_state.m_defaultClothes);

            CivilianStates.Remove(employee);
            Log(
                employee,
                "DRESSED",
                "family=" + (state != null ? state.Family : "unknown") +
                " | style=" + restoredStyleId +
                " | default=" + defaultStyleId +
                " | trigger=" + reason);
        }

        private static string GetClothingStyleId(Clothes clothes)
        {
            if (clothes == null ||
                !clothes.m_gameDBClothingStyle.IsValid)
            {
                return "Unknown";
            }

            GameDBClothingStyle style =
                clothes.m_gameDBClothingStyle.Entry;
            return style != null
                ? style.DatabaseID.ToString()
                : "Unknown";
        }

        private static void CancelActive(
            EmployeeComponent employee,
            string reason)
        {
            if (employee == null)
            {
                return;
            }

            TestState state;
            if (!States.TryGetValue(employee, out state) ||
                state == null)
            {
                RestoreProfessionalClothes(employee, reason);
                States.Remove(employee);
                return;
            }

            Entity owner = employee.m_entity;

            RestoreLockerVisual(owner, state.Locker);
            ReleaseAccessTileReservation(
                owner,
                state.AccessPosition,
                state.Locker != null
                    ? state.Locker.GetFloorIndex()
                    : 0);
            ReleaseLockerReservation(owner, state.Locker);
            RestoreProfessionalClothes(employee, reason);

            CompletedStamps[employee] = state.Stamp;
            Log(
                employee,
                "CANCEL",
                "reason=" + reason +
                " | locker=" + GetObjectId(state.Locker));

            States.Remove(employee);
        }

        private static void FailActive(
            EmployeeComponent employee,
            string reason)
        {
            if (employee == null)
            {
                return;
            }

            TestState state;
            if (!States.TryGetValue(employee, out state) ||
                state == null)
            {
                RestoreProfessionalClothes(employee, reason);
                return;
            }

            Entity owner = employee.m_entity;

            RestoreLockerVisual(owner, state.Locker);
            ReleaseAccessTileReservation(
                owner,
                state.AccessPosition,
                state.Locker != null
                    ? state.Locker.GetFloorIndex()
                    : 0);
            ReleaseLockerReservation(owner, state.Locker);
            RestoreProfessionalClothes(employee, reason);

            CompletedStamps[employee] = state.Stamp;
            Log(
                employee,
                "FAIL",
                "reason=" + reason +
                " | locker=" + GetObjectId(state.Locker));

            States.Remove(employee);
        }

        private static void CompleteActive(
            EmployeeComponent employee,
            bool success,
            string details)
        {
            if (employee == null)
            {
                return;
            }

            TestState state;
            if (!States.TryGetValue(employee, out state) ||
                state == null)
            {
                return;
            }

            CompletedStamps[employee] = state.Stamp;

            Log(
                employee,
                success ? "SUCCESS" : "FAIL",
                details);

            States.Remove(employee);
        }

        private static int GetStamp(EmployeeComponent employee)
        {
            int day =
                DayTime.Instance != null
                    ? DayTime.Instance.GetDay()
                    : 0;
            int shift =
                employee != null && employee.m_state != null
                    ? (int)employee.m_state.m_shift
                    : 0;
            return day * 4 + shift;
        }

        private static int GetStableDecisionValue(
            EmployeeComponent employee,
            int channel,
            int modulo)
        {
            if (employee == null ||
                employee.m_state == null ||
                modulo <= 1)
            {
                return 0;
            }

            unchecked
            {
                uint hash = 2166136261u;

                hash = MixStableDecision(
                    hash,
                    employee.m_state.m_workPlacePosition.m_x);
                hash = MixStableDecision(
                    hash,
                    employee.m_state.m_workPlacePosition.m_y);
                hash = MixStableDecision(
                    hash,
                    employee.m_state.m_workPlaceFloorIndex);
                hash = MixStableDecision(
                    hash,
                    employee.m_state.m_salary);
                hash = MixStableDecision(
                    hash,
                    (int)employee.m_state.m_shift);
                hash = MixStableDecision(
                    hash,
                    (int)employee.m_state.m_employeeType);

                if (DayTime.Instance != null)
                {
                    hash = MixStableDecision(
                        hash,
                        DayTime.Instance.GetDay());
                }

                // The decision channel is mixed as a full input and followed by an
                // avalanche step. This avoids the modulo correlation caused by the
                // previous linear salt-only hash.
                hash = MixStableDecision(hash, channel);
                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                hash *= 0x846ca68bu;
                hash ^= hash >> 16;

                return (int)(hash % (uint)modulo);
            }
        }

        private static uint MixStableDecision(
            uint hash,
            int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= 16777619u;
                return hash;
            }
        }

        private static void LogStableDecision(
            EmployeeComponent employee,
            string decision,
            int roll,
            int threshold,
            string result)
        {
            if (Plugin.Log == null ||
                !ShiftHandoverConfig.DiagnosticsEnabled)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[DRESSING_RANDOM] " +
                GetTimestamp() +
                " | " +
                GetCharacterName(
                    employee != null
                        ? employee.m_entity
                        : null) +
                " | decision=" + decision +
                " | roll=" +
                roll.ToString(CultureInfo.InvariantCulture) +
                " | threshold=" +
                threshold.ToString(CultureInfo.InvariantCulture) +
                " | result=" + result);
        }

        private static string GetObjectId(TileObject tileObject)
        {
            if (tileObject == null ||
                tileObject.m_state == null ||
                tileObject.m_state.m_gameDBObject.Entry == null)
            {
                return "Unknown";
            }

            return tileObject.m_state.m_gameDBObject.Entry.DatabaseID.ToString();
        }

        private static void Log(
            EmployeeComponent employee,
            string step,
            string details)
        {
            if (Plugin.Log == null)
            {
                return;
            }

            string characterName =
                GetCharacterName(
                    employee != null
                        ? employee.m_entity
                        : null);
            Plugin.Log.LogInfo(
                "[DRESSING] " + GetTimestamp() +
                " | " + characterName +
                " | " + step +
                (string.IsNullOrEmpty(details)
                    ? string.Empty
                    : " | " + details));
        }

        private static string GetCharacterName(Entity entity)
        {
            if (entity == null)
            {
                return "Unknown";
            }

            CharacterPersonalInfoComponent personalInfo =
                entity.GetComponent<CharacterPersonalInfoComponent>();
            if (personalInfo == null ||
                personalInfo.m_personalInfo == null)
            {
                return "Unknown";
            }

            string characterName =
                personalInfo.m_personalInfo.GetFullName();
            return characterName != null
                ? characterName.Trim()
                : "Unknown";
        }

        private static string GetTimestamp()
        {
            if (DayTime.Instance == null)
            {
                return "D? ??:??:??";
            }

            double hours =
                DayTime.Instance.GetDayTimeHours();
            int hour = (int)Math.Floor(hours);
            double minutesWithFraction =
                (hours - hour) * 60.0;
            int minute =
                (int)Math.Floor(minutesWithFraction);
            int second =
                (int)Math.Floor(
                    (minutesWithFraction - minute) * 60.0);

            if (second < 0)
            {
                second = 0;
            }
            else if (second > 59)
            {
                second = 59;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "D{0} {1:00}:{2:00}:{3:00}",
                DayTime.Instance.GetDay(),
                hour,
                minute,
                second);
        }
    }
}
