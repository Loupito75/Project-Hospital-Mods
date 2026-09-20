using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(WalkComponent), nameof(WalkComponent.IsBusy))]
    internal static class BlockedBusyPatch
    {
        private static void Postfix(WalkComponent __instance, ref bool __result)
        {
            if (__instance.m_state.m_walkState == WalkState.NoPath &&
                (BlockedRouteManager.IsBlocked(__instance) ||
                 CrossFloorBlockedManager.IsBlocked(__instance)))
            {
                // Keep the native behavior/procedure waiting until the blocked route
                // is genuinely available again instead of treating NoPath as arrival.
                __result = true;
            }
        }
    }
}
