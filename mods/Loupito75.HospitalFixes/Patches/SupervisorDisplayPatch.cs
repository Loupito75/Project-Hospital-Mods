using System;
using HarmonyLib;
using GLib;
using Lopital;
using UnityEngine.UI;

namespace HospitalFixes.Patches
{
    [HarmonyPatch(
        typeof(CharacterPanelWorkPanelController),
        nameof(CharacterPanelWorkPanelController.UpdateWorkData),
        new Type[] { typeof(EmployeeComponent) })]
    internal static class SupervisorDisplayPatch
    {
        private static void Postfix(
            CharacterPanelWorkPanelController __instance,
            EmployeeComponent employeeComponent)
        {
            if (!FixConfig.FixSupervisorDisplay ||
                __instance == null ||
                employeeComponent == null ||
                employeeComponent.m_state == null ||
                employeeComponent.m_state.m_supervisor == null ||
                __instance.m_supervisorSegmentStatic == null)
            {
                return;
            }

            Entity supervisor =
                employeeComponent.m_state.m_supervisor.GetEntity();
            if (supervisor == null)
            {
                return;
            }

            string occupationId =
                EmployeeLevelVisualHelper.GetOccupationId(supervisor);
            bool doctor =
                occupationId == "OCCUPATION_DOCTOR" &&
                supervisor.GetComponent<BehaviorDoctor>() != null;
            bool nurse =
                occupationId == "OCCUPATION_NURSE" &&
                supervisor.GetComponent<BehaviorNurse>() != null;
            bool janitor =
                occupationId == "OCCUPATION_JANITOR" &&
                supervisor.GetComponent<BehaviorJanitor>() != null;

            EmployeeComponent supervisorEmployee =
                supervisor.GetComponent<EmployeeComponent>();
            if ((!doctor && !nurse && !janitor) ||
                supervisorEmployee == null ||
                supervisorEmployee.m_state == null)
            {
                return;
            }

            SegmentController segment =
                __instance.m_supervisorSegmentStatic
                    .GetComponent<SegmentController>();
            if (segment == null)
            {
                return;
            }

            // Vanilla has only Doctor and Nurse portrait containers.
            // Janitors therefore reuse the non-doctor portrait container,
            // while their level gauge is restored from the native Janitor
            // level prefab below.
            SegmentItemType expectedType =
                doctor
                    ? SegmentItemType.PORTRAIT_DOCTOR
                    : SegmentItemType.PORTRAIT_NURSE;

            if (segment.GetSegmentItemType() != expectedType)
            {
                segment.SwitchType(expectedType);
            }

            Text name = segment.GetItemComponent<Text>(0);
            RawImage portrait = segment.GetItemComponent<RawImage>(0);
            GaugeIconsController level =
                segment.GetItemComponent<GaugeIconsController>(0);

            CharacterPersonalInfoComponent personalInfo =
                supervisor.GetComponent<CharacterPersonalInfoComponent>();

            if (name != null &&
                personalInfo != null &&
                personalInfo.m_personalInfo != null)
            {
                name.text =
                    personalInfo.m_personalInfo.GetFullName();
            }

            if (portrait != null &&
                PortraitManager.Instance != null)
            {
                portrait.texture =
                    PortraitManager.Instance
                        .GetSlot(supervisor)
                        .GetTexture();
            }

            if (level != null)
            {
                // Restores Doctor/Nurse/Janitor native gauge artwork and
                // applies the correct 5/3/3 scale. In particular, Janitor
                // supervisors no longer inherit the Doctor five-notch gauge.
                if (!EmployeeLevelVisualHelper
                        .ApplyNativeVanillaProfessionVisuals(
                            level,
                            supervisor,
                            true))
                {
                    level.SetValues(
                        supervisorEmployee.m_state.m_level,
                        doctor ? 5 : 3);
                }
            }

            if (HfxDiagnostics.Enabled)
            {
                string profession =
                    doctor
                        ? "doctor"
                        : nurse
                            ? "nurse"
                            : "janitor";
                int maxLevel = doctor ? 5 : 3;
                string supervisorLabel =
                    HfxDiagnostics.EntityLabel(supervisor);

                HfxDiagnostics.ResultRateLimited(
                    "FixSupervisorDisplay",
                    "CharacterPanelWorkPanelController.UpdateWorkData",
                    "supervisor-ui-values-updated",
                    "supervisor=" + supervisorLabel +
                    " profession=" + profession +
                    " level=" +
                    supervisorEmployee.m_state.m_level +
                    " max=" + maxLevel,
                    "FixSupervisorDisplay|" + supervisorLabel,
                    5000);
            }
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
    internal static class SupervisorEmployeeTableIconPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Normal)]
        private static void Postfix(
            Entity employee,
            int rowIndex,
            TableController tableController)
        {
            if (!FixConfig.FixSupervisorDisplay ||
                employee == null ||
                tableController == null ||
                IconManager.Instance == null)
            {
                return;
            }

            EmployeeComponent employeeComponent =
                employee.GetComponent<EmployeeComponent>();
            if (employeeComponent == null ||
                employeeComponent.m_state == null ||
                employeeComponent.m_state.m_supervisor == null)
            {
                return;
            }

            Entity supervisor =
                employeeComponent.m_state.m_supervisor.GetEntity();
            if (supervisor == null ||
                EmployeeLevelVisualHelper.GetOccupationId(supervisor) !=
                    "OCCUPATION_JANITOR" ||
                supervisor.GetComponent<BehaviorJanitor>() == null)
            {
                return;
            }

            int visibleRow =
                tableController.GetRowIndexForDataRow(rowIndex);
            if (visibleRow < 0)
            {
                return;
            }

            TableItem supervisorItem =
                tableController.GetItem(visibleRow, 7);
            Image icon =
                supervisorItem == null ||
                supervisorItem.m_gameObject == null
                    ? null
                    : supervisorItem.m_gameObject
                        .GetComponentInChildren<Image>();
            if (icon == null)
            {
                return;
            }

            // Vanilla hard-codes icon 130 here, which is the gray/light
            // background Doctor variant (ICON_DOCTOR + 2). The matching
            // Janitor variant is therefore ICON_JANITOR + 2.
            icon.sprite =
                IconManager.Instance.GetIcon(
                    IconManager.ICON_JANITOR + 2);
        }
    }

}
