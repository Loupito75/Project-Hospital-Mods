using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalCareLevelTransfer.Patches
{
    internal static class DepartmentPanelTooltipSupport
    {
        private static bool s_insideSelectorUpdate;

        internal static bool InsideSelectorUpdate
        {
            get { return s_insideSelectorUpdate; }
        }

        internal static void BeginSelectorUpdate()
        {
            s_insideSelectorUpdate = true;
        }

        internal static void EndSelectorUpdate()
        {
            s_insideSelectorUpdate = false;
        }

        internal static string GetCurrentDepartmentId(Entity patient)
        {
            if (patient == null)
            {
                return null;
            }

            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            if (behavior == null)
            {
                return null;
            }

            Department currentDepartment = behavior.GetDepartment();
            if (currentDepartment == null || currentDepartment.GetDepartmentType() == null)
            {
                return null;
            }

            return currentDepartment.GetDepartmentType().DatabaseID.ToString();
        }

        internal static string GetLocalizedDepartmentName(string departmentId)
        {
            if (string.IsNullOrEmpty(departmentId))
            {
                return string.Empty;
            }

            StringTable stringTable = StringTable.GetInstance();
            if (stringTable == null)
            {
                return departmentId;
            }

            string localizedName = stringTable.GetLocalizedText(departmentId, null);
            return string.IsNullOrEmpty(localizedName) ? departmentId : localizedName;
        }

        internal static string GetTooltipId(CharacterPanelController characterPanel, Entity patient)
        {
            if (characterPanel == null || patient == null)
            {
                return null;
            }

            if (!characterPanel.IsAbleToChangeDepartments())
            {
                return LocalizationManager.TooltipCurrentUnavailableId;
            }

            HduRequirementReason hduRequirement;
            CareLevelTransferEligibility eligibility =
                CareLevelTransferService.GetEligibility(patient, out hduRequirement);

            if (eligibility == CareLevelTransferEligibility.Eligible)
            {
                return CareLevelTransferService.HasFreeRegularWardBed(patient)
                    ? LocalizationManager.TooltipCurrentReadyId
                    : LocalizationManager.TooltipCurrentNoBedId;
            }

            if (eligibility == CareLevelTransferEligibility.StillRequiresHdu)
            {
                if (hduRequirement == HduRequirementReason.HighHazard)
                {
                    return LocalizationManager.TooltipCurrentStillHduHazardId;
                }

                if (hduRequirement == HduRequirementReason.ImmobileSymptom)
                {
                    return LocalizationManager.TooltipCurrentStillHduImmobileId;
                }

                if (hduRequirement == HduRequirementReason.HighHazardAndImmobile)
                {
                    return LocalizationManager.TooltipCurrentStillHduBothId;
                }

                return LocalizationManager.TooltipCurrentStillHduId;
            }

            if (eligibility == CareLevelTransferEligibility.NotReadyInBed)
            {
                return LocalizationManager.TooltipCurrentWaitBedId;
            }

            return LocalizationManager.TooltipCurrentUnavailableId;
        }
    }

    [HarmonyPatch(typeof(SelectDepartmentFloatingController), nameof(SelectDepartmentFloatingController.Update))]
    internal static class DepartmentPanelTooltipContextPatch
    {
        private static void Prefix()
        {
            DepartmentPanelTooltipSupport.BeginSelectorUpdate();
        }

        private static void Postfix()
        {
            DepartmentPanelTooltipSupport.EndSelectorUpdate();
        }
    }

    [HarmonyPatch]
    internal static class DepartmentPanelNativeTooltipPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(TooltipText),
                nameof(TooltipText.SetText),
                new[] { typeof(string), typeof(string[]) });
        }

        private static bool Prefix(TooltipText __instance, string textLocalizationID)
        {
            if (!DepartmentPanelTooltipSupport.InsideSelectorUpdate ||
                __instance == null ||
                string.IsNullOrEmpty(textLocalizationID))
            {
                return true;
            }

            CharacterPanelController characterPanel = CharacterPanelController.Instance;
            if (characterPanel == null)
            {
                return true;
            }

            Entity patient = characterPanel.Character;
            if (patient == null || !CareLevelTransferService.IsHighPriorityHospitalized(patient))
            {
                return true;
            }

            string currentDepartmentId = DepartmentPanelTooltipSupport.GetCurrentDepartmentId(patient);
            if (string.IsNullOrEmpty(currentDepartmentId) ||
                !string.Equals(textLocalizationID, currentDepartmentId, StringComparison.Ordinal))
            {
                return true;
            }

            string tooltipId = DepartmentPanelTooltipSupport.GetTooltipId(characterPanel, patient);
            if (string.IsNullOrEmpty(tooltipId))
            {
                return true;
            }

            string departmentName = DepartmentPanelTooltipSupport.GetLocalizedDepartmentName(currentDepartmentId);
            string tooltipText = LocalizationManager.GetCurrentText(
                tooltipId,
                new[] { departmentName });

            __instance.SetTextDirect(tooltipText);
            return false;
        }
    }
}
