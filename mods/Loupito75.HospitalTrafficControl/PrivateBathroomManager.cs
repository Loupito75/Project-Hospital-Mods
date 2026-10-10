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
            if (!RoomGeometry.IsInsideFloor(floor, objectTile))
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
            if (!RoomGeometry.IsInsideFloor(floor, objectTile))
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
            Room patientRoom = GetHospitalizationRoom(character);
            if (patientRoom == null)
            {
                return null;
            }

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
                        floor,
                        origin,
                        patientRoom,
                        procedure,
                        accessRights,
                        ref best,
                        ref bestDistance);

                    TryOwnPrivateCandidate(
                        objects.m_attachmentObject,
                        floor,
                        origin,
                        patientRoom,
                        procedure,
                        accessRights,
                        ref best,
                        ref bestDistance);
                }
            }

            return best;
        }

        internal static TileObject FindAllowedBathroomReplacement(
            Entity character,
            AccessRights accessRights)
        {
            if (character == null || Hospital.Instance == null)
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
            Room patientRoom = hospitalized
                ? GetHospitalizationRoom(character)
                : null;
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
                        null,
                        accessRights,
                        hospitalized,
                        patientRoom,
                        ref best,
                        ref bestDistance);

                    TryCandidate(
                        objects.m_attachmentObject,
                        floor,
                        origin,
                        null,
                        accessRights,
                        hospitalized,
                        patientRoom,
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
            Room patientRoom = hospitalized
                ? GetHospitalizationRoom(character)
                : null;
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
                        hospitalized,
                        patientRoom,
                        ref best,
                        ref bestDistance);

                    TryCandidate(
                        objects.m_attachmentObject,
                        floor,
                        origin,
                        procedure,
                        accessRights,
                        hospitalized,
                        patientRoom,
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
            bool hospitalized,
            Room patientRoom,
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
            if (!RoomGeometry.IsInsideFloor(floor, objectTile))
            {
                return;
            }

            Vector2f usePosition = candidate.GetDefaultUsePosition();
            Vector2i useTile = new Vector2i(
                (int)(usePosition.m_x + 0.5f),
                (int)(usePosition.m_y + 0.5f));

            if (!RoomGeometry.IsInsideFloor(floor, useTile) ||
                CannotBeatBestDistance(origin, useTile, bestDistance))
            {
                return;
            }

            Room room = floor.m_roomTiles[objectTile.m_x, objectTile.m_y];
            if (!IsAllowedRoom(
                    room,
                    procedure == null ? null : procedure.RequiredRoomTags,
                    accessRights))
            {
                return;
            }

            // Same native connection check as the public WC API, but only
            // once per candidate instead of checking private status and
            // then checking the connected patient room again.
            Room connectedHospitalRoom;
            if (TryGetConnectedHospitalizationRoom(
                    room, floor, out connectedHospitalRoom) &&
                (!hospitalized ||
                 !object.ReferenceEquals(connectedHospitalRoom, patientRoom)))
            {
                return;
            }

            if (!IsAccessAllowed(floor, objectTile, accessRights) ||
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
            Floor floor,
            Vector2i origin,
            Room patientRoom,
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
                candidate.Owner != null)
            {
                return;
            }

            Vector2i objectTile = candidate.m_state.m_position;
            if (!RoomGeometry.IsInsideFloor(floor, objectTile))
            {
                return;
            }

            Vector2f usePosition = candidate.GetDefaultUsePosition();
            Vector2i useTile = new Vector2i(
                (int)(usePosition.m_x + 0.5f),
                (int)(usePosition.m_y + 0.5f));
            if (!RoomGeometry.IsInsideFloor(floor, useTile) ||
                CannotBeatBestDistance(origin, useTile, bestDistance))
            {
                return;
            }

            Room room = floor.m_roomTiles[objectTile.m_x, objectTile.m_y];
            if (!IsAllowedRoom(room, procedure.RequiredRoomTags, accessRights))
            {
                return;
            }

            Room connectedHospitalRoom;
            if (!TryGetConnectedHospitalizationRoom(
                    room, floor, out connectedHospitalRoom) ||
                !object.ReferenceEquals(connectedHospitalRoom, patientRoom))
            {
                return;
            }

            if (!IsAccessAllowed(floor, objectTile, accessRights) ||
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

        // Native GridMap distances are compared after truncation to int.
        // On the same floor, an exact route cannot beat the straight-line
        // lower bound; this preserves the original >= tie rule. No maximum
        // distance is introduced, and eligible winners still use GridMap.
        private static bool CannotBeatBestDistance(
            Vector2i origin,
            Vector2i destination,
            int bestDistance)
        {
            if (bestDistance == int.MaxValue)
            {
                return false;
            }

            long dx = (long)destination.m_x - origin.m_x;
            long dy = (long)destination.m_y - origin.m_y;
            return dx * dx + dy * dy >=
                   (long)bestDistance * bestDistance;
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
            if (!RoomGeometry.IsInsideFloor(floor, tile))
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
            if (!RoomGeometry.IsInsideFloor(floor, tile))
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
            if (!RoomGeometry.IsInsideFloor(floor, next))
            {
                return;
            }

            Room nextRoom = floor.m_roomTiles[next.m_x, next.m_y];
            if (object.ReferenceEquals(nextRoom, bathroom) ||
                !RoomGeometry.IsBoundaryTraversable(floor, current, next))
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

    }
}
