using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    [HarmonyPatch]
    internal static class HospitalizedRoomLeisureWeightPatch
    {
        private enum LeisureKind
        {
            Rest,
            Education,
            Visual,
            Television
        }

        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(ProcedureScriptControlHopitalizedFreeTime),
                "GetEntertainmentItem",
                new Type[]
                {
                    typeof(Entity),
                    typeof(string)
                });

            if (method == null)
            {
                throw new MissingMethodException(
                    "ProcedureScriptControlHopitalizedFreeTime.GetEntertainmentItem(Entity, string) was not found.");
            }

            return method;
        }

        [HarmonyPostfix]
        private static void Postfix(
            ProcedureScriptControlHopitalizedFreeTime __instance,
            Entity mainCharacter,
            string tag,
            ref TileObject __result)
        {
            if (__instance == null ||
                mainCharacter == null ||
                tag != "hospitalized_patient")
            {
                return;
            }

            HospitalizedTelevisionAccess.ClearPending(__instance);

            HospitalizationComponent hospitalization =
                mainCharacter.GetComponent<HospitalizationComponent>();
            WalkComponent walk = mainCharacter.GetComponent<WalkComponent>();
            if (hospitalization == null ||
                !hospitalization.IsHospitalized() ||
                !hospitalization.IsAllowedToWalk() ||
                hospitalization.m_state == null ||
                walk == null)
            {
                return;
            }

            Room currentRoom = MapScriptInterface.Instance.GetRoomAt(walk);
            if (currentRoom == null)
            {
                return;
            }

            if (__result != null)
            {
                Room resultRoom = MapScriptInterface.Instance.GetRoomAt(
                    __result.m_state.m_position,
                    __result.GetFloorIndex());
                if (resultRoom != currentRoom)
                {
                    return;
                }
            }

            PatientLeisureConfig.EnsureLoaded();
            if (PatientLeisureConfig.RoomRestActivityWeight == 0 &&
                PatientLeisureConfig.RoomEducationActivityWeight == 0 &&
                PatientLeisureConfig.RoomVisualActivityWeight == 0 &&
                PatientLeisureConfig.RoomTelevisionActivityWeight == 0)
            {
                return;
            }

            List<TileObject> allCandidates =
                MapScriptInterface.Instance.FindAllObjectWithTags(
                    currentRoom,
                    new string[] { "hospitalized_patient" },
                    AccessRights.PATIENT);

            if (allCandidates == null)
            {
                allCandidates = new List<TileObject>();
            }

            if (PatientLeisureConfig.AllowYellowLeisureObjectInteraction)
            {
                List<TileObject> yellowPosters =
                    YellowPosterLeisureAccess.FindEligiblePosters(
                        mainCharacter,
                        currentRoom,
                        "hospitalized_patient");
                for (int i = 0; i < yellowPosters.Count; i++)
                {
                    if (!allCandidates.Contains(yellowPosters[i]))
                    {
                        allCandidates.Add(yellowPosters[i]);
                    }
                }
            }

            List<TileObject> rest = new List<TileObject>();
            List<TileObject> education = new List<TileObject>();
            List<TileObject> visual = new List<TileObject>();

            for (int i = 0; i < allCandidates.Count; i++)
            {
                TileObject candidate = allCandidates[i];
                if (candidate == null || candidate.User != null)
                {
                    continue;
                }

                if (candidate.HasTag("rest"))
                {
                    rest.Add(candidate);
                }
                else if (candidate.HasTag("education"))
                {
                    education.Add(candidate);
                }
                else
                {
                    visual.Add(candidate);
                }
            }

            TileObject television =
                HospitalizedTelevisionAccess.FindClosestTelevision(
                    walk,
                    currentRoom);
            TileObject televisionSeat =
                FindClosestAvailableRestSeat(
                    mainCharacter,
                    rest);

            TileObject bed = hospitalization.m_state.m_bed == null
                ? null
                : hospitalization.m_state.m_bed.GetEntity();
            bool canWatchFromBed =
                bed != null && walk.IsSittingOn(bed);
            bool canWatchFromSeat =
                televisionSeat != null;
            bool televisionAvailable =
                television != null &&
                (canWatchFromBed || canWatchFromSeat);

            LeisureKind selectedKind;
            int roll;
            int totalWeight;
            if (!TryChooseKind(
                    rest.Count,
                    education.Count,
                    visual.Count,
                    televisionAvailable,
                    out selectedKind,
                    out roll,
                    out totalWeight))
            {
                return;
            }

            TileObject vanillaTarget = __result;

            if (selectedKind == LeisureKind.Television)
            {
                bool watchFromSeat = false;
                int locationRoll = -1;

                if (canWatchFromBed && canWatchFromSeat)
                {
                    locationRoll = UnityEngine.Random.Range(0, 2);
                    watchFromSeat = locationRoll == 1;
                }
                else if (canWatchFromSeat)
                {
                    watchFromSeat = true;
                }

                if (watchFromSeat)
                {
                    __result = televisionSeat;
                    HospitalizedTelevisionAccess.QueueAmbulatoryTelevision(
                        __instance,
                        mainCharacter,
                        televisionSeat,
                        televisionSeat,
                        roll,
                        totalWeight,
                        locationRoll);
                }
                else
                {
                    HospitalizedTelevisionAccess.QueueAmbulatoryTelevision(
                        __instance,
                        mainCharacter,
                        null,
                        __result,
                        roll,
                        totalWeight,
                        locationRoll);
                }

                if (HospitalizedPatientTrace.Enabled)
                {
                    HospitalizedPatientTrace.Log(
                        HospitalizedPatientTrace.GetName(mainCharacter) +
                        " | room-leisure-weight=SELECTED" +
                        " | activity=TELEVISION" +
                        " | target=" + GetObjectId(television) +
                        " | watchFrom=" +
                        (watchFromSeat ? "SEAT" : "BED") +
                        " | weights=" +
                        PatientLeisureConfig.RoomRestActivityWeight + "/" +
                        PatientLeisureConfig.RoomEducationActivityWeight + "/" +
                        PatientLeisureConfig.RoomVisualActivityWeight + "/" +
                        PatientLeisureConfig.RoomTelevisionActivityWeight +
                        " | roll=" + roll + "/" + totalWeight +
                        " | locationRoll=" + locationRoll +
                        " | rest=" + rest.Count +
                        " | education=" + education.Count +
                        " | visual=" + visual.Count +
                        " | television=1" +
                        " | vanillaTarget=" + GetObjectId(vanillaTarget));
                }

                return;
            }

            List<TileObject> selectedCandidates = visual;
            if (selectedKind == LeisureKind.Rest)
            {
                selectedCandidates = rest;
            }
            else if (selectedKind == LeisureKind.Education)
            {
                selectedCandidates = education;
            }

            TileObject selected = FindClosestByRoute(
                mainCharacter,
                selectedCandidates);
            if (selected == null)
            {
                return;
            }

            __result = selected;

            if (YellowPosterLeisureAccess.IsYellowPoster(selected))
            {
                YellowPosterLeisureAccess.MarkForPatientUse(selected);
            }

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(mainCharacter) +
                    " | room-leisure-weight=SELECTED" +
                    " | activity=" + selectedKind.ToString().ToUpperInvariant() +
                    " | target=" + GetObjectId(selected) +
                    " | weights=" +
                    PatientLeisureConfig.RoomRestActivityWeight + "/" +
                    PatientLeisureConfig.RoomEducationActivityWeight + "/" +
                    PatientLeisureConfig.RoomVisualActivityWeight + "/" +
                    PatientLeisureConfig.RoomTelevisionActivityWeight +
                    " | roll=" + roll + "/" + totalWeight +
                    " | rest=" + rest.Count +
                    " | education=" + education.Count +
                    " | visual=" + visual.Count +
                    " | television=" + (televisionAvailable ? "1" : "0") +
                    " | vanillaTarget=" + GetObjectId(vanillaTarget));
            }
        }

        private static bool TryChooseKind(
            int restCount,
            int educationCount,
            int visualCount,
            bool televisionAvailable,
            out LeisureKind selectedKind,
            out int roll,
            out int totalWeight)
        {
            int restWeight = restCount > 0
                ? PatientLeisureConfig.RoomRestActivityWeight
                : 0;
            int educationWeight = educationCount > 0
                ? PatientLeisureConfig.RoomEducationActivityWeight
                : 0;
            int visualWeight = visualCount > 0
                ? PatientLeisureConfig.RoomVisualActivityWeight
                : 0;
            int televisionWeight = televisionAvailable
                ? PatientLeisureConfig.RoomTelevisionActivityWeight
                : 0;

            totalWeight =
                restWeight +
                educationWeight +
                visualWeight +
                televisionWeight;
            roll = -1;
            selectedKind = LeisureKind.Visual;

            if (totalWeight <= 0)
            {
                return false;
            }

            roll = UnityEngine.Random.Range(0, totalWeight);
            if (roll < restWeight)
            {
                selectedKind = LeisureKind.Rest;
                return true;
            }

            if (roll < restWeight + educationWeight)
            {
                selectedKind = LeisureKind.Education;
                return true;
            }

            if (roll < restWeight + educationWeight + visualWeight)
            {
                selectedKind = LeisureKind.Visual;
                return true;
            }

            selectedKind = LeisureKind.Television;
            return true;
        }

        private static TileObject FindClosestAvailableRestSeat(
            Entity patient,
            List<TileObject> candidates)
        {
            TileObject result = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                TileObject candidate = candidates[i];
                if (candidate == null ||
                    candidate.User != null ||
                    candidate.Owner != null ||
                    candidate.IsBroken() ||
                    !candidate.IsValid())
                {
                    continue;
                }

                float distance = YellowPosterLeisureAccess.GetRouteDistance(
                    patient,
                    candidate);
                if (distance >= 0f && distance < bestDistance)
                {
                    bestDistance = distance;
                    result = candidate;
                }
            }

            return result;
        }

        private static TileObject FindClosestByRoute(
            Entity patient,
            List<TileObject> candidates)
        {
            TileObject result = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                TileObject candidate = candidates[i];
                float distance = YellowPosterLeisureAccess.GetRouteDistance(
                    patient,
                    candidate);
                if (distance >= 0f && distance < bestDistance)
                {
                    bestDistance = distance;
                    result = candidate;
                }
            }

            return result;
        }

        private static string GetObjectId(TileObject target)
        {
            if (target == null ||
                target.m_state == null ||
                !target.m_state.m_gameDBObject.IsValid ||
                target.m_state.m_gameDBObject.Entry == null ||
                ID.IsNullOrNoID(target.m_state.m_gameDBObject.Entry.DatabaseID))
            {
                return "none";
            }

            return target.m_state.m_gameDBObject.Entry.DatabaseID.ToString();
        }
    }
}
