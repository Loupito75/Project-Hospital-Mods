using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalPorters
{
    internal sealed class PorterRectSnapshot
    {
        internal Vector2 AnchoredPosition;
    }

    internal sealed class PorterHospitalizationLayoutState
    {
        internal string DepartmentId;
        internal int RoomRows = 4;
        internal float DayStaffExtra;
        internal float NightStaffExtra;
        internal float AppliedRoomExtra;
        internal float AppliedDayStaffExtra;
        internal float AppliedNightStaffExtra;
        internal RectTransform RightRect;
        internal RectTransform RoomRect;
        internal RectTransform RoomLockedRect;
        internal RectTransform DayRect;
        internal RectTransform NightRect;
        internal RectTransform LayoutContainer;
        internal RectTransform RightBottomRect;
        internal RectTransform RightHideRect;
        internal RectTransform TutorialRoomsRect;
        internal RectTransform TutorialStaffRect;
        internal PorterRectSnapshot NativeRoom;
        internal PorterRectSnapshot NativeDay;
        internal PorterRectSnapshot NativeNight;
        internal readonly Dictionary<RectTransform, PorterRectSnapshot> NativeLayoutChildren =
            new Dictionary<RectTransform, PorterRectSnapshot>();
    }

    internal static class PorterHospitalizationLayout
    {
        private const int NativeRoomRows = 4;
        private const float RoomRowStep = 26f;
        private const float PositionTolerance = 0.5f;

        private static readonly Dictionary<DepartmentManagementLogisticsHybridController, PorterHospitalizationLayoutState> States =
            new Dictionary<DepartmentManagementLogisticsHybridController, PorterHospitalizationLayoutState>();
        private static readonly FieldInfo HospitalizationRoomButtonsField =
            AccessTools.Field(
                typeof(DepartmentManagementLogisticsHybridController),
                "m_roomButtonsHospitalization");

        internal static void RestoreBeforeNativeLayout(
            DepartmentManagementLogisticsHybridController controller)
        {
            PorterHospitalizationLayoutState state = EnsureState(controller);
            if (state == null)
            {
                return;
            }

            ApplyLayout(state, 0f, 0f, 0f);
            state.RoomRows = NativeRoomRows;
            state.DayStaffExtra = 0f;
            state.NightStaffExtra = 0f;
        }

        internal static void AfterNativeLayout(
            DepartmentManagementLogisticsHybridController controller,
            Department department)
        {
            PorterHospitalizationLayoutState state = EnsureState(controller);
            if (state == null)
            {
                return;
            }

            string departmentId = department == null || department.GetDepartmentType() == null
                ? null
                : department.GetDepartmentType().DatabaseID.ToString();
            if (state.DepartmentId != departmentId)
            {
                state.DepartmentId = departmentId;
                state.RoomRows = NativeRoomRows;
                state.DayStaffExtra = 0f;
                state.NightStaffExtra = 0f;
            }

            CaptureNativeState(state);
            Apply(state);
        }

        internal static void OnHospitalizationRoomButtonsCreated(
            DepartmentManagementLogisticsHybridController controller)
        {
            PorterHospitalizationLayoutState state = EnsureState(controller);
            Department department = GetActiveDepartment();
            if (state == null || department == null || department.GetDepartmentType() == null ||
                department.GetDepartmentType().NoHospitalization || state.RoomRect == null)
            {
                return;
            }

            List<RectTransform> buttons = GetHospitalizationRoomButtonRects(
                controller,
                state.RoomRect);
            ApplyHospitalizationRoomButtonPositions(state, buttons);
            Apply(state);

        }

        internal static void ReflowHospitalizationRoomButtons(
            DepartmentManagementLogisticsHybridController controller)
        {
            PorterHospitalizationLayoutState state = EnsureState(controller);
            if (state == null || state.RoomRect == null)
            {
                return;
            }

            List<RectTransform> buttons = GetHospitalizationRoomButtonRects(
                controller,
                state.RoomRect);
            ApplyHospitalizationRoomButtonPositions(state, buttons);
        }

        private static List<RectTransform> GetHospitalizationRoomButtonRects(
            DepartmentManagementLogisticsHybridController controller,
            RectTransform roomPanel)
        {
            List<RectTransform> result = new List<RectTransform>();
            List<GameObject> nativeButtons = null;
            if (controller != null && !object.ReferenceEquals(HospitalizationRoomButtonsField, null))
            {
                nativeButtons =
                    HospitalizationRoomButtonsField.GetValue(controller) as List<GameObject>;
            }

            if (nativeButtons != null)
            {
                for (int i = 0; i < nativeButtons.Count; i++)
                {
                    GameObject button = nativeButtons[i];
                    RectTransform rect = button == null
                        ? null
                        : button.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        result.Add(rect);
                    }
                }
                return result;
            }

            if (roomPanel == null)
            {
                return result;
            }

            for (int i = 0; i < roomPanel.childCount; i++)
            {
                RectTransform child = roomPanel.GetChild(i) as RectTransform;
                if (child != null &&
                    child.GetComponent<SegmentItemIconTextTextIconController>() != null)
                {
                    result.Add(child);
                }
            }
            return result;
        }

        private static void ApplyHospitalizationRoomButtonPositions(
            PorterHospitalizationLayoutState state,
            List<RectTransform> buttons)
        {
            if (state == null || buttons == null)
            {
                return;
            }

            int rows = Math.Max(NativeRoomRows, (buttons.Count + 1) / 2);
            state.RoomRows = rows;
            for (int i = 0; i < buttons.Count; i++)
            {
                RectTransform button = buttons[i];
                if (button == null)
                {
                    continue;
                }

                int column = i / rows;
                int row = i % rows;
                Vector2 targetPosition = new Vector2(
                    50f + column * 115f,
                    -18f - row * RoomRowStep);
                if (Mathf.Abs(button.anchoredPosition.x - targetPosition.x) >
                        PositionTolerance ||
                    Mathf.Abs(button.anchoredPosition.y - targetPosition.y) >
                        PositionTolerance)
                {
                    button.anchoredPosition = targetPosition;
                }
            }
        }

        internal static void OnPorterStaffingPresented(
            DepartmentManagementLogisticsHybridController controller,
            float dayExtra,
            float nightExtra)
        {
            PorterHospitalizationLayoutState state = EnsureState(controller);
            Department department = GetActiveDepartment();
            if (state == null || department == null || department.GetDepartmentType() == null ||
                department.GetDepartmentType().NoHospitalization)
            {
                return;
            }

            state.DayStaffExtra = Math.Max(0f, dayExtra);
            state.NightStaffExtra = Math.Max(0f, nightExtra);
            Apply(state);
        }

        private static Department GetActiveDepartment()
        {
            return Hospital.Instance == null
                ? null
                : Hospital.Instance.GetActiveDepartment();
        }

        private static PorterHospitalizationLayoutState EnsureState(
            DepartmentManagementLogisticsHybridController controller)
        {
            if (controller == null)
            {
                return null;
            }

            PorterHospitalizationLayoutState existing;
            if (States.TryGetValue(controller, out existing))
            {
                return existing;
            }

            PorterHospitalizationLayoutState state = new PorterHospitalizationLayoutState
            {
                RightRect = controller.m_panelRight == null
                    ? null
                    : controller.m_panelRight.GetComponent<RectTransform>(),
                RoomRect = controller.m_roomPanelHospitalization == null
                    ? null
                    : controller.m_roomPanelHospitalization.GetComponent<RectTransform>(),
                RoomLockedRect = controller.m_roomsLockedHospitalizationText == null
                    ? null
                    : controller.m_roomsLockedHospitalizationText.GetComponent<RectTransform>(),
                DayRect = controller.m_staffHospitalizationPanel == null
                    ? null
                    : controller.m_staffHospitalizationPanel.GetComponent<RectTransform>(),
                NightRect = controller.m_staffHospitalizationPanelNight == null
                    ? null
                    : controller.m_staffHospitalizationPanelNight.GetComponent<RectTransform>(),
                LayoutContainer = controller.m_panelHospitalization == null
                    ? null
                    : controller.m_panelHospitalization.GetComponent<RectTransform>()
            };

            state.RightBottomRect = FindDirectChildByName(state.RightRect, "BottomPanel");
            state.RightHideRect = FindDirectChildByName(
                state.RightRect,
                "LogisticsPanelHideHospitalization");
            state.TutorialRoomsRect = FindDirectChildByName(
                state.RightRect,
                "TutorialArrowMandatoryRoomsHospitalization");
            state.TutorialStaffRect = FindDirectChildByName(
                state.RightRect,
                "TutorialArrowMandatoryStaffHospitalization");
            CaptureNativeState(state);
            States.Add(controller, state);
            return state;
        }

        private static void CaptureNativeState(PorterHospitalizationLayoutState state)
        {
            if (state == null)
            {
                return;
            }

            state.NativeRoom = Capture(state.RoomRect);
            state.NativeDay = Capture(state.DayRect);
            state.NativeNight = Capture(state.NightRect);

            state.NativeLayoutChildren.Clear();
            if (state.LayoutContainer == null)
            {
                return;
            }

            for (int childIndex = 0; childIndex < state.LayoutContainer.childCount; childIndex++)
            {
                RectTransform child = state.LayoutContainer.GetChild(childIndex) as RectTransform;
                if (child != null)
                {
                    state.NativeLayoutChildren.Add(child, Capture(child));
                }
            }
        }

        private static PorterRectSnapshot Capture(RectTransform rect)
        {
            if (rect == null)
            {
                return null;
            }

            return new PorterRectSnapshot
            {
                AnchoredPosition = rect.anchoredPosition
            };
        }

        private static void Apply(PorterHospitalizationLayoutState state)
        {
            if (state == null)
            {
                return;
            }

            float roomExtra = Math.Max(0, state.RoomRows - NativeRoomRows) * RoomRowStep;
            float dayStaffExtra = Math.Max(0f, state.DayStaffExtra);
            float nightStaffExtra = Math.Max(0f, state.NightStaffExtra);
            ApplyLayout(state, roomExtra, dayStaffExtra, nightStaffExtra);
        }

        private static void ApplyLayout(
            PorterHospitalizationLayoutState state,
            float roomExtra,
            float dayStaffExtra,
            float nightStaffExtra)
        {
            if (state == null)
            {
                return;
            }

            float roomDelta = roomExtra - state.AppliedRoomExtra;
            float dayDelta = dayStaffExtra - state.AppliedDayStaffExtra;
            float nightDelta = nightStaffExtra - state.AppliedNightStaffExtra;
            if (Mathf.Abs(roomDelta) <= PositionTolerance &&
                Mathf.Abs(dayDelta) <= PositionTolerance &&
                Mathf.Abs(nightDelta) <= PositionTolerance)
            {
                return;
            }

            // Apply only HPO's change in required space. Do not restore a captured absolute
            // layout: another UI mod may have adjusted these same right-side RectTransforms.
            float totalDelta = roomDelta + dayDelta + nightDelta;
            AdjustLayoutChildren(state, roomDelta, dayDelta, nightDelta);

            AdjustExpandedRect(state.RoomRect, roomDelta, 0f);
            AdjustExpandedRect(state.RoomLockedRect, roomDelta, 0f);
            AdjustExpandedRect(state.DayRect, dayDelta, roomDelta);
            AdjustExpandedRect(
                state.NightRect,
                nightDelta,
                roomDelta + dayDelta);

            if (state.LayoutContainer != null && state.LayoutContainer != state.RoomRect)
            {
                AdjustExpandedRect(state.LayoutContainer, totalDelta, 0f);
            }

            if (state.RightRect != null && state.RightRect != state.LayoutContainer &&
                state.RightRect != state.RoomRect)
            {
                AdjustExpandedRect(state.RightRect, totalDelta, 0f);
                AdjustDown(state.RightBottomRect, totalDelta);
                AdjustExpandedRect(state.RightHideRect, totalDelta, 0f);

                AdjustForExpandedParent(
                    state.TutorialRoomsRect,
                    totalDelta,
                    0f);
                AdjustForExpandedParent(
                    state.TutorialStaffRect,
                    totalDelta,
                    -roomDelta);
            }

            state.AppliedRoomExtra = roomExtra;
            state.AppliedDayStaffExtra = dayStaffExtra;
            state.AppliedNightStaffExtra = nightStaffExtra;
        }

        private static void AdjustLayoutChildren(
            PorterHospitalizationLayoutState state,
            float roomDelta,
            float dayStaffDelta,
            float nightStaffDelta)
        {
            if (state == null || state.LayoutContainer == null || state.NativeRoom == null)
            {
                return;
            }

            foreach (KeyValuePair<RectTransform, PorterRectSnapshot> pair in state.NativeLayoutChildren)
            {
                RectTransform child = pair.Key;
                PorterRectSnapshot snapshot = pair.Value;
                if (child == null || snapshot == null ||
                    child == state.RoomRect ||
                    child == state.RoomLockedRect ||
                    child == state.DayRect ||
                    child == state.NightRect)
                {
                    continue;
                }

                float shiftDelta = 0f;
                if (snapshot.AnchoredPosition.y <
                    state.NativeRoom.AnchoredPosition.y - PositionTolerance)
                {
                    shiftDelta += roomDelta;
                }
                if (state.NativeDay != null &&
                    snapshot.AnchoredPosition.y <
                    state.NativeDay.AnchoredPosition.y - PositionTolerance)
                {
                    shiftDelta += dayStaffDelta;
                }
                if (state.NativeNight != null &&
                    snapshot.AnchoredPosition.y <
                    state.NativeNight.AnchoredPosition.y - PositionTolerance)
                {
                    shiftDelta += nightStaffDelta;
                }

                if (Mathf.Abs(shiftDelta) <= PositionTolerance)
                {
                    continue;
                }

                child.anchoredPosition = new Vector2(
                    child.anchoredPosition.x,
                    child.anchoredPosition.y - shiftDelta);
            }
        }

        private static void AdjustExpandedRect(
            RectTransform rect,
            float extraHeightDelta,
            float verticalShiftDelta)
        {
            if (rect == null ||
                (Mathf.Abs(extraHeightDelta) <= PositionTolerance &&
                 Mathf.Abs(verticalShiftDelta) <= PositionTolerance))
            {
                return;
            }

            rect.sizeDelta = new Vector2(
                rect.sizeDelta.x,
                rect.sizeDelta.y + extraHeightDelta);
            rect.anchoredPosition = new Vector2(
                rect.anchoredPosition.x,
                rect.anchoredPosition.y -
                verticalShiftDelta -
                (1f - rect.pivot.y) * extraHeightDelta);
        }

        private static void AdjustDown(RectTransform rect, float amountDelta)
        {
            if (rect == null || Mathf.Abs(amountDelta) <= PositionTolerance)
            {
                return;
            }

            rect.anchoredPosition = new Vector2(
                rect.anchoredPosition.x,
                rect.anchoredPosition.y - amountDelta);
        }

        private static void AdjustForExpandedParent(
            RectTransform rect,
            float parentExtraDelta,
            float desiredVerticalShiftDelta)
        {
            if (rect == null ||
                (Mathf.Abs(parentExtraDelta) <= PositionTolerance &&
                 Mathf.Abs(desiredVerticalShiftDelta) <= PositionTolerance))
            {
                return;
            }

            float anchorY = (rect.anchorMin.y + rect.anchorMax.y) * 0.5f;
            float parentAnchorShiftDelta = -(1f - anchorY) * parentExtraDelta;
            float anchoredDelta = desiredVerticalShiftDelta - parentAnchorShiftDelta;

            rect.anchoredPosition = new Vector2(
                rect.anchoredPosition.x,
                rect.anchoredPosition.y + anchoredDelta);
        }

        private static RectTransform FindDirectChildByName(
            RectTransform parent,
            string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            for (int childIndex = 0; childIndex < parent.childCount; childIndex++)
            {
                RectTransform child = parent.GetChild(childIndex) as RectTransform;
                if (child != null && child.name == name)
                {
                    return child;
                }
            }
            return null;
        }

    }

    [HarmonyPatch(
        typeof(DepartmentManagementLogisticsHybridController),
        "UpdateLayout",
        new Type[] { typeof(Department) })]
    internal static class PorterHospitalizationNativeLayoutPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(DepartmentManagementLogisticsHybridController __instance)
        {
            PorterHospitalizationLayout.RestoreBeforeNativeLayout(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            DepartmentManagementLogisticsHybridController __instance,
            Department department)
        {
            PorterHospitalizationLayout.AfterNativeLayout(__instance, department);
        }
    }

    [HarmonyPatch(
        typeof(DepartmentManagementLogisticsHybridController),
        "CreateRoomButtons",
        new Type[] { typeof(bool), typeof(bool) })]
    internal static class PorterHospitalizationRoomLayoutPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            DepartmentManagementLogisticsHybridController __instance,
            bool clinic,
            bool shared)
        {
            if (!clinic && !shared)
            {
                PorterHospitalizationLayout.OnHospitalizationRoomButtonsCreated(__instance);
            }
        }
    }
    [HarmonyPatch(
        typeof(DepartmentManagementLogisticsHybridController),
        "UpdateRoomButtons",
        new Type[] { typeof(bool), typeof(bool) })]
    internal static class PorterHospitalizationRoomUpdateLayoutPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            DepartmentManagementLogisticsHybridController __instance,
            bool clinic,
            bool shared)
        {
            if (!clinic && !shared)
            {
                PorterHospitalizationLayout.ReflowHospitalizationRoomButtons(__instance);
            }
        }
    }

}
