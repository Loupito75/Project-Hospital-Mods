using GLib;
using Lopital;

namespace HospitalFlowOptimizer
{
    internal static class FollowUpQueuePriority
    {
        internal static bool CanAdvance(Room room, Entity character)
        {
            BehaviorPatient patient;
            int bonusMinutes;
            string reason;

            if (!TryGetFollowUp(
                    room,
                    character,
                    out patient,
                    out bonusMinutes,
                    out reason))
            {
                LogNotFollowUp(room, character, patient, reason);
                return false;
            }

            float candidateWaitingTime =
                patient.m_state.m_continuousWaitingTime;
            float candidateEffectiveWaitingTime =
                candidateWaitingTime +
                GetBonusSeconds(bonusMinutes);

            foreach (EntityIDPointer<Entity> pointer in
                room.m_roomPersistentData.m_characterQueue)
            {
                Entity queuedCharacter = pointer.GetEntity();

                if (queuedCharacter == character)
                {
                    LogDecision(
                        "hfo-allowed",
                        room,
                        character,
                        patient,
                        bonusMinutes,
                        candidateWaitingTime,
                        candidateEffectiveWaitingTime,
                        null,
                        null,
                        0,
                        0.0f,
                        0.0f,
                        false);
                    return true;
                }

                if (queuedCharacter == null)
                {
                    continue;
                }

                BehaviorPatient queuedPatient =
                    queuedCharacter.GetComponent<BehaviorPatient>();

                if (queuedPatient == null ||
                    queuedPatient.m_state == null ||
                    !IsActivelyWaiting(queuedPatient.m_state.m_patientState))
                {
                    continue;
                }

                if (queuedPatient.m_state.m_priority > patient.m_state.m_priority)
                {
                    LogDecision(
                        "blocked-higher-priority",
                        room,
                        character,
                        patient,
                        bonusMinutes,
                        candidateWaitingTime,
                        candidateEffectiveWaitingTime,
                        queuedCharacter,
                        queuedPatient,
                        0,
                        queuedPatient.m_state.m_continuousWaitingTime,
                        queuedPatient.m_state.m_continuousWaitingTime,
                        false);
                    return false;
                }

                if (queuedPatient.m_state.m_priority <
                    patient.m_state.m_priority)
                {
                    continue;
                }

                int queuedBonusMinutes;
                bool queuedIsFollowUp;
                float queuedEffectiveWaitingTime =
                    GetEffectiveWaitingTime(
                        room,
                        queuedCharacter,
                        queuedPatient,
                        out queuedBonusMinutes,
                        out queuedIsFollowUp);

                if (queuedEffectiveWaitingTime >= candidateEffectiveWaitingTime)
                {
                    LogDecision(
                        "blocked-same-priority-older",
                        room,
                        character,
                        patient,
                        bonusMinutes,
                        candidateWaitingTime,
                        candidateEffectiveWaitingTime,
                        queuedCharacter,
                        queuedPatient,
                        queuedBonusMinutes,
                        queuedPatient.m_state.m_continuousWaitingTime,
                        queuedEffectiveWaitingTime,
                        queuedIsFollowUp);
                    return false;
                }
            }

            LogDecision(
                "candidate-missing-from-queue",
                room,
                character,
                patient,
                bonusMinutes,
                candidateWaitingTime,
                candidateEffectiveWaitingTime,
                null,
                null,
                0,
                0.0f,
                0.0f,
                false);
            return false;
        }

        internal static void LogVanillaTurn(Room room, Entity character)
        {
            if (!FlowOptimizerConfig.DoctorDiagnosticLogging)
            {
                return;
            }

            BehaviorPatient patient;
            int bonusMinutes;
            string reason;

            if (!TryGetFollowUp(
                    room,
                    character,
                    out patient,
                    out bonusMinutes,
                    out reason))
            {
                return;
            }

            float waitingTime = patient.m_state.m_continuousWaitingTime;
            float effectiveWaitingTime =
                waitingTime + GetBonusSeconds(bonusMinutes);

            LogDecision(
                "vanilla-allowed",
                room,
                character,
                patient,
                bonusMinutes,
                waitingTime,
                effectiveWaitingTime,
                null,
                null,
                0,
                0.0f,
                0.0f,
                false);
        }

        private static float GetEffectiveWaitingTime(
            Room room,
            Entity character,
            BehaviorPatient patient,
            out int bonusMinutes,
            out bool isFollowUp)
        {
            BehaviorPatient followUpPatient;
            string reason;

            isFollowUp =
                TryGetFollowUp(
                    room,
                    character,
                    true,
                    out followUpPatient,
                    out bonusMinutes,
                    out reason);

            if (isFollowUp)
            {
                return patient.m_state.m_continuousWaitingTime +
                    GetBonusSeconds(bonusMinutes);
            }

            bonusMinutes = 0;
            return patient.m_state.m_continuousWaitingTime;
        }

