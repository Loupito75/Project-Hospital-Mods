using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class IdleStaffLookupReusePatch
    {
        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            PatchTarget(
                harmony,
                typeof(BehaviorDoctor),
                "UpdateStateIdle");
            PatchTarget(
                harmony,
                typeof(BehaviorNurse),
                "UpdateStateIdle");
            PatchTarget(
                harmony,
                typeof(BehaviorLabSpecialist),
                "UpdateStateIdle");

            Plugin.Log.LogInfo(
                "OptimizeIdleStaffLookups installed: EmployeeComponent lookups are reused in Doctor, Nurse and Lab Specialist idle states.");
        }

        private static void PatchTarget(
            Harmony harmony,
            Type behaviorType,
            string methodName)
        {
            MethodInfo target = AccessTools.Method(
                behaviorType,
                methodName,
                new Type[] { typeof(float) });
            MethodInfo transpiler = AccessTools.Method(
                typeof(IdleStaffLookupReusePatch),
                "Transpiler");

            if (target == null)
            {
                throw new MissingMethodException(
                    behaviorType.FullName,
                    methodName + "(Single)");
            }
            if (transpiler == null)
            {
                throw new MissingMethodException(
                    typeof(IdleStaffLookupReusePatch).FullName,
                    "Transpiler");
            }

            harmony.Patch(
                target,
                transpiler: new HarmonyMethod(transpiler));
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

            MethodInfo employeeGetter = GetBehaviorComponentGetter(
                typeof(EmployeeComponent));
            if (employeeGetter == null)
            {
                throw new MissingMethodException(
                    typeof(Behavior).FullName,
                    "GetComponent<EmployeeComponent>()");
            }

            int expected = GetExpectedLookupCount(__originalMethod);
            List<int> calls =
                new List<int>();

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(employeeGetter))
                {
                    continue;
                }

                if (i < 1 || !codes[i - 1].IsLdarg(0))
                {
                    throw new InvalidOperationException(
                        "Unexpected EmployeeComponent lookup IL pattern in " +
                        GetMethodLabel(__originalMethod) + ".");
                }

                calls.Add(i);
            }

            if (calls.Count != expected)
            {
                throw new InvalidOperationException(
                    "Expected " + expected +
                    " EmployeeComponent lookups in " +
                    GetMethodLabel(__originalMethod) +
                    ", found " + calls.Count + ".");
            }

            LocalBuilder local =
                generator.DeclareLocal(typeof(EmployeeComponent));

            // Rewrite later lookups before inserting new instructions so the
            // captured indexes still refer to the original instruction list.
            for (int i = 1; i < calls.Count; i++)
            {
                int callIndex = calls[i];

                codes[callIndex - 1].opcode = OpCodes.Nop;
                codes[callIndex - 1].operand = null;

                codes[callIndex].opcode = OpCodes.Ldloc;
                codes[callIndex].operand = local;
            }

            int firstCall = calls[0];

            // The first lookup remains Vanilla. Store the same reference while
            // keeping it on the stack for its original consumer.
            codes.Insert(
                firstCall + 1,
                new CodeInstruction(OpCodes.Dup));
            codes.Insert(
                firstCall + 2,
                new CodeInstruction(OpCodes.Stloc, local));

            return codes;
        }

        private static MethodInfo GetBehaviorComponentGetter(
            Type componentType)
        {
            MethodInfo[] methods = typeof(Behavior).GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.DeclaredOnly);

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

        private static int GetExpectedLookupCount(
            MethodBase originalMethod)
        {
            if (originalMethod == null ||
                originalMethod.DeclaringType == null ||
                originalMethod.Name != "UpdateStateIdle")
            {
                throw new InvalidOperationException(
                    "Unexpected idle staff target: " +
                    GetMethodLabel(originalMethod) + ".");
            }

            Type type = originalMethod.DeclaringType;
            if (type == typeof(BehaviorDoctor) ||
                type == typeof(BehaviorNurse))
            {
                return 10;
            }
            if (type == typeof(BehaviorLabSpecialist))
            {
                return 14;
            }

            throw new InvalidOperationException(
                "Unexpected idle staff target: " +
                GetMethodLabel(originalMethod) + ".");
        }

        private static string GetMethodLabel(
            MethodBase originalMethod)
        {
            if (originalMethod == null)
            {
                return "unknown method";
            }

            return
                (originalMethod.DeclaringType == null
                    ? "unknown type"
                    : originalMethod.DeclaringType.Name) +
                "." + originalMethod.Name;
        }
    }
}
