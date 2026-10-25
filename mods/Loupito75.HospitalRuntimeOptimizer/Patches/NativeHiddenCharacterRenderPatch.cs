using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class NativeHiddenCharacterRenderPatch
    {
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
            MethodInfo transpiler = AccessTools.Method(
                typeof(NativeHiddenCharacterRenderPatch),
                "Transpiler");

            if (renderMap == null)
            {
                throw new MissingMethodException(
                    typeof(MapRenderer).FullName,
                    "RenderMap()");
            }
            if (transpiler == null)
            {
                throw new MissingMethodException(
                    typeof(NativeHiddenCharacterRenderPatch).FullName,
                    "Transpiler");
            }

            harmony.Patch(
                renderMap,
                transpiler: new HarmonyMethod(transpiler));

            Plugin.Log.LogInfo(
                "OptimizeNativeHiddenCharacters installed: character rendering is skipped when Vanilla m_hideCharacters is active.");
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo renderCharacters = AccessTools.Method(
                typeof(CharacterRenderer),
                "RenderCharacters",
                new Type[]
                {
                    typeof(FloorRenderData),
                    typeof(Floor),
                    typeof(LightMap2DUnity)
                });
            MethodInfo replacement = AccessTools.Method(
                typeof(NativeHiddenCharacterRenderPatch),
                "RenderCharactersIfVisible");

            if (renderCharacters == null)
            {
                throw new MissingMethodException(
                    typeof(CharacterRenderer).FullName,
                    "RenderCharacters(FloorRenderData, Floor, LightMap2DUnity)");
            }
            if (replacement == null)
            {
                throw new MissingMethodException(
                    typeof(NativeHiddenCharacterRenderPatch).FullName,
                    "RenderCharactersIfVisible");
            }

            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);
            int replacements = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                if (!instruction.Calls(renderCharacters))
                {
                    continue;
                }

                // The original stack already contains:
                // CharacterRenderer, FloorRenderData, Floor, LightMap2DUnity.
                // The static helper consumes the exact same arguments.
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replacements++;
            }

            if (replacements != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one CharacterRenderer.RenderCharacters() " +
                    "call in MapRenderer.RenderMap(), found " +
                    replacements + ".");
            }

            return codes;
        }

        private static void RenderCharactersIfVisible(
            CharacterRenderer renderer,
            FloorRenderData floorRenderData,
            Floor floor,
            LightMap2DUnity lightMap)
        {
            if (SettingsManager.Instance.m_viewSettings.m_hideCharacters.m_value)
            {
                return;
            }

            renderer.RenderCharacters(
                floorRenderData,
                floor,
                lightMap);
        }
    }
}
