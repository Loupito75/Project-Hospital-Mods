using System;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class OneWayDirectionHelper
    {
        internal static Direction GetDisplayedAllowedDirection(Door door, byte mode)
        {
            if (door == null || mode == 0)
            {
                return Direction.None;
            }

            Vector2i source = GetAllowedSourceTile(door, mode);
            Vector2i destination = GetAllowedDestinationTile(door, mode);
            Vector2i displayedSource;
            Vector2i displayedDestination;

            if (!TryGetDisplayedTile(source, out displayedSource) ||
                !TryGetDisplayedTile(destination, out displayedDestination))
            {
                return GetRawGridDirection(source, destination);
            }

            // Native access arrows use the movement direction itself. After both
            // endpoints have been transformed into display coordinates, their raw
            // delta already maps to the same Direction values used by Pathfinder
            // and MapRenderer (NE/SW/NW/SE). Do not invert it a second time.
            return GetRawGridDirection(displayedSource, displayedDestination);
        }

        internal static Vector2i GetAllowedSourceTile(Door door, byte mode)
        {
            if (door == null || mode == 0)
            {
                return Vector2i.ZERO_VECTOR;
            }

            int x = door.m_state.m_position.m_x;
            int y = door.m_state.m_position.m_y;
            Direction orientation = door.m_state.m_orientation;
            bool southWestWall = orientation == Direction.NE || orientation == Direction.SW;

            if (southWestWall)
            {
                return mode == 1
                    ? new Vector2i(x, y - 1)
                    : new Vector2i(x, y);
            }

            return mode == 1
                ? new Vector2i(x - 1, y)
                : new Vector2i(x, y);
        }

        internal static Vector2i GetAllowedDestinationTile(Door door, byte mode)
        {
            if (door == null || mode == 0)
            {
                return Vector2i.ZERO_VECTOR;
            }

            int x = door.m_state.m_position.m_x;
            int y = door.m_state.m_position.m_y;
            Direction orientation = door.m_state.m_orientation;
            bool southWestWall = orientation == Direction.NE || orientation == Direction.SW;

            if (southWestWall)
            {
                return mode == 1
                    ? new Vector2i(x, y)
                    : new Vector2i(x, y - 1);
            }

            return mode == 1
                ? new Vector2i(x, y)
                : new Vector2i(x - 1, y);
        }

        internal static bool TryGetDisplayedTile(
            Vector2i internalTile,
            out Vector2i displayedTile)
        {
            displayedTile = internalTile;

            try
            {
                displayedTile = IsometricCameraUtils.GetRotatedPositionInverse(
                    internalTile.m_x,
                    internalTile.m_y);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Direction GetRawGridDirection(
            Vector2i source,
            Vector2i destination)
        {
            int dx = destination.m_x - source.m_x;
            int dy = destination.m_y - source.m_y;

            if (dx > 0 && dy == 0)
            {
                return Direction.NW;
            }

            if (dx < 0 && dy == 0)
            {
                return Direction.SE;
            }

            if (dy > 0 && dx == 0)
            {
                return Direction.NE;
            }

            if (dy < 0 && dx == 0)
            {
                return Direction.SW;
            }

            return Direction.None;
        }
    }
}
