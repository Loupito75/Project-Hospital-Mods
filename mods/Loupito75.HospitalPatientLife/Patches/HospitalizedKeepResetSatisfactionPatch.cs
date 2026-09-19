using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    [HarmonyPatch(typeof(MoodComponent), "KeepReset")]
    internal static class HospitalizedKeepResetSatisfactionPatch
    {
        private static bool Prefix(MoodComponent __instance)
        {
            if (__instance == null || __instance.m_entity == null || Database.Instance == null)
            {
                return true;
            }

            HospitalizationComponent hospitalization =
                __instance.m_entity.GetComponent<HospitalizationComponent>();
            if (hospitalization == null ||
                hospitalization.m_state == null ||
                hospitalization.m_state.m_hospitalizationTreatment == null ||
                hospitalization.m_state.m_hospitalizationTreatment.Entry == null)
            {
                return true;
            }

            GameDBTreatment treatment =
                hospitalization.m_state.m_hospitalizationTreatment.Entry;

            // ICU and Trauma keep the exact vanilla behavior: KeepReset() runs every
            // update and fully resets both bladder and patient hunger to zero.
            if (treatment == Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_ICU") ||
                treatment == Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_TRAUMA"))
            {
                return true;
            }

            // KeepReset() is only called by vanilla for ICU, Trauma or patients that
            // cannot walk. Preserve vanilla unchanged if another caller ever invokes it
            // for a walking patient.
            if (hospitalization.IsAllowedToWalk())
            {
                return true;
            }

            Need hunger = __instance.GetNeed("NEED_HUNGER_PATIENT");
            if (hunger != null)
            {
                // Hunger is deliberately left as vanilla for now.
                hunger.Reset();
            }

            Need bladder = __instance.GetNeed("NEED_BLADDER");
            if (bladder != null && bladder.m_currentValue >= Need.NEED_LEVEL_CRITICAL)
            {
                float previousValue = bladder.m_currentValue;
                float remaining = UnityEngine.Random.Range(
                    (float)HospitalPatientLifeConfig.BedsideBladderRemainingMin,
                    (float)HospitalPatientLifeConfig.BedsideBladderRemainingMax);

                bladder.m_currentValue = remaining;

                Entity patient = __instance.m_entity;
                if (HospitalizedPatientTrace.IsTrackedPatient(patient))
                {
                    HospitalizedPatientTrace.Log(
                        HospitalizedPatientTrace.GetName(patient) +
                        " | bedside-bladder-relief=APPLIED" +
                        " | before=" + previousValue.ToString("0.0") +
                        " | after=" + remaining.ToString("0.0") +
                        " | range=" + HospitalPatientLifeConfig.BedsideBladderRemainingMin +
                        "-" + HospitalPatientLifeConfig.BedsideBladderRemainingMax);
                }
            }

            __instance.UpdateTotalSatisfaction();
            return false;
        }

        private static void Postfix(MoodComponent __instance)
        {
            if (__instance == null || Database.Instance == null)
            {
                return;
            }

            bool bladderChanged = ClearCriticalModifier(__instance, "NEED_BLADDER");
            bool hungerChanged = ClearCriticalModifier(__instance, "NEED_HUNGER_PATIENT");
            if (!bladderChanged && !hungerChanged)
            {
                return;
            }

            __instance.UpdateTotalSatisfaction();

            Entity patient = __instance.m_entity;
            if (HospitalizedPatientTrace.IsTrackedPatient(patient))
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | bedside-reset-satisfaction=NORMALIZED" +
                    " | bladderCriticalRemoved=" + bladderChanged +
                    " | hungerCriticalRemoved=" + hungerChanged +
                    " | bladder=" + HospitalizedPatientTrace.GetNeedValue(patient, "NEED_BLADDER") +
                    " | hunger=" + HospitalizedPatientTrace.GetNeedValue(patient, "NEED_HUNGER_PATIENT") +
                    " | satisfaction=" + __instance.GetTotalSatisfaction());
            }
        }

        private static bool ClearCriticalModifier(MoodComponent mood, string needId)
        {
            GameDBNeed need = Database.Instance.GetEntry<GameDBNeed>(needId);
            if (need == null ||
                need.SatisfactionModifierCritical == null ||
                need.SatisfactionModifierCritical.Entry == null)
            {
                return false;
            }

            string modifierId = need.SatisfactionModifierCritical.Entry.DatabaseID.ToString();
            bool changed = false;

            while (mood.HasSatisfactionModifier(modifierId))
            {
                mood.RemoveSatisfactionModifier(modifierId);
                changed = true;
            }

            return changed;
        }
    }
}
