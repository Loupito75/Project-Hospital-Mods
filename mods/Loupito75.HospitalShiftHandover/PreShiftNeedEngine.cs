using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalShiftHandover
{
    internal static class PreShiftNeedEngine
    {
        private const float AssistedNeedMinimumValue = 10f;
        private const float AssistedNeedTriggerValue = 50.1f;
        private const float RetryIntervalSeconds = 5f;

        private static int AssistedNeedChancePercent
        {
            get { return ShiftHandoverConfig.AssistedNeedChancePercent; }
        }

        private sealed class NeedState
        {
            internal int Stamp;
            internal bool Completed;
            internal bool AssistedDecisionMade;
            internal bool AssistedAllowed;
            internal int AssistedRoll;
            internal float ElapsedSeconds;
            internal float NextCheckElapsedSeconds;
            internal string LastDiagnostic;
        }

        private static readonly Dictionary<EmployeeComponent, NeedState> States =
            new Dictionary<EmployeeComponent, NeedState>();

        private static readonly MethodInfo DoctorCheckNeeds =
            AccessTools.Method(typeof(BehaviorDoctor), "CheckNeeds", new Type[] { typeof(AccessRights) });

        private static readonly MethodInfo NurseCheckNeeds =
            AccessTools.Method(typeof(BehaviorNurse), "CheckNeeds", new Type[] { typeof(AccessRights), typeof(bool) });

        private static readonly MethodInfo LabSpecialistCheckNeeds =
            AccessTools.Method(typeof(BehaviorLabSpecialist), "CheckNeeds", new Type[] { typeof(AccessRights) });

        private static bool s_reflectionWarningLogged;

        internal static bool Handle(
            Behavior behavior,
            EmployeeComponent employee,
            float activitySlackMinutes,
            float deltaTime)
        {
            if (behavior == null || employee == null || employee.m_state == null || DayTime.Instance == null)
            {
                return false;
            }

            NeedState state = GetState(employee);
            if (state == null || state.Completed)
            {
                return false;
            }

            if (deltaTime > 0f)
            {
                state.ElapsedSeconds += deltaTime;
            }

            float initialDelay = GetInitialDelaySeconds(behavior);
            if (state.ElapsedSeconds < initialDelay)
            {
                LogDiagnosticOnce(
                    state,
                    behavior,
                    employee,
                    "need-wait",
                    "reason=pre-shift-timer | required=" +
                    initialDelay.ToString("0.0", CultureInfo.InvariantCulture) + "s");
                return true;
            }

            if (state.ElapsedSeconds < state.NextCheckElapsedSeconds)
            {
                return false;
            }
            state.NextCheckElapsedSeconds = state.ElapsedSeconds + RetryIntervalSeconds;

            string temporaryBlockReason = GetTemporaryBlockReason(behavior);
            if (!string.IsNullOrEmpty(temporaryBlockReason))
            {
                LogDiagnosticOnce(
                    state,
                    behavior,
                    employee,
                    "need-deferred",
                    "reason=" + temporaryBlockReason +
                    " | slack=" + activitySlackMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");
                return false;
            }

            MethodInfo checkNeedsMethod;
            object[] checkNeedsArguments;
            if (!GetNativeNeedCheck(behavior, out checkNeedsMethod, out checkNeedsArguments))
            {
                LogDiagnosticOnce(state, behavior, employee, "need-skip", "reason=native-check-unavailable");
                return false;
            }

            LogActivity(
                behavior,
                employee,
                "need-check",
                "mode=native | elapsed=" +
                state.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture) +
                "s | slack=" + activitySlackMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");

            bool nativeResult = InvokeCheckNeeds(checkNeedsMethod, behavior, checkNeedsArguments);
            LogActivity(
                behavior,
                employee,
                "need-result",
                "mode=native | result=" + (nativeResult ? "yes" : "no") +
                " | elapsed=" + state.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s");

            if (nativeResult)
            {
                SwitchToNeeds(behavior);
                state.Completed = true;
                LogActivity(
                    behavior,
                    employee,
                    "native-needs",
                    "slack=" + activitySlackMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");
                return true;
            }

            if (!ShiftHandoverConfig.AssistedNeedsEnabled)
            {
                LogDiagnosticOnce(
                    state,
                    behavior,
                    employee,
                    "need-skip",
                    "reason=assisted-disabled | native-retry=yes");
                return false;
            }

            if (!state.AssistedDecisionMade)
            {
                state.AssistedRoll = GetStableValue(employee, 631, 100);
                state.AssistedAllowed = state.AssistedRoll < AssistedNeedChancePercent;
                state.AssistedDecisionMade = true;
                LogActivity(
                    behavior,
                    employee,
                    "need-assistance-decision",
                    "allowed=" + (state.AssistedAllowed ? "yes" : "no") +
                    " | roll=" + state.AssistedRoll.ToString(CultureInfo.InvariantCulture) +
                    " | limit=" + AssistedNeedChancePercent.ToString(CultureInfo.InvariantCulture));
            }

            if (!state.AssistedAllowed)
            {
                LogDiagnosticOnce(
                    state,
                    behavior,
                    employee,
                    "need-skip",
                    "reason=assisted-chance | roll=" +
                    state.AssistedRoll.ToString(CultureInfo.InvariantCulture) +
                    " | limit=" + AssistedNeedChancePercent.ToString(CultureInfo.InvariantCulture) +
                    " | native-retry=yes");
                return false;
            }

            MoodComponent mood = behavior.GetComponent<MoodComponent>();
            ProcedureComponent procedureComponent = behavior.GetComponent<ProcedureComponent>();
            Department department = behavior.GetDepartment();
            if (mood == null || procedureComponent == null || department == null || employee.m_entity == null)
            {
                LogDiagnosticOnce(
                    state,
                    behavior,
                    employee,
                    "need-skip",
                    "reason=need-context-unavailable | mood=" + (mood != null ? "yes" : "no") +
                    " | procedure=" + (procedureComponent != null ? "yes" : "no") +
                    " | department=" + (department != null ? "yes" : "no"));
                return false;
            }

            if (procedureComponent.IsBusy())
            {
                LogDiagnosticOnce(state, behavior, employee, "need-deferred", "reason=procedure-component-busy");
                return false;
            }

            EquipmentListRules equipmentRules = !employee.ShouldGoToTraining()
                ? EquipmentListRules.ONLY_FREE_SAME_FLOOR_PREFER_DPT
                : EquipmentListRules.ONLY_FREE_SAME_FLOOR;

            string candidateDiagnostic;
            Need selectedNeed = FindAvailableAssistedNeedCandidate(
                behavior,
                employee,
                mood,
                procedureComponent,
                department,
                AccessRights.STAFF_ONLY,
                equipmentRules,
                out candidateDiagnostic);

            if (selectedNeed == null)
            {
                LogDiagnosticOnce(
                    state,
                    behavior,
                    employee,
                    "need-skip",
                    candidateDiagnostic +
                    " | access=STAFF_ONLY" +
                    " | slack=" + activitySlackMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");
                return false;
            }

            float previousValue = selectedNeed.m_currentValue;
            string satisfiedModifierId = GetSatisfiedModifierId(selectedNeed);
            bool hadSatisfiedModifier = !string.IsNullOrEmpty(satisfiedModifierId) &&
                                        mood.HasSatisfactionModifier(satisfiedModifierId);

            if (hadSatisfiedModifier)
            {
                mood.RemoveSatisfactionModifier(satisfiedModifierId);
            }

            selectedNeed.m_currentValue = AssistedNeedTriggerValue;
            LogActivity(
                behavior,
                employee,
                "need-check",
                "mode=assisted | need=" + selectedNeed.m_gameDBNeed.Entry.DatabaseID +
                " | old=" + previousValue.ToString("0.0", CultureInfo.InvariantCulture) +
                " | access=STAFF_ONLY");

            bool assistedResult = InvokeCheckNeeds(checkNeedsMethod, behavior, checkNeedsArguments);
            LogActivity(
                behavior,
                employee,
                "need-result",
                "mode=assisted | result=" + (assistedResult ? "yes" : "no") +
                " | need=" + selectedNeed.m_gameDBNeed.Entry.DatabaseID +
                " | old=" + previousValue.ToString("0.0", CultureInfo.InvariantCulture));

            if (assistedResult)
            {
                SwitchToNeeds(behavior);
                state.Completed = true;
                LogActivity(
                    behavior,
                    employee,
                    "native-needs-assisted",
                    "need=" + selectedNeed.m_gameDBNeed.Entry.DatabaseID +
                    " | old=" + previousValue.ToString("0.0", CultureInfo.InvariantCulture) +
                    " | access=STAFF_ONLY" +
                    " | slack=" + activitySlackMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");
                return true;
            }

            selectedNeed.m_currentValue = previousValue;
            if (hadSatisfiedModifier && !mood.HasSatisfactionModifier(satisfiedModifierId))
            {
                mood.AddSatisfactionModifier(satisfiedModifierId);
            }

            LogDiagnosticOnce(
                state,
                behavior,
                employee,
                "need-skip",
                "reason=assisted-check-rejected | need=" + selectedNeed.m_gameDBNeed.Entry.DatabaseID +
                " | old=" + previousValue.ToString("0.0", CultureInfo.InvariantCulture) +
                " | access=STAFF_ONLY | retry=yes");
            return false;
        }

        internal static void Clear(EmployeeComponent employee)
        {
            if (employee != null)
            {
                States.Remove(employee);
            }
        }

        internal static void Shutdown()
        {
            States.Clear();
        }

        private static NeedState GetState(EmployeeComponent employee)
        {
            if (employee == null || employee.m_state == null)
            {
                return null;
            }

            int stamp = GetStamp(employee);
            NeedState state;
            if (States.TryGetValue(employee, out state))
            {
                if (state != null && state.Stamp == stamp)
                {
                    return state;
                }
                States.Remove(employee);
            }

            state = new NeedState();
            state.Stamp = stamp;
            States[employee] = state;
            return state;
        }

        private static float GetInitialDelaySeconds(Behavior behavior)
        {
            if (behavior is BehaviorDoctor)
            {
                return 1f;
            }
            if (behavior is BehaviorNurse)
            {
                return 5f;
            }
            if (behavior is BehaviorLabSpecialist)
            {
                return 10f;
            }
            return 0f;
        }

        private static string GetTemporaryBlockReason(Behavior behavior)
        {
            BehaviorNurse nurse = behavior as BehaviorNurse;
            if (nurse != null)
            {
                Department department = nurse.GetDepartment();
                if (department == null)
                {
                    return "nurse-department-unavailable";
                }
                if (department.HasAnyCriticalPatients())
                {
                    return "nurse-critical-patients";
                }
            }
            return null;
        }

        private static bool GetNativeNeedCheck(
            Behavior behavior,
            out MethodInfo method,
            out object[] arguments)
        {
            method = null;
            arguments = null;

            if (behavior is BehaviorDoctor)
            {
                method = DoctorCheckNeeds;
                arguments = new object[] { AccessRights.STAFF_ONLY };
                return true;
            }
            if (behavior is BehaviorNurse)
            {
                method = NurseCheckNeeds;
                arguments = new object[] { AccessRights.STAFF_ONLY, false };
                return true;
            }
            if (behavior is BehaviorLabSpecialist)
            {
                method = LabSpecialistCheckNeeds;
                arguments = new object[] { AccessRights.STAFF_ONLY };
                return true;
            }
            return false;
        }

        private static Need FindAvailableAssistedNeedCandidate(
            Behavior behavior,
            EmployeeComponent employee,
            MoodComponent mood,
            ProcedureComponent procedureComponent,
            Department department,
            AccessRights accessRights,
            EquipmentListRules equipmentRules,
            out string diagnostic)
        {
            diagnostic = "reason=no-available-assisted-need";
            List<Need> needs = mood.GetNeedsSortedFromMostCritical();
            if (needs == null || needs.Count == 0)
            {
                diagnostic = "reason=no-procedural-needs-over-5";
                return null;
            }

            int eligibleCount = 0;
            int availabilityChecks = 0;
            float highestValue = 0f;
            string lastAvailability = "none";

            for (int i = 0; i < needs.Count; i++)
            {
                Need need = needs[i];
                if (need == null || need.m_gameDBNeed.Entry.Procedure == null)
                {
                    continue;
                }

                if (need.m_currentValue > highestValue)
                {
                    highestValue = need.m_currentValue;
                }

                if (need.m_currentValue < AssistedNeedMinimumValue || need.m_currentValue >= 50f)
                {
                    continue;
                }

                eligibleCount++;
                availabilityChecks++;
                ProcedureSceneAvailability availability = procedureComponent.GetProcedureAvailabilty(
                    need.m_gameDBNeed.Entry.Procedure,
                    employee.m_entity,
                    department,
                    accessRights,
                    equipmentRules);
                lastAvailability = availability.ToString();
                if (availability == ProcedureSceneAvailability.AVAILABLE)
                {
                    diagnostic = "candidate=" + need.m_gameDBNeed.Entry.DatabaseID +
                                 " | value=" + need.m_currentValue.ToString("0.0", CultureInfo.InvariantCulture);
                    return need;
                }
            }

            diagnostic = "reason=no-available-assisted-need" +
                         " | proceduralNeeds=" + needs.Count.ToString(CultureInfo.InvariantCulture) +
                         " | eligible=" + eligibleCount.ToString(CultureInfo.InvariantCulture) +
                         " | availabilityChecks=" + availabilityChecks.ToString(CultureInfo.InvariantCulture) +
                         " | highest=" + highestValue.ToString("0.0", CultureInfo.InvariantCulture) +
                         " | lastAvailability=" + lastAvailability +
                         " | rules=" + equipmentRules;
            return null;
        }

        private static string GetSatisfiedModifierId(Need need)
        {
            if (need == null || need.m_gameDBNeed.Entry.SatisfactionModifierSatisfied == null)
            {
                return null;
            }
            return need.m_gameDBNeed.Entry.SatisfactionModifierSatisfied.Entry.DatabaseID.ToString();
        }

        private static bool InvokeCheckNeeds(MethodInfo method, object instance, object[] arguments)
        {
            if (method == null || instance == null)
            {
                LogReflectionWarning("Native CheckNeeds method was not found.");
                return false;
            }

            try
            {
                object result = method.Invoke(instance, arguments);
                return result is bool && (bool)result;
            }
            catch (Exception exception)
            {
                Exception actual = exception.InnerException != null ? exception.InnerException : exception;
                LogReflectionWarning("Native CheckNeeds call failed: " + actual.Message);
                return false;
            }
        }

        private static void SwitchToNeeds(Behavior behavior)
        {
            BehaviorDoctor doctor = behavior as BehaviorDoctor;
            if (doctor != null)
            {
                doctor.SwitchState(DoctorState.FulfilingNeeds);
                return;
            }

            BehaviorNurse nurse = behavior as BehaviorNurse;
            if (nurse != null)
            {
                nurse.SwitchState(NurseState.FulfillingNeeds);
                return;
            }

            BehaviorLabSpecialist lab = behavior as BehaviorLabSpecialist;
            if (lab != null)
            {
                lab.SwitchState(LabSpecialistState.FulfillingNeeds);
            }
        }

        private static void LogDiagnosticOnce(
            NeedState state,
            Behavior behavior,
            EmployeeComponent employee,
            string type,
            string details)
        {
            string diagnostic = type + "|" + details;
            if (state != null && state.LastDiagnostic == diagnostic)
            {
                return;
            }
            if (state != null)
            {
                state.LastDiagnostic = diagnostic;
            }
            LogActivity(behavior, employee, type, details);
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

        private static void LogReflectionWarning(string message)
        {
            if (s_reflectionWarningLogged || Plugin.Log == null)
            {
                return;
            }
            s_reflectionWarningLogged = true;
            Plugin.Log.LogWarning("Pre-shift native needs disabled: " + message);
        }
    }
}
