using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class ShiftChangeMaintenancePatch
    {
        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo departmentShiftStart = AccessTools.Method(
                typeof(Department),
                "OnShiftStart",
                new Type[]
                {
                    typeof(EntityIDPointer<Entity>),
                    typeof(Shift)
                });
            MethodInfo departmentPrefix = AccessTools.Method(
                typeof(ShiftChangeMaintenancePatch),
                "DepartmentShiftStartPrefix");

            MethodInfo reportRoomUsage = AccessTools.Method(
                typeof(Hospital),
                "ReportRoomUsage",
                new Type[] { typeof(Shift) });
            MethodInfo reportPrefix = AccessTools.Method(
                typeof(ShiftChangeMaintenancePatch),
                "ReportRoomUsagePrefix");

            if (departmentShiftStart == null)
            {
                throw new MissingMethodException(
                    typeof(Department).FullName,
                    "OnShiftStart(EntityIDPointer<Entity>, Shift)");
            }
            if (departmentPrefix == null)
            {
                throw new MissingMethodException(
                    typeof(ShiftChangeMaintenancePatch).FullName,
                    "DepartmentShiftStartPrefix");
            }
            if (reportRoomUsage == null)
            {
                throw new MissingMethodException(
                    typeof(Hospital).FullName,
                    "ReportRoomUsage(Shift)");
            }
            if (reportPrefix == null)
            {
                throw new MissingMethodException(
                    typeof(ShiftChangeMaintenancePatch).FullName,
                    "ReportRoomUsagePrefix");
            }

            harmony.Patch(
                departmentShiftStart,
                prefix: new HarmonyMethod(departmentPrefix));
            harmony.Patch(
                reportRoomUsage,
                prefix: new HarmonyMethod(reportPrefix));

            Plugin.Log.LogInfo(
                "OptimizeShiftChangeMaintenance installed: shift mood reset lookups and room workload reporting are deduplicated.");
        }

        private static bool DepartmentShiftStartPrefix(
            EntityIDPointer<Entity> character,
            Shift shift)
        {
            Entity entity = character.GetEntity();
            EmployeeComponent employee =
                entity.GetComponent<EmployeeComponent>();

            if (employee.m_state.m_shift == shift)
            {
                MoodComponent mood =
                    entity.GetComponent<MoodComponent>();
                mood.Reset();
                mood.ResetSatisfaction();
                mood.UpdateTotalSatisfaction();
            }

            return false;
        }

        private static bool ReportRoomUsagePrefix(
            Hospital __instance,
            Shift shift)
        {
            int noneCount = 0;
            int criticalCount = 0;

            foreach (Department department in __instance.m_departments)
            {
                foreach (EntityIDPointer<Room> roomPointer in
                    department.m_departmentPersistentData.m_rooms)
                {
                    Room room = roomPointer.GetEntity();
                    if (room == null ||
                        !room.ShowsWorkloadStatistics() ||
                        (room.m_roomPersistentData.m_valid &
                            RoomValidity.OUTDOORS) > (RoomValidity)0)
                    {
                        continue;
                    }

                    float usage;
                    switch (shift)
                    {
                        case Shift.DAY:
                            usage = room.GetWorkloadDayPercent();
                            break;
                        case Shift.NIGHT:
                            usage = room.GetWorkloadNightPercent();
                            break;
                        default:
                            continue;
                    }

                    WorkloadLevel level =
                        room.GetWorkloadLevel(usage);
                    if (level == WorkloadLevel.NONE)
                    {
                        noneCount++;
                    }
                    else if (level == WorkloadLevel.CRITICAL)
                    {
                        criticalCount++;
                    }
                }
            }

            if (shift == Shift.DAY)
            {
                NotificationManager.GetInstance().AddMessage(
                    null,
                    "NOTIF_ROOM_USAGE_STATS_DAY",
                    noneCount.ToString(),
                    criticalCount.ToString(),
                    string.Empty,
                    103);
            }
            else
            {
                NotificationManager.GetInstance().AddMessage(
                    null,
                    "NOTIF_ROOM_USAGE_STATS_NIGHT",
                    noneCount.ToString(),
                    criticalCount.ToString(),
                    string.Empty,
                    104);
            }

            return false;
        }
    }
}
