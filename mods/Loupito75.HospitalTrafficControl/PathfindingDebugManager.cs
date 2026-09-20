using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal sealed class PathfindingDebugMarkerTile
    {
        internal PathfindingDebugMarkerTile(int floorIndex, Vector2i position)
        {
            FloorIndex = floorIndex;
            Position = position;
        }

        internal int FloorIndex { get; private set; }
        internal Vector2i Position { get; private set; }
    }

    internal static class PathfindingDebugManager
    {
        private sealed class DeniedTransition
        {
            internal int FloorIndex;
            internal Vector2i CurrentPosition;
            internal Vector2i NextPosition;
            internal string Source;
            internal string ExactReason;
            internal string Details;

            internal string ToLogString()
            {
                string result =
                    "floor=" + FloorIndex +
                    " " + CurrentPosition + " -> " + NextPosition +
                    " source=" + Source +
                    " exact=" + ExactReason;

                if (!string.IsNullOrEmpty(Details))
                {
                    result += " " + Details;
                }

                return result;
            }
        }

        private sealed class FailedPathTrace
        {
            internal readonly List<DeniedTransition> DeniedTransitions =
                new List<DeniedTransition>();
            internal int DeniedTransitionCount;
        }

        private sealed class NativeDenialDiagnosis
        {
            internal NativeDenialDiagnosis(string reason, string details)
            {
                Reason = reason;
                Details = details;
            }

            internal string Reason;
            internal string Details;
        }

        private const int MaxLoggedTransitions = 32;

        private static readonly FieldInfo PathfinderJobField =
            AccessTools.Field(typeof(WalkComponent), "m_pathfinderJob");

        private static readonly FieldInfo NoFloorField =
            AccessTools.Field(typeof(Floor), "sm_noFloor");

        private static readonly Hashtable FailedJobs =
            Hashtable.Synchronized(new Hashtable());

        [ThreadStatic]
        private static PathfinderJob s_currentJob;

        [ThreadStatic]
        private static FailedPathTrace s_currentTrace;

        internal static void Begin(PathfinderJob job)
        {
            if (!TrafficControlConfig.PathfindingDebug)
            {
                s_currentJob = null;
                s_currentTrace = null;
                return;
            }

            s_currentJob = job;
            s_currentTrace = new FailedPathTrace();
        }

        internal static void RecordDeniedTransition(
            Floor floor,
            Vector2i currentPosition,
            Vector2i nextPosition,
            Vector2i startPosition,
            Vector2i targetPosition,
            int accessRightsLevel,
            bool ignoreObjects,
            bool ignoreAccessRights,
            string source)
        {
            if (!TrafficControlConfig.PathfindingDebug ||
                s_currentJob == null ||
                s_currentTrace == null)
            {
                return;
            }

            s_currentTrace.DeniedTransitionCount++;

            if (s_currentTrace.DeniedTransitions.Count >= MaxLoggedTransitions)
            {
                return;
            }

            NativeDenialDiagnosis diagnosis;
            if (source == "one-way")
            {
                diagnosis = new NativeDenialDiagnosis(
                    "HTC_ONE_WAY",
                    "vanillaAccessible=true");
            }
            else
            {
                diagnosis = DiagnoseVanillaDenial(
                    floor,
                    currentPosition,
                    nextPosition,
                    startPosition,
                    targetPosition,
                    accessRightsLevel,
                    ignoreObjects,
                    ignoreAccessRights);
            }

            var transition = new DeniedTransition
            {
                FloorIndex = floor == null ? -1 : floor.m_floorIndex,
                CurrentPosition = currentPosition,
                NextPosition = nextPosition,
                Source = source,
                ExactReason = diagnosis.Reason,
                Details = diagnosis.Details
            };

            if (!ContainsTransition(s_currentTrace.DeniedTransitions, transition))
            {
                s_currentTrace.DeniedTransitions.Add(transition);
            }
        }

        internal static void End(PathfinderJob job)
        {
            try
            {
                if (!TrafficControlConfig.PathfindingDebug ||
                    s_currentJob == null ||
                    !ReferenceEquals(s_currentJob, job))
                {
                    return;
                }

                PathfinderResult result = job == null ? null : job.m_result;
                bool noRoute = result == null || result.m_route == null;

                if (noRoute)
                {
                    FailedJobs[job] = s_currentTrace ?? new FailedPathTrace();
                }
                else
                {
                    FailedJobs.Remove(job);
                }
            }
            finally
            {
                s_currentJob = null;
                s_currentTrace = null;
            }
        }

        internal static void LogNoPath(WalkComponent walk)
        {
            if (!TrafficControlConfig.PathfindingDebug || walk == null)
            {
                return;
            }

            PathfinderJob job =
                PathfinderJobField == null ? null : PathfinderJobField.GetValue(walk) as PathfinderJob;

            Entity entity = CharacterAccess.GetEntity(walk);
            string entityName = entity == null ? "<unknown>" : (entity.Name ?? string.Empty).Trim();
            string entityKind = GetEntityKind(entity);

            int floorIndex = walk.Floor == null ? -1 : walk.Floor.m_floorIndex;
            string currentTile = SafeCurrentTile(walk);
            string destinationTile = SafeDestinationTile(walk);
            string destinationFloor = walk.m_state == null ? "?" : walk.m_state.m_destinationFloor.ToString();

            if (job == null)
            {
                Plugin.Log?.LogWarning(
                    "[PathDebug] NO PATH entity='" + entityName + "' kind=" + entityKind +
                    " floor=" + floorIndex +
                    " current=" + currentTile +
                    " destination=" + destinationTile +
                    " destinationFloor=" + destinationFloor +
                    " pathfinderJob=<null>.");
                return;
            }

            PathfinderResult result = job.m_result;
            string resultState = result == null ? "<null>" : result.m_state.ToString();
            string exceptionText =
                result == null || result.m_exception == null
                    ? "none"
                    : result.m_exception.GetType().FullName + ": " + result.m_exception.Message;

            Plugin.Log?.LogWarning(
                "[PathDebug] NO PATH entity='" + entityName + "' kind=" + entityKind +
                " floor=" + floorIndex +
                " current=" + currentTile +
                " destination=" + destinationTile +
                " destinationFloor=" + destinationFloor +
                " jobStart=" + job.m_start +
                " jobEnd=" + job.m_end +
                " state=" + resultState +
                " flags=" + job.m_flags +
                " access=" + FormatAccessRights(job.m_accessRightsLevel) +
                " preferredAccess=" + FormatAccessRights(job.m_preferredAccessRightsLevel) +
                " lookAhead=" + job.m_lookAheadDistance +
                " stepLimit=" + job.m_stepLimit +
                " exception=" + exceptionText + ".");

            FailedPathTrace trace = FailedJobs[job] as FailedPathTrace;
            if (trace == null)
            {
                Plugin.Log?.LogWarning(
                    "[PathDebug] No denied-transition trace was captured for this final job.");
                PublishFallbackMarker(floorIndex, job.m_end);
                return;
            }

            if (trace.DeniedTransitions.Count == 0)
            {
                Plugin.Log?.LogWarning(
                    "[PathDebug] Final job failed without a captured Floor.IsAccessible denial.");
                PublishFallbackMarker(floorIndex, job.m_end);
            }
            else
            {
                Plugin.Log?.LogWarning(
                    "[PathDebug] Denied transitions captured=" + trace.DeniedTransitions.Count +
                    " totalChecksRejected=" + trace.DeniedTransitionCount +
                    (trace.DeniedTransitionCount > trace.DeniedTransitions.Count
                        ? " (list capped/deduplicated)."
                        : "."));

                for (int i = 0; i < trace.DeniedTransitions.Count; i++)
                {
                    Plugin.Log?.LogWarning(
                        "[PathDebug]   #" + (i + 1) + " " +
                        trace.DeniedTransitions[i].ToLogString());
                }

                PublishMarkers(trace, floorIndex, job.m_end);
            }

            FailedJobs.Remove(job);
        }

        internal static void Forget(PathfinderJob job)
        {
            if (job != null)
            {
                FailedJobs.Remove(job);
            }
        }

        internal static void Reset()
        {
            FailedJobs.Clear();
            s_currentJob = null;
            s_currentTrace = null;
        }

        private static NativeDenialDiagnosis DiagnoseVanillaDenial(
            Floor floor,
            Vector2i currentPosition,
            Vector2i nextPosition,
            Vector2i startPosition,
            Vector2i targetPosition,
            int accessRightsLevel,
            bool ignoreObjects,
            bool ignoreAccessRights)
        {
            try
            {
                if (floor == null)
                {
                    return new NativeDenialDiagnosis(
                        "NO_FLOOR_INSTANCE",
                        "requestedAccess=" + FormatAccessRights(accessRightsLevel));
                }

                if (!IsInBounds(floor, nextPosition))
                {
                    return new NativeDenialDiagnosis(
                        "OUT_OF_BOUNDS",
                        "next=" + nextPosition + " size=" + floor.Size);
                }

                GameDBFloorType noFloor =
                    NoFloorField == null ? null : NoFloorField.GetValue(null) as GameDBFloorType;
                if (noFloor != null &&
                    floor.m_mapPersistentData.m_tiles[nextPosition.m_x, nextPosition.m_y].FloorType == noFloor)
                {
                    return new NativeDenialDiagnosis(
                        "NO_FLOOR_TILE",
                        "next=" + nextPosition);
                }

                if (!ignoreAccessRights)
                {
                    AccessRights roomAccess =
                        floor.m_roomAccessRights[currentPosition.m_x, currentPosition.m_y];
                    if ((int)roomAccess > accessRightsLevel)
                    {
                        return new NativeDenialDiagnosis(
                            "ROOM_ACCESS_RIGHTS",
                            "tile=" + currentPosition +
                            " roomAccess=" + FormatAccessRights((int)roomAccess) +
                            " requestedAccess=" + FormatAccessRights(accessRightsLevel));
                    }

                    AccessRights logisticsAccess =
                        floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                            currentPosition.m_x,
                            currentPosition.m_y];
                    if ((int)logisticsAccess > accessRightsLevel &&
                        logisticsAccess != AccessRights.BIOHAZARD)
                    {
                        return new NativeDenialDiagnosis(
                            "LOGISTICS_ACCESS_RIGHTS",
                            "tile=" + currentPosition +
                            " logisticsAccess=" + FormatAccessRights((int)logisticsAccess) +
                            " requestedAccess=" + FormatAccessRights(accessRightsLevel));
                    }
                }

                if (currentPosition.m_x == nextPosition.m_x)
                {
                    int wallY = Math.Max(currentPosition.m_y, nextPosition.m_y);
                    TileWalls walls =
                        floor.m_mapPersistentData.m_tileWalls[currentPosition.m_x, wallY];
                    if (walls.m_wallSW != null)
                    {
                        if (walls.m_doorSW == null)
                        {
                            return new NativeDenialDiagnosis(
                                "WALL_SW",
                                "edgeTile=[" + currentPosition.m_x + ", " + wallY + "] door=none");
                        }

                        if (!walls.m_doorSW.CanBeOpenedBy(null))
                        {
                            return new NativeDenialDiagnosis(
                                "DOOR_SW_NOT_OPENABLE",
                                "edgeTile=[" + currentPosition.m_x + ", " + wallY + "]");
                        }
                    }

                    if (currentPosition.m_y < nextPosition.m_y)
                    {
                        if (IsDirectionalWall(floor, currentPosition, Direction.NE))
                        {
                            return DirectionalWallDiagnosis(
                                "CURRENT_DIRECTIONAL_WALL",
                                currentPosition,
                                Direction.NE);
                        }

                        if (IsDirectionalWall(floor, nextPosition, Direction.SW))
                        {
                            return DirectionalWallDiagnosis(
                                "NEXT_DIRECTIONAL_WALL",
                                nextPosition,
                                Direction.SW);
                        }
                    }
                    else
                    {
                        if (IsDirectionalWall(floor, currentPosition, Direction.SW))
                        {
                            return DirectionalWallDiagnosis(
                                "CURRENT_DIRECTIONAL_WALL",
                                currentPosition,
                                Direction.SW);
                        }

                        if (IsDirectionalWall(floor, nextPosition, Direction.NE))
                        {
                            return DirectionalWallDiagnosis(
                                "NEXT_DIRECTIONAL_WALL",
                                nextPosition,
                                Direction.NE);
                        }
                    }
                }

                if (currentPosition.m_y == nextPosition.m_y)
                {
                    int wallX = Math.Max(currentPosition.m_x, nextPosition.m_x);
                    TileWalls walls =
                        floor.m_mapPersistentData.m_tileWalls[wallX, currentPosition.m_y];
                    if (walls.m_wallSE != null)
                    {
                        if (walls.m_doorSE == null)
                        {
                            return new NativeDenialDiagnosis(
                                "WALL_SE",
                                "edgeTile=[" + wallX + ", " + currentPosition.m_y + "] door=none");
                        }

                        if (!walls.m_doorSE.CanBeOpenedBy(null))
                        {
                            return new NativeDenialDiagnosis(
                                "DOOR_SE_NOT_OPENABLE",
                                "edgeTile=[" + wallX + ", " + currentPosition.m_y + "]");
                        }
                    }

                    if (currentPosition.m_x < nextPosition.m_x)
                    {
                        if (IsDirectionalWall(floor, currentPosition, Direction.NW))
                        {
                            return DirectionalWallDiagnosis(
                                "CURRENT_DIRECTIONAL_WALL",
                                currentPosition,
                                Direction.NW);
                        }

                        if (IsDirectionalWall(floor, nextPosition, Direction.SE))
                        {
                            return DirectionalWallDiagnosis(
                                "NEXT_DIRECTIONAL_WALL",
                                nextPosition,
                                Direction.SE);
                        }
                    }
                    else
                    {
                        if (IsDirectionalWall(floor, currentPosition, Direction.SE))
                        {
                            return DirectionalWallDiagnosis(
                                "CURRENT_DIRECTIONAL_WALL",
                                currentPosition,
                                Direction.SE);
                        }

                        if (IsDirectionalWall(floor, nextPosition, Direction.NW))
                        {
                            return DirectionalWallDiagnosis(
                                "NEXT_DIRECTIONAL_WALL",
                                nextPosition,
                                Direction.NW);
                        }
                    }
                }

                if (currentPosition.m_x != nextPosition.m_x &&
                    currentPosition.m_y != nextPosition.m_y)
                {
                    bool sideA = floor.IsAccessibleIgnoreDoors(
                        new Vector2i(currentPosition.m_x, currentPosition.m_y),
                        new Vector2i(currentPosition.m_x, nextPosition.m_y));
                    bool sideB = floor.IsAccessibleIgnoreDoors(
                        new Vector2i(currentPosition.m_x, currentPosition.m_y),
                        new Vector2i(nextPosition.m_x, currentPosition.m_y));
                    bool sideC = floor.IsAccessibleIgnoreDoors(
                        new Vector2i(nextPosition.m_x, nextPosition.m_y),
                        new Vector2i(currentPosition.m_x, nextPosition.m_y));
                    bool sideD = floor.IsAccessibleIgnoreDoors(
                        new Vector2i(nextPosition.m_x, nextPosition.m_y),
                        new Vector2i(nextPosition.m_x, currentPosition.m_y));

                    if (!sideA || !sideB || !sideC || !sideD)
                    {
                        return new NativeDenialDiagnosis(
                            "DIAGONAL_CORNER_BLOCKED",
                            "sides=" + sideA + "," + sideB + "," + sideC + "," + sideD);
                    }
                }

                if (!ignoreObjects)
                {
                    TileObject currentObject =
                        floor.m_tileObjects[currentPosition.m_x, currentPosition.m_y].m_centerObject;
                    if (currentObject != null &&
                        !currentObject.m_state.m_gameDBObject.Entry.NonBlocking &&
                        !currentObject.CanBeAccessedFrom(nextPosition, floor))
                    {
                        return new NativeDenialDiagnosis(
                            "BLOCKING_OBJECT",
                            "tile=" + currentPosition +
                            " objectType=" + currentObject.GetType().Name +
                            " orientation=" + currentObject.m_state.m_orientation);
                    }
                }

                if (currentPosition != startPosition &&
                    currentPosition != targetPosition &&
                    floor.m_mapPersistentData.m_tiles[currentPosition.m_x, currentPosition.m_y].m_user != null &&
                    floor.m_mapPersistentData.m_tiles[currentPosition.m_x, currentPosition.m_y].m_user.GetEntity() is ProcedureScript)
                {
                    return new NativeDenialDiagnosis(
                        "PROCEDURE_RESERVED_TILE",
                        "tile=" + currentPosition);
                }

                return new NativeDenialDiagnosis(
                    "VANILLA_UNCLASSIFIED",
                    BuildAccessSnapshot(
                        floor,
                        currentPosition,
                        nextPosition,
                        accessRightsLevel,
                        ignoreObjects,
                        ignoreAccessRights));
            }
            catch (Exception exception)
            {
                Exception root = exception.InnerException ?? exception;
                return new NativeDenialDiagnosis(
                    "DIAGNOSTIC_ERROR",
                    root.GetType().Name + ": " + root.Message);
            }
        }

        private static NativeDenialDiagnosis DirectionalWallDiagnosis(
            string reason,
            Vector2i tile,
            Direction orientation)
        {
            return new NativeDenialDiagnosis(
                reason,
                "tile=" + tile + " orientation=" + orientation + " IsWallNW=true");
        }

        private static bool IsDirectionalWall(
            Floor floor,
            Vector2i position,
            Direction orientation)
        {
            TileObject tileObject =
                floor.m_tileObjects[position.m_x, position.m_y].m_centerObject;
            return tileObject != null &&
                   tileObject.m_state.m_orientation == orientation &&
                   tileObject.m_state.m_gameDBObject.Entry.IsWallNW;
        }

        private static string BuildAccessSnapshot(
            Floor floor,
            Vector2i currentPosition,
            Vector2i nextPosition,
            int accessRightsLevel,
            bool ignoreObjects,
            bool ignoreAccessRights)
        {
            string result =
                "requestedAccess=" + FormatAccessRights(accessRightsLevel) +
                " ignoreObjects=" + ignoreObjects +
                " ignoreAccessRights=" + ignoreAccessRights;

            if (IsInBounds(floor, currentPosition))
            {
                AccessRights roomAccess =
                    floor.m_roomAccessRights[currentPosition.m_x, currentPosition.m_y];
                AccessRights logisticsAccess =
                    floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                        currentPosition.m_x,
                        currentPosition.m_y];
                result +=
                    " currentRoomAccess=" + FormatAccessRights((int)roomAccess) +
                    " currentLogisticsAccess=" + FormatAccessRights((int)logisticsAccess);
            }

            if (IsInBounds(floor, nextPosition))
            {
                AccessRights roomAccess =
                    floor.m_roomAccessRights[nextPosition.m_x, nextPosition.m_y];
                AccessRights logisticsAccess =
                    floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                        nextPosition.m_x,
                        nextPosition.m_y];
                result +=
                    " nextRoomAccess=" + FormatAccessRights((int)roomAccess) +
                    " nextLogisticsAccess=" + FormatAccessRights((int)logisticsAccess);
            }

            return result;
        }

        private static bool IsInBounds(Floor floor, Vector2i position)
        {
            return floor != null &&
                   position.m_x >= 0 && position.m_x < floor.Size.m_x &&
                   position.m_y >= 0 && position.m_y < floor.Size.m_y;
        }

        private static bool ContainsTransition(
            List<DeniedTransition> transitions,
            DeniedTransition candidate)
        {
            for (int i = 0; i < transitions.Count; i++)
            {
                DeniedTransition existing = transitions[i];
                if (existing.FloorIndex == candidate.FloorIndex &&
                    existing.CurrentPosition == candidate.CurrentPosition &&
                    existing.NextPosition == candidate.NextPosition &&
                    existing.Source == candidate.Source &&
                    existing.ExactReason == candidate.ExactReason)
                {
                    return true;
                }
            }

            return false;
        }

        private static void PublishMarkers(
            FailedPathTrace trace,
            int fallbackFloorIndex,
            Vector2i fallbackPosition)
        {
            var markers = new List<PathfindingDebugMarkerTile>();

            for (int i = 0; i < trace.DeniedTransitions.Count; i++)
            {
                DeniedTransition transition = trace.DeniedTransitions[i];
                Vector2i markerPosition = transition.CurrentPosition;

                if (transition.ExactReason == "NO_FLOOR_TILE" ||
                    transition.ExactReason == "NEXT_DIRECTIONAL_WALL")
                {
                    markerPosition = transition.NextPosition;
                }

                AddMarkerIfMissing(markers, transition.FloorIndex, markerPosition);
            }

            if (markers.Count == 0)
            {
                AddMarkerIfMissing(markers, fallbackFloorIndex, fallbackPosition);
            }

            PathfindingDebugMarkerRenderer.SetMarkers(markers);
            Plugin.Log?.LogWarning(
                "[PathDebug] Logistics marker updated: " + markers.Count +
                " blocked tile(s). Open Logistics view on the reported floor to see the flashing white marker(s).");
        }

        private static void PublishFallbackMarker(int floorIndex, Vector2i position)
        {
            var markers = new List<PathfindingDebugMarkerTile>();
            AddMarkerIfMissing(markers, floorIndex, position);
            PathfindingDebugMarkerRenderer.SetMarkers(markers);
            Plugin.Log?.LogWarning(
                "[PathDebug] Logistics marker fallback placed on destination " + position +
                " floor=" + floorIndex + ".");
        }

        private static void AddMarkerIfMissing(
            List<PathfindingDebugMarkerTile> markers,
            int floorIndex,
            Vector2i position)
        {
            if (floorIndex < 0)
            {
                return;
            }

            for (int i = 0; i < markers.Count; i++)
            {
                if (markers[i].FloorIndex == floorIndex &&
                    markers[i].Position == position)
                {
                    return;
                }
            }

            markers.Add(new PathfindingDebugMarkerTile(floorIndex, position));
        }

        private static string FormatAccessRights(int value)
        {
            return ((AccessRights)value) + "(" + value + ")";
        }

        private static string GetEntityKind(Entity entity)
        {
            if (entity == null)
            {
                return "unknown";
            }

            if (entity.GetComponent<BehaviorPatient>() != null)
            {
                return "patient";
            }

            Behavior behavior = entity.GetComponent<Behavior>();
            return behavior == null ? "entity" : behavior.GetType().Name;
        }

        private static string SafeCurrentTile(WalkComponent walk)
        {
            try
            {
                return walk.GetCurrentTile().ToString();
            }
            catch
            {
                return "?";
            }
        }

        private static string SafeDestinationTile(WalkComponent walk)
        {
            try
            {
                return walk.GetDestinationTile().ToString();
            }
            catch
            {
                return "?";
            }
        }
    }
}
