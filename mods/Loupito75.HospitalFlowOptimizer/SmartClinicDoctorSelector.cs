using System;
using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalFlowOptimizer
{
    internal static class SmartClinicDoctorSelector
    {
        private const float ScoreTieTolerance = 0.03f;
        private const float MaximumDoctorSkill = 5.0f;
        private const float DistanceNormalizationTiles = 20.0f;
        private const float RecentRoomLoadFactor = 0.50f;

        internal static bool CanKeepCurrentDoctor(
            BehaviorPatient patient,
            Entity doctor,
            Department department)
        {
            if (patient == null ||
                doctor == null ||
                department == null)
            {
                return false;
            }

            return MatchesWalkInCandidate(
                doctor,
                patient,
                department,
                null,
                requireLocal: false,
                requireFree: false);
        }

        internal static Entity SelectWalkInDoctor(
            BehaviorPatient patient,
            Department department,
            Room waitingRoom)
        {
            if (patient == null || department == null)
            {
                return null;
            }

            List<Entity> freeLocal =
                GetWalkInCandidates(
                    patient,
                    department,
                    waitingRoom,
                    requireLocal: true,
                    requireFree: true);

            if (freeLocal.Count > 0)
            {
                return SelectBest(
                    freeLocal,
                    patient,
                    department,
                    null,
                    "walk-in/local");
            }

            List<Entity> localDoctors =
                GetWalkInCandidates(
                    patient,
                    department,
                    waitingRoom,
                    requireLocal: true,
                    requireFree: false);

            if (localDoctors.Count > 0)
            {
                HfoDiagnostics.LogState(
                    "ClinicDoctor",
                    "no-local:" + HfoDiagnostics.EntityKey(patient.m_entity),
                    "No local doctor is free for " +
                    HfoDiagnostics.EntityName(patient.m_entity) +
                    "; keeping the patient in the local waiting area.");
                return null;
            }

            List<Entity> freeDepartment =
                GetWalkInCandidates(
                    patient,
                    department,
                    waitingRoom,
                    requireLocal: false,
                    requireFree: true);

            if (freeDepartment.Count == 0)
            {
                return null;
            }

            return SelectBest(
                freeDepartment,
                patient,
                department,
                null,
                "walk-in/fallback");
        }

        internal static Entity SelectProcedureDoctor(
            DatabaseEntryRef<GameDBSkill>[] qualifications,
            Department department,
            Room room,
            ProcedureScene scene,
            bool hasToBeFree,
            Entity patient,
            GameDBEmployeeRole role)
        {
            if (department == null)
            {
                return null;
            }

            List<Entity> candidates = new List<Entity>();

            foreach (EntityIDPointer<Entity> pointer in department.m_departmentPersistentData.m_doctors)
            {
                Entity doctor = pointer.GetEntity();
                if (!MatchesProcedureCandidate(
                        doctor,
                        qualifications,
                        department,
                        room,
                        scene,
                        hasToBeFree,
                        patient,
                        role))
                {
                    continue;
                }

                candidates.Add(doctor);
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            BehaviorPatient behaviorPatient =
                patient == null ? null : patient.GetComponent<BehaviorPatient>();

            return SelectBest(
                candidates,
                behaviorPatient,
                department,
                qualifications,
                hasToBeFree ? "procedure/free" : "procedure/available");
        }


        private static List<Entity> GetWalkInCandidates(
            BehaviorPatient patient,
            Department department,
            Room waitingRoom,
            bool requireLocal,
            bool requireFree)
        {
            List<Entity> candidates = new List<Entity>();

            foreach (EntityIDPointer<Entity> pointer in department.m_departmentPersistentData.m_doctors)
            {
                Entity doctor = pointer.GetEntity();

                if (MatchesWalkInCandidate(
                        doctor,
                        patient,
                        department,
                        waitingRoom,
                        requireLocal,
                        requireFree))
                {
                    candidates.Add(doctor);
                }
            }

            return candidates;
        }

        private static bool MatchesWalkInCandidate(
            Entity doctor,
            BehaviorPatient patient,
            Department department,
            Room waitingRoom,
            bool requireLocal,
            bool requireFree)
        {
            if (doctor == null || patient == null || department == null)
            {
                return false;
            }

            BehaviorDoctor behavior = doctor.GetComponent<BehaviorDoctor>();
            EmployeeComponent employee = doctor.GetComponent<EmployeeComponent>();

            if (behavior == null ||
                employee == null ||
                employee.m_state == null ||
                employee.m_state.m_skillSet == null ||
                employee.m_state.m_department == null ||
                DayTime.Instance == null ||
                Database.Instance == null ||
                employee.IsFired() ||
                employee.ShouldGoToTraining())
            {
                return false;
            }

            if (employee.m_state.m_department.GetEntity() != department ||
                employee.m_state.m_shift != DayTime.Instance.GetShift())
            {
                return false;
            }

            GameDBEmployeeRole examinationRole =
                Database.Instance.GetEntry<GameDBEmployeeRole>("EMPL_ROLE_EXM_TRT");

            if (examinationRole != null && !employee.HasRole(examinationRole))
            {
                return false;
            }

            Room homeRoom = GetValidClinicHomeRoom(employee);
            if (homeRoom == null)
            {
                return false;
            }

            if (requireLocal &&
                waitingRoom != null &&
                (waitingRoom.m_roomPersistentData.m_nearbyRooms == null ||
                 !waitingRoom.m_roomPersistentData.m_nearbyRooms.Contains(homeRoom)))
            {
                return false;
            }

            if (requireFree && !behavior.IsFree(patient.m_entity))
            {
                return false;
            }

            return true;
        }

        private static bool MatchesProcedureCandidate(
            Entity doctor,
            DatabaseEntryRef<GameDBSkill>[] qualifications,
            Department department,
            Room room,
            ProcedureScene scene,
            bool hasToBeFree,
            Entity patient,
            GameDBEmployeeRole role)
        {
            if (doctor == null)
            {
                return false;
            }

            BehaviorDoctor behavior = doctor.GetComponent<BehaviorDoctor>();
            EmployeeComponent employee = doctor.GetComponent<EmployeeComponent>();

            if (behavior == null ||
                employee == null ||
                employee.m_state == null ||
                employee.m_state.m_skillSet == null ||
                DayTime.Instance == null ||
                MapScriptInterface.Instance == null ||
                employee.IsFired() ||
                employee.ShouldGoToTraining())
            {
                return false;
            }

            if (hasToBeFree &&
                employee.m_state.m_shift != DayTime.Instance.GetShift())
            {
                return false;
            }

            if (MapScriptInterface.Instance.IsDoctorSelectedAlready(scene, doctor))
            {
                return false;
            }

            if (role != null && !employee.HasRole(role))
            {
                return false;
            }

            if (!HasAnyRequiredQualification(employee, qualifications))
            {
                return false;
            }

            Room homeRoom = GetValidClinicHomeRoom(employee);
            if (homeRoom == null)
            {
                return false;
            }

            if (room != null &&
                (room.m_roomPersistentData.m_nearbyRooms == null ||
                 !room.m_roomPersistentData.m_nearbyRooms.Contains(homeRoom)))
            {
                return false;
            }

            if (hasToBeFree && !behavior.IsFree(patient))
            {
                return false;
            }

            return true;
        }

        private static Room GetValidClinicHomeRoom(EmployeeComponent employee)
        {
            if (employee == null ||
                employee.m_state == null ||
                employee.m_state.m_homeRoom == null ||
                Database.Instance == null)
            {
                return null;
            }

            Room homeRoom = employee.m_state.m_homeRoom.GetEntity();

            if (homeRoom == null ||
                homeRoom.m_roomPersistentData.m_valid != RoomValidity.OK)
            {
                return null;
            }

            GameDBRoomType onCallRoom =
                Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_ON_CALL_ROOM");

            if (homeRoom.m_roomPersistentData.m_roomType.Entry == onCallRoom)
            {
                return null;
            }

            return homeRoom;
        }

        private static bool HasAnyRequiredQualification(
            EmployeeComponent employee,
            DatabaseEntryRef<GameDBSkill>[] qualifications)
        {
            if (qualifications == null)
            {
                return true;
            }

            foreach (DatabaseEntryRef<GameDBSkill> qualification in qualifications)
            {
                if (qualification != null &&
                    qualification.Entry != null &&
                    employee.HasSkill(qualification.Entry))
                {
                    return true;
                }
            }

            return false;
        }

        private static Entity SelectBest(
            List<Entity> candidates,
            BehaviorPatient patient,
            Department department,
            DatabaseEntryRef<GameDBSkill>[] qualifications,
            string context)
        {
            float minimumScore = float.MaxValue;
            List<ClinicScoredDoctor> scored =
                new List<ClinicScoredDoctor>();
            string decisionKey =
                context + ":" +
                (patient == null
                    ? "no-patient"
                    : HfoDiagnostics.EntityKey(patient.m_entity));

            foreach (Entity doctor in candidates)
            {
                float currentAssignedLoad;
                float recentRoomLoad;
                float distance;
                float clinicalFit;

                float score =
                    GetCandidateScore(
                        doctor,
                        patient,
                        department,
                        qualifications,
                        out currentAssignedLoad,
                        out recentRoomLoad,
                        out distance,
                        out clinicalFit);

                scored.Add(
                    new ClinicScoredDoctor
                    {
                        Doctor = doctor,
                        Score = score
                    });

                if (score < minimumScore)
                {
                    minimumScore = score;
                }

                HfoDiagnostics.LogState(
                    "ClinicDoctor",
                    decisionKey + ":candidate:" + HfoDiagnostics.EntityKey(doctor),
                    "department=" + HfoDiagnostics.DepartmentId(department) +
                    " context=" + context +
                    " candidate=" + HfoDiagnostics.EntityName(doctor) +
                    " level=" + HfoDiagnostics.DoctorLevel(doctor) +
                    " assigned=" + HfoDiagnostics.Number(currentAssignedLoad) +
                    " roomLoad=" + HfoDiagnostics.Number(recentRoomLoad) +
                    " distance=" + HfoDiagnostics.Number(distance) +
                    " fit=" + HfoDiagnostics.Number(clinicalFit) +
                    " score=" + HfoDiagnostics.Number(score));
            }

            if (scored.Count == 0)
            {
                return null;
            }

            int highestLevel = int.MinValue;
            int nearTieCount = 0;
            List<Entity> finalists = new List<Entity>();

            foreach (ClinicScoredDoctor candidate in scored)
            {
                if (candidate.Score > minimumScore + ScoreTieTolerance)
                {
                    continue;
                }

                nearTieCount++;

                int level =
                    HfoDiagnostics.DoctorLevel(candidate.Doctor);

                if (level > highestLevel)
                {
                    highestLevel = level;
                    finalists.Clear();
                    finalists.Add(candidate.Doctor);
                }
                else if (level == highestLevel)
                {
                    finalists.Add(candidate.Doctor);
                }
            }

            if (finalists.Count == 0)
            {
                return null;
            }

            Entity selected =
                finalists[
                    UnityEngine.Random.Range(
                        0,
                        finalists.Count)];

            HfoDiagnostics.LogState(
                "ClinicDoctor",
                decisionKey + ":selected",
                "department=" + HfoDiagnostics.DepartmentId(department) +
                " context=" + context +
                " selected=" + HfoDiagnostics.EntityName(selected) +
                " level=" + HfoDiagnostics.DoctorLevel(selected) +
                " operationalBestScore=" +
                    HfoDiagnostics.Number(minimumScore) +
                " nearTieCandidates=" + nearTieCount +
                " finalistsAtBestLevel=" + finalists.Count +
                (patient == null
                    ? string.Empty
                    : " patient=" +
                      HfoDiagnostics.EntityName(patient.m_entity)));

            return selected;
        }


        private static float GetCandidateScore(
            Entity doctor,
            BehaviorPatient patient,
            Department department,
            DatabaseEntryRef<GameDBSkill>[] qualifications,
            out float currentAssignedLoad,
            out float recentRoomLoad,
            out float distance,
            out float clinicalFit)
        {
            currentAssignedLoad =
                GetCurrentAssignedOutpatientLoad(
                    department,
                    doctor);
            recentRoomLoad =
                GetRecentClinicRoomLoad(doctor);

            float combinedLoad =
                currentAssignedLoad +
                recentRoomLoad * RecentRoomLoadFactor;

            distance =
                GetDistanceScore(
                    patient,
                    doctor);
            clinicalFit =
                GetClinicClinicalFit(
                    doctor,
                    department,
                    qualifications);

            return
                combinedLoad *
                    FlowOptimizerConfig.ClinicLoadBalancingWeight +
                distance *
                    FlowOptimizerConfig.ClinicDistanceWeight -
                clinicalFit *
                    FlowOptimizerConfig.ClinicClinicalFitWeight;
        }

        private static float GetCurrentAssignedOutpatientLoad(
            Department department,
            Entity doctor)
        {
            if (department == null || doctor == null)
            {
                return 0.0f;
            }

            float load = 0.0f;

            foreach (EntityIDPointer<Entity> pointer in department.m_departmentPersistentData.m_patients)
            {
                Entity patient = pointer.GetEntity();
                if (patient == null)
                {
                    continue;
                }

                BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
                HospitalizationComponent hospitalization =
                    patient.GetComponent<HospitalizationComponent>();

                if (behavior == null ||
                    behavior.m_state == null ||
                    (hospitalization != null && hospitalization.IsHospitalized()) ||
                    behavior.m_state.m_doctor == null ||
                    behavior.m_state.m_doctor.GetEntity() != doctor)
                {
                    continue;
                }

                PatientState state = behavior.m_state.m_patientState;
                if (state == PatientState.Left ||
                    state == PatientState.Leaving ||
                    state == PatientState.Dead)
                {
                    continue;
                }

                load += 1.0f;
            }

            return load;
        }

        private static float GetRecentClinicRoomLoad(Entity doctor)
        {
            EmployeeComponent employee = doctor.GetComponent<EmployeeComponent>();
            Room room = GetValidClinicHomeRoom(employee);

            if (room == null)
            {
                return 1.0f;
            }

            if (DayTime.Instance == null)
            {
                return 1.0f;
            }

            int workloadPercent =
                DayTime.Instance.GetShift() == Shift.DAY
                    ? room.GetWorkloadDayPercent()
                    : room.GetWorkloadNightPercent();

            return Clamp((float)workloadPercent / 100.0f, 0.0f, 1.0f);
        }

        private static float GetDistanceScore(
            BehaviorPatient patient,
            Entity doctor)
        {
            if (patient == null ||
                patient.m_entity == null ||
                doctor == null)
            {
                return 0.0f;
            }

            WalkComponent patientWalk =
                patient.m_entity.GetComponent<WalkComponent>();
            WalkComponent doctorWalk =
                doctor.GetComponent<WalkComponent>();

            if (patientWalk == null || doctorWalk == null)
            {
                return 0.0f;
            }

            int floorDifference =
                Math.Abs(
                    patientWalk.GetFloorIndex() -
                    doctorWalk.GetFloorIndex());

            int squaredDistance =
                (doctorWalk.GetCurrentTile() -
                 patientWalk.GetCurrentTile()).LengthSquared();

            float tileDistance =
                (float)Math.Sqrt((double)squaredDistance);

            return
                (tileDistance + (float)floorDifference * DistanceNormalizationTiles) /
                DistanceNormalizationTiles;
        }

        private static float GetClinicClinicalFit(
            Entity doctor,
            Department department,
            DatabaseEntryRef<GameDBSkill>[] qualifications)
        {
            EmployeeComponent employee = doctor.GetComponent<EmployeeComponent>();
            if (employee == null || employee.m_state == null)
            {
                return 0.0f;
            }

            float weighted = 0.0f;
            float totalWeight = 0.0f;

            GameDBDepartment departmentType =
                department == null ? null : department.GetDepartmentType();

            if (departmentType != null &&
                departmentType.FirstDoctorSpecialization != null &&
                departmentType.FirstDoctorSpecialization.Entry != null)
            {
                AddFit(
                    ref weighted,
                    ref totalWeight,
                    NormalizeSkill(
                        employee.GetSkillLevel(
                            departmentType.FirstDoctorSpecialization.Entry)),
                    0.35f);
            }

            AddFit(
                ref weighted,
                ref totalWeight,
                NormalizeSkill(
                    employee.GetSkillLevel("SKILL_DOC_QUALIF_GENERAL_MEDICINE")),
                0.20f);

            float diagnosisSkill =
                employee.GetSkillLevel("SKILL_DOC_QUALIF_DIAGNOSIS");

            PerkComponent perks = doctor.GetComponent<PerkComponent>();
            if (perks != null &&
                perks.m_perkSet != null &&
                perks.m_perkSet.HasPerk("PERK_DIAGNOSTIC_GENIUS"))
            {
                diagnosisSkill += 1.0f;
            }

            AddFit(
                ref weighted,
                ref totalWeight,
                NormalizeSkill(diagnosisSkill),
                0.25f);

            if (qualifications != null)
            {
                float bestRequiredSkill = 0.0f;

                foreach (DatabaseEntryRef<GameDBSkill> qualification in qualifications)
                {
                    if (qualification == null || qualification.Entry == null)
                    {
                        continue;
                    }

                    float skill =
                        employee.GetSkillLevel(qualification.Entry);

                    if (skill > bestRequiredSkill)
                    {
                        bestRequiredSkill = skill;
                    }
                }

                if (bestRequiredSkill > 0.0f)
                {
                    AddFit(
                        ref weighted,
                        ref totalWeight,
                        NormalizeSkill(bestRequiredSkill),
                        0.35f);
                }
            }

            float efficiencyMultiplier =
                employee.GetEfficiencyTimeMultiplier();

            if (efficiencyMultiplier > 0.0f)
            {
                float relativeEfficiency =
                    Clamp(1.0f / efficiencyMultiplier, 0.50f, 1.25f) / 1.25f;

                AddFit(
                    ref weighted,
                    ref totalWeight,
                    relativeEfficiency,
                    0.20f);
            }

            return totalWeight <= 0.0f ? 0.0f : weighted / totalWeight;
        }

        private static void AddFit(
            ref float weighted,
            ref float totalWeight,
            float value,
            float weight)
        {
            weighted += value * weight;
            totalWeight += weight;
        }

        private static float NormalizeSkill(float skill)
        {
            return Clamp(skill, 0.0f, MaximumDoctorSkill) / MaximumDoctorSkill;
        }

        private sealed class ClinicScoredDoctor
        {
            internal Entity Doctor;
            internal float Score;
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
