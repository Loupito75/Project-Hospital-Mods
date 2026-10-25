using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class CharacterFloorListReusePatch
    {
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
                    typeof(Floor),
                    typeof(LightMap2DUnity)
                });
            MethodInfo transpiler = AccessTools.Method(
                typeof(CharacterFloorListReusePatch),
                "Transpiler");

            if (renderCharacters == null)
            {
                throw new MissingMethodException(
                    typeof(CharacterRenderer).FullName,
                    "RenderCharacters(FloorRenderData, Floor, LightMap2DUnity)");
            }
            if (transpiler == null)
            {
                throw new MissingMethodException(
                    typeof(CharacterFloorListReusePatch).FullName,
                    "Transpiler");
            }

            harmony.Patch(
                renderCharacters,
                transpiler: new HarmonyMethod(transpiler));

            Plugin.Log.LogInfo(
                "OptimizeCharactersByFloor installed: RenderCharacters reuses Floor.m_characters after Vanilla cleanup.");
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getCharacters = AccessTools.Method(
                typeof(Floor),
                "GetCharacters",
                Type.EmptyTypes);
            FieldInfo characters = AccessTools.Field(
                typeof(Floor),
                "m_characters");

            if (getCharacters == null)
            {
                throw new MissingMethodException(
                    typeof(Floor).FullName,
                    "GetCharacters()");
            }
            if (characters == null)
            {
                throw new MissingFieldException(
                    typeof(Floor).FullName,
                    "m_characters");
            }

            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);
            int replacements = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                if (!instruction.Calls(getCharacters))
                {
                    continue;
                }

                // Floor is already on the evaluation stack. Both the original
                // call and this field load consume that same receiver and push
                // List<Entity>, so the surrounding foreach IL is unchanged.
                instruction.opcode = OpCodes.Ldfld;
                instruction.operand = characters;
                replacements++;
            }

            if (replacements != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one Floor.GetCharacters() call in " +
                    "CharacterRenderer.RenderCharacters(), found " +
                    replacements + ".");
            }

            return codes;
        }
    }
}