        private static bool TryGetFollowUp(
            Room room,
            Entity character,
            out BehaviorPatient patient,
            out int bonusMinutes,
            out string reason)
        {
            return TryGetFollowUp(
                room,
                character,
                false,
                out patient,
                out bonusMinutes,
                out reason);
        }

        private static bool TryGetFollowUp(
            Room room,
            Entity character,
            bool queueMembershipAlreadyKnown,
            out BehaviorPatient patient,
            out int bonusMinutes,
            out string reason)
        {
            patient = null;
            bonusMinutes = 0;
            reason = "invalid-context";

            if (room == null ||
                room.m_roomPersistentData == null ||
                character == null ||
                Database.Instance == null)
            {
                return false;
            }

            GameDBRoomType waitingRoomType =
                Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_WAITING");

            if (waitingRoomType == null ||
                room.m_roomPersistentData.m_roomType == null ||
                room.m_roomPersistentData.m_roomType.Entry != waitingRoomType)
            {
                reason = "not-clinic-waiting-room";
                return false;
            }

            if (room.m_roomPersistentData.m_characterQueue == null ||
                (!queueMembershipAlreadyKnown &&
                 !room.m_roomPersistentData.m_characterQueue.Contains(character)))
            {
                reason = "not-main-waiting-queue";
                return false;
            }

            patient = character.GetComponent<BehaviorPatient>();

            if (patient == null || patient.m_state == null)
            {
                reason = "not-patient";
                return false;
            }

            if (!IsActivelyWaiting(patient.m_state.m_patientState))
            {
                reason = "not-actively-waiting";
                return false;
            }

            if (patient.m_state.m_waitingRoom == null ||
                patient.m_state.m_waitingRoom.GetEntity() != room)
            {
                reason = "waiting-room-mismatch";
                return false;
            }

            if (patient.m_state.m_doctor == null ||
                patient.m_state.m_doctor.GetEntity() == null)
            {
                reason = "doctor-missing";
                return false;
            }

            if (patient.m_state.m_labSpecialist != null &&
                patient.m_state.m_labSpecialist.GetEntity() != null)
            {
                reason = "lab-specialist-active";
                return false;
            }

            HospitalizationComponent hospitalization =
                character.GetComponent<HospitalizationComponent>();

            if (hospitalization != null && hospitalization.IsHospitalized())
            {
                reason = "hospitalized";
                return false;
            }

            ProcedureComponent procedures =
                character.GetComponent<ProcedureComponent>();

            if (procedures == null ||
                procedures.m_state == null ||
                procedures.m_state.m_procedureQueue == null)
            {
                reason = "procedure-queue-missing";
                return false;
            }

            ProcedureQueue queue = procedures.m_state.m_procedureQueue;
            GameDBExamination interview =
                Database.Instance.GetEntry<GameDBExamination>("EXM_INTERVIEW");

            if (interview == null)
            {
                reason = "interview-definition-missing";
                return false;
            }

            Entity doctor =
                patient.m_state.m_doctor.GetEntity();
            FinishedExaminationAnalysis examinations =
                AnalyzeFinishedExaminations(
                    character,
                    doctor,
                    procedures.m_state.m_lastProcedureID,
                    queue,
                    interview);

            if (!examinations.InterviewFinished)
            {
                reason = "interview-not-finished";
                return false;
            }

            if (HasPendingPatientLabHandoff(queue))
            {
                reason = "pending-lab-handoff";
                return false;
            }

            if (!examinations.HasPostInterviewExamination)
            {
                reason = "no-post-interview-exam";
                return false;
            }

            if (!examinations.LastProcedureIsFinishedExamination)
            {
                reason = "last-procedure-not-finished-exam";
                return false;
            }

            bonusMinutes =
                GetStableBonusMinutes(
                    examinations.StableBonusHash);

            reason = "eligible";
            return true;
        }

        private struct FinishedExaminationAnalysis
        {
            internal bool InterviewFinished;
            internal bool HasPostInterviewExamination;
            internal bool LastProcedureIsFinishedExamination;
            internal uint StableBonusHash;
        }

