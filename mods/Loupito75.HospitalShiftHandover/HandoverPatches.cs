using System;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalShiftHandover
{
    internal static class HandoverRules
    {
        internal static int NativeEarlyExtensionMinutes
        {
            get { return ShiftHandoverConfig.NativeEarlyExtensionMinutes; }
        }

        internal static int PlannedCommuteLeadMinMinutes
        {
            get { return ShiftHandoverConfig.PlannedCommuteLeadMinMinutes; }
        }

        internal static int PlannedCommuteLeadMaxMinutes
        {
            get { return ShiftHandoverConfig.PlannedCommuteLeadMaxMinutes; }
        }

        private const float PreShiftHoldWindowHours = 2f;
        private const float InitialArrivalProtectionMinutes = 30f;

        internal static bool ShouldStartEarlier(EmployeeComponent employee)
        {
            if (employee == null || employee.m_state == null || employee.m_state.m_commuteTime > 0f)
            {
                return false;
            }

            float hoursUntilShift = GetHoursUntilOwnShift(employee);
            if (hoursUntilShift <= 0f)
            {
                return false;
            }

            float plannedLeadHours = GetPlannedCommuteLeadMinutes(employee) / 60f;
            float nativeLeadHours = employee.m_state.m_commuteTime < 0f ? -employee.m_state.m_commuteTime : 0f;
            float nativeExtendedLeadHours = nativeLeadHours + NativeEarlyExtensionMinutes / 60f;
            float requiredLeadHours = plannedLeadHours > nativeExtendedLeadHours ? plannedLeadHours : nativeExtendedLeadHours;

            return hoursUntilShift < requiredLeadHours;
        }

        internal static bool ShouldHoldBeforeShift(EmployeeComponent employee)
        {
            if (employee == null || employee.m_state == null || employee.IsFired() || DayTime.Instance == null)
            {
                return false;
            }

            if (DayTime.Instance.GetShift() == employee.m_state.m_shift)
            {
                return false;
            }

            float hoursUntilShift = GetHoursUntilOwnShift(employee);
            return hoursUntilShift >= 0f && hoursUntilShift <= PreShiftHoldWindowHours;
        }

        internal static bool ShouldStageBeforeWorkplace(EmployeeComponent employee)
        {
            return ShouldHoldBeforeShift(employee);
        }

        internal static bool IsInitialArrivalProtectionWindow(EmployeeComponent employee)
        {
            if (employee == null || employee.m_state == null || DayTime.Instance == null)
            {
                return false;
            }

            if (DayTime.Instance.GetShift() != employee.m_state.m_shift)
            {
                return false;
            }

            float minutesRelativeToShift = GetMinutesRelativeToShift(employee);
            return minutesRelativeToShift >= 0f && minutesRelativeToShift <= InitialArrivalProtectionMinutes;
        }

        internal static int GetPlannedCommuteLeadMinutes(EmployeeComponent employee)
        {
            int leadMinutes = GetStableRange(
                employee,
                101,
                PlannedCommuteLeadMinMinutes,
                PlannedCommuteLeadMaxMinutes);

            if (PreShiftArrival.ShouldPlanLockerDressing(employee))
            {
                leadMinutes +=
                    (int)PreShiftLockerInteractionTest.MinimumRequiredSlackMinutes;
            }

            return leadMinutes;
        }

        internal static float GetMinutesUntilOwnShift(EmployeeComponent employee)
        {
            if (employee == null || employee.m_state == null)
            {
                return 0f;
            }

            return GetHoursUntilOwnShift(employee) * 60f;
        }

        internal static float GetMinutesRelativeToShift(EmployeeComponent employee)
        {
            if (employee == null || employee.m_state == null || DayTime.Instance == null)
            {
                return 0f;
            }

            return GetWrappedDifferenceMinutes(
                DayTime.Instance.GetDayTimeHours(),
                GetShiftStartHour(employee.m_state.m_shift));
        }

        internal static float GetMinutesRelativeToShiftEnd(EmployeeComponent employee)
        {
            if (employee == null || employee.m_state == null || DayTime.Instance == null)
            {
                return 0f;
            }

            return GetWrappedDifferenceMinutes(
                DayTime.Instance.GetDayTimeHours(),
                GetShiftEndHour(employee.m_state.m_shift));
        }

        private static int GetStableRange(EmployeeComponent employee, int salt, int minInclusive, int maxInclusive)
        {
            if (employee == null || employee.m_state == null || maxInclusive <= minInclusive)
            {
                return minInclusive;
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
            int span = maxInclusive - minInclusive + 1;
            return minInclusive + (int)(value % span);
        }

        private static float GetWrappedDifferenceMinutes(float currentHour, float referenceHour)
        {
            float differenceHours = currentHour - referenceHour;
            if (differenceHours > 12f)
            {
                differenceHours -= 24f;
            }
            else if (differenceHours < -12f)
            {
                differenceHours += 24f;
            }

            return differenceHours * 60f;
        }

        private static float GetHoursUntilOwnShift(EmployeeComponent employee)
        {
            if (DayTime.Instance == null)
            {
                return 24f;
            }

            float targetHour = GetShiftStartHour(employee.m_state.m_shift);
            float currentHour = DayTime.Instance.GetDayTimeHours();
            float difference = targetHour - currentHour;
            if (difference < 0f)
            {
                difference += 24f;
            }
            return difference;
        }

        internal static float GetShiftStartHour(Shift shift)
        {
            GameDBSchedule schedule = Database.Instance.GetEntry<GameDBSchedule>("SCHEDULE_OPENING_HOURS_STAFF");
            if (schedule != null)
            {
                return shift == Shift.DAY ? schedule.StartTime : schedule.EndTime;
            }

            return shift == Shift.DAY ? 7f : 20f;
        }

        internal static float GetShiftEndHour(Shift shift)
        {
            GameDBSchedule schedule = Database.Instance.GetEntry<GameDBSchedule>("SCHEDULE_OPENING_HOURS_STAFF");
            if (schedule != null)
            {
                return shift == Shift.DAY ? schedule.EndTime : schedule.StartTime;
            }

            return shift == Shift.DAY ? 20f : 7f;
        }
    }

    [HarmonyPatch(typeof(EmployeeComponent), "ShouldStartCommuting")]
    internal static class EmployeeShouldStartCommutingPatch
    {
        private static void Postfix(EmployeeComponent __instance, ref bool __result)
        {
            bool nativeResult = __result;

            if (!nativeResult && HandoverRules.ShouldStartEarlier(__instance))
            {
                __result = true;
                ShiftDiagnostics.RecordCommuteTrigger(__instance, true);
            }
            else if (nativeResult)
            {
                ShiftDiagnostics.RecordCommuteTrigger(__instance, false);
            }
        }
    }

    [HarmonyPatch(typeof(EmployeeComponent), "GetOppositeShiftEmployee")]
    internal static class EmployeeGetOppositeShiftEmployeePatch
    {
        private static void Postfix(EmployeeComponent __instance, ref Entity __result)
        {
            if (__result != null &&
                __result.GetComponent<BehaviorDoctor>() != null &&
                HandoverRules.ShouldHoldBeforeShift(__instance))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorDoctor), "GoToWorkPlace")]
    internal static class DoctorGoToWorkPlacePatch
    {
        private static bool Prefix(BehaviorDoctor __instance)
        {
            EmployeeComponent employee = __instance.GetComponent<EmployeeComponent>();
            return !PreShiftCoordinator.TryInterceptWorkplaceRequest(__instance, employee);
        }
    }

    [HarmonyPatch(typeof(BehaviorNurse), "GoToWorkplace")]
    internal static class NurseGoToWorkplacePatch
    {
        private static bool Prefix(BehaviorNurse __instance)
        {
            EmployeeComponent employee = __instance.GetComponent<EmployeeComponent>();
            return !PreShiftCoordinator.TryInterceptWorkplaceRequest(__instance, employee);
        }
    }

    [HarmonyPatch(typeof(BehaviorLabSpecialist), "GoToWorkplace")]
    internal static class LabSpecialistGoToWorkplacePatch
    {
        private static bool Prefix(BehaviorLabSpecialist __instance)
        {
            EmployeeComponent employee = __instance.GetComponent<EmployeeComponent>();
            return !PreShiftCoordinator.TryInterceptWorkplaceRequest(__instance, employee);
        }
    }

    [HarmonyPatch(typeof(AnimModelComponent), "RevertToDefaultClothes")]
    internal static class AnimModelRevertToDefaultClothesPatch
    {
        private static bool Prefix(
            AnimModelComponent __instance,
            bool colorsOnly)
        {
            return PreShiftLockerInteractionTest
                .ShouldAllowRevertToDefaultClothes(
                    __instance,
                    colorsOnly);
        }
    }

    internal static class PreShiftCivilianCommonRoomArrivalGuard
    {
        internal static bool ShouldPreserveCivilianClothes(
            Behavior behavior,
            EmployeeComponent employee)
        {
            if (behavior == null ||
                employee == null ||
                employee.m_state == null)
            {
                return false;
            }

            WalkComponent walk = behavior.GetComponent<WalkComponent>();
            if (walk == null || walk.IsBusy())
            {
                return false;
            }

            return PreShiftArrival.IsInCommonArea(behavior) &&
                   PreShiftLockerInteractionTest.HasPendingCivilianClothes(employee);
        }
    }

    [HarmonyPatch(typeof(BehaviorDoctor), "UpdateStateGoingToWorkplace")]
    internal static class DoctorGoingToWorkplaceCivilianGuardPatch
    {
        private static bool Prefix(BehaviorDoctor __instance)
        {
            EmployeeComponent employee =
                __instance.GetComponent<EmployeeComponent>();
            if (!PreShiftCivilianCommonRoomArrivalGuard.ShouldPreserveCivilianClothes(
                    __instance,
                    employee))
            {
                return true;
            }

            PreShiftLockerInteractionTest.RecordCommonRoomArrivalPreserved(employee);
            __instance.SwitchState(DoctorState.Idle);
            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorNurse), "UpdateStateGoingToWorkplace")]
    internal static class NurseGoingToWorkplaceCivilianGuardPatch
    {
        private static bool Prefix(BehaviorNurse __instance)
        {
            EmployeeComponent employee =
                __instance.GetComponent<EmployeeComponent>();
            if (!PreShiftCivilianCommonRoomArrivalGuard.ShouldPreserveCivilianClothes(
                    __instance,
                    employee))
            {
                return true;
            }

            PreShiftLockerInteractionTest.RecordCommonRoomArrivalPreserved(employee);
            __instance.SwitchState(NurseState.Idle);
            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorLabSpecialist), "UpdateStateGoingToWorkplace")]
    internal static class LabSpecialistGoingToWorkplaceCivilianGuardPatch
    {
        private static bool Prefix(BehaviorLabSpecialist __instance)
        {
            EmployeeComponent employee =
                __instance.GetComponent<EmployeeComponent>();
            if (!PreShiftCivilianCommonRoomArrivalGuard.ShouldPreserveCivilianClothes(
                    __instance,
                    employee))
            {
                return true;
            }

            PreShiftLockerInteractionTest.RecordCommonRoomArrivalPreserved(employee);
            __instance.SwitchState(LabSpecialistState.Idle);
            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorDoctor), "UpdateStateIdle")]
    internal static class DoctorIdlePatch
    {
        private static bool Prefix(BehaviorDoctor __instance, float deltaTime)
        {
            if (OutgoingHandover.TryHoldForLateRelief(__instance))
            {
                return false;
            }

            if (PreShiftLockerInteractionTest.TryHandleOutgoing(__instance))
            {
                return false;
            }

            if (PreShiftLockerInteractionTest
                    .ShouldRunVanillaOutgoingDeparture(__instance))
            {
                return true;
            }

            bool result;
            if (PreShiftCommonAreaController.TryHandleDoctor(__instance, deltaTime, out result))
            {
                return result;
            }
            return PreShiftCoordinator.HandleDoctorIdle(__instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorNurse), "UpdateStateIdle")]
    internal static class NurseIdlePatch
    {
        private static bool Prefix(BehaviorNurse __instance, float deltaTime)
        {
            if (OutgoingHandover.TryHoldForLateRelief(__instance))
            {
                return false;
            }

            if (PreShiftLockerInteractionTest.TryHandleOutgoing(__instance))
            {
                return false;
            }

            if (PreShiftLockerInteractionTest
                    .ShouldRunVanillaOutgoingDeparture(__instance))
            {
                return true;
            }

            bool result;
            if (PreShiftCommonAreaController.TryHandleNurse(__instance, deltaTime, out result))
            {
                return result;
            }
            return PreShiftCoordinator.HandleNurseIdle(__instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorLabSpecialist), "UpdateStateIdle")]
    internal static class LabSpecialistIdlePatch
    {
        private static bool Prefix(BehaviorLabSpecialist __instance, float deltaTime)
        {
            if (OutgoingHandover.TryHoldForLateRelief(__instance))
            {
                return false;
            }

            if (PreShiftLockerInteractionTest.TryHandleOutgoing(__instance))
            {
                return false;
            }

            if (PreShiftLockerInteractionTest
                    .ShouldRunVanillaOutgoingDeparture(__instance))
            {
                return true;
            }

            bool result;
            if (PreShiftCommonAreaController.TryHandleLabSpecialist(__instance, deltaTime, out result))
            {
                return result;
            }
            return PreShiftCoordinator.HandleLabSpecialistIdle(__instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorDoctor), "IsFree", new Type[] { })]
    internal static class DoctorIsFreePatch
    {
        private static void Postfix(BehaviorDoctor __instance, ref bool __result)
        {
            EmployeeComponent employee = __instance.GetComponent<EmployeeComponent>();
            if (__result && ShouldBlockAvailability(__instance, employee))
            {
                __result = false;
            }
        }

        private static bool ShouldBlockAvailability(BehaviorDoctor behavior, EmployeeComponent employee)
        {
            return employee != null &&
                   (HandoverRules.ShouldHoldBeforeShift(employee) ||
                    PreShiftCoordinator.IsTransitioning(employee) ||
                    PreShiftLockerInteractionTest.IsOutgoingDressingActive(employee) ||
                    (HandoverRules.IsInitialArrivalProtectionWindow(employee) && !behavior.IsAtWorkplace(employee)));
        }
    }

    [HarmonyPatch(typeof(BehaviorDoctor), "IsFree", new Type[] { typeof(Entity) })]
    internal static class DoctorIsFreeForPatientPatch
    {
        private static void Postfix(BehaviorDoctor __instance, ref bool __result)
        {
            EmployeeComponent employee = __instance.GetComponent<EmployeeComponent>();
            if (__result && employee != null &&
                (HandoverRules.ShouldHoldBeforeShift(employee) ||
                 PreShiftCoordinator.IsTransitioning(employee) ||
                    PreShiftLockerInteractionTest.IsOutgoingDressingActive(employee) ||
                 (HandoverRules.IsInitialArrivalProtectionWindow(employee) && !__instance.IsAtWorkplace(employee))))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorNurse), "IsFree", new Type[] { })]
    internal static class NurseIsFreePatch
    {
        private static void Postfix(BehaviorNurse __instance, ref bool __result)
        {
            EmployeeComponent employee = __instance.GetComponent<EmployeeComponent>();
            if (__result && employee != null &&
                (HandoverRules.ShouldHoldBeforeShift(employee) ||
                 PreShiftCoordinator.IsTransitioning(employee) ||
                    PreShiftLockerInteractionTest.IsOutgoingDressingActive(employee) ||
                 (HandoverRules.IsInitialArrivalProtectionWindow(employee) && !__instance.IsAtWorkplace(employee))))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorLabSpecialist), "IsFree", new Type[] { })]
    internal static class LabSpecialistIsFreePatch
    {
        private static void Postfix(BehaviorLabSpecialist __instance, ref bool __result)
        {
            EmployeeComponent employee = __instance.GetComponent<EmployeeComponent>();
            if (__result && employee != null &&
                (HandoverRules.ShouldHoldBeforeShift(employee) ||
                 PreShiftCoordinator.IsTransitioning(employee) ||
                    PreShiftLockerInteractionTest.IsOutgoingDressingActive(employee) ||
                 (HandoverRules.IsInitialArrivalProtectionWindow(employee) && !__instance.IsAtWorkplace(employee))))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorLabSpecialist), "IsFree", new Type[] { typeof(Entity) })]
    internal static class LabSpecialistIsFreeForPatientPatch
    {
        private static void Postfix(BehaviorLabSpecialist __instance, ref bool __result)
        {
            EmployeeComponent employee = __instance.GetComponent<EmployeeComponent>();
            if (__result && employee != null &&
                (HandoverRules.ShouldHoldBeforeShift(employee) ||
                 PreShiftCoordinator.IsTransitioning(employee) ||
                    PreShiftLockerInteractionTest.IsOutgoingDressingActive(employee) ||
                 (HandoverRules.IsInitialArrivalProtectionWindow(employee) && !__instance.IsAtWorkplace(employee))))
            {
                __result = false;
            }
        }
    }
}
