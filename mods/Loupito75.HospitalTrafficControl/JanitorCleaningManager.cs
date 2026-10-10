using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
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

        private const float ProtectedRoomWaitRecheckSeconds = 1f;
        private const float ProtectedRoomApproachTimeoutSeconds = 45f;

        // Optional occupied-room waiting must never park a janitor directly
        // in a doorway. These values mirror the proven HPO/HSH staging idea:
        // identify the nearby public doorway anchor, then prefer a natural
        // public position a few pathfinding tiles away from it.
        private const int ProtectedRoomWaitDoorAnchorRadius = 2;
        private const int ProtectedRoomWaitPublicRadius = 5;
        private const float ProtectedRoomWaitIdealDoorDistance = 3.5f;
        private const float ProtectedRoomWaitMaximumDoorDistance = 7f;
        private const float ProtectedRoomWaitMinimumDoorDistance = 2f;

        // Native speech bubbles atlas: row 2, column 9 (16 icons per row,
        // zero-based linear index). Provisional icon until a custom wait icon.
        private const int ProtectedRoomWaitBubbleIndex = 24;

        private sealed class ProtectedRoomWaitState
        {
            internal Room Room;
            internal float DeadlineMinute;
            internal int DurationMinutes;
            internal bool LastProtectedRoom;
            internal bool LastClaimedByOther;
            internal float NextProtectionRecheckRealtime;
            internal bool ShowingWaitingBubble;
        }

        private static readonly Dictionary<Entity, ProtectedRoomWaitState>
            ProtectedRoomWaits =
                new Dictionary<Entity, ProtectedRoomWaitState>();

        // Only one chance roll per already-selected, newly protected room.
        // This is a runtime detour: the native room job remains the target,
        // and no patient procedure or save object is modified.
        private sealed class ProtectedRoomApproachState
        {
            internal Room Room;
            internal Vector2i OriginalDestination;
            internal int OriginalFloor;
            internal Vector2i WaitTile;
            internal Vector2i DoorwayAnchor;
            internal float DoorRouteDistance;
            internal bool HasWaitTile;
            internal float StartedRealtime;
            internal PathfinderJob ValidationJob;
            internal bool RouteValidated;
            internal PathfinderRoute CheckedNativeRoute;
        }

        private static readonly Dictionary<Entity, ProtectedRoomApproachState>
            ProtectedRoomApproaches =
                new Dictionary<Entity, ProtectedRoomApproachState>();

        private static readonly Dictionary<Room, float>
            DeferredProtectedRooms =
                new Dictionary<Room, float>();

        // Per-hospital save state, indexed by stable native entity IDs.
        // Only decisions and post-wait cooldowns persist. Walks, cart
        // transitions and pathfinder jobs always remain runtime-only.
        private const string WaitSaveHeader = "HTC_JANITOR_WAIT_V1";
        private const string WaitSaveScriptName = "GlobalScript";
        private static readonly Dictionary<uint, float>
            NextOccupiedWaitRollMinute = new Dictionary<uint, float>();
        // janitor ID -> (room ID -> current occupying patient/procedure ID).
        // An entry records BOTH accepted and rejected rolls: never reroll
        // the same encounter just because a janitor changed task.
        private static readonly Dictionary<uint, Dictionary<uint, uint>>
            OccupiedWaitDecisions =
                new Dictionary<uint, Dictionary<uint, uint>>();

        // Direct room -> entity ownership. The owner is released only when
        // neither an approach nor a real wait is still active.
        private static readonly Dictionary<Room, Entity>
            OccupiedWaitRoomOwners = new Dictionary<Room, Entity>();

        private static readonly Dictionary<Entity, string>
            LastCartRouteDiagnostics =
                new Dictionary<Entity, string>();

        private static readonly Dictionary<Room, float>
            NextPatientDiagnosticRealtime =
                new Dictionary<Room, float>();

        private sealed class WcCleaningTrace
        {
            internal Room Room;
            internal string Signature;
            internal Vector2i LastPosition;
            internal float LastProgressRealtime;
            internal float LastWarningRealtime;
        }

        private static readonly Dictionary<Entity, WcCleaningTrace>
            WcCleaningTraces =
                new Dictionary<Entity, WcCleaningTrace>();

        private sealed class JanitorStationaryTrace
        {
            internal string Signature;
            internal float SinceRealtime;
            internal float LastLogRealtime;
        }

        private static readonly Dictionary<Entity, JanitorStationaryTrace>
            StationaryJanitorTraces =
                new Dictionary<Entity, JanitorStationaryTrace>();

        private static bool s_missingNativeMethodLogged;
        private static bool s_nativeInvocationErrorLogged;

        internal static void Reset()
        {
            foreach (KeyValuePair<Entity, ProtectedRoomWaitState> entry in
                     ProtectedRoomWaits)
            {
                HideProtectedRoomWaitingBubble(entry.Key, entry.Value);
            }

            ProtectedRoomWaits.Clear();
            foreach (ProtectedRoomApproachState approach in
                     ProtectedRoomApproaches.Values)
            {
                if (approach.ValidationJob != null &&
                    !approach.ValidationJob.IsDone)
                {
                    approach.ValidationJob.Abort();
                }
            }
            ProtectedRoomApproaches.Clear();
            DeferredProtectedRooms.Clear();
            NextOccupiedWaitRollMinute.Clear();
            OccupiedWaitDecisions.Clear();
            OccupiedWaitRoomOwners.Clear();
            LastCartRouteDiagnostics.Clear();
            NextPatientDiagnosticRealtime.Clear();
            WcCleaningTraces.Clear();
            StationaryJanitorTraces.Clear();
        }

        private static void DiscardProtectedRoomApproach(Entity entity)
        {
            if (entity == null)
            {
                return;
            }

            ProtectedRoomApproachState approach;
            if (!ProtectedRoomApproaches.TryGetValue(
                    entity, out approach))
            {
                return;
            }

            if (approach.ValidationJob != null &&
                !approach.ValidationJob.IsDone)
            {
                approach.ValidationJob.Abort();
            }
            ProtectedRoomApproaches.Remove(entity);
            ReleaseOccupiedWaitRoomIfInactive(entity, approach.Room);
        }

        private static void ReleaseOccupiedWaitRoomIfInactive(
            Entity entity, Room room)
        {
            if (entity == null || room == null)
            {
                return;
            }

            Entity owner;
            if (!OccupiedWaitRoomOwners.TryGetValue(room, out owner) ||
                !ReferenceEquals(owner, entity))
            {
                return;
            }

            ProtectedRoomApproachState pending;
            if (ProtectedRoomApproaches.TryGetValue(entity, out pending) &&
                ReferenceEquals(pending.Room, room))
            {
                return;
            }

            ProtectedRoomWaitState waiting;
            if (ProtectedRoomWaits.TryGetValue(entity, out waiting) &&
                ReferenceEquals(waiting.Room, room))
            {
                return;
            }

            OccupiedWaitRoomOwners.Remove(room);
        }

        internal static void PruneProtectedRoomApproach(
            BehaviorJanitor janitor)
        {
            if (janitor == null ||
                janitor.m_entity == null ||
                janitor.m_state == null)
            {
                return;
            }

            ProtectedRoomApproachState approach;
            if (!ProtectedRoomApproaches.TryGetValue(
                    janitor.m_entity, out approach))
            {
                return;
            }

            Room currentRoom = janitor.m_state.m_room == null
                ? null
                : janitor.m_state.m_room.GetEntity();
            BehaviorJanitorState state =
                janitor.m_state.m_janitorState;
            if (ReferenceEquals(currentRoom, approach.Room) &&
                (state == BehaviorJanitorState.WalkingToCartToNextRoom ||
                 state == BehaviorJanitorState.WalkingToNextRoom))
            {
                return;
            }

            DiscardProtectedRoomApproach(janitor.m_entity);
            ClearProtectedRoomWait(janitor);
        }

        internal static void LogJanitorStationaryIfNeeded(
            BehaviorJanitor janitor)
        {
            if (!TrafficControlConfig.PathfindingDebug ||
                janitor == null ||
                janitor.m_entity == null ||
                janitor.m_state == null)
            {
                return;
            }

            Entity entity = janitor.m_entity;
            WalkComponent walk =
                janitor.GetComponent<WalkComponent>();
            if (walk == null || walk.m_state == null)
            {
                StationaryJanitorTraces.Remove(entity);
                return;
            }

            BehaviorJanitorState state =
                janitor.m_state.m_janitorState;
            bool isTravelState =
                state == BehaviorJanitorState.Walking ||
                state == BehaviorJanitorState.WalkingToNextRoom ||
                state == BehaviorJanitorState.WalkingToCartToNextRoom ||
                state == BehaviorJanitorState.WalkingToCart ||
                state == BehaviorJanitorState.GoingToReturnCart ||
                state == BehaviorJanitorState.ReturningCart ||
                state == BehaviorJanitorState.GoingHome ||
                state == BehaviorJanitorState.GoingToWorkplace;
            bool waiting = ProtectedRoomWaits.ContainsKey(entity);
            if (!isTravelState && !waiting)
            {
                StationaryJanitorTraces.Remove(entity);
                return;
            }

            // Detect an unchanged travel/stop state; cleaning a tile or using
            // the WC is intentionally excluded from this watchdog.
            Vector2i tile = walk.GetCurrentTile();
            string signature =
                state + "|" +
                walk.m_state.m_walkState + "|" +
                tile + "|" +
                walk.GetFloorIndex() + "|" +
                walk.m_state.m_destination + "|" +
                walk.m_state.m_destinationFloor + "|" +
                waiting;
            float now = UnityEngine.Time.realtimeSinceStartup;
            JanitorStationaryTrace trace;
            if (!StationaryJanitorTraces.TryGetValue(
                    entity, out trace) ||
                trace.Signature != signature)
            {
                StationaryJanitorTraces[entity] =
                    new JanitorStationaryTrace
                    {
                        Signature = signature,
                        SinceRealtime = now,
                        LastLogRealtime = now - 40f
                    };
                return;
            }

            if (now - trace.SinceRealtime < 20f ||
                now - trace.LastLogRealtime < 60f)
            {
                return;
            }

            trace.LastLogRealtime = now;
            AnimModelComponent animation =
                janitor.GetComponent<AnimModelComponent>();
            AnimModelComponentPersistentData animState =
                animation == null
                    ? null
                    : animation.GetPersistentData() as
                        AnimModelComponentPersistentData;
            SpeechComponent speech =
                janitor.GetComponent<SpeechComponent>();
            Room targetRoom =
                janitor.m_state.m_room == null
                    ? null
                    : janitor.m_state.m_room.GetEntity();
            Room physicalRoom = GetRoomAtSafe(
                walk.GetCurrentTile(),
                walk.GetFloorIndex());
            Entity targetOwner = GetRoomReservationOwner(targetRoom);
            bool deferred =
                targetRoom != null &&
                DeferredProtectedRooms.ContainsKey(targetRoom);
            // Snapshot the reason only in this already throttled diagnostic.
            // Never let the diagnostic itself update deferral state.
            bool clinicallyProtected =
                targetRoom != null &&
                state == BehaviorJanitorState.WalkingToNextRoom &&
                ShouldAvoidWholeRoomFresh(targetRoom);
            string decision =
                waiting ? "HTC_WAIT" :
                deferred ? "TARGET_DEFERRED" :
                targetOwner != null &&
                !ReferenceEquals(targetOwner, entity)
                    ? "FOREIGN_ROOM_OWNER" :
                clinicallyProtected ? "CLINICALLY_PROTECTED" :
                state == BehaviorJanitorState.WalkingToNextRoom
                    ? "VANILLA_ARRIVAL_EXPECTED"
                    : "NATIVE_TRAVEL_STATE";
            Plugin.Log?.LogWarning(
                "[JanitorDebug] JANITOR_STATIONARY" +
                " | janitor=" + CharacterName(entity) +
                " | state=" + state +
                " | walkState=" + walk.m_state.m_walkState +
                " | tile=" + tile +
                " | floor=" + walk.GetFloorIndex() +
                " | target=" + walk.m_state.m_destination +
                " | targetFloor=" +
                    walk.m_state.m_destinationFloor +
                " | roomType=" + RoomTypeId(targetRoom) +
                " | roomBounds=" + RoomBounds(targetRoom) +
                " | physicalRoom=" + RoomTypeId(physicalRoom) +
                " | insideTarget=" +
                    ReferenceEquals(physicalRoom, targetRoom) +
                " | roomOwner=" + CharacterName(targetOwner) +
                " | roomDeferred=" + deferred +
                " | roomProtected=" + clinicallyProtected +
                " | walkBusy=" + walk.IsBusy() +
                " | decision=" + decision +
                " | waitActive=" + waiting +
                " | animation=" +
                    (animState == null
                        ? "<none>"
                        : animState.m_currentAnimationID) +
                " | animationLoops=" +
                    (animState != null &&
                     animState.m_currentAnimationLooping) +
                " | bubble=" +
                    (speech == null
                        ? -1
                        : speech.GetBubbleTextureIndex()) +
                " | seconds=" + (now - trace.SinceRealtime) +
                ".");
        }

        internal static void LogWcCleaningProgress(BehaviorJanitor janitor)
        {
            if (!TrafficControlConfig.BathroomFlowDebug ||
                janitor == null ||
                janitor.m_entity == null ||
                janitor.m_state == null ||
                MapScriptInterface.Instance == null)
            {
                return;
            }

            WalkComponent walk =
                janitor.GetComponent<WalkComponent>();
            if (walk == null || walk.m_state == null)
            {
                return;
            }

            BehaviorJanitorState state =
                janitor.m_state.m_janitorState;
            bool cleaningWorkflow =
                state == BehaviorJanitorState.Cleaning ||
                state == BehaviorJanitorState.Walking ||
                state == BehaviorJanitorState.WalkingToNextRoom;

            WcCleaningTrace trace;
            bool tracked =
                WcCleaningTraces.TryGetValue(
                    janitor.m_entity, out trace);

            // Do not inspect the map for idle / resting / needs / returning
            // janitors unless we must close an existing WC cleaning trace.
            if (!cleaningWorkflow && !tracked)
            {
                return;
            }

            Room physicalRoom =
                MapScriptInterface.Instance.GetRoomAt(
                    walk.GetCurrentTile(), walk.GetFloorIndex());
            Room assignedRoom =
                janitor.m_state.m_room == null
                    ? null
                    : janitor.m_state.m_room.GetEntity();

            // Both vanilla room cleaning and its tile-only fallback matter.
            // This diagnostic does not require a cart to exist.
            bool roomJob =
                assignedRoom != null &&
                ReferenceEquals(physicalRoom, assignedRoom) &&
                ReferenceEquals(
                    GetRoomReservationOwner(assignedRoom),
                    janitor.m_entity);
            bool tileJob =
                janitor.m_state.m_reservedTile !=
                    Vector2i.ZERO_VECTOR;
            bool inWcCleaning =
                cleaningWorkflow &&
                IsBathroomRoom(physicalRoom) &&
                janitor.m_state.m_object == null &&
                (roomJob || tileJob);
            if (!inWcCleaning)
            {
                if (tracked)
                {
                    Plugin.Log?.LogInfo(
                        "[JanitorDebug] WC_CLEANING_EXIT" +
                        " | janitor=" + CharacterName(janitor.m_entity) +
                        " | lastRoom=" + RoomBounds(trace.Room) +
                        " | state=" + state +
                        " | tile=" + walk.GetCurrentTile() +
                        ".");
                    WcCleaningTraces.Remove(janitor.m_entity);
                }
                return;
            }

            Vector2i position = walk.GetCurrentTile();
            Vector2i destination =
                ToTile(walk.m_state.m_destination);
            string signature =
                state + "|" + walk.m_state.m_walkState +
                "|" + destination +
                "|" + walk.m_state.m_destinationFloor +
                "|" + RoomBounds(physicalRoom) +
                "|" + janitor.m_state.m_reservedTile;
            float now = UnityEngine.Time.realtimeSinceStartup;
            bool changed =
                !tracked ||
                !ReferenceEquals(trace.Room, physicalRoom) ||
                trace.Signature != signature;

            if (changed)
            {
                trace = new WcCleaningTrace
                {
                    Room = physicalRoom,
                    Signature = signature,
                    LastPosition = position,
                    LastProgressRealtime = now,
                    LastWarningRealtime = now
                };
                WcCleaningTraces[janitor.m_entity] = trace;
                Plugin.Log?.LogInfo(
                    "[JanitorDebug] WC_CLEANING_STAGE" +
                    " | janitor=" + CharacterName(janitor.m_entity) +
                    " | floor=" + walk.GetFloorIndex() +
                    " | roomBounds=" + RoomBounds(physicalRoom) +
                    " | job=" +
                        (janitor.m_state.m_reservedTile ==
                         Vector2i.ZERO_VECTOR
                            ? "room-or-unreserved"
                            : "reserved-tile") +
                    " | state=" + state +
                    " | walkState=" + walk.m_state.m_walkState +
                    " | tile=" + position +
                    " | destination=" + destination +
                    " | reservedTile=" + janitor.m_state.m_reservedTile +
                    " | stateTime=" + janitor.m_state.m_timeInState +
                    " | cleaningTime=" + janitor.m_state.m_cleaningTime +
                    ".");
                return;
            }

            if (position != trace.LastPosition)
            {
                trace.LastPosition = position;
                trace.LastProgressRealtime = now;
                return;
            }

            // Log a compact watchdog only for genuinely unchanged movement
            // or cleaning, not at every BehaviorJanitor.Update frame.
            if (now - trace.LastProgressRealtime >= 20f &&
                now - trace.LastWarningRealtime >= 20f)
            {
                trace.LastWarningRealtime = now;
                Plugin.Log?.LogWarning(
                    "[JanitorDebug] WC_CLEANING_STALL" +
                    " | janitor=" + CharacterName(janitor.m_entity) +
                    " | floor=" + walk.GetFloorIndex() +
                    " | roomBounds=" + RoomBounds(physicalRoom) +
                    " | state=" + state +
                    " | walkState=" + walk.m_state.m_walkState +
                    " | tile=" + position +
                    " | destination=" + destination +
                    " | reservedTile=" + janitor.m_state.m_reservedTile +
                    " | stateTime=" + janitor.m_state.m_timeInState +
                    " | cleaningTime=" + janitor.m_state.m_cleaningTime +
                    " | unchangedRealSeconds=" +
                        (now - trace.LastProgressRealtime) +
                    ".");
            }
        }

        internal static void LogCartRouteIfChanged(BehaviorJanitor janitor)
        {
            if (!TrafficControlConfig.JanitorCartDebug ||
                janitor == null ||
                janitor.m_entity == null ||
                janitor.m_state == null)
            {
                return;
            }

            TileObject cart =
                janitor.m_state.m_cart == null
                    ? null
                    : janitor.m_state.m_cart.GetEntity();
            if (cart == null || cart.m_state == null)
            {
                LastCartRouteDiagnostics.Remove(janitor.m_entity);
                return;
            }

            WalkComponent walk = janitor.GetComponent<WalkComponent>();
            Room stateRoom =
                janitor.m_state.m_room == null
                    ? null
                    : janitor.m_state.m_room.GetEntity();

            Vector2i destination =
                walk == null || walk.m_state == null
                    ? Vector2i.ZERO_VECTOR
                    : ToTile(walk.m_state.m_destination);
            int destinationFloor =
                walk == null || walk.m_state == null
                    ? -1
                    : walk.m_state.m_destinationFloor;
            string walkState =
                walk == null || walk.m_state == null
                    ? "<none>"
                    : walk.m_state.m_walkState.ToString();

            string signature =
                janitor.m_state.m_janitorState +
                "|" + walkState +
                "|" + destination +
                "|" + destinationFloor +
                "|" + RoomTypeId(stateRoom) +
                "|" + (stateRoom == null ? -1 : stateRoom.GetFloorIndex()) +
                "|" + janitor.m_state.m_reservedTile +
                "|" + cart.m_state.m_attachedToCharacter +
                "|" + cart.m_state.m_moving +
                "|" + cart.m_state.m_position +
                "|" + cart.GetFloorIndex();

            string previous;
            if (LastCartRouteDiagnostics.TryGetValue(
                    janitor.m_entity,
                    out previous) &&
                previous == signature)
            {
                return;
            }

            LastCartRouteDiagnostics[janitor.m_entity] = signature;

            Plugin.Log?.LogInfo(
                "[JanitorDebug] CART_ROUTE" +
                " | janitor=" + CharacterName(janitor.m_entity) +
                " | janitorState=" + janitor.m_state.m_janitorState +
                " | walkState=" + walkState +
                " | currentTile=" +
                    (walk == null
                        ? Vector2i.ZERO_VECTOR
                        : walk.GetCurrentTile()) +
                " | currentFloor=" +
                    (walk == null ? -1 : walk.GetFloorIndex()) +
                " | destination=" + destination +
                " | destinationFloor=" + destinationFloor +
                " | stateRoom=" + RoomTypeId(stateRoom) +
                " | stateRoomFloor=" +
                    (stateRoom == null ? -1 : stateRoom.GetFloorIndex()) +
                " | reservedTile=" + janitor.m_state.m_reservedTile +
                " | cartAttached=" +
                    cart.m_state.m_attachedToCharacter +
                " | cartMoving=" + cart.m_state.m_moving +
                " | cartTile=" + cart.m_state.m_position +
                " | cartFloor=" + cart.GetFloorIndex() +
                ".");
        }

        internal static void LogClinicalCleaningStart(BehaviorJanitor janitor)
        {
            if (!TrafficControlConfig.PathfindingDebug ||
                janitor == null ||
                janitor.m_state == null)
            {
                return;
            }

            WalkComponent walk = janitor.GetComponent<WalkComponent>();
            Room stateRoom = janitor.m_state.m_room == null
                ? null
                : janitor.m_state.m_room.GetEntity();
            Room physicalRoom = walk == null
                ? null
                : MapScriptInterface.Instance.GetRoomAt(
                    walk.GetCurrentTile(),
                    walk.GetFloorIndex());
            Room room = physicalRoom ?? stateRoom;

            if (!CanCheckDirectPatientOccupancy(room))
            {
                return;
            }

            GameDBRoomType roomType =
                room.m_roomPersistentData.m_roomType.Entry;
            Entity patientInside = FindPatientInsideRoom(room);
            Entity procedureOwner = GetCurrentProcedureOwner(room);
            Entity reservedBy = GetRoomReservationOwner(room);

            Plugin.Log?.LogInfo(
                "[JanitorDebug] CLINIC_CLEANING_START" +
                " | janitor=" + CharacterName(janitor.m_entity) +
                " | floor=" + room.GetFloorIndex() +
                " | roomType=" + RoomTypeId(room) +
                " | roomBounds=" + RoomBounds(room) +
                " | access=" + roomType.AccessRights +
                " | patientInside=" + CharacterName(patientInside) +
                " | procedureOwner=" + CharacterName(procedureOwner) +
                " | reservedBy=" +
                    (reservedBy == null
                        ? "<none>"
                        : reservedBy.GetType().Name + ":" +
                          CharacterName(reservedBy)) +
                ".");
        }

        internal static void LogBathroomCleaningStart(BehaviorJanitor janitor)
        {
            if (!TrafficControlConfig.BathroomFlowDebug ||
                janitor == null ||
                janitor.m_state == null)
            {
                return;
            }

            WalkComponent walk = janitor.GetComponent<WalkComponent>();
            Room stateRoom = janitor.m_state.m_room == null
                ? null
                : janitor.m_state.m_room.GetEntity();
            Room physicalRoom = walk == null
                ? null
                : MapScriptInterface.Instance.GetRoomAt(
                    walk.GetCurrentTile(),
                    walk.GetFloorIndex());
            Room room = physicalRoom ?? stateRoom;

            if (!IsBathroomRoom(room))
            {
                return;
            }

            Vector2i currentTile =
                walk == null
                    ? Vector2i.ZERO_VECTOR
                    : walk.GetCurrentTile();
            BathroomCleaningTopology topology =
                BathroomCleaningTopology.Create(room);

            Plugin.Log?.LogInfo("[BathroomDebug] JANITOR_WC_CLEANING_START" +
                " | janitor=" + CharacterName(janitor.m_entity) +
                " | floor=" + room.GetFloorIndex() +
                " | roomType=" + RoomTypeId(room) +
                " | roomBounds=" + RoomBounds(room) +
                " | tile=" + currentTile +
                " | tileCompartment=" +
                    (topology == null ? -1 : topology.DiagnosticGetTileCompartment(currentTile)) +
                " | toiletCount=" +
                    (topology == null ? 0 : topology.DiagnosticToiletCount) +
                " | toiletCompartments=" +
                    (topology == null ? 0 : topology.DiagnosticToiletCompartmentCount) +
                " | occupiedCompartments=" +
                    (topology == null ? 0 : topology.DiagnosticOccupiedCompartmentCount) +
                " | avoidOccupied=" +
                    TrafficControlConfig.AvoidCleaningOccupiedBathrooms +
                " | tileBlocked=" +
                    (topology != null &&
                     topology.IsTileProtected(currentTile)) +
                " | anyOccupiedCompartment=" +
                    (topology != null &&
                     topology.HasOccupiedCompartment));
        }

        internal static bool TryInterruptProtectedCleaningRoom(BehaviorJanitor janitor)
        {
            if (janitor == null ||
                janitor.m_state == null ||
                janitor.m_entity == null ||
                janitor.m_state.m_janitorState != BehaviorJanitorState.Cleaning)
            {
                return false;
            }

            WalkComponent walk = janitor.GetComponent<WalkComponent>();
            Room stateRoom = janitor.m_state.m_room == null
                ? null
                : janitor.m_state.m_room.GetEntity();
            Room physicalRoom = walk == null
                ? null
                : MapScriptInterface.Instance.GetRoomAt(
                    walk.GetCurrentTile(),
                    walk.GetFloorIndex());

            // Vanilla can enter Cleaning immediately after FulfillingNeeds,
            // with no target acquired. That is not an active cleaning job and
            // must be left entirely to the native SelectNextAction cycle.
            bool hasRoomJob =
                stateRoom != null &&
                ReferenceEquals(stateRoom, physicalRoom) &&
                ReferenceEquals(
                    GetRoomReservationOwner(stateRoom),
                    janitor.m_entity);
            bool hasTileJob =
                walk != null &&
                janitor.m_state.m_reservedTile !=
                    Vector2i.ZERO_VECTOR &&
                janitor.m_state.m_reservedTile ==
                    walk.GetCurrentTile();
            bool hasRepairJob = janitor.m_state.m_object != null;
            if ((!hasRoomJob && !hasTileJob) || hasRepairJob)
            {
                return false;
            }

            // Vanilla cleans WalkComponent.GetCurrentTile(). The physical room is
            // authoritative for the target, not a possibly stale m_state.m_room.
            Room room = physicalRoom ?? stateRoom;

            Vector2i currentTile =
                walk == null
                    ? Vector2i.ZERO_VECTOR
                    : walk.GetCurrentTile();
            bool protectedRoom;
            bool blockedBathroomTile;
            CheckCleaningTargetProtection(
                room,
                currentTile,
                null,
                null,
                out protectedRoom,
                out blockedBathroomTile);

            // Vanilla reserves a room for a room-wide assignment, but the
            // indoor-tile fallback reserves only m_reservedTile. Never treat a
            // tile-only job as a stolen room reservation: two janitors can
            // legitimately clean different tiles of the same physical room.
            // Compare room owners only if this is the janitor's room assignment.
            Entity roomOwner = GetRoomReservationOwner(room);
            bool roomAssignment =
                stateRoom != null &&
                ReferenceEquals(stateRoom, room) &&
                janitor.m_state.m_reservedTile ==
                    Vector2i.ZERO_VECTOR;
            bool reservedByAnother =
                TrafficControlConfig.AvoidCleaningActiveProcedureRooms &&
                roomAssignment &&
                roomOwner != null &&
                !ReferenceEquals(roomOwner, janitor.m_entity);

            if (!protectedRoom &&
                !blockedBathroomTile &&
                !reservedByAnother)
            {
                return false;
            }

            if (protectedRoom)
            {
                DeferProtectedRoom(room);
            }

            if (reservedByAnother && TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[JanitorDebug] CLEANING_FOREIGN_ROOM_RESERVATION" +
                    " | janitor=" + CharacterName(janitor.m_entity) +
                    " | other=" + CharacterName(roomOwner) +
                    " | floor=" + room.GetFloorIndex() +
                    " | roomType=" + RoomTypeId(room) +
                    " | roomBounds=" + RoomBounds(room) +
                    ".");
            }

            LogProtectedRoomBlock(janitor, room, "cleaning-recheck");

            if (blockedBathroomTile)
            {
                LogBathroomCleaningBlock(
                    janitor,
                    room,
                    currentTile,
                    "cleaning-recheck");
            }

            ReleaseCleaningTargetAndReselect(janitor, room);
            // Keep this Cleaning tick suppressed even if no new native target
            // was found. Otherwise vanilla may clean the tile we just rejected.
            return true;
        }

        internal static bool TrySkipProtectedCurrentRoomSelection(
            BehaviorJanitor janitor)
        {
            if (janitor == null ||
                janitor.m_state == null ||
                janitor.m_state.m_room == null)
            {
                return false;
            }

            Room room = janitor.m_state.m_room.GetEntity();
            Entity owner = GetRoomReservationOwner(room);
            bool foreignOwner =
                TrafficControlConfig.AvoidCleaningActiveProcedureRooms &&
                owner != null &&
                !ReferenceEquals(owner, janitor.m_entity);
            if (!IsProtectedRoomDeferred(room) &&
                !ShouldAvoidWholeRoomFresh(room) &&
                !foreignOwner)
            {
                return false;
            }

            if (TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[JanitorDebug] CURRENT_ROOM_SELECTION_BLOCK" +
                    " | janitor=" + CharacterName(janitor.m_entity) +
                    " | floor=" +
                        (room == null ? -1 : room.GetFloorIndex()) +
                    " | roomType=" + RoomTypeId(room) +
                    " | roomBounds=" + RoomBounds(room) +
                    " | foreignOwner=" +
                        CharacterName(foreignOwner ? owner : null) +
                    " | patientInside=" +
                        CharacterName(FindPatientInsideRoom(room)) +
                    ".");
            }

            ReleaseRoomReservation(janitor, room);
            ReleaseReservedTile(
                janitor,
                janitor.GetComponent<WalkComponent>());
            janitor.m_state.m_room = null;
            return true;
        }

        internal static bool TryInterruptProtectedTileTravel(
            BehaviorJanitor janitor)
        {
            if (janitor == null ||
                janitor.m_state == null ||
                janitor.m_entity == null ||
                janitor.m_state.m_janitorState != BehaviorJanitorState.Walking ||
                janitor.m_state.m_object != null)
            {
                return false;
            }

            WalkComponent walk = janitor.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return false;
            }

            // TryToSelectIndoorTile() exposes its exact target in m_reservedTile.
            // TryToSelectTileInCurrentRoom() does not, but WalkComponent always stores
            // the destination selected before BehaviorJanitor switches to Walking.
            Vector2i targetTile;
            int targetFloor;

            if (janitor.m_state.m_reservedTile != Vector2i.ZERO_VECTOR)
            {
                targetTile = janitor.m_state.m_reservedTile;
                targetFloor = walk.GetFloorIndex();
            }
            else if (walk.m_state != null)
            {
                targetTile = ToTile(walk.m_state.m_destination);
                targetFloor = walk.m_state.m_destinationFloor;
            }
            else
            {
                return false;
            }

            Room targetRoom = GetRoomAtSafe(targetTile, targetFloor);
            if (targetRoom == null && janitor.m_state.m_room != null)
            {
                targetRoom = janitor.m_state.m_room.GetEntity();
            }

            bool protectedRoom;
            bool blockedBathroomTile;
            CheckCleaningTargetProtection(
                targetRoom,
                targetTile,
                null,
                null,
                out protectedRoom,
                out blockedBathroomTile);

            // Match vanilla's distinction: a tile reservation does not give
            // this job ownership of the enclosing room. Only the actual room
            // selection is subject to the room's reservation owner.
            Room stateRoom = janitor.m_state.m_room == null
                ? null
                : janitor.m_state.m_room.GetEntity();
            Entity roomOwner = GetRoomReservationOwner(targetRoom);
            bool foreignRoomAssignment =
                TrafficControlConfig.AvoidCleaningActiveProcedureRooms &&
                janitor.m_state.m_reservedTile ==
                    Vector2i.ZERO_VECTOR &&
                ReferenceEquals(stateRoom, targetRoom) &&
                roomOwner != null &&
                !ReferenceEquals(roomOwner, janitor.m_entity);

            if (!protectedRoom &&
                !blockedBathroomTile &&
                !foreignRoomAssignment)
            {
                return false;
            }

            // The tile-level selector can otherwise rechoose the same room
            // immediately after this interruption. Only defer the whole
            // room for a room-wide clinical conflict, not for an occupied
            // cubicle in a shared bathroom.
            if (protectedRoom)
            {
                DeferProtectedRoom(targetRoom);
            }

            LogProtectedRoomBlock(janitor, targetRoom, "tile-target-travel");

            if (blockedBathroomTile)
            {
                LogBathroomCleaningBlock(
                    janitor,
                    targetRoom,
                    targetTile,
                    "tile-target-travel");
            }

            StopWalkCleanly(walk, janitor.m_entity);
            ReleaseCleaningTargetAndReselect(janitor, targetRoom);
            // The obsolete native Walking update must never run in the same
            // tick after HTC has abandoned/reselected its cleaning target.
            return true;
        }

        internal static bool TryInterruptProtectedRoomBeforeTravel(
            BehaviorJanitor janitor)
        {
            if (janitor == null ||
                janitor.m_state == null ||
                janitor.m_entity == null ||
                janitor.m_state.m_janitorState !=
                    BehaviorJanitorState.WalkingToCartToNextRoom)
            {
                return false;
            }

            Room room = janitor.m_state.m_room == null
                ? null
                : janitor.m_state.m_room.GetEntity();
            if (room == null)
            {
                ClearProtectedRoomWait(janitor);
                return false;
            }

            // This state is frequently entered while the janitor is still in
            // the previous room (including a WC). Never start a stationary
            // wait here: vanilla has not travelled to the new target yet.
            // Let an existing native trip to fetch the cart finish first.
            WalkComponent walk =
                janitor.GetComponent<WalkComponent>();
            if (walk != null && walk.IsBusy())
            {
                return false;
            }

            // Initial occupied-room intentions must reach the native cart
            // preparation step; their room remains unreserved by the janitor.
            ProtectedRoomApproachState pending;
            if (ProtectedRoomApproaches.TryGetValue(
                    janitor.m_entity, out pending) &&
                ReferenceEquals(pending.Room, room))
            {
                return false;
            }

            // Do not repeat costly clinical rechecks on a deferred stale job.
            if (IsProtectedRoomDeferred(room))
            {
                ReleaseCleaningTargetAndReselect(janitor, room);
                return true;
            }

            Entity reservationOwner =
                GetRoomReservationOwner(room);
            bool claimedByOther =
                reservationOwner != null &&
                !ReferenceEquals(
                    reservationOwner,
                    janitor.m_entity);
            ProtectedRoomWaitState existingWait;
            bool hasWait =
                TryGetProtectedRoomWait(
                    janitor, room, out existingWait);
            bool protectedRoom =
                hasWait
                    ? GetProtectedRoomStateDuringWait(
                        existingWait, room, claimedByOther,
                        HasProtectedRoomWaitExpired(existingWait))
                    : ShouldAvoidWholeRoomFresh(room);

            if (!protectedRoom &&
                !claimedByOther &&
                !IsProtectedRoomDeferred(room))
            {
                if (hasWait)
                {
                    ClearProtectedRoomWait(janitor);
                    LogProtectedRoomWaitEvent(
                        janitor, room, "ROOM_WAIT_RESUME",
                        existingWait, "pretravel-room-free");
                }

                if (reservationOwner == null)
                {
                    room.m_roomPersistentData.m_reservedByCharacter =
                        janitor.m_entity;
                }
                return false;
            }

            // Realism is an optional, once-per-job detour. Allow vanilla
            // to collect its cart before HTC stages the outside waiting tile.
            // Never wait in the former room (including a WC).
            if (protectedRoom &&
                !claimedByOther &&
                TryRollProtectedRoomWait(
                    janitor, room, walk, "before-room-travel"))
            {
                return false;
            }

            ClearProtectedRoomWait(janitor);

            // Far from the target: defer instead of immobilizing the janitor
            // in the previous room. Keep native cart/selector transitions.
            if (TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[JanitorDebug] ROOM_PRETRAVEL_DEFER" +
                    " | janitor=" + CharacterName(janitor.m_entity) +
                    " | floor=" + room.GetFloorIndex() +
                    " | roomType=" + RoomTypeId(room) +
                    " | roomBounds=" + RoomBounds(room) +
                    " | protected=" + protectedRoom +
                    " | reservedBy=" +
                        CharacterName(reservationOwner) +
                    ".");
            }

            DeferProtectedRoom(room);
            ReleaseCleaningTargetAndReselect(janitor, room);
            return true;
        }

        internal static bool TryHandleProtectedRoomTravel(
            BehaviorJanitor janitor)
        {
            if (janitor == null ||
                janitor.m_state == null ||
                janitor.m_entity == null ||
                janitor.m_state.m_janitorState !=
                    BehaviorJanitorState.WalkingToNextRoom)
            {
                return false;
            }

            Room room = janitor.m_state.m_room == null
                ? null
                : janitor.m_state.m_room.GetEntity();
            WalkComponent walk = janitor.GetComponent<WalkComponent>();

            if (room == null || walk == null)
            {
                ClearProtectedRoomWait(janitor);
                return false;
            }

            ProtectedRoomApproachState approach;
            if (janitor.m_entity != null &&
                ProtectedRoomApproaches.TryGetValue(
                    janitor.m_entity, out approach))
            {
                if (ReferenceEquals(approach.Room, room))
                {
                    return HandleProtectedRoomApproach(
                        janitor, room, walk, approach);
                }

                DiscardProtectedRoomApproach(janitor.m_entity);
                ClearProtectedRoomWait(janitor);
            }

            // A native room-wide job always points INTO its assigned room.
            // If the transient HTC approach state was lost on save/load, its
            // saved exterior destination is not a valid vanilla arrival.
            // In particular, never let vanilla park/destroy a cart outside.
            if (walk.m_state != null &&
                CanCheckDirectPatientOccupancy(room) &&
                janitor.m_state.m_object == null &&
                janitor.m_state.m_reservedTile == Vector2i.ZERO_VECTOR &&
                walk.m_state.m_destinationFloor == room.GetFloorIndex() &&
                !room.IsPositionInRoom(
                    ToTile(walk.m_state.m_destination)))
            {
                if (TrafficControlConfig.PathfindingDebug)
                {
                    Plugin.Log?.LogWarning(
                        "[JanitorDebug] ROOM_WAIT_LOAD_RECOVERY" +
                        " | janitor=" + CharacterName(janitor.m_entity) +
                        " | room=" + RoomTypeId(room) +
                        " | destination=" +
                            ToTile(walk.m_state.m_destination) +
                        " | cartAttached=" + IsCartAttached(janitor) +
                        " | waitActive=" +
                            ProtectedRoomWaits.ContainsKey(janitor.m_entity) +
                        ".");
                }

                if (walk.IsBusy())
                {
                    StopWalkCleanly(walk, janitor.m_entity);
                }
                if (IsCartAttached(janitor))
                {
                    ReturnAttachedCartToHome(janitor, walk, room);
                    return true;
                }
                if (ShouldAvoidWholeRoomFresh(room))
                {
                    DeferProtectedRoom(room);
                }
                ReleaseCleaningTargetAndReselect(janitor, room);
                return true;
            }

            if (IsProtectedRoomDeferred(room))
            {
                if (walk.IsBusy())
                {
                    StopWalkCleanly(walk, janitor.m_entity);
                }

                if (!ParkAttachedCartForReselection(janitor, walk))
                {
                    ReturnAttachedCartToHome(janitor, walk, room);
                    return true;
                }

                ReleaseCleaningTargetAndReselect(janitor, room);
                return true;
            }

            bool janitorInside = IsEntityPhysicallyInsideRoom(
                janitor.m_entity,
                room);
            Vector2i targetTile =
                walk.m_state == null
                    ? Vector2i.ZERO_VECTOR
                    : ToTile(walk.m_state.m_destination);
            Room destinationRoom =
                walk.m_state == null
                    ? null
                    : GetRoomAtSafe(
                        targetTile,
                        walk.m_state.m_destinationFloor);
            bool blockedBathroomTile =
                object.ReferenceEquals(destinationRoom, room) &&
                IsBathroomCleaningTileProtected(
                    room,
                    targetTile,
                    null);

            Entity reservationOwner = GetRoomReservationOwner(room);
            bool claimedByOther =
                reservationOwner != null &&
                !ReferenceEquals(reservationOwner, janitor.m_entity);

            ProtectedRoomWaitState existingWait;
            bool hasExistingWait =
                TryGetProtectedRoomWait(
                    janitor,
                    room,
                    out existingWait);

            // Before the janitor is stopped, keep the fresh per-update check so a
            // patient entering at the last moment is still caught before cleaning.
            // Once the janitor is already paused outside the room, throttle only the
            // expensive clinical occupancy re-check to once per real second.
            bool protectedRoom =
                hasExistingWait
                    ? GetProtectedRoomStateDuringWait(
                        existingWait,
                        room,
                        claimedByOther,
                        janitorInside ||
                        HasProtectedRoomWaitExpired(existingWait))
                    : ShouldAvoidWholeRoomFresh(room);

            // A WC compartment is a tile-level conflict, not a reason to reserve
            // an entire shared bathroom and wait outside it.
            if (blockedBathroomTile &&
                !protectedRoom &&
                !claimedByOther)
            {
                LogBathroomCleaningBlock(
                    janitor,
                    room,
                    targetTile,
                    "room-travel");
                ReleaseRoomReservation(janitor, room);
                ClearProtectedRoomWait(janitor);

                if (walk.IsBusy())
                {
                    StopWalkCleanly(walk, janitor.m_entity);
                }

                janitor.m_state.m_room = null;
                ReselectJanitor(janitor);
                return true;
            }

            if (protectedRoom || claimedByOther)
            {
                if (janitorInside)
                {
                    if (protectedRoom)
                    {
                        LogProtectedRoomBlock(
                            janitor,
                            room,
                            "room-travel-inside");
                    }

                    ReleaseRoomReservation(janitor, room);
                    ClearProtectedRoomWait(janitor);
                    janitor.m_state.m_room = null;

                    if (IsCartAttached(janitor))
                    {
                        walk.SetDestination(
                            janitor.m_state.m_cartHomeTile,
                            janitor.m_state.m_cartHomeFloorIndex);
                        janitor.SwitchState(
                            BehaviorJanitorState.ReturningCart);
                        return true;
                    }

                    ReselectJanitor(janitor);
                    return true;
                }

                // A patient can occupy the selected room after pre-travel
                // validation. Roll once for an outside detour, or requeue.
                if (protectedRoom &&
                    !claimedByOther &&
                    !hasExistingWait &&
                    TryRollProtectedRoomWait(
                        janitor, room, walk, "room-travel"))
                {
                    ProtectedRoomApproachState selected =
                        ProtectedRoomApproaches[janitor.m_entity];
                    return HandleProtectedRoomApproach(
                        janitor, room, walk, selected);
                }

                if (walk.IsBusy())
                {
                    StopWalkCleanly(walk, janitor.m_entity);
                }

                DeferProtectedRoom(room);
                ClearProtectedRoomWait(janitor);
                if (!ParkAttachedCartForReselection(
                        janitor, walk))
                {
                    ReturnAttachedCartToHome(janitor, walk, room);
                    return true;
                }

                ReleaseCleaningTargetAndReselect(janitor, room);
                return true;
            }

            ProtectedRoomWaitState completedWait;
            bool resumingRealWait = TryGetProtectedRoomWait(
                janitor,
                room,
                out completedWait);
            if (resumingRealWait)
            {
                ClearProtectedRoomWait(janitor);
                LogProtectedRoomWaitEvent(
                    janitor,
                    room,
                    "ROOM_WAIT_RESUME",
                    completedWait,
                    "room-free");
            }

            // Vanilla owns the normal arrival transition, including stopping
            // the cart and switching to Cleaning. Retargeting every completed
            // trip (even with no HTC wait) can SetDestination to the current
            // tile forever and strand the janitor in WalkingToNextRoom/Idle.
            // Only a janitor explicitly resuming an HTC wait needs a new
            // destination and reservation acquisition.
            if (resumingRealWait && !walk.IsBusy() && !janitorInside)
            {
                reservationOwner = GetRoomReservationOwner(room);
                if (reservationOwner != null &&
                    !ReferenceEquals(
                        reservationOwner,
                        janitor.m_entity))
                {
                    bool started;
                    GetOrStartProtectedRoomWait(
                        janitor,
                        room,
                        "room-reacquire",
                        "reserved-by-other",
                        false,
                        true,
                        out started);
                    return true;
                }

                Vector2i destination;
                if (IsCartAttached(janitor))
                {
                    destination =
                        MapScriptInterface.Instance.FindClosest3x3Area(
                            walk.GetCurrentTile(),
                            room);

                    if (destination == Vector2i.ZERO_VECTOR)
                    {
                        janitor.m_state.m_room = null;
                        walk.SetDestination(
                            janitor.m_state.m_cartHomeTile,
                            janitor.m_state.m_cartHomeFloorIndex);
                        janitor.SwitchState(
                            BehaviorJanitorState.ReturningCart);
                        return true;
                    }

                    janitor.m_state.m_cartAvailable = true;
                }
                else
                {
                    destination =
                        MapScriptInterface.Instance.FindDirtiestTileInARoom(
                            room);

                    if (destination == Vector2i.ZERO_VECTOR)
                    {
                        ReleaseRoomReservation(janitor, room);
                        janitor.m_state.m_room = null;
                        ReselectJanitor(janitor);
                        return true;
                    }

                    janitor.m_state.m_cartAvailable = false;
                }

                room.m_roomPersistentData.m_reservedByCharacter =
                    janitor.m_entity;
                walk.SetDestination(
                    destination,
                    room.GetFloorIndex());
                return true;
            }

            return false;
        }

        private static ProtectedRoomWaitState GetOrStartProtectedRoomWait(
            BehaviorJanitor janitor,
            Room room,
            string stage,
            string reason,
            bool protectedRoom,
            bool claimedByOther,
            out bool started)
        {
            started = false;

            ProtectedRoomWaitState existing;
            if (janitor != null &&
                janitor.m_entity != null &&
                ProtectedRoomWaits.TryGetValue(
                    janitor.m_entity,
                    out existing) &&
                ReferenceEquals(existing.Room, room))
            {
                ShowProtectedRoomWaitingBubble(
                    janitor.m_entity,
                    existing);
                return existing;
            }

            // Changing target rooms also ends the bubble belonging to the old
            // waiting attempt, without changing unrelated vanilla bubbles.
            ClearProtectedRoomWait(janitor);

            int duration =
                GetRandomizedProtectedRoomWaitMinutes(
                    TrafficControlConfig
                        .JanitorOccupiedRoomWaitMinutes);
            float now = GetGameClockMinutes();
            ProtectedRoomWaitState wait =
                new ProtectedRoomWaitState
                {
                    Room = room,
                    DurationMinutes = duration,
                    DeadlineMinute = now + duration,
                    LastProtectedRoom = protectedRoom,
                    LastClaimedByOther = claimedByOther,
                    NextProtectionRecheckRealtime =
                        UnityEngine.Time.realtimeSinceStartup +
                        ProtectedRoomWaitRecheckSeconds
                };

            if (janitor != null && janitor.m_entity != null)
            {
                ProtectedRoomWaits[janitor.m_entity] = wait;
            }

            started = true;
            PlayProtectedRoomWaitingAnimation(
                janitor == null ? null : janitor.m_entity);
            ShowProtectedRoomWaitingBubble(
                janitor == null ? null : janitor.m_entity,
                wait);
            LogProtectedRoomWaitEvent(
                janitor,
                room,
                "ROOM_WAIT_START",
                wait,
                stage + ":" + reason);
            return wait;
        }

        private static bool GetProtectedRoomStateDuringWait(
            ProtectedRoomWaitState wait,
            Room room,
            bool claimedByOther,
            bool force)
        {
            if (wait == null)
            {
                return ShouldAvoidWholeRoomFresh(room);
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            bool reservationReleased =
                wait.LastClaimedByOther &&
                !claimedByOther;
            wait.LastClaimedByOther = claimedByOther;

            if (!force &&
                !reservationReleased &&
                now < wait.NextProtectionRecheckRealtime)
            {
                return wait.LastProtectedRoom;
            }

            bool protectedRoom =
                ShouldAvoidWholeRoomFresh(room);
            wait.LastProtectedRoom = protectedRoom;
            wait.NextProtectionRecheckRealtime =
                now + ProtectedRoomWaitRecheckSeconds;
            return protectedRoom;
        }

        private static void PlayProtectedRoomWaitingAnimation(Entity entity)
        {
            if (entity == null)
            {
                return;
            }

            WalkComponent walk = entity.GetComponent<WalkComponent>();
            if (walk != null &&
                walk.m_state != null &&
                walk.m_state.m_lying)
            {
                return;
            }

            AnimModelComponent animation =
                entity.GetComponent<AnimModelComponent>();
            if (animation != null)
            {
                // Native waiting animation used by multiple vanilla procedures.
                animation.PlayAnimation("stand_wait");
            }
        }

        private static void ShowProtectedRoomWaitingBubble(
            Entity entity,
            ProtectedRoomWaitState wait)
        {
            if (entity == null ||
                wait == null ||
                wait.DurationMinutes <= 0 ||
                wait.ShowingWaitingBubble)
            {
                return;
            }

            SpeechComponent speech =
                entity.GetComponent<SpeechComponent>();
            if (speech == null || speech.IsActive())
            {
                // Never replace speech from native gameplay. If that bubble
                // expires during the wait, retry on the next janitor update.
                return;
            }

            speech.SetBubble(ProtectedRoomWaitBubbleIndex);
            wait.ShowingWaitingBubble = true;
        }

        private static void HideProtectedRoomWaitingBubble(
            Entity entity,
            ProtectedRoomWaitState wait)
        {
            if (entity == null ||
                wait == null ||
                !wait.ShowingWaitingBubble)
            {
                return;
            }

            SpeechComponent speech =
                entity.GetComponent<SpeechComponent>();
            SpeechComponentPersistentData state =
                speech == null
                    ? null
                    : speech.GetPersistentData() as
                        SpeechComponentPersistentData;

            // Only hide the raw atlas icon HTC placed. Do not remove a
            // subsequently displayed vanilla database/asset speech bubble.
            if (state != null &&
                state.m_speechBubble == null &&
                state.m_bubbleTextureIndex ==
                    ProtectedRoomWaitBubbleIndex &&
                state.m_bubbleIconIndex == -1 &&
                state.m_bubbleIconAssetID == null)
            {
                speech.HideBubble();
            }

            wait.ShowingWaitingBubble = false;
        }

        private static bool TryGetProtectedRoomWait(
            BehaviorJanitor janitor,
            Room room,
            out ProtectedRoomWaitState wait)
        {
            wait = null;
            return janitor != null &&
                   janitor.m_entity != null &&
                   ProtectedRoomWaits.TryGetValue(
                       janitor.m_entity,
                       out wait) &&
                   ReferenceEquals(wait.Room, room);
        }

        private static void ClearProtectedRoomWait(
            BehaviorJanitor janitor)
        {
            if (janitor == null || janitor.m_entity == null)
            {
                return;
            }

            ProtectedRoomWaitState existing;
            if (ProtectedRoomWaits.TryGetValue(
                    janitor.m_entity,
                    out existing))
            {
                HideProtectedRoomWaitingBubble(
                    janitor.m_entity,
                    existing);
                ProtectedRoomWaits.Remove(janitor.m_entity);

                // Only a real ROOM_WAIT_START creates this cooldown. It
                // starts after the wait ends, including early completion.
                NextOccupiedWaitRollMinute[janitor.m_entity.GetEntityID()] =
                    GetGameClockMinutes() + TrafficControlConfig
                        .JanitorOccupiedRoomWaitCooldownMinutes;
                ReleaseOccupiedWaitRoomIfInactive(
                    janitor.m_entity, existing.Room);
            }
        }

        private static bool HasProtectedRoomWaitExpired(
            ProtectedRoomWaitState wait)
        {
            if (wait == null)
            {
                return true;
            }

            return wait.DurationMinutes <= 0 ||
                   GetGameClockMinutes() >=
                       wait.DeadlineMinute;
        }

        private static float GetGameClockMinutes()
        {
            if (DayTime.Instance == null)
            {
                return 0f;
            }

            return DayTime.Instance.GetDay() * 1440f +
                   DayTime.Instance.GetDayTimeHours() * 60f;
        }

        private static int GetRandomizedProtectedRoomWaitMinutes(
            int baseMinutes)
        {
            if (baseMinutes <= 0)
            {
                return 0;
            }

            int randomness =
                TrafficControlConfig
                    .JanitorOccupiedRoomWaitRandomnessMinutes;
            if (randomness <= 0)
            {
                return baseMinutes;
            }

            int minimum =
                Math.Max(
                    1,
                    baseMinutes - randomness);
            int maximum =
                Math.Min(
                    240,
                    baseMinutes + randomness);

            if (maximum <= minimum)
            {
                return minimum;
            }

            return UnityEngine.Random.Range(
                minimum,
                maximum + 1);
        }

        private static void DeferProtectedRoom(
            Room room)
        {
            if (room == null)
            {
                return;
            }

            int duration =
                GetRandomizedProtectedRoomWaitMinutes(
                    TrafficControlConfig
                        .JanitorOccupiedRoomWaitMinutes);

            // Even when stationary waiting is disabled, one in-game minute of
            // deferral prevents the same room from being selected again in the
            // exact same decision loop.
            if (duration <= 0)
            {
                duration = 1;
            }

            // Do not renew an existing deferral on every interrupted
            // selection. It can outlive the minimum time while protection
            // remains, but it must not form an ever-extending feedback loop.
            if (!DeferredProtectedRooms.ContainsKey(room))
            {
                DeferredProtectedRooms[room] =
                    GetGameClockMinutes() + duration;
            }
        }

        // The native room selector immediately reserves the room. A patient-
        // occupied room must be a wait intention, never a normal room claim.
        // For SelectNextAction(), this is invoked after vanilla found no free
        // room in the janitor's department but before the floor-wide indoor-tile
        // fallback can send the janitor into another department. Traverse room
        // candidates once, keeping assigned work first.
        internal static bool TryOfferOccupiedClinicalRoomWait(
            BehaviorJanitor janitor)
        {
            if (!TrafficControlConfig.AvoidCleaningActiveProcedureRooms ||
                TrafficControlConfig.JanitorOccupiedRoomWaitChance <= 0 ||
                TrafficControlConfig.JanitorOccupiedRoomWaitMinutes <= 0 ||
                janitor == null || janitor.m_entity == null ||
                janitor.m_state == null || janitor.m_state.m_room != null ||
                janitor.m_state.m_object != null || Hospital.Instance == null)
            {
                return false;
            }

            WalkComponent walk = janitor.GetComponent<WalkComponent>();
            Department department = janitor.GetDepartment();
            if (walk == null || walk.m_state == null ||
                department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_rooms == null ||
                janitor.m_state.m_assignedRooms == null)
            {
                return false;
            }

            float nextAllowed;
            if (NextOccupiedWaitRollMinute.TryGetValue(
                    janitor.m_entity.GetEntityID(), out nextAllowed) &&
                GetGameClockMinutes() < nextAllowed)
            {
                return false;
            }

            Room chosen = null;
            Vector2i chosenTile = Vector2i.ZERO_VECTOR;
            float chosenDirt = 0f;
            bool chosenAssigned = false;
            Entity chosenOccupant = null;

            foreach (EntityIDPointer<Room> pointer in
                     department.m_departmentPersistentData.m_rooms)
            {
                Room room = pointer == null ? null : pointer.GetEntity();
                if (room == null || room.m_roomPersistentData == null ||
                    room.GetFloorIndex() != walk.GetFloorIndex() ||
                    !CanCheckDirectPatientOccupancy(room) ||
                    IsProtectedRoomDeferred(room) ||
                    IsRoomAlreadyAwaited(room))
                {
                    continue;
                }

                // Native reservations are cheap and checked before dirt and
                // occupancy. A ProcedureScript may reserve the patient's
                // room; an exterior wait must never steal that reservation.
                Entity owner = GetRoomReservationOwner(room);
                if (owner != null && !(owner is ProcedureScript))
                {
                    continue;
                }

                bool assigned =
                    janitor.m_state.m_assignedRooms.Contains(room);
                if (chosenAssigned && !assigned)
                {
                    continue;
                }

                // The native tile eligibility remains the source of truth;
                // avoid checking any occupants for clean rooms.
                Vector2i tile =
                    MapScriptInterface.Instance.FindDirtiestTileInARoom(
                        room, false, 10);
                if (tile == Vector2i.ZERO_VECTOR)
                {
                    continue;
                }

                // Spatial room-local check, no enumeration of all patients.
                Entity occupant = FindPatientInsideRoom(room);
                if (occupant == null ||
                    HasWaitDecisionForEncounter(
                        janitor.m_entity, room, occupant))
                {
                    continue;
                }

                Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];
                float dirt = floor.m_mapPersistentData.m_tiles[
                    tile.m_x, tile.m_y].m_dirtLevel;
                if (chosen == null ||
                    (assigned && !chosenAssigned) ||
                    (assigned == chosenAssigned && dirt > chosenDirt))
                {
                    chosen = room;
                    chosenTile = tile;
                    chosenDirt = dirt;
                    chosenAssigned = assigned;
                    chosenOccupant = occupant;
                }
            }

            if (chosen == null ||
                !TryRollProtectedRoomWait(
                    janitor, chosen, walk,
                    "before-floor-wide-indoor-fallback",
                    chosenOccupant))
            {
                return false;
            }

            janitor.m_state.m_room = chosen;
            janitor.m_state.m_cleaningTime =
                (MapScriptInterface.Instance.GetDirtType(
                     chosenTile, chosen.GetFloorIndex()) == DirtType.BLOOD
                     ? 10f : 1f) *
                (janitor.m_state.m_cartAvailable ? 1f : 2f);

            // Match BehaviorJanitor.TryToSelectTileInARoom(): the native
            // WalkingToCartToNextRoom state assumes that a distant cart
            // has already been approached. PickUpObject() itself does not
            // enforce physical proximity.
            EmployeeComponent employee =
                janitor.GetComponent<EmployeeComponent>();
            if (employee != null && employee.m_state != null &&
                employee.m_state.m_workDesk == null)
            {
                employee.FindWorkplace();
            }

            if (janitor.m_state.m_cart != null &&
                !janitor.m_state.m_cart.CheckEntity())
            {
                janitor.m_state.m_cart = null;
            }

            TileObject cart = janitor.m_state.m_cart == null
                ? null : janitor.m_state.m_cart.GetEntity();
            if (cart != null &&
                (cart.m_state.m_position - walk.GetCurrentTile())
                    .LengthSquared() > 1)
            {
                walk.SetDestination(
                    cart.GetDefaultUsePosition(),
                    cart.GetFloorIndex());
            }

            janitor.SwitchState(
                BehaviorJanitorState.WalkingToCartToNextRoom);

            if (TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[JanitorDebug] ROOM_WAIT_INTENT" +
                    " | janitor=" + CharacterName(janitor.m_entity) +
                    " | roomType=" + RoomTypeId(chosen) +
                    " | floor=" + chosen.GetFloorIndex() +
                    " | roomBounds=" + RoomBounds(chosen) +
                    " | dirtTile=" + chosenTile +
                    " | assigned=" + chosenAssigned +
                    " | nativeRoomOwner=" +
                        CharacterName(GetRoomReservationOwner(chosen)) +
                    " | stage=after-vanilla-free-work.");
            }

            return true;
        }

        // Persistence reuses vanilla's serialized GlobalScript marker (as
        // HPO does), but the marker itself is uniquely named for HTC. Never
        // persist an in-flight PathfinderJob, an attached-cart maneuver, or
        // an exterior wait animation; those must be recovered safely from
        // the saved vanilla janitor state on load.
        internal static void AppendWaitStateToSave(GameSave save)
        {
            if (save == null ||
                (NextOccupiedWaitRollMinute.Count == 0 &&
                 OccupiedWaitDecisions.Count == 0))
            {
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(WaitSaveHeader).Append('\n');
            foreach (KeyValuePair<uint, float> entry in
                     NextOccupiedWaitRollMinute)
            {
                if (entry.Value <= GetGameClockMinutes())
                {
                    continue;
                }
                builder.Append("C|").Append(entry.Key).Append('|')
                    .Append(entry.Value.ToString(
                        "R", CultureInfo.InvariantCulture)).Append('\n');
            }
            foreach (KeyValuePair<uint, Dictionary<uint, uint>> owner in
                     OccupiedWaitDecisions)
            {
                foreach (KeyValuePair<uint, uint> decision in owner.Value)
                {
                    builder.Append("D|").Append(owner.Key).Append('|')
                        .Append(decision.Key).Append('|')
                        .Append(decision.Value).Append('\n');
                }
            }

            GlobalScriptPersistentData data =
                new GlobalScriptPersistentData();
            data.m_scriptName = WaitSaveScriptName;
            data.m_state = builder.ToString();
            if (save.m_entityListEvents == null)
            {
                save.m_entityListEvents = new EntityListSave();
            }
            save.m_entityListEvents.m_entities.Add(new EntitySave(data));
        }

        internal static bool TryRestoreWaitStateMarker(EntitySave save)
        {
            GlobalScriptPersistentData data = save == null
                ? null : save.m_persistentData as GlobalScriptPersistentData;
            if (data == null ||
                data.m_scriptName != WaitSaveScriptName ||
                string.IsNullOrEmpty(data.m_state) ||
                !data.m_state.StartsWith(
                    WaitSaveHeader + "\n", StringComparison.Ordinal))
            {
                return false;
            }

            // Parse into ID-only state; character and room entities may
            // not have been materialized yet when the marker is loaded.
            // No native reservation or path is changed by this operation.
            string[] lines = data.m_state.Split(
                new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('|');
                uint janitorId;
                if (parts.Length < 3 ||
                    !uint.TryParse(parts[1], out janitorId))
                {
                    continue;
                }

                if (parts[0] == "C" && parts.Length == 3)
                {
                    float until;
                    if (float.TryParse(
                            parts[2], NumberStyles.Float,
                            CultureInfo.InvariantCulture, out until) &&
                        !float.IsNaN(until) && !float.IsInfinity(until) &&
                        until >= 0f)
                    {
                        NextOccupiedWaitRollMinute[janitorId] = until;
                    }
                }
                else if (parts[0] == "D" && parts.Length == 4)
                {
                    uint roomId;
                    uint occupantId;
                    if (!uint.TryParse(parts[2], out roomId) ||
                        !uint.TryParse(parts[3], out occupantId))
                    {
                        continue;
                    }
                    Dictionary<uint, uint> choices;
                    if (!OccupiedWaitDecisions.TryGetValue(
                            janitorId, out choices))
                    {
                        choices = new Dictionary<uint, uint>();
                        OccupiedWaitDecisions[janitorId] = choices;
                    }
                    choices[roomId] = occupantId;
                }
            }

            return true;
        }

        private static bool IsRoomAlreadyAwaited(Room room)
        {
            Entity owner;
            if (room == null ||
                !OccupiedWaitRoomOwners.TryGetValue(room, out owner))
            {
                return false;
            }

            ProtectedRoomApproachState approach;
            if (ProtectedRoomApproaches.TryGetValue(owner, out approach) &&
                ReferenceEquals(approach.Room, room))
            {
                return true;
            }

            ProtectedRoomWaitState wait;
            if (ProtectedRoomWaits.TryGetValue(owner, out wait) &&
                ReferenceEquals(wait.Room, room))
            {
                return true;
            }

            // Safety against stale runtime entries after an unexpected
            // state transition or an entity disappearing from the hospital.
            OccupiedWaitRoomOwners.Remove(room);
            return false;
        }

        // Immediately turn a newly prepared native interior route into the
        // validated exterior approach, before its first movement update.
        internal static void StagePendingOccupiedWaitImmediately(
            BehaviorJanitor janitor)
        {
            if (janitor == null || janitor.m_entity == null ||
                janitor.m_state == null ||
                janitor.m_state.m_janitorState !=
                    BehaviorJanitorState.WalkingToNextRoom ||
                janitor.m_state.m_room == null)
            {
                return;
            }

            ProtectedRoomApproachState approach;
            if (ProtectedRoomApproaches.TryGetValue(
                    janitor.m_entity, out approach) &&
                ReferenceEquals(
                    approach.Room, janitor.m_state.m_room.GetEntity()))
            {
                TryHandleProtectedRoomTravel(janitor);
            }
        }

        private static bool HasWaitDecisionForEncounter(
            Entity janitor, Room room, Entity occupant)
        {
            if (janitor == null || room == null || occupant == null)
            {
                return false;
            }

            Dictionary<uint, uint> decisions;
            uint previousOccupantId;
            return OccupiedWaitDecisions.TryGetValue(
                       janitor.GetEntityID(), out decisions) &&
                   decisions.TryGetValue(
                       room.GetEntityID(), out previousOccupantId) &&
                   previousOccupantId == occupant.GetEntityID();
        }

        private static bool TryRollProtectedRoomWait(
            BehaviorJanitor janitor,
            Room room,
            WalkComponent walk,
            string stage)
        {
            return TryRollProtectedRoomWait(
                janitor, room, walk, stage, null);
        }

        private static bool TryRollProtectedRoomWait(
            BehaviorJanitor janitor,
            Room room,
            WalkComponent walk,
            string stage,
            Entity knownOccupant)
        {
            if (janitor == null ||
                janitor.m_entity == null ||
                janitor.m_state == null ||
                room == null ||
                !CanCheckDirectPatientOccupancy(room) ||
                walk == null ||
                walk.m_state == null ||
                walk.GetFloorIndex() != room.GetFloorIndex() ||
                TrafficControlConfig.JanitorOccupiedRoomWaitMinutes <= 0 ||
                TrafficControlConfig.JanitorOccupiedRoomWaitChance <= 0 ||
                IsProtectedRoomDeferred(room) ||
                ProtectedRoomApproaches.ContainsKey(janitor.m_entity) ||
                IsRoomAlreadyAwaited(room))
            {
                return false;
            }

            uint janitorId = janitor.m_entity.GetEntityID();
            float nextAllowed;
            if (NextOccupiedWaitRollMinute.TryGetValue(
                    janitorId, out nextAllowed) &&
                GetGameClockMinutes() < nextAllowed)
            {
                return false;
            }

            // The occupant, not the global hospital patient list, defines
            // the encounter. A procedure owner is a safe fallback when
            // protection starts before the patient physically arrives.
            Entity occupant = knownOccupant;
            string occupantSource = "room-patient-scan";
            if (occupant == null)
            {
                occupant = FindPatientInsideRoom(room);
            }
            if (occupant == null)
            {
                occupant = GetCurrentProcedureOwner(room);
                occupantSource = "current-procedure-owner";
            }
            if (occupant == null)
            {
                Entity owner = GetRoomReservationOwner(room);
                if (owner is ProcedureScript)
                {
                    occupant = owner;
                    occupantSource = "room-reserved-procedure";
                }
            }
            if (occupant == null)
            {
                // Cannot define a stable occupied episode, so no random
                // wait should be offered here.
                return false;
            }

            uint roomId = room.GetEntityID();
            uint occupantId = occupant.GetEntityID();
            Dictionary<uint, uint> decisions;
            if (!OccupiedWaitDecisions.TryGetValue(
                    janitorId, out decisions))
            {
                decisions = new Dictionary<uint, uint>();
                OccupiedWaitDecisions[janitorId] = decisions;
            }

            if (HasWaitDecisionForEncounter(
                    janitor.m_entity, room, occupant))
            {
                return false;
            }

            // Store the result of the *attempt*, not only a successful
            // roll. A refusal cannot be rerolled one minute later.
            decisions[roomId] = occupantId;
            int roll = UnityEngine.Random.Range(0, 100);
            bool accepted =
                roll < TrafficControlConfig.JanitorOccupiedRoomWaitChance;
            if (TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[JanitorDebug] ROOM_WAIT_CHANCE" +
                    " | janitor=" + CharacterName(janitor.m_entity) +
                    " | room=" + RoomTypeId(room) +
                    " | bounds=" + RoomBounds(room) +
                    " | occupant=" + CharacterName(occupant) +
                    " | occupantId=" + occupantId +
                    " | occupantType=" + occupant.GetType().Name +
                    " | occupantSource=" + occupantSource +
                    " | roll=" + roll +
                    " | chance=" +
                        TrafficControlConfig.JanitorOccupiedRoomWaitChance +
                    " | accepted=" + accepted +
                    " | stage=" + stage +
                    ".");
            }

            if (!accepted)
            {
                return false;
            }
            OccupiedWaitRoomOwners[room] = janitor.m_entity;
            ProtectedRoomApproaches[janitor.m_entity] =
                new ProtectedRoomApproachState
                {
                    Room = room,
                    OriginalDestination =
                        ToTile(walk.m_state.m_destination),
                    OriginalFloor =
                        walk.m_state.m_destinationFloor,
                    StartedRealtime =
                        UnityEngine.Time.realtimeSinceStartup
                };

            // A patient's procedure always has priority. The janitor owns
            // only its job intent, never an occupied clinical room.
            ReleaseRoomReservation(janitor, room);
            return true;
        }

        private static bool IsValidProtectedRoomWaitTile(
            Entity janitorEntity,
            Floor floor,
            Room targetRoom,
            Vector2i candidate)
        {
            if (janitorEntity == null ||
                floor == null ||
                targetRoom == null ||
                candidate.m_x < 0 ||
                candidate.m_y < 0 ||
                candidate.m_x >= floor.Size.m_x ||
                candidate.m_y >= floor.Size.m_y ||
                targetRoom.IsPositionInRoom(candidate) ||
                floor.IsAnyObjectAt(candidate))
            {
                return false;
            }

            GridMap grid = GridMap.GetInstance();
            if (grid == null ||
                !grid.IsInGrid(candidate, floor.m_floorIndex))
            {
                return false;
            }

            Room publicRoom = floor.GetRoomTileSafe(
                candidate.m_x, candidate.m_y);
            string id = RoomTypeId(publicRoom);
            if (id != "ROOM_TYPE_CORRIDOR" &&
                id != "ROOM_TYPE_WAITING" &&
                id != "ROOM_TYPE_RECEPTION")
            {
                return false;
            }

            var user =
                floor.m_mapPersistentData.m_tiles[
                    candidate.m_x, candidate.m_y].m_user;
            return user == null ||
                user.GetEntity() == null ||
                ReferenceEquals(user.GetEntity(), janitorEntity);
        }

        private static bool TryFindProtectedRoomWaitTile(
            BehaviorJanitor janitor,
            Room room,
            WalkComponent walk,
            out Vector2i waitTile,
            out Vector2i doorwayAnchor,
            out float doorRouteDistance)
        {
            waitTile = Vector2i.ZERO_VECTOR;
            doorwayAnchor = Vector2i.ZERO_VECTOR;
            doorRouteDistance = float.MaxValue;
            if (room == null ||
                room.m_roomPersistentData == null ||
                walk == null ||
                janitor == null ||
                janitor.m_entity == null ||
                Hospital.Instance == null ||
                MapScriptInterface.Instance == null ||
                GridMap.GetInstance() == null ||
                walk.GetFloorIndex() != room.GetFloorIndex())
            {
                return false;
            }

            int floorIndex = room.GetFloorIndex();
            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null)
            {
                return false;
            }

            Vector2i bottom =
                room.m_roomPersistentData.m_positionBottom;
            Vector2i top =
                room.m_roomPersistentData.m_positionTop;
            Vector2i origin = walk.GetCurrentTile();
            GridMap gridMap = GridMap.GetInstance();
            AccessRights janitorAccessRights = janitor.GetAccessRights();

            // First find the closest reachable public tile around the room.
            // As in HPO this is the route-derived doorway anchor, not the
            // final waiting position.
            float bestAnchorDistance = float.MaxValue;
            int anchorMinX = Math.Max(
                0, bottom.m_x - ProtectedRoomWaitDoorAnchorRadius);
            int anchorMaxX = Math.Min(
                floor.Size.m_x - 1,
                top.m_x + ProtectedRoomWaitDoorAnchorRadius);
            int anchorMinY = Math.Max(
                0, bottom.m_y - ProtectedRoomWaitDoorAnchorRadius);
            int anchorMaxY = Math.Min(
                floor.Size.m_y - 1,
                top.m_y + ProtectedRoomWaitDoorAnchorRadius);

            for (int x = anchorMinX; x <= anchorMaxX; x++)
            {
                for (int y = anchorMinY; y <= anchorMaxY; y++)
                {
                    if (bestAnchorDistance < float.MaxValue)
                    {
                        float dx = x - origin.m_x;
                        float dy = y - origin.m_y;
                        if (dx * dx + dy * dy >=
                            bestAnchorDistance * bestAnchorDistance)
                        {
                            continue;
                        }
                    }

                    Vector2i candidate = new Vector2i(x, y);
                    if (!IsValidProtectedRoomWaitTile(
                            janitor.m_entity, floor, room, candidate))
                    {
                        continue;
                    }

                    float distance = gridMap.GetDistance(
                        floorIndex,
                        origin,
                        floorIndex,
                        candidate,
                        janitorAccessRights);
                    if (float.IsNaN(distance) ||
                        distance < 0f ||
                        distance >= bestAnchorDistance)
                    {
                        continue;
                    }

                    bestAnchorDistance = distance;
                    doorwayAnchor = candidate;
                }
            }

            if (bestAnchorDistance == float.MaxValue)
            {
                return false;
            }

            // Then stage away from that anchor. Unlike HPO's transport wait,
            // HTC has no gameplay obligation to wait: if every valid public
            // tile is still within the near-door zone, abort the optional
            // wait instead of parking on the doorway as a fallback.
            float bestScore = float.MaxValue;
            float bestDoorDistance = float.MaxValue;
            float bestFromJanitor = float.MaxValue;
            int minX = Math.Max(
                0, doorwayAnchor.m_x - ProtectedRoomWaitPublicRadius);
            int maxX = Math.Min(
                floor.Size.m_x - 1,
                doorwayAnchor.m_x + ProtectedRoomWaitPublicRadius);
            int minY = Math.Max(
                0, doorwayAnchor.m_y - ProtectedRoomWaitPublicRadius);
            int maxY = Math.Min(
                floor.Size.m_y - 1,
                doorwayAnchor.m_y + ProtectedRoomWaitPublicRadius);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Vector2i candidate = new Vector2i(x, y);
                    if (!IsValidProtectedRoomWaitTile(
                            janitor.m_entity, floor, room, candidate))
                    {
                        continue;
                    }

                    float dx = x - doorwayAnchor.m_x;
                    float dy = y - doorwayAnchor.m_y;
                    if (dx * dx + dy * dy >
                        ProtectedRoomWaitMaximumDoorDistance *
                        ProtectedRoomWaitMaximumDoorDistance)
                    {
                        continue;
                    }

                    float fromDoor = gridMap.GetDistance(
                        floorIndex,
                        doorwayAnchor,
                        floorIndex,
                        candidate,
                        janitorAccessRights);
                    if (float.IsNaN(fromDoor) ||
                        fromDoor < ProtectedRoomWaitMinimumDoorDistance ||
                        fromDoor > ProtectedRoomWaitMaximumDoorDistance)
                    {
                        continue;
                    }

                    float score = Math.Abs(
                        fromDoor - ProtectedRoomWaitIdealDoorDistance);
                    if (score > bestScore ||
                        (score == bestScore &&
                         fromDoor > bestDoorDistance))
                    {
                        continue;
                    }

                    float fromJanitor = gridMap.GetDistance(
                        floorIndex,
                        origin,
                        floorIndex,
                        candidate,
                        janitorAccessRights);
                    if (float.IsNaN(fromJanitor) ||
                        fromJanitor < 0f ||
                        fromJanitor >= float.MaxValue)
                    {
                        continue;
                    }

                    if (score == bestScore &&
                        fromDoor == bestDoorDistance &&
                        fromJanitor >= bestFromJanitor)
                    {
                        continue;
                    }

                    bestScore = score;
                    bestDoorDistance = fromDoor;
                    bestFromJanitor = fromJanitor;
                    waitTile = candidate;
                }
            }

            if (bestScore == float.MaxValue)
            {
                return false;
            }

            doorRouteDistance = bestDoorDistance;
            return true;
        }

        private static PathfinderJob CreateProtectedRoomRouteJob(
            BehaviorJanitor janitor,
            WalkComponent walk,
            Vector2i destination)
        {
            if (janitor == null || walk == null || walk.Floor == null ||
                SettingsManager.Instance == null)
            {
                return null;
            }

            PathfinderFlags flags =
                SettingsManager.Instance.m_gameSettings.m_8DirMovement
                    ? PathfinderFlags.DIAGONALS
                    : (PathfinderFlags)0;
            return new PathfinderJob(
                walk.GetCurrentTile(),
                destination,
                walk.Floor,
                9216,
                flags,
                (int)janitor.GetAccessRights(),
                (int)janitor.GetDefaultAccessRights(),
                janitor.GetLookAheadDistance());
        }

        private static bool IsProtectedRoomCartFootprintSafe(
            BehaviorJanitor janitor,
            Room room,
            Floor floor,
            Vector2f janitorPosition,
            Direction direction)
        {
            TileObject cart = janitor == null ||
                janitor.m_state == null ||
                janitor.m_state.m_cart == null
                    ? null
                    : janitor.m_state.m_cart.GetEntity();
            if (cart == null || cart.m_state == null ||
                !cart.m_state.m_attachedToCharacter ||
                janitor.m_entity == null || room == null || floor == null)
            {
                return false;
            }

            Vector2f offset = TileObject.TransformOffsetForDirection(
                cart.GetCharacterAttachmentOffset(), direction);
            Vector2i cartTile = janitorPosition.add(offset).ToVector2i();
            if (cartTile.m_x < 0 || cartTile.m_y < 0 ||
                cartTile.m_x >= floor.Size.m_x ||
                cartTile.m_y >= floor.Size.m_y ||
                room.IsPositionInRoom(cartTile) ||
                floor.IsAnyBlockingObjectAt(cartTile))
            {
                return false;
            }

            EntityIDPointer<Entity> tileUser =
                floor.m_mapPersistentData.m_tiles[
                    cartTile.m_x, cartTile.m_y].m_user;
            Entity user = tileUser == null
                ? null
                : tileUser.GetEntity();
            return user == null ||
                ReferenceEquals(user, janitor.m_entity);
        }

        private static bool IsCurrentProtectedRoomCartPositionSafe(
            BehaviorJanitor janitor,
            Room room,
            WalkComponent walk)
        {
            if (!IsCartAttached(janitor))
            {
                return true;
            }

            AnimModelComponent animation = janitor == null
                ? null
                : janitor.GetComponent<AnimModelComponent>();
            if (walk == null || walk.m_state == null ||
                animation == null || animation.m_state == null)
            {
                return false;
            }

            return IsProtectedRoomCartFootprintSafe(
                janitor,
                room,
                walk.Floor,
                walk.m_state.m_currentPosition,
                animation.m_state.m_direction);
        }

        private static bool IsProtectedRoomApproachRouteSafe(
            BehaviorJanitor janitor,
            Room room,
            Floor floor,
            PathfinderRoute route)
        {
            if (janitor == null || room == null || floor == null ||
                route == null || route.Nodes == null ||
                route.Nodes.Count < 2)
            {
                return false;
            }

            TileObject cart =
                janitor.m_state == null ||
                janitor.m_state.m_cart == null
                    ? null
                    : janitor.m_state.m_cart.GetEntity();
            bool cartAttached = cart != null && cart.m_state != null &&
                cart.m_state.m_attachedToCharacter;
            // Validate the result on the main thread, after IsDone.
            // Never access Unity scene objects in the worker job.
            for (int i = 0; i < route.Nodes.Count; i++)
            {
                Vector2i tile = route.Nodes[i].Position;
                if (tile.m_x < 0 || tile.m_y < 0 ||
                    tile.m_x >= floor.Size.m_x ||
                    tile.m_y >= floor.Size.m_y ||
                    room.IsPositionInRoom(tile))
                {
                    return false;
                }

                if (!cartAttached)
                {
                    continue;
                }

                int directionNode = Math.Min(
                    route.Nodes.Count - 2, i);
                Direction direction =
                    route.Nodes[directionNode].Direction
                        .GetOppositeDirection();
                if (!IsProtectedRoomCartFootprintSafe(
                        janitor, room, floor,
                        new Vector2f(tile), direction))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool AbortProtectedRoomApproach(
            BehaviorJanitor janitor,
            Room room,
            WalkComponent walk,
            string reason)
        {
            if (janitor == null || janitor.m_entity == null)
            {
                return true;
            }

            DiscardProtectedRoomApproach(janitor.m_entity);
            ClearProtectedRoomWait(janitor);
            DeferProtectedRoom(room);
            if (walk != null && walk.IsBusy())
            {
                StopWalkCleanly(walk, janitor.m_entity);
            }

            if (TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[JanitorDebug] ROOM_WAIT_APPROACH_ABORT" +
                    " | janitor=" + CharacterName(janitor.m_entity) +
                    " | room=" + RoomTypeId(room) +
                    " | reason=" + reason +
                    ".");
            }

            if (!ParkAttachedCartForReselection(janitor, walk))
            {
                ReturnAttachedCartToHome(janitor, walk, room);
                return true;
            }

            ReleaseCleaningTargetAndReselect(janitor, room);
            return true;
        }

        private static bool HandleProtectedRoomApproach(
            BehaviorJanitor janitor,
            Room room,
            WalkComponent walk,
            ProtectedRoomApproachState approach)
        {
            if (walk.m_state == null ||
                walk.GetFloorIndex() != room.GetFloorIndex())
            {
                return AbortProtectedRoomApproach(
                    janitor, room, walk, "floor-changed");
            }

            // The exterior staging route must never turn into cleaning
            // inside a protected room. A native path may change after a
            // reservation or OneWay update; abandon immediately on entry.
            if (room.IsPositionInRoom(walk.GetCurrentTile()) &&
                (ShouldAvoidWholeRoomFresh(room) ||
                 (GetRoomReservationOwner(room) != null &&
                  !ReferenceEquals(
                      GetRoomReservationOwner(room), janitor.m_entity))))
            {
                DiscardProtectedRoomApproach(janitor.m_entity);
                ClearProtectedRoomWait(janitor);
                if (TrafficControlConfig.PathfindingDebug)
                {
                    Plugin.Log?.LogWarning(
                        "[JanitorDebug] ROOM_WAIT_APPROACH_ABORT" +
                        " | janitor=" + CharacterName(janitor.m_entity) +
                        " | room=" + RoomTypeId(room) +
                        " | reason=entered-protected-room" +
                        ".");
                }

                if (walk.IsBusy())
                {
                    StopWalkCleanly(walk, janitor.m_entity);
                }

                if (IsCartAttached(janitor))
                {
                    ReturnAttachedCartToHome(janitor, walk, room);
                    return true;
                }

                DeferProtectedRoom(room);
                ReleaseCleaningTargetAndReselect(janitor, room);
                return true;
            }

            // Pretravel chooses the roll before vanilla picks its final room
            // destination. Snapshot that native destination on first travel
            // update, even if the patient leaves before outside staging.
            if (!approach.HasWaitTile)
            {
                approach.OriginalDestination =
                    ToTile(walk.m_state.m_destination);
                approach.OriginalFloor =
                    walk.m_state.m_destinationFloor;
            }

            Entity owner = GetRoomReservationOwner(room);
            bool claimedByOther =
                owner != null &&
                !ReferenceEquals(owner, janitor.m_entity);
            ProtectedRoomWaitState wait;
            bool hasWait =
                TryGetProtectedRoomWait(janitor, room, out wait);
            bool protectedRoom =
                hasWait
                    ? GetProtectedRoomStateDuringWait(
                        wait, room, claimedByOther,
                        HasProtectedRoomWaitExpired(wait))
                    : ShouldAvoidWholeRoomFresh(room);

            if (!protectedRoom && !claimedByOther)
            {
                // The room can become free either during the approach or
                // after a real ROOM_WAIT_START. In both cases resume directly
                // from this still-valid approach snapshot. Never discard the
                // approach and recursively enter the load-recovery branch
                // while the exterior destination is still active.
                if (hasWait)
                {
                    LogProtectedRoomWaitEvent(
                        janitor,
                        room,
                        "ROOM_WAIT_RESUME",
                        wait,
                        "room-free-during-wait");
                }
                else if (TrafficControlConfig.PathfindingDebug)
                {
                    Plugin.Log?.LogInfo(
                        "[JanitorDebug] ROOM_WAIT_EARLY_RESUME" +
                        " | janitor=" + CharacterName(janitor.m_entity) +
                        " | roomType=" + RoomTypeId(room) +
                        " | floor=" + room.GetFloorIndex() +
                        " | roomBounds=" + RoomBounds(room) +
                        " | reason=free-during-approach.");
                }

                DiscardProtectedRoomApproach(janitor.m_entity);
                if (hasWait)
                {
                    // Clear only after logging: this removes HTC's bubble,
                    // starts the post-wait cooldown and releases wait ownership.
                    ClearProtectedRoomWait(janitor);
                }

                if (owner == null)
                {
                    room.m_roomPersistentData.m_reservedByCharacter =
                        janitor.m_entity;
                }

                if (walk.IsBusy())
                {
                    // The exterior route is no longer the job destination.
                    // Clear its completed route as well as its native job.
                    StopWalkCleanly(walk, janitor.m_entity);
                }

                walk.SetDestination(
                    approach.OriginalDestination,
                    approach.OriginalFloor);
                return true;
            }

            if (UnityEngine.Time.realtimeSinceStartup -
                    approach.StartedRealtime >
                ProtectedRoomApproachTimeoutSeconds &&
                !hasWait)
            {
                return AbortProtectedRoomApproach(
                    janitor, room, walk, "approach-timeout");
            }

            if (!approach.HasWaitTile)
            {
                Vector2i candidate;
                Vector2i doorwayAnchor;
                float doorRouteDistance;
                if (!TryFindProtectedRoomWaitTile(
                        janitor,
                        room,
                        walk,
                        out candidate,
                        out doorwayAnchor,
                        out doorRouteDistance))
                {
                    return AbortProtectedRoomApproach(
                        janitor,
                        room,
                        walk,
                        "no-nonblocking-public-wait-tile");
                }

                // Stop the native trip before launching route validation:
                // it must not continue into a protected patient room.
                if (walk.IsBusy())
                {
                    StopWalkCleanly(walk, janitor.m_entity);
                }

                approach.WaitTile = candidate;
                approach.DoorwayAnchor = doorwayAnchor;
                approach.DoorRouteDistance = doorRouteDistance;
                approach.HasWaitTile = true;
                if (walk.GetCurrentTile() == candidate)
                {
                    if (!IsCurrentProtectedRoomCartPositionSafe(
                            janitor, room, walk))
                    {
                        return AbortProtectedRoomApproach(
                            janitor, room, walk,
                            "already-there-cart-footprint-unsafe");
                    }
                    approach.RouteValidated = true;
                }
                else
                {
                    approach.ValidationJob =
                        CreateProtectedRoomRouteJob(
                            janitor, walk, candidate);
                    if (approach.ValidationJob == null)
                    {
                        return AbortProtectedRoomApproach(
                            janitor, room, walk, "no-route-validator");
                    }

                    approach.ValidationJob.TryToStart(
                        ThreadedJob.THREAD_CATEGORY_PATHFINDING);
                }
                return true;
            }

            if (!approach.RouteValidated)
            {
                PathfinderJob job = approach.ValidationJob;
                if (job == null)
                {
                    return AbortProtectedRoomApproach(
                        janitor, room, walk, "route-validator-lost");
                }

                if (!job.IsDone)
                {
                    if (!job.IsRunning)
                    {
                        job.TryToStart(
                            ThreadedJob.THREAD_CATEGORY_PATHFINDING);
                    }
                    return true;
                }

                Floor floor = Hospital.Instance == null ||
                    room.GetFloorIndex() < 0 ||
                    room.GetFloorIndex() >= Hospital.Instance.m_floors.Count
                        ? null
                        : Hospital.Instance.m_floors[room.GetFloorIndex()];
                if (job.m_result == null ||
                    job.m_result.m_state !=
                        PathfinderResult.PathfinderState.FOUND_ROUTE ||
                    !IsProtectedRoomApproachRouteSafe(
                        janitor, room, floor, job.m_result.m_route))
                {
                    return AbortProtectedRoomApproach(
                        janitor, room, walk, "unsafe-public-route-or-cart");
                }

                approach.RouteValidated = true;
                approach.ValidationJob = null;
                walk.SetDestination(
                    approach.WaitTile, room.GetFloorIndex());
                if (TrafficControlConfig.PathfindingDebug)
                {
                    Plugin.Log?.LogInfo(
                        "[JanitorDebug] ROOM_WAIT_APPROACH" +
                        " | janitor=" + CharacterName(janitor.m_entity) +
                        " | room=" + RoomTypeId(room) +
                        " | target=" + approach.WaitTile +
                        " | doorwayAnchor=" + approach.DoorwayAnchor +
                        " | doorRouteDistance=" +
                            approach.DoorRouteDistance.ToString(
                                "0.0", CultureInfo.InvariantCulture) +
                        " | floor=" + room.GetFloorIndex() +
                        " | cartAttached=" + IsCartAttached(janitor) +
                        " | routeValidated=True.");
                }
                return true;
            }

            if (walk.IsBusy())
            {
                PathfinderRoute nativeRoute = walk.m_route;
                if (nativeRoute != null &&
                    !ReferenceEquals(
                        nativeRoute, approach.CheckedNativeRoute))
                {
                    Floor floor = Hospital.Instance == null ||
                        room.GetFloorIndex() < 0 ||
                        room.GetFloorIndex() >= Hospital.Instance.m_floors.Count
                            ? null
                            : Hospital.Instance.m_floors[room.GetFloorIndex()];
                    if (!IsProtectedRoomApproachRouteSafe(
                            janitor, room, floor, nativeRoute))
                    {
                        return AbortProtectedRoomApproach(
                            janitor, room, walk, "native-route-unsafe");
                    }
                    approach.CheckedNativeRoute = nativeRoute;
                }
                return true;
            }

            if (walk.GetCurrentTile() != approach.WaitTile ||
                !IsValidProtectedRoomWaitTile(
                    janitor.m_entity,
                    Hospital.Instance.m_floors[room.GetFloorIndex()],
                    room,
                    approach.WaitTile))
            {
                return AbortProtectedRoomApproach(
                    janitor, room, walk, "position-lost");
            }

            if (!IsCurrentProtectedRoomCartPositionSafe(
                    janitor, room, walk))
            {
                return AbortProtectedRoomApproach(
                    janitor, room, walk, "arrival-cart-footprint-unsafe");
            }

            bool started;
            ProtectedRoomWaitState currentWait =
                GetOrStartProtectedRoomWait(
                    janitor, room, "exterior-staged",
                    protectedRoom ? "protected-room" : "reserved-by-other",
                    protectedRoom, claimedByOther, out started);
            ReleaseRoomReservation(janitor, room);

            if (!HasProtectedRoomWaitExpired(currentWait))
            {
                return true;
            }

            LogProtectedRoomWaitEvent(
                janitor, room, "ROOM_WAIT_RETRY",
                currentWait, "exterior-timeout");
            return AbortProtectedRoomApproach(
                janitor, room, walk, "wait-timeout");
        }

        private static bool IsAdjacentToRoom(
            WalkComponent walk,
            Room room)
        {
            if (walk == null ||
                room == null ||
                walk.GetFloorIndex() != room.GetFloorIndex())
            {
                return false;
            }

            Vector2i tile = walk.GetCurrentTile();
            int floor = walk.GetFloorIndex();
            // In particular, never freeze a janitor who is still inside
            // a different bathroom while targeting a neighbouring room.
            if (IsBathroomRoom(GetRoomAtSafe(tile, floor)))
            {
                return false;
            }

            return ReferenceEquals(
                       GetRoomAtSafe(
                           new Vector2i(tile.m_x + 1, tile.m_y),
                           floor), room) ||
                   ReferenceEquals(
                       GetRoomAtSafe(
                           new Vector2i(tile.m_x - 1, tile.m_y),
                           floor), room) ||
                   ReferenceEquals(
                       GetRoomAtSafe(
                           new Vector2i(tile.m_x, tile.m_y + 1),
                           floor), room) ||
                   ReferenceEquals(
                       GetRoomAtSafe(
                           new Vector2i(tile.m_x, tile.m_y - 1),
                           floor), room);
        }

        private static bool IsProtectedRoomDeferred(
            Room room)
        {
            if (room == null)
            {
                return false;
            }

            float until;
            if (!DeferredProtectedRooms.TryGetValue(
                    room,
                    out until))
            {
                return false;
            }

            if (GetGameClockMinutes() < until)
            {
                return true;
            }

            // A timed-out room stays deferred while its original reason
            // persists. A timer alone must not trigger another 15-minute wait.
            // Check native ownership and clinical occupancy fresh only after
            // the minimum deferral has elapsed.
            if (GetRoomReservationOwner(room) != null ||
                ShouldAvoidWholeRoomFresh(room))
            {
                return true;
            }

            DeferredProtectedRooms.Remove(room);

            if (TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[JanitorDebug] ROOM_REQUEUE_AVAILABLE" +
                    " | floor=" + room.GetFloorIndex() +
                    " | roomType=" + RoomTypeId(room) +
                    " | currentMinute=" +
                        GetGameClockMinutes() +
                    ".");
            }

            return false;
        }

        private static void ReturnAttachedCartToHome(
            BehaviorJanitor janitor,
            WalkComponent walk,
            Room room)
        {
            // Reuse vanilla's ReturningCart state rather than retrying an
            // impossible cart placement once per frame forever.
            ReleaseRoomReservation(janitor, room);
            ClearProtectedRoomWait(janitor);
            ReleaseReservedTile(janitor, walk);
            janitor.m_state.m_room = null;
            if (walk != null)
            {
                walk.SetDestination(
                    janitor.m_state.m_cartHomeTile,
                    janitor.m_state.m_cartHomeFloorIndex);
            }
            janitor.SwitchState(BehaviorJanitorState.ReturningCart);
        }

        private static bool ParkAttachedCartForReselection(
            BehaviorJanitor janitor,
            WalkComponent walk)
        {
            if (!IsCartAttached(janitor))
            {
                return true;
            }

            TileObject cart =
                janitor.m_state.m_cart == null
                    ? null
                    : janitor.m_state.m_cart.GetEntity();
            if (cart == null || walk == null)
            {
                return false;
            }

            cart.StopSounds();
            if (!MapScriptInterface.Instance.MoveObject(
                    cart,
                    walk.GetCurrentTile()))
            {
                if (TrafficControlConfig.PathfindingDebug)
                {
                    Plugin.Log?.LogWarning(
                        "[JanitorDebug] CART_REQUEUE_PARK_FAILED" +
                        " | janitor=" +
                            CharacterName(janitor.m_entity) +
                        " | currentTile=" +
                            walk.GetCurrentTile() +
                        " | floor=" +
                            walk.GetFloorIndex() +
                        ".");
                }

                return false;
            }

            cart.SetAttachedToCharacter(false);
            return true;
        }

        private static void LogProtectedRoomWaitEvent(
            BehaviorJanitor janitor,
            Room room,
            string eventName,
            ProtectedRoomWaitState wait,
            string reason)
        {
            if (!TrafficControlConfig.PathfindingDebug ||
                room == null)
            {
                return;
            }

            Plugin.Log?.LogInfo(
                "[JanitorDebug] " + eventName +
                " | janitor=" +
                    CharacterName(
                        janitor == null
                            ? null
                            : janitor.m_entity) +
                " | floor=" + room.GetFloorIndex() +
                " | roomType=" + RoomTypeId(room) +
                " | roomBounds=" + RoomBounds(room) +
                " | reason=" + reason +
                " | waitMinutes=" +
                    (wait == null
                        ? 0
                        : wait.DurationMinutes) +
                " | deadlineMinute=" +
                    (wait == null
                        ? 0
                        : wait.DeadlineMinute) +
                " | currentMinute=" +
                    GetGameClockMinutes() +
                " | recheckSeconds=" +
                    ProtectedRoomWaitRecheckSeconds +
                ".");
        }

        private static void LogProtectedRoomBlock(
            BehaviorJanitor janitor,
            Room room,
            string stage)
        {
            if (!TrafficControlConfig.PathfindingDebug || room == null)
            {
                return;
            }

            GameDBRoomType roomType =
                room.m_roomPersistentData == null
                    ? null
                    : room.m_roomPersistentData.m_roomType.Entry;
            Entity patientInside = FindPatientInsideRoom(room);
            Entity procedureOwner = GetCurrentProcedureOwner(room);
            Entity reservedBy = GetRoomReservationOwner(room);

            Plugin.Log?.LogInfo(
                "[JanitorDebug] PROTECTED_ROOM_BLOCK" +
                " | stage=" + stage +
                " | janitor=" + CharacterName(
                    janitor == null ? null : janitor.m_entity) +
                " | floor=" + room.GetFloorIndex() +
                " | roomType=" + RoomTypeId(room) +
                " | roomBounds=" + RoomBounds(room) +
                " | access=" +
                    (roomType == null
                        ? "<none>"
                        : roomType.AccessRights.ToString()) +
                " | acceptsOutpatients=" +
                    (roomType != null && roomType.AcceptsOutpatients) +
                " | patientInside=" + CharacterName(patientInside) +
                " | procedureOwner=" + CharacterName(procedureOwner) +
                " | reservedBy=" +
                    (reservedBy == null
                        ? "<none>"
                        : reservedBy.GetType().Name + ":" +
                          CharacterName(reservedBy)) +
                ".");
        }

        private static void LogBathroomCleaningBlock(
            BehaviorJanitor janitor,
            Room room,
            Vector2i tile,
            string reason)
        {
            if (!TrafficControlConfig.BathroomFlowDebug)
            {
                return;
            }

            // Diagnostic only, after the safety decision was already made.
            BathroomCleaningTopology topology = BathroomCleaningTopology.Create(room);
            Plugin.Log?.LogInfo("[BathroomDebug] JANITOR_WC_CLEANING_BLOCK" +
                " | janitor=" + CharacterName(
                    janitor == null ? null : janitor.m_entity) +
                " | floor=" + (room == null ? -1 : room.GetFloorIndex()) +
                " | roomType=" + RoomTypeId(room) +
                " | roomBounds=" + RoomBounds(room) +
                " | tile=" + tile +
                " | tileCompartment=" +
                    (topology == null ? -1 : topology.DiagnosticGetTileCompartment(tile)) +
                " | toiletCount=" +
                    (topology == null ? 0 : topology.DiagnosticToiletCount) +
                " | toiletCompartments=" +
                    (topology == null ? 0 : topology.DiagnosticToiletCompartmentCount) +
                " | occupiedCompartments=" +
                    (topology == null ? 0 : topology.DiagnosticOccupiedCompartmentCount) +
                " | tileBlocked=" +
                    (topology != null && topology.IsTileProtected(tile)) +
                " | reason=" + reason + ".");
        }

        private static bool IsBathroomRoom(Room room)
        {
            return room != null &&
                   room.m_roomPersistentData != null &&
                   room.m_roomPersistentData.m_roomType.Entry != null &&
                   room.m_roomPersistentData.m_roomType.Entry.HasTag("wc");
        }

        private static Room GetRoomAtSafe(
            Vector2i tile,
            int floorIndex)
        {
            if (Hospital.Instance == null ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (!RoomGeometry.IsInsideFloor(floor, tile))
            {
                return null;
            }

            return floor.m_roomTiles[tile.m_x, tile.m_y];
        }

        private static Vector2i ToTile(Vector2f position)
        {
            return new Vector2i(
                (int)(position.m_x + 0.5f),
                (int)(position.m_y + 0.5f));
        }

        private static string RoomBounds(Room room)
        {
            return room == null || room.m_roomPersistentData == null
                ? "<none>"
                : room.m_roomPersistentData.m_positionBottom.ToString() +
                  ".." +
                  room.m_roomPersistentData.m_positionTop.ToString();
        }

        private static string RoomTypeId(Room room)
        {
            GameDBRoomType roomType =
                room == null ||
                room.m_roomPersistentData == null
                    ? null
                    : room.m_roomPersistentData.m_roomType.Entry;

            return roomType == null
                ? "<none>"
                : roomType.DatabaseID.ToString();
        }

        private static string CharacterName(Entity entity)
        {
            return entity == null
                ? "<unknown>"
                : (entity.Name ?? string.Empty).Trim();
        }

        private static bool ReleaseCleaningTargetAndReselect(
            BehaviorJanitor janitor,
            Room room)
        {
            ClearProtectedRoomWait(janitor);
            if (janitor.m_entity != null)
            {
                DiscardProtectedRoomApproach(janitor.m_entity);
            }
            WalkComponent walk = janitor.GetComponent<WalkComponent>();
            Room stateRoom = janitor.m_state.m_room == null
                ? null
                : janitor.m_state.m_room.GetEntity();

            ReleaseRoomReservation(janitor, room);
            if (stateRoom != null && !ReferenceEquals(stateRoom, room))
            {
                ReleaseRoomReservation(janitor, stateRoom);
            }

            ReleaseReservedTile(janitor, walk);
            janitor.m_state.m_room = null;

            if (room != null && IsProtectedRoomDeferred(room))
            {
                // A room just rejected by HTC must not immediately be picked
                // again by the same janitor's room selection. We can still use
                // vanilla's tile-only fallback, or return the cart as vanilla
                // does when no acceptable work is available.
                if (InvokeBool(
                        TryToSelectIndoorTileMethod,
                        janitor,
                        new object[] { 10 }))
                {
                    return true;
                }

                return InvokeVoid(GoReturnCartMethod, janitor, null);
            }

            return ReselectJanitor(janitor);
        }

        private static bool ReselectJanitor(BehaviorJanitor janitor)
        {
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

            return InvokeVoid(GoReturnCartMethod, janitor, null);
        }

        private static bool IsCartAttached(BehaviorJanitor janitor)
        {
            TileObject cart =
                janitor == null ||
                janitor.m_state == null ||
                janitor.m_state.m_cart == null
                    ? null
                    : janitor.m_state.m_cart.GetEntity();

            return cart != null &&
                   cart.m_state != null &&
                   cart.m_state.m_attachedToCharacter;
        }

        internal static Vector3i FindDirtiestTileInRoomWithMatchingAssignmentAnyFloor(
            BehaviorJanitor behaviorJanitor,
            Department department,
            int threshold)
        {
            return FindDirtiestRoomTile(
                behaviorJanitor,
                department,
                threshold,
                true);
        }

        internal static Vector3i FindDirtiestTileInAnyUnreservedRoomAnyFloor(
            Department department,
            int threshold)
        {
            return FindDirtiestRoomTile(
                null,
                department,
                threshold,
                false);
        }

        private static Vector3i FindDirtiestRoomTile(
            BehaviorJanitor behaviorJanitor,
            Department department,
            int threshold,
            bool respectAssignments)
        {
            if (department == null ||
                (respectAssignments &&
                 (behaviorJanitor == null || behaviorJanitor.m_state == null)))
            {
                return new Vector3i(0, 0, 0);
            }

            Dictionary<Room, bool> procedureRoomCache =
                new Dictionary<Room, bool>();
            Dictionary<Room, BathroomCleaningTopology> bathroomTopologyCache =
                new Dictionary<Room, BathroomCleaningTopology>();

            float highestDirt = 0f;
            Vector2i selectedPosition = Vector2i.ZERO_VECTOR;
            int selectedFloor = 0;

            foreach (EntityIDPointer<Room> roomPointer in
                     department.m_departmentPersistentData.m_rooms)
            {
                Room room = roomPointer.GetEntity();
                if (!IsEligibleRoom(
                        behaviorJanitor,
                        room,
                        respectAssignments,
                        procedureRoomCache,
                        bathroomTopologyCache))
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
                            !floor.m_tileObjects[x, y].HasAnyObject() &&
                            floor.m_accessibility[x, y] != 2 &&
                            !IsBathroomCleaningTileProtected(
                                room,
                                new Vector2i(x, y),
                                bathroomTopologyCache))
                        {
                            highestDirt =
                                floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel;
                            selectedPosition = new Vector2i(x, y);
                            selectedFloor = room.GetFloorIndex();
                        }
                    }
                }

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

            foreach (EntityIDPointer<Room> roomPointer in
                     department.m_departmentPersistentData.m_rooms)
            {
                Room room = roomPointer.GetEntity();
                if (!IsEligibleRoom(
                        behaviorJanitor,
                        room,
                        respectAssignments,
                        procedureRoomCache,
                        bathroomTopologyCache))
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
                        float dirtLevel =
                            floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel;

                        if (dirtLevel <= (float)threshold ||
                            floor.m_mapPersistentData.m_tiles[x, y].m_user != null ||
                            floor.m_roomTiles[x, y] == null ||
                            floor.m_roomTiles[x, y].m_roomPersistentData.m_reservedByCharacter != null ||
                            floor.m_tileObjects[x, y].HasAnyObject() ||
                            floor.m_accessibility[x, y] == 2 ||
                            IsBathroomCleaningTileProtected(
                                room,
                                new Vector2i(x, y),
                                bathroomTopologyCache))
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

        private static bool IsEligibleRoom(
            BehaviorJanitor behaviorJanitor,
            Room room,
            bool respectAssignments,
            Dictionary<Room, bool> procedureRoomCache,
            Dictionary<Room, BathroomCleaningTopology> bathroomTopologyCache)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_reservedByCharacter != null ||
                IsProtectedRoomDeferred(room) ||
                ShouldAvoidWholeRoomCached(
                    room,
                    procedureRoomCache))
            {
                return false;
            }

            if (!respectAssignments)
            {
                return true;
            }

            bool assignedToJanitor =
                behaviorJanitor.m_state.m_assignedRooms.Contains(room);
            bool unrestrictedAssignments =
                room.m_roomPersistentData.m_assignedJanitors.Count == 0 &&
                behaviorJanitor.m_state.m_assignedRooms.Count == 0;

            return assignedToJanitor || unrestrictedAssignments;
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
            Dictionary<Room, BathroomCleaningTopology> bathroomTopologyCache =
                new Dictionary<Room, BathroomCleaningTopology>();
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
                        !ShouldAvoidCleaningTileCached(
                            floor.m_roomTiles[x, y],
                            new Vector2i(x, y),
                            procedureRoomCache,
                            bathroomTopologyCache))
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
                        ShouldAvoidCleaningTileCached(
                            floor.m_roomTiles[x, y],
                            new Vector2i(x, y),
                            procedureRoomCache,
                            bathroomTopologyCache))
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

        internal static Vector2i FilterDirtiestBathroomTile(
            Room room,
            bool bloodOnly,
            int threshold,
            Vector2i selected)
        {
            if (selected == Vector2i.ZERO_VECTOR ||
                !TrafficControlConfig.AvoidCleaningOccupiedBathrooms ||
                !IsBathroomRoom(room))
            {
                return selected;
            }

            BathroomCleaningTopology topology =
                BathroomCleaningTopology.Create(room);
            if (topology == null ||
                !topology.IsTileProtected(selected))
            {
                return selected;
            }

            float highestDirt = 0f;
            Vector2i result = Vector2i.ZERO_VECTOR;
            Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];

            for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                 x <= room.m_roomPersistentData.m_positionTop.m_x;
                 x++)
            {
                for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                     y <= room.m_roomPersistentData.m_positionTop.m_y;
                     y++)
                {
                    Vector2i tile = new Vector2i(x, y);
                    if (room.IsPositionInRoom(tile) &&
                        !topology.IsTileProtected(tile) &&
                        floor.m_mapPersistentData.m_tiles[x, y].m_dirtType ==
                            DirtType.BLOOD &&
                        floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel >
                            highestDirt &&
                        floor.m_roomTiles[x, y] != null &&
                        floor.m_mapPersistentData.m_tiles[x, y].m_user == null &&
                        !floor.m_tileObjects[x, y].HasAnyObject() &&
                        floor.m_accessibility[x, y] != 2)
                    {
                        highestDirt =
                            floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel;
                        result = tile;
                    }
                }
            }

            if (highestDirt > 0f || bloodOnly)
            {
                return result;
            }

            for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                 x <= room.m_roomPersistentData.m_positionTop.m_x;
                 x++)
            {
                for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                     y <= room.m_roomPersistentData.m_positionTop.m_y;
                     y++)
                {
                    Vector2i tile = new Vector2i(x, y);
                    float dirtLevel =
                        floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel;

                    if (!topology.IsTileProtected(tile) &&
                        dirtLevel > (float)threshold &&
                        dirtLevel > highestDirt &&
                        floor.m_roomTiles[x, y] != null &&
                        floor.m_mapPersistentData.m_tiles[x, y].m_user == null &&
                        !floor.m_tileObjects[x, y].HasAnyObject() &&
                        floor.m_accessibility[x, y] != 2)
                    {
                        highestDirt = dirtLevel;
                        result = tile;
                    }
                }
            }

            return result;
        }

        internal static Vector2i FilterClosestBathroomTile(
            Room room,
            Vector2i characterPosition,
            Vector2i selected)
        {
            if (selected == Vector2i.ZERO_VECTOR ||
                !TrafficControlConfig.AvoidCleaningOccupiedBathrooms ||
                !IsBathroomRoom(room))
            {
                return selected;
            }

            BathroomCleaningTopology topology =
                BathroomCleaningTopology.Create(room);
            if (topology == null ||
                !topology.IsTileProtected(selected))
            {
                return selected;
            }

            float highestDirt = 0f;
            float closestDistance = 2.1474836E+09f;
            Vector2i result = Vector2i.ZERO_VECTOR;
            Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];

            for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                 x <= room.m_roomPersistentData.m_positionTop.m_x;
                 x++)
            {
                for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                     y <= room.m_roomPersistentData.m_positionTop.m_y;
                     y++)
                {
                    Vector2i tile = new Vector2i(x, y);
                    if (!room.IsPositionInRoom(tile) ||
                        topology.IsTileProtected(tile) ||
                        floor.m_mapPersistentData.m_tiles[x, y].m_dirtType !=
                            DirtType.BLOOD ||
                        floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel <=
                            highestDirt ||
                        floor.m_roomTiles[x, y] == null ||
                        floor.m_mapPersistentData.m_tiles[x, y].m_user != null ||
                        floor.m_tileObjects[x, y].HasAnyObject() ||
                        floor.m_accessibility[x, y] == 2)
                    {
                        continue;
                    }

                    float distance =
                        Pathfinder.getTaxiDistanceCost(
                            characterPosition,
                            tile);
                    if (distance < closestDistance)
                    {
                        highestDirt =
                            floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel;
                        closestDistance = distance;
                        result = tile;
                    }
                }
            }

            if (highestDirt > 0f)
            {
                return result;
            }

            for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                 x <= room.m_roomPersistentData.m_positionTop.m_x;
                 x++)
            {
                for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                     y <= room.m_roomPersistentData.m_positionTop.m_y;
                     y++)
                {
                    Vector2i tile = new Vector2i(x, y);
                    float dirtLevel =
                        floor.m_mapPersistentData.m_tiles[x, y].m_dirtLevel;

                    if (topology.IsTileProtected(tile) ||
                        dirtLevel <= 10f ||
                        dirtLevel <= highestDirt ||
                        floor.m_roomTiles[x, y] == null ||
                        floor.m_mapPersistentData.m_tiles[x, y].m_user != null ||
                        floor.m_tileObjects[x, y].HasAnyObject() ||
                        floor.m_accessibility[x, y] == 2)
                    {
                        continue;
                    }

                    float distance =
                        Pathfinder.getTaxiDistanceCost(
                            characterPosition,
                            tile);
                    if (distance < closestDistance)
                    {
                        highestDirt = dirtLevel;
                        closestDistance = distance;
                        result = tile;
                    }
                }
            }

            return result;
        }

        internal static Vector2i FilterBathroomCartDestination(
            Vector2i position,
            Room room,
            Vector2i selected)
        {
            if (selected == Vector2i.ZERO_VECTOR ||
                !TrafficControlConfig.AvoidCleaningOccupiedBathrooms ||
                !IsBathroomRoom(room))
            {
                return selected;
            }

            BathroomCleaningTopology topology =
                BathroomCleaningTopology.Create(room);
            if (topology == null ||
                !IsCartAreaProtected(topology, selected))
            {
                return selected;
            }

            int closestDistance = int.MaxValue;
            Vector2i result = Vector2i.ZERO_VECTOR;
            Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];
            bool crossSelection =
                room.m_roomPersistentData.m_roomType.Entry.HasTag(
                    "cross_janitor_cart_tile_selection");

            for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                 x <= room.m_roomPersistentData.m_positionTop.m_x;
                 x++)
            {
                for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                     y <= room.m_roomPersistentData.m_positionTop.m_y;
                     y++)
                {
                    int distance =
                        (x - position.m_x) * (x - position.m_x) +
                        (y - position.m_y) * (y - position.m_y);

                    if (distance >= closestDistance)
                    {
                        continue;
                    }

                    Vector2i candidate = new Vector2i(x, y);
                    bool valid = room.IsPositionInRoom(candidate) &&
                                 !IsCartAreaProtected(topology, candidate);

                    for (int dx = -1; dx <= 1 && valid; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx != 0 && dy != 0 && crossSelection)
                            {
                                continue;
                            }

                            Vector2i tile =
                                new Vector2i(x + dx, y + dy);
                            if (!RoomGeometry.IsInsideFloor(floor, tile))
                            {
                                valid = false;
                                break;
                            }

                            if (dx > -1 &&
                                floor.m_mapPersistentData.m_tileWalls[
                                    x + dx,
                                    y + dy].m_wallSE != null)
                            {
                                valid = false;
                            }

                            if (dy > -1 &&
                                floor.m_mapPersistentData.m_tileWalls[
                                    x + dx,
                                    y + dy].m_wallSW != null)
                            {
                                valid = false;
                            }

                            if (floor.m_mapPersistentData.m_tiles[
                                    x + dx,
                                    y + dy].m_user != null ||
                                floor.m_mapPersistentData.m_foundationsLayer
                                    .m_foundations[x + dx, y + dy] != 2 ||
                                floor.m_tileObjects[x + dx, y + dy]
                                    .IsAnyObjectBlocking() ||
                                floor.m_tileObjects[x + dx, y + dy]
                                    .m_centerObject != null ||
                                floor.m_accessibility[x + dx, y + dy] == 2)
                            {
                                valid = false;
                            }

                            if (!valid)
                            {
                                break;
                            }
                        }
                    }

                    if (valid)
                    {
                        closestDistance = distance;
                        result = candidate;
                    }
                }
            }

            return result;
        }

        private static bool IsCartAreaProtected(
            BathroomCleaningTopology topology,
            Vector2i center)
        {
            if (topology == null)
            {
                return false;
            }

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (topology.IsTileProtected(
                            new Vector2i(
                                center.m_x + dx,
                                center.m_y + dy)))
                    {
                        return true;
                    }
                }
            }

            return false;
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

        private static bool ShouldAvoidWholeRoomFresh(Room room)
        {
            return TrafficControlConfig.AvoidCleaningActiveProcedureRooms &&
                   HasActiveProcedure(room);
        }

        private static bool ShouldAvoidWholeRoomCached(
            Room room,
            Dictionary<Room, bool> procedureRoomCache)
        {
            return ShouldAvoidActiveProcedureCached(
                room,
                procedureRoomCache);
        }

        // Shared admission decision for tile selection, target travel and
        // the final Cleaning recheck. Room-wide clinical protection wins over
        // compartment checks; WC occupancy is evaluated only when relevant.
        // Passing null caches always requests a fresh, safety-critical check.
        private static void CheckCleaningTargetProtection(
            Room room,
            Vector2i tile,
            Dictionary<Room, bool> procedureRoomCache,
            Dictionary<Room, BathroomCleaningTopology> bathroomTopologyCache,
            out bool protectedRoom,
            out bool blockedBathroomTile)
        {
            protectedRoom = ShouldAvoidWholeRoomCached(
                room, procedureRoomCache);
            blockedBathroomTile = !protectedRoom &&
                IsBathroomCleaningTileProtected(
                    room, tile, bathroomTopologyCache);
        }

        private static bool ShouldAvoidCleaningTileCached(
            Room room,
            Vector2i tile,
            Dictionary<Room, bool> procedureRoomCache,
            Dictionary<Room, BathroomCleaningTopology> bathroomTopologyCache)
        {
            if (IsProtectedRoomDeferred(room))
            {
                return true;
            }

            bool protectedRoom;
            bool blockedBathroomTile;
            CheckCleaningTargetProtection(
                room, tile, procedureRoomCache, bathroomTopologyCache,
                out protectedRoom, out blockedBathroomTile);
            return protectedRoom || blockedBathroomTile;
        }

        private static bool IsBathroomCleaningTileProtected(
            Room room,
            Vector2i tile,
            Dictionary<Room, BathroomCleaningTopology> bathroomTopologyCache)
        {
            if (!TrafficControlConfig.AvoidCleaningOccupiedBathrooms ||
                !IsBathroomRoom(room))
            {
                return false;
            }

            BathroomCleaningTopology topology;
            if (bathroomTopologyCache != null &&
                bathroomTopologyCache.TryGetValue(room, out topology))
            {
                return topology != null &&
                       topology.IsTileProtected(tile);
            }

            topology = BathroomCleaningTopology.Create(room);

            if (bathroomTopologyCache != null)
            {
                bathroomTopologyCache[room] = topology;
            }

            return topology != null &&
                   topology.IsTileProtected(tile);
        }

        internal static bool IsOccupiedBathroom(Room room)
        {
            if (!TrafficControlConfig.AvoidCleaningOccupiedBathrooms ||
                !IsBathroomRoom(room))
            {
                return false;
            }

            BathroomCleaningTopology topology =
                BathroomCleaningTopology.Create(room);
            return topology != null &&
                   topology.HasOccupiedCompartment;
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
            if (roomType == null)
            {
                return false;
            }

            // Long-stay hospitalization rooms must remain cleanable while occupied.
            if (roomType.HasTag("hospitalization"))
            {
                return false;
            }

            // WC rooms have their own stricter physical-occupancy policy. Do not let
            // a distant bladder reservation turn AvoidCleaningActiveProcedureRooms
            // into a second, broader WC rule.
            if (roomType.HasTag("wc"))
            {
                return false;
            }

            Entity procedureOwner = GetCurrentProcedureOwner(room);
            Entity reservedBy = GetRoomReservationOwner(room);

            // Native room ownership/reservation always wins while it exists.
            if (procedureOwner != null || reservedBy is ProcedureScript)
            {
                return true;
            }

            // Direct source of truth after the cheap room exclusions below: if a
            // patient is physically inside this candidate clinical room, routine
            // cleaning must wait. This intentionally does not depend on a procedure
            // script because clinic patients can already be seated in the doctor's
            // office while still in GoingToDoctor, before SelectNextProcedure()
            // creates the examination script.
            if (!CanCheckDirectPatientOccupancy(room))
            {
                return false;
            }

            // The diagnostic enumerates other characters and is optional.
            // Do not run it for an excluded room or a room whose native
            // procedure owner already proves occupancy.
            if (TrafficControlConfig.PathfindingDebug)
            {
                LogPatientOccupancyCheck(room, roomType);
            }

            return FindPatientInsideRoom(room) != null;
        }

        private static bool CanCheckDirectPatientOccupancy(Room room)
        {
            GameDBRoomType roomType =
                room == null ||
                room.m_roomPersistentData == null
                    ? null
                    : room.m_roomPersistentData.m_roomType.Entry;

            return roomType != null &&
                   GetDirectPatientOccupancyExclusionReason(roomType) == null;
        }

        private static string GetDirectPatientOccupancyExclusionReason(
            GameDBRoomType roomType)
        {
            if (roomType == null)
            {
                return "no-room-type";
            }

            if (roomType.AccessRights == AccessRights.STAFF)
            {
                return "access-staff";
            }

            if (roomType.AccessRights == AccessRights.STAFF_ONLY)
            {
                return "access-staff-only";
            }

            if (roomType.HasTag("hospitalization"))
            {
                return "hospitalization";
            }

            if (roomType.HasTag("operating_room"))
            {
                return "operating-room";
            }

            if (roomType.HasTag("wc"))
            {
                return "wc";
            }

            if (roomType.HasTag("corridor"))
            {
                return "corridor";
            }

            if (roomType.HasTag("waiting_room"))
            {
                return "waiting-room";
            }

            if (roomType.HasTag("nurse_reception"))
            {
                return "reception";
            }

            if (roomType.HasTag("cafeteria"))
            {
                return "cafeteria";
            }

            if (roomType.HasTag("lounge"))
            {
                return "lounge";
            }

            if (roomType.HasTag("gift_shop"))
            {
                return "gift-shop";
            }

            if (roomType.HasTag("pharmacy"))
            {
                return "pharmacy";
            }

            if (roomType.HasTag("common_room"))
            {
                return "common-room";
            }

            if (roomType.HasTag("classroom"))
            {
                return "classroom";
            }

            if (roomType.DatabaseID.ToString() == "ROOM_TYPE_ELEVATOR_MARKER")
            {
                return "elevator-marker";
            }

            return null;
        }

        private static void LogPatientOccupancyCheck(
            Room room,
            GameDBRoomType roomType)
        {
            if (!TrafficControlConfig.PathfindingDebug ||
                room == null ||
                roomType == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_characters == null)
            {
                return;
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            float nextCheck;
            if (NextPatientDiagnosticRealtime.TryGetValue(room, out nextCheck) &&
                now < nextCheck)
            {
                return;
            }

            NextPatientDiagnosticRealtime[room] = now + 5f;
            List<string> patients = new List<string>();

            foreach (Entity character in Hospital.Instance.m_characters)
            {
                BehaviorPatient patient =
                    character == null
                        ? null
                        : character.GetComponent<BehaviorPatient>();
                WalkComponent walk =
                    character == null
                        ? null
                        : character.GetComponent<WalkComponent>();

                if (patient == null ||
                    walk == null ||
                    walk.GetFloorIndex() != room.GetFloorIndex())
                {
                    continue;
                }

                Vector2i tile = walk.GetCurrentTile();
                bool insideByBounds = room.IsPositionInRoom(tile);
                Room roomAtWalk = MapScriptInterface.Instance.GetRoomAt(walk);
                bool insideByLookup = roomAtWalk == room;

                if (!insideByBounds && !insideByLookup)
                {
                    continue;
                }

                string patientState =
                    patient.m_state == null
                        ? "<none>"
                        : patient.m_state.m_patientState.ToString();
                string walkState =
                    walk.m_state == null
                        ? "<none>"
                        : walk.m_state.m_walkState.ToString();
                bool lying =
                    walk.m_state != null &&
                    walk.m_state.m_lying;

                patients.Add(
                    CharacterName(character) +
                    "{tile=" + tile +
                    ",state=" + patientState +
                    ",walkState=" + walkState +
                    ",sitting=" + walk.IsSitting() +
                    ",lying=" + lying +
                    ",insideByBounds=" + insideByBounds +
                    ",insideByRoomLookup=" + insideByLookup +
                    ",lookupRoom=" + RoomTypeId(roomAtWalk) +
                    "}");
            }

            if (patients.Count == 0)
            {
                return;
            }

            string exclusion =
                GetDirectPatientOccupancyExclusionReason(roomType);

            Plugin.Log?.LogInfo(
                "[JanitorDebug] ROOM_PATIENT_SCAN" +
                " | floor=" + room.GetFloorIndex() +
                " | roomType=" + RoomTypeId(room) +
                " | access=" + roomType.AccessRights +
                " | directScanEligible=" + (exclusion == null) +
                " | exclusion=" + (exclusion ?? "<none>") +
                " | patients=" + string.Join(";", patients.ToArray()) +
                ".");
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

        internal static Entity FindPatientInsideRoom(Room room)
        {
            if (room == null || room.m_roomPersistentData == null ||
                Hospital.Instance == null)
            {
                return null;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            // The native direct lying-patient pointer is a cheap *positive*
            // signal only. Always verify its physical position; null does
            // not mean that a standing/seated patient is absent.
            EntityIDPointer<Entity> lyingPointer =
                room.m_roomPersistentData.m_currentLyingPatient;
            Entity lyingPatient =
                lyingPointer == null ? null : lyingPointer.GetEntity();
            if (lyingPatient != null &&
                lyingPatient.GetComponent<BehaviorPatient>() != null &&
                IsEntityPhysicallyInsideRoom(lyingPatient, room))
            {
                return lyingPatient;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Grid grid = floor == null ? null : floor.m_grid;
            if (grid == null || grid.m_objects == null)
            {
                // During early load only: the native spatial index may
                // not be constructed yet. Keep the conservative slow path.
                return FindPatientInsideRoomBeforeGridReady(room);
            }

            // Room-first spatial query: occupied GridComponents on covered
            // tiles only. The grid is maintained by Hospital.UpdateCharacters.
            Vector2i bottom = room.m_roomPersistentData.m_positionBottom;
            Vector2i top = room.m_roomPersistentData.m_positionTop;
            for (int x = Math.Max(0, bottom.m_x);
                 x <= Math.Min(floor.Size.m_x - 1, top.m_x); x++)
            {
                for (int y = Math.Max(0, bottom.m_y);
                     y <= Math.Min(floor.Size.m_y - 1, top.m_y); y++)
                {
                    // Empty grid buckets are the common case. No need to
                    // construct or geometrically test a tile without an
                    // occupant. Still check the exact irregular-room shape
                    // for populated buckets, never just the bounding box.
                    List<GridObject> occupants = grid.m_objects[x, y];
                    if (occupants == null || occupants.Count == 0 ||
                        !room.IsPositionInRoom(new Vector2i(x, y)))
                    {
                        continue;
                    }

                    foreach (GridObject occupant in occupants)
                    {
                        GridComponent gridComponent =
                            occupant as GridComponent;
                        Entity entity = gridComponent == null
                            ? null : gridComponent.m_entity;
                        if (entity != null &&
                            entity.GetComponent<BehaviorPatient>() != null &&
                            IsEntityPhysicallyInsideRoom(entity, room))
                        {
                            return entity;
                        }
                    }
                }
            }

            return null;
        }

        private static Entity FindPatientInsideRoomBeforeGridReady(Room room)
        {
            if (Hospital.Instance == null ||
                Hospital.Instance.m_departments == null)
            {
                return null;
            }

            foreach (Department department in Hospital.Instance.m_departments)
            {
                if (department == null ||
                    department.m_departmentPersistentData == null ||
                    department.m_departmentPersistentData.m_patients == null)
                {
                    continue;
                }

                foreach (EntityIDPointer<Entity> pointer in
                         department.m_departmentPersistentData.m_patients)
                {
                    Entity patient = pointer == null
                        ? null : pointer.GetEntity();
                    if (patient != null &&
                        patient.GetComponent<BehaviorPatient>() != null &&
                        IsEntityPhysicallyInsideRoom(patient, room))
                    {
                        return patient;
                    }
                }
            }

            return null;
        }

        private static void StopWalkCleanly(WalkComponent walk, Entity entity)
        {
            if (walk == null || walk.m_state == null)
            {
                return;
            }

            walk.Stop();
            walk.m_route = null;
            walk.m_blockedCount = 0;
            walk.m_state.m_nextPosition = walk.m_state.m_currentPosition;
            walk.m_state.m_walkMidpoint1 = null;
            walk.m_state.m_walkMidpoint2 = null;

            if (walk.m_state.m_lying)
            {
                return;
            }

            AnimModelComponent animation =
                entity == null
                    ? null
                    : entity.GetComponent<AnimModelComponent>();
            if (animation != null)
            {
                if (entity != null && ProtectedRoomWaits.ContainsKey(entity))
                {
                    animation.PlayAnimation("stand_wait");
                }
                else
                {
                    // Vanilla loops stand_idle. A one-shot idle animation
                    // visibly freezes the model if no new task starts quickly.
                    animation.PlayAnimation("stand_idle");
                }
            }
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

        internal static bool TryPreserveOtherJanitorRoomReservation(
            BehaviorJanitor janitor)
        {
            if (!TrafficControlConfig.AvoidCleaningActiveProcedureRooms ||
                janitor == null ||
                janitor.m_entity == null ||
                janitor.m_state == null ||
                janitor.m_state.m_room == null)
            {
                return false;
            }

            Room room = janitor.m_state.m_room.GetEntity();
            Entity owner = GetRoomReservationOwner(room);
            if (owner == null || ReferenceEquals(owner, janitor.m_entity))
            {
                return false;
            }

            // Vanilla FreeRoom() clears the reservation without checking who
            // currently owns it; the previous janitor may have a stale pointer.
            if (TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[JanitorDebug] FOREIGN_ROOM_RESERVATION_PRESERVED" +
                    " | janitor=" + CharacterName(janitor.m_entity) +
                    " | other=" + CharacterName(owner) +
                    " | floor=" + room.GetFloorIndex() +
                    " | roomType=" + RoomTypeId(room) +
                    " | roomBounds=" + RoomBounds(room) +
                    ".");
            }

            janitor.m_state.m_room = null;
            return true;
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
                "Janitor protected-room handling could not resolve one or more native BehaviorJanitor methods.");
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
                "Janitor protected-room handling failed while calling native BehaviorJanitor logic: " +
                root.GetType().FullName + ": " + root.Message);
        }
    }
}
