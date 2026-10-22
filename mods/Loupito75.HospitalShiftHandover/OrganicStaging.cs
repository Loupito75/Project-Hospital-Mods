using System;
using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalShiftHandover
{
    internal static class OrganicStaging
    {
        private const int CandidateJitterSteps = 1000;
        private const int PublicAreaRadius = 5;
        private const float PublicAreaIdealDistance = 3.5f;
        private const float PublicAreaMaximumRouteDistance = 7f;
        private const float PublicAreaNearDoorDistance = 2f;
        private const float PublicAreaJitterDistance = 2.5f;

        private sealed class StagingCandidate
        {
            internal Vector2i Position;
            internal int DoorFrontTier;
            internal int DoorNearTier;
            internal int WallTier;
            internal int AnchorTier;
            internal float RouteDistance;
            internal float Score;
        }

        internal static bool RequiresPublicStaging(EmployeeComponent employee, int floorIndex)
        {
            Room workplaceRoom = GetWorkplaceRoom(employee, floorIndex);
            return workplaceRoom != null && IsPatientFacing(workplaceRoom);
        }

        internal static bool TrySelectNearbyTarget(
            Behavior behavior,
            EmployeeComponent employee,
            List<Vector2i> excludedPositions,
            Vector2i anchor,
            int floorIndex,
            out Vector2i selectedTarget,
            out string selectionDetails)
        {
            selectedTarget = anchor;
            selectionDetails = "reason=unavailable";

            if (behavior == null || employee == null || employee.m_state == null ||
                Hospital.Instance == null || Hospital.Instance.m_floors == null ||
                floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                selectionDetails = "reason=context-unavailable";
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null || !IsInBounds(floor, anchor))
            {
                selectionDetails = "reason=anchor-unavailable";
                return false;
            }

            Room workplaceRoom = GetWorkplaceRoom(employee, floorIndex);
            if (workplaceRoom == null)
            {
                selectionDetails = "reason=workplace-room-unavailable";
                return false;
            }

            Vector2i workplaceTarget;
            int workplaceFloor;
            if (!WorkplaceTravelEstimator.TryGetWorkplaceTarget(employee, out workplaceTarget, out workplaceFloor))
            {
                workplaceTarget = Vector2i.ZERO_VECTOR;
                workplaceFloor = floorIndex;
            }

            bool patientFacing = IsPatientFacing(workplaceRoom);
            List<StagingCandidate> candidates = new List<StagingCandidate>();
            if (patientFacing)
            {
                CollectPatientFacingCandidates(
                    candidates,
                    behavior,
                    employee,
                    floor,
                    floorIndex,
                    workplaceRoom,
                    anchor,
                    workplaceTarget,
                    workplaceFloor,
                    excludedPositions);
            }
            else
            {
                CollectStaffRoomCandidates(
                    candidates,
                    behavior,
                    employee,
                    floor,
                    floorIndex,
                    workplaceRoom,
                    anchor,
                    workplaceTarget,
                    workplaceFloor,
                    excludedPositions);
            }

            if (candidates.Count == 0)
            {
                selectionDetails = patientFacing
                    ? "reason=no-public-local-candidate"
                    : "reason=no-staff-room-candidate";
                return false;
            }

            candidates.Sort(delegate(StagingCandidate first, StagingCandidate second)
            {
                // Preserve every valid HSH fallback, but rank physical doorway exposure
                // ahead of cosmetic wall preference. A clear open tile must beat a wall
                // position that blocks either side of a door.
                int comparison = first.DoorFrontTier.CompareTo(second.DoorFrontTier);
                if (comparison != 0) return comparison;
                comparison = first.DoorNearTier.CompareTo(second.DoorNearTier);
                if (comparison != 0) return comparison;
                comparison = first.WallTier.CompareTo(second.WallTier);
                if (comparison != 0) return comparison;
                comparison = first.AnchorTier.CompareTo(second.AnchorTier);
                if (comparison != 0) return comparison;

                if (first.Score < second.Score)
                {
                    return -1;
                }
                if (first.Score > second.Score)
                {
                    return 1;
                }
                if (first.Position.m_y != second.Position.m_y)
                {
                    return first.Position.m_y < second.Position.m_y ? -1 : 1;
                }
                if (first.Position.m_x == second.Position.m_x)
                {
                    return 0;
                }
                return first.Position.m_x < second.Position.m_x ? -1 : 1;
            });

            StagingCandidate selected = candidates[0];
            selectedTarget = selected.Position;
            Room anchorRoom = floor.GetRoomTileSafe(anchor.m_x, anchor.m_y);
            Room selectedRoom = floor.GetRoomTileSafe(selectedTarget.m_x, selectedTarget.m_y);
            selectionDetails =
                "area=" + (patientFacing ? "public-local" : "staff-room") +
                " | candidates=" + candidates.Count +
                " | anchorRoom=" + GetRoomTypeId(anchorRoom) +
                " | selectedRoom=" + GetRoomTypeId(selectedRoom) +
                " | anchorDistance=" + selected.RouteDistance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        private static void CollectStaffRoomCandidates(
            List<StagingCandidate> candidates,
            Behavior behavior,
            EmployeeComponent employee,
            Floor floor,
            int floorIndex,
            Room workplaceRoom,
            Vector2i anchor,
            Vector2i workplaceTarget,
            int workplaceFloor,
            List<Vector2i> excludedPositions)
        {
            if (workplaceRoom == null || workplaceRoom.m_roomPersistentData == null)
            {
                return;
            }

            float jitterDistance = (float)Math.Sqrt(Math.Max(1, workplaceRoom.GetTileCount()));
            if (jitterDistance < 2f)
            {
                jitterDistance = 2f;
            }

            Vector2i bottom = workplaceRoom.m_roomPersistentData.m_positionBottom;
            Vector2i top = workplaceRoom.m_roomPersistentData.m_positionTop;
            for (int x = bottom.m_x; x <= top.m_x; x++)
            {
                for (int y = bottom.m_y; y <= top.m_y; y++)
                {
                    Vector2i candidate = new Vector2i(x, y);
                    if (!workplaceRoom.IsPositionInRoom(candidate))
                    {
                        continue;
                    }

                    AddCandidateIfValid(
                        candidates,
                        behavior,
                        employee,
                        floor,
                        floorIndex,
                        workplaceRoom,
                        anchor,
                        candidate,
                        workplaceTarget,
                        workplaceFloor,
                        excludedPositions,
                        false,
                        jitterDistance);
                }
            }
        }

        private static void CollectPatientFacingCandidates(
            List<StagingCandidate> candidates,
            Behavior behavior,
            EmployeeComponent employee,
            Floor floor,
            int floorIndex,
            Room workplaceRoom,
            Vector2i anchor,
            Vector2i workplaceTarget,
            int workplaceFloor,
            List<Vector2i> excludedPositions)
        {
            // Keep patient-facing staging near the route-derived doorway anchor while allowing
            // nearby corridor, waiting-room and reception tiles to compete.
            for (int deltaX = -PublicAreaRadius; deltaX <= PublicAreaRadius; deltaX++)
            {
                for (int deltaY = -PublicAreaRadius; deltaY <= PublicAreaRadius; deltaY++)
                {
                    Vector2i candidate = new Vector2i(anchor.m_x + deltaX, anchor.m_y + deltaY);
                    if (!IsInBounds(floor, candidate))
                    {
                        continue;
                    }

                    Room candidateRoom = floor.GetRoomTileSafe(candidate.m_x, candidate.m_y);
                    if (!IsAllowedPublicStagingRoom(candidateRoom))
                    {
                        continue;
                    }

                    AddCandidateIfValid(
                        candidates,
                        behavior,
                        employee,
                        floor,
                        floorIndex,
                        workplaceRoom,
                        anchor,
                        candidate,
                        workplaceTarget,
                        workplaceFloor,
                        excludedPositions,
                        true,
                        PublicAreaJitterDistance);
                }
            }
        }

        private static void AddCandidateIfValid(
            List<StagingCandidate> candidates,
            Behavior behavior,
            EmployeeComponent employee,
            Floor floor,
            int floorIndex,
            Room workplaceRoom,
            Vector2i anchor,
            Vector2i candidate,
            Vector2i workplaceTarget,
            int workplaceFloor,
            List<Vector2i> excludedPositions,
            bool patientFacing,
            float jitterDistance)
        {
            if (candidates == null || behavior == null || employee == null || floor == null ||
                !IsInBounds(floor, candidate) || ContainsPosition(excludedPositions, candidate))
            {
                return;
            }

            if (workplaceFloor == floorIndex && candidate == workplaceTarget)
            {
                return;
            }

            Room candidateRoom = floor.GetRoomTileSafe(candidate.m_x, candidate.m_y);
            if (patientFacing)
            {
                if (candidateRoom == workplaceRoom || !IsAllowedPublicStagingRoom(candidateRoom))
                {
                    return;
                }
            }
            else if (candidateRoom != workplaceRoom)
            {
                return;
            }

            if (floor.IsAnyObjectAt(candidate))
            {
                return;
            }

            if (floor.m_mapPersistentData != null && floor.m_mapPersistentData.m_tiles != null &&
                floor.m_mapPersistentData.m_tiles[candidate.m_x, candidate.m_y].m_user != null)
            {
                return;
            }

            GridMap gridMap = GridMap.GetInstance();
            if (gridMap == null)
            {
                return;
            }

            float routeDistance = gridMap.GetDistance(
                floorIndex,
                anchor,
                floorIndex,
                candidate,
                behavior.GetAccessRights());
            if (routeDistance < 0f || routeDistance >= float.MaxValue)
            {
                return;
            }

            if (patientFacing && routeDistance > PublicAreaMaximumRouteDistance)
            {
                return;
            }

            int doorFrontTier = IsDoorFrontTile(floor, candidate) ? 1 : 0;
            int doorNearTier = HasNearbyDoor(floor, candidate) ? 1 : 0;
            int wallTier = HasAdjacentWall(floor, candidate) ? 0 : 1;
            int anchorTier =
                patientFacing && routeDistance < PublicAreaNearDoorDistance ? 1 : 0;

            float jitter = GetCandidateJitter(employee, candidate, patientFacing ? 941 : 887, jitterDistance);
            float score = patientFacing
                ? Math.Abs(routeDistance - PublicAreaIdealDistance) + jitter
                : routeDistance + jitter;

            StagingCandidate stagingCandidate = new StagingCandidate();
            stagingCandidate.Position = candidate;
            stagingCandidate.DoorFrontTier = doorFrontTier;
            stagingCandidate.DoorNearTier = doorNearTier;
            stagingCandidate.WallTier = wallTier;
            stagingCandidate.AnchorTier = anchorTier;
            stagingCandidate.RouteDistance = routeDistance;
            stagingCandidate.Score = score;
            candidates.Add(stagingCandidate);
        }

        private static bool IsDoorFrontTile(Floor floor, Vector2i tile)
        {
            if (!IsInBounds(floor, tile))
            {
                return false;
            }

            // Native wall/door edges are stored on SW/SE edges. Read the matching
            // neighboring edges as well so both standing tiles beside a door are ranked
            // as door frontage instead of only the tile that stores the Door reference.
            TileWalls current = floor.GetWallsSafe(tile.m_x, tile.m_y);
            TileWalls east = tile.m_x + 1 < floor.m_size.m_x
                ? floor.GetWallsSafe(tile.m_x + 1, tile.m_y)
                : null;
            TileWalls north = tile.m_y + 1 < floor.m_size.m_y
                ? floor.GetWallsSafe(tile.m_x, tile.m_y + 1)
                : null;

            return (current != null &&
                    (current.m_doorSE != null || current.m_doorSW != null)) ||
                   (east != null && east.m_doorSE != null) ||
                   (north != null && north.m_doorSW != null);
        }

        private static bool HasNearbyDoor(Floor floor, Vector2i position)
        {
            if (floor == null)
            {
                return false;
            }

            for (int x = position.m_x - 1; x <= position.m_x + 1; x++)
            {
                for (int y = position.m_y - 1; y <= position.m_y + 1; y++)
                {
                    Vector2i tile = new Vector2i(x, y);
                    if (!IsInBounds(floor, tile))
                    {
                        continue;
                    }

                    TileWalls walls = floor.GetWallsSafe(x, y);
                    if (walls != null &&
                        (walls.m_doorSW != null || walls.m_doorSE != null))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasAdjacentWall(Floor floor, Vector2i position)
        {
            if (!IsInBounds(floor, position))
            {
                return false;
            }

            TileWalls current = floor.GetWallsSafe(position.m_x, position.m_y);
            TileWalls north = position.m_y + 1 < floor.m_size.m_y
                ? floor.GetWallsSafe(position.m_x, position.m_y + 1)
                : null;
            TileWalls east = position.m_x + 1 < floor.m_size.m_x
                ? floor.GetWallsSafe(position.m_x + 1, position.m_y)
                : null;

            return (current != null &&
                    (current.m_wallSW != null || current.m_wallSE != null)) ||
                   (north != null && north.m_wallSW != null) ||
                   (east != null && east.m_wallSE != null);
        }

        private static Room GetWorkplaceRoom(EmployeeComponent employee, int floorIndex)
        {
            if (employee == null || employee.m_state == null || employee.m_state.m_workDesk == null ||
                Hospital.Instance == null || Hospital.Instance.m_floors == null ||
                floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            TileObject workDesk = employee.m_state.m_workDesk.GetEntity();
            if (workDesk == null || workDesk.m_state == null || workDesk.GetFloorIndex() != floorIndex)
            {
                return null;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            return floor != null
                ? floor.GetRoomTileSafe(workDesk.m_state.m_position.m_x, workDesk.m_state.m_position.m_y)
                : null;
        }

        private static bool IsPatientFacing(Room room)
        {
            if (room == null || room.m_roomPersistentData == null)
            {
                return true;
            }

            GameDBRoomType roomType = room.m_roomPersistentData.m_roomType.Entry;
            return roomType == null || roomType.AccessRights == AccessRights.PATIENT_PROCEDURE;
        }

        private static bool IsAllowedPublicStagingRoom(Room room)
        {
            if (room == null || room.m_roomPersistentData == null || Database.Instance == null)
            {
                return false;
            }

            GameDBRoomType roomType = room.m_roomPersistentData.m_roomType.Entry;
            if (roomType == null)
            {
                return false;
            }

            GameDBRoomType corridor = Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_CORRIDOR");
            GameDBRoomType waiting = Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_WAITING");
            GameDBRoomType reception = Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_RECEPTION");
            return roomType == corridor || roomType == waiting || roomType == reception;
        }

        private static string GetRoomTypeId(Room room)
        {
            if (room == null || room.m_roomPersistentData == null)
            {
                return "none";
            }

            GameDBRoomType roomType = room.m_roomPersistentData.m_roomType.Entry;
            return roomType != null ? roomType.DatabaseID + string.Empty : "unknown";
        }

        private static bool IsInBounds(Floor floor, Vector2i position)
        {
            return floor != null &&
                   position.m_x >= 0 && position.m_y >= 0 &&
                   position.m_x < floor.m_size.m_x && position.m_y < floor.m_size.m_y;
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

        private static float GetCandidateJitter(
            EmployeeComponent employee,
            Vector2i candidate,
            int salt,
            float maximumDistance)
        {
            if (employee == null || employee.m_state == null || maximumDistance <= 0f)
            {
                return 0f;
            }

            long value = 17L;
            value = value * 31L + employee.m_state.m_workPlacePosition.m_x;
            value = value * 31L + employee.m_state.m_workPlacePosition.m_y;
            value = value * 31L + employee.m_state.m_workPlaceFloorIndex;
            value = value * 31L + employee.m_state.m_salary;
            value = value * 31L + (int)employee.m_state.m_shift;
            value = value * 31L + (int)employee.m_state.m_employeeType;
            value = value * 31L + candidate.m_x;
            value = value * 31L + candidate.m_y;
            value = value * 31L + salt;
            if (DayTime.Instance != null)
            {
                value = value * 31L + DayTime.Instance.GetDay();
            }
            value &= 0x7fffffffL;

            int step = (int)(value % CandidateJitterSteps);
            return maximumDistance * ((float)step / (float)(CandidateJitterSteps - 1));
        }
    }
}
