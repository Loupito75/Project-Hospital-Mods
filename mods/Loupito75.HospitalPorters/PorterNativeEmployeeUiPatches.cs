using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalPorters
{
    internal sealed class PorterEmployeeTableViewState
    {
        internal DepartmentPersistentData Data;
        internal List<EntityIDPointer<Entity>> OriginalNurses;
        internal List<EntityIDPointer<Entity>> OriginalLabSpecialists;
        internal bool NursesChanged;
        internal bool LabSpecialistsChanged;
    }

    internal sealed class PorterEmployeeFilterState
    {
        internal GameObject Button;
        internal GameObject Background;
        internal bool Active;
    }

    internal static class PorterNativeEmployeeUi
    {
        private const float DefaultButtonStep = 30f;
        private const string PorterButtonName = "HospitalPorters_EmployeesPorterFilter";
        private const string PorterBackgroundName = "HospitalPorters_EmployeesPorterFilterBackground";

        private static readonly FieldInfo AnimModelEntityField =
            AccessTools.Field(typeof(AnimModelComponent), "m_entity");
        private static readonly FieldInfo EmployeesTableCharacterTypeField =
            AccessTools.Field(typeof(EmployeesTable), "m_characterType");
        private static readonly Dictionary<EmployeesTable, PorterEmployeeFilterState> FilterStates =
            new Dictionary<EmployeesTable, PorterEmployeeFilterState>();

        [ThreadStatic]
        private static bool s_selectingPorters;

        internal static Entity GetAnimModelOwner(AnimModelComponent component)
        {
            if (component == null || object.ReferenceEquals(AnimModelEntityField, null))
            {
                return null;
            }

            return AnimModelEntityField.GetValue(component) as Entity;
        }

        internal static void EnsurePorterFilter(EmployeesTable table)
        {
            if (table == null || FilterStates.ContainsKey(table) ||
                table.m_buttonJanitors == null || table.m_buttonAllEmployees == null ||
                table.m_buttonBackgrounds == null || table.m_buttonBackgrounds.Length < 5)
            {
                return;
            }

            GameObject sourceButton = table.m_buttonJanitors;
            GameObject button = UnityEngine.Object.Instantiate(sourceButton);
            button.name = PorterButtonName;
            button.transform.SetParent(sourceButton.transform.parent, false);
            button.transform.localScale = sourceButton.transform.localScale;

            IconButtonController iconButton =
                button.GetComponentInChildren<IconButtonController>(includeInactive: true);
            if (iconButton == null)
            {
                UnityEngine.Object.Destroy(button);
                Plugin.Log?.LogError("Porter Employees filter could not resolve IconButtonController.");
                return;
            }

            iconButton.RemoveOnClickDelegate();
            PorterVisuals.ApplyEmployeesIcons(iconButton);
            iconButton.SetTextLocID(LocalizationManager.Get(PorterIds.PorterCandidates), directText: true);
            iconButton.SetOnClickedDelegate(delegate
            {
                SelectPorters(table);
            });

            GameObject sourceBackground = table.m_buttonBackgrounds[3];
            GameObject background = sourceBackground == null
                ? null
                : UnityEngine.Object.Instantiate(sourceBackground);
            if (background != null)
            {
                background.name = PorterBackgroundName;
                background.transform.SetParent(sourceBackground.transform.parent, false);
                background.transform.localScale = sourceBackground.transform.localScale;
                Image backgroundImage = background.GetComponent<Image>();
                if (backgroundImage != null)
                {
                    backgroundImage.raycastTarget = false;
                }
                background.SetActive(false);
            }

            RectTransform janitorRect = sourceButton.GetComponent<RectTransform>();
            RectTransform allRect = table.m_buttonAllEmployees.GetComponent<RectTransform>();
            RectTransform porterRect = button.GetComponent<RectTransform>();
            float step = DefaultButtonStep;
            Vector2 originalAllPosition = allRect == null ? Vector2.zero : allRect.anchoredPosition;

            if (janitorRect != null && allRect != null)
            {
                float detectedStep = allRect.anchoredPosition.x - janitorRect.anchoredPosition.x;
                if (Math.Abs(detectedStep) > 0.01f)
                {
                    step = detectedStep;
                }

                porterRect.anchoredPosition = originalAllPosition;
                allRect.anchoredPosition = originalAllPosition + new Vector2(step, 0f);
            }
            else if (porterRect != null && janitorRect != null)
            {
                porterRect.anchoredPosition =
                    janitorRect.anchoredPosition + new Vector2(step, 0f);
            }

            if (background != null)
            {
                RectTransform backgroundRect = background.GetComponent<RectTransform>();
                RectTransform porterButtonRect = button.GetComponent<RectTransform>();
                RectTransform sourceBackgroundRect = sourceBackground.GetComponent<RectTransform>();
                if (backgroundRect != null && porterButtonRect != null &&
                    sourceBackgroundRect != null && janitorRect != null)
                {
                    Vector2 offset =
                        sourceBackgroundRect.anchoredPosition - janitorRect.anchoredPosition;
                    backgroundRect.anchoredPosition = porterButtonRect.anchoredPosition + offset;
                }
            }

            if (table.m_buttonBackgrounds[4] != null && allRect != null)
            {
                RectTransform allBackgroundRect =
                    table.m_buttonBackgrounds[4].GetComponent<RectTransform>();
                if (allBackgroundRect != null)
                {
                    allBackgroundRect.anchoredPosition += new Vector2(step, 0f);
                }
            }

            FilterStates.Add(
                table,
                new PorterEmployeeFilterState
                {
                    Button = button,
                    Background = background,
                    Active = false
                });

            PorterDiagnostics.Log(
                "Porter Employees filter created next to Janitors with dedicated Porter artwork.");
        }

        internal static void RefreshPorterFilterIcon(EmployeesTable table)
        {
            if (table == null ||
                !FilterStates.TryGetValue(table, out PorterEmployeeFilterState state) ||
                state.Button == null)
            {
                return;
            }

            IconButtonController iconButton =
                state.Button.GetComponentInChildren<IconButtonController>(includeInactive: true);
            PorterVisuals.ApplyEmployeesIcons(iconButton);
        }

        internal static void SyncPorterFilterVisualState(EmployeesTable table)
        {
            if (table == null ||
                !FilterStates.TryGetValue(table, out PorterEmployeeFilterState state) ||
                !state.Active)
            {
                return;
            }

            if (table.m_buttonBackgrounds != null)
            {
                foreach (GameObject nativeBackground in table.m_buttonBackgrounds)
                {
                    nativeBackground?.SetActive(false);
                }
            }

            if (state.Background != null)
            {
                state.Background.SetActive(true);
            }
        }

        internal static bool IsPorterFilterActive(EmployeesTable table)
        {
            return table != null &&
                FilterStates.TryGetValue(table, out PorterEmployeeFilterState state) &&
                state.Active;
        }

        internal static void DeactivatePorterFilter(EmployeesTable table)
        {
            if (table == null ||
                !FilterStates.TryGetValue(table, out PorterEmployeeFilterState state))
            {
                return;
            }

            state.Active = false;
            if (state.Background != null)
            {
                state.Background.SetActive(false);
            }
        }

        internal static void SelectPorters(EmployeesTable table)
        {
            if (table == null ||
                !FilterStates.TryGetValue(table, out PorterEmployeeFilterState state))
            {
                return;
            }

            state.Active = true;
            s_selectingPorters = true;
            try
            {
                // Reuse the complete native Nurse table path. During Update() only,
                // m_nurses is narrowed to real Porters, then restored immediately.
                table.SwitchFilter(DisplayedCharacterType.NURSE);
            }
            finally
            {
                s_selectingPorters = false;
            }

            if (table.m_buttonBackgrounds != null)
            {
                foreach (GameObject nativeBackground in table.m_buttonBackgrounds)
                {
                    nativeBackground?.SetActive(false);
                }
            }

            if (state.Background != null)
            {
                state.Background.SetActive(true);
            }

            Text heading = table.gameObject.GetComponentInChildren<Text>();
            if (heading != null)
            {
                heading.text = LocalizationManager.Get(PorterIds.EmployeesHeading);
            }
        }

        internal static void OnNativeFilterSelected(EmployeesTable table)
        {
            if (!s_selectingPorters)
            {
                DeactivatePorterFilter(table);
            }
        }

        internal static PorterEmployeeTableViewState PrepareEmployeeTableView(EmployeesTable table)
        {
            if (table == null || object.ReferenceEquals(EmployeesTableCharacterTypeField, null) ||
                Hospital.Instance == null)
            {
                return null;
            }

            Department department = table.m_currentDepartment == null
                ? Hospital.Instance.m_activeDepartment.GetEntity()
                : table.m_currentDepartment;
            if (department == null || department.m_departmentPersistentData == null)
            {
                return null;
            }

            DisplayedCharacterType filter =
                (DisplayedCharacterType)EmployeesTableCharacterTypeField.GetValue(table);
            bool porterFilter = IsPorterFilterActive(table);
            bool hidePortersFromNativeNurseView =
                !porterFilter &&
                (filter == DisplayedCharacterType.NURSE ||
                 filter == DisplayedCharacterType.DOCTORS_NURSES);

            TableController tableController = table.GetComponent<TableController>();
            SelectCharacterFloatingController selector = tableController == null
                ? null
                : tableController.GetTableSelectionListener() as SelectCharacterFloatingController;
            bool porterSelector = PorterSelectionUi.IsPorterSelector(selector);

            if (!porterFilter && !hidePortersFromNativeNurseView && !porterSelector)
            {
                return null;
            }

            DepartmentPersistentData data = department.m_departmentPersistentData;
            List<EntityIDPointer<Entity>> porterPointers = new List<EntityIDPointer<Entity>>();
            List<EntityIDPointer<Entity>> selectablePorterPointers =
                new List<EntityIDPointer<Entity>>();
            List<EntityIDPointer<Entity>> nursesWithoutPorters = new List<EntityIDPointer<Entity>>();

            foreach (EntityIDPointer<Entity> pointer in data.m_nurses)
            {
                Entity entity = pointer == null ? null : pointer.GetEntity();
                if (PorterIdentity.IsPorter(entity))
                {
                    porterPointers.Add(pointer);
                    if (PorterSelectionUi.IsSelectablePorter(entity))
                    {
                        selectablePorterPointers.Add(pointer);
                    }
                }
                else
                {
                    nursesWithoutPorters.Add(pointer);
                }
            }

            PorterEmployeeTableViewState state = new PorterEmployeeTableViewState
            {
                Data = data,
                OriginalNurses = data.m_nurses,
                OriginalLabSpecialists = data.m_labSpecialists
            };

            if (porterFilter)
            {
                data.m_nurses = porterSelector
                    ? selectablePorterPointers
                    : porterPointers;
                state.NursesChanged = true;
            }
            else if (porterSelector)
            {
                // The Porter locker selector still opens its native table through the historical
                // LAB_SPECIALIST route. Limit this scoped type swap to that selector only; the
                // normal Technologists Employees filter never receives Porters.
                data.m_labSpecialists = selectablePorterPointers;
                state.LabSpecialistsChanged = true;
            }
            else if (hidePortersFromNativeNurseView)
            {
                data.m_nurses = nursesWithoutPorters;
                state.NursesChanged = true;
            }

            return state;
        }

        internal static void RestoreEmployeeTableView(PorterEmployeeTableViewState state)
        {
            if (state == null || state.Data == null)
            {
                return;
            }

            if (state.NursesChanged)
            {
                state.Data.m_nurses = state.OriginalNurses;
            }

            if (state.LabSpecialistsChanged)
            {
                state.Data.m_labSpecialists = state.OriginalLabSpecialists;
            }
        }
    }

    [HarmonyPatch(typeof(EmployeesTable), "Awake")]
    internal static class PorterEmployeeFilterCreationPatch
    {
        private static void Postfix(EmployeesTable __instance)
        {
            PorterNativeEmployeeUi.EnsurePorterFilter(__instance);
        }
    }

    [HarmonyPatch(
        typeof(EmployeesTable),
        nameof(EmployeesTable.SwitchFilter),
        new Type[] { typeof(DisplayedCharacterType) })]
    internal static class PorterEmployeeNativeFilterPatch
    {
        private static void Prefix(EmployeesTable __instance)
        {
            PorterNativeEmployeeUi.OnNativeFilterSelected(__instance);
        }
    }

    [HarmonyPatch(typeof(EmployeesTable), nameof(EmployeesTable.Update))]
    internal static class PorterNativeEmployeeTableViewPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            EmployeesTable __instance,
            ref PorterEmployeeTableViewState __state)
        {
            __state = PorterNativeEmployeeUi.PrepareEmployeeTableView(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(EmployeesTable __instance)
        {
            PorterNativeEmployeeUi.SyncPorterFilterVisualState(__instance);

            if (!PorterNativeEmployeeUi.IsPorterFilterActive(__instance))
            {
                return;
            }

            Text heading = __instance.gameObject.GetComponentInChildren<Text>();
            if (heading != null)
            {
                heading.text = LocalizationManager.Get(PorterIds.EmployeesHeading);
            }
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(
            Exception __exception,
            PorterEmployeeTableViewState __state)
        {
            PorterNativeEmployeeUi.RestoreEmployeeTableView(__state);
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(EmployeesTable),
        "GetJobTitle",
        new Type[] { typeof(Entity) })]
    internal static class PorterNativeEmployeeJobTitlePatch
    {
        private static void Postfix(Entity employee, ref string __result)
        {
            if (!PorterIdentity.IsPorter(employee))
            {
                return;
            }

            EmployeeComponent employeeComponent =
                employee.GetComponent<EmployeeComponent>();
            if (employeeComponent != null)
            {
                __result = LocalizationManager.Get(
                    PorterIds.GetPorterLevelLocalizationId(
                        employeeComponent.m_state.m_level));
            }
        }
    }

    internal sealed class PorterBiohazardColorState
    {
        internal bool Active;
        internal Vector3 Coat;
        internal Vector3 Top;
        internal Vector3 Pants;
    }

    [HarmonyPatch(
        typeof(AnimModelComponent),
        nameof(AnimModelComponent.ForceClothingStyle),
        new Type[]
        {
            typeof(string[]),
            typeof(GameDBColor),
            typeof(DressLevel)
        })]
    internal static class PorterBiohazardClothesColorPatch
    {
        private static void Prefix(
            AnimModelComponent __instance,
            string[] compatibleStyleIDs,
            ref PorterBiohazardColorState __state)
        {
            __state = null;

            Entity entity =
                PorterNativeEmployeeUi.GetAnimModelOwner(__instance);
            if (!PorterIdentity.IsPorter(entity) ||
                __instance == null ||
                __instance.m_state == null ||
                __instance.m_state.m_clothes == null ||
                !IsNativeBiohazardStyle(compatibleStyleIDs))
            {
                return;
            }

            __state = new PorterBiohazardColorState
            {
                Active = true,
                Coat = __instance.m_state.m_clothes.m_colorClothesCoat,
                Top = __instance.m_state.m_clothes.m_colorClothesTop,
                Pants = __instance.m_state.m_clothes.m_colorClothesPants
            };
        }

        private static void Postfix(
            AnimModelComponent __instance,
            PorterBiohazardColorState __state)
        {
            if (__state == null ||
                !__state.Active ||
                __instance == null ||
                __instance.m_state == null ||
                __instance.m_state.m_clothes == null)
            {
                return;
            }

            __instance.m_state.m_clothes.m_colorClothesCoat =
                __state.Coat;
            __instance.m_state.m_clothes.m_colorClothesTop =
                __state.Top;
            __instance.m_state.m_clothes.m_colorClothesPants =
                __state.Pants;
            __instance.m_colorsDirty = true;
        }

        private static bool IsNativeBiohazardStyle(
            string[] compatibleStyleIDs)
        {
            if (compatibleStyleIDs == null)
            {
                return false;
            }

            for (int i = 0; i < compatibleStyleIDs.Length; i++)
            {
                string id = compatibleStyleIDs[i];
                if (id == "CLTHSTL_FEMALE_QUARANTINE_NURSE_LAB" ||
                    id == "CLTHSTL_MALE_QUARANTINE_NURSE_LAB")
                {
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(
        typeof(AnimModelComponent),
        nameof(AnimModelComponent.RevertToDefaultClothes),
        new Type[] { typeof(bool) })]
    internal static class PorterNativeDefaultClothesPatch
    {
        private static void Postfix(AnimModelComponent __instance)
        {
            Entity entity = PorterNativeEmployeeUi.GetAnimModelOwner(__instance);
            if (!PorterIdentity.IsPorter(entity) || __instance == null || __instance.m_state == null)
            {
                return;
            }

            if (WorldEventManager.Instance != null &&
                WorldEventManager.Instance.HasEventForcingBiohazardClothes())
            {
                return;
            }

            CharacterPersonalInfoComponent personalInfo =
                entity.GetComponent<CharacterPersonalInfoComponent>();
            GameDBClothingStyle clothingStyle =
                personalInfo?.m_personalInfo?.m_clothingStyle.Entry;
            if (clothingStyle == null)
            {
                return;
            }

            __instance.ForceClothingStyle(
                new string[] { clothingStyle.DatabaseID.ToString() },
                null,
                DressLevel.FULL);
        }
    }
}