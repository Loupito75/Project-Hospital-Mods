using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class OneWayDoorGroup
    {
        internal static bool IsEligible(Door door)
        {
            return door != null &&
                   door.m_state != null &&
                   door.m_state.m_gameDBDoor != null &&
                   door.m_state.m_gameDBDoor.Entry != null &&
                   door.m_state.m_gameDBDoor.Entry.Passable;
        }

        internal static Door[] GetGroup(Door door, Floor floor)
        {
            if (!IsEligible(door))
            {
                return new Door[0];
            }

            var group = new List<Door>();
            AddUnique(group, door);

            // Native prefab double doors are not all linked through m_neighbourDoor.
            // When the door was instantiated directly from its door prefab, trust the
            // PrefabInstance door list only if that instance is the exact PrefabParentRef
            // declared by the GameDBDoor type.
            TryAddNativePrefabDoorGroup(group, door);

            // Some native room/prefab layouts contain the same L/R double-door leaves
            // without a usable runtime prefab parent. In that case identify only the
            // single adjacent reciprocal leaf declared by the same native door prefab.
            // This deliberately does not group doors from proximity alone.
            if (group.Count == 1)
            {
                TryAddDeclaredDoubleDoorSibling(group, door, floor);
            }

            Door directNeighbour = GetNativeNeighbour(door);
            if (IsCompatibleNeighbour(door, directNeighbour))
            {
                AddUnique(group, directNeighbour);
            }

            // Native saves/prefabs may expose the neighbour link from only one leaf.
            // Search the floor for a Door whose native m_neighbourDoor points back to
            // the clicked leaf. This remains a fallback for legacy/native links.
            if (group.Count == 1 && floor != null && floor.m_mapPersistentData != null &&
                floor.m_mapPersistentData.m_tileWalls != null)
            {
                TileWalls[,] walls = floor.m_mapPersistentData.m_tileWalls;
                int width = walls.GetLength(0);
                int height = walls.GetLength(1);

                for (int x = 0; x < width && group.Count == 1; x++)
                {
                    for (int y = 0; y < height && group.Count == 1; y++)
                    {
                        TileWalls tileWalls = walls[x, y];
                        if (tileWalls == null)
                        {
                            continue;
                        }

                        TryAddReverseNeighbour(group, door, tileWalls.m_doorSW);
                        TryAddReverseNeighbour(group, door, tileWalls.m_doorSE);
                    }
                }
            }

            return group.ToArray();
        }

        private static void TryAddNativePrefabDoorGroup(
            List<Door> group,
            Door clicked)
        {
            if (!IsEligible(clicked) || clicked.m_state.m_prefabParent == null)
            {
                return;
            }

            GameDBDoor clickedType = clicked.m_state.m_gameDBDoor.Entry;
            DatabaseEntryRef<GameDBPrefabObject> declaredPrefabRef = clickedType.PrefabParentRef;
            if (declaredPrefabRef == null || !declaredPrefabRef.IsValid)
            {
                return;
            }

            GameDBPrefabObject declaredPrefab = declaredPrefabRef.Entry;
            PrefabInstance prefabInstance = clicked.m_state.m_prefabParent.GetEntity();
            if (declaredPrefab == null ||
                prefabInstance == null ||
                prefabInstance.m_persistentData == null ||
                prefabInstance.m_persistentData.m_prefabObject == null)
            {
                return;
            }

            GameDBPrefabObject actualPrefab = prefabInstance.m_persistentData.m_prefabObject.Entry;
            if (!SameDatabaseEntry(declaredPrefab, actualPrefab))
            {
                return;
            }

            List<EntityIDPointer<Door>> prefabDoors = prefabInstance.m_persistentData.m_doors;
            if (prefabDoors == null || prefabDoors.Count <= 1)
            {
                return;
            }

            for (int i = 0; i < prefabDoors.Count; i++)
            {
                EntityIDPointer<Door> doorPointer = prefabDoors[i];
                Door candidate = doorPointer != null ? doorPointer.GetEntity() : null;
                if (!IsCompatibleNeighbour(clicked, candidate) ||
                    candidate.m_state.m_prefabParent == null ||
                    !ReferenceEquals(candidate.m_state.m_prefabParent.GetEntity(), prefabInstance))
                {
                    continue;
                }

                AddUnique(group, candidate);
            }
        }

        private static void TryAddDeclaredDoubleDoorSibling(
            List<Door> group,
            Door clicked,
            Floor floor)
        {
            if (!IsEligible(clicked) || floor == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_tileWalls == null)
            {
                return;
            }

            GameDBDoor clickedType = clicked.m_state.m_gameDBDoor.Entry;
            DatabaseEntryRef<GameDBDoor> inverseRef = clickedType.InverseDoorType;
            DatabaseEntryRef<GameDBPrefabObject> declaredPrefabRef = clickedType.PrefabParentRef;
            if (inverseRef == null || !inverseRef.IsValid ||
                declaredPrefabRef == null || !declaredPrefabRef.IsValid)
            {
                return;
            }

            GameDBDoor inverseType = inverseRef.Entry;
            GameDBPrefabObject declaredPrefab = declaredPrefabRef.Entry;
            if (inverseType == null || declaredPrefab == null)
            {
                return;
            }

            bool southWestWall = IsSouthWestWall(clicked);
            int x = clicked.m_state.m_position.m_x;
            int y = clicked.m_state.m_position.m_y;

            Door candidateA = southWestWall
                ? GetDoorAtWall(floor, x - 1, y, true)
                : GetDoorAtWall(floor, x, y - 1, false);
            Door candidateB = southWestWall
                ? GetDoorAtWall(floor, x + 1, y, true)
                : GetDoorAtWall(floor, x, y + 1, false);

            Door match = null;
            int matchCount = 0;

            if (IsDeclaredDoubleDoorSibling(
                clicked,
                clickedType,
                inverseType,
                declaredPrefab,
                southWestWall,
                candidateA))
            {
                match = candidateA;
                matchCount++;
            }

            if (IsDeclaredDoubleDoorSibling(
                clicked,
                clickedType,
                inverseType,
                declaredPrefab,
                southWestWall,
                candidateB))
            {
                match = candidateB;
                matchCount++;
            }

            // Ambiguous layouts are left untouched rather than risking grouping
            // unrelated doors. A real native double door has exactly one reciprocal
            // adjacent leaf on its wall axis.
            if (matchCount == 1)
            {
                AddUnique(group, match);
            }
        }

        private static bool IsDeclaredDoubleDoorSibling(
            Door clicked,
            GameDBDoor clickedType,
            GameDBDoor inverseType,
            GameDBPrefabObject declaredPrefab,
            bool southWestWall,
            Door candidate)
        {
            if (!IsCompatibleNeighbour(clicked, candidate) ||
                IsSouthWestWall(candidate) != southWestWall)
            {
                return false;
            }

            GameDBDoor candidateType = candidate.m_state.m_gameDBDoor.Entry;
            if (!SameDatabaseEntry(candidateType, inverseType) ||
                candidateType.PrefabParentRef == null ||
                !candidateType.PrefabParentRef.IsValid ||
                !SameDatabaseEntry(candidateType.PrefabParentRef.Entry, declaredPrefab) ||
                candidateType.InverseDoorType == null ||
                !candidateType.InverseDoorType.IsValid ||
                !SameDatabaseEntry(candidateType.InverseDoorType.Entry, clickedType))
            {
                return false;
            }

            return true;
        }

        private static Door GetDoorAtWall(
            Floor floor,
            int x,
            int y,
            bool southWestWall)
        {
            TileWalls[,] walls = floor.m_mapPersistentData.m_tileWalls;
            if (x < 0 || y < 0 || x >= walls.GetLength(0) || y >= walls.GetLength(1))
            {
                return null;
            }

            TileWalls tileWalls = walls[x, y];
            if (tileWalls == null)
            {
                return null;
            }

            return southWestWall ? tileWalls.m_doorSW : tileWalls.m_doorSE;
        }

        private static bool IsSouthWestWall(Door door)
        {
            Direction orientation = door.m_state.m_orientation;
            return orientation == Direction.NE || orientation == Direction.SW;
        }

        private static bool SameDatabaseEntry(
            DatabaseEntry left,
            DatabaseEntry right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null ||
                ID.IsNullOrNoID(left.DatabaseID) ||
                ID.IsNullOrNoID(right.DatabaseID))
            {
                return false;
            }

            return left.DatabaseID.ToString() == right.DatabaseID.ToString();
        }

        private static void TryAddReverseNeighbour(
            List<Door> group,
            Door clicked,
            Door candidate)
        {
            if (!IsCompatibleNeighbour(clicked, candidate))
            {
                return;
            }

            Door candidateNeighbour = GetNativeNeighbour(candidate);
            if (ReferenceEquals(candidateNeighbour, clicked))
            {
                AddUnique(group, candidate);
            }
        }

        private static Door GetNativeNeighbour(Door door)
        {
            if (!IsEligible(door) || door.m_state.m_neighbourDoor == null)
            {
                return null;
            }

            return door.m_state.m_neighbourDoor.GetEntity();
        }

        private static bool IsCompatibleNeighbour(Door source, Door candidate)
        {
            return IsEligible(source) &&
                   IsEligible(candidate) &&
                   !ReferenceEquals(source, candidate) &&
                   source.m_state.m_floorIndex == candidate.m_state.m_floorIndex;
        }

        private static void AddUnique(List<Door> doors, Door candidate)
        {
            if (!IsEligible(candidate))
            {
                return;
            }

            for (int i = 0; i < doors.Count; i++)
            {
                if (ReferenceEquals(doors[i], candidate))
                {
                    return;
                }
            }

            doors.Add(candidate);
        }
    }
}
