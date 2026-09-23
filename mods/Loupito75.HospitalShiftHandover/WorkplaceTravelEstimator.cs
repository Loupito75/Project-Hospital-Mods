using System;
using System.Collections.Generic;
using System.Globalization;
using GLib;
using Lopital;

namespace HospitalShiftHandover
{
    internal static class WorkplaceTravelEstimator
    {
        private const float NativeWalkSpeed = 1.75f;
        private const int PathfinderStepLimit = 9216;

        private sealed class ApproachCandidate
        {
            internal Vector2i Position;
            internal float RouteDistanceFromStart;
        }

        private sealed class EstimateState
        {
            internal PathfinderJob Job;
            internal PathfinderJob ApproachJob;
            internal Vector2i Start;
            internal int StartFloor;
            internal Vector2i Target;
            internal int TargetFloor;
            internal float RouteDistance;
            internal bool HasRoute;
            internal bool Failed;
            internal bool CrossFloor;
            internal List<ApproachCandidate> ApproachCandidates;
            internal string ApproachFailureReason;
        }

        private static readonly Dictionary<EmployeeComponent, EstimateState> Estimates =
            new Dictionary<EmployeeComponent, EstimateState>();

        internal static bool TryGetTravelMinutes(Behavior behavior, EmployeeComponent employee, out float travelMinutes)
        {
            travelMinutes = 0f;

            if (behavior == null || employee == null || employee.m_state == null || DayTime.Instance == null)
            {
                return false;
            }

            WalkComponent walk = behavior.GetComponent<WalkComponent>();
            if (walk == null || walk.Floor == null)
            {
                return false;
            }

            Vector2i target;
            int targetFloor;
            if (!TryGetWorkplaceTarget(employee, out target, out targetFloor))
            {
                return false;
            }

            int currentFloor = walk.GetFloorIndex();
            Vector2i start = walk.GetCurrentTile();
            EstimateState state;
            if (!Estimates.TryGetValue(employee, out state) ||
                state.Start != start ||
                state.StartFloor != currentFloor ||
                state.Target != target ||
                state.TargetFloor != targetFloor)
            {
                Clear(employee);
                state = CreateState(behavior, walk, start, currentFloor, target, targetFloor);
                Estimates[employee] = state;
            }

            if (state.Failed)
            {
                return false;
            }

            if (state.CrossFloor)
            {
                if (!state.HasRoute)
                {
                    return false;
                }

                travelMinutes = DistanceToMinutes(behavior, state.RouteDistance);
                return true;
            }

            if (!state.HasRoute)
            {
                if (state.Job == null)
                {
                    return false;
                }

                if (!state.Job.IsDone)
                {
                    if (!state.Job.IsRunning)
                    {
                        state.Job.TryToStart(ThreadedJob.THREAD_CATEGORY_PATHFINDING);
                    }
                    return false;
                }

                if (state.Job.m_result == null ||
                    state.Job.m_result.m_state != PathfinderResult.PathfinderState.FOUND_ROUTE ||
                    state.Job.m_result.m_route == null ||
                    state.Job.m_result.m_route.Nodes == null ||
                    state.Job.m_result.m_route.Nodes.Count == 0)
                {
                    state.Failed = true;
                    state.ApproachFailureReason = "pathfinder-no-route";
                    state.Job = null;
                    return false;
                }

                PathfinderRoute route = state.Job.m_result.m_route;
                state.RouteDistance = CalculateRouteDistance(route);
                string approachFailureReason;
                state.ApproachCandidates = BuildApproachCandidates(
                    employee,
                    walk.Floor,
                    route,
                    state.RouteDistance,
                    out approachFailureReason);
                state.ApproachFailureReason = approachFailureReason;
                state.HasRoute = true;
                state.Job = null;
            }

            travelMinutes = DistanceToMinutes(behavior, state.RouteDistance);
            return true;
        }

