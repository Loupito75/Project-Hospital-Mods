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
        private const float PublicAreaNearDoorPenalty = 50f;
        private const float PublicAreaJitterDistance = 2.5f;

        private sealed class StagingCandidate
        {
            internal Vector2i Position;
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
            // Keep patient-facing staging local to the real route-derived doorway anchor, but
            // allow nearby corridor, waiting-room and reception tiles to compete. This avoids
            // the old behavior where the anchor room alone strongly favored the tile directly
            // in front of the consultation door.
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

            float jitter = GetCandidateJitter(employee, candidate, patientFacing ? 941 : 887, jitterDistance);
            float score;
            if (patientFacing)
            {
                // Prefer a small, natural buffer away from the door. Near-door tiles remain a
                // safety fallback, but a large penalty ensures any valid position 2+ tiles away
                // wins first. The ideal distance keeps staff local instead of wandering down a
                // long corridor.
                score = Math.Abs(routeDistance - PublicAreaIdealDistance) + jitter;
                if (routeDistance < PublicAreaNearDoorDistance)
                {
                    score += PublicAreaNearDoorPenalty;
                }
            }
            else
            {
                score = routeDistance + jitter;
            }

            StagingCandidate stagingCandidate = new StagingCandidate();
            stagingCandidate.Position = candidate;
            stagingCandidate.RouteDistance = routeDistance;
            stagingCandidate.Score = score;
            candidates.Add(stagingCandidate);
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
