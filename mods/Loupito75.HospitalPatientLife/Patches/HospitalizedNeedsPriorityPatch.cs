using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class HospitalizedNeedsPriorityRules
    {
        private static readonly Dictionary<Entity, bool> NightBladderFailureLogged =
            new Dictionary<Entity, bool>();

        private static readonly Dictionary<Entity, string> UrgentFailureSignatures =
            new Dictionary<Entity, string>();

        private static bool PassesPersonalNeedSafety(
            HospitalizationComponent hospitalization,
            BehaviorPatient behavior)
        {
            if (hospitalization == null || behavior == null || hospitalization.m_state == null)
            {
                return false;
            }

            if (hospitalization.m_state.m_hospitalizationTreatment == null ||
                hospitalization.m_state.m_hospitalizationTreatment.Entry == null)
            {
                return false;
            }

            GameDBTreatment hospitalizationTreatment =
                hospitalization.m_state.m_hospitalizationTreatment.Entry;

            if (hospitalizationTreatment == Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_ICU") ||
                hospitalizationTreatment == Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_TRAUMA"))
            {
                return false;
            }

            if (!hospitalization.IsAllowedToWalk())
            {
                return false;
            }

            if (behavior.m_state.m_sentAway ||
                behavior.m_state.m_deathTriggered ||
                behavior.m_state.m_collapseSymptom != null ||
                behavior.m_state.m_collapseProcedure != null)
            {
                return false;
            }

            // Conservative HospitalPatientLife safety gate. The native mobility
            // authority remains HospitalizationComponent.IsAllowedToWalk().
            if (behavior.GetWorstKnownHazard() >= SymptomHazard.High)
            {
                return false;
            }

            return true;
        }

        internal static bool CanRunPersonalNeed(
            HospitalizationComponent hospitalization,
            BehaviorPatient behavior,
            ProcedureComponent procedures)
        {
            if (!PassesPersonalNeedSafety(hospitalization, behavior) || procedures == null)
            {
                return false;
            }

            if (procedures.IsBusy() ||
                procedures.m_state.m_reservedProcedureScript != null ||
                hospitalization.m_state.m_procedureReservationStatus != ProcedureReservationStatus.NONE)
            {
                return false;
            }

            return true;
        }

        internal static bool CanChainPersonalNeed(
            HospitalizationComponent hospitalization,
            BehaviorPatient behavior,
            ProcedureComponent procedures)
        {
            if (!CanRunPersonalNeed(hospitalization, behavior, procedures) ||
                procedures.m_state == null ||
                procedures.m_state.m_procedureQueue == null)
            {
                return false;
            }

            // A chained personal need is optional. Any medical work already queued or
            // reserved wins and lets HospitalizationComponent return to its native
            // InBed/SelectNextStep cycle instead.
            ProcedureQueue queue = procedures.m_state.m_procedureQueue;
            if (queue.m_plannedTreatmentStates.Count > 0 ||
                queue.m_plannedExaminationStates.Count > 0 ||
                queue.m_labProcedures.Count > 0)
            {
                return false;
            }

            return true;
        }

        internal static bool CanRunNightBathroomTrip(
            HospitalizationComponent hospitalization,
            BehaviorPatient behavior,
            ProcedureComponent procedures)
        {
            if (!PassesPersonalNeedSafety(hospitalization, behavior) || procedures == null)
            {
                return false;
            }

            // UpdateStateSleeping has just given native SelectNextStep() its normal
            // chance to start a medical step. If the patient is still sleeping, only an
            // actually active procedure prevents a bathroom trip. Do not reject a trip
            // merely because a future procedure remains reserved.
            return !procedures.IsBusy();
        }

        internal static bool ShouldSuppressFreeTimeForUrgentBladder(
            Entity patient,
            ProcedureComponent procedures)
        {
            if (patient == null || procedures == null)
            {
                return false;
            }

            HospitalizationComponent hospitalization = patient.GetComponent<HospitalizationComponent>();
            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            MoodComponent mood = patient.GetComponent<MoodComponent>();
            if (hospitalization == null || behavior == null || mood == null ||
                hospitalization.m_state == null ||
                hospitalization.m_state.m_hospitalizationState != HospitalizationState.InBed ||
                !PassesPersonalNeedSafety(hospitalization, behavior))
            {
                return false;
            }

            Need bladderNeed = mood.GetNeed("NEED_BLADDER");
            float bladder = bladderNeed == null ? 0f : bladderNeed.m_currentValue;
            return bladder >= HospitalPatientLifeConfig.UrgentBladderThreshold;
        }

        internal static bool IsHospitalizedFreeTimeProcedure(GameDBProcedure procedure)
        {
            if (procedure == null || Database.Instance == null)
            {
                return false;
            }

            GameDBProcedure freeTime = Database.Instance.GetEntry<GameDBProcedure>(
                "CONTROL_PROCEDURE_HOSPITALZED_FREE_TIME");
            GameDBProcedure lyingFreeTime = Database.Instance.GetEntry<GameDBProcedure>(
                "CONTROL_PROCEDURE_HOSPITALZED_LYING_FREE_TIME");

            return object.ReferenceEquals(procedure, freeTime) ||
                object.ReferenceEquals(procedure, lyingFreeTime);
        }

        internal static ProcedureSceneAvailability GetBladderAvailability(
            BehaviorPatient behavior,
            ProcedureComponent procedures)
        {
            GameDBNeed bladderNeed = Database.Instance.GetEntry<GameDBNeed>("NEED_BLADDER");
            Department department = behavior.m_state.m_department.GetEntity();

            if (bladderNeed == null || bladderNeed.Procedure == null || department == null)
            {
                return ProcedureSceneAvailability.EQUIPMENT_UNAVAILABLE;
            }

            // Use the same requested access as native BehaviorPatient.CheckNeeds().
            // PatientBladderProcedureScenePatch may widen only this bladder scene to
            // PATIENT_PROCEDURE when the patient's current movement rights are blue.
            return procedures.GetProcedureAvailabilty(
                bladderNeed.Procedure,
                behavior.m_entity,
                department,
                AccessRights.PATIENT,
                EquipmentListRules.ONLY_FREE_SAME_FLOOR);
        }

        internal static bool TryStartNightBladder(
            HospitalizationComponent hospitalization,
            BehaviorPatient behavior,
            ProcedureComponent procedures,
            float bladder)
        {
            if (!NightBathroomChancePatch.CanStart(hospitalization, bladder))
            {
                return false;
            }

            ProcedureSceneAvailability availability =
                GetBladderAvailability(behavior, procedures);
            Entity patient = hospitalization.m_entity;

            if (availability != ProcedureSceneAvailability.AVAILABLE)
            {
                if (!NightBladderFailureLogged.ContainsKey(patient))
                {
                    NightBladderFailureLogged[patient] = true;
                    HospitalizedPatientTrace.Log(
                        HospitalizedPatientTrace.GetName(patient) +
                        " | night-bladder=NO_DESTINATION" +
                        " | gameTime=" + HospitalizedPatientTrace.GetGameTime() +
                        " | bladder=" + bladder.ToString("0.0") +
                        " | availability=" + availability +
                        " | searchAccess=" + PatientBladderRules.GetEffectiveSearchAccess(
                            patient,
                            AccessRights.PATIENT) +
                        " | floor=" + HospitalizedPatientTrace.GetFloor(patient) +
                        NeedDestinationDiagnostics.BuildBladder(patient));
                }
                return false;
            }

            Department department = behavior.m_state.m_department.GetEntity();
            GameDBNeed bladderNeed = Database.Instance.GetEntry<GameDBNeed>("NEED_BLADDER");
            if (department == null || bladderNeed == null || bladderNeed.Procedure == null)
            {
                return false;
            }

            // Start the same bladder procedure used by BehaviorPatient.CheckNeeds().
            // The scene patch only widens this WC search when the patient's current
            // movement rights already allow PATIENT_PROCEDURE.
            procedures.StartProcedure(
                bladderNeed.Procedure,
                patient,
                department,
                AccessRights.PATIENT,
                EquipmentListRules.ONLY_FREE_SAME_FLOOR);

            hospitalization.GetCovered(LyingState.UNCOVERED);
            hospitalization.m_state.m_procedureReservationStatus = ProcedureReservationStatus.NONE;
            hospitalization.m_nextStepDelay = 0f;
            hospitalization.SwitchState(HospitalizationState.FulfillingNeeds);
            NightBladderFailureLogged.Remove(patient);
            ResetUrgentFailure(patient);
            NightBathroomChancePatch.MarkStarted(patient);

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | night-bladder=STARTED" +
                " | gameTime=" + HospitalizedPatientTrace.GetGameTime() +
                " | source=HPL_FALLBACK" +
                " | bladder=" + bladder.ToString("0.0") +
                " | searchAccess=" + PatientBladderRules.GetEffectiveSearchAccess(
                    patient,
                    AccessRights.PATIENT) +
                " | floor=" + HospitalizedPatientTrace.GetFloor(patient));

            return true;
        }

        internal static bool ShouldLogUrgentFailure(
            Entity patient,
            string bladderAvailability,
            int floor)
        {
            if (patient == null)
            {
                return false;
            }

            string signature = bladderAvailability + "|" + floor;
            string previousSignature;
            if (UrgentFailureSignatures.TryGetValue(patient, out previousSignature) &&
                previousSignature == signature)
            {
                return false;
            }

            UrgentFailureSignatures[patient] = signature;
            return true;
        }

        internal static void ResetUrgentFailure(Entity patient)
        {
            if (patient != null)
            {
                UrgentFailureSignatures.Remove(patient);
            }
        }

        internal static void ResetNightFailure(Entity patient)
        {
            if (patient != null)
            {
                NightBladderFailureLogged.Remove(patient);
            }
        }
    }

    [HarmonyPatch(typeof(HospitalizationComponent), "SelectNextStep")]
    internal static class HospitalizedNeedsPriorityPatch
    {
        private static bool Prefix(HospitalizationComponent __instance, ref bool __result)
        {
            if (__instance == null || __instance.m_entity == null || __instance.m_state == null)
            {
                return true;
            }

            if (__instance.m_state.m_hospitalizationState != HospitalizationState.InBed)
            {
                return true;
            }

            Entity patient = __instance.m_entity;
            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            ProcedureComponent procedures = patient.GetComponent<ProcedureComponent>();
            MoodComponent mood = patient.GetComponent<MoodComponent>();

            if (behavior == null || procedures == null || mood == null ||
                !HospitalizedNeedsPriorityRules.CanRunPersonalNeed(
                    __instance,
                    behavior,
                    procedures))
            {
                return true;
            }

            HospitalizedNeedsPriorityRules.ResetNightFailure(patient);

            if (__instance.m_state.m_lunchReady && !__instance.m_state.m_lunchEaten)
            {
                return true;
            }

            Need hungerNeed = mood.GetNeed("NEED_HUNGER_PATIENT");
            Need bladderNeed = mood.GetNeed("NEED_BLADDER");
            float hunger = hungerNeed == null ? 0f : hungerNeed.m_currentValue;
            float bladder = bladderNeed == null ? 0f : bladderNeed.m_currentValue;

            if (hunger < HospitalPatientLifeConfig.UrgentHungerThreshold &&
                bladder < HospitalPatientLifeConfig.UrgentBladderThreshold)
            {
                HospitalizedNeedsPriorityRules.ResetUrgentFailure(patient);
                return true;
            }

            // Delegate actual need ordering, availability, destination selection and
            // procedure start to the native BehaviorPatient implementation.
            if (!behavior.CheckNeeds(AccessRights.PATIENT))
            {
                string bladderAvailability = "n/a";
                if (bladder >= HospitalPatientLifeConfig.UrgentBladderThreshold)
                {
                    bladderAvailability =
                        HospitalizedNeedsPriorityRules.GetBladderAvailability(
                            behavior,
                            procedures).ToString();
                }

                int floor = HospitalizedPatientTrace.GetFloor(patient);
                if (HospitalizedNeedsPriorityRules.ShouldLogUrgentFailure(
                    patient,
                    bladderAvailability,
                    floor))
                {
                    string diagnostics = bladder >= HospitalPatientLifeConfig.UrgentBladderThreshold
                        ? NeedDestinationDiagnostics.BuildBladder(patient)
                        : string.Empty;

                    HospitalizedPatientTrace.Log(
                        HospitalizedPatientTrace.GetName(patient) +
                        " | urgent-needs-window=NO_DESTINATION" +
                        " | hunger=" + hunger.ToString("0.0") +
                        " | bladder=" + bladder.ToString("0.0") +
                        " | bladderAvailability=" + bladderAvailability +
                        " | searchAccess=" + PatientBladderRules.GetEffectiveSearchAccess(
                            patient,
                            AccessRights.PATIENT) +
                        " | floor=" + floor +
                        diagnostics);
                }
                return true;
            }

            HospitalizedNeedsPriorityRules.ResetUrgentFailure(patient);

            __instance.GetCovered(LyingState.UNCOVERED);
            __instance.m_state.m_procedureReservationStatus = ProcedureReservationStatus.NONE;
            __instance.m_nextStepDelay = 0f;
            __instance.SwitchState(HospitalizationState.FulfillingNeeds);

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | urgent-needs-window=STARTED" +
                " | hunger=" + hunger.ToString("0.0") +
                " | bladder=" + bladder.ToString("0.0") +
                " | floor=" + HospitalizedPatientTrace.GetFloor(patient));

            __result = true;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class HospitalizedUrgentBladderFreeTimePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(ProcedureComponent),
                "GetProcedureAvailabilty",
                new Type[]
                {
                    typeof(GameDBProcedure),
                    typeof(Entity),
                    typeof(Department),
                    typeof(AccessRights),
                    typeof(EquipmentListRules)
                });
        }

        private static bool Prefix(
            ProcedureComponent __instance,
            GameDBProcedure procedure,
            Entity patient,
            ref ProcedureSceneAvailability __result)
        {
            if (!HospitalizedNeedsPriorityRules.IsHospitalizedFreeTimeProcedure(procedure) ||
                !HospitalizedNeedsPriorityRules.ShouldSuppressFreeTimeForUrgentBladder(
                    patient,
                    __instance))
            {
                return true;
            }

            // Do not let optional free-time procedures take the patient out of InBed
            // while an urgent bladder need is waiting for a WC. Treatments/examinations
            // are untouched, so native medical work keeps its normal priority. Staying
            // InBed lets the normal 0.5-0.9 s hospitalization cycle retry the need.
            __result = ProcedureSceneAvailability.EQUIPMENT_UNAVAILABLE;
            return false;
        }
    }

    [HarmonyPatch(typeof(HospitalizationComponent), "UpdateStateSleeping")]
    internal static class HospitalizedNightBladderPatch
    {
        private static void Prefix(HospitalizationComponent __instance, out float __state)
        {
            __state = __instance == null ? 0f : __instance.m_nextStepDelay;
        }

        private static void Postfix(HospitalizationComponent __instance, float __state)
        {
            // Native UpdateStateSleeping checks SelectNextStep every two seconds and
            // gives medical work the first chance. Only try the bladder afterwards, and
            // only if native code left the patient in Sleeping.
            if (__instance == null || __instance.m_entity == null || __instance.m_state == null ||
                __state <= 2f ||
                !HospitalPatientLifeConfig.AllowNightBathroomTrips ||
                DayTime.Instance == null || DayTime.Instance.IsOpenForStaff() ||
                __instance.m_state.m_hospitalizationState != HospitalizationState.Sleeping)
            {
                return;
            }

            Entity patient = __instance.m_entity;
            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            ProcedureComponent procedures = patient.GetComponent<ProcedureComponent>();
            MoodComponent mood = patient.GetComponent<MoodComponent>();

            if (behavior == null || procedures == null || mood == null ||
                !HospitalizedNeedsPriorityRules.CanRunNightBathroomTrip(
                    __instance,
                    behavior,
                    procedures))
            {
                return;
            }

            Need bladderNeed = mood.GetNeed("NEED_BLADDER");
            float bladder = bladderNeed == null ? 0f : bladderNeed.m_currentValue;
            if (bladder < HospitalPatientLifeConfig.UrgentBladderThreshold)
            {
                HospitalizedNeedsPriorityRules.ResetNightFailure(patient);
                return;
            }

            HospitalizedNeedsPriorityRules.TryStartNightBladder(
                __instance,
                behavior,
                procedures,
                bladder);
        }
    }
}