        internal static bool TryGetTravelMinutesWithFallback(
            Behavior behavior,
            EmployeeComponent employee,
            out float travelMinutes,
            out bool pending)
        {
            travelMinutes = 0f;
            pending = false;

            if (TryGetTravelMinutes(
                    behavior,
                    employee,
                    out travelMinutes))
            {
                return true;
            }

            pending = IsTravelEstimatePending(employee);
            if (pending)
            {
                return false;
            }

            if (behavior == null ||
                employee == null ||
                employee.m_state == null)
            {
                return false;
            }

            WalkComponent walk =
                behavior.GetComponent<WalkComponent>();
            GridMap gridMap = GridMap.GetInstance();
            if (walk == null ||
                walk.Floor == null ||
                gridMap == null)
            {
                return false;
            }

            Vector2i target;
            int targetFloor;
            if (!TryGetWorkplaceTarget(
                    employee,
                    out target,
                    out targetFloor))
            {
                return false;
            }

            float routeDistance =
                gridMap.GetDistance(
                    walk.GetFloorIndex(),
                    walk.GetCurrentTile(),
                    targetFloor,
                    target,
                    behavior.GetAccessRights());
            if (routeDistance < 0f ||
                routeDistance >= float.MaxValue)
            {
                return false;
            }

            travelMinutes =
                DistanceToMinutes(
                    behavior,
                    routeDistance);
            return true;
        }

        internal static bool TryGetApproachTarget(
            Behavior behavior,
            EmployeeComponent employee,
            List<Vector2i> excludedPositions,
            out Vector2i approachTarget,
            out int floorIndex,
            out float travelMinutes)
        {
            approachTarget = Vector2i.ZERO_VECTOR;
            floorIndex = 0;
            travelMinutes = 0f;

            float ignoredWorkplaceTravel;
            if (!TryGetTravelMinutes(behavior, employee, out ignoredWorkplaceTravel))
            {
                return false;
            }

            EstimateState state;
            if (!Estimates.TryGetValue(employee, out state) || state == null || !state.HasRoute)
            {
                return false;
            }

            if (state.CrossFloor && state.ApproachCandidates == null)
            {
                if (!EnsureCrossFloorApproachCandidates(behavior, employee, state))
                {
                    return false;
                }
            }

            if (state.ApproachCandidates == null || state.ApproachCandidates.Count == 0)
            {
                if (string.IsNullOrEmpty(state.ApproachFailureReason))
                {
                    state.ApproachFailureReason = "no-approach-candidate";
                }
                return false;
            }

            if (Hospital.Instance == null || Hospital.Instance.m_floors == null ||
                state.TargetFloor < 0 || state.TargetFloor >= Hospital.Instance.m_floors.Count)
            {
                state.ApproachFailureReason = "target-floor-unavailable";
                return false;
            }

            Floor targetFloor = Hospital.Instance.m_floors[state.TargetFloor];
            if (targetFloor == null)
            {
                state.ApproachFailureReason = "target-floor-unavailable";
                return false;
            }

            bool requirePublicStaging = OrganicStaging.RequiresPublicStaging(employee, state.TargetFloor);

            for (int i = 0; i < state.ApproachCandidates.Count; i++)
            {
                ApproachCandidate candidate = state.ApproachCandidates[i];
                if (candidate == null || candidate.Position == Vector2i.ZERO_VECTOR ||
                    ContainsPosition(excludedPositions, candidate.Position))
                {
                    continue;
                }

                if (targetFloor.m_mapPersistentData != null &&
                    targetFloor.m_mapPersistentData.m_tiles != null &&
                    targetFloor.m_mapPersistentData.m_tiles[candidate.Position.m_x, candidate.Position.m_y].m_user != null)
                {
                    continue;
                }

                Vector2i selectedTarget = candidate.Position;
                Vector2i organicTarget;
                string organicDetails;
                bool expanded = OrganicStaging.TrySelectNearbyTarget(
                    behavior,
                    employee,
                    excludedPositions,
                    candidate.Position,
                    state.TargetFloor,
                    out organicTarget,
                    out organicDetails);

                string selectionSource = "route-fallback";
                if (expanded)
                {
                    selectedTarget = organicTarget;
                    selectionSource = organicTarget == candidate.Position
                        ? "organic-anchor"
                        : "organic-expanded";
                }
                else if (requirePublicStaging)
                {
                    continue;
                }

                float selectedDistance = candidate.RouteDistanceFromStart;
                if (selectedTarget != candidate.Position)
                {
                    GridMap gridMap = GridMap.GetInstance();
                    if (gridMap == null)
                    {
                        continue;
                    }

                    selectedDistance = gridMap.GetDistance(
                        state.StartFloor,
                        state.Start,
                        state.TargetFloor,
                        selectedTarget,
                        behavior.GetAccessRights());
                    if (selectedDistance < 0f || selectedDistance >= float.MaxValue)
                    {
                        continue;
                    }
                }

                approachTarget = selectedTarget;
                floorIndex = state.TargetFloor;
                travelMinutes = DistanceToMinutes(behavior, selectedDistance);
                state.ApproachFailureReason = null;
                LogApproachSelection(
                    behavior,
                    employee,
                    candidate.Position,
                    selectedTarget,
                    state.TargetFloor,
                    selectionSource,
                    requirePublicStaging,
                    organicDetails,
                    travelMinutes);
                return true;
            }

            state.ApproachFailureReason = requirePublicStaging
                ? "no-public-handover-position"
                : "all-handover-nodes-busy-or-claimed";
            LogApproachFailure(behavior, employee, state.ApproachFailureReason, state.TargetFloor);
            return false;
        }

