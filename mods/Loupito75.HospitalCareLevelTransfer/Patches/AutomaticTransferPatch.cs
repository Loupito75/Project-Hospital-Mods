using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalCareLevelTransfer.Patches
{
    [HarmonyPatch(typeof(HospitalizationComponent), nameof(HospitalizationComponent.SwitchState))]
    internal static class AutomaticTransferPatch
    {
        private const float CompletedCheckUpTimeTolerance = 0.001f;

        private static void Prefix(
            HospitalizationComponent __instance,
            HospitalizationState state,
            out HospitalizationState __state)
        {
            __state = HospitalizationState.Idle;

            if (CareLevelTransferConfig.HduToRegularChancePercent <= 0)
            {
                return;
            }

            if (__instance != null && __instance.m_state != null)
            {
                __state = __instance.m_state.m_hospitalizationState;
            }
        }

        private static void Postfix(
            HospitalizationComponent __instance,
            HospitalizationState state,
            HospitalizationState __state)
        {
            if (CareLevelTransferConfig.HduToRegularChancePercent <= 0 ||
                __instance == null ||
                __instance.m_state == null ||
                __state != HospitalizationState.OverridenByNurseCheckUp ||
                state != HospitalizationState.InBed ||
                __instance.m_state.m_hospitalizationState != HospitalizationState.InBed)
            {
                return;
            }

            if (__instance.m_state.m_checkUpTime > CompletedCheckUpTimeTolerance)
            {
                return;
            }

            Entity patient = __instance.m_entity;
            if (patient == null)
            {
                return;
            }

            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            if (behavior == null || behavior.m_state == null || behavior.m_state.m_waitingForPlayer)
            {
                return;
            }

            if (!IsNativeTransferStateSafe(patient, behavior, __instance))
            {
                return;
            }

            if (CareLevelTransferService.GetEligibility(patient) != CareLevelTransferEligibility.Eligible ||
                !CareLevelTransferService.HasFreeRegularWardBed(patient) ||
                !CareLevelTransferConfig.ShouldAttemptAutomaticTransfer())
            {
                return;
            }

            CareLevelTransferService.StartTransferToRegularWard(patient);
        }

        private static bool IsNativeTransferStateSafe(
            Entity patient,
            BehaviorPatient behavior,
            HospitalizationComponent hospitalization)
        {
            if (patient == null ||
                behavior == null ||
                behavior.m_state == null ||
                hospitalization == null ||
                hospitalization.m_state == null ||
                Database.Instance == null)
            {
                return false;
            }

            Department department = behavior.GetDepartment();
            if (department == null)
            {
                return false;
            }

            GameDBDepartment icuDepartment = Database.Instance.GetEntry<GameDBDepartment>("DPT_ICU");
            if (icuDepartment != null && department.GetDepartmentType() == icuDepartment)
            {
                return false;
            }

            if (behavior.m_state.m_patientState == PatientState.Collapsing ||
                hospitalization.m_state.m_hospitalizationState == HospitalizationState.Collapsing ||
                hospitalization.m_state.m_hospitalizationState == HospitalizationState.CollapsingInBed ||
                hospitalization.IsBeingTransported() ||
                hospitalization.IsBeingStabilized() ||
                hospitalization.IsDeadOnPathology())
            {
                return false;
            }

            ProcedureComponent procedures = patient.GetComponent<ProcedureComponent>();
            if (hospitalization.m_state.m_hospitalizationState == HospitalizationState.BeingTreated &&
                procedures != null &&
                procedures.IsPatientInSurgery())
            {
                return false;
            }

            return true;
        }
    }
}
