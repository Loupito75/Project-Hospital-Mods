using System;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalPorters
{
    internal static class PorterHiringIcons
    {
        internal const int PorterIcon = PorterVisuals.CategoryFallbackIcon;
        internal const int SampleTransportIcon = 2406;

        internal static IconButtonController FindPorterButton(HiringPanelController controller)
        {
            if (controller == null || controller.m_administrationCharacters == null || controller.m_administrationCharacters.Count == 0)
            {
                return null;
            }

            GameObject source = controller.m_administrationCharacters[0];
            Transform parent = source?.transform.parent;
            if (parent == null)
            {
                return null;
            }

            Transform porterFilter = parent.Find("HospitalPorters_PorterFilter");
            return porterFilter?.GetComponentInChildren<IconButtonController>(includeInactive: true);
        }
    }

    [HarmonyPatch(typeof(PorterHiringUi), nameof(PorterHiringUi.EnsureButton))]
    internal static class PorterHiringFilterIconPatch
    {
        private static void Postfix(HiringPanelController controller)
        {
            IconButtonController iconButton = PorterHiringIcons.FindPorterButton(controller);
            if (iconButton == null)
            {
                return;
            }

            PorterVisuals.ApplyCategoryIcons(iconButton);
        }
    }

    [HarmonyPatch(
        typeof(HiringPanelController),
        nameof(HiringPanelController.SetCharacterType),
        new Type[] { typeof(LopitalTypes), typeof(int), typeof(string) })]
    internal static class PorterCandidatesHeaderIconPatch
    {
        private static void Postfix(HiringPanelController __instance)
        {
            if (!PorterHiringState.Active || __instance == null || __instance.m_candidatesIcon == null)
            {
                return;
            }

            IconController icon = __instance.m_candidatesIcon.GetComponent<IconController>();
            if (icon != null)
            {
                PorterVisuals.ApplyCategoryLightIcon(icon);
            }
        }
    }
}