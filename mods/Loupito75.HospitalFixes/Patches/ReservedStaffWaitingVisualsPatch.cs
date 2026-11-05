using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
    internal static class ReservedStaffWaitingVisuals
    {
        internal const string WaitingBubbleAsset =
            "L75_HFX_ASSET_RESERVED_STAFF_WAIT_BUBBLE";

        internal const string ThinkingBubbleAsset =
            "L75_HFX_ASSET_CLINICAL_THINKING_BUBBLE";

        internal const string WaitingAnimationId =
            "stand_wait";

        private enum PauseVisualKind
        {
            Waiting,
            PreparationWaiting,
            CollapseTransportWaiting,
            Thinking
        }

        private static readonly HashSet<Entity> BubbleOwners =
            new HashSet<Entity>();

        private static readonly HashSet<Entity> WaitAnimationOwners =
            new HashSet<Entity>();

        private static readonly HashSet<Entity> BubbleOnlyOwners =
            new HashSet<Entity>();

        private static readonly HashSet<Entity> RenderedBubbleOwners =
            new HashSet<Entity>();

        private static readonly Dictionary<Entity, ProcedureScript>
            ReservedProcedureObservations =
                new Dictionary<Entity, ProcedureScript>();

        private static readonly Dictionary<ProcedureScript, string>
            LastProcedureStates =
                new Dictionary<ProcedureScript, string>();

        private static readonly HashSet<ProcedureScript>
            ActiveProcedureScripts =
                new HashSet<ProcedureScript>();

        private static readonly List<ProcedureScript>
            StaleProcedureStates =
                new List<ProcedureScript>();

        private static readonly Dictionary<Entity, string>
            WaitOriginAnimations =
                new Dictionary<Entity, string>();

        private static readonly Dictionary<Entity, PauseVisualKind>
            PauseKinds =
                new Dictionary<Entity, PauseVisualKind>();

        private static bool s_missingAssetWarningLogged;

        private static string GetFeatureName(
            PauseVisualKind kind)
        {
            if (kind == PauseVisualKind.Thinking)
            {
                return "ClinicalProcedureThinkingVisuals";
            }

            if (kind == PauseVisualKind.CollapseTransportWaiting)
            {
                return "CollapseTransportNurseWaitingVisuals";
            }

            return "ClinicalProcedureWaitingVisuals";
        }

        private static string GetBubbleAsset(
            PauseVisualKind kind)
        {
            return kind == PauseVisualKind.Thinking
                ? ThinkingBubbleAsset
                : WaitingBubbleAsset;
        }

        private static bool IsFeatureEnabled(
            PauseVisualKind kind)
        {
            if (kind == PauseVisualKind.Thinking)
            {
                return FixConfig.ClinicalProcedureThinkingVisuals;
            }

            if (kind == PauseVisualKind.CollapseTransportWaiting)
            {
                return FixConfig.CollapseTransportNurseWaitingVisuals;
            }

            return FixConfig.ClinicalProcedureWaitingVisuals;
        }

        private static bool HasOccurrenceTracking(
            Entity character)
        {
            return character != null &&
                (BubbleOwners.Contains(character) ||
                 WaitAnimationOwners.Contains(character) ||
                 BubbleOnlyOwners.Contains(character) ||
                 RenderedBubbleOwners.Contains(character) ||
                 ReservedProcedureObservations.ContainsKey(character) ||
                 WaitOriginAnimations.ContainsKey(character) ||
                 PauseKinds.ContainsKey(character));
        }

        private static void ReleaseAnimationOwnershipIfReplaced(
            Entity character)
        {
            if (character == null ||
                !WaitAnimationOwners.Contains(character))
            {
                return;
            }

            AnimModelComponent animation =
                character.GetComponent<AnimModelComponent>();

            if (!IsHfxWaitAnimation(animation))
            {
                WaitAnimationOwners.Remove(character);
            }
        }

        internal static void ClearWaitingState(
            Behavior staff)
        {
            if (staff == null ||
                staff.m_entity == null)
            {
                return;
            }

            Entity character = staff.m_entity;

            HideOwnWaitingBubble(character);
            BubbleOnlyOwners.Remove(character);
            ReservedProcedureObservations.Remove(character);
            RenderedBubbleOwners.Remove(character);
            WaitOriginAnimations.Remove(character);
            PauseKinds.Remove(character);

            ReleaseAnimationOwnershipIfReplaced(character);
        }

        internal static void HandleWaitingState(
            Behavior staff,
            Entity patient,
            string procedureLocID)
        {
            HandleCurrentReservation(staff);
        }

        internal static void HandleCurrentReservation(
            Behavior staff)
        {
            if (staff == null ||
                staff.m_entity == null)
            {
                return;
            }

            Entity character = staff.m_entity;

            if (IsActiveProcedureControlState(staff))
            {
                ReservedProcedureObservations.Remove(character);
                ReleaseAnimationOwnershipIfReplaced(character);
                return;
            }

            if (!IsReservedProcedureControlState(staff))
            {
                ReservedProcedureObservations.Remove(character);
                ReleaseAnimationOwnershipIfReplaced(character);

                if (HasOccurrenceTracking(character))
                {
                    ClearWaitingState(staff);
                }

                return;
            }

            Entity patient;
            ProcedureScript script;
            string procedureLocID;

            if (!TryGetReservedProcedure(
                    staff,
                    out patient,
                    out script,
                    out procedureLocID))
            {
                ClearWaitingState(staff);
                return;
            }

            ProcedureScript observed;
            if (!ReservedProcedureObservations.TryGetValue(
                    character,
                    out observed) ||
                observed != script)
            {
                ReservedProcedureObservations[character] = script;
                HideOwnWaitingBubble(character);

                return;
            }

            ApplyOrMaintainWait(
                staff,
                patient,
                procedureLocID,
                "reserved-script",
                script.GetType().Name,
                script.m_stateData == null
                    ? "unknown"
                    : script.m_stateData.m_state,
                PauseVisualKind.Waiting);
        }

        internal static void RefreshActiveProcedures(
            ProcedureManager manager)
        {
            if (manager == null ||
                manager.m_scriptEntities == null)
            {
                LastProcedureStates.Clear();
                ActiveProcedureScripts.Clear();
                return;
            }

            List<ProcedureScript> scripts =
                manager.m_scriptEntities;

            ActiveProcedureScripts.Clear();

            for (int i = 0; i < scripts.Count; i++)
            {
                ProcedureScript script = scripts[i];
                ActiveProcedureScripts.Add(script);

                if (script == null ||
                    script.m_stateData == null ||
                    script.m_stateData.m_procedureScene == null)
                {
                    continue;
                }

                string currentState =
                    script.m_stateData.m_state ??
                    string.Empty;

                string previousState;
                bool stable =
                    LastProcedureStates.TryGetValue(
                        script,
                        out previousState) &&
                    string.Equals(
                        previousState,
                        currentState,
                        StringComparison.Ordinal);

                bool continuousSurgeryPreparation =
                    !stable &&
                    !string.IsNullOrEmpty(previousState) &&
                    IsSurgeryPreparationState(
                        script,
                        previousState) &&
                    IsSurgeryPreparationState(
                        script,
                        currentState);

                bool continuousCollapseTransportWait =
                    !stable &&
                    !string.IsNullOrEmpty(previousState) &&
                    IsCollapseTransportNurseWaitingState(
                        script,
                        previousState) &&
                    IsCollapseTransportNurseWaitingState(
                        script,
                        currentState);

                LastProcedureStates[script] =
                    currentState;

                if (!stable &&
                    !continuousSurgeryPreparation &&
                    !continuousCollapseTransportWait)
                {
                    ClearScriptWaitingState(script);
                    continue;
                }

                RefreshActiveProcedure(
                    script,
                    currentState);
            }

            StaleProcedureStates.Clear();

            foreach (KeyValuePair<ProcedureScript, string> pair
                     in LastProcedureStates)
            {
                if (pair.Key == null ||
                    !ActiveProcedureScripts.Contains(pair.Key))
                {
                    StaleProcedureStates.Add(pair.Key);
                }
            }

            for (int i = 0;
                 i < StaleProcedureStates.Count;
                 i++)
            {
                LastProcedureStates.Remove(
                    StaleProcedureStates[i]);
            }

            StaleProcedureStates.Clear();
        }

        private static void RefreshActiveProcedure(
            ProcedureScript script,
            string scriptState)
        {
            ProcedureScene scene =
                script == null ||
                script.m_stateData == null
                    ? null
                    : script.m_stateData.m_procedureScene;

            if (scene == null)
            {
                return;
            }

            if (scene.m_patient == null)
            {
                return;
            }

            Entity patient =
                scene.m_patient.GetEntity();

            if (patient == null)
            {
                return;
            }

            string procedureLocID =
                script.m_stateData.GetProcedureLocID() ??
                string.Empty;

            ProcedureScriptCollapse collapseScript =
                script as ProcedureScriptCollapse;
            if (collapseScript != null)
            {
                RefreshCollapseTransportNurseWait(
                    collapseScript,
                    scene,
                    patient,
                    procedureLocID,
                    scriptState);
                return;
            }

            RefreshSceneStaffList(
                scene.m_doctors,
                script,
                patient,
                procedureLocID,
                scriptState);

            RefreshSceneStaffList(
                scene.m_nurses,
                script,
                patient,
                procedureLocID,
                scriptState);

            RefreshSceneStaffList(
                scene.m_indistinctEmployees,
                script,
                patient,
                procedureLocID,
                scriptState);

            if (scene.m_labSpecialist != null)
            {
                RefreshActiveStaff(
                    scene.m_labSpecialist.GetEntity(),
                    script,
                    patient,
                    procedureLocID,
                    scriptState);
            }
        }

        private static bool IsCollapseTransportNurseWaitingState(
            ProcedureScript script,
            string scriptState)
        {
            return script is ProcedureScriptCollapse &&
                !string.IsNullOrEmpty(scriptState) &&
                !string.Equals(
                    scriptState,
                    "IDLE",
                    StringComparison.Ordinal) &&
                !string.Equals(
                    scriptState,
                    "TRANSPORTING",
                    StringComparison.Ordinal) &&
                !string.Equals(
                    scriptState,
                    "TRANSPORTED_AWAY",
                    StringComparison.Ordinal);
        }

        private static void RefreshCollapseTransportNurseWait(
            ProcedureScriptCollapse script,
            ProcedureScene scene,
            Entity patient,
            string procedureLocID,
            string scriptState)
        {
            if (scene == null ||
                scene.m_nurses == null ||
                scene.m_nurses.Count == 0)
            {
                return;
            }

            EntityIDPointer<Entity> pointer =
                scene.m_nurses[0];
            Entity character =
                pointer == null
                    ? null
                    : pointer.GetEntity();
            BehaviorNurse nurse =
                character == null
                    ? null
                    : character.GetComponent<BehaviorNurse>();

            if (nurse == null)
            {
                return;
            }

            if (!IsCollapseTransportNurseWaitingState(
                    script,
                    scriptState) ||
                !IsActiveProcedureControlState(nurse))
            {
                if (HasOccurrenceTracking(character))
                {
                    ClearWaitingState(nurse);
                }

                return;
            }

            ApplyOrMaintainBubbleOnly(
                nurse,
                patient,
                procedureLocID,
                "collapse-transport-nurse",
                script.GetType().Name,
                scriptState,
                PauseVisualKind.CollapseTransportWaiting);

            TryApplyCollapseTransportWaitAnimation(
                nurse,
                patient,
                procedureLocID,
                script.GetType().Name,
                scriptState);
        }

        private static void TryApplyCollapseTransportWaitAnimation(
            BehaviorNurse nurse,
            Entity patient,
            string procedureLocID,
            string scriptType,
            string scriptState)
        {
            if (nurse == null ||
                nurse.m_entity == null ||
                !FixConfig.CollapseTransportNurseWaitingVisuals)
            {
                return;
            }

            Entity character = nurse.m_entity;

            if (!IsShowingOwnWaitingBubble(character))
            {
                return;
            }

            WalkComponent walk =
                nurse.GetComponent<WalkComponent>();
            UseComponent use =
                nurse.GetComponent<UseComponent>();
            AnimModelComponent animation =
                nurse.GetComponent<AnimModelComponent>();

            if (walk == null ||
                walk.m_state == null ||
                animation == null ||
                animation.m_state == null ||
                walk.IsBusy() ||
                walk.IsSitting() ||
                (use != null && use.IsUsing()))
            {
                return;
            }

            ReleaseAnimationOwnershipIfReplaced(character);

            if (IsHfxWaitAnimation(animation))
            {
                BubbleOnlyOwners.Remove(character);
                return;
            }

            if (animation.m_state.m_animationQueue != null &&
                animation.m_state.m_animationQueue.Count > 0)
            {
                return;
            }

            string currentAnimation =
                animation.m_state.m_currentAnimationID;

            if (!animation.IsIdle() ||
                (!string.IsNullOrEmpty(currentAnimation) &&
                 !string.Equals(
                     currentAnimation,
                     "stand_idle",
                     StringComparison.Ordinal)))
            {
                return;
            }

            string previousAnimation =
                string.IsNullOrEmpty(currentAnimation)
                    ? "none"
                    : currentAnimation;

            animation.PlayAnimation(WaitingAnimationId);
            WaitAnimationOwners.Add(character);
            WaitOriginAnimations[character] =
                previousAnimation;
            BubbleOnlyOwners.Remove(character);

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.ResultRateLimited(
                    "CollapseTransportNurseWaitingVisuals",
                    "collapse transport nurse waiting animation",
                    "stand-wait-applied",
                    BuildDiagnosticContext(
                        nurse,
                        patient,
                        procedureLocID,
                        "collapse-transport-nurse",
                        scriptType,
                        scriptState,
                        previousAnimation,
                        true,
                        PauseVisualKind.CollapseTransportWaiting),
                    "CollapseTransportNurseWaitingVisuals|animation|" +
                    HfxDiagnostics.EntityLabel(character),
                    3000);
            }
        }

        private static void RefreshSceneStaffList(
            List<EntityIDPointer<Entity>> staffList,
            ProcedureScript script,
            Entity patient,
            string procedureLocID,
            string scriptState)
        {
            if (staffList == null)
            {
                return;
            }

            for (int i = 0;
                 i < staffList.Count;
                 i++)
            {
                EntityIDPointer<Entity> pointer =
                    staffList[i];

                if (pointer == null)
                {
                    continue;
                }

                RefreshActiveStaff(
                    pointer.GetEntity(),
                    script,
                    patient,
                    procedureLocID,
                    scriptState);
            }
        }

        private static void RefreshActiveStaff(
            Entity character,
            ProcedureScript script,
            Entity patient,
            string procedureLocID,
            string scriptState)
        {
            Behavior staff =
                GetSupportedBehavior(character);

            if (staff == null ||
                !IsActiveProcedureControlState(staff) ||
                !IsActiveProcedureOwnershipValid(
                    staff,
                    script,
                    patient,
                    procedureLocID))
            {
                if (staff != null &&
                    HasOccurrenceTracking(character))
                {
                    ClearWaitingState(staff);
                }

                return;
            }

            string surgeryWaitSource;
            if (TryClassifySurgeryPreparationWait(
                    script,
                    staff,
                    scriptState,
                    out surgeryWaitSource))
            {
                ApplyOrMaintainBubbleOnly(
                    staff,
                    patient,
                    procedureLocID,
                    surgeryWaitSource,
                    script.GetType().Name,
                    scriptState,
                    PauseVisualKind.PreparationWaiting);
                return;
            }

            string patientWaitSource;
            if (TryClassifyPatientWaitBubble(
                    script,
                    scriptState,
                    out patientWaitSource))
            {
                ApplyOrMaintainBubbleOnly(
                    staff,
                    patient,
                    procedureLocID,
                    patientWaitSource,
                    script.GetType().Name,
                    scriptState,
                    PauseVisualKind.Waiting);
                return;
            }

            PauseVisualKind kind;
            string pauseSource;

            if (!TryClassifyActivePause(
                    script,
                    staff,
                    scriptState,
                    out kind,
                    out pauseSource))
            {
                if (HasOccurrenceTracking(character))
                {
                    ClearWaitingState(staff);
                }

                return;
            }

            ApplyOrMaintainWait(
                staff,
                patient,
                procedureLocID,
                pauseSource,
                script.GetType().Name,
                scriptState,
                kind);
        }

        private static bool IsSurgeryPreparationState(
            ProcedureScript script,
            string scriptState)
        {
            if (script == null ||
                string.IsNullOrEmpty(scriptState))
            {
                return false;
            }

            string scriptType =
                script.GetType().Name;

            if (!scriptType.StartsWith(
                    "ProcedureScriptTreatmentSurgery",
                    StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(
                       scriptState,
                       "NURSES_GOING_TO_WASH_HANDS",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       scriptState,
                       "NURSES_WASHING_HANDS",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       scriptState,
                       "NURSES_GOING_TO_PPEPARE",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       scriptState,
                       "NURSES_PPEPARIRNG",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       scriptState,
                       "NURSES_GOING_TO_PPEPARE_PATIENT",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       scriptState,
                       "NURSES_PPEPARIRNG_PATIENT",
                       StringComparison.Ordinal);
        }

        private static bool TryClassifySurgeryPreparationWait(
            ProcedureScript script,
            Behavior staff,
            string scriptState,
            out string waitSource)
        {
            waitSource = null;

            if (!(staff is BehaviorDoctor) ||
                !IsSurgeryPreparationState(
                    script,
                    scriptState))
            {
                return false;
            }

            waitSource =
                "surgery-preparation";
            return true;
        }

        private static bool TryClassifyPatientWaitBubble(
            ProcedureScript script,
            string scriptState,
            out string waitSource)
        {
            waitSource = null;

            if (script == null ||
                string.IsNullOrEmpty(scriptState))
            {
                return false;
            }

            if (script is ProcedureScriptExaminationDoctorsInterview &&
                string.Equals(
                    scriptState,
                    "DOCTOR_WAITING_FOR_PATIENT",
                    StringComparison.Ordinal))
            {
                waitSource =
                    "interview-waiting-for-patient";
                return true;
            }

            if (string.Equals(
                    scriptState,
                    "EMPLOYEE_WAITING_FOR_PATIENT",
                    StringComparison.Ordinal))
            {
                waitSource =
                    "waiting-for-patient";
                return true;
            }

            if (string.Equals(
                    scriptState,
                    "EMPLOYEE_WAITING_FOR_HOSPITALIZED_PATIENT",
                    StringComparison.Ordinal))
            {
                waitSource =
                    "waiting-for-hospitalized-patient";
                return true;
            }

            if (script is ProcedureScriptExaminationXray &&
                string.Equals(
                    scriptState,
                    "EMPLOYEE_STOPPED_WAITING_FOR_HOSPITALIZED_PATIENT",
                    StringComparison.Ordinal))
            {
                // Vanilla X-ray names this constant as a waiting state even
                // though its string value contains "STOPPED"; ScriptUpdate()
                // still routes it to UpdateStateEmployeeWaitingForHospitalizedPatient().
                waitSource =
                    "waiting-for-hospitalized-patient";
                return true;
            }

            return false;
        }

        private static void ApplyOrMaintainBubbleOnly(
            Behavior staff,
            Entity patient,
            string procedureLocID,
            string waitSource,
            string scriptType,
            string scriptState,
            PauseVisualKind kind)
        {
            if (staff == null ||
                staff.m_entity == null)
            {
                return;
            }

            Entity character =
                staff.m_entity;
            WalkComponent walk =
                staff.GetComponent<WalkComponent>();
            UseComponent use =
                staff.GetComponent<UseComponent>();

            ReleaseAnimationOwnershipIfReplaced(character);

            if (walk == null ||
                walk.m_state == null ||
                walk.IsBusy() ||
                (use != null && use.IsUsing()))
            {
                HideOwnWaitingBubble(character);
                BubbleOnlyOwners.Remove(character);
                PauseKinds.Remove(character);
                return;
            }

            if (!IsFeatureEnabled(kind))
            {
                HideOwnWaitingBubble(character);
                BubbleOnlyOwners.Remove(character);
                PauseKinds.Remove(character);

                return;
            }

            PauseKinds[character] =
                kind;
            BubbleOnlyOwners.Add(character);

            bool shown =
                TryShowWaitingBubble(
                    character,
                    kind);

            if (HfxDiagnostics.Enabled &&
                shown)
            {
                string featureName =
                    GetFeatureName(kind);
                string diagnosticAction =
                    kind == PauseVisualKind.PreparationWaiting
                        ? "clinical staff surgery preparation bubble"
                        : kind == PauseVisualKind.CollapseTransportWaiting
                            ? "collapse transport nurse waiting bubble"
                            : "clinical staff waiting-for-patient bubble";
                string diagnosticResult =
                    kind == PauseVisualKind.PreparationWaiting
                        ? "surgery-preparation-bubble-applied"
                        : kind == PauseVisualKind.CollapseTransportWaiting
                            ? "collapse-transport-nurse-bubble-applied"
                            : "patient-wait-bubble-applied";

                HfxDiagnostics.ResultRateLimited(
                    featureName,
                    diagnosticAction,
                    diagnosticResult,
                    BuildDiagnosticContext(
                        staff,
                        patient,
                        procedureLocID,
                        waitSource,
                        scriptType,
                        scriptState,
                        "vanilla-unchanged",
                        true,
                        kind),
                    featureName + "|" +
                    waitSource + "|" +
                    GetStaffType(staff) + "|" +
                    HfxDiagnostics.EntityLabel(character),
                    3000);
            }
        }

        private static bool TryClassifyActivePause(
            ProcedureScript script,
            Behavior staff,
            string scriptState,
            out PauseVisualKind kind,
            out string pauseSource)
        {
            kind = PauseVisualKind.Waiting;
            pauseSource = null;

            if (script == null ||
                staff == null ||
                string.IsNullOrEmpty(scriptState))
            {
                return false;
            }

            string scriptType =
                script.GetType().Name;

            if (string.Equals(
                    scriptType,
                    "ProcedureScriptTreatmentReceipt",
                    StringComparison.Ordinal) &&
                string.Equals(
                    scriptState,
                    "DOCTOR_START_PRESCRIBING",
                    StringComparison.Ordinal) &&
                staff is BehaviorDoctor)
            {
                kind = PauseVisualKind.Thinking;
                pauseSource = "prescription-thinking";
                return true;
            }

            if (string.Equals(
                    scriptState,
                    "PROCEDURE_PATIENT_DRESS",
                    StringComparison.Ordinal))
            {
                if (OutpatientExamClothingSequencePatch
                        .ShouldSuppressRedundantDressVisuals(script))
                {
                    return false;
                }

                kind = PauseVisualKind.Waiting;
                pauseSource = "patient-dressing";
                return true;
            }

            return false;
        }

        private static void ClearScriptWaitingState(
            ProcedureScript script)
        {
            ProcedureScene scene =
                script == null ||
                script.m_stateData == null
                    ? null
                    : script.m_stateData.m_procedureScene;

            if (scene == null)
            {
                return;
            }

            ClearSceneStaffList(scene.m_doctors);
            ClearSceneStaffList(scene.m_nurses);
            ClearSceneStaffList(scene.m_indistinctEmployees);

            if (scene.m_patient != null)
            {
                ClearStaffWaitingState(
                    scene.m_patient.GetEntity());
            }

            if (scene.m_labSpecialist != null)
            {
                ClearStaffWaitingState(
                    scene.m_labSpecialist.GetEntity());
            }
        }

        private static void ClearSceneStaffList(
            List<EntityIDPointer<Entity>> staffList)
        {
            if (staffList == null)
            {
                return;
            }

            for (int i = 0;
                 i < staffList.Count;
                 i++)
            {
                EntityIDPointer<Entity> pointer =
                    staffList[i];

                if (pointer != null)
                {
                    ClearStaffWaitingState(
                        pointer.GetEntity());
                }
            }
        }

        private static void ClearStaffWaitingState(
            Entity character)
        {
            if (character == null ||
                !HasOccurrenceTracking(character))
            {
                return;
            }

            Behavior staff =
                GetSupportedBehavior(character);

            if (staff != null)
            {
                ClearWaitingState(staff);
            }
        }

        private static void ApplyOrMaintainWait(
            Behavior staff,
            Entity patient,
            string procedureLocID,
            string waitSource,
            string scriptType,
            string scriptState,
            PauseVisualKind kind)
        {
            if (staff == null ||
                staff.m_entity == null)
            {
                return;
            }

            Entity character = staff.m_entity;
            BubbleOnlyOwners.Remove(character);

            string featureName =
                GetFeatureName(kind);
            bool featureEnabled =
                IsFeatureEnabled(kind);

            PauseVisualKind previousKind;
            if (PauseKinds.TryGetValue(
                    character,
                    out previousKind) &&
                previousKind != kind)
            {
                HideOwnWaitingBubble(character);
                RenderedBubbleOwners.Remove(character);
            }

            WalkComponent walk =
                staff.GetComponent<WalkComponent>();
            UseComponent use =
                staff.GetComponent<UseComponent>();
            AnimModelComponent animation =
                staff.GetComponent<AnimModelComponent>();

            if (walk == null ||
                animation == null ||
                animation.m_state == null ||
                walk.m_state == null ||
                walk.IsBusy() ||
                walk.IsSitting() ||
                (use != null && use.IsUsing()))
            {
                HideOwnWaitingBubble(character);
                ReleaseAnimationOwnershipIfReplaced(character);

                PauseKinds.Remove(character);

                return;
            }

            ReleaseAnimationOwnershipIfReplaced(character);
            PauseKinds[character] = kind;

            if (IsHfxWaitAnimation(animation))
            {
                if (!featureEnabled)
                {
                    HideOwnWaitingBubble(character);
                    return;
                }

                bool bubbleShown =
                    TryShowWaitingBubble(
                        character,
                        kind);

                if (HfxDiagnostics.Enabled &&
                    bubbleShown)
                {
                    HfxDiagnostics.ResultRateLimited(
                        featureName,
                        kind == PauseVisualKind.Thinking
                            ? "clinical staff script thinking"
                            : "clinical staff script wait",
                        "pause-bubble-restored",
                        BuildDiagnosticContext(
                            staff,
                            patient,
                            procedureLocID,
                            waitSource,
                            scriptType,
                            scriptState,
                            null,
                            null,
                            kind),
                        featureName + "|bubble|" +
                        GetStaffType(staff) + "|" +
                        HfxDiagnostics.EntityLabel(character),
                        3000);
                }

                return;
            }

            if (WaitAnimationOwners.Remove(character))
            {
                RenderedBubbleOwners.Remove(character);
                WaitOriginAnimations.Remove(character);
                HideOwnWaitingBubble(character);
            }

            if (!IsActualAnimationGap(animation))
            {
                HideOwnWaitingBubble(character);

                PauseKinds.Remove(character);

                return;
            }

            string previousAnimation =
                string.IsNullOrEmpty(
                    animation.m_state.m_currentAnimationID)
                    ? "none"
                    : animation.m_state.m_currentAnimationID;

            if (!featureEnabled)
            {
                PauseKinds.Remove(character);
                return;
            }

            animation.PlayAnimation(WaitingAnimationId);
            WaitAnimationOwners.Add(character);
            WaitOriginAnimations[character] =
                previousAnimation;

            bool shown =
                TryShowWaitingBubble(
                    character,
                    kind);

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.ResultRateLimited(
                    featureName,
                    kind == PauseVisualKind.Thinking
                        ? "clinical staff script thinking"
                        : "clinical staff script wait",
                    "pause-visuals-applied",
                    BuildDiagnosticContext(
                        staff,
                        patient,
                        procedureLocID,
                        waitSource,
                        scriptType,
                        scriptState,
                        previousAnimation,
                        shown,
                        kind),
                    featureName + "|apply|" +
                    GetStaffType(staff) + "|" +
                    HfxDiagnostics.EntityLabel(character),
                    3000);
            }
        }

        private static bool TryGetReservedProcedure(
            Behavior staff,
            out Entity patient,
            out ProcedureScript script,
            out string procedureLocID)
        {
            patient = null;
            script = null;
            procedureLocID = string.Empty;

            if (staff == null ||
                staff.m_entity == null)
            {
                return false;
            }

            EmployeeComponent employee =
                staff.GetComponent<EmployeeComponent>();

            if (employee == null ||
                employee.m_state == null ||
                employee.m_state.m_reservedByPatient == null)
            {
                return false;
            }

            patient =
                employee.m_state.m_reservedByPatient
                    .GetEntity();

            if (patient == null)
            {
                return false;
            }

            procedureLocID =
                employee.m_state.m_reservedForProcedureLocID ??
                string.Empty;

            ProcedureComponent procedure =
                patient.GetComponent<ProcedureComponent>();

            if (procedure == null ||
                procedure.m_state == null ||
                (procedure.m_state.m_currentProcedureScript != null &&
                 procedure.m_state.m_currentProcedureScript
                    .GetEntity() != null) ||
                procedure.m_state.m_reservedProcedureScript == null)
            {
                return false;
            }

            script =
                procedure.m_state.m_reservedProcedureScript
                    .GetEntity();

            if (script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_procedureScene == null ||
                !string.Equals(
                    script.m_stateData.m_state,
                    "RESERVED",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    script.m_stateData.GetProcedureLocID() ??
                        string.Empty,
                    procedureLocID,
                    StringComparison.Ordinal) ||
                !ProcedureSceneContainsStaff(
                    script.m_stateData.m_procedureScene,
                    staff.m_entity))
            {
                script = null;
                return false;
            }

            return true;
        }

        private static bool IsActiveProcedureOwnershipValid(
            Behavior staff,
            ProcedureScript script,
            Entity patient,
            string procedureLocID)
        {
            if (staff == null ||
                staff.m_entity == null ||
                script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_procedureScene == null ||
                patient == null)
            {
                return false;
            }

            EmployeeComponent employee =
                staff.GetComponent<EmployeeComponent>();

            if (employee == null ||
                employee.m_state == null ||
                employee.m_state.m_reservedByPatient == null ||
                employee.m_state.m_reservedByPatient.GetEntity() !=
                    patient ||
                !string.Equals(
                    employee.m_state.m_reservedForProcedureLocID ??
                        string.Empty,
                    procedureLocID ?? string.Empty,
                    StringComparison.Ordinal))
            {
                return false;
            }

            ProcedureComponent procedure =
                patient.GetComponent<ProcedureComponent>();

            return procedure != null &&
                procedure.m_state != null &&
                procedure.m_state.m_currentProcedureScript != null &&
                procedure.m_state.m_currentProcedureScript.GetEntity() ==
                    script &&
                ProcedureSceneContainsStaff(
                    script.m_stateData.m_procedureScene,
                    staff.m_entity);
        }

        private static bool ProcedureSceneContainsStaff(
            ProcedureScene scene,
            Entity character)
        {
            if (scene == null ||
                character == null)
            {
                return false;
            }

            if (ContainsEntity(
                    scene.m_doctors,
                    character) ||
                ContainsEntity(
                    scene.m_nurses,
                    character) ||
                ContainsEntity(
                    scene.m_indistinctEmployees,
                    character))
            {
                return true;
            }

            return scene.m_labSpecialist != null &&
                scene.m_labSpecialist.GetEntity() ==
                    character;
        }

        private static bool ContainsEntity(
            List<EntityIDPointer<Entity>> pointers,
            Entity character)
        {
            if (pointers == null)
            {
                return false;
            }

            for (int i = 0;
                 i < pointers.Count;
                 i++)
            {
                EntityIDPointer<Entity> pointer =
                    pointers[i];

                if (pointer != null &&
                    pointer.GetEntity() == character)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsActualAnimationGap(
            AnimModelComponent animation)
        {
            if (animation == null ||
                animation.m_state == null)
            {
                return false;
            }

            if (animation.m_state.m_animationQueue != null &&
                animation.m_state.m_animationQueue.Count > 0)
            {
                return false;
            }

            return !animation.IsLooping() &&
                animation.IsIdle();
        }

        internal static bool IsHfxWaitAnimation(
            AnimModelComponent animation)
        {
            if (animation == null ||
                animation.m_entity == null ||
                animation.m_state == null ||
                !WaitAnimationOwners.Contains(
                    animation.m_entity) ||
                animation.m_state.m_currentAnimationID !=
                    WaitingAnimationId ||
                !animation.m_state.m_currentAnimationLooping)
            {
                return false;
            }

            return true;
        }

        internal static bool IsShowingOwnWaitingBubble(
            Entity character)
        {
            SpeechComponent speech =
                character == null
                    ? null
                    : character
                        .GetComponent<SpeechComponent>();

            SpeechComponentPersistentData state =
                speech == null
                    ? null
                    : speech.GetPersistentData()
                        as SpeechComponentPersistentData;

            return state != null &&
                state.m_speechBubble != null &&
                state.m_bubbleIconIndex == -1 &&
                (string.Equals(
                    state.m_bubbleIconAssetID,
                    WaitingBubbleAsset,
                    StringComparison.Ordinal) ||
                 string.Equals(
                    state.m_bubbleIconAssetID,
                    ThinkingBubbleAsset,
                    StringComparison.Ordinal));
        }

        internal static string GetOwnedBubbleAsset(
            Entity character)
        {
            PauseVisualKind kind;
            return character != null &&
                PauseKinds.TryGetValue(
                    character,
                    out kind)
                ? GetBubbleAsset(kind)
                : null;
        }

        internal static bool IsEligibleBubbleOwner(
            Entity character)
        {
            PauseVisualKind kind;
            if (character == null ||
                !BubbleOwners.Contains(character) ||
                !PauseKinds.TryGetValue(
                    character,
                    out kind) ||
                !IsFeatureEnabled(kind))
            {
                return false;
            }

            Behavior staff =
                GetSupportedBehavior(character);

            if (staff == null ||
                !IsSupportedProcedureControlState(staff))
            {
                return false;
            }

            WalkComponent walk =
                character.GetComponent<WalkComponent>();

            if (walk == null ||
                walk.m_state == null ||
                walk.IsBusy())
            {
                return false;
            }

            UseComponent use =
                character.GetComponent<UseComponent>();

            if (BubbleOnlyOwners.Contains(character))
            {
                // Bubble-only waits preserve Vanilla animation ownership but
                // stay silent while the employee is actively using equipment.
                return use == null ||
                    !use.IsUsing();
            }

            AnimModelComponent animation =
                character.GetComponent<AnimModelComponent>();

            return WaitAnimationOwners.Contains(character) &&
                !walk.IsSitting() &&
                (use == null || !use.IsUsing()) &&
                IsHfxWaitAnimation(animation);
        }

        internal static void MarkBubbleRendered(
            Entity character)
        {
            if (character != null &&
                IsShowingOwnWaitingBubble(character) &&
                IsEligibleBubbleOwner(character))
            {
                RenderedBubbleOwners.Add(character);
            }
        }

        internal static void HideOwnWaitingBubble(
            Entity character)
        {
            if (character == null)
            {
                return;
            }

            SpeechComponent speech =
                character.GetComponent<SpeechComponent>();

            if (speech != null &&
                IsShowingOwnWaitingBubble(character))
            {
                speech.HideBubble();
            }

            BubbleOwners.Remove(character);
            RenderedBubbleOwners.Remove(character);
        }

        internal static void ClearPersistedWaitingBubble(
            SpeechComponentPersistentData state)
        {
            if (state == null ||
                (!string.Equals(
                    state.m_bubbleIconAssetID,
                    WaitingBubbleAsset,
                    StringComparison.Ordinal) &&
                 !string.Equals(
                    state.m_bubbleIconAssetID,
                    ThinkingBubbleAsset,
                    StringComparison.Ordinal)))
            {
                return;
            }

            state.m_bubbleTextureIndex = -1;
            state.m_bubbleIconIndex = -1;
            state.m_bubbleIconAssetID = null;
            state.m_speechBubble = null;
            state.m_displayTime = -1f;
        }

        internal static void ClearTransientState()
        {
            if (BubbleOwners.Count > 0)
            {
                Entity[] owners =
                    new Entity[BubbleOwners.Count];
                BubbleOwners.CopyTo(owners);

                for (int i = 0;
                     i < owners.Length;
                     i++)
                {
                    HideOwnWaitingBubble(owners[i]);
                }
            }

            BubbleOwners.Clear();

            if (WaitAnimationOwners.Count > 0)
            {
                Entity[] animationOwners =
                    new Entity[WaitAnimationOwners.Count];
                WaitAnimationOwners.CopyTo(animationOwners);

                for (int i = 0;
                     i < animationOwners.Length;
                     i++)
                {
                    Entity owner = animationOwners[i];
                    AnimModelComponent animation =
                        owner == null
                            ? null
                            : owner.GetComponent<AnimModelComponent>();

                    if (IsHfxWaitAnimation(animation))
                    {
                        animation.PlayAnimation(
                            "stand_idle",
                            looping: false);
                    }
                }
            }

            WaitAnimationOwners.Clear();
            BubbleOnlyOwners.Clear();
            RenderedBubbleOwners.Clear();
            ReservedProcedureObservations.Clear();
            LastProcedureStates.Clear();
            StaleProcedureStates.Clear();
            ActiveProcedureScripts.Clear();
            WaitOriginAnimations.Clear();
            PauseKinds.Clear();
            s_missingAssetWarningLogged = false;

            ReservedStaffWaitingBubbleRendererPatch.Reset();
        }

        private static Behavior GetSupportedBehavior(
            Entity character)
        {
            if (character == null)
            {
                return null;
            }

            BehaviorNurse nurse =
                character.GetComponent<BehaviorNurse>();

            if (nurse != null)
            {
                return nurse;
            }

            BehaviorDoctor doctor =
                character.GetComponent<BehaviorDoctor>();

            if (doctor != null)
            {
                return doctor;
            }

            return character
                .GetComponent<BehaviorLabSpecialist>();
        }

        private static bool IsReservedProcedureControlState(
            Behavior staff)
        {
            BehaviorNurse nurse =
                staff as BehaviorNurse;

            if (nurse != null)
            {
                return nurse.m_state != null &&
                    nurse.m_state.m_nurseState ==
                        NurseState.OverridenReservedForProcedure;
            }

            BehaviorDoctor doctor =
                staff as BehaviorDoctor;

            if (doctor != null)
            {
                return doctor.m_state != null &&
                    doctor.m_state.m_doctorState ==
                        DoctorState.OverridenReservedForProcedure;
            }

            BehaviorLabSpecialist lab =
                staff as BehaviorLabSpecialist;

            return lab != null &&
                lab.m_state != null &&
                lab.m_state.m_labSpecialistState ==
                    LabSpecialistState.OverridenReservedForProcedure;
        }

        private static bool IsActiveProcedureControlState(
            Behavior staff)
        {
            BehaviorNurse nurse =
                staff as BehaviorNurse;

            if (nurse != null)
            {
                return nurse.m_state != null &&
                    nurse.m_state.m_nurseState ==
                        NurseState.OverridenByProcedureScript;
            }

            BehaviorDoctor doctor =
                staff as BehaviorDoctor;

            if (doctor != null)
            {
                return doctor.m_state != null &&
                    doctor.m_state.m_doctorState ==
                        DoctorState.OverridenByProcedureScript;
            }

            BehaviorLabSpecialist lab =
                staff as BehaviorLabSpecialist;

            return lab != null &&
                lab.m_state != null &&
                lab.m_state.m_labSpecialistState ==
                    LabSpecialistState.OverridenByProcedureScript;
        }

        private static bool IsSupportedProcedureControlState(
            Behavior staff)
        {
            return IsReservedProcedureControlState(staff) ||
                IsActiveProcedureControlState(staff);
        }

        private static string GetStaffType(
            Behavior staff)
        {
            if (staff is BehaviorDoctor)
            {
                return "Doctor";
            }

            return staff is BehaviorLabSpecialist
                ? "LabSpecialist"
                : "Nurse";
        }

        private static string GetControlState(
            Behavior staff)
        {
            BehaviorNurse nurse =
                staff as BehaviorNurse;

            if (nurse != null &&
                nurse.m_state != null)
            {
                return nurse.m_state.m_nurseState
                    .ToString();
            }

            BehaviorDoctor doctor =
                staff as BehaviorDoctor;

            if (doctor != null &&
                doctor.m_state != null)
            {
                return doctor.m_state.m_doctorState
                    .ToString();
            }

            BehaviorLabSpecialist lab =
                staff as BehaviorLabSpecialist;

            return lab == null ||
                lab.m_state == null
                    ? "unknown"
                    : lab.m_state.m_labSpecialistState
                        .ToString();
        }

        private static string BuildDiagnosticContext(
            Behavior staff,
            Entity patient,
            string procedureLocID,
            string waitSource,
            string scriptType,
            string scriptState,
            string previousAnimation,
            bool? bubbleShown,
            PauseVisualKind kind)
        {
            string context =
                "staff={" +
                HfxDiagnostics.EntityContext(staff.m_entity) +
                "} staffType=" +
                GetStaffType(staff) +
                " controlState=" +
                GetControlState(staff) +
                " patient={" +
                HfxDiagnostics.EntityContext(patient) +
                "} procedure=" +
                (string.IsNullOrEmpty(procedureLocID)
                    ? "none"
                    : procedureLocID) +
                " pauseKind=" +
                kind.ToString() +
                " waitSource=" +
                (string.IsNullOrEmpty(waitSource)
                    ? "unknown"
                    : waitSource) +
                " scriptType=" +
                (string.IsNullOrEmpty(scriptType)
                    ? "unknown"
                    : scriptType) +
                " scriptState=" +
                (string.IsNullOrEmpty(scriptState)
                    ? "unknown"
                    : scriptState);

            if (previousAnimation != null)
            {
                context +=
                    " previousAnimation=" +
                    previousAnimation;
            }

            if (bubbleShown.HasValue)
            {
                context +=
                    " bubbleShown=" +
                    bubbleShown.Value;
            }

            return context;
        }

        private static bool TryShowWaitingBubble(
            Entity character,
            PauseVisualKind kind)
        {
            SpeechComponent speech =
                character == null
                    ? null
                    : character
                        .GetComponent<SpeechComponent>();

            if (speech == null)
            {
                return false;
            }

            if (IsShowingOwnWaitingBubble(character))
            {
                BubbleOwners.Add(character);
                return false;
            }

            if (speech.IsActive())
            {
                BubbleOwners.Remove(character);
                RenderedBubbleOwners.Remove(character);
                return false;
            }

            string bubbleAsset =
                GetBubbleAsset(kind);
            PauseKinds[character] = kind;

            StreamingAssetManager assets =
                StreamingAssetManager.GetInstance();
            Texture texture = null;

            bool assetReady =
                assets != null &&
                assets.m_textureAssets != null &&
                assets.m_textureAssets.TryGetValue(
                    bubbleAsset,
                    out texture) &&
                texture != null &&
                Database.Instance != null &&
                Database.Instance
                    .GetEntry<GameDBSpeechBubble>(
                        "BUBBLE_EMPTY") != null;

            if (!assetReady)
            {
                if (!s_missingAssetWarningLogged &&
                    Plugin.Log != null)
                {
                    s_missingAssetWarningLogged = true;
                    Plugin.Log.LogWarning(
                        "[ClinicalStaffPause] HFX pause bubble asset is unavailable; " +
                        "the stand_wait correction remains active.");
                }

                return false;
            }

            speech.SetBubble(
                "BUBBLE_EMPTY",
                bubbleAsset);
            BubbleOwners.Add(character);
            RenderedBubbleOwners.Remove(character);
            return true;
        }
    }

    // HFX's looping stand_wait is a visual placeholder only. Vanilla procedure
    // scripts frequently use IsIdle()/IsLooping() as control-flow gates, and
    // AnimModelComponent's own queue advances on IsIdle() && !IsLooping().
    // Report HFX's exact owned wait as logically idle/non-looping so native
    // scripts and queued animations can continue without delay.
    [HarmonyPatch(
        typeof(AnimModelComponent),
        nameof(AnimModelComponent.IsIdle))]
    internal static class ReservedStaffWaitingLogicalIdlePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            AnimModelComponent __instance,
            ref bool __result)
        {
            if (!ReservedStaffWaitingVisuals
                    .IsHfxWaitAnimation(__instance))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(AnimModelComponent),
        nameof(AnimModelComponent.IsLooping))]
    internal static class ReservedStaffWaitingLogicalLoopPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            AnimModelComponent __instance,
            ref bool __result)
        {
            if (!ReservedStaffWaitingVisuals
                    .IsHfxWaitAnimation(__instance))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(ProcedureManager),
        nameof(ProcedureManager.Update))]
    internal static class ReservedStaffWaitingProcedureManagerPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ProcedureManager __instance)
        {
            ReservedStaffWaitingVisuals
                .RefreshActiveProcedures(__instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.SetReserved))]
    internal static class ReservedNurseWaitingSetReservedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            BehaviorNurse __instance,
            bool reserved,
            string procedureLocID,
            Entity patient)
        {
            HandleSetReserved(
                __instance,
                reserved,
                procedureLocID,
                patient);
        }

        internal static void HandleSetReserved(
            Behavior staff,
            bool reserved,
            string procedureLocID,
            Entity patient)
        {
            if (staff == null)
            {
                return;
            }

            if (!reserved)
            {
                ReservedStaffWaitingVisuals
                    .ClearWaitingState(staff);
                return;
            }

            ReservedStaffWaitingVisuals
                .HandleWaitingState(
                    staff,
                    patient,
                    procedureLocID);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorDoctor),
        nameof(BehaviorDoctor.SetReserved))]
    internal static class ReservedDoctorWaitingSetReservedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            BehaviorDoctor __instance,
            bool reserved,
            string procedureLocID,
            Entity patient)
        {
            ReservedNurseWaitingSetReservedPatch
                .HandleSetReserved(
                    __instance,
                    reserved,
                    procedureLocID,
                    patient);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorLabSpecialist),
        nameof(BehaviorLabSpecialist.SetReserved))]
    internal static class ReservedLabSpecialistWaitingSetReservedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            BehaviorLabSpecialist __instance,
            bool reserved,
            string procedureLocID,
            Entity patient)
        {
            ReservedNurseWaitingSetReservedPatch
                .HandleSetReserved(
                    __instance,
                    reserved,
                    procedureLocID,
                    patient);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.Update))]
    internal static class ReservedNurseWaitingRefreshPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            BehaviorNurse __instance)
        {
            ReservedStaffWaitingVisuals
                .HandleCurrentReservation(
                    __instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorDoctor),
        nameof(BehaviorDoctor.Update))]
    internal static class ReservedDoctorWaitingRefreshPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            BehaviorDoctor __instance)
        {
            ReservedStaffWaitingVisuals
                .HandleCurrentReservation(
                    __instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorLabSpecialist),
        nameof(BehaviorLabSpecialist.Update))]
    internal static class ReservedLabSpecialistWaitingRefreshPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            BehaviorLabSpecialist __instance)
        {
            ReservedStaffWaitingVisuals
                .HandleCurrentReservation(
                    __instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.SwitchState))]
    internal static class ReservedNurseWaitingStateCleanupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            BehaviorNurse __instance,
            NurseState state)
        {
            if (__instance == null ||
                state ==
                    NurseState.OverridenReservedForProcedure ||
                state ==
                    NurseState.OverridenByProcedureScript)
            {
                return;
            }

            ReservedStaffWaitingVisuals
                .ClearWaitingState(__instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorDoctor),
        nameof(BehaviorDoctor.SwitchState))]
    internal static class ReservedDoctorWaitingStateCleanupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            BehaviorDoctor __instance,
            DoctorState state)
        {
            if (__instance == null ||
                state ==
                    DoctorState.OverridenReservedForProcedure ||
                state ==
                    DoctorState.OverridenByProcedureScript)
            {
                return;
            }

            ReservedStaffWaitingVisuals
                .ClearWaitingState(__instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorLabSpecialist),
        nameof(BehaviorLabSpecialist.SwitchState))]
    internal static class ReservedLabSpecialistWaitingStateCleanupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            BehaviorLabSpecialist __instance,
            LabSpecialistState state)
        {
            if (__instance == null ||
                state ==
                    LabSpecialistState.OverridenReservedForProcedure ||
                state ==
                    LabSpecialistState.OverridenByProcedureScript)
            {
                return;
            }

            ReservedStaffWaitingVisuals
                .ClearWaitingState(__instance);
        }
    }

    [HarmonyPatch(
        typeof(WalkComponent),
        nameof(WalkComponent.PostLoad))]
    internal static class ReservedStaffWaitingLoadPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            WalkComponent __instance)
        {
            if (__instance == null ||
                __instance.m_entity == null)
            {
                return;
            }

            Behavior staff =
                __instance.m_entity
                    .GetComponent<BehaviorNurse>();

            if (staff == null)
            {
                staff =
                    __instance.m_entity
                        .GetComponent<BehaviorDoctor>();
            }

            if (staff == null)
            {
                staff =
                    __instance.m_entity
                        .GetComponent<BehaviorLabSpecialist>();
            }

            if (staff == null)
            {
                return;
            }

            if (!FixConfig
                    .ClinicalProcedureWaitingVisuals)
            {
                ReservedStaffWaitingVisuals
                    .ClearWaitingState(staff);
                return;
            }

            ReservedStaffWaitingVisuals
                .HandleCurrentReservation(staff);
        }
    }

    [HarmonyPatch]
    internal static class ReservedStaffWaitingSpeechLoadCleanupPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Constructor(
                typeof(SpeechComponent),
                new Type[]
                {
                    typeof(Entity),
                    typeof(SpeechComponentPersistentData)
                });
        }

        [HarmonyPostfix]
        private static void Postfix(
            SpeechComponentPersistentData state)
        {
            ReservedStaffWaitingVisuals
                .ClearPersistedWaitingBubble(state);
        }
    }
}
