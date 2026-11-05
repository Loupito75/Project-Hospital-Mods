using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    internal static class ObservationReadmissionGuard
    {
        internal static bool IsFreshDoctorReviewActive(
            Entity patient)
        {
            if (patient == null)
            {
                return false;
            }

            BehaviorPatient behavior =
                patient.GetComponent<BehaviorPatient>();
            if (behavior == null ||
                behavior.m_state == null ||
                behavior.m_state.m_patientState !=
                    PatientState.GoingToDoctor ||
                behavior.m_state.m_waitingRoom != null ||
                behavior.m_state.m_labSpecialist != null ||
                behavior.m_state.m_doctor == null)
            {
                return false;
            }

            Entity doctor =
                behavior.m_state.m_doctor.GetEntity();
            if (doctor == null)
            {
                return false;
            }

            BehaviorDoctor doctorBehavior =
                doctor.GetComponent<BehaviorDoctor>();
            if (doctorBehavior == null ||
                doctorBehavior.CurrentPatient != patient)
            {
                return false;
            }

            WalkComponent walk =
                patient.GetComponent<WalkComponent>();
            return walk != null && !walk.IsBusy();
        }
    }

    // Vanilla can legitimately prescribe Observation again after an earlier
    // Observation. HFX only hides that repeated option while the patient is
    // between consultations; once a doctor has actually received the patient
    // for a fresh review, Vanilla is allowed to offer Observation again.
    [HarmonyPatch(
        typeof(ProcedureComponent),
        "GetAllTreatmentsForMedicalCondition")]
    internal static class PreventImmediateObservationReadmissionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ProcedureComponent __instance)
        {
            if (__instance == null)
            {
                return;
            }

            if (__instance.m_state == null ||
                __instance.m_state.m_procedureQueue == null ||
                __instance.m_treatmentAvailability == null ||
                Database.Instance == null)
            {
                return;
            }

            GameDBTreatment observation =
                Database.Instance.GetEntry<GameDBTreatment>(
                    "TRT_HOSPITALIZATION_OBSERVATION");

            if (observation == null ||
                !__instance.m_state.m_procedureQueue
                    .HasFinishedTreatment(observation) ||
                !__instance.m_treatmentAvailability
                    .ContainsKey(observation))
            {
                return;
            }

            ProcedureSceneAvailability previous =
                __instance.m_treatmentAvailability[
                    observation];

            bool freshDoctorReview =
                ObservationReadmissionGuard
                    .IsFreshDoctorReviewActive(
                        __instance.m_entity);

            if (freshDoctorReview)
            {
                if (HfxDiagnostics.Enabled &&
                    ProcedureScene.IsProcedureAvailable(
                        previous))
                {
                    HfxDiagnostics.ObservedRateLimited(
                        "PreventImmediateObservationReadmission",
                        "ProcedureComponent.GetAllTreatmentsForMedicalCondition",
                        "repeat-observation-allowed-after-doctor-review",
                        "patient={" +
                        HfxDiagnostics.EntityContext(
                            __instance.m_entity) +
                        "} availability=" +
                        previous +
                        " freshDoctorReview=true action=Vanilla",
                        "PreventImmediateObservationReadmission|doctor-review|" +
                        HfxDiagnostics.EntityLabel(
                            __instance.m_entity),
                        3000);
                }

                return;
            }

            bool selectable =
                ProcedureScene.IsProcedureAvailable(
                    previous);

            if (HfxDiagnostics.Enabled &&
                selectable)
            {
                if (!FixConfig.PreventImmediateObservationReadmission)
                {
                    HfxDiagnostics.ObservedRateLimited(
                        "PreventImmediateObservationReadmission",
                        "ProcedureComponent.GetAllTreatmentsForMedicalCondition",
                        "repeat-observation-before-doctor-review",
                        "patient={" +
                        HfxDiagnostics.EntityContext(
                            __instance.m_entity) +
                        "} availability=" +
                        previous +
                        " freshDoctorReview=false action=Vanilla",
                        "PreventImmediateObservationReadmission|off|" +
                        HfxDiagnostics.EntityLabel(
                            __instance.m_entity),
                        3000);
                }
            }

            if (!FixConfig.PreventImmediateObservationReadmission)
            {
                return;
            }

            __instance.m_treatmentAvailability
                .Remove(observation);

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.InterventionRateLimited(
                    "PreventImmediateObservationReadmission",
                    "ProcedureComponent.GetAllTreatmentsForMedicalCondition",
                    "repeat-observation-hidden-until-doctor-review",
                    "patient={" +
                    HfxDiagnostics.EntityContext(
                        __instance.m_entity) +
                    "} previousAvailability=" +
                    previous +
                    " selectable=" +
                    selectable +
                    " freshDoctorReview=false action=removed",
                    "PreventImmediateObservationReadmission|hidden|" +
                    HfxDiagnostics.EntityLabel(
                        __instance.m_entity),
                    3000);
            }
        }
    }
}