        internal static bool IsApproachPending(EmployeeComponent employee)
        {
            if (employee == null)
            {
                return false;
            }

            EstimateState state;
            if (!Estimates.TryGetValue(employee, out state) || state == null)
            {
                return false;
            }

            return state.ApproachJob != null && !state.ApproachJob.IsDone;
        }

        internal static bool IsTravelEstimatePending(EmployeeComponent employee)
        {
            if (employee == null)
            {
                return false;
            }

            EstimateState state;
            if (!Estimates.TryGetValue(employee, out state) || state == null ||
                state.Failed || state.HasRoute || state.CrossFloor)
            {
                return false;
            }

            return state.Job != null && !state.Job.IsDone;
        }

        internal static string GetLastApproachFailureReason(EmployeeComponent employee)
        {
            if (employee == null)
            {
                return "employee-unavailable";
            }

            EstimateState state;
            if (!Estimates.TryGetValue(employee, out state) || state == null)
            {
                return "route-state-unavailable";
            }

            return string.IsNullOrEmpty(state.ApproachFailureReason)
                ? "unknown"
                : state.ApproachFailureReason;
        }

        internal static bool TryGetWorkplaceTarget(EmployeeComponent employee, out Vector2i target, out int floorIndex)
        {
            target = Vector2i.ZERO_VECTOR;
            floorIndex = 0;

            if (employee == null || employee.m_state == null)
            {
                return false;
            }

            TileObject workChair = employee.GetWorkChair();
            if (workChair != null)
            {
                Vector2f usePosition = workChair.GetDefaultUsePosition();
                target = new Vector2i((int)(usePosition.m_x + 0.5f), (int)(usePosition.m_y + 0.5f));
                floorIndex = workChair.GetFloorIndex();
                return target != Vector2i.ZERO_VECTOR;
            }

            target = employee.m_state.m_workPlacePosition;
            floorIndex = employee.m_state.m_workPlaceFloorIndex;
            return target != Vector2i.ZERO_VECTOR;
        }

        internal static void Clear(EmployeeComponent employee)
        {
            if (employee == null)
            {
                return;
            }

            EstimateState state;
            if (!Estimates.TryGetValue(employee, out state))
            {
                return;
            }

            AbortIfRunning(state.Job);
            AbortIfRunning(state.ApproachJob);
            Estimates.Remove(employee);
        }

        internal static void Shutdown()
        {
            foreach (KeyValuePair<EmployeeComponent, EstimateState> pair in Estimates)
            {
                if (pair.Value != null)
                {
                    AbortIfRunning(pair.Value.Job);
                    AbortIfRunning(pair.Value.ApproachJob);
                }
            }

            Estimates.Clear();
        }

        private static EstimateState CreateState(Behavior behavior, WalkComponent walk, Vector2i start,
            int startFloor, Vector2i target, int targetFloor)
        {
            EstimateState state = new EstimateState();
            state.Start = start;
            state.StartFloor = startFloor;
            state.Target = target;
            state.TargetFloor = targetFloor;

            if (startFloor == targetFloor && start == target)
            {
                state.RouteDistance = 0f;
                state.HasRoute = true;
                state.ApproachCandidates = new List<ApproachCandidate>();
                state.ApproachFailureReason = "start-already-at-workplace";
                return state;
            }

            if (startFloor != targetFloor)
            {
                float routeDistance = GridMap.GetInstance().GetDistance(
                    startFloor,
                    start,
                    targetFloor,
                    target,
                    behavior.GetAccessRights());
                if (routeDistance < 0f || routeDistance >= float.MaxValue)
                {
                    state.Failed = true;
                    state.ApproachFailureReason = "gridmap-no-cross-floor-route";
                    return state;
                }

                state.RouteDistance = routeDistance;
                state.HasRoute = true;
                state.CrossFloor = true;
                return state;
            }

            PathfinderFlags flags = SettingsManager.Instance.m_gameSettings.m_8DirMovement
                ? PathfinderFlags.DIAGONALS
                : (PathfinderFlags)0;

            state.Job = new PathfinderJob(
                start,
                target,
                walk.Floor,
                PathfinderStepLimit,
                flags,
                (int)behavior.GetAccessRights(),
                (int)behavior.GetDefaultAccessRights(),
                behavior.GetLookAheadDistance());

            state.Job.TryToStart(ThreadedJob.THREAD_CATEGORY_PATHFINDING);
            return state;
        }

