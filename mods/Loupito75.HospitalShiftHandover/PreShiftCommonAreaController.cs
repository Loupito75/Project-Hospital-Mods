using System;
using System.Collections.Generic;
using System.Globalization;
using GLib;
using Lopital;

namespace HospitalShiftHandover
{
    internal static class PreShiftCommonAreaController
    {
        private const float FinalApproachLeadMinutes = 2f;
        private const float NeedActivitySlackMinutes = 8f;
        private const float FreeTimeActivitySlackMinutes = 12f;

        private sealed class ActivityState
        {
            internal int Stamp;
            internal bool FreeTimeAttempted;
            internal bool StandingWaitLogged;
        }

        private static readonly Dictionary<EmployeeComponent, ActivityState> States =
            new Dictionary<EmployeeComponent, ActivityState>();

        internal static bool TryHandleDoctor(BehaviorDoctor behavior, float deltaTime, out bool prefixResult)
        {
            return TryHandle(
                behavior,
                behavior != null ? behavior.GetComponent<EmployeeComponent>() : null,
                deltaTime,
                out prefixResult);
        }

        internal static bool TryHandleNurse(BehaviorNurse behavior, float deltaTime, out bool prefixResult)
        {
            return TryHandle(
                behavior,
                behavior != null ? behavior.GetComponent<EmployeeComponent>() : null,
                deltaTime,
                out prefixResult);
        }

        internal static bool TryHandleLabSpecialist(BehaviorLabSpecialist behavior, float deltaTime, out bool prefixResult)
        {
            return TryHandle(
                behavior,
                behavior != null ? behavior.GetComponent<EmployeeComponent>() : null,
                deltaTime,
                out prefixResult);
        }

        internal static bool HasManagedPreShiftState(EmployeeComponent employee)
        {
            if (employee == null || employee.m_state == null || DayTime.Instance == null)
            {
                return false;
            }

            ActivityState state;
            if (!States.TryGetValue(employee, out state) || state == null)
            {
                return false;
            }

            if (state.Stamp == GetStamp(employee))
            {
                return true;
            }

            States.Remove(employee);
            PreShiftNeedEngine.Clear(employee);
            PreShiftLockerInteractionTest.Clear(employee);
            return false;
        }

        internal static void Clear(EmployeeComponent employee)
        {
            if (employee == null)
            {
                return;
            }

            States.Remove(employee);
            PreShiftNeedEngine.Clear(employee);
            PreShiftLockerInteractionTest.Clear(employee);
        }

        internal static void Shutdown()
        {
            States.Clear();
            PreShiftNeedEngine.Shutdown();
            PreShiftLockerInteractionTest.Shutdown();
        }

