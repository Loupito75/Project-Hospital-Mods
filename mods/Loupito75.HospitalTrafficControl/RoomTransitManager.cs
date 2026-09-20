using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class RoomTransitManager
    {
        internal static void ApplyPenalty(Floor floor, Vector2i position, ref float movementCost)
        {
            if (!TrafficControlConfig.AvoidRoomShortcuts ||
                TrafficControlConfig.RoomTransitPenalty <= 0f ||
                floor == null)
            {
                return;
            }

            PathfinderJob job = OneWayPathfinderTracker.CurrentJob;
            if (job == null)
            {
                return;
            }

            // Outdoor routing keeps the game's native movement cost unchanged.
            if (floor.IsOutdoors(position))
            {
                return;
            }

            Room[,] roomTiles = floor.m_roomTiles;
            if (roomTiles == null ||
                position.m_x < 0 || position.m_y < 0 ||
                position.m_x >= roomTiles.GetLength(0) ||
                position.m_y >= roomTiles.GetLength(1))
            {
                return;
            }

            Room room = roomTiles[position.m_x, position.m_y];
            if (!ShouldPenalizeRoom(room))
            {
                return;
            }

            Room startRoom = GetRoomAt(roomTiles, job.m_start);
            if (ReferenceEquals(room, startRoom))
            {
                return;
            }

            Room endRoom = GetRoomAt(roomTiles, job.m_end);
            if (ReferenceEquals(room, endRoom))
            {
                return;
            }

            movementCost += TrafficControlConfig.RoomTransitPenalty;
        }

        private static Room GetRoomAt(Room[,] roomTiles, Vector2i position)
        {
            if (roomTiles == null ||
                position.m_x < 0 || position.m_y < 0 ||
                position.m_x >= roomTiles.GetLength(0) ||
                position.m_y >= roomTiles.GetLength(1))
            {
                return null;
            }

            return roomTiles[position.m_x, position.m_y];
        }

        private static bool ShouldPenalizeRoom(Room room)
        {
            // Unzoned / empty tiles are normal transit and always keep vanilla movement cost.
            if (room == null || room.m_roomPersistentData == null)
            {
                return false;
            }

            GameDBPointer<GameDBRoomType> roomType = room.m_roomPersistentData.m_roomType;
            if (!roomType.IsValid)
            {
                return false;
            }

            string roomTypeId = roomType.m_id.ToString();
            if (TrafficControlConfig.IsTransitException(roomTypeId))
            {
                return false;
            }

            // Every other actual room is treated as functional space rather than circulation.
            // This automatically includes DLC rooms and custom room types added by other mods.
            return true;
        }
    }
}
