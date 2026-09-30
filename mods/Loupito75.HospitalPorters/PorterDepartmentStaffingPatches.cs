using System;
using System.Collections.Generic;
using HarmonyLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalPorters
{
    internal sealed class PorterStaffRowSnapshot
    {
        internal GameObject Row;
        internal bool FallbackClone;
        internal bool ActiveSelf;
        internal int SiblingIndex;
        internal Vector2 AnchoredPosition;
        internal Vector3 LocalScale;
        internal string Label;
        internal string Value;
        internal Sprite Icon;
        internal Color ValueColor;
    }

    internal sealed class PorterStaffPositionSnapshot
    {
        internal GameObject Row;
        internal Vector2 AnchoredPosition;
    }

    internal sealed class PorterStaffPanelState
    {
        internal PorterStaffRowSnapshot DayBorrowed;
        internal PorterStaffRowSnapshot NightBorrowed;
        internal GameObject DayFallback;
        internal GameObject NightFallback;
        internal readonly List<PorterStaffPositionSnapshot> DayReflowed =
            new List<PorterStaffPositionSnapshot>();
        internal readonly List<PorterStaffPositionSnapshot> NightReflowed =
            new List<PorterStaffPositionSnapshot>();
    }

    internal static class PorterDepartmentStaffingRows
    {
        private const string LegacyDayRowName = "HospitalPorters_PorterStaffDay";
        private const string LegacyNightRowName = "HospitalPorters_PorterStaffNight";
        private const string FallbackDayRowName = "HospitalPorters_PorterStaffFallbackDay";
        private const string FallbackNightRowName = "HospitalPorters_PorterStaffFallbackNight";

        private const float LeftColumn = 30f;
        private const float RightColumn = 150f;
        private const float Top = -15f;
        private const float Step = 25f;
        private const float PositionTolerance = 2f;
        private const int NativeRows = 4;

        private static readonly Dictionary<DepartmentManagementLogisticsHybridController, PorterStaffPanelState> States =
            new Dictionary<DepartmentManagementLogisticsHybridController, PorterStaffPanelState>();
        private static readonly Dictionary<GameObject, float> TooltipHoverTimes =
            new Dictionary<GameObject, float>();
        private static readonly Vector3[] TooltipWorldCorners = new Vector3[4];

        internal static void RestoreBeforeNativeUpdate(
            DepartmentManagementLogisticsHybridController controller)
        {
            PorterStaffPanelState state = GetState(controller);
            if (state == null)
            {
                return;
            }

            RestoreReflowed(state.DayReflowed);
            RestoreReflowed(state.NightReflowed);
            RestoreBorrowed(state.DayBorrowed);
            RestoreBorrowed(state.NightBorrowed);
            state.DayBorrowed = null;
            state.NightBorrowed = null;

            HideLegacyAddedRow(controller == null ? null : controller.m_staffHospitalizationPanel);
            HideLegacyAddedRow(controller == null ? null : controller.m_staffHospitalizationPanelNight);
        }

        internal static void ApplyAfterNativeUpdate(
            DepartmentManagementLogisticsHybridController controller)
        {
            if (controller == null || Hospital.Instance == null ||
                StreamingAssetManager.GetInstance() == null ||
                !StreamingAssetManager.GetInstance().IsReady())
            {
                return;
            }

            Department department = Hospital.Instance.GetActiveDepartment();
            if (department == null || department.GetDepartmentType() == null ||
                department.GetDepartmentType().NoHospitalization)
            {
                return;
            }

            PorterStaffPanelState state = GetState(controller);
            if (state == null)
            {
                return;
            }

            float dayExtra;
            float nightExtra;
            state.DayBorrowed = BorrowAndPresent(
                controller.m_staffHospitalizationPanel,
                department,
                Shift.DAY,
                ref state.DayFallback,
                state.DayReflowed,
                out dayExtra);
            state.NightBorrowed = BorrowAndPresent(
                controller.m_staffHospitalizationPanelNight,
                department,
                Shift.NIGHT,
                ref state.NightFallback,
                state.NightReflowed,
                out nightExtra);

            PorterHospitalizationLayout.OnPorterStaffingPresented(
                controller,
                dayExtra,
                nightExtra);
        }

        private static PorterStaffPanelState GetState(
            DepartmentManagementLogisticsHybridController controller)
        {
            if (controller == null)
            {
                return null;
            }

            PorterStaffPanelState state;
            if (!States.TryGetValue(controller, out state))
            {
                state = new PorterStaffPanelState();
                States.Add(controller, state);
            }
            return state;
        }

        private static PorterStaffRowSnapshot BorrowAndPresent(
            GameObject panel,
            Department department,
            Shift shift,
            ref GameObject fallback,
            List<PorterStaffPositionSnapshot> reflowed,
            out float extraHeight)
        {
            extraHeight = 0f;
            if (panel == null || !panel.activeInHierarchy)
            {
                return null;
            }

            HideLegacyAddedRow(panel);
            if (reflowed != null)
            {
                reflowed.Clear();
            }

            GameObject row = FindInactiveNativeRow(panel, fallback);
            bool fallbackClone = false;
            if (row == null)
            {
                row = GetOrCreateFallback(panel, shift, ref fallback);
                fallbackClone = true;
            }
            if (row == null)
            {
                Plugin.Log?.LogError(
                    "Porter staffing presentation could not borrow or clone a native hospitalization staff row.");
                return null;
            }

            PorterStaffRowSnapshot snapshot = Capture(row, fallbackClone);
            SegmentItemIconTextTextIconController segment =
                row.GetComponent<SegmentItemIconTextTextIconController>();
            RectTransform rect = row.GetComponent<RectTransform>();
            if (segment == null || rect == null)
            {
                if (!fallbackClone)
                {
                    RestoreBorrowed(snapshot);
                }
                return null;
            }

            List<GameObject> ordered = GetOrderedActiveNativeRows(panel, row, fallback);

            row.transform.localScale = Vector3.one;
            row.SetActive(true);
            row.transform.SetAsLastSibling();
            ordered.Add(row);

            int rows = Math.Max(NativeRows, (ordered.Count + 1) / 2);
            extraHeight = Math.Max(0, rows - NativeRows) * Step;
            ReflowRows(ordered, row, reflowed, rows);

            segment.UpdateData(
                LocalizationManager.Get(PorterIds.PorterCandidates),
                PorterStaffing.CountPorters(department, shift).ToString(),
                PorterVisuals.GetRightPanelSprite(),
                UISettings.Instance.EXAMINATION_COLOR_SUCCESSFUL);

            EnsureTooltip(row);
            ShowTooltipIfNeeded(row);

            return snapshot;
        }

        private static List<GameObject> GetOrderedActiveNativeRows(
            GameObject panel,
            GameObject porterRow,
            GameObject fallback)
        {
            List<GameObject> rows = new List<GameObject>();
            for (int i = 0; i < panel.transform.childCount; i++)
            {
                GameObject child = panel.transform.GetChild(i).gameObject;
                if (child == null || child == porterRow || child == fallback ||
                    !child.activeSelf || IsModRow(child) ||
                    child.GetComponent<SegmentItemIconTextTextIconController>() == null)
                {
                    continue;
                }
                rows.Add(child);
            }

            rows.Sort(delegate(GameObject left, GameObject right)
            {
                RectTransform leftRect = left.GetComponent<RectTransform>();
                RectTransform rightRect = right.GetComponent<RectTransform>();
                if (leftRect == null || rightRect == null)
                {
                    return 0;
                }

                bool leftColumn = Mathf.Abs(leftRect.anchoredPosition.x - LeftColumn) <=
                    PositionTolerance;
                bool rightColumn = Mathf.Abs(rightRect.anchoredPosition.x - LeftColumn) <=
                    PositionTolerance;
                if (leftColumn != rightColumn)
                {
                    return leftColumn ? -1 : 1;
                }

                if (Mathf.Abs(leftRect.anchoredPosition.y - rightRect.anchoredPosition.y) >
                    PositionTolerance)
                {
                    return leftRect.anchoredPosition.y > rightRect.anchoredPosition.y ? -1 : 1;
                }
                return left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex());
            });
            return rows;
        }

        private static void ReflowRows(
            List<GameObject> ordered,
            GameObject porterRow,
            List<PorterStaffPositionSnapshot> reflowed,
            int rows)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                GameObject item = ordered[i];
                RectTransform rect = item == null ? null : item.GetComponent<RectTransform>();
                if (rect == null)
                {
                    continue;
                }

                if (item != porterRow && reflowed != null)
                {
                    reflowed.Add(new PorterStaffPositionSnapshot
                    {
                        Row = item,
                        AnchoredPosition = rect.anchoredPosition
                    });
                }

                int column = i / rows;
                int row = i % rows;
                rect.anchoredPosition = new Vector2(
                    column == 0 ? LeftColumn : RightColumn,
                    Top - row * Step);
            }
        }

        private static void RestoreReflowed(
            List<PorterStaffPositionSnapshot> snapshots)
        {
            if (snapshots == null)
            {
                return;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                PorterStaffPositionSnapshot snapshot = snapshots[i];
                if (snapshot == null || snapshot.Row == null)
                {
                    continue;
                }
                RectTransform rect = snapshot.Row.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchoredPosition = snapshot.AnchoredPosition;
                }
            }
            snapshots.Clear();
        }

        private static PorterStaffRowSnapshot Capture(GameObject row, bool fallbackClone)
        {
            SegmentItemIconTextTextIconController segment =
                row == null ? null : row.GetComponent<SegmentItemIconTextTextIconController>();
            RectTransform rect = row == null ? null : row.GetComponent<RectTransform>();
            Text label = segment == null || segment.m_textName == null
                ? null
                : segment.m_textName.GetComponent<Text>();
            Text value = segment == null || segment.m_textValue == null
                ? null
                : segment.m_textValue.GetComponent<Text>();
            Image icon = segment == null || segment.m_icon == null
                ? null
                : segment.m_icon.GetComponent<Image>();

            return new PorterStaffRowSnapshot
            {
                Row = row,
                FallbackClone = fallbackClone,
                ActiveSelf = row != null && row.activeSelf,
                SiblingIndex = row == null ? 0 : row.transform.GetSiblingIndex(),
                AnchoredPosition = rect == null ? Vector2.zero : rect.anchoredPosition,
                LocalScale = row == null ? Vector3.one : row.transform.localScale,
                Label = label == null ? null : label.text,
                Value = value == null ? null : value.text,
                Icon = icon == null ? null : icon.sprite,
                ValueColor = value == null ? Color.white : value.color
            };
        }

        private static void RestoreBorrowed(PorterStaffRowSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Row == null)
            {
                return;
            }

            GameObject row = snapshot.Row;
            if (snapshot.FallbackClone)
            {
                row.SetActive(false);
                return;
            }

            SegmentItemIconTextTextIconController segment =
                row.GetComponent<SegmentItemIconTextTextIconController>();
            RectTransform rect = row.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchoredPosition = snapshot.AnchoredPosition;
            }
            row.transform.localScale = snapshot.LocalScale;

            if (segment != null)
            {
                Text label = segment.m_textName == null
                    ? null
                    : segment.m_textName.GetComponent<Text>();
                Text value = segment.m_textValue == null
                    ? null
                    : segment.m_textValue.GetComponent<Text>();
                Image icon = segment.m_icon == null
                    ? null
                    : segment.m_icon.GetComponent<Image>();
                if (label != null)
                {
                    label.text = snapshot.Label;
                }
                if (value != null)
                {
                    value.text = snapshot.Value;
                    value.color = snapshot.ValueColor;
                }
                if (icon != null)
                {
                    icon.sprite = snapshot.Icon;
                }
            }

            Transform parent = row.transform.parent;
            if (parent != null)
            {
                int maxIndex = Math.Max(0, parent.childCount - 1);
                row.transform.SetSiblingIndex(Math.Max(0, Math.Min(snapshot.SiblingIndex, maxIndex)));
            }
            row.SetActive(snapshot.ActiveSelf);
        }

        private static GameObject FindInactiveNativeRow(
            GameObject panel,
            GameObject fallback)
        {
            if (panel == null)
            {
                return null;
            }

            for (int i = 0; i < panel.transform.childCount; i++)
            {
                GameObject child = panel.transform.GetChild(i).gameObject;
                if (child == null || child == fallback || child.activeSelf ||
                    IsModRow(child) ||
                    child.GetComponent<SegmentItemIconTextTextIconController>() == null)
                {
                    continue;
                }
                return child;
            }
            return null;
        }

        private static GameObject GetOrCreateFallback(
            GameObject panel,
            Shift shift,
            ref GameObject fallback)
        {
            if (fallback != null)
            {
                fallback.transform.SetParent(panel.transform, worldPositionStays: false);
                return fallback;
            }

            GameObject source = null;
            for (int i = 0; i < panel.transform.childCount; i++)
            {
                GameObject child = panel.transform.GetChild(i).gameObject;
                if (child != null && !IsModRow(child) &&
                    child.GetComponent<SegmentItemIconTextTextIconController>() != null)
                {
                    source = child;
                    break;
                }
            }

            if (source == null)
            {
                return null;
            }

            fallback = UnityEngine.Object.Instantiate(source);
            fallback.name = shift == Shift.NIGHT
                ? FallbackNightRowName
                : FallbackDayRowName;
            fallback.transform.SetParent(panel.transform, worldPositionStays: false);
            fallback.transform.localScale = Vector3.one;
            fallback.SetActive(false);

            Button button = fallback.GetComponentInChildren<Button>(true);
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
            }
            EnsureTooltip(fallback);
            return fallback;
        }

        private static bool IsModRow(GameObject row)
        {
            if (row == null)
            {
                return false;
            }
            string name = row.name;
            return name == LegacyDayRowName || name == LegacyNightRowName ||
                name == FallbackDayRowName || name == FallbackNightRowName;
        }

        private static void HideLegacyAddedRow(GameObject panel)
        {
            if (panel == null)
            {
                return;
            }

            Transform day = panel.transform.Find(LegacyDayRowName);
            if (day != null)
            {
                day.gameObject.SetActive(false);
            }
            Transform night = panel.transform.Find(LegacyNightRowName);
            if (night != null)
            {
                night.gameObject.SetActive(false);
            }
        }

        private static void EnsureTooltip(GameObject row)
        {
            Button button = row == null ? null : row.GetComponentInChildren<Button>(true);
            if (button != null && button.GetComponent<HoverTooltipDelay>() == null)
            {
                button.gameObject.AddComponent<HoverTooltipDelay>();
            }
        }

        private static bool IsPointerOver(RectTransform rect)
        {
            if (rect == null)
            {
                return false;
            }

            rect.GetWorldCorners(TooltipWorldCorners);
            Vector3 mousePosition = Input.mousePosition;

            float minX = TooltipWorldCorners[0].x;
            float maxX = TooltipWorldCorners[0].x;
            float minY = TooltipWorldCorners[0].y;
            float maxY = TooltipWorldCorners[0].y;

            for (int i = 1; i < TooltipWorldCorners.Length; i++)
            {
                Vector3 corner = TooltipWorldCorners[i];
                if (corner.x < minX)
                {
                    minX = corner.x;
                }
                if (corner.x > maxX)
                {
                    maxX = corner.x;
                }
                if (corner.y < minY)
                {
                    minY = corner.y;
                }
                if (corner.y > maxY)
                {
                    maxY = corner.y;
                }
            }

            return mousePosition.x >= minX &&
                mousePosition.x <= maxX &&
                mousePosition.y >= minY &&
                mousePosition.y <= maxY;
        }

        private static void ShowTooltipIfNeeded(GameObject row)
        {
            Button button = row == null
                ? null
                : row.GetComponentInChildren<Button>(true);
            RectTransform rect = button == null
                ? null
                : button.GetComponent<RectTransform>();
            bool pointerOver = IsPointerOver(rect);

            if (!pointerOver)
            {
                if (row != null)
                {
                    TooltipHoverTimes[row] = 0f;
                }
                return;
            }

            float hoverTime;
            if (!TooltipHoverTimes.TryGetValue(row, out hoverTime))
            {
                hoverTime = 0f;
            }
            hoverTime += Time.deltaTime;
            TooltipHoverTimes[row] = hoverTime;

            if (hoverTime <= SettingsManager.Instance.m_viewSettings.m_tooltipDelay.m_value)
            {
                return;
            }

            TooltipManager.Instance
                .GetTooltipComponent<TooltipHeadingIconSmallTextController>()
                .UpdateData(
                    LocalizationManager.Get(PorterIds.Occupation),
                    LocalizationManager.Get(PorterIds.PorterStaffingTooltip),
                    PorterVisuals.GetCategoryLightSprite(),
                    TextAnchor.UpperLeft);
        }
    }

    [HarmonyPatch(typeof(DepartmentManagementLogisticsHybridController), nameof(DepartmentManagementLogisticsHybridController.Update))]
    internal static class PorterHospitalizationStaffFinalPresentationPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(DepartmentManagementLogisticsHybridController __instance)
        {
            PorterDepartmentStaffingRows.RestoreBeforeNativeUpdate(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(DepartmentManagementLogisticsHybridController __instance)
        {
            PorterDepartmentStaffingRows.ApplyAfterNativeUpdate(__instance);
        }
    }
}