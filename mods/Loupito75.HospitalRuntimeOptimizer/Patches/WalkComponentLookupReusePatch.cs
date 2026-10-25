using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class WalkComponentLookupReusePatch
    {
        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo update = AccessTools.Method(
                typeof(WalkComponent),
                "Update",
                new Type[] { typeof(float) });
            MethodInfo transpiler = AccessTools.Method(
                typeof(WalkComponentLookupReusePatch),
                "Transpiler");

            if (update == null)
            {
                throw new MissingMethodException(
                    typeof(WalkComponent).FullName,
                    "Update(Single)");
            }
            if (transpiler == null)
            {
                throw new MissingMethodException(
                    typeof(WalkComponentLookupReusePatch).FullName,
                    "Transpiler");
            }

            harmony.Patch(
                update,
                transpiler: new HarmonyMethod(transpiler));

            Plugin.Log.LogInfo(
                "OptimizeWalkComponentLookups installed: repeated Grid, SoundSource and Hospitalization component searches are reused within WalkComponent.Update.");
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
            if (entityField == null)
            {
                throw new MissingFieldException(
                    typeof(GLib.Component).FullName,
                    "m_entity");
            }

            CacheLookupGroup(
                codes,
                generator,
                entityField,
                typeof(GridComponent),
                2);
            CacheLookupGroup(
                codes,
                generator,
                entityField,
                typeof(SoundSourceComponent),
                2);
            CacheLookupGroup(
                codes,
                generator,
                entityField,
                typeof(HospitalizationComponent),
                3);

            return codes;
        }

        private static void CacheLookupGroup(
            List<CodeInstruction> codes,
            ILGenerator generator,
            FieldInfo entityField,
            Type componentType,
            int expectedCalls)
        {
            MethodInfo getter = GetComponentGetter(componentType);
            if (getter == null)
            {
                throw new MissingMethodException(
                    typeof(Entity).FullName,
                    "GetComponent<" + componentType.Name + ">()");
            }

            List<int> calls = FindAndValidateCalls(
                codes,
                getter,
                entityField,
                componentType.Name);

            if (calls.Count != expectedCalls)
            {
                throw new InvalidOperationException(
                    "Expected " + expectedCalls + " " +
                    componentType.Name +
                    " lookups in WalkComponent.Update(), found " +
                    calls.Count + ".");
            }

            LocalBuilder local =
                generator.DeclareLocal(componentType);

            // Replace every lookup after the first one before inserting any
            // new instructions, so the captured indices remain valid.
            for (int i = 1; i < calls.Count; i++)
            {
                int callIndex = calls[i];

                // Preserve labels/exception metadata on the original
                // receiver-load instructions by turning them into NOPs.
                codes[callIndex - 2].opcode = OpCodes.Nop;
                codes[callIndex - 2].operand = null;
                codes[callIndex - 1].opcode = OpCodes.Nop;
                codes[callIndex - 1].operand = null;

                codes[callIndex].opcode = OpCodes.Ldloc;
                codes[callIndex].operand = local;
            }

            int firstCall = calls[0];

            // Keep the original first GetComponent<T>() result on the stack
            // for Vanilla's null/state test while storing the same reference
            // for the immediately following uses in that block.
            codes.Insert(
                firstCall + 1,
                new CodeInstruction(OpCodes.Dup));
            codes.Insert(
                firstCall + 2,
                new CodeInstruction(OpCodes.Stloc, local));
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
                        " GetComponent IL pattern in WalkComponent.Update().");
                }

                result.Add(i);
            }

            return result;
        }
    }
}
