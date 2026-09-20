using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class BlockedRouteManager
    {
        private sealed class BlockedEntry
        {
            internal Entity Entity;
            internal EntityIDPointer<TileObject> ReservedObject;
            internal bool OneWayBlocked;
            internal bool AccessRecoveryBlocked;
        }

        private static readonly Dictionary<WalkComponent, BlockedEntry> Blocked =
            new Dictionary<WalkComponent, BlockedEntry>();

        private static readonly FieldInfo PathfinderJobField =
            AccessTools.Field(typeof(WalkComponent), "m_pathfinderJob");

        internal static void Reset()
        {
            Blocked.Clear();
        }

        internal static bool IsBlocked(WalkComponent walk)
        {
            return walk != null && Blocked.ContainsKey(walk);
        }

        internal static bool IsAccessRecoveryBlocked(WalkComponent walk)
        {
            BlockedEntry entry;
            return walk != null &&
                   Blocked.TryGetValue(walk, out entry) &&
                   entry.AccessRecoveryBlocked;
        }

        internal static void Register(WalkComponent walk)
        {
            if (walk == null || !CharacterAccess.MustRespectStaffOnly(walk))
            {
                return;
            }

            RegisterInternal(
                walk,
                false,
                GetCurrentReservation(walk));
        }

        internal static void RegisterOneWay(WalkComponent walk)
        {
            RegisterInternal(
                walk,
                true,
                GetCurrentReservation(walk));
        }

        internal static void RegisterAccessRecovery(
            WalkComponent walk,
            EntityIDPointer<TileObject> reservedObject)
        {
            RegisterInternal(
                walk,
                false,
                reservedObject);

            BlockedEntry entry;
            if (walk != null && Blocked.TryGetValue(walk, out entry))
            {
                entry.AccessRecoveryBlocked = true;
            }
        }

        internal static EntityIDPointer<TileObject> GetReservedObject(
            WalkComponent walk)
        {
            if (walk == null || walk.m_state == null)
            {
                return null;
            }

            if (walk.m_state.m_objectToSitOn != null)
            {
                return walk.m_state.m_objectToSitOn;
            }

            BlockedEntry entry;
            return Blocked.TryGetValue(walk, out entry)
                ? entry.ReservedObject
                : null;
        }

        private static EntityIDPointer<TileObject> GetCurrentReservation(
            WalkComponent walk)
        {
            return walk == null || walk.m_state == null
                ? null
                : walk.m_state.m_objectToSitOn;
        }

        private static void RegisterInternal(
            WalkComponent walk,
            bool oneWayBlocked,
            EntityIDPointer<TileObject> reservedObject)
        {
            if (walk == null || walk.m_state == null)
            {
                return;
            }

            Entity entity = CharacterAccess.GetEntity(walk);
            if (entity == null)
            {
                return;
            }

            BehaviorPatient patient = entity.GetComponent<BehaviorPatient>();
            if (patient != null && patient.m_state != null)
            {
                PatientState patientState = patient.m_state.m_patientState;
                if (patientState == PatientState.Left || patientState == PatientState.Dead)
                {
                    return;
                }
            }

            if (Blocked.TryGetValue(walk, out BlockedEntry existing))
            {
                if (oneWayBlocked)
                {
                    existing.OneWayBlocked = true;
                }

                if (existing.ReservedObject == null && reservedObject != null)
                {
                    existing.ReservedObject = reservedObject;
                }

                return;
            }

            Blocked.Add(walk, new BlockedEntry
            {
                Entity = entity,
                ReservedObject = reservedObject,
                OneWayBlocked = oneWayBlocked,
                AccessRecoveryBlocked = false
            });

            walk.m_state.m_nextPosition = walk.m_state.m_currentPosition;
            if (!walk.m_state.m_lying)
            {
                entity.GetComponent<AnimModelComponent>()?.PlayAnimation("stand_idle");
            }

            NotificationService.AddBlockedPathMessage(entity, walk);
        }

        internal static void RetryBlockedOneWay(WalkComponent walk)
        {
            if (walk == null || walk.m_state == null)
            {
                return;
            }

            if (Blocked.TryGetValue(walk, out BlockedEntry entry) && entry.OneWayBlocked)
            {
                if (!RestoreReservation(walk, entry))
                {
                    return;
                }

                Blocked.Remove(walk);
            }

            ForceRepath(walk);
        }

        internal static void ClearRecovered(WalkComponent walk)
        {
            if (walk != null)
            {
                Blocked.Remove(walk);
            }
        }

        internal static void ClearOneWayBlock(WalkComponent walk)
        {
            if (walk != null &&
                Blocked.TryGetValue(walk, out BlockedEntry entry) &&
                entry.OneWayBlocked)
            {
                Blocked.Remove(walk);
            }
        }

        internal static void ClearForAccessRecovery(
            WalkComponent walk,
            bool restoreReservation)
        {
            if (walk == null)
            {
                return;
            }

            BlockedEntry entry;
            if (!Blocked.TryGetValue(walk, out entry))
            {
                return;
            }

            if (restoreReservation)
            {
                RestoreReservation(walk, entry);
            }
            else
            {
                ReleaseReservation(walk, entry);
            }

            Blocked.Remove(walk);
        }

        internal static void RepathFloor(Floor floor)
        {
            if (floor == null || Hospital.Instance == null)
            {
                return;
            }

            var characters = new List<Entity>(Hospital.Instance.m_characters);

            foreach (Entity entity in characters)
            {
                WalkComponent walk = entity?.GetComponent<WalkComponent>();
                if (walk == null ||
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

                if (AccessZoneRecoveryManager.IsRecovering(walk))
                {
                    continue;
                }

                WalkState state = walk.m_state.m_walkState;

                if (state == WalkState.NoPath && Blocked.TryGetValue(walk, out BlockedEntry entry))
                {
                    if (!RestoreReservation(walk, entry))
                    {
                        continue;
                    }

                    Blocked.Remove(walk);
                    ForceRepath(walk);
                    continue;
                }

                if (state == WalkState.Walking ||
                    state == WalkState.DestinationSet ||
                    state == WalkState.LookingForPath ||
                    state == WalkState.LookingForPathFallback ||
                    state == WalkState.LookingForPathFallback2)
                {
                    ForceRepath(walk);
                }
            }
        }

        internal static void ForceRepath(WalkComponent walk)
        {
            if (walk == null || walk.m_state == null)
            {
                return;
            }

            bool wasWalking = walk.m_state.m_walkState == WalkState.Walking;

            AbortCurrentJob(walk);
            walk.m_route = null;
            walk.m_blockedCount = 0;
            walk.m_state.m_nextPosition = walk.m_state.m_currentPosition;

            if (wasWalking && !walk.m_state.m_lying)
            {
                Entity entity = CharacterAccess.GetEntity(walk);
                entity?.GetComponent<AnimModelComponent>()?.PlayAnimation("stand_idle");
            }

            walk.SwitchState(WalkState.DestinationSet);
        }

        private static void ReleaseReservation(WalkComponent walk, BlockedEntry entry)
        {
            if (entry?.ReservedObject != null && entry.ReservedObject.GetEntity() != null)
            {
                TileObject reservedObject = entry.ReservedObject.GetEntity();
                if (reservedObject.User == entry.Entity)
                {
                    reservedObject.User = null;
                }
            }

            if (walk?.m_state != null)
            {
                walk.m_state.m_objectToSitOn = null;
            }
        }

        private static bool RestoreReservation(WalkComponent walk, BlockedEntry entry)
        {
            if (entry == null || entry.ReservedObject == null)
            {
                return true;
            }

            TileObject reservedObject = entry.ReservedObject.GetEntity();
            if (reservedObject == null)
            {
                return false;
            }

            if (reservedObject.User != null && reservedObject.User != entry.Entity)
            {
                return false;
            }

            reservedObject.User = entry.Entity;
            if (walk != null && walk.m_state != null)
            {
                walk.m_state.m_objectToSitOn = entry.ReservedObject;
            }

            return true;
        }

        private static void AbortCurrentJob(WalkComponent walk)
        {
            try
            {
                if (PathfinderJobField?.GetValue(walk) is PathfinderJob job)
                {
                    job.Abort();
                    PathfinderJobField.SetValue(walk, null);
                }
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogWarning($"Failed to abort a stale pathfinding job: {exception.Message}");
                PathfinderJobField?.SetValue(walk, null);
            }
        }
    }
}
