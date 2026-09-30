using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalPorters
{
    [HarmonyPatch(
        typeof(CharacterPanelSkillPanelController),
        "FillCharacterLevelSegment",
        new Type[] { typeof(EmployeeComponent) })]
    internal static class PorterEmployeeCardLegacySkillRepairPatch
    {
        private static readonly FieldInfo EmployeeEntityField =
            AccessTools.Field(typeof(EmployeeComponent), "m_entity");

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(EmployeeComponent employeeComponent)
        {
            Entity entity = GetEmployeeEntity(employeeComponent);
            if (!PorterIdentity.IsPorter(entity))
            {
                return;
            }

            RepairLegacyPorterSkillSet(entity, employeeComponent);
        }

        private static Entity GetEmployeeEntity(EmployeeComponent employeeComponent)
        {
            if (employeeComponent == null || object.ReferenceEquals(EmployeeEntityField, null))
            {
                return null;
            }

            return EmployeeEntityField.GetValue(employeeComponent) as Entity;
        }

        private static void RepairLegacyPorterSkillSet(
            Entity entity,
            EmployeeComponent employeeComponent)
        {
            Database database = Database.Instance;
            GameDBSkill porterQualification = database == null
                ? null
                : database.GetEntry<GameDBSkill>(PorterIds.PorterQualification);
            if (porterQualification == null || employeeComponent == null ||
                employeeComponent.m_state == null)
            {
                return;
            }

            SkillSet current = employeeComponent.m_state.m_skillSet;
            if (current != null && current.HasSkill(porterQualification) &&
                current.m_qualifications.Count == 1 &&
                current.m_specialization1 == null &&
                current.m_specialization2 == null)
            {
                return;
            }

            Skill porterSkill = current == null
                ? null
                : current.GetSkill(porterQualification);
            if (porterSkill == null)
            {
                float qualificationLevel = Math.Max(
                    1f,
                    Math.Min(5f, (float)employeeComponent.m_state.m_level));
                porterSkill = new Skill(porterQualification, qualificationLevel);
            }

            SkillSet repaired = new SkillSet();
            repaired.m_qualifications.Add(porterSkill);
            employeeComponent.m_state.m_skillSet = repaired;

            Plugin.Log?.LogWarning(
                "Repaired legacy Porter skill set for " + entity.Name +
                "; removed non-Porter qualifications/specializations while preserving an existing Porter qualification when available.");
        }
    }

    [HarmonyPatch(
        typeof(CharacterPanelSkillPanelController),
        nameof(CharacterPanelSkillPanelController.UpdateSkills),
        new Type[] { typeof(EmployeeComponent) })]
    internal static class PorterEmployeeCardQualificationIconPatch
    {
        private static readonly FieldInfo EmployeeEntityField =
            AccessTools.Field(typeof(EmployeeComponent), "m_entity");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            CharacterPanelSkillPanelController __instance,
            EmployeeComponent employeeComponent)
        {
            if (__instance == null || employeeComponent == null ||
                __instance.m_qualificationsSegmentStatic == null ||
                object.ReferenceEquals(EmployeeEntityField, null))
            {
                return;
            }

            Entity entity = EmployeeEntityField.GetValue(employeeComponent) as Entity;
            if (!PorterIdentity.IsPorter(entity))
            {
                return;
            }

            SkillSet skillSet = employeeComponent.m_state.m_skillSet;
            SegmentController segment =
                __instance.m_qualificationsSegmentStatic.GetComponent<SegmentController>();
            Sprite sprite = PorterVisuals.GetQualificationSprite(lightBackground: false);
            if (skillSet == null || segment == null || sprite == null)
            {
                return;
            }

            for (int i = 0; i < skillSet.m_qualifications.Count; i++)
            {
                Skill skill = skillSet.m_qualifications[i];
                if (skill == null || skill.m_gameDBSkill.Entry == null ||
                    skill.m_gameDBSkill.Entry.DatabaseID.ToString() !=
                    PorterIds.PorterQualification ||
                    i >= segment.m_itemCount || !segment.IsInRange(i))
                {
                    continue;
                }

                SkillLevelSegmentController item =
                    segment.GetItemComponent<SkillLevelSegmentController>(i);
                Image image =
                    item == null || item.m_icon == null
                        ? null
                        : item.m_icon.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = sprite;
                }
            }
        }
    }

    [HarmonyPatch(
        typeof(CharacterPanelSkillPanelController),
        nameof(CharacterPanelSkillPanelController.UpdateSkills),
        new Type[] { typeof(EmployeeComponent) })]
    internal static class PorterEmployeeCardLevelVisualPatch
    {
        private static readonly FieldInfo EmployeeEntityField =
            AccessTools.Field(typeof(EmployeeComponent), "m_entity");
        private static bool s_loggedFailure;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            CharacterPanelSkillPanelController __instance,
            EmployeeComponent employeeComponent)
        {
            if (__instance == null || employeeComponent == null ||
                __instance.m_levelSegmentStatic == null)
            {
                return;
            }

            Entity entity = GetEmployeeEntity(employeeComponent);
            bool porter = PorterIdentity.IsPorter(entity);
            bool nurse = entity != null && entity.GetComponent<BehaviorNurse>() != null;
            if (!porter && !nurse)
            {
                return;
            }

            SegmentController segment =
                __instance.m_levelSegmentStatic.GetComponent<SegmentController>();
            if (segment == null || segment.m_itemCount <= 0 ||
                segment.GetSegmentItemType() != SegmentItemType.CHARACTER_LEVEL_NURSE)
            {
                LogFailureOnce(
                    "Porter employee-card level visual found no native Nurse level item after UpdateSkills().");
                return;
            }

            GaugeIconsController gauge =
                segment.GetItemComponent<GaugeIconsController>(0);
            PorterVisuals.ApplyNativeNurseLevelVisuals(gauge);
            if (!porter)
            {
                return;
            }

            if (!PorterVisuals.ApplyLevelVisuals(gauge))
            {
                return;
            }

            string levelTitle = LocalizationManager.Get(
                PorterIds.GetPorterLevelLocalizationId(
                    employeeComponent.m_state.m_level));
            Text levelText = segment.GetItemComponent<Text>(0);
            if (levelText != null)
            {
                levelText.text = levelTitle;
            }

            GaugeController progressGauge =
                segment.GetItemComponent<GaugeController>(0);
            if (progressGauge != null)
            {
                if (employeeComponent.m_state.m_level >= 3)
                {
                    progressGauge.SetValues(100, 100, levelTitle);
                }
                else
                {
                    progressGauge.SetValues(
                        employeeComponent.m_state.m_points,
                        employeeComponent.GetPointsNeededForNextLevel(),
                        levelTitle);
                }
            }
        }

        private static Entity GetEmployeeEntity(EmployeeComponent employeeComponent)
        {
            if (employeeComponent == null || object.ReferenceEquals(EmployeeEntityField, null))
            {
                return null;
            }

            return EmployeeEntityField.GetValue(employeeComponent) as Entity;
        }

        private static void LogFailureOnce(string message)
        {
            if (s_loggedFailure)
            {
                return;
            }

            s_loggedFailure = true;
            Plugin.Log?.LogWarning(message);
        }
    }
}