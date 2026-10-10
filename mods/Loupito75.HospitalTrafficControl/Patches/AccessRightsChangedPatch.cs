using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    // Editor painting calls FillAccessRights() followed immediately by the
    // native full navigation rebuild within UpdateDragging().
    [HarmonyPatch(typeof(MapEditorUIController), "UpdateDragging")]
    internal static class EditorAccessRightsDragPatch
    {
        private static int s_activeDepth;

        internal static bool IsActive
        {
            get { return s_activeDepth > 0; }
        }

        private static void Prefix(out bool __state)
        {
            __state =
                MapEditorController.sm_instance != null &&
                MapEditorController.sm_instance.m_mapEditorState ==
                    MapEditorState.SettingAccessRights;

            if (__state)
            {
                s_activeDepth++;
            }
        }

        private static void Postfix(bool __state)
        {
            if (__state && s_activeDepth > 0)
            {
                s_activeDepth--;
            }
        }
    }

    [HarmonyPatch(typeof(MapEditorController), "FillAccessRights")]
    internal static class FillAccessRightsPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            Floor floor = Hospital.Instance?.GetCurrentFloor();
            if (floor != null)
            {
                NavigationChangeTracker.BeginExternalAccessMutation(floor);
            }
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            Floor floor = Hospital.Instance?.GetCurrentFloor();
            if (floor == null)
            {
                return;
            }

            // The editor will rebuild all graphs after this call. Retain
            // the pre-paint snapshot for NavigationRebuiltPatch instead.
            if (EditorAccessRightsDragPatch.IsActive)
            {
                return;
            }

            AccessChangeSet accessChange =
                NavigationChangeTracker.CompleteExternalAccessMutation(floor);
            if (accessChange == null)
            {
                return;
            }

            if (TrafficControlConfig.PathfindingDebug)
            {
                PathfindingDebugMarkerRenderer.ClearMarkersForFloor(
                    floor.m_floorIndex);
            }

            // FillAccessRights() changes only the logistics matrix and does not
            // trigger a native static-navigation rebuild. Refresh only the graph
            // access levels whose passability changed before any recovery/repath.
            Plugin.EnsureDeferredGridMapPatches(floor);
            int rebuiltGraphs =
                ExactAccessGraphManager.RecalculateAffectedGraphs(
                    GridMap.GetInstance(),
                    floor,
                    accessChange);

            if (rebuiltGraphs < 0)
            {
                // Safe compatibility fallback for changed private graph internals.
                floor.UpdateStaticNavigationData();
            }

            // FillAccessRights() changes the logistics layer directly and vanilla
            // does not call Floor.UpdateStaticNavigationData() from this method.
            // Complete the access mutation here so characters already standing on
            // newly forbidden tiles can receive HTC's bounded temporary-exit recovery.
            AccessZoneRecoveryManager.HandleAccessRightsChanged(
                floor,
                accessChange);

            CrossFloorBlockedManager.RetryForNavigationFloor(floor);
            BlockedRouteManager.RepathFloor(floor);
        }
    }

    [HarmonyPatch(typeof(UndoManager), nameof(UndoManager.RecallSnapshot))]
    internal static class UndoAccessMutationPatch
    {
        private static void Prefix()
        {
            Floor floor = Hospital.Instance?.GetCurrentFloor();
            if (floor != null)
            {
                NavigationChangeTracker.BeginExternalAccessMutation(floor);
            }
        }
    }

    [HarmonyPatch(typeof(Floor), nameof(Floor.UpdateStaticNavigationData))]
    internal static class NavigationRebuiltPatch
    {
        private static void Prefix(Floor __instance, out long __state)
        {
            __state = GraphPerformanceDiagnostics.BeginSample();
            NavigationChangeTracker.BeginNavigationRebuild(__instance);
            NavigationChangeTracker.PrepareRoomAccessForNativeRebuild(__instance);
        }

        private static void Postfix(Floor __instance, long __state)
        {
            // Floor original plus its GridMap postfix, before recovery and repath.
            GraphPerformanceDiagnostics.RecordRebuild(
                __state,
                "FLOOR",
                __instance.m_floorIndex,
                0);

            bool roomAccessChangedDuringRebuild;
            AccessChangeSet accessChange =
                NavigationChangeTracker.CompleteNavigationRebuild(
                    __instance,
                    out roomAccessChangedDuringRebuild);

            // Vanilla has already called GridMap.GetInstance().Recalculate(...)
            // inside UpdateStaticNavigationData() before this Postfix. This is the
            // first safe point to install HTC's direct GridMap hooks without forcing
            // GridMap's static constructor during BepInEx/database startup.
            Plugin.EnsureDeferredGridMapPatches(__instance);

            if (roomAccessChangedDuringRebuild &&
                TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[PathDebug] ACCESS_GRAPH_RESYNC floor=" +
                    __instance.m_floorIndex +
                    " result=prepared-before-native-recalculate.");
            }

            bool accessGraphChanged =
                accessChange != null || roomAccessChangedDuringRebuild;

            if (accessGraphChanged &&
                TrafficControlConfig.PathfindingDebug)
            {
                // Existing markers describe the graph that produced the previous
                // NoPath. An access-rights edit invalidates that diagnosis immediately.
                // If the replacement routes still fail, the normal NoPath flow will
                // publish fresh causal markers from the rebuilt graph.
                PathfindingDebugMarkerRenderer.ClearMarkersForFloor(
                    __instance.m_floorIndex);
            }

            if (accessChange != null)
            {
                WaitingRoomDiagnostics.LogInvalidWaitingRoomPatients(
                    __instance);

                AccessZoneRecoveryManager.HandleAccessRightsChanged(
                    __instance,
                    accessChange);
            }

            // Cross-floor retries must see the corrected GridMap when room access
            // changed during the native rebuild.
            CrossFloorBlockedManager.RetryForNavigationFloor(__instance);

            if (accessChange != null)
            {
                BlockedRouteManager.RepathFloor(__instance);
            }
        }
    }

    // Native bladder scripts consider NoPath equivalent to arrival because
    // WalkComponent.IsBusy() is false. Suppress only the "going to fixture"
    // transitions while HTC is routing the character out of the closed zone.
    [HarmonyPatch(typeof(ProcedureScriptNeedBladder), nameof(ProcedureScriptNeedBladder.ScriptUpdate))]
    internal static class BladderAccessRecoveryPatch
    {
        private static bool Prefix(ProcedureScriptNeedBladder __instance)
        {
            return !AccessZoneRecoveryManager.ShouldPauseBladderMovement(
                __instance);
        }
    }

    [HarmonyPatch]
    internal static class BladderAccessContinuationFixturePatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(MapScriptInterface),
                nameof(MapScriptInterface.FindClosestFreeObjectWithTag),
                new System.Type[]
                {
                    typeof(Entity),
                    typeof(Entity),
                    typeof(Vector2i),
                    typeof(Room),
                    typeof(string),
                    typeof(AccessRights),
                    typeof(bool),
                    typeof(DatabaseEntryRef<GameDBRoomType>[]),
                    typeof(bool)
                });
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Entity character,
            Entity owner,
            Room room,
            string tag,
            bool allowObjectsWithAttachments,
            ref TileObject __result)
        {
            if (__result != null)
            {
                return;
            }

            ProcedureScriptNeedBladder bladder =
                owner as ProcedureScriptNeedBladder;
            if (bladder == null)
            {
                return;
            }

            __result =
                AccessZoneRecoveryManager.FindGrandfatheredBladderFixture(
                    bladder,
                    character,
                    room,
                    tag,
                    allowObjectsWithAttachments);
        }
    }

    [HarmonyPatch(typeof(WalkComponent), nameof(WalkComponent.SetDestination), new System.Type[]
    {
        typeof(Vector2i),
        typeof(int),
        typeof(MovementType)
    })]
    internal static class AccessZoneRecoveryVector2iDestinationPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            WalkComponent __instance,
            Vector2i destinationTile,
            int floorIndex,
            MovementType movementType)
        {
            return !AccessZoneRecoveryManager.TryDeferDestinationDuringAccessExit(
                __instance,
                new Vector2f(destinationTile.m_x, destinationTile.m_y),
                floorIndex,
                movementType);
        }
    }

    [HarmonyPatch(typeof(WalkComponent), nameof(WalkComponent.SetDestination), new System.Type[]
    {
        typeof(Vector2f),
        typeof(int),
        typeof(MovementType)
    })]
    internal static class AccessZoneRecoveryVector2fDestinationPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            WalkComponent __instance,
            Vector2f destination,
            int floorIndex,
            MovementType movementType)
        {
            return !AccessZoneRecoveryManager.TryDeferDestinationDuringAccessExit(
                __instance,
                destination,
                floorIndex,
                movementType);
        }
    }

    [HarmonyPatch(typeof(WalkComponent), nameof(WalkComponent.GoSit), new System.Type[]
    {
        typeof(TileObject),
        typeof(MovementType)
    })]
    internal static class AccessZoneRecoveryGoSitPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            WalkComponent __instance,
            TileObject objectToSitOn,
            MovementType movementType)
        {
            return !AccessZoneRecoveryManager.TryDeferGoSitDuringAccessExit(
                __instance,
                objectToSitOn,
                movementType);
        }
    }

    [HarmonyPatch(typeof(WalkComponent), nameof(WalkComponent.SwitchState))]
    internal static class AccessZoneRecoveryStatePatch
    {
        private static void Postfix(WalkComponent __instance, WalkState state)
        {
            AccessZoneRecoveryManager.OnWalkStateChanged(__instance, state);
        }
    }
}
