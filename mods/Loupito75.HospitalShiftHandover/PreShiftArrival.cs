using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalShiftHandover
{
    internal static class PreShiftArrival
    {
        private sealed class LockerArrivalPlan
        {
            internal int Stamp;
            internal bool Eligible;
            internal Room CommonRoom;
        }

        private static readonly Dictionary<EmployeeComponent, LockerArrivalPlan> LockerArrivalPlans =
            new Dictionary<EmployeeComponent, LockerArrivalPlan>();

        internal static bool ShouldPlanLockerDressing(EmployeeComponent employee)
        {
            if (employee == null ||
                employee.m_state == null ||
                employee.m_entity == null ||
                DayTime.Instance == null)
            {
                return false;
            }

            int stamp = GetPlanStamp(employee);

            if (!ShiftHandoverConfig.DressingEnabled)
            {
                LockerArrivalPlans[employee] = new LockerArrivalPlan
                {
                    Stamp = stamp,
                    Eligible = false,
                    CommonRoom = null
                };
                PreShiftLockerInteractionTest
                    .RestoreProfessionalAtHomeIfDressingUnavailable(
                        employee);
                return false;
            }

            if (WorldEventManager.Instance != null &&
                WorldEventManager.Instance.HasEventForcingBiohazardClothes())
            {
                LockerArrivalPlans[employee] = new LockerArrivalPlan
                {
                    Stamp = stamp,
                    Eligible = false,
                    CommonRoom = null
                };
                return false;
            }

            if (PreShiftLockerInteractionTest.HasResolvedDressingForCurrentShift(employee))
            {
                return false;
            }
            LockerArrivalPlan cached;
            if (LockerArrivalPlans.TryGetValue(employee, out cached) &&
                cached != null &&
                cached.Stamp == stamp)
            {
                if (!cached.Eligible)
                {
                    PreShiftLockerInteractionTest
                        .RestoreProfessionalAtHomeIfDressingUnavailable(
                            employee);
                }
                return cached.Eligible;
            }

            Room plannedCommonRoom;
            bool eligible =
                EvaluateLockerDressingEligibility(
                    employee,
                    out plannedCommonRoom);
            LockerArrivalPlans[employee] = new LockerArrivalPlan
            {
                Stamp = stamp,
                Eligible = eligible,
                CommonRoom = plannedCommonRoom
            };
            if (!eligible)
            {
                PreShiftLockerInteractionTest
                    .RestoreProfessionalAtHomeIfDressingUnavailable(
                        employee);
            }
            return eligible;
        }

        internal static bool TryRouteToCommonArea(Behavior behavior, EmployeeComponent employee)
        {
            if (behavior == null || employee == null || employee.m_state == null ||
                !HandoverRules.ShouldStageBeforeWorkplace(employee))
            {
                return false;
            }

            bool lockerDressingPlanned =
                ShouldPlanLockerDressing(employee);

            Room plannedCommonRoom = null;
            LockerArrivalPlan lockerPlan;
            if (LockerArrivalPlans.TryGetValue(employee, out lockerPlan) &&
                lockerPlan != null &&
                lockerPlan.Stamp == GetPlanStamp(employee))
            {
                plannedCommonRoom = lockerPlan.CommonRoom;
            }

            Vector2i workplaceTarget;
            int workplaceFloor;
            if (!WorkplaceTravelEstimator.TryGetWorkplaceTarget(employee, out workplaceTarget, out workplaceFloor))
            {
                return false;
            }

            Department department = behavior.GetDepartment();
            if (department == null)
            {
                return false;
            }

            GameDBRoomType commonRoomType = Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_COMMON_ROOM");
            if (commonRoomType == null)
            {
                return false;
            }

            List<Room> departmentCommonRooms = MapScriptInterface.Instance.FindValidRoomsWithType(commonRoomType, department);
            if (departmentCommonRooms == null)
            {
                departmentCommonRooms = new List<Room>();
            }

            WalkComponent walk = behavior.GetComponent<WalkComponent>();
            if (walk == null || walk.Floor == null)
            {
                return false;
            }

            Vector2i commonAreaPosition = Vector2i.ZERO_VECTOR;
            int commonAreaFloor = -1;
            Room selectedCommonRoom = null;
            int bestDistanceSquared = int.MaxValue;

            // The room was selected before the commute with the same historical
            // common-room rules. Reuse that room so the +8 minute dressing lead is granted
            // only to employees who are actually routed to a room containing a locker.
            if (plannedCommonRoom != null)
            {
                List<Room> validCommonRooms =
                    GetHospitalCommonRooms(
                        commonRoomType,
                        departmentCommonRooms);
                if (validCommonRooms.Contains(plannedCommonRoom))
                {
                    Vector2i plannedPosition =
                        MapScriptInterface.Instance.GetRandomFreePosition(
                            plannedCommonRoom,
                            behavior.GetAccessRights());
                    if (plannedPosition != Vector2i.ZERO_VECTOR)
                    {
                        commonAreaPosition = plannedPosition;
                        commonAreaFloor = plannedCommonRoom.GetFloorIndex();
                        selectedCommonRoom = plannedCommonRoom;
                    }
                }
            }

            // Preserve the validated behavior whenever the employee's own department already
            // provides a usable common room on the workplace floor.
            for (int i = 0;
                 commonAreaPosition == Vector2i.ZERO_VECTOR &&
                 i < departmentCommonRooms.Count;
                 i++)
            {
                Room commonRoom = departmentCommonRooms[i];
                if (commonRoom == null || commonRoom.GetFloorIndex() != workplaceFloor)
                {
                    continue;
                }

                Vector2i candidatePosition = MapScriptInterface.Instance.GetRandomFreePosition(
                    commonRoom,
                    behavior.GetAccessRights());
                if (candidatePosition == Vector2i.ZERO_VECTOR)
                {
                    continue;
                }

                int deltaX = candidatePosition.m_x - workplaceTarget.m_x;
                int deltaY = candidatePosition.m_y - workplaceTarget.m_y;
                int distanceSquared = deltaX * deltaX + deltaY * deltaY;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    commonAreaPosition = candidatePosition;
                    commonAreaFloor = workplaceFloor;
                    selectedCommonRoom = commonRoom;
                }
            }

            // If the employee's own department has no usable common room, expand the fallback
            // to all valid common rooms in the hospital. FindValidRoomsWithType() remains the
            // native validity filter for each department; GetRandomFreePosition() and GridMap
            // then enforce the employee's real access rights and reachability.
            if (commonAreaPosition == Vector2i.ZERO_VECTOR)
            {
                List<Room> fallbackCommonRooms = GetHospitalCommonRooms(commonRoomType, departmentCommonRooms);
                float bestRouteScore = float.MaxValue;
                int currentFloor = walk.GetFloorIndex();
                Vector2i currentTile = walk.GetCurrentTile();

                for (int i = 0; i < fallbackCommonRooms.Count; i++)
                {
                    Room commonRoom = fallbackCommonRooms[i];
                    if (commonRoom == null)
                    {
                        continue;
                    }

                    int candidateFloor = commonRoom.GetFloorIndex();
                    Vector2i candidatePosition = MapScriptInterface.Instance.GetRandomFreePosition(
                        commonRoom,
                        behavior.GetAccessRights());
                    if (candidatePosition == Vector2i.ZERO_VECTOR)
                    {
                        continue;
                    }

                    float candidateArrivalDistance = GridMap.GetInstance().GetDistance(
                        currentFloor,
                        currentTile,
                        candidateFloor,
                        candidatePosition,
                        behavior.GetAccessRights());
                    if (candidateArrivalDistance < 0f ||
                        candidateArrivalDistance >= float.MaxValue)
                    {
                        continue;
                    }

                    float workplaceDistance = GridMap.GetInstance().GetDistance(
                        candidateFloor,
                        candidatePosition,
                        workplaceFloor,
                        workplaceTarget,
                        behavior.GetAccessRights());
                    if (workplaceDistance < 0f || workplaceDistance >= float.MaxValue)
                    {
                        continue;
                    }

                    float routeScore =
                        candidateArrivalDistance + workplaceDistance;
                    if (routeScore < bestRouteScore)
                    {
                        bestRouteScore = routeScore;
                        commonAreaPosition = candidatePosition;
                        commonAreaFloor = candidateFloor;
                        selectedCommonRoom = commonRoom;
                    }
                }
            }

            if (commonAreaPosition == Vector2i.ZERO_VECTOR || commonAreaFloor < 0)
            {
                return false;
            }

            WorkplaceTravelEstimator.Clear(employee);

            // Common-room selection above intentionally ignores lockers and matches the
            // validated pre-dressing HSH routing. Dressing is considered only after that
            // room has already been selected. If this exact room has no usable locker,
            // the employee keeps professional clothes and HSH never reroutes to another
            // common room just to find a locker.
            //
            // When the selected room does have a locker, the existing timing guard still
            // requires enough margin for arrival -> locker/common room -> workplace.
            float arrivalDistance = GridMap.GetInstance().GetDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                commonAreaFloor,
                commonAreaPosition,
                behavior.GetAccessRights());
            float workplaceDistanceFromCommonArea = GridMap.GetInstance().GetDistance(
                commonAreaFloor,
                commonAreaPosition,
                workplaceFloor,
                workplaceTarget,
                behavior.GetAccessRights());

            bool civilianTimingSafe =
                lockerDressingPlanned &&
                selectedCommonRoom != null &&
                PreShiftLockerInteractionTest.RoomHasUsableLocker(selectedCommonRoom) &&
                arrivalDistance >= 0f &&
                arrivalDistance < float.MaxValue &&
                workplaceDistanceFromCommonArea >= 0f &&
                workplaceDistanceFromCommonArea < float.MaxValue;

            if (civilianTimingSafe)
            {
                float projectedArrivalMinutes =
                    WorkplaceTravelEstimator.DistanceToMinutes(
                        behavior,
                        arrivalDistance);
                float projectedWorkplaceMinutes =
                    WorkplaceTravelEstimator.DistanceToMinutes(
                        behavior,
                        workplaceDistanceFromCommonArea);
                float projectedLockerSlack =
                    HandoverRules.GetMinutesUntilOwnShift(employee) -
                    projectedArrivalMinutes -
                    projectedWorkplaceMinutes;

                civilianTimingSafe =
                    projectedLockerSlack >=
                    PreShiftLockerInteractionTest.MinimumRequiredSlackMinutes;
            }

            if (civilianTimingSafe)
            {
                PreShiftLockerInteractionTest.PrepareCivilianArrival(
                    behavior,
                    employee,
                    selectedCommonRoom);
            }

            // WalkComponent.SetDestination() calls CheckElevator(), which uses GridMap.GetWaypoints()
            // for cross-floor travel. No custom elevator/pathfinding state is introduced here.
            walk.SetDestination(
                new Vector2f(commonAreaPosition.m_x, commonAreaPosition.m_y),
                commonAreaFloor);
            ShiftDiagnostics.RecordCommonAreaRoute(behavior, employee);
            return true;
        }

        internal static bool IsInCommonArea(Behavior behavior)
        {
            if (behavior == null)
            {
                return false;
            }

            WalkComponent walk = behavior.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return false;
            }

            Room room = MapScriptInterface.Instance.GetRoomAt(
                walk.GetCurrentTile(),
                walk.GetFloorIndex());
            if (room == null)
            {
                return false;
            }

            GameDBRoomType commonRoomType = Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_COMMON_ROOM");
            return commonRoomType != null &&
                   room.m_roomPersistentData.m_roomType.Entry == commonRoomType;
        }

        internal static void Shutdown()
        {
            LockerArrivalPlans.Clear();
            WorkplaceTravelEstimator.Shutdown();
        }

        private static bool EvaluateLockerDressingEligibility(
            EmployeeComponent employee,
            out Room selectedCommonRoom)
        {
            selectedCommonRoom = null;

            if (employee == null ||
                employee.m_state == null ||
                employee.m_entity == null ||
                MapScriptInterface.Instance == null ||
                Database.Instance == null)
            {
                return false;
            }

            if (employee.m_state.m_commuteTime > 0.25f)
            {
                return false;
            }

            PerkComponent perkComponent =
                employee.m_entity.GetComponent<PerkComponent>();
            if (perkComponent != null &&
                perkComponent.m_perkSet != null &&
                perkComponent.m_perkSet.HasPerk("PERK_LONG_COMMUTE"))
            {
                return false;
            }

            if (WorldEventManager.Instance != null &&
                WorldEventManager.Instance.HasEventForcingBiohazardClothes())
            {
                return false;
            }

            if (employee.m_entity.GetComponent<BehaviorDoctor>() == null &&
                employee.m_entity.GetComponent<BehaviorNurse>() == null &&
                employee.m_entity.GetComponent<BehaviorLabSpecialist>() == null)
            {
                return false;
            }

            Behavior behavior =
                employee.m_entity.GetComponent<Behavior>();
            WalkComponent walk =
                employee.m_entity.GetComponent<WalkComponent>();
            if (behavior == null ||
                walk == null ||
                walk.Floor == null)
            {
                return false;
            }

            Vector2i workplaceTarget;
            int workplaceFloor;
            if (!WorkplaceTravelEstimator.TryGetWorkplaceTarget(
                    employee,
                    out workplaceTarget,
                    out workplaceFloor))
            {
                return false;
            }

            Department department = behavior.GetDepartment();
            GameDBRoomType commonRoomType =
                Database.Instance.GetEntry<GameDBRoomType>(
                    "ROOM_TYPE_COMMON_ROOM");
            if (department == null || commonRoomType == null)
            {
                return false;
            }

            List<Room> departmentCommonRooms =
                MapScriptInterface.Instance.FindValidRoomsWithType(
                    commonRoomType,
                    department);
            if (departmentCommonRooms == null)
            {
                departmentCommonRooms = new List<Room>();
            }

            int bestDistanceSquared = int.MaxValue;
            for (int i = 0; i < departmentCommonRooms.Count; i++)
            {
                Room commonRoom = departmentCommonRooms[i];
                if (commonRoom == null ||
                    commonRoom.GetFloorIndex() != workplaceFloor)
                {
                    continue;
                }

                Vector2i candidatePosition =
                    MapScriptInterface.Instance.GetRandomFreePosition(
                        commonRoom,
                        behavior.GetAccessRights());
                if (candidatePosition == Vector2i.ZERO_VECTOR)
                {
                    continue;
                }

                int deltaX =
                    candidatePosition.m_x - workplaceTarget.m_x;
                int deltaY =
                    candidatePosition.m_y - workplaceTarget.m_y;
                int distanceSquared =
                    deltaX * deltaX + deltaY * deltaY;

                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    selectedCommonRoom = commonRoom;
                }
            }

            if (selectedCommonRoom == null)
            {
                List<Room> fallbackCommonRooms =
                    GetHospitalCommonRooms(
                        commonRoomType,
                        departmentCommonRooms);
                float bestRouteScore = float.MaxValue;
                int currentFloor = walk.GetFloorIndex();
                Vector2i currentTile = walk.GetCurrentTile();

                for (int i = 0; i < fallbackCommonRooms.Count; i++)
                {
                    Room commonRoom = fallbackCommonRooms[i];
                    if (commonRoom == null)
                    {
                        continue;
                    }

                    int candidateFloor = commonRoom.GetFloorIndex();
                    Vector2i candidatePosition =
                        MapScriptInterface.Instance.GetRandomFreePosition(
                            commonRoom,
                            behavior.GetAccessRights());
                    if (candidatePosition == Vector2i.ZERO_VECTOR)
                    {
                        continue;
                    }

                    float arrivalDistance =
                        GridMap.GetInstance().GetDistance(
                            currentFloor,
                            currentTile,
                            candidateFloor,
                            candidatePosition,
                            behavior.GetAccessRights());
                    if (arrivalDistance < 0f ||
                        arrivalDistance >= float.MaxValue)
                    {
                        continue;
                    }

                    float workplaceDistance =
                        GridMap.GetInstance().GetDistance(
                            candidateFloor,
                            candidatePosition,
                            workplaceFloor,
                            workplaceTarget,
                            behavior.GetAccessRights());
                    if (workplaceDistance < 0f ||
                        workplaceDistance >= float.MaxValue)
                    {
                        continue;
                    }

                    float routeScore =
                        arrivalDistance + workplaceDistance;
                    if (routeScore < bestRouteScore)
                    {
                        bestRouteScore = routeScore;
                        selectedCommonRoom = commonRoom;
                    }
                }
            }

            return selectedCommonRoom != null &&
                   PreShiftLockerInteractionTest.RoomHasUsableLocker(
                       selectedCommonRoom) &&
                   PreShiftLockerInteractionTest.ShouldUseArrivalDressing(
                       employee);
        }

        private static int GetPlanStamp(EmployeeComponent employee)
        {
            int day =
                DayTime.Instance != null
                    ? DayTime.Instance.GetDay()
                    : 0;
            int shift =
                employee != null && employee.m_state != null
                    ? (int)employee.m_state.m_shift
                    : 0;
            return day * 4 + shift;
        }

        private static List<Room> GetHospitalCommonRooms(GameDBRoomType commonRoomType, List<Room> preferredRooms)
        {
            List<Room> rooms = new List<Room>();
            AddUniqueRooms(rooms, preferredRooms);

            if (commonRoomType == null || Hospital.Instance == null || Hospital.Instance.m_departments == null)
            {
                return rooms;
            }

            for (int i = 0; i < Hospital.Instance.m_departments.Count; i++)
            {
                Department hospitalDepartment = Hospital.Instance.m_departments[i];
                if (hospitalDepartment == null)
                {
                    continue;
                }

                List<Room> departmentRooms = MapScriptInterface.Instance.FindValidRoomsWithType(
                    commonRoomType,
                    hospitalDepartment);
                AddUniqueRooms(rooms, departmentRooms);
            }

            return rooms;
        }

        private static void AddUniqueRooms(List<Room> target, List<Room> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                Room room = source[i];
                if (room != null && !target.Contains(room))
                {
                    target.Add(room);
                }
            }
        }
    }
}
