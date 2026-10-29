using System;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class RoomTransitManager
    {
        [ThreadStatic]
        private static PathfinderJob s_currentJob;

        [ThreadStatic]
        private static Room s_lastClassifiedRoom;

        [ThreadStatic]
        private static ID s_lastClassifiedRoomTypeId;

        [ThreadStatic]
        private static bool s_lastRoomShouldBePenalized;

        internal static void Begin(PathfinderJob job)
        {
            if (!TrafficControlConfig.AvoidRoomShortcuts ||
                TrafficControlConfig.RoomTransitPenalty <= 0f)
            {
                ClearThreadContext();
                return;
            }

            s_currentJob = job;
            ClearRoomClassificationCache();
        }

        internal static void End(PathfinderJob job)
        {
            if (ReferenceEquals(s_currentJob, job))
            {
                ClearThreadContext();
            }
        }

        internal static void ApplyPenalty(
            Floor floor,
            Vector2i position,
            ref float movementCost)
        {
            PathfinderJob job = s_currentJob;
            if (job == null ||
                floor == null)
            {
                return;
            }

            Room[,] roomTiles = floor.m_roomTiles;
            if (roomTiles == null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= roomTiles.GetLength(0) ||
                position.m_y >= roomTiles.GetLength(1))
            {
                return;
            }

            Room room =
                roomTiles[position.m_x, position.m_y];
            if (room == null)
            {
                return;
            }

            // Preserve the old live-room semantics. These references are
            // deliberately not cached across calls.
            Room startRoom =
                GetRoomAt(roomTiles, job.m_start);
            if (ReferenceEquals(room, startRoom))
            {
                return;
            }

            Room endRoom =
                GetRoomAt(roomTiles, job.m_end);
            if (ReferenceEquals(room, endRoom))
            {
                return;
            }

            // Keep outdoor state live as well.
            if (floor.IsOutdoors(position) ||
                !ShouldPenalizeRoom(room))
            {
                return;
            }

            movementCost +=
                TrafficControlConfig.RoomTransitPenalty;
        }

        private static Room GetRoomAt(
            Room[,] roomTiles,
            Vector2i position)
        {
            if (roomTiles == null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= roomTiles.GetLength(0) ||
                position.m_y >= roomTiles.GetLength(1))
            {
                return null;
            }

            return roomTiles[
                position.m_x,
                position.m_y];
        }

        private static bool ShouldPenalizeRoom(
            Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null)
            {
                return false;
            }

            GameDBPointer<GameDBRoomType> roomType =
                room.m_roomPersistentData.m_roomType;
            ID roomTypeId = roomType.m_id;

            if (ID.IsNullOrNoID(roomTypeId))
            {
                return false;
            }

            // Only Room + current type ID classification is reused.
            // Retyping changes the interned ID reference and invalidates
            // this one-entry cache immediately.
            if (ReferenceEquals(room, s_lastClassifiedRoom) &&
                ReferenceEquals(
                    roomTypeId,
                    s_lastClassifiedRoomTypeId))
            {
                return s_lastRoomShouldBePenalized;
            }

            bool shouldPenalize =
                !TrafficControlConfig.IsTransitException(
                    roomTypeId.ToString());

            s_lastClassifiedRoom = room;
            s_lastClassifiedRoomTypeId = roomTypeId;
            s_lastRoomShouldBePenalized =
                shouldPenalize;

            return shouldPenalize;
        }

        private static void ClearThreadContext()
        {
            s_currentJob = null;
            ClearRoomClassificationCache();
        }

        private static void ClearRoomClassificationCache()
        {
            s_lastClassifiedRoom = null;
            s_lastClassifiedRoomTypeId = null;
            s_lastRoomShouldBePenalized = false;
        }
    }
}