        private static bool TryHandle(
            Behavior behavior,
            EmployeeComponent employee,
            float deltaTime,
            out bool prefixResult)
        {
            prefixResult = true;

            if (behavior == null || employee == null || employee.m_state == null)
            {
                return false;
            }

            bool isInCommonArea = PreShiftArrival.IsInCommonArea(behavior);
            bool lockerInteractionActive =
                PreShiftLockerInteractionTest.HasActiveInteraction(employee);

            // Only a locker interaction that already started may finish before the handover
            // cutoff. New interactions still use the validated real activity slack below.
            if (lockerInteractionActive)
            {
                if (PreShiftLockerInteractionTest.TryHandle(
                        behavior,
                        employee,
                        0f))
                {
                    prefixResult = false;
                    return true;
                }

                if (PreShiftLockerInteractionTest.HasActiveInteraction(employee))
                {
                    prefixResult = false;
                    return true;
                }
            }

            // Once the handover transition starts, PreShiftCoordinator remains the single
            // authority. This helper owns only the common-room activity window.
            if (PreShiftCoordinator.IsTransitioning(employee))
            {
                PreShiftLockerInteractionTest.AbandonForWorkplaceFallback(
                    employee,
                    "handover-transitioning");
                return false;
            }

            if (!HandoverRules.ShouldHoldBeforeShift(employee))
            {
                PreShiftLockerInteractionTest.AbandonForWorkplaceFallback(
                    employee,
                    "shift-started");
                Clear(employee);
                return false;
            }

            if (!isInCommonArea)
            {
                return false;
            }

            ActivityState state = GetState(employee);
            float routeEstimateMinutes;
            bool routeEstimatePending;
            if (!WorkplaceTravelEstimator.TryGetTravelMinutesWithFallback(
                    behavior,
                    employee,
                    out routeEstimateMinutes,
                    out routeEstimatePending))
            {
                if (routeEstimatePending)
                {
                    prefixResult = false;
                    return true;
                }

                PreShiftLockerInteractionTest.AbandonForWorkplaceFallback(
                    employee,
                    "route-estimate-unavailable");
                Clear(employee);
                RequestWorkplace(behavior);
                prefixResult = false;
                return true;
            }

            float minutesUntilShift = HandoverRules.GetMinutesUntilOwnShift(employee);
            float activitySlackMinutes = minutesUntilShift - routeEstimateMinutes;

            if (minutesUntilShift <= routeEstimateMinutes + FinalApproachLeadMinutes)
            {
                PreShiftLockerInteractionTest.AbandonForWorkplaceFallback(
                    employee,
                    "handover-due");
                ShiftDiagnostics.RecordWorkplaceDispatch(behavior, employee, routeEstimateMinutes);
                RequestWorkplace(behavior);
                prefixResult = false;
                return true;
            }

            if (PreShiftLockerInteractionTest.TryHandle(
                    behavior,
                    employee,
                    activitySlackMinutes))
            {
                prefixResult = false;
                return true;
            }

            if (activitySlackMinutes >= NeedActivitySlackMinutes &&
                PreShiftNeedEngine.Handle(behavior, employee, activitySlackMinutes, deltaTime))
            {
                prefixResult = false;
                return true;
            }

            if (!state.FreeTimeAttempted && activitySlackMinutes >= FreeTimeActivitySlackMinutes)
            {
                state.FreeTimeAttempted = true;
                if (TryStartFreeTime(behavior, employee))
                {
                    prefixResult = false;
                    return true;
                }
            }

            if (TryStartSeatedWait(behavior, employee))
            {
                state.StandingWaitLogged = false;
                LogActivity(behavior, employee, "seated-wait", null);
                prefixResult = false;
                return true;
            }

            WalkComponent walk = behavior.GetComponent<WalkComponent>();
            if (walk != null && (walk.IsBusy() || walk.IsSitting()))
            {
                prefixResult = false;
                return true;
            }

            if (!state.StandingWaitLogged)
            {
                state.StandingWaitLogged = true;
                LogActivity(behavior, employee, "standing-wait", "reason=no-native-activity-or-free-seat");
            }

            prefixResult = false;
            return true;
        }

        private static ActivityState GetState(EmployeeComponent employee)
        {
            int stamp = GetStamp(employee);
            ActivityState state;
            if (States.TryGetValue(employee, out state))
            {
                if (state != null && state.Stamp == stamp)
                {
                    return state;
                }
                States.Remove(employee);
            }

            state = new ActivityState();
            state.Stamp = stamp;
            States[employee] = state;
            return state;
        }

