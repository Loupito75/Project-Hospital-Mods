using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using GLib;
using UnityEngine;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class DiagonalAnimationManager
    {
        private const string MaleControllerId = "MALE_DEFAULT";
        private const string FemaleControllerId = "FEMALE_DEFAULT";
        private const string WalkAnimationId = "walk";

        private static readonly Direction[] CardinalDirections =
        {
            Direction.N,
            Direction.E,
            Direction.S,
            Direction.W
        };

        private static readonly Dictionary<string, AnimModel>
            CardinalModelAliases =
                new Dictionary<string, AnimModel>();

        private static Animation s_modernizedMaleNorthWalkAnimation;

        private sealed class NorthWalkPoseDelta
        {
            internal readonly string BoneId;
            internal readonly float OffsetX;
            internal readonly float OffsetY;
            internal readonly int ZOffset;

            internal NorthWalkPoseDelta(
                string boneId,
                float offsetX,
                float offsetY,
                int zOffset)
            {
                BoneId = boneId;
                OffsetX = offsetX;
                OffsetY = offsetY;
                ZOffset = zOffset;
            }
        }

        // Exact deltas captured in HDT on male N walk keyframe 1 (t=0).
        // They are intentionally propagated as additive placement/Z deltas
        // across the remaining N keyframes so each pose keeps its native
        // rotation and scale while sharing the corrected cardinal alignment.
        private static readonly NorthWalkPoseDelta[] MaleNorthWalkPoseDeltas =
        {
            new NorthWalkPoseDelta("right_leg", -0.00929167f, 0f, 0),
            new NorthWalkPoseDelta("right_calf", -0.002f, 0f, 0),
            new NorthWalkPoseDelta("right_foot", 0.00778733f, 0.00180996f, -3),
            new NorthWalkPoseDelta("left_leg", 0.00158371f, 0f, 0),
            new NorthWalkPoseDelta("left_calf", 0.00135747f, 0f, 0),
            new NorthWalkPoseDelta("left_foot", -0.00180996f, 0.01176471f, -1),
            new NorthWalkPoseDelta("head", -0.00588235f, -0.00475113f, 0),
            new NorthWalkPoseDelta("right_arm", -0.00384616f, -0.01380091f, 0),
            new NorthWalkPoseDelta("left_arm", 0.00384615f, -0.00565611f, -6),
            new NorthWalkPoseDelta("right_forearm", -0.00090498f, 0.00859729f, 1),
            new NorthWalkPoseDelta("left_forearm", -0.00701358f, 0.02262443f, 0),
            new NorthWalkPoseDelta("left_hand", 0.00361991f, -0.01153846f, 0)
        };

        internal static void RegisterCardinalWalkAssets(
            StreamingAssetManager streamingAssets)
        {
            if (!TrafficControlConfig.ExperimentalDiagonalCornerSmoothing ||
                streamingAssets == null ||
                streamingAssets.m_animationsToLoad == null)
            {
                return;
            }

            // These six files are present in Project Hospital's real
            // StreamingAssets. W uses the E file mirrored, exactly like
            // EIGHT_DIR_MIRRORED does in GameDBAnimationSet.
            streamingAssets.m_animationsToLoad.Add(
                "anim_walk/m_n_walk.xml");
            streamingAssets.m_animationsToLoad.Add(
                "anim_walk/m_e_walk.xml");
            streamingAssets.m_animationsToLoad.Add(
                "anim_walk/m_s_walk.xml");
            streamingAssets.m_animationsToLoad.Add(
                "anim_walk/f_n_walk.xml");
            streamingAssets.m_animationsToLoad.Add(
                "anim_walk/f_e_walk.xml");
            streamingAssets.m_animationsToLoad.Add(
                "anim_walk/f_s_walk.xml");
        }

        internal static bool SupportsDiagonalAnimation(string animationId)
        {
            return animationId == WalkAnimationId;
        }

        internal static bool SupportsDiagonalAnimation(
            Entity character,
            string animationId)
        {
            return animationId == WalkAnimationId &&
                   SupportsCardinalWalk(character);
        }

        internal static bool SupportsCardinalWalk(Entity character)
        {
            if (character == null)
            {
                return false;
            }

            AnimModelComponent animation =
                character.GetComponent<AnimModelComponent>();
            if (animation == null ||
                animation.m_state == null ||
                animation.m_state.m_gameDBAnimatedModelController == null ||
                animation.m_state.m_gameDBAnimatedModelController.Entry == null)
            {
                return false;
            }

            return IsSupportedController(
                animation.m_state.m_gameDBAnimatedModelController.Entry);
        }

        internal static void AddCardinalWalkInstances(
            AnimatedModel8DirController runtimeController,
            GameDBAnimatedModel8DirController controller,
            Face face,
            Clothes clothes,
            GameDBBodyType bodyType,
            Dictionary<int, AnimModelInstanceData> instances)
        {
            if (!TrafficControlConfig.ExperimentalDiagonalCornerSmoothing ||
                runtimeController == null ||
                controller == null ||
                instances == null ||
                !IsSupportedController(controller))
            {
                return;
            }

            GameDBAnimationSet walkSet =
                controller.GetAnimationSet(WalkAnimationId);
            if (walkSet == null)
            {
                return;
            }

            string folder =
                string.IsNullOrEmpty(walkSet.Folder)
                    ? string.Empty
                    : walkSet.Folder + "/";
            string prefix = controller.AnimationFilenamePrefix;
            if (string.IsNullOrEmpty(prefix))
            {
                return;
            }

            bool useCompleteMaleVisualModels =
                controller.DatabaseID != null &&
                controller.DatabaseID.ToString() == MaleControllerId;

            AnimModel modernizedMaleNModel = null;
            bool useModernizedMaleNVisual =
                useCompleteMaleVisualModels &&
                MaleNCardinalRuntime.TryGetModel(
                    controller.ModelN,
                    controller.ModelNE,
                    out modernizedMaleNModel);

            // MALE_DEFAULT keeps the complete modern NE skeleton/slice set,
            // but N is rebuilt at runtime from the real legacy N placement
            // where that information exists. Modern-only slices stay on their
            // NE placement until dedicated N artwork/placement exists. The
            // edited PNG cells remain separate files and are packed only into
            // a transient in-memory texture; no generated atlas/model is
            // installed on disk. If runtime construction fails, keep the
            // complete NE visual fallback used by the earlier prototype.
            string modelN = useCompleteMaleVisualModels
                ? (useModernizedMaleNVisual
                    ? controller.ModelN
                    : controller.ModelNE)
                : controller.ModelN;
            bool mirrorN = useCompleteMaleVisualModels
                ? (useModernizedMaleNVisual
                    ? controller.MirrorN
                    : true)
                : controller.MirrorN;

            string modelE = useCompleteMaleVisualModels
                ? controller.ModelSW
                : controller.ModelE;
            bool mirrorE = useCompleteMaleVisualModels
                ? false
                : controller.MirrorE;

            string modelS = useCompleteMaleVisualModels
                ? controller.ModelSW
                : controller.ModelS;
            bool mirrorS = useCompleteMaleVisualModels
                ? false
                : controller.MirrorS;

            string modelW = useCompleteMaleVisualModels
                ? controller.ModelSW
                : controller.ModelW;
            bool mirrorW = useCompleteMaleVisualModels
                ? true
                : controller.MirrorW;

            bool added = false;

            added |= AddCardinalInstance(
                instances,
                Direction.N,
                modelN,
                mirrorN,
                folder + prefix + "n_walk.xml",
                bodyType,
                useModernizedMaleNVisual
                    ? modernizedMaleNModel
                    : null);

            added |= AddCardinalInstance(
                instances,
                Direction.E,
                modelE,
                mirrorE,
                folder + prefix + "e_walk.xml",
                bodyType,
                null);

            added |= AddCardinalInstance(
                instances,
                Direction.S,
                modelS,
                mirrorS,
                folder + prefix + "s_walk.xml",
                bodyType,
                null);

            added |= AddCardinalInstance(
                instances,
                Direction.W,
                modelW,
                mirrorW,
                folder + prefix + "e_walk.xml",
                bodyType,
                null);

            if (added)
            {
                // Reuse the controller's native clothing/face pipeline once
                // after all cardinal instances exist.
                runtimeController.SetClothing(
                    face,
                    clothes,
                    DressLevel.FULL,
                    null);
            }
        }

        internal static void PlayCardinalWalk(
            Dictionary<int, AnimModelInstanceData> instances,
            string animationId,
            bool looping)
        {
            if (!SupportsDiagonalAnimation(animationId) ||
                instances == null)
            {
                return;
            }

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Direction direction = CardinalDirections[i];
                AnimModelInstanceData data;
                if (!instances.TryGetValue((int)direction, out data) ||
                    data == null ||
                    data.m_animationController == null ||
                    !data.m_animationController.hasAnimation(animationId))
                {
                    continue;
                }

                data.m_animationController.playAnimation(
                    animationId,
                    looping,
                    direction);
            }
        }

        internal static void PlayCardinalWalkFrom(
            Dictionary<int, AnimModelInstanceData> instances,
            string animationId,
            float time,
            bool looping)
        {
            if (!SupportsDiagonalAnimation(animationId) ||
                instances == null)
            {
                return;
            }

            for (int i = 0; i < CardinalDirections.Length; i++)
            {
                Direction direction = CardinalDirections[i];
                AnimModelInstanceData data;
                if (!instances.TryGetValue((int)direction, out data) ||
                    data == null ||
                    data.m_animationController == null ||
                    !data.m_animationController.hasAnimation(animationId))
                {
                    continue;
                }

                data.m_animationController.playAnimationFrom(
                    animationId,
                    time,
                    looping,
                    direction);
            }
        }

        internal static void ApplyDiagonalFacing(WalkComponent walk)
        {
            if (!TrafficControlConfig.ExperimentalDiagonalCornerSmoothing ||
                walk == null ||
                walk.m_state == null ||
                walk.m_state.m_walkState != WalkState.Walking ||
                walk.m_route == null ||
                walk.m_route.Nodes == null ||
                walk.m_route.Nodes.Count < 2 ||
                walk.m_state.m_routeIndex <= 0 ||
                walk.m_state.m_routeIndex >= walk.m_route.Nodes.Count)
            {
                return;
            }

            Entity character = CharacterAccess.GetEntity(walk);
            AnimModelComponent animation =
                character == null
                    ? null
                    : character.GetComponent<AnimModelComponent>();
            if (animation == null ||
                animation.m_state == null)
            {
                return;
            }

            if (!SupportsDiagonalAnimation(
                    character,
                    animation.m_state.m_currentAnimationID))
            {
                NormalizeCardinalDirection(animation);
                return;
            }

            PathfinderNode fromNode =
                walk.m_route.Nodes[walk.m_state.m_routeIndex - 1];
            PathfinderNode toNode =
                walk.m_route.Nodes[walk.m_state.m_routeIndex];
            if (fromNode == null || toNode == null)
            {
                return;
            }

            Direction direction;
            if (!TryGetCardinalMovementDirection(
                    fromNode.Position,
                    toNode.Position,
                    out direction))
            {
                return;
            }

            animation.SetDirection(direction);
        }

        internal static void NormalizeFacingOutsideWalking(
            WalkComponent walk,
            WalkState newState)
        {
            if (!TrafficControlConfig.ExperimentalDiagonalCornerSmoothing ||
                newState == WalkState.Walking ||
                walk == null)
            {
                return;
            }

            Entity character = CharacterAccess.GetEntity(walk);
            AnimModelComponent animation =
                character == null
                    ? null
                    : character.GetComponent<AnimModelComponent>();
            if (animation == null || animation.m_state == null)
            {
                return;
            }

            NormalizeCardinalDirection(animation);
        }

        private static void NormalizeCardinalDirection(
            AnimModelComponent animation)
        {
            if (animation == null || animation.m_state == null)
            {
                return;
            }

            Direction direction = animation.m_state.m_direction;
            switch (direction)
            {
                case Direction.N:
                    animation.m_state.m_direction = Direction.NE;
                    break;
                case Direction.E:
                    animation.m_state.m_direction = Direction.SE;
                    break;
                case Direction.S:
                    animation.m_state.m_direction = Direction.SW;
                    break;
                case Direction.W:
                    animation.m_state.m_direction = Direction.NW;
                    break;
            }
        }

        internal static bool TryGetOppositeCardinal(
            Direction direction,
            out Direction opposite)
        {
            switch (direction)
            {
                case Direction.N:
                    opposite = Direction.S;
                    return true;
                case Direction.E:
                    opposite = Direction.W;
                    return true;
                case Direction.S:
                    opposite = Direction.N;
                    return true;
                case Direction.W:
                    opposite = Direction.E;
                    return true;
                default:
                    opposite = Direction.None;
                    return false;
            }
        }

        internal static bool TryRotateCardinal(
            Direction direction,
            Direction cameraRotation,
            bool inverse,
            out Direction rotated)
        {
            if (!IsCardinal(direction))
            {
                rotated = Direction.None;
                return false;
            }

            int quarterTurns;
            switch (cameraRotation)
            {
                case Direction.NW:
                    quarterTurns = 0;
                    break;
                case Direction.NE:
                    quarterTurns = inverse ? 3 : 1;
                    break;
                case Direction.SE:
                    quarterTurns = 2;
                    break;
                case Direction.SW:
                    quarterTurns = inverse ? 1 : 3;
                    break;
                default:
                    rotated = Direction.None;
                    return false;
            }

            rotated = RotateCardinal(direction, quarterTurns);
            return true;
        }

        private static bool AddCardinalInstance(
            Dictionary<int, AnimModelInstanceData> instances,
            Direction direction,
            string modelPath,
            bool mirrored,
            string animationPath,
            GameDBBodyType bodyType,
            AnimModel sourceModelOverride)
        {
            if (instances.ContainsKey((int)direction) ||
                string.IsNullOrEmpty(modelPath) ||
                string.IsNullOrEmpty(animationPath))
            {
                return false;
            }

            StreamingAssetManager streamingAssets =
                StreamingAssetManager.GetInstance();
            if (streamingAssets == null)
            {
                return false;
            }

            AnimModel sourceModel =
                sourceModelOverride ??
                streamingAssets.GetAnimModel(modelPath);
            Animation walkAnimation =
                streamingAssets.GetAnimation(animationPath);
            if (sourceModel == null || walkAnimation == null)
            {
                return false;
            }

            if (direction == Direction.N &&
                sourceModelOverride != null &&
                animationPath.EndsWith("m_n_walk.xml"))
            {
                walkAnimation =
                    GetOrCreateModernizedMaleNorthWalkAnimation(
                        streamingAssets,
                        animationPath,
                        walkAnimation);
            }

            // AnimationManager stores animations globally by
            // AnimModel.m_filename + animation ID. FEMALE_DEFAULT deliberately
            // reuses female_ne_model/female_sw_model for N/E/S/W, so registering
            // f_n_walk as "walk" on the original model name would collide with
            // the existing f_ne_walk/f_sw_walk entry. Use a lightweight shared
            // runtime alias with a unique AnimationManager namespace for each
            // cardinal direction.
            AnimModel cardinalModel =
                GetOrCreateCardinalModelAlias(
                    sourceModel,
                    direction);

            AnimModelPose morph = null;
            if (bodyType ==
                Database.Instance.GetEntry<GameDBBodyType>("BODYTYPE_BIG"))
            {
                morph = new AnimModelPose(streamingAssets.m_morphBig);
            }
            else if (bodyType ==
                     Database.Instance.GetEntry<GameDBBodyType>("BODYTYPE_SLIM"))
            {
                morph = new AnimModelPose(streamingAssets.m_morphSkinny);
            }

            AnimModelInstanceData data =
                new AnimModelInstanceData(
                    cardinalModel,
                    morph,
                    mirrored);
            data.m_animationController.addAnimation(
                WalkAnimationId,
                walkAnimation,
                direction);

            instances[(int)direction] = data;
            return true;
        }

        private static AnimModel GetOrCreateCardinalModelAlias(
            AnimModel source,
            Direction direction)
        {
            string aliasName =
                source.m_filename +
                "#L75_HTC_CARDINAL_" +
                direction.ToString() +
                "#" +
                source.m_textureFilename;

            AnimModel alias;
            if (CardinalModelAliases.TryGetValue(aliasName, out alias))
            {
                return alias;
            }

            alias = new AnimModel(source.m_skeleton);
            alias.m_name = source.m_name;
            alias.m_filename = aliasName;
            alias.m_skeleton = source.m_skeleton;
            alias.m_skeletonFilename = source.m_skeletonFilename;
            alias.m_textureFilename = source.m_textureFilename;
            alias.m_slices = source.m_slices;
            CardinalModelAliases[aliasName] = alias;
            return alias;
        }

        internal static void ResetRuntimeAssets()
        {
            CardinalModelAliases.Clear();
            s_modernizedMaleNorthWalkAnimation = null;
            MaleNCardinalRuntime.Reset();
        }

        private static Animation GetOrCreateModernizedMaleNorthWalkAnimation(
            StreamingAssetManager streamingAssets,
            string northAnimationPath,
            Animation northAnimation)
        {
            if (s_modernizedMaleNorthWalkAnimation != null)
            {
                return s_modernizedMaleNorthWalkAnimation;
            }

            if (streamingAssets == null ||
                northAnimation == null ||
                string.IsNullOrEmpty(northAnimationPath))
            {
                return northAnimation;
            }

            string northEastAnimationPath =
                northAnimationPath.Replace(
                    "m_n_walk.xml",
                    "m_ne_walk.xml");
            Animation northEastAnimation =
                streamingAssets.GetAnimation(northEastAnimationPath);
            if (northEastAnimation == null)
            {
                Plugin.Log?.LogWarning(
                    "Male N walk modernization could not load " +
                    northEastAnimationPath +
                    "; keeping the native N animation.");
                return northAnimation;
            }

            Animation modernized = northAnimation.clone();
            int missingBoneCount =
                CompleteAnimationBonesFromReference(
                    modernized,
                    northEastAnimation);
            ApplyMaleNorthWalkPoseDeltas(modernized);

            s_modernizedMaleNorthWalkAnimation = modernized;
            Plugin.Log?.LogInfo(
                "Male N runtime walk animation ready: " +
                missingBoneCount +
                " missing modern bone(s) transferred across " +
                modernized.m_keyFrames.Count +
                " keyframes; captured N placement/Z deltas propagated.");
            return s_modernizedMaleNorthWalkAnimation;
        }

        private static int CompleteAnimationBonesFromReference(
            Animation target,
            Animation reference)
        {
            if (target == null ||
                reference == null ||
                target.m_keyFrames == null ||
                reference.m_keyFrames == null ||
                target.m_keyFrames.Count == 0 ||
                reference.m_keyFrames.Count == 0)
            {
                return 0;
            }

            HashSet<string> referenceBones =
                new HashSet<string>();
            for (int i = 0; i < reference.m_keyFrames.Count; i++)
            {
                KeyFrame referenceKeyFrame =
                    reference.m_keyFrames[i];
                if (referenceKeyFrame == null ||
                    referenceKeyFrame.m_boneTransforms == null)
                {
                    continue;
                }

                foreach (string boneId in
                         referenceKeyFrame.m_boneTransforms.Keys)
                {
                    referenceBones.Add(boneId);
                }
            }

            HashSet<string> missingBones =
                new HashSet<string>();
            KeyFrame firstTarget = target.m_keyFrames[0];
            foreach (string boneId in referenceBones)
            {
                if (firstTarget.m_boneTransforms == null ||
                    !firstTarget.m_boneTransforms.ContainsKey(boneId))
                {
                    missingBones.Add(boneId);
                }
            }

            foreach (string boneId in missingBones)
            {
                for (int i = 0; i < target.m_keyFrames.Count; i++)
                {
                    KeyFrame targetKeyFrame =
                        target.m_keyFrames[i];
                    if (targetKeyFrame.m_boneTransforms.ContainsKey(boneId))
                    {
                        continue;
                    }

                    AnimModelBoneTransform sourceTransform =
                        FindClosestReferenceTransform(
                            reference,
                            boneId,
                            targetKeyFrame.m_fTime);
                    if (sourceTransform == null)
                    {
                        sourceTransform =
                            AnimModelBoneTransform.ZERO_TRANSFORM;
                    }

                    targetKeyFrame.m_boneTransforms[boneId] =
                        CloneBoneTransform(sourceTransform);
                }
            }

            return missingBones.Count;
        }

        private static AnimModelBoneTransform FindClosestReferenceTransform(
            Animation reference,
            string boneId,
            float targetTime)
        {
            AnimModelBoneTransform best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < reference.m_keyFrames.Count; i++)
            {
                KeyFrame keyFrame = reference.m_keyFrames[i];
                if (keyFrame == null ||
                    keyFrame.m_boneTransforms == null)
                {
                    continue;
                }

                AnimModelBoneTransform transform;
                if (!keyFrame.m_boneTransforms.TryGetValue(
                        boneId,
                        out transform))
                {
                    continue;
                }

                float distance =
                    Math.Abs(keyFrame.m_fTime - targetTime);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = transform;
                }
            }

            return best;
        }

        private static AnimModelBoneTransform CloneBoneTransform(
            AnimModelBoneTransform source)
        {
            return new AnimModelBoneTransform(
                source.m_rotation,
                new Vector2f(
                    source.m_offset.m_x,
                    source.m_offset.m_y),
                new Vector2f(
                    source.m_textureOffset.m_x,
                    source.m_textureOffset.m_y),
                new Vector2f(
                    source.m_scale.m_x,
                    source.m_scale.m_y),
                source.m_zOffset,
                source.m_hidden,
                null,
                null);
        }

        private static void ApplyMaleNorthWalkPoseDeltas(
            Animation animation)
        {
            if (animation == null ||
                animation.m_keyFrames == null)
            {
                return;
            }

            for (int keyFrameIndex = 0;
                 keyFrameIndex < animation.m_keyFrames.Count;
                 keyFrameIndex++)
            {
                KeyFrame keyFrame =
                    animation.m_keyFrames[keyFrameIndex];
                if (keyFrame == null ||
                    keyFrame.m_boneTransforms == null)
                {
                    continue;
                }

                for (int deltaIndex = 0;
                     deltaIndex < MaleNorthWalkPoseDeltas.Length;
                     deltaIndex++)
                {
                    NorthWalkPoseDelta delta =
                        MaleNorthWalkPoseDeltas[deltaIndex];
                    AnimModelBoneTransform transform;
                    if (!keyFrame.m_boneTransforms.TryGetValue(
                            delta.BoneId,
                            out transform) ||
                        transform == null)
                    {
                        continue;
                    }

                    transform.m_offset =
                        new Vector2f(
                            transform.m_offset.m_x +
                            delta.OffsetX,
                            transform.m_offset.m_y +
                            delta.OffsetY);
                    transform.m_zOffset += delta.ZOffset;
                }
            }
        }

        private static bool IsSupportedController(
            GameDBAnimatedModel8DirController controller)
        {
            if (controller == null || controller.DatabaseID == null)
            {
                return false;
            }

            string id = controller.DatabaseID.ToString();
            return id == MaleControllerId || id == FemaleControllerId;
        }

        private static bool TryGetCardinalMovementDirection(
            Vector2i from,
            Vector2i to,
            out Direction direction)
        {
            int dx = to.m_x - from.m_x;
            int dy = to.m_y - from.m_y;

            if (dx == 1 && dy == 1)
            {
                direction = Direction.N;
                return true;
            }
            if (dx == -1 && dy == 1)
            {
                direction = Direction.E;
                return true;
            }
            if (dx == -1 && dy == -1)
            {
                direction = Direction.S;
                return true;
            }
            if (dx == 1 && dy == -1)
            {
                direction = Direction.W;
                return true;
            }

            direction = Direction.None;
            return false;
        }

        private static bool IsCardinal(Direction direction)
        {
            return direction == Direction.N ||
                   direction == Direction.E ||
                   direction == Direction.S ||
                   direction == Direction.W;
        }

        private static Direction RotateCardinal(
            Direction direction,
            int quarterTurns)
        {
            Direction result = direction;
            for (int i = 0; i < quarterTurns; i++)
            {
                switch (result)
                {
                    case Direction.N:
                        result = Direction.E;
                        break;
                    case Direction.E:
                        result = Direction.S;
                        break;
                    case Direction.S:
                        result = Direction.W;
                        break;
                    case Direction.W:
                        result = Direction.N;
                        break;
                }
            }

            return result;
        }
    }
}
