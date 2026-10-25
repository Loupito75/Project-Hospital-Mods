using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class HourlyRoleValidationPatch
    {
        [ThreadStatic]
        private static int s_scopeDepth;

        [ThreadStatic]
        private static GameDBEmployeeRole[] s_roles;

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo validateRoles = AccessTools.Method(
                typeof(MapEditorController),
                "ValidateRoles",
                Type.EmptyTypes);
            MethodInfo checkAnyRoleAssigned = AccessTools.Method(
                typeof(EmployeeComponent),
                "CheckAnyRoleAssigned",
                Type.EmptyTypes);
            MethodInfo prefix = AccessTools.Method(
                typeof(HourlyRoleValidationPatch),
                "ValidateRolesPrefix");
            MethodInfo finalizer = AccessTools.Method(
                typeof(HourlyRoleValidationPatch),
                "ValidateRolesFinalizer");
            MethodInfo transpiler = AccessTools.Method(
                typeof(HourlyRoleValidationPatch),
                "CheckAnyRoleAssignedTranspiler");

            if (validateRoles == null)
            {
                throw new MissingMethodException(
                    typeof(MapEditorController).FullName,
                    "ValidateRoles()");
            }
            if (checkAnyRoleAssigned == null)
            {
                throw new MissingMethodException(
                    typeof(EmployeeComponent).FullName,
                    "CheckAnyRoleAssigned()");
            }
            if (prefix == null ||
                finalizer == null ||
                transpiler == null)
            {
                throw new MissingMethodException(
                    typeof(HourlyRoleValidationPatch).FullName,
                    "HRO hourly role validation patch method");
            }

            harmony.Patch(
                validateRoles,
                prefix: new HarmonyMethod(prefix),
                finalizer: new HarmonyMethod(finalizer));
            harmony.Patch(
                checkAnyRoleAssigned,
                transpiler: new HarmonyMethod(transpiler));

            Plugin.Log.LogInfo(
                "OptimizeHourlyRoleValidation installed: one GameDBEmployeeRole array is reused only during each native ValidateRoles pass.");
        }

        internal static void Reset()
        {
            s_scopeDepth = 0;
            s_roles = null;
        }

        private static void ValidateRolesPrefix()
        {
            if (s_scopeDepth == 0)
            {
                s_roles =
                    Database.Instance.GetEntries<GameDBEmployeeRole>();
            }

            s_scopeDepth++;
        }

        private static Exception ValidateRolesFinalizer(
            Exception __exception)
        {
            if (s_scopeDepth > 0)
            {
                s_scopeDepth--;
            }

            if (s_scopeDepth == 0)
            {
                s_roles = null;
            }

            return __exception;
        }

        private static GameDBEmployeeRole[] GetEmployeeRoles(
            Database database)
        {
            if (s_scopeDepth > 0 && s_roles != null)
            {
                return s_roles;
            }

            return database.GetEntries<GameDBEmployeeRole>();
        }

        private static IEnumerable<CodeInstruction>
            CheckAnyRoleAssignedTranspiler(
                IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            MethodInfo getEntries = FindClosedGetEntriesMethod();
            MethodInfo helper = AccessTools.Method(
                typeof(HourlyRoleValidationPatch),
                "GetEmployeeRoles");

            if (getEntries == null)
            {
                throw new MissingMethodException(
                    typeof(Database).FullName,
                    "GetEntries<GameDBEmployeeRole>()");
            }
            if (helper == null)
            {
                throw new MissingMethodException(
                    typeof(HourlyRoleValidationPatch).FullName,
                    "GetEmployeeRoles(Database)");
            }

            int matches = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(getEntries))
                {
                    continue;
                }

                codes[i].opcode = OpCodes.Call;
                codes[i].operand = helper;
                matches++;
            }

            if (matches != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one Database.GetEntries<GameDBEmployeeRole>() call in EmployeeComponent.CheckAnyRoleAssigned, found " +
                    matches + ".");
            }

            return codes;
        }

        private static MethodInfo FindClosedGetEntriesMethod()
        {
            MethodInfo[] methods =
                typeof(Database).GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Public);

            foreach (MethodInfo method in methods)
            {
                if (method.Name != "GetEntries" ||
                    !method.IsGenericMethodDefinition ||
                    method.GetParameters().Length != 0)
                {
                    continue;
                }

                Type[] arguments =
                    method.GetGenericArguments();

                if (arguments.Length != 1)
                {
                    continue;
                }

                return method.MakeGenericMethod(
                    typeof(GameDBEmployeeRole));
            }

            return null;
        }
    }
}
