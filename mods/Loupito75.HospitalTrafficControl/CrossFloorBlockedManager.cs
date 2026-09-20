using System;
using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class CrossFloorBlockedManager
    {
        private sealed class BlockedEntry
        {
            internal Entity Entity;
            internal EntityIDPointer<TileObject> ReservedObject;
        }

        private static readonly Dictionary<WalkComponent, BlockedEntry> Blocked =
            new Dictionary<WalkComponent, BlockedEntry>();

        internal static void Reset()
        {
            Blocked.Clear();
        }

        internal static void Clear(WalkComponent walk)
        {
            if (walk != null)
            {
                Blocked.Remove(walk);
            }
        }

        internal static bool IsBlocked(WalkComponent walk)
        {
            if (walk == null)
            {
                return false;
            }

            BlockedEntry entry;
            if (!Blocked.TryGetValue(walk, out entry))
            {
                return false;
            }

            if (walk.m_state == null ||
                walk.Floor == null ||
                walk.m_state.m_walkState != WalkState.NoPath ||
                walk.Floor.m_floorIndex == walk.m_state.m_destinationFloor ||
                walk.m_state.m_walkMidpoint1 != null)
            {
                Blocked.Remove(walk);
                return false;
            }

            return true;
        }

        internal static bool TryRegister(WalkComponent walk)
        {
            if (walk == null || walk.m_state == null || walk.Floor == null ||
                walk.m_state.m_walkState != WalkState.NoPath || walk.m_state.m_lying)
            {
                Clear(walk);
                return false;
            }

            if (walk.Floor.m_floorIndex == walk.m_state.m_destinationFloor ||
                walk.m_state.m_walkMidpoint1 != null)
            {
                Clear(walk);
                return false;
            }

            Entity entity = CharacterAccess.GetEntity(walk);
            if (entity == null)
            {
                Clear(walk);
                return false;
            }

            BehaviorPatient patient = entity.GetComponent<BehaviorPatient>();
            if (patient != null && patient.m_state != null &&
                (patient.m_state.m_patientState == PatientState.Left ||
                 patient.m_state.m_patientState == PatientState.Dead))
            {
                Clear(walk);
                return false;
            }

            BlockedEntry existing;
            if (Blocked.TryGetValue(walk, out existing))
            {
                if (existing.ReservedObject == null && walk.m_state.m_objectToSitOn != null)
                {
                    existing.ReservedObject = walk.m_state.m_objectToSitOn;
                }
                return true;
            }

            Blocked.Add(walk, new BlockedEntry
            {
                Entity = entity,
                ReservedObject = walk.m_state.m_objectToSitOn
            });

            walk.m_state.m_nextPosition = walk.m_state.m_currentPosition;
            if (!walk.m_state.m_lying)
            {
                AnimModelComponent animation = entity.GetComponent<AnimModelComponent>();
                if (animation != null)
                {
                    animation.PlayAnimation("stand_idle");
                }
            }

            NotificationService.AddBlockedPathMessage(entity, walk);
            return true;
        }

        internal static void RetryForNavigationFloor(Floor rebuiltFloor)
        {
            if (rebuiltFloor == null || Blocked.Count == 0)
            {
                return;
            }

            int rebuiltFloorIndex = rebuiltFloor.m_floorIndex;
            List<WalkComponent> blockedWalks = new List<WalkComponent>(Blocked.Keys);

            foreach (WalkComponent walk in blockedWalks)
            {
                BlockedEntry entry;
                if (walk == null || !Blocked.TryGetValue(walk, out entry))
                {
                    continue;
                }

                if (walk.m_state == null || walk.Floor == null ||
                    walk.m_state.m_walkState != WalkState.NoPath)
                {
                    Blocked.Remove(walk);
                    continue;
                }

                int currentFloor = walk.Floor.m_floorIndex;
                int destinationFloor = walk.m_state.m_destinationFloor;

                if (currentFloor != rebuiltFloorIndex &&
                    destinationFloor != rebuiltFloorIndex)
                {
                    continue;
                }

                if (currentFloor == destinationFloor ||
                    walk.m_state.m_walkMidpoint1 != null)
                {
                    ResumeRoute(walk, entry);
                    continue;
                }

                try
                {
                    walk.CheckElevator();
                }
                catch (Exception exception)
                {
                    LogRetry(walk, "error=" + exception.GetType().Name + ":" + exception.Message);
                    continue;
                }

                if (walk.Floor.m_floorIndex != walk.m_state.m_destinationFloor &&
                    walk.m_state.m_walkMidpoint1 == null)
                {
                    LogRetry(walk, "still-blocked");
                    continue;
                }

                ResumeRoute(walk, entry);
            }
        }

        private static void ResumeRoute(WalkComponent walk, BlockedEntry entry)
        {
            RestoreReservation(walk, entry);
            Blocked.Remove(walk);
            LogRetry(walk, "recovered");
            BlockedRouteManager.ForceRepath(walk);
        }

        private static void RestoreReservation(WalkComponent walk, BlockedEntry entry)
        {
            if (walk == null || walk.m_state == null ||
                entry == null || entry.ReservedObject == null ||
                entry.ReservedObject.GetEntity() == null)
            {
                return;
            }

            TileObject reservedObject = entry.ReservedObject.GetEntity();
            if (reservedObject.User == null || reservedObject.User == entry.Entity)
            {
                reservedObject.User = entry.Entity;
                walk.m_state.m_objectToSitOn = entry.ReservedObject;
            }
        }

        private static void LogRetry(WalkComponent walk, string result)
        {
            if (!TrafficControlConfig.PathfindingDebug)
            {
                return;
            }

            Entity entity = CharacterAccess.GetEntity(walk);
            string characterName = entity == null
                ? "<unknown>"
                : (entity.Name ?? string.Empty).Trim();

            Plugin.Log?.LogWarning(
                "[PathDebug] CROSS_FLOOR_RETRY entity='" + characterName +
                "' floor=" + (walk.Floor == null ? -1 : walk.Floor.m_floorIndex) +
                " destinationFloor=" +
                (walk.m_state == null ? -1 : walk.m_state.m_destinationFloor) +
                " result=" + result + ".");
        }
    }

    [HarmonyPatch(typeof(MapEditorController), nameof(MapEditorController.Destroy))]
    internal static class CrossFloorMapDestroyedPatch
    {
        private static void Prefix()
        {
            CrossFloorBlockedManager.Reset();
        }
    }
}
