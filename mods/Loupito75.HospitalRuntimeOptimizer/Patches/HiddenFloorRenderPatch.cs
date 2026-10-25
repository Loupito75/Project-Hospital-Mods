using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace HospitalRuntimeOptimizer
{
    internal static class HiddenFloorRenderPatch
    {
        // Main-thread only. Entries mean that Vanilla has already received one
        // RenderMap pass after this renderer entered ABOVE_HIDDEN.
        private static readonly HashSet<MapRenderer> StableHiddenRenderers =
            new HashSet<MapRenderer>();

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo renderMap = AccessTools.Method(
                typeof(MapRenderer),
                "RenderMap",
                Type.EmptyTypes);
            MethodInfo createFloor = AccessTools.Method(
                typeof(MapRenderer),
                "CreateFloor",
                Type.EmptyTypes);
            MethodInfo destroy = AccessTools.Method(
                typeof(MapRenderer),
                "Destroy",
                Type.EmptyTypes);

            if (renderMap == null)
            {
                throw new MissingMethodException(typeof(MapRenderer).FullName, "RenderMap()");
            }
            if (createFloor == null)
            {
                throw new MissingMethodException(typeof(MapRenderer).FullName, "CreateFloor()");
            }
            if (destroy == null)
            {
                throw new MissingMethodException(typeof(MapRenderer).FullName, "Destroy()");
            }

            harmony.Patch(
                renderMap,
                prefix: new HarmonyMethod(
                    typeof(HiddenFloorRenderPatch),
                    "RenderMapPrefix"));

            harmony.Patch(
                createFloor,
                postfix: new HarmonyMethod(
                    typeof(HiddenFloorRenderPatch),
                    "InvalidatePostfix"));

            harmony.Patch(
                destroy,
                prefix: new HarmonyMethod(
                    typeof(HiddenFloorRenderPatch),
                    "DestroyPrefix"));

            Plugin.Log.LogInfo(
                "OptimizeHiddenFloors installed: stable ABOVE_HIDDEN RenderMap calls will be skipped after one Vanilla hide pass.");
        }

        internal static void Reset()
        {
            StableHiddenRenderers.Clear();
        }

        private static bool RenderMapPrefix(MapRenderer __instance)
        {
            if (__instance == null || __instance.m_floorRenderData == null)
            {
                return true;
            }

            if (__instance.m_floorRenderData.m_floorRenderMode !=
                FloorRenderMode.ABOVE_HIDDEN)
            {
                StableHiddenRenderers.Remove(__instance);
                return true;
            }

            // HashSet.Add returns false when the renderer was already stable,
            // so the hidden hot path needs only one lookup.
            if (!StableHiddenRenderers.Add(__instance))
            {
                return false;
            }

            // The first ABOVE_HIDDEN call still enters Vanilla. A later
            // transition back to BELOW/NORMAL removes the marker before
            // Vanilla runs again.
            return true;
        }

        private static void InvalidatePostfix(MapRenderer __instance)
        {
            if (__instance != null)
            {
                StableHiddenRenderers.Remove(__instance);
            }
        }

        private static void DestroyPrefix(MapRenderer __instance)
        {
            if (__instance != null)
            {
                StableHiddenRenderers.Remove(__instance);
            }
        }
    }
}
