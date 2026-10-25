using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class DuplicateStatisticsRefreshPatch
    {
        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo survivedCollapse = AccessTools.Method(
                typeof(HospitalizationComponent),
                "CheckSurvivedCollapseCount",
                Type.EmptyTypes);
            MethodInfo sendHome = AccessTools.Method(
                typeof(HospitalizationComponent),
                "SendHome",
                Type.EmptyTypes);
            MethodInfo transpiler = AccessTools.Method(
                typeof(DuplicateStatisticsRefreshPatch),
                "Transpiler");

            if (survivedCollapse == null)
            {
                throw new MissingMethodException(
                    typeof(HospitalizationComponent).FullName,
                    "CheckSurvivedCollapseCount()");
            }
            if (sendHome == null)
            {
                throw new MissingMethodException(
                    typeof(HospitalizationComponent).FullName,
                    "SendHome()");
            }
            if (transpiler == null)
            {
                throw new MissingMethodException(
                    typeof(DuplicateStatisticsRefreshPatch).FullName,
                    "Transpiler");
            }

            HarmonyMethod patch = new HarmonyMethod(transpiler);
            harmony.Patch(survivedCollapse, transpiler: patch);
            harmony.Patch(sendHome, transpiler: patch);

            Plugin.Log.LogInfo(
                "OptimizeDuplicateStatisticsRefresh installed: duplicate hospitalization statistics refreshes are removed before the native InsuranceManager immediate refresh.");
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            MethodInfo hospitalGetter = AccessTools.PropertyGetter(
                typeof(Hospital),
                "Instance");
            MethodInfo updateStatistics = AccessTools.Method(
                typeof(Hospital),
                "UpdateCurrentStatistics",
                Type.EmptyTypes);

            if (hospitalGetter == null)
            {
                throw new MissingMethodException(
                    typeof(Hospital).FullName,
                    "get_Instance()");
            }
            if (updateStatistics == null)
            {
                throw new MissingMethodException(
                    typeof(Hospital).FullName,
                    "UpdateCurrentStatistics()");
            }

            int matches = 0;

            for (int i = 1; i < codes.Count; i++)
            {
                if (!codes[i].Calls(updateStatistics))
                {
                    continue;
                }

                if (!codes[i - 1].Calls(hospitalGetter))
                {
                    throw new InvalidOperationException(
                        "Unexpected Hospital.UpdateCurrentStatistics() IL pattern in " +
                        (__originalMethod == null ? "unknown method" : __originalMethod.Name) +
                        ".");
                }

                // Preserve labels and exception metadata on both original
                // instructions while removing their stack effects.
                codes[i - 1].opcode = OpCodes.Nop;
                codes[i - 1].operand = null;
                codes[i].opcode = OpCodes.Nop;
                codes[i].operand = null;
                matches++;
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one duplicate Hospital.UpdateCurrentStatistics() call in " +
                    (__originalMethod == null ? "target method" : __originalMethod.Name) +
                    ", found " + matches + ".");
            }

            return codes;
        }
    }
}
