using GLib;
using Lopital;

namespace HospitalCareLevelTransfer
{
    internal enum CareLevelTransferEligibility
    {
        NotHighPriority,
        NotReadyInBed,
        Unavailable,
        StillRequiresHdu,
        Eligible
    }

    internal enum TransferAttemptResult
    {
        Success,
        NotHighPriority,
        NotReadyInBed,
        Unavailable,
        StillRequiresHdu,
        NoRegularWardBed
    }

    internal enum HduRequirementReason
    {
        None,
        HighHazard,
        ImmobileSymptom,
        HighHazardAndImmobile
    }

    internal static class CareLevelTransferService
    {
        private const string HighPriorityTreatmentId = "TRT_HOSPITALIZATION_HIGH_PRIORITY";
        private const string RegularWardRoomTypeId = "ROOM_TYPE_INPATIENT_WARD";

        internal static bool IsHighPriorityHospitalized(Entity patient)
        {
            if (patient == null || Database.Instance == null)
            {
                return false;
            }

            HospitalizationComponent hospitalization = patient.GetComponent<HospitalizationComponent>();
            if (hospitalization == null ||
                !hospitalization.IsHospitalized() ||
                hospitalization.m_state == null ||
                hospitalization.m_state.m_hospitalizationTreatment == null)
            {
                return false;
            }

            GameDBTreatment highPriorityTreatment = Database.Instance.GetEntry<GameDBTreatment>(HighPriorityTreatmentId);
            return highPriorityTreatment != null &&
                hospitalization.m_state.m_hospitalizationTreatment.Entry == highPriorityTreatment;
        }

        internal static HduRequirementReason GetHduRequirementReason(Entity patient)
        {
            if (patient == null)
            {
                return HduRequirementReason.None;
            }

            return GetHduRequirementReason(patient.GetComponent<BehaviorPatient>());
        }

        internal static CareLevelTransferEligibility GetEligibility(Entity patient)
        {
            HduRequirementReason ignored;
            return GetEligibility(patient, out ignored);
        }

        internal static CareLevelTransferEligibility GetEligibility(
            Entity patient,
            out HduRequirementReason hduRequirement)
        {
            hduRequirement = HduRequirementReason.None;

            if (!IsHighPriorityHospitalized(patient))
            {
                return CareLevelTransferEligibility.NotHighPriority;
            }

            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            HospitalizationComponent hospitalization = patient.GetComponent<HospitalizationComponent>();
            StretcherComponent stretcher = patient.GetComponent<StretcherComponent>();
            if (behavior == null ||
                hospitalization == null ||
                stretcher == null ||
                behavior.m_state == null ||
                hospitalization.m_state == null ||
                stretcher.m_state == null)
            {
                return CareLevelTransferEligibility.Unavailable;
            }

            if (behavior.m_state.m_sentAway ||
                behavior.m_state.m_sentHome ||
                behavior.m_state.m_deathTriggered ||
                behavior.m_state.m_medicalCondition == null)
            {
                return CareLevelTransferEligibility.Unavailable;
            }

            if (behavior.m_state.m_patientState != PatientState.OverriddenByHospitalization ||
                hospitalization.m_state.m_bed == null ||
                hospitalization.m_state.m_bed.GetEntity() == null)
            {
                return CareLevelTransferEligibility.NotReadyInBed;
            }

            if (!hospitalization.IsFree() || !stretcher.IsIdle())
            {
                return CareLevelTransferEligibility.Unavailable;
            }

            hduRequirement = GetHduRequirementReason(behavior);

            if (behavior.m_state.m_medicalCondition.IsInfectious() ||
                RequiresHospitalizationAboveNormalForTreatment(patient) ||
                hduRequirement != HduRequirementReason.None)
            {
                return CareLevelTransferEligibility.StillRequiresHdu;
            }

            return CareLevelTransferEligibility.Eligible;
        }

        internal static TransferAttemptResult TryTransferToRegularWard(Entity patient)
        {
            CareLevelTransferEligibility eligibility = GetEligibility(patient);
            if (eligibility == CareLevelTransferEligibility.NotHighPriority)
            {
                return TransferAttemptResult.NotHighPriority;
            }
            if (eligibility == CareLevelTransferEligibility.NotReadyInBed)
            {
                return TransferAttemptResult.NotReadyInBed;
            }
            if (eligibility == CareLevelTransferEligibility.Unavailable)
            {
                return TransferAttemptResult.Unavailable;
            }
            if (eligibility == CareLevelTransferEligibility.StillRequiresHdu)
            {
                return TransferAttemptResult.StillRequiresHdu;
            }

            if (!HasFreeRegularWardBed(patient))
            {
                return TransferAttemptResult.NoRegularWardBed;
            }

            return StartTransferToRegularWard(patient)
                ? TransferAttemptResult.Success
                : TransferAttemptResult.Unavailable;
        }