        private static bool EnsureCrossFloorApproachCandidates(
            Behavior behavior,
            EmployeeComponent employee,
            EstimateState state)
        {
            if (behavior == null || employee == null || state == null || !state.CrossFloor)
            {
                return false;
            }

            if (state.ApproachCandidates != null)
            {
                return true;
            }

            if (state.ApproachJob == null)
            {
                KeyValuePair<List<WalkMidpoint>, float> waypoints = GridMap.GetInstance().GetWaypoints(
                    state.StartFloor,
                    state.Start,
                    state.TargetFloor,
                    state.Target,
                    behavior.GetAccessRights());

                if (waypoints.Value < 0f || waypoints.Value >= float.MaxValue ||
                    waypoints.Key == null || waypoints.Key.Count == 0)
                {
                    state.ApproachCandidates = new List<ApproachCandidate>();
                    state.ApproachFailureReason = "gridmap-no-target-floor-waypoint";
                    return true;
                }

                WalkMidpoint finalMidpoint = waypoints.Key[waypoints.Key.Count - 1];
                Vector2i finalFloorStart = new Vector2i(
                    (int)(finalMidpoint.m_destination.m_x + 0.5f),
                    (int)(finalMidpoint.m_destination.m_y + 0.5f));

                if (Hospital.Instance == null || Hospital.Instance.m_floors == null ||
                    state.TargetFloor < 0 || state.TargetFloor >= Hospital.Instance.m_floors.Count)
                {
                    state.ApproachCandidates = new List<ApproachCandidate>();
                    state.ApproachFailureReason = "target-floor-unavailable";
                    return true;
                }

                Floor targetFloor = Hospital.Instance.m_floors[state.TargetFloor];
                if (targetFloor == null)
                {
                    state.ApproachCandidates = new List<ApproachCandidate>();
                    state.ApproachFailureReason = "target-floor-unavailable";
                    return true;
                }

                PathfinderFlags flags = SettingsManager.Instance.m_gameSettings.m_8DirMovement
                    ? PathfinderFlags.DIAGONALS
                    : (PathfinderFlags)0;

                state.ApproachJob = new PathfinderJob(
                    finalFloorStart,
                    state.Target,
                    targetFloor,
                    PathfinderStepLimit,
                    flags,
                    (int)behavior.GetAccessRights(),
                    (int)behavior.GetDefaultAccessRights(),
                    behavior.GetLookAheadDistance());
                state.ApproachFailureReason = "approach-pathfinder-pending";
                state.ApproachJob.TryToStart(ThreadedJob.THREAD_CATEGORY_PATHFINDING);
                return false;
            }

            if (!state.ApproachJob.IsDone)
            {
                if (!state.ApproachJob.IsRunning)
                {
                    state.ApproachJob.TryToStart(ThreadedJob.THREAD_CATEGORY_PATHFINDING);
                }
                state.ApproachFailureReason = "approach-pathfinder-pending";
                return false;
            }

            if (state.ApproachJob.m_result == null ||
                state.ApproachJob.m_result.m_state != PathfinderResult.PathfinderState.FOUND_ROUTE ||
                state.ApproachJob.m_result.m_route == null ||
                state.ApproachJob.m_result.m_route.Nodes == null ||
                state.ApproachJob.m_result.m_route.Nodes.Count == 0)
            {
                state.ApproachCandidates = new List<ApproachCandidate>();
                state.ApproachFailureReason = "approach-pathfinder-no-route";
                state.ApproachJob = null;
                return true;
            }

            PathfinderRoute finalRoute = state.ApproachJob.m_result.m_route;
            float finalSegmentDistance = CalculateRouteDistance(finalRoute);
            string failureReason;
            List<ApproachCandidate> candidates = BuildApproachCandidates(
                employee,
                Hospital.Instance.m_floors[state.TargetFloor],
                finalRoute,
                finalSegmentDistance,
                out failureReason);

            float distanceBeforeFinalFloor = state.RouteDistance - finalSegmentDistance;
            if (distanceBeforeFinalFloor < 0f)
            {
                distanceBeforeFinalFloor = 0f;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                candidates[i].RouteDistanceFromStart += distanceBeforeFinalFloor;
            }

            state.ApproachCandidates = candidates;
            state.ApproachFailureReason = failureReason;
            state.ApproachJob = null;
            return true;
        }

