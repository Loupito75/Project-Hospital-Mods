using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class PatientProcedureNeedAccessRules
    {
        internal static bool IsCandidateAccessAllowed(
            Floor floor,
            Vector2i origin,
            Vector2i candidatePosition,
            AccessRights accessRights,
            string tag)
        {
            if (!IsInsideFloor(floor, origin))
            {
                return false;
            }

            return IsTileAccessAllowed(floor, candidatePosition, accessRights);
        }

        internal static bool IsObjectAndUseDestinationAllowed(
            Floor floor,
            TileObject candidate,
            AccessRights accessRights,
            out Vector2i useTile)
        {
            useTile = Vector2i.ZERO_VECTOR;
            if (candidate == null || candidate.m_state == null)
            {
                return false;
            }

            Vector2i objectTile = candidate.m_state.m_position;
            Vector2f usePosition = candidate.GetDefaultUsePosition();
            useTile = new Vector2i(
                (int)(usePosition.m_x + 0.5f),
                (int)(usePosition.m_y + 0.5f));

            return IsTileAccessAllowed(floor, objectTile, accessRights) &&
                IsTileAccessAllowed(floor, useTile, accessRights);
        }

        internal static bool IsFoodDestinationAllowed(
            Floor floor,
            TileObject candidate,
            AccessRights accessRights,
            out Vector2i useTile)
        {
            return IsObjectAndUseDestinationAllowed(
                floor,
                candidate,
                accessRights,
                out useTile);
        }

        private static bool IsTileAccessAllowed(
            Floor floor,
            Vector2i position,
            AccessRights accessRights)
        {
            if (!IsInsideFloor(floor, position))
            {
                return false;
            }

            AccessRights roomAccess =
                floor.m_roomAccessRights[position.m_x, position.m_y];
            AccessRights logisticsAccess =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    position.m_x,
                    position.m_y];

            // Match Floor.IsAccessible(...): room access is a hard limit, while the
            // logistics BIOHAZARD paint is a special vanilla exception and does not by
            // itself block a PATIENT_PROCEDURE route. This keeps real BIOHAZARD rooms
            // (including isolation) unavailable to ambulatory HPL need searches, while a
            // normal room merely painted BIOHAZARD remains usable/traversable as vanilla
            // navigation intends. STAFF / STAFF_ONLY logistics paint remains forbidden.
            if ((int)roomAccess > (int)accessRights)
            {
                return false;
            }

            if ((int)logisticsAccess > (int)accessRights &&
                logisticsAccess != AccessRights.BIOHAZARD)
            {
                return false;
            }

            return true;
        }

        private static bool IsInsideFloor(Floor floor, Vector2i position)
        {
            return floor != null &&
                position.m_x >= 0 &&
                position.m_y >= 0 &&
                position.m_x < floor.Size.m_x &&
                position.m_y < floor.Size.m_y;
        }
    }

    internal static class NeedDestinationDiagnostics
    {
        internal static string BuildBladder(Entity patient)
        {
            return Build(
                patient,
                "wc",
                new string[] { "wc", "ward", "any_office" },
                false,
                true,
                null);
        }

        internal static string BuildCafeteria(Entity patient)
        {
            return Build(
                patient,
                "food",
                null,
                true,
                true,
                PatientCafeteriaRules.CafeteriaRoomTag);
        }

        internal static string GetDepartmentId(Department department)
        {
            if (department == null)
            {
                return "<none>";
            }

            GameDBDepartment departmentType = department.GetDepartmentType();
            return departmentType == null
                ? "<none>"
                : departmentType.DatabaseID.ToString();
        }

        private static string Build(
            Entity patient,
            string tag,
            string[] roomTags,
            bool allowedOutsideOfRoom,
            bool needsToBeFree,
            string ignoreDepartmentForRoomTag)
        {
            if (patient == null || Hospital.Instance == null)
            {
                return " | diag=unavailable";
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            if (walk == null || behavior == null)
            {
                return " | diag=missing-components";
            }

            int floorIndex = walk.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return " | diag=invalid-floor";
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null)
            {
                return " | diag=null-floor";
            }

            Department preferredDepartment = behavior.m_state.m_department == null
                ? null
                : behavior.m_state.m_department.GetEntity();
            string patientDepartmentId = GetDepartmentId(preferredDepartment);
            Vector2i origin = walk.GetCurrentTile();
            AccessRights accessRights = AccessRights.PATIENT_PROCEDURE;

            int tagged = 0;
            int invalidObject = 0;
            int busy = 0;
            int accessBlocked = 0;
            int invalidRoom = 0;
            int roomTagBlocked = 0;
            int objectSameDepartment = 0;
            int objectOtherDepartment = 0;
            int objectNoDepartment = 0;
            int roomSameDepartment = 0;
            int roomOtherDepartment = 0;
            int roomNoDepartment = 0;
            int departmentBlocked = 0;
            int pathBlocked = 0;
            int reachable = 0;
            int cafeteriaTagged = 0;
            int cafeteriaDepartmentAllowed = 0;
            int cafeteriaReachable = 0;

            Dictionary<string, bool> objectDepartmentIds = new Dictionary<string, bool>();
            Dictionary<string, bool> roomDepartmentIds = new Dictionary<string, bool>();

            for (int x = 1; x < floor.Size.m_x - 1; x++)
            {
                for (int y = 1; y < floor.Size.m_y - 1; y++)
                {
                    for (int objectIndex = 0; objectIndex <= 1; objectIndex++)
                    {
                        TileObject candidate = objectIndex == 0
                            ? floor.m_tileObjects[x, y].m_centerObject
                            : floor.m_tileObjects[x, y].m_attachmentObject;

                        if (candidate == null || !candidate.HasTag(tag))
                        {
                            continue;
                        }

                        tagged++;

                        if (candidate.IsBroken() || !candidate.IsValid())
                        {
                            invalidObject++;
                            continue;
                        }

                        if (needsToBeFree && (candidate.User != null || candidate.Owner != null))
                        {
                            busy++;
                            continue;
                        }

                        Vector2i candidatePosition = candidate.m_state.m_position;
                        Vector2i routePosition = candidatePosition;
                        bool accessAllowed =
                            PatientProcedureNeedAccessRules.IsCandidateAccessAllowed(
                                floor,
                                origin,
                                candidatePosition,
                                accessRights,
                                tag);

                        if (tag == "food")
                        {
                            accessAllowed =
                                PatientProcedureNeedAccessRules.IsFoodDestinationAllowed(
                                    floor,
                                    candidate,
                                    accessRights,
                                    out routePosition);
                        }
                        else if (tag == "wc")
                        {
                            accessAllowed =
                                PatientProcedureNeedAccessRules.IsObjectAndUseDestinationAllowed(
                                    floor,
                                    candidate,
                                    accessRights,
                                    out routePosition);
                        }

                        if (!accessAllowed)
                        {
                            accessBlocked++;
                            continue;
                        }

                        Room room = floor.m_roomTiles[
                            candidatePosition.m_x,
                            candidatePosition.m_y];

                        bool validRoom =
                            (allowedOutsideOfRoom && room == null) ||
                            (room != null &&
                                (room.m_roomPersistentData.m_valid == RoomValidity.OK ||
                                 room.m_roomPersistentData.m_valid == RoomValidity.MISSING_STAFF));

                        if (!validRoom)
                        {
                            invalidRoom++;
                            continue;
                        }

                        bool allowedRoomTag = allowedOutsideOfRoom && room == null;
                        if (roomTags != null && room != null)
                        {
                            for (int roomTagIndex = 0;
                                roomTagIndex < roomTags.Length;
                                roomTagIndex++)
                            {
                                if (room.m_roomPersistentData.m_roomType.Entry.HasTag(
                                    roomTags[roomTagIndex]))
                                {
                                    allowedRoomTag = true;
                                    break;
                                }
                            }
                        }

                        if (roomTags != null && !allowedRoomTag)
                        {
                            roomTagBlocked++;
                            continue;
                        }

                        bool cafeteriaRoom = PatientCafeteriaRules.IsCafeteriaRoom(room);
                        if (cafeteriaRoom)
                        {
                            cafeteriaTagged++;
                        }

                        Department objectDepartment = candidate.m_state.m_department == null
                            ? null
                            : candidate.m_state.m_department.GetEntity();
                        Department roomDepartment =
                            room == null ||
                            room.m_roomPersistentData == null ||
                            room.m_roomPersistentData.m_department == null
                                ? null
                                : room.m_roomPersistentData.m_department.GetEntity();

                        CountDepartment(
                            objectDepartment,
                            preferredDepartment,
                            objectDepartmentIds,
                            ref objectSameDepartment,
                            ref objectOtherDepartment,
                            ref objectNoDepartment);
                        CountDepartment(
                            roomDepartment,
                            preferredDepartment,
                            roomDepartmentIds,
                            ref roomSameDepartment,
                            ref roomOtherDepartment,
                            ref roomNoDepartment);

                        bool departmentAllowed = true;
                        if (room != null && preferredDepartment != null &&
                            objectDepartment != preferredDepartment &&
                            (ignoreDepartmentForRoomTag == null ||
                             !room.m_roomPersistentData.m_roomType.Entry.HasTag(
                                 ignoreDepartmentForRoomTag)))
                        {
                            departmentAllowed = false;
                            departmentBlocked++;
                        }

                        if (!departmentAllowed)
                        {
                            continue;
                        }

                        if (cafeteriaRoom)
                        {
                            cafeteriaDepartmentAllowed++;
                        }

                        int distance = (int)GridMap.GetInstance().GetDistance(
                            floorIndex,
                            origin,
                            candidate.GetFloorIndex(),
                            routePosition,
                            accessRights);

                        if (distance == -1)
                        {
                            pathBlocked++;
                            continue;
                        }

                        reachable++;
                        if (cafeteriaRoom)
                        {
                            cafeteriaReachable++;
                        }
                    }
                }
            }

            StringBuilder result = new StringBuilder();
            result.Append(" | diag-tag=").Append(tag);
            result.Append(" | patientDpt=").Append(patientDepartmentId);
            result.Append(" | diagFloor=").Append(floorIndex);
            result.Append(" | tagged=").Append(tagged);
            result.Append(" | invalid=").Append(invalidObject);
            result.Append(" | busy=").Append(busy);
            result.Append(" | accessBlocked=").Append(accessBlocked);
            result.Append(" | invalidRoom=").Append(invalidRoom);
            result.Append(" | roomTagBlocked=").Append(roomTagBlocked);
            result.Append(" | objectDptSame=").Append(objectSameDepartment);
            result.Append(" | objectDptOther=").Append(objectOtherDepartment);
            result.Append(" | objectDptNone=").Append(objectNoDepartment);
            result.Append(" | roomDptSame=").Append(roomSameDepartment);
            result.Append(" | roomDptOther=").Append(roomOtherDepartment);
            result.Append(" | roomDptNone=").Append(roomNoDepartment);
            result.Append(" | dptBlocked=").Append(departmentBlocked);
            result.Append(" | pathBlocked=").Append(pathBlocked);
            result.Append(" | reachable=").Append(reachable);

            if (tag == "food")
            {
                result.Append(" | cafeteriaFood=").Append(cafeteriaTagged);
                result.Append(" | cafeteriaDptAllowed=").Append(cafeteriaDepartmentAllowed);
                result.Append(" | cafeteriaReachable=").Append(cafeteriaReachable);
            }

            result.Append(" | objectDpts=").Append(FormatDepartments(objectDepartmentIds));
            result.Append(" | roomDpts=").Append(FormatDepartments(roomDepartmentIds));
            return result.ToString();
        }

        private static void CountDepartment(
            Department candidateDepartment,
            Department preferredDepartment,
            Dictionary<string, bool> ids,
            ref int same,
            ref int other,
            ref int none)
        {
            if (candidateDepartment == null)
            {
                none++;
                return;
            }

            string id = GetDepartmentId(candidateDepartment);
            if (!ids.ContainsKey(id))
            {
                ids[id] = true;
            }

            if (candidateDepartment == preferredDepartment)
            {
                same++;
            }
            else
            {
                other++;
            }
        }

        private static string FormatDepartments(Dictionary<string, bool> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return "<none>";
            }

            StringBuilder text = new StringBuilder();
            foreach (string id in ids.Keys)
            {
                if (text.Length > 0)
                {
                    text.Append(',');
                }
                text.Append(id);
            }
            return text.ToString();
        }
    }

    [HarmonyPatch]
    internal static class PatientProcedureNeedSearchPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(MapScriptInterface),
                "FindClosestCenterObjectWithTagShortestPath",
                new Type[]
                {
                    typeof(Vector2i),
                    typeof(int),
                    typeof(string),
                    typeof(AccessRights),
                    typeof(string[]),
                    typeof(bool),
                    typeof(bool),
                    typeof(Department),
                    typeof(string)
                });
        }

        private static void Postfix(
            Vector2i position,
            int floorIndex,
            string tag,
            AccessRights accessRights,
            string[] roomTags,
            bool allowedOutsideOfRoom,
            bool needsToBeFree,
            Department preferredDepartment,
            string ignoreDepartmentForRoomTag,
            ref TileObject __result)
        {
            if (accessRights != AccessRights.PATIENT_PROCEDURE ||
                (tag != "wc" && tag != "food"))
            {
                return;
            }

            if (Hospital.Instance == null ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null)
            {
                return;
            }

            // Need scripts walk to GetDefaultUsePosition(), not the object's storage tile.
            // Validate the exact destination for both food and WC before the procedure starts.
            if (__result != null)
            {
                Vector2i resultUseTile;
                if (PatientProcedureNeedAccessRules.IsObjectAndUseDestinationAllowed(
                        floor,
                        __result,
                        accessRights,
                        out resultUseTile))
                {
                    int resultDistance = (int)GridMap.GetInstance().GetDistance(
                        floorIndex,
                        position,
                        __result.GetFloorIndex(),
                        resultUseTile,
                        accessRights);

                    if (resultDistance != -1)
                    {
                        return;
                    }
                }

                __result = null;
            }

            int bestDistance = int.MaxValue;
            TileObject bestObject = null;

            for (int x = 1; x < floor.Size.m_x - 1; x++)
            {
                for (int y = 1; y < floor.Size.m_y - 1; y++)
                {
                    for (int objectIndex = 0; objectIndex <= 1; objectIndex++)
                    {
                        TileObject candidate = objectIndex == 0
                            ? floor.m_tileObjects[x, y].m_centerObject
                            : floor.m_tileObjects[x, y].m_attachmentObject;

                        if (candidate == null)
                        {
                            continue;
                        }

                        Vector2i candidatePosition = candidate.m_state.m_position;
                        Vector2i routePosition = candidatePosition;
                        bool accessAllowed =
                            PatientProcedureNeedAccessRules.IsCandidateAccessAllowed(
                                floor,
                                position,
                                candidatePosition,
                                accessRights,
                                tag);

                        if (tag == "food")
                        {
                            accessAllowed =
                                PatientProcedureNeedAccessRules.IsFoodDestinationAllowed(
                                    floor,
                                    candidate,
                                    accessRights,
                                    out routePosition);
                        }
                        else if (tag == "wc")
                        {
                            accessAllowed =
                                PatientProcedureNeedAccessRules.IsObjectAndUseDestinationAllowed(
                                    floor,
                                    candidate,
                                    accessRights,
                                    out routePosition);
                        }

                        if (!accessAllowed)
                        {
                            continue;
                        }

                        Room room = floor.m_roomTiles[
                            candidatePosition.m_x,
                            candidatePosition.m_y];

                        bool validRoom =
                            (allowedOutsideOfRoom && room == null) ||
                            (room != null &&
                                (room.m_roomPersistentData.m_valid == RoomValidity.OK ||
                                 room.m_roomPersistentData.m_valid == RoomValidity.MISSING_STAFF));

                        if (!validRoom)
                        {
                            continue;
                        }

                        bool allowedRoomTag = allowedOutsideOfRoom && room == null;
                        if (roomTags != null && room != null)
                        {
                            for (int roomTagIndex = 0;
                                roomTagIndex < roomTags.Length;
                                roomTagIndex++)
                            {
                                if (room.m_roomPersistentData.m_roomType.Entry.HasTag(
                                    roomTags[roomTagIndex]))
                                {
                                    allowedRoomTag = true;
                                    break;
                                }
                            }
                        }

                        if (roomTags != null && !allowedRoomTag)
                        {
                            continue;
                        }

                        if (!candidate.HasTag(tag) ||
                            candidate.IsBroken() ||
                            !candidate.IsValid() ||
                            (((candidate.User != null || candidate.Owner != null) &&
                              needsToBeFree)))
                        {
                            continue;
                        }

                        if (room != null && preferredDepartment != null)
                        {
                            Department candidateDepartment =
                                candidate.m_state.m_department == null
                                    ? null
                                    : candidate.m_state.m_department.GetEntity();

                            if (candidateDepartment != preferredDepartment &&
                                (ignoreDepartmentForRoomTag == null ||
                                 !room.m_roomPersistentData.m_roomType.Entry.HasTag(
                                     ignoreDepartmentForRoomTag)))
                            {
                                continue;
                            }
                        }

                        int distance = (int)GridMap.GetInstance().GetDistance(
                            floorIndex,
                            position,
                            candidate.GetFloorIndex(),
                            routePosition,
                            accessRights);

                        if (distance == -1 || distance >= bestDistance)
                        {
                            continue;
                        }

                        bestDistance = distance;
                        bestObject = candidate;
                    }
                }
            }

            if (bestObject != null)
            {
                __result = bestObject;
            }
        }
    }

    [HarmonyPatch]
    internal static class PatientProcedureNeedDepartmentFoodSearchPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(MapScriptInterface),
                "FindClosestObjectWithTag",
                new Type[]
                {
                    typeof(Vector2i),
                    typeof(int),
                    typeof(Department),
                    typeof(string),
                    typeof(AccessRights),
                    typeof(string[]),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool)
                });
        }

        private static void Postfix(
            Vector2i position,
            int floorIndex,
            string tag,
            AccessRights accessRights,
            ref TileObject __result)
        {
            if (__result == null ||
                accessRights != AccessRights.PATIENT_PROCEDURE ||
                tag != "food" ||
                Hospital.Instance == null)
            {
                return;
            }

            int targetFloorIndex = __result.GetFloorIndex();
            if (targetFloorIndex < 0 ||
                targetFloorIndex >= Hospital.Instance.m_floors.Count)
            {
                __result = null;
                return;
            }

            Floor targetFloor = Hospital.Instance.m_floors[targetFloorIndex];
            Vector2i useTile;
            if (!PatientProcedureNeedAccessRules.IsFoodDestinationAllowed(
                    targetFloor,
                    __result,
                    accessRights,
                    out useTile))
            {
                __result = null;
                return;
            }

            int distance = (int)GridMap.GetInstance().GetDistance(
                floorIndex,
                position,
                targetFloorIndex,
                useTile,
                accessRights);

            if (distance == -1)
            {
                __result = null;
            }
        }
    }

}
