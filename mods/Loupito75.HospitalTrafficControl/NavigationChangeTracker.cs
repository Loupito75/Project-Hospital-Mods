using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal sealed class AccessChangeSet
    {
        private readonly int _width;
        private readonly int _height;
        private readonly AccessRights[] _beforeRoom;
        private readonly AccessRights[] _beforeLogistics;
        private readonly AccessRights[] _afterRoom;
        private readonly AccessRights[] _afterLogistics;

        internal AccessChangeSet(
            int width,
            int height,
            AccessRights[] beforeRoom,
            AccessRights[] beforeLogistics,
            AccessRights[] afterRoom,
            AccessRights[] afterLogistics)
        {
            _width = width;
            _height = height;
            _beforeRoom = beforeRoom;
            _beforeLogistics = beforeLogistics;
            _afterRoom = afterRoom;
            _afterLogistics = afterLogistics;
        }

        internal bool WasTileNewlyRestricted(
            Vector2i tile,
            AccessRights grantedRights)
        {
            if (tile.m_x < 0 ||
                tile.m_y < 0 ||
                tile.m_x >= _width ||
                tile.m_y >= _height)
            {
                return false;
            }

            int index = tile.m_x * _height + tile.m_y;

            bool wasLegal = IsLegal(
                _beforeRoom[index],
                _beforeLogistics[index],
                grantedRights);

            bool isLegal = IsLegal(
                _afterRoom[index],
                _afterLogistics[index],
                grantedRights);

            return wasLegal && !isLegal;
        }

        private static bool IsLegal(
            AccessRights roomAccess,
            AccessRights logisticsAccess,
            AccessRights grantedRights)
        {
            int granted = (int)grantedRights;

            if ((int)roomAccess > granted)
            {
                return false;
            }

            return (int)logisticsAccess <= granted ||
                   logisticsAccess == AccessRights.BIOHAZARD;
        }
    }

    internal static class NavigationChangeTracker
    {
        private sealed class AccessSnapshot
        {
            internal int Width;
            internal int Height;
            internal AccessRights[] RoomAccess;
            internal AccessRights[] LogisticsAccess;
        }

        private static readonly Dictionary<int, AccessSnapshot> LastAppliedSnapshots =
            new Dictionary<int, AccessSnapshot>();

        private static readonly Dictionary<int, AccessSnapshot> PreRebuildSnapshots =
            new Dictionary<int, AccessSnapshot>();

        private static readonly Dictionary<int, AccessSnapshot> ExternalMutationSnapshots =
            new Dictionary<int, AccessSnapshot>();

        internal static void BeginExternalAccessMutation(Floor floor)
        {
            if (!CanSnapshot(floor))
            {
                return;
            }

            if (!ExternalMutationSnapshots.ContainsKey(floor.m_floorIndex))
            {
                ExternalMutationSnapshots[floor.m_floorIndex] =
                    CreateSnapshot(floor);
            }
        }

        internal static void BeginNavigationRebuild(Floor floor)
        {
            if (!CanSnapshot(floor))
            {
                return;
            }

            PreRebuildSnapshots[floor.m_floorIndex] = CreateSnapshot(floor);
        }

        internal static AccessChangeSet CompleteNavigationRebuild(
            Floor floor,
            out bool roomAccessChangedDuringRebuild)
        {
            roomAccessChangedDuringRebuild = false;

            if (!CanSnapshot(floor))
            {
                return null;
            }

            int floorIndex = floor.m_floorIndex;
            AccessSnapshot current = CreateSnapshot(floor);

            AccessSnapshot beforeRebuild;
            if (PreRebuildSnapshots.TryGetValue(floorIndex, out beforeRebuild))
            {
                roomAccessChangedDuringRebuild =
                    !RoomAccessEquals(beforeRebuild, current);
            }

            AccessSnapshot baseline = null;

            AccessSnapshot externalBefore;
            if (ExternalMutationSnapshots.TryGetValue(
                    floorIndex,
                    out externalBefore))
            {
                baseline = externalBefore;
            }
            else
            {
                AccessSnapshot previousApplied;
                if (LastAppliedSnapshots.TryGetValue(
                        floorIndex,
                        out previousApplied))
                {
                    baseline = previousApplied;
                }
            }

            AccessChangeSet changeSet = null;
            if (baseline != null && !AccessEquals(baseline, current))
            {
                changeSet = new AccessChangeSet(
                    current.Width,
                    current.Height,
                    baseline.RoomAccess,
                    baseline.LogisticsAccess,
                    current.RoomAccess,
                    current.LogisticsAccess);
            }

            LastAppliedSnapshots[floorIndex] = current;
            PreRebuildSnapshots.Remove(floorIndex);
            ExternalMutationSnapshots.Remove(floorIndex);

            return changeSet;
        }

        internal static void PrepareRoomAccessForNativeRebuild(Floor floor)
        {
            if (floor == null ||
                floor.m_roomAccessRights == null ||
                floor.m_roomTiles == null)
            {
                return;
            }

            int width = floor.Size.m_x;
            int height = floor.Size.m_y;

            if (floor.m_roomAccessRights.GetLength(0) != width ||
                floor.m_roomAccessRights.GetLength(1) != height ||
                floor.m_roomTiles.GetLength(0) != width ||
                floor.m_roomTiles.GetLength(1) != height)
            {
                return;
            }

            // Floor.UpdateStaticNavigationData() calls GridMap.Recalculate() before
            // rebuilding m_roomAccessRights. Prepare the exact vanilla room-access
            // matrix first so the single native graph rebuild consumes final rights.
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    AccessRights access = AccessRights.PEDESTRIAN;
                    Room room = floor.m_roomTiles[x, y];

                    if (room != null &&
                        room.m_roomPersistentData != null &&
                        room.m_roomPersistentData.m_roomType != null &&
                        room.m_roomPersistentData.m_roomType.Entry != null &&
                        room.m_roomPersistentData.m_roomType.Entry.AccessRights >
                            AccessRights.PEDESTRIAN)
                    {
                        access = room.m_roomPersistentData.m_roomType.Entry.AccessRights;
                    }

                    floor.m_roomAccessRights[x, y] = access;
                }
            }
        }

        internal static void Reset()
        {
            LastAppliedSnapshots.Clear();
            PreRebuildSnapshots.Clear();
            ExternalMutationSnapshots.Clear();
        }

        private static bool CanSnapshot(Floor floor)
        {
            return floor != null &&
                   floor.m_mapPersistentData != null &&
                   floor.m_mapPersistentData.m_mapLogisticsLayer != null &&
                   floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights != null &&
                   floor.m_roomAccessRights != null;
        }

        private static AccessSnapshot CreateSnapshot(Floor floor)
        {
            int width = floor.Size.m_x;
            int height = floor.Size.m_y;
            int count = width * height;

            AccessSnapshot snapshot = new AccessSnapshot
            {
                Width = width,
                Height = height,
                RoomAccess = new AccessRights[count],
                LogisticsAccess = new AccessRights[count]
            };

            AccessRights[,] logistics =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights;

            int index = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    snapshot.RoomAccess[index] = floor.m_roomAccessRights[x, y];
                    snapshot.LogisticsAccess[index] = logistics[x, y];
                    index++;
                }
            }

            return snapshot;
        }

        private static bool AccessEquals(
            AccessSnapshot left,
            AccessSnapshot right)
        {
            return RoomAccessEquals(left, right) &&
                   LogisticsAccessEquals(left, right);
        }

        private static bool RoomAccessEquals(
            AccessSnapshot left,
            AccessSnapshot right)
        {
            if (!SameSize(left, right))
            {
                return false;
            }

            for (int i = 0; i < left.RoomAccess.Length; i++)
            {
                if (left.RoomAccess[i] != right.RoomAccess[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LogisticsAccessEquals(
            AccessSnapshot left,
            AccessSnapshot right)
        {
            if (!SameSize(left, right))
            {
                return false;
            }

            for (int i = 0; i < left.LogisticsAccess.Length; i++)
            {
                if (left.LogisticsAccess[i] != right.LogisticsAccess[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameSize(
            AccessSnapshot left,
            AccessSnapshot right)
        {
            return left != null &&
                   right != null &&
                   left.Width == right.Width &&
                   left.Height == right.Height;
        }
    }
}
