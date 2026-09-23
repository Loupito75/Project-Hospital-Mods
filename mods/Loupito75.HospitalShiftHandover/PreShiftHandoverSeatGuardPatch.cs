using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalShiftHandover
{
    // PreShiftCoordinator normally keeps the incoming employee staged while the
    // opposite-shift owner is still active. A departing employee can temporarily leave
    // GetWorkChair().User pointing at themselves after switching to GoingHome, however.
    // Treat that residual User value as occupied only while the departing employee is
    // still physically sitting on the chair; never clear the game's User field here.
    //
    // Clinic doctors need one additional guard: a consultation can remain clinically
    // active after the desk/chair has been released. In that case the native patient
    // reservation and the doctor's physical room remain authoritative until the
    // procedure releases the doctor.
    [HarmonyPatch(typeof(PreShiftCoordinator), "TryGetActiveOppositeWorkspaceOwner")]
    internal static class PreShiftCoordinatorStaleChairGuardPatch
    {
        private static readonly HashSet<EmployeeComponent> ClinicalRoomGuardLogged =
            new HashSet<EmployeeComponent>();

        internal static void Shutdown()
        {
            ClinicalRoomGuardLogged.Clear();
        }

        private static void Postfix(
            EmployeeComponent employee,
            ref Entity oppositeEmployee,
            ref bool __result)
        {
            if (employee == null || employee.m_state == null)
            {
                return;
            }

            // The coordinator's normal fallback relies on the outgoing doctor's workdesk.
            // If that desk has already been released while the doctor is still physically
            // examining a patient in the consultation room, recover the clinical owner from
            // the native patient reservation/state and keep the incoming doctor staged.
            if (!__result)
            {
                Entity busyDoctor;
                if (TryGetBusyOppositeDoctorPhysicallyInWorkplaceRoom(employee, out busyDoctor))
                {
                    oppositeEmployee = busyDoctor;
                    __result = true;
                    LogClinicalRoomGuardOnce(employee, busyDoctor);
                }
                else
                {
                    ClinicalRoomGuardLogged.Remove(employee);
                }
                return;
            }

            ClinicalRoomGuardLogged.Remove(employee);

            if (oppositeEmployee == null || !IsFinishedOrLeaving(oppositeEmployee))
            {
                return;
            }

            // A doctor with a live patient or a live native patient reservation remains
            // authoritative even if the chair is currently empty.
            BehaviorDoctor doctor = oppositeEmployee.GetComponent<BehaviorDoctor>();
            EmployeeComponent oppositeComponent = oppositeEmployee.GetComponent<EmployeeComponent>();
            if (doctor != null && HasActiveClinicalReservation(doctor, oppositeComponent))
            {
                return;
            }

            TileObject oppositeChair = oppositeComponent != null ? oppositeComponent.GetWorkChair() : null;
            if (oppositeChair != null && oppositeChair.User == oppositeEmployee)
            {
                WalkComponent oppositeWalk = oppositeEmployee.GetComponent<WalkComponent>();
                if (oppositeWalk == null || oppositeWalk.IsSittingOn(oppositeChair))
                {
                    return;
                }
            }

            // The opposite employee is already leaving and is not physically occupying
            // the work chair. Let the coordinator perform its normal RELEASE + native
            // GoToWorkplace dispatch without mutating the stale chair reservation.
            oppositeEmployee = null;
            __result = false;
        }

        private static bool TryGetBusyOppositeDoctorPhysicallyInWorkplaceRoom(
            EmployeeComponent incomingEmployee,
            out Entity oppositeDoctor)
        {
            oppositeDoctor = null;

            if (incomingEmployee == null || incomingEmployee.m_state == null ||
                incomingEmployee.m_entity == null ||
                incomingEmployee.m_entity.GetComponent<BehaviorDoctor>() == null ||
                incomingEmployee.m_state.m_workDesk == null ||
                Hospital.Instance == null || Hospital.Instance.m_characters == null)
            {
                return false;
            }

            TileObject incomingDesk = incomingEmployee.m_state.m_workDesk.GetEntity();
            if (incomingDesk == null || incomingDesk.m_state == null)
            {
                return false;
            }

            Room workplaceRoom = MapScriptInterface.Instance.GetRoomAt(
                incomingDesk.m_state.m_position,
                incomingDesk.GetFloorIndex());
            if (workplaceRoom == null || workplaceRoom.m_roomPersistentData == null ||
                workplaceRoom.m_roomPersistentData.m_roomType.Entry == null ||
                workplaceRoom.m_roomPersistentData.m_roomType.Entry.AccessRights != AccessRights.PATIENT_PROCEDURE)
            {
                return false;
            }

            foreach (Entity character in Hospital.Instance.m_characters)
            {
                if (character == null || character == incomingEmployee.m_entity)
                {
                    continue;
                }

                BehaviorDoctor doctor = character.GetComponent<BehaviorDoctor>();
                EmployeeComponent employee = character.GetComponent<EmployeeComponent>();
                WalkComponent walk = character.GetComponent<WalkComponent>();
                if (doctor == null || doctor.m_state == null ||
                    employee == null || employee.m_state == null || employee.IsFired() ||
                    employee.m_state.m_shift == incomingEmployee.m_state.m_shift ||
                    walk == null || !HasActiveClinicalReservation(doctor, employee))
                {
                    continue;
                }

                Room currentRoom = MapScriptInterface.Instance.GetRoomAt(walk);
                if (currentRoom != workplaceRoom)
                {
                    continue;
                }

                oppositeDoctor = character;
                return true;
            }

            return false;
        }

        private static bool HasActiveClinicalReservation(
            BehaviorDoctor doctor,
            EmployeeComponent employee)
        {
            if (doctor == null || doctor.m_state == null || employee == null || employee.m_state == null)
            {
                return false;
            }

            if (doctor.CurrentPatient != null)
            {
                return true;
            }

            if (employee.m_state.m_reservedByPatient != null &&
                employee.m_state.m_reservedByPatient.GetEntity() != null)
            {
                return true;
            }

            DoctorState state = doctor.m_state.m_doctorState;
            return state == DoctorState.OverridenByProcedureScript ||
                   state == DoctorState.OverridenReservedForProcedure;
        }

        private static void LogClinicalRoomGuardOnce(EmployeeComponent incomingEmployee, Entity outgoingDoctor)
        {
            if (Plugin.Log == null || incomingEmployee == null || outgoingDoctor == null ||
                !ClinicalRoomGuardLogged.Add(incomingEmployee))
            {
                return;
            }

            BehaviorDoctor doctor = outgoingDoctor.GetComponent<BehaviorDoctor>();
            EmployeeComponent employee = outgoingDoctor.GetComponent<EmployeeComponent>();
            bool hasCurrentPatient = doctor != null && doctor.CurrentPatient != null;
            bool hasPatientReservation = employee != null && employee.m_state != null &&
                                         employee.m_state.m_reservedByPatient != null &&
                                         employee.m_state.m_reservedByPatient.GetEntity() != null;
            string doctorState = doctor != null && doctor.m_state != null
                ? doctor.m_state.m_doctorState.ToString()
                : "unknown";

            Plugin.Log.LogInfo(
                "[SHIFT] WORKPLACE_HANDOVER_CLINICAL_HOLD" +
                " | incoming=" + GetCharacterName(incomingEmployee.m_entity) +
                " | outgoing=" + GetCharacterName(outgoingDoctor) +
                " | currentPatient=" + (hasCurrentPatient ? "yes" : "no") +
                " | reservedByPatient=" + (hasPatientReservation ? "yes" : "no") +
                " | state=" + doctorState);
        }

        private static string GetCharacterName(Entity entity)
        {
            if (entity == null || string.IsNullOrEmpty(entity.Name))
            {
                return "unknown";
            }

            return entity.Name.Trim();
        }

        private static bool IsFinishedOrLeaving(Entity employee)
        {
            BehaviorDoctor doctor = employee.GetComponent<BehaviorDoctor>();
            if (doctor != null && doctor.m_state != null)
            {
                DoctorState state = doctor.m_state.m_doctorState;
                return state == DoctorState.GoingHome || state == DoctorState.AtHome ||
                       state == DoctorState.Commuting || state == DoctorState.FiredAtHome;
            }

            BehaviorNurse nurse = employee.GetComponent<BehaviorNurse>();
            if (nurse != null && nurse.m_state != null)
            {
                NurseState state = nurse.m_state.m_nurseState;
                return state == NurseState.GoingHome || state == NurseState.AtHome ||
                       state == NurseState.Commuting || state == NurseState.FiredAtHome;
            }

            BehaviorLabSpecialist lab = employee.GetComponent<BehaviorLabSpecialist>();
            if (lab != null && lab.m_state != null)
            {
                LabSpecialistState state = lab.m_state.m_labSpecialistState;
                return state == LabSpecialistState.GoingHome || state == LabSpecialistState.AtHome ||
                       state == LabSpecialistState.Commuting || state == LabSpecialistState.FiredAtHome;
            }

            return false;
        }
    }
}
