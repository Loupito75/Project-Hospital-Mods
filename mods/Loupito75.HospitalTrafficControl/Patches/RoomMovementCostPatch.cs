using System;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(Floor), nameof(Floor.GetMovementCost), new Type[] { typeof(Vector2i), typeof(int), typeof(bool) })]
    internal static class RoomMovementCostByAccessPatch
    {
        private static void Postfix(Floor __instance, Vector2i position, ref float __result)
        {
            RoomTransitManager.ApplyPenalty(__instance, position, ref __result);
        }
    }

    [HarmonyPatch(typeof(Floor), nameof(Floor.GetMovementCost), new Type[] { typeof(Vector2i), typeof(Direction), typeof(bool) })]
    internal static class RoomMovementCostByDirectionPatch
    {
        private static void Postfix(Floor __instance, Vector2i position, ref float __result)
        {
            RoomTransitManager.ApplyPenalty(__instance, position, ref __result);
        }
    }
}