        internal static bool StartTransferToRegularWard(Entity patient)
        {
            if (patient == null)
            {
                return false;
            }

            HospitalizationComponent hospitalization = patient.GetComponent<HospitalizationComponent>();
            StretcherComponent stretcher = patient.GetComponent<StretcherComponent>();
            if (hospitalization == null ||
                stretcher == null ||
                !hospitalization.IsFree() ||
                !stretcher.IsIdle())
            {
                return false;
            }

            hospitalization.HospitalizationChange();
            return true;
        }

        internal static bool HasFreeRegularWardBed(Entity patient)
        {
            if (patient == null)
            {
                return false;
            }

            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            WalkComponent walk = patient.GetComponent<WalkComponent>();
            if (behavior == null || walk == null || MapScriptInterface.Instance == null || Database.Instance == null)
            {
                return false;
            }

            Department department = behavior.GetDepartment();
            GameDBRoomType regularWard = Database.Instance.GetEntry<GameDBRoomType>(RegularWardRoomTypeId);
            if (department == null || regularWard == null)
            {
                return false;
            }

            TileObject bed = MapScriptInterface.Instance.FindClosestFreeObjectWithTags(
                walk.GetCurrentTile(),
                walk.GetFloorIndex(),
                department,
                new string[1] { "hospitalization" },
                AccessRights.STAFF,
                regularWard);

            return bed != null;
        }

        private static HduRequirementReason GetHduRequirementReason(BehaviorPatient behavior)
        {
            if (behavior == null || behavior.m_state == null || behavior.m_state.m_medicalCondition == null)
            {
                return HduRequirementReason.None;
            }

            bool highHazard = (int)behavior.GetWorstKnownHazard() >= (int)SymptomHazard.High;
            bool immobile = behavior.m_state.m_medicalCondition.HasImmobileSymptom();

            if (highHazard && immobile)
            {
                return HduRequirementReason.HighHazardAndImmobile;
            }
            if (highHazard)
            {
                return HduRequirementReason.HighHazard;
            }
            if (immobile)
            {
                return HduRequirementReason.ImmobileSymptom;
            }

            return HduRequirementReason.None;
        }

        private static bool RequiresHospitalizationAboveNormalForTreatment(Entity patient)
        {
            if (patient == null)
            {
                return true;
            }

            ProcedureComponent procedures = patient.GetComponent<ProcedureComponent>();
            if (procedures == null || procedures.m_state == null || procedures.m_state.m_procedureQueue == null)
            {
                return true;
            }

            ProcedureQueue queue = procedures.m_state.m_procedureQueue;

            foreach (PlannedTreatmentState plannedTreatment in queue.m_plannedTreatmentStates)
            {
                if (plannedTreatment != null &&
                    plannedTreatment.m_treatment != null &&
                    TreatmentStrictlyRequiresHospitalizationAboveNormal(plannedTreatment.m_treatment.Entry))
                {
                    return true;
                }
            }

            foreach (TreatmentState activeTreatment in queue.m_activeTreatmentStates)
            {
                if (activeTreatment != null &&
                    activeTreatment.m_treatment != null &&
                    TreatmentStrictlyRequiresHospitalizationAboveNormal(activeTreatment.m_treatment.Entry))
                {
                    return true;
                }
            }

            GameDBTreatment nativeRequirement = procedures.GetSurgeryHospitalizationTreatment();
            return nativeRequirement != null &&
                nativeRequirement.Procedure != null &&
                nativeRequirement.Procedure.HospitalizationLevel == HospitalizationLevel.ICU;
        }

        private static bool TreatmentStrictlyRequiresHospitalizationAboveNormal(GameDBTreatment treatment)
        {
            if (treatment == null ||
                treatment.AllowedWithAnyHospitalization ||
                treatment.HospitalizationTreatmentRef == null)
            {
                return false;
            }

            GameDBTreatment requiredHospitalization = treatment.HospitalizationTreatmentRef.Entry;
            return requiredHospitalization != null &&
                requiredHospitalization.Procedure != null &&
                requiredHospitalization.Procedure.HospitalizationLevel > HospitalizationLevel.NORMAL;
        }
    }
}
