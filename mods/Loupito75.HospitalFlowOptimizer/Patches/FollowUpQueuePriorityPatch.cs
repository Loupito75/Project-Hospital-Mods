using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFlowOptimizer.Patches
{
    [HarmonyPatch(typeof(Room), nameof(Room.IsCharactersTurn))]
    internal static class FollowUpQueuePriorityPatch
    {
        private static void Postfix(
            Room __instance,
            Entity character,
            ref bool __result)
        {
            if (!FlowOptimizerConfig.FollowUpQueuePriority)
            {
                return;
            }

            if (__result)
            {
                FollowUpQueuePriority.LogVanillaTurn(
                    __instance,
                    character);
                return;
            }

            if (FollowUpQueuePriority.CanAdvance(__instance, character))
            {
                __result = true;
            }
        }
    }
}
