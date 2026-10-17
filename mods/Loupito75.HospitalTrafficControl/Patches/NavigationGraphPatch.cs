using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    internal static class ExactAccessGraphManager
    {
        private static readonly AccessRights[] AdditionalGraphRights =
        {
            AccessRights.PEDESTRIAN,
            AccessRights.PATIENT_PROCEDURE,
            AccessRights.BIOHAZARD,
            AccessRights.STAFF
        };

        private static readonly AccessRights[] AllGraphRights =
        {
            AccessRights.PEDESTRIAN,
            AccessRights.PATIENT,
            AccessRights.PATIENT_PROCEDURE,
            AccessRights.BIOHAZARD,
            AccessRights.STAFF,
            AccessRights.STAFF_ONLY
        };

        private static readonly FieldInfo ElevatorSeedsField =
            AccessTools.Field(typeof(GridMap), "m_elevatorSeeds");

        private static readonly MethodInfo CalculateElevatorSeedsMethod =
            AccessTools.Method(
                typeof(GridMap),
                "CalculateElevatorSeeds",
                new Type[] { typeof(List<TileObject>) });

        internal static void RecalculateAdditionalGraphs(
            GridMap gridMap,
            int floorIndex,
            TileWalls[,] tileWalls,
            Tile[,] tiles,
            AccessRights[,] logisticsAccess,
            AccessRights[,] roomAccess)
        {
            if (gridMap == null ||
                gridMap.m_floorGraphs == null ||
                floorIndex < 0 ||
                floorIndex >= gridMap.m_floorGraphs.Length ||
                gridMap.m_floorGraphs[floorIndex] == null ||
                ElevatorSeedsField == null)
            {
                return;
            }

            List<KeyValuePair<Vector2i, Direction>> elevatorSeeds =
                ElevatorSeedsField.GetValue(gridMap)
                    as List<KeyValuePair<Vector2i, Direction>>;

            if (elevatorSeeds == null)
            {
                return;
            }

            AccessRights[,] combinedAccess =
                CombineAccessRights(logisticsAccess, roomAccess);

            Dictionary<AccessRights, RoomGraph> graphs =
                gridMap.m_floorGraphs[floorIndex];

            for (int i = 0; i < AdditionalGraphRights.Length; i++)
            {
                AccessRights access = AdditionalGraphRights[i];
                RoomGraph graph;

                if (!graphs.TryGetValue(access, out graph) || graph == null)
                {
                    graph = new RoomGraph(floorIndex);
                    graphs[access] = graph;
                }

                graph.Recalculate(
                    tileWalls,
                    tiles,
                    combinedAccess,
                    access,
                    floorIndex > 0,
                    elevatorSeeds);
            }

            if (TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[PathDebug] EXACT_ACCESS_GRAPHS floor=" + floorIndex +
                    " graphs=PEDESTRIAN,PATIENT,PATIENT_PROCEDURE,BIOHAZARD,STAFF,STAFF_ONLY.");
            }
        }

        internal static int RecalculateAffectedGraphs(
            GridMap gridMap,
            Floor floor,
            AccessChangeSet accessChange)
        {
            if (gridMap == null ||
                floor == null ||
                accessChange == null ||
                gridMap.m_floorGraphs == null ||
                floor.m_floorIndex < 0 ||
                floor.m_floorIndex >= gridMap.m_floorGraphs.Length ||
                gridMap.m_floorGraphs[floor.m_floorIndex] == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights == null ||
                floor.m_roomAccessRights == null)
            {
                return -1;
            }

            int affectedMask = accessChange.GetAffectedGraphMask(AllGraphRights);

            // Avoid creating elevator seeds and a full matrix when no graph
            // changes passability at the selected access levels.
            if (affectedMask == 0)
            {
                return 0;
            }

            if (CalculateElevatorSeedsMethod == null)
            {
                return -1;
            }

            List<KeyValuePair<Vector2i, Direction>> elevatorSeeds;
            try
            {
                elevatorSeeds =
                    CalculateElevatorSeedsMethod.Invoke(
                        gridMap,
                        new object[] { floor.m_elevators })
                    as List<KeyValuePair<Vector2i, Direction>>;
            }
            catch (Exception exception)
            {
                if (TrafficControlConfig.PathfindingDebug)
                {
                    Exception root = exception.InnerException ?? exception;
                    Plugin.Log?.LogInfo(
                        "[PathDebug] ACCESS_GRAPH_TARGETED_REBUILD failed floor=" +
                        floor.m_floorIndex +
                        " reason=elevator-seeds " +
                        root.GetType().Name + ": " + root.Message);
                }

                return -1;
            }

            if (elevatorSeeds == null)
            {
                return -1;
            }

            AccessRights[,] combinedAccess =
                CombineAccessRights(
                    floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights,
                    floor.m_roomAccessRights);

            Dictionary<AccessRights, RoomGraph> graphs =
                gridMap.m_floorGraphs[floor.m_floorIndex];

            int rebuiltCount = 0;
            string rebuiltGraphs = string.Empty;

            for (int i = 0; i < AllGraphRights.Length; i++)
            {
                AccessRights access = AllGraphRights[i];
                if ((affectedMask & (1 << i)) == 0)
                {
                    continue;
                }

                RoomGraph graph;
                if (!graphs.TryGetValue(access, out graph) || graph == null)
                {
                    graph = new RoomGraph(floor.m_floorIndex);
                    graphs[access] = graph;
                }

                graph.Recalculate(
                    floor.m_mapPersistentData.m_tileWalls,
                    floor.m_mapPersistentData.m_tiles,
                    combinedAccess,
                    access,
                    floor.m_floorIndex > 0,
                    elevatorSeeds);

                rebuiltCount++;
                if (rebuiltGraphs.Length > 0)
                {
                    rebuiltGraphs += ",";
                }
                rebuiltGraphs += access.ToString();
            }

            if (rebuiltCount > 0 && TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[PathDebug] ACCESS_GRAPH_TARGETED_REBUILD floor=" +
                    floor.m_floorIndex +
                    " graphs=" + rebuiltGraphs +
                    " reason=logistics-access-change.");
            }

            return rebuiltCount;
        }

        internal static bool TryGetExactGraph(
            GridMap gridMap,
            int floorIndex,
            AccessRights accessRights,
            out RoomGraph graph)
        {
            graph = null;

            if (gridMap == null ||
                gridMap.m_floorGraphs == null ||
                floorIndex < 0 ||
                floorIndex >= gridMap.m_floorGraphs.Length)
            {
                return false;
            }

            Dictionary<AccessRights, RoomGraph> graphs =
                gridMap.m_floorGraphs[floorIndex];

            return graphs != null &&
                   graphs.TryGetValue(accessRights, out graph) &&
                   graph != null;
        }

        internal static bool TryGetCompatibilityGrid(
            GridMap gridMap,
            int floorIndex,
            out RoomGraph graph)
        {
            return TryGetExactGraph(
                gridMap,
                floorIndex,
                AccessRights.STAFF_ONLY,
                out graph);
        }

        private static AccessRights[,] CombineAccessRights(
            AccessRights[,] logisticsAccess,
            AccessRights[,] roomAccess)
        {
            int width = logisticsAccess.GetLength(0);
            int height = logisticsAccess.GetLength(1);
            AccessRights[,] combined =
                new AccessRights[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (logisticsAccess[x, y] == AccessRights.BIOHAZARD)
                    {
                        combined[x, y] = (AccessRights)Math.Max(
                            (int)AccessRights.PATIENT,
                            (int)roomAccess[x, y]);
                    }
                    else
                    {
                        combined[x, y] = (AccessRights)Math.Max(
                            (int)logisticsAccess[x, y],
                            (int)roomAccess[x, y]);
                    }
                }
            }

            return combined;
        }
    }

    internal static class DeferredGridMapPatches
    {
        private static bool s_applied;
        private static bool s_applying;

        internal static void Apply(
            Harmony harmony,
            Floor initializedFloor)
        {
            if (harmony == null || initializedFloor == null)
            {
                return;
            }

            // This method is called only from the Postfix of
            // Floor.UpdateStaticNavigationData(). Vanilla has therefore already
            // executed GridMap.GetInstance().Recalculate(...) successfully.
            GridMap gridMap = GridMap.GetInstance();
            if (gridMap == null)
            {
                return;
            }

            if (s_applied)
            {
                return;
            }

            if (s_applying)
            {
                return;
            }

            s_applying = true;
            try
            {

                Patch(
                    harmony,
                    AccessTools.Method(
                        typeof(GridMap),
                        nameof(GridMap.Recalculate),
                        new Type[]
                        {
                            typeof(int),
                            typeof(TileWalls[,]),
                            typeof(Tile[,]),
                            typeof(AccessRights[,]),
                            typeof(AccessRights[,]),
                            typeof(List<TileObject>)
                        }),
                    typeof(ExactAccessGraphRecalculatePatch),
                    postfixName: nameof(ExactAccessGraphRecalculatePatch.Postfix));

                Patch(
                    harmony,
                    AccessTools.Method(
                        typeof(GridMap),
                        "GetGraphForFloorAndAccessRights",
                        new Type[]
                        {
                            typeof(int),
                            typeof(AccessRights)
                        }),
                    typeof(ExactAccessGraphSelectionPatch),
                    postfixName: nameof(ExactAccessGraphSelectionPatch.Postfix));

                Patch(
                    harmony,
                    AccessTools.Method(
                        typeof(GridMap),
                        nameof(GridMap.IsInGrid),
                        new Type[]
                        {
                            typeof(Vector2i),
                            typeof(int)
                        }),
                    typeof(ExactAccessGraphIsInGridPatch),
                    prefixName: nameof(ExactAccessGraphIsInGridPatch.Prefix));

                s_applied = true;
                EnsureFloorGraphs(gridMap, initializedFloor);

                if (TrafficControlConfig.PathfindingDebug)
                {
                    Plugin.Log?.LogInfo(
                        "[PathDebug] DEFERRED_GRIDMAP_PATCHES result=applied-after-native-gridmap-init.");
                }
            }
            catch (TypeInitializationException exception)
            {
                Plugin.Log?.LogError(
                    "GridMap type initialization failed after native navigation initialization: " +
                    exception);

                if (exception.InnerException != null)
                {
                    Plugin.Log?.LogError(
                        "GridMap inner exception: " +
                        exception.InnerException);
                }
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogError(
                    "Failed to apply deferred GridMap patches: " +
                    exception);
            }
            finally
            {
                s_applying = false;
            }
        }

        internal static void Reset()
        {
            s_applied = false;
            s_applying = false;
        }

        private static void EnsureFloorGraphs(
            GridMap gridMap,
            Floor floor)
        {
            if (gridMap == null ||
                floor == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer == null)
            {
                return;
            }

            ExactAccessGraphManager.RecalculateAdditionalGraphs(
                gridMap,
                floor.m_floorIndex,
                floor.m_mapPersistentData.m_tileWalls,
                floor.m_mapPersistentData.m_tiles,
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights,
                floor.m_roomAccessRights);
        }


        private static void Patch(
            Harmony harmony,
            MethodBase original,
            Type patchType,
            string prefixName = null,
            string postfixName = null)
        {
            if (object.ReferenceEquals(original, null))
            {
                throw new MissingMethodException(
                    "Could not resolve a deferred GridMap method.");
            }

            HarmonyMethod prefix =
                CreateHarmonyMethod(patchType, prefixName);
            HarmonyMethod postfix =
                CreateHarmonyMethod(patchType, postfixName);

            harmony.Patch(
                original,
                prefix,
                postfix,
                null,
                null,
                null);
        }

        private static HarmonyMethod CreateHarmonyMethod(
            Type patchType,
            string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            MethodInfo method =
                AccessTools.Method(patchType, methodName);

            if (object.ReferenceEquals(method, null))
            {
                throw new MissingMethodException(
                    patchType.FullName,
                    methodName);
            }

            return new HarmonyMethod(method);
        }
    }

    internal static class ExactAccessGraphRecalculatePatch
    {
        internal static void Postfix(
            GridMap __instance,
            int floor,
            TileWalls[,] tileWalls,
            Tile[,] tiles,
            AccessRights[,] accessRights,
            AccessRights[,] roomAccessRights)
        {
            ExactAccessGraphManager.RecalculateAdditionalGraphs(
                __instance,
                floor,
                tileWalls,
                tiles,
                accessRights,
                roomAccessRights);
        }
    }

    internal static class ExactAccessGraphSelectionPatch
    {
        internal static void Postfix(
            GridMap __instance,
            int floorIndex,
            AccessRights accessRights,
            ref RoomGraph __result)
        {
            RoomGraph exactGraph;
            if (ExactAccessGraphManager.TryGetExactGraph(
                    __instance,
                    floorIndex,
                    accessRights,
                    out exactGraph))
            {
                __result = exactGraph;
            }
        }
    }

    // GridMap.IsInGrid() historically asks for STAFF only as a proxy for the
    // broadest vanilla graph. Preserve that diagnostic meaning after STAFF gets
    // its own exact graph.
    internal static class ExactAccessGraphIsInGridPatch
    {
        internal static bool Prefix(
            GridMap __instance,
            Vector2i position,
            int floorIndex,
            ref bool __result)
        {
            RoomGraph compatibilityGraph;
            if (!ExactAccessGraphManager.TryGetCompatibilityGrid(
                    __instance,
                    floorIndex,
                    out compatibilityGraph))
            {
                return true;
            }

            __result = compatibilityGraph.IsInGrid(position);
            return false;
        }
    }

    [HarmonyPatch(typeof(WalkComponent), nameof(WalkComponent.CheckElevator))]
    internal static class ExactAccessElevatorPatch
    {
        private static bool Prefix(WalkComponent __instance)
        {
            if (__instance == null ||
                __instance.m_state == null ||
                __instance.Floor == null ||
                Hospital.Instance == null)
            {
                return true;
            }

            Entity entity = CharacterAccess.GetEntity(__instance);
            Behavior behavior =
                entity == null ? null : entity.GetComponent<Behavior>();

            if (behavior == null)
            {
                return true;
            }

            AccessRights actualAccess =
                NavigationAccessPolicy.GetMovementAccess(__instance);
            int currentFloor = __instance.Floor.m_floorIndex;
            int destinationFloor = __instance.m_state.m_destinationFloor;

            try
            {
                AccessRights graphAccess = actualAccess;
                string routeMode = "exact";
                KeyValuePair<List<WalkMidpoint>, float> waypoints =
                    GridMap.GetInstance().GetWaypoints(
                        currentFloor,
                        __instance.GetCurrentTile(),
                        destinationFloor,
                        __instance.GetDestinationTile(),
                        graphAccess);

                if (currentFloor != destinationFloor &&
                    (waypoints.Key == null || waypoints.Key.Count == 0) &&
                    NavigationAccessPolicy.NeedsBiohazardEndpointGraph(__instance))
                {
                    graphAccess = AccessRights.BIOHAZARD;
                    routeMode = "biohazard-endpoint";
                    waypoints = GridMap.GetInstance().GetWaypoints(
                        currentFloor,
                        __instance.GetCurrentTile(),
                        destinationFloor,
                        __instance.GetDestinationTile(),
                        graphAccess);
                }

                ApplyWaypoints(__instance, waypoints.Key);

                bool hasMidpoints =
                    waypoints.Key != null && waypoints.Key.Count > 0;
                bool invalidCost =
                    waypoints.Value < 0f ||
                    waypoints.Value >= float.MaxValue;

                if (TrafficControlConfig.PathfindingDebug &&
                    (currentFloor != destinationFloor ||
                     routeMode != "exact" ||
                     hasMidpoints ||
                     invalidCost))
                {
                    string characterName =
                        (entity.Name ?? string.Empty).Trim();

                    Plugin.Log?.LogInfo(
                        "[PathDebug] ACCESS_GRAPH_ROUTE entity='" +
                        characterName +
                        "' movementAccess=" + actualAccess +
                        "(" + (int)actualAccess + ")" +
                        " graphAccess=" + graphAccess +
                        "(" + (int)graphAccess + ")" +
                        " mode=" + routeMode +
                        " currentFloor=" + currentFloor +
                        " destinationFloor=" + destinationFloor +
                        " midpointCount=" +
                        (waypoints.Key == null ? 0 : waypoints.Key.Count) +
                        " cost=" + waypoints.Value + ".");
                }

                if (TrafficControlConfig.PathfindingDebug &&
                    currentFloor != destinationFloor &&
                    !hasMidpoints &&
                    invalidCost)
                {
                    LogCrossFloorGraphComparison(
                        __instance,
                        entity,
                        actualAccess,
                        currentFloor,
                        destinationFloor);
                }

                return false;
            }
            catch (Exception exception)
            {
                if (TrafficControlConfig.PathfindingDebug)
                {
                    Plugin.Log?.LogInfo(
                        "[PathDebug] ACCESS_GRAPH_ROUTE failed; using vanilla CheckElevator: " +
                        exception.GetType().Name + ": " + exception.Message);
                }

                return true;
            }
        }

        private static void LogCrossFloorGraphComparison(
            WalkComponent walk,
            Entity entity,
            AccessRights accessRights,
            int currentFloor,
            int destinationFloor)
        {
            try
            {
                GridMap gridMap = GridMap.GetInstance();
                if (gridMap == null ||
                    Hospital.Instance == null ||
                    Hospital.Instance.m_floors == null ||
                    currentFloor < 0 ||
                    destinationFloor < 0 ||
                    currentFloor >= Hospital.Instance.m_floors.Count ||
                    destinationFloor >= Hospital.Instance.m_floors.Count)
                {
                    return;
                }

                Floor originFloor = Hospital.Instance.m_floors[currentFloor];
                Floor destinationFloorObject =
                    Hospital.Instance.m_floors[destinationFloor];
                if (originFloor == null || destinationFloorObject == null)
                {
                    return;
                }

                Vector2i origin = walk.GetCurrentTileSafe();
                Vector2i destination = walk.GetDestinationTile();

                RoomGraph exactOriginGraph;
                RoomGraph exactDestinationGraph;
                RoomGraph compatibilityOriginGraph;
                RoomGraph compatibilityDestinationGraph;

                ExactAccessGraphManager.TryGetExactGraph(
                    gridMap,
                    currentFloor,
                    accessRights,
                    out exactOriginGraph);
                ExactAccessGraphManager.TryGetExactGraph(
                    gridMap,
                    destinationFloor,
                    accessRights,
                    out exactDestinationGraph);
                ExactAccessGraphManager.TryGetCompatibilityGrid(
                    gridMap,
                    currentFloor,
                    out compatibilityOriginGraph);
                ExactAccessGraphManager.TryGetCompatibilityGrid(
                    gridMap,
                    destinationFloor,
                    out compatibilityDestinationGraph);

                Dictionary<GridRectangle, float> exactOriginElevators =
                    GetElevatorDistances(exactOriginGraph, origin);
                Dictionary<GridRectangle, float> exactDestinationElevators =
                    GetElevatorDistances(exactDestinationGraph, destination);
                Dictionary<GridRectangle, float> compatibilityOriginElevators =
                    GetElevatorDistances(compatibilityOriginGraph, origin);
                Dictionary<GridRectangle, float> compatibilityDestinationElevators =
                    GetElevatorDistances(
                        compatibilityDestinationGraph,
                        destination);

                KeyValuePair<List<WalkMidpoint>, float> exactWaypoints =
                    gridMap.GetWaypoints(
                        currentFloor,
                        origin,
                        destinationFloor,
                        destination,
                        accessRights);
                KeyValuePair<List<WalkMidpoint>, float> compatibilityWaypoints =
                    gridMap.GetWaypoints(
                        currentFloor,
                        origin,
                        destinationFloor,
                        destination,
                        AccessRights.STAFF_ONLY);

                int exactCommonElevators =
                    CountCommonElevators(
                        exactOriginElevators,
                        exactDestinationElevators);
                int compatibilityCommonElevators =
                    CountCommonElevators(
                        compatibilityOriginElevators,
                        compatibilityDestinationElevators);

                string characterName =
                    entity == null
                        ? "<unknown>"
                        : (entity.Name ?? string.Empty).Trim();

                Plugin.Log?.LogInfo(
                    "[PathDebug] ACCESS_GRAPH_COMPARE entity='" +
                    characterName +
                    "' route=F" + currentFloor + " " + origin +
                    " -> F" + destinationFloor + " " + destination +
                    " access=" + FormatAccessRights(accessRights) +
                    " exactMidpoints=" + GetMidpointCount(exactWaypoints.Key) +
                    " exactCost=" + exactWaypoints.Value +
                    " compatibilityMidpoints=" +
                    GetMidpointCount(compatibilityWaypoints.Key) +
                    " compatibilityCost=" + compatibilityWaypoints.Value +
                    " exactCommonElevators=" + exactCommonElevators +
                    " compatibilityCommonElevators=" +
                    compatibilityCommonElevators +
                    " reason=" +
                    DescribeNoMidpointReason(
                        currentFloor,
                        destinationFloor,
                        exactOriginElevators,
                        exactDestinationElevators,
                        exactCommonElevators) +
                    ".");

                Plugin.Log?.LogInfo(
                    "[PathDebug]   ORIGIN " +
                    DescribeTileAccess(originFloor, origin) +
                    " exactGraph={" +
                    DescribeGraphEndpoint(exactOriginGraph, origin) +
                    "} compatibilityGraph={" +
                    DescribeGraphEndpoint(compatibilityOriginGraph, origin) +
                    "} exactElevators={" +
                    DescribeElevators(exactOriginElevators) +
                    "} compatibilityElevators={" +
                    DescribeElevators(compatibilityOriginElevators) +
                    "} endpointBridges={" +
                    DescribeOriginEndpointBridges(
                        originFloor,
                        exactOriginGraph,
                        origin,
                        accessRights) +
                    "}.");

                Plugin.Log?.LogInfo(
                    "[PathDebug]   DESTINATION " +
                    DescribeTileAccess(destinationFloorObject, destination) +
                    " exactGraph={" +
                    DescribeGraphEndpoint(exactDestinationGraph, destination) +
                    "} compatibilityGraph={" +
                    DescribeGraphEndpoint(
                        compatibilityDestinationGraph,
                        destination) +
                    "} exactElevators={" +
                    DescribeElevators(exactDestinationElevators) +
                    "} compatibilityElevators={" +
                    DescribeElevators(compatibilityDestinationElevators) +
                    "}.");
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogInfo(
                    "[PathDebug] ACCESS_GRAPH_COMPARE diagnostic failed: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static Dictionary<GridRectangle, float> GetElevatorDistances(
            RoomGraph graph,
            Vector2i position)
        {
            if (graph == null)
            {
                return new Dictionary<GridRectangle, float>();
            }

            return graph.GetAllElevatorDistances(position);
        }

        private static int CountCommonElevators(
            Dictionary<GridRectangle, float> originElevators,
            Dictionary<GridRectangle, float> destinationElevators)
        {
            int count = 0;
            foreach (
                KeyValuePair<GridRectangle, float> originElevator
                in originElevators)
            {
                foreach (
                    KeyValuePair<GridRectangle, float> destinationElevator
                    in destinationElevators)
                {
                    if (originElevator.Key != null &&
                        destinationElevator.Key != null &&
                        originElevator.Key.m_bottom ==
                            destinationElevator.Key.m_bottom)
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private static string DescribeNoMidpointReason(
            int currentFloor,
            int destinationFloor,
            Dictionary<GridRectangle, float> originElevators,
            Dictionary<GridRectangle, float> destinationElevators,
            int commonElevators)
        {
            if (originElevators.Count == 0 &&
                destinationElevators.Count == 0)
            {
                return "no-exact-elevator-both-sides";
            }

            if (originElevators.Count == 0)
            {
                return "no-exact-elevator-from-origin";
            }

            if (destinationElevators.Count == 0)
            {
                return "no-exact-elevator-from-destination";
            }

            if (commonElevators > 0)
            {
                return "unexpected-common-elevator-without-midpoint";
            }

            if (currentFloor == 0 || destinationFloor == 0)
            {
                return "no-common-exact-elevator-with-ground-floor-endpoint";
            }

            return "unexpected-no-midpoint-despite-reachable-elevators";
        }

        private static string DescribeTileAccess(
            Floor floor,
            Vector2i position)
        {
            if (floor == null ||
                floor.m_roomAccessRights == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights ==
                    null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= floor.Size.m_x ||
                position.m_y >= floor.Size.m_y)
            {
                return "tile=" + position + " access=<unavailable>";
            }

            AccessRights roomAccess =
                floor.m_roomAccessRights[position.m_x, position.m_y];
            AccessRights logisticsAccess =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    position.m_x,
                    position.m_y];

            return
                "tile=" + position +
                " roomAccess=" + FormatAccessRights(roomAccess) +
                " logisticsAccess=" + FormatAccessRights(logisticsAccess) +
                " requiredAccess=" +
                NavigationAccessPolicy.GetRequiredTileAccess(
                    floor,
                    position);
        }

        private static string DescribeGraphEndpoint(
            RoomGraph graph,
            Vector2i position)
        {
            if (graph == null)
            {
                return "graph=<missing>";
            }

            bool inGrid = graph.IsInGrid(position);
            GridRectangle rectangle = FindRectangle(graph, position);

            if (rectangle == null)
            {
                return "inGrid=" + inGrid + " rectangle=<none>";
            }

            return
                "inGrid=" + inGrid +
                " rectangle=" + rectangle.m_bottom +
                ".." + rectangle.m_top +
                " rectangleAccess=" +
                FormatAccessRights(rectangle.m_accessRights) +
                " exits=" +
                (rectangle.m_exits == null ? 0 : rectangle.m_exits.Count);
        }

        private static GridRectangle FindRectangle(
            RoomGraph graph,
            Vector2i position)
        {
            if (graph == null || graph.m_allRectangles == null)
            {
                return null;
            }

            for (int i = 0; i < graph.m_allRectangles.Count; i++)
            {
                GridRectangle rectangle = graph.m_allRectangles[i];
                if (rectangle != null &&
                    position.m_x >= rectangle.m_bottom.m_x &&
                    position.m_x <= rectangle.m_top.m_x &&
                    position.m_y >= rectangle.m_bottom.m_y &&
                    position.m_y <= rectangle.m_top.m_y)
                {
                    return rectangle;
                }
            }

            return null;
        }

        private static string DescribeElevators(
            Dictionary<GridRectangle, float> elevators)
        {
            if (elevators == null || elevators.Count == 0)
            {
                return "count=0";
            }

            const int maxDetails = 24;
            List<string> details = new List<string>();

            foreach (
                KeyValuePair<GridRectangle, float> elevator
                in elevators)
            {
                if (elevator.Key == null)
                {
                    continue;
                }

                details.Add(
                    elevator.Key.m_bottom +
                    ":" + elevator.Value);
            }

            details.Sort();

            int detailsToWrite =
                Math.Min(details.Count, maxDetails);
            string result = "count=" + elevators.Count + " reachable=";
            for (int i = 0; i < detailsToWrite; i++)
            {
                if (i > 0)
                {
                    result += ",";
                }

                result += details[i];
            }

            if (details.Count > maxDetails)
            {
                result += ",...";
            }

            return result;
        }

        private static string DescribeOriginEndpointBridges(
            Floor floor,
            RoomGraph exactGraph,
            Vector2i origin,
            AccessRights accessRights)
        {
            if (floor == null || exactGraph == null)
            {
                return "unavailable";
            }

            Vector2i[] neighbors =
            {
                new Vector2i(origin.m_x - 1, origin.m_y),
                new Vector2i(origin.m_x + 1, origin.m_y),
                new Vector2i(origin.m_x, origin.m_y - 1),
                new Vector2i(origin.m_x, origin.m_y + 1)
            };

            List<string> bridges = new List<string>();
            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector2i neighbor = neighbors[i];
                if (neighbor.m_x < 0 ||
                    neighbor.m_y < 0 ||
                    neighbor.m_x >= floor.Size.m_x ||
                    neighbor.m_y >= floor.Size.m_y)
                {
                    continue;
                }

                // PathfinderJob searches backwards from the requested destination.
                // Its final expansion into the real movement origin therefore checks
                // the neighbor's access rights, not the origin's. Reproduce only that
                // vanilla endpoint edge here; OneWay remains intentionally outside
                // GridMap/RoomGraph semantics.
                if (!floor.IsAccessible(
                        neighbor,
                        origin,
                        origin,
                        origin,
                        (int)accessRights,
                        true,
                        false))
                {
                    continue;
                }

                Dictionary<GridRectangle, float> elevatorDistances =
                    exactGraph.GetAllElevatorDistances(neighbor);
                bridges.Add(
                    neighbor +
                    ":elevators=" + elevatorDistances.Count);
            }

            if (bridges.Count == 0)
            {
                return "count=0";
            }

            bridges.Sort();
            string result = "count=" + bridges.Count + " candidates=";
            for (int i = 0; i < bridges.Count; i++)
            {
                if (i > 0)
                {
                    result += ",";
                }

                result += bridges[i];
            }

            return result;
        }

        private static int GetMidpointCount(
            List<WalkMidpoint> waypoints)
        {
            return waypoints == null ? 0 : waypoints.Count;
        }

        private static string FormatAccessRights(
            AccessRights accessRights)
        {
            return accessRights + "(" + (int)accessRights + ")";
        }

        private static void ApplyWaypoints(
            WalkComponent walk,
            List<WalkMidpoint> waypoints)
        {
            walk.m_state.m_walkMidpoint1 =
                waypoints != null && waypoints.Count > 0
                    ? waypoints[0]
                    : null;

            walk.m_state.m_walkMidpoint2 =
                waypoints != null && waypoints.Count > 1
                    ? waypoints[1]
                    : null;
        }
    }
}
