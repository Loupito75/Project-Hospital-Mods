using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(WalkComponent), nameof(WalkComponent.SwitchState))]
    internal static class NoPathPatch
    {
        private static void Postfix(WalkComponent __instance, WalkState state)
        {
            if (state == WalkState.NoPath)
            {
                if (TrafficControlConfig.PathfindingDebug)
                {
                    PathfindingDebugManager.LogNoPath(__instance);
                    StrictAccessDebug.LogNoPathContext(__instance);
                }

                if (CrossFloorBlockedManager.TryRegister(__instance))
                {
                    return;
                }

                if (OneWayRouteManager.ShouldRegisterNoPathAsOneWay(__instance))
                {
                    BlockedRouteManager.RegisterOneWay(__instance);
                }
                else
                {
                    BlockedRouteManager.Register(__instance);
                }

                return;
            }

            // Another SwitchState() can be triggered from a different postfix
            // on the same outer call (access-zone recovery does this after reaching
            // its temporary safe tile). Do not let the stale outer state clean up a
            // newer NoPath that is already the component's real current state.
            if (__instance.m_state != null &&
                __instance.m_state.m_walkState == WalkState.NoPath)
            {
                return;
            }

            CrossFloorBlockedManager.Clear(__instance);
            BlockedRouteManager.ClearRecovered(__instance);
            OneWayRouteManager.OnWalkStateChanged(__instance, state);
        }
    }

    internal static class StrictAccessDebug
    {
        internal static void LogNoPathContext(WalkComponent walk)
        {
            if (!TrafficControlConfig.PathfindingDebug ||
                walk == null ||
                walk.m_state == null ||
                walk.Floor == null)
            {
                return;
            }

            Entity entity = CharacterAccess.GetEntity(walk);
            Behavior behavior = entity == null ? null : entity.GetComponent<Behavior>();
            BehaviorPatient patient = entity == null ? null : entity.GetComponent<BehaviorPatient>();

            AccessRights currentAccess =
                behavior == null ? AccessRights.PEDESTRIAN : behavior.GetAccessRights();
            AccessRights defaultAccess =
                behavior == null ? AccessRights.PEDESTRIAN : behavior.GetDefaultAccessRights();

            Vector2i currentTile = walk.GetCurrentTileSafe();
            Vector2i destinationTile = walk.GetDestinationTile();

            string patientState =
                patient != null && patient.m_state != null
                    ? patient.m_state.m_patientState.ToString()
                    : "n/a";

            string midpoint1 = FormatMidpoint(walk, walk.m_state.m_walkMidpoint1);
            string midpoint2 = FormatMidpoint(walk, walk.m_state.m_walkMidpoint2);
            string patientGraph = GetPatientGraphDiagnostic(
                walk,
                currentTile,
                destinationTile);
            string procedure = GetProcedureDiagnostic(entity);
            string destinationRoom = GetDestinationRoomDiagnostic(
                walk.m_state.m_destinationFloor,
                destinationTile);

            string entityName =
                entity == null ? "<unknown>" : (entity.Name ?? string.Empty).Trim();

            Plugin.Log?.LogWarning(
                "[PathDebug] ACCESS_CONTEXT entity='" + entityName +
                "' behavior=" + (behavior == null ? "null" : behavior.GetType().Name) +
                " access=" + FormatAccess(currentAccess) +
                " defaultAccess=" + FormatAccess(defaultAccess) +
                " patientState=" + patientState +
                " current=" + FormatTileAccess(walk.Floor, currentTile) +
                " destination=" + destinationTile +
                " destinationFloor=" + walk.m_state.m_destinationFloor +
                " destinationRoom=" + destinationRoom +
                " procedure=" + procedure +
                " midpoint1=" + midpoint1 +
                " midpoint2=" + midpoint2 +
                " patientGraph=" + patientGraph + ".");
        }

        private static string GetProcedureDiagnostic(Entity entity)
        {
            if (entity == null)
            {
                return "none";
            }

            ProcedureComponent component = entity.GetComponent<ProcedureComponent>();
            if (component == null || component.m_state == null)
            {
                return "none";
            }

            ProcedureScript script = null;
            string source = "current";

            if (component.m_state.m_currentProcedureScript != null)
            {
                script = component.m_state.m_currentProcedureScript.GetEntity();
            }

            if (script == null && component.m_state.m_reservedProcedureScript != null)
            {
                source = "reserved";
                script = component.m_state.m_reservedProcedureScript.GetEntity();
            }

            if (script == null)
            {
                return "none";
            }

            ProcedureScriptPersistentData data = script.m_stateData;
            if (data == null)
            {
                return source + "=" + script.GetType().Name + "{stateData=null}";
            }

            string item = "none";
            if (data.m_examination != null)
            {
                item = "examination=" + FormatDatabaseEntry(data.m_examination.Entry);
            }
            else if (data.m_treatment != null)
            {
                item = "treatment=" + FormatDatabaseEntry(data.m_treatment.Entry);
            }
            else if (data.m_procedure != null)
            {
                item = "procedure=" + FormatDatabaseEntry(data.m_procedure.Entry);
            }

            return source + "=" + script.GetType().Name +
                   "{state=" + (data.m_state ?? "<null>") +
                   "," + item +
                   ",baseProcedure=" + FormatDatabaseEntry(data.GetProcedure()) + "}";
        }

        private static string GetDestinationRoomDiagnostic(
            int destinationFloorIndex,
            Vector2i destinationTile)
        {
            if (Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                destinationFloorIndex < 0 ||
                destinationFloorIndex >= Hospital.Instance.m_floors.Count)
            {
                return "unavailable";
            }

            Floor floor = Hospital.Instance.m_floors[destinationFloorIndex];
            if (floor == null ||
                destinationTile.m_x < 0 || destinationTile.m_y < 0 ||
                destinationTile.m_x >= floor.Size.m_x ||
                destinationTile.m_y >= floor.Size.m_y)
            {
                return "out-of-bounds";
            }

            Room room = floor.m_roomTiles[destinationTile.m_x, destinationTile.m_y];
            if (room == null || room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null ||
                room.m_roomPersistentData.m_roomType.Entry == null)
            {
                return "none";
            }

            return FormatDatabaseEntry(room.m_roomPersistentData.m_roomType.Entry);
        }

        private static string FormatDatabaseEntry(DatabaseEntry entry)
        {
            if (entry == null)
            {
                return "none";
            }

            string id = entry.DatabaseID.ToString();
            string localizedName = null;

            try
            {
                localizedName = StringTable.GetInstance().GetLocalizedText(entry);
            }
            catch (System.Exception)
            {
                localizedName = null;
            }

            if (string.IsNullOrEmpty(localizedName) || localizedName == "!LOC! " + id)
            {
                return id;
            }

            localizedName = localizedName.Replace("\r", " ").Replace("\n", " ").Trim();
            return id + "('" + localizedName + "')";
        }

        private static string FormatMidpoint(WalkComponent walk, WalkMidpoint midpoint)
        {
            if (midpoint == null)
            {
                return "null";
            }

            Vector2i tile = new Vector2i(
                (int)(midpoint.m_destination.m_x + 0.5f),
                (int)(midpoint.m_destination.m_y + 0.5f));

            // m_walkMidpoint1 is pathfound on the character's current floor; its
            // destinationFloor is the floor entered after reaching that midpoint.
            string access =
                walk != null && walk.Floor != null
                    ? FormatTileAccess(walk.Floor, tile)
                    : tile.ToString();

            return access + " -> floor " + midpoint.m_destinationFloor;
        }

        private static string GetPatientGraphDiagnostic(
            WalkComponent walk,
            Vector2i currentTile,
            Vector2i destinationTile)
        {
            if (walk == null || walk.m_state == null || walk.Floor == null ||
                GridMap.GetInstance() == null)
            {
                return "unavailable";
            }

            try
            {
                KeyValuePair<List<WalkMidpoint>, float> result =
                    GridMap.GetInstance().GetWaypoints(
                        walk.Floor.m_floorIndex,
                        currentTile,
                        walk.m_state.m_destinationFloor,
                        destinationTile,
                        AccessRights.PATIENT);

                string first = "none";
                if (result.Key != null && result.Key.Count > 0 && result.Key[0] != null)
                {
                    Vector2i firstTile = new Vector2i(
                        (int)(result.Key[0].m_destination.m_x + 0.5f),
                        (int)(result.Key[0].m_destination.m_y + 0.5f));
                    first = firstTile + "->floor " + result.Key[0].m_destinationFloor;
                }

                return "count=" + (result.Key == null ? 0 : result.Key.Count) +
                       " cost=" + result.Value.ToString("0.###") +
                       " first=" + first;
            }
            catch (System.Exception exception)
            {
                return "error=" + exception.GetType().Name + ":" + exception.Message;
            }
        }

        private static string FormatTileAccess(Floor floor, Vector2i tile)
        {
            if (floor == null ||
                tile.m_x < 0 || tile.m_y < 0 ||
                tile.m_x >= floor.Size.m_x || tile.m_y >= floor.Size.m_y)
            {
                return tile + "{out-of-bounds}";
            }

            AccessRights roomAccess =
                floor.m_roomAccessRights[tile.m_x, tile.m_y];
            AccessRights logisticsAccess =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    tile.m_x,
                    tile.m_y];

            return tile +
                   "{room=" + FormatAccess(roomAccess) +
                   " logistics=" + FormatAccess(logisticsAccess) + "}";
        }

        private static string FormatAccess(AccessRights access)
        {
            return access + "(" + (int)access + ")";
        }
    }
}
