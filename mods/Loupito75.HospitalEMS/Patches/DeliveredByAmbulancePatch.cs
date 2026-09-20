using System;
using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalEMS.Patches
{
    internal static class AmbulanceDeliveryTracker
    {
        private static readonly List<uint> SetupInProgress =
            new List<uint>();
        private static readonly List<uint> ExaminationPhaseHandledDuringSetup =
            new List<uint>();

        internal static void BeginSetup(Entity patient)
        {
            if (patient == null)
            {
                return;
            }

            uint entityId = patient.GetEntityID();
            if (!SetupInProgress.Contains(entityId))
            {
                SetupInProgress.Add(entityId);
            }

            ExaminationPhaseHandledDuringSetup.Remove(entityId);
        }

        internal static bool IsSetupInProgress(Entity patient)
        {
            return patient != null &&
                SetupInProgress.Contains(patient.GetEntityID());
        }

        internal static bool IsExaminationPhaseHandled(Entity patient)
        {
            return patient != null &&
                ExaminationPhaseHandledDuringSetup.Contains(
                    patient.GetEntityID());
        }

        internal static void MarkExaminationPhaseHandled(Entity patient)
        {
            if (patient == null)
            {
                return;
            }

            uint entityId = patient.GetEntityID();
            if (!ExaminationPhaseHandledDuringSetup.Contains(entityId))
            {
                ExaminationPhaseHandledDuringSetup.Add(entityId);
            }
        }

        internal static void EndSetup(Entity patient)
        {
            if (patient == null)
            {
                return;
            }

            uint entityId = patient.GetEntityID();
            SetupInProgress.Remove(entityId);
            ExaminationPhaseHandledDuringSetup.Remove(entityId);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorPatient),
        nameof(BehaviorPatient.SetupDeliveredByAmbulance))]
    internal static class DeliveredByAmbulanceSetupPatch
    {
        private static void Prefix(BehaviorPatient __instance)
        {
            if (__instance == null || __instance.m_entity == null)
            {
                return;
            }

            AmbulanceDeliveryTracker.BeginSetup(__instance.m_entity);
        }

        private static void Postfix(BehaviorPatient __instance)
        {
            if (__instance == null || __instance.m_entity == null)
            {
                return;
            }

            Entity patient = __instance.m_entity;

            try
            {
                if (!AmbulanceDeliveryTracker.IsExaminationPhaseHandled(patient))
                {
                    AmbulanceDeliveryTracker.MarkExaminationPhaseHandled(patient);
                    Plugin.Log.LogWarning(
                        "Ambulance setup reached its Postfix before the expected " +
                        "critical-treatment planning hook for " + patient.Name +
                        " [entity " + patient.GetEntityID() +
                        "]; applying prehospital examinations as a fallback.");

                    try
                    {
                        PrehospitalAssessmentService.ApplyExaminations(__instance);
                    }
                    catch (Exception exception)
                    {
                        Plugin.Log.LogError(
                            "Prehospital examination fallback failed for " +
                            patient.Name + " [entity " + patient.GetEntityID() +
                            "]; vanilla ambulance routing will continue unchanged: " +
                            exception.GetType().Name + ": " + exception.Message);
                    }
                }

                try
                {
                    PrehospitalAssessmentService.ApplyCareAfterNativePlacement(
                        __instance);
                }
                catch (Exception exception)
                {
                    Plugin.Log.LogError(
                        "Prehospital care failed for " + patient.Name +
                        " [entity " + patient.GetEntityID() +
                        "]; vanilla ambulance state will continue unchanged: " +
                        exception.GetType().Name + ": " + exception.Message);
                }
            }
            finally
            {
                AmbulanceDeliveryTracker.EndSetup(patient);
            }
        }
    }

    [HarmonyPatch(
        typeof(ProcedureComponent),
        nameof(ProcedureComponent.PlanAllTreatments),
        new Type[]
        {
            typeof(MedicalCondition),
            typeof(bool),
            typeof(bool)
        })]
    internal static class AmbulanceCriticalTreatmentPlanningPatch
    {
        private static void Postfix(
            ProcedureComponent __instance,
            bool onlyCritical)
        {
            if (!onlyCritical ||
                __instance == null ||
                __instance.m_entity == null)
            {
                return;
            }

            Entity patient = __instance.m_entity;
            if (!AmbulanceDeliveryTracker.IsSetupInProgress(patient) ||
                AmbulanceDeliveryTracker.IsExaminationPhaseHandled(patient))
            {
                return;
            }

            BehaviorPatient patientBehavior =
                patient.GetComponent<BehaviorPatient>();
            if (patientBehavior == null)
            {
                return;
            }

            AmbulanceDeliveryTracker.MarkExaminationPhaseHandled(patient);

            try
            {
                PrehospitalAssessmentService.ApplyExaminations(patientBehavior);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    "Prehospital examination phase failed for " + patient.Name +
                    " [entity " + patient.GetEntityID() +
                    "]; vanilla ambulance setup will continue unchanged: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }
    }
}
