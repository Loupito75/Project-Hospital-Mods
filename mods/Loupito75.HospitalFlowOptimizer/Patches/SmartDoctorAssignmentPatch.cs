using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFlowOptimizer.Patches
{
    [HarmonyPatch(typeof(HospitalizationComponent), nameof(HospitalizationComponent.ReserveDoctor))]
    internal static class SmartDoctorAssignmentPatch
    {
        private const float ScoreTieTolerance = 0.05f;
        private const float MaximumDoctorSkill = 5.0f;

        private const float NormalPatientLoad = 1.00f;
        private const float ObservationPatientLoad = 1.15f;
        private const float HighPriorityPatientLoad = 1.55f;
        private const float IcuPatientLoad = 2.00f;
        private const float TraumaPatientLoad = 2.00f;
        private const float MediumHazardLoadBonus = 0.15f;
        private const float HighHazardLoadBonus = 0.40f;

        private const float GeneralMedicineFitFactor = 0.20f;
        private const float UndiagnosedPatientDiagnosisFitFactor = 0.35f;
        private const float EfficiencyFitFactor = 0.15f;
        private const float ExperienceFitFactor = 0.10f;
        private const float SurgicalCapacityPenalty = 1.50f;

        private static bool Prefix(
            HospitalizationComponent __instance,
            GameDBTreatment hospitalizationTreatment)
        {
            if (!FlowOptimizerConfig.SmartDoctorAssignment)
            {
                return true;
            }

            if (__instance == null || __instance.m_entity == null)
            {
                return true;
            }

            BehaviorPatient patient = __instance.m_entity.GetComponent<BehaviorPatient>();
            if (patient == null || patient.m_state == null)
            {
                return true;
            }

            Entity assignedDoctor =
                patient.m_state.m_doctor == null
                    ? null
                    : patient.m_state.m_doctor.GetEntity();

            if (assignedDoctor != null)
            {
                return false;
            }

            Department department =
                patient.m_state.m_department == null
                    ? null
                    : patient.m_state.m_department.GetEntity();

            if (department == null)
            {
                return true;
            }

            GameDBEmployeeRole examinationRole =
                Database.Instance.GetEntry<GameDBEmployeeRole>("EMPL_ROLE_EXM_TRT");
            GameDBEmployeeRole surgeryRole =
                Database.Instance.GetEntry<GameDBEmployeeRole>("EMPL_ROLE_SURGERY");
            GameDBEmployeeRole anesthesiologyRole =
                Database.Instance.GetEntry<GameDBEmployeeRole>("EMPL_ROLE_SURGERY_ANESTHESIOLOGY");
            GameDBEmployeeRole surgeryAssistRole =
                Database.Instance.GetEntry<GameDBEmployeeRole>("EMPL_ROLE_SURGERY_ASSIST");

            GameDBEmployeeRole requiredRole = examinationRole;

            if (hospitalizationTreatment != null &&
                hospitalizationTreatment ==
                Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_TRAUMA"))
            {
                requiredRole = surgeryRole;
            }

            List<EntityIDPointer<Entity>> candidates =
                GetVanillaCandidates(__instance.m_entity, department, requiredRole, true);

            if (candidates.Count == 0)
            {
                candidates =
                    GetVanillaCandidates(__instance.m_entity, department, requiredRole, false);
            }

            if (candidates.Count > 0)
            {
                Entity selected = SelectDoctor(
                    candidates,
                    patient,
                    department,
                    hospitalizationTreatment,
                    requiredRole,
                    surgeryRole,
                    anesthesiologyRole,
                    surgeryAssistRole);

                if (selected != null)
                {
                    patient.SetDoctor(selected);
                    HfoDiagnostics.Log(
                        "HospitalDoctor",
                        "Selected " + HfoDiagnostics.EntityName(selected) +
                        " for " + HfoDiagnostics.EntityName(__instance.m_entity) + ".");
                }
            }

            if (patient.m_state.m_doctor == null)
            {
                __instance.m_entity.LogWarning(
                    "No doctor available for hospitalized patient at all!");
            }

            return false;
        }


        private static List<EntityIDPointer<Entity>> GetVanillaCandidates(
            Entity patient,
            Department department,
            GameDBEmployeeRole role,
            bool hasToBeFree)
        {
            return MapScriptInterface.Instance.FindDoctorsAssignedToARoomType(
                Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_ON_CALL_ROOM"),
                patient,
                hasToBeFree,
                department,
                role,
                2);
        }

        private static Entity SelectDoctor(
            List<EntityIDPointer<Entity>> candidates,
            BehaviorPatient patient,
            Department department,
            GameDBTreatment hospitalizationTreatment,
            GameDBEmployeeRole requiredRole,
            GameDBEmployeeRole surgeryRole,
            GameDBEmployeeRole anesthesiologyRole,
            GameDBEmployeeRole surgeryAssistRole)
        {
            float bestScore = float.MaxValue;
            List<Entity> best = new List<Entity>();

            foreach (EntityIDPointer<Entity> pointer in candidates)
            {
                Entity doctor = pointer.GetEntity();
                if (doctor == null)
                {
                    continue;
                }

                EmployeeComponent employee = doctor.GetComponent<EmployeeComponent>();
                if (employee == null || employee.m_state == null)
                {
                    continue;
                }

                float workload = GetDoctorWorkload(doctor);
                float clinicalFit =
                    GetClinicalFit(
                        doctor,
                        patient,
                        department,
                        hospitalizationTreatment);
                float surgicalPenalty = 0.0f;

                if (FlowOptimizerConfig.ProtectSurgicalCapacity &&
                    requiredRole != surgeryRole &&
                    IsNeededSurgicalResource(
                        employee,
                        department,
                        surgeryRole,
                        anesthesiologyRole,
                        surgeryAssistRole))
                {
                    surgicalPenalty = SurgicalCapacityPenalty;
                }

                float score =
                    workload * FlowOptimizerConfig.WorkloadWeight -
                    clinicalFit * FlowOptimizerConfig.ClinicalFitWeight +
                    surgicalPenalty;

                HfoDiagnostics.Log(
                    "HospitalDoctor",
                    "department=" + HfoDiagnostics.DepartmentId(department) +
                    " candidate=" + HfoDiagnostics.EntityName(doctor) +
                    " level=" + HfoDiagnostics.DoctorLevel(doctor) +
                    " workload=" + HfoDiagnostics.Number(workload) +
                    " fit=" + HfoDiagnostics.Number(clinicalFit) +
                    " surgicalPenalty=" + HfoDiagnostics.Number(surgicalPenalty) +
                    " score=" + HfoDiagnostics.Number(score));

                if (score < bestScore - ScoreTieTolerance)
                {
                    bestScore = score;
                    best.Clear();
                    best.Add(doctor);
                }
                else if (score <= bestScore + ScoreTieTolerance)
                {
                    best.Add(doctor);
                }
            }

            if (best.Count == 0)
            {
                return null;
            }

            return best[UnityEngine.Random.Range(0, best.Count)];
        }

        private static float GetDoctorWorkload(Entity doctor)
        {
            PatientsOfADoctor patients = Hospital.Instance.GetPatientsOfADoctor(
                doctor,
                includeCurrentUnassigned: false,
                addPreviousShift: false,
                dontPutCurrentAtTheTop: true);

            if (patients == null || patients.m_currentPatients == null)
            {
                return 0.0f;
            }

            float workload = 0.0f;

            foreach (Entity assignedPatient in patients.m_currentPatients)
            {
                workload += GetPatientLoad(assignedPatient);
            }

            return workload;
        }

        private static float GetPatientLoad(Entity patient)
        {
            if (patient == null)
            {
                return 0.0f;
            }

            float load = NormalPatientLoad;

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();

            if (hospitalization != null)
            {
                GameDBTreatment treatment = hospitalization.GetHospitalizationTreatment();
                HospitalizationLevel level = GetHospitalizationLevel(treatment);

                switch (level)
                {
                    case HospitalizationLevel.OBSERVATION:
                        load = ObservationPatientLoad;
                        break;

                    case HospitalizationLevel.HIGH_PRIORITY:
                        load = HighPriorityPatientLoad;
                        break;

                    case HospitalizationLevel.ICU:
                        load = IcuPatientLoad;
                        break;

                    case HospitalizationLevel.TRAUMA:
                        load = TraumaPatientLoad;
                        break;

                    case HospitalizationLevel.NORMAL:
                    case HospitalizationLevel.NONE:
                    default:
                        load = NormalPatientLoad;
                        break;
                }
            }

            BehaviorPatient behaviorPatient = patient.GetComponent<BehaviorPatient>();
            if (behaviorPatient != null)
            {
                SymptomHazard hazard = behaviorPatient.GetWorstKnownHazard();

                if (hazard == SymptomHazard.High)
                {
                    load += HighHazardLoadBonus;
                }
                else if (hazard == SymptomHazard.Medium)
                {
                    load += MediumHazardLoadBonus;
                }
            }

            return load;
        }

        private static float GetClinicalFit(
            Entity doctor,
            BehaviorPatient patient,
            Department department,
            GameDBTreatment hospitalizationTreatment)
        {
            EmployeeComponent employee = doctor.GetComponent<EmployeeComponent>();
            if (employee == null || employee.m_state == null)
            {
                return 0.0f;
            }

            HospitalizationLevel level =
                GetHospitalizationLevel(hospitalizationTreatment);

            float fit = 0.0f;

            GameDBDepartment departmentType = department.GetDepartmentType();
            if (departmentType != null &&
                departmentType.FirstDoctorSpecialization != null &&
                departmentType.FirstDoctorSpecialization.Entry != null)
            {
                fit +=
                    NormalizeSkill(
                        employee.GetSkillLevel(
                            departmentType.FirstDoctorSpecialization.Entry)) *
                    GetDepartmentSpecializationFitFactor(level);
            }

            fit +=
                NormalizeSkill(
                    employee.GetSkillLevel("SKILL_DOC_QUALIF_GENERAL_MEDICINE")) *
                GeneralMedicineFitFactor;

            if (patient.m_state.m_medicalCondition != null &&
                patient.m_state.m_medicalCondition.m_diagnosedMedicalCondition == null)
            {
                float diagnosisSkill =
                    employee.GetSkillLevel("SKILL_DOC_QUALIF_DIAGNOSIS");

                PerkComponent perkComponent = doctor.GetComponent<PerkComponent>();
                if (perkComponent != null &&
                    perkComponent.m_perkSet != null &&
                    perkComponent.m_perkSet.HasPerk("PERK_DIAGNOSTIC_GENIUS"))
                {
                    diagnosisSkill += 1.0f;
                }

                fit +=
                    NormalizeSkill(diagnosisSkill) *
                    UndiagnosedPatientDiagnosisFitFactor;
            }

            float efficiencyMultiplier = employee.GetEfficiencyTimeMultiplier();
            if (efficiencyMultiplier > 0.0f)
            {
                float relativeEfficiency = 1.0f / efficiencyMultiplier;
                fit +=
                    Clamp(relativeEfficiency, 0.50f, 1.25f) *
                    EfficiencyFitFactor;
            }

            float experience =
                Clamp(
                    ((float)employee.m_state.m_level - 2.0f) / 3.0f,
                    0.0f,
                    1.0f);

            fit += experience * ExperienceFitFactor;

            return fit;
        }

        private static float GetDepartmentSpecializationFitFactor(
            HospitalizationLevel level)
        {
            switch (level)
            {
                case HospitalizationLevel.OBSERVATION:
                    return 0.55f;

                case HospitalizationLevel.HIGH_PRIORITY:
                    return 0.65f;

                case HospitalizationLevel.TRAUMA:
                    return 0.75f;

                case HospitalizationLevel.ICU:
                    return 0.85f;

                case HospitalizationLevel.NORMAL:
                case HospitalizationLevel.NONE:
                default:
                    return 0.45f;
            }
        }

        private static HospitalizationLevel GetHospitalizationLevel(
            GameDBTreatment treatment)
        {
            if (treatment == null || treatment.Procedure == null)
            {
                return HospitalizationLevel.NONE;
            }

            return treatment.Procedure.HospitalizationLevel;
        }

        private static bool IsNeededSurgicalResource(
            EmployeeComponent employee,
            Department department,
            GameDBEmployeeRole surgeryRole,
            GameDBEmployeeRole anesthesiologyRole,
            GameDBEmployeeRole surgeryAssistRole)
        {
            if (department == null ||
                (!department.HasWaitingSurgery() &&
                 !department.HasAnyCriticalSurgeryScheduled()))
            {
                return false;
            }

            return
                (surgeryRole != null && employee.HasRole(surgeryRole)) ||
                (anesthesiologyRole != null && employee.HasRole(anesthesiologyRole)) ||
                (surgeryAssistRole != null && employee.HasRole(surgeryAssistRole));
        }

        private static float NormalizeSkill(float skill)
        {
            return Clamp(skill, 0.0f, MaximumDoctorSkill) / MaximumDoctorSkill;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            if (value > maximum)
            {
                return maximum;
            }

            return value;
        }
    }
}
