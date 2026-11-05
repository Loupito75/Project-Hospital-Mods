using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using GLib;
using Lopital;

namespace HospitalFixes.Patches
{
    internal static class HshStaffClothingChangeIntegration
    {
        private const string HshTypeName =
            "HospitalShiftHandover.PreShiftLockerInteraction";

        // Start the overlay shortly before HSH completes the locker-use
        // animation. HSH still owns the actual swap and workflow timing.
        private const float StaffEffectLeadTime = 0.30f;

        private static bool Installed;
        private static FieldInfo IncomingStatesField;
        private static FieldInfo IncomingPhaseField;
        private static FieldInfo OutgoingStatesField;
        private static FieldInfo OutgoingPhaseField;

        private static readonly Dictionary<Entity, bool>
            PrestartedEffects =
                new Dictionary<Entity, bool>();

        private sealed class SwapState
        {
            internal Entity Entity;
            internal Clothes ClothesBefore;
            internal bool EffectStarted;
        }

        internal static void Install(Harmony harmony)
        {
            if (Installed ||
                harmony == null ||
                !FixConfig.IsHshStaffClothingAnimationEnabled)
            {
                return;
            }

            try
            {
                Type hshType =
                    AccessTools.TypeByName(HshTypeName);
                if (hshType == null)
                {
                    Plugin.Log.LogWarning(
                        "[HSH Clothing] HSH type not found; visual integration disabled.");
                    return;
                }

                MethodInfo outgoing =
                    AccessTools.Method(
                        hshType,
                        "ApplyOutgoingCivilianClothes",
                        new Type[]
                        {
                            typeof(Behavior),
                            typeof(EmployeeComponent)
                        });

                MethodInfo incoming =
                    AccessTools.Method(
                        hshType,
                        "RestoreProfessionalClothes",
                        new Type[]
                        {
                            typeof(EmployeeComponent),
                            typeof(string)
                        });

                MethodInfo incomingUpdate =
                    AccessTools.Method(
                        hshType,
                        "TryHandle",
                        new Type[]
                        {
                            typeof(Behavior),
                            typeof(EmployeeComponent),
                            typeof(float)
                        });

                MethodInfo outgoingUpdate =
                    AccessTools.Method(
                        hshType,
                        "TryHandleOutgoing",
                        new Type[]
                        {
                            typeof(Behavior)
                        });

                IncomingStatesField =
                    AccessTools.Field(
                        hshType,
                        "States");
                OutgoingStatesField =
                    AccessTools.Field(
                        hshType,
                        "OutgoingStates");

                if (outgoing == null ||
                    incoming == null)
                {
                    Plugin.Log.LogWarning(
                        "[HSH Clothing] Expected HSH dressing methods were not found; visual integration disabled.");
                    return;
                }

                harmony.Patch(
                    outgoing,
                    prefix: new HarmonyMethod(
                        typeof(HshStaffClothingChangeIntegration),
                        "OutgoingPrefix"),
                    postfix: new HarmonyMethod(
                        typeof(HshStaffClothingChangeIntegration),
                        "OutgoingPostfix"));

                harmony.Patch(
                    incoming,
                    prefix: new HarmonyMethod(
                        typeof(HshStaffClothingChangeIntegration),
                        "IncomingPrefix"),
                    postfix: new HarmonyMethod(
                        typeof(HshStaffClothingChangeIntegration),
                        "IncomingPostfix"));

                if (incomingUpdate != null &&
                    IncomingStatesField != null)
                {
                    harmony.Patch(
                        incomingUpdate,
                        prefix: new HarmonyMethod(
                            typeof(HshStaffClothingChangeIntegration),
                            "IncomingUpdatePrefix"));
                }

                if (outgoingUpdate != null &&
                    OutgoingStatesField != null)
                {
                    harmony.Patch(
                        outgoingUpdate,
                        prefix: new HarmonyMethod(
                            typeof(HshStaffClothingChangeIntegration),
                            "OutgoingUpdatePrefix"));
                }

                Installed = true;

                if (HfxDiagnostics.Enabled)
                {
                    Plugin.Log.LogInfo(
                        "[Diagnostics] HSH locker clothing visual integration enabled.");
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    "[HSH Clothing] Optional integration failed safely: " +
                    exception.GetType().Name + ": " +
                    exception.Message);
            }
        }

