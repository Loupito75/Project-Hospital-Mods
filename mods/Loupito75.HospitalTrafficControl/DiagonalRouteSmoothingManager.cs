using System;
using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class DiagonalRouteSmoothingManager
    {
        internal static void TrySmooth(
            WalkComponent walk,
            PathfinderRoute route)
        {
            if (!TrafficControlConfig.ExperimentalDiagonalCornerSmoothing ||
                walk == null ||
                walk.m_state == null ||
                walk.m_state.m_movementType != MovementType.WALKING ||
                walk.m_state.m_lying ||
                route == null ||
                route.Nodes == null ||
                route.Nodes.Count < 3)
            {
                return;
            }

            Entity character = CharacterAccess.GetEntity(walk);
            Floor floor = walk.Floor;
            if (character == null ||
                floor == null ||
                AttachedMovingObjectTracker.HasAttachedObject(character))
            {
                return;
            }

            Behavior behavior = character.GetComponent<Behavior>();
            if (behavior == null ||
                !DiagonalAnimationManager.SupportsCardinalWalk(character))
            {
                return;
            }

            string walkAnimation =
                behavior.GetSpecificAnimation("walk");
            if (walkAnimation != "walk")
            {
                return;
            }

            List<PathfinderNode> nodes = route.Nodes;
            PathfinderNode routeStartNode = nodes[0];
            PathfinderNode routeEndNode = nodes[nodes.Count - 1];
            if (routeStartNode == null || routeEndNode == null)
            {
                return;
            }

            Vector2i routeStart = routeStartNode.Position;
            Vector2i routeEnd = routeEndNode.Position;
            int accessRights = (int)behavior.GetAccessRights();

            // Compact in place instead of RemoveAt() per accepted corner.
            // A staircase can therefore be smoothed in one O(N) pass with no
            // temporary collection and one tail removal at the end.
            int readIndex = 1;
            int writeIndex = 1;
            PathfinderNode firstNode = nodes[0];

            while (readIndex < nodes.Count)
            {
                if (firstNode != null &&
                    readIndex + 1 < nodes.Count)
                {
                    PathfinderNode middleNode = nodes[readIndex];
                    PathfinderNode lastNode = nodes[readIndex + 1];

                    if (middleNode != null &&
                        lastNode != null &&
                        CanSmoothCorner(
                            floor,
                            firstNode.Position,
                            middleNode.Position,
                            lastNode.Position,
                            routeStart,
                            routeEnd,
                            accessRights))
                    {
                        nodes[writeIndex++] = lastNode;
                        firstNode = lastNode;
                        readIndex += 2;
                        continue;
                    }
                }

                PathfinderNode nextNode = nodes[readIndex++];
                nodes[writeIndex++] = nextNode;
                firstNode = nextNode;
            }

            if (writeIndex < nodes.Count)
            {
                nodes.RemoveRange(
                    writeIndex,
                    nodes.Count - writeIndex);
            }
        }

        private static bool CanSmoothCorner(
            Floor floor,
            Vector2i first,
            Vector2i middle,
            Vector2i last,
            Vector2i routeStart,
            Vector2i routeEnd,
            int accessRights)
        {
            if (!IsRightAngleUnitCorner(first, middle, last))
            {
                return false;
            }

            Vector2i alternate = new Vector2i(
                first.m_x + last.m_x - middle.m_x,
                first.m_y + last.m_y - middle.m_y);

            // Keep the cheapest structural filters before any Vanilla
            // accessibility calls. The original route already proves first,
            // middle and last are in-bounds; only the unused corner is new.
            if (!IsInsideFloor(floor, alternate) ||
                !IsSameRoomSquare(
                    floor,
                    first,
                    middle,
                    last,
                    alternate) ||
                HasBlockingObject(floor, first) ||
                HasBlockingObject(floor, middle) ||
                HasBlockingObject(floor, last) ||
                HasBlockingObject(floor, alternate))
            {
                return false;
            }

            return IsAlternateCornerTraversable(
                floor,
                first,
                middle,
                last,
                alternate,
                routeStart,
                routeEnd,
                accessRights);
        }

        private static bool IsRightAngleUnitCorner(
            Vector2i first,
            Vector2i middle,
            Vector2i last)
        {
            int firstDx = middle.m_x - first.m_x;
            int firstDy = middle.m_y - first.m_y;
            int secondDx = last.m_x - middle.m_x;
            int secondDy = last.m_y - middle.m_y;

            bool firstOrthogonalUnit =
                Math.Abs(firstDx) + Math.Abs(firstDy) == 1;
            bool secondOrthogonalUnit =
                Math.Abs(secondDx) + Math.Abs(secondDy) == 1;

            if (!firstOrthogonalUnit || !secondOrthogonalUnit)
            {
                return false;
            }

            bool perpendicular =
                firstDx * secondDx + firstDy * secondDy == 0;
            bool diagonalEndpoint =
                Math.Abs(last.m_x - first.m_x) == 1 &&
                Math.Abs(last.m_y - first.m_y) == 1;

            return perpendicular && diagonalEndpoint;
        }

        private static bool IsAlternateCornerTraversable(
            Floor floor,
            Vector2i first,
            Vector2i middle,
            Vector2i last,
            Vector2i alternate,
            Vector2i routeStart,
            Vector2i routeEnd,
            int accessRights)
        {
            // The direct diagonal call is intentionally made through Vanilla.
            // Floor.IsAccessible already has a diagonal-specific branch that
            // rejects cutting across any wall or door around the 2x2 square.
            if (!floor.IsAccessible(
                    first,
                    last,
                    routeStart,
                    routeEnd,
                    accessRights,
                    false,
                    false))
            {
                return false;
            }

            // The unused corner must also be a genuinely traversable tile.
            // This prevents smoothing across FLOOR_TYPE_NONE, access zones,
            // procedure occupancy or a blocking object that the original
            // orthogonal route never needed to enter.
            if (!floor.IsAccessible(
                    first,
                    alternate,
                    routeStart,
                    routeEnd,
                    accessRights,
                    false,
                    false) ||
                !floor.IsAccessible(
                    alternate,
                    last,
                    routeStart,
                    routeEnd,
                    accessRights,
                    false,
                    false))
            {
                return false;
            }

            // No extra OneWay lookup is needed here. HTC OneWay rules
            // exist only on doors, while the direct diagonal validation above
            // requires all four sides of this 2x2 square to have no wall at
            // all. A door therefore makes the diagonal fail before this point.
            return true;
        }

        private static bool IsSameRoomSquare(
            Floor floor,
            Vector2i first,
            Vector2i middle,
            Vector2i last,
            Vector2i alternate)
        {
            if (!IsInsideFloor(floor, first) ||
                !IsInsideFloor(floor, middle) ||
                !IsInsideFloor(floor, last) ||
                !IsInsideFloor(floor, alternate))
            {
                return false;
            }

            Room room = floor.m_roomTiles[first.m_x, first.m_y];
            return
                ReferenceEquals(
                    room,
                    floor.m_roomTiles[middle.m_x, middle.m_y]) &&
                ReferenceEquals(
                    room,
                    floor.m_roomTiles[last.m_x, last.m_y]) &&
                ReferenceEquals(
                    room,
                    floor.m_roomTiles[alternate.m_x, alternate.m_y]);
        }

        private static bool HasBlockingObject(
            Floor floor,
            Vector2i position)
        {
            return floor.m_tileObjects[position.m_x, position.m_y]
                .IsAnyObjectBlocking();
        }

        private static bool IsInsideFloor(
            Floor floor,
            Vector2i position)
        {
            return floor != null &&
                   floor.m_roomTiles != null &&
                   position.m_x >= 0 &&
                   position.m_y >= 0 &&
                   position.m_x < floor.Size.m_x &&
                   position.m_y < floor.Size.m_y;
        }
    }
}
