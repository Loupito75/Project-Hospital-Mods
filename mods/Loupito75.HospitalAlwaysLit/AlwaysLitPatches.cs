using System;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalAlwaysLit
{
    internal static class AlwaysLitRoomTypes
    {
        internal static bool IsTarget(Room room)
        {
            if (room == null || room.m_roomPersistentData == null)
            {
                return false;
            }

            GameDBRoomType roomType = room.m_roomPersistentData.m_roomType.Entry;
            if (roomType == null)
            {
                return false;
            }

            return AlwaysLitConfig.IsEnabled(roomType.DatabaseID.ToString());
        }

        internal static Room GetRoom(TileObject tileObject)
        {
            if (tileObject == null || tileObject.m_state == null || Hospital.Instance == null)
            {
                return null;
            }

            Floor floor = Hospital.Instance.m_floors[tileObject.GetFloorIndex()];
            if (floor == null)
            {
                return null;
            }

            return floor.GetRoomTileSafe(tileObject.m_state.m_position.m_x, tileObject.m_state.m_position.m_y);
        }
    }

    [HarmonyPatch(typeof(TileObject), nameof(TileObject.SetLightEnabled), new Type[] { typeof(bool) })]
    internal static class AutomaticLightPatch
    {
        private static void Prefix(TileObject __instance, ref bool enabled)
        {
            if (__instance == null || __instance.m_state == null)
            {
                return;
            }

            if (enabled)
            {
                return;
            }

            GameDBObject objectType = __instance.m_state.m_gameDBObject.Entry;
            if (objectType == null || !objectType.LightAutomatic)
            {
                return;
            }

            Room room = AlwaysLitRoomTypes.GetRoom(__instance);
            if (AlwaysLitRoomTypes.IsTarget(room))
            {
                enabled = true;
            }
        }
    }

    [HarmonyPatch(typeof(Room), nameof(Room.UpdateLightLevel), new Type[] { typeof(Floor) })]
    internal static class RoomLightLevelPatch
    {
        private const int CharacterLightLevel = 255;

        private static void Postfix(Room __instance)
        {
            if (!AlwaysLitRoomTypes.IsTarget(__instance))
            {
                return;
            }

            int[] lightLevel = __instance.m_roomPersistentData.m_lightLevel;
            if (lightLevel == null || lightLevel.Length < 3)
            {
                return;
            }

            float timer = __instance.m_roomPersistentData.m_characterLightTimer;
            int nativeCharacterLight = timer > 0f
                ? (int)(CharacterLightLevel * Math.Min(1f, timer))
                : 0;

            int missingCharacterLight = CharacterLightLevel - nativeCharacterLight;
            if (missingCharacterLight <= 0)
            {
                return;
            }

            lightLevel[0] += missingCharacterLight;
            lightLevel[1] += missingCharacterLight;
            lightLevel[2] += missingCharacterLight;
        }
    }

    [HarmonyPatch(typeof(Floor), nameof(Floor.AddRoom), new Type[]
    {
        typeof(GameDBRoomType),
        typeof(Vector2i),
        typeof(Vector2i),
        typeof(Vector2i),
        typeof(bool)
    })]
    internal static class RoomCreationLightRefreshPatch
    {
        private static void Postfix(Floor __instance, Vector2i selectionStart, bool rotatedCoordinates)
        {
            if (__instance == null)
            {
                return;
            }

            Vector2i roomPosition = rotatedCoordinates
                ? selectionStart
                : IsometricCameraUtils.GetRotatedPosition(selectionStart.m_x, selectionStart.m_y);

            Room room = __instance.GetRoomTileSafe(roomPosition.m_x, roomPosition.m_y);
            if (!AlwaysLitRoomTypes.IsTarget(room))
            {
                return;
            }

            room.UpdateLightLevel(__instance);
        }
    }
}