        private static FinishedExaminationAnalysis AnalyzeFinishedExaminations(
            Entity patient,
            Entity doctor,
            string lastProcedureId,
            ProcedureQueue queue,
            GameDBExamination interview)
        {
            FinishedExaminationAnalysis result =
                new FinishedExaminationAnalysis();

            int variation =
                FlowOptimizerConfig.FollowUpQueueRandomVariationMinutes;
            bool buildStableHash =
                variation > 0 &&
                patient != null &&
                doctor != null;

            uint hash = 2166136261u;
            if (buildStableHash)
            {
                MixHash(ref hash, patient.GetEntityID());
                MixHash(ref hash, doctor.GetEntityID());
                MixHash(ref hash, lastProcedureId);
                MixHash(
                    ref hash,
                    (uint)(queue.m_finishedExaminations == null
                        ? 0
                        : queue.m_finishedExaminations.Count));
            }

            if (queue.m_finishedExaminations != null)
            {
                foreach (ExaminationState examinationState in
                    queue.m_finishedExaminations)
                {
                    if (examinationState == null ||
                        examinationState.m_examination.Entry == null)
                    {
                        continue;
                    }

                    GameDBExamination examination =
                        examinationState.m_examination.Entry;
                    string examinationId =
                        examination.DatabaseID.ToString();

                    if (buildStableHash)
                    {
                        MixHash(
                            ref hash,
                            examinationId);
                    }

                    if (examination == interview)
                    {
                        result.InterviewFinished = true;
                        continue;
                    }

                    result.HasPostInterviewExamination = true;

                    if (!string.IsNullOrEmpty(lastProcedureId) &&
                        examinationId == lastProcedureId)
                    {
                        result.LastProcedureIsFinishedExamination = true;
                    }
                }
            }

            result.StableBonusHash = hash;
            return result;
        }

        private static bool HasPendingPatientLabHandoff(ProcedureQueue queue)
        {
            if (queue.m_labProcedures == null)
            {
                return false;
            }

            foreach (EntityIDPointer<LabProcedure> pointer in
                queue.m_labProcedures)
            {
                LabProcedure labProcedure = pointer.GetEntity();

                if (labProcedure == null || labProcedure.m_state == null)
                {
                    continue;
                }

                if (labProcedure.m_state.m_labProcedureState ==
                        LabProcedureState.SamplingFinished ||
                    labProcedure.m_state.m_labProcedureState ==
                        LabProcedureState.ResultsReady)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsActivelyWaiting(PatientState state)
        {
            return state == PatientState.WaitingSitting ||
                state == PatientState.WaitingStandingIdle;
        }

        private static float GetBonusSeconds(int bonusMinutes)
        {
            if (bonusMinutes <= 0 || DayTime.Instance == null)
            {
                return 0.0f;
            }

            return DayTime.Instance.IngameTimeHoursToRealTimeSeconds(
                (float)bonusMinutes / 60.0f);
        }

        private static float GetIngameMinutes(float realTimeSeconds)
        {
            if (DayTime.Instance == null)
            {
                return 0.0f;
            }

            return DayTime.Instance.RealTimeSecondsToIngameTimeHours(
                realTimeSeconds) * 60.0f;
        }

        private static int GetStableBonusMinutes(
            uint stableHash)
        {
            int baseMinutes =
                FlowOptimizerConfig.FollowUpQueueBaseBonusMinutes;
            int variation =
                FlowOptimizerConfig.FollowUpQueueRandomVariationMinutes;

            if (variation <= 0)
            {
                return baseMinutes;
            }

            int span = variation * 2 + 1;
            int offset =
                (int)(stableHash % (uint)span) - variation;
            int result = baseMinutes + offset;

            return result < 0 ? 0 : result;
        }

        private static void LogNotFollowUp(
            Room room,
            Entity character,
            BehaviorPatient patient,
            string reason)
        {
            if (!FlowOptimizerConfig.DoctorDiagnosticLogging ||
                !ShouldDiagnose(room, character))
            {
                return;
            }

            ProcedureComponent procedures =
                character.GetComponent<ProcedureComponent>();
            ProcedureQueue queue =
                procedures == null ||
                procedures.m_state == null
                    ? null
                    : procedures.m_state.m_procedureQueue;

            string state =
                "not-follow-up:" +
                (reason ?? "unknown") + ":" +
                GetLastProcedureId(procedures) + ":" +
                GetFinishedExamCount(queue).ToString();

            HfoDiagnostics.LogStateChange(
                "FollowUpQueue",
                HfoDiagnostics.EntityKey(character),
                state,
                "decision=not-follow-up" +
                "; reason=" + (reason ?? "unknown") +
                "; patient=" + HfoDiagnostics.EntityName(character) +
                "; patientState=" +
                    (patient == null || patient.m_state == null
                        ? "<none>"
                        : patient.m_state.m_patientState.ToString()) +
                "; priority=" +
                    (patient == null || patient.m_state == null
                        ? "<none>"
                        : patient.m_state.m_priority.ToString()) +
                "; doctor=" +
                    GetDoctorName(patient) +
                "; lastProcedure=" +
                    GetLastProcedureId(procedures) +
                "; finishedExams=" +
                    GetFinishedExamCount(queue).ToString() +
                "; labProcedures=" +
                    GetLabProcedureCount(queue).ToString());
        }

        private static void LogDecision(
            string decision,
            Room room,
            Entity character,
            BehaviorPatient patient,
            int bonusMinutes,
            float waitingTime,
            float effectiveWaitingTime,
            Entity blocker,
            BehaviorPatient blockerPatient,
            int blockerBonusMinutes,
            float blockerWaitingTime,
            float blockerEffectiveWaitingTime,
            bool blockerIsFollowUp)
        {
            if (!FlowOptimizerConfig.DoctorDiagnosticLogging ||
                character == null ||
                patient == null ||
                patient.m_state == null)
            {
                return;
            }

            string episode = GetEpisodeKey(character, patient);
            string blockerKey =
                blocker == null
                    ? "none"
                    : HfoDiagnostics.EntityKey(blocker);

            HfoDiagnostics.LogStateChange(
                "FollowUpQueue",
                HfoDiagnostics.EntityKey(character),
                episode + ":" + decision + ":" + blockerKey,
                "decision=" + decision +
                "; patient=" + HfoDiagnostics.EntityName(character) +
                "; priority=" + patient.m_state.m_priority +
                "; waitMinutes=" +
                    HfoDiagnostics.Number(GetIngameMinutes(waitingTime)) +
                "; bonusMinutes=" + bonusMinutes +
                "; effectiveMinutes=" +
                    HfoDiagnostics.Number(
                        GetIngameMinutes(effectiveWaitingTime)) +
                "; doctor=" + GetDoctorName(patient) +
                "; lastProcedure=" +
                    GetLastProcedureId(
                        character.GetComponent<ProcedureComponent>()) +
                "; blocker=" +
                    (blocker == null
                        ? "<none>"
                        : HfoDiagnostics.EntityName(blocker)) +
                "; blockerPriority=" +
                    (blockerPatient == null ||
                     blockerPatient.m_state == null
                        ? "<none>"
                        : blockerPatient.m_state.m_priority.ToString()) +
                "; blockerWaitMinutes=" +
                    HfoDiagnostics.Number(
                        GetIngameMinutes(blockerWaitingTime)) +
                "; blockerBonusMinutes=" + blockerBonusMinutes +
                "; blockerEffectiveMinutes=" +
                    HfoDiagnostics.Number(
                        GetIngameMinutes(blockerEffectiveWaitingTime)) +
                "; blockerFollowUp=" +
                    blockerIsFollowUp.ToString().ToLowerInvariant());
        }

        private static bool ShouldDiagnose(Room room, Entity character)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                character == null ||
                Database.Instance == null)
            {
                return false;
            }

            GameDBRoomType waitingRoomType =
                Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_WAITING");

            return waitingRoomType != null &&
                room.m_roomPersistentData.m_roomType != null &&
                room.m_roomPersistentData.m_roomType.Entry == waitingRoomType &&
                room.m_roomPersistentData.m_characterQueue != null &&
                room.m_roomPersistentData.m_characterQueue.Contains(character) &&
                character.GetComponent<BehaviorPatient>() != null;
        }

