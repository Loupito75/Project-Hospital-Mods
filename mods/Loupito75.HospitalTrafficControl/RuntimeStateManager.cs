using HospitalTrafficControl.Patches;

namespace HospitalTrafficControl
{
    internal static class RuntimeStateManager
    {
        internal static void Reset()
        {
            OneWayIndicatorRenderer.Reset();
            PathfindingDebugMarkerRenderer.Reset();
            DoorDebugManager.Reset();

            BiohazardEndpointAccessTracker.Reset();
            AccessZoneRecoveryManager.Reset();
            OneWayPathfinderTracker.Reset();
            OneWayWalkValidationContext.Reset();
            PathfindingDebugManager.Reset();
            WaitingRoomDiagnostics.Reset();

            OneWayPersistenceManager.Reset();
            OneWayManager.Reset();
            OneWayRouteManager.Reset();
            BlockedRouteManager.Reset();
            CrossFloorBlockedManager.Reset();
            NavigationChangeTracker.Reset();

            CrossFloorVisitorRecovery.Reset();
            StatLabMeetingExitManager.Reset();
            JanitorCleaningManager.Reset();
            BathroomFixtureHandoff.Reset();
            SingleToiletBathroomLockManager.Reset();
            BathroomFlowDiagnostics.Reset();
            BathroomAvailabilityAudit.Reset();
        }
    }
}
