using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HospitalRuntimeOptimizer
{
    internal static class PlayerStatisticsSaveWritePatch
    {
        private static AccessTools.FieldRef<PlayerStatistics, Dictionary<string, int>> s_statisticsRef;
        private static bool s_fastPathReady;

        [ThreadStatic]
        private static string s_currentStatisticId;

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo increment = AccessTools.Method(
                typeof(PlayerStatistics),
                "IncrementStatistic",
                new Type[]
                {
                    typeof(string),
                    typeof(int)
                });
            MethodInfo save = AccessTools.Method(
                typeof(PlayerStatistics),
                "Save",
                Type.EmptyTypes);
            MethodInfo load = AccessTools.Method(
                typeof(PlayerStatistics),
                "Load",
                Type.EmptyTypes);
            MethodInfo debugReset = AccessTools.Method(
                typeof(PlayerStatistics),
                "DEBUG_Reset",
                Type.EmptyTypes);

            MethodInfo incrementPrefix = AccessTools.Method(
                typeof(PlayerStatisticsSaveWritePatch),
                "IncrementPrefix");
            MethodInfo incrementFinalizer = AccessTools.Method(
                typeof(PlayerStatisticsSaveWritePatch),
                "IncrementFinalizer");
            MethodInfo savePrefix = AccessTools.Method(
                typeof(PlayerStatisticsSaveWritePatch),
                "SavePrefix");
            MethodInfo savePostfix = AccessTools.Method(
                typeof(PlayerStatisticsSaveWritePatch),
                "SavePostfix");
            MethodInfo resetPostfix = AccessTools.Method(
                typeof(PlayerStatisticsSaveWritePatch),
                "ResetFastPathPostfix");

            if (increment == null)
            {
                throw new MissingMethodException(
                    typeof(PlayerStatistics).FullName,
                    "IncrementStatistic(String, Int32)");
            }
            if (save == null)
            {
                throw new MissingMethodException(
                    typeof(PlayerStatistics).FullName,
                    "Save()");
            }
            if (load == null)
            {
                throw new MissingMethodException(
                    typeof(PlayerStatistics).FullName,
                    "Load()");
            }
            if (debugReset == null)
            {
                throw new MissingMethodException(
                    typeof(PlayerStatistics).FullName,
                    "DEBUG_Reset()");
            }
            if (incrementPrefix == null ||
                incrementFinalizer == null ||
                savePrefix == null ||
                savePostfix == null ||
                resetPostfix == null)
            {
                throw new MissingMethodException(
                    typeof(PlayerStatisticsSaveWritePatch).FullName,
                    "HRO PlayerStatistics patch method");
            }

            s_statisticsRef =
                AccessTools.FieldRefAccess<PlayerStatistics, Dictionary<string, int>>(
                    "m_statistics");
            s_fastPathReady = false;
            s_currentStatisticId = null;

            harmony.Patch(
                increment,
                prefix: new HarmonyMethod(incrementPrefix),
                finalizer: new HarmonyMethod(incrementFinalizer));
            HarmonyMethod savePrefixPatch = new HarmonyMethod(savePrefix);
            savePrefixPatch.priority = Priority.Last;

            harmony.Patch(
                save,
                prefix: savePrefixPatch,
                postfix: new HarmonyMethod(savePostfix));
            harmony.Patch(
                load,
                postfix: new HarmonyMethod(resetPostfix));
            harmony.Patch(
                debugReset,
                postfix: new HarmonyMethod(resetPostfix));

            Plugin.Log.LogInfo(
                "OptimizePlayerStatisticsSaveWrites installed: PlayerStatistics.Save remains the native call point; after the first full save following load/reset, unchanged statistic keys are not rewritten.");
        }

        internal static void Reset()
        {
            s_fastPathReady = false;
            s_currentStatisticId = null;
            s_statisticsRef = null;
        }

        private static void ResetFastPathPostfix()
        {
            s_fastPathReady = false;
        }

        private static void IncrementPrefix(
            string statisticID,
            out string __state)
        {
            __state = s_currentStatisticId;
            s_currentStatisticId = statisticID;
        }

        private static Exception IncrementFinalizer(
            Exception __exception,
            string __state)
        {
            s_currentStatisticId = __state;
            return __exception;
        }

        private static bool SavePrefix(PlayerStatistics __instance)
        {
            if (__instance == null ||
                string.IsNullOrEmpty(s_currentStatisticId) ||
                s_statisticsRef == null)
            {
                return true;
            }

            // The first increment after Load/DEBUG_Reset deliberately runs the
            // complete Vanilla Save() body. This preserves creation/update of
            // every statistic PlayerPrefs key before enabling the targeted path.
            if (!s_fastPathReady)
            {
                return true;
            }

            Dictionary<string, int> statistics = s_statisticsRef(__instance);
            int value;
            if (statistics == null ||
                !statistics.TryGetValue(s_currentStatisticId, out value))
            {
                return true;
            }

            // Preserve Vanilla durability: only the redundant SetInt calls are
            // removed. The changed key is written and PlayerPrefs is flushed at
            // the exact Save() call point used by IncrementStatistic.
            PlayerPrefs.SetInt(s_currentStatisticId, value);
            PlayerPrefs.Save();
            return false;
        }

        private static void SavePostfix(bool __runOriginal)
        {
            if (__runOriginal &&
                !s_fastPathReady &&
                !string.IsNullOrEmpty(s_currentStatisticId))
            {
                s_fastPathReady = true;
            }
        }
    }
}