        private static string GetEpisodeKey(
            Entity character,
            BehaviorPatient patient)
        {
            ProcedureComponent procedures =
                character.GetComponent<ProcedureComponent>();
            ProcedureQueue queue =
                procedures == null ||
                procedures.m_state == null
                    ? null
                    : procedures.m_state.m_procedureQueue;

            Entity doctor =
                patient.m_state.m_doctor == null
                    ? null
                    : patient.m_state.m_doctor.GetEntity();

            return
                GetLastProcedureId(procedures) + ":" +
                GetFinishedExamCount(queue).ToString() + ":" +
                (doctor == null
                    ? "no-doctor"
                    : doctor.GetEntityID().ToString());
        }

        private static string GetDoctorName(BehaviorPatient patient)
        {
            if (patient == null ||
                patient.m_state == null ||
                patient.m_state.m_doctor == null)
            {
                return "<none>";
            }

            return HfoDiagnostics.EntityName(
                patient.m_state.m_doctor.GetEntity());
        }

        private static string GetLastProcedureId(
            ProcedureComponent procedures)
        {
            if (procedures == null ||
                procedures.m_state == null ||
                string.IsNullOrEmpty(procedures.m_state.m_lastProcedureID))
            {
                return "<none>";
            }

            return procedures.m_state.m_lastProcedureID;
        }

        private static int GetFinishedExamCount(ProcedureQueue queue)
        {
            return queue == null ||
                queue.m_finishedExaminations == null
                    ? 0
                    : queue.m_finishedExaminations.Count;
        }

        private static int GetLabProcedureCount(ProcedureQueue queue)
        {
            return queue == null ||
                queue.m_labProcedures == null
                    ? 0
                    : queue.m_labProcedures.Count;
        }

        private static void MixHash(ref uint hash, uint value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 16777619u;
            }
        }

        private static void MixHash(ref uint hash, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                MixHash(ref hash, 0u);
                return;
            }

            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }
            }
        }
    }
}
