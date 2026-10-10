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
                !TrafficControlConfig.AvoidCleaningOccupiedBathrooms &&
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
                !TrafficControlConfig.AvoidCleaningOccupiedBathrooms &&
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
                !TrafficControlConfig.AvoidCleaningOccupiedBathrooms &&
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


    // Persist only the per-janitor waiting decision and cooldown, not
    // the transient exterior route or pathfinder job.
    [HarmonyPatch]
    internal static class JanitorWaitSavePatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Constructor(
                typeof(GameSave), new Type[] { typeof(Hospital) });
        }

        private static void Postfix(GameSave __instance)
        {
            JanitorCleaningManager.AppendWaitStateToSave(__instance);
        }
    }

    [HarmonyPatch(
        typeof(LopitalEntityFactory),
        nameof(LopitalEntityFactory.LoadEntity),
        new Type[] { typeof(EntitySave), typeof(Floor) })]
    internal static class JanitorWaitLoadPatch
    {
        private static bool Prefix(EntitySave entitySave, ref Entity __result)
        {
            if (!JanitorCleaningManager.TryRestoreWaitStateMarker(entitySave))
            {
                return true;
            }
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(InGameMenuController),
        nameof(InGameMenuController.Load),
        new Type[] { typeof(string), typeof(string) })]
    internal static class JanitorWaitLoadResetPatch
    {
        private static void Prefix()
        {
            JanitorCleaningManager.Reset();
        }
    }

    [HarmonyPatch(typeof(BehaviorJanitor), "FreeRoom")]
    internal static class JanitorPreserveForeignRoomReservationPatch
    {
        private static bool Prefix(BehaviorJanitor __instance)
        {
            return !JanitorCleaningManager
                .TryPreserveOtherJanitorRoomReservation(__instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorJanitor),
        nameof(BehaviorJanitor.Update),
        new Type[] { typeof(float) })]
    internal static class JanitorCartRouteDiagnosticPatch
    {
        private static void Postfix(BehaviorJanitor __instance)
        {
            JanitorCleaningManager.LogCartRouteIfChanged(__instance);
            JanitorCleaningManager.LogWcCleaningProgress(__instance);
            JanitorCleaningManager.LogJanitorStationaryIfNeeded(__instance);
            JanitorCleaningManager.PruneProtectedRoomApproach(__instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorJanitor), "UpdateStateCleaning", new Type[] { typeof(float) })]
    internal static class JanitorProtectedCleaningStatePatch
    {
        private static bool Prefix(BehaviorJanitor __instance)
        {
            return !JanitorCleaningManager.TryInterruptProtectedCleaningRoom(__instance);
        }
    }

    // SelectNextAction() first asks vanilla for a free room in the janitor's
    // department, then calls TryToSelectIndoorTile(10), whose vanilla search is
    // floor-wide and may therefore jump to another department. Give an occupied
    // clinical room in the janitor's own department the chance to become an HTC
    // exterior wait before that cross-department fallback. Keep the separate
    // TryToSelectIndoorTile(50) idle recovery path vanilla-first.
    [HarmonyPatch(typeof(BehaviorJanitor), "TryToSelectIndoorTile")]
    internal static class JanitorOccupiedRoomWaitSelectionPatch
    {
        private static bool Prefix(
            BehaviorJanitor __instance,
            int threshold,
            ref bool __result)
        {
            if (threshold == 10 &&
                JanitorCleaningManager.TryOfferOccupiedClinicalRoomWait(
                    __instance))
            {
                __result = true;
                return false;
            }

            return true;
        }

        private static void Postfix(
            BehaviorJanitor __instance,
            int threshold,
            ref bool __result)
        {
            if (threshold != 10 && !__result)
            {
                __result =
                    JanitorCleaningManager.TryOfferOccupiedClinicalRoomWait(
                        __instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(BehaviorJanitor),
        "TryToSelectTileInCurrentRoom")]
    internal static class JanitorCurrentRoomSelectionPatch
    {
        private static bool Prefix(
            BehaviorJanitor __instance,
            ref bool __result)
        {
            if (!JanitorCleaningManager.TrySkipProtectedCurrentRoomSelection(
                    __instance))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(BehaviorJanitor),
        "UpdateStateWalking",
        new Type[] { typeof(float) })]
    internal static class JanitorProtectedTileTravelPatch
    {
        private static bool Prefix(BehaviorJanitor __instance)
        {
            return !JanitorCleaningManager.TryInterruptProtectedTileTravel(
                __instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorJanitor), "UpdateStateWalkingToCartToNextRoom")]
    internal static class JanitorProtectedRoomPreTravelPatch
    {
        private static bool Prefix(BehaviorJanitor __instance)
        {
            return !JanitorCleaningManager.TryInterruptProtectedRoomBeforeTravel(
                __instance);
        }

        private static void Postfix(BehaviorJanitor __instance)
        {
            JanitorCleaningManager.StagePendingOccupiedWaitImmediately(
                __instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorJanitor), "UpdateStateWalkingToNextRoom")]
    internal static class JanitorProtectedRoomTravelPatch
    {
        private static bool Prefix(BehaviorJanitor __instance)
        {
            return !JanitorCleaningManager.TryHandleProtectedRoomTravel(
                __instance);
        }
    }

    [HarmonyPatch(typeof(BehaviorJanitor), nameof(BehaviorJanitor.SwitchState))]
    internal static class JanitorBathroomCleaningDiagnosticPatch
    {
        private static void Postfix(
            BehaviorJanitor __instance,
            BehaviorJanitorState state)
        {
            if (state != BehaviorJanitorState.Cleaning)
            {
                return;
            }

            // SwitchState(Cleaning) itself performs no cleaning in vanilla. Recheck
            // immediately so an occupied WC selected just before this transition is
            // abandoned before the first UpdateStateCleaning() tick.
            if (JanitorCleaningManager.TryInterruptProtectedCleaningRoom(__instance))
            {
                return;
            }

            JanitorCleaningManager.LogClinicalCleaningStart(__instance);
            JanitorCleaningManager.LogBathroomCleaningStart(__instance);
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindClosest3x3Area),
        new Type[] { typeof(Vector2i), typeof(Room) })]
    internal static class JanitorBathroomCartDestinationPatch
    {
        private static void Postfix(
            Vector2i position,
            Room room,
            ref Vector2i __result)
        {
            __result = JanitorCleaningManager.FilterBathroomCartDestination(
                position,
                room,
                __result);
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindDirtiestTileInARoom),
        new Type[] { typeof(Room), typeof(bool), typeof(int) })]
    internal static class JanitorCurrentRoomNightPriorityPatch
    {
        private static void Postfix(
            Room room,
            bool bloodOnly,
            int threshold,
            ref Vector2i __result)
        {
            __result = JanitorCleaningManager.FilterDirtiestBathroomTile(
                room,
                bloodOnly,
                threshold,
                __result);

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
        private static void Postfix(
            Room room,
            Vector2i characterPosition,
            ref Vector2i __result)
        {
            __result = JanitorCleaningManager.FilterClosestBathroomTile(
                room,
                characterPosition,
                __result);

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
