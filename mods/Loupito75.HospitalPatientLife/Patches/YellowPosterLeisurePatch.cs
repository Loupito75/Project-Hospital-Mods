using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class YellowPosterLeisureAccess
    {
        private static readonly HashSet<TileObject> PendingPatientPosters =
            new HashSet<TileObject>();

        internal static List<TileObject> FindEligiblePosters(
            Entity patient,
            Room room,
            string requiredTag)
        {
            List<TileObject> result = new List<TileObject>();

            if (patient == null ||
                room == null ||
                string.IsNullOrEmpty(requiredTag) ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return result;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return result;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null || floor.m_tileObjects == null)
            {
                return result;
            }

            int minimumX = Math.Max(0, room.m_roomPersistentData.m_positionBottom.m_x);
            int minimumY = Math.Max(0, room.m_roomPersistentData.m_positionBottom.m_y);
            int maximumX = Math.Min(
                floor.m_size.m_x - 1,
                room.m_roomPersistentData.m_positionTop.m_x);
            int maximumY = Math.Min(
                floor.m_size.m_y - 1,
                room.m_roomPersistentData.m_positionTop.m_y);

            for (int x = minimumX; x <= maximumX; x++)
            {
                for (int y = minimumY; y <= maximumY; y++)
                {
                    Vector2i position = new Vector2i(x, y);
                    if (!room.IsPositionInRoom(position))
                    {
                        continue;
                    }

                    TileObject centerObject =
                        floor.m_tileObjects[x, y].m_centerObject;
                    if (centerObject == null)
                    {
                        continue;
                    }

                    TryAddPoster(
                        result,
                        patient,
                        room,
                        floor,
                        floor.m_tileObjects[x, y].m_ObjectNW,
                        requiredTag);
                    TryAddPoster(
                        result,
                        patient,
                        room,
                        floor,
                        floor.m_tileObjects[x, y].m_ObjectNE,
                        requiredTag);
                    TryAddPoster(
                        result,
                        patient,
                        room,
                        floor,
                        floor.m_tileObjects[x, y].m_ObjectSE,
                        requiredTag);
                    TryAddPoster(
                        result,
                        patient,
                        room,
                        floor,
                        floor.m_tileObjects[x, y].m_ObjectSW,
                        requiredTag);
                }
            }

            return result;
        }

        private static void TryAddPoster(
            List<TileObject> result,
            Entity patient,
            Room room,
            Floor floor,
            TileObject poster,
            string requiredTag)
        {
            if (poster == null ||
                poster.User != null ||
                poster.m_state == null ||
                !poster.m_state.m_gameDBObject.IsValid ||
                poster.m_state.m_gameDBObject.Entry == null)
            {
                return;
            }

            GameDBObject objectType = poster.m_state.m_gameDBObject.Entry;
            if (!objectType.AttachedToWall ||
                !objectType.BlockedAboveOtherObjects ||
                !objectType.NonBlocking ||
                !poster.HasTag("ui_posters") ||
                !poster.HasTag(requiredTag))
            {
                return;
            }

            Vector2i frontPosition;
            if (!TryGetFrontPosition(poster, out frontPosition))
            {
                return;
            }

            if (MapScriptInterface.Instance.GetRoomAt(
                    frontPosition,
                    floor.m_floorIndex) != room ||
                floor.IsAnyBlockingObjectAt(frontPosition))
            {
                return;
            }

            if (IsUsePositionOccupied(
                    patient,
                    frontPosition,
                    floor.m_floorIndex))
            {
                if (HospitalizedPatientTrace.Enabled)
                {
                    HospitalizedPatientTrace.Log(
                        HospitalizedPatientTrace.GetName(patient) +
                        " | yellow-poster=REJECTED_OCCUPIED_FRONT" +
                        " | floor=" + floor.m_floorIndex +
                        " | position=" + poster.m_state.m_position.m_x +
                        "," + poster.m_state.m_position.m_y +
                        " | front=" + frontPosition.m_x +
                        "," + frontPosition.m_y);
                }
                return;
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            if (walk == null || walk.GetFloorIndex() != floor.m_floorIndex)
            {
                return;
            }

            float distance = GridMap.GetInstance().GetDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                floor.m_floorIndex,
                frontPosition,
                AccessRights.PATIENT);

            if (distance < 0f)
            {
                return;
            }

            result.Add(poster);
        }

        internal static bool IsUsePositionOccupied(
            Entity patient,
            Vector2i position,
            int floorIndex)
        {
            if (Hospital.Instance == null || Hospital.Instance.m_characters == null)
            {
                return false;
            }

            for (int i = 0; i < Hospital.Instance.m_characters.Count; i++)
            {
                Entity character = Hospital.Instance.m_characters[i];
                if (character == null || character == patient)
                {
                    continue;
                }

                WalkComponent walk = character.GetComponent<WalkComponent>();
                if (walk == null ||
                    walk.m_state == null ||
                    walk.GetFloorIndex() != floorIndex)
                {
                    continue;
                }

                if (walk.GetCurrentTile() == position)
                {
                    return true;
                }

                TileObject sittingOn = walk.m_state.m_objectSittingOn != null
                    ? walk.m_state.m_objectSittingOn.GetEntity()
                    : null;
                if (ObjectUsesPosition(sittingOn, position, floorIndex))
                {
                    return true;
                }

                TileObject goingToSitOn = walk.m_state.m_objectToSitOn != null
                    ? walk.m_state.m_objectToSitOn.GetEntity()
                    : null;
                if (ObjectUsesPosition(goingToSitOn, position, floorIndex))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ObjectUsesPosition(
            TileObject target,
            Vector2i position,
            int floorIndex)
        {
            if (target == null ||
                target.m_state == null ||
                target.GetFloorIndex() != floorIndex)
            {
                return false;
            }

            return target.m_state.m_position == position ||
                target.GetDefaultUseTile() == position;
        }

        internal static bool TryGetFrontPosition(
            TileObject poster,
            out Vector2i position)
        {
            position = Vector2i.ZERO_VECTOR;
            if (poster == null || poster.m_state == null)
            {
                return false;
            }

            position = TileObject.GetDefaultUsePositionForDirection(
                poster.m_state.m_position,
                poster.Orientation);
            return true;
        }

        internal static float GetRouteDistance(
            Entity patient,
            TileObject target)
        {
            if (patient == null || target == null)
            {
                return -1f;
            }

            if (WallBookcaseLeisureAccess.IsAllowedNonValidBookcase(target))
            {
                return WallBookcaseLeisureAccess.GetRouteDistance(
                    patient,
                    target);
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return -1f;
            }

            Vector2f usePosition = target.GetDefaultUsePosition();
            Vector2i destination = new Vector2i(
                (int)(usePosition.m_x + 0.5f),
                (int)(usePosition.m_y + 0.5f));
            if (IsYellowPoster(target))
            {
                TryGetFrontPosition(target, out destination);
            }

            return GridMap.GetInstance().GetDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                target.GetFloorIndex(),
                destination,
                AccessRights.PATIENT);
        }

        internal static bool IsYellowPoster(TileObject target)
        {
            if (target == null ||
                target.m_state == null ||
                !target.m_state.m_gameDBObject.IsValid ||
                target.m_state.m_gameDBObject.Entry == null)
            {
                return false;
            }

            GameDBObject objectType = target.m_state.m_gameDBObject.Entry;
            if (!objectType.AttachedToWall ||
                !objectType.BlockedAboveOtherObjects ||
                !objectType.NonBlocking ||
                !target.HasTag("ui_posters"))
            {
                return false;
            }

            int floorIndex = target.GetFloorIndex();
            if (Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Vector2i position = target.m_state.m_position;
            return floor != null &&
                floor.m_tileObjects != null &&
                floor.m_tileObjects[position.m_x, position.m_y].m_centerObject != null;
        }

        internal static void MarkForPatientUse(TileObject poster)
        {
            if (poster != null)
            {
                PendingPatientPosters.Add(poster);
            }
        }

        internal static bool ShouldUseFrontPosition(TileObject poster)
        {
            if (poster == null)
            {
                return false;
            }

            bool pending = PendingPatientPosters.Contains(poster);
            Entity user = poster.User;
            bool patientUsing =
                user != null && user.GetComponent<BehaviorPatient>() != null;

            if (!pending && !patientUsing)
            {
                return false;
            }

            if (!IsYellowPoster(poster))
            {
                PendingPatientPosters.Remove(poster);
                return false;
            }

            if (patientUsing)
            {
                PendingPatientPosters.Remove(poster);
            }

            return true;
        }

        internal static void ClearPending(TileObject poster)
        {
            if (poster != null)
            {
                PendingPatientPosters.Remove(poster);
            }
        }
    }

    [HarmonyPatch]
    internal static class YellowPosterLeisureSearchPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(MapScriptInterface),
                "FindClosestFreeObjectWithTags",
                new Type[]
                {
                    typeof(Entity),
                    typeof(Entity),
                    typeof(Vector2i),
                    typeof(Room),
                    typeof(string[]),
                    typeof(AccessRights),
                    typeof(bool),
                    typeof(DatabaseEntryRef<GameDBRoomType>[]),
                    typeof(bool)
                });
        }

        [HarmonyPostfix]
        private static void Postfix(
            Entity character,
            Room room,
            string[] tags,
            AccessRights accessRights,
            ref TileObject __result)
        {
            PatientLeisureConfig.EnsureLoaded();

            if (!PatientLeisureConfig.AllowYellowLeisureObjectInteraction ||
                character == null ||
                character.GetComponent<BehaviorPatient>() == null ||
                room == null ||
                tags == null ||
                accessRights != AccessRights.PATIENT)
            {
                return;
            }

            string requiredTag = null;
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == "hospitalized_patient")
                {
                    requiredTag = "hospitalized_patient";
                    break;
                }
                if (tags[i] == "distraction")
                {
                    requiredTag = "distraction";
                }
            }

            if (requiredTag == null)
            {
                return;
            }

            List<TileObject> posters =
                YellowPosterLeisureAccess.FindEligiblePosters(
                    character,
                    room,
                    requiredTag);
            if (posters.Count == 0)
            {
                return;
            }

            TileObject bestPoster = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < posters.Count; i++)
            {
                float distance = YellowPosterLeisureAccess.GetRouteDistance(
                    character,
                    posters[i]);
                if (distance >= 0f && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPoster = posters[i];
                }
            }

            if (bestPoster == null)
            {
                return;
            }

            float vanillaDistance = YellowPosterLeisureAccess.GetRouteDistance(
                character,
                __result);
            if (__result != null && vanillaDistance >= 0f && vanillaDistance <= bestDistance)
            {
                return;
            }

            __result = bestPoster;
            YellowPosterLeisureAccess.MarkForPatientUse(bestPoster);

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(character) +
                    " | yellow-poster=SELECTED" +
                    " | tag=" + requiredTag +
                    " | floor=" + bestPoster.GetFloorIndex() +
                    " | position=" + bestPoster.m_state.m_position.m_x +
                    "," + bestPoster.m_state.m_position.m_y +
                    " | frontDistance=" + bestDistance.ToString("0.0"));
            }
        }
    }

    [HarmonyPatch(typeof(TileObject), "GetDefaultUsePosition")]
    internal static class YellowPosterUsePositionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(TileObject __instance, ref Vector2f __result)
        {
            PatientLeisureConfig.EnsureLoaded();
            if (!PatientLeisureConfig.AllowYellowLeisureObjectInteraction ||
                !YellowPosterLeisureAccess.ShouldUseFrontPosition(__instance))
            {
                return;
            }

            Vector2i frontPosition;
            if (YellowPosterLeisureAccess.TryGetFrontPosition(
                    __instance,
                    out frontPosition))
            {
                __result = new Vector2f(frontPosition);
            }
        }
    }

    [HarmonyPatch(typeof(UseComponent), "Interrupt")]
    internal static class YellowPosterInterruptCleanupPatch
    {
        [HarmonyPrefix]
        private static void Prefix(UseComponent __instance)
        {
            if (__instance == null || __instance.m_state == null)
            {
                return;
            }

            TileObject reserved = __instance.m_state.m_reservedObject != null
                ? __instance.m_state.m_reservedObject.GetEntity()
                : null;
            TileObject current = __instance.m_state.m_object != null
                ? __instance.m_state.m_object.GetEntity()
                : null;

            YellowPosterLeisureAccess.ClearPending(reserved);
            YellowPosterLeisureAccess.ClearPending(current);
        }
    }

    [HarmonyPatch(typeof(UseComponent), "Deactivate")]
    internal static class YellowPosterDeactivateCleanupPatch
    {
        [HarmonyPrefix]
        private static void Prefix(UseComponent __instance)
        {
            if (__instance == null || __instance.m_state == null)
            {
                return;
            }

            TileObject current = __instance.m_state.m_object != null
                ? __instance.m_state.m_object.GetEntity()
                : null;
            YellowPosterLeisureAccess.ClearPending(current);
        }
    }
}
