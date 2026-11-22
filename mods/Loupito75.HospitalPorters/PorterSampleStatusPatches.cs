using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine.UI;

namespace HospitalPorters
{
    [HarmonyPatch(
        typeof(CharacterPanelController),
        nameof(CharacterPanelController.Update),
        new Type[] { })]
    internal static class PorterCharacterStatusPatch
    {
        private static readonly FieldInfo CharacterField =
            AccessTools.Field(
                typeof(CharacterPanelController),
                "m_character");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            CharacterPanelController __instance)
        {
            if (__instance == null ||
                __instance.m_status == null ||
                object.ReferenceEquals(CharacterField, null))
            {
                return;
            }

            object value =
                CharacterField.GetValue(__instance);
            if (value == null)
            {
                return;
            }

            EntityIDPointer<Entity> characterPointer =
                (EntityIDPointer<Entity>)value;
            Entity employee =
                characterPointer.GetEntity();
            if (employee == null)
            {
                return;
            }

            string statusTextValue = null;
            string statusLocId = null;

            if (PorterIdentity.IsPorter(employee))
            {
                if (PorterSampleTransportRuntime.IsBusy(employee))
                {
                    statusTextValue =
                        GetSampleTransportStatus(employee);
                    if (string.IsNullOrEmpty(statusTextValue))
                    {
                        statusLocId =
                            PorterLocalizationIds.SampleTransportRole;
                    }
                }
                else
                {
                    BehaviorNurse nurse =
                        employee.GetComponent<BehaviorNurse>();
                    if (nurse != null &&
                        nurse.m_state != null &&
                        nurse.m_state.m_nurseState ==
                            NurseState.OverridenMovingStretcher)
                    {
                        statusLocId =
                            PorterLocalizationIds.PatientTransportRole;
                    }
                }
            }
            else
            {
                // HPO also presents the physical Sample transport phases of
                // vanilla Lab Technologists. HCP deliberately reuses these
                // same vanilla states/currentLabProcedure fields, so its stored
                // Sample fallback gets the same status without an HCP reference.
                statusTextValue =
                    GetLabSpecialistSampleTransportStatus(
                        employee);
            }

            if (string.IsNullOrEmpty(statusTextValue) &&
                string.IsNullOrEmpty(statusLocId))
            {
                return;
            }

            Text statusText =
                __instance.m_status.GetComponent<Text>();
            if (statusText == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(statusTextValue))
            {
                statusText.text = statusTextValue;
            }
            else
            {
                StringTable strings =
                    StringTable.GetInstance();
                if (strings == null)
                {
                    return;
                }

                statusText.text =
                    strings.GetLocalizedText(
                        statusLocId);
            }

            UIManager.UpdateFont(__instance.m_status);
        }

        private static string GetSampleTransportStatus(
            Entity porter)
        {
            PorterSampleTransportPhase phase;
            Room sourceRoom;
            Room statLab;
            if (!PorterSampleTransportRuntime.TryGetStatusContext(
                    porter,
                    out phase,
                    out sourceRoom,
                    out statLab))
            {
                return null;
            }

            switch (phase)
            {
                case PorterSampleTransportPhase.GoingToCart:
                case PorterSampleTransportPhase.GoingToSampleParking:
                case PorterSampleTransportPhase.GoingToSample:
                case PorterSampleTransportPhase.TakingSample:
                case PorterSampleTransportPhase.ReturningToCartAfterSample:
                    return GetRoomStatus(
                        PorterLocalizationIds
                            .SampleTransportPickupStatus,
                        sourceRoom);

                case PorterSampleTransportPhase.WaitingForFridge:
                    return GetRoomStatus(
                        PorterLocalizationIds
                            .SampleTransportWaitingStatus,
                        statLab);

                case PorterSampleTransportPhase
                        .ReturningToCartForFridgeDeparture:
                case PorterSampleTransportPhase.GoingToFridgeParking:
                case PorterSampleTransportPhase.GoingToFridge:
                case PorterSampleTransportPhase.StoringSample:
                case PorterSampleTransportPhase.ReturningToCartAfterFridge:
                    return GetRoomStatus(
                        PorterLocalizationIds
                            .SampleTransportDeliveryStatus,
                        statLab);

                case PorterSampleTransportPhase.ReturningCart:
                case PorterSampleTransportPhase.RecoveringCartAfterCancel:
                case PorterSampleTransportPhase.WaitingForCartReturnSlot:
                case PorterSampleTransportPhase.ReturningToStation:
                    return LocalizationManager.Get(
                        PorterLocalizationIds
                            .SampleTransportReturnStatus);

                default:
                    return null;
            }
        }


        private static string GetLabSpecialistSampleTransportStatus(
            Entity employee)
        {
            BehaviorLabSpecialist specialist =
                employee == null
                    ? null
                    : employee.GetComponent<BehaviorLabSpecialist>();
            if (specialist == null ||
                specialist.m_state == null ||
                specialist.m_state.m_currentLabProcedure == null)
            {
                return null;
            }

            LabProcedure procedure =
                specialist.m_state.m_currentLabProcedure.GetEntity();
            if (procedure == null ||
                procedure.m_state == null)
            {
                return null;
            }

            switch (specialist.m_state.m_labSpecialistState)
            {
                case LabSpecialistState
                        .GoingForSampleFromHospitalizedPatient:
                {
                    TileObject source =
                        procedure.m_state.m_usedEquipment == null
                            ? null
                            : procedure.m_state.m_usedEquipment.GetEntity();
                    Room sourceRoom =
                        source == null ||
                        source.m_state == null ||
                        MapScriptInterface.Instance == null
                            ? null
                            : MapScriptInterface.Instance.GetRoomAt(
                                source.m_state.m_position,
                                source.GetFloorIndex());
                    return GetRoomStatus(
                        PorterLocalizationIds
                            .SampleTransportPickupStatus,
                        sourceRoom);
                }

                case LabSpecialistState
                        .GoingToStoreSampleFromPatient:
                case LabSpecialistState
                        .StoringSampleFromPatient:
                case LabSpecialistState
                        .SampleFromPatientStored:
                {
                    EmployeeComponent employeeComponent =
                        employee.GetComponent<EmployeeComponent>();
                    Room destinationLab =
                        employeeComponent == null ||
                        employeeComponent.m_state == null ||
                        employeeComponent.m_state.m_homeRoom == null
                            ? null
                            : employeeComponent.m_state
                                .m_homeRoom.GetEntity();
                    return GetRoomStatus(
                        PorterLocalizationIds
                            .SampleTransportDeliveryStatus,
                        destinationLab);
                }

                default:
                    return null;
            }
        }

        private static string GetRoomStatus(
            string statusLocId,
            Room room)
        {
            string roomName =
                GetLocalizedRoomName(room);
            if (string.IsNullOrEmpty(roomName))
            {
                return null;
            }

            return LocalizationManager.Get(
                statusLocId,
                roomName);
        }

        private static string GetLocalizedRoomName(
            Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null ||
                room.m_roomPersistentData.m_roomType.Entry == null)
            {
                return null;
            }

            StringTable strings =
                StringTable.GetInstance();
            if (strings == null)
            {
                return null;
            }

            return strings.GetLocalizedText(
                room.m_roomPersistentData.m_roomType.Entry);
        }
    }
}
