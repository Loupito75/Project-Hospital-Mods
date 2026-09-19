using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class NightBathroomChancePatch
    {
        private const float SecondChanceThreshold = 90f;
        private const float ForcedWakeThreshold = 100f;

        private sealed class EpisodeState
        {
            internal int HighestStage;
            internal bool Allowed;
        }

        private static readonly Dictionary<Entity, EpisodeState> EpisodeDecisions =
            new Dictionary<Entity, EpisodeState>();

        private static readonly Dictionary<Entity, bool> NightTripStarted =
            new Dictionary<Entity, bool>();

        internal static bool CanStart(
            HospitalizationComponent hospitalization,
            float bladder)
        {
            if (!HospitalPatientLifeConfig.AllowNightBathroomTrips ||
                hospitalization == null ||
                hospitalization.m_entity == null)
            {
                return false;
            }

            Entity patient = hospitalization.m_entity;
            EpisodeState state;
            if (!EpisodeDecisions.TryGetValue(patient, out state))
            {
                state = new EpisodeState();
                EpisodeDecisions[patient] = state;
            }

            if (state.Allowed)
            {
                return true;
            }

            int stage;
            int stageThreshold;
            if (bladder >= ForcedWakeThreshold)
            {
                stage = 3;
                stageThreshold = 100;
            }
            else if (bladder >= SecondChanceThreshold)
            {
                stage = 2;
                stageThreshold = 90;
            }
            else
            {
                stage = 1;
                stageThreshold = HospitalPatientLifeConfig.UrgentBladderThreshold;
            }

            if (stage <= state.HighestStage)
            {
                return false;
            }

            state.HighestStage = stage;

            if (NightTripStarted.ContainsKey(patient) && stage == 1)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | night-bladder-chance=DEFERRED" +
                    " | gameTime=" + HospitalizedPatientTrace.GetGameTime() +
                    " | stageThreshold=" + stageThreshold +
                    " | nextStageThreshold=90" +
                    " | chance=0" +
                    " | roll=-1" +
                    " | bladder=" + bladder.ToString("0.0") +
                    " | floor=" + HospitalizedPatientTrace.GetFloor(patient));
                return false;
            }

            int roll = -1;
            int chance = HospitalPatientLifeConfig.NightBathroomTripChancePercent;
            string action;

            if (stage == 3)
            {
                state.Allowed = true;
                chance = 100;
                action = "FORCED";
            }
            else
            {
                state.Allowed = HospitalPatientLifeConfig.RollNightBathroomTrip(out roll);
                action = state.Allowed ? "PASSED" : "SKIPPED";
            }

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | night-bladder-chance=" + action +
                " | gameTime=" + HospitalizedPatientTrace.GetGameTime() +
                " | stageThreshold=" + stageThreshold +
                " | chance=" + chance +
                " | roll=" + roll +
                " | bladder=" + bladder.ToString("0.0") +
                " | floor=" + HospitalizedPatientTrace.GetFloor(patient));

            return state.Allowed;
        }

        internal static void MarkStarted(Entity patient)
        {
            if (patient != null)
            {
                NightTripStarted[patient] = true;
            }
        }

        internal static void ResetEpisode(Entity patient)
        {
            if (patient != null)
            {
                EpisodeDecisions.Remove(patient);
            }
        }

        internal static void ResetNight(Entity patient)
        {
            if (patient != null)
            {
                EpisodeDecisions.Remove(patient);
                NightTripStarted.Remove(patient);
            }
        }
    }

    [HarmonyPatch(typeof(HospitalizationComponent), "Update")]
    internal static class NightBathroomChanceResetPatch
    {
        private static void Postfix(HospitalizationComponent __instance)
        {
            if (__instance == null || __instance.m_entity == null)
            {
                return;
            }

            Entity patient = __instance.m_entity;
            if (!__instance.IsHospitalized() ||
                (DayTime.Instance != null && DayTime.Instance.IsOpenForStaff()))
            {
                NightBathroomChancePatch.ResetNight(patient);
                return;
            }

            MoodComponent mood = patient.GetComponent<MoodComponent>();
            if (mood == null)
            {
                return;
            }

            Need bladderNeed = mood.GetNeed("NEED_BLADDER");
            if (bladderNeed == null ||
                bladderNeed.m_currentValue < HospitalPatientLifeConfig.UrgentBladderThreshold)
            {
                NightBathroomChancePatch.ResetEpisode(patient);
            }
        }
    }
}
