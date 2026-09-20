using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalTrafficControl
{
    internal static class OneWayIndicatorRenderer
    {
        private sealed class ArrowLayers
        {
            internal RendererObject Back;
            internal RendererObject Front;
        }

        private const int NativeAccessArrowTextureBase = 416;
        private const float NativeArrowScale = 0.75f;
        private const float NativeArrowBackZ = 90f;
        private const float NativeArrowFrontZ = -90f;
        private const float NativeArrowBackOpacity = 0.33f;
        private const float NativeArrowFrontOpacity = 0.10f;

        private static readonly FieldInfo SceneFloorFolderField =
            AccessTools.Field(typeof(MapRenderer), "m_sceneFloorFolder");

        private static readonly Type TileTextureHelperType =
            AccessTools.TypeByName("TileTextureHelper");

        private static readonly MethodInfo GetTileTextureCoordinatesMethod =
            ReferenceEquals(TileTextureHelperType, null)
                ? null
                : AccessTools.Method(
                    TileTextureHelperType,
                    "GetTileTextureCooridnates",
                    new Type[] { typeof(int) });

        private static readonly MethodInfo WorldToIsometricMethod =
            AccessTools.Method(
                typeof(IsometricUtils),
                "WorldToIsometric",
                new Type[] { typeof(Vector3) });

        private static readonly List<ArrowLayers> Arrows =
            new List<ArrowLayers>();

        private static MapRenderer s_renderer;
        private static int s_revision = -1;
        private static int s_floorIndex = -1;
        private static Direction s_rotation = Direction.None;
        private static bool s_renderErrorLogged;

        internal static void Update()
        {
            MapEditorController editor = MapEditorController.sm_instance;
            ViewModeController viewMode = ViewModeController.Instance;
            Hospital hospital = Hospital.Instance;

            if (editor == null || editor.m_unloading || viewMode == null || hospital == null)
            {
                SetActive(false);
                return;
            }

            if (viewMode.m_currentMode != ViewModes.LOGISTICS || OneWayManager.RuleCount == 0)
            {
                if (OneWayManager.RuleCount == 0 && Arrows.Count > 0)
                {
                    ClearRenderers();
                    s_revision = OneWayManager.Revision;
                }
                else
                {
                    SetActive(false);
                }
                return;
            }

            Floor floor = hospital.GetCurrentFloor();
            MapRenderer renderer = editor.CurrentFloorRenderer;
            if (floor == null || renderer == null)
            {
                SetActive(false);
                return;
            }

            Direction rotation = hospital.m_state.m_rotation;
            bool rebuild =
                !ReferenceEquals(renderer, s_renderer) ||
                s_revision != OneWayManager.Revision ||
                s_floorIndex != floor.m_floorIndex ||
                s_rotation != rotation;

            if (rebuild)
            {
                Rebuild(renderer, floor, rotation);
            }

            SetActive(true);
        }

        internal static void Reset()
        {
            ClearRenderers();
            s_renderer = null;
            s_revision = -1;
            s_floorIndex = -1;
            s_rotation = Direction.None;
        }

        private static void Rebuild(
            MapRenderer renderer,
            Floor floor,
            Direction rotation)
        {
            ClearRenderers();

            s_renderer = renderer;
            s_revision = OneWayManager.Revision;
            s_floorIndex = floor.m_floorIndex;
            s_rotation = rotation;

            if (ReferenceEquals(SceneFloorFolderField, null) ||
                ReferenceEquals(GetTileTextureCoordinatesMethod, null) ||
                ReferenceEquals(WorldToIsometricMethod, null))
            {
                LogRenderErrorOnce(
                    "HTC could not resolve the native floor hierarchy or arrow rendering helpers.");
                return;
            }

            GameObject floorFolder = SceneFloorFolderField.GetValue(renderer) as GameObject;
            if (floorFolder == null || floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_tileWalls == null)
            {
                return;
            }

            var seenDoors = new List<Door>();

            try
            {
                for (int x = 0; x < floor.Size.m_x; x++)
                {
                    for (int y = 0; y < floor.Size.m_y; y++)
                    {
                        TileWalls walls = floor.m_mapPersistentData.m_tileWalls[x, y];
                        if (walls == null)
                        {
                            continue;
                        }

                        AddArrowForDoor(floor, floorFolder, walls.m_doorSW, seenDoors);
                        AddArrowForDoor(floor, floorFolder, walls.m_doorSE, seenDoors);
                    }
                }
            }
            catch (Exception exception)
            {
                Exception root = exception.InnerException ?? exception;
                LogRenderErrorOnce(
                    "HTC one-way arrow rendering failed: " +
                    root.GetType().FullName + ": " + root.Message);
                ClearRenderers();
            }
        }

        private static void AddArrowForDoor(
            Floor floor,
            GameObject floorFolder,
            Door door,
            List<Door> seenDoors)
        {
            if (door == null || ContainsReference(seenDoors, door))
            {
                return;
            }

            seenDoors.Add(door);

            byte mode = OneWayManager.GetMode(door);
            if (mode == 0)
            {
                return;
            }

            Direction displayDirection =
                OneWayDirectionHelper.GetDisplayedAllowedDirection(door, mode);
            if (displayDirection == Direction.None)
            {
                return;
            }

            Vector2i sourceTile = OneWayDirectionHelper.GetAllowedSourceTile(door, mode);
            Vector2i destinationTile = OneWayDirectionHelper.GetAllowedDestinationTile(door, mode);
            if (sourceTile.m_x < 0 || sourceTile.m_y < 0 ||
                sourceTile.m_x >= floor.Size.m_x || sourceTile.m_y >= floor.Size.m_y ||
                destinationTile.m_x < 0 || destinationTile.m_y < 0 ||
                destinationTile.m_x >= floor.Size.m_x || destinationTile.m_y >= floor.Size.m_y)
            {
                return;
            }

            Vector2i displayedSourceTile;
            if (!OneWayDirectionHelper.TryGetDisplayedTile(
                sourceTile,
                out displayedSourceTile))
            {
                displayedSourceTile = sourceTile;
            }

            Vector2i displayedDestinationTile;
            if (!OneWayDirectionHelper.TryGetDisplayedTile(
                destinationTile,
                out displayedDestinationTile))
            {
                displayedDestinationTile = destinationTile;
            }

            int textureId = NativeAccessArrowTextureBase + (int)displayDirection;
            object coordinatesValue = GetTileTextureCoordinatesMethod.Invoke(
                null,
                new object[] { textureId });
            Vector2[] textureCoordinates = coordinatesValue as Vector2[];
            if (textureCoordinates == null || textureCoordinates.Length < 4)
            {
                return;
            }

            object worldValue = WorldToIsometricMethod.Invoke(
                null,
                new object[]
                {
                    new Vector3(
                        displayedDestinationTile.m_x,
                        displayedDestinationTile.m_y,
                        -0.5f)
                });
            if (!(worldValue is Vector3))
            {
                return;
            }

            // The texture already points in the permitted movement direction. Anchor
            // it on the tile reached after crossing the door so the visual sits on
            // the opposite side of the wall from the previous source-tile placement.
            // The floor folder supplies the native per-floor world offset.
            Vector3 basePosition = (Vector3)worldValue;
            Vector3 worldPosition = floorFolder.transform.TransformPoint(basePosition);
            Vector3 floorOffset = floorFolder.transform.localPosition;

            DoorDebugManager.LogArrow(
                door,
                mode,
                displayDirection,
                sourceTile,
                destinationTile,
                displayedSourceTile,
                displayedDestinationTile,
                basePosition,
                floorOffset,
                worldPosition);

            var layers = new ArrowLayers
            {
                Back = CreateArrowLayer(
                    "HTC_OneWayDirection",
                    floorFolder,
                    basePosition,
                    textureCoordinates,
                    NativeArrowBackZ,
                    NativeArrowBackOpacity),
                Front = CreateArrowLayer(
                    "HTC_OneWayDirectionFront",
                    floorFolder,
                    basePosition,
                    textureCoordinates,
                    NativeArrowFrontZ,
                    NativeArrowFrontOpacity)
            };

            if (layers.Back != null || layers.Front != null)
            {
                Arrows.Add(layers);
            }
        }

        private static RendererObject CreateArrowLayer(
            string name,
            GameObject floorFolder,
            Vector3 basePosition,
            Vector2[] textureCoordinates,
            float zOffset,
            float opacity)
        {
            var arrow = new RendererObject(
                name,
                "Materials/floor_selection",
                "ASSET_TEX_TILES",
                1,
                twoTextures: false,
                vertexColors: true,
                floorFolder);

            // RendererObject uses Transform.SetParent(parent) with Unity's default
            // worldPositionStays=true. Native access-arrow RendererObjects are created
            // before m_sceneFloorFolder receives its per-floor offset, but HTC creates
            // these dynamically after RenderMap has already moved that folder. Reset
            // the local position so the arrow inherits exactly the same floor offset
            // as the native floor and access-direction geometry.
            arrow.m_gameObject.transform.localPosition = Vector3.zero;

            Vector3 position = basePosition;
            position.z = zOffset;

            Vector3[] vertices = arrow.m_meshData.m_vertices;
            Vector2[] texCoords = arrow.m_meshData.m_texCoords;
            int[] indices = arrow.m_meshData.m_indices;
            Color[] vertexColors = arrow.m_meshData.m_vertexColors;

            vertices[0] = (-Vector3.right + Vector3.up * 0.5f) * NativeArrowScale + position;
            vertices[1] = (Vector3.right + Vector3.up * 0.5f) * NativeArrowScale + position;
            vertices[2] = (-Vector3.right - Vector3.up * 0.5f) * NativeArrowScale + position;
            vertices[3] = (Vector3.right - Vector3.up * 0.5f) * NativeArrowScale + position;

            texCoords[0] = textureCoordinates[0];
            texCoords[1] = textureCoordinates[1];
            texCoords[2] = textureCoordinates[2];
            texCoords[3] = textureCoordinates[3];

            indices[0] = 0;
            indices[1] = 1;
            indices[2] = 2;
            indices[3] = 2;
            indices[4] = 1;
            indices[5] = 3;

            for (int i = 0; i < 4; i++)
            {
                vertexColors[i] = Color.white;
            }

            arrow.UpdateMesh();

            Renderer unityRenderer = arrow.m_gameObject.GetComponent<Renderer>();
            if (unityRenderer != null)
            {
                unityRenderer.material.SetFloat("_Opacity", opacity);
            }

            return arrow;
        }

        private static void SetActive(bool active)
        {
            foreach (ArrowLayers layers in Arrows)
            {
                SetLayerActive(layers?.Back, active);
                SetLayerActive(layers?.Front, active);
            }
        }

        private static void SetLayerActive(RendererObject arrow, bool active)
        {
            if (arrow?.m_gameObject != null)
            {
                arrow.m_gameObject.SetActive(active);
            }
        }

        private static void ClearRenderers()
        {
            foreach (ArrowLayers layers in Arrows)
            {
                layers?.Back?.Destroy();
                layers?.Front?.Destroy();
            }

            Arrows.Clear();
        }

        private static bool ContainsReference(List<Door> doors, Door candidate)
        {
            foreach (Door door in doors)
            {
                if (ReferenceEquals(door, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static void LogRenderErrorOnce(string message)
        {
            if (s_renderErrorLogged)
            {
                return;
            }

            s_renderErrorLogged = true;
            Plugin.Log?.LogWarning(message);
        }
    }
}
