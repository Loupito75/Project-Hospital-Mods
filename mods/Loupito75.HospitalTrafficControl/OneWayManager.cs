using System;
using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal struct OneWayPersistedRule
    {
        internal int X;
        internal int Y;
        internal bool SouthWestWall;
        internal byte Mode;
    }

    internal static class OneWayManager
    {
        private struct DoorKey : IEquatable<DoorKey>
        {
            internal int Floor;
            internal int X;
            internal int Y;
            internal bool SouthWestWall;

            public bool Equals(DoorKey other)
            {
                return Floor == other.Floor &&
                    X == other.X &&
                    Y == other.Y &&
                    SouthWestWall == other.SouthWestWall;
            }

            public override bool Equals(object obj)
            {
                return obj is DoorKey && Equals((DoorKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Floor;
                    hash = (hash * 397) ^ X;
                    hash = (hash * 397) ^ Y;
                    hash = (hash * 397) ^ SouthWestWall.GetHashCode();
                    return hash;
                }
            }
        }

        private struct DoorRule
        {
            internal DoorKey Key;
            internal byte Mode;
        }

        // Writes happen on Unity's main thread. PathfinderJob workers only read
        // the latest immutable array snapshot. No lock/Monitor is used because
        // Project Hospital's old Mono lacks the Monitor.Enter overload emitted by
        // the compiler used for the previous implementation.
        private static volatile DoorRule[] s_rules = new DoorRule[0];
        private static int s_revision;

        internal static int RuleCount => s_rules.Length;
        internal static int Revision => s_revision;

        internal static byte GetMode(Door door)
        {
            if (door == null)
            {
                return 0;
            }

            DoorRule[] current = s_rules;
            int index = FindRule(current, GetDoorKey(door));
            return index >= 0 ? current[index].Mode : (byte)0;
        }

        internal static byte CycleGroup(Door[] doors)
        {
            DoorKey[] keys = GetUniqueKeys(doors);
            if (keys.Length == 0)
            {
                return 0;
            }

            DoorRule[] current = s_rules;
            byte currentMode = GetLogicalGroupMode(current, keys);
            byte next = (byte)((currentMode + 1) % 3);
            ApplyGroupMode(current, keys, next);
            return next;
        }

        internal static byte NormalizeGroup(Door[] doors)
        {
            DoorKey[] keys = GetUniqueKeys(doors);
            if (keys.Length <= 1)
            {
                return keys.Length == 1 ? GetMode(doors[0]) : (byte)0;
            }

            DoorRule[] current = s_rules;
            byte mode = GetLogicalGroupMode(current, keys);
            ApplyGroupMode(current, keys, mode);
            return mode;
        }

        internal static void Remove(Door door)
        {
            if (door == null)
            {
                return;
            }

            DoorRule[] current = s_rules;
            int index = FindRule(current, GetDoorKey(door));
            if (index < 0)
            {
                return;
            }

            DoorRule[] reduced = new DoorRule[current.Length - 1];
            if (index > 0)
            {
                Array.Copy(current, 0, reduced, 0, index);
            }
            if (index < current.Length - 1)
            {
                Array.Copy(current, index + 1, reduced, index, current.Length - index - 1);
            }
            s_rules = reduced;
            s_revision++;
        }

        internal static OneWayPersistedRule[] GetFloorSnapshot(int floorIndex)
        {
            DoorRule[] current = s_rules;
            var snapshot = new List<OneWayPersistedRule>();

            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].Key.Floor != floorIndex)
                {
                    continue;
                }

                snapshot.Add(new OneWayPersistedRule
                {
                    X = current[i].Key.X,
                    Y = current[i].Key.Y,
                    SouthWestWall = current[i].Key.SouthWestWall,
                    Mode = current[i].Mode
                });
            }

            return snapshot.ToArray();
        }

        internal static void RestoreFloorSnapshot(
            int floorIndex,
            OneWayPersistedRule[] snapshot)
        {
            DoorRule[] current = s_rules;
            var rebuilt = new List<DoorRule>(current.Length + (snapshot?.Length ?? 0));

            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].Key.Floor != floorIndex)
                {
                    rebuilt.Add(current[i]);
                }
            }

            if (snapshot != null)
            {
                for (int i = 0; i < snapshot.Length; i++)
                {
                    OneWayPersistedRule rule = snapshot[i];
                    if (rule.Mode != 1 && rule.Mode != 2)
                    {
                        continue;
                    }

                    DoorKey key = new DoorKey
                    {
                        Floor = floorIndex,
                        X = rule.X,
                        Y = rule.Y,
                        SouthWestWall = rule.SouthWestWall
                    };

                    if (FindRule(rebuilt, key) >= 0)
                    {
                        continue;
                    }

                    rebuilt.Add(new DoorRule
                    {
                        Key = key,
                        Mode = rule.Mode
                    });
                }
            }

            DoorRule[] updated = rebuilt.ToArray();
            if (!RulesEqual(current, updated))
            {
                s_rules = updated;
                s_revision++;
            }
        }

        internal static void Reset()
        {
            if (s_rules.Length > 0)
            {
                s_revision++;
            }

            s_rules = new DoorRule[0];
        }

        internal static bool IsTransitionAllowed(
            Floor floor,
            Vector2i currentPosition,
            Vector2i nextPosition)
        {
            DoorRule[] rules = s_rules;
            if (rules.Length == 0 || floor == null)
            {
                return true;
            }

            DoorKey key;
            bool increasing;

            if (currentPosition.m_x == nextPosition.m_x &&
                currentPosition.m_y != nextPosition.m_y)
            {
                key = new DoorKey
                {
                    Floor = floor.m_floorIndex,
                    X = currentPosition.m_x,
                    Y = Math.Max(currentPosition.m_y, nextPosition.m_y),
                    SouthWestWall = true
                };
                increasing = nextPosition.m_y > currentPosition.m_y;
            }
            else if (currentPosition.m_y == nextPosition.m_y &&
                currentPosition.m_x != nextPosition.m_x)
            {
                key = new DoorKey
                {
                    Floor = floor.m_floorIndex,
                    X = Math.Max(currentPosition.m_x, nextPosition.m_x),
                    Y = currentPosition.m_y,
                    SouthWestWall = false
                };
                increasing = nextPosition.m_x > currentPosition.m_x;
            }
            else
            {
                return true;
            }

            int index = FindRule(rules, key);
            if (index < 0)
            {
                return true;
            }

            return rules[index].Mode == 1 ? increasing : !increasing;
        }

        private static void ApplyGroupMode(
            DoorRule[] current,
            DoorKey[] keys,
            byte mode)
        {
            var rebuilt = new List<DoorRule>(current.Length + keys.Length);

            for (int i = 0; i < current.Length; i++)
            {
                if (!ContainsKey(keys, current[i].Key))
                {
                    rebuilt.Add(current[i]);
                }
            }

            if (mode == 1 || mode == 2)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    rebuilt.Add(new DoorRule
                    {
                        Key = keys[i],
                        Mode = mode
                    });
                }
            }

            DoorRule[] updated = rebuilt.ToArray();
            if (!RulesEqual(current, updated))
            {
                s_rules = updated;
                s_revision++;
            }
        }

        private static byte GetLogicalGroupMode(
            DoorRule[] rules,
            DoorKey[] keys)
        {
            byte firstNonZero = 0;

            for (int i = 0; i < keys.Length; i++)
            {
                int index = FindRule(rules, keys[i]);
                if (index < 0)
                {
                    continue;
                }

                byte mode = rules[index].Mode;
                if (mode != 0 && firstNonZero == 0)
                {
                    firstNonZero = mode;
                }
            }

            return firstNonZero;
        }

        private static DoorKey[] GetUniqueKeys(Door[] doors)
        {
            if (doors == null || doors.Length == 0)
            {
                return new DoorKey[0];
            }

            var keys = new List<DoorKey>();
            for (int i = 0; i < doors.Length; i++)
            {
                Door door = doors[i];
                if (!OneWayDoorGroup.IsEligible(door))
                {
                    continue;
                }

                DoorKey key = GetDoorKey(door);
                bool exists = false;
                for (int j = 0; j < keys.Count; j++)
                {
                    if (keys[j].Equals(key))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    keys.Add(key);
                }
            }

            return keys.ToArray();
        }

        private static bool ContainsKey(DoorKey[] keys, DoorKey key)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].Equals(key))
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindRule(DoorRule[] rules, DoorKey key)
        {
            for (int i = 0; i < rules.Length; i++)
            {
                if (rules[i].Key.Equals(key))
                {
                    return i;
                }
            }
            return -1;
        }

        private static int FindRule(List<DoorRule> rules, DoorKey key)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Key.Equals(key))
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool RulesEqual(DoorRule[] left, DoorRule[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                int matching = FindRule(right, left[i].Key);
                if (matching < 0 || right[matching].Mode != left[i].Mode)
                {
                    return false;
                }
            }

            return true;
        }

        private static DoorKey GetDoorKey(Door door)
        {
            Direction orientation = door.m_state.m_orientation;
            bool southWestWall = orientation == Direction.NE || orientation == Direction.SW;

            return new DoorKey
            {
                Floor = door.m_state.m_floorIndex,
                X = door.m_state.m_position.m_x,
                Y = door.m_state.m_position.m_y,
                SouthWestWall = southWestWall
            };
        }
    }
}
