using System;
using System.Globalization;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFlowOptimizer.Patches
{
    [HarmonyPatch(typeof(ProcedureSceneFactory))]
    internal static class SmartStaffNeedResourcePatch
    {
        private const string StaffLunchProcedureId = "CONTROL_PROCEDURE_STAFF_LUNCH";
        private const string StaffHungerNeedId = "NEED_HUNGER_STAFF";
        private const string BladderNeedId = "NEED_BLADDER";
        private const string CafeteriaRoomTag = "cafeteria";

        private static bool ResourceProceduresResolved;
        private static GameDBProcedure StaffLunchProcedure;
        private static GameDBProcedure StaffHungerProcedure;
        private static GameDBProcedure BladderProcedure;
        // Only immutable Database metadata; never cache a dynamic resource.
        private static bool CafeteriaDlcAvailable;
        private static GameDBDepartment AdministrativeDepartmentType;
        private static string[] StaffLunchCafeteriaRoomTags;
        private static string[] StaffHungerCafeteriaRoomTags;

        [HarmonyPostfix]
        [HarmonyPatch(
            "CreateProcedureScene",
            new Type[]
            {
                typeof(GameDBProcedure),
                typeof(Entity),
                typeof(Department),
                typeof(Room),
                typeof(AccessRights),
                typeof(ProcedureSceneType),
                typeof(EquipmentListRules),
                typeof(StaffSelectionRules)
            })]
        private static void CreateProcedureScenePostfix(
            GameDBProcedure procedure,
            Entity patient,
            Department department,
            AccessRights accessRights,
            ProcedureSceneType procedureSceneType,
            EquipmentListRules equipmentListRules,
            ProcedureScene __result)
        {
            if (!FlowOptimizerConfig.SmartStaffNeedResourceSelection ||
                procedure == null ||
                patient == null ||
                department == null ||
                __result == null ||
                equipmentListRules != EquipmentListRules.ONLY_FREE_SAME_FLOOR_PREFER_DPT ||
                __result.m_availability != ProcedureSceneAvailability.AVAILABLE ||
                __result.m_equipment == null ||
                __result.m_equipment.Length == 0)
            {
                return;
            }

            ResourceKind resourceKind = GetResourceKind(procedure);
            if (resourceKind == ResourceKind.None)
            {
                return;
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            EmployeeComponent employee = patient.GetComponent<EmployeeComponent>();
            if (walk == null || employee == null)
            {
                return;
            }

            TileObject vanilla = __result.m_equipment[0] == null
                ? null
                : __result.m_equipment[0].GetEntity();
            if (vanilla == null)
            {
                return;
            }

            TileObject selected = resourceKind == ResourceKind.Toilet
                ? SelectToilet(
                    procedure,
                    patient,
                    walk,
                    accessRights,
                    vanilla)
                : SelectMeal(
                    procedure,
                    patient,
                    walk,
                    accessRights,
                    procedureSceneType,
                    vanilla);

            if (selected == null || selected == vanilla)
            {
                return;
            }

            __result.m_equipment[0] = selected;

            if (FlowOptimizerConfig.StaffNeedsDiagnosticLogging)
            {
                Plugin.Log.LogInfo(
                    "[Diagnostic][StaffResource] kind=" + resourceKind +
                    "; phase=" + procedureSceneType +
                    "; vanilla=" + Describe(vanilla, patient, accessRights) +
                    "; selected=" + Describe(selected, patient, accessRights));
            }
        }

        internal static void ClearCachedProcedures()
        {
            ResourceProceduresResolved = false;
            StaffLunchProcedure = null;
            StaffHungerProcedure = null;
            BladderProcedure = null;
            CafeteriaDlcAvailable = false;
            AdministrativeDepartmentType = null;
            StaffLunchCafeteriaRoomTags = null;
            StaffHungerCafeteriaRoomTags = null;
        }

        private static ResourceKind GetResourceKind(GameDBProcedure procedure)
        {
            ResolveResourceProcedures();

            if (StaffLunchProcedure != null &&
                object.ReferenceEquals(
                    procedure,
                    StaffLunchProcedure))
            {
                return ResourceKind.Meal;
            }

            if (StaffHungerProcedure != null &&
                object.ReferenceEquals(
                    procedure,
                    StaffHungerProcedure))
            {
                return ResourceKind.Meal;
            }

            if (BladderProcedure != null &&
                object.ReferenceEquals(
                    procedure,
                    BladderProcedure))
            {
                return ResourceKind.Toilet;
            }

            return ResourceKind.None;
        }

        private static void ResolveResourceProcedures()
        {
            if (ResourceProceduresResolved ||
                Database.Instance == null ||
                !Database.Instance.Loaded)
            {
                return;
            }

            StaffLunchProcedure =
                Database.Instance.GetEntry<GameDBProcedure>(
                    StaffLunchProcedureId);

            GameDBNeed hunger =
                Database.Instance.GetEntry<GameDBNeed>(
                    StaffHungerNeedId);
            StaffHungerProcedure =
                hunger == null
                    ? null
                    : hunger.Procedure;

            GameDBNeed bladder =
                Database.Instance.GetEntry<GameDBNeed>(
                    BladderNeedId);
            BladderProcedure =
                bladder == null
                    ? null
                    : bladder.Procedure;

            CafeteriaDlcAvailable =
                Database.Instance.GetEntry<GameDBTweakableInt>(
                    "DLC_ADMIN_PATHOLOGY_ENABLED") != null;
            AdministrativeDepartmentType = CafeteriaDlcAvailable
                ? Database.Instance.GetEntry<GameDBDepartment>(
                    "DLC_DPT_ADMINISTRATIVE")
                : null;
            StaffLunchCafeteriaRoomTags =
                BuildCafeteriaRoomTags(StaffLunchProcedure);
            StaffHungerCafeteriaRoomTags =
                BuildCafeteriaRoomTags(StaffHungerProcedure);
            ResourceProceduresResolved = true;
        }

        private static string[] BuildCafeteriaRoomTags(GameDBProcedure procedure)
        {
            if (procedure == null ||
                string.IsNullOrEmpty(procedure.IgnoreDepratmentForRoom))
            {
                return null;
            }

            return new string[] { procedure.IgnoreDepratmentForRoom };
        }

        private static TileObject SelectToilet(
            GameDBProcedure procedure,
            Entity character,
            WalkComponent walk,
            AccessRights accessRights,
            TileObject vanilla)
        {
            RequiredEquipment required = GetPrimaryEquipment(procedure);
            if (required == null)
            {
                return vanilla;
            }

            if (walk == null)
            {
                return vanilla;
            }

            // A reachable distance-zero vanilla WC cannot be beaten.
            // Preserve the HTC restriction and skip this only in diagnostic mode.
            if (!FlowOptimizerConfig.StaffNeedsDiagnosticLogging &&
                vanilla.GetFloorIndex() == walk.GetFloorIndex() &&
                vanilla.m_state.m_position.m_x == walk.GetCurrentTile().m_x &&
                vanilla.m_state.m_position.m_y == walk.GetCurrentTile().m_y)
            {
                bool ignoredApiAvailable;
                if (HtcCompatibility.IsBathroomAllowedForCharacter(
                        character, vanilla, out ignoredApiAvailable) &&
                    GetComparableRouteDistance(walk, vanilla, accessRights) == 0)
                {
                    return vanilla;
                }
            }

            TileObject local = FindClosestSameFloor(
                walk,
                required.Tag,
                accessRights,
                procedure.RequiredRoomTags,
                null,
                null);

            bool vanillaApiAvailable;
            bool vanillaAllowed =
                HtcCompatibility.IsBathroomAllowedForCharacter(
                    character,
                    vanilla,
                    out vanillaApiAvailable);

            bool localApiAvailable = false;
            bool localAllowed = true;
            if (local != null)
            {
                if (object.ReferenceEquals(local, vanilla))
                {
                    localAllowed = vanillaAllowed;
                    localApiAvailable = vanillaApiAvailable;
                }
                else
                {
                    localAllowed =
                        HtcCompatibility.IsBathroomAllowedForCharacter(
                            character,
                            local,
                            out localApiAvailable);
                }
            }

            bool fallbackApiAvailable = false;
            if ((local == null || !localAllowed) &&
                (vanillaApiAvailable || localApiAvailable))
            {
                TileObject allowedFallback =
                    HtcCompatibility.FindAllowedBathroomForCharacter(
                        character,
                        procedure,
                        accessRights,
                        out fallbackApiAvailable);

                if (fallbackApiAvailable && allowedFallback != null)
                {
                    local = allowedFallback;
                    localAllowed = true;
                }
            }

            bool htcApiAvailable =
                vanillaApiAvailable || localApiAvailable;

            if (local == null)
            {
                if (!vanillaAllowed)
                {
                    LogToiletChoice(
                        fallbackApiAvailable
                            ? "defer-htc-no-allowed-fallback"
                            : "defer-htc-private-fallback-api-unavailable",
                        character,
                        accessRights,
                        vanilla,
                        null,
                        vanillaAllowed,
                        localAllowed,
                        htcApiAvailable);
                }

                return vanilla;
            }

            if (!localAllowed)
            {
                LogToiletChoice(
                    vanillaAllowed
                        ? "keep-vanilla-htc-rejected-local"
                        : fallbackApiAvailable
                            ? "defer-htc-no-allowed-fallback"
                            : "defer-htc-private-fallback-api-unavailable",
                    character,
                    accessRights,
                    vanilla,
                    local,
                    vanillaAllowed,
                    localAllowed,
                    htcApiAvailable);
                return vanilla;
            }

            if (local == vanilla)
            {
                if (!vanillaAllowed)
                {
                    LogToiletChoice(
                        fallbackApiAvailable
                            ? "defer-htc-no-allowed-fallback"
                            : "defer-htc-private-fallback-api-unavailable",
                        character,
                        accessRights,
                        vanilla,
                        local,
                        vanillaAllowed,
                        localAllowed,
                        htcApiAvailable);
                }

                return vanilla;
            }

            int localDistance =
                GetComparableRouteDistance(
                    walk,
                    local,
                    accessRights);
            if (localDistance < 0)
            {
                LogToiletChoice(
                    "keep-vanilla-local-unreachable",
                    character,
                    accessRights,
                    vanilla,
                    local,
                    vanillaAllowed,
                    localAllowed,
                    htcApiAvailable);
                return vanilla;
            }

            if (!vanillaAllowed)
            {
                LogToiletChoice(
                    "replace-htc-private-vanilla",
                    character,
                    accessRights,
                    vanilla,
                    local,
                    vanillaAllowed,
                    localAllowed,
                    htcApiAvailable);
                return local;
            }

            if (vanilla.GetFloorIndex() != walk.GetFloorIndex())
            {
                LogToiletChoice(
                    "avoid-floor-change",
                    character,
                    accessRights,
                    vanilla,
                    local,
                    vanillaAllowed,
                    localAllowed,
                    htcApiAvailable);
                return local;
            }

            int vanillaDistance =
                GetComparableRouteDistance(
                    walk,
                    vanilla,
                    accessRights);

            if (vanillaDistance < 0 || localDistance < vanillaDistance)
            {
                LogToiletChoice(
                    "closer-local",
                    character,
                    accessRights,
                    vanilla,
                    local,
                    vanillaAllowed,
                    localAllowed,
                    htcApiAvailable);
                return local;
            }

            LogToiletChoice(
                "keep-vanilla-same-floor",
                character,
                accessRights,
                vanilla,
                local,
                vanillaAllowed,
                localAllowed,
                htcApiAvailable);
            return vanilla;
        }

        private static TileObject SelectMeal(
            GameDBProcedure procedure,
            Entity character,
            WalkComponent walk,
            AccessRights accessRights,
            ProcedureSceneType procedureSceneType,
            TileObject vanilla)
        {
            RequiredEquipment required = GetPrimaryEquipment(procedure);
            if (required == null)
            {
                return vanilla;
            }

            if (walk == null)
            {
                return vanilla;
            }

            TileObject local = FindClosestSameFloor(
                walk,
                required.Tag,
                accessRights,
                procedure.RequiredRoomTags,
                null,
                null);

            int roll = -1;
            if (local != null)
            {
                roll = GetStableMealRoll(character, procedure);
                // No cafeteria chance exceeds 75%. With a valid local
                // candidate, this decision cannot be changed by the
                // cafeteria search or by route-distance calculations.
                if (roll >= 75 &&
                    !FlowOptimizerConfig.StaffNeedsDiagnosticLogging)
                {
                    return local;
                }
            }

            TileObject cafeteria = FindVanillaCafeteriaCandidate(
                walk,
                required.Tag,
                accessRights,
                procedure);

            if (local == null)
            {
                return cafeteria ?? vanilla;
            }

            if (cafeteria == null || cafeteria == local)
            {
                return local;
            }

            int localDistance = -1;
            int cafeteriaDistance = -1;
            if (walk.GetFloorIndex() == cafeteria.GetFloorIndex())
            {
                localDistance = GetRouteDistance(walk, local, accessRights);
                cafeteriaDistance = GetRouteDistance(walk, cafeteria, accessRights);
            }
            int chance = GetCafeteriaChancePercent(
                walk.GetFloorIndex(),
                cafeteria.GetFloorIndex(),
                localDistance,
                cafeteriaDistance);

            // QUERY and INSTANTIATION must resolve to the same resource.
            // A second random roll here can validate one destination and then
            // execute another a few frames later.
            TileObject selected = roll < chance ? cafeteria : local;

            if (FlowOptimizerConfig.StaffNeedsDiagnosticLogging)
            {
                Plugin.Log.LogInfo(
                    "[Diagnostic][StaffMealChoice] phase=" +
                    procedureSceneType +
                    "; local=" +
                    Describe(local, character, accessRights) +
                    "; cafeteria=" +
                    Describe(cafeteria, character, accessRights) +
                    "; cafeteriaChance=" + chance +
                    "; roll=" + roll +
                    "; selectedType=" +
                    (selected == cafeteria ? "CAFETERIA" : "LOCAL") +
                    "; selected=" +
                    Describe(selected, character, accessRights));
            }

            return selected;
        }

        private static TileObject FindClosestSameFloor(
            WalkComponent walk,
            string tag,
            AccessRights accessRights,
            string[] roomTags,
            Department preferredDepartment,
            string ignoreDepartmentForRoomTag)
        {
            if (walk == null)
            {
                return null;
            }

            TileObject result =
                MapScriptInterface.Instance.FindClosestCenterObjectWithTagShortestPath(
                    walk.GetCurrentTile(),
                    walk.GetFloorIndex(),
                    tag,
                    accessRights,
                    roomTags,
                    false,
                    true,
                    preferredDepartment,
                    ignoreDepartmentForRoomTag);

            if (result == null && accessRights == AccessRights.STAFF_ONLY)
            {
                result =
                    MapScriptInterface.Instance.FindClosestCenterObjectWithTagShortestPath(
                        walk.GetCurrentTile(),
                        walk.GetFloorIndex(),
                        tag,
                        AccessRights.STAFF,
                        roomTags,
                        false,
                        true,
                        preferredDepartment,
                        ignoreDepartmentForRoomTag);
            }

            return result;
        }

        private static TileObject FindVanillaCafeteriaCandidate(
            WalkComponent walk,
            string tag,
            AccessRights accessRights,
            GameDBProcedure procedure)
        {
            string[] cafeteriaRoomTags =
                object.ReferenceEquals(procedure, StaffLunchProcedure)
                    ? StaffLunchCafeteriaRoomTags
                    : StaffHungerCafeteriaRoomTags;

            if (walk == null ||
                cafeteriaRoomTags == null ||
                !CafeteriaDlcAvailable ||
                AdministrativeDepartmentType == null)
            {
                return null;
            }

            // The department instance remains dynamic and is resolved afresh.
            Department administrative = MapScriptInterface.Instance.GetDepartmentOfType(
                AdministrativeDepartmentType);
            if (administrative == null)
            {
                return null;
            }

            return MapScriptInterface.Instance.FindClosestObjectWithTag(
                walk.GetCurrentTile(),
                walk.GetFloorIndex(),
                administrative,
                tag,
                accessRights,
                cafeteriaRoomTags);
        }

        private static int GetStableMealRoll(
            Entity character,
            GameDBProcedure procedure)
        {
            unchecked
            {
                uint hash = 2166136261u;
                MixMealRollHash(
                    ref hash,
                    character == null
                        ? 0u
                        : character.GetEntityID());
                MixMealRollHash(
                    ref hash,
                    GetStableMealProcedureKey(procedure));
                MixMealRollHash(
                    ref hash,
                    DayTime.Instance == null
                        ? 0u
                        : (uint)DayTime.Instance.GetDay());

                return (int)(hash % 100u);
            }
        }

        private static string GetStableMealProcedureKey(
            GameDBProcedure procedure)
        {
            // NEED_HUNGER_STAFF owns an embedded GameDBProcedure without its
            // own DatabaseID. Use the semantic owner key so QUERY and
            // INSTANTIATION keep the same deterministic roll without
            // dereferencing a null ID.
            if (object.ReferenceEquals(
                    procedure,
                    StaffLunchProcedure))
            {
                return StaffLunchProcedureId;
            }

            if (object.ReferenceEquals(
                    procedure,
                    StaffHungerProcedure))
            {
                return StaffHungerNeedId;
            }

            if (procedure == null ||
                ID.IsNullOrNoID(procedure.DatabaseID))
            {
                return string.Empty;
            }

            return procedure.DatabaseID.ToString();
        }

        private static void MixMealRollHash(
            ref uint hash,
            uint value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 16777619u;
            }
        }

        private static void MixMealRollHash(
            ref uint hash,
            string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                MixMealRollHash(ref hash, 0u);
                return;
            }

            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }
            }
        }

        private static int GetCafeteriaChancePercent(
            int originFloor,
            int cafeteriaFloor,
            int localDistance,
            int cafeteriaDistance)
        {
            int chance;

            if (originFloor != cafeteriaFloor)
            {
                int floorDifference = Math.Abs(cafeteriaFloor - originFloor);
                chance = 12;
                for (int i = 1; i < floorDifference; i++)
                {
                    chance /= 2;
                }

                return Math.Max(3, chance);
            }

            if (localDistance < 0 || cafeteriaDistance < 0)
            {
                return 20;
            }

            if (cafeteriaDistance <= localDistance + 4)
            {
                return 75;
            }

            if (cafeteriaDistance <= localDistance + 12)
            {
                return 50;
            }

            if (cafeteriaDistance <= localDistance + 30)
            {
                return 25;
            }

            return 8;
        }

        private static int GetRouteDistance(
            Entity character,
            TileObject target,
            AccessRights accessRights)
        {
            return GetRouteDistance(
                character == null ? null : character.GetComponent<WalkComponent>(),
                target,
                accessRights);
        }

        private static int GetRouteDistance(
            WalkComponent walk,
            TileObject target,
            AccessRights accessRights)
        {
            if (walk == null || target == null)
            {
                return -1;
            }

            if (walk.GetFloorIndex() != target.GetFloorIndex())
            {
                return -1;
            }

            return (int)GridMap.GetInstance().GetDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                target.GetFloorIndex(),
                target.m_state.m_position,
                accessRights);
        }

        private static int GetComparableRouteDistance(
            Entity character,
            TileObject target,
            AccessRights accessRights)
        {
            return GetComparableRouteDistance(
                character == null ? null : character.GetComponent<WalkComponent>(),
                target,
                accessRights);
        }

        private static int GetComparableRouteDistance(
            WalkComponent walk,
            TileObject target,
            AccessRights accessRights)
        {
            int distance = GetRouteDistance(walk, target, accessRights);

            if (distance < 0 && accessRights == AccessRights.STAFF_ONLY)
            {
                distance = GetRouteDistance(
                    walk,
                    target,
                    AccessRights.STAFF);
            }

            return distance;
        }

        private static void LogToiletChoice(
            string reason,
            Entity character,
            AccessRights accessRights,
            TileObject vanilla,
            TileObject local,
            bool vanillaHtcAllowed,
            bool localHtcAllowed,
            bool htcApiAvailable)
        {
            if (!FlowOptimizerConfig.StaffNeedsDiagnosticLogging ||
                Plugin.Log == null)
            {
                return;
            }

            int vanillaDistance =
                GetComparableRouteDistance(
                    character,
                    vanilla,
                    accessRights);
            int localDistance =
                GetComparableRouteDistance(
                    character,
                    local,
                    accessRights);

            Plugin.Log.LogInfo(
                "[Diagnostic][StaffToiletChoice] reason=" + reason +
                "; htcApiAvailable=" + htcApiAvailable +
                "; vanilla=" +
                    Describe(vanilla, character, accessRights) +
                "; vanillaComparableDistance=" + vanillaDistance +
                "; vanillaHtcAllowed=" + vanillaHtcAllowed +
                "; local=" +
                    Describe(local, character, accessRights) +
                "; localComparableDistance=" + localDistance +
                "; localHtcAllowed=" + localHtcAllowed);
        }

        private static RequiredEquipment GetPrimaryEquipment(GameDBProcedure procedure)
        {
            if (procedure == null ||
                procedure.RequiredEquipment == null ||
                procedure.RequiredEquipment.Length == 0)
            {
                return null;
            }

            return procedure.RequiredEquipment[0];
        }

        private static string Describe(
            TileObject target,
            Entity character,
            AccessRights accessRights)
        {
            if (target == null)
            {
                return "none";
            }

            int distance = GetRouteDistance(character, target, accessRights);
            return target.m_state.m_gameDBObject.Entry.DatabaseID +
                   "@F" + target.GetFloorIndex().ToString(CultureInfo.InvariantCulture) +
                   ":distance=" + distance.ToString(CultureInfo.InvariantCulture);
        }

        private enum ResourceKind
        {
            None,
            Toilet,
            Meal
        }
    }
}