        private static void IncomingUpdatePrefix(
            Behavior behavior,
            EmployeeComponent employee,
            float activitySlackMinutes)
        {
            TryPrestartEffect(
                employee,
                IncomingStatesField,
                ref IncomingPhaseField);
        }

        private static void OutgoingUpdatePrefix(
            Behavior behavior)
        {
            EmployeeComponent employee =
                behavior != null
                    ? behavior.GetComponent<EmployeeComponent>()
                    : null;

            TryPrestartEffect(
                employee,
                OutgoingStatesField,
                ref OutgoingPhaseField);
        }

        private static void TryPrestartEffect(
            EmployeeComponent employee,
            FieldInfo statesField,
            ref FieldInfo phaseField)
        {
            if (!FixConfig.IsHshStaffClothingAnimationEnabled ||
                employee == null ||
                employee.m_entity == null ||
                statesField == null)
            {
                return;
            }

            Entity entity =
                employee.m_entity;

            bool usingLocker =
                IsUsingLocker(
                    employee,
                    statesField,
                    ref phaseField);

            if (!usingLocker)
            {
                if (PrestartedEffects.ContainsKey(entity))
                {
                    ClothingChangeEffectRenderer.Stop(entity);
                    PrestartedEffects.Remove(entity);
                }

                return;
            }

            if (PrestartedEffects.ContainsKey(entity))
            {
                return;
            }

            WalkComponent walk =
                entity.GetComponent<WalkComponent>();
            AnimModelComponent animation =
                entity.GetComponent<AnimModelComponent>();

            if (animation == null ||
                animation.m_state == null ||
                (walk != null && walk.IsOnBiohazard()) ||
                (WorldEventManager.Instance != null &&
                 WorldEventManager.Instance
                     .HasEventForcingBiohazardClothes()))
            {
                return;
            }

            float duration =
                animation.GetCurrentAnimationDuration();
            float position =
                animation.GetCurrentAnimationPlaybackPosition();

            if (duration <= 0f ||
                position < 0f)
            {
                return;
            }

            float remaining =
                duration - position;

            if (remaining <= 0f ||
                remaining > StaffEffectLeadTime)
            {
                return;
            }

            ClothingChangeEffectRenderer.StartStandalone(
                entity,
                animation);
            PrestartedEffects[entity] = true;
        }

        private static bool IsUsingLocker(
            EmployeeComponent employee,
            FieldInfo statesField,
            ref FieldInfo phaseField)
        {
            if (employee == null ||
                statesField == null)
            {
                return false;
            }

            IDictionary states =
                statesField.GetValue(null)
                    as IDictionary;
            if (states == null ||
                !states.Contains(employee))
            {
                return false;
            }

            object state =
                states[employee];
            if (state == null)
            {
                return false;
            }

            Type stateType =
                state.GetType();

            if (phaseField == null ||
                phaseField.DeclaringType != stateType)
            {
                phaseField =
                    AccessTools.Field(
                        stateType,
                        "Phase");
            }

            if (phaseField == null)
            {
                return false;
            }

            object phase =
                phaseField.GetValue(state);

            return phase != null &&
                string.Equals(
                    phase.ToString(),
                    "UsingLocker",
                    StringComparison.Ordinal);
        }

        internal static void ClearTransientState()
        {
            PrestartedEffects.Clear();
            IncomingPhaseField = null;
            OutgoingPhaseField = null;
        }

        private static void OutgoingPrefix(
            Behavior behavior,
            EmployeeComponent employee,
            out SwapState __state)
        {
            __state =
                CaptureAndStart(employee);
        }

