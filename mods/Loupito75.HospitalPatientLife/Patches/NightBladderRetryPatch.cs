using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    // Vanilla sleeping patients only evaluate SelectNextStep every two seconds.
    // Under heavy WC traffic, a toilet can become free and be reserved again by
    // staff before an urgent hospitalized patient reaches the next two-second tick.
    //
    // Keep the vanilla sleeping state machine and medical priority intact, but when
    // an eligible urgent-bladder patient remains asleep after one of those ticks,
    // preload the existing native timer so the next SelectNextStep happens after
    // roughly the same 0.5-0.9 second cadence vanilla uses while patients are InBed.
    [HarmonyPatch(typeof(HospitalizationComponent), "UpdateStateSleeping")]
    internal static class NightBladderRetryPatch
    {
        private static void Prefix(HospitalizationComponent __instance, out float __state)
        {
            __state = __instance == null ? 0f : __instance.m_nextStepDelay;
        }

        private static void Postfix(HospitalizationComponent __instance, float __state)
        {
            if (__instance == null ||
                __instance.m_entity == null ||
                __instance.m_state == null ||
                __state <= 2f ||
                !HospitalPatientLifeConfig.AllowNightBathroomTrips ||
                DayTime.Instance == null ||
                DayTime.Instance.IsOpenForStaff() ||
                __instance.m_state.m_hospitalizationState != HospitalizationState.Sleeping)
            {
                return;
            }

            Entity patient = __instance.m_entity;
            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            ProcedureComponent procedures = patient.GetComponent<ProcedureComponent>();
            MoodComponent mood = patient.GetComponent<MoodComponent>();

            if (behavior == null ||
                procedures == null ||
                mood == null ||
                !HospitalizedNeedsPriorityRules.CanRunNightBathroomTrip(
                    __instance,
                    behavior,
                    procedures))
            {
                return;
            }

            Need bladderNeed = mood.GetNeed("NEED_BLADDER");
            float bladder = bladderNeed == null ? 0f : bladderNeed.m_currentValue;
            if (bladder < HospitalPatientLifeConfig.UrgentBladderThreshold)
            {
                return;
            }

            float retryDelay = UnityEngine.Random.Range(0.5f, 0.9f);
            float preloadedDelay = 2f - retryDelay;
            if (__instance.m_nextStepDelay < preloadedDelay)
            {
                __instance.m_nextStepDelay = preloadedDelay;
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | night-bladder-retry=SCHEDULED" +
                    " | gameTime=" + HospitalizedPatientTrace.GetGameTime() +
                    " | bladder=" + bladder.ToString("0.0") +
                    " | retryDelay=" + retryDelay.ToString("0.00") +
                    " | preloadedDelay=" + preloadedDelay.ToString("0.00") +
                    " | floor=" + HospitalizedPatientTrace.GetFloor(patient));
            }
        }
    }
}
