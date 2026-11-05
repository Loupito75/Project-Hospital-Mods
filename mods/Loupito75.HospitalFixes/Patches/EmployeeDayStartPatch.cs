using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    [HarmonyPatch(typeof(Floor), nameof(Floor.OnDayStart))]
    internal static class EmployeeDayStartPatch
    {
        private static bool Prefix(Floor __instance)
        {
            if (!FixConfig.PreventDuplicateEmployeeDayStart ||
                __instance == null ||
                __instance.m_floorIndex <= 0)
            {
                return true;
            }

            HfxDiagnostics.ReportDuplicateEmployeeDayStartSuppressed(
                __instance);

            // Floor.OnDayStart() only performs global employee day-start work
            // plus a pedestrian check whose only mutation is restricted by
            // Vanilla to floor 0. Higher-floor executions therefore replay
            // the same global employee work without a floor-local effect.
            return false;
        }

        private static void Postfix(Floor __instance)
        {
            if (!FixConfig.PreventDuplicateEmployeeDayStart ||
                __instance == null ||
                __instance.m_floorIndex != 0)
            {
                return;
            }

            HfxDiagnostics.ReportEmployeeDayStartResult(
                __instance);
        }
    }
}
