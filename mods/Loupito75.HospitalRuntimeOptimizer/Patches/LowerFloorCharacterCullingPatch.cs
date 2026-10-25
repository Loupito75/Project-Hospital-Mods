using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HospitalRuntimeOptimizer
{
    internal static class LowerFloorCharacterCullingPatch
    {
        // Main-thread only. Entries mean that Vanilla already received one
        // RenderCharacters pass after this renderer entered BELOW.
        private static readonly HashSet<CharacterRenderer> StableBelowRenderers =
            new HashSet<CharacterRenderer>();

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo renderCharacters = AccessTools.Method(
                typeof(CharacterRenderer),
                "RenderCharacters",
                new Type[]
                {
                    typeof(FloorRenderData),
                    typeof(Lopital.Floor),
                    typeof(LightMap2DUnity)
                });
            MethodInfo renderMap = AccessTools.Method(
                typeof(MapRenderer),
                "RenderMap",
                Type.EmptyTypes);
            MethodInfo destroy = AccessTools.Method(
                typeof(CharacterRenderer),
                "Destroy",
                Type.EmptyTypes);

            if (renderCharacters == null)
            {
                throw new MissingMethodException(
                    typeof(CharacterRenderer).FullName,
                    "RenderCharacters(FloorRenderData, Floor, LightMap2DUnity)");
            }
            if (renderMap == null)
            {
                throw new MissingMethodException(
                    typeof(MapRenderer).FullName,
                    "RenderMap()");
            }
            if (destroy == null)
            {
                throw new MissingMethodException(
                    typeof(CharacterRenderer).FullName,
                    "Destroy()");
            }

            harmony.Patch(
                renderCharacters,
                prefix: new HarmonyMethod(
                    typeof(LowerFloorCharacterCullingPatch),
                    "RenderCharactersPrefix"));

            harmony.Patch(
                renderMap,
                postfix: new HarmonyMethod(
                    typeof(LowerFloorCharacterCullingPatch),
                    "RenderMapPostfix"));

            harmony.Patch(
                destroy,
                prefix: new HarmonyMethod(
                    typeof(LowerFloorCharacterCullingPatch),
                    "CharacterRendererDestroyPrefix"));

            Plugin.Log.LogInfo(
                "HideCharactersOnLowerFloors installed: BELOW floors keep one Vanilla cleanup pass, then repeated character rendering is skipped.");
        }

        internal static void Reset()
        {
            StableBelowRenderers.Clear();
        }

        private static bool RenderCharactersPrefix(
            CharacterRenderer __instance,
            FloorRenderData floorRenderData)
        {
            if (__instance == null || floorRenderData == null)
            {
                return true;
            }

            if (floorRenderData.m_floorRenderMode != FloorRenderMode.BELOW)
            {
                StableBelowRenderers.Remove(__instance);
                return true;
            }

            // Keep one Vanilla BELOW pass for ClearAbandonedCharacters,
            // lab-procedure bubble cleanup and normal renderer state changes.
            if (StableBelowRenderers.Add(__instance))
            {
                return true;
            }

            return false;
        }

        private static void RenderMapPostfix(
            MapRenderer __instance,
            GameObject ___m_sceneCharactersFolder)
        {
            if (__instance == null ||
                __instance.m_floorRenderData == null ||
                ___m_sceneCharactersFolder == null)
            {
                return;
            }

            if (__instance.m_floorRenderData.m_floorRenderMode ==
                FloorRenderMode.BELOW)
            {
                // Vanilla re-applies this folder's active state near the end of
                // RenderMap, so enforce the optional BELOW cull afterwards.
                ___m_sceneCharactersFolder.SetActive(false);
            }
        }

        private static void CharacterRendererDestroyPrefix(
            CharacterRenderer __instance)
        {
            if (__instance != null)
            {
                StableBelowRenderers.Remove(__instance);
            }
        }
    }
}
