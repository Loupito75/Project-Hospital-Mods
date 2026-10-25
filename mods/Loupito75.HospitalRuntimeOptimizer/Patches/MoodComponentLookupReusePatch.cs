using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class MoodComponentLookupReusePatch
    {
        private const int ExpectedPerkLookups = 4;
        private const int ExpectedHospitalizationLookups = 3;

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo update = AccessTools.Method(
                typeof(MoodComponent),
                "Update",
                new Type[] { typeof(float) });
            MethodInfo transpiler = AccessTools.Method(
                typeof(MoodComponentLookupReusePatch),
                "Transpiler");

            if (update == null)
            {
                throw new MissingMethodException(
                    typeof(MoodComponent).FullName,
                    "Update(Single)");
            }
            if (transpiler == null)
            {
                throw new MissingMethodException(
                    typeof(MoodComponentLookupReusePatch).FullName,
                    "Transpiler");
            }

            harmony.Patch(
                update,
                transpiler: new HarmonyMethod(transpiler));

            Plugin.Log.LogInfo(
                "OptimizeMoodComponentLookups installed: repeated PerkComponent and HospitalizationComponent searches are reused within MoodComponent.Update.");
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            if (generator == null)
            {
                throw new ArgumentNullException("generator");
            }

            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            FieldInfo entityField = AccessTools.Field(
                typeof(GLib.Component),
                "m_entity");
            MethodInfo perkGetter = GetComponentGetter(
                typeof(PerkComponent));
            MethodInfo hospitalizationGetter = GetComponentGetter(
                typeof(HospitalizationComponent));

            if (entityField == null)
            {
                throw new MissingFieldException(
                    typeof(GLib.Component).FullName,
                    "m_entity");
            }
            if (perkGetter == null || hospitalizationGetter == null)
            {
                throw new MissingMethodException(
                    typeof(Entity).FullName,
                    "GetComponent<T>()");
            }

            List<int> perkCalls = FindAndValidateCalls(
                codes,
                perkGetter,
                entityField,
                "PerkComponent");
            List<int> hospitalizationCalls = FindAndValidateCalls(
                codes,
                hospitalizationGetter,
                entityField,
                "HospitalizationComponent");

            if (perkCalls.Count != ExpectedPerkLookups)
            {
                throw new InvalidOperationException(
                    "Expected " + ExpectedPerkLookups +
                    " PerkComponent lookups in MoodComponent.Update(), found " +
                    perkCalls.Count + ".");
            }
            if (hospitalizationCalls.Count !=
                ExpectedHospitalizationLookups)
            {
                throw new InvalidOperationException(
                    "Expected " + ExpectedHospitalizationLookups +
                    " HospitalizationComponent lookups in MoodComponent.Update(), found " +
                    hospitalizationCalls.Count + ".");
            }
            if (perkCalls[0] >= hospitalizationCalls[0])
            {
                throw new InvalidOperationException(
                    "Unexpected MoodComponent.Update() lookup order.");
            }

            LocalBuilder perkLocal =
                generator.DeclareLocal(typeof(PerkComponent));
            LocalBuilder hospitalizationLocal =
                generator.DeclareLocal(typeof(HospitalizationComponent));

            ReplaceCallsWithLocal(codes, perkCalls, perkLocal);
            ReplaceCallsWithLocal(
                codes,
                hospitalizationCalls,
                hospitalizationLocal);

            // Insert after Vanilla's scattered-update early return and the
            // GameDBNeed local setup, immediately before the first original
            // PerkComponent lookup. Both GetComponent calls are pure list
            // scans, so this preserves gameplay while avoiding repeats.
            int anchorIndex = perkCalls[0] - 2;
            CodeInstruction anchor = codes[anchorIndex];

            CodeInstruction first =
                new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(anchor.labels);
            anchor.labels.Clear();
            first.blocks.AddRange(anchor.blocks);
            anchor.blocks.Clear();

            List<CodeInstruction> initialization =
                new List<CodeInstruction>();
            initialization.Add(first);
            initialization.Add(
                new CodeInstruction(OpCodes.Ldfld, entityField));
            initialization.Add(
                new CodeInstruction(OpCodes.Callvirt, perkGetter));
            initialization.Add(
                new CodeInstruction(OpCodes.Stloc, perkLocal));
            initialization.Add(
                new CodeInstruction(OpCodes.Ldarg_0));
            initialization.Add(
                new CodeInstruction(OpCodes.Ldfld, entityField));
            initialization.Add(
                new CodeInstruction(
                    OpCodes.Callvirt,
                    hospitalizationGetter));
            initialization.Add(
                new CodeInstruction(
                    OpCodes.Stloc,
                    hospitalizationLocal));

            codes.InsertRange(anchorIndex, initialization);
            return codes;
        }

        private static MethodInfo GetComponentGetter(
            Type componentType)
        {
            MethodInfo[] methods = typeof(Entity).GetMethods(
                BindingFlags.Instance | BindingFlags.Public);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "GetComponent" ||
                    !method.IsGenericMethodDefinition ||
                    method.GetGenericArguments().Length != 1 ||
                    method.GetParameters().Length != 0)
                {
                    continue;
                }

                return method.MakeGenericMethod(componentType);
            }

            return null;
        }

        private static List<int> FindAndValidateCalls(
            List<CodeInstruction> codes,
            MethodInfo getter,
            FieldInfo entityField,
            string label)
        {
            List<int> result = new List<int>();

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(getter))
                {
                    continue;
                }

                if (i < 2 ||
                    !codes[i - 2].IsLdarg(0) ||
                    !codes[i - 1].LoadsField(entityField))
                {
                    throw new InvalidOperationException(
                        "Unexpected " + label +
                        " GetComponent IL pattern in MoodComponent.Update().");
                }

                result.Add(i);
            }

            return result;
        }

        private static void ReplaceCallsWithLocal(
            List<CodeInstruction> codes,
            List<int> callIndices,
            LocalBuilder local)
        {
            for (int i = 0; i < callIndices.Count; i++)
            {
                int callIndex = callIndices[i];

                // Keep labels/exception metadata on the original instructions:
                // turning the receiver loads into NOPs preserves every branch
                // target while removing their stack effect.
                codes[callIndex - 2].opcode = OpCodes.Nop;
                codes[callIndex - 2].operand = null;
                codes[callIndex - 1].opcode = OpCodes.Nop;
                codes[callIndex - 1].operand = null;

                codes[callIndex].opcode = OpCodes.Ldloc;
                codes[callIndex].operand = local;
            }
        }
    }
}
