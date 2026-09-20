using System;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindDirtiestTileInRoomWithMatchingAssignmentAnyFloor),
        new Type[] { typeof(BehaviorJanitor), typeof(Department), typeof(int) })]
    internal static class JanitorAssignedRoomSelectionPatch
    {
        private static bool Prefix(
            BehaviorJanitor behaviorJanitor,
            Department department,
            int threshold,
            ref Vector3i __result)
        {
            if (!TrafficControlConfig.AvoidCleaningActiveProcedureRooms &&
                !TrafficControlConfig.ReduceOccupiedHospitalizationCleaningAtNight)
            {
                return true;
            }

            __result = JanitorCleaningManager.FindDirtiestTileInRoomWithMatchingAssignmentAnyFloor(
                behaviorJanitor,
                department,
                threshold);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindDirtiestTileInAnyUnreservedRoomAnyFloor),
        new Type[] { typeof(Department), typeof(int) })]
    internal static class JanitorAnyRoomSelectionPatch
    {
        private static bool Prefix(
            Department department,
            int threshold,
            ref Vector3i __result)
        {
            if (!TrafficControlConfig.AvoidCleaningActiveProcedureRooms &&
                !TrafficControlConfig.ReduceOccupiedHospitalizationCleaningAtNight)
            {
                return true;
            }

            __result = JanitorCleaningManager.FindDirtiestTileInAnyUnreservedRoomAnyFloor(
                department,
                threshold);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindClosestDirtyIndoorsTile),
        new Type[] { typeof(Vector2i), typeof(int), typeof(int) })]
    internal static class JanitorIndoorTileSelectionPatch
    {
        private static bool Prefix(
            Vector2i position,
            int floorIndex,
            int threshold,
            ref Vector2i __result)
        {
            if (!TrafficControlConfig.AvoidCleaningActiveProcedureRooms &&
                !TrafficControlConfig.ReduceOccupiedHospitalizationCleaningAtNight)
            {
                return true;
            }

            __result = JanitorCleaningManager.FindClosestDirtyIndoorsTile(
                position,
                floorIndex,
                threshold);
            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorJanitor), "UpdateStateCleaning", new Type[] { typeof(float) })]
    internal static class JanitorActiveProcedureCleaningStatePatch
    {
        private static bool Prefix(BehaviorJanitor __instance)
        {
            return !JanitorCleaningManager.TryInterruptActiveProcedureRoom(__instance);
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindDirtiestTileInARoom),
        new Type[] { typeof(Room), typeof(bool), typeof(int) })]
    internal static class JanitorCurrentRoomNightPriorityPatch
    {
        private static void Postfix(Room room, ref Vector2i __result)
        {
            if (__result == Vector2i.ZERO_VECTOR ||
                !JanitorCleaningManager.ShouldDeferOrdinaryCleaning(room))
            {
                return;
            }

            Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];
            if (MapScriptInterface.Instance.GetDirtType(__result, floor.m_floorIndex) != DirtType.BLOOD)
            {
                __result = Vector2i.ZERO_VECTOR;
            }
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindClosestDirtyTileInARoom),
        new Type[] { typeof(Room), typeof(Vector2i) })]
    internal static class JanitorClosestRoomNightPriorityPatch
    {
        private static void Postfix(Room room, ref Vector2i __result)
        {
            if (__result == Vector2i.ZERO_VECTOR ||
                !JanitorCleaningManager.ShouldDeferOrdinaryCleaning(room))
            {
                return;
            }

            Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];
            if (MapScriptInterface.Instance.GetDirtType(__result, floor.m_floorIndex) != DirtType.BLOOD)
            {
                __result = Vector2i.ZERO_VECTOR;
            }
        }
    }
}