        private static List<ApproachCandidate> BuildApproachCandidates(
            EmployeeComponent employee,
            Floor floor,
            PathfinderRoute route,
            float totalRouteDistance,
            out string failureReason)
        {
            failureReason = null;
            List<ApproachCandidate> candidates = new List<ApproachCandidate>();
            if (employee == null || employee.m_state == null || floor == null ||
                route == null || route.Nodes == null || route.Nodes.Count == 0)
            {
                failureReason = "route-data-unavailable";
                return candidates;
            }

            Room workplaceRoom = null;
            if (employee.m_state.m_workDesk != null)
            {
                TileObject workDesk = employee.m_state.m_workDesk.GetEntity();
                if (workDesk != null && workDesk.m_state != null)
                {
                    workplaceRoom = floor.GetRoomTileSafe(
                        workDesk.m_state.m_position.m_x,
                        workDesk.m_state.m_position.m_y);
                }
            }

            if (workplaceRoom == null)
            {
                Vector2i target = route.Nodes[route.Nodes.Count - 1].Position;
                workplaceRoom = floor.GetRoomTileSafe(target.m_x, target.m_y);
            }

            if (workplaceRoom == null)
            {
                failureReason = "workplace-room-not-found";
                return candidates;
            }

            bool waitOutside = ShouldWaitOutsideWorkplaceRoom(workplaceRoom);

            if (!waitOutside)
            {
                bool enteredWorkplaceRoom = false;
                float distanceFromStart = 0f;
                for (int i = 0; i < route.Nodes.Count; i++)
                {
                    if (i > 0)
                    {
                        distanceFromStart += GetSegmentDistance(
                            route.Nodes[i - 1].Position,
                            route.Nodes[i].Position);
                    }

                    Vector2i position = route.Nodes[i].Position;
                    Room room = floor.GetRoomTileSafe(position.m_x, position.m_y);
                    if (room != workplaceRoom)
                    {
                        if (enteredWorkplaceRoom)
                        {
                            break;
                        }
                        continue;
                    }

                    enteredWorkplaceRoom = true;
                    if (i == route.Nodes.Count - 1)
                    {
                        continue;
                    }

                    ApproachCandidate insideCandidate = new ApproachCandidate();
                    insideCandidate.Position = position;
                    insideCandidate.RouteDistanceFromStart = distanceFromStart;
                    candidates.Add(insideCandidate);
                }
            }

            bool sawWorkplaceRoom = false;
            float distanceFromDestination = 0f;
            for (int i = route.Nodes.Count - 1; i >= 0; i--)
            {
                if (i < route.Nodes.Count - 1)
                {
                    distanceFromDestination += GetSegmentDistance(
                        route.Nodes[i + 1].Position,
                        route.Nodes[i].Position);
                }

                Vector2i position = route.Nodes[i].Position;
                Room room = floor.GetRoomTileSafe(position.m_x, position.m_y);
                if (room == workplaceRoom)
                {
                    sawWorkplaceRoom = true;
                    continue;
                }

                if (!sawWorkplaceRoom)
                {
                    continue;
                }

                ApproachCandidate outsideCandidate = new ApproachCandidate();
                outsideCandidate.Position = position;
                outsideCandidate.RouteDistanceFromStart = totalRouteDistance - distanceFromDestination;
                if (outsideCandidate.RouteDistanceFromStart < 0f)
                {
                    outsideCandidate.RouteDistanceFromStart = 0f;
                }
                candidates.Add(outsideCandidate);
            }

            if (!sawWorkplaceRoom)
            {
                failureReason = "route-does-not-enter-workplace-room";
            }
            else if (candidates.Count == 0)
            {
                failureReason = waitOutside
                    ? "no-node-outside-workplace-room"
                    : "no-safe-handover-node";
            }

            return candidates;
        }

