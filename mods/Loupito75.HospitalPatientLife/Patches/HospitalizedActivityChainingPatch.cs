using HarmonyLib;
using GLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class HospitalizedActivityChainingRules
    {
        internal static bool TryStartNextNeed(
            HospitalizationComponent hospitalization,
            string source)
        {
            if (!HospitalPatientLifeConfig.AllowPersonalNeedChaining ||
                hospitalization == null ||
                hospitalization.m_entity == null ||
                hospitalization.m_state == null)
            {
                return false;
            }

            HospitalizationState state =
                hospitalization.m_state.m_hospitalizationState;
            if (state != HospitalizationState.FulfillingNeeds &&
                state != HospitalizationState.FillingFreeTime)
            {
                return false;
            }

            Entity patient = hospitalization.m_entity;
            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            ProcedureComponent procedures =
                patient.GetComponent<ProcedureComponent>();

            if (behavior == null ||
                procedures == null ||
                procedures.IsBusy() ||
                !HospitalizedNeedsPriorityRules.CanChainPersonalNeed(
                    hospitalization,
                    behavior,
                    procedures))
            {
                return false;
            }

            // A scheduled meal is already a native hospitalization priority and
            // should be handled by the normal InBed -> CheckNeeds path.
            if (hospitalization.m_state.m_lunchReady &&
                !hospitalization.m_state.m_lunchEaten)
            {
                return false;
            }

            // Do not extend a patient's day past the same personal bedtime used by
            // the sleep-stagger patch. Real needs can still wake the patient later
            // through the existing night-bathroom logic.
            if (!HospitalizedSleepStaggerRules.IsOpenForStaffForBedtime(
                    DayTime.Instance,
                    hospitalization))
            {
                return false;
            }

            // Reuse Project Hospital's native need ordering, >50 threshold,
            // availability checks and procedure start. If nothing qualifies,
            // vanilla immediately continues with the normal return to bed.
            if (!behavior.CheckNeeds(AccessRights.PATIENT))
            {
                return false;
            }

            hospitalization.GetCovered(LyingState.UNCOVERED);
            hospitalization.m_state.m_procedureReservationStatus =
                ProcedureReservationStatus.NONE;
            hospitalization.m_nextStepDelay = 0f;
            hospitalization.SwitchState(HospitalizationState.FulfillingNeeds);

            if (HospitalizedPatientTrace.Enabled)
            {
                MoodComponent mood = patient.GetComponent<MoodComponent>();
                Need hunger = mood == null
                    ? null
                    : mood.GetNeed("NEED_HUNGER_PATIENT");
                Need bladder = mood == null
                    ? null
                    : mood.GetNeed("NEED_BLADDER");
                Need boredom = mood == null
                    ? null
                    : mood.GetNeed("NEED_BOREDOM");

                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | activity-chain=STARTED" +
                    " | source=" + source +
                    " | hunger=" +
                    (hunger == null
                        ? "n/a"
                        : hunger.m_currentValue.ToString("0.0")) +
                    " | bladder=" +
                    (bladder == null
                        ? "n/a"
                        : bladder.m_currentValue.ToString("0.0")) +
                    " | boredom=" +
                    (boredom == null
                        ? "n/a"
                        : boredom.m_currentValue.ToString("0.0")) +
                    " | floor=" +
                    HospitalizedPatientTrace.GetFloor(patient));
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(HospitalizationComponent), "UpdateStateFulfillingNeeds")]
    internal static class HospitalizedNeedChainingPatch
    {
        private static bool Prefix(HospitalizationComponent __instance)
        {
            if (__instance == null ||
                __instance.m_entity == null)
            {
                return true;
            }

            ProcedureComponent procedures =
                __instance.m_entity.GetComponent<ProcedureComponent>();
            if (procedures == null || procedures.IsBusy())
            {
                return true;
            }

            return !HospitalizedActivityChainingRules.TryStartNextNeed(
                __instance,
                "NEED");
        }
    }

    [HarmonyPatch(typeof(HospitalizationComponent), "UpdateStateFulfillingFreeTime")]
    internal static class HospitalizedFreeTimeNeedChainingPatch
    {
        private static bool Prefix(HospitalizationComponent __instance)
        {
            if (__instance == null ||
                __instance.m_entity == null)
            {
                return true;
            }

            ProcedureComponent procedures =
                __instance.m_entity.GetComponent<ProcedureComponent>();
            if (procedures == null || procedures.IsBusy())
            {
                return true;
            }

            return !HospitalizedActivityChainingRules.TryStartNextNeed(
                __instance,
                "FREE_TIME");
        }
    }
}
