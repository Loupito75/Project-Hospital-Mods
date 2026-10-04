using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    internal static class PorterStationRegistry
    {
        private const int FloorDistanceFactor = 100;

        private static readonly MethodInfo RequiredRoomsHospitalizationSetter =
            AccessTools.PropertySetter(typeof(GameDBDepartment), nameof(GameDBDepartment.RequiredRoomsHospitalization));

        internal static void Register(Database database)
        {
            if (database == null)
            {
                return;
            }

            GameDBRoomType porterStation = database.GetEntry<GameDBRoomType>(PorterIds.PorterStationRoom);
            if (porterStation == null)
            {
                Plugin.Log?.LogError("Porter station room type is missing from the mod database.");
                return;
            }

            if (object.ReferenceEquals(RequiredRoomsHospitalizationSetter, null))
            {
                Plugin.Log?.LogError("Could not resolve GameDBDepartment.RequiredRoomsHospitalization setter; porter station registration skipped.");
                return;
            }

            int registeredDepartments = 0;
            foreach (GameDBDepartment department in database.GetEntries<GameDBDepartment>())
            {
                if (department == null || department.NoHospitalization || department.RequiredRoomsHospitalization == null)
                {
                    continue;
                }

                bool alreadyRegistered = false;
                foreach (GameDBDepartmentRoomRequirement requirement in department.RequiredRoomsHospitalization)
                {
                    if (requirement != null &&
                        requirement.RoomDatabaseEntryRef != null &&
                        requirement.RoomDatabaseEntryRef.Entry == porterStation)
                    {
                        alreadyRegistered = true;
                        break;
                    }
                }

                if (alreadyRegistered)
                {
                    continue;
                }

                GameDBDepartmentRoomRequirement porterRequirement = new GameDBDepartmentRoomRequirement
                {
                    RoomDatabaseEntryRef = new DatabaseEntryRef<GameDBRoomType>(porterStation),
                    MinCount = 0,
                    MaxCount = 2
                };

                GameDBDepartmentRoomRequirement[] current = department.RequiredRoomsHospitalization;
                GameDBDepartmentRoomRequirement[] updated = new GameDBDepartmentRoomRequirement[current.Length + 1];
                Array.Copy(current, updated, current.Length);
                updated[current.Length] = porterRequirement;
                RequiredRoomsHospitalizationSetter.Invoke(department, new object[] { updated });
                registeredDepartments++;
            }

            PorterDiagnostics.Log($"Porter station registered as an optional hospitalization room for {registeredDepartments} departments.");
        }

        internal static bool HasValidStation(Department department)
        {
            if (department == null)
            {
                return false;
            }

            GameDBRoomType porterStation = Database.Instance.GetEntry<GameDBRoomType>(PorterIds.PorterStationRoom);
            if (porterStation == null)
            {
                return false;
            }

            foreach (EntityIDPointer<Room> pointer in department.m_departmentPersistentData.m_rooms)
            {
                Room room = pointer.GetEntity();
                if (room == null || room.m_roomPersistentData.m_roomType.Entry != porterStation)
                {
                    continue;
                }

                RoomValidity validity = room.m_roomPersistentData.m_valid;
                if (validity == RoomValidity.OK || validity == RoomValidity.MISSING_STAFF)
                {
                    return true;
                }
            }

            return false;
        }

        internal static TileObject FindClosestFreeLocker(
            Vector2i position,
            int floorIndex,
            Department department,
            Shift shift,
            Entity porter)
        {
            if (department == null || MapScriptInterface.Instance == null)
            {
                return null;
            }

            Shift effectiveShift = shift;
            if (PorterHiringState.HiringPorter && HiringManager.Instance != null)
            {
                effectiveShift = HiringManager.Instance.m_shift;
                TileObject requestedLocker = HiringManager.Instance.m_workspace.GetEntity();
                if (IsAvailablePorterLocker(requestedLocker, department, effectiveShift, porter, out Room _))
                {
                    return requestedLocker;
                }
            }

            TileObject best = null;
            int bestDistance = int.MaxValue;

            foreach (EntityIDPointer<TileObject> pointer in department.m_departmentPersistentData.m_objects)
            {
                TileObject locker = pointer.GetEntity();
                if (!IsAvailablePorterLocker(locker, department, effectiveShift, porter, out Room _))
                {
                    continue;
                }

                int floorDistance = Math.Abs(floorIndex - locker.GetFloorIndex()) * FloorDistanceFactor;
                int distance = (locker.m_state.m_position - position).LengthSquared() + floorDistance * floorDistance;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = locker;
                }
            }

            return best;
        }

        internal static bool IsPorterStation(GameDBRoomType roomType)
        {
            if (roomType == null)
            {
                return false;
            }

            GameDBRoomType porterStation = Database.Instance.GetEntry<GameDBRoomType>(PorterIds.PorterStationRoom);
            return porterStation != null && roomType == porterStation;
        }

        internal static bool IsPorterLocker(TileObject locker, Department department, out Room room)
        {
            room = null;
            if (locker == null || department == null || !locker.HasTag("ui_locker") || MapScriptInterface.Instance == null)
            {
                return false;
            }

            room = MapScriptInterface.Instance.GetRoomAt(locker.m_state.m_position, locker.GetFloorIndex());
            return room != null &&
                room.m_roomPersistentData.m_department.GetEntity() == department &&
                IsPorterStation(room.m_roomPersistentData.m_roomType.Entry) &&
                room.GetEquipmentOk();
        }

        private static bool IsAvailablePorterLocker(
            TileObject locker,
            Department department,
            Shift shift,
            Entity porter,
            out Room room)
        {
            if (!IsPorterLocker(locker, department, out room))
            {
                return false;
            }

            Entity owner = locker.GetWorkspaceOwner(shift);
            return owner == null || owner == porter;
        }

    }

    [HarmonyPatch(typeof(BehaviorNurse), nameof(BehaviorNurse.GetWorkspaceRoomTag))]
    internal static class PorterWorkspaceRoomTagPatch
    {
        private static readonly FieldInfo EntityField = AccessTools.Field(typeof(BehaviorNurse), "m_entity");

        private static void Postfix(BehaviorNurse __instance, ref string __result)
        {
            Entity entity = EntityField?.GetValue(__instance) as Entity;
            if (!PorterIdentity.IsPorter(entity))
            {
                return;
            }

            __result = PorterIds.PorterWorkspaceTag;
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindClosestWorkspace),
        new Type[]
        {
            typeof(Vector2i), typeof(int), typeof(Department), typeof(string),
            typeof(Shift), typeof(EmployeeComponent)
        })]
    internal static class PorterFindClosestWorkspacePatch
    {
        private static readonly FieldInfo EntityField = AccessTools.Field(typeof(EmployeeComponent), "m_entity");

        private static bool Prefix(
            Vector2i position,
            int floorIndex,
            Department department,
            string roomTag,
            Shift shift,
            EmployeeComponent employeeComponent,
            ref TileObject __result)
        {
            Entity entity = EntityField?.GetValue(employeeComponent) as Entity;
            if (!PorterIdentity.IsPorter(entity) || roomTag != PorterIds.PorterWorkspaceTag)
            {
                return true;
            }

            __result = PorterStationRegistry.FindClosestFreeLocker(
                position,
                floorIndex,
                department,
                shift,
                entity);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(EmployeeComponent),
        nameof(EmployeeComponent.HasWorkspaceForRole),
        new Type[] { typeof(GameDBEmployeeRole) })]
    internal static class PorterRoleWorkspacePatch
    {
        private static readonly FieldInfo EntityField = AccessTools.Field(typeof(EmployeeComponent), "m_entity");

        private static bool Prefix(
            EmployeeComponent __instance,
            GameDBEmployeeRole employeeRole,
            ref bool __result)
        {
            Entity entity = EntityField?.GetValue(__instance) as Entity;
            if (!PorterIdentity.IsPorter(entity) || !PorterIds.IsPorterRole(employeeRole))
            {
                return true;
            }

            Room homeRoom = __instance.m_state.m_homeRoom.GetEntity();
            __result = homeRoom != null &&
                PorterStationRegistry.IsPorterStation(
                    homeRoom.m_roomPersistentData.m_roomType.Entry);
            return false;
        }
    }
}