        private static bool ShouldWaitOutsideWorkplaceRoom(Room workplaceRoom)
        {
            if (workplaceRoom == null || workplaceRoom.m_roomPersistentData == null)
            {
                return true;
            }

            GameDBRoomType roomType = workplaceRoom.m_roomPersistentData.m_roomType.Entry;
            if (roomType == null)
            {
                return true;
            }

            return roomType.AccessRights == AccessRights.PATIENT_PROCEDURE;
        }

        private static float CalculateRouteDistance(PathfinderRoute route)
        {
            float distance = 0f;
            List<PathfinderNode> nodes = route.Nodes;

            for (int i = 1; i < nodes.Count; i++)
            {
                distance += GetSegmentDistance(nodes[i - 1].Position, nodes[i].Position);
            }

            return distance;
        }

        private static float GetSegmentDistance(Vector2i first, Vector2i second)
        {
            int deltaX = second.m_x - first.m_x;
            int deltaY = second.m_y - first.m_y;
            return (float)Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        internal static float DistanceToMinutes(Behavior behavior, float distance)
        {
            if (behavior == null || DayTime.Instance == null)
            {
                return 0f;
            }

            float speedModifier = behavior.GetSpeedModifier();
            if (speedModifier <= 0f)
            {
                speedModifier = 1f;
            }

            float realSeconds = distance / (NativeWalkSpeed * speedModifier);
            return DayTime.Instance.RealTimeSecondsToIngameTimeHours(realSeconds) * 60f;
        }

        private static bool ContainsPosition(List<Vector2i> positions, Vector2i position)
        {
            if (positions == null)
            {
                return false;
            }

            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i] == position)
                {
                    return true;
                }
            }

            return false;
        }

        private static void LogApproachSelection(
            Behavior behavior,
            EmployeeComponent employee,
            Vector2i anchor,
            Vector2i selected,
            int floorIndex,
            string source,
            bool publicStaging,
            string organicDetails,
            float travelMinutes)
        {
            if (Plugin.Log == null || behavior == null || employee == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[STAGING_SELECTION] " + GetProfession(behavior) +
                " | " + GetCharacterName(employee.m_entity) +
                " | anchor=" + anchor.m_x.ToString(CultureInfo.InvariantCulture) +
                "," + anchor.m_y.ToString(CultureInfo.InvariantCulture) +
                " | selected=" + selected.m_x.ToString(CultureInfo.InvariantCulture) +
                "," + selected.m_y.ToString(CultureInfo.InvariantCulture) +
                " | floor=" + floorIndex.ToString(CultureInfo.InvariantCulture) +
                " | source=" + source +
                " | public=" + (publicStaging ? "yes" : "no") +
                " | travel=" + travelMinutes.ToString("0.0", CultureInfo.InvariantCulture) + "m" +
                (string.IsNullOrEmpty(organicDetails) ? string.Empty : " | " + organicDetails));
        }

        private static void LogApproachFailure(
            Behavior behavior,
            EmployeeComponent employee,
            string reason,
            int floorIndex)
        {
            if (Plugin.Log == null || behavior == null || employee == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[STAGING_SELECTION] " + GetProfession(behavior) +
                " | " + GetCharacterName(employee.m_entity) +
                " | source=none | floor=" + floorIndex.ToString(CultureInfo.InvariantCulture) +
                " | reason=" + reason);
        }

        private static string GetProfession(Behavior behavior)
        {
            if (behavior is BehaviorDoctor)
            {
                return "Doctor";
            }
            if (behavior is BehaviorNurse)
            {
                return "Nurse";
            }
            if (behavior is BehaviorLabSpecialist)
            {
                return "LabSpecialist";
            }
            return "Staff";
        }

        private static string GetCharacterName(Entity entity)
        {
            if (entity == null)
            {
                return "Unknown";
            }

            CharacterPersonalInfoComponent personalInfo = entity.GetComponent<CharacterPersonalInfoComponent>();
            if (personalInfo == null || personalInfo.m_personalInfo == null)
            {
                return "Unknown";
            }

            string characterName = personalInfo.m_personalInfo.GetFullName();
            return characterName != null ? characterName.Trim() : "Unknown";
        }

        private static void AbortIfRunning(PathfinderJob job)
        {
            if (job == null || !job.IsRunning)
            {
                return;
            }

            try
            {
                job.Abort();
            }
            catch
            {
            }
        }
    }
}
