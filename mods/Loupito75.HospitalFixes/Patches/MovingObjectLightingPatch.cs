using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
    [HarmonyPatch(typeof(ObjectRenderer), "CreateSingleObject")]
    internal static class MovingObjectLightingPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            TileObject tileObject,
            Floor floor,
            bool ghost,
            GameObject __result)
        {
            if (!FixConfig.FixMovingObjectLighting ||
                ghost ||
                tileObject == null ||
                tileObject.m_state == null ||
                !tileObject.m_state.m_moving ||
                floor == null ||
                __result == null ||
                floor.Size.m_x == 0 ||
                floor.Size.m_y == 0)
            {
                return;
            }

            // Vanilla already samples the live first-part move position for
            // moving composite objects (for example stretchers). Leave that
            // native path untouched and apply the same principle only to
            // ordinary moving TileObjects such as carts and wheelchairs.
            if (tileObject.m_state.m_compositeParent != null &&
                tileObject.m_state.m_compositeParent.GetEntity() != null)
            {
                return;
            }

            MeshFilter filter =
                __result.GetComponent<MeshFilter>();
            if (filter == null ||
                filter.sharedMesh == null)
            {
                return;
            }

            Mesh mesh = filter.sharedMesh;
            Vector2[] uv = mesh.uv2;
            if (uv == null ||
                uv.Length != 4)
            {
                return;
            }

            Vector2f movePosition =
                tileObject.m_state.m_movePosition;
            Vector2 lightUv =
                new Vector2(
                    (0.5f + movePosition.m_x) /
                        (float)floor.Size.m_x,
                    (0.5f + movePosition.m_y) /
                        (float)floor.Size.m_y);

            for (int i = 0; i < uv.Length; i++)
            {
                uv[i] = lightUv;
            }

            mesh.uv2 = uv;

            if (HfxDiagnostics.Enabled)
            {
                string objectId =
                    tileObject.m_state.m_gameDBObject.Entry == null ||
                    tileObject.m_state.m_gameDBObject.Entry.DatabaseID == null
                        ? "unknown"
                        : tileObject.m_state.m_gameDBObject.Entry.DatabaseID.ToString();

                HfxDiagnostics.ResultRateLimited(
                    "FixMovingObjectLighting",
                    "ObjectRenderer.CreateSingleObject",
                    "moving-light-uv-updated",
                    "object=" + objectId +
                    " floor=" + tileObject.GetFloorIndex() +
                    " move=" + movePosition.m_x + "," + movePosition.m_y,
                    "FixMovingObjectLighting|" + objectId,
                    5000);
            }
        }
    }
}
