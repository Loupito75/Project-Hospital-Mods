using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalCareLevelTransfer.Patches
{
    [HarmonyPatch(typeof(CharacterPanelController), nameof(CharacterPanelController.Update))]
    internal static class CharacterPanelTooltipPatch
    {
        private static readonly FieldInfo PointerOverField =
            AccessTools.Field(typeof(IconButtonController), "m_pointerOver");

        private static readonly FieldInfo DirectTextField =
            AccessTools.Field(typeof(IconButtonController), "m_directText");

        private static IconButtonController s_smallButton;
        private static string s_smallButtonOriginalTooltip;
        private static bool s_smallButtonOriginalDirectText;
        private static bool s_smallButtonOverridden;

        private static void Postfix(CharacterPanelController __instance)
        {
            if (__instance == null)
            {
                return;
            }

            Entity patient = __instance.Character;
            if (patient == null ||
                patient.GetComponent<BehaviorPatient>() == null ||
                !CareLevelTransferService.IsHighPriorityHospitalized(patient))
            {
                RestoreSmallButtonTooltip(__instance.m_buttonSelectDepartmentSmall);
                return;
            }

            if (!__instance.IsAbleToChangeDepartments())
            {
                RestoreSmallButtonTooltip(__instance.m_buttonSelectDepartmentSmall);
                return;
            }

            bool transferButtonHovered = IsHovered(__instance.m_buttonAssignToDepartment) ||
                IsHovered(__instance.m_buttonSelectDepartmentSmall);

            HduRequirementReason hduRequirement;
            CareLevelTransferEligibility eligibility =
                CareLevelTransferService.GetEligibility(patient, out hduRequirement);

            string tooltipId = GetTooltipId(
                eligibility,
                hduRequirement,
                patient,
                transferButtonHovered);

            if (string.IsNullOrEmpty(tooltipId))
            {
                RestoreSmallButtonTooltip(__instance.m_buttonSelectDepartmentSmall);
                return;
            }

            string tooltipText = LocalizationManager.GetCurrentText(tooltipId);
            SetTooltipDirect(__instance.m_buttonAssignToDepartment, tooltipText);
            SetSmallButtonTooltipDirect(__instance.m_buttonSelectDepartmentSmall, tooltipText);
        }

        private static string GetTooltipId(
            CareLevelTransferEligibility eligibility,
            HduRequirementReason hduRequirement,
            Entity patient,
            bool isHovered)
        {
            if (eligibility == CareLevelTransferEligibility.Eligible)
            {
                if (isHovered && !CareLevelTransferService.HasFreeRegularWardBed(patient))
                {
                    return LocalizationManager.TooltipTransferReadyNoBedId;
                }

                return LocalizationManager.TooltipTransferReadyId;
            }

            if (eligibility == CareLevelTransferEligibility.StillRequiresHdu)
            {
                return GetStillHduTooltipId(hduRequirement);
            }

            if (eligibility == CareLevelTransferEligibility.NotReadyInBed)
            {
                return LocalizationManager.TooltipTransferWaitBedId;
            }

            if (eligibility == CareLevelTransferEligibility.Unavailable)
            {
                return LocalizationManager.TooltipTransferUnavailableId;
            }

            return null;
        }

        private static string GetStillHduTooltipId(HduRequirementReason reason)
        {
            if (reason == HduRequirementReason.HighHazard)
            {
                return LocalizationManager.TooltipTransferStillHduHazardId;
            }

            if (reason == HduRequirementReason.ImmobileSymptom)
            {
                return LocalizationManager.TooltipTransferStillHduImmobileId;
            }

            if (reason == HduRequirementReason.HighHazardAndImmobile)
            {
                return LocalizationManager.TooltipTransferStillHduBothId;
            }

            return LocalizationManager.TooltipTransferStillHduId;
        }

        private static bool IsHovered(GameObject button)
        {
            if (button == null || PointerOverField == null)
            {
                return false;
            }

            IconButtonController iconButton = button.GetComponentInChildren<IconButtonController>();
            if (iconButton == null)
            {
                return false;
            }

            object value = PointerOverField.GetValue(iconButton);
            return value is bool && (bool)value;
        }

        private static void SetTooltipDirect(GameObject button, string tooltipText)
        {
            IconButtonController iconButton = GetIconButton(button);
            if (iconButton != null)
            {
                iconButton.SetTextLocID(tooltipText, true);
            }
        }

        private static void SetSmallButtonTooltipDirect(GameObject button, string tooltipText)
        {
            IconButtonController iconButton = GetIconButton(button);
            if (iconButton == null)
            {
                return;
            }

            if (!s_smallButtonOverridden || s_smallButton != iconButton)
            {
                s_smallButton = iconButton;
                s_smallButtonOriginalTooltip = iconButton.m_toolTipTextLocalizationID;
                s_smallButtonOriginalDirectText = GetDirectText(iconButton);
                s_smallButtonOverridden = true;
            }

            iconButton.SetTextLocID(tooltipText, true);
        }

        private static void RestoreSmallButtonTooltip(GameObject button)
        {
            if (!s_smallButtonOverridden)
            {
                return;
            }

            IconButtonController iconButton = GetIconButton(button);
            if (iconButton != null && iconButton == s_smallButton)
            {
                iconButton.SetTextLocID(s_smallButtonOriginalTooltip, s_smallButtonOriginalDirectText);
            }

            s_smallButton = null;
            s_smallButtonOriginalTooltip = null;
            s_smallButtonOriginalDirectText = false;
            s_smallButtonOverridden = false;
        }

        private static IconButtonController GetIconButton(GameObject button)
        {
            return button == null ? null : button.GetComponentInChildren<IconButtonController>();
        }

        private static bool GetDirectText(IconButtonController iconButton)
        {
            if (iconButton == null || DirectTextField == null)
            {
                return false;
            }

            object value = DirectTextField.GetValue(iconButton);
            return value is bool && (bool)value;
        }
    }
}
