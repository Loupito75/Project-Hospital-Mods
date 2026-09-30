using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalPorters
{
    internal static class PorterLogisticsUi
    {
        private const string PorterFilterObjectName = "HospitalPorters_PorterFilter";

        private static readonly MethodInfo ManageWorkplaceMethod =
            AccessTools.Method(
                typeof(LogisticsUIController),
                "ManageWorkplace",
                new Type[]
                {
                    typeof(Entity), typeof(Entity), typeof(Vector2i), typeof(TileObject), typeof(Shift)
                });

        internal static void AddLockerPanels(LogisticsUIController controller, Department department)
        {
            if (controller == null || department == null ||
                controller.m_workspacePanels == null || controller.m_workspaceAnchors == null ||
                controller.m_prefabLogisticsWorkspacePanel == null || controller.m_prefabLogisticsAnchor == null ||
                controller.m_canvas == null)
            {
                return;
            }

            foreach (EntityIDPointer<TileObject> pointer in department.m_departmentPersistentData.m_objects)
            {
                TileObject locker = pointer.GetEntity();
                if (!TryGetValidPorterLocker(locker, department, out Room room))
                {
                    continue;
                }

                if (locker.GetFloorIndex() != Hospital.Instance.m_currentFloorIndex ||
                    locker.m_state.m_placementValidity == PlacementValidationResult.MISSING_REQUIREMENT)
                {
                    continue;
                }

                Vector2i rotatedPosition = IsometricCameraUtils.GetRotatedPositionInverse(
                    locker.m_state.m_position.m_x,
                    locker.m_state.m_position.m_y);

                Vector3 panelPosition = Camera.main.WorldToScreenPoint(
                    IsometricUtils.WorldToIsometric(new Vector2f(rotatedPosition.m_x, rotatedPosition.m_y)) +
                    new Vector3(
                        0f,
                        1.5f + (float)Hospital.Instance.m_currentFloorIndex * IsometricUtils.FLOOR_HEIGHT,
                        0f));

                Vector3 anchorPosition = Camera.main.WorldToScreenPoint(
                    IsometricUtils.WorldToIsometric(new Vector2f(rotatedPosition.m_x, rotatedPosition.m_y)) +
                    new Vector3(
                        0f,
                        (float)Hospital.Instance.m_currentFloorIndex * IsometricUtils.FLOOR_HEIGHT,
                        0f));

                float scale = Mathf.Min(
                    1f,
                    0.25f + 1f / CameraController.sm_instance.GetZoomLevel() * 0.125f);

                Entity dayOwner = locker.GetWorkspaceOwner(Shift.DAY);
                Entity nightOwner = locker.GetWorkspaceOwner(Shift.NIGHT);

                if (!controller.m_workspacePanels.ContainsKey(locker))
                {
                    CreateLockerPanel(
                        controller,
                        department,
                        room,
                        locker,
                        dayOwner,
                        nightOwner,
                        panelPosition,
                        anchorPosition,
                        scale);
                }
                else
                {
                    UpdateLockerPanel(
                        controller,
                        locker,
                        dayOwner,
                        nightOwner,
                        panelPosition,
                        anchorPosition,
                        scale);
                }
            }
        }

        internal static bool IsPorterLocker(TileObject locker)
        {
            if (locker == null || !locker.HasTag("ui_locker") || MapScriptInterface.Instance == null)
            {
                return false;
            }

            Room room = MapScriptInterface.Instance.GetRoomAt(locker.m_state.m_position, locker.GetFloorIndex());
            return room != null &&
                PorterStationRegistry.IsPorterStation(room.m_roomPersistentData.m_roomType.Entry);
        }

        private static bool TryGetValidPorterLocker(
            TileObject locker,
            Department department,
            out Room room)
        {
            room = null;
            if (!IsPorterLocker(locker))
            {
                return false;
            }

            room = MapScriptInterface.Instance.GetRoomAt(locker.m_state.m_position, locker.GetFloorIndex());
            if (room == null || room.m_roomPersistentData.m_department.GetEntity() != department)
            {
                return false;
            }

            return room.GetEquipmentOk();
        }

        private static void CreateLockerPanel(
            LogisticsUIController controller,
            Department department,
            Room room,
            TileObject locker,
            Entity dayOwner,
            Entity nightOwner,
            Vector3 panelPosition,
            Vector3 anchorPosition,
            float scale)
        {
            GameObject anchor = UnityEngine.Object.Instantiate(controller.m_prefabLogisticsAnchor);
            controller.m_workspaceAnchors[locker] = anchor;
            anchor.transform.SetParent(controller.m_canvas.transform);
            RectTransform anchorRect = anchor.GetComponent<RectTransform>();
            anchorRect.anchoredPosition = Vector2.zero;
            anchorRect.localScale = new Vector3(scale, scale, 1f);
            anchorRect.position = anchorPosition +
                new Vector3(0f, anchorRect.sizeDelta.y * anchorRect.localScale.y / 2f);

            GameObject panel = UnityEngine.Object.Instantiate(controller.m_prefabLogisticsWorkspacePanel);
            LogisticsWorkspacePanelController panelController = panel.GetComponent<LogisticsWorkspacePanelController>();
            panelController.Init(
                controller.m_selectCharactersPanel,
                controller.m_characterCard,
                DisplayedCharacterType.LAB_SPECIALIST,
                locker);

            // DisplayedCharacterType is an UI classification here. Porter entities still carry
            // BehaviorNurse and stay in Department.m_nurses for the native transport engine.
            // LAB_SPECIALIST is retained only as the already-validated internal workspace routing key;
            // Hospital Porters replaces the visible recruitment/employee presentation with Porter UI.
            panelController.SetWorkspaceButtonClickedDelegate(delegate(Shift shift)
            {
                HandleLockerWorkspaceClick(controller, department, room, locker, shift);
            });

            panelController.SetDelegateCharacterSelected(delegate(int index, DisplayedCharacterType displayedCharacterType, Shift shift)
            {
                return HandleLockerCharacterSelected(
                    controller,
                    department,
                    room,
                    locker,
                    index,
                    displayedCharacterType,
                    shift);
            });

            MapEditorUIController.Instance.m_panelsBlockingInput.Add(panel);
            MapEditorUIController.Instance.m_panelsBlockingInput.Add(panelController.m_panelButtons);
            MapEditorUIController.Instance.m_panelsBlockingInput.Add(panelController.m_panelButtonsNight);

            panel.transform.SetParent(controller.m_canvas.transform);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.position = panelPosition;

            panelController.SetCharacter(dayOwner, nightOwner);
            ApplyPorterEmptyIcons(panelController, dayOwner, nightOwner);
            ApplyPorterWorkspaceRoleIcons(panelController, dayOwner, nightOwner);
            ApplyPanelScale(panelController, panelRect, scale);

            controller.m_workspacePanels[locker] = panel;
            panel.SetActive(value: true);
        }

        private static void UpdateLockerPanel(
            LogisticsUIController controller,
            TileObject locker,
            Entity dayOwner,
            Entity nightOwner,
            Vector3 panelPosition,
            Vector3 anchorPosition,
            float scale)
        {
            if (!controller.m_workspaceAnchors.TryGetValue(locker, out GameObject anchor) || anchor == null ||
                !controller.m_workspacePanels.TryGetValue(locker, out GameObject panel) || panel == null)
            {
                return;
            }

            RectTransform anchorRect = anchor.GetComponent<RectTransform>();
            anchorRect.anchoredPosition = Vector2.zero;
            anchorRect.localScale = new Vector3(scale, scale, 1f);
            anchorRect.position = anchorPosition +
                new Vector3(0f, anchorRect.sizeDelta.y * anchorRect.localScale.y / 2f);

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.position = panelPosition;

            LogisticsWorkspacePanelController panelController = panel.GetComponent<LogisticsWorkspacePanelController>();
            panelController.SetCharacter(dayOwner, nightOwner);
            ApplyPorterEmptyIcons(panelController, dayOwner, nightOwner);
            ApplyPorterWorkspaceRoleIcons(panelController, dayOwner, nightOwner);
            ApplyPanelScale(panelController, panelRect, scale);
        }

        private static void ApplyPanelScale(
            LogisticsWorkspacePanelController panelController,
            RectTransform panelRect,
            float scale)
        {
            panelRect.localScale = panelController.m_underCursor
                ? new Vector3(scale, scale, 1f)
                : new Vector3(scale * 0.8f, scale * 0.8f, 1f);
        }

        private static void ApplyPorterEmptyIcons(
            LogisticsWorkspacePanelController panelController,
            Entity dayOwner,
            Entity nightOwner)
        {
            if (dayOwner == null && panelController.m_icon != null)
            {
                PorterVisuals.ApplyWorkspaceEmptyIcon(panelController.m_icon.GetComponent<IconController>());
            }

            if (nightOwner == null && panelController.m_iconNight != null)
            {
                PorterVisuals.ApplyWorkspaceEmptyIcon(panelController.m_iconNight.GetComponent<IconController>());
            }
        }

        private static void ApplyPorterWorkspaceRoleIcons(
            LogisticsWorkspacePanelController panelController,
            Entity dayOwner,
            Entity nightOwner)
        {
            if (panelController == null)
            {
                return;
            }

            ApplyPorterWorkspaceRoleIcons(dayOwner, panelController.m_dayRoles);
            ApplyPorterWorkspaceRoleIcons(nightOwner, panelController.m_nightRoles);
        }

        private static void ApplyPorterWorkspaceRoleIcons(
            Entity character,
            List<GameObject> roles)
        {
            if (!PorterIdentity.IsPorter(character) || roles == null)
            {
                return;
            }

            EmployeeComponent employee = character.GetComponent<EmployeeComponent>();
            if (employee == null || Database.Instance == null)
            {
                return;
            }

            int visibleRoleIndex = 0;
            GameDBEmployeeRole[] roleEntries =
                Database.Instance.GetEntries<GameDBEmployeeRole>();

            foreach (GameDBEmployeeRole role in roleEntries)
            {
                if (!employee.HasOccupationForRole(role))
                {
                    continue;
                }

                bool visible = false;
                bool active = false;

                if (role.AlwaysChecked &&
                    role.ForceWorkSpace &&
                    employee.m_state.m_homeRoom != null &&
                    employee.m_state.m_homeRoom.GetEntity() != null &&
                    employee.m_state.m_homeRoom.GetEntity()
                        .m_roomPersistentData.m_roomType.Entry.HasTag(role.ForceWorkSpaceTag))
                {
                    visible = true;
                    active = true;
                }
                else if (!employee.HasSkillForRole(role) ||
                         !employee.HasWorkspaceForRole(role))
                {
                    if (employee.HasRole(role))
                    {
                        visible = true;
                        active = true;
                    }
                }
                else
                {
                    visible = true;
                    active = employee.HasRole(role);
                }

                if (!visible)
                {
                    continue;
                }

                if (visibleRoleIndex >= roles.Count)
                {
                    break;
                }

                GameObject roleObject = roles[visibleRoleIndex];
                if (roleObject != null)
                {
                    IconController icon = roleObject.GetComponent<IconController>();
                    PorterVisuals.ApplyWorkspaceRoleIcon(icon, role, active);
                }

                visibleRoleIndex++;
            }
        }

        internal static bool ClickPorterHiringFilter()
        {
            GameObject hiringPanel = MapEditorUIController.Instance?.m_hiringPanel;
            if (hiringPanel == null)
            {
                return false;
            }

            IconButtonController[] buttons =
                hiringPanel.GetComponentsInChildren<IconButtonController>(includeInactive: true);

            foreach (IconButtonController button in buttons)
            {
                Transform current = button.transform;
                while (current != null && current != hiringPanel.transform)
                {
                    if (current.name == PorterFilterObjectName)
                    {
                        button.OnClick();
                        return true;
                    }
                    current = current.parent;
                }
            }

            return false;
        }

        private static bool HandleLockerCharacterSelected(
            LogisticsUIController controller,
            Department department,
            Room room,
            TileObject locker,
            int index,
            DisplayedCharacterType displayedCharacterType,
            Shift shift)
        {
            if (controller == null || department == null || room == null || locker == null ||
                index < 0 || index >= department.m_departmentPersistentData.m_nurses.Count)
            {
                return false;
            }

            // SelectCharacterFloatingController translates Porter table rows back to the real
            // Department.m_nurses index. Portrait buttons already use that native Nurse index.
            Entity character = department.m_departmentPersistentData.m_nurses[index].GetEntity();
            EmployeeComponent employee = character == null
                ? null
                : character.GetComponent<EmployeeComponent>();
            if (character == null || !PorterIdentity.IsPorter(character) ||
                employee == null || employee.IsFired())
            {
                UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_FORBIDDEN");
                return false;
            }

            if (!LogisticsWorkspacePanelController.CheckWorkspaceRules(room, shift, forHiring: false))
            {
                UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_FORBIDDEN");
                return false;
            }

            Entity currentOwner = locker.GetWorkspaceOwner(shift);
            return ApplyNativeWorkplace(controller, currentOwner, character, locker, shift);
        }

        private static void HandleLockerWorkspaceClick(
            LogisticsUIController controller,
            Department department,
            Room room,
            TileObject locker,
            Shift shift)
        {
            if (controller.m_characterSelectingWorkspace != null)
            {
                Entity selected = controller.m_characterSelectingWorkspace.GetEntity();
                EmployeeComponent selectedEmployee = selected == null
                    ? null
                    : selected.GetComponent<EmployeeComponent>();
                if (!PorterIdentity.IsPorter(selected) ||
                    selectedEmployee == null || selectedEmployee.IsFired())
                {
                    UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_FORBIDDEN");
                    return;
                }

                if (!LogisticsWorkspacePanelController.CheckWorkspaceRules(room, shift, forHiring: false))
                {
                    return;
                }

                Entity currentOwner = locker.GetWorkspaceOwner(shift);
                if (!ApplyNativeWorkplace(controller, currentOwner, selected, locker, shift))
                {
                    return;
                }

                controller.m_characterSelectingWorkspace = null;
                controller.m_mode = LogisticsUIControllerMode.ACTIVE_DEPARTMENT;
                UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_WINDOW_OPEN");
                MapEditorUIController.Instance.m_logisticsPanel.SetActive(value: true);
                MapEditorUIController.Instance.m_logisticsPanel
                    .GetComponent<DepartmentManagementLogisticsHybridController>()
                    .m_panelRight.SetActive(value: true);
                controller.UpdateWorkspacePanels(forceClear: true);
                return;
            }

            Entity owner = locker.GetWorkspaceOwner(shift);
            if (owner != null)
            {
                GameModePanelController.Instance.OnButtonLogisticsClicked();
                controller.m_characterCard.SetActive(value: true);
                controller.m_characterCard.GetComponent<CharacterPanelController>().Character = owner;
            }
            UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_WINDOW_OPEN");
        }

        private static bool ApplyNativeWorkplace(
            LogisticsUIController controller,
            Entity currentOwner,
            Entity character,
            TileObject locker,
            Shift shift)
        {
            if (controller == null || character == null || locker == null)
            {
                return false;
            }

            EmployeeComponent employee = character.GetComponent<EmployeeComponent>();
            if (employee == null || employee.IsFired())
            {
                UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_FORBIDDEN");
                return false;
            }

            if (object.ReferenceEquals(ManageWorkplaceMethod, null))
            {
                Plugin.Log?.LogError("Could not resolve LogisticsUIController.ManageWorkplace(...).");
                UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_FORBIDDEN");
                return false;
            }

            try
            {
                ManageWorkplaceMethod.Invoke(
                    controller,
                    new object[] { currentOwner, character, locker.m_state.m_position, locker, shift });
                return true;
            }
            catch (TargetInvocationException exception)
            {
                Plugin.Log?.LogError(
                    "Native Porter workspace assignment failed: " +
                    (exception.InnerException ?? exception));
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogError("Native Porter workspace assignment failed: " + exception);
            }

            UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_FORBIDDEN");
            return false;
        }
    }

    [HarmonyPatch(
        typeof(LogisticsUIController),
        "AddWorkspacePanels",
        new Type[] { typeof(Department) })]
    internal static class PorterLockerWorkspacePanelsPatch
    {
        private static void Postfix(LogisticsUIController __instance, Department department)
        {
            PorterLogisticsUi.AddLockerPanels(__instance, department);
        }
    }

    [HarmonyPatch(
        typeof(LogisticsWorkspacePanelController),
        "OpenHiringCard",
        new Type[] { typeof(Shift) })]
    internal static class PorterOpenHiringCardPatch
    {
        private static void Postfix(Shift shift)
        {
            if (HiringManager.Instance == null || HiringManager.Instance.m_workspace == null)
            {
                return;
            }

            TileObject workspace = HiringManager.Instance.m_workspace.GetEntity();
            if (!PorterLogisticsUi.IsPorterLocker(workspace))
            {
                return;
            }

            // The internal workspace route selects Porter before OpenHiringCard returns.
            // Keep this only as a defensive fallback, not as the normal late UI switch.
            if (!PorterHiringState.Active && !PorterLogisticsUi.ClickPorterHiringFilter())
            {
                Plugin.Log?.LogError("Porter locker opened hiring but the Porter filter button could not be selected.");
                return;
            }
        }
    }
}