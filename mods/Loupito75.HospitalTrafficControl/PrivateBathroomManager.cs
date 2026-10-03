using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class PrivateBathroomManager
    {
        internal static bool IsBladderProcedure(GameDBProcedure procedure)
        {
            if (procedure == null || Database.Instance == null)
            {
                return false;
            }

            GameDBNeed bladderNeed =
                Database.Instance.GetEntry<GameDBNeed>("NEED_BLADDER");

            return bladderNeed != null &&
                   object.ReferenceEquals(bladderNeed.Procedure, procedure);
        }

        internal static bool IsHospitalizedPatient(Entity character)
        {
            if (character == null)
            {
                return false;
            }

            HospitalizationComponent hospitalization =
                character.GetComponent<HospitalizationComponent>();

            return hospitalization != null && hospitalization.IsHospitalized();
        }

        internal static bool IsPrivateHospitalBathroom(TileObject wc)
        {
            if (wc == null ||
                wc.m_state == null ||
                !wc.HasTag("wc") ||
                Hospital.Instance == null)
            {
                return false;
            }

            int floorIndex = wc.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Vector2i objectTile = wc.m_state.m_position;
            if (!IsInsideFloor(floor, objectTile))
            {
                return false;
            }

            Room room = floor.m_roomTiles[objectTile.m_x, objectTile.m_y];
            return IsPrivateHospitalBathroom(room, floor);
        }

        internal static bool IsPrivateHospitalBathroom(Room room, Floor floor)
        {
            Room connectedHospitalizationRoom;
            return TryGetConnectedHospitalizationRoom(
                room,
                floor,
                out connectedHospitalizationRoom);
        }

        internal static bool IsPrivateHospitalBathroomForPatient(
            TileObject wc,
            Entity patient)
        {
            if (!IsHospitalizedPatient(patient) ||
                wc == null ||
                wc.m_state == null ||
                Hospital.Instance == null)
            {
                return false;
            }

            Room patientRoom = GetHospitalizationRoom(patient);
            if (patientRoom == null)
            {
                return false;
            }

            int floorIndex = wc.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Vector2i objectTile = wc.m_state.m_position;
            if (!IsInsideFloor(floor, objectTile))
            {
                return false;
            }

            Room bathroom = floor.m_roomTiles[objectTile.m_x, objectTile.m_y];
            Room connectedHospitalizationRoom;
            return TryGetConnectedHospitalizationRoom(
                       bathroom,
                       floor,
                       out connectedHospitalizationRoom) &&
                   object.ReferenceEquals(
                       connectedHospitalizationRoom,
                       patientRoom);
        }

        internal static TileObject FindOwnPrivateBathroom(
            Entity character,
            GameDBProcedure procedure,
            AccessRights accessRights)
        {
            if (!IsHospitalizedPatient(character) ||
                procedure == null ||
                Hospital.Instance == null)
            {
                return null;
            }

            HospitalizationComponent hospitalization =
                character.GetComponent<HospitalizationComponent>();
            TileObject bed =
                hospitalization == null ||
                hospitalization.m_state == null ||
                hospitalization.m_state.m_bed == null
                    ? null
                    : hospitalization.m_state.m_bed.GetEntity();
            WalkComponent walk = character.GetComponent<WalkComponent>();

            if (bed == null || walk == null)
            {
                return null;
            }

            int floorIndex = walk.GetFloorIndex();
            if (floorIndex != bed.GetFloorIndex() ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Vector2i origin = walk.GetCurrentTile();
            int bestDistance = int.MaxValue;
            TileObject best = null;

            for (int x = 0; x < floor.Size.m_x; x++)
            {
                for (int y = 0; y < floor.Size.m_y; y++)
                {
                    TileObjects objects = floor.m_tileObjects[x, y];
                    if (objects == null)
                    {
                        continue;
                    }

                    TryOwnPrivateCandidate(
                        objects.m_centerObject,
                        character,
                        floor,
                        origin,
                        procedure,
                        accessRights,
                        ref best,
                        ref bestDistance);

                    TryOwnPrivateCandidate(
                        objects.m_attachmentObject,
                        character,
                        floor,
                        origin,
                        procedure,
                        accessRights,
                        ref best,
                        ref bestDistance);
                }
            }

            return best;
        }

        internal static TileObject FindAllowedBladderReplacement(
            Entity character,
            GameDBProcedure procedure,
            AccessRights accessRights)
        {
            if (character == null ||
                procedure == null ||
                Hospital.Instance == null)
            {
                return null;
            }

            WalkComponent walk = character.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return null;
            }

            int floorIndex = walk.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Vector2i origin = walk.GetCurrentTile();
            bool hospitalized = IsHospitalizedPatient(character);
            int bestDistance = int.MaxValue;
            TileObject best = null;

            for (int x = 0; x < floor.Size.m_x; x++)
            {
                for (int y = 0; y < floor.Size.m_y; y++)
                {
                    TileObjects objects = floor.m_tileObjects[x, y];
                    if (objects == null)
                    {
                        continue;
                    }

                    TryCandidate(
                        objects.m_centerObject,
                        floor,
                        origin,
                        procedure,
                        accessRights,
                        character,
                        hospitalized,
                        ref best,
                        ref bestDistance);

                    TryCandidate(
                        objects.m_attachmentObject,
                        floor,
                        origin,
                        procedure,
                        accessRights,
                        character,
                        hospitalized,
                        ref best,
                        ref bestDistance);
                }
            }

            if (best == null && accessRights == AccessRights.STAFF_ONLY)
            {
                return FindAllowedBladderReplacement(
                    character,
                    procedure,
                    AccessRights.STAFF);
            }

            return best;
        }

        private static void TryCandidate(
            TileObject candidate,
            Floor floor,
            Vector2i origin,
            GameDBProcedure procedure,
            AccessRights accessRights,
            Entity character,
            bool hospitalized,
            ref TileObject best,
            ref int bestDistance)
        {
            if (candidate == null ||
                candidate.m_state == null ||
                !candidate.HasTag("wc") ||
                candidate.IsBroken() ||
                !candidate.IsValid() ||
                candidate.User != null ||
                candidate.Owner != null)
            {
                return;
            }

            Vector2i objectTile = candidate.m_state.m_position;
            if (!IsInsideFloor(floor, objectTile))
            {
                return;
            }

            Room room = floor.m_roomTiles[objectTile.m_x, objectTile.m_y];
            if (!IsAllowedRoom(room, procedure.RequiredRoomTags, accessRights))
            {
                return;
            }

            if (IsPrivateHospitalBathroom(candidate) &&
                (!hospitalized ||
                 !IsPrivateHospitalBathroomForPatient(candidate, character)))
            {
                return;
            }

            Vector2f usePosition = candidate.GetDefaultUsePosition();
            Vector2i useTile = new Vector2i(
                (int)(usePosition.m_x + 0.5f),
                (int)(usePosition.m_y + 0.5f));

            if (!IsInsideFloor(floor, useTile) ||
                !IsAccessAllowed(floor, objectTile, accessRights) ||
                !IsAccessAllowed(floor, useTile, accessRights))
            {
                return;
            }

            int distance = (int)GridMap.GetInstance().GetDistance(
                floor.m_floorIndex,
                origin,
                candidate.GetFloorIndex(),
                useTile,
                accessRights);

            if (distance == -1 || distance >= bestDistance)
            {
                return;
            }

            bestDistance = distance;
            best = candidate;
        }

        private static void TryOwnPrivateCandidate(
            TileObject candidate,
            Entity character,
            Floor floor,
            Vector2i origin,
            GameDBProcedure procedure,
            AccessRights accessRights,
            ref TileObject best,
            ref int bestDistance)
        {
            if (candidate == null ||
                candidate.m_state == null ||
                !candidate.HasTag("wc") ||
                candidate.IsBroken() ||
                !candidate.IsValid() ||
                candidate.User != null ||
                candidate.Owner != null ||
                !IsPrivateHospitalBathroomForPatient(candidate, character))
            {
                return;
            }

            Vector2i objectTile = candidate.m_state.m_position;
            if (!IsInsideFloor(floor, objectTile))
            {
                return;
            }

            Room room = floor.m_roomTiles[objectTile.m_x, objectTile.m_y];
            if (!IsAllowedRoom(room, procedure.RequiredRoomTags, accessRights))
            {
                return;
            }

            Vector2f usePosition = candidate.GetDefaultUsePosition();
            Vector2i useTile = new Vector2i(
                (int)(usePosition.m_x + 0.5f),
                (int)(usePosition.m_y + 0.5f));

            if (!IsInsideFloor(floor, useTile) ||
                !IsAccessAllowed(floor, objectTile, accessRights) ||
                !IsAccessAllowed(floor, useTile, accessRights))
            {
                return;
            }

            int distance = (int)GridMap.GetInstance().GetDistance(
                floor.m_floorIndex,
                origin,
                candidate.GetFloorIndex(),
                useTile,
                accessRights);

            if (distance == -1 || distance >= bestDistance)
            {
                return;
            }

            bestDistance = distance;
            best = candidate;
        }

        private static Room GetHospitalizationRoom(Entity patient)
        {
            HospitalizationComponent hospitalization =
                patient == null
                    ? null
                    : patient.GetComponent<HospitalizationComponent>();

            TileObject bed =
                hospitalization == null ||
                hospitalization.m_state == null ||
                hospitalization.m_state.m_bed == null
                    ? null
                    : hospitalization.m_state.m_bed.GetEntity();

            if (bed == null)
            {
                return null;
            }

            return MapScriptInterface.Instance.GetRoomAt(
                bed.m_state.m_position,
                bed.GetFloorIndex());
        }

        private static bool TryGetConnectedHospitalizationRoom(
            Room room,
            Floor floor,
            out Room connectedHospitalizationRoom)
        {
            connectedHospitalizationRoom = null;

            if (room == null ||
                floor == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType.Entry == null ||
                !room.m_roomPersistentData.m_roomType.Entry.HasTag("wc") ||
                !ContainsProcedureControlledWc(room, floor))
            {
                return false;
            }

            HashSet<Room> connectedRooms = new HashSet<Room>();
            bool openToUnzonedSpace = false;

            for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                 x <= room.m_roomPersistentData.m_positionTop.m_x;
                 x++)
            {
                for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                     y <= room.m_roomPersistentData.m_positionTop.m_y;
                     y++)
                {
                    Vector2i current = new Vector2i(x, y);
                    if (!room.IsPositionInRoom(current))
                    {
                        continue;
                    }

                    CheckBoundary(
                        floor,
                        room,
                        current,
                        new Vector2i(x - 1, y),
                        connectedRooms,
                        ref openToUnzonedSpace);
                    CheckBoundary(
                        floor,
                        room,
                        current,
                        new Vector2i(x + 1, y),
                        connectedRooms,
                        ref openToUnzonedSpace);
                    CheckBoundary(
                        floor,
                        room,
                        current,
                        new Vector2i(x, y - 1),
                        connectedRooms,
                        ref openToUnzonedSpace);
                    CheckBoundary(
                        floor,
                        room,
                        current,
                        new Vector2i(x, y + 1),
                        connectedRooms,
                        ref openToUnzonedSpace);
                }
            }

            if (openToUnzonedSpace || connectedRooms.Count != 1)
            {
                return false;
            }

            foreach (Room connectedRoom in connectedRooms)
            {
                GameDBRoomType roomType =
                    connectedRoom == null ||
                    connectedRoom.m_roomPersistentData == null
                        ? null
                        : connectedRoom.m_roomPersistentData.m_roomType.Entry;

                if (roomType == null || !roomType.HasTag("hospitalization"))
                {
                    return false;
                }

                connectedHospitalizationRoom = connectedRoom;
                return true;
            }

            return false;
        }

        private static bool IsAllowedRoom(
            Room room,
            string[] requiredRoomTags,
            AccessRights accessRights)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType.Entry == null)
            {
                return false;
            }

            RoomValidity validity = room.m_roomPersistentData.m_valid;
            bool validityAllowed =
                validity == RoomValidity.OK ||
                validity == RoomValidity.MISSING_STAFF ||
                (validity == RoomValidity.INACCESSIBLE_PATIENTS &&
                 (int)accessRights >= (int)AccessRights.PATIENT_PROCEDURE);

            if (!validityAllowed)
            {
                return false;
            }

            if (requiredRoomTags == null || requiredRoomTags.Length == 0)
            {
                return true;
            }

            GameDBRoomType roomType = room.m_roomPersistentData.m_roomType.Entry;
            for (int i = 0; i < requiredRoomTags.Length; i++)
            {
                if (roomType.HasTag(requiredRoomTags[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsProcedureControlledWc(Room room, Floor floor)
        {
            List<TileObject> objects = room.GetAllObjects(floor);
            for (int i = 0; i < objects.Count; i++)
            {
                TileObject candidate = objects[i];
                if (candidate == null ||
                    candidate.m_state == null ||
                    !candidate.HasTag("wc"))
                {
                    continue;
                }

                Vector2i objectTile = candidate.m_state.m_position;
                Vector2f usePosition = candidate.GetDefaultUsePosition();
                Vector2i useTile = new Vector2i(
                    (int)(usePosition.m_x + 0.5f),
                    (int)(usePosition.m_y + 0.5f));

                if (IsProcedureControlledTile(floor, objectTile) ||
                    IsProcedureControlledTile(floor, useTile))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsProcedureControlledTile(Floor floor, Vector2i tile)
        {
            if (!IsInsideFloor(floor, tile))
            {
                return false;
            }

            return floor.m_roomAccessRights[tile.m_x, tile.m_y] ==
                       AccessRights.PATIENT_PROCEDURE ||
                   floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                       tile.m_x,
                       tile.m_y] == AccessRights.PATIENT_PROCEDURE;
        }

        private static bool IsAccessAllowed(
            Floor floor,
            Vector2i tile,
            AccessRights accessRights)
        {
            if (!IsInsideFloor(floor, tile))
            {
                return false;
            }

            AccessRights roomAccess = floor.m_roomAccessRights[tile.m_x, tile.m_y];
            AccessRights logistics =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    tile.m_x,
                    tile.m_y];

            return (int)roomAccess <= (int)accessRights &&
                   (int)logistics <= (int)accessRights;
        }

        private static void CheckBoundary(
            Floor floor,
            Room bathroom,
            Vector2i current,
            Vector2i next,
            HashSet<Room> connectedRooms,
            ref bool openToUnzonedSpace)
        {
            if (!IsInsideFloor(floor, next))
            {
                return;
            }

            Room nextRoom = floor.m_roomTiles[next.m_x, next.m_y];
            if (object.ReferenceEquals(nextRoom, bathroom) ||
                !IsBoundaryTraversable(floor, current, next))
            {
                return;
            }

            if (nextRoom == null)
            {
                openToUnzonedSpace = true;
                return;
            }

            connectedRooms.Add(nextRoom);
        }

        private static bool IsBoundaryTraversable(
            Floor floor,
            Vector2i current,
            Vector2i next)
        {
            return floor.IsAccessible(
                       current,
                       next,
                       current,
                       current,
                       (int)AccessRights.STAFF_ONLY,
                       true,
                       true) ||
                   floor.IsAccessible(
                       next,
                       current,
                       next,
                       next,
                       (int)AccessRights.STAFF_ONLY,
                       true,
                       true);
        }

        private static bool IsInsideFloor(Floor floor, Vector2i tile)
        {
            return floor != null &&
                   tile.m_x >= 0 &&
                   tile.m_y >= 0 &&
                   tile.m_x < floor.Size.m_x &&
                   tile.m_y < floor.Size.m_y;
        }
    }
}
