using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using GLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class OpeningClosingLightRefreshPatch
    {
        private const string HospitalAlwaysLitHarmonyId =
            "Loupito75:HospitalAlwaysLit";

        [ThreadStatic]
        private static Floor s_scanFloor;

        [ThreadStatic]
        private static int s_scanDepth;

        [ThreadStatic]
        private static Dictionary<Room, RoomLightCache> s_roomCache;

        [ThreadStatic]
        private static LightChangeContext s_lightChange;

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo setLightEnabled = AccessTools.Method(
                typeof(TileObject),
                "SetLightEnabled",
                new Type[] { typeof(bool) });
            MethodInfo roomUpdateLightLevel = AccessTools.Method(
                typeof(Room),
                "UpdateLightLevel",
                new Type[] { typeof(Floor) });

            MethodInfo setLightPrefix = AccessTools.Method(
                typeof(OpeningClosingLightRefreshPatch),
                "SetLightEnabledPrefix");
            MethodInfo setLightFinalizer = AccessTools.Method(
                typeof(OpeningClosingLightRefreshPatch),
                "SetLightEnabledFinalizer");
            MethodInfo roomPrefix = AccessTools.Method(
                typeof(OpeningClosingLightRefreshPatch),
                "RoomUpdateLightLevelPrefix");
            MethodInfo roomPostfix = AccessTools.Method(
                typeof(OpeningClosingLightRefreshPatch),
                "RoomUpdateLightLevelPostfix");
            MethodInfo floorPrefix = AccessTools.Method(
                typeof(OpeningClosingLightRefreshPatch),
                "FloorPrefix");
            MethodInfo floorFinalizer = AccessTools.Method(
                typeof(OpeningClosingLightRefreshPatch),
                "FloorFinalizer");

            if (setLightEnabled == null)
            {
                throw new MissingMethodException(
                    typeof(TileObject).FullName,
                    "SetLightEnabled(Boolean)");
            }
            if (roomUpdateLightLevel == null)
            {
                throw new MissingMethodException(
                    typeof(Room).FullName,
                    "UpdateLightLevel(Floor)");
            }
            if (setLightPrefix == null ||
                setLightFinalizer == null ||
                roomPrefix == null ||
                roomPostfix == null ||
                floorPrefix == null ||
                floorFinalizer == null)
            {
                throw new MissingMethodException(
                    typeof(OpeningClosingLightRefreshPatch).FullName,
                    "HRO opening/closing light patch method");
            }

            HarmonyMethod setLightPrefixPatch =
                new HarmonyMethod(setLightPrefix);
            setLightPrefixPatch.after =
                new string[] { HospitalAlwaysLitHarmonyId };

            harmony.Patch(
                setLightEnabled,
                prefix: setLightPrefixPatch,
                finalizer: new HarmonyMethod(setLightFinalizer));

            HarmonyMethod roomPostfixPatch =
                new HarmonyMethod(roomPostfix);
            roomPostfixPatch.before =
                new string[] { HospitalAlwaysLitHarmonyId };

            harmony.Patch(
                roomUpdateLightLevel,
                prefix: new HarmonyMethod(roomPrefix),
                postfix: roomPostfixPatch);

            PatchFloorMethod(
                harmony,
                "OnOpenForPatients",
                floorPrefix,
                floorFinalizer);
            PatchFloorMethod(
                harmony,
                "OnClosedForDayShift",
                floorPrefix,
                floorFinalizer);

            Plugin.Log.LogInfo(
                "OptimizeOpeningClosingLightRefresh installed: Room.UpdateLightLevel call points are preserved; repeated room scans during opening/closing use exact light deltas after the first native recalculation per room.");
        }

        internal static void Reset()
        {
            s_scanDepth = 0;
            s_scanFloor = null;
            s_lightChange = new LightChangeContext();

            if (s_roomCache != null)
            {
                s_roomCache.Clear();
            }
        }

        internal static void BeginFloorScan(Floor floor)
        {
            if (floor == null)
            {
                return;
            }

            if (s_scanDepth == 0)
            {
                s_scanFloor = floor;
                s_scanDepth = 1;

                if (s_roomCache == null)
                {
                    s_roomCache =
                        new Dictionary<Room, RoomLightCache>();
                }
                else
                {
                    s_roomCache.Clear();
                }

                return;
            }

            if (object.ReferenceEquals(s_scanFloor, floor))
            {
                s_scanDepth++;
            }
        }

        internal static void EndFloorScan(Floor floor)
        {
            if (floor == null ||
                s_scanDepth <= 0 ||
                !object.ReferenceEquals(s_scanFloor, floor))
            {
                return;
            }

            s_scanDepth--;

            if (s_scanDepth == 0)
            {
                s_scanFloor = null;
                s_lightChange = new LightChangeContext();

                if (s_roomCache != null)
                {
                    s_roomCache.Clear();
                }
            }
        }

        private static void PatchFloorMethod(
            Harmony harmony,
            string methodName,
            MethodInfo prefix,
            MethodInfo finalizer)
        {
            MethodInfo target = AccessTools.Method(
                typeof(Floor),
                methodName,
                Type.EmptyTypes);

            if (target == null)
            {
                throw new MissingMethodException(
                    typeof(Floor).FullName,
                    methodName + "()");
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                finalizer: new HarmonyMethod(finalizer));
        }

        private static void FloorPrefix(Floor __instance)
        {
            BeginFloorScan(__instance);
        }

        private static Exception FloorFinalizer(
            Floor __instance,
            Exception __exception)
        {
            EndFloorScan(__instance);
            return __exception;
        }

        private static void SetLightEnabledPrefix(
            TileObject __instance,
            bool enabled,
            out LightChangeContext __state)
        {
            __state = s_lightChange;
            s_lightChange = new LightChangeContext();

            if (s_scanDepth <= 0 ||
                s_scanFloor == null ||
                __instance == null ||
                __instance.m_state == null ||
                __instance.GetFloorIndex() != s_scanFloor.m_floorIndex)
            {
                return;
            }

            Vector2i position = __instance.m_state.m_position;
            if (position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= s_scanFloor.m_size.m_x ||
                position.m_y >= s_scanFloor.m_size.m_y)
            {
                return;
            }

            Room room =
                s_scanFloor.m_roomTiles[position.m_x, position.m_y];

            if (room == null ||
                __instance.m_state.m_gameDBObject == null ||
                __instance.m_state.m_gameDBObject.Entry == null)
            {
                return;
            }

            GameDBObject objectType =
                __instance.m_state.m_gameDBObject.Entry;

            int red = 0;
            int green = 0;
            int blue = 0;

            // Match Room.UpdateLightLevel exactly: only objects with
            // LightLevelRed > 0 contribute, then all three channels use
            // integer division by 12.
            if (objectType.LightLevelRed > 0)
            {
                red = objectType.LightLevelRed / 12;
                green = objectType.LightLevelGreen / 12;
                blue = objectType.LightLevelBlue / 12;
            }

            int direction = 0;
            bool previousEnabled =
                __instance.m_state.m_lightEnabled;

            if (!previousEnabled && enabled)
            {
                direction = 1;
            }
            else if (previousEnabled && !enabled)
            {
                direction = -1;
            }

            s_lightChange = new LightChangeContext(
                room,
                s_scanFloor,
                red * direction,
                green * direction,
                blue * direction);
        }

        private static Exception SetLightEnabledFinalizer(
            Exception __exception,
            LightChangeContext __state)
        {
            s_lightChange = __state;
            return __exception;
        }

        private static bool RoomUpdateLightLevelPrefix(
            Room __instance,
            Floor floor,
            out RoomUpdateState __state)
        {
            __state = new RoomUpdateState();

            if (s_scanDepth <= 0 ||
                s_scanFloor == null ||
                s_roomCache == null ||
                __instance == null ||
                floor == null ||
                !object.ReferenceEquals(floor, s_scanFloor))
            {
                return true;
            }

            if (!s_lightChange.IsValid ||
                !object.ReferenceEquals(s_lightChange.Room, __instance) ||
                !object.ReferenceEquals(s_lightChange.Floor, floor))
            {
                // A context-free recalculation may reflect another legitimate
                // mutation. Let Vanilla run and invalidate the cached baseline
                // so the next SetLightEnabled call starts from a fresh scan.
                s_roomCache.Remove(__instance);
                return true;
            }

            __state = new RoomUpdateState(__instance, true);

            RoomLightCache cache;
            if (!s_roomCache.TryGetValue(__instance, out cache))
            {
                // First affected object in this room: preserve a complete
                // Vanilla recalculation to establish an exact native baseline.
                return true;
            }

            int[] lightLevel =
                __instance.m_roomPersistentData.m_lightLevel;

            if (lightLevel == null || lightLevel.Length < 3)
            {
                s_roomCache.Remove(__instance);
                return true;
            }

            cache.ObjectRed += s_lightChange.DeltaRed;
            cache.ObjectGreen += s_lightChange.DeltaGreen;
            cache.ObjectBlue += s_lightChange.DeltaBlue;
            s_roomCache[__instance] = cache;

            int characterLight =
                GetNativeCharacterLight(__instance);

            lightLevel[0] = cache.ObjectRed + characterLight;
            lightLevel[1] = cache.ObjectGreen + characterLight;
            lightLevel[2] = cache.ObjectBlue + characterLight;

            // Skip only the expensive native room scan. The method call itself
            // remains observable and all Harmony postfixes still execute.
            return false;
        }

        private static void RoomUpdateLightLevelPostfix(
            Room __instance,
            RoomUpdateState __state)
        {
            if (!__state.CaptureNativeBaseline ||
                __instance == null ||
                s_roomCache == null ||
                s_scanDepth <= 0 ||
                !object.ReferenceEquals(__state.Room, __instance))
            {
                return;
            }

            int[] lightLevel =
                __instance.m_roomPersistentData.m_lightLevel;

            if (lightLevel == null || lightLevel.Length < 3)
            {
                s_roomCache.Remove(__instance);
                return;
            }

            int characterLight =
                GetNativeCharacterLight(__instance);

            // This postfix is explicitly ordered before HospitalAlwaysLit.
            // Store only the native object contribution so any later character
            // light postfix can run normally on every call without being
            // accumulated into the next HRO baseline.
            s_roomCache[__instance] = new RoomLightCache(
                lightLevel[0] - characterLight,
                lightLevel[1] - characterLight,
                lightLevel[2] - characterLight);
        }

        private static int GetNativeCharacterLight(Room room)
        {
            float timer =
                room.m_roomPersistentData.m_characterLightTimer;

            if (timer <= 0f)
            {
                return 0;
            }

            return (int)(255f * Math.Min(1f, timer));
        }

        private struct LightChangeContext
        {
            internal readonly Room Room;
            internal readonly Floor Floor;
            internal readonly int DeltaRed;
            internal readonly int DeltaGreen;
            internal readonly int DeltaBlue;

            internal bool IsValid
            {
                get
                {
                    return Room != null && Floor != null;
                }
            }

            internal LightChangeContext(
                Room room,
                Floor floor,
                int deltaRed,
                int deltaGreen,
                int deltaBlue)
            {
                Room = room;
                Floor = floor;
                DeltaRed = deltaRed;
                DeltaGreen = deltaGreen;
                DeltaBlue = deltaBlue;
            }
        }

        private struct RoomLightCache
        {
            internal int ObjectRed;
            internal int ObjectGreen;
            internal int ObjectBlue;

            internal RoomLightCache(
                int objectRed,
                int objectGreen,
                int objectBlue)
            {
                ObjectRed = objectRed;
                ObjectGreen = objectGreen;
                ObjectBlue = objectBlue;
            }
        }

        private struct RoomUpdateState
        {
            internal readonly Room Room;
            internal readonly bool CaptureNativeBaseline;

            internal RoomUpdateState(
                Room room,
                bool captureNativeBaseline)
            {
                Room = room;
                CaptureNativeBaseline = captureNativeBaseline;
            }
        }
    }
}
