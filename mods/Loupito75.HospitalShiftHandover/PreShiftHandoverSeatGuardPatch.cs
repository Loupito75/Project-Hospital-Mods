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
    // Clinic doctors need two additional guards. A consultation can remain clinically
    // active after the desk/chair has been released, and a finished consultation can
    // release its logical reservation before the outgoing doctor and last procedure
    // patient have physically left a doctors office. Native clinical state stays
    // authoritative first; physical occupancy only extends the handover afterward.
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
                Entity physicalOutgoingDoctor;
                if (TryGetFinishedOppositeDoctorStillPhysicallyOccupyingOffice(
                        employee,
                        out physicalOutgoingDoctor))
                {
                    oppositeEmployee = physicalOutgoingDoctor;
                    __result = true;
                    return;
                }

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

            if (ShouldHoldFinishedDoctorOfficeForPhysicalOccupancy(employee, oppositeEmployee))
            {
                return;
            }

            // The opposite employee is already leaving, has no live clinical reservation,
            // is not physically occupying the work chair, and neither the outgoing doctor
            // nor their last procedure patient remains inside the protected doctors office.
            // Let the coordinator perform its normal RELEASE + native GoToWorkplace dispatch.
            oppositeEmployee = null;
            __result = false;
        }

        private static bool TryGetFinishedOppositeDoctorStillPhysicallyOccupyingOffice(
            EmployeeComponent incomingEmployee,
            out Entity outgoingDoctor)
        {
            outgoingDoctor = null;

            if (incomingEmployee == null || incomingEmployee.m_state == null ||
                incomingEmployee.m_entity == null ||
                incomingEmployee.m_entity.GetComponent<BehaviorDoctor>() == null ||
                incomingEmployee.m_state.m_workDesk == null)
            {
                return false;
            }

            TileObject workDesk = incomingEmployee.m_state.m_workDesk.GetEntity();
            if (workDesk == null)
            {
                return false;
            }

            Shift oppositeShift =
                incomingEmployee.m_state.m_shift == Shift.DAY ? Shift.NIGHT : Shift.DAY;
            Entity candidate = workDesk.GetWorkspaceOwner(oppositeShift);
            if (!ShouldHoldFinishedDoctorOfficeForPhysicalOccupancy(
                    incomingEmployee,
                    candidate))
            {
                return false;
            }

            outgoingDoctor = candidate;
            return true;
        }

        private static bool ShouldHoldFinishedDoctorOfficeForPhysicalOccupancy(
            EmployeeComponent incomingEmployee,
            Entity outgoingDoctor)
        {
            if (incomingEmployee == null || incomingEmployee.m_state == null ||
                outgoingDoctor == null || outgoingDoctor == incomingEmployee.m_entity ||
                incomingEmployee.m_state.m_workDesk == null ||
                MapScriptInterface.Instance == null || Database.Instance == null)
            {
                return false;
            }

            TileObject incomingDesk = incomingEmployee.m_state.m_workDesk.GetEntity();
            BehaviorDoctor doctor = outgoingDoctor.GetComponent<BehaviorDoctor>();
            EmployeeComponent outgoingEmployee =
                outgoingDoctor.GetComponent<EmployeeComponent>();
            if (incomingDesk == null || incomingDesk.m_state == null ||
                doctor == null || doctor.m_state == null ||
                outgoingEmployee == null || outgoingEmployee.m_state == null ||
                outgoingEmployee.IsFired() ||
                outgoingEmployee.m_state.m_shift == incomingEmployee.m_state.m_shift ||
                outgoingEmployee.m_state.m_workDesk == null ||
                outgoingEmployee.m_state.m_workDesk.GetEntity() != incomingDesk ||
                !IsFinishedOrLeaving(outgoingDoctor) ||
                HasActiveClinicalReservation(doctor, outgoingEmployee))
            {
                return false;
            }

            Room workplaceRoom = MapScriptInterface.Instance.GetRoomAt(
                incomingDesk.m_state.m_position,
                incomingDesk.GetFloorIndex());
            GameDBRoomType doctorsOffice =
                Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_DOCTORS_OFFICE");
            if (workplaceRoom == null || workplaceRoom.m_roomPersistentData == null ||
                workplaceRoom.m_roomPersistentData.m_roomType.Entry == null ||
                doctorsOffice == null ||
                workplaceRoom.m_roomPersistentData.m_roomType.Entry != doctorsOffice)
            {
                return false;
            }

            if (IsPhysicallyInRoom(outgoingDoctor, workplaceRoom))
            {
                return true;
            }

            Entity lastProcedurePatient =
                doctor.m_state.m_lastProcedurePatient != null
                    ? doctor.m_state.m_lastProcedurePatient.GetEntity()
                    : null;
            if (lastProcedurePatient == null)
            {
                return false;
            }

            BehaviorPatient patient =
                lastProcedurePatient.GetComponent<BehaviorPatient>();
            if (patient == null || patient.m_state == null ||
                patient.m_state.m_patientState == PatientState.Left ||
                patient.m_state.m_patientState == PatientState.Dead)
            {
                return false;
            }

            return IsPhysicallyInRoom(lastProcedurePatient, workplaceRoom);
        }

        private static bool IsPhysicallyInRoom(Entity character, Room room)
        {
            if (character == null || room == null || MapScriptInterface.Instance == null)
            {
                return false;
            }

            WalkComponent walk = character.GetComponent<WalkComponent>();
            return walk != null && MapScriptInterface.Instance.GetRoomAt(walk) == room;
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
