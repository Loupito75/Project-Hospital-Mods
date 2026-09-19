using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    [HarmonyPatch]
    internal static class LyingPatientYellowBookcasePatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(ProcedureScriptControlHopitalizedLyingFreeTime),
                "GetEntertainmentItem",
                new Type[]
                {
                    typeof(Entity),
                    typeof(string)
                });

            if (method == null)
            {
                throw new MissingMethodException(
                    "ProcedureScriptControlHopitalizedLyingFreeTime.GetEntertainmentItem(Entity, string) was not found.");
            }

            return method;
        }

        [HarmonyPostfix]
        private static void Postfix(
            ProcedureScriptControlHopitalizedLyingFreeTime __instance,
            Entity mainCharacter,
            string tag,
            ref TileObject __result)
        {
            if (__result != null ||
                __instance == null ||
                mainCharacter == null ||
                tag != "education")
            {
                return;
            }

            PatientLeisureConfig.EnsureLoaded();
            if (!PatientLeisureConfig.AllowYellowLeisureObjectInteraction)
            {
                return;
            }

            HospitalizationComponent hospitalization =
                mainCharacter.GetComponent<HospitalizationComponent>();
            if (hospitalization == null || !hospitalization.IsHospitalized())
            {
                return;
            }

            Entity nurse = GetAssignedNurse(__instance);
            if (nurse == null || nurse.GetComponent<BehaviorNurse>() == null)
            {
                return;
            }

            WalkComponent patientWalk = mainCharacter.GetComponent<WalkComponent>();
            if (patientWalk == null || MapScriptInterface.Instance == null)
            {
                return;
            }

            Room room = MapScriptInterface.Instance.GetRoomAt(patientWalk);
            if (room == null)
            {
                return;
            }

            List<TileObject> candidates =
                WallBookcaseLeisureAccess.FindEligibleBookcases(
                    null,
                    room,
                    new string[1] { "education" });

            TileObject bestBookcase = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                TileObject candidate = candidates[i];
                if (candidate == null ||
                    candidate.User != null ||
                    candidate.Owner != null)
                {
                    continue;
                }

                float distance = GetNurseRouteDistance(nurse, candidate);
                if (distance >= 0f && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestBookcase = candidate;
                }
            }

            if (bestBookcase == null)
            {
                return;
            }

            __result = bestBookcase;

            if (HospitalizedPatientTrace.Enabled)
            {
                Vector2i usePosition = bestBookcase.GetDefaultUseTile();
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(mainCharacter) +
                    " | lying-patient-bookcase=SELECTED" +
                    " | nurse=" + HospitalizedPatientTrace.GetName(nurse) +
                    " | floor=" + bestBookcase.GetFloorIndex() +
                    " | position=" + bestBookcase.m_state.m_position.m_x +
                    "," + bestBookcase.m_state.m_position.m_y +
                    " | usePosition=" + usePosition.m_x +
                    "," + usePosition.m_y +
                    " | routeDistance=" + bestDistance.ToString("0.0"));
            }
        }

        private static Entity GetAssignedNurse(
            ProcedureScriptControlHopitalizedLyingFreeTime script)
        {
            if (script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_procedureScene == null ||
                script.m_stateData.m_procedureScene.Nurse == null)
            {
                return null;
            }

            return script.m_stateData.m_procedureScene.Nurse.GetEntity();
        }

        private static float GetNurseRouteDistance(
            Entity nurse,
            TileObject target)
        {
            if (nurse == null ||
                target == null ||
                !WallBookcaseLeisureAccess.IsAllowedNonValidBookcase(target) ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return -1f;
            }

            int floorIndex = target.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return -1f;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            WalkComponent walk = nurse.GetComponent<WalkComponent>();
            GridMap gridMap = GridMap.GetInstance();
            if (floor == null || walk == null || gridMap == null)
            {
                return -1f;
            }

            Vector2i usePosition = target.GetDefaultUseTile();
            if (usePosition.m_x < 0 ||
                usePosition.m_y < 0 ||
                usePosition.m_x >= floor.m_size.m_x ||
                usePosition.m_y >= floor.m_size.m_y ||
                floor.IsAnyBlockingObjectAt(usePosition) ||
                YellowPosterLeisureAccess.IsUsePositionOccupied(
                    nurse,
                    usePosition,
                    floorIndex) ||
                IsUsePositionTargetedByAnotherCharacter(
                    nurse,
                    usePosition,
                    floorIndex))
            {
                return -1f;
            }

            return gridMap.GetDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                floorIndex,
                usePosition,
                AccessRights.STAFF);
        }

        private static bool IsUsePositionTargetedByAnotherCharacter(
            Entity nurse,
            Vector2i usePosition,
            int floorIndex)
        {
            if (Hospital.Instance == null || Hospital.Instance.m_characters == null)
            {
                return false;
            }

            for (int i = 0; i < Hospital.Instance.m_characters.Count; i++)
            {
                Entity character = Hospital.Instance.m_characters[i];
                if (character == null || character == nurse)
                {
                    continue;
                }

                WalkComponent walk = character.GetComponent<WalkComponent>();
                if (walk == null ||
                    walk.m_state == null ||
                    !walk.IsBusy() ||
                    walk.m_state.m_destinationFloor != floorIndex)
                {
                    continue;
                }

                if (walk.GetDestinationTile() == usePosition)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
