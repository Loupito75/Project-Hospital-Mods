using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    [HarmonyPatch]
    internal static class OutpatientExamClothingSequencePatch
    {
        private sealed class ClothingSequence
        {
            internal Room Room;
            internal DressLevel RestoreLevel;
            internal DressLevel RetainedLevel;
        }

        private static readonly Dictionary<Entity, ClothingSequence> Sequences =
            new Dictionary<Entity, ClothingSequence>();

        private static readonly HashSet<Entity> Restoring =
            new HashSet<Entity>();

        [HarmonyPatch(
            typeof(ProcedureScript),
            nameof(ProcedureScript.SpeakCharacter),
            new System.Type[]
            {
                typeof(Entity),
                typeof(string),
                typeof(string),
                typeof(float)
            })]
        [HarmonyPrefix]
        private static bool DressInstructionPrefix(
            ProcedureScript __instance,
            string message)
        {
            if (message != "BUBBLE_DRESS")
            {
                return true;
            }

            Entity patientEntity;
            DressLevel requestedLevel;

            if (!TryGetRedundantDressSuppression(
                    __instance,
                    out patientEntity,
                    out requestedLevel))
            {
                return true;
            }

            // Vanilla announces BUBBLE_DRESS before entering the dress
            // state. If HFX will deliberately suppress that exact clothing
            // change, suppress the associated instruction too.
            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Intervention(
                    "AvoidRedundantExamClothingChanges",
                    "ProcedureScript.SpeakCharacter",
                    "dress-instruction-suppressed",
                    "patient=" +
                    HfxDiagnostics.EntityLabel(patientEntity) +
                    " requested=" + requestedLevel);
            }

            return false;
        }

        internal static bool ShouldSuppressRedundantDressVisuals(
            ProcedureScript script)
        {
            Entity patientEntity;
            DressLevel requestedLevel;

            return TryGetRedundantDressSuppression(
                script,
                out patientEntity,
                out requestedLevel);
        }

        private static bool TryGetRedundantDressSuppression(
            ProcedureScript script,
            out Entity patientEntity,
            out DressLevel requestedLevel)
        {
            patientEntity = null;
            requestedLevel = DressLevel.FULL;

            if (!FixConfig.AvoidRedundantExamClothingChanges ||
                script == null ||
                !IsManagedDressExamination(script) ||
                script.m_stateData == null ||
                script.m_stateData.m_procedureScene == null ||
                script.m_stateData.m_procedureScene.m_patient == null ||
                script.m_stateData.GetProcedure() == null)
            {
                return false;
            }

            patientEntity =
                script.m_stateData.m_procedureScene
                    .m_patient.GetEntity();
            if (patientEntity == null)
            {
                return false;
            }

            BehaviorPatient patient =
                patientEntity.GetComponent<BehaviorPatient>();
            if (patient == null)
            {
                return false;
            }

            requestedLevel =
                script.GetParam(0) < 1f
                    ? patient.GetTreatedSymptomDressLevel(
                        script.m_stateData
                            .GetProcedure().DressLevel)
                    : patient.GetTreatedSymptomDressLevel(
                        script.m_stateData
                            .GetProcedure().DressLevelAfter);

            DressLevel effectiveLevel;
            return !TryResolveVisualClothingChange(
                patientEntity,
                requestedLevel,
                out effectiveLevel);
        }

        [HarmonyPatch(typeof(AnimModelComponent), nameof(AnimModelComponent.SetClothingLevel))]
        [HarmonyPrefix]
        private static bool ClothingLevelPrefix(
            AnimModelComponent __instance,
            ref DressLevel dressLevel)
        {
            if (!FixConfig.AvoidRedundantExamClothingChanges ||
                __instance == null ||
                __instance.m_entity == null ||
                __instance.m_state == null ||
                Restoring.Contains(__instance.m_entity))
            {
                return true;
            }

            Entity patientEntity = __instance.m_entity;
            BehaviorPatient patient = patientEntity.GetComponent<BehaviorPatient>();
            HospitalizationComponent hospitalization =
                patientEntity.GetComponent<HospitalizationComponent>();

            if (patient == null || hospitalization == null)
            {
                return true;
            }

            ClothingSequence sequence;
            if (hospitalization.IsHospitalized())
            {
                RestoreAndClear(patientEntity);
                return true;
            }

            ProcedureScript examinationScript =
                GetCurrentManagedExamination(patientEntity);

            if (!Sequences.TryGetValue(patientEntity, out sequence))
            {
                if (examinationScript == null ||
                    examinationScript.GetParam(0) >= 1f ||
                    !IsExaminationExposureLevel(
                        examinationScript.m_stateData.GetProcedure().DressLevel) ||
                    !IsExaminationExposureLevel(dressLevel) ||
                    __instance.m_state.m_dressLevel == dressLevel)
                {
                    return true;
                }

                Room room = GetCurrentRoom(patientEntity);
                if (room == null)
                {
                    return true;
                }

                sequence = new ClothingSequence
                {
                    Room = room,
                    RestoreLevel = __instance.m_state.m_dressLevel,
                    RetainedLevel = dressLevel
                };
                Sequences[patientEntity] = sequence;

                if (HfxDiagnostics.Enabled)
                {
                    HfxDiagnostics.Observed(
                        "AvoidRedundantExamClothingChanges",
                        "AnimModelComponent.SetClothingLevel",
                        "sequence-started",
                        "patient=" +
                        HfxDiagnostics.EntityLabel(patientEntity) +
                        " restore=" + sequence.RestoreLevel +
                        " retained=" + sequence.RetainedLevel);
                }
                return true;
            }

            if (!IsStillInSequenceRoom(patientEntity, sequence))
            {
                RestoreAndClear(patientEntity);
                return true;
            }

            if (examinationScript != null)
            {
                if (examinationScript.GetParam(0) >= 1f)
                {
                    sequence.RestoreLevel = dressLevel;
                    if (HfxDiagnostics.Enabled)
                    {
                        HfxDiagnostics.Intervention(
                            "AvoidRedundantExamClothingChanges",
                            "AnimModelComponent.SetClothingLevel",
                            "change-suppressed",
                            "patient=" +
                            HfxDiagnostics.EntityLabel(patientEntity) +
                            " reason=examination-post-step restore=" +
                            dressLevel);
                    }
                    return false;
                }

                if (dressLevel == DressLevel.FULL ||
                    dressLevel == sequence.RestoreLevel)
                {
                    if (HfxDiagnostics.Enabled)
                    {
                        HfxDiagnostics.Intervention(
                            "AvoidRedundantExamClothingChanges",
                            "AnimModelComponent.SetClothingLevel",
                            "change-suppressed",
                            "patient=" +
                            HfxDiagnostics.EntityLabel(patientEntity) +
                            " requested=" + dressLevel +
                            " retained=" + sequence.RetainedLevel);
                    }
                    return false;
                }

                if (IsExaminationExposureLevel(dressLevel))
                {
                    DressLevel requested =
                        dressLevel;
                    DressLevel merged =
                        MergeExposure(sequence.RetainedLevel, dressLevel);
                    sequence.RetainedLevel = merged;
                    dressLevel = merged;

                    if (HfxDiagnostics.Enabled)
                    {
                        HfxDiagnostics.Intervention(
                            "AvoidRedundantExamClothingChanges",
                            "AnimModelComponent.SetClothingLevel",
                            "exposure-merged",
                            "patient=" +
                            HfxDiagnostics.EntityLabel(patientEntity) +
                            " requested=" + requested +
                            " applied=" + merged);
                    }
                    return true;
                }

                Sequences.Remove(patientEntity);
                return true;
            }

            if (dressLevel == DressLevel.FULL ||
                dressLevel == sequence.RestoreLevel)
            {
                return false;
            }

            Sequences.Remove(patientEntity);
            return true;
        }

        [HarmonyPatch(typeof(BehaviorPatient), nameof(BehaviorPatient.SwitchState))]
        [HarmonyPrefix]
        private static void PatientStatePrefix(
            BehaviorPatient __instance,
            PatientState state)
        {
            if (!FixConfig.AvoidRedundantExamClothingChanges ||
                __instance == null ||
                __instance.m_entity == null ||
                !Sequences.ContainsKey(__instance.m_entity))
            {
                return;
            }

            if (state != PatientState.GoingToDoctor &&
                state != PatientState.BeingExamined)
            {
                RestoreAndClear(__instance.m_entity);
            }
        }

        [HarmonyPatch(typeof(BehaviorPatient), nameof(BehaviorPatient.Leave))]
        [HarmonyPrefix]
        private static void LeavePrefix(BehaviorPatient __instance)
        {
            if (FixConfig.AvoidRedundantExamClothingChanges &&
                __instance != null)
            {
                RestoreAndClear(__instance.m_entity);
            }
        }

        [HarmonyPatch(typeof(BehaviorPatient), nameof(BehaviorPatient.Update))]
        [HarmonyPrefix]
        private static void PatientUpdatePrefix(BehaviorPatient __instance)
        {
            if (!FixConfig.AvoidRedundantExamClothingChanges ||
                __instance == null ||
                __instance.m_entity == null)
            {
                return;
            }

            Entity patientEntity = __instance.m_entity;
            ClothingSequence sequence;
            if (Sequences.TryGetValue(patientEntity, out sequence))
            {
                if (!IsStillInSequenceRoom(patientEntity, sequence))
                {
                    RestoreAndClear(patientEntity);
                }
                return;
            }

            TryRecoverConsultationSequence(__instance);
        }

        [HarmonyPatch]
        private static class NonExaminationProcedureStartPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                MethodInfo[] methods = typeof(ProcedureComponent).GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);

                foreach (MethodInfo method in methods)
                {
                    if (method.Name == "StartTreatment" ||
                        method.Name == "StartReservedTreatment" ||
                        method.Name == "StartProcedure" ||
                        method.Name == "StartReservedProcedure")
                    {
                        yield return method;
                    }
                }
            }

            [HarmonyPrefix]
            private static void Prefix(ProcedureComponent __instance)
            {
                if (FixConfig.AvoidRedundantExamClothingChanges &&
                    __instance != null)
                {
                    RestoreAndClear(__instance.m_entity);
                }
            }
        }

        [HarmonyPatch(typeof(InGameMenuController), "Destroy")]
        [HarmonyPostfix]
        private static void ClearOnUnload()
        {
            Sequences.Clear();
            Restoring.Clear();
        }

        private static bool IsExaminationExposureLevel(DressLevel level)
        {
            return level == DressLevel.HALF ||
                level == DressLevel.HALF_TOP ||
                level == DressLevel.UNDERWEAR ||
                level == DressLevel.NONE;
        }

        private static DressLevel MergeExposure(
            DressLevel retained,
            DressLevel requested)
        {
            if (retained == DressLevel.NONE || requested == DressLevel.NONE)
            {
                return DressLevel.NONE;
            }

            if (retained == DressLevel.UNDERWEAR ||
                requested == DressLevel.UNDERWEAR)
            {
                return DressLevel.UNDERWEAR;
            }

            if ((retained == DressLevel.HALF &&
                 requested == DressLevel.HALF_TOP) ||
                (retained == DressLevel.HALF_TOP &&
                 requested == DressLevel.HALF))
            {
                return DressLevel.UNDERWEAR;
            }

            return requested;
        }

        private static void TryRecoverConsultationSequence(
            BehaviorPatient patient)
        {
            if (patient.m_state == null ||
                patient.m_state.m_patientState != PatientState.GoingToDoctor ||
                patient.m_state.m_doctor == null ||
                patient.m_state.m_doctor.GetEntity() == null)
            {
                return;
            }

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            AnimModelComponent animation =
                patient.GetComponent<AnimModelComponent>();
            Room room = GetCurrentRoom(patient.m_entity);

            if (hospitalization == null ||
                hospitalization.IsHospitalized() ||
                animation == null ||
                animation.m_state == null ||
                !IsExaminationExposureLevel(animation.m_state.m_dressLevel) ||
                room == null)
            {
                return;
            }

            EmployeeComponent doctor =
                patient.m_state.m_doctor.GetEntity()
                    .GetComponent<EmployeeComponent>();
            if (doctor == null ||
                doctor.m_state == null ||
                doctor.m_state.m_homeRoom == null ||
                doctor.m_state.m_homeRoom.GetEntity() != room)
            {
                return;
            }

            Sequences[patient.m_entity] = new ClothingSequence
            {
                Room = room,
                RestoreLevel = patient.GetTreatedSymptomDressLevel(),
                RetainedLevel = animation.m_state.m_dressLevel
            };
        }

        private static ProcedureScript GetCurrentManagedExamination(
            Entity patientEntity)
        {
            ProcedureComponent procedure =
                patientEntity.GetComponent<ProcedureComponent>();
            if (procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_currentProcedureScript == null)
            {
                return null;
            }

            ProcedureScript script =
                procedure.m_state.m_currentProcedureScript.GetEntity();
            if (script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_examination == null ||
                script.m_stateData.GetProcedure() == null ||
                !IsManagedDressExamination(script))
            {
                return null;
            }

            return script;
        }

        internal static bool IsManagedDressExamination(ProcedureScript script)
        {
            return script is ProcedureScriptExaminationGeneral ||
                script is ProcedureScriptExaminationGeneralEquipment ||
                script is ProcedureScriptExaminationOptionalEquipment ||
                script is ProcedureScriptExaminationSpecialEquipment ||
                script is ProcedureScriptExaminationPhysicalChest ||
                script is ProcedureScriptExaminationRadiology ||
                script is ProcedureScriptExaminationXray;
        }

        internal static bool TryResolveVisualClothingChange(
            Entity patientEntity,
            DressLevel requestedLevel,
            out DressLevel effectiveLevel)
        {
            effectiveLevel = requestedLevel;

            if (patientEntity == null)
            {
                return false;
            }

            AnimModelComponent animation =
                patientEntity.GetComponent<AnimModelComponent>();
            if (animation == null || animation.m_state == null)
            {
                return false;
            }

            DressLevel currentLevel = animation.m_state.m_dressLevel;
            if (!FixConfig.AvoidRedundantExamClothingChanges ||
                Restoring.Contains(patientEntity))
            {
                return currentLevel != effectiveLevel;
            }

            HospitalizationComponent hospitalization =
                patientEntity.GetComponent<HospitalizationComponent>();
            if (hospitalization == null || hospitalization.IsHospitalized())
            {
                return currentLevel != effectiveLevel;
            }

            ClothingSequence sequence;
            if (!Sequences.TryGetValue(patientEntity, out sequence) ||
                !IsStillInSequenceRoom(patientEntity, sequence))
            {
                return currentLevel != effectiveLevel;
            }

            ProcedureScript examinationScript =
                GetCurrentManagedExamination(patientEntity);

            if (examinationScript != null)
            {
                if (examinationScript.GetParam(0) >= 1f ||
                    requestedLevel == DressLevel.FULL ||
                    requestedLevel == sequence.RestoreLevel)
                {
                    return false;
                }

                if (IsExaminationExposureLevel(requestedLevel))
                {
                    effectiveLevel =
                        MergeExposure(sequence.RetainedLevel, requestedLevel);
                    return currentLevel != effectiveLevel;
                }

                return currentLevel != effectiveLevel;
            }

            if (requestedLevel == DressLevel.FULL ||
                requestedLevel == sequence.RestoreLevel)
            {
                return false;
            }

            return currentLevel != effectiveLevel;
        }

        private static Room GetCurrentRoom(Entity patientEntity)
        {
            if (patientEntity == null || MapScriptInterface.Instance == null)
            {
                return null;
            }

            WalkComponent walk = patientEntity.GetComponent<WalkComponent>();
            return walk == null
                ? null
                : MapScriptInterface.Instance.GetRoomAt(walk);
        }

        private static bool IsStillInSequenceRoom(
            Entity patientEntity,
            ClothingSequence sequence)
        {
            return sequence != null &&
                sequence.Room != null &&
                GetCurrentRoom(patientEntity) == sequence.Room;
        }

        private static void RestoreAndClear(Entity patientEntity)
        {
            if (patientEntity == null)
            {
                return;
            }

            ClothingSequence sequence;
            if (!Sequences.TryGetValue(patientEntity, out sequence))
            {
                return;
            }

            Sequences.Remove(patientEntity);

            AnimModelComponent animation =
                patientEntity.GetComponent<AnimModelComponent>();
            if (animation == null || animation.m_state == null)
            {
                return;
            }

            Restoring.Add(patientEntity);
            try
            {
                animation.SetClothingLevel(sequence.RestoreLevel);

                if (HfxDiagnostics.Enabled)
                {
                    HfxDiagnostics.Result(
                        "AvoidRedundantExamClothingChanges",
                        "OutpatientExamClothingSequence.RestoreAndClear",
                        "clothing-restored",
                        "patient=" +
                        HfxDiagnostics.EntityLabel(patientEntity) +
                        " level=" + sequence.RestoreLevel);
                }
            }
            finally
            {
                Restoring.Remove(patientEntity);
            }
        }
    }
}
