using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using GLib;
using Lopital;

namespace HospitalFixes.Patches
{
    internal static class PatientClothingChangeAnimationPatch
    {
        private const string DressState = "PROCEDURE_PATIENT_DRESS";

        private const string FallbackAnimationId = "stand_treatment_idle";

        private static readonly FieldInfo AnimatedControllerField =
            AccessTools.Field(
                typeof(AnimModelComponent),
                "m_animatedModel8DirController");

        private static readonly Dictionary<Entity, AnimModelComponent>
            ActiveAnimations =
                new Dictionary<Entity, AnimModelComponent>();

        [HarmonyPatch(typeof(ProcedureScript), nameof(ProcedureScript.SwitchState))]
        private static class ProcedureStatePatch
        {
            [HarmonyPrefix]
            private static void Prefix(
                ProcedureScript __instance,
                string state)
            {
                if (__instance == null ||
                    __instance.m_stateData == null ||
                    __instance.m_stateData.m_state != DressState ||
                    state == DressState)
                {
                    return;
                }

                RestoreForProcedure(__instance);
            }

            [HarmonyPostfix]
            private static void Postfix(
                ProcedureScript __instance,
                string state)
            {
                if (state == DressState)
                {
                    TryStart(__instance);
                }
            }
        }

        [HarmonyPatch(
            typeof(AnimModelComponent),
            nameof(AnimModelComponent.SetClothingLevel))]
        private static class ClothingAppliedPatch
        {
            [HarmonyPostfix]
            private static void Postfix(AnimModelComponent __instance)
            {
                if (__instance != null && __instance.m_entity != null)
                {
                    Restore(__instance.m_entity);
                }
            }
        }

        [HarmonyPatch(typeof(InGameMenuController), "Destroy")]
        private static class ClearOnUnloadPatch
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                if (ActiveAnimations.Count != 0)
                {
                    List<Entity> entities =
                        new List<Entity>(ActiveAnimations.Keys);
                    foreach (Entity entity in entities)
                    {
                        Restore(entity);
                    }

                    ActiveAnimations.Clear();
                }

                HshStaffClothingChangeIntegration
                    .ClearTransientState();