        private static void OutgoingPostfix(
            bool __result,
            SwapState __state)
        {
            if (__state == null ||
                !__state.EffectStarted)
            {
                return;
            }

            bool clothesChanged =
                __result &&
                DidClothesChange(__state);

            if (!clothesChanged)
            {
                ClothingChangeEffectRenderer.Stop(
                    __state.Entity);
            }

            if (HfxDiagnostics.Enabled)
            {
                string employeeLabel =
                    HfxDiagnostics.EntityLabel(
                        __state.Entity);

                if (clothesChanged)
                {
                    HfxDiagnostics.Result(
                        "AnimateHshStaffClothingChanges",
                        "HospitalShiftHandover outgoing locker swap",
                        "clothing-change-confirmed",
                        "employee=" + employeeLabel +
                        " nativeResult=true");
                }
                else if (!__result)
                {
                    HfxDiagnostics.Fallback(
                        "AnimateHshStaffClothingChanges",
                        "HospitalShiftHandover outgoing locker swap",
                        "native-rejected",
                        "employee=" + employeeLabel);
                }
                else
                {
                    HfxDiagnostics.Observed(
                        "AnimateHshStaffClothingChanges",
                        "HospitalShiftHandover outgoing locker swap",
                        "no-clothing-change",
                        "employee=" + employeeLabel +
                        " nativeResult=true");
                }
            }
        }

        private static void IncomingPrefix(
            EmployeeComponent employee,
            string reason,
            out SwapState __state)
        {
            __state = null;

            if (!string.Equals(
                    reason,
                    "locker-use-complete",
                    StringComparison.Ordinal))
            {
                return;
            }

            __state =
                CaptureAndStart(employee);
        }

        private static void IncomingPostfix(
            SwapState __state)
        {
            if (__state == null ||
                !__state.EffectStarted)
            {
                return;
            }

            bool clothesChanged =
                DidClothesChange(__state);

            if (!clothesChanged)
            {
                ClothingChangeEffectRenderer.Stop(
                    __state.Entity);
            }

            if (HfxDiagnostics.Enabled)
            {
                string employeeLabel =
                    HfxDiagnostics.EntityLabel(
                        __state.Entity);

                if (clothesChanged)
                {
                    HfxDiagnostics.Result(
                        "AnimateHshStaffClothingChanges",
                        "HospitalShiftHandover incoming locker swap",
                        "clothing-change-confirmed",
                        "employee=" + employeeLabel);
                }
                else
                {
                    HfxDiagnostics.Observed(
                        "AnimateHshStaffClothingChanges",
                        "HospitalShiftHandover incoming locker swap",
                        "no-clothing-change",
                        "employee=" + employeeLabel);
                }
            }
        }

        private static SwapState CaptureAndStart(
            EmployeeComponent employee)
        {
            if (!FixConfig.IsHshStaffClothingAnimationEnabled ||
                employee == null ||
                employee.m_entity == null)
            {
                return null;
            }

            Entity entity =
                employee.m_entity;
            WalkComponent walk =
                entity.GetComponent<WalkComponent>();
            AnimModelComponent animation =
                entity.GetComponent<AnimModelComponent>();

            if (walk == null ||
                animation == null ||
                animation.m_state == null ||
                walk.IsOnBiohazard())
            {
                return null;
            }

            SwapState state =
                new SwapState();
            state.Entity = entity;
            state.ClothesBefore =
                animation.m_state.m_clothes;

            if (PrestartedEffects.ContainsKey(entity))
            {
                PrestartedEffects.Remove(entity);
                state.EffectStarted = true;
            }
            else
            {
                ClothingChangeEffectRenderer.StartStandalone(
                    entity,
                    animation);
                state.EffectStarted = true;
            }

            return state;
        }

        private static bool DidClothesChange(
            SwapState state)
        {
            if (state == null ||
                state.Entity == null)
            {
                return false;
            }

            AnimModelComponent animation =
                state.Entity.GetComponent<AnimModelComponent>();
            if (animation == null ||
                animation.m_state == null)
            {
                return false;
            }

            return !object.ReferenceEquals(
                state.ClothesBefore,
                animation.m_state.m_clothes);
        }
    }
}
