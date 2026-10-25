using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class HospitalizationComponentLookupReusePatch
    {
        private const int ExpectedUpdateLookups = 28;
        private const int ExpectedSleepingLookups = 6;

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo update = AccessTools.Method(
                typeof(HospitalizationComponent),
                "Update",
                new Type[] { typeof(float) });
            MethodInfo sleeping = AccessTools.Method(
                typeof(HospitalizationComponent),
                "UpdateStateSleeping",
                new Type[] { typeof(float) });
            MethodInfo transpiler = AccessTools.Method(
                typeof(HospitalizationComponentLookupReusePatch),
                "Transpiler");

            if (update == null)
            {
                throw new MissingMethodException(
                    typeof(HospitalizationComponent).FullName,
                    "Update(Single)");
            }
            if (sleeping == null)
            {
                throw new MissingMethodException(
                    typeof(HospitalizationComponent).FullName,
                    "UpdateStateSleeping(Single)");
            }
            if (transpiler == null)
            {
                throw new MissingMethodException(
                    typeof(HospitalizationComponentLookupReusePatch).FullName,
                    "Transpiler");
            }

            HarmonyMethod patch = new HarmonyMethod(transpiler);
            harmony.Patch(update, transpiler: patch);
            harmony.Patch(sleeping, transpiler: patch);

            Plugin.Log.LogInfo(
                "OptimizeHospitalizationComponentLookups installed: repeated BehaviorPatient searches are reused within HospitalizationComponent.Update and UpdateStateSleeping.");
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator,
            MethodBase __originalMethod)
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
            MethodInfo behaviorPatientGetter =
                GetComponentGetter(typeof(BehaviorPatient));

            if (entityField == null)
            {
                throw new MissingFieldException(
                    typeof(GLib.Component).FullName,
                    "m_entity");
            }
            if (behaviorPatientGetter == null)
            {
                throw new MissingMethodException(
                    typeof(Entity).FullName,
                    "GetComponent<BehaviorPatient>()");
            }

            int expected = GetExpectedLookupCount(__originalMethod);
            List<int> calls = FindAndValidateCalls(
                codes,
                behaviorPatientGetter,
                entityField,
                __originalMethod);

            if (calls.Count != expected)
            {
                throw new InvalidOperationException(
                    "Expected " + expected +
                    " BehaviorPatient lookups in " +
                    GetMethodName(__originalMethod) +
                    ", found " + calls.Count + ".");
            }

            LocalBuilder local =
                generator.DeclareLocal(typeof(BehaviorPatient));

            // Replace later lookups before inserting instructions so all
            // captured indexes still refer to the original instruction list.
            for (int i = 1; i < calls.Count; i++)
            {
                int callIndex = calls[i];

                // Preserve labels and exception metadata by leaving the
                // original receiver-load instructions in place as NOPs.
                codes[callIndex - 2].opcode = OpCodes.Nop;
                codes[callIndex - 2].operand = null;
                codes[callIndex - 1].opcode = OpCodes.Nop;
                codes[callIndex - 1].operand = null;

                codes[callIndex].opcode = OpCodes.Ldloc;
                codes[callIndex].operand = local;
            }

            int firstCall = calls[0];

            // Keep the first native GetComponent result on the stack for its
            // original consumer while storing the same reference for all
            // subsequent uses during this method invocation.
            codes.Insert(
                firstCall + 1,
                new CodeInstruction(OpCodes.Dup));
            codes.Insert(
                firstCall + 2,
                new CodeInstruction(OpCodes.Stloc, local));

            return codes;
        }

        private static int GetExpectedLookupCount(
            MethodBase originalMethod)
        {
            if (originalMethod != null &&
                originalMethod.Name == "Update")
            {
                return ExpectedUpdateLookups;
            }
            if (originalMethod != null &&
                originalMethod.Name == "UpdateStateSleeping")
            {
                return ExpectedSleepingLookups;
            }

            throw new InvalidOperationException(
                "Unexpected HospitalizationComponent target: " +
                GetMethodName(originalMethod) + ".");
        }

        private static string GetMethodName(
            MethodBase originalMethod)
        {
            return originalMethod == null
                ? "unknown method"
                : originalMethod.Name;
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
            MethodBase originalMethod)
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
                        "Unexpected BehaviorPatient GetComponent IL pattern in " +
                        GetMethodName(originalMethod) + ".");
                }

                result.Add(i);
            }

            return result;
        }
    }
}
