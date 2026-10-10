using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class RoomGeometry
    {
        internal static bool IsInsideFloor(Floor floor, Vector2i tile)
        {
            return floor != null &&
                   tile.m_x >= 0 &&
                   tile.m_y >= 0 &&
                   tile.m_x < floor.Size.m_x &&
                   tile.m_y < floor.Size.m_y;
        }

        internal static bool IsInsideRoom(
            Room room,
            Floor floor,
            Vector2i tile)
        {
            return room != null &&
                   IsInsideFloor(floor, tile) &&
                   object.ReferenceEquals(
                       floor.m_roomTiles[tile.m_x, tile.m_y],
                       room) &&
                   room.IsPositionInRoom(tile);
        }

        internal static bool IsBoundaryTraversable(
            Floor floor,
            Vector2i current,
            Vector2i next)
        {
            if (!IsInsideFloor(floor, current) ||
                !IsInsideFloor(floor, next))
            {
                return false;
            }

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

        internal static bool IsConnectedInsideRoomWithoutWall(
            Room room,
            Floor floor,
            Vector2i current,
            Vector2i next)
        {
            return IsInsideRoom(room, floor, current) &&
                   IsInsideRoom(room, floor, next) &&
                   floor.IsAccessibleIgnoreDoors(current, next);
        }
    }
}
