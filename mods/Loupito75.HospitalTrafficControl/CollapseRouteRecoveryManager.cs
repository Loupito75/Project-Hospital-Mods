using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class CollapseRouteRecoveryManager
    {
        internal static bool IsRecoveryCandidate(WalkComponent walk)
        {
            if (walk == null ||
                walk.m_state == null ||
                walk.Floor == null ||
                walk.m_state.m_lying)
            {
                return false;
            }

            Entity character = CharacterAccess.GetEntity(walk);
            BehaviorPatient patient = character == null
                ? null
                : character.GetComponent<BehaviorPatient>();
            Behavior behavior = character == null
                ? null
                : character.GetComponent<Behavior>();

            if (character == null ||
                patient == null ||
                patient.m_state == null ||
                patient.m_state.m_patientState != PatientState.GoingToCollapse ||
                behavior == null)
            {
                return false;
            }

            Floor floor = walk.Floor;
            int floorIndex = floor.m_floorIndex;
            if (walk.m_state.m_destinationFloor != floorIndex)
            {
                return false;
            }

            Vector2i destination = walk.GetDestinationTile();
            return !IsTileLegalForAccess(
                floor,
                destination,
                behavior.GetAccessRights());
        }

        internal static bool TryRecoverNoPath(WalkComponent walk)
        {
            if (walk == null ||
                walk.m_state == null ||
                walk.Floor == null ||
                walk.m_state.m_lying)
            {
                return false;
            }

            Entity character = CharacterAccess.GetEntity(walk);
            BehaviorPatient patient = character == null
                ? null
                : character.GetComponent<BehaviorPatient>();
            Behavior behavior = character == null
                ? null
                : character.GetComponent<Behavior>();

            if (character == null ||
                patient == null ||
                patient.m_state == null ||
                patient.m_state.m_patientState != PatientState.GoingToCollapse ||
                behavior == null)
            {
                return false;
            }

            Floor floor = walk.Floor;
            int floorIndex = floor.m_floorIndex;
            if (walk.m_state.m_destinationFloor != floorIndex)
            {
                return false;
            }

            Vector2i oldDestination = walk.GetDestinationTile();
            AccessRights accessRights = behavior.GetAccessRights();

            // HTC only owns the recovery when the collapse destination itself is
            // no longer legal under the character's real access rights.
            if (IsTileLegalForAccess(floor, oldDestination, accessRights))
            {
                return false;
            }

            Vector2i replacement = FindClosestReachableFree3x3(
                character,
                walk,
                floor,
                accessRights);

            if (replacement == Vector2i.ZERO_VECTOR ||
                replacement == oldDestination)
            {
                return false;
            }

            ReleaseOwned3x3(character, floorIndex, oldDestination);
            Reserve3x3(character, floorIndex, replacement);
            walk.SetDestination(replacement, floorIndex);

            if (TrafficControlConfig.PathfindingDebug)
            {
                string characterName =
                    (character.Name ?? string.Empty).Trim();

                Plugin.Log?.LogInfo(
                    "[PathDebug] COLLAPSE_ROUTE_RECOVERY entity='" +
                    characterName +
                    "' access=" + accessRights + "(" + (int)accessRights + ")" +
                    " oldDestination=" + oldDestination +
                    " newDestination=" + replacement +
                    " floor=" + floorIndex +
                    ". Replaced only the invalid movement destination; native collapse procedure remains reserved.");
            }

            return true;
        }

        private static Vector2i FindClosestReachableFree3x3(
            Entity character,
            WalkComponent walk,
            Floor floor,
            AccessRights accessRights)
        {
            GridMap gridMap = GridMap.GetInstance();
            if (gridMap == null)
            {
                return Vector2i.ZERO_VECTOR;
            }

            Vector2i origin = walk.GetCurrentTile();
            int floorIndex = floor.m_floorIndex;
            int bestSquaredDistance = int.MaxValue;
            Vector2i result = Vector2i.ZERO_VECTOR;

            for (int x = 1; x < floor.Size.m_x - 1; x++)
            {
                for (int y = 1; y < floor.Size.m_y - 1; y++)
                {
                    int dx = x - origin.m_x;
                    int dy = y - origin.m_y;
                    int squaredDistance = dx * dx + dy * dy;
                    if (squaredDistance >= bestSquaredDistance)
                    {
                        continue;
                    }

                    Vector2i candidate = new Vector2i(x, y);
                    if (!IsNativeCompatibleFree3x3(
                            character,
                            floor,
                            candidate) ||
                        !IsTileLegalForAccess(
                            floor,
                            candidate,
                            accessRights))
                    {
                        continue;
                    }

                    float routeDistance = gridMap.GetDistance(
                        floorIndex,
                        origin,
                        floorIndex,
                        candidate,
                        accessRights);

                    if (routeDistance < 0f)
                    {
                        continue;
                    }

                    bestSquaredDistance = squaredDistance;
                    result = candidate;
                }
            }

            return result;
        }

        private static bool IsNativeCompatibleFree3x3(
            Entity character,
            Floor floor,
            Vector2i center)
        {
            int floorIndex = floor.m_floorIndex;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = center.m_x + dx;
                    int y = center.m_y + dy;

                    if (dx > -1 &&
                        floor.m_mapPersistentData.m_tileWalls[x, y].m_wallSE != null)
                    {
                        return false;
                    }

                    if (dy > -1 &&
                        floor.m_mapPersistentData.m_tileWalls[x, y].m_wallSW != null)
                    {
                        return false;
                    }

                    Vector2i tile = new Vector2i(x, y);
                    Entity reservedBy =
                        MapScriptInterface.Instance.GetTileReservedBy(
                            tile,
                            floorIndex);

                    if (reservedBy != null &&
                        !object.ReferenceEquals(reservedBy, character))
                    {
                        return false;
                    }

                    if (floor.m_mapPersistentData.m_foundationsLayer
                            .m_foundations[x, y] != 2 ||
                        (int)floor.m_roomAccessRights[x, y] > (int)AccessRights.PATIENT ||
                        floor.m_tileObjects[x, y].IsAnyObjectBlocking() ||
                        floor.m_tileObjects[x, y].m_centerObject != null ||
                        floor.m_accessibility[x, y] == 2)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsTileLegalForAccess(
            Floor floor,
            Vector2i tile,
            AccessRights grantedRights)
        {
            return NavigationAccessPolicy.IsTileAccessible(
                floor,
                tile,
                grantedRights);
        }

        private static void ReleaseOwned3x3(
            Entity character,
            int floorIndex,
            Vector2i center)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    Vector2i tile =
                        new Vector2i(center.m_x + dx, center.m_y + dy);

                    Entity reservedBy =
                        MapScriptInterface.Instance.GetTileReservedBy(
                            tile,
                            floorIndex);
                    if (object.ReferenceEquals(reservedBy, character))
                    {
                        MapScriptInterface.Instance.FreeTile(
                            tile,
                            floorIndex);
                    }
                }
            }
        }

        private static void Reserve3x3(
            Entity character,
            int floorIndex,
            Vector2i center)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    MapScriptInterface.Instance.ReserveTile(
                        new Vector2i(
                            center.m_x + dx,
                            center.m_y + dy),
                        character,
                        floorIndex);
                }
            }
        }
    }
}
