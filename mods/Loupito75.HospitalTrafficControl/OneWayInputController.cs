using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalTrafficControl
{
    internal static class OneWayInputController
    {
        private static readonly MethodInfo GetDoorAtPositionMethod =
            AccessTools.Method(
                typeof(MapEditorUIController),
                "GetDoorAtPosition",
                new Type[] { typeof(Vector2i), typeof(Direction) });

        private static readonly MethodInfo IsMouseInUiMethod =
            AccessTools.Method(typeof(MapEditorUIController), "IsMouseInUI");

        private static readonly string[] NoLocalizationParameters = new string[0];

        private static bool s_missingMethodLogged;
        private static bool s_runtimeErrorLogged;
        private static bool s_indicatorRefreshErrorLogged;

        internal static bool TryHandleMouseIdleClick(
            MapEditorUIController controller,
            Vector2 mouseCoords)
        {
            if (controller == null ||
                (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)) ||
                !Input.GetMouseButtonDown(0))
            {
                return false;
            }

            ViewModeController viewMode = ViewModeController.Instance;
            if (viewMode == null || viewMode.m_currentMode != ViewModes.LOGISTICS)
            {
                return false;
            }

            if (ReferenceEquals(GetDoorAtPositionMethod, null) ||
                ReferenceEquals(IsMouseInUiMethod, null))
            {
                if (!s_missingMethodLogged)
                {
                    s_missingMethodLogged = true;
                    Plugin.Log?.LogWarning(
                        "One-way input is unavailable because a required MapEditorUIController method could not be resolved.");
                }
                return false;
            }

            try
            {
                if ((bool)IsMouseInUiMethod.Invoke(controller, null))
                {
                    return false;
                }

                Hospital hospital = Hospital.Instance;
                Floor floor = hospital?.GetCurrentFloor();
                MapRenderer mapRenderer = MapEditorController.sm_instance?.CurrentFloorRenderer;
                if (floor == null || mapRenderer == null)
                {
                    return false;
                }

                Vector2i tilePosition = controller.GetTilePosition();

                Door door = OneWayDoorVisualGeometry.FindDoorUnderCursor(
                    floor,
                    mapRenderer,
                    tilePosition,
                    mouseCoords);

                Direction selectedEdge = Direction.None;

                if (door == null)
                {
                    selectedEdge = controller.GetDirectionFromEdgeSelection(0.5f);
                    if (selectedEdge != Direction.None)
                    {
                        door = GetDoorAtPositionMethod.Invoke(
                            controller,
                            new object[] { tilePosition, selectedEdge }) as Door;
                    }
                }

                // Door is also the game's base entity for windows. Only native
                // passable GameDBDoor entries are valid OneWay controls.
                if (!OneWayDoorGroup.IsEligible(door))
                {
                    DoorDebugManager.LogMiss(
                        floor,
                        tilePosition,
                        mouseCoords,
                        selectedEdge,
                        door,
                        "no eligible passable door selected");
                    return false;
                }

                Door[] group = OneWayDoorGroup.GetGroup(door, floor);
                if (group.Length == 0)
                {
                    DoorDebugManager.LogMiss(
                        floor,
                        tilePosition,
                        mouseCoords,
                        selectedEdge,
                        door,
                        "selected door produced an empty OneWay group");
                    return false;
                }

                // Project Hospital takes a map snapshot before native building edits.
                // OneWay persistence follows MapPersistentData.Clone/ApplyClone, so
                // doing the same here lets the native Undo/Redo lifecycle carry the
                // HTC rule snapshot too.
                if (UndoManager.sm_Instance != null)
                {
                    UndoManager.sm_Instance.CreateSnapshot();
                }

                byte previousMode = OneWayManager.GetMode(door);
                byte mode = OneWayManager.CycleGroup(group);

                DoorDebugManager.LogClick(
                    floor,
                    tilePosition,
                    mouseCoords,
                    selectedEdge,
                    door,
                    group,
                    previousMode,
                    mode);

                OneWayRouteManager.OnRuleChanged(door);

                Direction displayedDirection =
                    OneWayDirectionHelper.GetDisplayedAllowedDirection(door, mode);

                // Floating building notifications expect coordinates in the currently
                // displayed map orientation. Show this before touching the custom
                // renderer so a renderer failure can never suppress click feedback.
                ShowFloatingNotification(door, mode, displayedDirection);
                UISoundManager.sm_instance?.PlaySoundEvent("SFX_UI_OBJECT_SELECTED");

                try
                {
                    OneWayIndicatorRenderer.Update();
                }
                catch (Exception exception)
                {
                    if (!s_indicatorRefreshErrorLogged)
                    {
                        s_indicatorRefreshErrorLogged = true;
                        Exception root = exception.InnerException ?? exception;
                        Plugin.Log?.LogWarning(
                            "HTC one-way indicator refresh failed after click: " +
                            root.GetType().FullName + ": " + root.Message);
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                if (!s_runtimeErrorLogged)
                {
                    s_runtimeErrorLogged = true;
                    Exception root = exception.InnerException ?? exception;
                    Plugin.Log?.LogError(
                        "One-way input failed: " +
                        $"{root.GetType().FullName}: {root.Message}");
                }

                return false;
            }
        }

        private static string GetDirectionLabel(Direction direction)
        {
            string stringId;
            switch (direction)
            {
                case Direction.NW:
                    stringId = LocalizationManager.OneWayNorthWestId;
                    break;
                case Direction.NE:
                    stringId = LocalizationManager.OneWayNorthEastId;
                    break;
                case Direction.SE:
                    stringId = LocalizationManager.OneWaySouthEastId;
                    break;
                case Direction.SW:
                    stringId = LocalizationManager.OneWaySouthWestId;
                    break;
                default:
                    return string.Empty;
            }

            StringTable table = StringTable.GetInstance();
            if (table != null)
            {
                string localized = table.GetLocalizedText(stringId, NoLocalizationParameters);
                if (!string.IsNullOrEmpty(localized))
                {
                    return localized;
                }
            }

            string fallback;
            LocalizationManager.TryGetLocalizedText(
                null,
                stringId,
                NoLocalizationParameters,
                out fallback);
            return fallback ?? string.Empty;
        }

        private static void ShowFloatingNotification(
            Door door,
            byte mode,
            Direction displayedDirection)
        {
            string stringId = mode == 0
                ? LocalizationManager.OneWayOffId
                : LocalizationManager.OneWayDirectionId;

            string[] parameters = mode == 0
                ? NoLocalizationParameters
                : new[] { GetDirectionLabel(displayedDirection) };

            string text = null;
            StringTable table = StringTable.GetInstance();
            if (table != null)
            {
                text = table.GetLocalizedText(stringId, parameters);
            }

            if (string.IsNullOrEmpty(text))
            {
                LocalizationManager.TryGetLocalizedText(
                    null,
                    stringId,
                    parameters,
                    out text);
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Vector2i displayedPosition;
            if (!OneWayDirectionHelper.TryGetDisplayedTile(
                door.m_state.m_position,
                out displayedPosition))
            {
                displayedPosition = door.m_state.m_position;
            }

            NotificationManager.GetInstance()?.AddFloatingIngameNotification(
                displayedPosition,
                text,
                Color.white);
        }
    }
}
