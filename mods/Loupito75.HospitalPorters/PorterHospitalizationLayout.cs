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
        internal Vector2 SizeDelta;
        internal Vector2 AnchoredPosition;
    }

    internal sealed class PorterHospitalizationLayoutState
    {
        internal string DepartmentId;
        internal int RoomRows = 4;
        internal float DayStaffExtra;
        internal float NightStaffExtra;
        internal RectTransform RightRect;
        internal RectTransform RootRect;
        internal RectTransform BottomRect;
        internal RectTransform StatisticsRect;
        internal RectTransform RoomRect;
        internal RectTransform RoomLockedRect;
        internal RectTransform DayRect;
        internal RectTransform NightRect;
        internal RectTransform NightHeadingRect;
        internal RectTransform LayoutContainer;
        internal RectTransform RightBottomRect;
        internal RectTransform RightHideRect;
        internal RectTransform RootHideRoomControlRect;
        internal RectTransform RootHideRadiologyRect;
        internal RectTransform TutorialRoomsRect;
        internal RectTransform TutorialStaffRect;
        internal PorterRectSnapshot NativeRight;
        internal PorterRectSnapshot NativeRoot;
        internal PorterRectSnapshot NativeBottom;
        internal PorterRectSnapshot NativeStatistics;
        internal PorterRectSnapshot NativeRoom;
        internal PorterRectSnapshot NativeRoomLocked;
        internal PorterRectSnapshot NativeDay;
        internal PorterRectSnapshot NativeNight;
        internal PorterRectSnapshot NativeNightHeading;
        internal PorterRectSnapshot NativeLayoutContainer;
        internal PorterRectSnapshot NativeRightBottom;
        internal PorterRectSnapshot NativeRightHide;
        internal PorterRectSnapshot NativeRootHideRoomControl;
        internal PorterRectSnapshot NativeRootHideRadiology;
        internal PorterRectSnapshot NativeTutorialRooms;
        internal PorterRectSnapshot NativeTutorialStaff;
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

            state.DayStaffExtra = 0f;
            state.NightStaffExtra = 0f;
            RestoreNativeState(state);
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
                button.anchoredPosition = new Vector3(
                    50f + column * 115f,
                    -18f - row * RoomRowStep,
                    1000f);
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
                RootRect = controller.GetComponent<RectTransform>(),
                BottomRect = controller.m_panelBottom == null
                    ? null
                    : controller.m_panelBottom.GetComponent<RectTransform>(),
                StatisticsRect = controller.m_panelStatistics == null
                    ? null
                    : controller.m_panelStatistics.GetComponent<RectTransform>(),
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

            state.NightHeadingRect = FindDirectChildByName(
                state.LayoutContainer,
                "HeadingPanelStaffNight");
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
            state.RootHideRoomControlRect = FindDirectChildByName(
                state.RootRect,
                "LogisticsPanelHideRoomControl1");
            state.RootHideRadiologyRect = FindDirectChildByName(
                state.RootRect,
                "LogisticsPanelHideRoomControlRadiology");

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

            state.NativeRight = Capture(state.RightRect);
            state.NativeRoot = Capture(state.RootRect);
            state.NativeBottom = Capture(state.BottomRect);
            state.NativeStatistics = Capture(state.StatisticsRect);
            state.NativeRoom = Capture(state.RoomRect);
            state.NativeRoomLocked = Capture(state.RoomLockedRect);
            state.NativeDay = Capture(state.DayRect);
            state.NativeNight = Capture(state.NightRect);
            state.NativeNightHeading = Capture(state.NightHeadingRect);
            state.NativeLayoutContainer = Capture(state.LayoutContainer);
            state.NativeRightBottom = Capture(state.RightBottomRect);
            state.NativeRightHide = Capture(state.RightHideRect);
            state.NativeRootHideRoomControl = Capture(state.RootHideRoomControlRect);
            state.NativeRootHideRadiology = Capture(state.RootHideRadiologyRect);
            state.NativeTutorialRooms = Capture(state.TutorialRoomsRect);
            state.NativeTutorialStaff = Capture(state.TutorialStaffRect);

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
                SizeDelta = rect.sizeDelta,
                AnchoredPosition = rect.anchoredPosition
            };
        }

        private static void RestoreNativeState(PorterHospitalizationLayoutState state)
        {
            if (state == null)
            {
                return;
            }

            Restore(state.RightRect, state.NativeRight);
            Restore(state.RootRect, state.NativeRoot);
            Restore(state.BottomRect, state.NativeBottom);
            Restore(state.StatisticsRect, state.NativeStatistics);
            Restore(state.RoomRect, state.NativeRoom);
            Restore(state.RoomLockedRect, state.NativeRoomLocked);
            Restore(state.DayRect, state.NativeDay);
            Restore(state.NightRect, state.NativeNight);
            Restore(state.NightHeadingRect, state.NativeNightHeading);
            Restore(state.LayoutContainer, state.NativeLayoutContainer);
            Restore(state.RightBottomRect, state.NativeRightBottom);
            Restore(state.RightHideRect, state.NativeRightHide);
            Restore(state.RootHideRoomControlRect, state.NativeRootHideRoomControl);
            Restore(state.RootHideRadiologyRect, state.NativeRootHideRadiology);
            Restore(state.TutorialRoomsRect, state.NativeTutorialRooms);
            Restore(state.TutorialStaffRect, state.NativeTutorialStaff);

            foreach (KeyValuePair<RectTransform, PorterRectSnapshot> pair in state.NativeLayoutChildren)
            {
                Restore(pair.Key, pair.Value);
            }
        }

        private static void Restore(RectTransform rect, PorterRectSnapshot snapshot)
        {
            if (rect == null || snapshot == null)
            {
                return;
            }

            rect.sizeDelta = snapshot.SizeDelta;
            rect.anchoredPosition = snapshot.AnchoredPosition;
        }

        private static void Apply(PorterHospitalizationLayoutState state)
        {
            if (state == null)
            {
                return;
            }

            RestoreNativeState(state);

            float roomExtra = Math.Max(0, state.RoomRows - NativeRoomRows) * RoomRowStep;
            float dayStaffExtra = Math.Max(0f, state.DayStaffExtra);
            float nightStaffExtra = Math.Max(0f, state.NightStaffExtra);
            float totalExtra = roomExtra + dayStaffExtra + nightStaffExtra;
            if (totalExtra <= 0f)
            {
                return;
            }

            PositionLayoutChildren(state, roomExtra, dayStaffExtra, nightStaffExtra);

            ApplyExpandedRect(state.RoomRect, state.NativeRoom, roomExtra, 0f);
            ApplyExpandedRect(state.RoomLockedRect, state.NativeRoomLocked, roomExtra, 0f);
            ApplyExpandedRect(state.DayRect, state.NativeDay, dayStaffExtra, roomExtra);
            ApplyExpandedRect(
                state.NightRect,
                state.NativeNight,
                nightStaffExtra,
                roomExtra + dayStaffExtra);

            if (state.LayoutContainer != null && state.LayoutContainer != state.RoomRect)
            {
                ApplyExpandedRect(
                    state.LayoutContainer,
                    state.NativeLayoutContainer,
                    totalExtra,
                    0f);
            }

            if (state.RightRect != null && state.RightRect != state.LayoutContainer &&
                state.RightRect != state.RoomRect)
            {
                ApplyExpandedRect(state.RightRect, state.NativeRight, totalExtra, 0f);
                ShiftDown(state.RightBottomRect, state.NativeRightBottom, totalExtra);
                ApplyExpandedRect(state.RightHideRect, state.NativeRightHide, totalExtra, 0f);

                MoveForExpandedParent(
                    state.TutorialRoomsRect,
                    state.NativeTutorialRooms,
                    totalExtra,
                    0f);
                MoveForExpandedParent(
                    state.TutorialStaffRect,
                    state.NativeTutorialStaff,
                    totalExtra,
                    -roomExtra);
            }

            if (state.RootRect != null && state.RootRect != state.RightRect &&
                state.RootRect != state.LayoutContainer && state.RootRect != state.RoomRect)
            {
                ApplyExpandedRect(state.RootRect, state.NativeRoot, totalExtra, 0f);
                ShiftDown(state.StatisticsRect, state.NativeStatistics, totalExtra);
                ShiftDown(state.BottomRect, state.NativeBottom, totalExtra);
                ShiftDown(state.RootHideRoomControlRect, state.NativeRootHideRoomControl, totalExtra);
                ShiftDown(state.RootHideRadiologyRect, state.NativeRootHideRadiology, totalExtra);
            }
        }

        private static void PositionLayoutChildren(
            PorterHospitalizationLayoutState state,
            float roomExtra,
            float dayStaffExtra,
            float nightStaffExtra)
        {
            if (state == null || state.LayoutContainer == null || state.NativeRoom == null)
            {
                return;
            }

            foreach (KeyValuePair<RectTransform, PorterRectSnapshot> pair in state.NativeLayoutChildren)
            {
                RectTransform child = pair.Key;
                PorterRectSnapshot snapshot = pair.Value;
                if (child == null || snapshot == null)
                {
                    continue;
                }

                float shift = 0f;
                if (snapshot.AnchoredPosition.y < state.NativeRoom.AnchoredPosition.y - PositionTolerance)
                {
                    shift += roomExtra;
                }
                if (state.NativeDay != null &&
                    snapshot.AnchoredPosition.y < state.NativeDay.AnchoredPosition.y - PositionTolerance)
                {
                    shift += dayStaffExtra;
                }
                if (state.NativeNight != null &&
                    snapshot.AnchoredPosition.y < state.NativeNight.AnchoredPosition.y - PositionTolerance)
                {
                    shift += nightStaffExtra;
                }

                child.anchoredPosition = new Vector2(
                    snapshot.AnchoredPosition.x,
                    snapshot.AnchoredPosition.y - shift);
            }
        }

        private static void ApplyExpandedRect(
            RectTransform rect,
            PorterRectSnapshot snapshot,
            float extraHeight,
            float verticalShift)
        {
            if (rect == null || snapshot == null)
            {
                return;
            }

            float safeExtra = Math.Max(0f, extraHeight);
            float safeShift = Math.Max(0f, verticalShift);
            rect.sizeDelta = new Vector2(
                snapshot.SizeDelta.x,
                snapshot.SizeDelta.y + safeExtra);
            rect.anchoredPosition = new Vector2(
                snapshot.AnchoredPosition.x,
                snapshot.AnchoredPosition.y -
                safeShift -
                (1f - rect.pivot.y) * safeExtra);
        }

        private static void ShiftDown(
            RectTransform rect,
            PorterRectSnapshot snapshot,
            float amount)
        {
            if (rect == null || snapshot == null || amount <= 0f)
            {
                return;
            }

            rect.anchoredPosition = new Vector2(
                snapshot.AnchoredPosition.x,
                snapshot.AnchoredPosition.y - amount);
        }

        private static void MoveForExpandedParent(
            RectTransform rect,
            PorterRectSnapshot snapshot,
            float parentExtra,
            float desiredVerticalShift)
        {
            if (rect == null || snapshot == null || parentExtra <= 0f)
            {
                return;
            }

            float anchorY = (rect.anchorMin.y + rect.anchorMax.y) * 0.5f;
            float parentAnchorShift = -(1f - anchorY) * parentExtra;
            float anchoredDelta = desiredVerticalShift - parentAnchorShift;

            rect.anchoredPosition = new Vector2(
                snapshot.AnchoredPosition.x,
                snapshot.AnchoredPosition.y + anchoredDelta);
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
