using System;
using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    internal static class JanitorCleaningContentionDiagnostics
    {
        private static Dictionary<string, Entity>
            _hfxReservations;

        private static string GetKey(
            int floorIndex,
            Vector2i position)
        {
            return floorIndex + ":" +
                position.m_x + "," +
                position.m_y;
        }

        internal static void RegisterHfxReservation(
            int floorIndex,
            Vector2i position,
            Entity owner)
        {
            if (!HfxDiagnostics.Enabled ||
                owner == null)
            {
                return;
            }

            if (_hfxReservations == null)
            {
                _hfxReservations =
                    new Dictionary<string, Entity>();
            }

            _hfxReservations[
                GetKey(floorIndex, position)] =
                owner;
        }

        internal static void Unregister(
            int floorIndex,
            Vector2i position)
        {
            if (_hfxReservations == null)
            {
                return;
            }

            _hfxReservations.Remove(
                GetKey(floorIndex, position));
        }

        internal static bool TryGetLiveHfxReservationOwner(
            int floorIndex,
            Vector2i position,
            out Entity owner)
        {
            owner = null;

            if (!HfxDiagnostics.Enabled ||
                _hfxReservations == null ||
                Hospital.Instance == null ||
                floorIndex < 0 ||
                floorIndex >=
                    Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            string key =
                GetKey(floorIndex, position);

            if (!_hfxReservations.TryGetValue(
                    key,
                    out owner) ||
                owner == null)
            {
                return false;
            }

            Floor floor =
                Hospital.Instance.m_floors[floorIndex];

            EntityIDPointer<Entity> pointer =
                floor.m_mapPersistentData
                    .m_tiles[position.m_x, position.m_y]
                    .m_user;

            Entity currentOwner =
                pointer == null
                    ? null
                    : pointer.GetEntity();

            BehaviorJanitor ownerJanitor =
                owner.GetComponent<BehaviorJanitor>();

            if (!object.ReferenceEquals(
                    currentOwner,
                    owner) ||
                ownerJanitor == null ||
                ownerJanitor.m_state == null ||
                ownerJanitor.m_state.m_reservedTile !=
                    position)
            {
                _hfxReservations.Remove(key);
                owner = null;
                return false;
            }

            return true;
        }

        internal static Vector2i FindSelectionIgnoringReservations(
            BehaviorJanitor janitor,
            Room room,
            out string selector,
            out float skillLevel,
            out float dirtLevel)
        {
            selector = "none";
            skillLevel = 0f;
            dirtLevel = 0f;

            if (janitor == null ||
                room == null ||
                Hospital.Instance == null)
            {
                return Vector2i.ZERO_VECTOR;
            }

            EmployeeComponent employee =
                janitor.GetComponent<EmployeeComponent>();
            WalkComponent walk =
                janitor.GetComponent<WalkComponent>();

            if (employee == null ||
                walk == null ||
                walk.m_state == null)
            {
                return Vector2i.ZERO_VECTOR;
            }

            skillLevel =
                employee.GetSkillLevel(
                    "SKILL_JANITOR_QUALIF_EFFICIENCY");

            if (skillLevel <= 2f)
            {
                selector = "dirtiest";
                return FindDirtiestIgnoringReservations(
                    room,
                    out dirtLevel);
            }

            selector = "closest";
            return FindClosestIgnoringReservations(
                room,
                walk.GetCurrentTile(),
                out dirtLevel);
        }

        private static Vector2i FindClosestIgnoringReservations(
            Room room,
            Vector2i characterPosition,
            out float selectedDirt)
        {
            selectedDirt = 0f;
            float dirt = 0f;
            float bestDistance =
                2.1474836E+09f;
            Vector2i result =
                Vector2i.ZERO_VECTOR;

            Floor floor =
                Hospital.Instance.m_floors[
                    room.GetFloorIndex()];
            RoomPersistentData state =
                room.m_roomPersistentData;

            for (int x =
                    state.m_positionBottom.m_x;
                x <= state.m_positionTop.m_x;
                x++)
            {
                for (int y =
                        state.m_positionBottom.m_y;
                    y <= state.m_positionTop.m_y;
                    y++)
                {
                    Vector2i position =
                        new Vector2i(x, y);

                    if (room.IsPositionInRoom(
                            position) &&
                        floor.m_mapPersistentData
                            .m_tiles[x, y]
                            .m_dirtType ==
                            DirtType.BLOOD &&
                        floor.m_mapPersistentData
                            .m_tiles[x, y]
                            .m_dirtLevel > dirt &&
                        floor.m_roomTiles[x, y] !=
                            null &&
                        floor.m_tileObjects[x, y]
                            .GetAllObjects().Count == 0 &&
                        floor.m_accessibility[x, y] !=
                            2)
                    {
                        float distance =
                            Pathfinder
                                .getTaxiDistanceCost(
                                    characterPosition,
                                    position);

                        if (distance <
                            bestDistance)
                        {
                            dirt =
                                floor.m_mapPersistentData
                                    .m_tiles[x, y]
                                    .m_dirtLevel;
                            bestDistance = distance;
                            result = position;
                        }
                    }
                }
            }

            if (dirt > 0f)
            {
                selectedDirt = dirt;
                return result;
            }

            for (int x =
                    state.m_positionBottom.m_x;
                x <= state.m_positionTop.m_x;
                x++)
            {
                for (int y =
                        state.m_positionBottom.m_y;
                    y <= state.m_positionTop.m_y;
                    y++)
                {
                    float candidateDirt =
                        floor.m_mapPersistentData
                            .m_tiles[x, y]
                            .m_dirtLevel;

                    Vector2i position =
                        new Vector2i(x, y);

                    if (room.IsPositionInRoom(
                            position) &&
                        candidateDirt > 10f &&
                        candidateDirt > dirt &&
                        floor.m_roomTiles[x, y] !=
                            null &&
                        floor.m_tileObjects[x, y]
                            .GetAllObjects().Count == 0 &&
                        floor.m_accessibility[x, y] !=
                            2)
                    {
                        float distance =
                            Pathfinder
                                .getTaxiDistanceCost(
                                    characterPosition,
                                    position);

                        if (distance <
                            bestDistance)
                        {
                            dirt = candidateDirt;
                            bestDistance = distance;
                            result = position;
                        }
                    }
                }
            }

            selectedDirt = dirt;
            return result;
        }

        private static Vector2i FindDirtiestIgnoringReservations(
            Room room,
            out float selectedDirt)
        {
            selectedDirt = 0f;
            float dirt = 0f;
            Vector2i result =
                Vector2i.ZERO_VECTOR;

            Floor floor =
                Hospital.Instance.m_floors[
                    room.GetFloorIndex()];
            RoomPersistentData state =
                room.m_roomPersistentData;

            for (int x =
                    state.m_positionBottom.m_x;
                x <= state.m_positionTop.m_x;
                x++)
            {
                for (int y =
                        state.m_positionBottom.m_y;
                    y <= state.m_positionTop.m_y;
                    y++)
                {
                    Vector2i position =
                        new Vector2i(x, y);

                    if (room.IsPositionInRoom(
                            position) &&
                        floor.m_mapPersistentData
                            .m_tiles[x, y]
                            .m_dirtType ==
                            DirtType.BLOOD &&
                        floor.m_mapPersistentData
                            .m_tiles[x, y]
                            .m_dirtLevel > dirt &&
                        floor.m_roomTiles[x, y] !=
                            null &&
                        floor.m_tileObjects[x, y]
                            .GetAllObjects().Count == 0 &&
                        floor.m_accessibility[x, y] !=
                            2)
                    {
                        dirt =
                            floor.m_mapPersistentData
                                .m_tiles[x, y]
                                .m_dirtLevel;
                        result = position;
                    }
                }
            }

            if (dirt > 0f)
            {
                selectedDirt = dirt;
                return result;
            }

            for (int x =
                    state.m_positionBottom.m_x;
                x <= state.m_positionTop.m_x;
                x++)
            {
                for (int y =
                        state.m_positionBottom.m_y;
                    y <= state.m_positionTop.m_y;
                    y++)
                {
                    float candidateDirt =
                        floor.m_mapPersistentData
                            .m_tiles[x, y]
                            .m_dirtLevel;

                    Vector2i position =
                        new Vector2i(x, y);

                    if (room.IsPositionInRoom(
                            position) &&
                        candidateDirt > 15f &&
                        candidateDirt > dirt &&
                        floor.m_roomTiles[x, y] !=
                            null &&
                        floor.m_tileObjects[x, y]
                            .GetAllObjects().Count == 0 &&
                        floor.m_accessibility[x, y] !=
                            2)
                    {
                        dirt = candidateDirt;
                        result = position;
                    }
                }
            }

            selectedDirt = dirt;
            return result;
        }

        internal static void Reset()
        {

            if (_hfxReservations != null)
            {
                _hfxReservations.Clear();
                _hfxReservations = null;
            }

        }
    }

    internal static class JanitorRoomTileSelectionRules
    {
        internal static Vector2i CorrectDirtiestSelection(
            Room room,
            bool bloodOnly,
            int threshold,
            Vector2i selected)
        {
            if (!FixConfig.PreventDuplicateJanitorCleaning ||
                room == null ||
                selected == Vector2i.ZERO_VECTOR ||
                room.IsPositionInRoom(selected))
            {
                return selected;
            }

            // Vanilla's BLOOD pass already checks room membership. An
            // out-of-room result therefore comes from the normal-dirt pass.
            if (bloodOnly)
            {
                return Vector2i.ZERO_VECTOR;
            }

            Floor floor;
            if (!TryGetFloor(room, out floor))
            {
                return Vector2i.ZERO_VECTOR;
            }

            float dirt = 0f;
            Vector2i result = Vector2i.ZERO_VECTOR;
            RoomPersistentData state = room.m_roomPersistentData;

            for (int x = state.m_positionBottom.m_x;
                 x <= state.m_positionTop.m_x;
                 x++)
            {
                for (int y = state.m_positionBottom.m_y;
                     y <= state.m_positionTop.m_y;
                     y++)
                {
                    Vector2i position = new Vector2i(x, y);
                    float candidateDirt =
                        floor.m_mapPersistentData
                            .m_tiles[x, y]
                            .m_dirtLevel;

                    if (room.IsPositionInRoom(position) &&
                        candidateDirt > (float)threshold &&
                        candidateDirt > dirt &&
                        floor.m_roomTiles[x, y] != null &&
                        floor.m_mapPersistentData
                            .m_tiles[x, y]
                            .m_user == null &&
                        floor.m_tileObjects[x, y]
                            .GetAllObjects().Count == 0 &&
                        floor.m_accessibility[x, y] != 2)
                    {
                        dirt = candidateDirt;
                        result = position;
                    }
                }
            }

            return result;
        }

        internal static Vector2i CorrectClosestSelection(
            Room room,
            Vector2i characterPosition,
            Vector2i selected)
        {
            if (!FixConfig.PreventDuplicateJanitorCleaning ||
                room == null ||
                selected == Vector2i.ZERO_VECTOR ||
                room.IsPositionInRoom(selected))
            {
                return selected;
            }

            Floor floor;
            if (!TryGetFloor(room, out floor))
            {
                return Vector2i.ZERO_VECTOR;
            }

            float dirt = 0f;
            float bestDistance = 2.1474836E+09f;
            Vector2i result = Vector2i.ZERO_VECTOR;
            RoomPersistentData state = room.m_roomPersistentData;

            for (int x = state.m_positionBottom.m_x;
                 x <= state.m_positionTop.m_x;
                 x++)
            {
                for (int y = state.m_positionBottom.m_y;
                     y <= state.m_positionTop.m_y;
                     y++)
                {
                    Vector2i position = new Vector2i(x, y);
                    float candidateDirt =
                        floor.m_mapPersistentData
                            .m_tiles[x, y]
                            .m_dirtLevel;

                    if (!room.IsPositionInRoom(position) ||
                        candidateDirt <= 10f ||
                        candidateDirt <= dirt ||
                        floor.m_roomTiles[x, y] == null ||
                        floor.m_mapPersistentData
                            .m_tiles[x, y]
                            .m_user != null ||
                        floor.m_tileObjects[x, y]
                            .GetAllObjects().Count != 0 ||
                        floor.m_accessibility[x, y] == 2)
                    {
                        continue;
                    }

                    float distance =
                        Pathfinder.getTaxiDistanceCost(
                            characterPosition,
                            position);

                    if (distance < bestDistance)
                    {
                        dirt = candidateDirt;
                        bestDistance = distance;
                        result = position;
                    }
                }
            }

            return result;
        }

        private static bool TryGetFloor(
            Room room,
            out Floor floor)
        {
            floor = null;

            if (room == null ||
                room.m_roomPersistentData == null ||
                Hospital.Instance == null)
            {
                return false;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            floor = Hospital.Instance.m_floors[floorIndex];
            return floor != null;
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindDirtiestTileInARoom),
        new Type[] { typeof(Room), typeof(bool), typeof(int) })]
    [HarmonyAfter("Loupito75:HospitalTrafficControl")]
    internal static class JanitorDirtiestRoomBoundaryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Room room,
            bool bloodOnly,
            int threshold,
            ref Vector2i __result)
        {
            __result =
                JanitorRoomTileSelectionRules
                    .CorrectDirtiestSelection(
                        room,
                        bloodOnly,
                        threshold,
                        __result);
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindClosestDirtyTileInARoom),
        new Type[] { typeof(Room), typeof(Vector2i) })]
    [HarmonyAfter("Loupito75:HospitalTrafficControl")]
    internal static class JanitorClosestRoomBoundaryPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Room room,
            Vector2i characterPosition,
            ref Vector2i __result)
        {
            __result =
                JanitorRoomTileSelectionRules
                    .CorrectClosestSelection(
                        room,
                        characterPosition,
                        __result);
        }
    }

    internal static class JanitorCleaningTileReservation
    {
        internal static void ReserveCurrentDestination(BehaviorJanitor janitor)
        {
            if (!FixConfig.PreventDuplicateJanitorCleaning ||
                janitor == null ||
                janitor.m_state == null ||
                janitor.m_state.m_room == null ||
                janitor.m_state.m_room.GetEntity() == null ||
                janitor.m_state.m_reservedTile != Vector2i.ZERO_VECTOR)
            {
                return;
            }

            WalkComponent walk = janitor.GetComponent<WalkComponent>();
            if (walk == null || walk.m_state == null)
            {
                return;
            }

            int floorIndex = walk.m_state.m_destinationFloor;
            Vector2i target = new Vector2i(
                (int)walk.m_state.m_destination.m_x,
                (int)walk.m_state.m_destination.m_y);

            if (target == Vector2i.ZERO_VECTOR ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count ||
                janitor.m_state.m_room.GetEntity().GetFloorIndex() != floorIndex ||
                !janitor.m_state.m_room.GetEntity().IsPositionInRoom(target))
            {
                return;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor.m_mapPersistentData.m_tiles[target.m_x, target.m_y].m_user != null)
            {
                return;
            }

            MapScriptInterface.Instance.ReserveTile(target, janitor.m_entity, floorIndex);
            janitor.m_state.m_reservedTile = target;

            JanitorCleaningContentionDiagnostics
                .RegisterHfxReservation(
                    floorIndex,
                    target,
                    janitor.m_entity);

            if (HfxDiagnostics.Enabled)
            {
                string janitorLabel = HfxDiagnostics.EntityLabel(janitor.m_entity);
                HfxDiagnostics.InterventionRateLimited(
                    "PreventDuplicateJanitorCleaning",
                    "BehaviorJanitor.TryToSelectTileInCurrentRoom",
                    "tile-reserved",
                    "janitor={" +
                    HfxDiagnostics.EntityContext(janitor.m_entity) +
                    "} target=" +
                    floorIndex + ":" +
                    target.m_x + "," + target.m_y +
                    " room={" +
                    HfxDiagnostics.RoomContext(
                        janitor.m_state.m_room.GetEntity()) +
                    "}",
                    "PreventDuplicateJanitorCleaning|" + janitorLabel,
                    2000);
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorJanitor), "TryToSelectTileInCurrentRoom")]
    internal static class JanitorRoomTileReservationPatch
    {
        private sealed class SelectionState
        {
            internal Room Room;
            internal int FloorIndex;
            internal Vector2i WouldChoose;
            internal Entity ReservedBy;
            internal string Selector;
            internal float SkillLevel;
            internal float DirtLevel;
        }

        [HarmonyPrefix]
        private static void Prefix(
            BehaviorJanitor __instance,
            out SelectionState __state)
        {
            __state = null;

            if (!HfxDiagnostics.Enabled ||
                __instance == null ||
                __instance.m_state == null ||
                __instance.m_state.m_room == null ||
                !__instance.m_state.m_room.CheckEntity())
            {
                return;
            }

            Room room =
                __instance.m_state.m_room.GetEntity();
            if (room == null)
            {
                return;
            }

            string selector;
            float skillLevel;
            float dirtLevel;
            Vector2i wouldChoose =
                JanitorCleaningContentionDiagnostics
                    .FindSelectionIgnoringReservations(
                        __instance,
                        room,
                        out selector,
                        out skillLevel,
                        out dirtLevel);

            if (wouldChoose ==
                Vector2i.ZERO_VECTOR)
            {
                return;
            }

            int floorIndex =
                room.GetFloorIndex();
            Entity reservedBy;
            if (!JanitorCleaningContentionDiagnostics
                    .TryGetLiveHfxReservationOwner(
                        floorIndex,
                        wouldChoose,
                        out reservedBy) ||
                reservedBy == null ||
                object.ReferenceEquals(
                    reservedBy,
                    __instance.m_entity))
            {
                return;
            }

            __state =
                new SelectionState();
            __state.Room = room;
            __state.FloorIndex = floorIndex;
            __state.WouldChoose = wouldChoose;
            __state.ReservedBy = reservedBy;
            __state.Selector = selector;
            __state.SkillLevel = skillLevel;
            __state.DirtLevel = dirtLevel;
        }

        [HarmonyPostfix]
        private static void Postfix(
            BehaviorJanitor __instance,
            bool __result,
            SelectionState __state)
        {
            if (HfxDiagnostics.Enabled &&
                __state != null &&
                __instance != null)
            {
                WalkComponent walk =
                    __instance.GetComponent<WalkComponent>();

                Vector2i actual =
                    Vector2i.ZERO_VECTOR;
                int actualFloor = -1;

                if (__result &&
                    walk != null &&
                    walk.m_state != null)
                {
                    actual =
                        new Vector2i(
                            (int)walk.m_state
                                .m_destination.m_x,
                            (int)walk.m_state
                                .m_destination.m_y);
                    actualFloor =
                        walk.m_state
                            .m_destinationFloor;
                }

                if (!__result ||
                    actualFloor !=
                        __state.FloorIndex ||
                    actual !=
                        __state.WouldChoose)
                {
                    HfxDiagnostics.Result(
                        "PreventDuplicateJanitorCleaning",
                        "BehaviorJanitor.TryToSelectTileInCurrentRoom",
                        "contention-avoided",
                        "janitor={" +
                        HfxDiagnostics.EntityContext(
                            __instance.m_entity) +
                        "} wouldChoose=" +
                        __state.FloorIndex + ":" +
                        __state.WouldChoose.m_x +
                        "," +
                        __state.WouldChoose.m_y +
                        " reservedBy={" +
                        HfxDiagnostics.EntityContext(
                            __state.ReservedBy) +
                        "} actualSelection=" +
                        (__result
                            ? actualFloor + ":" +
                              actual.m_x + "," +
                              actual.m_y
                            : "none") +
                        " vanillaResult=" +
                        __result +
                        " selector=" +
                        __state.Selector +
                        " skill=" +
                        __state.SkillLevel.ToString(
                            "0.###",
                            System.Globalization
                                .CultureInfo.InvariantCulture) +
                        " dirt=" +
                        __state.DirtLevel.ToString(
                            "0.###",
                            System.Globalization
                                .CultureInfo.InvariantCulture) +
                        " room={" +
                        HfxDiagnostics.RoomContext(
                            __state.Room) +
                        "} reservationSource=HFX");


                }
            }

            if (__result)
            {
                JanitorCleaningTileReservation
                    .ReserveCurrentDestination(
                        __instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FreeTile),
        new Type[] { typeof(Vector2i), typeof(int) })]
    internal static class JanitorHfxReservationCleanupDiagnosticPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return FixConfig.PreventDuplicateJanitorCleaning &&
                FixConfig.EnableDiagnostics;
        }

        [HarmonyPostfix]
        private static void Postfix(
            Vector2i position,
            int floorIndex)
        {
            JanitorCleaningContentionDiagnostics
                .Unregister(
                    floorIndex,
                    position);
        }
    }

    [HarmonyPatch(typeof(InGameMenuController), "Destroy")]
    internal static class JanitorContentionDiagnosticUnloadPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            JanitorCleaningContentionDiagnostics.Reset();
        }
    }

    [HarmonyPatch(typeof(BehaviorJanitor), "UpdateStateWalkingToCartToNextRoom")]
    internal static class JanitorFirstRoomDestinationReservationPatch
    {
        private static void Postfix(BehaviorJanitor __instance)
        {
            if (__instance == null ||
                __instance.m_state == null ||
                __instance.m_state.m_janitorState != BehaviorJanitorState.WalkingToNextRoom)
            {
                return;
            }

            JanitorCleaningTileReservation.ReserveCurrentDestination(__instance);
        }
    }
    internal static class JanitorCleaningDiagnosticContext
    {
        [ThreadStatic]
        private static BehaviorJanitor _current;

        internal static BehaviorJanitor Current
        {
            get { return _current; }
            set { _current = value; }
        }
    }

    [HarmonyPatch(
        typeof(BehaviorJanitor),
        "SelectNextAction")]
    internal static class JanitorCleanCallContextDiagnosticPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return FixConfig.PreventDuplicateJanitorCleaning &&
                FixConfig.EnableDiagnostics;
        }

        [HarmonyPrefix]
        private static void Prefix(
            BehaviorJanitor __instance,
            ref BehaviorJanitor __state)
        {
            __state =
                JanitorCleaningDiagnosticContext.Current;
            JanitorCleaningDiagnosticContext.Current =
                __instance;
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(
            Exception __exception,
            BehaviorJanitor __state)
        {
            JanitorCleaningDiagnosticContext.Current =
                __state;
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.CleanTile),
        new Type[] { typeof(Vector2i), typeof(int) })]
    internal static class JanitorCleanCallResultDiagnosticPatch
    {
        private sealed class CleanState
        {
            internal BehaviorJanitor Janitor;
            internal bool ReservationOwned;
            internal bool ReservationMatchesState;
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            return FixConfig.PreventDuplicateJanitorCleaning &&
                FixConfig.EnableDiagnostics;
        }

        [HarmonyPrefix]
        private static void Prefix(
            Vector2i position,
            int floorIndex,
            out CleanState __state)
        {
            __state = null;

            if (!HfxDiagnostics.Enabled)
            {
                return;
            }

            BehaviorJanitor janitor =
                JanitorCleaningDiagnosticContext.Current;
            if (janitor == null ||
                janitor.m_state == null ||
                Hospital.Instance == null ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return;
            }

            Floor floor =
                Hospital.Instance.m_floors[floorIndex];
            EntityIDPointer<Entity> reservationOwner =
                floor.m_mapPersistentData
                    .m_tiles[position.m_x, position.m_y]
                    .m_user;

            __state =
                new CleanState();
            __state.Janitor =
                janitor;
            __state.ReservationOwned =
                reservationOwner != null &&
                object.ReferenceEquals(
                    reservationOwner.GetEntity(),
                    janitor.m_entity);
            __state.ReservationMatchesState =
                janitor.m_state.m_reservedTile ==
                position;
        }

        [HarmonyPostfix]
        private static void Postfix(
            Vector2i position,
            int floorIndex,
            CleanState __state)
        {
            if (!HfxDiagnostics.Enabled ||
                __state == null ||
                __state.Janitor == null)
            {
                return;
            }

            HfxDiagnostics.Result(
                "PreventDuplicateJanitorCleaning",
                "BehaviorJanitor.SelectNextAction -> MapScriptInterface.CleanTile",
                "clean-call-confirmed",
                "janitor={" +
                HfxDiagnostics.EntityContext(
                    __state.Janitor.m_entity) +
                "} target=" +
                floorIndex + ":" +
                position.m_x + "," + position.m_y +
                " room={" +
                HfxDiagnostics.RoomContext(
                    __state.Janitor.m_state != null &&
                    __state.Janitor.m_state.m_room != null &&
                    __state.Janitor.m_state.m_room.CheckEntity()
                        ? __state.Janitor.m_state.m_room.GetEntity()
                        : null) +
                "} reservationOwnedBeforeClean=" +
                __state.ReservationOwned +
                " reservedTileMatches=" +
                __state.ReservationMatchesState);
        }
    }

}
