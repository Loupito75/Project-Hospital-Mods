using System;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalPorters
{
    internal static class PorterHospitalizationHiringUi
    {
        internal static void RefreshDisabledPorterFilter(HiringPanelController controller)
        {
            if (controller == null || HiringManager.Instance == null ||
                HiringManager.Instance.m_workspace == null || MapScriptInterface.Instance == null)
            {
                return;
            }

            TileObject workspace = HiringManager.Instance.m_workspace.GetEntity();
            if (workspace == null || PorterLogisticsUi.IsPorterLocker(workspace))
            {
                return;
            }

            Room room = MapScriptInterface.Instance.GetRoomAt(
                workspace.GetDefaultUseTile(),
                workspace.GetFloorIndex());
            Department department = room?.m_roomPersistentData.m_department.GetEntity();
            GameDBDepartment departmentType = department?.GetDepartmentType();
            if (room == null || departmentType == null || departmentType.NoHospitalization ||
                !room.m_roomPersistentData.m_roomType.Entry.HasTag("hospitalization_workspace") ||
                Database.Instance.GetEntry<GameDBOccupation>(PorterIds.Occupation) == null)
            {
                return;
            }

            PorterHiringUi.EnsureButton(controller);
            if (!PorterHiringUi.TryGetState(controller, out PorterHiringUiState state) ||
                state.Button == null || state.IconButton == null || state.CountText == null)
            {
                return;
            }

            PositionBesideNativeJanitor(controller, state);

            state.Button.SetActive(value: true);
            PorterVisuals.ApplyCategoryIcons(state.IconButton);
            state.IconButton.RemoveOnClickDelegate();
            state.IconButton.SetInactive(inactive: true, force: true);
            PorterHiringState.Active = false;
            PorterHiringState.SelectingPorter = false;

            int hired = PorterStaffing.CountPorters(department, HiringManager.Instance.m_shift);
            state.CountText.text = hired.ToString();
            state.CountText.color = UISettings.Instance.EXAMINATION_COLOR_SUCCESSFUL;

            if (state.Background != null)
            {
                bool showOptionalBackground = hired == 0;
                state.Background.SetActive(showOptionalBackground);
                if (showOptionalBackground)
                {
                    Image image = state.Background.GetComponent<Image>();
                    if (image != null)
                    {
                        image.color = UISettings.Instance.HIRING_OPTIONAL;
                    }
                }
            }
        }

        private static void PositionBesideNativeJanitor(
            HiringPanelController controller,
            PorterHiringUiState state)
        {
            if (controller.m_administrationCharacters == null ||
                controller.m_administrationCharacters.Count == 0)
            {
                return;
            }

            RectTransform porterRect = state.Button.GetComponent<RectTransform>();
            RectTransform sourceRect =
                controller.m_administrationCharacters[0]?.GetComponent<RectTransform>();
            if (porterRect == null || sourceRect == null)
            {
                return;
            }

            Vector2 newPosition = sourceRect.anchoredPosition;
            porterRect.anchoredPosition = newPosition;
            controller.m_administrationCharacters[0].SetActive(value: false);

            if (!state.BackgroundIsChild && state.Background != null &&
                controller.m_administrationCharactersBackGround != null &&
                controller.m_administrationCharactersBackGround.Count > 0)
            {
                RectTransform backgroundRect = state.Background.GetComponent<RectTransform>();
                RectTransform sourceBackgroundRect =
                    controller.m_administrationCharactersBackGround[0]?.GetComponent<RectTransform>();
                if (backgroundRect != null && sourceBackgroundRect != null)
                {
                    Vector2 offset =
                        sourceBackgroundRect.anchoredPosition - sourceRect.anchoredPosition;
                    backgroundRect.anchoredPosition = newPosition + offset;
                }
            }
        }
    }

    [HarmonyPatch(
        typeof(LogisticsWorkspacePanelController),
        "OpenHiringCard",
        new Type[] { typeof(Shift) })]
    internal static class PorterHospitalizationHiringOpenPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            GameObject hiringPanel = MapEditorUIController.Instance?.m_hiringPanel;
            HiringPanelController controller =
                hiringPanel == null ? null : hiringPanel.GetComponent<HiringPanelController>();
            PorterHospitalizationHiringUi.RefreshDisabledPorterFilter(controller);
        }
    }

    [HarmonyPatch(
        typeof(HiringPanelController),
        nameof(HiringPanelController.SortHiringSpecializationsButtons),
        new Type[] { typeof(bool) })]
    internal static class PorterHospitalizationHiringSortPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(HiringPanelController __instance, bool clinic)
        {
            if (!clinic)
            {
                PorterHospitalizationHiringUi.RefreshDisabledPorterFilter(__instance);
            }
        }
    }
}