using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using UnityEngine.UI;

namespace HospitalPorters
{
    [HarmonyPatch(
        typeof(CharacterPanelController),
        nameof(CharacterPanelController.Update),
        new Type[] { })]
    internal static class PorterSampleCharacterStatusPatch
    {
        private const string TechnologistSampleStatusLocId =
            "TECHNOLOGIST_STATE_GoingForSampleFromHospitalizedPatient";

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

            object value = CharacterField.GetValue(__instance);
            if (value == null)
            {
                return;
            }

            EntityIDPointer<Entity> characterPointer =
                (EntityIDPointer<Entity>)value;
            Entity employee = characterPointer.GetEntity();
            if (!PorterIdentity.IsPorter(employee) ||
                !PorterSampleTransportRuntime.IsBusy(employee))
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
                    TechnologistSampleStatusLocId);
            UIManager.UpdateFont(__instance.m_status);
        }
    }
}
