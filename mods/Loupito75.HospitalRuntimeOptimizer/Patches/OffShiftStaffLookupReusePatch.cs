using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class OffShiftStaffLookupReusePatch
    {
        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            PatchTarget(harmony, typeof(BehaviorDoctor), "UpdateStateAtHome");
            PatchTarget(harmony, typeof(BehaviorDoctor), "UpdateStateCommuting");
            PatchTarget(harmony, typeof(BehaviorNurse), "UpdateStateAtHome");
            PatchTarget(harmony, typeof(BehaviorNurse), "UpdateStateCommuting");
            PatchTarget(harmony, typeof(BehaviorLabSpecialist), "UpdateStateAtHome");
            PatchTarget(harmony, typeof(BehaviorLabSpecialist), "UpdateStateCommuting");

            Plugin.Log.LogInfo(
                "OptimizeOffShiftStaffLookups installed: EmployeeComponent lookups are reused in Doctor, Nurse and Lab Specialist AtHome/Commuting states.");
        }

        private static void PatchTarget(
            Harmony harmony,
            Type behaviorType,
            string methodName)
        {
            MethodInfo target = AccessTools.Method(
                behaviorType,
                methodName,
                Type.EmptyTypes);
            MethodInfo transpiler = AccessTools.Method(
                typeof(OffShiftStaffLookupReusePatch),
                "Transpiler");

            if (target == null)
            {
                throw new MissingMethodException(
                    behaviorType.FullName,
                    methodName + "()");
            }
            if (transpiler == null)
            {
                throw new MissingMethodException(
                    typeof(OffShiftStaffLookupReusePatch).FullName,
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

            FieldInfo entityField = AccessTools.Field(
                typeof(GLib.Component),
                "m_entity");
            MethodInfo behaviorGetter = GetGenericComponentGetter(
                typeof(Behavior),
                typeof(EmployeeComponent));
            MethodInfo entityGetter = GetGenericComponentGetter(
                typeof(Entity),
                typeof(EmployeeComponent));

            if (entityField == null)
            {
                throw new MissingFieldException(
                    typeof(GLib.Component).FullName,
                    "m_entity");
            }
            if (behaviorGetter == null || entityGetter == null)
            {
                throw new MissingMethodException(
                    "EmployeeComponent GetComponent<T>() lookup method was not found.");
            }

            int expected = GetExpectedLookupCount(__originalMethod);
            List<LookupSite> sites = FindAndValidateSites(
                codes,
                behaviorGetter,
                entityGetter,
                entityField,
                __originalMethod);

            if (sites.Count != expected)
            {
                throw new InvalidOperationException(
                    "Expected " + expected +
                    " EmployeeComponent lookups in " +
                    GetMethodLabel(__originalMethod) +
                    ", found " + sites.Count + ".");
            }

            LocalBuilder local =
                generator.DeclareLocal(typeof(EmployeeComponent));

            for (int i = 1; i < sites.Count; i++)
            {
                LookupSite site = sites[i];

                if (site.DirectEntityReceiver)
                {
                    codes[site.CallIndex - 2].opcode = OpCodes.Nop;
                    codes[site.CallIndex - 2].operand = null;
                    codes[site.CallIndex - 1].opcode = OpCodes.Nop;
                    codes[site.CallIndex - 1].operand = null;
                }
                else
                {
                    codes[site.CallIndex - 1].opcode = OpCodes.Nop;
                    codes[site.CallIndex - 1].operand = null;
                }

                codes[site.CallIndex].opcode = OpCodes.Ldloc;
                codes[site.CallIndex].operand = local;
            }

            int firstCall = sites[0].CallIndex;
            codes.Insert(
                firstCall + 1,
                new CodeInstruction(OpCodes.Dup));
            codes.Insert(
                firstCall + 2,
                new CodeInstruction(OpCodes.Stloc, local));

            return codes;
        }

        private static List<LookupSite> FindAndValidateSites(
            List<CodeInstruction> codes,
            MethodInfo behaviorGetter,
            MethodInfo entityGetter,
            FieldInfo entityField,
            MethodBase originalMethod)
        {
            List<LookupSite> result =
                new List<LookupSite>();

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(behaviorGetter))
                {
                    if (i < 1 || !codes[i - 1].IsLdarg(0))
                    {
                        throw new InvalidOperationException(
                            "Unexpected Behavior.GetComponent<EmployeeComponent>() IL pattern in " +
                            GetMethodLabel(originalMethod) + ".");
                    }

                    result.Add(new LookupSite(i, false));
                    continue;
                }

                if (codes[i].Calls(entityGetter))
                {
                    if (i < 2 ||
                        !codes[i - 2].IsLdarg(0) ||
                        !codes[i - 1].LoadsField(entityField))
                    {
                        throw new InvalidOperationException(
                            "Unexpected Entity.GetComponent<EmployeeComponent>() IL pattern in " +
                            GetMethodLabel(originalMethod) + ".");
                    }

                    result.Add(new LookupSite(i, true));
                }
            }

            return result;
        }

        private static MethodInfo GetGenericComponentGetter(
            Type declaringType,
            Type componentType)
        {
            MethodInfo[] methods = declaringType.GetMethods(
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
                originalMethod.DeclaringType == null)
            {
                throw new InvalidOperationException(
                    "Unknown off-shift staff target.");
            }

            Type type = originalMethod.DeclaringType;
            string name = originalMethod.Name;

            if (name == "UpdateStateAtHome" &&
                (type == typeof(BehaviorDoctor) ||
                 type == typeof(BehaviorNurse) ||
                 type == typeof(BehaviorLabSpecialist)))
            {
                return 8;
            }

            if (name == "UpdateStateCommuting")
            {
                if (type == typeof(BehaviorDoctor))
                {
                    return 5;
                }
                if (type == typeof(BehaviorNurse) ||
                    type == typeof(BehaviorLabSpecialist))
                {
                    return 3;
                }
            }

            throw new InvalidOperationException(
                "Unexpected off-shift staff target: " +
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

        private struct LookupSite
        {
            internal readonly int CallIndex;
            internal readonly bool DirectEntityReceiver;

            internal LookupSite(
                int callIndex,
                bool directEntityReceiver)
            {
                CallIndex = callIndex;
                DirectEntityReceiver = directEntityReceiver;
            }
        }
    }
}
