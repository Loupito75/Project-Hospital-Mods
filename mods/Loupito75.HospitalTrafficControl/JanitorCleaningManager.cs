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

        private static readonly MethodInfo TryToSelectTileInCurrentRoomMethod =
            AccessTools.Method(
                typeof(BehaviorJanitor),
                "TryToSelectTileInCurrentRoom");

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
        // Strict physical ranking replaces the former additive preference.

        // Exterior cart staging uses the bucket cart's real one-tile
        // placement + pickup access, like HPO's movable sample cart. Vanilla
        // FindClosest3x3Area(...) remains the untouched interior fallback.
        private const int JanitorCartExteriorRadius = 5;
        private const int JanitorCartDoorPreferenceRadius = 2;
        private static readonly Direction[] DoorExposureCartDirections =
            new Direction[]
            {
                Direction.NW,
                Direction.NE,
                Direction.SE,
                Direction.SW
            };

        // Dedicated full-size bubble, with vanilla index 24 as a safe
        // fallback when the custom image is not available.
        private const string ProtectedRoomWaitBubbleAssetId =
            "HTC_ASSET_JANITOR_ROOM_OCCUPIED_BUBBLE";
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

        [ThreadStatic]
        private static BehaviorJanitor s_cartDestinationJanitor;

        // Runtime-only proof that HTC itself selected an exterior Bucket Cart
        // parking destination for the current room job. This must never be
        // persisted: after save/load, the existing ROOM_WAIT_LOAD_RECOVERY
        // still owns genuinely orphaned exterior wait destinations.
        private sealed class ExteriorCartTravelState
        {
            internal Room Room;
            internal Vector2i Destination;
            internal int FloorIndex;
        }

        private static readonly Dictionary<Entity, ExteriorCartTravelState>
            ExteriorCartTravels =
                new Dictionary<Entity, ExteriorCartTravelState>();

        // Runtime-only memory of a cart that HTC successfully parked outside
        // a private target room. It lets a repeated selection of the SAME room
        // keep using that cart instead of alternating between adjacent staging
        // tiles. Every reuse is validated against the live cart position.
        private sealed class ParkedExteriorCartState
        {
            internal Room Room;
            internal Vector2i Position;
            internal int FloorIndex;
        }

        private static readonly Dictionary<Entity, ParkedExteriorCartState>
            ParkedExteriorCarts =
                new Dictionary<Entity, ParkedExteriorCartState>();

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
            HtcFullSpeechBubblePatch.Reset();
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
            s_cartDestinationJanitor = null;
            ExteriorCartTravels.Clear();
            ParkedExteriorCarts.Clear();
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
            bool avoidActiveProcedureRooms =
                TrafficControlConfig.AvoidCleaningActiveProcedureRooms;
            bool avoidOccupiedBathrooms =
                TrafficControlConfig.AvoidCleaningOccupiedBathrooms;

            if ((!avoidActiveProcedureRooms && !avoidOccupiedBathrooms) ||
                janitor == null ||
                janitor.m_state == null ||
                janitor.m_entity == null ||
                janitor.m_state.m_janitorState != BehaviorJanitorState.Cleaning ||
                janitor.m_state.m_object != null)
            {
                return false;
            }

            WalkComponent walk = janitor.GetComponent<WalkComponent>();
            Vector2i currentTile =
                walk == null
                    ? Vector2i.ZERO_VECTOR
                    : walk.GetCurrentTile();
            Room stateRoom = janitor.m_state.m_room == null
                ? null
                : janitor.m_state.m_room.GetEntity();
            Room physicalRoom = walk == null
                ? null
                : MapScriptInterface.Instance.GetRoomAt(
                    currentTile,
                    walk.GetFloorIndex());

            // Vanilla can enter Cleaning immediately after FulfillingNeeds,
            // with no target acquired. A tile-only job is already enough to
            // prove real work; do not resolve a room owner in that common case.
            Vector2i reservedTile =
                janitor.m_state.m_reservedTile;
            bool hasTileJob =
                walk != null &&
                reservedTile != Vector2i.ZERO_VECTOR &&
                reservedTile == currentTile;

            Entity roomOwner = null;
            bool roomOwnerResolved = false;
            bool hasRoomJob = false;
            if (!hasTileJob &&
                stateRoom != null &&
                ReferenceEquals(stateRoom, physicalRoom))
            {
                roomOwner =
                    GetRoomReservationOwner(stateRoom);
                roomOwnerResolved = true;
                hasRoomJob =
                    ReferenceEquals(
                        roomOwner,
                        janitor.m_entity);
            }

            if (!hasRoomJob && !hasTileJob)
            {
                return false;
            }

            // Vanilla cleans WalkComponent.GetCurrentTile(). The physical room is
            // authoritative for the target, not a possibly stale m_state.m_room.
            Room room = physicalRoom ?? stateRoom;

            bool protectedRoom =
                avoidActiveProcedureRooms &&
                HasActiveProcedure(
                    room,
                    roomOwner,
                    roomOwnerResolved);
            bool blockedBathroomTile =
                !protectedRoom &&
                avoidOccupiedBathrooms &&
                IsBathroomCleaningTileProtected(
                    room,
                    currentTile,
                    null);

            // Vanilla reserves a room for a room-wide assignment, but the
            // indoor-tile fallback reserves only m_reservedTile. Never treat a
            // tile-only job as a stolen room reservation.
            bool roomAssignment =
                stateRoom != null &&
                ReferenceEquals(stateRoom, room) &&
                reservedTile == Vector2i.ZERO_VECTOR;

            if (roomAssignment && !roomOwnerResolved)
            {
                roomOwner =
                    GetRoomReservationOwner(room);
                roomOwnerResolved = true;
            }

            bool reservedByAnother =
                avoidActiveProcedureRooms &&
                roomAssignment &&
                roomOwner != null &&
                !ReferenceEquals(
                    roomOwner,
                    janitor.m_entity);

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

        internal static void TryInterruptProtectedRoomBeforeMovement(
            WalkComponent walk)
        {
            if (walk == null ||
                walk.m_state == null ||
                walk.m_state.m_walkState != WalkState.Walking ||
                walk.m_route == null ||
                walk.Floor == null)
            {
                return;
            }

            // MultiUpdate is a hot path for every moving character. Stay on
            // cheap grid data until the next movement segment actually crosses
            // a room boundary; only then resolve BehaviorJanitor.
            Vector2i currentTile = walk.GetCurrentTile();
            Vector2i nextTile = ToTile(walk.m_state.m_nextPosition);
            Room nextRoom =
                walk.Floor.GetRoomTileSafe(nextTile.m_x, nextTile.m_y);
            if (nextRoom == null ||
                ReferenceEquals(
                    nextRoom,
                    walk.Floor.GetRoomTileSafe(
                        currentTile.m_x,
                        currentTile.m_y)))
            {
                return;
            }

            Entity entity = CharacterAccess.GetEntity(walk);
            BehaviorJanitor janitor =
                entity == null
                    ? null
                    : entity.GetComponent<BehaviorJanitor>();
            if (janitor == null ||
                janitor.m_state == null ||
                janitor.m_state.m_janitorState !=
                    BehaviorJanitorState.WalkingToNextRoom ||
                janitor.m_state.m_room == null)
            {
                return;
            }

            Room room = janitor.m_state.m_room.GetEntity();
            if (room == null ||
                !ReferenceEquals(room, nextRoom) ||
                walk.GetFloorIndex() != room.GetFloorIndex())
            {
                return;
            }

            Entity reservationOwner = GetRoomReservationOwner(room);
            bool claimedByOther =
                reservationOwner != null &&
                !ReferenceEquals(reservationOwner, janitor.m_entity);
            if (!claimedByOther && !ShouldAvoidWholeRoomFresh(room))
            {
                return;
            }

            // Reuse the existing HTC room-travel recovery. It owns the
            // reservation/cart cleanup and optional exterior-wait decision.
            // This executes before WalkComponent.UpdateMovement().
            TryHandleProtectedRoomTravel(janitor);
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
                ClearExteriorCartTravel(janitor);
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

            // Vanilla room-wide jobs point INTO their assigned room. HTC may
            // now deliberately select an exterior Bucket Cart parking tile,
            // but only the runtime marker created by that selector proves the
            // exterior destination is legitimate. Without that marker, keep
            // the existing save/load recovery for an orphaned wait approach.
            // In particular, never let vanilla mistake a lost wait tile for
            // a valid room arrival and park/destroy a cart there.
            if (walk.m_state != null &&
                !IsExpectedExteriorCartTravel(janitor, room, walk) &&
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

                // SpeechComponentPersistentData is serialized by vanilla,
                // while HTC intentionally does not persist the transient
                // ProtectedRoomWait/Approach objects. Therefore an indefinite
                // raw wait icon can survive a save/load after its HTC owner
                // state is gone. This branch is already the narrowly-scoped
                // recovery for exactly that lost exterior wait destination.
                TryClearRecoveredProtectedRoomWaitingBubble(
                    janitor.m_entity);

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
                    if (!TrySelectJanitorCartDestination(
                            janitor,
                            walk.GetCurrentTile(),
                            room,
                            out destination))
                    {
                        destination =
                            MapScriptInterface.Instance.FindClosest3x3Area(
                                walk.GetCurrentTile(),
                                room);
                    }

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

        private static bool IsOwnProtectedRoomWaitingBubble(
            SpeechComponent speech)
        {
            SpeechComponentPersistentData state =
                speech == null ? null
                    : speech.GetPersistentData() as SpeechComponentPersistentData;
            if (state == null ||
                state.m_displayTime >= 0f ||
                state.m_bubbleIconIndex != -1)
            {
                return false;
            }

            bool ownAsset =
                state.m_speechBubble != null &&
                string.Equals(
                    state.m_bubbleIconAssetID,
                    ProtectedRoomWaitBubbleAssetId,
                    StringComparison.Ordinal);
            bool ownFallback =
                state.m_speechBubble == null &&
                state.m_bubbleTextureIndex == ProtectedRoomWaitBubbleIndex &&
                state.m_bubbleIconAssetID == null;
            return ownAsset || ownFallback;
        }

        internal static bool IsShowingProtectedRoomWaitingBubble(Entity entity)
        {
            ProtectedRoomWaitState wait;
            return entity != null &&
                   ProtectedRoomWaits.TryGetValue(entity, out wait) &&
                   wait.ShowingWaitingBubble &&
                   IsOwnProtectedRoomWaitingBubble(
                       entity.GetComponent<SpeechComponent>());
        }

        private static void ShowProtectedRoomWaitingBubble(
            Entity entity,
            ProtectedRoomWaitState wait)
        {
            if (entity == null ||
                wait == null ||
                wait.DurationMinutes <= 0)
            {
                return;
            }

            SpeechComponent speech =
                entity.GetComponent<SpeechComponent>();
            if (speech == null)
            {
                return;
            }

            if (wait.ShowingWaitingBubble &&
                IsOwnProtectedRoomWaitingBubble(speech))
            {
                return;
            }

            // A native bubble may have replaced HTC's. Never overwrite it.
            // Retry only once it has expired or been dismissed by vanilla.
            wait.ShowingWaitingBubble = false;
            if (speech.IsActive())
            {
                return;
            }

            StreamingAssetManager assets =
                StreamingAssetManager.GetInstance();
            UnityEngine.Texture texture;
            bool hasTexture =
                assets != null &&
                assets.m_textureAssets != null &&
                assets.m_textureAssets.TryGetValue(
                    ProtectedRoomWaitBubbleAssetId, out texture) &&
                texture != null;
            if (hasTexture &&
                Database.Instance != null &&
                Database.Instance.GetEntry<GameDBSpeechBubble>(
                    "BUBBLE_EMPTY") != null)
            {
                speech.SetBubble(
                    "BUBBLE_EMPTY", ProtectedRoomWaitBubbleAssetId);
            }
            else
            {
                speech.SetBubble(ProtectedRoomWaitBubbleIndex);
            }

            wait.ShowingWaitingBubble = true;
        }

        private static void HideProtectedRoomWaitingBubble(
            Entity entity,
            ProtectedRoomWaitState wait)
        {
            if (entity == null || wait == null)
            {
                return;
            }

            SpeechComponent speech =
                entity.GetComponent<SpeechComponent>();
            if (wait.ShowingWaitingBubble &&
                IsOwnProtectedRoomWaitingBubble(speech))
            {
                speech.HideBubble();
            }

            wait.ShowingWaitingBubble = false;
            // Restores only HTC's own renderer changes. Gameplay bubbles
            // placed by vanilla after ours remain untouched.
            HtcFullSpeechBubblePatch.Restore(entity);
        }

        private static void TryClearRecoveredProtectedRoomWaitingBubble(
            Entity entity)
        {
            if (entity == null)
            {
                return;
            }

            SpeechComponent speech =
                entity.GetComponent<SpeechComponent>();
            if (!IsOwnProtectedRoomWaitingBubble(speech))
            {
                return;
            }

            // Native serializes speech but HTC's transient wait does not
            // survive loading. Clear only our unique asset or atlas fallback.
            speech.HideBubble();
            HtcFullSpeechBubblePatch.Restore(entity);

            if (TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[JanitorDebug] ROOM_WAIT_LOAD_BUBBLE_CLEANUP" +
                    " | janitor=" + CharacterName(entity) + ".");
            }
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

            if (!IsJanitorStagingArea(
                    floor,
                    candidate))
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

        private static bool IsPublicJanitorStagingRoom(Room room)
        {
            string id = RoomTypeId(room);
            return id == "ROOM_TYPE_CORRIDOR" ||
                   id == "ROOM_TYPE_WAITING" ||
                   id == "ROOM_TYPE_RECEPTION";
        }

        private static bool IsJanitorInRoomWallParkingTarget(Room room)
        {
            return IsPublicJanitorStagingRoom(room);
        }

        private static bool IsJanitorStagingArea(
            Floor floor,
            Vector2i position)
        {
            if (floor == null ||
                !RoomGeometry.IsInsideFloor(floor, position) ||
                floor.IsOutdoors(position) ||
                floor.m_mapPersistentData.m_foundationsLayer
                    .m_foundations[
                        position.m_x,
                        position.m_y] != 2)
            {
                return false;
            }

            Room room =
                floor.GetRoomTileSafe(
                    position.m_x,
                    position.m_y);

            // Unzoned indoor tiles are normal transit in HTC and can be used
            // like corridor / waiting / reception for temporary janitor
            // staging, provided the caller's physical/access checks pass.
            return room == null ||
                   IsPublicJanitorStagingRoom(room);
        }

        private static bool IsJanitorCartLocalAreaCompatible(
            Floor floor,
            Vector2i parkingPosition,
            Vector2i relatedPosition)
        {
            if (floor == null ||
                !RoomGeometry.IsInsideFloor(
                    floor,
                    parkingPosition) ||
                !RoomGeometry.IsInsideFloor(
                    floor,
                    relatedPosition))
            {
                return false;
            }

            Room parkingRoom =
                floor.GetRoomTileSafe(
                    parkingPosition.m_x,
                    parkingPosition.m_y);
            Room relatedRoom =
                floor.GetRoomTileSafe(
                    relatedPosition.m_x,
                    relatedPosition.m_y);

            if (parkingRoom != null &&
                ReferenceEquals(
                    parkingRoom,
                    relatedRoom))
            {
                return true;
            }

            return IsJanitorStagingArea(
                       floor,
                       parkingPosition) &&
                   IsJanitorStagingArea(
                       floor,
                       relatedPosition);
        }

        private static bool HasNearbyDoor(
            Floor floor,
            Vector2i position,
            int radius)
        {
            if (floor == null || radius < 0)
            {
                return false;
            }

            for (int x = position.m_x - radius;
                 x <= position.m_x + radius;
                 x++)
            {
                for (int y = position.m_y - radius;
                     y <= position.m_y + radius;
                     y++)
                {
                    if (x < 0 ||
                        y < 0 ||
                        x >= floor.Size.m_x ||
                        y >= floor.Size.m_y)
                    {
                        continue;
                    }

                    TileWalls walls = floor.GetWallsSafe(x, y);
                    if (walls != null &&
                        (walls.m_doorSW != null ||
                         walls.m_doorSE != null))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasDoorOnTileBoundary(
            Floor floor,
            Vector2i position)
        {
            if (floor == null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= floor.Size.m_x ||
                position.m_y >= floor.Size.m_y)
            {
                return false;
            }

            TileWalls current =
                floor.GetWallsSafe(position.m_x, position.m_y);
            TileWalls north =
                position.m_y + 1 < floor.Size.m_y
                    ? floor.GetWallsSafe(
                        position.m_x,
                        position.m_y + 1)
                    : null;
            TileWalls east =
                position.m_x + 1 < floor.Size.m_x
                    ? floor.GetWallsSafe(
                        position.m_x + 1,
                        position.m_y)
                    : null;

            return
                (current != null &&
                 (current.m_doorSW != null ||
                  current.m_doorSE != null)) ||
                (north != null &&
                 north.m_doorSW != null) ||
                (east != null &&
                 east.m_doorSE != null);
        }

        private static void GetProtectedRoomWaitDoorExposure(
            BehaviorJanitor janitor,
            Floor floor,
            Vector2i janitorTile,
            out int doorFrontRank,
            out int nearbyDoorRank)
        {
            doorFrontRank = 0;
            nearbyDoorRank = 0;
            AddDoorExposure(
                floor,
                janitorTile,
                1,
                ref doorFrontRank,
                ref nearbyDoorRank);

            if (!IsCartAttached(janitor) ||
                janitor.m_state == null ||
                janitor.m_state.m_cart == null)
            {
                return;
            }

            TileObject cart =
                janitor.m_state.m_cart.GetEntity();
            if (cart == null || cart.m_state == null)
            {
                return;
            }

            Vector2f janitorPosition =
                new Vector2f(janitorTile);
            Vector2f attachmentOffset =
                cart.GetCharacterAttachmentOffset();
            for (int i = 0;
                 i < DoorExposureCartDirections.Length;
                 i++)
            {
                Vector2f offset =
                    TileObject.TransformOffsetForDirection(
                        attachmentOffset,
                        DoorExposureCartDirections[i]);
                Vector2i cartTile =
                    janitorPosition.add(offset).ToVector2i();
                AddDoorExposure(
                    floor,
                    cartTile,
                    1,
                    ref doorFrontRank,
                    ref nearbyDoorRank);
            }
        }

        private static void AddDoorExposure(
            Floor floor,
            Vector2i tile,
            int nearbyRadius,
            ref int doorFrontRank,
            ref int nearbyDoorRank)
        {
            if (!RoomGeometry.IsInsideFloor(floor, tile))
            {
                return;
            }

            if (HasDoorOnTileBoundary(floor, tile))
            {
                doorFrontRank = 1;
            }

            if (HasNearbyDoor(
                    floor,
                    tile,
                    nearbyRadius))
            {
                nearbyDoorRank = 1;
            }
        }


        private static bool HasAdjacentWall(
            Floor floor,
            Vector2i position)
        {
            if (floor == null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= floor.Size.m_x ||
                position.m_y >= floor.Size.m_y)
            {
                return false;
            }

            TileWalls current =
                floor.GetWallsSafe(position.m_x, position.m_y);
            TileWalls north =
                position.m_y + 1 < floor.Size.m_y
                    ? floor.GetWallsSafe(
                        position.m_x,
                        position.m_y + 1)
                    : null;
            TileWalls east =
                position.m_x + 1 < floor.Size.m_x
                    ? floor.GetWallsSafe(
                        position.m_x + 1,
                        position.m_y)
                    : null;

            return
                (current != null &&
                 (current.m_wallSW != null ||
                  current.m_wallSE != null)) ||
                (north != null &&
                 north.m_wallSW != null) ||
                (east != null &&
                 east.m_wallSE != null);
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
            int bestDoorFrontRank = int.MaxValue;
            int bestNearbyDoorRank = int.MaxValue;
            int bestWallRank = int.MaxValue;
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
                    // Reject out-of-radius candidates without consulting
                    // room occupancy or pathfinding.
                    float dx = x - doorwayAnchor.m_x;
                    float dy = y - doorwayAnchor.m_y;
                    if (dx * dx + dy * dy >
                        ProtectedRoomWaitMaximumDoorDistance *
                        ProtectedRoomWaitMaximumDoorDistance ||
                        !IsValidProtectedRoomWaitTile(
                            janitor.m_entity, floor, room, candidate))
                    {
                        continue;
                    }

                    // Door clearance is more important than WALL > OPEN.
                    // Exact frontage checks both standing sides of every
                    // native door edge and also covers every possible
                    // attached bucket-cart footprint at this wait tile.
                    int doorFrontRank;
                    int nearDoorRank;
                    GetProtectedRoomWaitDoorExposure(
                        janitor,
                        floor,
                        candidate,
                        out doorFrontRank,
                        out nearDoorRank);
                    int wallRank =
                        HasAdjacentWall(floor, candidate) ? 0 : 1;

                    // Once a reachable winner exists, an inferior physical
                    // tier cannot win; avoid the exact route for that tile.
                    if (doorFrontRank > bestDoorFrontRank ||
                        (doorFrontRank == bestDoorFrontRank &&
                         nearDoorRank > bestNearbyDoorRank) ||
                        (doorFrontRank == bestDoorFrontRank &&
                         nearDoorRank == bestNearbyDoorRank &&
                         wallRank > bestWallRank))
                    {
                        continue;
                    }

                    // All remaining candidates still require a real route
                    // to validate their distance and admissibility.
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
                    if (doorFrontRank == bestDoorFrontRank &&
                        nearDoorRank == bestNearbyDoorRank &&
                        wallRank == bestWallRank &&
                        (score > bestScore ||
                         (score == bestScore &&
                          fromDoor > bestDoorDistance)))
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

                    if (doorFrontRank == bestDoorFrontRank &&
                        nearDoorRank == bestNearbyDoorRank &&
                        wallRank == bestWallRank &&
                        score == bestScore &&
                        fromDoor == bestDoorDistance &&
                        fromJanitor >= bestFromJanitor)
                    {
                        continue;
                    }

                    bestDoorFrontRank = doorFrontRank;
                    bestNearbyDoorRank = nearDoorRank;
                    bestWallRank = wallRank;
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
            ClearExteriorCartTravel(janitor);

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
            ClearExteriorCartTravel(janitor);
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
            ClearExteriorCartTravel(janitor);
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

        private static bool ReselectJanitorSkippingRoom(
            BehaviorJanitor janitor,
            Room room)
        {
            if (janitor == null ||
                janitor.m_entity == null ||
                janitor.m_state == null ||
                room == null ||
                room.m_roomPersistentData == null)
            {
                return ReselectJanitor(janitor);
            }

            // The 1.5.2.30 ThreadStatic selector skip was too dependent on
            // Harmony prefix ordering. Use the native room-reservation signal
            // instead: both vanilla room selectors already reject any room
            // whose m_reservedByCharacter is non-null.
            //
            // This reservation exists only for this synchronous re-selection
            // call. No frame/update can observe a janitor "abandoning" it.
            EntityIDPointer<Entity> previousReservation =
                room.m_roomPersistentData.m_reservedByCharacter;
            bool installedTemporaryReservation =
                previousReservation == null;

            if (installedTemporaryReservation)
            {
                room.m_roomPersistentData.m_reservedByCharacter =
                    janitor.m_entity;
            }

            bool selectedRoom = false;
            try
            {
                selectedRoom =
                    InvokeBool(
                        TryToSelectTileInARoomMethod,
                        janitor,
                        null);
            }
            finally
            {
                if (installedTemporaryReservation &&
                    room.m_roomPersistentData.m_reservedByCharacter != null &&
                    ReferenceEquals(
                        room.m_roomPersistentData
                            .m_reservedByCharacter.GetEntity(),
                        janitor.m_entity))
                {
                    room.m_roomPersistentData.m_reservedByCharacter = null;
                }
            }

            if (selectedRoom)
            {
                Room selected =
                    janitor.m_state.m_room == null
                        ? null
                        : janitor.m_state.m_room.GetEntity();

                // This should be impossible while the temporary native
                // reservation is installed. Fail safe if another patch
                // bypasses the reservation rule: do not let the same room
                // restart the cart-staging cycle.
                if (!ReferenceEquals(selected, room))
                {
                    return true;
                }

                ReleaseRoomReservation(janitor, room);
                janitor.m_state.m_room = null;
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
            Vector3i assignedCandidate = FindDirtiestRoomTile(
                behaviorJanitor,
                department,
                threshold,
                true);

            // During NIGHT, an occupied hospitalization room is only a
            // deferred fallback for ordinary dirt. Do not let an assigned
            // deferred room hide an ordinary non-deferred room that vanilla
            // will query immediately afterwards.
            if (!IsOrdinaryNightDeferredRoomCandidate(assignedCandidate))
            {
                return assignedCandidate;
            }

            Vector3i anyCandidate = FindDirtiestRoomTile(
                null,
                department,
                threshold,
                false);
            if (!IsZeroRoomCandidate(anyCandidate) &&
                !IsOrdinaryNightDeferredRoomCandidate(anyCandidate))
            {
                return new Vector3i(0, 0, 0);
            }

            return assignedCandidate;
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

        internal static bool ShouldYieldNightDeferredRoomToIndoorTile(
            BehaviorJanitor behaviorJanitor)
        {
            if (!TrafficControlConfig.ReduceOccupiedHospitalizationCleaningAtNight ||
                DayTime.Instance == null ||
                DayTime.Instance.GetShift() != Shift.NIGHT ||
                behaviorJanitor == null ||
                behaviorJanitor.m_state == null ||
                Hospital.Instance == null)
            {
                return false;
            }

            Department department = behaviorJanitor.GetDepartment();
            WalkComponent walk =
                behaviorJanitor.GetComponent<WalkComponent>();
            if (department == null ||
                department.m_departmentPersistentData == null ||
                walk == null ||
                walk.m_state == null)
            {
                return false;
            }

            int floorIndex = walk.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Vector3i assignedCandidate = FindDirtiestRoomTile(
                behaviorJanitor,
                department,
                10,
                true);
            bool assignedDeferred =
                IsOrdinaryNightDeferredRoomCandidate(assignedCandidate);
            if (!IsZeroRoomCandidate(assignedCandidate) &&
                !assignedDeferred)
            {
                if (IsNightOccupiedHospitalizationBloodCandidate(
                        assignedCandidate))
                {
                    LogNightHospitalizationPriorityDecision(
                        behaviorJanitor,
                        "select-next-action",
                        "BLOOD_PRIORITY",
                        assignedCandidate,
                        "none",
                        new Vector3i(0, 0, 0),
                        10);
                }
                return false;
            }

            Vector3i anyCandidate = FindDirtiestRoomTile(
                null,
                department,
                10,
                false);
            bool anyDeferred =
                IsOrdinaryNightDeferredRoomCandidate(anyCandidate);
            if (!IsZeroRoomCandidate(anyCandidate) &&
                !anyDeferred)
            {
                if (IsNightOccupiedHospitalizationBloodCandidate(
                        anyCandidate))
                {
                    LogNightHospitalizationPriorityDecision(
                        behaviorJanitor,
                        "select-next-action",
                        "BLOOD_PRIORITY",
                        anyCandidate,
                        assignedDeferred ? "deferred-room" : "none",
                        assignedDeferred
                            ? assignedCandidate
                            : new Vector3i(0, 0, 0),
                        10);
                }
                else if (assignedDeferred)
                {
                    LogNightHospitalizationPriorityDecision(
                        behaviorJanitor,
                        "select-next-action",
                        "YIELD_TO_NORMAL_ROOM",
                        assignedCandidate,
                        "room",
                        anyCandidate,
                        10);
                }
                return false;
            }

            if (!assignedDeferred && !anyDeferred)
            {
                return false;
            }

            Vector3i nightRoomCandidate =
                assignedDeferred ? assignedCandidate : anyCandidate;
            Vector2i indoorCandidate = FindClosestDirtyIndoorsTile(
                walk.GetCurrentTile(),
                floorIndex,
                10);
            bool yieldToIndoor = IsNonDeferredIndoorCandidate(
                indoorCandidate,
                floorIndex);

            LogNightHospitalizationPriorityDecision(
                behaviorJanitor,
                "select-next-action",
                yieldToIndoor
                    ? "YIELD_TO_INDOOR"
                    : "KEEP_DEFERRED_FALLBACK",
                nightRoomCandidate,
                "indoor",
                indoorCandidate == Vector2i.ZERO_VECTOR
                    ? new Vector3i(0, 0, 0)
                    : new Vector3i(
                        indoorCandidate.m_x,
                        indoorCandidate.m_y,
                        floorIndex),
                10);

            return yieldToIndoor;
        }

        internal static bool TrySelectNightPreferredIndoorTileAtHome(
            BehaviorJanitor janitor)
        {
            if (!TrafficControlConfig.ReduceOccupiedHospitalizationCleaningAtNight ||
                DayTime.Instance == null ||
                DayTime.Instance.GetShift() != Shift.NIGHT ||
                janitor == null ||
                janitor.m_entity == null ||
                janitor.m_state == null ||
                !janitor.m_state.m_finished ||
                janitor.m_state.m_timeInState <= 30f ||
                Hospital.Instance == null)
            {
                return false;
            }

            EmployeeComponent employee =
                janitor.GetComponent<EmployeeComponent>();
            WalkComponent walk =
                janitor.GetComponent<WalkComponent>();
            Department department = janitor.GetDepartment();
            if (employee == null ||
                employee.m_state == null ||
                walk == null ||
                walk.m_state == null ||
                department == null ||
                department.IsClosed() ||
                DayTime.Instance.GetShift() != employee.m_state.m_shift)
            {
                return false;
            }

            GameDBRoomType homeRoomType = employee.GetHomeRoomType();
            if (homeRoomType != null &&
                homeRoomType.HasTag("janitor_admin_workplace"))
            {
                return false;
            }

            // Vanilla AtHome recovery checks a department room at threshold 25
            // before its floor-wide indoor fallback. If that room is only an
            // ordinary NIGHT-deferred hospitalization fallback, give normal
            // work anywhere on the current floor the same threshold first.
            Vector3i roomCandidate = FindDirtiestRoomTile(
                null,
                department,
                25,
                false);
            if (!IsOrdinaryNightDeferredRoomCandidate(roomCandidate))
            {
                if (IsNightOccupiedHospitalizationBloodCandidate(
                        roomCandidate))
                {
                    LogNightHospitalizationPriorityDecision(
                        janitor,
                        "at-home-recovery",
                        "BLOOD_PRIORITY",
                        roomCandidate,
                        "none",
                        new Vector3i(0, 0, 0),
                        25);
                }
                return false;
            }

            int floorIndex = walk.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Vector2i indoorCandidate = FindClosestDirtyIndoorsTile(
                walk.GetCurrentTile(),
                floorIndex,
                25);
            if (!IsNonDeferredIndoorCandidate(
                    indoorCandidate,
                    floorIndex))
            {
                LogNightHospitalizationPriorityDecision(
                    janitor,
                    "at-home-recovery",
                    "KEEP_DEFERRED_FALLBACK",
                    roomCandidate,
                    "indoor",
                    indoorCandidate == Vector2i.ZERO_VECTOR
                        ? new Vector3i(0, 0, 0)
                        : new Vector3i(
                            indoorCandidate.m_x,
                            indoorCandidate.m_y,
                            floorIndex),
                    25);
                return false;
            }

            // Reuse the native selector for reservation, state transition,
            // destination and cleaning-time updates. The probe above only
            // changes ordering; it never reserves a second target.
            bool selected = InvokeBool(
                TryToSelectIndoorTileMethod,
                janitor,
                new object[] { 25 });

            LogNightHospitalizationPriorityDecision(
                janitor,
                "at-home-recovery",
                selected
                    ? "YIELD_TO_INDOOR"
                    : "INDOOR_SELECTION_FAILED",
                roomCandidate,
                "indoor",
                new Vector3i(
                    indoorCandidate.m_x,
                    indoorCandidate.m_y,
                    floorIndex),
                25);

            return selected;
        }

        private static void LogNightHospitalizationPriorityDecision(
            BehaviorJanitor janitor,
            string stage,
            string decision,
            Vector3i nightRoomCandidate,
            string alternativeKind,
            Vector3i alternativeCandidate,
            int threshold)
        {
            if (!TrafficControlConfig.JanitorCartDebug ||
                janitor == null ||
                janitor.m_entity == null)
            {
                return;
            }

            Room nightRoom = GetRoomForCandidate(nightRoomCandidate);
            Room alternativeRoom = GetRoomForCandidate(alternativeCandidate);

            Plugin.Log?.LogInfo(
                "[JanitorDebug] NIGHT_HOSPITALIZATION_PRIORITY" +
                " | janitor=" + CharacterName(janitor.m_entity) +
                " | stage=" + stage +
                " | decision=" + decision +
                " | threshold=" + threshold +
                " | nightRoomTile=" + nightRoomCandidate +
                " | nightRoom=" + RoomTypeId(nightRoom) +
                " | nightRoomDepartment=" +
                    DepartmentTypeId(nightRoom) +
                " | nightRoomFloor=" +
                    (nightRoom == null
                        ? nightRoomCandidate.m_z
                        : nightRoom.GetFloorIndex()) +
                " | nightRoomDirt=" +
                    CandidateDirtType(nightRoomCandidate) +
                " | nightRoomBedReservationPercent=" +
                    BedReservationPercent(nightRoom) +
                " | alternativeKind=" + alternativeKind +
                " | alternativeTile=" + alternativeCandidate +
                " | alternativeRoom=" + RoomTypeId(alternativeRoom) +
                " | alternativeDepartment=" +
                    DepartmentTypeId(alternativeRoom) +
                " | alternativeFloor=" +
                    (IsZeroRoomCandidate(alternativeCandidate)
                        ? -1
                        : alternativeRoom == null
                            ? alternativeCandidate.m_z
                            : alternativeRoom.GetFloorIndex()) +
                " | alternativeDirt=" +
                    CandidateDirtType(alternativeCandidate) +
                ".");
        }

        private static bool IsNightOccupiedHospitalizationBloodCandidate(
            Vector3i candidate)
        {
            Room room = GetRoomForCandidate(candidate);
            return room != null &&
                   ShouldDeferOrdinaryCleaning(room) &&
                   CandidateDirtType(candidate) == DirtType.BLOOD.ToString();
        }

        private static Room GetRoomForCandidate(Vector3i candidate)
        {
            if (IsZeroRoomCandidate(candidate) ||
                Hospital.Instance == null ||
                candidate.m_z < 0 ||
                candidate.m_z >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            Floor floor = Hospital.Instance.m_floors[candidate.m_z];
            if (candidate.m_x < 0 ||
                candidate.m_y < 0 ||
                candidate.m_x >= floor.Size.m_x ||
                candidate.m_y >= floor.Size.m_y)
            {
                return null;
            }

            return floor.m_roomTiles[candidate.m_x, candidate.m_y];
        }

        private static string CandidateDirtType(Vector3i candidate)
        {
            if (IsZeroRoomCandidate(candidate) ||
                Hospital.Instance == null ||
                candidate.m_z < 0 ||
                candidate.m_z >= Hospital.Instance.m_floors.Count)
            {
                return "<none>";
            }

            Floor floor = Hospital.Instance.m_floors[candidate.m_z];
            if (candidate.m_x < 0 ||
                candidate.m_y < 0 ||
                candidate.m_x >= floor.Size.m_x ||
                candidate.m_y >= floor.Size.m_y)
            {
                return "<none>";
            }

            return floor.m_mapPersistentData.m_tiles[
                candidate.m_x,
                candidate.m_y].m_dirtType.ToString();
        }

        private static string DepartmentTypeId(Room room)
        {
            Department department =
                room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_department == null
                    ? null
                    : room.m_roomPersistentData.m_department.GetEntity();
            GameDBDepartment departmentType =
                department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_departmentType == null
                    ? null
                    : department.m_departmentPersistentData.m_departmentType.Entry;

            return departmentType == null
                ? "<none>"
                : departmentType.DatabaseID.ToString();
        }

        private static float BedReservationPercent(Room room)
        {
            if (room == null ||
                Hospital.Instance == null)
            {
                return 0f;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return 0f;
            }

            return room.GetBedReservationPercent(
                Hospital.Instance.m_floors[floorIndex]);
        }

        private static bool IsZeroRoomCandidate(Vector3i candidate)
        {
            return candidate.m_x == 0 && candidate.m_y == 0;
        }

        private static bool IsOrdinaryNightDeferredRoomCandidate(
            Vector3i candidate)
        {
            if (IsZeroRoomCandidate(candidate) ||
                Hospital.Instance == null ||
                candidate.m_z < 0 ||
                candidate.m_z >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[candidate.m_z];
            if (candidate.m_x < 0 ||
                candidate.m_y < 0 ||
                candidate.m_x >= floor.Size.m_x ||
                candidate.m_y >= floor.Size.m_y)
            {
                return false;
            }

            Room room = floor.m_roomTiles[
                candidate.m_x, candidate.m_y];
            return ShouldDeferOrdinaryCleaning(room) &&
                floor.m_mapPersistentData.m_tiles[
                    candidate.m_x, candidate.m_y].m_dirtType !=
                    DirtType.BLOOD;
        }

        private static bool IsNonDeferredIndoorCandidate(
            Vector2i candidate,
            int floorIndex)
        {
            if (candidate == Vector2i.ZERO_VECTOR ||
                Hospital.Instance == null ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (candidate.m_x < 0 ||
                candidate.m_y < 0 ||
                candidate.m_x >= floor.Size.m_x ||
                candidate.m_y >= floor.Size.m_y)
            {
                return false;
            }

            if (floor.m_mapPersistentData.m_tiles[
                    candidate.m_x, candidate.m_y].m_dirtType ==
                DirtType.BLOOD)
            {
                return true;
            }

            return !ShouldDeferOrdinaryCleaning(
                floor.m_roomTiles[candidate.m_x, candidate.m_y]);
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

            bool avoidActiveProcedureRooms =
                TrafficControlConfig.AvoidCleaningActiveProcedureRooms;
            bool avoidOccupiedBathrooms =
                TrafficControlConfig.AvoidCleaningOccupiedBathrooms;
            bool nightDeferralActive =
                TrafficControlConfig.ReduceOccupiedHospitalizationCleaningAtNight &&
                DayTime.Instance != null &&
                DayTime.Instance.GetShift() == Shift.NIGHT;

            // Allocate only the per-call caches that can actually be used.
            Dictionary<Room, bool> procedureRoomCache =
                avoidActiveProcedureRooms
                    ? new Dictionary<Room, bool>()
                    : null;
            Dictionary<Room, BathroomCleaningTopology> bathroomTopologyCache =
                avoidOccupiedBathrooms
                    ? new Dictionary<Room, BathroomCleaningTopology>()
                    : null;
            Dictionary<Room, bool> nightCleaningCache =
                nightDeferralActive
                    ? new Dictionary<Room, bool>()
                    : null;

            int width = floor.Size.m_x;
            int height = floor.Size.m_y;
            var tiles = floor.m_mapPersistentData.m_tiles;
            var foundations =
                floor.m_mapPersistentData.m_foundationsLayer.m_foundations;
            var tileObjects = floor.m_tileObjects;
            var roomTiles = floor.m_roomTiles;

            // Vanilla gives BLOOD absolute priority. Keep the same full scan and
            // strict nearest-distance tie behavior, but reject farther tiles
            // before touching dirt/protection state.
            for (int x = 0; x < width; x++)
            {
                int dx = x - position.m_x;
                int dxSquared = dx * dx;

                for (int y = 0; y < height; y++)
                {
                    int dy = y - position.m_y;
                    int distance =
                        dxSquared + dy * dy;
                    if (distance >= closestDistance)
                    {
                        continue;
                    }

                    if (tiles[x, y].m_dirtLevel <= 0f ||
                        tiles[x, y].m_dirtType != DirtType.BLOOD ||
                        tiles[x, y].m_user != null ||
                        foundations[x, y] != 2 ||
                        tileObjects[x, y].IsAnyObjectBlocking())
                    {
                        continue;
                    }

                    Vector2i candidate =
                        new Vector2i(x, y);
                    if (ShouldAvoidCleaningTileCached(
                            roomTiles[x, y],
                            candidate,
                            procedureRoomCache,
                            bathroomTopologyCache))
                    {
                        continue;
                    }

                    closestDistance = distance;
                    selectedPosition = candidate;
                }
            }

            if (selectedPosition != Vector2i.ZERO_VECTOR)
            {
                return selectedPosition;
            }

            int deferredClosestDistance = int.MaxValue;
            Vector2i deferredPosition = Vector2i.ZERO_VECTOR;
            closestDistance = int.MaxValue;

            for (int x = 0; x < width; x++)
            {
                int dx = x - position.m_x;
                int dxSquared = dx * dx;

                for (int y = 0; y < height; y++)
                {
                    int dy = y - position.m_y;
                    int distance =
                        dxSquared + dy * dy;

                    // If this tile cannot improve either possible winner, none
                    // of the more expensive tile/room checks can affect the result.
                    if (distance >= closestDistance &&
                        (!nightDeferralActive ||
                         distance >= deferredClosestDistance))
                    {
                        continue;
                    }

                    if (tiles[x, y].m_dirtLevel <= (float)threshold ||
                        tiles[x, y].m_user != null ||
                        foundations[x, y] != 2 ||
                        tileObjects[x, y].IsAnyObjectBlocking())
                    {
                        continue;
                    }

                    Vector2i candidate =
                        new Vector2i(x, y);
                    Room room =
                        roomTiles[x, y];
                    if (ShouldAvoidCleaningTileCached(
                            room,
                            candidate,
                            procedureRoomCache,
                            bathroomTopologyCache))
                    {
                        continue;
                    }

                    if (ShouldDeferOrdinaryCleaningCached(
                            room,
                            nightCleaningCache,
                            nightDeferralActive))
                    {
                        if (distance < deferredClosestDistance)
                        {
                            deferredClosestDistance = distance;
                            deferredPosition = candidate;
                        }
                    }
                    else if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        selectedPosition = candidate;
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

        private static void RememberExteriorCartTravel(
            BehaviorJanitor janitor,
            Room room,
            Vector2i destination,
            int floorIndex)
        {
            if (janitor == null ||
                janitor.m_entity == null ||
                room == null ||
                destination == Vector2i.ZERO_VECTOR ||
                room.IsPositionInRoom(destination))
            {
                ClearExteriorCartTravel(janitor);
                return;
            }

            ParkedExteriorCarts.Remove(janitor.m_entity);
            ExteriorCartTravels[janitor.m_entity] =
                new ExteriorCartTravelState
                {
                    Room = room,
                    Destination = destination,
                    FloorIndex = floorIndex
                };
        }

        private static bool IsExpectedExteriorCartTravel(
            BehaviorJanitor janitor,
            Room room,
            WalkComponent walk)
        {
            if (janitor == null ||
                janitor.m_entity == null ||
                room == null ||
                walk == null ||
                walk.m_state == null)
            {
                return false;
            }

            ExteriorCartTravelState expected;
            if (!ExteriorCartTravels.TryGetValue(
                    janitor.m_entity, out expected))
            {
                return false;
            }

            Vector2i destination =
                ToTile(walk.m_state.m_destination);
            bool matches =
                ReferenceEquals(expected.Room, room) &&
                expected.FloorIndex ==
                    walk.m_state.m_destinationFloor &&
                expected.Destination == destination &&
                IsCartAttached(janitor);

            if (!matches)
            {
                ExteriorCartTravels.Remove(janitor.m_entity);
            }

            return matches;
        }

        private static void ClearExteriorCartTravel(
            BehaviorJanitor janitor)
        {
            if (janitor != null && janitor.m_entity != null)
            {
                ExteriorCartTravels.Remove(janitor.m_entity);
            }
        }

        internal static BehaviorJanitor PushJanitorCartDestinationContext(
            BehaviorJanitor janitor)
        {
            ClearExteriorCartTravel(janitor);
            BehaviorJanitor previous = s_cartDestinationJanitor;
            s_cartDestinationJanitor = janitor;
            return previous;
        }

        internal static void PopJanitorCartDestinationContext(
            BehaviorJanitor previous)
        {
            s_cartDestinationJanitor = previous;
        }

        internal static bool TrySelectJanitorCartDestination(
            Vector2i position,
            Room room,
            out Vector2i result)
        {
            return TrySelectJanitorCartDestination(
                s_cartDestinationJanitor,
                position,
                room,
                out result);
        }

        private static bool TrySelectJanitorCartDestination(
            BehaviorJanitor janitor,
            Vector2i position,
            Room room,
            out Vector2i result)
        {
            result = Vector2i.ZERO_VECTOR;
            if (janitor == null ||
                janitor.m_entity == null ||
                janitor.m_state == null ||
                janitor.m_state.m_cart == null ||
                janitor.m_state.m_cart.GetEntity() == null ||
                janitor.m_state.m_room == null ||
                room == null ||
                !ReferenceEquals(janitor.m_state.m_room.GetEntity(), room) ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType.Entry == null ||
                Hospital.Instance == null ||
                GridMap.GetInstance() == null ||
                room.GetFloorIndex() < 0 ||
                room.GetFloorIndex() >= Hospital.Instance.m_floors.Count ||
                (janitor.m_state.m_janitorState !=
                     BehaviorJanitorState.WalkingToCartToNextRoom &&
                 janitor.m_state.m_janitorState !=
                     BehaviorJanitorState.WalkingToNextRoom))
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];
            if (floor == null)
            {
                return false;
            }

            AccessRights accessRights = janitor.GetAccessRights();
            TileObject cart = janitor.m_state.m_cart.GetEntity();

            // Shared/public rooms are themselves the cleaning destination.
            // Vanilla FindClosest3x3Area(...) only prefers the closest clear
            // center and has no wall-parking notion. For these rooms HTC
            // keeps the cart inside the target room but chooses a real,
            // one-tile wall-side parking position.
            if (IsJanitorInRoomWallParkingTarget(room))
            {
                return TrySelectJanitorCartDestinationInsideSharedRoom(
                    janitor,
                    cart,
                    position,
                    room,
                    floor,
                    accessRights,
                    out result);
            }

            // Reuse the same topology principle as private bathrooms:
            // only room boundaries that are physically traversable count as
            // real exits. This avoids treating a neighbouring room behind a
            // wall as a place where the cart could be staged.
            HashSet<Vector2i> doorwayAnchors = new HashSet<Vector2i>();
            Vector2i bottom = room.m_roomPersistentData.m_positionBottom;
            Vector2i top = room.m_roomPersistentData.m_positionTop;

            for (int x = bottom.m_x; x <= top.m_x; x++)
            {
                for (int y = bottom.m_y; y <= top.m_y; y++)
                {
                    Vector2i inside = new Vector2i(x, y);
                    if (!room.IsPositionInRoom(inside))
                    {
                        continue;
                    }

                    TryAddJanitorCartDoorwayAnchor(
                        floor,
                        room,
                        inside,
                        new Vector2i(x - 1, y),
                        accessRights,
                        doorwayAnchors);
                    TryAddJanitorCartDoorwayAnchor(
                        floor,
                        room,
                        inside,
                        new Vector2i(x + 1, y),
                        accessRights,
                        doorwayAnchors);
                    TryAddJanitorCartDoorwayAnchor(
                        floor,
                        room,
                        inside,
                        new Vector2i(x, y - 1),
                        accessRights,
                        doorwayAnchors);
                    TryAddJanitorCartDoorwayAnchor(
                        floor,
                        room,
                        inside,
                        new Vector2i(x, y + 1),
                        accessRights,
                        doorwayAnchors);
                }
            }

            if (doorwayAnchors.Count == 0)
            {
                // No proven exterior connection: leave the native
                // FindClosest3x3Area(...) completely untouched.
                return false;
            }

            BathroomCleaningTopology bathroomTopology = null;
            if (TrafficControlConfig.AvoidCleaningOccupiedBathrooms &&
                IsBathroomRoom(room))
            {
                bathroomTopology =
                    BathroomCleaningTopology.Create(room);
            }

            GridMap gridMap = GridMap.GetInstance();
            int bestDoorFrontRank = int.MaxValue;
            int bestDoorRank = int.MaxValue;
            int bestWallRank = int.MaxValue;
            float bestScore = float.MaxValue;
            int bestDistance = int.MaxValue;

            foreach (Vector2i anchor in doorwayAnchors)
            {
                int minX = Math.Max(
                    0, anchor.m_x - JanitorCartExteriorRadius);
                int maxX = Math.Min(
                    floor.Size.m_x - 1,
                    anchor.m_x + JanitorCartExteriorRadius);
                int minY = Math.Max(
                    0, anchor.m_y - JanitorCartExteriorRadius);
                int maxY = Math.Min(
                    floor.Size.m_y - 1,
                    anchor.m_y + JanitorCartExteriorRadius);

                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        Vector2i candidate = new Vector2i(x, y);
                        if (room.IsPositionInRoom(candidate) ||
                            doorwayAnchors.Contains(candidate) ||
                            !IsJanitorStagingArea(
                                floor,
                                candidate) ||
                            !NavigationAccessPolicy.IsTileAccessible(
                                floor,
                                candidate,
                                accessRights) ||
                            (bathroomTopology != null &&
                             bathroomTopology.IsTileProtected(
                                 candidate)) ||
                            !IsJanitorCartParkingTileSafe(
                                janitor,
                                cart,
                                floor,
                                candidate))
                        {
                            continue;
                        }

                        int anchorDx =
                            candidate.m_x - anchor.m_x;
                        int anchorDy =
                            candidate.m_y - anchor.m_y;
                        int anchorDistanceSquared =
                            anchorDx * anchorDx +
                            anchorDy * anchorDy;
                        if (anchorDistanceSquared >
                            JanitorCartExteriorRadius *
                            JanitorCartExteriorRadius)
                        {
                            continue;
                        }

                        int doorFrontRank =
                            HasDoorOnTileBoundary(
                                floor,
                                candidate)
                                ? 1
                                : 0;
                        int doorRank =
                            HasNearbyDoor(
                                floor,
                                candidate,
                                JanitorCartDoorPreferenceRadius)
                                ? 1
                                : 0;
                        int wallRank =
                            HasAdjacentWall(
                                floor,
                                candidate)
                                ? 0
                                : 1;

                        // Door clearance outranks WALL > OPEN. A clear open
                        // staging tile beats a wall-side tile directly in
                        // front of either real side of a door.
                        if (doorFrontRank > bestDoorFrontRank ||
                            (doorFrontRank == bestDoorFrontRank &&
                             doorRank > bestDoorRank) ||
                            (doorFrontRank == bestDoorFrontRank &&
                             doorRank == bestDoorRank &&
                             wallRank > bestWallRank))
                        {
                            continue;
                        }

                        float lowerBound =
                            (float)Math.Sqrt(
                                anchorDistanceSquared);
                        if (doorFrontRank == bestDoorFrontRank &&
                            doorRank == bestDoorRank &&
                            wallRank == bestWallRank &&
                            lowerBound > bestScore)
                        {
                            continue;
                        }

                        float fromDoor = gridMap.GetDistance(
                            floor.m_floorIndex,
                            anchor,
                            floor.m_floorIndex,
                            candidate,
                            accessRights);
                        if (float.IsNaN(fromDoor) ||
                            fromDoor < 0f ||
                            fromDoor >
                                JanitorCartExteriorRadius + 2)
                        {
                            continue;
                        }

                        int distance =
                            (candidate.m_x - position.m_x) *
                            (candidate.m_x - position.m_x) +
                            (candidate.m_y - position.m_y) *
                            (candidate.m_y - position.m_y);
                        float score = fromDoor;

                        if (doorFrontRank > bestDoorFrontRank ||
                            (doorFrontRank == bestDoorFrontRank &&
                             (doorRank > bestDoorRank ||
                              (doorRank == bestDoorRank &&
                               (wallRank > bestWallRank ||
                                (wallRank == bestWallRank &&
                                 (score > bestScore ||
                                  (score == bestScore &&
                                   distance >= bestDistance))))))))
                        {
                            continue;
                        }

                        bestDoorFrontRank = doorFrontRank;
                        bestDoorRank = doorRank;
                        bestWallRank = wallRank;
                        bestScore = score;
                        bestDistance = distance;
                        result = candidate;
                    }
                }
            }

            // No safe exterior cart tile is a compatibility fallback, not an
            // error: return false so the original Vanilla interior selector
            // runs exactly as before.
            if (result == Vector2i.ZERO_VECTOR)
            {
                return false;
            }

            RememberExteriorCartTravel(
                janitor,
                room,
                result,
                room.GetFloorIndex());
            return true;
        }

        private static bool TrySelectJanitorCartDestinationInsideSharedRoom(
            BehaviorJanitor janitor,
            TileObject cart,
            Vector2i position,
            Room room,
            Floor floor,
            AccessRights accessRights,
            out Vector2i result)
        {
            result = Vector2i.ZERO_VECTOR;
            if (janitor == null ||
                cart == null ||
                room == null ||
                floor == null ||
                room.m_roomPersistentData == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            WalkComponent walk =
                janitor.m_entity.GetComponent<WalkComponent>();
            int originFloor =
                walk == null
                    ? room.GetFloorIndex()
                    : walk.GetFloorIndex();
            GridMap gridMap = GridMap.GetInstance();

            int bestDoorFrontRank = int.MaxValue;
            int bestDoorRank = int.MaxValue;
            int bestWallRank = int.MaxValue;
            float bestRoute = float.MaxValue;
            int bestDistance = int.MaxValue;

            Vector2i bottom =
                room.m_roomPersistentData.m_positionBottom;
            Vector2i top =
                room.m_roomPersistentData.m_positionTop;

            for (int x = bottom.m_x; x <= top.m_x; x++)
            {
                for (int y = bottom.m_y; y <= top.m_y; y++)
                {
                    Vector2i candidate =
                        new Vector2i(x, y);
                    if (!room.IsPositionInRoom(candidate) ||
                        !NavigationAccessPolicy.IsTileAccessible(
                            floor,
                            candidate,
                            accessRights) ||
                        !IsJanitorCartParkingTileSafe(
                            janitor,
                            cart,
                            floor,
                            candidate))
                    {
                        continue;
                    }

                    int doorFrontRank =
                        HasDoorOnTileBoundary(
                            floor,
                            candidate)
                            ? 1
                            : 0;
                    int doorRank =
                        HasNearbyDoor(
                            floor,
                            candidate,
                            JanitorCartDoorPreferenceRadius)
                            ? 1
                            : 0;
                    int wallRank =
                        HasAdjacentWall(floor, candidate)
                            ? 0
                            : 1;

                    if (doorFrontRank > bestDoorFrontRank ||
                        (doorFrontRank == bestDoorFrontRank &&
                         doorRank > bestDoorRank) ||
                        (doorFrontRank == bestDoorFrontRank &&
                         doorRank == bestDoorRank &&
                         wallRank > bestWallRank))
                    {
                        continue;
                    }

                    int distance =
                        (candidate.m_x - position.m_x) *
                        (candidate.m_x - position.m_x) +
                        (candidate.m_y - position.m_y) *
                        (candidate.m_y - position.m_y);

                    float route =
                        gridMap.GetDistance(
                            originFloor,
                            position,
                            room.GetFloorIndex(),
                            candidate,
                            accessRights);
                    if (float.IsNaN(route) ||
                        route < 0f ||
                        route >= float.MaxValue)
                    {
                        continue;
                    }

                    if (doorFrontRank > bestDoorFrontRank ||
                        (doorFrontRank == bestDoorFrontRank &&
                         (doorRank > bestDoorRank ||
                          (doorRank == bestDoorRank &&
                           (wallRank > bestWallRank ||
                            (wallRank == bestWallRank &&
                             (route > bestRoute ||
                              (route == bestRoute &&
                               distance >= bestDistance))))))))
                    {
                        continue;
                    }

                    bestDoorFrontRank = doorFrontRank;
                    bestDoorRank = doorRank;
                    bestWallRank = wallRank;
                    bestRoute = route;
                    bestDistance = distance;
                    result = candidate;
                }
            }

            return result != Vector2i.ZERO_VECTOR;
        }

        private static void TryAddJanitorCartDoorwayAnchor(
            Floor floor,
            Room room,
            Vector2i inside,
            Vector2i outside,
            AccessRights accessRights,
            HashSet<Vector2i> doorwayAnchors)
        {
            if (floor == null ||
                room == null ||
                doorwayAnchors == null ||
                !RoomGeometry.IsInsideFloor(floor, outside) ||
                room.IsPositionInRoom(outside) ||
                !IsJanitorStagingArea(
                    floor,
                    outside) ||
                !NavigationAccessPolicy.IsTileAccessible(
                    floor,
                    outside,
                    accessRights) ||
                !RoomGeometry.IsBoundaryTraversable(
                    floor,
                    inside,
                    outside))
            {
                return;
            }

            doorwayAnchors.Add(outside);
        }

        private static bool IsJanitorCartParkingTileSafe(
            BehaviorJanitor janitor,
            TileObject cart,
            Floor floor,
            Vector2i position)
        {
            if (janitor == null ||
                janitor.m_entity == null ||
                cart == null ||
                cart.m_state == null ||
                cart.m_state.m_gameDBObject == null ||
                cart.m_state.m_gameDBObject.Entry == null ||
                floor == null ||
                MapScriptInterface.Instance == null ||
                GridMap.GetInstance() == null ||
                !RoomGeometry.IsInsideFloor(floor, position) ||
                !GridMap.GetInstance().IsInGrid(
                    position,
                    floor.m_floorIndex))
            {
                return false;
            }

            TileObjects objects =
                floor.m_tileObjects[
                    position.m_x,
                    position.m_y];
            if (objects == null ||
                objects.HasAnyObject() ||
                floor.m_mapPersistentData.m_foundationsLayer
                    .m_foundations[
                        position.m_x,
                        position.m_y] != 2 ||
                floor.m_accessibility[
                    position.m_x,
                    position.m_y] == 2)
            {
                return false;
            }

            // CanMoveObject(...) evaluates the object's CURRENT floor. When
            // the janitor is about to carry the cart to another floor, only
            // validate the target tile here; Vanilla will move the attached
            // object across floors with the character before the final drop.
            if (cart.GetFloorIndex() == floor.m_floorIndex &&
                !MapScriptInterface.Instance.CanMoveObject(
                    cart,
                    position))
            {
                return false;
            }

            Entity tileUser =
                floor.m_mapPersistentData
                    .m_tiles[
                        position.m_x,
                        position.m_y]
                    .m_user == null
                        ? null
                        : floor.m_mapPersistentData
                            .m_tiles[
                                position.m_x,
                                position.m_y]
                            .m_user.GetEntity();
            if (tileUser != null &&
                tileUser != janitor.m_entity)
            {
                return false;
            }

            Direction safeDirection;
            return TryFindJanitorCartDropDirection(
                janitor,
                cart,
                floor,
                position,
                Direction.None,
                out safeDirection);
        }

        private static bool TryFindJanitorCartDropDirection(
            BehaviorJanitor janitor,
            TileObject cart,
            Floor floor,
            Vector2i janitorPosition,
            Direction preferredDirection,
            out Direction direction)
        {
            direction = Direction.None;
            if (janitor == null ||
                janitor.m_entity == null ||
                cart == null ||
                floor == null)
            {
                return false;
            }

            if (preferredDirection != Direction.None &&
                IsJanitorCartDropDirectionSafe(
                    janitor,
                    cart,
                    floor,
                    janitorPosition,
                    preferredDirection))
            {
                direction = preferredDirection;
                return true;
            }

            Direction[] directions =
                new Direction[]
                {
                    Direction.NW,
                    Direction.NE,
                    Direction.SE,
                    Direction.SW
                };

            for (int i = 0; i < directions.Length; i++)
            {
                Direction candidate = directions[i];
                if (candidate == preferredDirection)
                {
                    continue;
                }

                if (IsJanitorCartDropDirectionSafe(
                        janitor,
                        cart,
                        floor,
                        janitorPosition,
                        candidate))
                {
                    direction = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsJanitorCartDropDirectionSafe(
            BehaviorJanitor janitor,
            TileObject cart,
            Floor floor,
            Vector2i janitorPosition,
            Direction direction)
        {
            if (janitor == null ||
                janitor.m_entity == null ||
                cart == null ||
                floor == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            Vector2f offset =
                TileObject.TransformOffsetForDirection(
                    cart.GetCharacterAttachmentOffset(),
                    direction);
            Vector2i attachedCartTile =
                new Vector2f(janitorPosition)
                    .add(offset)
                    .ToVector2i();

            if (!RoomGeometry.IsInsideFloor(
                    floor,
                    attachedCartTile) ||
                !IsJanitorCartLocalAreaCompatible(
                    floor,
                    janitorPosition,
                    attachedCartTile) ||
                floor.m_tileObjects[
                    attachedCartTile.m_x,
                    attachedCartTile.m_y].HasAnyObject() ||
                !floor.IsAccessibleIgnoreDoors(
                    janitorPosition,
                    attachedCartTile) ||
                !NavigationAccessPolicy.IsTileAccessible(
                    floor,
                    attachedCartTile,
                    janitor.GetAccessRights()) ||
                !GridMap.GetInstance().IsInGrid(
                    attachedCartTile,
                    floor.m_floorIndex))
            {
                return false;
            }

            Entity tileUser =
                floor.m_mapPersistentData
                    .m_tiles[
                        attachedCartTile.m_x,
                        attachedCartTile.m_y]
                    .m_user == null
                        ? null
                        : floor.m_mapPersistentData
                            .m_tiles[
                                attachedCartTile.m_x,
                                attachedCartTile.m_y]
                            .m_user.GetEntity();
            if (tileUser != null &&
                tileUser != janitor.m_entity)
            {
                return false;
            }

            Direction parkedOrientation =
                cart.TransformAttachmentOrientation(direction);
            return parkedOrientation != Direction.None &&
                IsJanitorCartPickupReachable(
                    janitor,
                    cart,
                    floor,
                    janitorPosition,
                    parkedOrientation);
        }

        private static bool TryFindJanitorCartParkingOrientation(
            BehaviorJanitor janitor,
            TileObject cart,
            Floor floor,
            Vector2i placement,
            Direction preferredOrientation,
            out Direction orientation)
        {
            orientation = Direction.None;
            if (janitor == null ||
                cart == null ||
                floor == null)
            {
                return false;
            }

            if (preferredOrientation != Direction.None &&
                IsJanitorCartPickupReachable(
                    janitor,
                    cart,
                    floor,
                    placement,
                    preferredOrientation))
            {
                orientation = preferredOrientation;
                return true;
            }

            Direction[] orientations =
                new Direction[]
                {
                    Direction.NW,
                    Direction.NE,
                    Direction.SE,
                    Direction.SW
                };

            for (int i = 0; i < orientations.Length; i++)
            {
                Direction candidate = orientations[i];
                if (candidate == preferredOrientation)
                {
                    continue;
                }

                if (IsJanitorCartPickupReachable(
                        janitor,
                        cart,
                        floor,
                        placement,
                        candidate))
                {
                    orientation = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsJanitorCartPickupReachable(
            BehaviorJanitor janitor,
            TileObject cart,
            Floor floor,
            Vector2i placement,
            Direction orientation)
        {
            if (janitor == null ||
                cart == null ||
                floor == null ||
                cart.m_state == null ||
                cart.m_state.m_gameDBObject == null ||
                cart.m_state.m_gameDBObject.Entry == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            Vector2i[] accessPositions =
                cart.m_state.m_gameDBObject.Entry.AccessPositions;
            if (accessPositions == null ||
                accessPositions.Length == 0)
            {
                accessPositions =
                    new Vector2i[]
                    {
                        new Vector2i(-1, 0)
                    };
            }

            AccessRights accessRights =
                janitor.GetAccessRights();

            for (int i = 0; i < accessPositions.Length; i++)
            {
                Vector2i accessTile =
                    placement +
                    TileObject.TransformUsePositionForDirection(
                        accessPositions[i],
                        orientation);

                if (!RoomGeometry.IsInsideFloor(
                        floor,
                        accessTile) ||
                    !IsJanitorCartLocalAreaCompatible(
                        floor,
                        placement,
                        accessTile) ||
                    floor.IsAnyBlockingObjectAt(
                        accessTile) ||
                    !floor.IsAccessibleIgnoreDoors(
                        accessTile,
                        placement) ||
                    !NavigationAccessPolicy.IsTileAccessible(
                        floor,
                        accessTile,
                        accessRights) ||
                    !GridMap.GetInstance().IsInGrid(
                        accessTile,
                        floor.m_floorIndex))
                {
                    continue;
                }

                float route =
                    GridMap.GetInstance().GetDistance(
                        floor.m_floorIndex,
                        placement,
                        floor.m_floorIndex,
                        accessTile,
                        accessRights);
                if (!float.IsNaN(route) &&
                    route >= 0f &&
                    route < float.MaxValue)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RememberParkedExteriorCart(
            BehaviorJanitor janitor,
            Room room,
            Vector2i position,
            int floorIndex)
        {
            if (janitor == null ||
                janitor.m_entity == null ||
                room == null ||
                position == Vector2i.ZERO_VECTOR)
            {
                return;
            }

            ParkedExteriorCarts[janitor.m_entity] =
                new ParkedExteriorCartState
                {
                    Room = room,
                    Position = position,
                    FloorIndex = floorIndex
                };
        }

        private static void ClearParkedExteriorCart(
            BehaviorJanitor janitor)
        {
            if (janitor != null && janitor.m_entity != null)
            {
                ParkedExteriorCarts.Remove(janitor.m_entity);
            }
        }

        internal static bool TryReuseParkedExteriorCartForCurrentRoom(
            BehaviorJanitor janitor)
        {
            if (janitor == null ||
                janitor.m_entity == null ||
                janitor.m_state == null ||
                janitor.m_state.m_janitorState !=
                    BehaviorJanitorState.WalkingToCartToNextRoom ||
                janitor.m_state.m_cart == null ||
                janitor.m_state.m_room == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return false;
            }

            WalkComponent walk =
                janitor.GetComponent<WalkComponent>();
            if (walk == null || walk.IsBusy())
            {
                return false;
            }

            Room room = janitor.m_state.m_room.GetEntity();
            TileObject cart = janitor.m_state.m_cart.GetEntity();
            ParkedExteriorCartState parked;
            if (room == null ||
                cart == null ||
                cart.m_state == null ||
                !ParkedExteriorCarts.TryGetValue(
                    janitor.m_entity, out parked))
            {
                return false;
            }

            if (!ReferenceEquals(parked.Room, room) ||
                parked.FloorIndex != room.GetFloorIndex() ||
                cart.GetFloorIndex() != parked.FloorIndex ||
                cart.m_state.m_position != parked.Position ||
                cart.IsAttachedToCharacter() ||
                cart.m_state.m_moving ||
                room.IsPositionInRoom(parked.Position) ||
                parked.FloorIndex < 0 ||
                parked.FloorIndex >= Hospital.Instance.m_floors.Count)
            {
                ClearParkedExteriorCart(janitor);
                return false;
            }

            Floor floor =
                Hospital.Instance.m_floors[parked.FloorIndex];
            if (floor == null ||
                !IsJanitorStagingArea(floor, parked.Position) ||
                !IsJanitorCartPickupReachable(
                    janitor,
                    cart,
                    floor,
                    parked.Position,
                    cart.Orientation))
            {
                ClearParkedExteriorCart(janitor);
                return false;
            }

            // The cart is already exactly where HTC deliberately parked it for
            // this room. Do not PickUpObject -> pathfind -> MoveObject again.
            // Continue with the native current-room tile selector so cleaning
            // time, skill progression and destination semantics stay vanilla.
            janitor.m_state.m_cartAvailable = true;
            bool forceTileSelection =
                janitor.m_state.m_forceTileSelection;
            if (InvokeBool(
                    TryToSelectTileInCurrentRoomMethod,
                    janitor,
                    null))
            {
                if (TrafficControlConfig.JanitorCartDebug)
                {
                    Plugin.Log?.LogInfo(
                        "[JanitorDebug] CART_STAGING_REUSE" +
                        " | janitor=" +
                            CharacterName(janitor.m_entity) +
                        " | roomType=" + RoomTypeId(room) +
                        " | floor=" + parked.FloorIndex +
                        " | cartTile=" + parked.Position +
                        " | action=continue-current-room.");
                }
                return true;
            }

            // A DLC assignment refresh deliberately forces one room
            // reselection. Preserve that native behavior, but still keep the
            // already-valid cart parked instead of moving it first.
            if (forceTileSelection)
            {
                ReselectJanitor(janitor);
                Room refreshedRoom =
                    janitor.m_state.m_room == null
                        ? null
                        : janitor.m_state.m_room.GetEntity();
                if (!ReferenceEquals(refreshedRoom, room))
                {
                    ClearParkedExteriorCart(janitor);
                }

                if (TrafficControlConfig.JanitorCartDebug)
                {
                    Plugin.Log?.LogInfo(
                        "[JanitorDebug] CART_STAGING_REUSE" +
                        " | janitor=" +
                            CharacterName(janitor.m_entity) +
                        " | roomType=" + RoomTypeId(room) +
                        " | floor=" + parked.FloorIndex +
                        " | cartTile=" + parked.Position +
                        " | action=assignment-refresh.");
                }
                return true;
            }

            // No currently cleanable tile remains in this room. Exclude only
            // this room from the immediate synchronous room-selection pass;
            // other rooms and the native indoor fallback remain available.
            // The room is eligible again on the next normal decision cycle.
            ReselectJanitorSkippingRoom(janitor, room);
            ClearParkedExteriorCart(janitor);
            if (TrafficControlConfig.JanitorCartDebug)
            {
                Plugin.Log?.LogInfo(
                    "[JanitorDebug] CART_STAGING_REUSE" +
                    " | janitor=" +
                        CharacterName(janitor.m_entity) +
                    " | roomType=" + RoomTypeId(room) +
                    " | floor=" + parked.FloorIndex +
                    " | cartTile=" + parked.Position +
                    " | action=no-cleanable-tile-reselect" +
                    " | resultRoom=" +
                        RoomTypeId(
                            janitor.m_state.m_room == null
                                ? null
                                : janitor.m_state.m_room.GetEntity()) +
                    " | resultState=" +
                        janitor.m_state.m_janitorState +
                    ".");
            }
            return true;
        }

        internal static void PrepareExteriorJanitorCartParking(
            BehaviorJanitor janitor)
        {
            if (janitor == null ||
                janitor.m_entity == null ||
                janitor.m_state == null ||
                janitor.m_state.m_cart == null ||
                janitor.m_state.m_room == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return;
            }

            WalkComponent walk =
                janitor.m_entity.GetComponent<WalkComponent>();
            TileObject cart =
                janitor.m_state.m_cart.GetEntity();
            Room targetRoom =
                janitor.m_state.m_room.GetEntity();
            if (walk == null ||
                walk.IsBusy() ||
                cart == null ||
                targetRoom == null ||
                cart.m_state == null ||
                !cart.IsAttachedToCharacter() ||
                walk.GetFloorIndex() < 0 ||
                walk.GetFloorIndex() >=
                    Hospital.Instance.m_floors.Count)
            {
                return;
            }

            Floor floor =
                Hospital.Instance.m_floors[
                    walk.GetFloorIndex()];
            Vector2i placement =
                walk.GetCurrentTile();
            Room placementRoom =
                floor == null
                    ? null
                    : floor.GetRoomTileSafe(
                        placement.m_x,
                        placement.m_y);
            bool sharedRoomParking =
                IsJanitorInRoomWallParkingTarget(targetRoom) &&
                ReferenceEquals(placementRoom, targetRoom);
            bool exteriorParking =
                !targetRoom.IsPositionInRoom(placement) &&
                IsJanitorStagingArea(
                    floor,
                    placement);

            if (floor == null ||
                (!sharedRoomParking &&
                 !exteriorParking))
            {
                return;
            }

            AnimModelComponent animation =
                janitor.m_entity.GetComponent<AnimModelComponent>();
            Direction preferredDirection =
                animation == null ||
                animation.m_state == null
                    ? Direction.None
                    : animation.m_state.m_direction;
            Direction safeDirection;
            if (!TryFindJanitorCartDropDirection(
                    janitor,
                    cart,
                    floor,
                    placement,
                    preferredDirection,
                    out safeDirection) ||
                safeDirection == Direction.None)
            {
                return;
            }

            if (animation != null &&
                animation.m_state != null &&
                animation.m_state.m_direction != safeDirection)
            {
                animation.SetDirection(safeDirection);
            }

            // BehaviorJanitor will call MoveObject(...) immediately after this
            // prefix. Set the same orientation TileObject.Update would derive
            // from the chosen character direction so Vanilla parks the cart
            // with its pickup side facing a structurally reachable tile.
            cart.m_state.m_moveOrientation =
                cart.TransformAttachmentOrientation(
                    safeDirection);
        }

        internal static void FinalizeExteriorJanitorCartParking(
            BehaviorJanitor janitor)
        {
            if (janitor == null ||
                janitor.m_state == null)
            {
                return;
            }

            if (janitor.m_state.m_cart == null ||
                janitor.m_state.m_room == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                ClearExteriorCartTravel(janitor);
                return;
            }

            TileObject cart =
                janitor.m_state.m_cart.GetEntity();
            Room targetRoom =
                janitor.m_state.m_room.GetEntity();
            if (cart == null ||
                targetRoom == null ||
                cart.m_state == null)
            {
                ClearExteriorCartTravel(janitor);
                return;
            }

            if (cart.IsAttachedToCharacter())
            {
                return;
            }

            ExteriorCartTravelState expectedTravel;
            bool hadExpectedExteriorTravel =
                janitor.m_entity != null &&
                ExteriorCartTravels.TryGetValue(
                    janitor.m_entity,
                    out expectedTravel) &&
                ReferenceEquals(expectedTravel.Room, targetRoom) &&
                expectedTravel.FloorIndex == cart.GetFloorIndex() &&
                expectedTravel.Destination == cart.m_state.m_position;

            ClearExteriorCartTravel(janitor);

            if (cart.GetFloorIndex() < 0 ||
                cart.GetFloorIndex() >=
                    Hospital.Instance.m_floors.Count)
            {
                return;
            }

            Vector2i placement =
                cart.m_state.m_position;
            Floor floor =
                Hospital.Instance.m_floors[
                    cart.GetFloorIndex()];
            Room placementRoom =
                floor == null
                    ? null
                    : floor.GetRoomTileSafe(
                        placement.m_x,
                        placement.m_y);
            bool sharedRoomParking =
                IsJanitorInRoomWallParkingTarget(targetRoom) &&
                ReferenceEquals(placementRoom, targetRoom);
            bool exteriorParking =
                !targetRoom.IsPositionInRoom(placement) &&
                IsJanitorStagingArea(
                    floor,
                    placement);

            if (floor == null ||
                (!sharedRoomParking &&
                 !exteriorParking))
            {
                return;
            }

            if (hadExpectedExteriorTravel && exteriorParking)
            {
                RememberParkedExteriorCart(
                    janitor,
                    targetRoom,
                    placement,
                    cart.GetFloorIndex());
            }

            Direction safeOrientation;
            if (!TryFindJanitorCartParkingOrientation(
                    janitor,
                    cart,
                    floor,
                    placement,
                    cart.Orientation,
                    out safeOrientation) ||
                safeOrientation == Direction.None ||
                safeOrientation == cart.Orientation)
            {
                return;
            }

            // MoveObject(...) already completed natively. Only rotate the
            // parked one-tile cart so its real pickup side stays reachable.
            cart.Orientation = safeOrientation;
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

            // Exterior staging is validated on the bucket cart's real
            // one-tile placement and pickup access. Keep the 3x3 WC protection
            // only for Vanilla interior destinations.
            if (!room.IsPositionInRoom(selected))
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
            Dictionary<Room, bool> nightCleaningCache,
            bool nightDeferralActive)
        {
            if (!nightDeferralActive || room == null)
            {
                return false;
            }

            bool deferred;
            if (nightCleaningCache != null &&
                nightCleaningCache.TryGetValue(room, out deferred))
            {
                return deferred;
            }

            deferred =
                ShouldDeferOrdinaryCleaningDuringActiveNight(room);

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
                DayTime.Instance.GetShift() != Shift.NIGHT)
            {
                return false;
            }

            return ShouldDeferOrdinaryCleaningDuringActiveNight(room);
        }

        private static bool ShouldDeferOrdinaryCleaningDuringActiveNight(
            Room room)
        {
            if (!IsHospitalizationRoom(room))
            {
                return false;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
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
            if (DeferredProtectedRooms.Count != 0 &&
                IsProtectedRoomDeferred(room))
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
            return HasActiveProcedure(
                room,
                null,
                false);
        }

        private static bool HasActiveProcedure(
            Room room,
            Entity reservationOwner,
            bool reservationOwnerResolved)
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

            Entity procedureOwner =
                GetCurrentProcedureOwner(room);
            if (!reservationOwnerResolved)
            {
                reservationOwner =
                    GetRoomReservationOwner(room);
            }

            // Native room ownership/reservation always wins while it exists.
            if (procedureOwner != null ||
                reservationOwner is ProcedureScript)
            {
                return true;
            }

            // Direct source of truth after the cheap room exclusions below: if a
            // patient is physically inside this candidate clinical room, routine
            // cleaning must wait.
            if (!CanCheckDirectPatientOccupancy(room))
            {
                return false;
            }

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
