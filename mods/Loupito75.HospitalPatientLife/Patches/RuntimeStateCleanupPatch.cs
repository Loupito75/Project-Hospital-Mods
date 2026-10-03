using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class HospitalPatientLifeRuntimeState
    {
        internal static void Reset()
        {
            CafeteriaAccessChangeTracker.ResetRuntimeState();
            ClinicCafeteriaState.ResetRuntimeState();
            HospitalizedNeedsPriorityRules.ResetRuntimeState();
            HospitalizedPatientTrace.ResetRuntimeState();
            HospitalizedTelevisionAccess.ResetRuntimeState();
            NightBathroomChancePatch.ResetRuntimeState();
            ScheduledCafeteriaMealState.ResetRuntimeState();
        }
    }

    [HarmonyPatch(typeof(Hospital), "Destroy")]
    internal static class HospitalPatientLifeHospitalDestroyPatch
    {
        private static void Prefix()
        {
            HospitalPatientLifeRuntimeState.Reset();
        }
    }

    [HarmonyPatch(typeof(Hospital), "Reset")]
    internal static class HospitalPatientLifeHospitalResetPatch
    {
        private static void Prefix()
        {
            HospitalPatientLifeRuntimeState.Reset();
        }
    }
}
