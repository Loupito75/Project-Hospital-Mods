using System;
using HarmonyLib;
using GLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalFixes.Patches
{
    internal static class EmployeeLevelVisualHelper
    {
        internal static string GetOccupationId(Entity employee)
        {
            CharacterPersonalInfoComponent personalInfo =
                employee == null
                    ? null
                    : employee.GetComponent<CharacterPersonalInfoComponent>();

            return personalInfo == null ||
                personalInfo.m_personalInfo == null ||
                personalInfo.m_personalInfo.m_occupation.Entry == null
                    ? null
                    : personalInfo.m_personalInfo.m_occupation.Entry
                        .DatabaseID.ToString();
        }

        internal static bool IsVanillaProfession(Entity employee)
        {
            string occupationId = GetOccupationId(employee);

            if (occupationId == "OCCUPATION_DOCTOR")
            {
                return employee.GetComponent<BehaviorDoctor>() != null;
            }
            if (occupationId == "OCCUPATION_NURSE")
            {
                return employee.GetComponent<BehaviorNurse>() != null;
            }
            if (occupationId == "OCCUPATION_LAB_SPECIALIST")
            {
                return employee.GetComponent<BehaviorLabSpecialist>() != null;
            }
            if (occupationId == "OCCUPATION_JANITOR")
            {
                return employee.GetComponent<BehaviorJanitor>() != null;
            }

            return false;
        }

        internal static bool ApplyNativeVanillaProfessionVisuals(
            GaugeIconsController target,
            Entity employee,
            bool updateValue)
        {
            return ApplyNativeVanillaProfessionVisualsInternal(
                target,
                employee,
                updateValue,
                false);
        }

        internal static bool ApplyNativeVanillaProfessionTableVisuals(
            GaugeIconsController target,
            Entity employee,
            bool updateValue)
        {
            return ApplyNativeVanillaProfessionVisualsInternal(
                target,
                employee,
                updateValue,
                true);
        }

        private static bool ApplyNativeVanillaProfessionVisualsInternal(
            GaugeIconsController target,
            Entity employee,
            bool updateValue,
            bool fitTableGauge)
        {
            if (target == null || employee == null)
            {
                return false;
            }

            int maxLevel;
            UnityEngine.Object prefab;
            if (!TryGetVanillaProfessionLevelPrefab(
                    employee,
                    out maxLevel,
                    out prefab))
            {
                return false;
            }

            GameObject sourceObject = prefab as GameObject;
            GaugeIconsController source =
                sourceObject == null
                    ? null
                    : sourceObject.GetComponentInChildren<GaugeIconsController>(true);
            if (!CopyGaugeVisuals(target, source))
            {
                return false;
            }

            if (fitTableGauge)
            {
                FitTableGaugeToNativeAspect(target, source);
            }

            if (updateValue)
            {
                EmployeeComponent employeeComponent =
                    employee.GetComponent<EmployeeComponent>();
                if (employeeComponent != null &&
                    employeeComponent.m_state != null)
                {
                    target.SetValues(
                        employeeComponent.m_state.m_level,
                        maxLevel);
                }
            }

            return true;
        }

        private static bool TryGetVanillaProfessionLevelPrefab(
            Entity employee,
            out int maxLevel,
            out UnityEngine.Object prefab)
        {
            maxLevel = 0;
            prefab = null;

            StreamingAssetManager assets =
                StreamingAssetManager.GetInstance();
            string occupationId = GetOccupationId(employee);

            if (assets == null ||
                string.IsNullOrEmpty(occupationId))
            {
                return false;
            }

            if (occupationId == "OCCUPATION_DOCTOR" &&
                employee.GetComponent<BehaviorDoctor>() != null)
            {
                maxLevel = 5;
                prefab = assets.m_prefabCharacterLevelPanelDoctor;
                return prefab != null;
            }

            if (occupationId == "OCCUPATION_NURSE" &&
                employee.GetComponent<BehaviorNurse>() != null)
            {
                maxLevel = 3;
                prefab = assets.m_prefabCharacterLevelPanelNurse;
                return prefab != null;
            }

            if (occupationId == "OCCUPATION_LAB_SPECIALIST" &&
                employee.GetComponent<BehaviorLabSpecialist>() != null)
            {
                maxLevel = 3;
                prefab = assets.m_prefabCharacterLevelPanelLabSpecialist;
                return prefab != null;
            }

            if (occupationId == "OCCUPATION_JANITOR" &&
                employee.GetComponent<BehaviorJanitor>() != null)
            {
                maxLevel = 3;
                prefab = assets.m_prefabCharacterLevelPanelJanitor;
                return prefab != null;
            }

            // Custom occupations deliberately remain owned by their defining mod.
            return false;
        }

        private static bool CopyGaugeVisuals(
            GaugeIconsController target,
            GaugeIconsController source)
        {
            if (target == null || source == null)
            {
                return false;
            }

            Image targetBase = GetGaugeBaseImage(target);
            Image targetTop = GetGaugeTopImage(target);
            Image sourceBase = GetGaugeBaseImage(source);
            Image sourceTop = GetGaugeTopImage(source);

            if (targetBase == null ||
                targetTop == null ||
                sourceBase == null ||
                sourceTop == null ||
                sourceBase.sprite == null ||
                sourceTop.sprite == null)
            {
                return false;
            }

            targetBase.sprite = sourceBase.sprite;
            targetTop.sprite = sourceTop.sprite;
            return true;
        }

        private static void FitTableGaugeToNativeAspect(
            GaugeIconsController target,
            GaugeIconsController source)
        {
            Image targetBase = GetGaugeBaseImage(target);
            Image sourceBase = GetGaugeBaseImage(source);
            RectTransform targetRect =
                targetBase == null
                    ? null
                    : targetBase.rectTransform;
            RectTransform sourceRect =
                sourceBase == null
                    ? null
                    : sourceBase.rectTransform;

            if (targetRect == null ||
                sourceRect == null ||
                targetRect.sizeDelta.y <= 0f ||
                sourceRect.sizeDelta.x <= 0f ||
                sourceRect.sizeDelta.y <= 0f)
            {
                return;
            }

            float targetHeight = targetRect.sizeDelta.y;
            targetRect.sizeDelta =
                new Vector2(
                    targetHeight *
                        sourceRect.sizeDelta.x /
                        sourceRect.sizeDelta.y,
                    targetHeight);
            targetRect.anchoredPosition = Vector2.zero;
        }

        private static Image GetGaugeBaseImage(
            GaugeIconsController gauge)
        {
            if (gauge == null ||
                gauge.transform == null ||
                gauge.transform.parent == null)
            {
                return null;
            }

            return gauge.transform.parent.GetComponent<Image>();
        }

        private static Image GetGaugeTopImage(
            GaugeIconsController gauge)
        {
            if (gauge == null ||
                gauge.m_gaugeTopLayer == null)
            {
                return null;
            }

            return gauge.m_gaugeTopLayer.GetComponent<Image>();
        }
    }

    [HarmonyPatch(
        typeof(EmployeesTable),
        "FillCharacterRow",
        new Type[]
        {
            typeof(Entity),
            typeof(int),
            typeof(TableController)
        })]
    internal static class EmployeeListLevelDisplayPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Normal)]
        private static void Postfix(
            Entity employee,
            int rowIndex,
            TableController tableController)
        {
            if (!FixConfig.FixEmployeeListLevelDisplay ||
                employee == null ||
                tableController == null ||
                !EmployeeLevelVisualHelper
                    .IsVanillaProfession(employee))
            {
                // Custom occupations remain owned by their defining mod.
                return;
            }

            int visibleRow =
                tableController.GetRowIndexForDataRow(rowIndex);
            if (visibleRow < 0)
            {
                return;
            }

            TableItem item =
                tableController.GetItem(visibleRow, 3);
            GaugeIconsController gauge =
                item == null ||
                item.m_gameObject == null
                    ? null
                    : item.m_gameObject
                        .GetComponentInChildren<GaugeIconsController>();

            EmployeeLevelVisualHelper
                .ApplyNativeVanillaProfessionTableVisuals(
                    gauge,
                    employee,
                    true);
        }
    }

}
