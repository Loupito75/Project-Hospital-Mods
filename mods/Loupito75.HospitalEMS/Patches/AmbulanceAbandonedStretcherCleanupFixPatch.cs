using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalEMS.Patches
{
    [HarmonyPatch(typeof(Ambulance), "DEBUG_ClearAbandonedStretchers")]
    internal static class AmbulanceAbandonedStretcherCleanupFixPatch
    {
        private static bool Prefix(Ambulance __instance)
        {
            List<TileObject> toRemove = new List<TileObject>();
            __instance.LogError(
                "Ambulance has paramedic " + __instance.m_state.m_paramedic);

            foreach (TileObject movingObject in
                Hospital.Instance.GetGroundFloor().m_movingObjects)
            {
                if (movingObject.m_state.m_position.m_x > 9)
                {
                    continue;
                }

                string cleanupMessage = null;

                if (movingObject.m_state.m_user ==
                    __instance.m_state.m_paramedic)
                {
                    cleanupMessage =
                        "Clearing abandoned stretcher assigned to paramedic " +
                        __instance.m_state.m_paramedic;
                }
                else if (movingObject.m_state.m_owner == null &&
                         movingObject.m_state.m_user.GetEntity() != null &&
                         movingObject.m_state.m_user.GetEntity()
                             .GetComponent<BehaviorParamedic>() != null &&
                         movingObject.m_state.m_user.GetEntity()
                             .GetComponent<BehaviorParamedic>()
                             .m_state.m_paramedicState == ParamedicState.Idle)
                {
                    cleanupMessage =
                        "Clearing abandoned stretcher assigned to idle paramedic " +
                        movingObject.m_state.m_user.GetEntity().Name;
                }
                else if (movingObject.m_state.m_owner == null &&
                         movingObject.m_state.m_user == null &&
                         (movingObject.HasTag("stretcher") ||
                          movingObject.HasTag("stretcher_patient")))
                {
                    cleanupMessage =
                        "Clearing abandoned stretcher on the road";
                }

                if (cleanupMessage == null)
                {
                    continue;
                }

                Entity movingCharacter =
                    movingObject.m_state.m_user.GetEntity();
                if (IsActiveAmbulanceStretcherPart(
                    movingObject,
                    movingCharacter))
                {
                    continue;
                }

                movingObject.LogError(cleanupMessage);
                toRemove.Add(movingObject);
            }

            foreach (TileObject movingObject in toRemove)
            {
                Hospital.Instance.GetGroundFloor().m_movingObjects.Remove(
                    movingObject);
            }

            return false;
        }

        private static bool IsActiveAmbulanceStretcherPart(
            TileObject movingObject,
            Entity movingCharacter)
        {
            if (movingCharacter == null)
            {
                return false;
            }

            BehaviorParamedic paramedic =
                movingCharacter.GetComponent<BehaviorParamedic>();
            if (paramedic == null ||
                paramedic.m_state == null ||
                paramedic.m_state.m_currentPatient == null)
            {
                return false;
            }

            Entity patient =
                paramedic.m_state.m_currentPatient.GetEntity();
            if (patient == null)
            {
                return false;
            }

            StretcherComponent stretcher =
                patient.GetComponent<StretcherComponent>();
            if (stretcher == null || stretcher.m_state == null)
            {
                return false;
            }

            if (stretcher.m_state.m_movingCharacter != movingCharacter)
            {
                return false;
            }

            return stretcher.m_state.m_stretcherPatientSide == movingObject ||
                stretcher.m_state.m_stretcherMovingCharacterSide ==
                    movingObject;
        }
    }
}
