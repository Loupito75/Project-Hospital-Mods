using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    [HarmonyPatch]
    internal static class WallBookcasePlacementPreviewPatch
    {
        private const string WallBookcaseObjectId = "OBJECT_DLC_BOOKSHELF";

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
            ref PlacementValidationResult __result)
        {
            if (__result != PlacementValidationResult.ALLOWED ||
                __instance == null ||
                __instance.m_tileObjects == null ||
                !IsWallBookcaseType(objectType))
            {
                return;
            }

            Vector2i rotatedPosition = IsometricCameraUtils.GetRotatedPosition(
                position.m_x,
                position.m_y);

            if (rotatedPosition.m_x < 0 ||
                rotatedPosition.m_y < 0 ||
                rotatedPosition.m_x >= __instance.m_size.m_x ||
                rotatedPosition.m_y >= __instance.m_size.m_y)
            {
                return;
            }

            if (__instance.m_tileObjects[
                    rotatedPosition.m_x,
                    rotatedPosition.m_y].m_centerObject != null)
            {
                __result = PlacementValidationResult.ALLOWED_NOT_ACCESSIBLE;
            }
        }

        private static bool IsWallBookcaseType(GameDBObject objectType)
        {
            if (objectType == null ||
                ID.IsNullOrNoID(objectType.DatabaseID) ||
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
    }
}
