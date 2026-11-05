using System;
using System.Collections.Generic;
using System.Globalization;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
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
    internal static class CharacterVisualDepthDiagnosticsPatch
    {
        private const float SuspiciousUseOffset = 0.50f;
        private const long SampleIntervalTicks =
            TimeSpan.TicksPerSecond * 5L;

        private static readonly Dictionary<string, long>
            sm_lastSampleTicks =
                new Dictionary<string, long>(
                    StringComparer.Ordinal);

        [HarmonyPostfix]
        private static void Postfix(
            Entity character,
            Floor floor,
            float yOffset,
            Dictionary<Entity, RendererObject>
                ___m_characterRendererObjects)
        {
            if (!HfxDiagnostics.Enabled ||
                character == null ||
                floor == null)
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
                        out tileObject))
            {
                return;
            }

            bool crossTile =
                CharacterVisualDepthRules
                    .UsesDifferentTile(
                        walk,
                        tileObject);

            string renderMode =
                GetObjectRenderMode(tileObject);
            string cameraRotation =
                Hospital.Instance == null ||
                Hospital.Instance.m_state == null
                    ? "unknown"
                    : Hospital.Instance.m_state
                        .m_rotation.ToString();

            string sampleKey =
                CharacterVisualDepthDiagnostics
                    .GetObjectId(tileObject) +
                "|" +
                tileObject.m_state.m_position.m_x
                    .ToString(
                        CultureInfo.InvariantCulture) +
                "," +
                tileObject.m_state.m_position.m_y
                    .ToString(
                        CultureInfo.InvariantCulture) +
                "|" +
                cameraRotation +
                "|" +
                use.m_state.m_useComponentState +
                "|" +
                renderMode;

            if (!ShouldSample(sampleKey))
            {
                return;
            }

            float vanillaZ =
                CharacterVisualDepthRules
                    .GetVanillaActiveUseZ(
                        tileObject);
            float correctedCandidateZ =
                CharacterVisualDepthRules
                    .GetCorrectedActiveUseZ(
                        walk,
                        tileObject);
            Vector3 naturalPosition =
                CharacterVisualDepthDiagnostics
                    .GetNaturalCharacterPosition(
                        walk,
                        yOffset);

            float depthDelta =
                vanillaZ -
                naturalPosition.z;
            float correctionDelta =
                correctedCandidateZ -
                vanillaZ;

            Vector2f usePosition =
                tileObject.GetDefaultUsePosition();
            float useOffsetX =
                usePosition.m_x -
                tileObject.m_state.m_position.m_x;
            float useOffsetY =
                usePosition.m_y -
                tileObject.m_state.m_position.m_y;

            bool suspicious =
                crossTile ||
                Math.Abs(useOffsetX) +
                    Math.Abs(useOffsetY) >=
                    SuspiciousUseOffset;

            string rendererZAtPostfix = "unavailable";
            RendererObject rendererObject = null;
            if (___m_characterRendererObjects != null &&
                ___m_characterRendererObjects.TryGetValue(
                    character,
                    out rendererObject) &&
                rendererObject != null &&
                rendererObject.m_gameObject != null)
            {
                rendererZAtPostfix =
                    FormatFloat(
                        rendererObject.m_gameObject
                            .transform.localPosition.z);
            }

            AnimModelComponent animation =
                character.GetComponent<AnimModelComponent>();
            string animationId =
                animation == null ||
                animation.m_state == null ||
                string.IsNullOrEmpty(
                    animation.m_state.m_currentAnimationID)
                    ? "none"
                    : animation.m_state
                        .m_currentAnimationID;

            Vector2i characterTile =
                walk.GetCurrentTile();

            string characterWalls =
                suspicious
                    ? GetWallNeighborhood(
                        floor,
                        characterTile)
                    : "not-sampled";
            string objectWalls =
                suspicious
                    ? GetWallNeighborhood(
                        floor,
                        tileObject.m_state.m_position)
                    : "not-sampled";

            HfxDiagnostics.Observed(
                "FixCharacterVisualDepth",
                "CharacterRenderer.RenderCharacter",
                crossTile
                    ? "active-use-cross-tile-depth-snapshot"
                    : (suspicious
                        ? "active-use-depth-suspect"
                        : "active-use-depth-snapshot"),
                "character={" +
                HfxDiagnostics.EntityContext(
                    character) +
                "} object={" +
                HfxDiagnostics.ObjectContext(
                    tileObject) +
                "} characterWorld=" +
                Vector3Label(
                    walk.m_state.m_worldPosition) +
                " characterTile=" +
                Vector2iLabel(characterTile) +
                " objectTile=" +
                Vector2iLabel(
                    tileObject.m_state.m_position) +
                " objectOrientation=" +
                tileObject.Orientation +
                " crossTile=" +
                crossTile +
                " usePosition=" +
                Vector2fLabel(usePosition) +
                " useOffset=" +
                Vector2fLabel(
                    new Vector2f(
                        useOffsetX,
                        useOffsetY)) +
                " useOrientation=" +
                tileObject.GetUseOrientation() +
                " useState=" +
                use.m_state.m_useComponentState +
                " characterAnimation=" +
                animationId +
                " cameraRotation=" +
                cameraRotation +
                " renderMode=" +
                renderMode +
                " objectAnimation={" +
                GetAnimatedObjectContext(
                    tileObject) +
                "} vanillaZ=" +
                FormatFloat(
                    vanillaZ) +
                " correctedCandidateZ=" +
                FormatFloat(
                    correctedCandidateZ) +
                " correctionDelta=" +
                FormatFloat(
                    correctionDelta) +
                " nativeBias=" +
                FormatFloat(
                    CharacterVisualDepthRules
                        .GetNativeActiveUseDepthBias(
                            tileObject)) +
                " naturalZ=" +
                FormatFloat(
                    naturalPosition.z) +
                " depthDelta=" +
                FormatFloat(depthDelta) +
                " rendererZAtPostfix=" +
                rendererZAtPostfix +
                " directionalObjectZ=" +
                FormatFloat(
                    tileObject
                        .GetDirectionalZOffset()) +
                " offsetZFront=" +
                FormatFloat(
                    tileObject.m_state
                        .m_gameDBObject.Entry
                        .OffsetZFront) +
                " alwaysFront=" +
                tileObject.m_state
                    .m_gameDBObject.Entry
                    .AlwaysFront +
                " placedToEdge=" +
                tileObject.m_state
                    .m_gameDBObject.Entry
                    .PlacedToEdge +
                " attachedToObject=" +
                tileObject.m_state
                    .m_gameDBObject.Entry
                    .AttachedToObject +
                " sliding=" +
                tileObject.m_state
                    .m_gameDBObject.Entry
                    .Sliding +
                " moving=" +
                tileObject.m_state.m_moving +
                " characterWalls=" +
                characterWalls +
                " objectWalls=" +
                objectWalls);
        }

        private static bool ShouldSample(
            string key)
        {
            long now =
                DateTime.UtcNow.Ticks;
            long previous;

            if (sm_lastSampleTicks.TryGetValue(
                    key,
                    out previous) &&
                now - previous <
                    SampleIntervalTicks)
            {
                return false;
            }

            sm_lastSampleTicks[key] = now;
            return true;
        }

        private static string GetObjectRenderMode(
            TileObject tileObject)
        {
            GameDBObject entry =
                tileObject.m_state
                    .m_gameDBObject.Entry;

            if (entry
                    .GameDBAnimatedModel8DirControllerRef !=
                null)
            {
                return "animated-model";
            }

            if (tileObject.IsAttachedToCharacter() ||
                entry.Sliding ||
                tileObject.m_state.m_moving)
            {
                return "attached-or-moving";
            }

            Direction orientation =
                entry.TexturesBackwards
                    ? IsometricCameraUtils
                        .GetRotatedDirectionInverse(
                            tileObject.Orientation)
                    : IsometricCameraUtils
                        .GetRotatedDirection(
                            tileObject.Orientation);

            int frontTexture;
            int backTexture;
            switch (orientation)
            {
                case Direction.NW:
                    frontTexture = entry.TextureIDNW;
                    backTexture = entry.TextureIDBackNW;
                    break;
                case Direction.NE:
                    frontTexture = entry.TextureIDNE;
                    backTexture = entry.TextureIDBackNE;
                    break;
                case Direction.SE:
                    frontTexture = entry.TextureIDSE;
                    backTexture = entry.TextureIDBackSE;
                    break;
                case Direction.SW:
                    frontTexture = entry.TextureIDSW;
                    backTexture = entry.TextureIDBackSW;
                    break;
                default:
                    return "unsupported-orientation";
            }

            AnimatedObjectComponent animated =
                tileObject.GetComponent<
                    AnimatedObjectComponent>();
            bool activeObjectAnimation =
                entry.m_animated &&
                animated != null &&
                animated.m_state != null &&
                !animated.IsIdle();

            if (activeObjectAnimation)
            {
                if (entry.OffsetZFront >= 0f ||
                    animated.m_state.m_forcedFrame > 0)
                {
                    frontTexture = 0;
                }

                backTexture = 0;
            }

            if (!entry.AttachedToObject &&
                !entry.PlacedToEdge &&
                backTexture != -1)
            {
                return frontTexture == -1
                    ? "back-layer-only"
                    : "front-back-layered";
            }

            if (entry.PlacedToEdge)
            {
                return "simple-edge";
            }

            if (entry.AttachedToObject)
            {
                return "simple-attached";
            }

            return "simple";
        }

        private static string GetAnimatedObjectContext(
            TileObject tileObject)
        {
            AnimatedObjectComponent animated =
                tileObject.GetComponent<
                    AnimatedObjectComponent>();
            if (animated == null ||
                animated.m_state == null)
            {
                return "none";
            }

            string frame = "n/a";
            if (!animated.IsIdle() &&
                (animated.m_state.m_forcedFrame > 0 ||
                 animated.m_state.m_duration > 0f))
            {
                try
                {
                    frame =
                        animated.GetCurrentFrame()
                            .ToString(
                                CultureInfo
                                    .InvariantCulture);
                }
                catch
                {
                    frame = "error";
                }
            }

            return "mode=" +
                animated.m_state.m_mode +
                " frame=" +
                frame +
                " forcedFrame=" +
                animated.m_state.m_forcedFrame
                    .ToString(
                        CultureInfo.InvariantCulture) +
                " frames=" +
                animated.m_state.m_animationFrames
                    .ToString(
                        CultureInfo.InvariantCulture) +
                " firstFrame=" +
                animated.m_state
                    .m_firstFrameTextureID
                    .ToString(
                        CultureInfo.InvariantCulture);
        }

        private static string GetWallNeighborhood(
            Floor floor,
            Vector2i center)
        {
            List<string> walls =
                new List<string>();

            for (int xOffset = -1;
                 xOffset <= 1;
                 xOffset++)
            {
                for (int yOffset = -1;
                     yOffset <= 1;
                     yOffset++)
                {
                    int x =
                        center.m_x + xOffset;
                    int y =
                        center.m_y + yOffset;
                    TileWalls tileWalls =
                        floor.GetWallsSafe(x, y);

                    if (tileWalls == null ||
                        (tileWalls.m_wallSW == null &&
                         tileWalls.m_wallSE == null &&
                         tileWalls.m_wallSWOpposite ==
                            null &&
                         tileWalls.m_wallSEOpposite ==
                            null &&
                         tileWalls.m_doorSW == null &&
                         tileWalls.m_doorSE == null))
                    {
                        continue;
                    }

                    walls.Add(
                        x.ToString(
                            CultureInfo
                                .InvariantCulture) +
                        "," +
                        y.ToString(
                            CultureInfo
                                .InvariantCulture) +
                        "[SW=" +
                        WallLabel(
                            tileWalls.m_wallSW) +
                        ",SE=" +
                        WallLabel(
                            tileWalls.m_wallSE) +
                        ",SWOpp=" +
                        WallLabel(
                            tileWalls
                                .m_wallSWOpposite) +
                        ",SEOpp=" +
                        WallLabel(
                            tileWalls
                                .m_wallSEOpposite) +
                        ",doorSW=" +
                        (tileWalls.m_doorSW != null) +
                        ",doorSE=" +
                        (tileWalls.m_doorSE != null) +
                        "]");
                }
            }

            return walls.Count == 0
                ? "none"
                : string.Join(
                    ";",
                    walls.ToArray());
        }

        private static string WallLabel(
            GameDBWall wall)
        {
            return wall == null
                ? "-"
                : wall.DatabaseID.ToString();
        }

        private static string Vector2iLabel(
            Vector2i value)
        {
            return "(" +
                value.m_x.ToString(
                    CultureInfo.InvariantCulture) +
                "," +
                value.m_y.ToString(
                    CultureInfo.InvariantCulture) +
                ")";
        }

        private static string Vector2fLabel(
            Vector2f value)
        {
            return "(" +
                FormatFloat(value.m_x) +
                "," +
                FormatFloat(value.m_y) +
                ")";
        }

        private static string Vector3Label(
            Vector3 value)
        {
            return "(" +
                FormatFloat(value.x) +
                "," +
                FormatFloat(value.y) +
                "," +
                FormatFloat(value.z) +
                ")";
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
