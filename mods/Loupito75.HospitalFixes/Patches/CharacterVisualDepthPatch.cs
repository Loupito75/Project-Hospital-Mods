using System;
using System.Collections.Generic;
using System.Globalization;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
    internal static class CharacterVisualDepthRules
    {
        internal static bool TryGetActiveStandingUse(
            Entity character,
            out UseComponent use,
            out WalkComponent walk,
            out TileObject tileObject)
        {
            use = null;
            walk = null;
            tileObject = null;

            if (character == null)
            {
                return false;
            }

            use = character.GetComponent<UseComponent>();
            if (use == null ||
                use.m_state == null ||
                use.m_state.m_object == null ||
                !use.m_state.m_object.CheckEntity() ||
                (use.m_state.m_useComponentState !=
                    UseComponentState.STARTING &&
                 use.m_state.m_useComponentState !=
                    UseComponentState.USING &&
                 use.m_state.m_useComponentState !=
                    UseComponentState.FINISHING))
            {
                return false;
            }

            tileObject =
                use.m_state.m_object.GetEntity();
            if (tileObject == null ||
                tileObject.m_state == null ||
                tileObject.m_state.m_gameDBObject.Entry == null)
            {
                return false;
            }

            walk =
                character.GetComponent<WalkComponent>();
            if (walk == null ||
                walk.m_state == null)
            {
                return false;
            }

            // CharacterRenderer resolves these states through its
            // sitting/lying branch before active UseComponent depth.
            // Match that Vanilla branch boundary exactly.
            if (walk.m_state.m_objectSittingOn != null ||
                (walk.m_state.m_objectToSitOn != null &&
                 walk.m_state.m_walkState ==
                    WalkState.SittingDown) ||
                walk.m_state.m_lying)
            {
                return false;
            }

            return true;
        }

        internal static bool UsesDifferentTile(
            WalkComponent walk,
            TileObject tileObject)
        {
            Vector2i characterTile =
                walk.GetCurrentTile();
            Vector2i objectTile =
                tileObject.m_state.m_position;

            return characterTile.m_x != objectTile.m_x ||
                characterTile.m_y != objectTile.m_y;
        }

        internal static float GetNativeActiveUseDepthBias(
            TileObject tileObject)
        {
            Direction rotatedUseOrientation =
                IsometricCameraUtils.GetRotatedDirection(
                    tileObject.GetUseOrientation());

            return rotatedUseOrientation != Direction.NW &&
                rotatedUseOrientation != Direction.NE
                    ? 0.24f
                    : 0.26f;
        }

        internal static float GetVanillaActiveUseZ(
            TileObject tileObject)
        {
            Vector2f rotatedObjectPosition =
                IsometricCameraUtils.GetRotatedPositionInverse(
                    (float)tileObject.m_state.m_position.m_x,
                    (float)tileObject.m_state.m_position.m_y);

            float objectDepthAnchor =
                IsometricUtils.WorldToIsometric(
                    rotatedObjectPosition).z;

            return objectDepthAnchor -
                GetNativeActiveUseDepthBias(tileObject);
        }

        internal static float GetCorrectedActiveUseZ(
            WalkComponent walk,
            TileObject tileObject)
        {
            Vector2f rotatedCharacterPosition =
                IsometricCameraUtils.GetRotatedPositionInverse(
                    walk.m_state.m_worldPosition.x,
                    walk.m_state.m_worldPosition.y);

            // Keep Vanilla's local -0.24/-0.26 relationship,
            // but anchor it to the actual use position that
            // CharacterRenderer already uses for X/Y.
            float characterDepthAnchor =
                IsometricUtils.WorldToIsometric(
                    rotatedCharacterPosition).z;

            return characterDepthAnchor -
                GetNativeActiveUseDepthBias(tileObject);
        }
    }

    internal static class CharacterVisualDepthDiagnostics
    {
        internal static string GetObjectId(
            TileObject tileObject)
        {
            return tileObject != null &&
                tileObject.m_state != null &&
                tileObject.m_state
                    .m_gameDBObject.Entry != null &&
                tileObject.m_state
                    .m_gameDBObject.Entry
                    .DatabaseID != null
                    ? tileObject.m_state
                        .m_gameDBObject.Entry
                        .DatabaseID.ToString()
                    : "unknown";
        }

        internal static Vector3 GetNaturalCharacterPosition(
            WalkComponent walk,
            float yOffset)
        {
            Vector2f rotatedPosition =
                IsometricCameraUtils.GetRotatedPositionInverse(
                    walk.m_state.m_worldPosition.x,
                    walk.m_state.m_worldPosition.y);

            return IsometricUtils.WorldToIsometric(
                    new Vector3(
                        rotatedPosition.m_x,
                        rotatedPosition.m_y,
                        walk.m_state.m_worldPosition.z)) +
                new Vector3(0f, yOffset, 0f);
        }
    }

    [HarmonyPatch(
        typeof(CharacterRenderer),
        "RenderCharacter",
        new Type[]
        {
            typeof(Entity),
            typeof(FloorRenderData),
            typeof(Floor),
            typeof(LightMap2DUnity),
            typeof(bool),
            typeof(bool),
            typeof(float)
        })]
    internal static class CharacterActiveUseDepthPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Entity character,
            Dictionary<Entity, RendererObject>
                ___m_characterRendererObjects)
        {
            if (!FixConfig.FixCharacterVisualDepth ||
                character == null)
            {
                return;
            }

            UseComponent use;
            WalkComponent walk;
            TileObject tileObject;
            if (!CharacterVisualDepthRules
                    .TryGetActiveStandingUse(
                        character,
                        out use,
                        out walk,
                        out tileObject) ||
                !CharacterVisualDepthRules
                    .UsesDifferentTile(
                        walk,
                        tileObject))
            {
                return;
            }

            Vector2i characterTile =
                walk.GetCurrentTile();
            Vector2i objectTile =
                tileObject.m_state.m_position;

            RendererObject rendererObject = null;
            if (___m_characterRendererObjects == null ||
                !___m_characterRendererObjects.TryGetValue(
                    character,
                    out rendererObject) ||
                rendererObject == null ||
                rendererObject.m_gameObject == null)
            {
                return;
            }

            Vector3 correctedPosition =
                rendererObject.m_gameObject
                    .transform.localPosition;
            float vanillaZ =
                correctedPosition.z;
            float correctedZ =
                CharacterVisualDepthRules
                    .GetCorrectedActiveUseZ(
                        walk,
                        tileObject);

            correctedPosition.z = correctedZ;
            rendererObject.m_gameObject
                .transform.localPosition =
                correctedPosition;

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.ResultRateLimited(
                    "FixCharacterVisualDepth",
                    "CharacterRenderer.RenderCharacter",
                    "active-use-cross-tile-depth-corrected",
                    "character={" +
                    HfxDiagnostics.EntityContext(
                        character) +
                    "} object={" +
                    HfxDiagnostics.ObjectContext(
                        tileObject) +
                    "} characterTile=" +
                    TileLabel(characterTile) +
                    " objectTile=" +
                    TileLabel(objectTile) +
                    " useState=" +
                    use.m_state.m_useComponentState +
                    " vanillaZ=" +
                    FormatFloat(vanillaZ) +
                    " correctedZ=" +
                    FormatFloat(correctedZ) +
                    " correctionDelta=" +
                    FormatFloat(
                        correctedZ - vanillaZ) +
                    " nativeBias=" +
                    FormatFloat(
                        CharacterVisualDepthRules
                            .GetNativeActiveUseDepthBias(
                                tileObject)),
                    "FixCharacterVisualDepth|active-use-cross-tile|" +
                    CharacterVisualDepthDiagnostics
                        .GetObjectId(tileObject) +
                    "|" +
                    HfxDiagnostics.EntityLabel(
                        character),
                    5000);
            }
        }

        private static string TileLabel(
            Vector2i value)
        {
            return value.m_x.ToString(
                    CultureInfo.InvariantCulture) +
                "," +
                value.m_y.ToString(
                    CultureInfo.InvariantCulture);
        }

        private static string FormatFloat(
            float value)
        {
            return value.ToString(
                "0.###",
                CultureInfo.InvariantCulture);
        }
    }
}
