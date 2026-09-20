using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalCareLevelTransfer.Patches
{
    [HarmonyPatch(typeof(CharacterPanelController), nameof(CharacterPanelController.OnDepartmentSelected))]
    internal static class DepartmentTransferPatch
    {
        private static bool Prefix(CharacterPanelController __instance, int index, ref bool __result)
        {
            if (__instance == null || Hospital.Instance == null || index < 0 || index >= Hospital.Instance.m_departments.Count)
            {
                return true;
            }

            Entity patient = __instance.Character;
            if (patient == null)
            {
                return true;
            }

            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            if (behavior == null)
            {
                return true;
            }

            Department currentDepartment = behavior.GetDepartment();
            Department selectedDepartment = Hospital.Instance.m_departments[index];
            if (currentDepartment == null || selectedDepartment != currentDepartment)
            {
                return true;
            }

            if (!CareLevelTransferService.IsHighPriorityHospitalized(patient))
            {
                return true;
            }

            if (!__instance.IsAbleToChangeDepartments())
            {
                Reject(patient, TransferAttemptResult.Unavailable);
                __result = false;
                return false;
            }

            TransferAttemptResult transferResult = CareLevelTransferService.TryTransferToRegularWard(patient);
            if (transferResult != TransferAttemptResult.Success)
            {
                Reject(patient, transferResult);
                __result = false;
                return false;
            }

            if (UISoundManager.sm_instance != null)
            {
                UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_BEEP");
            }

            ShowFloating(patient, LocalizationManager.FloatingRequestedId);

            __result = true;
            return false;
        }

        private static void Reject(Entity patient, TransferAttemptResult result)
        {
            if (UISoundManager.sm_instance != null)
            {
                UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_FORBIDDEN");
            }

            ShowFloating(patient, GetFloatingTextId(result, patient));
        }

        private static void ShowFloating(Entity patient, string textId)
        {
            NotificationManager notificationManager = NotificationManager.GetInstance();
            if (notificationManager == null || patient == null || patient.GetComponent<WalkComponent>() == null)
            {
                return;
            }

            notificationManager.AddFloatingIngameNotification(
                patient,
                LocalizationManager.GetCurrentText(textId),
                Color.white);
        }

        private static string GetFloatingTextId(TransferAttemptResult result, Entity patient)
        {
            if (result == TransferAttemptResult.NoRegularWardBed)
            {
                return LocalizationManager.FloatingNoBedId;
            }
            if (result == TransferAttemptResult.StillRequiresHdu)
            {
                HduRequirementReason reason = CareLevelTransferService.GetHduRequirementReason(patient);
                if (reason == HduRequirementReason.HighHazard)
                {
                    return LocalizationManager.FloatingStillHduHazardId;
                }
                if (reason == HduRequirementReason.ImmobileSymptom)
                {
                    return LocalizationManager.FloatingStillHduImmobileId;
                }
                if (reason == HduRequirementReason.HighHazardAndImmobile)
                {
                    return LocalizationManager.FloatingStillHduBothId;
                }
                return LocalizationManager.FloatingStillHduId;
            }
            if (result == TransferAttemptResult.NotReadyInBed)
            {
                return LocalizationManager.FloatingWaitBedId;
            }
            return LocalizationManager.FloatingUnavailableId;
        }
    }
}
