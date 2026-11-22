using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using GLib;
using Lopital;

namespace HospitalFixes.Patches
{
    internal static class IntrinsicWorkspaceRoleFix
    {
        private sealed class RoleMetadataSnapshot
        {
            internal bool AlwaysChecked;
            internal bool ForceWorkSpace;
            internal string ForceWorkSpaceTag;
        }

        private static readonly MethodInfo AlwaysCheckedSetter =
            AccessTools.PropertySetter(
                typeof(GameDBEmployeeRole),
                "AlwaysChecked");

        private static readonly MethodInfo ForceWorkSpaceSetter =
            AccessTools.PropertySetter(
                typeof(GameDBEmployeeRole),
                "ForceWorkSpace");

        private static readonly MethodInfo ForceWorkSpaceTagSetter =
            AccessTools.PropertySetter(
                typeof(GameDBEmployeeRole),
                "ForceWorkSpaceTag");

        private static readonly Dictionary<
            GameDBEmployeeRole,
            RoleMetadataSnapshot>
            OriginalMetadata =
                new Dictionary<
                    GameDBEmployeeRole,
                    RoleMetadataSnapshot>();

        private static bool s_metadataErrorLogged;

        internal static void ResetForBaseLoad()
        {
            // A full base Database load replaces duplicate entries with fresh
            // objects, so snapshots of the previous table must not be restored
            // into the newly loaded table.
            OriginalMetadata.Clear();
            s_metadataErrorLogged = false;
        }

        internal static void ApplyDatabaseCorrections(
            Database database)
        {
            if (!FixConfig.FixIntrinsicWorkspaceRoles ||
                database == null)
            {
                return;
            }

            if (AlwaysCheckedSetter == null ||
                ForceWorkSpaceSetter == null ||
                ForceWorkSpaceTagSetter == null)
            {
                LogMetadataErrorOnce(
                    "GameDBEmployeeRole private setters could not be resolved.");
                return;
            }

            int changed = 0;

            // Explicit allow-list from the current Vanilla role audit.
            // Do not infer that every future ForceWorkSpace role is intrinsic.
            changed += NormalizeIntrinsicRole(
                database,
                "EMPL_ROLE_CLEANING",
                "cleaning_closet",
                required: true);
            changed += NormalizeIntrinsicRole(
                database,
                "EMPL_ROLE_RECEPTIONIST",
                "nurse_reception",
                required: true);
            changed += NormalizeIntrinsicRole(
                database,
                "EMPL_ROLE_PHARMACIST",
                "pharmacy",
                required: false);
            changed += NormalizeIntrinsicRole(
                database,
                "EMPL_ROLE_JANITOR_MANAGER",
                "janitor_admin",
                required: false);
            changed += NormalizeIntrinsicRole(
                database,
                "EMPL_ROLE_VENDOR",
                "gift_shop",
                required: false);

            if (HfxDiagnostics.Enabled &&
                changed > 0)
            {
                HfxDiagnostics.Result(
                    "IntrinsicWorkspaceRoles",
                    "Vanilla employee role metadata",
                    "normalized",
                    "roleCount=" + changed);
            }

            ReconcileExistingEmployees();
        }

        private static void ReconcileExistingEmployees()
        {
            if (Hospital.Instance == null ||
                Hospital.Instance.m_characters == null)
            {
                return;
            }

            for (int i = 0;
                 i < Hospital.Instance.m_characters.Count;
                 i++)
            {
                Entity character =
                    Hospital.Instance.m_characters[i];

                if (character == null ||
                    character.DEBUG_destroyed)
                {
                    continue;
                }

                EmployeeComponent employee =
                    character.GetComponent<EmployeeComponent>();

                if (employee != null)
                {
                    Reconcile(employee);
                }
            }
        }

        internal static void RestoreDatabaseCorrections()
        {
            if (OriginalMetadata.Count == 0 ||
                AlwaysCheckedSetter == null ||
                ForceWorkSpaceSetter == null ||
                ForceWorkSpaceTagSetter == null)
            {
                OriginalMetadata.Clear();
                return;
            }

            foreach (KeyValuePair<
                         GameDBEmployeeRole,
                         RoleMetadataSnapshot> pair
                     in OriginalMetadata)
            {
                GameDBEmployeeRole role =
                    pair.Key;
                RoleMetadataSnapshot state =
                    pair.Value;

                if (role == null ||
                    state == null)
                {
                    continue;
                }

                try
                {
                    SetRoleMetadata(
                        role,
                        state.AlwaysChecked,
                        state.ForceWorkSpace,
                        state.ForceWorkSpaceTag);
                }
                catch (Exception exception)
                {
                    LogMetadataErrorOnce(
                        "Failed to restore Vanilla employee role metadata: " +
                        exception.GetType().Name +
                        ": " +
                        exception.Message);
                }
            }

            OriginalMetadata.Clear();
        }

        internal static bool HasRequiredWorkspace(
            EmployeeComponent employee,
            GameDBEmployeeRole role)
        {
            if (employee == null ||
                employee.m_state == null ||
                role == null ||
                !role.ForceWorkSpace ||
                string.IsNullOrEmpty(
                    role.ForceWorkSpaceTag) ||
                employee.m_state.m_homeRoom == null)
            {
                return false;
            }

            Room room =
                employee.m_state.m_homeRoom.GetEntity();

            return room != null &&
                room.m_roomPersistentData != null &&
                room.m_roomPersistentData.m_roomType != null &&
                room.m_roomPersistentData.m_roomType.Entry != null &&
                room.m_roomPersistentData
                    .m_roomType.Entry.HasTag(
                        role.ForceWorkSpaceTag);
        }

