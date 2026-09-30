using System;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalPorters
{
    internal static class PorterNativeHiringUi
    {
        private const string PorterFilterObjectName = "HospitalPorters_PorterFilter";
        private const string PorterFilterBackgroundObjectName = "HospitalPorters_PorterFilterBackground";

        internal static bool IsCurrentPorterWorkspace()
        {
            return HiringManager.Instance != null &&
                HiringManager.Instance.m_workspace != null &&
                PorterLogisticsUi.IsPorterLocker(HiringManager.Instance.m_workspace.GetEntity());
        }

        internal static void ArrangeFilter(HiringPanelController controller)
        {
            if (controller == null || !IsCurrentPorterWorkspace() ||
                controller.m_administrationCharacters == null ||
                controller.m_administrationCharacters.Count == 0)
            {
                return;
            }

            GameObject sourceButton = controller.m_administrationCharacters[0];
            GameObject porterButton = FindNamedChild(
                controller.gameObject,
                PorterFilterObjectName);
            RectTransform sourceRect = sourceButton?.GetComponent<RectTransform>();
            RectTransform porterRect = porterButton?.GetComponent<RectTransform>();
            if (sourceButton == null || sourceRect == null || porterRect == null)
            {
                return;
            }

            // Janitor and Porter are mutually exclusive in this hiring context. Reuse the
            // native Janitor slot instead of widening or shifting the administration segment.
            porterRect.anchoredPosition = sourceRect.anchoredPosition;
            sourceButton.SetActive(value: false);

            GameObject porterBackground = FindNamedChild(
                controller.gameObject,
                PorterFilterBackgroundObjectName);
            if (porterBackground != null && porterButton != null &&
                !porterBackground.transform.IsChildOf(porterButton.transform) &&
                controller.m_administrationCharactersBackGround != null &&
                controller.m_administrationCharactersBackGround.Count > 0)
            {
                RectTransform rect = porterBackground.GetComponent<RectTransform>();
                RectTransform sourceBackgroundRect =
                    controller.m_administrationCharactersBackGround[0]?.GetComponent<RectTransform>();
                if (rect != null && sourceBackgroundRect != null)
                {
                    Vector2 backgroundOffset =
                        sourceBackgroundRect.anchoredPosition - sourceRect.anchoredPosition;
                    rect.anchoredPosition = porterRect.anchoredPosition + backgroundOffset;
                }
            }
        }

        private static GameObject FindNamedChild(GameObject root, string objectName)
        {
            if (root == null)
            {
                return null;
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
            foreach (Transform transform in transforms)
            {
                if (transform != null && transform.name == objectName)
                {
                    return transform.gameObject;
                }
            }
            return null;
        }
    }

    [HarmonyPatch(
        typeof(HiringPanelController),
        nameof(HiringPanelController.SetupTechnologistFiltersDelegates),
        new Type[]
        {
            typeof(EntityIDPointer<TileObject>), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
        })]
    internal static class PorterNativeTechnologistFilterSetupPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            HiringPanelController __instance,
            EntityIDPointer<TileObject> workspace)
        {
            if (!PorterHiringUi.IsPorterWorkspace(workspace))
            {
                return;
            }

            // Only attach the custom Porter filter to the native Technologist family. Automatic
            // selection is performed once, after OpenHiringCard() has completely returned. This
            // method must never simulate an intermediate filter click.
            PorterHiringUi.ConfigureNurseDelegate(
                __instance,
                workspace,
                forceReception: false);
        }
    }

    [HarmonyPatch(
        typeof(PorterHiringUi),
        nameof(PorterHiringUi.UpdateLayout),
        new Type[] { typeof(HiringPanelController), typeof(bool) })]
    internal static class PorterNativeHiringLayoutPatch
    {
        private static bool Prefix(HiringPanelController controller, bool clinic)
        {
            if (!PorterNativeHiringUi.IsCurrentPorterWorkspace())
            {
                return true;
            }

            PorterHiringUi.EnsureButton(controller);
            PorterNativeHiringUi.ArrangeFilter(controller);
            PorterHiringUi.UpdateStaffingVisual(controller);
            return false;
        }
    }

    [HarmonyPatch(typeof(HiringPanelController), nameof(HiringPanelController.SetBackGrounds))]
    internal static class PorterNativeHiringInactiveStatePatch
    {
        private static void Prefix(
            ref bool doctorInActive,
            ref bool nurseInActive,
            ref bool technologistInActive,
            ref bool administratorInActive)
        {
            if (!PorterNativeHiringUi.IsCurrentPorterWorkspace())
            {
                return;
            }

            // Keep every native filter visible when its native sorter wants it visible, but use
            // Project Hospital's own inactive state/delegate disabling for a Porter locker.
            doctorInActive = true;
            nurseInActive = true;
            technologistInActive = true;
            administratorInActive = true;
        }
    }

    [HarmonyPatch(typeof(HiringPanelController), "UpdateButtons")]
    internal static class PorterNativeHiringCategoryButtonsPatch
    {
        private static bool Prefix(HiringPanelController __instance)
        {
            if (!PorterHiringState.SelectingPorter)
            {
                return true;
            }

            // Keep the real candidate type as CharacterNurse, but present the active top-level
            // hiring category with Project Hospital's native Janitor/support visual state.
            __instance.SetButtons(241, 243, 245, 248);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(HiringCardCharacterPanelController),
        nameof(HiringCardCharacterPanelController.FillPersonalInfo),
        new Type[] { typeof(Entity) })]
    internal static class PorterNativeHiringCardLevelPatch
    {
        private static void Postfix(HiringCardCharacterPanelController __instance, Entity character)
        {
            if (__instance == null || character == null ||
                character.GetComponent<BehaviorNurse>() == null)
            {
                return;
            }

            EmployeeComponent employee = character.GetComponent<EmployeeComponent>();
            if (employee == null)
            {
                return;
            }

            GaugeIconsController gauge = __instance.m_levelGaugeNurses
                .GetComponentInChildren<GaugeIconsController>();
            if (gauge != null)
            {
                PorterVisuals.ApplyNativeNurseLevelVisuals(gauge);
            }

            if (!PorterIdentity.IsPorter(character))
            {
                return;
            }

            __instance.SetLevelGauge(
                doctorOn: false,
                nurseOn: true,
                labSpecialistOn: false,
                janitor: false);
            if (gauge != null)
            {
                gauge.SetValues(employee.m_state.m_level, 3);
                PorterVisuals.ApplyLevelVisuals(gauge);
            }

            __instance.m_employeeOccupationText.GetComponent<Text>().text =
                LocalizationManager.Get(
                    PorterIds.GetPorterLevelLocalizationId(employee.m_state.m_level));

            if (__instance.m_skill01Icon != null)
            {
                Image qualificationImage = __instance.m_skill01Icon.GetComponent<Image>();
                Sprite qualificationLight =
                    PorterVisuals.GetQualificationSprite(lightBackground: true);
                if (qualificationImage != null && qualificationLight != null)
                {
                    qualificationImage.sprite = qualificationLight;
                }
            }
        }
    }
}