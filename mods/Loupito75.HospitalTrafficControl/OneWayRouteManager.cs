using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class OneWayRouteManager
    {
        private static readonly HashSet<WalkComponent> PendingOneWayRepaths =
            new HashSet<WalkComponent>();

        private static readonly HashSet<WalkComponent> BlockedByOneWay =
            new HashSet<WalkComponent>();

        internal static void Reset()
        {
            PendingOneWayRepaths.Clear();
            BlockedByOneWay.Clear();
        }

        internal static void RegisterExternalOneWayBlock(WalkComponent walk)
        {
            if (walk != null)
            {
                BlockedByOneWay.Add(walk);
            }
        }

        internal static void OnRuleChanged(Door door)
        {
            if (door == null || Hospital.Instance == null)
            {
                return;
            }

            int floorIndex = door.m_state.m_floorIndex;
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return;
            }

            OnFloorRulesChanged(Hospital.Instance.m_floors[floorIndex]);
        }

        internal static void OnFloorRulesChanged(Floor floor)
        {
            if (floor == null || Hospital.Instance == null)
            {
                return;
            }

            // A OneWay edit can reopen the only safe exit from a zone whose access
            // rights changed earlier. Re-evaluate that bounded exit recovery before
            // applying the ordinary OneWay route retry.
            AccessZoneRecoveryManager.HandleAccessRightsChanged(
                floor,
                null);

            // Existing flashing markers describe the routing graph before this
            // OneWay edit. Invalidate them at the same moment as character routes;
            // any replacement job that still fails will publish fresh markers.
            if (TrafficControlConfig.PathfindingDebug)
            {
                PathfindingDebugMarkerRenderer.ClearMarkersForFloor(floor.m_floorIndex);
            }

            var characters = new List<Entity>(Hospital.Instance.m_characters);

            foreach (Entity entity in characters)
            {
                WalkComponent walk = entity?.GetComponent<WalkComponent>();
                if (walk == null || walk.m_state == null || walk.Floor != floor)
                {
                    continue;
                }

                WalkState state = walk.m_state.m_walkState;

                if (state == WalkState.NoPath && BlockedByOneWay.Contains(walk))
                {
                    // If the character is still physically inside a zone that its
                    // real rights no longer allow, keep the OneWay block in place.
                    // AccessZoneRecoveryManager will retry the bounded exit on the
                    // next relevant OneWay edit instead of forcing the original
                    // destination from an illegal starting tile.
                    if (AccessZoneRecoveryManager.IsCurrentTileRestricted(walk))
                    {
                        continue;
                    }

                    PendingOneWayRepaths.Add(walk);
                    BlockedRouteManager.RetryBlockedOneWay(walk);
                    continue;
                }

                if (state == WalkState.Walking &&
                    RouteContainsForbiddenTransition(walk.m_route, floor))
                {
                    PendingOneWayRepaths.Add(walk);
                    BlockedRouteManager.ForceRepath(walk);
                }
            }
        }

        internal static bool ShouldRegisterNoPathAsOneWay(WalkComponent walk)
        {
            if (walk == null)
            {
                return false;
            }

            // A pending repath only means a OneWay change invalidated the previous
            // route. It does not prove that the final replacement job failed because
            // of a OneWay edge; another vanilla restriction (for example STAFF access)
            // may be the real cause. Clear the pending flag, but classify the NoPath as
            // OneWay only when the worker actually recorded a OneWay denial.
            PendingOneWayRepaths.Remove(walk);

            OneWayFailureInfo failure;
            bool deniedByCurrentJob =
                OneWayPathfinderTracker.ConsumeDeniedNoPath(walk, out failure);

            if (!deniedByCurrentJob)
            {
                return false;
            }

            if (TrafficControlConfig.PathfindingDebug && failure != null)
            {
                PublishCausalOneWayMarkers(failure);
            }

            BlockedByOneWay.Add(walk);
            return true;
        }

        internal static void OnWalkStateChanged(WalkComponent walk, WalkState state)
        {
            if (walk == null || walk.m_state == null)
            {
                return;
            }

            if (state == WalkState.Walking)
            {
                if (RouteContainsForbiddenTransition(walk.m_route, walk.Floor))
                {
                    PendingOneWayRepaths.Add(walk);
                    BlockedRouteManager.ForceRepath(walk);
                    return;
                }

                PendingOneWayRepaths.Remove(walk);
                BlockedByOneWay.Remove(walk);
                BlockedRouteManager.ClearOneWayBlock(walk);
                return;
            }

            if (state == WalkState.Idle ||
                state == WalkState.ChangingFloor ||
                state == WalkState.LeavingElevator)
            {
                PendingOneWayRepaths.Remove(walk);
                BlockedByOneWay.Remove(walk);
                BlockedRouteManager.ClearOneWayBlock(walk);
            }
        }

        private static bool RouteContainsForbiddenTransition(
            PathfinderRoute route,
            Floor floor)
        {
            if (route == null || route.Nodes == null || route.Nodes.Count < 2 || floor == null)
            {
                return false;
            }

            List<PathfinderNode> nodes = route.Nodes;

            // PathfinderJob searches from the requested destination back toward
            // the character, but PathfinderRoute rebuilds the parent chain in
            // walking order: Nodes[0] is the character's start tile and each
            // following node is the next tile actually walked to.
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                PathfinderNode fromNode = nodes[i];
                PathfinderNode toNode = nodes[i + 1];
                if (toNode == null || fromNode == null)
                {
                    continue;
                }

                if (!OneWayManager.IsTransitionAllowed(
                    floor,
                    fromNode.Position,
                    toNode.Position))
                {
                    return true;
                }
            }

            return false;
        }

        private static void PublishCausalOneWayMarkers(OneWayFailureInfo failure)
        {
            if (failure == null || failure.FloorIndex < 0)
            {
                return;
            }

            var markers = new List<PathfindingDebugMarkerTile>();
            markers.Add(new PathfindingDebugMarkerTile(
                failure.FloorIndex,
                failure.From));

            if (failure.To != failure.From)
            {
                markers.Add(new PathfindingDebugMarkerTile(
                    failure.FloorIndex,
                    failure.To));
            }

            PathfindingDebugMarkerRenderer.SetMarkers(markers);
            Plugin.Log?.LogWarning(
                "[PathDebug] Logistics markers replaced with causal OneWay edge " +
                failure.From + " -> " + failure.To +
                " floor=" + failure.FloorIndex +
                " deniedChecks=" + failure.DeniedChecks + ".");
        }
    }
}
