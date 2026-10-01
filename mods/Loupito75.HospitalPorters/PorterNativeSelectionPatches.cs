using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalPorters
{
    internal static class PorterSelectionUi
    {
        private static readonly MethodInfo OnSelectCharacterMethod =
            AccessTools.Method(
                typeof(SelectCharacterFloatingController),
                "OnSelectCharacter",
                new Type[] { typeof(int) });
        private static readonly FieldInfo EmployeesTableCharactersField =
            AccessTools.Field(typeof(EmployeesTable), "m_characters");
        internal static readonly FieldInfo SelectCharacterButtonsField =
            AccessTools.Field(typeof(SelectCharacterFloatingController), "m_selectCharacterButtons");
        internal static readonly FieldInfo SelectCharacterPointersField =
            AccessTools.Field(typeof(SelectCharacterFloatingController), "m_charactersPointers");

        internal static bool IsPorterSelector(SelectCharacterFloatingController controller)
        {
            if (controller == null || controller.m_room == null || controller.m_room.GetEntity() == null)
            {
                return false;
            }

            return PorterStationRegistry.IsPorterStation(
                controller.m_room.GetEntity().m_roomPersistentData.m_roomType.Entry);
        }

        internal static bool IsSelectablePorter(Entity entity)
        {
            if (!PorterIdentity.IsPorter(entity))
            {
                return false;
            }

            EmployeeComponent employee = entity.GetComponent<EmployeeComponent>();
            return employee != null && !employee.IsFired();
        }

        internal static Department GetDepartment(SelectCharacterFloatingController controller)
        {
            if (controller != null && controller.m_department != null)
            {
                Department explicitDepartment = controller.m_department.GetEntity();
                if (explicitDepartment != null)
                {
                    return explicitDepartment;
                }
            }

            return Hospital.Instance?.m_activeDepartment.GetEntity();
        }

        private static bool TryGetNurseIndex(
            Department department,
            Entity target,
            out int nurseIndex)
        {
            nurseIndex = -1;
            if (department == null || target == null)
            {
                return false;
            }

            for (int i = 0; i < department.m_departmentPersistentData.m_nurses.Count; i++)
            {
                if (department.m_departmentPersistentData.m_nurses[i].GetEntity() == target)
                {
                    nurseIndex = i;
                    return true;
                }
            }
            return false;
        }

        internal static bool OpenSelectedPorterTableRow(
            SelectCharacterFloatingController controller,
            int rowIndex)
        {
            if (!IsPorterSelector(controller) || object.ReferenceEquals(OnSelectCharacterMethod, null) ||
                object.ReferenceEquals(EmployeesTableCharactersField, null) || controller.m_table == null)
            {
                return false;
            }

            EmployeesTable table = controller.m_table.GetComponent<EmployeesTable>();
            List<EntityIDPointer<Entity>> rows =
                table == null
                    ? null
                    : EmployeesTableCharactersField.GetValue(table) as List<EntityIDPointer<Entity>>;
            if (rows == null || rowIndex < 0 || rowIndex >= rows.Count)
            {
                UISoundManager.sm_instance?.PlaySoundEvent("SFX_UI_FORBIDDEN");
                return true;
            }

            Entity selected = rows[rowIndex].GetEntity();
            Department department = GetDepartment(controller);
            if (!IsSelectablePorter(selected) ||
                !TryGetNurseIndex(department, selected, out int nurseIndex))
            {
                UISoundManager.sm_instance?.PlaySoundEvent("SFX_UI_FORBIDDEN");
                return true;
            }

            try
            {
                OnSelectCharacterMethod.Invoke(controller, new object[] { nurseIndex });
            }
            catch (TargetInvocationException exception)
            {
                Plugin.Log?.LogError(
                    "Native Porter character selection failed: " +
                    (exception.InnerException ?? exception));
                UISoundManager.sm_instance?.PlaySoundEvent("SFX_UI_FORBIDDEN");
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogError("Native Porter character selection failed: " + exception);
                UISoundManager.sm_instance?.PlaySoundEvent("SFX_UI_FORBIDDEN");
            }
            return true;
        }
    }


    [HarmonyPatch(typeof(SelectCharacterFloatingController), "OnButtonCharacterListClicked")]
    internal static class PorterNativeSelectionOpenEmployeeListPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(SelectCharacterFloatingController __instance)
        {
            if (!PorterSelectionUi.IsPorterSelector(__instance) || __instance.m_table == null)
            {
                return;
            }

            EmployeesTable table = __instance.m_table.GetComponent<EmployeesTable>();
            if (table != null)
            {
                // Replace only the visible/list filter after the native LAB_SPECIALIST route opens the table.
                // Porters remain stored in Department.m_nurses.
                PorterNativeEmployeeUi.SelectPorters(table);
            }
        }
    }

    [HarmonyPatch(
        typeof(SelectCharacterFloatingController),
        nameof(SelectCharacterFloatingController.CreateButtons))]
    internal static class PorterNativeSelectionButtonsPatch
    {
        private static void Prefix(
            SelectCharacterFloatingController __instance,
            ref DisplayedCharacterType __state)
        {
            __state = __instance.m_characterType;
            if (PorterSelectionUi.IsPorterSelector(__instance) &&
                __instance.m_characterType == DisplayedCharacterType.LAB_SPECIALIST)
            {
                // Porters remain stored in Department.m_nurses. Reuse the native Nurse branch only
                // while it builds portrait buttons; the Porter station RequiredSkill filters out
                // ordinary nurses without any custom button-generation code.
                __instance.m_characterType = DisplayedCharacterType.NURSE;
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(SelectCharacterFloatingController __instance)
        {
            if (!PorterSelectionUi.IsPorterSelector(__instance) ||
                object.ReferenceEquals(PorterSelectionUi.SelectCharacterButtonsField, null) ||
                object.ReferenceEquals(PorterSelectionUi.SelectCharacterPointersField, null))
            {
                return;
            }

            List<GameObject> buttons =
                PorterSelectionUi.SelectCharacterButtonsField.GetValue(__instance) as List<GameObject>;
            List<EntityIDPointer<Entity>> pointers =
                PorterSelectionUi.SelectCharacterPointersField.GetValue(__instance) as List<EntityIDPointer<Entity>>;
            if (buttons == null || pointers == null || buttons.Count != pointers.Count)
            {
                return;
            }

            for (int i = pointers.Count - 1; i >= 0; i--)
            {
                Entity entity = pointers[i] == null ? null : pointers[i].GetEntity();
                if (PorterSelectionUi.IsSelectablePorter(entity))
                {
                    continue;
                }

                if (buttons[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(buttons[i]);
                }
                buttons.RemoveAt(i);
                pointers.RemoveAt(i);
            }

            int hiringOffset = __instance.m_openHiringPanelButton ? 64 : 0;
            for (int i = 0; i < buttons.Count; i++)
            {
                RectTransform rect = buttons[i] == null
                    ? null
                    : buttons[i].GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchoredPosition =
                        new Vector2(16 + hiringOffset + 64 * i, -22f);
                }
            }

            int itemWidth = 64;
            int itemCount = buttons.Count;
            if (__instance.m_openHiringPanelButton)
            {
                if (itemCount == 0)
                {
                    itemWidth = 48;
                }
                itemCount++;
            }

            RectTransform panelRect = __instance.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                panelRect.sizeDelta = __instance.m_table == null
                    ? new Vector2(30 + itemCount * itemWidth, 120f)
                    : new Vector2((itemCount + 1) * itemWidth, 120f);
            }
        }

        private static Exception Finalizer(
            SelectCharacterFloatingController __instance,
            DisplayedCharacterType __state,
            Exception __exception)
        {
            __instance.m_characterType = __state;
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(SelectCharacterFloatingController),
        nameof(SelectCharacterFloatingController.OnRowSelected),
        new Type[] { typeof(int) })]
    internal static class PorterNativeSelectionTableRowPatch
    {
        private static bool Prefix(
            SelectCharacterFloatingController __instance,
            int index)
        {
            if (!PorterSelectionUi.IsPorterSelector(__instance) ||
                __instance.m_characterType != DisplayedCharacterType.LAB_SPECIALIST)
            {
                return true;
            }

            // EmployeesTable sorts m_characters before rendering. Resolve the actual entity from
            // that sorted list, then translate it to the real Department.m_nurses index expected by
            // the workspace delegate. This keeps row selection correct under every native sort.
            PorterSelectionUi.OpenSelectedPorterTableRow(__instance, index);
            return false;
        }
    }
}
