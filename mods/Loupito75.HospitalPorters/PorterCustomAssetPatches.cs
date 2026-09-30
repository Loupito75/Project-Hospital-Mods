using System;
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

            int dataRow = tableController.GetRowIndexForDataRow(rowIndex);
            if (dataRow < 0)
            {
                return;
            }

            GameObject levelCell = tableController.GetItem(dataRow, 3).m_gameObject;
            GaugeIconsController gauge = levelCell == null
                ? null
                : levelCell.GetComponentInChildren<GaugeIconsController>();
            EmployeeComponent employeeComponent = employee.GetComponent<EmployeeComponent>();
            if (gauge == null || employeeComponent == null)
            {
                return;
            }

            PorterVisuals.ApplyNativeTableLevelVisuals(gauge);

            if (PorterIdentity.IsPorter(employee))
            {
                // EmployeesTable is a vanilla five-notch table for every occupation.
                // Nurses, lab specialists and janitors only ever fill the first three notches.
                gauge.SetValues(employeeComponent.m_state.m_level, 5);
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