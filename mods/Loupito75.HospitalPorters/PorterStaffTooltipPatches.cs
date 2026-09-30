using System;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalPorters
{
    [HarmonyPatch(
        typeof(TooltipStaff),
        nameof(TooltipStaff.UpdateData),
        new Type[] { typeof(Entity) })]
    internal static class PorterNurseStaffTooltipPatch
    {
        private static bool s_nativeSpritesCaptured;
        private static Sprite s_nativeBaseSprite;
        private static Sprite s_nativeTopSprite;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(TooltipStaff __instance)
        {
            CaptureNativeSprites(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(TooltipStaff __instance, Entity employee)
        {
            if (__instance == null || employee == null ||
                employee.GetComponent<BehaviorNurse>() == null ||
                __instance.m_gaugeLevelNurse == null)
            {
                return;
            }

            EmployeeComponent employeeComponent =
                employee.GetComponent<EmployeeComponent>();
            if (employeeComponent == null)
            {
                return;
            }

            __instance.SetLevelGauge(
                doctorOn: false,
                nurseOn: true,
                labSpecialistOn: false,
                janitor: false);

            GaugeIconsController gauge =
                __instance.m_gaugeLevelNurse
                    .GetComponentInChildren<GaugeIconsController>(
                        includeInactive: true);
            if (gauge == null)
            {
                return;
            }

            gauge.SetValues(employeeComponent.m_state.m_level, 3);

            if (PorterIdentity.IsPorter(employee))
            {
                RestoreNativeSprites(gauge);
                PorterVisuals.ApplyLightLevelVisuals(gauge);

                if (__instance.m_textOccupation != null)
                {
                    __instance.m_textOccupation.GetComponent<Text>().text =
                        LocalizationManager.Get(
                            PorterIds.GetPorterLevelLocalizationId(
                                employeeComponent.m_state.m_level));
                }
                return;
            }

            RestoreNativeSprites(gauge);
        }

        private static void CaptureNativeSprites(TooltipStaff tooltip)
        {
            if (s_nativeSpritesCaptured ||
                tooltip == null ||
                tooltip.m_gaugeLevelNurse == null)
            {
                return;
            }

            GaugeIconsController gauge =
                tooltip.m_gaugeLevelNurse
                    .GetComponentInChildren<GaugeIconsController>(
                        includeInactive: true);
            Image baseImage = PorterVisuals.GetGaugeBaseImage(gauge);
            Image topImage = PorterVisuals.GetGaugeTopImage(gauge);
            if (baseImage == null ||
                topImage == null ||
                baseImage.sprite == null ||
                topImage.sprite == null)
            {
                return;
            }

            s_nativeBaseSprite = baseImage.sprite;
            s_nativeTopSprite = topImage.sprite;
            s_nativeSpritesCaptured = true;
        }

        private static void RestoreNativeSprites(
            GaugeIconsController gauge)
        {
            if (!s_nativeSpritesCaptured || gauge == null)
            {
                return;
            }

            Image baseImage = PorterVisuals.GetGaugeBaseImage(gauge);
            Image topImage = PorterVisuals.GetGaugeTopImage(gauge);
            if (baseImage != null)
            {
                baseImage.sprite = s_nativeBaseSprite;
            }
            if (topImage != null)
            {
                topImage.sprite = s_nativeTopSprite;
            }
        }
    }
}
