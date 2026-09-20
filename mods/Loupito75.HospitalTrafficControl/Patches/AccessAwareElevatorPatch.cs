using System;
using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(WalkComponent), nameof(WalkComponent.CheckElevator))]
    internal static class AccessAwareElevatorPatch
    {
        private static bool Prefix(WalkComponent __instance)
        {
            if (__instance == null ||
                __instance.m_state == null ||
                __instance.Floor == null ||
                Hospital.Instance == null)
            {
                return true;
            }

            Entity entity = CharacterAccess.GetEntity(__instance);
            Behavior behavior = entity == null ? null : entity.GetComponent<Behavior>();
            if (behavior == null)
            {
                return true;
            }

            AccessRights actualAccess = behavior.GetAccessRights();
            if ((int)actualAccess >= (int)AccessRights.STAFF_ONLY)
            {
                return true;
            }

            int currentFloor = __instance.Floor.m_floorIndex;
            int destinationFloor = __instance.m_state.m_destinationFloor;

            if (currentFloor == destinationFloor)
            {
                __instance.m_state.m_walkMidpoint1 = null;
                __instance.m_state.m_walkMidpoint2 = null;
                return false;
            }

            try
            {
                KeyValuePair<List<WalkMidpoint>, float> waypoints;

                // GridMap has only PATIENT and STAFF_ONLY graphs. For every character
                // below STAFF_ONLY, prefer a PATIENT-valid elevator route when one
                // exists. It is guaranteed to be legal for the higher rights too and
                // avoids vanilla's unconditional use of the higher graph.
                if ((int)actualAccess > (int)AccessRights.PATIENT)
                {
                    waypoints = GetWaypoints(__instance, AccessRights.PATIENT);
                    if (waypoints.Key == null || waypoints.Key.Count == 0)
                    {
                        waypoints = GetWaypoints(__instance, actualAccess);
                    }
                }
                else
                {
                    waypoints = GetWaypoints(__instance, actualAccess);
                }

                ApplyWaypoints(__instance, waypoints.Key);

                if (TrafficControlConfig.PathfindingDebug)
                {
                    string characterName =
                        entity == null ? "<unknown>" : (entity.Name ?? string.Empty).Trim();

                    Plugin.Log?.LogWarning(
                        "[PathDebug] ACCESS_AWARE_ELEVATOR entity='" + characterName +
                        "' access=" + actualAccess + "(" + (int)actualAccess + ")" +
                        " currentFloor=" + currentFloor +
                        " destinationFloor=" + destinationFloor +
                        " midpointCount=" +
                        (waypoints.Key == null ? 0 : waypoints.Key.Count) + ".");
                }

                return false;
            }
            catch (Exception exception)
            {
                if (TrafficControlConfig.PathfindingDebug)
                {
                    Plugin.Log?.LogWarning(
                        "[PathDebug] ACCESS_AWARE_ELEVATOR failed; using vanilla CheckElevator: " +
                        exception.GetType().Name + ": " + exception.Message);
                }

                return true;
            }
        }

        private static KeyValuePair<List<WalkMidpoint>, float> GetWaypoints(
            WalkComponent walk,
            AccessRights accessRights)
        {
            return GridMap.GetInstance().GetWaypoints(
                walk.Floor.m_floorIndex,
                walk.GetCurrentTile(),
                walk.m_state.m_destinationFloor,
                walk.GetDestinationTile(),
                accessRights);
        }

        private static void ApplyWaypoints(
            WalkComponent walk,
            List<WalkMidpoint> waypoints)
        {
            walk.m_state.m_walkMidpoint1 =
                waypoints != null && waypoints.Count > 0 ? waypoints[0] : null;

            walk.m_state.m_walkMidpoint2 =
                waypoints != null && waypoints.Count > 1 ? waypoints[1] : null;
        }
    }
}
