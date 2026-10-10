using System;
using System.Collections;
using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal sealed class AccessZoneRecoveryJobData
    {
        internal readonly int FloorIndex;
        internal readonly int TemporaryAccessLevel;
        internal readonly HashSet<Vector2i> AllowedForbiddenTiles;

        internal AccessZoneRecoveryJobData(
            int floorIndex,
            int temporaryAccessLevel,
            HashSet<Vector2i> allowedForbiddenTiles)
        {
            FloorIndex = floorIndex;
            TemporaryAccessLevel = temporaryAccessLevel;
            AllowedForbiddenTiles = allowedForbiddenTiles;
        }
    }

    internal sealed class DeferredMovementRequest
    {
        internal Vector2f Destination;
        internal int DestinationFloor;
        internal MovementType MovementType;
        internal bool GoSit;
        internal EntityIDPointer<TileObject> ObjectToSitOn;
    }

    internal sealed class AccessZoneRecoveryEntry
    {
        internal int FloorIndex;
        internal int TemporaryAccessLevel;
        internal HashSet<Vector2i> AllowedForbiddenTiles;
        internal bool TemporaryExit;
        internal bool FinishBladderSequenceBeforeExit;
        internal bool BladderContinuationRepathAttempted;
        internal Vector2f OriginalDestination;
        internal int OriginalDestinationFloor;
        internal WalkState OriginalWalkState;
        internal MovementType OriginalMovementType;
        internal EntityIDPointer<TileObject> OriginalObjectToSitOn;
    }

    internal static class AccessZoneRecoveryManager
    {
        private static readonly Dictionary<WalkComponent, AccessZoneRecoveryEntry> Active =
            new Dictionary<WalkComponent, AccessZoneRecoveryEntry>();

        private static readonly Dictionary<WalkComponent, DeferredMovementRequest>
            DeferredMovements =
                new Dictionary<WalkComponent, DeferredMovementRequest>();

        private static readonly HashSet<WalkComponent> RecoveryMovement =
            new HashSet<WalkComponent>();

        // Main-thread only. A navigation change must not interrupt an ongoing
        // native UseComponent animation (STARTING / USING / FINISHING).
        private static readonly HashSet<WalkComponent> PendingPhysicalExit =
            new HashSet<WalkComponent>();

        private static readonly Vector2i[] CardinalDirections =
        {
            new Vector2i(-1, 0),
            new Vector2i(1, 0),
            new Vector2i(0, -1),
            new Vector2i(0, 1)
        };

        internal static bool IsRecovering(WalkComponent walk)
        {
            return walk != null &&
                   (Active.ContainsKey(walk) ||
                    PendingPhysicalExit.Contains(walk));
        }

        internal static bool IsCurrentTileRestricted(WalkComponent walk)
        {
            if (walk == null ||
                walk.m_state == null ||
                walk.Floor == null ||
                !CharacterAccess.CanBeRestrictedByAccessChange(walk))
            {
                return false;
            }

            Entity entity = CharacterAccess.GetEntity(walk);
            Behavior behavior = entity == null ? null : entity.GetComponent<Behavior>();
            if (behavior == null)
            {
                return false;
            }

            return !IsTileLegal(
                walk.Floor,
                walk.GetCurrentTileSafe(),
                behavior.GetAccessRights());
        }

        internal static void Reset()
        {
            foreach (
                KeyValuePair<WalkComponent, DeferredMovementRequest> pair
                in DeferredMovements)
            {
                ReleaseDeferredMovementReservation(pair.Key, pair.Value);
            }

            Active.Clear();
            PendingPhysicalExit.Clear();
            DeferredMovements.Clear();
            RecoveryMovement.Clear();
            WaitingRoomDiagnostics.Reset();
            AccessZoneRecoveryTracker.Reset();
        }

        internal static void UpdatePendingPhysicalExits()
        {
            if (PendingPhysicalExit.Count == 0 && Active.Count == 0)
            {
                return;
            }

            HashSet<Floor> readyFloors = new HashSet<Floor>();

            if (PendingPhysicalExit.Count > 0)
            {
                List<WalkComponent> pending =
                    new List<WalkComponent>(PendingPhysicalExit);

                foreach (WalkComponent walk in pending)
                {
                    if (walk == null || walk.Floor == null ||
                        walk.m_state == null)
                    {
                        PendingPhysicalExit.Remove(walk);
                        continue;
                    }

                    Entity entity = CharacterAccess.GetEntity(walk);
                    if (entity == null)
                    {
                        PendingPhysicalExit.Remove(walk);
                        continue;
                    }

                    UseComponent use = entity.GetComponent<UseComponent>();
                    if ((use == null || !use.IsBusy()) &&
                        !IsAwaitingBladderStageTransition(entity) &&
                        !IsUrineAnalysisInteractionActive(entity))
                    {
                        readyFloors.Add(walk.Floor);
                    }
                }
            }

            if (Active.Count > 0)
            {
                List<KeyValuePair<WalkComponent, AccessZoneRecoveryEntry>>
                    activeEntries =
                        new List<KeyValuePair<WalkComponent, AccessZoneRecoveryEntry>>(
                            Active);

                foreach (
                    KeyValuePair<WalkComponent, AccessZoneRecoveryEntry> pair
                    in activeEntries)
                {
                    WalkComponent walk = pair.Key;
                    AccessZoneRecoveryEntry entry = pair.Value;
                    if (entry == null ||
                        !entry.FinishBladderSequenceBeforeExit)
                    {
                        continue;
                    }

                    if (walk == null || walk.Floor == null ||
                        walk.m_state == null)
                    {
                        Active.Remove(walk);
                        continue;
                    }

                    Entity entity = CharacterAccess.GetEntity(walk);
                    if (entity == null)
                    {
                        Active.Remove(walk);
                        continue;
                    }

                    if (!IsBladderSequenceContinuationActive(entity))
                    {
                        readyFloors.Add(walk.Floor);
                    }
                }
            }

            foreach (Floor floor in readyFloors)
            {
                HandleAccessRightsChanged(floor, null);
            }
        }

        private static ProcedureScriptNeedBladder GetBladderScript(
            Entity entity)
        {
            ProcedureComponent procedure =
                entity == null ? null : entity.GetComponent<ProcedureComponent>();
            return procedure == null ||
                   procedure.m_state == null ||
                   procedure.m_state.m_currentProcedureScript == null
                ? null
                : procedure.m_state.m_currentProcedureScript.GetEntity()
                    as ProcedureScriptNeedBladder;
        }

        private static ProcedureScriptNeedHunger GetHungerScript(
            Entity entity)
        {
            ProcedureComponent procedure =
                entity == null ? null : entity.GetComponent<ProcedureComponent>();
            return procedure == null ||
                   procedure.m_state == null ||
                   procedure.m_state.m_currentProcedureScript == null
                ? null
                : procedure.m_state.m_currentProcedureScript.GetEntity()
                    as ProcedureScriptNeedHunger;
        }

        private static ProcedureScriptExaminationUrineAnalysis GetUrineAnalysisScript(
            Entity entity)
        {
            ProcedureComponent procedure =
                entity == null ? null : entity.GetComponent<ProcedureComponent>();
            return procedure == null ||
                   procedure.m_state == null ||
                   procedure.m_state.m_currentProcedureScript == null
                ? null
                : procedure.m_state.m_currentProcedureScript.GetEntity()
                    as ProcedureScriptExaminationUrineAnalysis;
        }

        private static bool IsUrineAnalysisInteractionActive(Entity entity)
        {
            ProcedureScriptExaminationUrineAnalysis urine =
                GetUrineAnalysisScript(entity);
            if (urine == null || urine.m_stateData == null)
            {
                return false;
            }

            if (urine.m_stateData.m_state ==
                ProcedureScriptExaminationUrineAnalysis.STATE_PATIENT_ON_TOILET)
            {
                return true;
            }

            if (urine.m_stateData.m_state !=
                ProcedureScriptExaminationUrineAnalysis.STATE_PATIENT_GOING_TO_PROCEDURE)
            {
                return false;
            }

            WalkComponent walk = entity.GetComponent<WalkComponent>();
            TileObject target =
                walk == null ||
                walk.m_state == null ||
                walk.m_state.m_objectToSitOn == null
                    ? null
                    : walk.m_state.m_objectToSitOn.GetEntity();

            return target != null &&
                   target.HasTag("wc") &&
                   walk.IsSittingOn(target);
        }

        internal static TileObject FindGrandfatheredBladderFixture(
            ProcedureScriptNeedBladder script,
            Entity character,
            Room room,
            string tag,
            bool allowObjectsWithAttachments)
        {
            if (script == null ||
                character == null ||
                room == null ||
                script.m_stateData == null ||
                (tag != "washing" && tag != "dryer") ||
                Hospital.Instance == null)
            {
                return null;
            }

            WalkComponent walk = character.GetComponent<WalkComponent>();
            if (walk == null || walk.m_state == null || walk.Floor == null)
            {
                return null;
            }

            AccessZoneRecoveryEntry entry;
            if (!Active.TryGetValue(walk, out entry) ||
                entry == null ||
                !entry.FinishBladderSequenceBeforeExit ||
                entry.FloorIndex != walk.Floor.m_floorIndex ||
                room.GetFloorIndex() != walk.Floor.m_floorIndex)
            {
                return null;
            }

            string state = script.m_stateData.m_state;
            bool expectedSearch =
                (tag == "washing" &&
                 state == ProcedureScriptNeedBladder.STATE_USING_OBJECT) ||
                (tag == "dryer" &&
                 (state == ProcedureScriptNeedBladder.STATE_USING_SINK ||
                  state == ProcedureScriptNeedBladder.STATE_USING_SINK_GERMAPHOBE));
            if (!expectedSearch)
            {
                return null;
            }

            RoomValidity roomValidity = room.m_roomPersistentData.m_valid;
            if (roomValidity != RoomValidity.OK &&
                roomValidity != RoomValidity.MISSING_STAFF &&
                roomValidity != RoomValidity.DEPARTMENT_CLOSED &&
                roomValidity != RoomValidity.INACCESSIBLE_PATIENTS)
            {
                return null;
            }

            Floor floor = walk.Floor;
            Behavior behavior = character.GetComponent<Behavior>();
            if (behavior == null)
            {
                return null;
            }

            AccessRights temporaryAccess =
                (AccessRights)entry.TemporaryAccessLevel;
            float bestDistance = float.MaxValue;
            TileObject best = null;

            for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                 x <= room.m_roomPersistentData.m_positionTop.m_x;
                 x++)
            {
                for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                     y <= room.m_roomPersistentData.m_positionTop.m_y;
                     y++)
                {
                    Vector2i tile = new Vector2i(x, y);
                    if (!IsInsideFloor(floor, tile) ||
                        !room.IsPositionInRoom(tile))
                    {
                        continue;
                    }

                    TileObjects objects = floor.m_tileObjects[x, y];
                    ConsiderGrandfatheredFixture(
                        objects.m_centerObject,
                        character,
                        script,
                        tag,
                        allowObjectsWithAttachments ||
                            objects.m_attachmentObject == null,
                        floor,
                        walk,
                        behavior,
                        entry,
                        temporaryAccess,
                        ref best,
                        ref bestDistance);

                    foreach (TileObject candidate in objects.GetAllNonCenterObjects())
                    {
                        ConsiderGrandfatheredFixture(
                            candidate,
                            character,
                            script,
                            tag,
                            true,
                            floor,
                            walk,
                            behavior,
                            entry,
                            temporaryAccess,
                            ref best,
                            ref bestDistance);
                    }
                }
            }

            if (best != null && TrafficControlConfig.PathfindingDebug)
            {
                LogRecovery(
                    character,
                    "bladder-sequence-fixture-" + tag,
                    walk.GetCurrentTileSafe(),
                    best.m_state.m_position,
                    behavior.GetAccessRights());
            }

            return best;
        }

        private static void ConsiderGrandfatheredFixture(
            TileObject candidate,
            Entity character,
            ProcedureScriptNeedBladder script,
            string tag,
            bool attachmentAllowed,
            Floor floor,
            WalkComponent walk,
            Behavior behavior,
            AccessZoneRecoveryEntry entry,
            AccessRights temporaryAccess,
            ref TileObject best,
            ref float bestDistance)
        {
            if (!attachmentAllowed ||
                candidate == null ||
                candidate.m_state == null ||
                !candidate.HasTag(tag) ||
                (candidate.User != null && candidate.User != character) ||
                (candidate.Owner != null && candidate.Owner != script) ||
                candidate.IsBroken() ||
                !candidate.IsValid())
            {
                return;
            }

            if (candidate.m_state.m_compositeParent != null &&
                candidate.m_state.m_compositeParent.GetEntity() != null &&
                candidate.m_state.m_compositeParent.GetEntity().HasUnusablePart())
            {
                return;
            }

            Vector2i objectTile = candidate.m_state.m_position;
            Vector2f usePositionF = candidate.GetDefaultUsePosition();
            Vector2i useTile = new Vector2i(
                (int)(usePositionF.m_x + 0.5f),
                (int)(usePositionF.m_y + 0.5f));

            if (!IsInsideFloor(floor, objectTile) ||
                !IsInsideFloor(floor, useTile) ||
                !entry.AllowedForbiddenTiles.Contains(useTile))
            {
                return;
            }

            if (!entry.AllowedForbiddenTiles.Contains(objectTile) &&
                !IsTileLegal(
                    floor,
                    objectTile,
                    behavior.GetAccessRights()))
            {
                return;
            }

            float distance =
                GridMap.GetInstance().GetDistance(
                    walk.GetFloorIndex(),
                    walk.GetCurrentTile(),
                    candidate.GetFloorIndex(),
                    useTile,
                    temporaryAccess);
            if (distance < 0f || distance >= bestDistance)
            {
                return;
            }

            bestDistance = distance;
            best = candidate;
        }

        private static bool TryCancelNewlyInaccessibleBladderDestination(
            WalkComponent walk,
            Entity entity,
            Floor changedFloor,
            AccessChangeSet accessChange,
            AccessRights accessRights)
        {
            if (walk == null ||
                entity == null ||
                changedFloor == null ||
                accessChange == null)
            {
                return false;
            }

            ProcedureScriptNeedBladder bladder = GetBladderScript(entity);
            if (bladder == null ||
                bladder.m_stateData == null ||
                bladder.m_stateData.m_state !=
                    ProcedureScriptNeedBladder.STATE_GOING_TO_OBJECT)
            {
                return false;
            }

            TileObject target = bladder.GetEquipment(0);
            if (target == null ||
                target.m_state == null ||
                target.GetFloorIndex() != changedFloor.m_floorIndex)
            {
                return false;
            }

            Vector2f usePositionF = target.GetDefaultUsePosition();
            Vector2i useTile = new Vector2i(
                (int)(usePositionF.m_x + 0.5f),
                (int)(usePositionF.m_y + 0.5f));
            Vector2i objectTile = target.m_state.m_position;

            if (!accessChange.WasTileNewlyRestricted(useTile, accessRights) &&
                !accessChange.WasTileNewlyRestricted(objectTile, accessRights))
            {
                return false;
            }

            UseComponent use = entity.GetComponent<UseComponent>();
            if (use != null && !use.IsBusy())
            {
                use.Interrupt();
            }

            bladder.SwitchState(ProcedureScriptNeedBladder.STATE_IDLE);
            BlockedRouteManager.ClearRecovered(walk);
            StopInvalidatedDestinationCleanly(walk, entity);

            LogRecovery(
                entity,
                "destination-invalidated-bladder-reselect",
                walk.GetCurrentTileSafe(),
                useTile,
                accessRights);
            return true;
        }

        private static bool TryRerouteNewlyInaccessibleUrineDestination(
            WalkComponent walk,
            Entity entity,
            Floor changedFloor,
            AccessChangeSet accessChange,
            AccessRights accessRights,
            bool currentTileLegal)
        {
            if (walk == null ||
                walk.m_state == null ||
                entity == null ||
                changedFloor == null ||
                accessChange == null)
            {
                return false;
            }

            ProcedureScriptExaminationUrineAnalysis urine =
                GetUrineAnalysisScript(entity);
            if (urine == null ||
                urine.m_stateData == null ||
                urine.m_stateData.m_state !=
                    ProcedureScriptExaminationUrineAnalysis.STATE_PATIENT_GOING_TO_PROCEDURE)
            {
                return false;
            }

            TileObject target =
                walk.m_state.m_objectToSitOn == null
                    ? null
                    : walk.m_state.m_objectToSitOn.GetEntity();
            if (target == null ||
                target.m_state == null ||
                !target.HasTag("wc") ||
                target.GetFloorIndex() != changedFloor.m_floorIndex ||
                walk.IsSittingOn(target))
            {
                return false;
            }

            Vector2f usePositionF = target.GetDefaultUsePosition();
            Vector2i useTile = new Vector2i(
                (int)(usePositionF.m_x + 0.5f),
                (int)(usePositionF.m_y + 0.5f));
            Vector2i objectTile = target.m_state.m_position;

            if (!accessChange.WasTileNewlyRestricted(useTile, accessRights) &&
                !accessChange.WasTileNewlyRestricted(objectTile, accessRights))
            {
                return false;
            }

            if (target.User == entity)
            {
                target.User = null;
            }

            walk.ResetGoSit();
            BlockedRouteManager.ClearRecovered(walk);
            StopInvalidatedDestinationCleanly(walk, entity);

            if (!currentTileLegal)
            {
                urine.SwitchState(
                    ProcedureScriptExaminationUrineAnalysis.STATE_WAIT_FOR_WC);

                LogRecovery(
                    entity,
                    "destination-invalidated-urine-wc-wait-for-exit",
                    walk.GetCurrentTileSafe(),
                    useTile,
                    accessRights);
                return true;
            }

            TileObject replacement =
                PrivateBathroomManager.FindAllowedBathroomReplacement(
                    entity,
                    AccessRights.PATIENT);

            if (replacement == null)
            {
                urine.SwitchState(
                    ProcedureScriptExaminationUrineAnalysis.STATE_WAIT_FOR_WC);

                LogRecovery(
                    entity,
                    "destination-invalidated-urine-wc-wait",
                    walk.GetCurrentTileSafe(),
                    useTile,
                    accessRights);
                return true;
            }

            walk.GoSit(replacement);
            urine.SwitchState(
                ProcedureScriptExaminationUrineAnalysis.STATE_PATIENT_GOING_TO_PROCEDURE);

            Vector2f replacementUsePosition = replacement.GetDefaultUsePosition();
            LogRecovery(
                entity,
                "destination-invalidated-urine-wc-reselect",
                walk.GetCurrentTileSafe(),
                new Vector2i(
                    (int)(replacementUsePosition.m_x + 0.5f),
                    (int)(replacementUsePosition.m_y + 0.5f)),
                accessRights);
            return true;
        }

        private static bool TryCancelNewlyInaccessibleHungerDestination(
            WalkComponent walk,
            Entity entity,
            Floor changedFloor,
            AccessChangeSet accessChange,
            AccessRights accessRights)
        {
            if (walk == null ||
                entity == null ||
                changedFloor == null ||
                accessChange == null)
            {
                return false;
            }

            ProcedureScriptNeedHunger hunger = GetHungerScript(entity);
            if (hunger == null ||
                hunger.m_stateData == null ||
                hunger.m_stateData.m_state !=
                    ProcedureScriptNeedHunger.STATE_GOING_TO_OBJECT)
            {
                return false;
            }

            TileObject target = hunger.GetEquipment(0);
            if (target == null ||
                target.m_state == null ||
                target.GetFloorIndex() != changedFloor.m_floorIndex ||
                IsCafeteriaHungerTarget(changedFloor, target))
            {
                return false;
            }

            Vector2f usePositionF = target.GetDefaultUsePosition();
            Vector2i useTile = new Vector2i(
                (int)(usePositionF.m_x + 0.5f),
                (int)(usePositionF.m_y + 0.5f));
            Vector2i objectTile = target.m_state.m_position;

            if (!accessChange.WasTileNewlyRestricted(useTile, accessRights) &&
                !accessChange.WasTileNewlyRestricted(objectTile, accessRights))
            {
                return false;
            }

            UseComponent use = entity.GetComponent<UseComponent>();
            if (use != null && !use.IsBusy())
            {
                use.Interrupt();
            }

            hunger.SwitchState(ProcedureScriptNeedHunger.STATE_IDLE);
            BlockedRouteManager.ClearRecovered(walk);
            StopInvalidatedDestinationCleanly(walk, entity);

            LogRecovery(
                entity,
                "destination-invalidated-hunger-reselect",
                walk.GetCurrentTileSafe(),
                useTile,
                accessRights);
            return true;
        }

        private static bool IsCafeteriaHungerTarget(
            Floor floor,
            TileObject target)
        {
            if (floor == null ||
                target == null ||
                target.m_state == null ||
                floor.m_roomTiles == null)
            {
                return false;
            }

            Vector2i tile = target.m_state.m_position;
            if (!IsInsideFloor(floor, tile))
            {
                return false;
            }

            Room room = floor.m_roomTiles[tile.m_x, tile.m_y];
            return room != null &&
                   room.m_roomPersistentData != null &&
                   room.m_roomPersistentData.m_roomType != null &&
                   room.m_roomPersistentData.m_roomType.Entry != null &&
                   room.m_roomPersistentData.m_roomType.Entry.HasTag("cafeteria");
        }

        internal static bool TryDeferDestinationDuringAccessExit(
            WalkComponent walk,
            Vector2f destination,
            int destinationFloor,
            MovementType movementType)
        {
            if (!ShouldDeferMovementDuringAccessExit(walk))
            {
                return false;
            }

            StoreDeferredMovement(
                walk,
                new DeferredMovementRequest
                {
                    Destination = destination,
                    DestinationFloor = destinationFloor,
                    MovementType = movementType,
                    GoSit = false,
                    ObjectToSitOn = null
                });

            LogDeferredMovement(walk, destination, false);
            return true;
        }

        internal static bool TryDeferGoSitDuringAccessExit(
            WalkComponent walk,
            TileObject objectToSitOn,
            MovementType movementType)
        {
            if (objectToSitOn == null ||
                !ShouldDeferMovementDuringAccessExit(walk))
            {
                return false;
            }

            StoreDeferredMovement(
                walk,
                new DeferredMovementRequest
                {
                    Destination = objectToSitOn.GetDefaultUsePosition(),
                    DestinationFloor = objectToSitOn.GetFloorIndex(),
                    MovementType = movementType,
                    GoSit = true,
                    ObjectToSitOn = objectToSitOn
                });

            Entity entity = CharacterAccess.GetEntity(walk);
            if (entity != null)
            {
                objectToSitOn.User = entity;
            }

            LogDeferredMovement(
                walk,
                objectToSitOn.GetDefaultUsePosition(),
                true);
            return true;
        }

        private static bool ShouldDeferMovementDuringAccessExit(
            WalkComponent walk)
        {
            if (walk == null ||
                walk.m_state == null ||
                walk.Floor == null ||
                RecoveryMovement.Contains(walk) ||
                !IsCurrentTileRestricted(walk))
            {
                return false;
            }

            AccessZoneRecoveryEntry entry;
            if (Active.TryGetValue(walk, out entry) && entry != null)
            {
                return entry.TemporaryExit &&
                       !entry.FinishBladderSequenceBeforeExit;
            }

            return PendingPhysicalExit.Contains(walk);
        }

        private static void StoreDeferredMovement(
            WalkComponent walk,
            DeferredMovementRequest request)
        {
            DeferredMovementRequest previous;
            if (DeferredMovements.TryGetValue(walk, out previous))
            {
                ReleaseDeferredMovementReservation(walk, previous);
            }

            DeferredMovements[walk] = request;
        }

        private static void ReleaseDeferredMovementReservation(
            WalkComponent walk,
            DeferredMovementRequest request)
        {
            if (walk == null ||
                request == null ||
                !request.GoSit ||
                request.ObjectToSitOn == null)
            {
                return;
            }

            TileObject target = request.ObjectToSitOn.GetEntity();
            Entity entity = CharacterAccess.GetEntity(walk);
            if (target != null && entity != null && target.User == entity)
            {
                target.User = null;
            }
        }

        private static void LogDeferredMovement(
            WalkComponent walk,
            Vector2f destination,
            bool goSit)
        {
            if (!TrafficControlConfig.PathfindingDebug ||
                walk == null ||
                walk.Floor == null)
            {
                return;
            }

            Entity entity = CharacterAccess.GetEntity(walk);
            Behavior behavior =
                entity == null ? null : entity.GetComponent<Behavior>();
            if (behavior == null)
            {
                return;
            }

            LogRecovery(
                entity,
                goSit
                    ? "destination-deferred-until-access-exit-gosit"
                    : "destination-deferred-until-access-exit",
                walk.GetCurrentTileSafe(),
                new Vector2i(
                    (int)(destination.m_x + 0.5f),
                    (int)(destination.m_y + 0.5f)),
                behavior.GetAccessRights());
        }

        private static void SetRecoveryDestination(
            WalkComponent walk,
            Vector2f destination,
            int floorIndex)
        {
            RecoveryMovement.Add(walk);
            try
            {
                walk.SetDestination(destination, floorIndex);
            }
            finally
            {
                RecoveryMovement.Remove(walk);
            }
        }

        private static bool TryReplayDeferredMovement(
            WalkComponent walk,
            Entity entity,
            Behavior behavior,
            Vector2i currentTile)
        {
            DeferredMovementRequest request;
            if (!DeferredMovements.TryGetValue(walk, out request))
            {
                return false;
            }

            DeferredMovements.Remove(walk);

            if (request == null)
            {
                return true;
            }

            if (request.GoSit)
            {
                TileObject target =
                    request.ObjectToSitOn == null
                        ? null
                        : request.ObjectToSitOn.GetEntity();
                if (target == null || target.m_state == null)
                {
                    ReleaseDeferredMovementReservation(walk, request);
                    walk.Stop();

                    LogRecovery(
                        entity,
                        "exit-complete-deferred-gosit-target-missing",
                        currentTile,
                        currentTile,
                        behavior.GetAccessRights());
                    return true;
                }

                walk.GoSit(target, request.MovementType);
            }
            else
            {
                walk.SetDestination(
                    request.Destination,
                    request.DestinationFloor,
                    request.MovementType);
            }

            BlockedRouteManager.ClearRecovered(walk);
            LogRecovery(
                entity,
                request.GoSit
                    ? "exit-complete-deferred-gosit-replayed"
                    : "exit-complete-deferred-destination-replayed",
                currentTile,
                new Vector2i(
                    (int)(request.Destination.m_x + 0.5f),
                    (int)(request.Destination.m_y + 0.5f)),
                behavior.GetAccessRights());
            return true;
        }

        private static void StopInvalidatedDestinationCleanly(
            WalkComponent walk,
            Entity entity)
        {
            if (walk == null || walk.m_state == null)
            {
                return;
            }

            walk.Stop();

            // WalkComponent.Stop() only switches the movement state to Idle.
            // It does not clear the old route or replace the active walk
            // animation. HPO hit the same vanilla behavior for Porters waiting
            // outside a room: the character can remain visually walking in place.
            walk.m_route = null;
            walk.m_blockedCount = 0;
            walk.m_state.m_nextPosition = walk.m_state.m_currentPosition;
            walk.m_state.m_walkMidpoint1 = null;
            walk.m_state.m_walkMidpoint2 = null;

            if (!walk.m_state.m_lying)
            {
                AnimModelComponent anim =
                    entity == null
                        ? null
                        : entity.GetComponent<AnimModelComponent>();
                if (anim != null)
                {
                    anim.PlayAnimation(
                        walk.IsSitting()
                            ? "sit_relax_pc_out"
                            : "stand_idle",
                        looping: false);
                }
            }
        }

        // Once the WC itself has started, keep the native WC -> sink -> dryer
        // sequence alive inside the already restricted component. The temporary
        // access is scoped to this character's pathfinding jobs and disappears as
        // soon as the sequence ends or the character reaches a legal tile.
        private static bool IsBladderSequenceContinuationActive(Entity entity)
        {
            ProcedureScriptNeedBladder script = GetBladderScript(entity);
            if (script == null || script.m_stateData == null)
            {
                return false;
            }

            string state = script.m_stateData.m_state;
            return state == ProcedureScriptNeedBladder.STATE_USING_OBJECT ||
                   state == ProcedureScriptNeedBladder.STATE_GOING_TO_SINK ||
                   state == ProcedureScriptNeedBladder.STATE_USING_SINK ||
                   state == ProcedureScriptNeedBladder.STATE_USING_SINK_GERMAPHOBE ||
                   state == ProcedureScriptNeedBladder.STATE_GOING_TO_DRYER ||
                   state == ProcedureScriptNeedBladder.STATE_USING_DRYER;
        }

        // UseComponent can become IDLE just before the bladder script
        // consumes the completed WC / sink / dryer action. Let the native
        // stage update run once, so BLADDER_REDUCED and the normal end of
        // the hand-washing cycle are not accidentally skipped.
        private static bool IsAwaitingBladderStageTransition(Entity entity)
        {
            ProcedureScriptNeedBladder script = GetBladderScript(entity);
            if (script == null || script.m_stateData == null)
            {
                return false;
            }

            string state = script.m_stateData.m_state;
            return state == ProcedureScriptNeedBladder.STATE_USING_OBJECT ||
                   state == ProcedureScriptNeedBladder.STATE_USING_SINK ||
                   state == ProcedureScriptNeedBladder.STATE_USING_SINK_GERMAPHOBE ||
                   state == ProcedureScriptNeedBladder.STATE_USING_DRYER;
        }

        internal static bool ShouldPauseBladderMovement(
            ProcedureScriptNeedBladder script)
        {
            if (script == null || script.m_stateData == null ||
                script.m_stateData.m_procedureScene == null)
            {
                return false;
            }

            Entity entity = script.m_stateData.m_procedureScene.MainCharacter;
            WalkComponent walk =
                entity == null ? null : entity.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return false;
            }

            AccessZoneRecoveryEntry entry;
            if (Active.TryGetValue(walk, out entry) &&
                entry != null &&
                entry.FinishBladderSequenceBeforeExit)
            {
                return false;
            }

            if (!Active.ContainsKey(walk) &&
                !PendingPhysicalExit.Contains(walk))
            {
                return false;
            }

            string state = script.m_stateData.m_state;
            return state == ProcedureScriptNeedBladder.STATE_GOING_TO_OBJECT ||
                   state == ProcedureScriptNeedBladder.STATE_GOING_TO_SINK ||
                   state == ProcedureScriptNeedBladder.STATE_GOING_TO_DRYER;
        }

        internal static bool IsRestrictedNoPathCandidate(WalkComponent walk)
        {
            return walk != null &&
                   walk.m_state != null &&
                   walk.Floor != null &&
                   !walk.m_state.m_lying &&
                   CharacterAccess.CanBeRestrictedByAccessChange(walk) &&
                   IsCurrentTileRestricted(walk);
        }

        internal static bool TryRecoverRestrictedNoPath(WalkComponent walk)
        {
            if (!IsRestrictedNoPathCandidate(walk))
            {
                return false;
            }

            Entity entity = CharacterAccess.GetEntity(walk);

            AccessZoneRecoveryEntry continuationEntry;
            if (Active.TryGetValue(walk, out continuationEntry) &&
                continuationEntry != null &&
                continuationEntry.FinishBladderSequenceBeforeExit &&
                IsBladderSequenceContinuationActive(entity))
            {
                if (!continuationEntry.BladderContinuationRepathAttempted)
                {
                    continuationEntry.BladderContinuationRepathAttempted = true;
                    Active[walk] = continuationEntry;
                    BlockedRouteManager.ClearRecovered(walk);
                    BlockedRouteManager.ForceRepath(walk);

                    Behavior continuationBehavior =
                        entity == null
                            ? null
                            : entity.GetComponent<Behavior>();
                    LogRecovery(
                        entity,
                        "bladder-sequence-repath",
                        walk.GetCurrentTileSafe(),
                        walk.GetDestinationTile(),
                        continuationBehavior == null
                            ? AccessRights.PEDESTRIAN
                            : continuationBehavior.GetAccessRights());
                    return true;
                }

                continuationEntry.FinishBladderSequenceBeforeExit = false;
                Active[walk] = continuationEntry;
            }

            UseComponent use =
                entity == null ? null : entity.GetComponent<UseComponent>();
            if (use != null && use.IsBusy())
            {
                PendingPhysicalExit.Add(walk);
                return true;
            }
            Behavior behavior = entity == null
                ? null
                : entity.GetComponent<Behavior>();
            if (behavior == null)
            {
                return false;
            }

            Vector2i currentTile = walk.GetCurrentTileSafe();
            AccessRights accessRights = behavior.GetAccessRights();

            HashSet<Vector2i> allowedForbiddenTiles;
            Vector2i exitTile;
            int temporaryAccessLevel;
            bool oneWayPreventedExit;

            if (!TryFindExit(
                    walk.Floor,
                    currentTile,
                    accessRights,
                    out allowedForbiddenTiles,
                    out exitTile,
                    out temporaryAccessLevel,
                    out oneWayPreventedExit))
            {
                LogRecovery(
                    entity,
                    oneWayPreventedExit
                        ? "no-path-no-safe-exit-oneway"
                        : "no-path-no-safe-exit",
                    currentTile,
                    Vector2i.ZERO_VECTOR,
                    accessRights);
                return false;
            }

            AccessZoneRecoveryEntry entry = new AccessZoneRecoveryEntry
            {
                FloorIndex = walk.Floor.m_floorIndex,
                TemporaryAccessLevel = temporaryAccessLevel,
                AllowedForbiddenTiles = allowedForbiddenTiles,
                TemporaryExit = true,
                OriginalDestination = walk.m_state.m_destination,
                OriginalDestinationFloor = walk.m_state.m_destinationFloor,
                OriginalWalkState = WalkState.Walking,
                OriginalMovementType = walk.m_state.m_movementType,
                OriginalObjectToSitOn = BlockedRouteManager.GetReservedObject(walk)
            };

            Active[walk] = entry;
            BlockedRouteManager.ClearForAccessRecovery(
                walk,
                restoreReservation: false);
            ReleaseCurrentSitReservation(walk, entity);

            SetRecoveryDestination(
                walk,
                new Vector2f(exitTile.m_x, exitTile.m_y),
                walk.Floor.m_floorIndex);

            LogRecovery(
                entity,
                "no-path-temporary-exit",
                currentTile,
                exitTile,
                accessRights);
            return true;
        }

        private static bool TryDeferForBladderSequence(
            WalkComponent walk,
            Entity entity,
            Behavior behavior,
            Vector2i currentTile,
            AccessRights accessRights)
        {
            if (walk == null ||
                entity == null ||
                behavior == null ||
                walk.Floor == null ||
                walk.m_state == null ||
                !IsBladderSequenceContinuationActive(entity))
            {
                return false;
            }

            HashSet<Vector2i> allowedForbiddenTiles;
            Vector2i exitTile;
            int temporaryAccessLevel;
            bool oneWayPreventedExit;

            if (!TryFindExit(
                    walk.Floor,
                    currentTile,
                    accessRights,
                    out allowedForbiddenTiles,
                    out exitTile,
                    out temporaryAccessLevel,
                    out oneWayPreventedExit))
            {
                return false;
            }

            Active[walk] = new AccessZoneRecoveryEntry
            {
                FloorIndex = walk.Floor.m_floorIndex,
                TemporaryAccessLevel = temporaryAccessLevel,
                AllowedForbiddenTiles = allowedForbiddenTiles,
                TemporaryExit = false,
                FinishBladderSequenceBeforeExit = true,
                OriginalDestination = walk.m_state.m_destination,
                OriginalDestinationFloor = walk.m_state.m_destinationFloor,
                OriginalWalkState = walk.m_state.m_walkState,
                OriginalMovementType = walk.m_state.m_movementType,
                OriginalObjectToSitOn =
                    BlockedRouteManager.GetReservedObject(walk)
            };

            PendingPhysicalExit.Remove(walk);

            LogRecovery(
                entity,
                "deferred-bladder-sequence",
                currentTile,
                exitTile,
                accessRights);
            return true;
        }

        internal static void HandleAccessRightsChanged(
            Floor floor,
            AccessChangeSet accessChange)
        {
            if (floor == null || Hospital.Instance == null)
            {
                return;
            }

            List<Entity> characters = new List<Entity>(Hospital.Instance.m_characters);

            foreach (Entity entity in characters)
            {
                if (entity == null)
                {
                    continue;
                }

                WalkComponent walk = entity.GetComponent<WalkComponent>();
                Behavior behavior = entity.GetComponent<Behavior>();

                if (walk == null ||
                    behavior == null ||
                    walk.m_state == null ||
                    !CharacterAccess.CanBeRestrictedByAccessChange(walk))
                {
                    continue;
                }

                bool currentFloorAffected = walk.Floor == floor;
                bool destinationFloorAffected =
                    walk.m_state.m_destinationFloor == floor.m_floorIndex;
                if (!currentFloorAffected && !destinationFloorAffected)
                {
                    continue;
                }

                Vector2i currentTile = walk.GetCurrentTileSafe();
                AccessRights accessRights = behavior.GetAccessRights();

                AccessZoneRecoveryEntry existingEntry;
                bool hasExistingRecovery =
                    Active.TryGetValue(walk, out existingEntry);
                bool accessRecoveryBlocked =
                    BlockedRouteManager.IsAccessRecoveryBlocked(walk);
                bool newlyRestricted =
                    currentFloorAffected &&
                    accessChange != null &&
                    accessChange.WasTileNewlyRestricted(
                        currentTile,
                        accessRights);

                Vector2i destinationTile = walk.GetDestinationTile();
                bool destinationNewlyRestricted =
                    destinationFloorAffected &&
                    accessChange != null &&
                    accessChange.WasTileNewlyRestricted(
                        destinationTile,
                        accessRights);

                bool currentTileLegal =
                    !currentFloorAffected ||
                    IsTileLegal(
                        walk.Floor,
                        currentTile,
                        accessRights);

                if (!hasExistingRecovery &&
                    newlyRestricted &&
                    IsUrineAnalysisInteractionActive(entity))
                {
                    if (PendingPhysicalExit.Add(walk))
                    {
                        LogRecovery(
                            entity,
                            "deferred-urine-analysis-sampling",
                            currentTile,
                            Vector2i.ZERO_VECTOR,
                            accessRights);
                    }
                    continue;
                }

                if (!hasExistingRecovery &&
                    destinationFloorAffected &&
                    accessChange != null &&
                    TryRerouteNewlyInaccessibleUrineDestination(
                        walk,
                        entity,
                        floor,
                        accessChange,
                        accessRights,
                        currentTileLegal) &&
                    currentTileLegal)
                {
                    continue;
                }

                if (!hasExistingRecovery &&
                    destinationNewlyRestricted &&
                    currentTileLegal &&
                    TryCancelNewlyInaccessibleBladderDestination(
                        walk,
                        entity,
                        floor,
                        accessChange,
                        accessRights))
                {
                    continue;
                }

                if (!hasExistingRecovery &&
                    destinationFloorAffected &&
                    accessChange != null &&
                    TryCancelNewlyInaccessibleHungerDestination(
                        walk,
                        entity,
                        floor,
                        accessChange,
                        accessRights) &&
                    currentTileLegal)
                {
                    continue;
                }

                if (!currentFloorAffected)
                {
                    continue;
                }

                bool pendingPhysicalExit =
                    PendingPhysicalExit.Contains(walk);

                if (!hasExistingRecovery &&
                    newlyRestricted &&
                    TryDeferForBladderSequence(
                        walk,
                        entity,
                        behavior,
                        currentTile,
                        accessRights))
                {
                    continue;
                }

                if (hasExistingRecovery &&
                    existingEntry != null &&
                    existingEntry.FinishBladderSequenceBeforeExit)
                {
                    if (IsBladderSequenceContinuationActive(entity))
                    {
                        continue;
                    }

                    existingEntry.FinishBladderSequenceBeforeExit = false;
                    Active[walk] = existingEntry;
                }

                // Let native interactions finish their physical animation
                // before moving a person off an access-restricted tile.
                if (!hasExistingRecovery &&
                    (newlyRestricted || pendingPhysicalExit))
                {
                    UseComponent use = entity.GetComponent<UseComponent>();
                    if ((use != null && use.IsBusy()) ||
                        IsAwaitingBladderStageTransition(entity))
                    {
                        if (PendingPhysicalExit.Add(walk))
                        {
                            LogRecovery(
                                entity,
                                "deferred-physical-interaction",
                                currentTile,
                                Vector2i.ZERO_VECTOR,
                                accessRights);
                        }
                        continue;
                    }

                    PendingPhysicalExit.Remove(walk);
                }

                if (!hasExistingRecovery &&
                    !accessRecoveryBlocked &&
                    !newlyRestricted &&
                    !pendingPhysicalExit)
                {
                    continue;
                }

                if (currentTileLegal)
                {
                    if (!hasExistingRecovery &&
                        TryReplayDeferredMovement(
                            walk,
                            entity,
                            behavior,
                            currentTile))
                    {
                        continue;
                    }

                    if (hasExistingRecovery)
                    {
                        if (!existingEntry.TemporaryExit)
                        {
                            Active.Remove(walk);
                        }
                        else if (walk.m_state.m_walkState == WalkState.Idle ||
                                 walk.m_state.m_walkState == WalkState.NoPath)
                        {
                            CompleteTemporaryExit(
                                walk,
                                existingEntry,
                                entity,
                                behavior,
                                currentTile);
                        }
                    }

                    continue;
                }

                // Lying/transported characters are controlled by hospitalization or
                // transport systems. Moving them as pedestrians would corrupt state.
                if (walk.m_state.m_lying)
                {
                    LogRecovery(
                        entity,
                        "skipped-lying",
                        currentTile,
                        Vector2i.ZERO_VECTOR,
                        accessRights);
                    continue;
                }

                Vector2f originalDestination =
                    hasExistingRecovery
                        ? existingEntry.OriginalDestination
                        : walk.m_state.m_destination;
                int originalDestinationFloor =
                    hasExistingRecovery
                        ? existingEntry.OriginalDestinationFloor
                        : walk.m_state.m_destinationFloor;
                WalkState originalWalkState =
                    hasExistingRecovery
                        ? existingEntry.OriginalWalkState
                        : walk.m_state.m_walkState;
                MovementType originalMovementType =
                    hasExistingRecovery
                        ? existingEntry.OriginalMovementType
                        : walk.m_state.m_movementType;
                EntityIDPointer<TileObject> originalObjectToSitOn =
                    hasExistingRecovery
                        ? existingEntry.OriginalObjectToSitOn
                        : BlockedRouteManager.GetReservedObject(walk);

                HashSet<Vector2i> allowedForbiddenTiles;
                Vector2i exitTile;
                int temporaryAccessLevel;
                bool oneWayPreventedExit;

                if (!TryFindExit(
                        floor,
                        currentTile,
                        accessRights,
                        out allowedForbiddenTiles,
                        out exitTile,
                        out temporaryAccessLevel,
                        out oneWayPreventedExit))
                {
                    Active.Remove(walk);
                    BlockedRouteManager.ClearForAccessRecovery(
                        walk,
                        restoreReservation: true);
                    walk.m_route = null;
                    walk.m_state.m_destination = originalDestination;
                    walk.m_state.m_destinationFloor = originalDestinationFloor;
                    walk.m_state.m_walkMidpoint1 = null;
                    walk.m_state.m_walkMidpoint2 = null;
                    walk.m_state.m_movementType = originalMovementType;
                    walk.m_state.m_nextPosition = walk.m_state.m_currentPosition;
                    walk.SwitchState(WalkState.NoPath);
                    BlockedRouteManager.RegisterAccessRecovery(
                        walk,
                        originalObjectToSitOn);

                    if (oneWayPreventedExit)
                    {
                        BlockedRouteManager.RegisterOneWay(walk);
                        OneWayRouteManager.RegisterExternalOneWayBlock(walk);
                    }

                    LogRecovery(
                        entity,
                        "no-safe-exit",
                        currentTile,
                        Vector2i.ZERO_VECTOR,
                        accessRights);
                    continue;
                }

                AccessZoneRecoveryEntry entry = new AccessZoneRecoveryEntry
                {
                    FloorIndex = floor.m_floorIndex,
                    TemporaryAccessLevel = temporaryAccessLevel,
                    AllowedForbiddenTiles = allowedForbiddenTiles,
                    OriginalDestination = originalDestination,
                    OriginalDestinationFloor = originalDestinationFloor,
                    OriginalWalkState = originalWalkState,
                    OriginalMovementType = originalMovementType,
                    OriginalObjectToSitOn = originalObjectToSitOn
                };

                bool destinationLegal = IsSavedDestinationLegal(
                    originalDestination,
                    originalDestinationFloor,
                    behavior.GetAccessRights());

                bool canResumeOriginal =
                    destinationLegal &&
                    TryRestoreOriginalReservation(
                        walk,
                        entity,
                        originalObjectToSitOn);

                if (canResumeOriginal)
                {
                    entry.TemporaryExit = false;
                    Active[walk] = entry;

                    BlockedRouteManager.ClearForAccessRecovery(
                        walk,
                        restoreReservation: true);

                    walk.m_route = null;
                    walk.m_state.m_destination = originalDestination;
                    walk.m_state.m_destinationFloor = originalDestinationFloor;
                    walk.m_state.m_walkMidpoint1 = null;
                    walk.m_state.m_walkMidpoint2 = null;
                    walk.m_state.m_movementType = originalMovementType;
                    walk.m_state.m_nextPosition = walk.m_state.m_currentPosition;

                    BlockedRouteManager.ForceRepath(walk);

                    LogRecovery(
                        entity,
                        "resume-original-destination",
                        currentTile,
                        exitTile,
                        accessRights);
                }
                else
                {
                    entry.TemporaryExit = true;
                    Active[walk] = entry;

                    BlockedRouteManager.ClearForAccessRecovery(
                        walk,
                        restoreReservation: false);

                    ReleaseCurrentSitReservation(
                        walk,
                        entity);

                    SetRecoveryDestination(
                        walk,
                        new Vector2f(exitTile.m_x, exitTile.m_y),
                        floor.m_floorIndex);

                    LogRecovery(
                        entity,
                        "temporary-exit",
                        currentTile,
                        exitTile,
                        accessRights);
                }
            }
        }

        internal static AccessZoneRecoveryJobData GetJobData(WalkComponent walk)
        {
            if (walk == null || walk.m_state == null || walk.Floor == null)
            {
                return null;
            }

            AccessZoneRecoveryEntry entry;
            if (!Active.TryGetValue(walk, out entry))
            {
                return null;
            }

            if (entry.FloorIndex != walk.Floor.m_floorIndex)
            {
                Active.Remove(walk);
                return null;
            }

            Vector2i currentTile = walk.GetCurrentTileSafe();
            Entity entity = CharacterAccess.GetEntity(walk);
            Behavior behavior = entity == null ? null : entity.GetComponent<Behavior>();

            if (behavior == null ||
                IsTileLegal(walk.Floor, currentTile, behavior.GetAccessRights()))
            {
                return null;
            }

            return new AccessZoneRecoveryJobData(
                entry.FloorIndex,
                entry.TemporaryAccessLevel,
                entry.AllowedForbiddenTiles);
        }

        internal static void OnWalkStateChanged(WalkComponent walk, WalkState state)
        {
            if (walk == null || walk.m_state == null || walk.Floor == null)
            {
                return;
            }

            AccessZoneRecoveryEntry entry;
            if (!Active.TryGetValue(walk, out entry))
            {
                return;
            }

            Entity entity = CharacterAccess.GetEntity(walk);
            Behavior behavior = entity == null ? null : entity.GetComponent<Behavior>();
            if (behavior == null)
            {
                Active.Remove(walk);
                return;
            }

            Vector2i currentTile = walk.GetCurrentTileSafe();
            if (!IsTileLegal(walk.Floor, currentTile, behavior.GetAccessRights()))
            {
                return;
            }

            if (!entry.TemporaryExit)
            {
                Active.Remove(walk);
                LogRecovery(
                    entity,
                    "left-restricted-zone",
                    currentTile,
                    currentTile,
                    behavior.GetAccessRights());
                return;
            }

            if (state != WalkState.Idle)
            {
                return;
            }

            CompleteTemporaryExit(
                walk,
                entry,
                entity,
                behavior,
                currentTile);
        }

        private static void CompleteTemporaryExit(
            WalkComponent walk,
            AccessZoneRecoveryEntry entry,
            Entity entity,
            Behavior behavior,
            Vector2i currentTile)
        {
            Active.Remove(walk);

            if (TryReplayDeferredMovement(
                    walk,
                    entity,
                    behavior,
                    currentTile))
            {
                return;
            }

            // The person is now physically outside the newly forbidden area.
            // A bladder procedure must not move back to a forbidden toilet,
            // sink or dryer, nor activate it when WalkState becomes NoPath.
            if (!IsSavedDestinationLegal(
                    entry.OriginalDestination,
                    entry.OriginalDestinationFloor,
                    behavior.GetAccessRights()) &&
                TryFinishInterruptedBladder(
                    walk,
                    entity,
                    currentTile))
            {
                LogRecovery(
                    entity,
                    "exit-complete-bladder-abandoned-inaccessible-fixture",
                    currentTile,
                    currentTile,
                    behavior.GetAccessRights());
                return;
            }

            if (entry.OriginalWalkState == WalkState.Idle)
            {
                if (walk.m_state.m_walkState != WalkState.Idle)
                {
                    walk.Stop();
                }

                LogRecovery(
                    entity,
                    "idle-exit-complete",
                    currentTile,
                    currentTile,
                    behavior.GetAccessRights());
                return;
            }

            walk.m_route = null;
            walk.m_state.m_destination = entry.OriginalDestination;
            walk.m_state.m_destinationFloor = entry.OriginalDestinationFloor;
            walk.m_state.m_walkMidpoint1 = null;
            walk.m_state.m_walkMidpoint2 = null;
            walk.m_state.m_movementType = entry.OriginalMovementType;
            walk.m_state.m_nextPosition = walk.m_state.m_currentPosition;

            if (IsSavedDestinationLegal(
                    entry.OriginalDestination,
                    entry.OriginalDestinationFloor,
                    behavior.GetAccessRights()) &&
                TryRestoreOriginalReservation(
                    walk,
                    entity,
                    entry.OriginalObjectToSitOn))
            {
                BlockedRouteManager.ClearRecovered(walk);
                BlockedRouteManager.ForceRepath(walk);

                LogRecovery(
                    entity,
                    "exit-complete-original-destination-recovered",
                    currentTile,
                    currentTile,
                    behavior.GetAccessRights());
                return;
            }

            if (walk.m_state.m_walkState != WalkState.NoPath)
            {
                walk.SwitchState(WalkState.NoPath);
            }

            BlockedRouteManager.RegisterAccessRecovery(
                walk,
                entry.OriginalObjectToSitOn);

            LogRecovery(
                entity,
                "exit-complete-original-destination-still-blocked",
                currentTile,
                currentTile,
                behavior.GetAccessRights());
        }

        private static bool TryFinishInterruptedBladder(
            WalkComponent walk,
            Entity entity,
            Vector2i currentTile)
        {
            Behavior behavior =
                entity == null ? null : entity.GetComponent<Behavior>();
            if (walk == null || walk.Floor == null ||
                behavior == null ||
                !IsTileLegal(
                    walk.Floor,
                    currentTile,
                    behavior.GetAccessRights()))
            {
                return false;
            }

            ProcedureComponent procedures =
                entity.GetComponent<ProcedureComponent>();
            ProcedureScriptNeedBladder bladder =
                procedures == null ||
                procedures.m_state == null ||
                procedures.m_state.m_currentProcedureScript == null
                    ? null
                    : procedures.m_state.m_currentProcedureScript.GetEntity()
                        as ProcedureScriptNeedBladder;
            if (bladder == null)
            {
                return false;
            }

            if (bladder.IsIdle())
            {
                BlockedRouteManager.ClearRecovered(walk);
                walk.Stop();
                return true;
            }

            UseComponent use = entity.GetComponent<UseComponent>();
            if (use != null && use.IsBusy())
            {
                return false;
            }

            if (use != null &&
                (use.m_state.m_reservedObject != null || use.IsUsing()))
            {
                use.Interrupt();
            }

            // Vanilla ProcedureComponent.Update() releases equipment owners,
            // removes the script and resumes the character on the next update.
            bladder.SwitchState(ProcedureScriptNeedBladder.STATE_IDLE);
            BlockedRouteManager.ClearRecovered(walk);
            walk.Stop();
            return true;
        }

        private static void ReleaseCurrentSitReservation(
            WalkComponent walk,
            Entity entity)
        {
            if (walk == null ||
                walk.m_state == null ||
                walk.m_state.m_objectToSitOn == null)
            {
                return;
            }

            TileObject objectToSitOn =
                walk.m_state.m_objectToSitOn.GetEntity();

            if (objectToSitOn != null && objectToSitOn.User == entity)
            {
                objectToSitOn.User = null;
            }

            walk.m_state.m_objectToSitOn = null;
        }

        private static bool TryRestoreOriginalReservation(
            WalkComponent walk,
            Entity entity,
            EntityIDPointer<TileObject> reservedObject)
        {
            if (reservedObject == null)
            {
                return true;
            }

            TileObject objectToSitOn = reservedObject.GetEntity();
            if (objectToSitOn == null)
            {
                return false;
            }

            if (objectToSitOn.User != null && objectToSitOn.User != entity)
            {
                return false;
            }

            objectToSitOn.User = entity;
            walk.m_state.m_objectToSitOn = reservedObject;
            return true;
        }

        internal static bool IsTileLegal(
            Floor floor,
            Vector2i tile,
            AccessRights accessRights)
        {
            return NavigationAccessPolicy.IsTileAccessible(
                floor,
                tile,
                accessRights);
        }

        private static bool IsSavedDestinationLegal(
            Vector2f destination,
            int destinationFloorIndex,
            AccessRights accessRights)
        {
            if (Hospital.Instance == null ||
                destinationFloorIndex < 0 ||
                destinationFloorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor destinationFloor =
                Hospital.Instance.m_floors[destinationFloorIndex];

            Vector2i destinationTile = new Vector2i(
                (int)(destination.m_x + 0.5f),
                (int)(destination.m_y + 0.5f));

            return IsTileLegal(
                destinationFloor,
                destinationTile,
                accessRights);
        }

        private static bool TryFindExit(
            Floor floor,
            Vector2i start,
            AccessRights accessRights,
            out HashSet<Vector2i> allowedForbiddenTiles,
            out Vector2i exitTile,
            out int temporaryAccessLevel,
            out bool oneWayPreventedExit)
        {
            allowedForbiddenTiles = new HashSet<Vector2i>();
            exitTile = Vector2i.ZERO_VECTOR;
            temporaryAccessLevel = GetRequiredAccess(floor, start);
            oneWayPreventedExit = false;

            if (temporaryAccessLevel <= (int)accessRights)
            {
                return false;
            }

            Queue<Vector2i> queue = new Queue<Vector2i>();
            allowedForbiddenTiles.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Vector2i current = queue.Dequeue();

                for (int i = 0; i < CardinalDirections.Length; i++)
                {
                    Vector2i next = current + CardinalDirections[i];

                    if (!IsInsideFloor(floor, next) ||
                        !CanPhysicallyTraverse(floor, start, current, next))
                    {
                        continue;
                    }

                    if (!OneWayManager.IsTransitionAllowed(floor, current, next))
                    {
                        bool legalNext = IsTileLegal(floor, next, accessRights);
                        int requiredNext = GetRequiredAccess(floor, next);

                        // The OneWay edge is relevant when it blocks either the legal
                        // exit itself or a tile that would otherwise belong to the
                        // same bounded recovery component.
                        if (legalNext || requiredNext <= temporaryAccessLevel)
                        {
                            oneWayPreventedExit = true;
                        }

                        continue;
                    }

                    if (IsTileLegal(floor, next, accessRights))
                    {
                        if (IsSafeExitTile(floor, start, current, next))
                        {
                            exitTile = next;
                            return true;
                        }

                        continue;
                    }

                    if (allowedForbiddenTiles.Contains(next))
                    {
                        continue;
                    }

                    int requiredAccess = GetRequiredAccess(floor, next);

                    // Exit recovery may move through the same or a less restrictive
                    // forbidden area, never into a more restrictive one.
                    if (requiredAccess > temporaryAccessLevel)
                    {
                        continue;
                    }

                    allowedForbiddenTiles.Add(next);
                    queue.Enqueue(next);
                }
            }

            return false;
        }

        private static bool IsSafeExitTile(
            Floor floor,
            Vector2i recoveryStart,
            Vector2i forbiddenNeighbor,
            Vector2i exitTile)
        {
            Tile tile = floor.m_mapPersistentData.m_tiles[exitTile.m_x, exitTile.m_y];
            if (tile != null &&
                tile.m_user != null &&
                tile.m_user.GetEntity() is ProcedureScript)
            {
                return false;
            }

            try
            {
                // Pathfinder searches backwards from the destination. Validate the
                // first reverse edge exactly as it will be checked when exitTile is
                // the temporary destination, so blocking center objects are respected.
                return floor.IsAccessible(
                    exitTile,
                    forbiddenNeighbor,
                    exitTile,
                    recoveryStart,
                    (int)AccessRights.STAFF_ONLY,
                    ignoreObjects: false,
                    ignoreAccessRights: true);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool CanPhysicallyTraverse(
            Floor floor,
            Vector2i recoveryStart,
            Vector2i current,
            Vector2i next)
        {
            try
            {
                return floor.IsAccessible(
                    current,
                    next,
                    recoveryStart,
                    next,
                    (int)AccessRights.STAFF_ONLY,
                    ignoreObjects: false,
                    ignoreAccessRights: true);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int GetRequiredAccess(Floor floor, Vector2i tile)
        {
            return NavigationAccessPolicy.GetRequiredTileAccess(floor, tile);
        }

        private static bool IsInsideFloor(Floor floor, Vector2i tile)
        {
            return floor != null &&
                   tile.m_x >= 0 &&
                   tile.m_y >= 0 &&
                   tile.m_x < floor.Size.m_x &&
                   tile.m_y < floor.Size.m_y;
        }

        private static void LogRecovery(
            Entity entity,
            string result,
            Vector2i current,
            Vector2i exit,
            AccessRights accessRights)
        {
            if (!TrafficControlConfig.PathfindingDebug)
            {
                return;
            }

            string characterName =
                entity == null ? "<unknown>" : (entity.Name ?? string.Empty).Trim();

            Plugin.Log?.LogInfo(
                "[PathDebug] ACCESS_ZONE_RECOVERY entity='" + characterName +
                "' result=" + result +
                " current=" + current +
                " exit=" + exit +
                " access=" + accessRights + "(" + (int)accessRights + ").");
        }
    }

    internal sealed class AccessZoneRecoveryJobRegistration
    {
        internal readonly AccessZoneRecoveryJobData Data;
        internal readonly int Generation;

        internal AccessZoneRecoveryJobRegistration(
            AccessZoneRecoveryJobData data,
            int generation)
        {
            Data = data;
            Generation = generation;
        }
    }

    internal static class AccessZoneRecoveryTracker
    {
        private static readonly Hashtable Jobs =
            Hashtable.Synchronized(new Hashtable());

        private static volatile int s_generation;

        [ThreadStatic]
        private static AccessZoneRecoveryJobData s_pendingJobData;

        [ThreadStatic]
        private static int s_pendingGeneration;

        [ThreadStatic]
        private static AccessZoneRecoveryJobData s_currentJobData;

        [ThreadStatic]
        private static int s_currentGeneration;

        internal static void PrepareNextJob(WalkComponent walk)
        {
            s_pendingJobData = AccessZoneRecoveryManager.GetJobData(walk);
            s_pendingGeneration = s_generation;
        }

        internal static void RegisterPendingJob(PathfinderJob job)
        {
            if (job == null || s_pendingJobData == null)
            {
                return;
            }

            Jobs[job] = new AccessZoneRecoveryJobRegistration(
                s_pendingJobData,
                s_pendingGeneration);
            s_pendingJobData = null;
            s_pendingGeneration = 0;
        }

        internal static void CancelPendingRegistration()
        {
            s_pendingJobData = null;
            s_pendingGeneration = 0;
        }

        internal static void Begin(PathfinderJob job)
        {
            AccessZoneRecoveryJobRegistration registration =
                job != null && Jobs.ContainsKey(job)
                    ? Jobs[job] as AccessZoneRecoveryJobRegistration
                    : null;

            if (registration != null &&
                registration.Generation == s_generation)
            {
                s_currentJobData = registration.Data;
                s_currentGeneration = registration.Generation;
            }
            else
            {
                s_currentJobData = null;
                s_currentGeneration = 0;
            }
        }

        internal static void End(PathfinderJob job)
        {
            try
            {
                if (job != null)
                {
                    Jobs.Remove(job);
                }
            }
            finally
            {
                s_currentJobData = null;
                s_currentGeneration = 0;
            }
        }

        internal static void Forget(PathfinderJob job)
        {
            if (job != null)
            {
                Jobs.Remove(job);
            }

            s_currentJobData = null;
            s_currentGeneration = 0;
        }

        internal static void Reset()
        {
            s_generation++;
            Jobs.Clear();
            s_pendingJobData = null;
            s_pendingGeneration = 0;
            s_currentJobData = null;
            s_currentGeneration = 0;
        }

        internal static void AdjustAccessRights(
            Floor floor,
            Vector2i currentPosition,
            ref int accessRightsLevel)
        {
            AccessZoneRecoveryJobData data = s_currentJobData;
            if (data == null ||
                s_currentGeneration != s_generation ||
                floor == null ||
                floor.m_floorIndex != data.FloorIndex ||
                !data.AllowedForbiddenTiles.Contains(currentPosition))
            {
                return;
            }

            if (accessRightsLevel < data.TemporaryAccessLevel)
            {
                accessRightsLevel = data.TemporaryAccessLevel;
            }
        }

        internal static bool ShouldDenyReentry(
            Floor floor,
            Vector2i currentPosition,
            Vector2i nextPosition)
        {
            AccessZoneRecoveryJobData data = s_currentJobData;
            if (data == null ||
                s_currentGeneration != s_generation ||
                floor == null ||
                floor.m_floorIndex != data.FloorIndex)
            {
                return false;
            }

            // Pathfinder expands backwards. Real movement for this check is
            // nextPosition -> currentPosition. Once real movement has reached a
            // legal tile, it may never enter the temporary forbidden component again.
            bool actualFromForbidden =
                data.AllowedForbiddenTiles.Contains(nextPosition);
            bool actualToForbidden =
                data.AllowedForbiddenTiles.Contains(currentPosition);

            return !actualFromForbidden && actualToForbidden;
        }
    }
}
