using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalTrafficControl.Patches
{
    internal static class CandidateNoPathEvaluationContext
    {
        [ThreadStatic]
        private static int s_depth;

        internal static bool IsActive
        {
            get { return s_depth > 0; }
        }

        internal static void Begin()
        {
            s_depth++;
        }

        internal static void End()
        {
            if (s_depth > 0)
            {
                s_depth--;
            }
        }

        internal static void Reset()
        {
            s_depth = 0;
        }
    }

    // This overload is a candidate search: it asks GridMap.GetDistance for
    // each eligible object and can legitimately receive -1 for an unreachable
    // candidate. Keep the returned distance and selection logic untouched;
    // only mark the scope so GridMap's generic error message can stay silent.
    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindClosestFreeObjectWithTags),
        new Type[]
        {
            typeof(Entity),
            typeof(Entity),
            typeof(Vector2i),
            typeof(Room),
            typeof(string[]),
            typeof(AccessRights),
            typeof(bool),
            typeof(DatabaseEntryRef<GameDBRoomType>[]),
            typeof(bool)
        })]
    internal static class CandidateNoPathEvaluationContextPatch
    {
        private static void Prefix()
        {
            CandidateNoPathEvaluationContext.Begin();
        }

        private static Exception Finalizer(Exception __exception)
        {
            CandidateNoPathEvaluationContext.End();
            return __exception;
        }
    }

    // IMPORTANT: do not add a HarmonyPatch attribute here. GridMap's static
    // constructor reads Database.Instance and must not be forced during
    // BepInEx PatchAll(). NavigationGraphPatch installs this transpiler only
    // after Vanilla has initialized GridMap successfully.
    internal static class CandidateNoPathLogGuardPatch
    {
        private static readonly MethodInfo DebugLogErrorMethod =
            AccessTools.Method(
                typeof(Debug),
                nameof(Debug.LogError),
                new Type[] { typeof(object) });

        private static readonly MethodInfo FilteredLogErrorMethod =
            AccessTools.Method(
                typeof(CandidateNoPathLogGuardPatch),
                nameof(LogErrorUnlessCandidateEvaluation),
                new Type[] { typeof(object) });

        internal static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            if (DebugLogErrorMethod == null ||
                FilteredLogErrorMethod == null)
            {
                Plugin.Log?.LogError(
                    "Candidate NoPath log guard could not resolve its log methods; " +
                    "leaving Vanilla logging unchanged.");
                return codes;
            }

            int matchIndex = -1;
            int matchCount = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                if ((instruction.opcode == OpCodes.Call ||
                     instruction.opcode == OpCodes.Callvirt) &&
                    object.Equals(
                        instruction.operand,
                        DebugLogErrorMethod))
                {
                    matchIndex = i;
                    matchCount++;
                }
            }

            // reference/GridMap.GetWaypoints contains exactly one LogError:
            // "No path found between ... Blocked elevator on ground floor?".
            // If a future game version changes that shape, fail open and keep
            // every Vanilla log instead of suppressing an unknown call.
            if (matchCount != 1)
            {
                Plugin.Log?.LogError(
                    "Candidate NoPath log guard expected one " +
                    "Debug.LogError call in GridMap.GetWaypoints but found " +
                    matchCount + "; leaving Vanilla logging unchanged.");
                return codes;
            }

            CodeInstruction replacement =
                new CodeInstruction(codes[matchIndex]);
            replacement.opcode = OpCodes.Call;
            replacement.operand = FilteredLogErrorMethod;
            codes[matchIndex] = replacement;

            return codes;
        }

        private static void LogErrorUnlessCandidateEvaluation(
            object message)
        {
            if (CandidateNoPathEvaluationContext.IsActive)
            {
                return;
            }

            Debug.LogError(message);
        }
    }
}
