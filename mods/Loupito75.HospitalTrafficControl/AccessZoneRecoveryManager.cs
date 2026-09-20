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

    internal sealed class AccessZoneRecoveryEntry
    {
        internal int FloorIndex;
        internal int TemporaryAccessLevel;
        internal HashSet<Vector2i> AllowedForbiddenTiles;
        internal bool TemporaryExit;
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

        private static readonly Vector2i[] CardinalDirections =
        {
            new Vector2i(-1, 0),
            new Vector2i(1, 0),
            new Vector2i(0, -1),
            new Vector2i(0, 1)
        };

        internal static bool IsRecovering(WalkComponent walk)
        {
            return walk != null && Active.ContainsKey(walk);
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
            Active.Clear();
            AccessZoneRecoveryTracker.Reset();
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
                    walk.Floor != floor ||
                    !CharacterAccess.CanBeRestrictedByAccessChange(walk))
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
                    accessChange != null &&
                    accessChange.WasTileNewlyRestricted(
                        currentTile,
                        accessRights);

                if (!hasExistingRecovery &&
                    !accessRecoveryBlocked &&
                    !newlyRestricted)
                {
                    continue;
                }

                if (IsTileLegal(floor, currentTile, accessRights))
                {
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

                    walk.SetDestination(
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
            if (!IsInsideFloor(floor, tile))
            {
                return false;
            }

            int granted = (int)accessRights;
            int roomAccess = (int)floor.m_roomAccessRights[tile.m_x, tile.m_y];
            AccessRights logistics =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    tile.m_x,
                    tile.m_y];

            if (roomAccess > granted)
            {
                return false;
            }

            return (int)logistics <= granted || logistics == AccessRights.BIOHAZARD;
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
            int roomAccess = (int)floor.m_roomAccessRights[tile.m_x, tile.m_y];
            AccessRights logistics =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    tile.m_x,
                    tile.m_y];

            int logisticsAccess =
                logistics == AccessRights.BIOHAZARD
                    ? (int)AccessRights.PEDESTRIAN
                    : (int)logistics;

            return Math.Max(roomAccess, logisticsAccess);
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

            Plugin.Log?.LogWarning(
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
