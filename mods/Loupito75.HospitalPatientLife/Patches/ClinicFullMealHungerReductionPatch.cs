using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    [HarmonyPatch(typeof(BehaviorPatient), "ReceiveMessage")]
    internal static class ClinicFullMealHungerReductionPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(BehaviorPatient __instance, Message message)
        {
            if (__instance == null || message.m_messageID != Messages.HUNGER_REDUCED)
            {
                return true;
            }

            Entity patient = __instance.m_entity;
            if (!ClinicCafeteriaState.IsClinicFullMealInProgress(patient))
            {
                return true;
            }

            MoodComponent mood = __instance.GetComponent<MoodComponent>();
            Need hunger = mood == null
                ? null
                : mood.GetNeed("NEED_HUNGER_PATIENT");
            if (mood == null || hunger == null)
            {
                return true;
            }

            int minimum = HospitalPatientLifeConfig.ClinicFullMealHungerReductionMin;
            int maximum = HospitalPatientLifeConfig.ClinicFullMealHungerReductionMax;
            float reduction = HospitalPatientLifeConfig.RollMealHungerReduction(
                minimum,
                maximum);

            hunger.Reduce(reduction, mood);

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | clinic-full-meal-hunger-reduction=" + reduction +
                    " | configured=" + minimum + "-" + maximum +
                    " | hunger=" +
                    HospitalizedPatientTrace.GetNeedValue(
                        patient,
                        "NEED_HUNGER_PATIENT"));
            }

            return false;
        }
    }

    [HarmonyPatch]
    internal static class ClinicCafeteriaInstantiationTargetPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(ProcedureSceneFactory),
                "CreateProcedureScene",
                new Type[]
                {
                    typeof(GameDBProcedure),
                    typeof(Entity),
                    typeof(Department),
                    typeof(Room),
                    typeof(AccessRights),
                    typeof(ProcedureSceneType),
                    typeof(EquipmentListRules),
                    typeof(StaffSelectionRules)
                });
        }

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            ref GameDBProcedure procedure,
            Entity patient,
            ref AccessRights accessRights,
            ProcedureSceneType procedureSceneType,
            ref EquipmentListRules equipmentListRules)
        {
            if (procedureSceneType != ProcedureSceneType.INSTANTIATION ||
                !ClinicCafeteriaState.IsClinicPatient(patient))
            {
                return;
            }

            ClinicCafeteriaDecision decision =
                ClinicCafeteriaState.GetDecision(patient);
            if (decision == null)
            {
                return;
            }

            ClinicCafeteriaState.ApplyDecisionToProcedure(
                patient,
                decision,
                ref procedure,
                ref accessRights,
                ref equipmentListRules);
        }
    }
}
