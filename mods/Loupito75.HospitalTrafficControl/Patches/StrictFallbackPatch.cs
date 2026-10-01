using System;
using System.Collections;
using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(WalkComponent), "SetupJob")]
    internal static class StrictFallbackPatch
    {
        // Third SetupJob argument: ignoreAccessRights.
        private static void Prefix(WalkComponent __instance, ref bool __2)
        {
            // Clear stale pending state before preparing the next pathfinder job.
            BiohazardEndpointAccessTracker.CancelPendingRegistration();
            AccessZoneRecoveryTracker.CancelPendingRegistration();
            AccessZoneRecoveryTracker.PrepareNextJob(__instance);

            if (!CharacterAccess.MustRespectStaffOnly(__instance))
            {
                return;
            }

            bool vanillaRequestedAccessFallback = __2;

            // Non-staff characters keep their real access rights during fallback.
            __2 = false;

            // SetupJob starts the worker before returning, so registration is armed
            // here and attached to the exact job in ThreadedJob.TryToStart.
            if (vanillaRequestedAccessFallback && BiohazardEndpointAccessTracker.IsPatient(__instance))
            {
                BiohazardEndpointAccessTracker.ArmNextPathfinderJobRegistration();
            }
        }

        private static void Postfix()
        {
            // Clear any marker left behind when no job was started.
            BiohazardEndpointAccessTracker.CancelPendingRegistration();
            AccessZoneRecoveryTracker.CancelPendingRegistration();
        }
    }

    [HarmonyPatch(typeof(ThreadedJob), nameof(ThreadedJob.TryToStart))]
    internal static class BiohazardFallbackJobStartPatch
    {
        private static void Prefix(ThreadedJob __instance)
        {
            PathfinderJob pathfinderJob = __instance as PathfinderJob;
            if (pathfinderJob != null)
            {
                BiohazardEndpointAccessTracker.RegisterPendingFallbackJob(pathfinderJob);
                AccessZoneRecoveryTracker.RegisterPendingJob(pathfinderJob);
            }
        }
    }

    internal static class BiohazardEndpointAccessTracker
    {
        private sealed class PatientFallbackContext
        {
            internal readonly Hashtable OriginRooms;
            internal readonly Hashtable DestinationRooms;

            internal PatientFallbackContext(
                Hashtable originRooms,
                Hashtable destinationRooms)
            {
                OriginRooms = originRooms;
                DestinationRooms = destinationRooms;
            }
        }

        private static readonly Hashtable PatientFallbackJobs =
            Hashtable.Synchronized(new Hashtable());

        [ThreadStatic]
        private static bool s_registerNextPathfinderJobAsPatientFallback;

        [ThreadStatic]
        private static PathfinderJob s_currentPatientFallbackJob;

        internal static bool IsPatient(WalkComponent walk)
        {
            Entity entity = CharacterAccess.GetEntity(walk);
            return entity != null && entity.GetComponent<BehaviorPatient>() != null;
        }

        internal static void ArmNextPathfinderJobRegistration()
        {
            s_registerNextPathfinderJobAsPatientFallback = true;
        }

        internal static void RegisterPendingFallbackJob(PathfinderJob job)
        {
            if (!s_registerNextPathfinderJobAsPatientFallback || job == null)
            {
                return;
            }

            // The Prefix runs before ThreadedJob starts its worker thread.
            PatientFallbackContext context = CreateFallbackContext(job);
            PatientFallbackJobs[job] = context == null ? (object)true : context;
            s_registerNextPathfinderJobAsPatientFallback = false;
        }

        internal static void CancelPendingRegistration()
        {
            s_registerNextPathfinderJobAsPatientFallback = false;
        }

        internal static void Begin(PathfinderJob job)
        {
            s_currentPatientFallbackJob =
                job != null && PatientFallbackJobs.ContainsKey(job) ? job : null;
        }

        internal static void End(PathfinderJob job)
        {
            try
            {
                if (job != null)
                {
                    PatientFallbackJobs.Remove(job);
                }
            }
            finally
            {
                if (ReferenceEquals(s_currentPatientFallbackJob, job))
                {
                    s_currentPatientFallbackJob = null;
                }
            }
        }

        internal static void Forget(PathfinderJob job)
        {
            if (job == null)
            {
                return;
            }

            PatientFallbackJobs.Remove(job);
            if (ReferenceEquals(s_currentPatientFallbackJob, job))
            {
                s_currentPatientFallbackJob = null;
            }
        }

        internal static void Reset()
        {
            PatientFallbackJobs.Clear();
            s_registerNextPathfinderJobAsPatientFallback = false;
            s_currentPatientFallbackJob = null;
        }

        internal static void AdjustAccessRightsForEndpointBiohazardRoom(
            Floor floor,
            Vector2i currentPosition,
            Vector2i nextPosition,
            Vector2i startPosition,
            Vector2i targetPosition,
            ref int accessRightsLevel)
        {
            if (s_currentPatientFallbackJob == null ||
                floor == null ||
                accessRightsLevel >= (int)AccessRights.BIOHAZARD ||
                !IsInsideFloor(floor, currentPosition) ||
                !IsInsideFloor(floor, nextPosition) ||
                !IsInsideFloor(floor, startPosition) ||
                !IsInsideFloor(floor, targetPosition))
            {
                return;
            }

            Room currentRoom = floor.m_roomTiles[currentPosition.m_x, currentPosition.m_y];
            if (currentRoom == null ||
                floor.m_roomAccessRights[currentPosition.m_x, currentPosition.m_y] != AccessRights.BIOHAZARD)
            {
                return;
            }

            bool allowBiohazard = false;
            PatientFallbackContext context =
                PatientFallbackJobs[s_currentPatientFallbackJob] as PatientFallbackContext;

            if (context != null)
            {
                // Pathfinder expands from destination back to the character.
                // A destination-side BIOHAZARD component must therefore be searchable
                // outward until the path reaches normal-access tiles.
                if (context.DestinationRooms.ContainsKey(currentRoom))
                {
                    allowBiohazard = true;
                }
                // For the character's starting BIOHAZARD component, allow only
                // BIOHAZARD-to-BIOHAZARD expansion. The reverse search can enter the
                // component from a normal tile, but it cannot use the component as a
                // shortcut after leaving it in real movement.
                else if (context.OriginRooms.ContainsKey(currentRoom) &&
                         floor.m_roomAccessRights[nextPosition.m_x, nextPosition.m_y] == AccessRights.BIOHAZARD)
                {
                    Room nextRoom = floor.m_roomTiles[nextPosition.m_x, nextPosition.m_y];
                    allowBiohazard =
                        nextRoom != null &&
                        context.OriginRooms.ContainsKey(nextRoom);
                }
            }

            if (!allowBiohazard)
            {
                // Keep the original exact-endpoint allowance as a safe fallback if
                // the navigation provider could not be resolved to a Floor.
                Room startRoom = floor.m_roomTiles[startPosition.m_x, startPosition.m_y];
                Room targetRoom = floor.m_roomTiles[targetPosition.m_x, targetPosition.m_y];

                allowBiohazard =
                    (ReferenceEquals(currentRoom, startRoom) &&
                     floor.m_roomAccessRights[startPosition.m_x, startPosition.m_y] == AccessRights.BIOHAZARD) ||
                    (ReferenceEquals(currentRoom, targetRoom) &&
                     floor.m_roomAccessRights[targetPosition.m_x, targetPosition.m_y] == AccessRights.BIOHAZARD);
            }

            if (allowBiohazard)
            {
                accessRightsLevel = (int)AccessRights.BIOHAZARD;
            }
        }

        private static PatientFallbackContext CreateFallbackContext(PathfinderJob job)
        {
            Floor floor = job == null ? null : job.m_navigationInfoProvider as Floor;
            if (floor == null ||
                floor.m_roomTiles == null ||
                floor.m_roomAccessRights == null)
            {
                return null;
            }

            Hashtable originRooms =
                BuildConnectedBiohazardRooms(floor, job.m_start);
            Hashtable destinationRooms =
                BuildConnectedBiohazardRooms(floor, job.m_end);

            if (originRooms.Count == 0 && destinationRooms.Count == 0)
            {
                return null;
            }

            return new PatientFallbackContext(originRooms, destinationRooms);
        }

        private static Hashtable BuildConnectedBiohazardRooms(
            Floor floor,
            Vector2i seedPosition)
        {
            Hashtable rooms = new Hashtable();
            if (!IsBiohazardRoomTile(floor, seedPosition))
            {
                return rooms;
            }

            bool[,] visited = new bool[floor.m_size.m_x, floor.m_size.m_y];
            Queue<Vector2i> pending = new Queue<Vector2i>();

            visited[seedPosition.m_x, seedPosition.m_y] = true;
            pending.Enqueue(seedPosition);

            while (pending.Count > 0)
            {
                Vector2i current = pending.Dequeue();
                Room room = floor.m_roomTiles[current.m_x, current.m_y];
                if (room != null)
                {
                    rooms[room] = true;
                }

                TryEnqueueConnectedBiohazardTile(
                    floor,
                    current,
                    new Vector2i(current.m_x - 1, current.m_y),
                    seedPosition,
                    visited,
                    pending);
                TryEnqueueConnectedBiohazardTile(
                    floor,
                    current,
                    new Vector2i(current.m_x + 1, current.m_y),
                    seedPosition,
                    visited,
                    pending);
                TryEnqueueConnectedBiohazardTile(
                    floor,
                    current,
                    new Vector2i(current.m_x, current.m_y - 1),
                    seedPosition,
                    visited,
                    pending);
                TryEnqueueConnectedBiohazardTile(
                    floor,
                    current,
                    new Vector2i(current.m_x, current.m_y + 1),
                    seedPosition,
                    visited,
                    pending);
            }

            return rooms;
        }

        private static void TryEnqueueConnectedBiohazardTile(
            Floor floor,
            Vector2i current,
            Vector2i next,
            Vector2i seedPosition,
            bool[,] visited,
            Queue<Vector2i> pending)
        {
            if (!IsInsideFloor(floor, next) ||
                visited[next.m_x, next.m_y] ||
                !IsBiohazardRoomTile(floor, next))
            {
                return;
            }

            // Use the game's own wall/door/object accessibility rules while granting
            // BIOHAZARD only for this connectivity probe. This runs before the worker
            // starts and does not alter the PathfinderJob's actual access level.
            if (!floor.IsAccessible(
                    current,
                    next,
                    seedPosition,
                    seedPosition,
                    (int)AccessRights.BIOHAZARD,
                    true,
                    false))
            {
                return;
            }

            visited[next.m_x, next.m_y] = true;
            pending.Enqueue(next);
        }

        private static bool IsBiohazardRoomTile(
            Floor floor,
            Vector2i position)
        {
            return IsInsideFloor(floor, position) &&
                   floor.m_roomTiles != null &&
                   floor.m_roomAccessRights != null &&
                   floor.m_roomTiles[position.m_x, position.m_y] != null &&
                   floor.m_roomAccessRights[position.m_x, position.m_y] == AccessRights.BIOHAZARD;
        }

        private static bool IsInsideFloor(Floor floor, Vector2i position)
        {
            return floor != null &&
                   position.m_x >= 0 &&
                   position.m_y >= 0 &&
                   position.m_x < floor.m_size.m_x &&
                   position.m_y < floor.m_size.m_y;
        }
    }
}
