using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class OneWayPersistenceManager
    {
        private const string SaveAttributeName = "HTCOneWayV1";
        private const string FormatPrefix = "1|";

        private static readonly Dictionary<MapPersistentData, OneWayPersistedRule[]> PendingLoaded =
            new Dictionary<MapPersistentData, OneWayPersistedRule[]>();

        private static readonly Dictionary<MapPersistentData, OneWayPersistedRule[]> CloneSnapshots =
            new Dictionary<MapPersistentData, OneWayPersistedRule[]>();

        private static readonly HashSet<Floor> PendingLoadedRouteRefresh =
            new HashSet<Floor>();

        internal static void Reset()
        {
            PendingLoaded.Clear();
            CloneSnapshots.Clear();
            PendingLoadedRouteRefresh.Clear();
        }

        internal static void WriteSaveAttribute(
            MapPersistentData mapData,
            XmlWriter writer)
        {
            if (mapData == null || writer == null)
            {
                return;
            }

            OneWayPersistedRule[] rules = null;
            int floorIndex = FindFloorIndex(mapData);

            if (floorIndex >= 0)
            {
                rules = OneWayManager.GetFloorSnapshot(floorIndex);
            }
            else if (CloneSnapshots.TryGetValue(
                mapData,
                out OneWayPersistedRule[] cloned))
            {
                rules = cloned;
            }
            else if (PendingLoaded.TryGetValue(
                mapData,
                out OneWayPersistedRule[] pending))
            {
                rules = pending;
            }

            if (rules == null || rules.Length == 0)
            {
                return;
            }

            writer.WriteAttributeString(SaveAttributeName, Encode(rules));
        }

        internal static void ReadSaveAttribute(
            MapPersistentData mapData,
            XmlReader reader)
        {
            if (mapData == null || reader == null)
            {
                return;
            }

            string encoded = reader.GetAttribute(SaveAttributeName);
            OneWayPersistedRule[] rules = Decode(encoded);

            if (rules.Length > 0)
            {
                PendingLoaded[mapData] = rules;
            }
            else
            {
                PendingLoaded.Remove(mapData);
            }
        }

        internal static void ApplyLoadedRules(Floor floor)
        {
            if (floor == null || floor.m_mapPersistentData == null)
            {
                return;
            }

            if (!PendingLoaded.TryGetValue(
                floor.m_mapPersistentData,
                out OneWayPersistedRule[] pending))
            {
                return;
            }

            PendingLoaded.Remove(floor.m_mapPersistentData);
            OneWayPersistedRule[] valid = FilterRulesForFloor(floor, pending);
            OneWayManager.RestoreFloorSnapshot(floor.m_floorIndex, valid);
            NormalizeDoubleDoorGroups(floor);

            // Floor.LoadEntities() runs before InGameMenuController adds this Floor
            // to Hospital.m_floors. Do not inspect character access or repath here:
            // BehaviorPatient.GetAccessRights() can call MapScriptInterface.GetRoomAt(),
            // which indexes Hospital.m_floors by walk.Floor.m_floorIndex.
            PendingLoadedRouteRefresh.Add(floor);
        }

        internal static void FinalizeLoadedRules(Floor floor)
        {
            if (floor == null || !PendingLoadedRouteRefresh.Contains(floor))
            {
                return;
            }

            Hospital hospital = Hospital.Instance;
            int floorIndex = floor.m_floorIndex;

            if (hospital == null ||
                hospital.m_floors == null ||
                floorIndex < 0 ||
                floorIndex >= hospital.m_floors.Count ||
                !ReferenceEquals(hospital.m_floors[floorIndex], floor))
            {
                return;
            }

            PendingLoadedRouteRefresh.Remove(floor);
            OneWayRouteManager.OnFloorRulesChanged(floor);
        }

        internal static void CaptureClone(
            MapPersistentData source,
            MapPersistentData clone)
        {
            if (source == null || clone == null)
            {
                return;
            }

            OneWayPersistedRule[] snapshot = null;
            int floorIndex = FindFloorIndex(source);

            if (floorIndex >= 0)
            {
                snapshot = OneWayManager.GetFloorSnapshot(floorIndex);
            }
            else if (CloneSnapshots.TryGetValue(
                source,
                out OneWayPersistedRule[] inherited))
            {
                snapshot = CopySnapshot(inherited);
            }
            else if (PendingLoaded.TryGetValue(
                source,
                out OneWayPersistedRule[] loaded))
            {
                snapshot = CopySnapshot(loaded);
            }

            if (snapshot == null)
            {
                snapshot = new OneWayPersistedRule[0];
            }

            CloneSnapshots[clone] = CopySnapshot(snapshot);
        }

        internal static void ApplyClone(
            MapPersistentData target,
            MapPersistentData clone)
        {
            if (target == null || clone == null)
            {
                return;
            }

            if (!CloneSnapshots.TryGetValue(
                clone,
                out OneWayPersistedRule[] snapshot))
            {
                return;
            }

            int floorIndex = FindFloorIndex(target);
            if (floorIndex < 0 || Hospital.Instance == null ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            OneWayPersistedRule[] valid = FilterRulesForFloor(floor, snapshot);
            OneWayManager.RestoreFloorSnapshot(floorIndex, valid);
            NormalizeDoubleDoorGroups(floor);
            OneWayRouteManager.OnFloorRulesChanged(floor);
        }

        private static int FindFloorIndex(MapPersistentData mapData)
        {
            Hospital hospital = Hospital.Instance;
            if (hospital == null || hospital.m_floors == null)
            {
                return -1;
            }

            for (int i = 0; i < hospital.m_floors.Count; i++)
            {
                Floor floor = hospital.m_floors[i];
                if (floor != null && ReferenceEquals(floor.m_mapPersistentData, mapData))
                {
                    return floor.m_floorIndex;
                }
            }

            return -1;
        }

        private static OneWayPersistedRule[] FilterRulesForFloor(
            Floor floor,
            OneWayPersistedRule[] rules)
        {
            if (floor == null || rules == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_tileWalls == null)
            {
                return new OneWayPersistedRule[0];
            }

            var valid = new List<OneWayPersistedRule>();
            TileWalls[,] walls = floor.m_mapPersistentData.m_tileWalls;
            int width = walls.GetLength(0);
            int height = walls.GetLength(1);

            for (int i = 0; i < rules.Length; i++)
            {
                OneWayPersistedRule rule = rules[i];
                if ((rule.Mode != 1 && rule.Mode != 2) ||
                    rule.X < 0 || rule.Y < 0 ||
                    rule.X >= width || rule.Y >= height)
                {
                    continue;
                }

                TileWalls tileWalls = walls[rule.X, rule.Y];
                Door door = rule.SouthWestWall
                    ? tileWalls?.m_doorSW
                    : tileWalls?.m_doorSE;

                // Windows share the Door entity type in Project Hospital, but their
                // GameDBDoor is non-passable. Ignore stale 0.9.0 rules on them.
                if (!OneWayDoorGroup.IsEligible(door))
                {
                    continue;
                }

                valid.Add(rule);
            }

            return valid.ToArray();
        }

        private static void NormalizeDoubleDoorGroups(Floor floor)
        {
            if (floor == null || floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_tileWalls == null)
            {
                return;
            }

            var seen = new List<Door>();
            TileWalls[,] walls = floor.m_mapPersistentData.m_tileWalls;
            int width = walls.GetLength(0);
            int height = walls.GetLength(1);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    TileWalls tileWalls = walls[x, y];
                    if (tileWalls == null)
                    {
                        continue;
                    }

                    NormalizeDoorGroup(floor, tileWalls.m_doorSW, seen);
                    NormalizeDoorGroup(floor, tileWalls.m_doorSE, seen);
                }
            }
        }

        private static void NormalizeDoorGroup(
            Floor floor,
            Door door,
            List<Door> seen)
        {
            if (!OneWayDoorGroup.IsEligible(door) || ContainsReference(seen, door))
            {
                return;
            }

            Door[] group = OneWayDoorGroup.GetGroup(door, floor);
            for (int i = 0; i < group.Length; i++)
            {
                AddReference(seen, group[i]);
            }

            if (group.Length > 1)
            {
                OneWayManager.NormalizeGroup(group);
            }
        }

        private static bool ContainsReference(List<Door> doors, Door candidate)
        {
            for (int i = 0; i < doors.Count; i++)
            {
                if (ReferenceEquals(doors[i], candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddReference(List<Door> doors, Door candidate)
        {
            if (candidate != null && !ContainsReference(doors, candidate))
            {
                doors.Add(candidate);
            }
        }

        private static string Encode(OneWayPersistedRule[] rules)
        {
            var builder = new StringBuilder(FormatPrefix);

            for (int i = 0; i < rules.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(';');
                }

                OneWayPersistedRule rule = rules[i];
                builder.Append(rule.X);
                builder.Append(',');
                builder.Append(rule.Y);
                builder.Append(',');
                builder.Append(rule.SouthWestWall ? '1' : '0');
                builder.Append(',');
                builder.Append(rule.Mode);
            }

            return builder.ToString();
        }

        private static OneWayPersistedRule[] Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded) ||
                !encoded.StartsWith(FormatPrefix, StringComparison.Ordinal))
            {
                return new OneWayPersistedRule[0];
            }

            string payload = encoded.Substring(FormatPrefix.Length);
            if (string.IsNullOrEmpty(payload))
            {
                return new OneWayPersistedRule[0];
            }

            var rules = new List<OneWayPersistedRule>();
            string[] entries = payload.Split(';');

            for (int i = 0; i < entries.Length; i++)
            {
                string[] parts = entries[i].Split(',');
                if (parts.Length != 4)
                {
                    continue;
                }

                if (!int.TryParse(parts[0], out int x) ||
                    !int.TryParse(parts[1], out int y) ||
                    !int.TryParse(parts[2], out int wall) ||
                    !byte.TryParse(parts[3], out byte mode) ||
                    (wall != 0 && wall != 1) ||
                    (mode != 1 && mode != 2))
                {
                    continue;
                }

                rules.Add(new OneWayPersistedRule
                {
                    X = x,
                    Y = y,
                    SouthWestWall = wall == 1,
                    Mode = mode
                });
            }

            return rules.ToArray();
        }

        private static OneWayPersistedRule[] CopySnapshot(OneWayPersistedRule[] source)
        {
            if (source == null || source.Length == 0)
            {
                return new OneWayPersistedRule[0];
            }

            var copy = new OneWayPersistedRule[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
