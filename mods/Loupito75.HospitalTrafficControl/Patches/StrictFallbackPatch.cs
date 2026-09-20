using System;
using System.Collections;
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
            PatientFallbackJobs[job] = true;
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
            Vector2i startPosition,
            Vector2i targetPosition,
            ref int accessRightsLevel)
        {
            if (s_currentPatientFallbackJob == null ||
                floor == null ||
                accessRightsLevel >= (int)AccessRights.BIOHAZARD ||
                !IsInsideFloor(floor, currentPosition) ||
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

            Room startRoom = floor.m_roomTiles[startPosition.m_x, startPosition.m_y];
            Room targetRoom = floor.m_roomTiles[targetPosition.m_x, targetPosition.m_y];

            bool currentRoomContainsBiohazardEndpoint =
                (ReferenceEquals(currentRoom, startRoom) &&
                 floor.m_roomAccessRights[startPosition.m_x, startPosition.m_y] == AccessRights.BIOHAZARD) ||
                (ReferenceEquals(currentRoom, targetRoom) &&
                 floor.m_roomAccessRights[targetPosition.m_x, targetPosition.m_y] == AccessRights.BIOHAZARD);

            if (currentRoomContainsBiohazardEndpoint)
            {
                // Raise only this endpoint check to BIOHAZARD; STAFF and STAFF_ONLY
                // remain forbidden and the rest of Floor.IsAccessible stays intact.
                accessRightsLevel = (int)AccessRights.BIOHAZARD;
            }
        }

        private static bool IsInsideFloor(Floor floor, Vector2i position)
        {
            return position.m_x >= 0 &&
                   position.m_y >= 0 &&
                   position.m_x < floor.m_size.m_x &&
                   position.m_y < floor.m_size.m_y;
        }
    }
}
