using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
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
        private static void Prefix(Floor __instance)
        {
            NavigationChangeTracker.BeginNavigationRebuild(__instance);
            NavigationChangeTracker.PrepareRoomAccessForNativeRebuild(__instance);
        }

        private static void Postfix(Floor __instance)
        {
            bool roomAccessChangedDuringRebuild;
            AccessChangeSet accessChange =
                NavigationChangeTracker.CompleteNavigationRebuild(
                    __instance,
                    out roomAccessChangedDuringRebuild);

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

    [HarmonyPatch(typeof(WalkComponent), nameof(WalkComponent.SwitchState))]
    internal static class AccessZoneRecoveryStatePatch
    {
        private static void Postfix(WalkComponent __instance, WalkState state)
        {
            AccessZoneRecoveryManager.OnWalkStateChanged(__instance, state);
        }
    }
}
