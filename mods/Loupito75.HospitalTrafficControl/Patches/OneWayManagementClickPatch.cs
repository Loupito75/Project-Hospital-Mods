using HarmonyLib;
using UnityEngine;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(
        typeof(MapEditorUIController),
        "UpdateMouseIdle",
        new System.Type[] { typeof(Vector2) })]
    internal static class OneWayManagementClickPatch
    {
        private static bool Prefix(
            MapEditorUIController __instance,
            Vector2 mouseCoords)
        {
            return !OneWayInputController.TryHandleMouseIdleClick(
                __instance,
                mouseCoords);
        }
    }
}
