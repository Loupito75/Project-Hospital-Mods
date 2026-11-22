using System;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    // Vanilla already owns the elevator fade timing. On the normal early-fade
    // path it tries to fade the attached patient together with the mover, but
    // that lookup can miss a physically attached stretcher/wheelchair patient.
    // Mirror the same native FadeOut call at the mover's actual fade boundary.
    [HarmonyPatch(
        typeof(AnimModelComponent),
        nameof(AnimModelComponent.FadeOut))]
    internal static class AttachedPatientElevatorFadePatch
    {
        [ThreadStatic]
        private static bool s_mirroringAttachedPatient;

        [HarmonyPrepare]
        private static bool Prepare()
        {
            return FixConfig.FixAttachedPatientElevatorFade;
        }

        [HarmonyPostfix]
        private static void Postfix(
            AnimModelComponent __instance,
            float fadeTime)
        {
            if (s_mirroringAttachedPatient ||
                __instance == null ||
                __instance.m_state == null ||
                __instance.m_entity == null ||
                fadeTime <= 0f)
            {
                return;
            }

            Entity mover = __instance.m_entity;
            WalkComponent moverWalk =
                mover.GetComponent<WalkComponent>();
            if (moverWalk == null ||
                moverWalk.m_state == null ||
                moverWalk.Floor == null ||
                moverWalk.m_state.m_walkMidpoint1 == null ||
                moverWalk.Floor.m_floorIndex ==
                    moverWalk.m_state.m_walkMidpoint1
                        .m_destinationFloor)
            {
                return;
            }

            Entity attached =
                FindPhysicallyAttachedPatient(mover);
            if (attached == null)
            {
                return;
            }

            AnimModelComponent patientAnimation =
                attached.GetComponent<AnimModelComponent>();
            if (patientAnimation == null ||
                patientAnimation.m_state == null ||
                patientAnimation.m_state.m_fadeState ==
                    FadeState.FADING_OUT ||
                patientAnimation.m_state.m_fadeState ==
                    FadeState.FADED_OUT)
            {
                return;
            }

            try
            {
                s_mirroringAttachedPatient = true;
                patientAnimation.FadeOut(fadeTime);
            }
            finally
            {
                s_mirroringAttachedPatient = false;
            }

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Intervention(
                    "FixAttachedPatientElevatorFade",
                    "AnimModelComponent.FadeOut",
                    "attached-patient-elevator-fade-started-with-mover",
                    "mover={" +
                    HfxDiagnostics.EntityContext(mover) +
                    "} patient={" +
                    HfxDiagnostics.EntityContext(attached) +
                    "} fadeTime=" +
                    fadeTime);
            }
        }

        private static Entity FindPhysicallyAttachedPatient(
            Entity mover)
        {
            if (mover == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_characters == null)
            {
                return null;
            }

            foreach (Entity candidate in
                Hospital.Instance.m_characters)
            {
                if (candidate == null)
                {
                    continue;
                }

                StretcherComponent stretcher =
                    candidate.GetComponent<StretcherComponent>();
                if (stretcher == null ||
                    stretcher.m_state == null ||
                    stretcher.m_state.m_movingCharacter == null ||
                    stretcher.m_state.m_movingCharacter
                        .GetEntity() != mover ||
                    stretcher.m_state.m_stretcherPatientSide ==
                        null)
                {
                    continue;
                }

                TileObject patientSide =
                    stretcher.m_state.m_stretcherPatientSide
                        .GetEntity();
                if (patientSide == null ||
                    !patientSide.IsAttachedToCharacter() ||
                    patientSide.User != mover)
                {
                    continue;
                }

                WalkComponent patientWalk =
                    candidate.GetComponent<WalkComponent>();
                if (patientWalk == null ||
                    patientWalk.m_state == null ||
                    patientWalk.m_state.m_objectSittingOn ==
                        null ||
                    patientWalk.m_state.m_objectSittingOn
                        .GetEntity() != patientSide)
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }
    }
}
