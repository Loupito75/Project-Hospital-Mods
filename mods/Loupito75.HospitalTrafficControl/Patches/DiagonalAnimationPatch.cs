using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(StreamingAssetManager), "FillAnimModelsToLoad")]
    internal static class DiagonalWalkAssetLoadPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix(StreamingAssetManager __instance)
        {
            DiagonalAnimationManager.RegisterCardinalWalkAssets(__instance);
        }
    }

    [HarmonyPatch]
    internal static class DiagonalAnimatedModelConstructorPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Constructor(
                typeof(AnimatedModel8DirController),
                new System.Type[]
                {
                    typeof(GameDBAnimatedModel8DirController),
                    typeof(Face),
                    typeof(Clothes),
                    typeof(GameDBBodyType)
                });
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix(
            AnimatedModel8DirController __instance,
            GameDBAnimatedModel8DirController controller,
            Face face,
            Clothes clothes,
            GameDBBodyType bodyType,
            Dictionary<int, AnimModelInstanceData> ___m_animModelInstanceData)
        {
            DiagonalAnimationManager.AddCardinalWalkInstances(
                __instance,
                controller,
                face,
                clothes,
                bodyType,
                ___m_animModelInstanceData);
        }
    }

    [HarmonyPatch(
        typeof(AnimatedModel8DirController),
        nameof(AnimatedModel8DirController.PlayAnimation),
        new System.Type[] { typeof(string), typeof(bool) })]
    internal static class DiagonalAnimatedModelPlayPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix(
            string animationID,
            bool looping,
            Dictionary<int, AnimModelInstanceData> ___m_animModelInstanceData)
        {
            DiagonalAnimationManager.PlayCardinalWalk(
                ___m_animModelInstanceData,
                animationID,
                looping);
        }
    }

    [HarmonyPatch(
        typeof(AnimatedModel8DirController),
        nameof(AnimatedModel8DirController.PlayAnimationFrom),
        new System.Type[] { typeof(string), typeof(float), typeof(bool) })]
    internal static class DiagonalAnimatedModelPlayFromPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix(
            string animationID,
            float time,
            bool looping,
            Dictionary<int, AnimModelInstanceData> ___m_animModelInstanceData)
        {
            DiagonalAnimationManager.PlayCardinalWalkFrom(
                ___m_animModelInstanceData,
                animationID,
                time,
                looping);
        }
    }

    [HarmonyPatch(typeof(AnimModelInstance), "updateSlices")]
    internal static class MaleNCardinalSliceUpdatePatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix(AnimModelInstance __instance)
        {
            MaleNCardinalRuntime.ApplyRuntimeSliceOverrides(__instance);
        }
    }

    [HarmonyPatch(typeof(WalkComponent), "PlayWalkAnim")]
    internal static class DiagonalWalkStartFacingPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix(WalkComponent __instance)
        {
            DiagonalAnimationManager.ApplyDiagonalFacing(__instance);
        }
    }

    [HarmonyPatch(typeof(WalkComponent), "UpdateMovementSingle")]
    internal static class DiagonalWalkFacingPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix(WalkComponent __instance)
        {
            DiagonalAnimationManager.ApplyDiagonalFacing(__instance);
        }
    }

    [HarmonyPatch(typeof(WalkComponent), nameof(WalkComponent.SwitchState))]
    internal static class DiagonalWalkStateFacingPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Postfix(
            WalkComponent __instance,
            WalkState state)
        {
            DiagonalAnimationManager.NormalizeFacingOutsideWalking(
                __instance,
                state);
        }
    }

    [HarmonyPatch(typeof(DirectionExtensions), nameof(DirectionExtensions.GetOppositeDirection))]
    internal static class DiagonalDirectionOppositePatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static bool Prefix(
            Direction direction,
            ref Direction __result)
        {
            Direction opposite;
            if (!DiagonalAnimationManager.TryGetOppositeCardinal(
                    direction,
                    out opposite))
            {
                return true;
            }

            __result = opposite;
            return false;
        }
    }

    [HarmonyPatch(typeof(IsometricCameraUtils), nameof(IsometricCameraUtils.GetRotatedDirection))]
    internal static class DiagonalRotatedDirectionPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static bool Prefix(
            Direction direction,
            ref Direction __result)
        {
            if (Hospital.Instance == null ||
                Hospital.Instance.m_state == null)
            {
                return true;
            }

            Direction rotated;
            if (!DiagonalAnimationManager.TryRotateCardinal(
                    direction,
                    Hospital.Instance.m_state.m_rotation,
                    false,
                    out rotated))
            {
                return true;
            }

            __result = rotated;
            return false;
        }
    }

    [HarmonyPatch(typeof(IsometricCameraUtils), nameof(IsometricCameraUtils.GetRotatedDirectionInverse))]
    internal static class DiagonalRotatedDirectionInversePatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static bool Prefix(
            Direction direction,
            ref Direction __result)
        {
            if (Hospital.Instance == null ||
                Hospital.Instance.m_state == null)
            {
                return true;
            }

            Direction rotated;
            if (!DiagonalAnimationManager.TryRotateCardinal(
                    direction,
                    Hospital.Instance.m_state.m_rotation,
                    true,
                    out rotated))
            {
                return true;
            }

            __result = rotated;
            return false;
        }
    }
}
