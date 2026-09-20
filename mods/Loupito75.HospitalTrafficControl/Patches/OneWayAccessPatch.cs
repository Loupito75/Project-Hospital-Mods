using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    internal static class OneWayWalkValidationContext
    {
        [System.ThreadStatic]
        private static int s_depth;

        internal static bool IsActive
        {
            get { return s_depth > 0; }
        }

        internal static void Begin()
        {
            s_depth++;
        }

        internal static void End()
        {
            if (s_depth > 0)
            {
                s_depth--;
            }
        }

        internal static void Reset()
        {
            s_depth = 0;
        }
    }

    // WalkComponent.UpdateMovement validates an already-built route by calling
    // Floor.IsAccessible(nextRouteNode, previousRouteNode, ...). This is the same
    // reverse-edge convention used by the backwards PathfinderJob search. Mark only
    // that native movement-validation scope so unrelated callers such as MapRenderer
    // are not interpreted as character movement.
    [HarmonyPatch(
        typeof(WalkComponent),
        "UpdateMovement",
        new System.Type[] { typeof(Floor), typeof(float) })]
    internal static class OneWayWalkValidationContextPatch
    {
        private static void Prefix()
        {
            OneWayWalkValidationContext.Begin();
        }

        private static void Postfix()
        {
            OneWayWalkValidationContext.End();
        }
    }

    [HarmonyPatch(
        typeof(Floor),
        nameof(Floor.IsAccessible),
        new System.Type[]
        {
            typeof(Vector2i), typeof(Vector2i), typeof(Vector2i), typeof(Vector2i),
            typeof(int), typeof(bool), typeof(bool)
        })]
    internal static class OneWayAccessPatch
    {
        private const string NoFloorId = "FLOOR_TYPE_NONE";

        private static void Prefix(
            Floor __instance,
            Vector2i currentPosition,
            Vector2i nextPosition,
            Vector2i startPosition,
            Vector2i targetPosition,
            ref int accessRightsLevel,
            bool ignoreObjects,
            bool ignoreAccessRights)
        {
            AccessZoneRecoveryTracker.AdjustAccessRights(
                __instance,
                currentPosition,
                ref accessRightsLevel);

            BiohazardEndpointAccessTracker.AdjustAccessRightsForEndpointBiohazardRoom(
                __instance,
                currentPosition,
                startPosition,
                targetPosition,
                ref accessRightsLevel);
        }

        private static void Postfix(
            Floor __instance,
            Vector2i currentPosition,
            Vector2i nextPosition,
            Vector2i startPosition,
            Vector2i targetPosition,
            int accessRightsLevel,
            bool ignoreObjects,
            bool ignoreAccessRights,
            ref bool __result)
        {
            if (!__result)
            {
                if (TrafficControlConfig.PathfindingDebug)
                {
                    PathfindingDebugManager.RecordDeniedTransition(
                        __instance,
                        currentPosition,
                        nextPosition,
                        startPosition,
                        targetPosition,
                        accessRightsLevel,
                        ignoreObjects,
                        ignoreAccessRights,
                        "vanilla");
                }

                return;
            }

            if (AccessZoneRecoveryTracker.ShouldDenyReentry(
                    __instance,
                    currentPosition,
                    nextPosition))
            {
                if (TrafficControlConfig.PathfindingDebug)
                {
                    PathfindingDebugManager.RecordDeniedTransition(
                        __instance,
                        currentPosition,
                        nextPosition,
                        startPosition,
                        targetPosition,
                        accessRightsLevel,
                        ignoreObjects,
                        ignoreAccessRights,
                        "access-recovery-reentry");
                }

                __result = false;
                return;
            }

            bool pathfinderSearch =
                !object.ReferenceEquals(OneWayPathfinderTracker.CurrentJob, null);
            bool walkValidation = OneWayWalkValidationContext.IsActive;

            // Floor.IsAccessible is also used by non-navigation systems such as
            // MapRenderer when it evaluates object access positions. OneWay and the
            // no-floor safeguard below are needed only in the two confirmed native
            // character-navigation contexts.
            if (!pathfinderSearch && !walkValidation)
            {
                return;
            }

            // Vanilla intends FLOOR_TYPE_NONE to be inaccessible. During HTC testing,
            // accepted routes were observed on tiles whose stable database ID was
            // FLOOR_TYPE_NONE. The reason the native comparison missed those tiles was
            // not proven, so preserve the native rule defensively only in the confirmed
            // character-navigation contexts instead of changing unrelated callers.
            if (IsNoFloor(__instance, nextPosition))
            {
                if (TrafficControlConfig.PathfindingDebug)
                {
                    PathfindingDebugManager.RecordDeniedTransition(
                        __instance,
                        currentPosition,
                        nextPosition,
                        startPosition,
                        targetPosition,
                        accessRightsLevel,
                        ignoreObjects,
                        ignoreAccessRights,
                        "native-no-floor");
                }

                __result = false;
                return;
            }

            // PathfinderJob searches from destination back to the character, and
            // WalkComponent.UpdateMovement validates route[i] -> route[i - 1]. In
            // both contexts the arguments are reversed relative to real movement.
            Vector2i oneWayFrom = nextPosition;
            Vector2i oneWayTo = currentPosition;

            if (!OneWayManager.IsTransitionAllowed(__instance, oneWayFrom, oneWayTo))
            {
                if (TrafficControlConfig.PathfindingDebug)
                {
                    PathfindingDebugManager.RecordDeniedTransition(
                        __instance,
                        currentPosition,
                        nextPosition,
                        startPosition,
                        targetPosition,
                        accessRightsLevel,
                        ignoreObjects,
                        ignoreAccessRights,
                        "one-way");
                }

                OneWayPathfinderTracker.RecordDenied(
                    __instance,
                    oneWayFrom,
                    oneWayTo);
                __result = false;
            }
        }

        private static bool IsNoFloor(Floor floor, Vector2i position)
        {
            if (floor == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_tiles == null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= floor.Size.m_x ||
                position.m_y >= floor.Size.m_y)
            {
                return false;
            }

            Tile tile = floor.m_mapPersistentData.m_tiles[position.m_x, position.m_y];
            GameDBFloorType floorType = tile == null ? null : tile.FloorType;
            if (floorType == null || ID.IsNullOrNoID(floorType.DatabaseID))
            {
                return false;
            }

            return floorType.DatabaseID.ToString() == NoFloorId;
        }
    }

    [HarmonyPatch(typeof(Door), nameof(Door.Destroy))]
    internal static class OneWayDoorDestroyedPatch
    {
        private static void Prefix(Door __instance)
        {
            OneWayManager.Remove(__instance);
        }
    }

    [HarmonyPatch(typeof(MapEditorController), nameof(MapEditorController.Destroy))]
    internal static class OneWayMapDestroyedPatch
    {
        private static void Prefix()
        {
            OneWayIndicatorRenderer.Reset();
            PathfindingDebugMarkerRenderer.Reset();
            DoorDebugManager.Reset();
            BiohazardEndpointAccessTracker.Reset();
            AccessZoneRecoveryManager.Reset();
            OneWayPathfinderTracker.Reset();
            OneWayWalkValidationContext.Reset();
            PathfindingDebugManager.Reset();
            OneWayPersistenceManager.Reset();
            OneWayManager.Reset();
            OneWayRouteManager.Reset();
            BlockedRouteManager.Reset();
            NavigationChangeTracker.Reset();
        }
    }
}
