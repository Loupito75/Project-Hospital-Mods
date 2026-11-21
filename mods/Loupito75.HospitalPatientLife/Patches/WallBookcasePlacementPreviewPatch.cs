using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalPatientLife.Patches
{
    internal static class YellowLeisurePlacementUiRules
    {
        private const string WallBookcaseObjectId = "OBJECT_DLC_BOOKSHELF";

        internal static PlacementValidationResult GetPresentedPlacementResult(
            Floor floor,
            GameDBObject objectType,
            Vector2i position,
            Direction orientation,
            PlacementValidationResult result)
        {
            if (floor == null || objectType == null)
            {
                return result;
            }

            PatientLeisureConfig.EnsureLoaded();

            Vector2i rotatedPosition = IsometricCameraUtils.GetRotatedPosition(
                position.m_x,
                position.m_y);

            if (result == PlacementValidationResult.ALLOWED &&
                IsSupportedBookcaseType(objectType) &&
                IsInBounds(floor, rotatedPosition) &&
                floor.m_tileObjects != null &&
                floor.m_tileObjects[
                    rotatedPosition.m_x,
                    rotatedPosition.m_y].m_centerObject != null)
            {
                return PatientLeisureConfig.AllowYellowLeisureObjectInteraction
                    ? PlacementValidationResult.ALLOWED
                    : PlacementValidationResult.ALLOWED_NOT_ACCESSIBLE;
            }

            return ShouldPresentPlacementAsAllowed(
                floor,
                objectType,
                position,
                orientation,
                result)
                    ? PlacementValidationResult.ALLOWED
                    : result;
        }

        internal static bool ShouldPresentPlacementAsAllowed(
            Floor floor,
            GameDBObject objectType,
            Vector2i position,
            Direction orientation,
            PlacementValidationResult result)
        {
            PatientLeisureConfig.EnsureLoaded();
            if (!PatientLeisureConfig.AllowYellowLeisureObjectInteraction ||
                result != PlacementValidationResult.ALLOWED_NOT_ACCESSIBLE ||
                floor == null ||
                objectType == null ||
                !IsSupportedPosterType(objectType))
            {
                return false;
            }

            Vector2i rotatedPosition = IsometricCameraUtils.GetRotatedPosition(
                position.m_x,
                position.m_y);
            if (!IsInBounds(floor, rotatedPosition))
            {
                return false;
            }

            Direction direction =
                IsometricCameraUtils.GetRotatedDirectionInverse(orientation);
            Direction forcedOrientation =
                floor.GetForcedOrientation(position, objectType);
            if (forcedOrientation != Direction.None)
            {
                direction = forcedOrientation;
            }

            Vector2i frontPosition =
                TileObject.GetDefaultUsePositionForDirection(
                    rotatedPosition,
                    direction);
            if (!IsInBounds(floor, frontPosition) ||
                floor.IsAnyBlockingObjectAt(frontPosition) ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            Room objectRoom = MapScriptInterface.Instance.GetRoomAt(
                rotatedPosition,
                floor.m_floorIndex);
            return objectRoom != null &&
                MapScriptInterface.Instance.GetRoomAt(
                    frontPosition,
                    floor.m_floorIndex) == objectRoom;
        }

        internal static bool ShouldRenderAsAllowed(TileObject target)
        {
            PatientLeisureConfig.EnsureLoaded();
            if (!PatientLeisureConfig.AllowYellowLeisureObjectInteraction ||
                target == null ||
                target.m_state == null ||
                target.m_state.m_placementValidity !=
                    PlacementValidationResult.ALLOWED_NOT_ACCESSIBLE)
            {
                return false;
            }

            if (WallBookcaseLeisureAccess.IsAllowedNonValidBookcase(target))
            {
                return true;
            }

            if (!YellowPosterLeisureAccess.IsYellowPoster(target) ||
                (!target.HasTag("hospitalized_patient") &&
                 !target.HasTag("distraction")))
            {
                return false;
            }

            Floor floor = GetFloor(target.GetFloorIndex());
            Vector2i frontPosition;
            if (floor == null ||
                !YellowPosterLeisureAccess.TryGetFrontPosition(
                    target,
                    out frontPosition) ||
                !IsInBounds(floor, frontPosition) ||
                floor.IsAnyBlockingObjectAt(frontPosition) ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            Room objectRoom = MapScriptInterface.Instance.GetRoomAt(
                target.m_state.m_position,
                target.GetFloorIndex());
            return objectRoom != null &&
                MapScriptInterface.Instance.GetRoomAt(
                    frontPosition,
                    target.GetFloorIndex()) == objectRoom;
        }

        private static bool IsSupportedBookcaseType(GameDBObject objectType)
        {
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
                objectType.AccessPositionsHaveToBeFree;
        }

        private static bool IsSupportedPosterType(GameDBObject objectType)
        {
            return objectType.AttachedToWall &&
                objectType.BlockedAboveOtherObjects &&
                objectType.NonBlocking &&
                objectType.HasTag("ui_posters") &&
                (objectType.HasTag("hospitalized_patient") ||
                 objectType.HasTag("distraction"));
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

        private static bool IsInBounds(Floor floor, Vector2i position)
        {
            return floor != null &&
                position.m_x >= 0 &&
                position.m_y >= 0 &&
                position.m_x < floor.m_size.m_x &&
                position.m_y < floor.m_size.m_y;
        }

        internal static void TemporarilyPresentAsAllowed(
            TileObject tileObject,
            out PlacementValidationResult state)
        {
            state = tileObject == null || tileObject.m_state == null
                ? PlacementValidationResult.ALLOWED
                : tileObject.m_state.m_placementValidity;

            if (ShouldRenderAsAllowed(tileObject))
            {
                tileObject.m_state.m_placementValidity =
                    PlacementValidationResult.ALLOWED;
            }
        }

        internal static void RestorePlacementValidity(
            TileObject tileObject,
            PlacementValidationResult state)
        {
            if (tileObject != null && tileObject.m_state != null)
            {
                tileObject.m_state.m_placementValidity = state;
            }
        }
    }

    [HarmonyPatch]
    internal static class YellowLeisurePlacementPreviewPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(Floor),
                "CanPlaceObject",
                new Type[]
                {
                    typeof(GameDBObject),
                    typeof(Vector2i),
                    typeof(Direction),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool)
                });

            if (method == null)
            {
                throw new MissingMethodException(
                    "Floor.CanPlaceObject(GameDBObject, Vector2i, Direction, bool, bool, bool) was not found.");
            }

            return method;
        }

        [HarmonyPostfix]
        private static void Postfix(
            Floor __instance,
            GameDBObject objectType,
            Vector2i position,
            Direction orientation,
            ref PlacementValidationResult __result)
        {
            __result = YellowLeisurePlacementUiRules.GetPresentedPlacementResult(
                __instance,
                objectType,
                position,
                orientation,
                __result);
        }
    }

    [HarmonyPatch]
    internal static class YellowLeisureCreateSingleObjectRenderPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(ObjectRenderer),
                "CreateSingleObject",
                new Type[]
                {
                    typeof(TileObject),
                    typeof(GameDBObject),
                    typeof(Vector2f),
                    typeof(int),
                    typeof(float),
                    typeof(float),
                    typeof(Vector2f),
                    typeof(float),
                    typeof(bool),
                    typeof(Floor),
                    typeof(LightMap2DUnity),
                    typeof(Color),
                    typeof(ObjectCut),
                    typeof(PlacementValidationResult),
                    typeof(bool),
                    typeof(bool)
                });

            if (method == null)
            {
                throw new MissingMethodException(
                    "ObjectRenderer.CreateSingleObject(...) was not found.");
            }

            return method;
        }

        [HarmonyPrefix]
        private static void Prefix(
            TileObject tileObject,
            out PlacementValidationResult __state)
        {
            YellowLeisurePlacementUiRules.TemporarilyPresentAsAllowed(
                tileObject,
                out __state);
        }

        [HarmonyPostfix]
        private static void Postfix(
            TileObject tileObject,
            PlacementValidationResult __state)
        {
            YellowLeisurePlacementUiRules.RestorePlacementValidity(
                tileObject,
                __state);
        }
    }

    [HarmonyPatch]
    internal static class YellowLeisureAppendSingleObjectRenderPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(ObjectRenderer),
                "AppendSingleObject",
                new Type[]
                {
                    typeof(RendererObject),
                    typeof(int),
                    typeof(TileObject),
                    typeof(GameDBObject),
                    typeof(Vector2f),
                    typeof(int),
                    typeof(float),
                    typeof(float),
                    typeof(Vector2f),
                    typeof(float),
                    typeof(bool),
                    typeof(Floor),
                    typeof(LightMap2DUnity)
                });

            if (method == null)
            {
                throw new MissingMethodException(
                    "ObjectRenderer.AppendSingleObject(...) was not found.");
            }

            return method;
        }

        [HarmonyPrefix]
        private static void Prefix(
            TileObject tileObject,
            out PlacementValidationResult __state)
        {
            YellowLeisurePlacementUiRules.TemporarilyPresentAsAllowed(
                tileObject,
                out __state);
        }

        [HarmonyPostfix]
        private static void Postfix(
            TileObject tileObject,
            PlacementValidationResult __state)
        {
            YellowLeisurePlacementUiRules.RestorePlacementValidity(
                tileObject,
                __state);
        }
    }
}
