using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalAlwaysLit
{
    internal static class NightPatientRoomLightingRules
    {
        private const string HospitalizationRoomTag = "hospitalization";

        internal static bool ShouldUseNativeCharacterLight(Room room)
        {
            if (!AlwaysLitConfig.KeepPatientRoomsDarkAtNightEnabled)
            {
                return true;
            }

            if (DayTime.Instance == null || DayTime.Instance.GetShift() != Shift.NIGHT)
            {
                return true;
            }

            // An explicit Enabled="true" room entry always wins over the optional
            // night-patient-room behavior.
            if (AlwaysLitRoomTypes.IsTarget(room))
            {
                return true;
            }

            if (room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType.Entry == null ||
                !room.m_roomPersistentData.m_roomType.Entry.HasTag(HospitalizationRoomTag))
            {
                return true;
            }

            if (!HasAssignedHospitalizedPatient(room))
            {
                return true;
            }

            if (HasImmediateClinicalNeedForRoomLight(room) ||
                HasProcedureRequiringNormalRoomLight(room))
            {
                return true;
            }

            return false;
        }

        internal static void ClearResidualCharacterLight(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return;
            }

            if (!room.m_roomPersistentData.m_characterLightOn &&
                room.m_roomPersistentData.m_characterLightTimer <= 0f)
            {
                return;
            }

            room.m_roomPersistentData.m_characterLightTimer = 0f;
            room.m_roomPersistentData.m_characterLightOn = false;

            int floorIndex = room.GetFloorIndex();
            if (floorIndex >= 0 &&
                floorIndex < Hospital.Instance.m_floors.Count &&
                Hospital.Instance.m_floors[floorIndex] != null)
            {
                room.UpdateLightLevel(Hospital.Instance.m_floors[floorIndex]);
            }
        }

        private static bool HasAssignedHospitalizedPatient(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_department == null)
            {
                return false;
            }

            Department department = room.m_roomPersistentData.m_department.GetEntity();
            if (department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_patients == null)
            {
                return false;
            }

            int roomFloorIndex = room.GetFloorIndex();

            foreach (EntityIDPointer<Entity> patientPointer in department.m_departmentPersistentData.m_patients)
            {
                if (patientPointer == null)
                {
                    continue;
                }

                Entity patient = patientPointer.GetEntity();
                if (patient == null)
                {
                    continue;
                }

                HospitalizationComponent hospitalization =
                    patient.GetComponent<HospitalizationComponent>();
                if (hospitalization == null ||
                    hospitalization.m_state == null ||
                    !hospitalization.IsHospitalized() ||
                    hospitalization.m_state.m_bed == null)
                {
                    continue;
                }

                TileObject bed = hospitalization.m_state.m_bed.GetEntity();
                if (bed != null &&
                    bed.m_state != null &&
                    bed.GetFloorIndex() == roomFloorIndex &&
                    room.IsPositionInRoom(bed.m_state.m_position))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasImmediateClinicalNeedForRoomLight(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_department == null)
            {
                return false;
            }

            Department department = room.m_roomPersistentData.m_department.GetEntity();
            if (department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_patients == null)
            {
                return false;
            }

            foreach (EntityIDPointer<Entity> patientPointer in department.m_departmentPersistentData.m_patients)
            {
                if (patientPointer == null)
                {
                    continue;
                }

                Entity patient = patientPointer.GetEntity();
                if (patient == null)
                {
                    continue;
                }

                WalkComponent patientWalk = patient.GetComponent<WalkComponent>();
                HospitalizationComponent hospitalization =
                    patient.GetComponent<HospitalizationComponent>();

                if (patientWalk == null ||
                    hospitalization == null ||
                    hospitalization.m_state == null ||
                    MapScriptInterface.Instance.GetRoomAt(patientWalk) != room)
                {
                    continue;
                }

                HospitalizationState state =
                    hospitalization.m_state.m_hospitalizationState;

                if (state != HospitalizationState.Collapsing &&
                    state != HospitalizationState.CollapsingInBed &&
                    state != HospitalizationState.WaitingForStabilization &&
                    state != HospitalizationState.BeingStabilized &&
                    state != HospitalizationState.BeingExamined &&
                    state != HospitalizationState.BeingTreated)
                {
                    continue;
                }

                ProcedureScript currentScript = null;
                ProcedureComponent procedureComponent =
                    patient.GetComponent<ProcedureComponent>();
                if (procedureComponent != null &&
                    procedureComponent.m_state != null &&
                    procedureComponent.m_state.m_currentProcedureScript != null)
                {
                    currentScript =
                        procedureComponent.m_state.m_currentProcedureScript.GetEntity();
                }

                if (state == HospitalizationState.BeingTreated &&
                    IsLightweightNightTreatmentScript(currentScript))
                {
                    continue;
                }

                if (state == HospitalizationState.BeingExamined &&
                    IsNightExaminationPhaseWithoutRoomLight(currentScript))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool IsLightweightNightTreatmentScript(
            ProcedureScript script)
        {
            return script is ProcedureScriptTreatmentReceipt ||
                   script is ProcedureScriptTreatmentPrescription;
        }

        private static bool IsNightExaminationPhaseWithoutRoomLight(
            ProcedureScript script)
        {
            if (script == null || script.m_stateData == null)
            {
                return false;
            }

            if (script is ProcedureScriptExaminationDoctorsInterview)
            {
                return true;
            }

            if (!(script is ProcedureScriptExaminationGeneral) &&
                !(script is ProcedureScriptExaminationGeneralEquipment) &&
                !(script is ProcedureScriptExaminationPhysicalChest))
            {
                return false;
            }

            string state = script.m_stateData.m_state;
            return state != "PROCEDURE_STARTED" &&
                   state != "PROCEDURE_IDLE_ANIMATION_IN" &&
                   state != "PROCEDURE_IDLE_ANIMATION" &&
                   state != "PROCEDURE_IDLE";
        }

        private static bool HasProcedureRequiringNormalRoomLight(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_currentProcedureOwner == null)
            {
                return false;
            }

            Entity procedureOwner =
                room.m_roomPersistentData.m_currentProcedureOwner.GetEntity();
            if (procedureOwner == null)
            {
                return false;
            }

            // Routine bedside checks and medicine delivery should not light the
            // whole shared room at night.
            if (procedureOwner is ProcedureScriptControlNursePatientCare ||
                procedureOwner is ProcedureScriptControlDoctorsRounds ||
                procedureOwner is ProcedureScriptControlNurseDeliveringMedicine)
            {
                return false;
            }

            ProcedureScript procedureScript = procedureOwner as ProcedureScript;
            if (IsLightweightNightTreatmentScript(procedureScript) ||
                IsNightExaminationPhaseWithoutRoomLight(procedureScript))
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Room), nameof(Room.ActivateCharacterLight))]
    internal static class NightPatientRoomCharacterLightPatch
    {
        private static bool Prefix(Room __instance)
        {
            if (__instance == null)
            {
                return true;
            }

            bool useNative =
                NightPatientRoomLightingRules.ShouldUseNativeCharacterLight(
                    __instance);

            if (!useNative)
            {
                NightPatientRoomLightingRules.ClearResidualCharacterLight(
                    __instance);
            }

            return useNative;
        }
    }
}
