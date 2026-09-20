using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class JanitorCleaningManager
    {
        private static readonly MethodInfo TryToSelectTileInARoomMethod =
            AccessTools.Method(typeof(BehaviorJanitor), "TryToSelectTileInARoom");

        private static readonly MethodInfo TryToSelectIndoorTileMethod =
            AccessTools.Method(
                typeof(BehaviorJanitor),
                "TryToSelectIndoorTile",
                new Type[] { typeof(int) });

        private static readonly MethodInfo GoReturnCartMethod =
            AccessTools.Method(typeof(BehaviorJanitor), "GoReturnCart");

        private static bool s_missingNativeMethodLogged;
        private static bool s_nativeInvocationErrorLogged;

        internal static bool TryInterruptActiveProcedureRoom(BehaviorJanitor janitor)
        {
            if (!TrafficControlConfig.AvoidCleaningActiveProcedureRooms ||
                janitor == null ||
                janitor.m_state == null ||
                janitor.m_entity == null ||
                janitor.m_state.m_janitorState != BehaviorJanitorState.Cleaning)
            {
                return false;
            }

            WalkComponent walk = janitor.GetComponent<WalkComponent>();
            Room room = janitor.m_state.m_room == null
                ? null
                : janitor.m_state.m_room.GetEntity();

            // TryToSelectIndoorTile() reserves only a tile and does not populate
            // BehaviorJanitorStateData.m_room, so resolve the actual current room too.
            if (room == null && walk != null)
            {
                room = MapScriptInterface.Instance.GetRoomAt(
                    walk.GetCurrentTile(),
                    walk.GetFloorIndex());
            }

            // Never use the selector cache here. This check runs while the janitor is
            // cleaning and must always see a patient who has just entered the room.
            if (!HasActiveProcedure(room))
            {
                return false;
            }

            // Movement and cart placement stay completely native. By waiting for the
            // Cleaning state, HTC only intervenes after the janitor has finished walking.
            ReleaseRoomReservation(janitor, room);
            ReleaseReservedTile(janitor, walk);
            janitor.m_state.m_room = null;

            if (InvokeBool(TryToSelectTileInARoomMethod, janitor, null))
            {
                return true;
            }

            if (InvokeBool(
                    TryToSelectIndoorTileMethod,
                    janitor,
                    new object[] { 10 }))
            {
                return true;
            }

            if (InvokeVoid(GoReturnCartMethod, janitor, null))
            {
                return true;
            }

            return false;
        }

        internal static Vector3i FindDirtiestTileInRoomWithMatchingAssignmentAnyFloor(
            BehaviorJanitor behaviorJanitor,
            Department department,
            int threshold)
        {
            float highestDirt = 0f;
            Vector2i selectedPosition = Vector2i.ZERO_VECTOR;
            int selectedFloor = 0;

            if (behaviorJanitor == null ||
                behaviorJanitor.m_state == null ||
                department == null)
            {
                return new Vector3i(0, 0, 0);
            }

            // The protection state is room-wide. Reuse it between the native-style
            // blood pass and regular dirt pass, then discard the cache when this scan ends.
            Dictionary<Room, bool> procedureRoomCache = new Dictionary<Room, bool>();

            foreach (EntityIDPointer<Room> roomPointer in department.m_departmentPersistentData.m_rooms)
            {
                Room room = roomPointer.GetEntity();
                if (room == null || room.m_roomPersistentData == null)
                {
                    continue;
                }

                bool assignedToJanitor = behaviorJanitor.m_state.m_assignedRooms.Contains(room);
                bool unrestrictedAssignments =
                    room.m_roomPersistentData.m_assignedJanitors.Count == 0 &&
                    behaviorJanitor.m_state.m_assignedRooms.Count == 0;

                if ((!assignedToJanitor && !unrestrictedAssignments) ||
                    ShouldAvoidActiveProcedureCached(room, procedureRoomCache))
                {
                    continue;
                }

                Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];
                for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                     x <= room.m_roomPersistentData.m_positionTop.m_x;
                     x++)
                {
                    for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                         y <= room.m_roomPersistentData.m_positionTop.m_y;
                         y++)
                    {
                        if (floor.m_mapPersistentData.m_tiles[x, y].m_dirtType == DirtType.BLOOD &&
                            floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel > highestDirt &&
                            floor.m_roomTiles[x, y] != null &&
                            floor.m_roomTiles[x, y].m_roomPersistentData.m_reservedByCharacter == null &&
                            floor.m_tileObjects[x, y].GetAllObjects().Count == 0 &&
                            floor.m_accessibility[x, y] != 2)
                        {
                            highestDirt = floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel;
                            selectedPosition = new Vector2i(x, y);
                            selectedFloor = room.GetFloorIndex();
                        }
                    }
                }

                // Preserve the native priority: blood in the first eligible room wins.
                if (highestDirt > 0f)
                {
                    return new Vector3i(
                        selectedPosition.m_x,
                        selectedPosition.m_y,
                        selectedFloor);
                }
            }

            selectedPosition = Vector2i.ZERO_VECTOR;
            highestDirt = 0f;
            float deferredHighestDirt = 0f;
            Vector2i deferredPosition = Vector2i.ZERO_VECTOR;
            int deferredFloor = 0;

            foreach (EntityIDPointer<Room> roomPointer in department.m_departmentPersistentData.m_rooms)
            {
                Room room = roomPointer.GetEntity();
                if (room == null || room.m_roomPersistentData == null)
                {
                    continue;
                }

                bool assignedToJanitor = behaviorJanitor.m_state.m_assignedRooms.Contains(room);
                bool unrestrictedAssignments =
                    room.m_roomPersistentData.m_assignedJanitors.Count == 0 &&
                    behaviorJanitor.m_state.m_assignedRooms.Count == 0;

                if ((!assignedToJanitor && !unrestrictedAssignments) ||
                    ShouldAvoidActiveProcedureCached(room, procedureRoomCache))
                {
                    continue;
                }

                bool deferRoom = ShouldDeferOrdinaryCleaning(room);
                Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];
                for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                     x <= room.m_roomPersistentData.m_positionTop.m_x;
                     x++)
                {
                    for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                         y <= room.m_roomPersistentData.m_positionTop.m_y;
                         y++)
                    {
                        float dirtLevel = floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel;
                        if (dirtLevel <= (float)threshold ||
                            floor.m_mapPersistentData.m_tiles[x, y].m_user != null ||
                            floor.m_roomTiles[x, y] == null ||
                            floor.m_roomTiles[x, y].m_roomPersistentData.m_reservedByCharacter != null ||
                            floor.m_tileObjects[x, y].GetAllObjects().Count != 0 ||
                            floor.m_accessibility[x, y] == 2)
                        {
                            continue;
                        }

                        if (deferRoom)
                        {
                            if (dirtLevel > deferredHighestDirt)
                            {
                                deferredHighestDirt = dirtLevel;
                                deferredPosition = new Vector2i(x, y);
                                deferredFloor = room.GetFloorIndex();
                            }
                        }
                        else if (dirtLevel > highestDirt)
                        {
                            highestDirt = dirtLevel;
                            selectedPosition = new Vector2i(x, y);
                            selectedFloor = room.GetFloorIndex();
                        }
                    }
                }
            }

            if (selectedPosition != Vector2i.ZERO_VECTOR)
            {
                return new Vector3i(
                    selectedPosition.m_x,
                    selectedPosition.m_y,
                    selectedFloor);
            }

            return new Vector3i(
                deferredPosition.m_x,
                deferredPosition.m_y,
                deferredFloor);
        }

        internal static Vector3i FindDirtiestTileInAnyUnreservedRoomAnyFloor(
            Department department,
            int threshold)
        {
            float highestDirt = 0f;
            Vector2i selectedPosition = Vector2i.ZERO_VECTOR;
            int selectedFloor = 0;

            if (department == null)
            {
                return new Vector3i(0, 0, 0);
            }

            Dictionary<Room, bool> procedureRoomCache = new Dictionary<Room, bool>();

            foreach (EntityIDPointer<Room> roomPointer in department.m_departmentPersistentData.m_rooms)
            {
                Room room = roomPointer.GetEntity();
                if (room == null ||
                    room.m_roomPersistentData == null ||
                    ShouldAvoidActiveProcedureCached(room, procedureRoomCache))
                {
                    continue;
                }

                Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];
                for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                     x <= room.m_roomPersistentData.m_positionTop.m_x;
                     x++)
                {
                    for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                         y <= room.m_roomPersistentData.m_positionTop.m_y;
                         y++)
                    {
                        if (floor.m_mapPersistentData.m_tiles[x, y].m_dirtType == DirtType.BLOOD &&
                            floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel > highestDirt &&
                            floor.m_roomTiles[x, y] != null &&
                            floor.m_roomTiles[x, y].m_roomPersistentData.m_reservedByCharacter == null &&
                            floor.m_tileObjects[x, y].GetAllObjects().Count == 0 &&
                            floor.m_accessibility[x, y] != 2)
                        {
                            highestDirt = floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel;
                            selectedPosition = new Vector2i(x, y);
                            selectedFloor = room.GetFloorIndex();
                        }
                    }
                }

                // Preserve the native priority: blood in the first eligible room wins.
                if (highestDirt > 0f)
                {
                    return new Vector3i(
                        selectedPosition.m_x,
                        selectedPosition.m_y,
                        selectedFloor);
                }
            }

            selectedPosition = Vector2i.ZERO_VECTOR;
            highestDirt = 0f;
            float deferredHighestDirt = 0f;
            Vector2i deferredPosition = Vector2i.ZERO_VECTOR;
            int deferredFloor = 0;

            foreach (EntityIDPointer<Room> roomPointer in department.m_departmentPersistentData.m_rooms)
            {
                Room room = roomPointer.GetEntity();
                if (room == null ||
                    room.m_roomPersistentData == null ||
                    ShouldAvoidActiveProcedureCached(room, procedureRoomCache))
                {
                    continue;
                }

                bool deferRoom = ShouldDeferOrdinaryCleaning(room);
                Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];
                for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                     x <= room.m_roomPersistentData.m_positionTop.m_x;
                     x++)
                {
                    for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                         y <= room.m_roomPersistentData.m_positionTop.m_y;
                         y++)
                    {
                        float dirtLevel = floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel;
                        if (dirtLevel <= (float)threshold ||
                            floor.m_mapPersistentData.m_tiles[x, y].m_user != null ||
                            floor.m_roomTiles[x, y] == null ||
                            floor.m_roomTiles[x, y].m_roomPersistentData.m_reservedByCharacter != null ||
                            floor.m_tileObjects[x, y].GetAllObjects().Count != 0 ||
                            floor.m_accessibility[x, y] == 2)
                        {
                            continue;
                        }

                        if (deferRoom)
                        {
                            if (dirtLevel > deferredHighestDirt)
                            {
                                deferredHighestDirt = dirtLevel;
                                deferredPosition = new Vector2i(x, y);
                                deferredFloor = room.GetFloorIndex();
                            }
                        }
                        else if (dirtLevel > highestDirt)
                        {
                            highestDirt = dirtLevel;
                            selectedPosition = new Vector2i(x, y);
                            selectedFloor = room.GetFloorIndex();
                        }
                    }
                }
            }

            if (selectedPosition != Vector2i.ZERO_VECTOR)
            {
                return new Vector3i(
                    selectedPosition.m_x,
                    selectedPosition.m_y,
                    selectedFloor);
            }

            return new Vector3i(
                deferredPosition.m_x,
                deferredPosition.m_y,
                deferredFloor);
        }

        internal static Vector2i FindClosestDirtyIndoorsTile(
            Vector2i position,
            int floorIndex,
            int threshold)
        {
            int closestDistance = int.MaxValue;
            Vector2i selectedPosition = Vector2i.ZERO_VECTOR;
            Floor floor = Hospital.Instance.m_floors[floorIndex];

            // The vanilla fallback scans the floor twice (blood, then regular dirt).
            // Cache only the room-wide HTC decision during this one call.
            Dictionary<Room, bool> procedureRoomCache = new Dictionary<Room, bool>();
            Dictionary<Room, bool> nightCleaningCache = new Dictionary<Room, bool>();

            for (int x = 0; x < floor.Size.m_x; x++)
            {
                for (int y = 0; y < floor.Size.m_y; y++)
                {
                    int distance =
                        (x - position.m_x) * (x - position.m_x) +
                        (y - position.m_y) * (y - position.m_y);

                    if (distance < closestDistance &&
                        floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel > 0f &&
                        floor.m_mapPersistentData.m_tiles[x, y].m_dirtType == DirtType.BLOOD &&
                        floor.m_mapPersistentData.m_tiles[x, y].m_user == null &&
                        floor.m_mapPersistentData.m_foundationsLayer.m_foundations[x, y] == 2 &&
                        !floor.m_tileObjects[x, y].IsAnyObjectBlocking() &&
                        !ShouldAvoidActiveProcedureCached(floor.m_roomTiles[x, y], procedureRoomCache))
                    {
                        closestDistance = distance;
                        selectedPosition = new Vector2i(x, y);
                    }
                }
            }

            if (selectedPosition != Vector2i.ZERO_VECTOR)
            {
                return selectedPosition;
            }

            int deferredClosestDistance = int.MaxValue;
            Vector2i deferredPosition = Vector2i.ZERO_VECTOR;
            closestDistance = int.MaxValue;

            for (int x = 0; x < floor.Size.m_x; x++)
            {
                for (int y = 0; y < floor.Size.m_y; y++)
                {
                    int distance =
                        (x - position.m_x) * (x - position.m_x) +
                        (y - position.m_y) * (y - position.m_y);

                    if (floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel <= (float)threshold ||
                        floor.m_mapPersistentData.m_tiles[x, y].m_user != null ||
                        floor.m_mapPersistentData.m_foundationsLayer.m_foundations[x, y] != 2 ||
                        floor.m_tileObjects[x, y].IsAnyObjectBlocking() ||
                        ShouldAvoidActiveProcedureCached(floor.m_roomTiles[x, y], procedureRoomCache))
                    {
                        continue;
                    }

                    if (ShouldDeferOrdinaryCleaningCached(
                            floor.m_roomTiles[x, y],
                            nightCleaningCache))
                    {
                        if (distance < deferredClosestDistance)
                        {
                            deferredClosestDistance = distance;
                            deferredPosition = new Vector2i(x, y);
                        }
                    }
                    else if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        selectedPosition = new Vector2i(x, y);
                    }
                }
            }

            if (selectedPosition != Vector2i.ZERO_VECTOR)
            {
                return selectedPosition;
            }

            return deferredPosition;
        }

        private static bool ShouldDeferOrdinaryCleaningCached(
            Room room,
            Dictionary<Room, bool> nightCleaningCache)
        {
            if (room == null)
            {
                return false;
            }

            bool deferred;
            if (nightCleaningCache != null &&
                nightCleaningCache.TryGetValue(room, out deferred))
            {
                return deferred;
            }

            deferred = ShouldDeferOrdinaryCleaning(room);

            if (nightCleaningCache != null)
            {
                nightCleaningCache[room] = deferred;
            }

            return deferred;
        }

        internal static bool ShouldDeferOrdinaryCleaning(Room room)
        {
            if (!TrafficControlConfig.ReduceOccupiedHospitalizationCleaningAtNight ||
                DayTime.Instance == null ||
                DayTime.Instance.GetShift() != Shift.NIGHT ||
                !IsHospitalizationRoom(room))
            {
                return false;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            return room.GetBedReservationPercent(floor) > 0f;
        }

        private static bool ShouldAvoidActiveProcedureCached(
            Room room,
            Dictionary<Room, bool> procedureRoomCache)
        {
            return TrafficControlConfig.AvoidCleaningActiveProcedureRooms &&
                   HasActiveProcedureCached(room, procedureRoomCache);
        }

        private static bool HasActiveProcedureCached(
            Room room,
            Dictionary<Room, bool> procedureRoomCache)
        {
            if (room == null)
            {
                return false;
            }

            bool blocked;
            if (procedureRoomCache != null &&
                procedureRoomCache.TryGetValue(room, out blocked))
            {
                return blocked;
            }

            blocked = HasActiveProcedure(room);

            if (procedureRoomCache != null)
            {
                procedureRoomCache[room] = blocked;
            }

            return blocked;
        }

        internal static bool HasActiveProcedure(Room room)
        {
            if (room == null || room.m_roomPersistentData == null)
            {
                return false;
            }

            GameDBRoomType roomType = room.m_roomPersistentData.m_roomType.Entry;

            // Long-stay hospitalization rooms must remain cleanable even while occupied.
            // This covers wards, ICU, trauma/observation-style rooms and DLC room types
            // that use the game's native hospitalization tag.
            if (roomType != null && roomType.HasTag("hospitalization"))
            {
                return false;
            }

            Entity procedureOwner = GetCurrentProcedureOwner(room);

            // Operating rooms are special: surgery reserves the whole room before the
            // patient arrives. Keep janitors out as soon as a surgery script reserves it,
            // and for the whole time a procedure owner is active.
            if (roomType != null && roomType.HasTag("operating_room"))
            {
                if (procedureOwner != null)
                {
                    return true;
                }

                Entity reservedBy = GetRoomReservationOwner(room);
                return reservedBy is ProcedureScript;
            }

            if (procedureOwner == null)
            {
                return false;
            }

            // For offices, radiology, CT/MRI and other short procedure rooms, the room
            // becomes protected only when that procedure's patient is physically inside.
            // Reservation and travel time therefore remain available for cleaning.
            Entity patient = GetProcedurePatient(procedureOwner);
            return IsEntityPhysicallyInsideRoom(patient, room);
        }

        internal static Entity GetCurrentProcedureOwner(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_currentProcedureOwner == null)
            {
                return null;
            }

            return room.m_roomPersistentData.m_currentProcedureOwner.GetEntity();
        }

        internal static Entity GetProcedurePatient(Entity procedureOwner)
        {
            if (procedureOwner == null)
            {
                return null;
            }

            ProcedureScript procedureScript = procedureOwner as ProcedureScript;
            if (procedureScript != null &&
                procedureScript.m_stateData != null &&
                procedureScript.m_stateData.m_procedureScene != null &&
                procedureScript.m_stateData.m_procedureScene.m_patient != null)
            {
                return procedureScript.m_stateData.m_procedureScene.m_patient.GetEntity();
            }

            if (procedureOwner.GetComponent<BehaviorPatient>() != null)
            {
                // HospitalizationComponent.MarkReservedRoomProcedureOwner() uses the
                // hospitalized patient itself as m_currentProcedureOwner.
                return procedureOwner;
            }

            return null;
        }

        internal static bool IsEntityPhysicallyInsideRoom(Entity entity, Room room)
        {
            if (entity == null || room == null)
            {
                return false;
            }

            WalkComponent walk = entity.GetComponent<WalkComponent>();
            if (walk == null || walk.GetFloorIndex() != room.GetFloorIndex())
            {
                return false;
            }

            return room.IsPositionInRoom(walk.GetCurrentTile());
        }

        internal static bool IsHospitalizationRoom(Room room)
        {
            return room != null &&
                   room.m_roomPersistentData != null &&
                   room.m_roomPersistentData.m_roomType.Entry != null &&
                   room.m_roomPersistentData.m_roomType.Entry.HasTag("hospitalization");
        }

        internal static bool IsOperatingRoom(Room room)
        {
            return room != null &&
                   room.m_roomPersistentData != null &&
                   room.m_roomPersistentData.m_roomType.Entry != null &&
                   room.m_roomPersistentData.m_roomType.Entry.HasTag("operating_room");
        }

        internal static Entity GetRoomReservationOwner(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_reservedByCharacter == null)
            {
                return null;
            }

            return room.m_roomPersistentData.m_reservedByCharacter.GetEntity();
        }

        private static void ReleaseRoomReservation(BehaviorJanitor janitor, Room room)
        {
            if (room == null || room.m_roomPersistentData == null)
            {
                return;
            }

            EntityIDPointer<Entity> reservedBy = room.m_roomPersistentData.m_reservedByCharacter;
            if (reservedBy != null && ReferenceEquals(reservedBy.GetEntity(), janitor.m_entity))
            {
                room.m_roomPersistentData.m_reservedByCharacter = null;
            }
        }

        private static void ReleaseReservedTile(BehaviorJanitor janitor, WalkComponent walk)
        {
            Vector2i reservedTile = janitor.m_state.m_reservedTile;
            if (reservedTile == Vector2i.ZERO_VECTOR)
            {
                return;
            }

            // The janitor is abandoning this target. Clear its local reservation pointer
            // even if the tile reservation was already changed by native game logic.
            janitor.m_state.m_reservedTile = Vector2i.ZERO_VECTOR;

            if (walk == null)
            {
                return;
            }

            int floorIndex = walk.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (reservedTile.m_x < 0 || reservedTile.m_y < 0 ||
                reservedTile.m_x >= floor.Size.m_x || reservedTile.m_y >= floor.Size.m_y)
            {
                return;
            }

            EntityIDPointer<Entity> tileUser =
                floor.m_mapPersistentData.m_tiles[reservedTile.m_x, reservedTile.m_y].m_user;

            if (tileUser != null && ReferenceEquals(tileUser.GetEntity(), janitor.m_entity))
            {
                floor.m_mapPersistentData.m_tiles[reservedTile.m_x, reservedTile.m_y].m_user = null;
            }
        }

        private static bool InvokeBool(MethodInfo method, BehaviorJanitor janitor, object[] arguments)
        {
            if (method == null)
            {
                LogMissingNativeMethodOnce();
                return false;
            }

            try
            {
                object result = method.Invoke(janitor, arguments);
                return result is bool && (bool)result;
            }
            catch (Exception exception)
            {
                LogNativeInvocationErrorOnce(exception);
                return false;
            }
        }

        private static bool InvokeVoid(MethodInfo method, BehaviorJanitor janitor, object[] arguments)
        {
            if (method == null)
            {
                LogMissingNativeMethodOnce();
                return false;
            }

            try
            {
                method.Invoke(janitor, arguments);
                return true;
            }
            catch (Exception exception)
            {
                LogNativeInvocationErrorOnce(exception);
                return false;
            }
        }

        private static void LogMissingNativeMethodOnce()
        {
            if (s_missingNativeMethodLogged)
            {
                return;
            }

            s_missingNativeMethodLogged = true;
            Plugin.Log?.LogWarning(
                "Janitor active-procedure avoidance could not resolve one or more native BehaviorJanitor methods.");
        }

        private static void LogNativeInvocationErrorOnce(Exception exception)
        {
            if (s_nativeInvocationErrorLogged)
            {
                return;
            }

            s_nativeInvocationErrorLogged = true;
            Exception root = exception.InnerException ?? exception;
            Plugin.Log?.LogError(
                "Janitor active-procedure avoidance failed while calling native BehaviorJanitor logic: " +
                root.GetType().FullName + ": " + root.Message);
        }
    }
}
