using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalTrafficControl
{
    internal static class OneWayDoorVisualGeometry
    {
        private const int SearchRadiusTiles = 2;

        private static readonly MethodInfo GetRotatedPositionMethod =
            AccessTools.Method(
                typeof(IsometricCameraUtils),
                "GetRotatedPosition",
                new Type[] { typeof(int), typeof(int) });

        private static readonly MethodInfo GetRotatedDirectionMethod =
            AccessTools.Method(
                typeof(IsometricCameraUtils),
                "GetRotatedDirection",
                new Type[] { typeof(Direction) });

        private static readonly MethodInfo GetRotatedDirectionInverseMethod =
            AccessTools.Method(
                typeof(IsometricCameraUtils),
                "GetRotatedDirectionInverse",
                new Type[] { typeof(Direction) });

        private static readonly MethodInfo GetRotatedPositionInverseDoorSwMethod =
            AccessTools.Method(
                typeof(IsometricCameraUtils),
                "GetRotatedPositionInverseDoorSW",
                new Type[] { typeof(int), typeof(int) });

        private static readonly MethodInfo GetRotatedPositionInverseDoorSeMethod =
            AccessTools.Method(
                typeof(IsometricCameraUtils),
                "GetRotatedPositionInverseDoorSE",
                new Type[] { typeof(int), typeof(int) });

        private static readonly Type TileTextureHelperType =
            AccessTools.TypeByName("TileTextureHelper");

        private static readonly MethodInfo GetDoorTextureOffsetsMethod =
            ReferenceEquals(TileTextureHelperType, null)
                ? null
                : AccessTools.Method(
                    TileTextureHelperType,
                    "GetDoorTextureOffsets",
                    new Type[] { typeof(int) });

        private static readonly MethodInfo CreateDoorObjectMethod =
            ResolveCreateDoorObjectMethod();

        private static bool s_missingNativeGeometryLogged;
        private static bool s_nativeGeometryErrorLogged;

        internal static Door FindDoorUnderCursor(
            Floor floor,
            MapRenderer mapRenderer,
            Vector2i projectedTile,
            Vector2 mouseCoords)
        {
            if (floor == null || mapRenderer == null || floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_tileWalls == null ||
                ReferenceEquals(GetRotatedPositionMethod, null))
            {
                return null;
            }

            object rotatedValue = GetRotatedPositionMethod.Invoke(
                null,
                new object[] { projectedTile.m_x, projectedTile.m_y });
            if (!(rotatedValue is Vector2i))
            {
                return null;
            }

            Vector2i center = (Vector2i)rotatedValue;
            var candidates = new List<Door>();

            for (int dx = -SearchRadiusTiles; dx <= SearchRadiusTiles; dx++)
            {
                for (int dy = -SearchRadiusTiles; dy <= SearchRadiusTiles; dy++)
                {
                    int x = center.m_x + dx;
                    int y = center.m_y + dy;
                    if (x < 0 || y < 0 || x >= floor.Size.m_x || y >= floor.Size.m_y)
                    {
                        continue;
                    }

                    TileWalls walls = floor.m_mapPersistentData.m_tileWalls[x, y];
                    AddUnique(candidates, walls?.m_doorSW);
                    AddUnique(candidates, walls?.m_doorSE);
                }
            }

            Door best = null;
            float bestDistance = float.MaxValue;

            foreach (Door candidate in candidates)
            {
                // Door is also the native base entity used for windows. OneWay hit
                // testing must never let a non-passable WINDOW_* overlap win over a
                // real door under the cursor.
                if (!OneWayDoorGroup.IsEligible(candidate))
                {
                    continue;
                }

                Bounds bounds;
                if (!TryGetDoorBounds(mapRenderer, floor, candidate, out bounds) ||
                    !Contains2D(bounds, mouseCoords))
                {
                    continue;
                }

                float dx = bounds.center.x - mouseCoords.x;
                float dy = bounds.center.y - mouseCoords.y;
                float distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        internal static bool TryGetDoorBounds(
            MapRenderer mapRenderer,
            Floor floor,
            Door door,
            out Bounds bounds)
        {
            bounds = new Bounds();

            if (mapRenderer == null || floor == null || !OneWayDoorGroup.IsEligible(door) ||
                mapRenderer.m_wallRenderer == null || mapRenderer.m_lightMap == null ||
                ReferenceEquals(GetRotatedDirectionMethod, null) ||
                ReferenceEquals(GetRotatedDirectionInverseMethod, null) ||
                ReferenceEquals(GetRotatedPositionInverseDoorSwMethod, null) ||
                ReferenceEquals(GetRotatedPositionInverseDoorSeMethod, null) ||
                ReferenceEquals(GetDoorTextureOffsetsMethod, null) ||
                ReferenceEquals(CreateDoorObjectMethod, null))
            {
                LogMissingNativeGeometryOnce();
                return false;
            }

            GameObject previewObject = null;

            try
            {
                Direction orientation = door.m_state.m_orientation;
                Direction rotatedDirection = (Direction)GetRotatedDirectionMethod.Invoke(
                    null,
                    new object[] { orientation });
                Direction inverseDirection = (Direction)GetRotatedDirectionInverseMethod.Invoke(
                    null,
                    new object[] { orientation });

                bool southWestGeometry =
                    inverseDirection == Direction.NE || inverseDirection == Direction.SW;

                MethodInfo positionMethod = southWestGeometry
                    ? GetRotatedPositionInverseDoorSwMethod
                    : GetRotatedPositionInverseDoorSeMethod;

                Vector2i drawPosition = (Vector2i)positionMethod.Invoke(
                    null,
                    new object[]
                    {
                        door.m_state.m_position.m_x,
                        door.m_state.m_position.m_y
                    });

                object doorType = door.m_state.m_gameDBDoor.Entry;
                int textureId;

                if (southWestGeometry)
                {
                    textureId = GetIntMember(
                        doorType,
                        rotatedDirection != Direction.SW
                            ? "TextureIDSW"
                            : "TextureIDBackSW");
                }
                else
                {
                    textureId = GetIntMember(
                        doorType,
                        rotatedDirection != Direction.SE
                            ? "TextureIDSE"
                            : "TextureIDBackSE");
                }

                Vector2i textureOffsets = (Vector2i)GetDoorTextureOffsetsMethod.Invoke(
                    null,
                    new object[] { textureId });

                object created = CreateDoorObjectMethod.Invoke(
                    mapRenderer.m_wallRenderer,
                    new object[]
                    {
                        drawPosition.m_x,
                        drawPosition.m_y,
                        textureOffsets.m_x,
                        textureOffsets.m_y,
                        0f,
                        1f,
                        floor,
                        mapRenderer.m_lightMap,
                        door.m_colorHSV,
                        0f,
                        0f,
                        false,
                        true
                    });

                previewObject = created as GameObject;
                if (previewObject == null)
                {
                    return false;
                }

                Renderer renderer = previewObject.GetComponent<Renderer>();
                if (renderer == null)
                {
                    renderer = previewObject.GetComponentInChildren<Renderer>();
                }

                if (renderer == null)
                {
                    return false;
                }

                bounds = renderer.bounds;
                return true;
            }
            catch (Exception exception)
            {
                if (!s_nativeGeometryErrorLogged)
                {
                    s_nativeGeometryErrorLogged = true;
                    Exception root = exception.InnerException ?? exception;
                    Plugin.Log?.LogWarning(
                        "HTC full-door hit test could not build native door geometry: " +
                        root.GetType().FullName + ": " + root.Message);
                }
                return false;
            }
            finally
            {
                if (previewObject != null)
                {
                    previewObject.SetActive(false);
                    UnityEngine.Object.Destroy(previewObject);
                }
            }
        }

        private static MethodInfo ResolveCreateDoorObjectMethod()
        {
            MethodInfo[] methods = typeof(WallRenderer).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (MethodInfo method in methods)
            {
                if (method.Name == "CreateDoorObject" && method.GetParameters().Length == 13)
                {
                    return method;
                }
            }

            return null;
        }

        private static int GetIntMember(object instance, string name)
        {
            if (instance == null)
            {
                return 0;
            }

            Type type = instance.GetType();
            PropertyInfo property = AccessTools.Property(type, name);
            if (!ReferenceEquals(property, null))
            {
                return Convert.ToInt32(property.GetValue(instance, null));
            }

            FieldInfo field = AccessTools.Field(type, name);
            if (!ReferenceEquals(field, null))
            {
                return Convert.ToInt32(field.GetValue(instance));
            }

            throw new MissingMemberException(type.FullName, name);
        }

        private static bool Contains2D(Bounds bounds, Vector2 point)
        {
            return point.x >= bounds.min.x && point.x <= bounds.max.x &&
                   point.y >= bounds.min.y && point.y <= bounds.max.y;
        }

        private static void AddUnique(List<Door> doors, Door candidate)
        {
            if (!OneWayDoorGroup.IsEligible(candidate))
            {
                return;
            }

            foreach (Door existing in doors)
            {
                if (ReferenceEquals(existing, candidate))
                {
                    return;
                }
            }

            doors.Add(candidate);
        }

        private static void LogMissingNativeGeometryOnce()
        {
            if (s_missingNativeGeometryLogged)
            {
                return;
            }

            s_missingNativeGeometryLogged = true;
            Plugin.Log?.LogWarning(
                "HTC full-door hit testing is unavailable because native door geometry methods could not be resolved.");
        }
    }
}
