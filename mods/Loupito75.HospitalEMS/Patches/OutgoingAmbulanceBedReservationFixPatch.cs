using System;
using HarmonyLib;
using Lopital;

namespace HospitalEMS.Patches
{
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
        private static void Prefix(
            HospitalizationComponent __instance,
            ref HospitalizationComponent __state)
        {
            __state = OutgoingAmbulanceBedReleaseContext.Current;
            OutgoingAmbulanceBedReleaseContext.Current = __instance;
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(
            Exception __exception,
            HospitalizationComponent __state)
        {
            OutgoingAmbulanceBedReleaseContext.Current = __state;
            return __exception;
        }
    }

    [HarmonyPatch(
        typeof(HospitalizationComponent),
        nameof(HospitalizationComponent.ClearBed))]
    internal static class OutgoingAmbulanceEarlyBedReleasePatch
    {
        private static bool Prefix(HospitalizationComponent __instance)
        {
            if (!object.ReferenceEquals(
                    OutgoingAmbulanceBedReleaseContext.Current,
                    __instance))
            {
                return true;
            }

            return !ShouldKeepBedReserved(__instance);
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
                hospitalization.m_entity.GetComponent<BehaviorPatient>();
            if (behavior == null ||
                behavior.m_state == null ||
                !behavior.m_state.m_sentAway)
            {
                return false;
            }

            WalkComponent walk =
                hospitalization.m_entity.GetComponent<WalkComponent>();
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
}
