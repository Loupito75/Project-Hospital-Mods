using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(PathfinderJob), "ThreadFunction")]
    internal static class OneWayPathfinderJobPatch
    {
        private static void Prefix(PathfinderJob __instance)
        {
            BiohazardEndpointAccessTracker.Begin(__instance);
            AccessZoneRecoveryTracker.Begin(__instance);
            OneWayPathfinderTracker.Begin(__instance);

            if (TrafficControlConfig.PathfindingDebug)
            {
                PathfindingDebugManager.Begin(__instance);
            }
        }

        private static void Postfix(PathfinderJob __instance)
        {
            BiohazardEndpointAccessTracker.End(__instance);
            AccessZoneRecoveryTracker.End(__instance);
            OneWayPathfinderTracker.End(__instance);

            if (TrafficControlConfig.PathfindingDebug)
            {
                PathfindingDebugManager.End(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ThreadedJob), nameof(ThreadedJob.Abort))]
    internal static class OneWayPathfinderAbortPatch
    {
        private static void Prefix(ThreadedJob __instance)
        {
            PathfinderJob pathfinderJob = __instance as PathfinderJob;
            if (pathfinderJob != null)
            {
                BiohazardEndpointAccessTracker.Forget(pathfinderJob);
                AccessZoneRecoveryTracker.Forget(pathfinderJob);
                OneWayPathfinderTracker.Forget(pathfinderJob);

                if (TrafficControlConfig.PathfindingDebug)
                {
                    PathfindingDebugManager.Forget(pathfinderJob);
                }
            }
        }
    }

    [HarmonyPatch(typeof(WalkComponent), "UpdateLookingForPath")]
    internal static class OneWayAcceptedDetourDebugPatch
    {
        private const int MaxAnomalyNodesToLog = 12;
        private const string NoFloorId = "FLOOR_TYPE_NONE";

        private static readonly FieldInfo PathfinderJobField =
            AccessTools.Field(typeof(WalkComponent), "m_pathfinderJob");

        private static void Prefix(WalkComponent __instance)
        {
            if (!TrafficControlConfig.PathfindingDebug ||
                __instance == null ||
                ReferenceEquals(PathfinderJobField, null))
            {
                return;
            }

            PathfinderJob job = PathfinderJobField.GetValue(__instance) as PathfinderJob;
            if (job == null ||
                job.IsRunning ||
                !job.IsDone ||
                job.m_result == null ||
                job.m_result.m_route == null ||
                job.m_result.m_route.Nodes == null ||
                job.m_result.m_route.Nodes.Count < 2)
            {
                return;
            }

            OneWayFailureInfo failure;
            if (!OneWayPathfinderTracker.ConsumeSuccessfulDetour(job, out failure))
            {
                return;
            }

            // This Prefix runs on the Unity/main thread. At this exact point vanilla's
            // UpdateLookingForPath() will adopt any non-null route with at least two nodes,
            // then call ThreadedJob.Abort() and clear m_pathfinderJob. Do not retain or read
            // the PathfinderJob after that Abort: old Mono is much safer if the diagnostic
            // completes before vanilla disposes its reference to the completed worker.
            try
            {
                Entity entity = CharacterAccess.GetEntity(__instance);
                if (entity == null || entity.GetComponent<EmployeeComponent>() == null)
                {
                    return;
                }

                LogAcceptedEmployeeDetour(
                    __instance,
                    entity,
                    job,
                    failure);
            }
            catch (Exception ex)
            {
                // Diagnostics must never be able to break movement or save loading.
                Plugin.Log?.LogWarning(
                    "[PathDebug] ONE_WAY_EMPLOYEE_DETOUR diagnostic failed: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void LogAcceptedEmployeeDetour(
            WalkComponent walk,
            Entity entity,
            PathfinderJob job,
            OneWayFailureInfo failure)
        {
            Floor floor = walk.Floor;
            PathfinderRoute route = job.m_result.m_route;
            if (floor == null ||
                route == null ||
                route.Nodes == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_tiles == null)
            {
                return;
            }

            Behavior behavior = entity.GetComponent<Behavior>();
            string characterName = entity.Name == null ? string.Empty : entity.Name.Trim();
            string behaviorType = behavior == null ? "<none>" : behavior.GetType().Name;
            string access = behavior == null
                ? "<unknown>"
                : FormatAccessRights((int)behavior.GetAccessRights());
            string defaultAccess = behavior == null
                ? "<unknown>"
                : FormatAccessRights((int)behavior.GetDefaultAccessRights());

            Plugin.Log?.LogWarning(
                "[PathDebug] ONE_WAY_EMPLOYEE_DETOUR" +
                " entity='" + characterName + "'" +
                " behavior=" + behaviorType +
                " access=" + access +
                " defaultAccess=" + defaultAccess +
                " floor=" + floor.m_floorIndex +
                " deniedEdge=" + failure.From + " -> " + failure.To +
                " deniedChecks=" + failure.DeniedChecks +
                " jobStart=" + job.m_start +
                " jobEnd=" + job.m_end +
                " routeNodes=" + route.Nodes.Count + ".");

            int nullNodes = 0;
            int outOfBoundsNodes = 0;
            int noFloorNodes = 0;
            int roofNodes = 0;
            int outdoorsNodes = 0;
            int nonFoundationNodes = 0;
            var anomalyIndices = new List<int>();

            for (int i = 0; i < route.Nodes.Count; i++)
            {
                PathfinderNode node = route.Nodes[i];
                if (node == null)
                {
                    nullNodes++;
                    AddAnomalyIndex(anomalyIndices, i);
                    continue;
                }

                Vector2i position = node.Position;
                if (!IsInBounds(floor, position))
                {
                    outOfBoundsNodes++;
                    AddAnomalyIndex(anomalyIndices, i);
                    continue;
                }

                GameDBFloorType floorType =
                    floor.m_mapPersistentData.m_tiles[position.m_x, position.m_y].FloorType;
                if (IsNoFloor(floorType))
                {
                    noFloorNodes++;
                    AddAnomalyIndex(anomalyIndices, i);
                }

                if (floor.IsRoof(position))
                {
                    roofNodes++;
                }

                if (floor.IsOutdoors(position))
                {
                    outdoorsNodes++;
                }

                if (GetFoundation(floor, position) != FoundationsLayer.FOUNDATIONS)
                {
                    nonFoundationNodes++;
                }
            }

            Plugin.Log?.LogWarning(
                "[PathDebug]   DETOUR_SUMMARY" +
                " start=" + GetNodePosition(route, 0) +
                " end=" + GetNodePosition(route, route.Nodes.Count - 1) +
                " null=" + nullNodes +
                " outOfBounds=" + outOfBoundsNodes +
                " noFloor=" + noFloorNodes +
                " roof=" + roofNodes +
                " outdoors=" + outdoorsNodes +
                " nonFoundation=" + nonFoundationNodes +
                " anomaliesLogged=" + anomalyIndices.Count + ".");

            for (int i = 0; i < anomalyIndices.Count; i++)
            {
                int routeIndex = anomalyIndices[i];
                PathfinderNode node = route.Nodes[routeIndex];
                if (node == null)
                {
                    Plugin.Log?.LogWarning(
                        "[PathDebug]   DETOUR_ANOMALY #" + routeIndex + " <null>.");
                    continue;
                }

                Plugin.Log?.LogWarning(
                    "[PathDebug]   DETOUR_ANOMALY #" + routeIndex +
                    " pos=" + node.Position +
                    " " + DescribeTile(floor, node.Position) + ".");
            }

            if (nullNodes + outOfBoundsNodes + noFloorNodes > anomalyIndices.Count)
            {
                Plugin.Log?.LogWarning(
                    "[PathDebug]   DETOUR_ANOMALY details capped at " +
                    MaxAnomalyNodesToLog + " node(s).");
            }
        }

        private static void AddAnomalyIndex(List<int> anomalyIndices, int index)
        {
            if (anomalyIndices.Count < MaxAnomalyNodesToLog)
            {
                anomalyIndices.Add(index);
            }
        }

        private static string GetNodePosition(PathfinderRoute route, int index)
        {
            if (route == null || route.Nodes == null ||
                index < 0 || index >= route.Nodes.Count ||
                route.Nodes[index] == null)
            {
                return "<null>";
            }

            return route.Nodes[index].Position.ToString();
        }

        private static bool IsInBounds(Floor floor, Vector2i position)
        {
            return floor != null &&
                   position.m_x >= 0 &&
                   position.m_y >= 0 &&
                   position.m_x < floor.Size.m_x &&
                   position.m_y < floor.Size.m_y;
        }

        private static bool IsNoFloor(GameDBFloorType floorType)
        {
            return floorType != null &&
                   !ID.IsNullOrNoID(floorType.DatabaseID) &&
                   floorType.DatabaseID.ToString() == NoFloorId;
        }

        private static byte GetFoundation(Floor floor, Vector2i position)
        {
            FoundationsLayer foundationsLayer =
                floor.m_mapPersistentData.m_foundationsLayer;
            if (foundationsLayer == null || foundationsLayer.m_foundations == null)
            {
                return FoundationsLayer.OUTDOORS;
            }

            return foundationsLayer.m_foundations[position.m_x, position.m_y];
        }

        private static string DescribeTile(Floor floor, Vector2i position)
        {
            if (!IsInBounds(floor, position))
            {
                return "tile=OUT_OF_BOUNDS";
            }

            GameDBFloorType floorType =
                floor.m_mapPersistentData.m_tiles[position.m_x, position.m_y].FloorType;
            string floorTypeId =
                floorType == null || ID.IsNullOrNoID(floorType.DatabaseID)
                    ? "<null>"
                    : floorType.DatabaseID.ToString();

            byte foundation = GetFoundation(floor, position);

            string roomAccess = "?";
            if (floor.m_roomAccessRights != null)
            {
                roomAccess = FormatAccessRights(
                    (int)floor.m_roomAccessRights[position.m_x, position.m_y]);
            }

            string logisticsAccess = "?";
            if (floor.m_mapPersistentData.m_mapLogisticsLayer != null &&
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights != null)
            {
                logisticsAccess = FormatAccessRights(
                    (int)floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                        position.m_x,
                        position.m_y]);
            }

            return
                "floorType=" + floorTypeId +
                " foundation=" + GetFoundationName(foundation) + "(" + foundation + ")" +
                " isRoof=" + floor.IsRoof(position) +
                " outdoors=" + floor.IsOutdoors(position) +
                " roomAccess=" + roomAccess +
                " logisticsAccess=" + logisticsAccess;
        }

        private static string FormatAccessRights(int value)
        {
            return ((AccessRights)value).ToString() + "(" + value + ")";
        }

        private static string GetFoundationName(byte foundation)
        {
            switch (foundation)
            {
                case FoundationsLayer.OUTDOORS:
                    return "OUTDOORS";
                case FoundationsLayer.EDGE:
                    return "EDGE";
                case FoundationsLayer.FOUNDATIONS:
                    return "FOUNDATIONS";
                case FoundationsLayer.LOCKED:
                    return "LOCKED";
                default:
                    return "UNKNOWN";
            }
        }
    }
}
