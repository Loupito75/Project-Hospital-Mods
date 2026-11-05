using System;
using System.Collections.Generic;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    internal static class HospitalBedDiagnosticState
    {
        private static HashSet<HospitalizationComponent> _missingBed;
        private static HashSet<HospitalizationComponent> _heldTransportBed;

        internal static void MarkMissingBed(
            HospitalizationComponent hospitalization)
        {
            if (!HfxDiagnostics.Enabled ||
                hospitalization == null)
            {
                return;
            }

            if (_missingBed == null)
            {
                _missingBed =
                    new HashSet<HospitalizationComponent>();
            }

            _missingBed.Add(hospitalization);
        }

        internal static bool IsMissingBedTracked(
            HospitalizationComponent hospitalization)
        {
            return HfxDiagnostics.Enabled &&
                hospitalization != null &&
                _missingBed != null &&
                _missingBed.Contains(hospitalization);
        }

        internal static bool ConsumeMissingBed(
            HospitalizationComponent hospitalization)
        {
            return HfxDiagnostics.Enabled &&
                hospitalization != null &&
                _missingBed != null &&
                _missingBed.Remove(hospitalization);
        }

        internal static void MarkHeldTransportBed(
            HospitalizationComponent hospitalization)
        {
            if (!HfxDiagnostics.Enabled ||
                hospitalization == null)
            {
                return;
            }

            if (_heldTransportBed == null)
            {
                _heldTransportBed =
                    new HashSet<HospitalizationComponent>();
            }

            _heldTransportBed.Add(hospitalization);
        }

        internal static bool IsHeldTransportBedTracked(
            HospitalizationComponent hospitalization)
        {
            return HfxDiagnostics.Enabled &&
                hospitalization != null &&
                _heldTransportBed != null &&
                _heldTransportBed.Contains(hospitalization);
        }

        internal static bool ConsumeHeldTransportBed(
            HospitalizationComponent hospitalization)
        {
            return HfxDiagnostics.Enabled &&
                hospitalization != null &&
                _heldTransportBed != null &&
                _heldTransportBed.Remove(hospitalization);
        }

        internal static void ReportPendingAndReset()
        {
            if (HfxDiagnostics.Enabled)
            {
                int missing =
                    _missingBed == null
                        ? 0
                        : _missingBed.Count;
                int transport =
                    _heldTransportBed == null
                        ? 0
                        : _heldTransportBed.Count;

                if (missing > 0)
                {
                    HfxDiagnostics.Observed(
                        "RecoverMissingHospitalBed",
                        "InGameMenuController.Destroy",
                        "pending-at-unload",
                        "patients=" + missing);
                }

                if (transport > 0)
                {
                    HfxDiagnostics.Observed(
                        "PreventPrematureTransportBedRelease",
                        "InGameMenuController.Destroy",
                        "pending-at-unload",
                        "patients=" + transport);
                }
            }

            if (_missingBed != null)
            {
                _missingBed.Clear();
                _missingBed = null;
            }

            if (_heldTransportBed != null)
            {
                _heldTransportBed.Clear();
                _heldTransportBed = null;
            }
        }
    }

    [HarmonyPatch(
        typeof(HospitalizationComponent),
        "CheckBedCabinet",
        new Type[] { typeof(bool) })]
    internal static class MissingHospitalBedCabinetGuardPatch
    {
        private static bool Prefix(
            HospitalizationComponent __instance)
        {
            if (!FixConfig.RecoverMissingHospitalBed ||
                __instance == null ||
                __instance.m_state == null)
            {
                return true;
            }

            bool hasValidBed =
                __instance.m_state.m_bed != null &&
                __instance.m_state.m_bed.GetEntity() != null;

            if (!hasValidBed)
            {
                if (HfxDiagnostics.Enabled)
                {
                    HospitalBedDiagnosticState.MarkMissingBed(
                        __instance);

                    string patientLabel =
                        HfxDiagnostics.EntityLabel(
                            __instance.m_entity);
                    HfxDiagnostics.InterventionRateLimited(
                        "RecoverMissingHospitalBed",
                        "HospitalizationComponent.CheckBedCabinet",
                        "cabinet-check-skipped-missing-bed",
                        "patient=" + patientLabel +
                        " state=" +
                        __instance.m_state.m_hospitalizationState +
                        " nativeRecovery=UpdateStateInBed.FindNewBed",
                        "RecoverMissingHospitalBed|" +
                        patientLabel,
                        5000);
                }

                return false;
            }

            if (HfxDiagnostics.Enabled &&
                HospitalBedDiagnosticState.ConsumeMissingBed(
                    __instance))
            {
                HfxDiagnostics.Result(
                    "RecoverMissingHospitalBed",
                    "HospitalizationComponent.CheckBedCabinet",
                    "bed-restored",
                    "patient=" +
                    HfxDiagnostics.EntityLabel(
                        __instance.m_entity) +
                    " state=" +
                    __instance.m_state.m_hospitalizationState +
                    " bedValid=true");
            }

            return true;
        }
    }

    // Diagnostic-only postcondition probe. It is not installed at all when
    // diagnostics are disabled.
    [HarmonyPatch(
        typeof(HospitalizationComponent),
        "UpdateStateInBed",
        new Type[] { typeof(float) })]
    internal static class MissingHospitalBedRecoveryResultPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return FixConfig.RecoverMissingHospitalBed &&
                FixConfig.EnableDiagnostics;
        }

        [HarmonyPostfix]
        private static void Postfix(
            HospitalizationComponent __instance)
        {
            if (!HfxDiagnostics.Enabled ||
                !HospitalBedDiagnosticState.IsMissingBedTracked(
                    __instance) ||
                __instance == null ||
                __instance.m_state == null ||
                __instance.m_state.m_hospitalizationState !=
                    HospitalizationState.FindNewBed)
            {
                return;
            }

            HfxDiagnostics.Result(
                "RecoverMissingHospitalBed",
                "HospitalizationComponent.UpdateStateInBed",
                "native-find-new-bed-started",
                "patient=" +
                HfxDiagnostics.EntityLabel(
                    __instance.m_entity) +
                " state=FindNewBed");
        }
    }

    internal static class OutgoingAmbulanceBedReleaseContext
    {
        [ThreadStatic]
        private static HospitalizationComponent _waitingForTransport;

        internal static HospitalizationComponent Current
        {
            get { return _waitingForTransport; }
            set { _waitingForTransport = value; }
        }
    }

    [HarmonyPatch(
        typeof(HospitalizationComponent),
        "UpdateStateWaitingForTransport")]
    internal static class WaitingForTransportBedReleaseContextPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return FixConfig.PreventPrematureTransportBedRelease;
        }

        private static void Prefix(
            HospitalizationComponent __instance,
            ref HospitalizationComponent __state)
        {
            __state =
                OutgoingAmbulanceBedReleaseContext.Current;
            OutgoingAmbulanceBedReleaseContext.Current =
                __instance;
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(
            Exception __exception,
            HospitalizationComponent __state)
        {
            OutgoingAmbulanceBedReleaseContext.Current =
                __state;
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(HospitalizationComponent),
        nameof(HospitalizationComponent.ClearBed))]
    internal static class PrematureTransportBedReleasePatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return FixConfig.PreventPrematureTransportBedRelease;
        }

        private static bool Prefix(
            HospitalizationComponent __instance)
        {
            if (!FixConfig.PreventPrematureTransportBedRelease ||
                !object.ReferenceEquals(
                    OutgoingAmbulanceBedReleaseContext.Current,
                    __instance))
            {
                return true;
            }

            bool keepReserved =
                ShouldKeepBedReserved(__instance);

            if (keepReserved &&
                HfxDiagnostics.Enabled)
            {
                HospitalBedDiagnosticState.MarkHeldTransportBed(
                    __instance);

                HfxDiagnostics.Intervention(
                    "PreventPrematureTransportBedRelease",
                    "HospitalizationComponent.ClearBed",
                    "premature-clearbed-blocked",
                    "patient=" +
                    HfxDiagnostics.EntityLabel(
                        __instance.m_entity) +
                    " state=WaitingForTransport" +
                    " sentAway=true" +
                    " stillOnReservedBed=true");
            }

            return !keepReserved;
        }

        private static bool ShouldKeepBedReserved(
            HospitalizationComponent hospitalization)
        {
            if (hospitalization == null ||
                hospitalization.m_state == null ||
                hospitalization.m_entity == null ||
                hospitalization.m_state.m_hospitalizationState !=
                    HospitalizationState.WaitingForTransport ||
                hospitalization.m_state.m_bed == null)
            {
                return false;
            }

            BehaviorPatient behavior =
                hospitalization.m_entity
                    .GetComponent<BehaviorPatient>();
            if (behavior == null ||
                behavior.m_state == null ||
                !behavior.m_state.m_sentAway)
            {
                return false;
            }

            WalkComponent walk =
                hospitalization.m_entity
                    .GetComponent<WalkComponent>();
            if (walk == null ||
                walk.m_state == null ||
                walk.m_state.m_objectSittingOn == null)
            {
                return false;
            }

            return walk.m_state.m_objectSittingOn ==
                hospitalization.m_state.m_bed;
        }
    }

    // Diagnostic-only confirmation of the native physical pickup path verified
    // in StretcherComponent.MoveToStretcher(). No patch is installed when the
    // diagnostic switch is off.
    [HarmonyPatch(
        typeof(StretcherComponent),
        nameof(StretcherComponent.MoveToStretcher),
        new Type[] { typeof(TileObject) })]
    internal static class TransportBedPhysicalPickupDiagnosticPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return FixConfig.PreventPrematureTransportBedRelease &&
                FixConfig.EnableDiagnostics;
        }

        [HarmonyPostfix]
        private static void Postfix(
            StretcherComponent __instance)
        {
            if (!HfxDiagnostics.Enabled ||
                __instance == null ||
                __instance.m_entity == null)
            {
                return;
            }

            HospitalizationComponent hospitalization =
                __instance.m_entity
                    .GetComponent<HospitalizationComponent>();
            if (hospitalization == null ||
                !HospitalBedDiagnosticState
                    .IsHeldTransportBedTracked(
                        hospitalization) ||
                hospitalization.m_state == null ||
                hospitalization.m_state.m_bed != null)
            {
                return;
            }

            HospitalBedDiagnosticState
                .ConsumeHeldTransportBed(
                    hospitalization);

            HfxDiagnostics.Result(
                "PreventPrematureTransportBedRelease",
                "StretcherComponent.MoveToStretcher",
                "bed-released-at-physical-pickup",
                "patient=" +
                HfxDiagnostics.EntityLabel(
                    __instance.m_entity) +
                " bed=null after native pickup");
        }
    }

    [HarmonyPatch(
        typeof(InGameMenuController),
        "Destroy")]
    internal static class HospitalBedDiagnosticCleanupPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return FixConfig.EnableDiagnostics &&
                (FixConfig.RecoverMissingHospitalBed ||
                 FixConfig.PreventPrematureTransportBedRelease);
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            HospitalBedDiagnosticState
                .ReportPendingAndReset();
        }
    }
}