        internal static void Reconcile(
            EmployeeComponent employee)
        {
            if (!FixConfig.FixIntrinsicWorkspaceRoles ||
                employee == null ||
                employee.m_state == null ||
                Database.Instance == null)
            {
                return;
            }

            GameDBEmployeeRole[] roles =
                Database.Instance.GetEntries<GameDBEmployeeRole>();

            if (roles == null)
            {
                return;
            }

            for (int i = 0;
                 i < roles.Length;
                 i++)
            {
                GameDBEmployeeRole role =
                    roles[i];

                if (role == null ||
                    !role.AlwaysChecked ||
                    !role.ForceWorkSpace)
                {
                    continue;
                }

                bool shouldBeActive =
                    employee.HasOccupationForRole(role) &&
                    employee.HasSkillForRole(role) &&
                    employee.HasWorkspaceForRole(role);

                if (employee.HasRole(role) ==
                    shouldBeActive)
                {
                    continue;
                }

                employee.SetAllowedRole(
                    role.FlagBit,
                    shouldBeActive);

                if (HfxDiagnostics.Enabled)
                {
                    HfxDiagnostics.Result(
                        "IntrinsicWorkspaceRoles",
                        "intrinsic workspace role",
                        shouldBeActive
                            ? "enabled"
                            : "disabled",
                        "role=" +
                        role.DatabaseID +
                        " employee={" +
                        HfxDiagnostics.EntityContext(
                            employee.m_entity) +
                        "}");
                }
            }
        }

        private static int NormalizeIntrinsicRole(
            Database database,
            string roleId,
            string workspaceTag,
            bool required)
        {
            GameDBEmployeeRole role =
                database.GetEntry<GameDBEmployeeRole>(
                    roleId);

            if (role == null)
            {
                if (required)
                {
                    LogMetadataErrorOnce(
                        "Expected Vanilla employee role is missing: " +
                        roleId +
                        ".");
                }

                return 0;
            }

            bool alreadyCorrect =
                role.AlwaysChecked &&
                role.ForceWorkSpace &&
                string.Equals(
                    role.ForceWorkSpaceTag,
                    workspaceTag,
                    StringComparison.Ordinal);

            if (alreadyCorrect)
            {
                return 0;
            }

            if (!OriginalMetadata.ContainsKey(role))
            {
                OriginalMetadata.Add(
                    role,
                    new RoleMetadataSnapshot
                    {
                        AlwaysChecked =
                            role.AlwaysChecked,
                        ForceWorkSpace =
                            role.ForceWorkSpace,
                        ForceWorkSpaceTag =
                            role.ForceWorkSpaceTag
                    });
            }

            try
            {
                SetRoleMetadata(
                    role,
                    alwaysChecked: true,
                    forceWorkSpace: true,
                    forceWorkSpaceTag: workspaceTag);
                return 1;
            }
            catch (Exception exception)
            {
                LogMetadataErrorOnce(
                    "Failed to normalize " +
                    roleId +
                    ": " +
                    exception.GetType().Name +
                    ": " +
                    exception.Message);
                return 0;
            }
        }

        private static void SetRoleMetadata(
            GameDBEmployeeRole role,
            bool alwaysChecked,
            bool forceWorkSpace,
            string forceWorkSpaceTag)
        {
            AlwaysCheckedSetter.Invoke(
                role,
                new object[] { alwaysChecked });
            ForceWorkSpaceSetter.Invoke(
                role,
                new object[] { forceWorkSpace });
            ForceWorkSpaceTagSetter.Invoke(
                role,
                new object[] { forceWorkSpaceTag });
        }

        private static void LogMetadataErrorOnce(
            string message)
        {
            if (s_metadataErrorLogged ||
                Plugin.Log == null)
            {
                return;
            }

            s_metadataErrorLogged = true;
            Plugin.Log.LogWarning(
                "[IntrinsicWorkspaceRoles] " +
                message);
        }
    }

    [HarmonyPatch(
        typeof(EmployeeComponent),
        nameof(EmployeeComponent.HasWorkspaceForRole))]
    internal static class IntrinsicWorkspaceRoleWorkspacePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            EmployeeComponent __instance,
            GameDBEmployeeRole employeeRole,
            ref bool __result)
        {
            if (!FixConfig.FixIntrinsicWorkspaceRoles ||
                !__result ||
                employeeRole == null ||
                !employeeRole.ForceWorkSpace)
            {
                return;
            }

            // Vanilla currently evaluates ForceWorkSpace only inside an
            // unrelated DLC guard. Restore the declared role invariant for
            // every ForceWorkSpace role without replacing the other Vanilla
            // workspace restrictions.
            __result =
                IntrinsicWorkspaceRoleFix
                    .HasRequiredWorkspace(
                        __instance,
                        employeeRole);
        }
    }

    [HarmonyPatch(
        typeof(EmployeeComponent),
        nameof(EmployeeComponent.ResetRoles))]
    internal static class IntrinsicWorkspaceRoleResetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            EmployeeComponent __instance)
        {
            IntrinsicWorkspaceRoleFix
                .Reconcile(__instance);
        }
    }

    [HarmonyPatch(
        typeof(EmployeeComponent),
        nameof(EmployeeComponent.CacheRoleCount))]
    internal static class IntrinsicWorkspaceRoleCachePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            EmployeeComponent __instance)
        {
            IntrinsicWorkspaceRoleFix
                .Reconcile(__instance);
        }
    }
}
