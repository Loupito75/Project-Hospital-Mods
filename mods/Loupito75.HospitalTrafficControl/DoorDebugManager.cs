using System.Collections.Generic;
using GLib;
using Lopital;
using UnityEngine;

namespace HospitalTrafficControl
{
    internal static class DoorDebugManager
    {
        private static readonly Dictionary<string, string> LastArrowSignatures =
            new Dictionary<string, string>();

        internal static void Reset()
        {
            LastArrowSignatures.Clear();
        }

        internal static void LogMiss(
            Floor floor,
            Vector2i cursorTile,
            Vector2 mouseCoords,
            Direction fallbackEdge,
            Door candidate,
            string reason)
        {
            if (!TrafficControlConfig.DoorDebug)
            {
                return;
            }

            Plugin.Log?.LogWarning(
                "[DoorDebug] CLICK MISS " +
                "floor=" + (floor != null ? floor.m_floorIndex.ToString() : "null") +
                " cursorTile=" + Format(cursorTile) +
                " mouse=(" + mouseCoords.x.ToString("0.###") + ", " + mouseCoords.y.ToString("0.###") + ")" +
                " fallbackEdge=" + fallbackEdge +
                " reason=" + (reason ?? "unknown") +
                " candidate=" + DescribeDoor(candidate) + ".");
        }

        internal static void LogClick(
            Floor floor,
            Vector2i cursorTile,
            Vector2 mouseCoords,
            Direction fallbackEdge,
            Door selectedDoor,
            Door[] group,
            byte previousMode,
            byte newMode)
        {
            if (!TrafficControlConfig.DoorDebug)
            {
                return;
            }

            Plugin.Log?.LogWarning(
                "[DoorDebug] CLICK " +
                "floor=" + (floor != null ? floor.m_floorIndex.ToString() : "null") +
                " cursorTile=" + Format(cursorTile) +
                " mouse=(" + mouseCoords.x.ToString("0.###") + ", " + mouseCoords.y.ToString("0.###") + ")" +
                " fallbackEdge=" + fallbackEdge +
                " previousMode=" + previousMode +
                " newMode=" + newMode +
                " selected=" + DescribeDoor(selectedDoor) +
                " groupCount=" + (group != null ? group.Length : 0) + ".");

            if (group == null)
            {
                return;
            }

            for (int i = 0; i < group.Length; i++)
            {
                Plugin.Log?.LogWarning(
                    "[DoorDebug]   GROUP #" + (i + 1) + " " + DescribeDoor(group[i]) + ".");
            }
        }

        internal static void LogArrow(
            Door door,
            byte mode,
            Direction displayedDirection,
            Vector2i sourceTile,
            Vector2i destinationTile,
            Vector2i displayedSourceTile,
            Vector2i displayedDestinationTile,
            Vector3 localIsometricPosition,
            Vector3 floorOffset,
            Vector3 worldIsometricPosition)
        {
            if (!TrafficControlConfig.DoorDebug)
            {
                return;
            }

            string key = GetArrowKey(door);
            string signature =
                mode + "|" + displayedDirection +
                "|" + Format(sourceTile) +
                "|" + Format(destinationTile) +
                "|" + Format(displayedSourceTile) +
                "|" + Format(displayedDestinationTile) +
                "|" + Format(localIsometricPosition) +
                "|" + Format(floorOffset) +
                "|" + Format(worldIsometricPosition);

            string previousSignature;
            if (LastArrowSignatures.TryGetValue(key, out previousSignature) &&
                previousSignature == signature)
            {
                return;
            }

            LastArrowSignatures[key] = signature;

            Plugin.Log?.LogWarning(
                "[DoorDebug] ARROW " +
                DescribeDoor(door) +
                " mode=" + mode +
                " source=" + Format(sourceTile) +
                " destination=" + Format(destinationTile) +
                " displayedSource=" + Format(displayedSourceTile) +
                " displayedDestination=" + Format(displayedDestinationTile) +
                " displayedDirection=" + displayedDirection +
                " localIso=" + Format(localIsometricPosition) +
                " floorOffset=" + Format(floorOffset) +
                " worldIso=" + Format(worldIsometricPosition) + ".");
        }

        internal static string DescribeDoor(Door door)
        {
            if (door == null || door.m_state == null)
            {
                return "door=null";
            }

            GameDBDoor doorType =
                door.m_state.m_gameDBDoor != null
                    ? door.m_state.m_gameDBDoor.Entry
                    : null;

            Door neighbour = null;
            if (door.m_state.m_neighbourDoor != null)
            {
                neighbour = door.m_state.m_neighbourDoor.GetEntity();
            }

            string typeId = GetDatabaseId(doorType);
            string declaredPrefabId = "null";
            if (doorType != null &&
                doorType.PrefabParentRef != null &&
                doorType.PrefabParentRef.IsValid)
            {
                declaredPrefabId = GetDatabaseId(doorType.PrefabParentRef.Entry);
            }

            PrefabInstance prefabParent =
                door.m_state.m_prefabParent != null
                    ? door.m_state.m_prefabParent.GetEntity()
                    : null;

            string actualPrefabId = "null";
            int prefabDoorCount = 0;
            if (prefabParent != null && prefabParent.m_persistentData != null)
            {
                if (prefabParent.m_persistentData.m_prefabObject != null)
                {
                    actualPrefabId = GetDatabaseId(
                        prefabParent.m_persistentData.m_prefabObject.Entry);
                }

                if (prefabParent.m_persistentData.m_doors != null)
                {
                    prefabDoorCount = prefabParent.m_persistentData.m_doors.Count;
                }
            }

            string neighbourText = neighbour != null && neighbour.m_state != null
                ? "floor=" + neighbour.m_state.m_floorIndex +
                  " pos=" + Format(neighbour.m_state.m_position) +
                  " orientation=" + neighbour.m_state.m_orientation
                : "null";

            bool southWestWall =
                door.m_state.m_orientation == Direction.NE ||
                door.m_state.m_orientation == Direction.SW;

            return "door{floor=" + door.m_state.m_floorIndex +
                   " pos=" + Format(door.m_state.m_position) +
                   " orientation=" + door.m_state.m_orientation +
                   " wall=" + (southWestWall ? "SW" : "SE") +
                   " type=" + typeId +
                   " passable=" + (doorType != null && doorType.Passable) +
                   " slide=" + (doorType != null ? doorType.SlideDirection.ToString() : "null") +
                   " neighbour={" + neighbourText + "}" +
                   " prefab={declared=" + declaredPrefabId +
                   " actual=" + actualPrefabId +
                   " doors=" + prefabDoorCount + "}}";
        }

        private static string GetArrowKey(Door door)
        {
            if (door == null || door.m_state == null)
            {
                return "null";
            }

            return
                door.m_state.m_floorIndex + ":" +
                door.m_state.m_position.m_x + ":" +
                door.m_state.m_position.m_y + ":" +
                door.m_state.m_orientation;
        }

        private static string GetDatabaseId(DatabaseEntry entry)
        {
            return entry != null && !ID.IsNullOrNoID(entry.DatabaseID)
                ? entry.DatabaseID.ToString()
                : "null";
        }

        private static string Format(Vector2i value)
        {
            return "[" + value.m_x + ", " + value.m_y + "]";
        }

        private static string Format(Vector3 value)
        {
            return "(" + value.x.ToString("0.###") +
                   ", " + value.y.ToString("0.###") +
                   ", " + value.z.ToString("0.###") + ")";
        }
    }
}
