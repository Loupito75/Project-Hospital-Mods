using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalPorters
{
    [HarmonyPatch(
        typeof(EmployeeComponent),
        nameof(EmployeeComponent.HasOccupationForRole),
        new Type[] { typeof(GameDBEmployeeRole) })]
    internal static class PorterReceptionRolePresentationPatch
    {
        private static readonly FieldInfo EmployeeEntityField =
            AccessTools.Field(typeof(EmployeeComponent), "m_entity");

        [HarmonyPostfix]
        private static void Postfix(
            EmployeeComponent __instance,
            GameDBEmployeeRole employeeRole,
            ref bool __result)
        {
            if (__result || __instance == null || employeeRole == null ||
                employeeRole != Database.Instance.GetEntry<GameDBEmployeeRole>("EMPL_ROLE_RECEPTIONIST") ||
                object.ReferenceEquals(EmployeeEntityField, null))
            {
                return;
            }

            Entity entity = EmployeeEntityField.GetValue(__instance) as Entity;
            if (PorterIdentity.IsPorter(entity))
            {
                // Presentation compatibility only. Porters never receive the Nurse
                // receptionist specialization, so native HasSkillForRole() keeps this
                // role grayed out and CharacterPanelRolesPanelController never wires
                // a clickable toggle for it.
                __result = true;
            }
        }
    }

    [HarmonyPatch(
        typeof(CharacterPanelRolesPanelController),
        nameof(CharacterPanelRolesPanelController.UpdateData),
        new Type[] { typeof(Entity) })]
    internal static class PorterRoleIconPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            CharacterPanelRolesPanelController __instance,
            Entity employee)
        {
            if (__instance == null || !PorterIdentity.IsPorter(employee) ||
                __instance.m_rolesSegmentStatic == null)
            {
                return;
            }

            EmployeeComponent employeeComponent = employee.GetComponent<EmployeeComponent>();
            SegmentController segment =
                __instance.m_rolesSegmentStatic.GetComponent<SegmentController>();
            if (employeeComponent == null || segment == null)
            {
                return;
            }

            GameDBEmployeeRole[] roles = Database.Instance.GetEntries<GameDBEmployeeRole>();
            int itemIndex = 0;
            foreach (GameDBEmployeeRole role in roles)
            {
                if (!employeeComponent.HasOccupationForRole(role))
                {
                    continue;
                }

                if (PorterIds.IsPorterRole(role) &&
                    itemIndex < segment.m_itemCount &&
                    segment.IsInRange(itemIndex))
                {
                    Sprite sprite =
                        PorterVisuals.GetRoleSprite(role, employeeComponent.HasRole(role));
                    Image image = segment.GetItemImageComponent(itemIndex, 1);
                    if (sprite != null && image != null)
                    {
                        image.sprite = sprite;
                    }
                }

                itemIndex++;
            }
        }
    }

    [HarmonyPatch(
        typeof(EmployeesTable),
        "FillCharacterRow",
        new Type[] { typeof(Entity), typeof(int), typeof(TableController) })]
    internal static class PorterEmployeeTableLevelIconPatch
    {
        private sealed class PorterLevelCellState
        {
            internal GaugeIconsController Gauge;
            internal Image BaseImage;
            internal Image TopImage;
            internal RectTransform GaugeRect;
            internal Sprite OriginalBaseSprite;
            internal Sprite OriginalTopSprite;
            internal Sprite PorterBaseSprite;
            internal Sprite PorterTopSprite;
            internal Vector2 OriginalSizeDelta;
            internal Vector2 OriginalAnchoredPosition;
            internal Vector2 PorterSizeDelta;
            internal Vector2 PorterAnchoredPosition;
        }

        // HPO owns only the temporary Porter delta. Keying by the already-known
        // table cell lets non-Porter rows avoid repeated component-tree scans.
        private static readonly Dictionary<GameObject, PorterLevelCellState>
            PorterLevelCells =
                new Dictionary<GameObject, PorterLevelCellState>();

        internal static void Reset()
        {
            PorterLevelCells.Clear();
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Entity employee,
            int rowIndex,
            TableController tableController)
        {
            if (tableController == null)
            {
                return;
            }

            bool porter = PorterIdentity.IsPorter(employee);
            if (!porter && PorterLevelCells.Count == 0)
            {
                return;
            }

            int dataRow = tableController.GetRowIndexForDataRow(rowIndex);
            if (dataRow < 0)
            {
                return;
            }

            TableItem levelItem = tableController.GetItem(dataRow, 3);
            GameObject levelCell =
                levelItem == null ? null : levelItem.m_gameObject;
            if (levelCell == null)
            {
                return;
            }

            if (!porter)
            {
                PorterLevelCellState previousState;
                if (!PorterLevelCells.TryGetValue(
                        levelCell,
                        out previousState))
                {
                    return;
                }

                // Capture HPO ownership before restoring anything. A three-level
                // Vanilla/HFX profession can legitimately use the same 36x12
                // geometry as the Porter gauge, so geometry alone cannot prove
                // that HPO still owns the recycled cell.
                bool baseStillPorter =
                    previousState.BaseImage != null &&
                    object.ReferenceEquals(
                        previousState.BaseImage.sprite,
                        previousState.PorterBaseSprite);
                bool topStillPorter =
                    previousState.TopImage != null &&
                    object.ReferenceEquals(
                        previousState.TopImage.sprite,
                        previousState.PorterTopSprite);
                bool geometryStillPorter =
                    previousState.GaugeRect != null &&
                    previousState.GaugeRect.sizeDelta ==
                        previousState.PorterSizeDelta &&
                    previousState.GaugeRect.anchoredPosition ==
                        previousState.PorterAnchoredPosition;

                // Restore only properties still carrying HPO's own Porter
                // delta. If HFX or another mod already changed the recycled
                // sprites before this postfix, preserve its geometry as well.
                if (baseStillPorter)
                {
                    previousState.BaseImage.sprite =
                        previousState.OriginalBaseSprite;
                }
                if (topStillPorter)
                {
                    previousState.TopImage.sprite =
                        previousState.OriginalTopSprite;
                }
                if (baseStillPorter &&
                    topStillPorter &&
                    geometryStillPorter)
                {
                    previousState.GaugeRect.sizeDelta =
                        previousState.OriginalSizeDelta;
                    previousState.GaugeRect.anchoredPosition =
                        previousState.OriginalAnchoredPosition;
                }

                PorterLevelCells.Remove(levelCell);
                return;
            }

            EmployeeComponent employeeComponent =
                employee.GetComponent<EmployeeComponent>();
            if (employeeComponent == null || employeeComponent.m_state == null)
            {
                return;
            }

            PorterLevelCellState state;
            bool newState = !PorterLevelCells.TryGetValue(
                levelCell,
                out state);
            if (newState)
            {
                GaugeIconsController gauge =
                    levelCell.GetComponentInChildren<GaugeIconsController>();
                Image baseImage;
                Image topImage;
                if (gauge == null ||
                    !PorterVisuals.TryGetLevelGaugeImages(
                        gauge,
                        out baseImage,
                        out topImage))
                {
                    return;
                }

                // GaugeIconsController lives on GaugeIconsFull, whose
                // RectTransform stretches to the parent GaugeIcons canvas.
                // Resize the parent/base Image canvas (60x12 Vanilla), not the
                // stretching overlay child, so the Porter 3-notch gauge
                // actually becomes 36x12 and remains centered.
                RectTransform gaugeRect = baseImage.rectTransform;
                if (gaugeRect == null)
                {
                    return;
                }

                state = new PorterLevelCellState
                {
                    Gauge = gauge,
                    BaseImage = baseImage,
                    TopImage = topImage,
                    GaugeRect = gaugeRect,
                    OriginalBaseSprite = baseImage.sprite,
                    OriginalTopSprite = topImage.sprite,
                    OriginalSizeDelta = gaugeRect.sizeDelta,
                    OriginalAnchoredPosition = gaugeRect.anchoredPosition
                };
            }

            // Employees uses HPO's dark three-notch artwork. This table-specific
            // path intentionally accepts its 150x50 source sprites and then
            // fits the UI canvas to their aspect ratio.
            if (!PorterVisuals.ApplyEmployeeTableLevelVisuals(state.Gauge))
            {
                return;
            }

            state.PorterBaseSprite = state.BaseImage.sprite;
            state.PorterTopSprite = state.TopImage.sprite;

            float spriteHeight = state.BaseImage.sprite.rect.height;
            float spriteWidth = state.BaseImage.sprite.rect.width;
            float rowHeight = state.OriginalSizeDelta.y;
            if (spriteHeight <= 0f || rowHeight <= 0f)
            {
                return;
            }

            state.PorterSizeDelta =
                new Vector2(
                    rowHeight * spriteWidth / spriteHeight,
                    rowHeight);
            state.PorterAnchoredPosition = Vector2.zero;
            state.GaugeRect.sizeDelta = state.PorterSizeDelta;
            state.GaugeRect.anchoredPosition =
                state.PorterAnchoredPosition;

            state.Gauge.SetValues(employeeComponent.m_state.m_level, 3);
            if (newState)
            {
                PorterLevelCells.Add(levelCell, state);
            }
        }
    }

    [HarmonyPatch(
        typeof(MapEditorUIController),
        "CreateRoomButton",
        new Type[] { typeof(int), typeof(string), typeof(Sprite) })]
    internal static class PorterBuildingRoomIconPatch
    {
        [HarmonyPrefix]
        private static void Prefix(string typeID, ref Sprite sprite)
        {
            if (typeID != PorterIds.PorterStationRoom)
            {
                return;
            }

            GameDBRoomType roomType =
                Database.Instance.GetEntry<GameDBRoomType>(PorterIds.PorterStationRoom);
            bool locked = roomType != null &&
                InsuranceManager.Instance != null &&
                InsuranceManager.Instance.IsRoomLocked(roomType);

            Sprite custom = PorterVisuals.GetStationSprite(locked);
            if (custom != null)
            {
                sprite = custom;
            }
        }
    }

    [HarmonyPatch(
        typeof(DepartmentManagementLogisticsHybridController),
        "UpdateRoomButton",
        new Type[] { typeof(GameObject), typeof(string), typeof(int), typeof(Color) })]
    internal static class PorterManagementRoomIconPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(GameObject prefabButon, string typeID)
        {
            if (typeID != PorterIds.PorterStationRoom || prefabButon == null)
            {
                return;
            }

            GameDBRoomType roomType =
                Database.Instance.GetEntry<GameDBRoomType>(PorterIds.PorterStationRoom);
            bool locked = roomType != null &&
                InsuranceManager.Instance != null &&
                InsuranceManager.Instance.IsRoomLocked(roomType);

            Sprite custom = PorterVisuals.GetStationSprite(locked);
            SegmentItemIconTextTextIconController item =
                prefabButon.GetComponentInChildren<SegmentItemIconTextTextIconController>();
            Image image = item == null || item.m_icon == null
                ? null
                : item.m_icon.GetComponent<Image>();
            if (custom != null && image != null)
            {
                image.sprite = custom;
            }
        }
    }

    [HarmonyPatch(typeof(RoomItemController), "Update")]
    internal static class PorterRoomItemIconPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(RoomItemController __instance)
        {
            if (__instance == null ||
                __instance.m_roomDatabaseID != PorterIds.PorterStationRoom ||
                __instance.m_icon == null)
            {
                return;
            }

            Sprite custom = PorterVisuals.GetStationSprite(locked: false);
            Image image = __instance.m_icon.GetComponentInChildren<Image>();
            if (custom != null && image != null)
            {
                image.sprite = custom;
            }
        }
    }

    [HarmonyPatch(
        typeof(RoomRenderer),
        "UpdateIconObject",
        new Type[]
        {
            typeof(Room), typeof(RendererObject), typeof(int), typeof(float), typeof(float)
        })]
    internal static class PorterRoomFloorIconPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Room room, RendererObject iconRendererObject)
        {
            if (room == null || iconRendererObject == null ||
                iconRendererObject.m_gameObject == null ||
                !PorterStationRegistry.IsPorterStation(
                    room.m_roomPersistentData.m_roomType.Entry))
            {
                return;
            }

            Texture texture = PorterVisuals.GetStationFloorTexture();
            Renderer renderer = iconRendererObject.m_gameObject.GetComponent<Renderer>();
            if (texture == null || renderer == null ||
                iconRendererObject.m_meshData == null ||
                iconRendererObject.m_meshData.m_texCoords == null ||
                iconRendererObject.m_meshData.m_texCoords.Length < 4)
            {
                return;
            }

            renderer.material.mainTexture = texture;

            // Native room icons use one 256x128 atlas cell. The custom Porter texture is a
            // standalone image, so keep native geometry/color and replace only the UV rectangle.
            iconRendererObject.m_meshData.m_texCoords[0] = Vector2.up;
            iconRendererObject.m_meshData.m_texCoords[1] = Vector2.one;
            iconRendererObject.m_meshData.m_texCoords[2] = Vector2.zero;
            iconRendererObject.m_meshData.m_texCoords[3] = Vector2.right;
            iconRendererObject.UpdateMesh();
        }
    }
}