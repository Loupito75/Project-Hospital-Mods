using System;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine.UI;

namespace HospitalShiftHandover
{
    internal enum HshEmployeeCardStatus
    {
        None,
        WaitingForShift,
        ShiftHandover,
        ChangingClothes,
        WaitingForRelief
    }

    internal static class HshEmployeeCardStatusResolver
    {
        internal static bool TryGetStatus(
            Entity character,
            out HshEmployeeCardStatus status)
        {
            status = HshEmployeeCardStatus.None;

            if (character == null)
            {
                return false;
            }

            EmployeeComponent employee =
                character.GetComponent<EmployeeComponent>();
            if (employee == null ||
                employee.m_state == null ||
                employee.IsFired())
            {
                return false;
            }

            if (PreShiftLockerInteraction.TryGetEmployeeCardStatus(
                    employee,
                    out status))
            {
                return true;
            }

            if (PreShiftCoordinator.TryGetEmployeeCardStatus(
                    employee,
                    out status))
            {
                return true;
            }

            if (OutgoingHandover.HasActiveLateReliefHold(employee) &&
                IsIdleForHshPresentation(character, employee))
            {
                status = HshEmployeeCardStatus.WaitingForRelief;
                return true;
            }

            if (!HandoverRules.ShouldHoldBeforeShift(employee))
            {
                return false;
            }

            Behavior behavior =
                character.GetComponent<Behavior>();

            if (behavior != null &&
                PreShiftArrival.IsInCommonArea(behavior) &&
                PreShiftCommonAreaController
                    .IsWaitingForShiftPresentation(employee) &&
                IsIdleForHshPresentation(character, employee))
            {
                status = HshEmployeeCardStatus.WaitingForShift;
                return true;
            }

            if (IsIdleForHshPresentation(character, employee) &&
                IsAtWorkplace(character, employee))
            {
                status = HshEmployeeCardStatus.WaitingForShift;
                return true;
            }

            return false;
        }

        private static bool IsAtWorkplace(
            Entity character,
            EmployeeComponent employee)
        {
            BehaviorDoctor doctor =
                character.GetComponent<BehaviorDoctor>();
            if (doctor != null)
            {
                return doctor.IsAtWorkplace(employee);
            }

            BehaviorNurse nurse =
                character.GetComponent<BehaviorNurse>();
            if (nurse != null)
            {
                return nurse.IsAtWorkplace(employee);
            }

            BehaviorLabSpecialist lab =
                character.GetComponent<BehaviorLabSpecialist>();
            return lab != null &&
                   lab.IsAtWorkplace(employee);
        }

        private static bool IsIdleForHshPresentation(
            Entity character,
            EmployeeComponent employee)
        {
            if (character == null ||
                employee == null ||
                employee.m_state == null)
            {
                return false;
            }

            ProcedureComponent procedure =
                character.GetComponent<ProcedureComponent>();
            if (procedure != null && procedure.IsBusy())
            {
                return false;
            }

            if (employee.m_state.m_reservedByPatient != null &&
                employee.m_state.m_reservedByPatient.GetEntity() != null)
            {
                return false;
            }

            BehaviorDoctor doctor =
                character.GetComponent<BehaviorDoctor>();
            if (doctor != null)
            {
                return doctor.m_state != null &&
                       doctor.m_state.m_doctorState == DoctorState.Idle &&
                       doctor.CurrentPatient == null;
            }

            BehaviorNurse nurse =
                character.GetComponent<BehaviorNurse>();
            if (nurse != null)
            {
                return nurse.m_state != null &&
                       nurse.m_state.m_nurseState == NurseState.Idle;
            }

            BehaviorLabSpecialist lab =
                character.GetComponent<BehaviorLabSpecialist>();
            if (lab != null)
            {
                return lab.m_state != null &&
                       lab.m_state.m_labSpecialistState ==
                           LabSpecialistState.Idle;
            }

            return false;
        }
    }

    internal static class HshEmployeeCardLocalization
    {
        private const string WaitingForShiftLocId =
            "L75_HSH_STATUS_WAITING_FOR_SHIFT";
        private const string ShiftHandoverLocId =
            "L75_HSH_STATUS_SHIFT_HANDOVER";
        private const string ChangingClothesLocId =
            "L75_HSH_STATUS_CHANGING_CLOTHES";
        private const string WaitingForReliefLocId =
            "L75_HSH_STATUS_WAITING_FOR_RELIEF";

        internal static string Get(HshEmployeeCardStatus status)
        {
            string locId;
            string englishFallback;

            switch (status)
            {
                case HshEmployeeCardStatus.WaitingForShift:
                    locId = WaitingForShiftLocId;
                    englishFallback = "Waiting for shift";
                    break;

                case HshEmployeeCardStatus.ShiftHandover:
                    locId = ShiftHandoverLocId;
                    englishFallback = "Shift handover";
                    break;

                case HshEmployeeCardStatus.ChangingClothes:
                    locId = ChangingClothesLocId;
                    englishFallback = "Changing clothes";
                    break;

                case HshEmployeeCardStatus.WaitingForRelief:
                    locId = WaitingForReliefLocId;
                    englishFallback = "Waiting for relief";
                    break;

                default:
                    return null;
            }

            StringTable strings = StringTable.GetInstance();
            if (strings != null &&
                !string.IsNullOrEmpty(strings.GetCurrentLanguage()))
            {
                string localized = strings.GetLocalizedText(locId);
                if (!string.IsNullOrEmpty(localized) &&
                    !localized.StartsWith(
                        "!LOC! ",
                        StringComparison.Ordinal))
                {
                    return localized;
                }
            }

            return englishFallback;
        }
    }

    [HarmonyPatch(
        typeof(CharacterPanelController),
        nameof(CharacterPanelController.Update),
        new Type[] { })]
    [HarmonyBefore("Loupito75:HospitalPorters")]
    internal static class HshCharacterPanelStatusPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            CharacterPanelController __instance)
        {
            if (__instance == null ||
                __instance.m_status == null)
            {
                return;
            }

            Entity character = __instance.Character;
            HshEmployeeCardStatus status;
            if (!HshEmployeeCardStatusResolver.TryGetStatus(
                    character,
                    out status))
            {
                return;
            }

            string localized =
                HshEmployeeCardLocalization.Get(status);
            if (string.IsNullOrEmpty(localized))
            {
                return;
            }

            Text statusText =
                __instance.m_status.GetComponent<Text>();
            if (statusText == null)
            {
                return;
            }

            statusText.text = localized;
            UIManager.UpdateFont(__instance.m_status);
        }
    }
}
