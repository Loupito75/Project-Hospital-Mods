using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalTrafficControl
{
    internal static class PathfindingDebugMarkerRenderer
    {
        // Native RoomRenderer constants / geometry:
        // - ROOM_MARKERS_ZOFFSET = 110
        // - room icons use ZOFFSET = 105
        // - room tile geometry uses MapRenderer.FLOOR_SCALE = 0.975
        // The debug tile is placed between the native room marker and room icons so it
        // remains visible without leaving the native Logistics rendering hierarchy.
        private const float DebugMarkerZOffset = 108f;
        private const float NativeFloorScale = 0.975f;
        private const float DebugScaleMultiplier = 1.08f;
        private const float MinOpacity = 0.25f;
        private const float MaxOpacity = 0.95f;
        private const float PulseSpeed = 5f;

        private static readonly FieldInfo SceneRoomsFolderField =
            AccessTools.Field(typeof(MapRenderer), "m_sceneRoomsFolder");

        private static readonly List<PathfindingDebugMarkerTile> Markers =
            new List<PathfindingDebugMarkerTile>();

        private static readonly List<RendererObject> Renderers =
            new List<RendererObject>();

        private static MapRenderer s_renderer;
        private static int s_markerRevision;
        private static int s_builtRevision = -1;
        private static int s_floorIndex = -1;
        private static Direction s_rotation = Direction.None;
        private static float s_zoomScale = -1f;
        private static bool s_renderErrorLogged;
        private static int s_lastRenderedMarkerCount = -1;

        internal static void SetMarkers(List<PathfindingDebugMarkerTile> markers)
        {
            Markers.Clear();

            if (markers != null)
            {
                for (int i = 0; i < markers.Count; i++)
                {
                    PathfindingDebugMarkerTile marker = markers[i];
                    if (marker != null && !ContainsMarker(marker.FloorIndex, marker.Position))
                    {
                        Markers.Add(
                            new PathfindingDebugMarkerTile(
                                marker.FloorIndex,
                                marker.Position));
                    }
                }
            }

            s_markerRevision++;
        }

        internal static void ClearMarkersForFloor(int floorIndex)
        {
            bool removed = false;

            for (int i = Markers.Count - 1; i >= 0; i--)
            {
                if (Markers[i].FloorIndex == floorIndex)
                {
                    Markers.RemoveAt(i);
                    removed = true;
                }
            }

            if (!removed)
            {
                return;
            }

            s_markerRevision++;

            // A OneWay edit immediately invalidates the diagnostic that was produced
            // from the previous routing graph. Hide it now; a new failed repath will
            // publish fresh causal markers through the normal NoPath flow.
            if (s_floorIndex == floorIndex)
            {
                SetActive(false);
            }
        }

        internal static void Update()
        {
            if (!TrafficControlConfig.PathfindingDebug)
            {
                SetActive(false);
                return;
            }

            MapEditorController editor = MapEditorController.sm_instance;
            ViewModeController viewMode = ViewModeController.Instance;
            Hospital hospital = Hospital.Instance;
            CameraController camera = CameraController.sm_instance;

            if (editor == null || editor.m_unloading ||
                viewMode == null || hospital == null || camera == null)
            {
                SetActive(false);
                return;
            }

            if (viewMode.m_currentMode != ViewModes.LOGISTICS || Markers.Count == 0)
            {
                SetActive(false);
                return;
            }

            Floor floor = hospital.GetCurrentFloor();
            MapRenderer renderer = editor.CurrentFloorRenderer;
            if (floor == null || renderer == null || !HasMarkerOnFloor(floor.m_floorIndex))
            {
                SetActive(false);
                return;
            }

            float zoomLevel = camera.GetZoomLevel();
            float zoomScale = Math.Max(1f, (9.75f + zoomLevel) / 10f);
            Direction rotation = hospital.m_state.m_rotation;

            bool rebuild =
                !ReferenceEquals(renderer, s_renderer) ||
                s_builtRevision != s_markerRevision ||
                s_floorIndex != floor.m_floorIndex ||
                s_rotation != rotation ||
                s_zoomScale != zoomScale;

            if (rebuild)
            {
                Rebuild(renderer, floor, rotation, zoomScale);
            }

            SetActive(true);
            UpdatePulse();
        }

        internal static void Reset()
        {
            ClearRenderers();
            Markers.Clear();
            s_renderer = null;
            s_markerRevision++;
            s_builtRevision = -1;
            s_floorIndex = -1;
            s_rotation = Direction.None;
            s_zoomScale = -1f;
            s_renderErrorLogged = false;
            s_lastRenderedMarkerCount = -1;
        }

        private static void Rebuild(
            MapRenderer renderer,
            Floor floor,
            Direction rotation,
            float zoomScale)
        {
            ClearRenderers();

            s_renderer = renderer;
            s_builtRevision = s_markerRevision;
            s_floorIndex = floor.m_floorIndex;
            s_rotation = rotation;
            s_zoomScale = zoomScale;

            if (SceneRoomsFolderField == null)
            {
                LogRenderErrorOnce(
                    "[PathDebug] Could not resolve MapRenderer.m_sceneRoomsFolder for Logistics markers.");
                return;
            }

            GameObject roomsFolder = SceneRoomsFolderField.GetValue(renderer) as GameObject;
            if (roomsFolder == null)
            {
                LogRenderErrorOnce(
                    "[PathDebug] Native Logistics room hierarchy is unavailable.");
                return;
            }

            int renderedCount = 0;

            try
            {
                string tileTexturePath =
                    Database.Instance.GetEntry<GameDBTweakableString>("TEXTURE_ASSET_FLOOR").Value;

                for (int i = 0; i < Markers.Count; i++)
                {
                    PathfindingDebugMarkerTile marker = Markers[i];
                    if (marker.FloorIndex != floor.m_floorIndex ||
                        marker.Position.m_x < 0 || marker.Position.m_y < 0 ||
                        marker.Position.m_x >= floor.Size.m_x ||
                        marker.Position.m_y >= floor.Size.m_y)
                    {
                        continue;
                    }

                    RendererObject markerRenderer = CreateNativeLogisticsTile(
                        floor,
                        roomsFolder,
                        tileTexturePath,
                        marker.Position,
                        zoomScale);

                    if (markerRenderer != null)
                    {
                        Renderers.Add(markerRenderer);
                        renderedCount++;
                    }
                }

                // Rebuild() may run repeatedly while the camera zoom/rotation changes.
                // Only log when the useful rendered-marker count itself changes; marker
                // coordinates are already logged when PathDebug publishes a new NoPath.
                if (renderedCount != s_lastRenderedMarkerCount)
                {
                    s_lastRenderedMarkerCount = renderedCount;
                    Plugin.Log?.LogWarning(
                        "[PathDebug] Native Logistics room renderer built " +
                        renderedCount + " flashing tile marker(s) on floor " +
                        floor.m_floorIndex + ".");
                }
            }
            catch (Exception exception)
            {
                Exception root = exception.InnerException ?? exception;
                LogRenderErrorOnce(
                    "[PathDebug] Native Logistics tile rendering failed: " +
                    root.GetType().FullName + ": " + root.Message);
                ClearRenderers();
            }
        }

        private static RendererObject CreateNativeLogisticsTile(
            Floor floor,
            GameObject roomsFolder,
            string tileTexturePath,
            Vector2i position,
            float zoomScale)
        {
            int textureId = floor.m_tileRoomTextures[position.m_x, position.m_y];
            if (floor.IsOutdoors(position))
            {
                textureId += 32;
            }

            Vector2[] textureCoordinates =
                TileTextureHelper.GetTileTextureCooridnates(textureId);
            if (textureCoordinates == null || textureCoordinates.Length < 4)
            {
                return null;
            }

            Vector2i rotatedPositionInverse =
                IsometricCameraUtils.GetRotatedPositionInverse(
                    position.m_x,
                    position.m_y);

            Vector3 tilePosition = IsometricUtils.WorldToIsometric(
                new Vector3(
                    rotatedPositionInverse.m_x,
                    rotatedPositionInverse.m_y,
                    0f));

            float z = DebugMarkerZOffset + tilePosition.z * 0.01f;
            tilePosition.z = 0f;

            var marker = new RendererObject(
                "HTC_PathDebugLogisticsTile",
                "Materials/floor_rooms",
                tileTexturePath,
                1,
                twoTextures: false,
                vertexColors: true,
                roomsFolder);

            Vector3[] vertices = marker.m_meshData.m_vertices;
            Vector2[] texCoords = marker.m_meshData.m_texCoords;
            int[] indices = marker.m_meshData.m_indices;
            Color[] vertexColors = marker.m_meshData.m_vertexColors;

            float scale = NativeFloorScale * zoomScale * DebugScaleMultiplier;

            vertices[0] = (-Vector3.right + Vector3.up * 0.5f) * scale + tilePosition;
            vertices[1] = (Vector3.right + Vector3.up * 0.5f) * scale + tilePosition;
            vertices[2] = (-Vector3.right - Vector3.up * 0.5f) * scale + tilePosition;
            vertices[3] = (Vector3.right - Vector3.up * 0.5f) * scale + tilePosition;

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

            Color initialColor = new Color(1f, 1f, 1f, MaxOpacity);
            for (int i = 0; i < 4; i++)
            {
                vertexColors[i] = initialColor;
            }

            marker.UpdateMesh();

            Renderer unityRenderer = marker.m_gameObject.GetComponent<Renderer>();
            if (unityRenderer != null)
            {
                unityRenderer.material.mainTexture =
                    StreamingAssetManager.GetInstance().GetTextureAsset(tileTexturePath);
            }

            marker.m_gameObject.transform.localPosition = new Vector3(0f, 0f, z);
            marker.m_gameObject.SetActive(true);
            return marker;
        }

        private static void UpdatePulse()
        {
            float pulse = (Mathf.Sin(Time.time * PulseSpeed) + 1f) * 0.5f;
            float opacity = MinOpacity + (MaxOpacity - MinOpacity) * pulse;
            Color color = new Color(1f, 1f, 1f, opacity);

            for (int i = 0; i < Renderers.Count; i++)
            {
                RendererObject marker = Renderers[i];
                if (marker == null || marker.m_gameObject == null)
                {
                    continue;
                }

                Color[] vertexColors = marker.m_meshData.m_vertexColors;
                for (int j = 0; j < 4; j++)
                {
                    vertexColors[j] = color;
                }

                marker.UpdateMesh();
            }
        }

        private static bool HasMarkerOnFloor(int floorIndex)
        {
            for (int i = 0; i < Markers.Count; i++)
            {
                if (Markers[i].FloorIndex == floorIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsMarker(int floorIndex, Vector2i position)
        {
            for (int i = 0; i < Markers.Count; i++)
            {
                if (Markers[i].FloorIndex == floorIndex &&
                    Markers[i].Position == position)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetActive(bool active)
        {
            for (int i = 0; i < Renderers.Count; i++)
            {
                RendererObject marker = Renderers[i];
                if (marker != null && marker.m_gameObject != null)
                {
                    marker.m_gameObject.SetActive(active);
                }
            }
        }

        private static void ClearRenderers()
        {
            for (int i = 0; i < Renderers.Count; i++)
            {
                RendererObject marker = Renderers[i];
                if (marker != null)
                {
                    marker.Destroy();
                }
            }

            Renderers.Clear();
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
