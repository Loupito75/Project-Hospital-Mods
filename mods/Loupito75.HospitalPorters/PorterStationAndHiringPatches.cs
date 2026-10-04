using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalPorters
{
    internal static class PorterDepartmentRegistration
    {
        private static readonly MethodInfo RequiredRoomsHospitalizationSetter =
            AccessTools.PropertySetter(typeof(GameDBDepartment), nameof(GameDBDepartment.RequiredRoomsHospitalization));

        internal static void RegisterAll(Database database)
        {
            if (database == null || object.ReferenceEquals(RequiredRoomsHospitalizationSetter, null))
            {
                return;
            }

            GameDBRoomType station = database.GetEntry<GameDBRoomType>(PorterIds.PorterStationRoom);
            GameDBDepartment[] departments = database.GetEntries<GameDBDepartment>();
            if (station == null || departments == null)
            {
                return;
            }

            int added = 0;
            int reordered = 0;
            int reboundReferences = 0;
            foreach (GameDBDepartment department in departments)
            {
                if (department == null || department.NoHospitalization)
                {
                    continue;
                }

                GameDBDepartmentRoomRequirement[] current = department.RequiredRoomsHospitalization;
                int reboundCount;
                GameDBDepartmentRoomRequirement[] rebound =
                    RebindRequirements(database, current, out reboundCount);

                GameDBDepartmentRoomRequirement stationRequirement = Find(rebound, station);
                bool wasPresent = stationRequirement != null;
                if (!wasPresent)
                {
                    stationRequirement = new GameDBDepartmentRoomRequirement
                    {
                        RoomDatabaseEntryRef = new DatabaseEntryRef<GameDBRoomType>(station),
                        MinCount = 0,
                        MaxCount = 2
                    };
                }

                GameDBDepartmentRoomRequirement[] normalized =
                    BuildRequiredThenStationThenOptional(rebound, stationRequirement, station);
                if (reboundCount > 0 || !SameOrder(current, normalized))
                {
                    RequiredRoomsHospitalizationSetter.Invoke(department, new object[] { normalized });
                    reboundReferences += reboundCount;
                    if (wasPresent)
                    {
                        reordered++;
                    }
                    else
                    {
                        added++;
                    }
                }
            }

            if (added + reordered + reboundReferences > 0)
            {
                PorterDiagnostics.Log(
                    $"Porter station hospitalization registration normalized: added={added}, reordered={reordered}, " +
                    $"reboundRoomRefs={reboundReferences}. " +
                    "The Porter station remains the first optional hospitalization room after native required rooms.");
            }
        }

        private static GameDBDepartmentRoomRequirement[] RebindRequirements(
            Database database,
            GameDBDepartmentRoomRequirement[] requirements,
            out int reboundCount)
        {
            reboundCount = 0;
            if (requirements == null)
            {
                return null;
            }

            GameDBDepartmentRoomRequirement[] result =
                new GameDBDepartmentRoomRequirement[requirements.Length];
            for (int i = 0; i < requirements.Length; i++)
            {
                bool rebound;
                result[i] = RebindRequirement(database, requirements[i], out rebound);
                if (rebound)
                {
                    reboundCount++;
                }
            }
            return result;
        }

        private static GameDBDepartmentRoomRequirement RebindRequirement(
            Database database,
            GameDBDepartmentRoomRequirement requirement,
            out bool rebound)
        {
            rebound = false;
            if (database == null || requirement == null ||
                requirement.RoomDatabaseEntryRef == null)
            {
                return requirement;
            }

            ID roomId = requirement.RoomDatabaseEntryRef;
            GameDBRoomType canonicalRoom = database.GetEntry<GameDBRoomType>(roomId);
            if (canonicalRoom == null ||
                object.ReferenceEquals(requirement.RoomDatabaseEntryRef.Entry, canonicalRoom))
            {
                return requirement;
            }

            rebound = true;
            return new GameDBDepartmentRoomRequirement
            {
                RoomDatabaseEntryRef = new DatabaseEntryRef<GameDBRoomType>(canonicalRoom),
                MinCount = requirement.MinCount,
                MaxCount = requirement.MaxCount
            };
        }

        private static GameDBDepartmentRoomRequirement Find(
            GameDBDepartmentRoomRequirement[] requirements,
            GameDBRoomType station)
        {
            if (requirements == null)
            {
                return null;
            }

            foreach (GameDBDepartmentRoomRequirement requirement in requirements)
            {
                if (IsStation(requirement, station))
                {
                    return requirement;
                }
            }
            return null;
        }

        private static GameDBDepartmentRoomRequirement[] BuildRequiredThenStationThenOptional(
            GameDBDepartmentRoomRequirement[] current,
            GameDBDepartmentRoomRequirement stationRequirement,
            GameDBRoomType station)
        {
            List<GameDBDepartmentRoomRequirement> result = new List<GameDBDepartmentRoomRequirement>();
            if (current != null)
            {
                foreach (GameDBDepartmentRoomRequirement requirement in current)
                {
                    if (requirement != null && !IsStation(requirement, station) && requirement.MinCount > 0)
                    {
                        result.Add(requirement);
                    }
                }
            }

            result.Add(stationRequirement);

            if (current != null)
            {
                foreach (GameDBDepartmentRoomRequirement requirement in current)
                {
                    if (requirement != null && !IsStation(requirement, station) && requirement.MinCount <= 0)
                    {
                        result.Add(requirement);
                    }
                }
            }
            return result.ToArray();
        }

        private static bool IsStation(GameDBDepartmentRoomRequirement requirement, GameDBRoomType station)
        {
            if (requirement == null || requirement.RoomDatabaseEntryRef == null || station == null)
            {
                return false;
            }

            GameDBRoomType roomType = requirement.RoomDatabaseEntryRef.Entry;
            return roomType != null &&
                roomType.DatabaseID.ToString() == station.DatabaseID.ToString();
        }

        private static bool SameOrder(
            GameDBDepartmentRoomRequirement[] left,
            GameDBDepartmentRoomRequirement[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (!object.ReferenceEquals(left[i], right[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }

    [HarmonyPatch(
        typeof(PorterStationRegistry),
        nameof(PorterStationRegistry.Register),
        new Type[] { typeof(Database) })]
    internal static class PorterNormalizeStationRegistrationPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Database database)
        {
            PorterDepartmentRegistration.RegisterAll(database);
        }
    }

    [HarmonyPatch(typeof(Database), nameof(Database.ReadFiles), new Type[] { typeof(string), typeof(bool) })]
    internal static class PorterDatabaseRegistrationPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Database __instance)
        {
            if (!ModDatabase.IsLoading &&
                __instance.GetEntry<GameDBRoomType>(PorterIds.PorterStationRoom) != null)
            {
                PorterDepartmentRegistration.RegisterAll(__instance);
            }
        }
    }

    internal static class PorterHiringOpenState
    {
        internal static int Depth;
    }

    internal static class PorterHiringSelection
    {
        internal static void SelectCurrentPorters(Shift shift)
        {
            if (HiringManager.Instance == null || HiringManager.Instance.m_workspace == null ||
                Hospital.Instance == null || Hospital.Instance.m_activeDepartment.GetEntity() == null)
            {
                return;
            }

            EntityIDPointer<TileObject> workspacePointer = HiringManager.Instance.m_workspace;
            TileObject workspace = workspacePointer.GetEntity();
            if (!PorterLogisticsUi.IsPorterLocker(workspace))
            {
                return;
            }

            GameObject hiringPanel = MapEditorUIController.Instance?.m_hiringPanel;
            HiringPanelController controller =
                hiringPanel == null ? null : hiringPanel.GetComponent<HiringPanelController>();
            if (controller == null)
            {
                Plugin.Log?.LogError("Porter locker hiring could not resolve HiringPanelController after native OpenHiringCard().");
                return;
            }

            try
            {
                GameDBDepartment department = Hospital.Instance.m_activeDepartment.GetEntity().GetDepartmentType();
                PorterCandidatePool.Ensure(department);

                // Reuse the same active custom filter/delegate that a manual click uses. The engine
                // type remains CharacterNurse, but UpdateButtons is presented as Technologist.
                PorterHiringUi.ConfigureNurseDelegate(
                    controller,
                    workspacePointer,
                    forceReception: false);

                PorterHiringState.Active = true;
                PorterHiringState.SelectingPorter = true;
                try
                {
                    controller.SetCharacterType(
                        LopitalTypes.CharacterNurse,
                        PorterHiringIcons.PorterIcon,
                        LocalizationManager.Get(PorterIds.PorterCandidates));
                }
                finally
                {
                    PorterHiringState.SelectingPorter = false;
                }

                controller.Update();
            }
            catch (Exception exception)
            {
                PorterHiringState.Active = false;
                PorterHiringState.SelectingPorter = false;
                Plugin.Log?.LogError("Porter locker hiring final selection failed: " + exception);
            }
        }
    }

    [HarmonyPatch(
        typeof(LogisticsWorkspacePanelController),
        "OpenHiringCard",
        new Type[] { typeof(Shift) })]
    internal static class PorterHiringOpenSelectionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            PorterHiringOpenState.Depth++;
            PorterHiringState.Active = false;
            PorterHiringState.SelectingPorter = false;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Shift shift)
        {
            PorterHiringSelection.SelectCurrentPorters(shift);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(Exception __exception)
        {
            if (PorterHiringOpenState.Depth > 0)
            {
                PorterHiringOpenState.Depth--;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PorterHiringUi), "SelectPorters")]
    internal static class PorterSuppressEarlyPorterSelectionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return PorterHiringOpenState.Depth == 0;
        }
    }
}