        private static bool TryStartFreeTime(Behavior behavior, EmployeeComponent employee)
        {
            if (!ShiftHandoverConfig.FreeTimeEnabled ||
                behavior == null || employee == null ||
                SettingsManager.Instance == null ||
                SettingsManager.Instance.m_debugSettings.m_disableFreeTime.m_value)
            {
                return false;
            }

            PerkComponent perkComponent = behavior.GetComponent<PerkComponent>();
            if (perkComponent != null && perkComponent.m_perkSet != null &&
                perkComponent.m_perkSet.HasPerk("PERK_HARD_WORKER"))
            {
                return false;
            }

            Entity owner = employee.m_entity;
            Department department = behavior.GetDepartment();
            ProcedureComponent procedureComponent = behavior.GetComponent<ProcedureComponent>();
            GameDBProcedure freeTimeProcedure =
                Database.Instance.GetEntry<GameDBProcedure>("CONTROL_PROCEDURE_NURSE_FREE_TIME");

            if (owner == null || department == null || procedureComponent == null ||
                freeTimeProcedure == null || procedureComponent.IsBusy())
            {
                return false;
            }

            if (procedureComponent.GetProcedureAvailabilty(
                    freeTimeProcedure,
                    owner,
                    department,
                    AccessRights.STAFF,
                    EquipmentListRules.ONLY_FREE_SAME_FLOOR_PREFER_DPT) != ProcedureSceneAvailability.AVAILABLE)
            {
                return false;
            }

            procedureComponent.StartProcedure(
                freeTimeProcedure,
                owner,
                department,
                AccessRights.STAFF,
                EquipmentListRules.ONLY_FREE_SAME_FLOOR_PREFER_DPT);

            SwitchToFreeTime(behavior);
            LogActivity(behavior, employee, "native-free-time", null);
            return true;
        }

        private static bool TryStartSeatedWait(Behavior behavior, EmployeeComponent employee)
        {
            WalkComponent walk = behavior != null ? behavior.GetComponent<WalkComponent>() : null;
            if (walk == null || walk.IsBusy() || walk.IsSitting() || walk.Floor == null)
            {
                return false;
            }

            Room room = MapScriptInterface.Instance.GetRoomAt(walk.GetCurrentTile(), walk.GetFloorIndex());
            GameDBRoomType commonRoomType = Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_COMMON_ROOM");
            if (room == null || commonRoomType == null ||
                room.m_roomPersistentData.m_roomType.Entry != commonRoomType)
            {
                return false;
            }

            List<TileObject> objects = room.GetAllObjects(walk.Floor);
            if (objects == null || objects.Count == 0)
            {
                return false;
            }

            List<TileObject> preferredSeats = new List<TileObject>();
            List<TileObject> otherSeats = new List<TileObject>();
            for (int i = 0; i < objects.Count; i++)
            {
                TileObject tileObject = objects[i];
                if (tileObject == null || !tileObject.IsValid() ||
                    tileObject.User != null || tileObject.Owner != null ||
                    !tileObject.HasTag("sitting"))
                {
                    continue;
                }

                if (tileObject.HasTag("rest"))
                {
                    preferredSeats.Add(tileObject);
                }
                else
                {
                    otherSeats.Add(tileObject);
                }
            }

            List<TileObject> seats = preferredSeats.Count > 0 ? preferredSeats : otherSeats;
            if (seats.Count == 0)
            {
                return false;
            }

            int selectedIndex = GetStableValue(employee, 541, seats.Count);
            TileObject selectedSeat = seats[selectedIndex];
            if (selectedSeat == null || selectedSeat.User != null || selectedSeat.Owner != null)
            {
                return false;
            }

            walk.GoSit(selectedSeat);
            return true;
        }

        private static void RequestWorkplace(Behavior behavior)
        {
            BehaviorDoctor doctor = behavior as BehaviorDoctor;
            if (doctor != null)
            {
                doctor.GoToWorkPlace();
                return;
            }

            BehaviorNurse nurse = behavior as BehaviorNurse;
            if (nurse != null)
            {
                nurse.GoToWorkplace();
                return;
            }

            BehaviorLabSpecialist lab = behavior as BehaviorLabSpecialist;
            if (lab != null)
            {
                lab.GoToWorkplace();
            }
        }

