using System;
using System.Collections.Generic;
using System.Globalization;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalShiftHandover
{
    // Keep outgoing behavior native, but prevent completed personal activities from
    // sending an off-shift employee back to the workplace before the native Idle
    // state can send them home.
    internal static class OutgoingHandover
    {
        private static int LateReliefMinimumMinutes
        {
            get { return ShiftHandoverConfig.LateReliefMinimumMinutes; }
        }

        private static int LateReliefMaximumMinutes
        {
            get { return ShiftHandoverConfig.LateReliefMaximumMinutes; }
        }

        private sealed class LateReliefState
        {
            internal Entity Relief;
            internal float StartedAtMinutesAfterShift;
            internal float TargetMinutesAfterShift;
        }

        private static readonly Dictionary<EmployeeComponent, LateReliefState> LateReliefHolds =
            new Dictionary<EmployeeComponent, LateReliefState>();

        internal static bool TryHoldForLateRelief(Behavior behavior)
        {
            if (!ShiftHandoverConfig.LateReliefEnabled || behavior == null || DayTime.Instance == null)
            {
                return false;
            }

            EmployeeComponent employee = behavior.GetComponent<EmployeeComponent>();
            if (employee == null || employee.m_state == null || employee.IsFired())
            {
                return false;
            }

            // Early arrivals are off-shift too, but belong to the pre-shift pipeline.
            // Late relief only applies after an employee's own shift has ended.
            if (HandoverRules.ShouldHoldBeforeShift(employee) ||
                DayTime.Instance.GetShift() == employee.m_state.m_shift)
            {
                LateReliefHolds.Remove(employee);
                return false;
            }

            float minutesAfterShift = HandoverRules.GetMinutesRelativeToShiftEnd(employee);
            if (minutesAfterShift < 0f)
            {
                LateReliefHolds.Remove(employee);
                return false;
            }

            Entity relief;
            if (!TryGetExactRelief(employee, out relief))
            {
                ReleaseLateReliefHold(behavior, employee, null, minutesAfterShift, "relief-unassigned");
                return false;
            }

            string reliefState;
            bool reliefAbsent = IsReliefAbsent(relief, out reliefState);
            if (!reliefAbsent)
            {
                ReleaseLateReliefHold(behavior, employee, relief, minutesAfterShift, "relief-present");
                return false;
            }

            LateReliefState holdState;
            bool hasHold = LateReliefHolds.TryGetValue(employee, out holdState) && holdState != null;
            float targetMinutesAfterShift = hasHold
                ? holdState.TargetMinutesAfterShift
                : GetLateReliefTargetMinutes(employee);

            // Each employee gets a stable daily grace target inside the configured
            // range. The cap is still measured from the official shift end, not from
            // the moment an employee happened to become Idle. If a professional task
            // runs past the target, it is never interrupted; the employee simply
            // leaves the next time the native behavior reaches Idle.
            if (minutesAfterShift >= targetMinutesAfterShift)
            {
                if (hasHold)
                {
                    LogLateRelief(
                        behavior,
                        employee,
                        "LATE_RELIEF_TIMEOUT",
                        "vsShiftEnd=" + FormatMinutes(minutesAfterShift) +
                        " | held=" + FormatDuration(minutesAfterShift - holdState.StartedAtMinutesAfterShift) +
                        " | relief=" + GetCharacterName(relief) +
                        " | reliefState=" + reliefState +
                        " | target=" + targetMinutesAfterShift.ToString("0.0", CultureInfo.InvariantCulture) + "m" +
                        " | range=" + FormatLateReliefRange());
                    LateReliefHolds.Remove(employee);
                }
                return false;
            }

            if (!hasHold)
            {
                holdState = new LateReliefState();
                holdState.Relief = relief;
                holdState.StartedAtMinutesAfterShift = minutesAfterShift;
                holdState.TargetMinutesAfterShift = targetMinutesAfterShift;
                LateReliefHolds[employee] = holdState;

                LogLateRelief(
                    behavior,
                    employee,
                    "LATE_RELIEF_HOLD",
                    "vsShiftEnd=" + FormatMinutes(minutesAfterShift) +
                    " | relief=" + GetCharacterName(relief) +
                    " | reliefState=" + reliefState +
                    " | target=" + targetMinutesAfterShift.ToString("0.0", CultureInfo.InvariantCulture) + "m" +
                    " | range=" + FormatLateReliefRange());
            }
            else
            {
                holdState.Relief = relief;
            }

            // Returning true tells the targeted UpdateStateIdle prefix to skip the vanilla
            // GoingHome branch for this tick. The employee remains genuinely Idle, so the
            // game's own schedulers may still use them while they cover the missing relief.
            return true;
        }

        internal static bool TryReturnToIdleAfterShift(Behavior behavior)
        {
            if (behavior == null || DayTime.Instance == null)
            {
                return false;
            }

            EmployeeComponent employee = behavior.GetComponent<EmployeeComponent>();
            ProcedureComponent procedureComponent = behavior.GetComponent<ProcedureComponent>();
            if (employee == null || employee.m_state == null ||
                procedureComponent == null || procedureComponent.IsBusy() ||
                employee.IsFired())
            {
                return false;
            }

            // An employee who is already inside during the pre-shift hold window is not
            // an outgoing worker. Personal activities may legitimately finish before the
            // shift starts; preserve the vanilla continuation so the existing pre-shift
            // workplace interception can keep them in the hospital.
            if (HandoverRules.ShouldHoldBeforeShift(employee))
            {
                return false;
            }

            // The activity is finished, but the employee is still on their own shift:
            // preserve the exact vanilla return-to-workplace behavior.
            if (DayTime.Instance.GetShift() == employee.m_state.m_shift)
            {
                return false;
            }

            // Do not interfere with a professional reservation that may still own the
            // employee even though the personal ProcedureComponent just became idle.
            if (employee.m_state.m_reservedByPatient != null &&
                employee.m_state.m_reservedByPatient.GetEntity() != null)
            {
                return false;
            }

            // Doctors with a live CurrentPatient are deliberately kept on the native
            // path. The handover already treats CurrentPatient as authoritative.
            BehaviorDoctor doctor = behavior as BehaviorDoctor;
            if (doctor != null && doctor.CurrentPatient != null)
            {
                return false;
            }

            // Do not duplicate UpdateStateIdle's GoingHome cleanup here. Returning to
            // Idle for one behavior tick lets Project Hospital perform its own light,
            // workspace, statistics and home-route cleanup, while avoiding the useless
            // physical trip back to the work desk.
            if (doctor != null)
            {
                doctor.SwitchState(DoctorState.Idle);
                return true;
            }

            BehaviorNurse nurse = behavior as BehaviorNurse;
            if (nurse != null)
            {
                nurse.SwitchState(NurseState.Idle);
                return true;
            }

            BehaviorLabSpecialist lab = behavior as BehaviorLabSpecialist;
            if (lab != null)
            {
                lab.SwitchState(LabSpecialistState.Idle);
                return true;
            }

            return false;
        }

        internal static void Shutdown()
        {
            LateReliefHolds.Clear();
        }

        private static float GetLateReliefTargetMinutes(EmployeeComponent employee)
        {
            int minimum = LateReliefMinimumMinutes;
            int maximum = LateReliefMaximumMinutes;
            if (employee == null || employee.m_state == null || maximum <= minimum)
            {
                return minimum;
            }

            long value = 17L;
            value = value * 31L + employee.m_state.m_workPlacePosition.m_x;
            value = value * 31L + employee.m_state.m_workPlacePosition.m_y;
            value = value * 31L + employee.m_state.m_workPlaceFloorIndex;
            value = value * 31L + employee.m_state.m_salary;
            value = value * 31L + (int)employee.m_state.m_shift;
            value = value * 31L + (int)employee.m_state.m_employeeType;
            value = value * 31L + 907L;
            value = value * 31L + DayTime.Instance.GetDay();
            value &= 0x7fffffffL;

            int span = maximum - minimum + 1;
            return minimum + (int)(value % span);
        }

        private static bool TryGetExactRelief(EmployeeComponent employee, out Entity relief)
        {
            relief = null;
            if (employee == null || employee.m_state == null || employee.m_state.m_workDesk == null)
            {
                return false;
            }

            TileObject workDesk = employee.m_state.m_workDesk.GetEntity();
            if (workDesk == null)
            {
                return false;
            }

            Shift oppositeShift = employee.m_state.m_shift == Shift.DAY ? Shift.NIGHT : Shift.DAY;
            relief = workDesk.GetWorkspaceOwner(oppositeShift);
            if (relief == null || relief == employee.m_entity)
            {
                relief = null;
                return false;
            }

            EmployeeComponent reliefEmployee = relief.GetComponent<EmployeeComponent>();
            if (reliefEmployee == null || reliefEmployee.m_state == null || reliefEmployee.IsFired() ||
                reliefEmployee.m_state.m_shift != oppositeShift || reliefEmployee.m_state.m_workDesk == null ||
                reliefEmployee.m_state.m_workDesk.GetEntity() != workDesk)
            {
                relief = null;
                return false;
            }

            return true;
        }

        private static bool IsReliefAbsent(Entity relief, out string stateName)
        {
            stateName = "unknown";
            if (relief == null)
            {
                return true;
            }

            BehaviorDoctor doctor = relief.GetComponent<BehaviorDoctor>();
            if (doctor != null && doctor.m_state != null)
            {
                DoctorState state = doctor.m_state.m_doctorState;
                stateName = state.ToString();
                return state == DoctorState.AtHome || state == DoctorState.Commuting ||
                       state == DoctorState.GoingHome || state == DoctorState.FiredAtHome;
            }

            BehaviorNurse nurse = relief.GetComponent<BehaviorNurse>();
            if (nurse != null && nurse.m_state != null)
            {
                NurseState state = nurse.m_state.m_nurseState;
                stateName = state.ToString();
                return state == NurseState.AtHome || state == NurseState.Commuting ||
                       state == NurseState.GoingHome || state == NurseState.FiredAtHome;
            }

            BehaviorLabSpecialist lab = relief.GetComponent<BehaviorLabSpecialist>();
            if (lab != null && lab.m_state != null)
            {
                LabSpecialistState state = lab.m_state.m_labSpecialistState;
                stateName = state.ToString();
                return state == LabSpecialistState.AtHome || state == LabSpecialistState.Commuting ||
                       state == LabSpecialistState.GoingHome || state == LabSpecialistState.FiredAtHome;
            }

            // Unknown behavior: fail open and do not keep somebody at work indefinitely.
            return false;
        }

        private static void ReleaseLateReliefHold(
            Behavior behavior,
            EmployeeComponent employee,
            Entity relief,
            float minutesAfterShift,
            string reason)
        {
            LateReliefState holdState;
            if (!LateReliefHolds.TryGetValue(employee, out holdState) || holdState == null)
            {
                return;
            }

            string reliefState;
            IsReliefAbsent(relief, out reliefState);
            LogLateRelief(
                behavior,
                employee,
                "LATE_RELIEF_RELEASE",
                "vsShiftEnd=" + FormatMinutes(minutesAfterShift) +
                " | held=" + FormatDuration(minutesAfterShift - holdState.StartedAtMinutesAfterShift) +
                " | reason=" + reason +
                " | relief=" + GetCharacterName(relief) +
                " | reliefState=" + reliefState +
                " | target=" + holdState.TargetMinutesAfterShift.ToString("0.0", CultureInfo.InvariantCulture) + "m");
            LateReliefHolds.Remove(employee);
        }

        private static string FormatLateReliefRange()
        {
            return LateReliefMinimumMinutes.ToString(CultureInfo.InvariantCulture) +
                   "-" + LateReliefMaximumMinutes.ToString(CultureInfo.InvariantCulture) + "m";
        }

        private static string FormatMinutes(float minutes)
        {
            return minutes.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "m";
        }

        private static string FormatDuration(float minutes)
        {
            if (minutes < 0f)
            {
                minutes = 0f;
            }
            return minutes.ToString("0.0", CultureInfo.InvariantCulture) + "m";
        }

        private static string GetCharacterName(Entity entity)
        {
            if (entity == null)
            {
                return "none";
            }

            CharacterPersonalInfoComponent component = entity.GetComponent<CharacterPersonalInfoComponent>();
            if (component == null || component.m_personalInfo == null)
            {
                return "unknown";
            }

            string characterName = component.m_personalInfo.GetFullName();
            return characterName != null ? characterName.Trim() : "unknown";
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

        private static void LogLateRelief(
            Behavior behavior,
            EmployeeComponent employee,
            string eventName,
            string details)
        {
            if (Plugin.Log == null || DayTime.Instance == null || behavior == null ||
                employee == null || employee.m_state == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[SHIFT] " + GetTimestamp() +
                " | " + GetProfession(behavior) +
                " | " + GetCharacterName(employee.m_entity) +
                " | " + employee.m_state.m_shift +
                " | " + eventName +
                " | " + details);
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

            return string.Format(
                CultureInfo.InvariantCulture,
                "D{0} {1:00}:{2:00}:{3:00}",
                DayTime.Instance.GetDay(),
                hour,
                minute,
                second);
        }
    }

    [HarmonyPatch(typeof(BehaviorDoctor), "UpdateFulfilingNeeds")]
    internal static class DoctorFinishedNeedsAfterShiftPatch
    {
        private static bool Prefix(BehaviorDoctor __instance)
        {
            return !OutgoingHandover.TryReturnToIdleAfterShift(__instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorDoctor), "UpdateStateFillingFreeTime", new Type[] { typeof(float) })]
    internal static class DoctorFinishedFreeTimeAfterShiftPatch
    {
        private static bool Prefix(BehaviorDoctor __instance)
        {
            return !OutgoingHandover.TryReturnToIdleAfterShift(__instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorNurse), "UpdateStateFulfillingNeeds")]
    internal static class NurseFinishedNeedsAfterShiftPatch
    {
        private static bool Prefix(BehaviorNurse __instance)
        {
            return !OutgoingHandover.TryReturnToIdleAfterShift(__instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorNurse), "UpdateStateFillingFreeTime", new Type[] { typeof(float) })]
    internal static class NurseFinishedFreeTimeAfterShiftPatch
    {
        private static bool Prefix(BehaviorNurse __instance)
        {
            return !OutgoingHandover.TryReturnToIdleAfterShift(__instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorLabSpecialist), "UpdateStateFulfillingNeeds")]
    internal static class LabFinishedNeedsAfterShiftPatch
    {
        private static bool Prefix(BehaviorLabSpecialist __instance)
        {
            return !OutgoingHandover.TryReturnToIdleAfterShift(__instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorLabSpecialist), "UpdateStateFillingFreeTime", new Type[] { typeof(float) })]
    internal static class LabFinishedFreeTimeAfterShiftPatch
    {
        private static bool Prefix(BehaviorLabSpecialist __instance)
        {
            return !OutgoingHandover.TryReturnToIdleAfterShift(__instance);
        }
    }
}
