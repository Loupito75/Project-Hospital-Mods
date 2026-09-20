using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalEMS
{
    internal static class PrehospitalAssessmentService
    {
        private enum CareCategory
        {
            FirstAid,
            AdvancedCare,
            RareCare
        }

        private static readonly string[] BasicAssessmentExaminationIds = new string[]
        {
            "EXM_PHYSICAL_AND_VISUAL_EXAMINATION",
            "EXM_BLOOD_PRESSURE_AND_PULSE_MEASUREMENT",
            "EXM_TEMPERATURE_MEASUREMENT",
            "EXM_PULSE_OXYMETRY"
        };

        private static readonly string[] FocusedAssessmentExaminationIds = new string[]
        {
            "EXM_CHEST_LISTENING",
            "EXM_NEUROLOGICAL_TESTING",
            "EXM_ABDOMINAL_PALPATION"
        };

        private static readonly string[] AdvancedDiagnosticExaminationIds = new string[]
        {
            "EXM_ECG",
            "EXM_FAST"
        };

        private static readonly string[] FirstAidTreatmentIds = new string[]
        {
            "TRT_PRESSURE_BANDAGE",
            "TRT_ANTIHEMORRHAGICS",
            "TRT_EMERGENCY_CARE"
        };

        private static readonly string[] AdvancedCareTreatmentIds = new string[]
        {
            "TRT_OXYGENOTHERAPY",
            "TRT_INTRAVENOUS_INFUSION",
            "TRT_ANALGESICS"
        };

        private static readonly string[] RareCareTreatmentIds = new string[]
        {
            "TRT_BLOOD_TRANSFUSION"
        };

        private static readonly List<string> MissingDatabaseEntriesLogged =
            new List<string>();

        internal static void ApplyExaminations(BehaviorPatient patientBehavior)
        {
            if (patientBehavior == null ||
                patientBehavior.m_entity == null ||
                patientBehavior.m_state == null ||
                patientBehavior.m_state.m_medicalCondition == null ||
                Database.Instance == null)
            {
                return;
            }

            Entity patient = patientBehavior.m_entity;
            ProcedureComponent procedures = patient.GetComponent<ProcedureComponent>();
            if (procedures == null ||
                procedures.m_state == null ||
                procedures.m_state.m_procedureQueue == null)
            {
                return;
            }

            ProcedureQueue queue = procedures.m_state.m_procedureQueue;

            ApplyBasicAssessment(patientBehavior, patient, queue);
            ApplyIndicatedExaminationCategory(
                patientBehavior,
                patient,
                queue,
                FocusedAssessmentExaminationIds,
                false);
            ApplyIndicatedExaminationCategory(
                patientBehavior,
                patient,
                queue,
                AdvancedDiagnosticExaminationIds,
                true);

            // Examinations are inserted after vanilla critical-treatment planning but
            // before vanilla finishes its initial ambulance placement decision.
            // Refresh only examination availability here; do not rerun treatment planning.
            RefreshExaminationAvailabilityOnly(patientBehavior, procedures);
        }

        internal static void ApplyCareAfterNativePlacement(
            BehaviorPatient patientBehavior)
        {
            if (patientBehavior == null ||
                patientBehavior.m_entity == null ||
                patientBehavior.m_state == null ||
                patientBehavior.m_state.m_medicalCondition == null ||
                Database.Instance == null)
            {
                return;
            }

            Entity patient = patientBehavior.m_entity;
            ProcedureComponent procedures = patient.GetComponent<ProcedureComponent>();
            if (procedures == null ||
                procedures.m_state == null ||
                procedures.m_state.m_procedureQueue == null)
            {
                return;
            }

            ProcedureQueue queue = procedures.m_state.m_procedureQueue;

            ApplyCareCategory(
                patientBehavior,
                patient,
                queue,
                FirstAidTreatmentIds,
                CareCategory.FirstAid);
            ApplyCareCategory(
                patientBehavior,
                patient,
                queue,
                AdvancedCareTreatmentIds,
                CareCategory.AdvancedCare);
            ApplyCareCategory(
                patientBehavior,
                patient,
                queue,
                RareCareTreatmentIds,
                CareCategory.RareCare);

            RefreshPostArrivalMedicalState(patientBehavior, procedures);
        }

        private static void ApplyBasicAssessment(
            BehaviorPatient patientBehavior,
            Entity patient,
            ProcedureQueue queue)
        {
            if (!HospitalEmsConfig.ShouldPerformBasicAssessment())
            {
                return;
            }

            for (int examinationIndex = 0;
                examinationIndex < BasicAssessmentExaminationIds.Length;
                examinationIndex++)
            {
                RecordExamination(
                    patientBehavior,
                    patient,
                    queue,
                    BasicAssessmentExaminationIds[examinationIndex]);
            }
        }

        private static void ApplyIndicatedExaminationCategory(
            BehaviorPatient patientBehavior,
            Entity patient,
            ProcedureQueue queue,
            string[] examinationIds,
            bool advancedDiagnostics)
        {
            for (int examinationIndex = 0;
                examinationIndex < examinationIds.Length;
                examinationIndex++)
            {
                string examinationId = examinationIds[examinationIndex];
                GameDBExamination examination = GetExamination(examinationId);
                if (examination == null ||
                    queue.HasFinishedExamination(examination) ||
                    (queue.m_activeExamination != null &&
                        queue.m_activeExamination.Entry == examination) ||
                    !IsExaminationIndicated(
                        patientBehavior,
                        queue,
                        examination))
                {
                    continue;
                }

                bool selected = advancedDiagnostics
                    ? HospitalEmsConfig.ShouldPerformAdvancedDiagnostics()
                    : HospitalEmsConfig.ShouldPerformFocusedAssessment();

                if (selected)
                {
                    RecordExamination(
                        patientBehavior,
                        patient,
                        queue,
                        examinationId);
                }
            }
        }

        private static bool IsExaminationIndicated(
            BehaviorPatient patientBehavior,
            ProcedureQueue queue,
            GameDBExamination examination)
        {
            if (queue.HasPlannedExamination(examination))
            {
                return true;
            }

            MedicalCondition medicalCondition =
                patientBehavior.m_state.m_medicalCondition;

            if (CountKnownActiveSymptomsForExamination(
                medicalCondition,
                examination) > 0)
            {
                return true;
            }

            if (medicalCondition.GetNumberOfUncoveredSymptoms() == 0)
            {
                return false;
            }

            CharacterPersonalInfoComponent personalInfo =
                patientBehavior.m_entity.GetComponent<CharacterPersonalInfoComponent>();
            if (personalInfo == null ||
                personalInfo.m_personalInfo == null ||
                !personalInfo.m_personalInfo.m_gender.IsValid ||
                personalInfo.m_personalInfo.m_gender.Entry == null)
            {
                return false;
            }

            // Query the game's own possible-diagnosis logic into a temporary list.
            // This uses visible findings only and avoids global diagnosis events here.
            List<PossibleDiagnosis> possibleDiagnoses =
                new List<PossibleDiagnosis>();
            medicalCondition.GetPossibleDiagnoses(
                possibleDiagnoses,
                true,
                personalInfo.m_personalInfo.m_gender.Entry);

            for (int diagnosisIndex = 0;
                diagnosisIndex < possibleDiagnoses.Count;
                diagnosisIndex++)
            {
                PossibleDiagnosis possibleDiagnosis =
                    possibleDiagnoses[diagnosisIndex];
                if (possibleDiagnosis == null ||
                    !possibleDiagnosis.m_diagnosis.IsValid ||
                    possibleDiagnosis.m_diagnosis.Entry == null ||
                    possibleDiagnosis.m_diagnosis.Entry.Examinations == null)
                {
                    continue;
                }

                DatabaseEntryRef<GameDBExamination>[] examinations =
                    possibleDiagnosis.m_diagnosis.Entry.Examinations;
                for (int candidateIndex = 0;
                    candidateIndex < examinations.Length;
                    candidateIndex++)
                {
                    DatabaseEntryRef<GameDBExamination> examinationRef =
                        examinations[candidateIndex];
                    if (examinationRef != null &&
                        examinationRef.Entry != null &&
                        examinationRef.Entry == examination)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ApplyCareCategory(
            BehaviorPatient patientBehavior,
            Entity patient,
            ProcedureQueue queue,
            string[] treatmentIds,
            CareCategory category)
        {
            if (CountEligibleTreatments(
                patientBehavior.m_state.m_medicalCondition,
                queue,
                treatmentIds) == 0)
            {
                return;
            }

            bool selected = ShouldPerformCareCategory(category);

            for (int treatmentIndex = 0;
                treatmentIndex < treatmentIds.Length;
                treatmentIndex++)
            {
                GameDBTreatment treatment =
                    GetTreatment(treatmentIds[treatmentIndex]);
                if (!selected ||
                    treatment == null ||
                    CountCompatibleKnownActiveSymptoms(
                        patientBehavior.m_state.m_medicalCondition,
                        treatment) == 0 ||
                    queue.HasFinishedTreatment(treatment) ||
                    queue.HasActiveTreatment(treatment))
                {
                    continue;
                }

                ApplyPrehospitalTreatment(
                    patientBehavior,
                    patient,
                    queue,
                    treatment);
            }
        }

        private static int CountEligibleTreatments(
            MedicalCondition medicalCondition,
            ProcedureQueue queue,
            string[] treatmentIds)
        {
            int eligibleTreatments = 0;

            for (int treatmentIndex = 0;
                treatmentIndex < treatmentIds.Length;
                treatmentIndex++)
            {
                GameDBTreatment treatment = GetTreatment(
                    treatmentIds[treatmentIndex]);
                if (treatment == null ||
                    queue.HasFinishedTreatment(treatment) ||
                    queue.HasActiveTreatment(treatment))
                {
                    continue;
                }

                if (CountCompatibleKnownActiveSymptoms(
                    medicalCondition,
                    treatment) > 0)
                {
                    eligibleTreatments++;
                }
            }

            return eligibleTreatments;
        }

        private static bool ShouldPerformCareCategory(CareCategory category)
        {
            if (category == CareCategory.FirstAid)
            {
                return HospitalEmsConfig.ShouldPerformFirstAid();
            }

            if (category == CareCategory.AdvancedCare)
            {
                return HospitalEmsConfig.ShouldPerformAdvancedCare();
            }

            return HospitalEmsConfig.ShouldPerformRareCare();
        }

        private static bool RecordExamination(
            BehaviorPatient patientBehavior,
            Entity patient,
            ProcedureQueue queue,
            string examinationId)
        {
            GameDBExamination examination = GetExamination(examinationId);
            if (examination == null ||
                queue.HasFinishedExamination(examination) ||
                (queue.m_activeExamination != null &&
                    queue.m_activeExamination.Entry == examination))
            {
                return false;
            }

            int discoveredSymptoms = RevealSpawnedSymptomsForExamination(
                patientBehavior.m_state.m_medicalCondition,
                examination);

            if (queue.HasPlannedExamination(examination))
            {
                queue.RemovePlannedExamination(examination);
            }

            ExaminationState examinationState = new ExaminationState();
            examinationState.m_examination = examination;
            examinationState.m_discoveredSymptoms = discoveredSymptoms;
            queue.m_finishedExaminations.Add(examinationState);

            return true;
        }

        private static void ApplyPrehospitalTreatment(
            BehaviorPatient patientBehavior,
            Entity patient,
            ProcedureQueue queue,
            GameDBTreatment treatment)
        {
            if (treatment == null ||
                queue.HasFinishedTreatment(treatment) ||
                queue.HasActiveTreatment(treatment))
            {
                return;
            }

            bool treatmentWasPlanned =
                queue.HasPlannedTreatment(treatment);
            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();

            int treatedSymptoms = SuppressKnownSpawnedSymptomsForTreatment(
                patientBehavior.m_state.m_medicalCondition,
                treatment,
                hospitalization);

            if (treatedSymptoms == 0)
            {
                return;
            }

            if (treatmentWasPlanned)
            {
                queue.RemovePlannedTreatment(treatment);
            }

            TreatmentState treatmentState = new TreatmentState();
            treatmentState.m_treatment = treatment;
            treatmentState.m_durationHours = treatment.DurationHours;

            if (IsOngoingReceiptTreatment(treatment))
            {
                queue.m_activeTreatmentStates.Add(treatmentState);
                ApplyOngoingReceiptState(
                    hospitalization,
                    treatment.ReceiptType);
            }
            else
            {
                queue.m_finishedTreatmentStates.Add(treatmentState);
            }

            ClearCollapseHandledByTreatment(
                patientBehavior,
                treatment);

            MoodComponent mood = patient.GetComponent<MoodComponent>();
            if (mood != null)
            {
                mood.UpdateSymptomDiscomfortModifiers();
            }
        }

        private static void RefreshExaminationAvailabilityOnly(
            BehaviorPatient patientBehavior,
            ProcedureComponent procedures)
        {
            if (patientBehavior == null ||
                patientBehavior.m_state == null ||
                patientBehavior.m_state.m_medicalCondition == null ||
                procedures == null)
            {
                return;
            }

            procedures.UpdateAllExaminationsForMedicalCondition(
                patientBehavior.m_state.m_medicalCondition);
            patientBehavior.m_examinationsDirty = false;
        }

        private static void RefreshPostArrivalMedicalState(
            BehaviorPatient patientBehavior,
            ProcedureComponent procedures)
        {
            if (patientBehavior == null ||
                patientBehavior.m_state == null ||
                patientBehavior.m_state.m_medicalCondition == null ||
                patientBehavior.m_entity == null ||
                procedures == null)
            {
                return;
            }

            patientBehavior.m_state.m_medicalCondition.UpdatePossibleDiagnoses(
                patientBehavior.m_entity);
            procedures.UpdateAllExaminationsForMedicalCondition(
                patientBehavior.m_state.m_medicalCondition);
            patientBehavior.m_examinationsDirty = false;
        }

        private static GameDBExamination GetExamination(string examinationId)
        {
            GameDBExamination examination =
                Database.Instance.GetEntry<GameDBExamination>(examinationId);
            if (examination == null)
            {
                LogMissingDatabaseEntryOnce(
                    "examination",
                    examinationId);
            }

            return examination;
        }

        private static GameDBTreatment GetTreatment(string treatmentId)
        {
            GameDBTreatment treatment =
                Database.Instance.GetEntry<GameDBTreatment>(treatmentId);
            if (treatment == null)
            {
                LogMissingDatabaseEntryOnce(
                    "treatment",
                    treatmentId);
            }

            return treatment;
        }

        private static void LogMissingDatabaseEntryOnce(
            string entryType,
            string databaseId)
        {
            string key = entryType + ":" + databaseId;
            if (MissingDatabaseEntriesLogged.Contains(key))
            {
                return;
            }

            MissingDatabaseEntriesLogged.Add(key);
            Plugin.Log.LogWarning(
                "Optional prehospital " + entryType + " " + databaseId +
                " is not available in the current game database; skipping it.");
        }

        private static int CountKnownActiveSymptomsForExamination(
            MedicalCondition medicalCondition,
            GameDBExamination examination)
        {
            int compatibleSymptoms = 0;

            for (int symptomIndex = 0;
                symptomIndex < medicalCondition.m_symptoms.Count;
                symptomIndex++)
            {
                Symptom symptom = medicalCondition.m_symptoms[symptomIndex];
                if (symptom == null ||
                    !symptom.m_spawned ||
                    !symptom.m_active ||
                    symptom.m_hidden ||
                    symptom.m_symptom == null ||
                    symptom.m_symptom.Entry == null ||
                    symptom.m_symptom.Entry.Examinations == null)
                {
                    continue;
                }

                DatabaseEntryRef<GameDBExamination>[] examinations =
                    symptom.m_symptom.Entry.Examinations;
                for (int examinationIndex = 0;
                    examinationIndex < examinations.Length;
                    examinationIndex++)
                {
                    DatabaseEntryRef<GameDBExamination> examinationRef =
                        examinations[examinationIndex];
                    if (examinationRef != null &&
                        examinationRef.Entry != null &&
                        examinationRef.Entry == examination)
                    {
                        compatibleSymptoms++;
                        break;
                    }
                }
            }

            return compatibleSymptoms;
        }

        private static int CountCompatibleKnownActiveSymptoms(
            MedicalCondition medicalCondition,
            GameDBTreatment treatment)
        {
            int compatibleSymptoms = 0;

            for (int symptomIndex = 0;
                symptomIndex < medicalCondition.m_symptoms.Count;
                symptomIndex++)
            {
                Symptom symptom = medicalCondition.m_symptoms[symptomIndex];
                if (symptom == null ||
                    !symptom.m_spawned ||
                    !symptom.m_active ||
                    symptom.m_hidden ||
                    symptom.m_symptom == null ||
                    symptom.m_symptom.Entry == null ||
                    symptom.m_symptom.Entry.Treatments == null)
                {
                    continue;
                }

                DatabaseEntryRef<GameDBTreatment>[] treatments =
                    symptom.m_symptom.Entry.Treatments;
                for (int treatmentIndex = 0;
                    treatmentIndex < treatments.Length;
                    treatmentIndex++)
                {
                    DatabaseEntryRef<GameDBTreatment> treatmentRef =
                        treatments[treatmentIndex];
                    if (treatmentRef != null &&
                        treatmentRef.Entry != null &&
                        treatmentRef.Entry == treatment)
                    {
                        compatibleSymptoms++;
                        break;
                    }
                }
            }

            return compatibleSymptoms;
        }

        private static bool IsOngoingReceiptTreatment(
            GameDBTreatment treatment)
        {
            return treatment.TreatmentType == TreatmentType.RECEIPT &&
                treatment.ReceiptType > ReceiptType.PILLS;
        }

        private static void ApplyOngoingReceiptState(
            HospitalizationComponent hospitalization,
            ReceiptType receiptType)
        {
            if (hospitalization == null ||
                hospitalization.m_state == null)
            {
                return;
            }

            if (receiptType == ReceiptType.IV_BAG)
            {
                hospitalization.m_state.m_fluidstandMedicine = true;
            }
            else if (receiptType == ReceiptType.BLOOD_BAG)
            {
                hospitalization.m_state.m_bloodBag = true;
            }
            else if (receiptType == ReceiptType.OXYGEN_MASK)
            {
                hospitalization.m_state.m_oxygenMask = true;
            }

            hospitalization.m_state.m_medicinePrescribed = true;
            hospitalization.m_state.m_medicineReceived = true;
        }

        private static int RevealSpawnedSymptomsForExamination(
            MedicalCondition medicalCondition,
            GameDBExamination examination)
        {
            int discoveredSymptoms = 0;

            for (int symptomIndex = 0;
                symptomIndex < medicalCondition.m_symptoms.Count;
                symptomIndex++)
            {
                Symptom symptom = medicalCondition.m_symptoms[symptomIndex];
                if (symptom == null ||
                    !symptom.m_spawned ||
                    !symptom.m_active ||
                    !symptom.m_hidden ||
                    symptom.m_symptom == null ||
                    symptom.m_symptom.Entry == null)
                {
                    continue;
                }

                DatabaseEntryRef<GameDBExamination>[] examinations =
                    symptom.m_symptom.Entry.Examinations;
                if (examinations == null)
                {
                    continue;
                }

                for (int examinationIndex = 0;
                    examinationIndex < examinations.Length;
                    examinationIndex++)
                {
                    DatabaseEntryRef<GameDBExamination> examinationRef =
                        examinations[examinationIndex];
                    if (examinationRef != null &&
                        examinationRef.Entry != null &&
                        examinationRef.Entry == examination)
                    {
                        symptom.m_hidden = false;
                        discoveredSymptoms++;
                        break;
                    }
                }
            }

            return discoveredSymptoms;
        }

        private static int SuppressKnownSpawnedSymptomsForTreatment(
            MedicalCondition medicalCondition,
            GameDBTreatment treatment,
            HospitalizationComponent hospitalization)
        {
            int treatedSymptoms = 0;

            for (int symptomIndex = 0;
                symptomIndex < medicalCondition.m_symptoms.Count;
                symptomIndex++)
            {
                Symptom symptom = medicalCondition.m_symptoms[symptomIndex];
                if (symptom == null ||
                    !symptom.m_spawned ||
                    !symptom.m_active ||
                    symptom.m_hidden ||
                    symptom.m_symptom == null ||
                    symptom.m_symptom.Entry == null)
                {
                    continue;
                }

                DatabaseEntryRef<GameDBTreatment>[] treatments =
                    symptom.m_symptom.Entry.Treatments;
                if (treatments == null)
                {
                    continue;
                }

                for (int treatmentIndex = 0;
                    treatmentIndex < treatments.Length;
                    treatmentIndex++)
                {
                    DatabaseEntryRef<GameDBTreatment> treatmentRef =
                        treatments[treatmentIndex];
                    if (treatmentRef != null &&
                        treatmentRef.Entry != null &&
                        treatmentRef.Entry == treatment)
                    {
                        symptom.m_active = false;
                        treatedSymptoms++;

                        if (hospitalization != null)
                        {
                            hospitalization.CheckSurvivedCollapseCount();
                        }

                        break;
                    }
                }
            }

            return treatedSymptoms;
        }

        private static void ClearCollapseHandledByTreatment(
            BehaviorPatient patientBehavior,
            GameDBTreatment treatment)
        {
            if (patientBehavior.m_state.m_collapseSymptom == null ||
                patientBehavior.m_state.m_collapseSymptom.Entry == null ||
                patientBehavior.m_state.m_collapseSymptom.Entry.Treatments == null ||
                patientBehavior.m_state.m_collapseSymptom.Entry.Treatments.Length == 0 ||
                patientBehavior.m_state.m_collapseSymptom.Entry.Treatments[0] == null ||
                patientBehavior.m_state.m_collapseSymptom.Entry.Treatments[0].Entry != treatment)
            {
                return;
            }

            patientBehavior.m_state.m_collapseProcedure = null;
            patientBehavior.m_state.m_collapseSymptom = null;
        }
    }
}
