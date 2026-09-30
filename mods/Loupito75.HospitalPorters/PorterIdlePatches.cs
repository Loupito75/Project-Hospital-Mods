using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    internal static class PorterIdleResting
    {
        private static readonly FieldInfo NurseEntityField =
            AccessTools.Field(typeof(BehaviorNurse), "m_entity");

        internal static Entity GetEntity(BehaviorNurse nurse)
        {
            if (nurse == null || object.ReferenceEquals(NurseEntityField, null))
            {
                return null;
            }

            return NurseEntityField.GetValue(nurse) as Entity;
        }

        internal static bool TryGetPorterStation(
            Entity porter,
            out EmployeeComponent employee,
            out Room station)
        {
            employee = porter == null
                ? null
                : porter.GetComponent<EmployeeComponent>();
            station = employee == null || employee.m_state.m_homeRoom == null
                ? null
                : employee.m_state.m_homeRoom.GetEntity();

            return PorterIdentity.IsPorter(porter) &&
                employee != null &&
                station != null &&
                station.m_roomPersistentData.m_roomType != null &&
                PorterStationRegistry.IsPorterStationOrLegacyNursesStation(
                    station.m_roomPersistentData.m_roomType.Entry) &&
                station.GetEquipmentOk();
        }

        internal static bool IsRestingAtValidIdlePlace(
            Entity porter,
            EmployeeComponent employee,
            Room station)
        {
            WalkComponent walk = porter == null
                ? null
                : porter.GetComponent<WalkComponent>();
            TileObject currentSeat = GetCurrentSeat(walk);
            if (walk == null ||
                currentSeat == null ||
                currentSeat.User != porter)
            {
                return false;
            }

            Room currentRoom = MapScriptInterface.Instance.GetRoomAt(walk);
            if (currentRoom == station)
            {
                return true;
            }

            GameDBRoomType commonRoomType =
                Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_COMMON_ROOM");
            if (currentRoom == null ||
                currentRoom.m_roomPersistentData.m_roomType != commonRoomType)
            {
                return false;
            }

            // A common-room seat is only the fallback idle place. As soon as
            // a seat becomes free in the Porter's own station, return there.
            return FindFreeSeat(porter, walk, station) == null;
        }

        internal static bool TrySendToIdlePlace(
            BehaviorNurse nurse,
            Entity porter,
            EmployeeComponent employee,
            Room station)
        {
            if (nurse == null || porter == null || employee == null || station == null ||
                PorterSampleTransportRuntime.IsBusy(porter))
            {
                return false;
            }

            WalkComponent walk = porter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return false;
            }

            TileObject currentSeat = GetCurrentSeat(walk);
            Room currentRoom = MapScriptInterface.Instance.GetRoomAt(walk);

            if (currentRoom == station &&
                TryReserveCurrentSeat(porter, currentSeat))
            {
                nurse.SwitchState(NurseState.Idle);
                return true;
            }

            TileObject seat = FindFreeSeat(porter, walk, station);
            if (seat != null)
            {
                walk.GoSit(seat);
                nurse.SwitchState(NurseState.GoingToWorkplace);
                return true;
            }

            GameDBRoomType commonRoomType =
                Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_COMMON_ROOM");

            // Keep the exact common-room seat the Porter is already using when
            // the station is full, even if the department has several break rooms.
            if (currentRoom != null &&
                commonRoomType != null &&
                currentRoom.m_roomPersistentData.m_roomType == commonRoomType &&
                TryReserveCurrentSeat(porter, currentSeat))
            {
                nurse.SwitchState(NurseState.Idle);
                return true;
            }

            Room commonRoom = commonRoomType == null
                ? null
                : MapScriptInterface.Instance.FindValidRoomWithType(
                    commonRoomType,
                    employee.m_state.m_department.GetEntity());

            if (commonRoom != null)
            {
                seat = FindFreeSeat(porter, walk, commonRoom);
                if (seat != null)
                {
                    walk.GoSit(seat);
                    nurse.SwitchState(NurseState.GoingToWorkplace);
                    return true;
                }

                Vector2i fallbackPosition =
                    MapScriptInterface.Instance.GetRandomFreePosition(
                        commonRoom,
                        AccessRights.STAFF);
                if (fallbackPosition != Vector2i.ZERO_VECTOR)
                {
                    walk.SetDestination(fallbackPosition, commonRoom.GetFloorIndex());
                    nurse.SwitchState(NurseState.GoingToWorkplace);
                    return true;
                }
            }

            Vector2i stationPosition =
                MapScriptInterface.Instance.GetRandomFreePosition(
                    station,
                    AccessRights.STAFF);
            if (stationPosition != Vector2i.ZERO_VECTOR)
            {
                walk.SetDestination(stationPosition, station.GetFloorIndex());
                nurse.SwitchState(NurseState.GoingToWorkplace);
                return true;
            }

            return false;
        }

        internal static TileObject GetCurrentSeat(
            WalkComponent walk)
        {
            return walk == null ||
                walk.m_state == null ||
                walk.m_state.m_objectSittingOn == null
                    ? null
                    : walk.m_state.m_objectSittingOn.GetEntity();
        }

        private static bool TryReserveCurrentSeat(
            Entity porter,
            TileObject seat)
        {
            if (porter == null ||
                seat == null ||
                seat.Owner != null ||
                (seat.User != null && seat.User != porter))
            {
                return false;
            }

            seat.User = porter;
            return true;
        }

        private static TileObject FindFreeSeat(
            Entity porter,
            WalkComponent walk,
            Room room)
        {
            if (porter == null || walk == null || room == null)
            {
                return null;
            }

            TileObject seat = MapScriptInterface.Instance.FindClosestFreeObjectWithTag(
                porter,
                null,
                walk.GetCurrentTile(),
                room,
                "office_chair",
                AccessRights.STAFF);
            if (seat != null)
            {
                return seat;
            }

            return MapScriptInterface.Instance.FindClosestFreeObjectWithTag(
                porter,
                null,
                walk.GetCurrentTile(),
                room,
                "sitting",
                AccessRights.STAFF);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.IsAtWorkplace),
        new Type[] { typeof(EmployeeComponent) })]
    internal static class PorterIdleIsAtWorkplacePatch
    {
        private static bool Prefix(
            BehaviorNurse __instance,
            EmployeeComponent employeeComponent,
            ref bool __result)
        {
            Entity porter = PorterIdleResting.GetEntity(__instance);
            EmployeeComponent employee;
            Room station;
            if (!PorterIdleResting.TryGetPorterStation(
                    porter,
                    out employee,
                    out station))
            {
                return true;
            }

            __result = PorterIdleResting.IsRestingAtValidIdlePlace(
                porter,
                employee,
                station);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.GoToWorkplace),
        new Type[] { })]
    internal static class PorterIdleGoToWorkplacePatch
    {
        private static bool Prefix(BehaviorNurse __instance)
        {
            Entity porter = PorterIdleResting.GetEntity(__instance);
            EmployeeComponent employee;
            Room station;
            if (!PorterIdleResting.TryGetPorterStation(
                    porter,
                    out employee,
                    out station))
            {
                return true;
            }

            return !PorterIdleResting.TrySendToIdlePlace(
                __instance,
                porter,
                employee,
                station);
        }
    }

    internal static class PorterIdleWorkstationVisuals
    {
        private static readonly Dictionary<Entity, TileObject> ActivePcByPorter =
            new Dictionary<Entity, TileObject>();

        internal static void Reset()
        {
            ActivePcByPorter.Clear();
        }

        internal static void Update(Entity porter)
        {
            if (!PorterIdentity.IsPorter(porter))
            {
                return;
            }

            EmployeeComponent employee;
            Room station;
            if (!PorterIdleResting.TryGetPorterStation(
                    porter,
                    out employee,
                    out station))
            {
                Deactivate(porter);
                return;
            }

            WalkComponent walk = porter.GetComponent<WalkComponent>();
            TileObject seat = PorterIdleResting.GetCurrentSeat(walk);
            TileObject pc = null;

            if (walk != null &&
                walk.IsSitting() &&
                seat != null &&
                seat.User == porter &&
                MapScriptInterface.Instance.GetRoomAt(walk) == station)
            {
                pc = FindPcForSeat(station, seat);
            }

            TileObject previous;
            ActivePcByPorter.TryGetValue(porter, out previous);
            if (previous == pc)
            {
                return;
            }

            if (previous != null)
            {
                SetPcActive(previous, false);
                ActivePcByPorter.Remove(porter);
            }

            if (pc != null)
            {
                SetPcActive(pc, true);
                ActivePcByPorter[porter] = pc;
            }
        }

        private static void Deactivate(Entity porter)
        {
            if (porter == null)
            {
                return;
            }

            TileObject previous;
            if (!ActivePcByPorter.TryGetValue(porter, out previous))
            {
                return;
            }

            if (previous != null)
            {
                SetPcActive(previous, false);
            }
            ActivePcByPorter.Remove(porter);
        }

        private static TileObject FindPcForSeat(
            Room station,
            TileObject seat)
        {
            if (station == null ||
                seat == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return null;
            }

            int floorIndex = station.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Vector2i seatPosition = seat.m_state.m_position;

            for (int x = station.m_roomPersistentData.m_positionBottom.m_x;
                x <= station.m_roomPersistentData.m_positionTop.m_x;
                x++)
            {
                for (int y = station.m_roomPersistentData.m_positionBottom.m_y;
                    y <= station.m_roomPersistentData.m_positionTop.m_y;
                    y++)
                {
                    Vector2i position = new Vector2i(x, y);
                    if (!station.IsPositionInRoom(position))
                    {
                        continue;
                    }

                    TileObject pc = MatchPc(
                        floor.m_tileObjects[x, y].m_attachmentObject,
                        seatPosition);
                    if (pc != null)
                    {
                        return pc;
                    }

                    pc = MatchPc(
                        floor.m_tileObjects[x, y].m_centerObject,
                        seatPosition);
                    if (pc != null)
                    {
                        return pc;
                    }
                }
            }

            return null;
        }

        private static TileObject MatchPc(
            TileObject candidate,
            Vector2i seatPosition)
        {
            if (candidate == null ||
                !candidate.HasTag("pc_work") ||
                candidate.GetDefaultUseTile() != seatPosition)
            {
                return null;
            }

            return candidate;
        }

        private static void SetPcActive(
            TileObject pc,
            bool active)
        {
            if (pc == null)
            {
                return;
            }

            pc.SetLightEnabled(active);
            AnimatedObjectComponent animation =
                pc.GetComponent<AnimatedObjectComponent>();
            if (animation != null)
            {
                animation.ForceFrame(active ? 1 : 0);
            }
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.Update),
        new Type[] { typeof(float) })]
    internal static class PorterIdleWorkstationVisualPatch
    {
        private static void Postfix(BehaviorNurse __instance)
        {
            Entity porter = PorterIdleResting.GetEntity(__instance);
            if (!PorterIdentity.IsPorter(porter))
            {
                return;
            }

            PorterIdleWorkstationVisuals.Update(porter);
        }
    }

    [HarmonyPatch(
        typeof(AnimatedObjectComponent),
        nameof(AnimatedObjectComponent.ForceFrame),
        new Type[] { typeof(int) })]
    internal static class PorterLockerPassiveAnimationPatch
    {
        private static bool Prefix(
            AnimatedObjectComponent __instance,
            int frame)
        {
            if (__instance == null ||
                frame <= 0)
            {
                return true;
            }

            TileObject locker = __instance.m_entity as TileObject;
            if (locker == null ||
                !locker.HasTag("ui_locker") ||
                locker.User != null)
            {
                return true;
            }

            Entity dayOwner = locker.GetWorkspaceOwner(Shift.DAY);
            Entity nightOwner = locker.GetWorkspaceOwner(Shift.NIGHT);
            return !PorterIdentity.IsPorter(dayOwner) &&
                !PorterIdentity.IsPorter(nightOwner);
        }
    }

}
