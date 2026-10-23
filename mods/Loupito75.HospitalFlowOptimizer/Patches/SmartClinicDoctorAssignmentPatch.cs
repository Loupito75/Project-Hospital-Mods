using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFlowOptimizer.Patches
{
    [HarmonyPatch(typeof(BehaviorPatient), "FindClinicDoctor")]
    internal static class SmartClinicDoctorAssignmentPatch
    {
        private static bool Prefix(BehaviorPatient __instance)
        {
            if (!FlowOptimizerConfig.SmartClinicDoctorAssignment)
            {
                return true;
            }

            if (__instance == null ||
                __instance.m_entity == null ||
                __instance.m_state == null)
            {
                return true;
            }

            HospitalizationComponent hospitalization =
                __instance.m_entity.GetComponent<HospitalizationComponent>();

            if (hospitalization != null && hospitalization.IsHospitalized())
            {
                return true;
            }

            Entity currentDoctor =
                __instance.m_state.m_doctor == null
                    ? null
                    : __instance.m_state.m_doctor.GetEntity();

            if (__instance.m_state.m_doctorAssignedByPlayer)
            {
                if (currentDoctor != null)
                {
                    if (FlowOptimizerConfig.DoctorDiagnosticLogging)
                    {
                        HfoDiagnostics.LogState(
                            "ClinicDoctor",
                            "manual:" + HfoDiagnostics.EntityKey(__instance.m_entity),
                            "Preserved player-assigned doctor " +
                            HfoDiagnostics.EntityName(currentDoctor) +
                            " for " +
                            HfoDiagnostics.EntityName(__instance.m_entity) + ".");
                    }
                    return false;
                }

                return true;
            }

            Department department =
                __instance.m_state.m_department == null
                    ? null
                    : __instance.m_state.m_department.GetEntity();

            if (department == null)
            {
                return true;
            }

            Room waitingRoom =
                __instance.m_state.m_waitingRoom == null
                    ? null
                    : __instance.m_state.m_waitingRoom.GetEntity();

            if (currentDoctor != null &&
                SmartClinicDoctorSelector.CanKeepCurrentDoctor(
                    __instance,
                    currentDoctor,
                    department))
            {
                return false;
            }

            Entity selected =
                SmartClinicDoctorSelector.SelectWalkInDoctor(
                    __instance,
                    department,
                    waitingRoom);

            __instance.SetDoctor(selected);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface.FindRandomClinicDoctorWithQualification))]
    internal static class SmartClinicProcedureDoctorPatch
    {
        private static bool Prefix(
            DatabaseEntryRef<GameDBSkill>[] qualifications,
            Department department,
            Room room,
            ProcedureScene scene,
            bool hasToBeFree,
            Entity patient,
            GameDBEmployeeRole role,
            ref Entity __result)
        {
            if (!FlowOptimizerConfig.SmartClinicDoctorAssignment)
            {
                return true;
            }

            // In ProcedureSceneFactory, the hasToBeFree=false / patient=null
            // call is only an availability probe used to distinguish STAFF_BUSY
            // from STAFF_UNAVAILABLE. The returned doctor's identity is discarded.
            // Let vanilla answer that probe and reserve HFO scoring for the real
            // free-doctor selection tied to a patient.
            if (!hasToBeFree && patient == null)
            {
                return true;
            }

            __result =
                SmartClinicDoctorSelector.SelectProcedureDoctor(
                    qualifications,
                    department,
                    room,
                    scene,
                    hasToBeFree,
                    patient,
                    role);

            return false;
        }
    }
}
