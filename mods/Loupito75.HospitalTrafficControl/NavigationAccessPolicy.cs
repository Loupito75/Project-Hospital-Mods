using System;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class NavigationAccessPolicy
    {
        internal static AccessRights GetMovementAccess(WalkComponent walk)
        {
            Entity entity = CharacterAccess.GetEntity(walk);
            Behavior behavior = entity == null ? null : entity.GetComponent<Behavior>();
            return behavior == null
                ? AccessRights.STAFF
                : behavior.GetAccessRights();
        }

        internal static bool IsTileAccessible(
            Entity character,
            Floor floor,
            Vector2i tile)
        {
            if (character == null || floor == null)
            {
                return false;
            }

            Behavior behavior = character.GetComponent<Behavior>();
            if (behavior == null)
            {
                return false;
            }

            return IsTileAccessible(
                floor,
                tile,
                behavior.GetAccessRights());
        }

        internal static bool IsTileAccessible(
            Floor floor,
            Vector2i tile,
            AccessRights accessRights)
        {
            if (floor == null ||
                floor.m_roomAccessRights == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights == null ||
                tile.m_x < 0 ||
                tile.m_y < 0 ||
                tile.m_x >= floor.Size.m_x ||
                tile.m_y >= floor.Size.m_y)
            {
                return false;
            }

            if ((int)floor.m_roomAccessRights[tile.m_x, tile.m_y] >
                (int)accessRights)
            {
                return false;
            }

            AccessRights logisticsAccess =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    tile.m_x,
                    tile.m_y];

            // Matches Floor.IsAccessible(): logistics BIOHAZARD is traversable
            // independently of the character's numeric access level. Room-level
            // BIOHAZARD remains subject to the normal room-access comparison.
            return logisticsAccess == AccessRights.BIOHAZARD ||
                   (int)logisticsAccess <= (int)accessRights;
        }

        internal static int GetRequiredTileAccess(Floor floor, Vector2i tile)
        {
            if (floor == null ||
                floor.m_roomAccessRights == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights == null ||
                tile.m_x < 0 ||
                tile.m_y < 0 ||
                tile.m_x >= floor.Size.m_x ||
                tile.m_y >= floor.Size.m_y)
            {
                return int.MaxValue;
            }

            int roomAccess =
                (int)floor.m_roomAccessRights[tile.m_x, tile.m_y];
            AccessRights logistics =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    tile.m_x,
                    tile.m_y];

            int logisticsAccess =
                logistics == AccessRights.BIOHAZARD
                    ? (int)AccessRights.PEDESTRIAN
                    : (int)logistics;

            return Math.Max(roomAccess, logisticsAccess);
        }

        internal static bool NeedsBiohazardEndpointGraph(WalkComponent walk)
        {
            if (walk == null ||
                walk.m_state == null ||
                Hospital.Instance == null)
            {
                return false;
            }

            Entity entity = CharacterAccess.GetEntity(walk);
            if (entity == null ||
                entity.GetComponent<BehaviorPatient>() == null)
            {
                return false;
            }

            Floor originFloor = walk.Floor;
            int destinationFloorIndex = walk.m_state.m_destinationFloor;
            if (originFloor == null ||
                destinationFloorIndex < 0 ||
                destinationFloorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor destinationFloor =
                Hospital.Instance.m_floors[destinationFloorIndex];

            return IsBiohazardRoomTile(
                       originFloor,
                       walk.GetCurrentTile()) ||
                   IsBiohazardRoomTile(
                       destinationFloor,
                       walk.GetDestinationTile());
        }

        private static bool IsBiohazardRoomTile(
            Floor floor,
            Vector2i tile)
        {
            return floor != null &&
                   floor.m_roomAccessRights != null &&
                   tile.m_x >= 0 &&
                   tile.m_y >= 0 &&
                   tile.m_x < floor.Size.m_x &&
                   tile.m_y < floor.Size.m_y &&
                   floor.m_roomAccessRights[tile.m_x, tile.m_y] ==
                       AccessRights.BIOHAZARD;
        }
    }
}
