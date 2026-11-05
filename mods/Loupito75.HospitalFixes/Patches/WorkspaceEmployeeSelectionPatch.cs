using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using GLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
    internal sealed class WorkspaceEmployeeTableViewState
    {
        internal DepartmentPersistentData Data;
        internal List<EntityIDPointer<Entity>> Doctors;
        internal List<EntityIDPointer<Entity>> Nurses;
        internal List<EntityIDPointer<Entity>> LabSpecialists;
        internal List<EntityIDPointer<Entity>> CivilianEmployees;
        internal List<EntityIDPointer<Entity>> InstalledDoctors;
        internal List<EntityIDPointer<Entity>> InstalledNurses;
        internal List<EntityIDPointer<Entity>> InstalledLabSpecialists;
        internal List<EntityIDPointer<Entity>> InstalledCivilianEmployees;
        internal bool Restored;

        internal void Restore()
        {
            if (Restored || Data == null)
            {
                return;
            }

            // Restore only references that still belong to this HFX view.
            // If another patch replaced one after HFX installed its temporary
            // list, leave that newer reference untouched.
            if (object.ReferenceEquals(
                    Data.m_doctors,
                    InstalledDoctors))
            {
                Data.m_doctors = Doctors;
            }
            if (object.ReferenceEquals(
                    Data.m_nurses,
                    InstalledNurses))
            {
                Data.m_nurses = Nurses;
            }
            if (object.ReferenceEquals(
                    Data.m_labSpecialists,
                    InstalledLabSpecialists))
            {
                Data.m_labSpecialists = LabSpecialists;
            }
            if (object.ReferenceEquals(
                    Data.m_civilianEmployees,
                    InstalledCivilianEmployees))
            {
                Data.m_civilianEmployees = CivilianEmployees;
            }

            Restored = true;
        }
    }

    internal sealed class WorkspaceEmployeeFilterBuffers
    {
        internal readonly List<EntityIDPointer<Entity>> Doctors =
            new List<EntityIDPointer<Entity>>();
        internal readonly List<EntityIDPointer<Entity>> Nurses =
            new List<EntityIDPointer<Entity>>();
        internal readonly List<EntityIDPointer<Entity>> LabSpecialists =
            new List<EntityIDPointer<Entity>>();
        internal readonly List<EntityIDPointer<Entity>> CivilianEmployees =
            new List<EntityIDPointer<Entity>>();
    }

    internal sealed class WorkspaceEmployeeSelectionUiState
    {
        internal readonly List<GameObject> HiddenButtons =
            new List<GameObject>();
        internal readonly List<GameObject> HiddenBackgrounds =
            new List<GameObject>();
        internal Vector2 AllButtonPosition;
        internal Vector2 AllButtonDelta;
        internal Vector2 AllBackgroundPosition;
        internal GameObject TrailingSeparator;
        internal Vector2 TrailingSeparatorPosition;
        internal bool HasAllButtonPosition;
        internal bool HasAllButtonDelta;
        internal bool HasAllBackgroundPosition;
        internal bool HasTrailingSeparatorPosition;
    }

    internal static class WorkspaceEmployeeSelectionHelper
    {
        // Native Employees UI (AssetRipper main_scene_default):
        // AllButton 24 px, trailing Separator 7.6 px, ~1.2 px edge gap.
        private const float FilterSeparatorGap = 1.2f;

        private static readonly FieldInfo EmployeesField =
            AccessTools.Field(
                typeof(EmployeesTable),
                "m_characters");

        private static readonly Dictionary<
            EmployeesTable,
            WorkspaceEmployeeSelectionUiState> UiStates =
                new Dictionary<
                    EmployeesTable,
                    WorkspaceEmployeeSelectionUiState>();

        private static readonly Dictionary<
            EmployeesTable,
            WorkspaceEmployeeFilterBuffers> FilterBuffers =
                new Dictionary<
                    EmployeesTable,
                    WorkspaceEmployeeFilterBuffers>();

        internal static Entity GetDisplayedEmployee(
            EmployeesTable table,
            int index)
        {
            if (table == null ||
                EmployeesField == null ||
                index < 0)
            {
                return null;
            }

            List<EntityIDPointer<Entity>> employees =
                EmployeesField.GetValue(table)
                    as List<EntityIDPointer<Entity>>;
            if (employees == null ||
                index >= employees.Count)
            {
                return null;
            }

            EntityIDPointer<Entity> pointer =
                employees[index];
            return pointer == null
                ? null
                : pointer.GetEntity();
        }

        internal static GameDBSkill GetRequiredSkill(
            SelectCharacterFloatingController selector)
        {
            Room room =
                selector == null ||
                selector.m_room == null
                    ? null
                    : selector.m_room.GetEntity();

            return room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType.Entry == null ||
                room.m_roomPersistentData.m_roomType.Entry.RequiredSkill == null
                    ? null
                    : room.m_roomPersistentData
                        .m_roomType.Entry.RequiredSkill.Entry;
        }

        internal static bool HasRequiredSkill(
            SelectCharacterFloatingController selector,
            Entity employee)
        {
            if (employee == null)
            {
                return false;
            }

            GameDBSkill requiredSkill =
                GetRequiredSkill(selector);
            EmployeeComponent employeeComponent =
                employee.GetComponent<EmployeeComponent>();

            return employeeComponent != null &&
                (requiredSkill == null ||
                    employeeComponent.HasSkill(requiredSkill));
        }

        internal static WorkspaceEmployeeTableViewState
            PrepareFilteredView(EmployeesTable table)
        {
            if (!FixConfig.FixWorkspaceEmployeeSelection ||
                table == null ||
                Hospital.Instance == null)
            {
                return null;
            }

            TableController tableController =
                table.GetComponent<TableController>();
            SelectCharacterFloatingController selector =
                tableController == null
                    ? null
                    : tableController.GetTableSelectionListener()
                        as SelectCharacterFloatingController;
            GameDBSkill requiredSkill =
                GetRequiredSkill(selector);

            if (selector == null ||
                requiredSkill == null)
            {
                return null;
            }

            Department department =
                table.m_currentDepartment == null
                    ? Hospital.Instance.m_activeDepartment.GetEntity()
                    : table.m_currentDepartment;
            if (department == null ||
                department.m_departmentPersistentData == null)
            {
                return null;
            }

            DepartmentPersistentData data =
                department.m_departmentPersistentData;
            WorkspaceEmployeeTableViewState state =
                new WorkspaceEmployeeTableViewState
                {
                    Data = data,
                    Doctors = data.m_doctors,
                    Nurses = data.m_nurses,
                    LabSpecialists = data.m_labSpecialists,
                    CivilianEmployees = data.m_civilianEmployees
                };

            WorkspaceEmployeeFilterBuffers buffers;
            if (!FilterBuffers.TryGetValue(
                    table,
                    out buffers))
            {
                buffers =
                    new WorkspaceEmployeeFilterBuffers();
                FilterBuffers[table] = buffers;
            }

            // Replace list references only for the duration of this table
            // update. Another mod may already have narrowed one of these
            // lists before HFX runs; HFX filters that current view instead of
            // reconstructing profession ownership. Reuse the same temporary
            // buffers while the selector is open because EmployeesTable.Update
            // is also Unity's per-frame Update method.
            state.InstalledDoctors =
                FilterByRequiredSkill(
                    state.Doctors,
                    requiredSkill,
                    buffers.Doctors);
            state.InstalledNurses =
                FilterByRequiredSkill(
                    state.Nurses,
                    requiredSkill,
                    buffers.Nurses);
            state.InstalledLabSpecialists =
                FilterByRequiredSkill(
                    state.LabSpecialists,
                    requiredSkill,
                    buffers.LabSpecialists);
            state.InstalledCivilianEmployees =
                FilterByRequiredSkill(
                    state.CivilianEmployees,
                    requiredSkill,
                    buffers.CivilianEmployees);

            data.m_doctors =
                state.InstalledDoctors;
            data.m_nurses =
                state.InstalledNurses;
            data.m_labSpecialists =
                state.InstalledLabSpecialists;
            data.m_civilianEmployees =
                state.InstalledCivilianEmployees;

            return state;
        }

        private static List<EntityIDPointer<Entity>>
            FilterByRequiredSkill(
                List<EntityIDPointer<Entity>> source,
                GameDBSkill requiredSkill,
                List<EntityIDPointer<Entity>> filtered)
        {
            if (filtered == null)
            {
                return source;
            }

            // A nested/re-entrant table refresh can see the already-installed
            // HFX buffer as its source. It is already filtered for this view;
            // clearing it here would erase the active candidate set.
            if (object.ReferenceEquals(
                    source,
                    filtered))
            {
                return filtered;
            }

            filtered.Clear();

            if (source == null)
            {
                return filtered;
            }

            for (int i = 0; i < source.Count; i++)
            {
                EntityIDPointer<Entity> pointer = source[i];
                Entity employee =
                    pointer == null
                        ? null
                        : pointer.GetEntity();
                EmployeeComponent employeeComponent =
                    employee == null
                        ? null
                        : employee.GetComponent<EmployeeComponent>();

                if (employeeComponent != null &&
                    employeeComponent.HasSkill(requiredSkill))
                {
                    filtered.Add(pointer);
                }
            }

            return filtered;
        }

        internal static void ReleaseFilterBuffers(
            EmployeesTable table)
        {
            if (table != null)
            {
                FilterBuffers.Remove(table);
            }
        }

        internal static void EnterSelectionUi(
            SelectCharacterFloatingController selector)
        {
            if (!FixConfig.FixWorkspaceEmployeeSelection ||
                selector == null ||
                selector.m_table == null)
            {
                return;
            }

            EmployeesTable table =
                selector.m_table.GetComponent<EmployeesTable>();
            if (table == null ||
                table.m_buttonAllEmployees == null)
            {
                return;
            }

            RestoreSelectionUi(table);

            WorkspaceEmployeeSelectionUiState state =
                new WorkspaceEmployeeSelectionUiState();
            UiStates[table] = state;

            GameObject allButton =
                table.m_buttonAllEmployees;
            RectTransform allRect =
                allButton.GetComponent<RectTransform>();
            if (allRect != null)
            {
                state.AllButtonPosition =
                    allRect.anchoredPosition;
                state.HasAllButtonPosition = true;

                GameObject anchor =
                    table.m_buttonDoctors ??
                    table.m_buttonNurses ??
                    table.m_buttonLabSpecialists ??
                    table.m_buttonJanitors;
                RectTransform anchorRect =
                    anchor == null
                        ? null
                        : anchor.GetComponent<RectTransform>();
                if (anchorRect != null)
                {
                    Vector2 targetPosition =
                        anchorRect.anchoredPosition;
                    state.AllButtonDelta =
                        targetPosition -
                        state.AllButtonPosition;
                    state.HasAllButtonDelta = true;
                    allRect.anchoredPosition =
                        targetPosition;
                }
            }

            GameObject trailingSeparator =
                FindTrailingSeparator(allButton);
            RectTransform trailingSeparatorRect =
                trailingSeparator == null
                    ? null
                    : trailingSeparator.GetComponent<RectTransform>();
            if (trailingSeparatorRect != null)
            {
                state.TrailingSeparator = trailingSeparator;
                state.TrailingSeparatorPosition =
                    trailingSeparatorRect.anchoredPosition;
                state.HasTrailingSeparatorPosition = true;
                AlignTrailingSeparator(
                    allRect,
                    trailingSeparatorRect);
            }

            HideSiblingFilterButtons(
                allButton,
                state.HiddenButtons);

            allButton.SetActive(true);
            IconButtonController allController =
                allButton.GetComponentInChildren<
                    IconButtonController>(true);
            if (allController != null)
            {
                allController.RemoveOnClickDelegate();
                allController.SetOnClickedDelegate(
                    delegate
                    {
                        // The only visible button represents the already
                        // filtered set of candidates. Clicking it simply
                        // refreshes that same view.
                        table.Update();
                    });
            }

            if (table.m_buttonBackgrounds != null &&
                table.m_buttonBackgrounds.Length > 4 &&
                table.m_buttonBackgrounds[4] != null)
            {
                GameObject allBackground =
                    table.m_buttonBackgrounds[4];
                RectTransform allBackgroundRect =
                    allBackground.GetComponent<RectTransform>();
                if (allBackgroundRect != null)
                {
                    state.AllBackgroundPosition =
                        allBackgroundRect.anchoredPosition;
                    state.HasAllBackgroundPosition = true;

                    if (state.HasAllButtonDelta)
                    {
                        allBackgroundRect.anchoredPosition =
                            state.AllBackgroundPosition +
                            state.AllButtonDelta;
                    }
                }

                HideSiblingFilterBackgrounds(
                    allBackground,
                    state.HiddenBackgrounds);
                table.SetBackGroundForButton(4);
            }
        }

        private static GameObject FindTrailingSeparator(
            GameObject allButton)
        {
            RectTransform allRect =
                allButton == null
                    ? null
                    : allButton.GetComponent<RectTransform>();
            Transform parent =
                allButton == null
                    ? null
                    : allButton.transform.parent;
            if (allRect == null || parent == null)
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                GameObject candidate =
                    parent.GetChild(i).gameObject;
                if (candidate == null ||
                    !string.Equals(
                        candidate.name,
                        "Separator",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                RectTransform rect =
                    candidate.GetComponent<RectTransform>();
                if (rect == null ||
                    Math.Abs(
                        rect.anchoredPosition.y -
                        allRect.anchoredPosition.y) >= 1f ||
                    Math.Abs(rect.anchorMin.x - allRect.anchorMin.x) >= 0.01f ||
                    Math.Abs(rect.anchorMin.y - allRect.anchorMin.y) >= 0.01f ||
                    Math.Abs(rect.anchorMax.x - allRect.anchorMax.x) >= 0.01f ||
                    Math.Abs(rect.anchorMax.y - allRect.anchorMax.y) >= 0.01f)
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static void AlignTrailingSeparator(
            RectTransform allRect,
            RectTransform separatorRect)
        {
            if (allRect == null ||
                separatorRect == null)
            {
                return;
            }

            float allWidth =
                Math.Abs(allRect.sizeDelta.x);
            float separatorWidth =
                Math.Abs(separatorRect.sizeDelta.x);
            if (allWidth <= 0f ||
                separatorWidth <= 0f)
            {
                return;
            }

            Vector2 position =
                separatorRect.anchoredPosition;
            position.x =
                allRect.anchoredPosition.x +
                (allWidth + separatorWidth) * 0.5f +
                FilterSeparatorGap;
            separatorRect.anchoredPosition =
                position;
        }

        private static void HideSiblingFilterButtons(
            GameObject allButton,
            List<GameObject> hidden)
        {
            Transform parent =
                allButton == null
                    ? null
                    : allButton.transform.parent;
            RectTransform allRect =
                allButton == null
                    ? null
                    : allButton.GetComponent<RectTransform>();

            if (parent == null ||
                allRect == null)
            {
                return;
            }

            float y = allRect.anchoredPosition.y;

            for (int i = 0; i < parent.childCount; i++)
            {
                GameObject candidate =
                    parent.GetChild(i).gameObject;
                if (candidate == null ||
                    candidate == allButton ||
                    !candidate.activeSelf)
                {
                    continue;
                }

                RectTransform rect =
                    candidate.GetComponent<RectTransform>();
                IconButtonController icon =
                    candidate.GetComponentInChildren<
                        IconButtonController>(true);

                if (rect != null &&
                    icon != null &&
                    Math.Abs(rect.anchoredPosition.y - y) < 1f)
                {
                    if (!hidden.Contains(candidate))
                    {
                        hidden.Add(candidate);
                    }
                    candidate.SetActive(false);
                }
            }
        }

        private static void HideSiblingFilterBackgrounds(
            GameObject allBackground,
            List<GameObject> hidden)
        {
            Transform parent =
                allBackground == null
                    ? null
                    : allBackground.transform.parent;
            RectTransform allRect =
                allBackground == null
                    ? null
                    : allBackground.GetComponent<RectTransform>();

            if (parent == null ||
                allRect == null)
            {
                return;
            }

            float y = allRect.anchoredPosition.y;

            for (int i = 0; i < parent.childCount; i++)
            {
                GameObject candidate =
                    parent.GetChild(i).gameObject;
                if (candidate == null ||
                    candidate == allBackground ||
                    !candidate.activeSelf)
                {
                    continue;
                }

                RectTransform rect =
                    candidate.GetComponent<RectTransform>();
                string objectName =
                    candidate.name == null
                        ? string.Empty
                        : candidate.name.ToLowerInvariant();

                if (rect != null &&
                    objectName.IndexOf("background") >= 0 &&
                    Math.Abs(rect.anchoredPosition.y - y) < 1f)
                {
                    if (!hidden.Contains(candidate))
                    {
                        hidden.Add(candidate);
                    }
                    candidate.SetActive(false);
                }
            }
        }

        internal static void MaintainSelectionUi(
            EmployeesTable table)
        {
            if (table == null)
            {
                return;
            }

            WorkspaceEmployeeSelectionUiState state;
            if (!UiStates.TryGetValue(
                    table,
                    out state))
            {
                return;
            }

            if (table.m_buttonAllEmployees != null)
            {
                HideSiblingFilterButtons(
                    table.m_buttonAllEmployees,
                    state.HiddenButtons);
                table.m_buttonAllEmployees.SetActive(true);

                RectTransform allRect =
                    table.m_buttonAllEmployees
                        .GetComponent<RectTransform>();
                RectTransform separatorRect =
                    state.TrailingSeparator == null
                        ? null
                        : state.TrailingSeparator
                            .GetComponent<RectTransform>();
                AlignTrailingSeparator(
                    allRect,
                    separatorRect);
            }

            if (table.m_buttonBackgrounds != null &&
                table.m_buttonBackgrounds.Length > 4 &&
                table.m_buttonBackgrounds[4] != null)
            {
                HideSiblingFilterBackgrounds(
                    table.m_buttonBackgrounds[4],
                    state.HiddenBackgrounds);
                table.SetBackGroundForButton(4);
            }
        }

        internal static void RestoreSelectionUi(
            EmployeesTable table)
        {
            if (table == null)
            {
                return;
            }

            WorkspaceEmployeeSelectionUiState state;
            if (!UiStates.TryGetValue(
                    table,
                    out state))
            {
                return;
            }

            for (int i = 0;
                i < state.HiddenButtons.Count;
                i++)
            {
                if (state.HiddenButtons[i] != null)
                {
                    state.HiddenButtons[i].SetActive(true);
                }
            }

            for (int i = 0;
                i < state.HiddenBackgrounds.Count;
                i++)
            {
                if (state.HiddenBackgrounds[i] != null)
                {
                    state.HiddenBackgrounds[i].SetActive(true);
                }
            }

            if (table.m_buttonAllEmployees != null)
            {
                RectTransform allRect =
                    table.m_buttonAllEmployees
                        .GetComponent<RectTransform>();
                if (allRect != null &&
                    state.HasAllButtonPosition)
                {
                    allRect.anchoredPosition =
                        state.AllButtonPosition;
                }

                IconButtonController allController =
                    table.m_buttonAllEmployees
                        .GetComponentInChildren<
                            IconButtonController>(true);
                if (allController != null)
                {
                    allController.RemoveOnClickDelegate();
                    allController.SetOnClickedDelegate(
                        delegate
                        {
                            table.SwitchFilter(
                                DisplayedCharacterType.ALL_EMPLOYEES);
                        });
                }
            }

            if (table.m_buttonBackgrounds != null &&
                table.m_buttonBackgrounds.Length > 4 &&
                table.m_buttonBackgrounds[4] != null)
            {
                RectTransform backgroundRect =
                    table.m_buttonBackgrounds[4]
                        .GetComponent<RectTransform>();
                if (backgroundRect != null &&
                    state.HasAllBackgroundPosition)
                {
                    backgroundRect.anchoredPosition =
                        state.AllBackgroundPosition;
                }
            }

            if (state.TrailingSeparator != null &&
                state.HasTrailingSeparatorPosition)
            {
                RectTransform separatorRect =
                    state.TrailingSeparator
                        .GetComponent<RectTransform>();
                if (separatorRect != null)
                {
                    separatorRect.anchoredPosition =
                        state.TrailingSeparatorPosition;
                }
            }

            UiStates.Remove(table);
        }

        internal static bool TryGetVanillaSelectorIndex(
            SelectCharacterFloatingController selector,
            Entity employee,
            out int index)
        {
            index = -1;

            if (selector == null ||
                employee == null ||
                Hospital.Instance == null)
            {
                return false;
            }

            Department department =
                selector.m_department == null
                    ? Hospital.Instance.m_activeDepartment.GetEntity()
                    : selector.m_department.GetEntity();
            if (department == null ||
                department.m_departmentPersistentData == null)
            {
                return false;
            }

            string occupationId =
                EmployeeLevelVisualHelper
                    .GetOccupationId(employee);
            DisplayedCharacterType target =
                selector.m_characterType;

            switch (target)
            {
                case DisplayedCharacterType.DOCTOR:
                    if (occupationId != "OCCUPATION_DOCTOR" ||
                        employee.GetComponent<BehaviorDoctor>() == null)
                    {
                        return false;
                    }
                    index =
                        FindEntityIndex(
                            department.m_departmentPersistentData.m_doctors,
                            employee);
                    return index >= 0;

                case DisplayedCharacterType.DOCTOR_CLINIC:
                case DisplayedCharacterType.DOCTOR_HOSPITALIZATION:
                    if (occupationId != "OCCUPATION_DOCTOR" ||
                        employee.GetComponent<BehaviorDoctor>() == null)
                    {
                        return false;
                    }
                    return TryGetFilteredDoctorIndex(
                        department,
                        employee,
                        target ==
                            DisplayedCharacterType.DOCTOR_CLINIC,
                        out index);

                case DisplayedCharacterType.NURSE:
                    if (occupationId != "OCCUPATION_NURSE" ||
                        employee.GetComponent<BehaviorNurse>() == null)
                    {
                        return false;
                    }
                    index =
                        FindEntityIndex(
                            department.m_departmentPersistentData.m_nurses,
                            employee);
                    return index >= 0;

                case DisplayedCharacterType.LAB_SPECIALIST:
                    if (occupationId !=
                            "OCCUPATION_LAB_SPECIALIST" ||
                        employee.GetComponent<BehaviorLabSpecialist>() == null)
                    {
                        return false;
                    }
                    index =
                        FindEntityIndex(
                            department.m_departmentPersistentData
                                .m_labSpecialists,
                            employee);
                    return index >= 0;

                case DisplayedCharacterType.JANITOR:
                    if (occupationId != "OCCUPATION_JANITOR" ||
                        employee.GetComponent<BehaviorJanitor>() == null)
                    {
                        return false;
                    }
                    index =
                        FindEntityIndex(
                            department.m_departmentPersistentData
                                .m_civilianEmployees,
                            employee);
                    return index >= 0;

                case DisplayedCharacterType.DOCTORS_NURSES:
                    if (occupationId == "OCCUPATION_DOCTOR" &&
                        employee.GetComponent<BehaviorDoctor>() != null)
                    {
                        index =
                            FindEntityIndex(
                                department.m_departmentPersistentData
                                    .m_doctors,
                                employee);
                        return index >= 0;
                    }
                    if (occupationId == "OCCUPATION_NURSE" &&
                        employee.GetComponent<BehaviorNurse>() != null)
                    {
                        int nurseIndex =
                            FindEntityIndex(
                                department.m_departmentPersistentData
                                    .m_nurses,
                                employee);
                        if (nurseIndex >= 0)
                        {
                            index =
                                department.m_departmentPersistentData
                                    .m_doctors.Count +
                                nurseIndex;
                            return true;
                        }
                    }
                    return false;

                case DisplayedCharacterType.ALL_EMPLOYEES:
                    return TryGetAllEmployeesIndex(
                        department,
                        employee,
                        occupationId,
                        out index);

                default:
                    return false;
            }
        }

        private static bool TryGetFilteredDoctorIndex(
            Department department,
            Entity employee,
            bool clinic,
            out int index)
        {
            index = -1;
            int filteredIndex = 0;

            for (int i = 0;
                i < department.m_departmentPersistentData.m_doctors.Count;
                i++)
            {
                Entity current =
                    department.m_departmentPersistentData
                        .m_doctors[i].GetEntity();
                if (current == null)
                {
                    continue;
                }

                EmployeeComponent currentEmployee =
                    current.GetComponent<EmployeeComponent>();
                GameDBRoomType homeRoom =
                    currentEmployee == null
                        ? null
                        : currentEmployee.GetHomeRoomType();

                if (homeRoom == null ||
                    homeRoom.AcceptsOutpatients != clinic)
                {
                    continue;
                }

                if (object.ReferenceEquals(
                        current,
                        employee))
                {
                    index = filteredIndex;
                    return true;
                }

                filteredIndex++;
            }

            return false;
        }

        private static bool TryGetAllEmployeesIndex(
            Department department,
            Entity employee,
            string occupationId,
            out int index)
        {
            index = -1;
            int offset = 0;

            if (occupationId == "OCCUPATION_DOCTOR" &&
                employee.GetComponent<BehaviorDoctor>() != null)
            {
                index =
                    FindEntityIndex(
                        department.m_departmentPersistentData.m_doctors,
                        employee);
                return index >= 0;
            }
            offset +=
                department.m_departmentPersistentData.m_doctors.Count;

            if (occupationId == "OCCUPATION_NURSE" &&
                employee.GetComponent<BehaviorNurse>() != null)
            {
                int local =
                    FindEntityIndex(
                        department.m_departmentPersistentData.m_nurses,
                        employee);
                if (local >= 0)
                {
                    index = offset + local;
                    return true;
                }
            }
            offset +=
                department.m_departmentPersistentData.m_nurses.Count;

            if (occupationId == "OCCUPATION_LAB_SPECIALIST" &&
                employee.GetComponent<BehaviorLabSpecialist>() != null)
            {
                int local =
                    FindEntityIndex(
                        department.m_departmentPersistentData
                            .m_labSpecialists,
                        employee);
                if (local >= 0)
                {
                    index = offset + local;
                    return true;
                }
            }
            offset +=
                department.m_departmentPersistentData
                    .m_labSpecialists.Count;

            if (occupationId == "OCCUPATION_JANITOR" &&
                employee.GetComponent<BehaviorJanitor>() != null)
            {
                int local =
                    FindEntityIndex(
                        department.m_departmentPersistentData
                            .m_civilianEmployees,
                        employee);
                if (local >= 0)
                {
                    index = offset + local;
                    return true;
                }
            }

            // Custom occupations are deliberately left to their owning mod.
            return false;
        }

        private static int FindEntityIndex(
            List<EntityIDPointer<Entity>> list,
            Entity employee)
        {
            if (list == null ||
                employee == null)
            {
                return -1;
            }

            for (int i = 0; i < list.Count; i++)
            {
                Entity current =
                    list[i] == null
                        ? null
                        : list[i].GetEntity();
                if (object.ReferenceEquals(
                        current,
                        employee))
                {
                    return i;
                }
            }

            return -1;
        }

        internal static void PlayForbidden()
        {
            if (UISoundManager.sm_instance != null)
            {
                UISoundManager.sm_instance
                    .PlaySoundEvent(
                        "SFX_UI_FORBIDDEN");
            }
        }
    }

    [HarmonyPatch(
        typeof(SelectCharacterFloatingController),
        "OnButtonCharacterListClicked")]
    [HarmonyAfter("Loupito75:HospitalPorters")]
    internal static class WorkspaceEmployeeSelectionOpenPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            SelectCharacterFloatingController __instance)
        {
            WorkspaceEmployeeSelectionHelper
                .EnterSelectionUi(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            SelectCharacterFloatingController __instance)
        {
            if (__instance == null ||
                __instance.m_table == null)
            {
                return;
            }

            EmployeesTable table =
                __instance.m_table.GetComponent<EmployeesTable>();
            WorkspaceEmployeeSelectionHelper
                .MaintainSelectionUi(table);
        }
    }

    [HarmonyPatch(
        typeof(EmployeesTable),
        nameof(EmployeesTable.SetStandlone))]
    internal static class WorkspaceEmployeeSelectionRestorePatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            EmployeesTable __instance)
        {
            WorkspaceEmployeeSelectionHelper
                .RestoreSelectionUi(__instance);
            WorkspaceEmployeeSelectionHelper
                .ReleaseFilterBuffers(__instance);
        }
    }

    [HarmonyPatch(
        typeof(EmployeesTable),
        nameof(EmployeesTable.Update))]
    [HarmonyAfter("Loupito75:HospitalPorters")]
    internal static class WorkspaceEmployeeSelectionTableViewPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            EmployeesTable __instance,
            ref WorkspaceEmployeeTableViewState __state)
        {
            __state =
                WorkspaceEmployeeSelectionHelper
                    .PrepareFilteredView(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            EmployeesTable __instance,
            WorkspaceEmployeeTableViewState __state)
        {
            if (__state != null)
            {
                __state.Restore();
            }

            WorkspaceEmployeeSelectionHelper
                .MaintainSelectionUi(__instance);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(
            Exception __exception,
            WorkspaceEmployeeTableViewState __state)
        {
            if (__state != null)
            {
                __state.Restore();
            }

            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(SelectCharacterFloatingController),
        nameof(SelectCharacterFloatingController.OnRowSelected))]
    [HarmonyAfter("Loupito75:HospitalPorters")]
    internal static class WorkspaceEmployeeRowSelectionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(
            SelectCharacterFloatingController __instance,
            ref int index)
        {
            if (!FixConfig.FixWorkspaceEmployeeSelection ||
                __instance == null ||
                __instance.m_table == null)
            {
                return true;
            }

            TableController tableController =
                __instance.m_table.GetComponent<TableController>();
            EmployeesTable employeesTable =
                __instance.m_table.GetComponent<EmployeesTable>();

            if (tableController == null ||
                employeesTable == null ||
                !object.ReferenceEquals(
                    tableController.GetTableSelectionListener(),
                    __instance))
            {
                return true;
            }

            Entity displayed =
                WorkspaceEmployeeSelectionHelper
                    .GetDisplayedEmployee(
                        employeesTable,
                        index);
            if (displayed == null)
            {
                WorkspaceEmployeeSelectionHelper
                    .PlayForbidden();
                return false;
            }

            int mappedIndex;
            if (!WorkspaceEmployeeSelectionHelper
                    .TryGetVanillaSelectorIndex(
                        __instance,
                        displayed,
                        out mappedIndex))
            {
                // A custom occupation/selector may deliberately reuse a
                // native DisplayedCharacterType. Leave it to the owner mod.
                return true;
            }

            if (!WorkspaceEmployeeSelectionHelper
                    .HasRequiredSkill(
                        __instance,
                        displayed))
            {
                WorkspaceEmployeeSelectionHelper
                    .PlayForbidden();
                return false;
            }

            index = mappedIndex;
            return true;
        }
    }
}
