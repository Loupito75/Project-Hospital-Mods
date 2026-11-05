using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
    [HarmonyPatch(typeof(ObjectRenderer), "RenderObjects")]
    internal static class MobileGhostPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Dictionary<TileObject, GameObject> ___m_ghostMovingObjects)
        {
            if (!FixConfig.FixMobileObjectGhosts || ___m_ghostMovingObjects == null ||
                ___m_ghostMovingObjects.Count == 0 ||
                ViewModeController.Instance == null)
            {
                return;
            }

            ViewModes mode = ViewModeController.Instance.m_currentMode;
            if (mode == ViewModes.BUILDING || mode == ViewModes.BUILDING_FLOORS)
            {
                return;
            }

            int destroyedGhosts = ___m_ghostMovingObjects.Count;

            // Destroy only renderer-owned ghosts. Never touch Floor.m_movingObjects.
            foreach (GameObject ghost in ___m_ghostMovingObjects.Values)
            {
                if (ghost != null)
                {
                    GameObjectManager.DestroyGameObject(ghost);
                }
            }
            ___m_ghostMovingObjects.Clear();

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.ResultRateLimited(
                    "FixMobileObjectGhosts",
                    "ObjectRenderer.RenderObjects",
                    "renderer-ghosts-destroyed",
                    "destroyedGhosts=" + destroyedGhosts,
                    "FixMobileObjectGhosts",
                    5000);
            }
        }
    }
}
