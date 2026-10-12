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
            if (!PorterIdentity.IsPorter(employee))
            {
                return;
            }

            string statusLocId = null;

            if (PorterSampleTransportRuntime.IsBusy(employee))
            {
                statusLocId =
                    PorterLocalizationIds.SampleTransportRole;
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

            if (string.IsNullOrEmpty(statusLocId))
            {
                return;
            }

            Text statusText =
                __instance.m_status.GetComponent<Text>();
            if (statusText == null)
            {
                return;
            }

            statusText.text =
                StringTable.GetInstance().GetLocalizedText(
                    statusLocId);
            UIManager.UpdateFont(__instance.m_status);
        }
    }
}
