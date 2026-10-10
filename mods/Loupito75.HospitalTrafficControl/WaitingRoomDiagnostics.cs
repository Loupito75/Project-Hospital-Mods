using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class WaitingRoomDiagnostics
    {
        private static readonly HashSet<Entity> ActiveGoToWaitingRoom =
            new HashSet<Entity>();

        internal static void Reset()
        {
            ActiveGoToWaitingRoom.Clear();
        }

        internal static void LogInvalidWaitingRoomPatients(Floor floor)
        {
            if (!TrafficControlConfig.PathfindingDebug ||
                floor == null ||
                Hospital.Instance == null)
            {
                return;
            }

            GameDBRoomType waitingType =
                Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_WAITING");
            HashSet<Entity> logged = new HashSet<Entity>();

            foreach (Department department in Hospital.Instance.m_departments)
            {
                if (department == null ||
                    department.m_departmentPersistentData == null ||
                    department.m_departmentPersistentData.m_patients == null)
                {
                    continue;
                }

                foreach (
                    EntityIDPointer<Entity> patientPointer
                    in department.m_departmentPersistentData.m_patients)
                {
                    Entity patient =
                        patientPointer == null
                            ? null
                            : patientPointer.GetEntity();
                    if (patient == null || logged.Contains(patient))
                    {
                        continue;
                    }

                    BehaviorPatient behavior =
                        patient.GetComponent<BehaviorPatient>();
                    if (behavior == null ||
                        behavior.m_state == null ||
                        behavior.m_state.m_waitingRoom == null)
                    {
                        continue;
                    }

                    Room waitingRoom =
                        behavior.m_state.m_waitingRoom.GetEntity();
                    if (waitingRoom == null ||
                        waitingRoom.GetFloorIndex() != floor.m_floorIndex ||
                        waitingRoom.m_roomPersistentData == null ||
                        waitingRoom.m_roomPersistentData.m_roomType == null ||
                        waitingRoom.m_roomPersistentData.m_roomType.Entry !=
                            waitingType ||
                        !IsInaccessibleToPatients(waitingRoom))
                    {
                        continue;
                    }

                    logged.Add(patient);
                    LogPatientSnapshot(
                        "WAITING_ROOM_INVALIDATED",
                        behavior,
                        "reason=access-change");
                }
            }
        }

        internal static void OnGoToWaitingRoomPrefix(
            BehaviorPatient behavior)
        {
            if (!TrafficControlConfig.PathfindingDebug ||
                behavior == null ||
                behavior.m_entity == null)
            {
                return;
            }

            ActiveGoToWaitingRoom.Add(behavior.m_entity);

            Room waitingRoom = GetWaitingRoom(behavior);
            if (waitingRoom == null ||
                IsInaccessibleToPatients(waitingRoom))
            {
                LogPatientSnapshot(
                    "WAITING_ROOM_GOTO",
                    behavior,
                    "phase=before");
            }
        }

        internal static void OnGoToWaitingRoomPostfix(
            BehaviorPatient behavior)
        {
            if (behavior == null || behavior.m_entity == null)
            {
                return;
            }

            if (TrafficControlConfig.PathfindingDebug)
            {
                Room waitingRoom = GetWaitingRoom(behavior);
                if (waitingRoom == null ||
                    IsInaccessibleToPatients(waitingRoom) ||
                    (behavior.m_state != null &&
                     behavior.m_state.m_patientState ==
                        PatientState.Leaving))
                {
                    LogPatientSnapshot(
                        "WAITING_ROOM_GOTO",
                        behavior,
                        "phase=after");
                }
            }

            ActiveGoToWaitingRoom.Remove(behavior.m_entity);
        }

        internal static void OnLeavePrefix(
            BehaviorPatient behavior,
            bool pay,
            bool leaveAfterHours,
            bool leavingHospitalizationPatient)
        {
            if (!TrafficControlConfig.PathfindingDebug ||
                behavior == null ||
                behavior.m_entity == null)
            {
                return;
            }

            Room waitingRoom = GetWaitingRoom(behavior);
            bool fromGoToWaitingRoom =
                ActiveGoToWaitingRoom.Contains(behavior.m_entity);

            if (!fromGoToWaitingRoom &&
                !IsInaccessibleToPatients(waitingRoom))
            {
                return;
            }

            LogPatientSnapshot(
                "WAITING_ROOM_LEAVE",
                behavior,
                "fromGoToWaitingRoom=" + fromGoToWaitingRoom +
                " pay=" + pay +
                " leaveAfterHours=" + leaveAfterHours +
                " leavingHospitalizationPatient=" +
                    leavingHospitalizationPatient);
        }

        private static Room GetWaitingRoom(BehaviorPatient behavior)
        {
            return behavior == null ||
                   behavior.m_state == null ||
                   behavior.m_state.m_waitingRoom == null
                ? null
                : behavior.m_state.m_waitingRoom.GetEntity();
        }

        private static bool IsInaccessibleToPatients(Room room)
        {
            return room != null &&
                   room.m_roomPersistentData != null &&
                   (room.m_roomPersistentData.m_valid &
                    RoomValidity.INACCESSIBLE_PATIENTS) !=
                        (RoomValidity)0;
        }

        private static void LogPatientSnapshot(
            string eventName,
            BehaviorPatient behavior,
            string details)
        {
            if (behavior == null ||
                behavior.m_entity == null ||
                behavior.m_state == null)
            {
                return;
            }

            Entity patient = behavior.m_entity;
            ProcedureComponent procedure =
                patient.GetComponent<ProcedureComponent>();
            ProcedureQueue queue =
                procedure == null ||
                procedure.m_state == null
                    ? null
                    : procedure.m_state.m_procedureQueue;
            WalkComponent walk = patient.GetComponent<WalkComponent>();
            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            Room waitingRoom = GetWaitingRoom(behavior);

            int plannedExaminations =
                queue == null ||
                queue.m_plannedExaminationStates == null
                    ? -1
                    : queue.m_plannedExaminationStates.Count;
            int labProcedures =
                queue == null ||
                queue.m_labProcedures == null
                    ? -1
                    : queue.m_labProcedures.Count;
            int plannedTreatments =
                queue == null ||
                queue.m_plannedTreatmentStates == null
                    ? -1
                    : queue.m_plannedTreatmentStates.Count;
            int activeTreatments =
                queue == null ||
                queue.m_activeTreatmentStates == null
                    ? -1
                    : queue.m_activeTreatmentStates.Count;

            string activeExamination = "<none>";
            if (queue != null &&
                queue.m_activeExamination != null &&
                queue.m_activeExamination.Entry != null)
            {
                activeExamination =
                    queue.m_activeExamination.Entry.DatabaseID.ToString();
            }

            string currentScript = "<none>";
            if (procedure != null &&
                procedure.m_state != null &&
                procedure.m_state.m_currentProcedureScript != null &&
                procedure.m_state.m_currentProcedureScript.GetEntity() != null)
            {
                currentScript =
                    procedure.m_state.m_currentProcedureScript
                        .GetEntity()
                        .GetType()
                        .Name;
            }

            string diagnosedCondition = "<none>";
            string actualCondition = "<none>";
            bool correctlyDiagnosed = false;

            if (behavior.m_state.m_medicalCondition != null)
            {
                correctlyDiagnosed =
                    behavior.m_state.m_medicalCondition.m_correctlyDiagnosed;

                if (behavior.m_state.m_medicalCondition
                        .m_diagnosedMedicalCondition != null &&
                    behavior.m_state.m_medicalCondition
                        .m_diagnosedMedicalCondition.Entry != null)
                {
                    diagnosedCondition =
                        behavior.m_state.m_medicalCondition
                            .m_diagnosedMedicalCondition.Entry.DatabaseID
                            .ToString();
                }

                if (behavior.m_state.m_medicalCondition
                        .m_gameDBMedicalCondition != null &&
                    behavior.m_state.m_medicalCondition
                        .m_gameDBMedicalCondition.Entry != null)
                {
                    actualCondition =
                        behavior.m_state.m_medicalCondition
                            .m_gameDBMedicalCondition.Entry.DatabaseID
                            .ToString();
                }
            }

            Plugin.Log?.LogInfo(
                "[PathDebug] " + eventName +
                " entity='" + patient.Name + "'" +
                " " + details +
                " patientState=" + behavior.m_state.m_patientState +
                " currentFloor=" +
                    (walk == null ? -1 : walk.GetFloorIndex()) +
                " waitingRoom=" + GetRoomLabel(waitingRoom) +
                " waitingRoomValid=" +
                    (waitingRoom == null ||
                     waitingRoom.m_roomPersistentData == null
                        ? "<none>"
                        : waitingRoom.m_roomPersistentData.m_valid.ToString()) +
                " diagnosed=" + diagnosedCondition +
                " actualCondition=" + actualCondition +
                " correctlyDiagnosed=" + correctlyDiagnosed +
                " plannedExams=" + plannedExaminations +
                " activeExam=" + activeExamination +
                " labProcedures=" + labProcedures +
                " labStates=" + GetLabStates(queue) +
                " plannedTreatments=" + plannedTreatments +
                " activeTreatments=" + activeTreatments +
                " currentScript=" + currentScript +
                " procedureBusy=" +
                    (procedure != null && procedure.IsBusy()) +
                " lastProcedure=" +
                    (procedure == null ||
                     procedure.m_state == null ||
                     string.IsNullOrEmpty(procedure.m_state.m_lastProcedureID)
                        ? "<none>"
                        : procedure.m_state.m_lastProcedureID) +
                " hospitalized=" +
                    (hospitalization != null &&
                     hospitalization.IsHospitalized()) +
                " hasBeenTreated=" + behavior.HasBeenTreated() +
                " correctlyTreated=" +
                    behavior.HasBeenCorrectlyTreated() +
                " sentHome=" + behavior.m_state.m_sentHome +
                " sentAway=" + behavior.m_state.m_sentAway);
        }

        private static string GetRoomLabel(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null ||
                room.m_roomPersistentData.m_roomType.Entry == null)
            {
                return "<none>";
            }

            return room.m_roomPersistentData.m_roomType.Entry.DatabaseID +
                   "@floor" + room.GetFloorIndex() +
                   "#" + room.GetEntityID();
        }

        private static string GetLabStates(ProcedureQueue queue)
        {
            if (queue == null ||
                queue.m_labProcedures == null ||
                queue.m_labProcedures.Count == 0)
            {
                return "<none>";
            }

            string result = string.Empty;
            int validCount = 0;

            for (int i = 0; i < queue.m_labProcedures.Count; i++)
            {
                EntityIDPointer<LabProcedure> pointer =
                    queue.m_labProcedures[i];
                LabProcedure lab =
                    pointer == null ? null : pointer.GetEntity();
                if (lab == null || lab.m_state == null)
                {
                    continue;
                }

                if (validCount > 0)
                {
                    result += ",";
                }

                result += lab.m_state.m_labProcedureState.ToString();
                validCount++;

                if (validCount >= 6)
                {
                    break;
                }
            }

            return validCount == 0 ? "<deleted>" : result;
        }
    }
}
