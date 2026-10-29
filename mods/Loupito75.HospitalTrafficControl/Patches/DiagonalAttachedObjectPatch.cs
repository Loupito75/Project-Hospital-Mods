using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(TileObject), nameof(TileObject.SetAttachedToCharacter))]
    internal static class DiagonalAttachedStatePatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix(TileObject __instance)
        {
            AttachedMovingObjectTracker.Refresh(__instance);
        }
    }

    [HarmonyPatch(typeof(TileObject), "set_User")]
    internal static class DiagonalAttachedUserPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix(TileObject __instance)
        {
            AttachedMovingObjectTracker.Refresh(__instance);
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.MoveObject),
        new System.Type[] { typeof(TileObject), typeof(GLib.Vector2i) })]
    internal static class DiagonalAttachedMoveObjectPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix(TileObject tileObject)
        {
            AttachedMovingObjectTracker.Refresh(tileObject);
        }
    }

    [HarmonyPatch(typeof(TileObject), nameof(TileObject.Destroy))]
    internal static class DiagonalAttachedDestroyPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Prefix(TileObject __instance)
        {
            AttachedMovingObjectTracker.Forget(__instance);
        }
    }

    [HarmonyPatch(typeof(GameSave), nameof(GameSave.Load))]
    internal static class DiagonalAttachedLoadPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix()
        {
            AttachedMovingObjectTracker.Invalidate();
        }
    }
}