                ClothingChangeEffectRenderer.ClearAll(
                    destroyAssets: true);
            }
        }

        private static void TryStart(ProcedureScript script)
        {
            if (!FixConfig.AnimatePatientClothingChanges ||
                script == null ||
                script.m_stateData == null ||
                !IsSupportedDressProcedure(script))
            {
                return;
            }

            Entity patientEntity = GetPatient(script);
            if (patientEntity == null)
            {
                return;
            }

            BehaviorPatient patient =
                patientEntity.GetComponent<BehaviorPatient>();
            AnimModelComponent animation =
                patientEntity.GetComponent<AnimModelComponent>();
            WalkComponent walk =
                patientEntity.GetComponent<WalkComponent>();

            if (patient == null ||
                animation == null ||
                animation.m_state == null ||
                walk == null ||
                walk.m_state == null ||
                walk.IsBusy() ||
                animation.m_state.m_animationQueue == null ||
                animation.m_state.m_animationQueue.Count != 0 ||
                string.IsNullOrEmpty(
                    animation.m_state.m_currentAnimationID))
            {
                return;
            }

            DressLevel requestedLevel;
            if (!TryGetRequestedDressLevel(
                    script,
                    patient,
                    out requestedLevel) ||
                ShouldSkipForFullyBandagedPatient(
                    script,
                    patient,
                    patientEntity))
            {
                return;
            }

            DressLevel effectiveLevel;
            if (!OutpatientExamClothingSequencePatch
                    .TryResolveVisualClothingChange(
                        patientEntity,
                        requestedLevel,
                        out effectiveLevel) ||
                animation.m_state.m_dressLevel == effectiveLevel)
            {
                return;
            }

            bool lying =
                walk.m_state.m_lying;

            bool lyingInBed =
                lying &&
                walk.m_state.m_objectSittingOn != null &&
                animation.m_state.m_currentAnimationLying &&
                animation.m_state.m_currentAnimationID.StartsWith(
                    "lie_bed_idle",
                    System.StringComparison.Ordinal);

            // Lying clothing visuals are deliberately limited to diagnostic
            // imaging where Vanilla performs the actual clothing change:
            // X-ray and Radiology (CT/MRI). Hospitalized patients are valid
            // here too; ordinary ward-bed and other lying contexts never use
            // these procedure scripts and therefore stay completely Vanilla.
            if (lying)
            {
                if (!lyingInBed ||
                    !IsImagingLyingProcedure(script))
                {
                    return;
                }

                Restore(patientEntity);

                ClothingChangeEffectRenderer.StartLying(
                    patientEntity,
                    animation,
                    script);

                if (HfxDiagnostics.Enabled)
                {
                    HfxDiagnostics.Observed(
                        "AnimatePatientClothingChanges",
                        "ProcedureScript dressing step",
                        "animation-started",
                        "patient=" +
                        HfxDiagnostics.EntityLabel(patientEntity) +
                        " profile=lying-imaging");
                }

                return;
            }

            AnimatedModel8DirController controller =
                GetAnimatedController(animation);
            if (controller == null)
            {
                return;
            }

            Restore(patientEntity);

            bool seated =
                walk.m_state.m_objectSittingOn != null;

            bool seatedExamBed =
                seated &&
                string.Equals(
                    animation.m_state.m_currentAnimationID,
                    "sit_exam_bed_idle",
                    System.StringComparison.Ordinal);

            // Never replace an arbitrary seated pose with the standing
            // clothing animation. The dedicated seated clip is intentionally
            // limited to the Vanilla examination-bed idle pose.
            if (seated && !seatedExamBed)
            {
                return;
            }

            bool customAnimation;
            string animationId;

            if (seatedExamBed)
            {
                customAnimation =
                    ClothingChangeAnimationFactory
                        .TryRegisterSeated(controller);

                if (!customAnimation)
                {
                    ClothingChangeEffectRenderer.Stop(
                        patientEntity);
                    return;
                }

                animationId =
                    ClothingChangeAnimationFactory
                        .SeatedAnimationId;
            }
            else
            {
                customAnimation =
                    ClothingChangeAnimationFactory
                        .TryRegister(controller);

                animationId =
                    customAnimation
                        ? ClothingChangeAnimationFactory.AnimationId
                        : FallbackAnimationId;
            }

            controller.PlayAnimation(
                animationId,
                looping: false);

            if (customAnimation)
            {
                if (seatedExamBed)
                {
                    ClothingChangeEffectRenderer.StartSeated(
                        patientEntity,
                        animation);
                }
                else
                {
                    ClothingChangeEffectRenderer.Start(
                        patientEntity,
                        animation);
                }
            }
            else
            {
                ClothingChangeEffectRenderer.Stop(patientEntity);
            }

            ActiveAnimations[patientEntity] = animation;

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Observed(
                    "AnimatePatientClothingChanges",
                    "ProcedureScript dressing step",
                    "animation-started",
                    "patient=" +
                    HfxDiagnostics.EntityLabel(patientEntity) +
                    " profile=" +
                    (seatedExamBed ? "seated-exam-bed" : "standing") +
                    " animation=" + animationId +
                    " custom=" + customAnimation);
            }
        }

        private static bool TryGetRequestedDressLevel(
            ProcedureScript script,
            BehaviorPatient patient,
            out DressLevel requestedLevel)
        {
            requestedLevel = DressLevel.FULL;

            if (script.m_stateData == null ||
                script.m_stateData.GetProcedure() == null ||
                patient == null)
            {
                return false;
            }

            bool beforeProcedure = script.GetParam(0) < 1f;

            // Vanilla checks optional equipment after the three-second
            // dress prompt. It can leave the state without changing clothes,
            // so do not display a false clothing animation in that branch.
            if (beforeProcedure &&
                script is ProcedureScriptExaminationOptionalEquipment)
            {
                return false;
            }

            if (script is ProcedureScriptTreatmentProcedure &&
                !beforeProcedure)
            {
                DressLevel treatedLevel =
                    patient.GetTreatedSymptomDressLevel();

                requestedLevel =
                    treatedLevel != DressLevel.FULL
                        ? treatedLevel
                        : patient.GetTreatedSymptomDressLevel(
                            script.m_stateData.GetProcedure()
                                .DressLevelAfter);
                return true;
            }

            DressLevel procedureLevel =
                beforeProcedure
                    ? script.m_stateData.GetProcedure().DressLevel
                    : script.m_stateData.GetProcedure().DressLevelAfter;

            requestedLevel =
                patient.GetTreatedSymptomDressLevel(procedureLevel);
            return true;
        }

        private static bool IsImagingLyingProcedure(
            ProcedureScript script)
        {
            return script is ProcedureScriptExaminationXray ||
                script is ProcedureScriptExaminationRadiology;
        }

        private static bool ShouldSkipForFullyBandagedPatient(
            ProcedureScript script,
            BehaviorPatient patient,
            Entity patientEntity)
        {
            if (!(script is ProcedureScriptExaminationGeneral) &&
                !(script is ProcedureScriptExaminationGeneralEquipment) &&
                !(script is ProcedureScriptExaminationPhysicalChest) &&
                !(script is ProcedureScriptTreatmentProcedure))
            {
                return false;
            }

            HospitalizationComponent hospitalization =
                patientEntity.GetComponent<HospitalizationComponent>();

            return hospitalization != null &&
                patient.MainSymptomTreated() &&
                patient.m_state.m_medicalCondition
                    .m_gameDBMedicalCondition.Entry.FullyBandaged &&
                hospitalization.IsInOwnBed();
        }

        private static bool IsSupportedDressProcedure(
            ProcedureScript script)
        {
            return
                OutpatientExamClothingSequencePatch
                    .IsManagedDressExamination(script) ||
                script is ProcedureScriptTreatmentProcedure;
        }

        private static Entity GetPatient(ProcedureScript script)
        {
            if (script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_procedureScene == null)
            {
                return null;
            }

            return script.m_stateData.m_procedureScene
                .m_patient.GetEntity();
        }

        private static AnimatedModel8DirController
            GetAnimatedController(AnimModelComponent animation)
        {
            if (animation == null ||
                AnimatedControllerField == null)
            {
                return null;
            }

            return AnimatedControllerField.GetValue(animation)
                as AnimatedModel8DirController;
        }

        private static void RestoreForProcedure(
            ProcedureScript script)
        {
            Entity patientEntity = GetPatient(script);
            if (patientEntity != null)
            {
                Restore(patientEntity);
            }
        }

        private static void Restore(Entity patientEntity)
        {
            if (patientEntity == null)
            {
                return;
            }

            ClothingChangeEffectRenderer.Stop(patientEntity);

            AnimModelComponent animation;
            if (!ActiveAnimations.TryGetValue(
                    patientEntity,
                    out animation))
            {
                return;
            }

            ActiveAnimations.Remove(patientEntity);

            if (animation == null ||
                animation.m_state == null)
            {
                return;
            }

            AnimatedModel8DirController controller =
                GetAnimatedController(animation);
            string animationId =
                animation.m_state.m_currentAnimationID;

            if (controller == null ||
                string.IsNullOrEmpty(animationId))
            {
                return;
            }

            if (animation.m_state.m_currentAnimationLying)
            {
                controller.PlayLyingAnimation(
                    animationId,
                    animation.m_state.m_currentAnimationLooping);
            }
            else
            {
                controller.PlayAnimationFrom(
                    animationId,
                    animation.m_state.m_animationTime,
                    animation.m_state.m_currentAnimationLooping);
            }
        }
    }
}
