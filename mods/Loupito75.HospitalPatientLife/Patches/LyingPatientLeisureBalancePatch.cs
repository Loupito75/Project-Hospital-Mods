using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using GLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    [HarmonyPatch]
    internal static class LyingPatientLeisureBalancePatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(ProcedureScriptControlHopitalizedLyingFreeTime),
                "ChooseSomething",
                Type.EmptyTypes);

            if (method == null)
            {
                throw new MissingMethodException(
                    "ProcedureScriptControlHopitalizedLyingFreeTime.ChooseSomething() was not found.");
            }

            return method;
        }

        private static int RollNativeBranch(
            ProcedureScriptControlHopitalizedLyingFreeTime script)
        {
            PatientLeisureConfig.EnsureLoaded();

            // Keep the game's existing ~1% PLAYING branch untouched. It currently
            // has no dedicated lying-phone animation, so HPL does not increase it.
            int phoneRoll = UnityEngine.Random.Range(0, 100);
            if (phoneRoll < 1)
            {
                LogDecision(script, "PLAYING", phoneRoll, -1, -1);
                return 0;
            }

            int restWeight = PatientLeisureConfig.LyingRestActivityWeight;
            int nurseBookWeight =
                PatientLeisureConfig.LyingNurseBookActivityWeight;
            int totalWeight = restWeight + nurseBookWeight;

            if (totalWeight <= 0)
            {
                LogDecision(script, "NURSE_BOOK_VANILLA", phoneRoll, -1, 0);
                return 1;
            }

            int roll = UnityEngine.Random.Range(0, totalWeight);
            if (roll < restWeight)
            {
                LogDecision(script, "REST", phoneRoll, roll, totalWeight);
                return 100;
            }

            LogDecision(script, "NURSE_BOOK", phoneRoll, roll, totalWeight);
            return 1;
        }

        private static void LogDecision(
            ProcedureScriptControlHopitalizedLyingFreeTime script,
            string activity,
            int phoneRoll,
            int activityRoll,
            int totalWeight)
        {
            if (!HospitalizedPatientTrace.Enabled ||
                script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_procedureScene == null)
            {
                return;
            }

            Entity patient = script.m_stateData.m_procedureScene.MainCharacter;
            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | lying-leisure=SELECTED" +
                " | activity=" + activity +
                " | weights=" +
                PatientLeisureConfig.LyingRestActivityWeight + "/" +
                PatientLeisureConfig.LyingNurseBookActivityWeight + "/" +
                PatientLeisureConfig.LyingTelevisionActivityWeight +
                " | phoneRoll=" + phoneRoll +
                " | activityRoll=" + activityRoll + "/" + totalWeight);
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            MethodInfo randomRange = AccessTools.Method(
                typeof(UnityEngine.Random),
                "Range",
                new Type[]
                {
                    typeof(int),
                    typeof(int)
                });
            MethodInfo helper = AccessTools.Method(
                typeof(LyingPatientLeisureBalancePatch),
                "RollNativeBranch",
                new Type[]
                {
                    typeof(ProcedureScriptControlHopitalizedLyingFreeTime)
                });

            if (randomRange == null || helper == null)
            {
                LogPatchWarning(
                    "Lying-patient leisure balance could not resolve Random.Range(int,int) or its helper; vanilla selection is kept.");
                return codes;
            }

            List<int> matches = new List<int>();
            for (int i = 2; i < codes.Count; i++)
            {
                MethodInfo called = codes[i].operand as MethodInfo;
                if (called == randomRange &&
                    LoadsInt(codes[i - 2], 0) &&
                    LoadsInt(codes[i - 1], 100))
                {
                    matches.Add(i);
                }
            }

            if (matches.Count != 1)
            {
                LogPatchWarning(
                    "Lying-patient leisure balance expected one Random.Range(0,100) decision but found " +
                    matches.Count + "; vanilla selection is kept.");
                return codes;
            }

            int callIndex = matches[0];

            // Reuse the existing instructions so labels/exception blocks remain
            // attached to the same locations in the native method.
            codes[callIndex - 2].opcode = OpCodes.Ldarg_0;
            codes[callIndex - 2].operand = null;
            codes[callIndex - 1].opcode = OpCodes.Nop;
            codes[callIndex - 1].operand = null;
            codes[callIndex].opcode = OpCodes.Call;
            codes[callIndex].operand = helper;

            return codes;
        }

        private static bool LoadsInt(CodeInstruction instruction, int value)
        {
            if (instruction == null)
            {
                return false;
            }

            if (value == 0 && instruction.opcode == OpCodes.Ldc_I4_0)
            {
                return true;
            }
            if (value == 1 && instruction.opcode == OpCodes.Ldc_I4_1)
            {
                return true;
            }
            if (value == 2 && instruction.opcode == OpCodes.Ldc_I4_2)
            {
                return true;
            }
            if (value == 3 && instruction.opcode == OpCodes.Ldc_I4_3)
            {
                return true;
            }
            if (value == 4 && instruction.opcode == OpCodes.Ldc_I4_4)
            {
                return true;
            }
            if (value == 5 && instruction.opcode == OpCodes.Ldc_I4_5)
            {
                return true;
            }
            if (value == 6 && instruction.opcode == OpCodes.Ldc_I4_6)
            {
                return true;
            }
            if (value == 7 && instruction.opcode == OpCodes.Ldc_I4_7)
            {
                return true;
            }
            if (value == 8 && instruction.opcode == OpCodes.Ldc_I4_8)
            {
                return true;
            }
            if (value == -1 && instruction.opcode == OpCodes.Ldc_I4_M1)
            {
                return true;
            }
            if (instruction.opcode == OpCodes.Ldc_I4)
            {
                return instruction.operand is int &&
                    (int)instruction.operand == value;
            }
            if (instruction.opcode == OpCodes.Ldc_I4_S)
            {
                if (instruction.operand is sbyte)
                {
                    return (sbyte)instruction.operand == value;
                }
                if (instruction.operand is byte)
                {
                    return (byte)instruction.operand == value;
                }
            }

            return false;
        }

        private static void LogPatchWarning(string message)
        {
            if (Plugin.Log != null)
            {
                Plugin.Log.LogWarning(message);
            }
        }
    }
}
