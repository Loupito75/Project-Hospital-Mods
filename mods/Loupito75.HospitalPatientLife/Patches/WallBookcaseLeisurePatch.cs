using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class WallBookcaseLeisureAccess
    {
        private const string WallBookcaseObjectId = "OBJECT_DLC_BOOKSHELF";

        internal static bool IsWallBookcase(TileObject target)
        {
            if (target == null ||
                target.m_state == null ||
                !target.m_state.m_gameDBObject.IsValid ||
                target.m_state.m_gameDBObject.Entry == null)
            {
                return false;
            }

            GameDBObject objectType = target.m_state.m_gameDBObject.Entry;
            if (ID.IsNullOrNoID(objectType.DatabaseID) ||
                objectType.DatabaseID.ToString() != WallBookcaseObjectId)
            {
                return false;
            }

            return objectType.PlacedToEdge &&
                objectType.AttachedToWall &&
                !objectType.NotInteractable &&
                objectType.AccessPositions != null &&
                objectType.AccessPositions.Length > 0 &&
                objectType.AccessPositionsHaveToBeFree &&
                target.HasTag("education") &&
                target.HasTag("ui_bookcase") &&
                target.HasTag("hospitalized_patient") &&
                target.HasTag("bookshelf");
        }

        internal static bool ShouldUseYellowPlacement(
            Floor floor,
            TileObject target)
        {
            if (!IsWallBookcase(target) ||
                floor == null ||
                floor.m_tileObjects == null ||
                target.GetFloorIndex() != floor.m_floorIndex)
            {
                return false;
            }

            Vector2i objectPosition = target.m_state.m_position;
            if (!IsInBounds(floor, objectPosition))
            {
                return false;
            }

            return floor.m_tileObjects[
                objectPosition.m_x,
                objectPosition.m_y].m_centerObject != null;
        }

        internal static bool IsAllowedNonValidBookcase(TileObject target)
        {
            if (!IsWallBookcase(target) ||
                target.m_state.m_placementValidity !=
                    PlacementValidationResult.ALLOWED_NOT_ACCESSIBLE)
            {
                return false;
            }

            Floor floor = GetFloor(target.GetFloorIndex());
            return ShouldUseYellowPlacement(floor, target);
        }

        internal static bool IsValidForHplCandidate(TileObject target)
        {
            if (target == null)
            {
                return false;
            }

            if (target.IsValid())
            {
                return true;
            }

            PatientLeisureConfig.EnsureLoaded();
            if (!PatientLeisureConfig.AllowYellowLeisureObjectInteraction ||
                !IsAllowedNonValidBookcase(target) ||
                target.IsBroken())
            {
                return false;
            }

            Floor floor = GetFloor(target.GetFloorIndex());
            if (floor == null)
            {
                return false;
            }

            Vector2i usePosition = target.GetDefaultUseTile();
            if (!IsPositionAllowedForPatient(floor, usePosition) ||
                floor.IsAnyBlockingObjectAt(usePosition) ||
                YellowPosterLeisureAccess.IsUsePositionOccupied(
                    null,
                    usePosition,
                    target.GetFloorIndex()))
            {
                return false;
            }

            return true;
        }

        internal static List<TileObject> FindEligibleBookcases(
            Entity patient,
            Room room,
            string[] requiredTags)
        {
            List<TileObject> result = new List<TileObject>();

            if (room == null ||
                requiredTags == null ||
                requiredTags.Length == 0 ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return result;
            }

            int floorIndex = room.GetFloorIndex();
            Floor floor = GetFloor(floorIndex);
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
                    if (!room.IsPositionInRoom(position) ||
                        floor.m_tileObjects[x, y].m_centerObject == null)
                    {
                        continue;
                    }

                    TryAddBookcase(
                        result,
                        patient,
                        room,
                        floor,
                        floor.m_tileObjects[x, y].m_ObjectNW,
                        requiredTags);
                    TryAddBookcase(
                        result,
                        patient,
                        room,
                        floor,
                        floor.m_tileObjects[x, y].m_ObjectNE,
                        requiredTags);
                    TryAddBookcase(
                        result,
                        patient,
                        room,
                        floor,
                        floor.m_tileObjects[x, y].m_ObjectSE,
                        requiredTags);
                    TryAddBookcase(
                        result,
                        patient,
                        room,
                        floor,
                        floor.m_tileObjects[x, y].m_ObjectSW,
                        requiredTags);
                }
            }

            return result;
        }

        private static void TryAddBookcase(
            List<TileObject> result,
            Entity patient,
            Room room,
            Floor floor,
            TileObject bookcase,
            string[] requiredTags)
        {
            if (bookcase == null ||
                !IsAllowedNonValidBookcase(bookcase) ||
                bookcase.IsBroken() ||
                !bookcase.HasAllTags(requiredTags) ||
                (bookcase.User != null && bookcase.User != patient))
            {
                return;
            }

            Vector2i objectPosition = bookcase.m_state.m_position;
            Vector2i usePosition = bookcase.GetDefaultUseTile();

            if (!IsPositionAllowedForPatient(floor, objectPosition) ||
                !IsPositionAllowedForPatient(floor, usePosition) ||
                MapScriptInterface.Instance.GetRoomAt(
                    usePosition,
                    floor.m_floorIndex) != room ||
                floor.IsAnyBlockingObjectAt(usePosition))
            {
                return;
            }

            if (patient != null)
            {
                if (YellowPosterLeisureAccess.IsUsePositionOccupied(
                        patient,
                        usePosition,
                        floor.m_floorIndex))
                {
                    return;
                }

                WalkComponent walk = patient.GetComponent<WalkComponent>();
                GridMap gridMap = GridMap.GetInstance();
                if (walk == null ||
                    gridMap == null ||
                    gridMap.GetDistance(
                        walk.GetFloorIndex(),
                        walk.GetCurrentTile(),
                        floor.m_floorIndex,
                        usePosition,
                        AccessRights.PATIENT) < 0f)
                {
                    return;
                }
            }

            if (!result.Contains(bookcase))
            {
                result.Add(bookcase);
            }
        }

        internal static float GetRouteDistance(Entity patient, TileObject target)
        {
            if (patient == null ||
                !IsWallBookcase(target) ||
                !IsAllowedNonValidBookcase(target))
            {
                return -1f;
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            GridMap gridMap = GridMap.GetInstance();
            Floor floor = GetFloor(target.GetFloorIndex());
            if (walk == null || gridMap == null || floor == null)
            {
                return -1f;
            }

            Vector2i usePosition = target.GetDefaultUseTile();
            if (floor.IsAnyBlockingObjectAt(usePosition) ||
                YellowPosterLeisureAccess.IsUsePositionOccupied(
                    patient,
                    usePosition,
                    target.GetFloorIndex()))
            {
                return -1f;
            }

            return gridMap.GetDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                target.GetFloorIndex(),
                usePosition,
                AccessRights.PATIENT);
        }

        private static bool IsPositionAllowedForPatient(
            Floor floor,
            Vector2i position)
        {
            if (!IsInBounds(floor, position) ||
                floor.m_roomAccessRights == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights == null)
            {
                return false;
            }

            AccessRights roomAccess =
                floor.m_roomAccessRights[position.m_x, position.m_y];
            AccessRights logisticsAccess =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    position.m_x,
                    position.m_y];

            return (int)roomAccess <= (int)AccessRights.PATIENT &&
                (int)logisticsAccess <= (int)AccessRights.PATIENT;
        }

        private static bool IsInBounds(Floor floor, Vector2i position)
        {
            return floor != null &&
                position.m_x >= 0 &&
                position.m_y >= 0 &&
                position.m_x < floor.m_size.m_x &&
                position.m_y < floor.m_size.m_y;
        }

        private static Floor GetFloor(int floorIndex)
        {
            if (Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            return Hospital.Instance.m_floors[floorIndex];
        }
    }

    [HarmonyPatch]
    internal static class WallBookcasePlacementValidationPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(Floor),
                "ValidateObjectsInARoom",
                new Type[]
                {
                    typeof(Room),
                    typeof(List<TileObject>)
                });

            if (method == null)
            {
                throw new MissingMethodException(
                    "Floor.ValidateObjectsInARoom(Room, List<TileObject>) was not found.");
            }

            return method;
        }

        [HarmonyPostfix]
        private static void Postfix(
            Floor __instance,
            List<TileObject> allObjects)
        {
            if (__instance == null || allObjects == null)
            {
                return;
            }

            for (int i = 0; i < allObjects.Count; i++)
            {
                TileObject bookcase = allObjects[i];
                if (bookcase == null ||
                    bookcase.m_state.m_placementValidity != PlacementValidationResult.ALLOWED ||
                    !WallBookcaseLeisureAccess.ShouldUseYellowPlacement(
                        __instance,
                        bookcase))
                {
                    continue;
                }

                bookcase.m_state.m_placementValidity =
                    PlacementValidationResult.ALLOWED_NOT_ACCESSIBLE;
            }
        }
    }

    [HarmonyPatch]
    internal static class WallBookcaseFindAllPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(MapScriptInterface),
                "FindAllObjectWithTags",
                new Type[]
                {
                    typeof(Room),
                    typeof(string[]),
                    typeof(AccessRights),
                    typeof(bool)
                });
        }

        [HarmonyPostfix]
        private static void Postfix(
            Room room,
            string[] tags,
            AccessRights accessRights,
            ref List<TileObject> __result)
        {
            PatientLeisureConfig.EnsureLoaded();
            if (!PatientLeisureConfig.AllowYellowLeisureObjectInteraction ||
                room == null ||
                tags == null ||
                accessRights != AccessRights.PATIENT ||
                !ContainsTag(tags, "hospitalized_patient"))
            {
                return;
            }

            if (__result == null)
            {
                __result = new List<TileObject>();
            }

            List<TileObject> bookcases =
                WallBookcaseLeisureAccess.FindEligibleBookcases(
                    null,
                    room,
                    tags);

            for (int i = 0; i < bookcases.Count; i++)
            {
                if (!__result.Contains(bookcases[i]))
                {
                    __result.Add(bookcases[i]);
                }
            }
        }

        private static bool ContainsTag(string[] tags, string requiredTag)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == requiredTag)
                {
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch]
    internal static class WallBookcaseClosestSearchPatch
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
                room == null ||
                tags == null ||
                accessRights != AccessRights.PATIENT)
            {
                return;
            }

            HospitalizationComponent hospitalization =
                character.GetComponent<HospitalizationComponent>();
            if (hospitalization == null || !hospitalization.IsHospitalized())
            {
                return;
            }

            if (!ContainsTag(tags, "hospitalized_patient") &&
                !ContainsTag(tags, "education"))
            {
                return;
            }

            List<TileObject> bookcases =
                WallBookcaseLeisureAccess.FindEligibleBookcases(
                    character,
                    room,
                    tags);
            if (bookcases.Count == 0)
            {
                return;
            }

            TileObject bestBookcase = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < bookcases.Count; i++)
            {
                float distance = WallBookcaseLeisureAccess.GetRouteDistance(
                    character,
                    bookcases[i]);
                if (distance >= 0f && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestBookcase = bookcases[i];
                }
            }

            if (bestBookcase == null)
            {
                return;
            }

            float vanillaDistance = GetNativeRouteDistance(character, __result);
            if (__result != null &&
                vanillaDistance >= 0f &&
                vanillaDistance <= bestDistance)
            {
                return;
            }

            __result = bestBookcase;

            if (HospitalizedPatientTrace.Enabled)
            {
                Vector2i usePosition = bestBookcase.GetDefaultUseTile();
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(character) +
                    " | wall-bookcase=SELECTED" +
                    " | floor=" + bestBookcase.GetFloorIndex() +
                    " | position=" + bestBookcase.m_state.m_position.m_x +
                    "," + bestBookcase.m_state.m_position.m_y +
                    " | usePosition=" + usePosition.m_x +
                    "," + usePosition.m_y +
                    " | routeDistance=" + bestDistance.ToString("0.0"));
            }
        }

        private static float GetNativeRouteDistance(
            Entity patient,
            TileObject target)
        {
            if (patient == null || target == null)
            {
                return -1f;
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            GridMap gridMap = GridMap.GetInstance();
            if (walk == null || gridMap == null)
            {
                return -1f;
            }

            Vector2i destination = target.GetDefaultUseTile();
            if (YellowPosterLeisureAccess.IsYellowPoster(target))
            {
                YellowPosterLeisureAccess.TryGetFrontPosition(
                    target,
                    out destination);
            }

            return gridMap.GetDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                target.GetFloorIndex(),
                destination,
                AccessRights.PATIENT);
        }

        private static bool ContainsTag(string[] tags, string requiredTag)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == requiredTag)
                {
                    return true;
                }
            }

            return false;
        }
    }

}
