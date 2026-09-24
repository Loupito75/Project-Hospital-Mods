using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalShiftHandover
{
    internal static class PreShiftCoordinator
    {
        private const float FinalApproachLeadMinutes = 2f;
        private const float NeedActivitySlackMinutes = 8f;
        private const float FreeTimeActivitySlackMinutes = 12f;
        private const float AssistedNeedMinimumValue = 10f;
        private const float AssistedNeedTriggerValue = 50.1f;
        private const int AssistedNeedChancePercent = 65;

        private enum TransitionPhase
        {
            None,
            PreparingApproach,
            Approaching,
            HandoverWaiting,
            FallbackWaiting,
            Released
        }

        private sealed class EmployeePlan
        {
            internal int Stamp;
            internal bool NeedAttempted;
            internal bool NeedDeferredLogged;
            internal bool FreeTimeAttempted;
            internal bool StandingWaitLogged;
            internal float LastRouteEstimateMinutes;
            internal TransitionPhase Phase;
            internal Vector2i ApproachTarget;
            internal int ApproachFloor;
            internal Entity OppositeEmployee;
            internal bool WaitLogged;
            internal bool StageUnavailableLogged;
        }

        private static readonly Dictionary<EmployeeComponent, EmployeePlan> Plans =
            new Dictionary<EmployeeComponent, EmployeePlan>();
        private static readonly Dictionary<EmployeeComponent, int> WorkplaceHoldLogStamps =
            new Dictionary<EmployeeComponent, int>();

        private static readonly MethodInfo DoctorCheckNeeds =
            AccessTools.Method(typeof(BehaviorDoctor), "CheckNeeds", new Type[] { typeof(AccessRights) });

        private static readonly MethodInfo NurseCheckNeeds =
            AccessTools.Method(typeof(BehaviorNurse), "CheckNeeds", new Type[] { typeof(AccessRights), typeof(bool) });

        private static readonly MethodInfo LabSpecialistCheckNeeds =
            AccessTools.Method(typeof(BehaviorLabSpecialist), "CheckNeeds", new Type[] { typeof(AccessRights) });

        private static bool s_reflectionWarningLogged;

        internal static bool HandleDoctorIdle(BehaviorDoctor behavior)
        {
            return HandleIdle(behavior, behavior != null ? behavior.GetComponent<EmployeeComponent>() : null);
        }

        internal static bool HandleNurseIdle(BehaviorNurse behavior)
        {
            return HandleIdle(behavior, behavior != null ? behavior.GetComponent<EmployeeComponent>() : null);
        }

        internal static bool HandleLabSpecialistIdle(BehaviorLabSpecialist behavior)
        {
            return HandleIdle(behavior, behavior != null ? behavior.GetComponent<EmployeeComponent>() : null);
        }

        internal static bool TryInterceptWorkplaceRequest(Behavior behavior, EmployeeComponent employee)
        {
            if (behavior == null || employee == null || employee.m_state == null)
            {
                return false;
            }

            EmployeePlan plan = GetPlan(employee, false);
            if (plan != null && plan.Phase == TransitionPhase.Released)
            {
                return false;
            }

            if (plan != null && IsTransitionPhase(plan.Phase))
            {
                Entity opposite;
                if (!TryGetActiveOppositeWorkspaceOwner(employee, out opposite))
                {
                    plan.Phase = TransitionPhase.Released;
                    Plans.Remove(employee);
                    WorkplaceTravelEstimator.Clear(employee);
                    return false;
                }

                plan.OppositeEmployee = opposite;
                if (plan.Phase == TransitionPhase.PreparingApproach &&
                    !TryStartApproach(behavior, employee, plan, opposite) &&
                    !WorkplaceTravelEstimator.IsApproachPending(employee))
                {
                    plan.Phase = TransitionPhase.FallbackWaiting;
                    LogStageUnavailableOnce(employee, plan);
                }

                LogWaitOnce(employee, plan, opposite);
                SwitchToIdle(behavior);
                return true;
            }

            bool isInCommonArea = PreShiftArrival.IsInCommonArea(behavior);
            bool returningFromManagedPreShiftActivity =
                plan == null &&
                !isInCommonArea &&
                PreShiftCommonAreaController.HasManagedPreShiftState(employee);
            bool protectedManagedReturnAfterShift =
                returningFromManagedPreShiftActivity &&
                HandoverRules.IsInitialArrivalProtectionWindow(employee);

            if (!HandoverRules.ShouldHoldBeforeShift(employee) && !protectedManagedReturnAfterShift)
            {
                return false;
            }

            if (returningFromManagedPreShiftActivity)
            {
                LogActivity(
                    behavior,
                    employee,
                    "return-handover-check",
                    "reason=managed-pre-shift-activity" +
                    " | postShift=" + (protectedManagedReturnAfterShift ? "yes" : "no"));
            }

            // Only a true first pre-shift workplace request is the native commute arrival.
            // An employee returning from a need/free-time already managed by HSH must first
            // check whether the final workplace handover is now due.
            if (plan == null && !isInCommonArea && !returningFromManagedPreShiftActivity)
            {
                if (PreShiftArrival.TryRouteToCommonArea(behavior, employee))
                {
                    SwitchToGoingWorkplace(behavior);
                    return true;
                }

                return false;
            }

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
                    return true;
                }

                PreShiftLockerInteraction.AbandonForWorkplaceFallback(
                    employee,
                    "route-estimate-unavailable");
                PreShiftCommonAreaController.Clear(employee);
                WorkplaceTravelEstimator.Clear(employee);
                return false;
            }

            if (plan == null)
            {
                plan = GetPlan(employee, true);
            }
            plan.LastRouteEstimateMinutes = routeEstimateMinutes;

            if (!protectedManagedReturnAfterShift &&
                !ShouldStartFinalTransition(employee, routeEstimateMinutes))
            {
                if (PreShiftArrival.TryRouteToCommonArea(behavior, employee))
                {
                    SwitchToGoingWorkplace(behavior);
                    return true;
                }

                return false;
            }

            Entity activeOpposite;
            if (!TryGetActiveOppositeWorkspaceOwner(employee, out activeOpposite))
            {
                return false;
            }

            plan.OppositeEmployee = activeOpposite;
            plan.Phase = TransitionPhase.PreparingApproach;
            if (!TryStartApproach(behavior, employee, plan, activeOpposite))
            {
                LogWaitOnce(employee, plan, activeOpposite);
                if (!WorkplaceTravelEstimator.IsApproachPending(employee))
                {
                    plan.Phase = TransitionPhase.FallbackWaiting;
                    LogStageUnavailableOnce(employee, plan);
                }
            }

            SwitchToIdle(behavior);
            return true;
        }

        internal static bool IsTransitioning(EmployeeComponent employee)
        {
            EmployeePlan plan = GetPlan(employee, false);
            return plan != null && IsTransitionPhase(plan.Phase);
        }

        internal static void Clear(EmployeeComponent employee)
        {
            if (employee == null)
            {
                return;
            }

            Plans.Remove(employee);
            WorkplaceTravelEstimator.Clear(employee);
        }

        internal static void Shutdown()
        {
            Plans.Clear();
            WorkplaceHoldLogStamps.Clear();
        }

        private static bool HandleIdle(Behavior behavior, EmployeeComponent employee)
        {
            if (behavior == null || employee == null || employee.m_state == null)
            {
                return true;
            }

            EmployeePlan plan = GetPlan(employee, false);
            if (plan != null && IsTransitionPhase(plan.Phase))
            {
                return HandleTransitionIdle(behavior, employee, plan);
            }

            if (!HandoverRules.ShouldHoldBeforeShift(employee))
            {
                if (plan != null)
                {
                    Plans.Remove(employee);
                }
                WorkplaceHoldLogStamps.Remove(employee);
                WorkplaceTravelEstimator.Clear(employee);
                return true;
            }

            if (!PreShiftArrival.IsInCommonArea(behavior))
            {
                if (IsAtWorkplace(behavior, employee))
                {
                    LogWorkplaceHoldOnce(behavior, employee);
                    return false;
                }

                return true;
            }

            plan = GetPlan(employee, true);

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
                    return false;
                }

                PreShiftLockerInteraction.AbandonForWorkplaceFallback(
                    employee,
                    "route-estimate-unavailable");
                PreShiftCommonAreaController.Clear(employee);
                RequestWorkplace(behavior);
                return false;
            }

            plan.LastRouteEstimateMinutes = routeEstimateMinutes;
            float minutesUntilShift = HandoverRules.GetMinutesUntilOwnShift(employee);
            float activitySlackMinutes = minutesUntilShift - routeEstimateMinutes;

            if (ShouldStartFinalTransition(employee, routeEstimateMinutes))
            {
                RequestWorkplace(behavior);
                return false;
            }

            if (!plan.NeedAttempted && activitySlackMinutes >= NeedActivitySlackMinutes)
            {
                // BehaviorNurse.CheckNeeds() has a native m_timeInState >= 2s guard. Do not
                // permanently consume the single pre-shift need attempt before that window opens.
                if (ShouldDeferNeedAttempt(behavior))
                {
                    return false;
                }

                string needBlockReason = GetNeedAttemptBlockReason(behavior);
                if (string.IsNullOrEmpty(needBlockReason))
                {
                    plan.NeedAttempted = true;
                    if (TryStartNeed(behavior, employee, activitySlackMinutes))
                    {
                        return false;
                    }
                }
                else if (!plan.NeedDeferredLogged)
                {
                    plan.NeedDeferredLogged = true;
                    LogActivity(
                        behavior,
                        employee,
                        "need-deferred",
                        "reason=" + needBlockReason +
                        " | slack=" + activitySlackMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");
                }
            }

            if (!plan.FreeTimeAttempted)
            {
                plan.FreeTimeAttempted = true;
                if (activitySlackMinutes >= FreeTimeActivitySlackMinutes &&
                    TryStartFreeTime(behavior, employee))
                {
                    return false;
                }
            }

            if (TryStartSeatedWait(behavior, employee))
            {
                LogActivity(behavior, employee, "seated-wait", null);
                return false;
            }

            WalkComponent waitingWalk = behavior.GetComponent<WalkComponent>();
            if (waitingWalk != null && (waitingWalk.IsBusy() || waitingWalk.IsSitting()))
            {
                return false;
            }

            if (!plan.StandingWaitLogged)
            {
                plan.StandingWaitLogged = true;
                LogActivity(behavior, employee, "standing-wait", "reason=no-native-activity-or-free-seat");
            }

            return false;
        }

        private static bool HandleTransitionIdle(
            Behavior behavior,
            EmployeeComponent employee,
            EmployeePlan plan)
        {
            Entity activeOpposite;
            if (!TryGetActiveOppositeWorkspaceOwner(employee, out activeOpposite))
            {
                if (plan.WaitLogged)
                {
                    LogHandover(employee, plan.OppositeEmployee, "WORKPLACE_HANDOVER_RELEASE", null);
                }

                plan.Phase = TransitionPhase.Released;
                RequestWorkplace(behavior);
                LogHandover(
                    employee,
                    plan.OppositeEmployee,
                    "WORKPLACE_HANDOVER_RELEASE_DISPATCH",
                    GetReleaseDispatchDetails(behavior, employee));
                Plans.Remove(employee);
                WorkplaceTravelEstimator.Clear(employee);
                return false;
            }

            plan.OppositeEmployee = activeOpposite;
            LogWaitOnce(employee, plan, activeOpposite);

            if (plan.Phase == TransitionPhase.PreparingApproach)
            {
                if (!TryStartApproach(behavior, employee, plan, activeOpposite))
                {
                    if (!WorkplaceTravelEstimator.IsApproachPending(employee))
                    {
                        plan.Phase = TransitionPhase.FallbackWaiting;
                        LogStageUnavailableOnce(employee, plan);
                    }
                    return false;
                }
            }

            if (plan.Phase == TransitionPhase.Approaching)
            {
                WalkComponent walk = behavior.GetComponent<WalkComponent>();
                if (walk == null)
                {
                    plan.Phase = TransitionPhase.FallbackWaiting;
                    return false;
                }

                if (walk.IsBusy())
                {
                    return false;
                }

                if (walk.GetFloorIndex() != plan.ApproachFloor || walk.GetCurrentTile() != plan.ApproachTarget)
                {
                    walk.SetDestination(
                        new Vector2f(plan.ApproachTarget.m_x, plan.ApproachTarget.m_y),
                        plan.ApproachFloor);
                    return false;
                }

                plan.Phase = TransitionPhase.HandoverWaiting;
                LogHandover(
                    employee,
                    activeOpposite,
                    "WORKPLACE_HANDOVER_STAGE_REACHED",
                    "position=" + plan.ApproachTarget.m_x.ToString(CultureInfo.InvariantCulture) +
                    "," + plan.ApproachTarget.m_y.ToString(CultureInfo.InvariantCulture) +
                    " | floor=" + plan.ApproachFloor.ToString(CultureInfo.InvariantCulture) +
                    " | " + GetStagePlacementDetails(employee, plan.ApproachTarget, plan.ApproachFloor));
                return false;
            }

            if (plan.Phase == TransitionPhase.FallbackWaiting && PreShiftArrival.IsInCommonArea(behavior))
            {
                TryStartSeatedWait(behavior, employee);
            }

            return false;
        }

        private static bool ShouldStartFinalTransition(EmployeeComponent employee, float routeEstimateMinutes)
        {
            return HandoverRules.GetMinutesUntilOwnShift(employee) <=
                   routeEstimateMinutes + FinalApproachLeadMinutes;
        }

        private static bool TryStartApproach(
            Behavior behavior,
            EmployeeComponent employee,
            EmployeePlan plan,
            Entity oppositeEmployee)
        {
            List<Vector2i> excludedPositions = GetClaimedApproachPositions(employee);
            Vector2i approachTarget;
            int approachFloor;
            float approachTravelMinutes;
            if (!WorkplaceTravelEstimator.TryGetApproachTarget(
                    behavior,
                    employee,
                    excludedPositions,
                    out approachTarget,
                    out approachFloor,
                    out approachTravelMinutes))
            {
                return false;
            }

            WalkComponent walk = behavior.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return false;
            }

            plan.ApproachTarget = approachTarget;
            plan.ApproachFloor = approachFloor;
            plan.OppositeEmployee = oppositeEmployee;
            plan.Phase = TransitionPhase.Approaching;
            LogWaitOnce(employee, plan, oppositeEmployee);

            walk.SetDestination(
                new Vector2f(approachTarget.m_x, approachTarget.m_y),
                approachFloor);

            LogHandover(
                employee,
                oppositeEmployee,
                "WORKPLACE_HANDOVER_STAGE",
                "position=" + approachTarget.m_x.ToString(CultureInfo.InvariantCulture) +
                "," + approachTarget.m_y.ToString(CultureInfo.InvariantCulture) +
                " | floor=" + approachFloor.ToString(CultureInfo.InvariantCulture) +
                " | source=path-route" +
                " | " + GetStagePlacementDetails(employee, approachTarget, approachFloor) +
                " | travel=" + approachTravelMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");

            WorkplaceTravelEstimator.Clear(employee);
            return true;
        }

        private static List<Vector2i> GetClaimedApproachPositions(EmployeeComponent employee)
        {
            List<Vector2i> positions = new List<Vector2i>();
            foreach (KeyValuePair<EmployeeComponent, EmployeePlan> pair in Plans)
            {
                if (pair.Key == employee || pair.Value == null)
                {
                    continue;
                }

                if (pair.Value.Phase == TransitionPhase.Approaching ||
                    pair.Value.Phase == TransitionPhase.HandoverWaiting)
                {
                    positions.Add(pair.Value.ApproachTarget);
                }
            }
            return positions;
        }

        private static bool ShouldDeferNeedAttempt(Behavior behavior)
        {
            BehaviorNurse nurse = behavior as BehaviorNurse;
            return nurse != null && nurse.m_state != null && nurse.m_state.m_timeInState < 2f;
        }

        private static string GetNeedAttemptBlockReason(Behavior behavior)
        {
            BehaviorNurse nurse = behavior as BehaviorNurse;
            if (nurse == null)
            {
                return null;
            }

            Department department = nurse.GetDepartment();
            if (department == null)
            {
                return "nurse-department-unavailable";
            }

            return department.HasAnyCriticalPatients()
                ? "nurse-critical-patients"
                : null;
        }

        private static bool TryStartNeed(
            Behavior behavior,
            EmployeeComponent employee,
            float activitySlackMinutes)
        {
            MethodInfo checkNeedsMethod;
            object[] checkNeedsArguments;
            if (!GetNeedCheck(behavior, out checkNeedsMethod, out checkNeedsArguments))
            {
                LogActivity(behavior, employee, "need-skip", "reason=native-check-unavailable");
                return false;
            }

            if (InvokeCheckNeeds(checkNeedsMethod, behavior, checkNeedsArguments))
            {
                SwitchToNeeds(behavior);
                LogActivity(behavior, employee, "native-needs",
                    "slack=" + activitySlackMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");
                return true;
            }

            int assistedRoll = GetStableValue(employee, 631, 100);
            if (assistedRoll >= AssistedNeedChancePercent)
            {
                LogActivity(
                    behavior,
                    employee,
                    "need-skip",
                    "reason=assisted-chance" +
                    " | roll=" + assistedRoll.ToString(CultureInfo.InvariantCulture) +
                    " | limit=" + AssistedNeedChancePercent.ToString(CultureInfo.InvariantCulture) +
                    " | slack=" + activitySlackMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");
                return false;
            }

            MoodComponent mood = behavior.GetComponent<MoodComponent>();
            if (mood == null)
            {
                LogActivity(behavior, employee, "need-skip", "reason=mood-unavailable");
                return false;
            }

            AccessRights needAccessRights;
            if (!GetNeedAccessRights(behavior, out needAccessRights))
            {
                LogActivity(behavior, employee, "need-skip", "reason=need-access-unavailable");
                return false;
            }

            string candidateDiagnostic;
            Need selectedNeed = FindAvailableAssistedNeedCandidate(
                behavior,
                employee,
                mood,
                needAccessRights,
                out candidateDiagnostic);
            if (selectedNeed == null)
            {
                LogActivity(
                    behavior,
                    employee,
                    "need-skip",
                    candidateDiagnostic +
                    " | access=" + needAccessRights +
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
            if (InvokeCheckNeeds(checkNeedsMethod, behavior, checkNeedsArguments))
            {
                SwitchToNeeds(behavior);
                LogActivity(
                    behavior,
                    employee,
                    "native-needs-assisted",
                    "need=" + selectedNeed.m_gameDBNeed.Entry.DatabaseID +
                    " | old=" + previousValue.ToString("0.0", CultureInfo.InvariantCulture) +
                    " | access=" + needAccessRights +
                    " | slack=" + activitySlackMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");
                return true;
            }

            selectedNeed.m_currentValue = previousValue;
            if (hadSatisfiedModifier && !mood.HasSatisfactionModifier(satisfiedModifierId))
            {
                mood.AddSatisfactionModifier(satisfiedModifierId);
            }

            LogActivity(
                behavior,
                employee,
                "need-skip",
                "reason=assisted-check-rejected" +
                " | need=" + selectedNeed.m_gameDBNeed.Entry.DatabaseID +
                " | old=" + previousValue.ToString("0.0", CultureInfo.InvariantCulture) +
                " | access=" + needAccessRights +
                " | slack=" + activitySlackMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m");
            return false;
        }

        private static bool GetNeedCheck(
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
                arguments = new object[] { AccessRights.STAFF, false };
                return true;
            }

            if (behavior is BehaviorLabSpecialist)
            {
                method = LabSpecialistCheckNeeds;
                arguments = new object[] { AccessRights.STAFF };
                return true;
            }

            return false;
        }

        private static bool GetNeedAccessRights(Behavior behavior, out AccessRights accessRights)
        {
            accessRights = AccessRights.STAFF_ONLY;

            if (behavior is BehaviorDoctor)
            {
                accessRights = AccessRights.STAFF_ONLY;
                return true;
            }

            if (behavior is BehaviorNurse)
            {
                accessRights = AccessRights.STAFF;
                return true;
            }

            if (behavior is BehaviorLabSpecialist)
            {
                accessRights = AccessRights.STAFF;
                return true;
            }

            return false;
        }

        private static Need FindAvailableAssistedNeedCandidate(
            Behavior behavior,
            EmployeeComponent employee,
            MoodComponent mood,
            AccessRights accessRights,
            out string diagnostic)
        {
            diagnostic = "reason=no-available-assisted-need";
            if (behavior == null || employee == null || mood == null || employee.m_entity == null)
            {
                diagnostic = "reason=need-context-unavailable";
                return null;
            }

            Department department = behavior.GetDepartment();
            ProcedureComponent procedureComponent = behavior.GetComponent<ProcedureComponent>();
            if (department == null)
            {
                diagnostic = "reason=department-unavailable";
                return null;
            }
            if (procedureComponent == null)
            {
                diagnostic = "reason=procedure-component-unavailable";
                return null;
            }
            if (procedureComponent.IsBusy())
            {
                diagnostic = "reason=procedure-component-busy";
                return null;
            }

            List<Need> needs = mood.GetNeedsSortedFromMostCritical();
            if (needs == null || needs.Count == 0)
            {
                diagnostic = "reason=no-procedural-needs-over-5";
                return null;
            }

            EquipmentListRules equipmentListRules = !employee.ShouldGoToTraining()
                ? EquipmentListRules.ONLY_FREE_SAME_FLOOR_PREFER_DPT
                : EquipmentListRules.ONLY_FREE_SAME_FLOOR;

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
                    equipmentListRules);
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
                         " | rules=" + equipmentListRules;
            return null;
        }

        private static string GetSatisfiedModifierId(Need need)
        {
            if (need == null ||
                need.m_gameDBNeed.Entry.SatisfactionModifierSatisfied == null)
            {
                return null;
            }

            return need.m_gameDBNeed.Entry.SatisfactionModifierSatisfied.Entry.DatabaseID.ToString();
        }

        private static bool TryStartFreeTime(Behavior behavior, EmployeeComponent employee)
        {
            if (behavior == null || employee == null ||
                SettingsManager.Instance == null ||
                SettingsManager.Instance.m_debugSettings.m_disableFreeTime.m_value)
            {
                return false;
            }

            PerkComponent perkComponent = behavior.GetComponent<PerkComponent>();
            if (perkComponent != null &&
                perkComponent.m_perkSet != null &&
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
            if (behavior == null || employee == null)
            {
                return false;
            }

            WalkComponent walk = behavior.GetComponent<WalkComponent>();
            if (walk == null || walk.IsBusy() || walk.IsSitting() || walk.Floor == null)
            {
                return false;
            }

            Room room = MapScriptInterface.Instance.GetRoomAt(walk.GetCurrentTile(), walk.GetFloorIndex());
            if (room == null)
            {
                return false;
            }

            GameDBRoomType commonRoomType = Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_COMMON_ROOM");
            if (commonRoomType == null || room.m_roomPersistentData.m_roomType.Entry != commonRoomType)
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

        private static bool TryGetActiveOppositeWorkspaceOwner(
            EmployeeComponent employee,
            out Entity oppositeEmployee)
        {
            oppositeEmployee = null;

            if (employee == null || employee.m_state == null || employee.IsFired() ||
                employee.m_state.m_workDesk == null)
            {
                return false;
            }

            TileObject workDesk = employee.m_state.m_workDesk.GetEntity();
            if (workDesk == null || workDesk.m_state == null)
            {
                return false;
            }

            Shift oppositeShift = employee.m_state.m_shift == Shift.DAY ? Shift.NIGHT : Shift.DAY;
            Entity exactOpposite = workDesk.GetWorkspaceOwner(oppositeShift);
            if (IsValidExactWorkspaceOwner(employee, workDesk, exactOpposite))
            {
                if (DoctorStillHasCurrentPatient(exactOpposite))
                {
                    oppositeEmployee = exactOpposite;
                    return true;
                }

                if (!IsFinishedOrLeaving(exactOpposite))
                {
                    oppositeEmployee = exactOpposite;
                    return true;
                }

                EmployeeComponent exactComponent = exactOpposite.GetComponent<EmployeeComponent>();
                TileObject oppositeChair = exactComponent != null ? exactComponent.GetWorkChair() : null;
                if (oppositeChair != null && oppositeChair.User == exactOpposite)
                {
                    oppositeEmployee = exactOpposite;
                    return true;
                }
            }

            // A consultation can still be functionally occupied even when the exact chair is
            // empty: the outgoing doctor may be standing next to the patient while finishing
            // the same consultation. For doctors only, protect the workplace room until any
            // opposite-shift doctor in that room has released CurrentPatient.
            if (employee.m_entity != null && employee.m_entity.GetComponent<BehaviorDoctor>() != null &&
                TryGetBusyOppositeDoctorInWorkplaceRoom(employee, workDesk, out oppositeEmployee))
            {
                return true;
            }

            oppositeEmployee = null;
            return false;
        }

        private static bool IsValidExactWorkspaceOwner(
            EmployeeComponent employee,
            TileObject workDesk,
            Entity candidate)
        {
            if (employee == null || employee.m_state == null || workDesk == null || candidate == null ||
                candidate == employee.m_entity)
            {
                return false;
            }

            EmployeeComponent candidateComponent = candidate.GetComponent<EmployeeComponent>();
            return candidateComponent != null && candidateComponent.m_state != null &&
                   !candidateComponent.IsFired() &&
                   candidateComponent.m_state.m_shift != employee.m_state.m_shift &&
                   candidateComponent.m_state.m_workDesk != null &&
                   candidateComponent.m_state.m_workDesk.GetEntity() == workDesk;
        }

        private static bool DoctorStillHasCurrentPatient(Entity employee)
        {
            if (employee == null)
            {
                return false;
            }

            BehaviorDoctor doctor = employee.GetComponent<BehaviorDoctor>();
            return doctor != null && doctor.CurrentPatient != null;
        }

        private static bool TryGetBusyOppositeDoctorInWorkplaceRoom(
            EmployeeComponent employee,
            TileObject workDesk,
            out Entity oppositeDoctor)
        {
            oppositeDoctor = null;

            if (employee == null || employee.m_state == null || workDesk == null || workDesk.m_state == null ||
                Hospital.Instance == null || Hospital.Instance.m_characters == null)
            {
                return false;
            }

            Room workplaceRoom = MapScriptInterface.Instance.GetRoomAt(
                workDesk.m_state.m_position,
                workDesk.GetFloorIndex());
            if (workplaceRoom == null)
            {
                return false;
            }

            foreach (Entity character in Hospital.Instance.m_characters)
            {
                if (character == null || character == employee.m_entity)
                {
                    continue;
                }

                BehaviorDoctor doctor = character.GetComponent<BehaviorDoctor>();
                EmployeeComponent otherEmployee = character.GetComponent<EmployeeComponent>();
                if (doctor == null || doctor.m_state == null || doctor.CurrentPatient == null ||
                    otherEmployee == null || otherEmployee.m_state == null || otherEmployee.IsFired() ||
                    otherEmployee.m_state.m_shift == employee.m_state.m_shift ||
                    otherEmployee.m_state.m_workDesk == null)
                {
                    continue;
                }

                TileObject otherDesk = otherEmployee.m_state.m_workDesk.GetEntity();
                if (otherDesk == null || otherDesk.m_state == null)
                {
                    continue;
                }

                Room otherRoom = MapScriptInterface.Instance.GetRoomAt(
                    otherDesk.m_state.m_position,
                    otherDesk.GetFloorIndex());
                if (otherRoom != workplaceRoom)
                {
                    continue;
                }

                oppositeDoctor = character;
                return true;
            }

            return false;
        }

        private static bool IsFinishedOrLeaving(Entity employee)
        {
            if (employee == null)
            {
                return true;
            }

            BehaviorDoctor doctor = employee.GetComponent<BehaviorDoctor>();
            if (doctor != null && doctor.m_state != null)
            {
                DoctorState state = doctor.m_state.m_doctorState;
                return state == DoctorState.GoingHome || state == DoctorState.AtHome ||
                       state == DoctorState.Commuting || state == DoctorState.FiredAtHome;
            }

            BehaviorNurse nurse = employee.GetComponent<BehaviorNurse>();
            if (nurse != null && nurse.m_state != null)
            {
                NurseState state = nurse.m_state.m_nurseState;
                return state == NurseState.GoingHome || state == NurseState.AtHome ||
                       state == NurseState.Commuting || state == NurseState.FiredAtHome;
            }

            BehaviorLabSpecialist lab = employee.GetComponent<BehaviorLabSpecialist>();
            if (lab != null && lab.m_state != null)
            {
                LabSpecialistState state = lab.m_state.m_labSpecialistState;
                return state == LabSpecialistState.GoingHome || state == LabSpecialistState.AtHome ||
                       state == LabSpecialistState.Commuting || state == LabSpecialistState.FiredAtHome;
            }

            return true;
        }

        private static string GetStagePlacementDetails(EmployeeComponent employee, Vector2i target, int floorIndex)
        {
            if (employee == null || employee.m_state == null || Hospital.Instance == null ||
                Hospital.Instance.m_floors == null || floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return "mode=unknown | room=unknown";
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null)
            {
                return "mode=unknown | room=unknown";
            }

            Room workplaceRoom = null;
            if (employee.m_state.m_workDesk != null)
            {
                TileObject workDesk = employee.m_state.m_workDesk.GetEntity();
                if (workDesk != null && workDesk.m_state != null && workDesk.GetFloorIndex() == floorIndex)
                {
                    workplaceRoom = floor.GetRoomTileSafe(
                        workDesk.m_state.m_position.m_x,
                        workDesk.m_state.m_position.m_y);
                }
            }

            Room targetRoom = floor.GetRoomTileSafe(target.m_x, target.m_y);
            GameDBRoomType roomType = workplaceRoom != null && workplaceRoom.m_roomPersistentData != null
                ? workplaceRoom.m_roomPersistentData.m_roomType.Entry
                : null;
            string roomId = roomType != null ? roomType.DatabaseID + string.Empty : "unknown";

            if (workplaceRoom != null && targetRoom == workplaceRoom)
            {
                return "mode=inside-staff | room=" + roomId;
            }

            if (roomType != null && roomType.AccessRights == AccessRights.PATIENT_PROCEDURE)
            {
                return "mode=outside-patient | room=" + roomId;
            }

            return "mode=outside-fallback | room=" + roomId;
        }

        private static string GetReleaseDispatchDetails(Behavior behavior, EmployeeComponent employee)
        {
            if (behavior == null || employee == null)
            {
                return "state=unknown | atWorkplace=unknown | walkBusy=unknown";
            }

            WalkComponent walk = behavior.GetComponent<WalkComponent>();
            string details = "state=" + GetBehaviorState(behavior) +
                             " | atWorkplace=" + (IsAtWorkplace(behavior, employee) ? "yes" : "no");
            if (walk == null)
            {
                return details + " | walkBusy=unknown";
            }

            Vector2i tile = walk.GetCurrentTile();
            return details +
                   " | walkBusy=" + (walk.IsBusy() ? "yes" : "no") +
                   " | floor=" + walk.GetFloorIndex().ToString(CultureInfo.InvariantCulture) +
                   " | tile=" + tile.m_x.ToString(CultureInfo.InvariantCulture) +
                   "," + tile.m_y.ToString(CultureInfo.InvariantCulture);
        }

        private static string GetBehaviorState(Behavior behavior)
        {
            BehaviorDoctor doctor = behavior as BehaviorDoctor;
            if (doctor != null && doctor.m_state != null)
            {
                return doctor.m_state.m_doctorState.ToString();
            }

            BehaviorNurse nurse = behavior as BehaviorNurse;
            if (nurse != null && nurse.m_state != null)
            {
                return nurse.m_state.m_nurseState.ToString();
            }

            BehaviorLabSpecialist lab = behavior as BehaviorLabSpecialist;
            if (lab != null && lab.m_state != null)
            {
                return lab.m_state.m_labSpecialistState.ToString();
            }

            return "unknown";
        }

        private static bool IsAtWorkplace(Behavior behavior, EmployeeComponent employee)
        {
            BehaviorDoctor doctor = behavior as BehaviorDoctor;
            if (doctor != null)
            {
                return doctor.IsAtWorkplace(employee);
            }

            BehaviorNurse nurse = behavior as BehaviorNurse;
            if (nurse != null)
            {
                return nurse.IsAtWorkplace(employee);
            }

            BehaviorLabSpecialist lab = behavior as BehaviorLabSpecialist;
            return lab != null && lab.IsAtWorkplace(employee);
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

        private static void SwitchToGoingWorkplace(Behavior behavior)
        {
            BehaviorDoctor doctor = behavior as BehaviorDoctor;
            if (doctor != null)
            {
                doctor.SwitchState(DoctorState.GoingToWorkPlace);
                return;
            }

            BehaviorNurse nurse = behavior as BehaviorNurse;
            if (nurse != null)
            {
                nurse.SwitchState(NurseState.GoingToWorkplace);
                return;
            }

            BehaviorLabSpecialist lab = behavior as BehaviorLabSpecialist;
            if (lab != null)
            {
                lab.SwitchState(LabSpecialistState.GoingToWorkplace);
            }
        }

        private static void SwitchToIdle(Behavior behavior)
        {
            BehaviorDoctor doctor = behavior as BehaviorDoctor;
            if (doctor != null)
            {
                doctor.SwitchState(DoctorState.Idle);
                return;
            }

            BehaviorNurse nurse = behavior as BehaviorNurse;
            if (nurse != null)
            {
                nurse.SwitchState(NurseState.Idle);
                return;
            }

            BehaviorLabSpecialist lab = behavior as BehaviorLabSpecialist;
            if (lab != null)
            {
                lab.SwitchState(LabSpecialistState.Idle);
            }
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

        private static EmployeePlan GetPlan(EmployeeComponent employee, bool create)
        {
            if (employee == null || employee.m_state == null)
            {
                return null;
            }

            int stamp = GetStamp(employee);
            EmployeePlan plan;
            if (Plans.TryGetValue(employee, out plan))
            {
                if (plan != null && plan.Stamp == stamp)
                {
                    return plan;
                }

                Plans.Remove(employee);
            }

            if (!create)
            {
                return null;
            }

            plan = new EmployeePlan();
            plan.Stamp = stamp;
            plan.Phase = TransitionPhase.None;
            Plans[employee] = plan;
            return plan;
        }

        private static bool IsTransitionPhase(TransitionPhase phase)
        {
            return phase == TransitionPhase.PreparingApproach ||
                   phase == TransitionPhase.Approaching ||
                   phase == TransitionPhase.HandoverWaiting ||
                   phase == TransitionPhase.FallbackWaiting;
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

        private static void LogWaitOnce(EmployeeComponent employee, EmployeePlan plan, Entity opposite)
        {
            if (plan == null || plan.WaitLogged)
            {
                return;
            }

            plan.WaitLogged = true;
            LogHandover(
                employee,
                opposite,
                "WORKPLACE_HANDOVER_WAIT",
                "scope=" + GetHandoverScope(employee, opposite));
        }

        private static string GetHandoverScope(EmployeeComponent employee, Entity opposite)
        {
            if (employee == null || employee.m_state == null || employee.m_state.m_workDesk == null || opposite == null)
            {
                return "unknown";
            }

            TileObject workDesk = employee.m_state.m_workDesk.GetEntity();
            EmployeeComponent oppositeComponent = opposite.GetComponent<EmployeeComponent>();
            if (workDesk != null && oppositeComponent != null && oppositeComponent.m_state != null &&
                oppositeComponent.m_state.m_workDesk != null &&
                oppositeComponent.m_state.m_workDesk.GetEntity() == workDesk)
            {
                return "workdesk";
            }

            BehaviorDoctor oppositeDoctor = opposite.GetComponent<BehaviorDoctor>();
            if (oppositeDoctor != null && oppositeDoctor.CurrentPatient != null)
            {
                return "room-current-patient";
            }

            return "room";
        }

        private static void LogStageUnavailableOnce(EmployeeComponent employee, EmployeePlan plan)
        {
            if (plan == null || plan.StageUnavailableLogged)
            {
                return;
            }

            plan.StageUnavailableLogged = true;
            LogHandover(
                employee,
                plan.OppositeEmployee,
                "WORKPLACE_HANDOVER_STAGE_UNAVAILABLE",
                "fallback=stay-current-position" +
                " | reason=" + WorkplaceTravelEstimator.GetLastApproachFailureReason(employee));
        }

        private static void LogWorkplaceHoldOnce(
            Behavior behavior,
            EmployeeComponent employee)
        {
            int stamp = GetStamp(employee);
            int loggedStamp;
            if (WorkplaceHoldLogStamps.TryGetValue(employee, out loggedStamp) &&
                loggedStamp == stamp)
            {
                return;
            }

            WorkplaceHoldLogStamps[employee] = stamp;
            LogActivity(
                behavior,
                employee,
                "workplace-hold",
                "reason=own-shift-not-started");
        }

        private static void LogActivity(
            Behavior behavior,
            EmployeeComponent employee,
            string type,
            string details)
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

        private static void LogHandover(
            EmployeeComponent employee,
            Entity opposite,
            string eventName,
            string details)
        {
            if (Plugin.Log == null || DayTime.Instance == null || employee == null || employee.m_state == null)
            {
                return;
            }

            string suffix = string.IsNullOrEmpty(details) ? string.Empty : " | " + details;
            Plugin.Log.LogInfo(
                "[SHIFT] " + GetTimestamp() +
                " | " + GetProfession(employee.m_entity) +
                " | " + GetCharacterName(employee.m_entity) +
                " | " + employee.m_state.m_shift +
                " | " + eventName +
                " | opposite=" + GetCharacterName(opposite) + suffix);
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

        private static string GetProfession(Entity entity)
        {
            if (entity == null)
            {
                return "Staff";
            }
            if (entity.GetComponent<BehaviorDoctor>() != null)
            {
                return "Doctor";
            }
            if (entity.GetComponent<BehaviorNurse>() != null)
            {
                return "Nurse";
            }
            if (entity.GetComponent<BehaviorLabSpecialist>() != null)
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
            if (personalInfo != null && personalInfo.m_personalInfo != null)
            {
                string fullName = personalInfo.m_personalInfo.GetFullName();
                if (!string.IsNullOrEmpty(fullName))
                {
                    fullName = fullName.Trim();
                    if (fullName.Length > 0)
                    {
                        return fullName;
                    }
                }
            }

            string entityName = entity.Name;
            return string.IsNullOrEmpty(entityName) ? "Unknown" : entityName.Trim();
        }

        private static string GetTimestamp()
        {
            float hours = DayTime.Instance.GetDayTimeHours();
            int totalSeconds = (int)(hours * 3600f + 0.5f);
            totalSeconds %= 24 * 3600;
            if (totalSeconds < 0)
            {
                totalSeconds += 24 * 3600;
            }

            int hour = totalSeconds / 3600;
            int minute = (totalSeconds % 3600) / 60;
            int second = totalSeconds % 60;
            return "D" + DayTime.Instance.GetDay().ToString(CultureInfo.InvariantCulture) +
                   " " + hour.ToString("00", CultureInfo.InvariantCulture) +
                   ":" + minute.ToString("00", CultureInfo.InvariantCulture) +
                   ":" + second.ToString("00", CultureInfo.InvariantCulture);
        }

        private static void LogReflectionWarning(string message)
        {
            if (s_reflectionWarningLogged)
            {
                return;
            }

            s_reflectionWarningLogged = true;
            if (Plugin.Log != null)
            {
                Plugin.Log.LogWarning("Pre-shift native need helper disabled: " + message);
            }
        }
    }
}