        private static void SwitchToFreeTime(Behavior behavior)
        {
            BehaviorDoctor doctor = behavior as BehaviorDoctor;
            if (doctor != null)
            {
                doctor.SwitchState(DoctorState.FillingFreeTime);
                return;
            }

            BehaviorNurse nurse = behavior as BehaviorNurse;
            if (nurse != null)
            {
                nurse.SwitchState(NurseState.FillingFreeTime);
                return;
            }

            BehaviorLabSpecialist lab = behavior as BehaviorLabSpecialist;
            if (lab != null)
            {
                lab.SwitchState(LabSpecialistState.FillingFreeTime);
            }
        }

        private static int GetStamp(EmployeeComponent employee)
        {
            int day = DayTime.Instance != null ? DayTime.Instance.GetDay() : 0;
            int shift = employee != null && employee.m_state != null ? (int)employee.m_state.m_shift : 0;
            return day * 4 + shift;
        }

        private static int GetStableValue(EmployeeComponent employee, int salt, int modulo)
        {
            if (employee == null || employee.m_state == null || modulo <= 1)
            {
                return 0;
            }

            long value = 17L;
            value = value * 31L + employee.m_state.m_workPlacePosition.m_x;
            value = value * 31L + employee.m_state.m_workPlacePosition.m_y;
            value = value * 31L + employee.m_state.m_workPlaceFloorIndex;
            value = value * 31L + employee.m_state.m_salary;
            value = value * 31L + (int)employee.m_state.m_shift;
            value = value * 31L + (int)employee.m_state.m_employeeType;
            value = value * 31L + salt;
            if (DayTime.Instance != null)
            {
                value = value * 31L + DayTime.Instance.GetDay();
            }
            value &= 0x7fffffffL;
            return (int)(value % modulo);
        }

        private static void LogActivity(Behavior behavior, EmployeeComponent employee, string type, string details)
        {
            if (Plugin.Log == null || DayTime.Instance == null || behavior == null || employee == null)
            {
                return;
            }

            string suffix = string.IsNullOrEmpty(details) ? string.Empty : " | " + details;
            Plugin.Log.LogInfo(
                "[SHIFT] " + GetTimestamp() +
                " | " + GetProfession(behavior) +
                " | " + GetCharacterName(employee.m_entity) +
                " | PRE_SHIFT_ACTIVITY | type=" + type +
                " | vsShift=" + HandoverRules.GetMinutesRelativeToShift(employee).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) +
                "m" + suffix);
        }

        private static string GetProfession(Behavior behavior)
        {
            if (behavior is BehaviorDoctor)
            {
                return "Doctor";
            }
            if (behavior is BehaviorNurse)
            {
                return "Nurse";
            }
            if (behavior is BehaviorLabSpecialist)
            {
                return "LabSpecialist";
            }
            return "Staff";
        }

        private static string GetCharacterName(Entity entity)
        {
            if (entity == null)
            {
                return "Unknown";
            }

            CharacterPersonalInfoComponent personalInfo = entity.GetComponent<CharacterPersonalInfoComponent>();
            if (personalInfo == null || personalInfo.m_personalInfo == null)
            {
                return "Unknown";
            }

            string characterName = personalInfo.m_personalInfo.GetFullName();
            return characterName != null ? characterName.Trim() : "Unknown";
        }

        private static string GetTimestamp()
        {
            double hours = DayTime.Instance.GetDayTimeHours();
            int hour = (int)Math.Floor(hours);
            double minutesWithFraction = (hours - hour) * 60.0;
            int minute = (int)Math.Floor(minutesWithFraction);
            int second = (int)Math.Floor((minutesWithFraction - minute) * 60.0);
            if (second < 0)
            {
                second = 0;
            }
            else if (second > 59)
            {
                second = 59;
            }
            return string.Format(CultureInfo.InvariantCulture, "D{0} {1:00}:{2:00}:{3:00}",
                DayTime.Instance.GetDay(), hour, minute, second);
        }
    }
}
