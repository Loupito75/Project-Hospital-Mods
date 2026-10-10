using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    internal static class StatLabMeetingExitManager
    {
        private static readonly HashSet<ProcedureScript> RestrictedExitLogged =
            new HashSet<ProcedureScript>();

        private static readonly HashSet<ProcedureScript> BlockedExitLogged =
            new HashSet<ProcedureScript>();

        internal static bool TryHandleSpecialistWalking(
            ProcedureScript procedure,
            string flow)
        {
            if (procedure == null ||
                procedure.m_stateData == null ||
                procedure.m_stateData.m_procedureScene == null)
            {
                return false;
            }

            ProcedureScene scene = procedure.m_stateData.m_procedureScene;
            Entity specialist =
                scene.m_labSpecialist == null
                    ? null
                    : scene.m_labSpecialist.GetEntity();
            Entity patient =
                scene.m_patient == null
                    ? null
                    : scene.m_patient.GetEntity();

            if (specialist == null || patient == null)
            {
                return false;
            }

            WalkComponent specialistWalk =
                specialist.GetComponent<WalkComponent>();
            EmployeeComponent employee =
                specialist.GetComponent<EmployeeComponent>();
            BehaviorPatient patientBehavior =
                patient.GetComponent<BehaviorPatient>();

            if (specialistWalk == null ||
                specialistWalk.Floor == null ||
                employee == null ||
                employee.m_state == null ||
                employee.m_state.m_homeRoom == null ||
                employee.m_state.m_homeRoom.GetEntity() == null ||
                patientBehavior == null)
            {
                return false;
            }

            Room homeRoom =
                employee.m_state.m_homeRoom.GetEntity();
            Room currentRoom =
                MapScriptInterface.Instance.GetRoomAt(specialistWalk);
            Vector2i currentTile =
                specialistWalk.GetCurrentTile();
            AccessRights patientAccess =
                patientBehavior.GetAccessRights();

            bool leftHomeRoom =
                !object.ReferenceEquals(homeRoom, currentRoom);
            bool patientCanStandHere =
                NavigationAccessPolicy.IsTileAccessible(
                    specialistWalk.Floor,
                    currentTile,
                    patientAccess);

            if (leftHomeRoom && patientCanStandHere)
            {
                RestrictedExitLogged.Remove(procedure);
                BlockedExitLogged.Remove(procedure);

                if (TrafficControlConfig.PathfindingDebug)
                {
                    Plugin.Log?.LogInfo(
                        "[PathDebug] STAT_LAB_MEETING_EXIT flow=" + flow +
                        " patient='" + (patient.Name ?? string.Empty).Trim() +
                        "' specialist='" + (specialist.Name ?? string.Empty).Trim() +
                        "' tile=" + currentTile +
                        " floor=" + specialistWalk.GetFloorIndex() +
                        " patientAccess=" + patientAccess +
                        "(" + (int)patientAccess + ")" +
                        " room=" + GetRoomId(currentRoom) +
                        " roomAccess=" +
                        specialistWalk.Floor.m_roomAccessRights[
                            currentTile.m_x,
                            currentTile.m_y] +
                        " logisticsAccess=" +
                        specialistWalk.Floor.m_mapPersistentData
                            .m_mapLogisticsLayer.m_accessRights[
                                currentTile.m_x,
                                currentTile.m_y] +
                        ". Continuing vanilla procedure from first patient-accessible tile.");
                }

                procedure.SwitchState("SPECIALIST_WALKING_EXTRA_STEP");
                return true;
            }

            if (leftHomeRoom &&
                !patientCanStandHere &&
                RestrictedExitLogged.Add(procedure) &&
                TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[PathDebug] STAT_LAB_EXIT_CONTINUE flow=" + flow +
                    " patient='" + (patient.Name ?? string.Empty).Trim() +
                    "' specialist='" + (specialist.Name ?? string.Empty).Trim() +
                    "' tile=" + currentTile +
                    " floor=" + specialistWalk.GetFloorIndex() +
                    " patientAccess=" + patientAccess +
                    "(" + (int)patientAccess + ")" +
                    " room=" + GetRoomId(currentRoom) +
                    " roomAccess=" +
                    specialistWalk.Floor.m_roomAccessRights[
                        currentTile.m_x,
                        currentTile.m_y] +
                    " logisticsAccess=" +
                    specialistWalk.Floor.m_mapPersistentData
                        .m_mapLogisticsLayer.m_accessRights[
                            currentTile.m_x,
                            currentTile.m_y] +
                    ". Specialist left home room but is still in a tile forbidden to the patient; keeping the vanilla walk toward the patient.");
            }

            if (!specialistWalk.IsBusy() &&
                !patientCanStandHere &&
                BlockedExitLogged.Add(procedure) &&
                TrafficControlConfig.PathfindingDebug)
            {
                Plugin.Log?.LogInfo(
                    "[PathDebug] STAT_LAB_EXIT_BLOCKED flow=" + flow +
                    " patient='" + (patient.Name ?? string.Empty).Trim() +
                    "' specialist='" + (specialist.Name ?? string.Empty).Trim() +
                    "' tile=" + currentTile +
                    " floor=" + specialistWalk.GetFloorIndex() +
                    " walkState=" + specialistWalk.m_state.m_walkState +
                    " patientAccess=" + patientAccess +
                    "(" + (int)patientAccess + ")" +
                    ". Specialist stopped before reaching a patient-accessible tile; procedure remains in SPECIALIST_WALKING.");
            }

            // The vanilla destination remains the patient's current position.
            // Suppress vanilla's early transition while the specialist is still in
            // another restricted room, or if the walk ended before reaching an
            // actually patient-accessible tile.
            return true;
        }

        internal static void Reset()
        {
            RestrictedExitLogged.Clear();
            BlockedExitLogged.Clear();
        }

        private static string GetRoomId(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null ||
                room.m_roomPersistentData.m_roomType.Entry == null)
            {
                return "<none>";
            }

            return room.m_roomPersistentData.m_roomType.Entry.DatabaseID.ToString();
        }
    }

    [HarmonyPatch(
        typeof(ProcedureScriptStatLabResult),
        "UpdateStateSpecialistWalking")]
    internal static class StatLabResultMeetingExitPatch
    {
        private static bool Prefix(
            ProcedureScriptStatLabResult __instance)
        {
            return !StatLabMeetingExitManager.TryHandleSpecialistWalking(
                __instance,
                "RESULT");
        }
    }

    [HarmonyPatch(
        typeof(ProcedureScriptStatLabSample),
        "UpdateStateSpecialistWalking")]
    internal static class StatLabSampleMeetingExitPatch
    {
        private static bool Prefix(
            ProcedureScriptStatLabSample __instance)
        {
            return !StatLabMeetingExitManager.TryHandleSpecialistWalking(
                __instance,
                "SAMPLE");
        }
    }
}
