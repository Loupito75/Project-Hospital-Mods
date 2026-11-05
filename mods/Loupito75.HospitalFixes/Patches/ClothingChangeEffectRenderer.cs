using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using GLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
    internal static class ClothingChangeEffectRenderer
    {
        private const int ExpectedTextureSize = 128;
        // Standing patient and HSH staff overlays use the same artwork
        // and therefore share one in-game directional calibration.
        // Unknown directions deliberately show no overlay instead of falling
        // back to an approximate position.
        private const float StandingNeWidth = 2.462236f;
        private const float StandingNeHeight = 2.663746f;
        private const float StandingNeXOffset = -0.022719f;
        private const float StandingNeYOffset = -0.278202f;

        private const float StandingNwWidth = 2.462236f;
        private const float StandingNwHeight = 2.663746f;
        private const float StandingNwXOffset = 0.017281f;
        private const float StandingNwYOffset = -0.313202f;

        private const float StandingSwWidth = 2.612236f;
        private const float StandingSwHeight = 2.663746f;
        private const float StandingSwXOffset = 0.062281f;
        private const float StandingSwYOffset = -0.253202f;

        private const float StandingSeWidth = 2.562236f;
        private const float StandingSeHeight = 2.663746f;
        private const float StandingSeXOffset = -0.052719f;
        private const float StandingSeYOffset = -0.253202f;

        // Examination-bed seated patient calibration measured in game.
        // Each rendered direction has its own X/Y and rectangular geometry.
        // Unknown directions deliberately show no overlay instead of falling
        // back to an approximate calibration.
        private const float SeatedNeWidth = 2.300906f;
        private const float SeatedNeHeight = 2.300906f;
        private const float SeatedNeXOffset = -0.096979f;
        private const float SeatedNeYOffset = -0.182175f;

        private const float SeatedNwWidth = 2.282779f;
        private const float SeatedNwHeight = 2.300906f;
        private const float SeatedNwXOffset = 0.093353f;
        private const float SeatedNwYOffset = -0.176133f;

        private const float SeatedSwWidth = 2.816617f;
        private const float SeatedSwHeight = 2.3f;
        private const float SeatedSwXOffset = 0.138671f;
        private const float SeatedSwYOffset = 0.056495f;

        private const float SeatedSeWidth = 2.57719f;
        private const float SeatedSeHeight = 2.363444f;
        private const float SeatedSeXOffset = -0.145317f;
        private const float SeatedSeYOffset = -0.014502f;

        // Bed-lying patient calibration measured in game. Each rendered
        // Lying* direction owns its complete visual geometry and rotation
        // delta. Unknown directions deliberately show no overlay.
        private const float LyingNeWidth = 2.331571f;
        private const float LyingNeHeight = 2.500302f;
        private const float LyingNeXOffset = -1.182224f;
        private const float LyingNeYOffset = 0.28852f;
        private const float LyingNeZOffset = -0.478811f;
        private const float LyingNeRotationDeltaDegrees = 1.41994f;

        private const float LyingNwWidth = 2.446827f;
        private const float LyingNwHeight = 2.446374f;
        private const float LyingNwXOffset = 0.100833f;
        private const float LyingNwYOffset = 0.906961f;
        private const float LyingNwZOffset = -0.49f;
        private const float LyingNwRotationDeltaDegrees = 125.2326f;

        private const float LyingSwWidth = 2.397129f;
        private const float LyingSwHeight = 2.396072f;
        private const float LyingSwXOffset = -0.001511f;
        private const float LyingSwYOffset = 0.984983f;
        private const float LyingSwZOffset = -0.49f;
        private const float LyingSwRotationDeltaDegrees = 222f;

        private const float LyingSeWidth = 2.150151f;
        private const float LyingSeHeight = 2.295771f;
        private const float LyingSeXOffset = 0.102764f;
        private const float LyingSeYOffset = 0.904983f;
        private const float LyingSeZOffset = -0.49f;
        private const float LyingSeRotationDeltaDegrees = -7.486405f;

        private const float LyingBodyRotationNeRadians = 1.07f;
        private const float LyingBodyRotationSwRadians = 1.91f;
        private const float RadiansToDegrees = 57.2957795f;

        private const float MainStartTime = 2.40f;
        private const float MainFrameDuration = 0.10f;
        private const float EffectEndTime =
            ClothingChangeAnimationFactory.AnimationLength;
        private const float StandaloneEffectDuration =
            EffectEndTime - MainStartTime;

        private const float AccentFrame1Time = 2.50f;
        private const float AccentFrame2Time = 2.65f;
        private const float AccentFrame3Time = 2.80f;
        private const float AccentFrame4Time = 2.95f;

        private const float MainLocalZ = -0.035f;
        private const float AccentLocalZ = -0.045f;

        private static readonly string[] MainFiles =
            new string[]
            {
                "L75_HFX_CLOTHING_CHANGE_MAIN_01.png",
                "L75_HFX_CLOTHING_CHANGE_MAIN_02.png",
                "L75_HFX_CLOTHING_CHANGE_MAIN_03.png",
                "L75_HFX_CLOTHING_CHANGE_MAIN_04.png",
                "L75_HFX_CLOTHING_CHANGE_MAIN_05.png",
                "L75_HFX_CLOTHING_CHANGE_MAIN_06.png"
            };

        private static readonly string[] AccentFiles =
            new string[]
            {
                "L75_HFX_CLOTHING_CHANGE_ACCENT_01.png",
                "L75_HFX_CLOTHING_CHANGE_ACCENT_02.png",
                "L75_HFX_CLOTHING_CHANGE_ACCENT_03.png",
                "L75_HFX_CLOTHING_CHANGE_ACCENT_04.png"
            };

        private static readonly string[] SeatedMainFiles =
            new string[]
            {
                "L75_HFX_CLOTHING_CHANGE_SIT_MAIN_01.png",
                "L75_HFX_CLOTHING_CHANGE_SIT_MAIN_02.png",
                "L75_HFX_CLOTHING_CHANGE_SIT_MAIN_03.png",
                "L75_HFX_CLOTHING_CHANGE_SIT_MAIN_04.png",
                "L75_HFX_CLOTHING_CHANGE_SIT_MAIN_05.png",
                "L75_HFX_CLOTHING_CHANGE_SIT_MAIN_06.png"
            };

        private static readonly string[] SeatedAccentFiles =
            new string[]
            {
                "L75_HFX_CLOTHING_CHANGE_SIT_ACCENT_01.png",
                "L75_HFX_CLOTHING_CHANGE_SIT_ACCENT_02.png",
                "L75_HFX_CLOTHING_CHANGE_SIT_ACCENT_03.png",
                "L75_HFX_CLOTHING_CHANGE_SIT_ACCENT_04.png"
            };

        private static readonly string[] LyingMainFiles =
            new string[]
            {
                "L75_HFX_CLOTHING_CHANGE_LYING_MAIN_01.png",
                "L75_HFX_CLOTHING_CHANGE_LYING_MAIN_02.png",
                "L75_HFX_CLOTHING_CHANGE_LYING_MAIN_03.png",
                "L75_HFX_CLOTHING_CHANGE_LYING_MAIN_04.png",
                "L75_HFX_CLOTHING_CHANGE_LYING_MAIN_05.png",
                "L75_HFX_CLOTHING_CHANGE_LYING_MAIN_06.png"
            };

        private static readonly string[] LyingAccentFiles =
            new string[]
            {
                "L75_HFX_CLOTHING_CHANGE_LYING_ACCENT_01.png",
                "L75_HFX_CLOTHING_CHANGE_LYING_ACCENT_02.png",
                "L75_HFX_CLOTHING_CHANGE_LYING_ACCENT_03.png",
                "L75_HFX_CLOTHING_CHANGE_LYING_ACCENT_04.png"
            };

        private static readonly Texture2D[] MainTextures =
            new Texture2D[6];

        private static readonly Texture2D[] AccentTextures =
            new Texture2D[4];

        private static readonly Texture2D[] SeatedMainTextures =
            new Texture2D[6];

        private static readonly Texture2D[] SeatedAccentTextures =
            new Texture2D[4];

        private static readonly Texture2D[] LyingMainTextures =
            new Texture2D[6];

        private static readonly Texture2D[] LyingAccentTextures =
            new Texture2D[4];

        private static readonly Dictionary<Entity, EffectState>
            ActiveEffects =
                new Dictionary<Entity, EffectState>();

        private static bool StandingAssetsLoadAttempted;
        private static bool SeatedAssetsLoadAttempted;
        private static bool LyingAssetsLoadAttempted;
        private static bool StandingAssetsAvailable;
        private static bool SeatedAssetsAvailable;
        private static bool LyingAssetsAvailable;

        private struct EffectCalibration
        {
            internal readonly float Width;
            internal readonly float Height;
            internal readonly float XOffset;
            internal readonly float YOffset;
            internal readonly float ZOffset;
            internal readonly float RotationZ;
            internal readonly bool CenterPivot;
            internal readonly bool OverrideMirroring;
            internal readonly bool Mirrored;

            internal EffectCalibration(
                float width,
                float height,
                float xOffset,
                float yOffset,
                float zOffset,
                float rotationZ,
                bool centerPivot,
                bool overrideMirroring,
                bool mirrored)
            {
                Width = width;
                Height = height;
                XOffset = xOffset;
                YOffset = yOffset;
                ZOffset = zOffset;
                RotationZ = rotationZ;
                CenterPivot = centerPivot;
                OverrideMirroring = overrideMirroring;
                Mirrored = mirrored;
            }
        }

        private sealed class EffectState
        {
            internal AnimModelComponent Animation;
            internal RendererObject MainLayer;
            internal RendererObject AccentLayer;
            internal GameObject Parent;
            internal int MainFrame = -2;
            internal int AccentFrame = -2;
            internal bool Mirrored;
            internal bool MirrorInitialized;
            internal bool Standalone;
            internal bool Seated;
            internal bool Lying;
            internal bool CalibrationInitialized;
            internal Direction CalibrationDirection;
            internal ProcedureScript Procedure;
            internal float StandaloneStartTime;
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
        private static class CharacterRenderPatch
        {
            [HarmonyPostfix]
            private static void Postfix(
                Entity character,
                Dictionary<Entity, RendererObject>
                    ___m_characterRendererObjects)
            {
                if (character == null ||
                    ___m_characterRendererObjects == null)
                {
                    return;
                }

                EffectState state;
                if (!ActiveEffects.TryGetValue(
                        character,
                        out state) ||
                    state == null ||
                    state.Animation == null)
                {
                    return;
                }

                RendererObject characterRenderer;
                if (!___m_characterRendererObjects.TryGetValue(
                        character,
                        out characterRenderer) ||
                    characterRenderer == null ||
                    characterRenderer.m_gameObject == null)
                {
                    return;
                }

                if (!UpdateEffect(
                        state,
                        characterRenderer.m_gameObject))
                {
                    Stop(character);
                }
            }
        }

        internal static void Start(
            Entity entity,
            AnimModelComponent animation)
        {
            StartInternal(
                entity,
                animation,
                seated: false,
                lying: false,
                procedure: null);
        }

        internal static void StartSeated(
            Entity entity,
            AnimModelComponent animation)
        {
            StartInternal(
                entity,
                animation,
                seated: true,
                lying: false,
                procedure: null);
        }

        internal static void StartLying(
            Entity entity,
            AnimModelComponent animation,
            ProcedureScript procedure)
        {
            StartInternal(
                entity,
                animation,
                seated: false,
                lying: true,
                procedure: procedure);
        }

        private static void StartInternal(
            Entity entity,
            AnimModelComponent animation,
            bool seated,
            bool lying,
            ProcedureScript procedure)
        {
            if (entity == null ||
                animation == null ||
                lying && procedure == null ||
                !EnsureAssets(
                    seated,
                    lying))
            {
                return;
            }

            Stop(entity);

            EffectState state =
                new EffectState();
            state.Animation = animation;
            state.Seated = seated;
            state.Lying = lying;
            state.Procedure = procedure;

            ActiveEffects[entity] = state;

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Observed(
                    "AnimatePatientClothingChanges",
                    "ClothingChangeEffectRenderer.StartInternal",
                    "effect-state-active",
                    "entity=" + HfxDiagnostics.EntityLabel(entity) +
                    " profile=" +
                    (lying ? "lying" : (seated ? "seated" : "standing")));
            }
        }

        internal static void StartStandalone(
            Entity entity,
            AnimModelComponent animation)
        {
            if (entity == null ||
                animation == null ||
                !EnsureAssets(
                    seated: false,
                    lying: false))
            {
                return;
            }

            Stop(entity);

            EffectState state =
                new EffectState();
            state.Animation = animation;
            state.Standalone = true;
            state.StandaloneStartTime = Time.time;

            ActiveEffects[entity] = state;

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Observed(
                    "AnimateHshStaffClothingChanges",
                    "ClothingChangeEffectRenderer.StartStandalone",
                    "effect-state-active",
                    "entity=" + HfxDiagnostics.EntityLabel(entity) +
                    " profile=staff-standing");
            }
        }

        internal static void Stop(Entity entity)
        {
            if (entity == null)
            {
                return;
            }

            EffectState state;
            if (!ActiveEffects.TryGetValue(
                    entity,
                    out state))
            {
                return;
            }

            ActiveEffects.Remove(entity);
            DestroyState(state);
        }

        internal static void ClearAll(bool destroyAssets)
        {
            if (ActiveEffects.Count != 0)
            {
                List<EffectState> states =
                    new List<EffectState>(
                        ActiveEffects.Values);

                ActiveEffects.Clear();

                foreach (EffectState state in states)
                {
                    DestroyState(state);
                }
            }

            if (destroyAssets)
            {
                DestroyTextures(MainTextures);
                DestroyTextures(AccentTextures);
                DestroyTextures(SeatedMainTextures);
                DestroyTextures(SeatedAccentTextures);
                DestroyTextures(LyingMainTextures);
                DestroyTextures(LyingAccentTextures);
                StandingAssetsLoadAttempted = false;
                SeatedAssetsLoadAttempted = false;
                LyingAssetsLoadAttempted = false;
                StandingAssetsAvailable = false;
                SeatedAssetsAvailable = false;
                LyingAssetsAvailable = false;
            }
        }

        private static bool UpdateEffect(
            EffectState state,
            GameObject characterRenderer)
        {
            if (state == null ||
                state.Animation == null ||
                characterRenderer == null)
            {
                return true;
            }

            if (!EnsureLayers(
                    state,
                    characterRenderer))
            {
                HideLayer(state.MainLayer);
                HideLayer(state.AccentLayer);
                return true;
            }

            if (state.MainLayer == null ||
                state.AccentLayer == null)
            {
                return true;
            }

            float time;
            if (state.Standalone)
            {
                float elapsed =
                    Time.time - state.StandaloneStartTime;

                if (elapsed < 0f)
                {
                    elapsed = 0f;
                }

                if (elapsed >= StandaloneEffectDuration)
                {
                    HideLayer(state.MainLayer);
                    HideLayer(state.AccentLayer);

                    if (HfxDiagnostics.Enabled)
                    {
                        HfxDiagnostics.Result(
                            "AnimateHshStaffClothingChanges",
                            "ClothingChangeEffectRenderer.UpdateEffect",
                            "effect-duration-completed",
                            "profile=staff-standing" +
                            " elapsed=" + elapsed);
                    }
                    return false;
                }

                time =
                    MainStartTime + elapsed;
            }
            else if (state.Lying)
            {
                if (state.Procedure == null ||
                    state.Procedure.m_stateData == null)
                {
                    return false;
                }

                // Native lying idle clips are normally one-second loops.
                // Use the real procedure dress-state timer instead so the
                // overlay keeps the same 2.40-3.00 s window as standing and
                // seated patients without replacing the lying animation.
                time =
                    state.Procedure.m_stateData.m_timeInState;
            }
            else
            {
                float duration =
                    state.Animation.GetCurrentAnimationDuration();
                time =
                    state.Animation.GetCurrentAnimationPlaybackPosition();

                if (time < 0f ||
                    Math.Abs(
                        duration -
                        ClothingChangeAnimationFactory.AnimationLength) >
                        0.05f)
                {
                    HideLayer(state.MainLayer);
                    HideLayer(state.AccentLayer);
                    return true;
                }
            }

            bool mirrored =
                state.Animation.IsMirrored();

            EffectCalibration mirrorCalibration;
            if (TryGetCalibration(
                    state,
                    state.Animation.GetRotatedDirection(),
                    out mirrorCalibration) &&
                mirrorCalibration.OverrideMirroring)
            {
                mirrored = mirrorCalibration.Mirrored;
            }

            if (!state.MirrorInitialized ||
                state.Mirrored != mirrored)
            {
                state.MirrorInitialized = true;
                state.Mirrored = mirrored;
                ApplyUv(
                    state.MainLayer,
                    mirrored);
                ApplyUv(
                    state.AccentLayer,
                    mirrored);
            }

            int mainFrame =
                GetMainFrame(time);
            int accentFrame =
                GetAccentFrame(time);

            Texture2D[] mainTextures =
                state.Lying
                    ? LyingMainTextures
                    : (state.Seated
                        ? SeatedMainTextures
                        : MainTextures);
            Texture2D[] accentTextures =
                state.Lying
                    ? LyingAccentTextures
                    : (state.Seated
                        ? SeatedAccentTextures
                        : AccentTextures);

            SetFrame(
                state.MainLayer,
                mainTextures,
                mainFrame,
                ref state.MainFrame);

            SetFrame(
                state.AccentLayer,
                accentTextures,
                accentFrame,
                ref state.AccentFrame);

            return true;
        }

        private static int GetMainFrame(float time)
        {
            if (time < MainStartTime ||
                time >= EffectEndTime)
            {
                return -1;
            }

            int frame =
                (int)((time - MainStartTime) /
                    MainFrameDuration);

            if (frame < 0)
            {
                return -1;
            }

            if (frame >= MainTextures.Length)
            {
                frame = MainTextures.Length - 1;
            }

            return frame;
        }

        private static int GetAccentFrame(float time)
        {
            if (time < AccentFrame1Time ||
                time >= EffectEndTime)
            {
                return -1;
            }

            if (time < AccentFrame2Time)
            {
                return 0;
            }

            if (time < AccentFrame3Time)
            {
                return 1;
            }

            if (time < AccentFrame4Time)
            {
                return 2;
            }

            return 3;
        }

        private static bool EnsureLayers(
            EffectState state,
            GameObject characterRenderer)
        {
            Direction direction =
                state.Animation == null
                    ? Direction.None
                    : state.Animation.GetRotatedDirection();

            EffectCalibration calibration;
            if (!TryGetCalibration(
                    state,
                    direction,
                    out calibration))
            {
                return false;
            }

            if (state.MainLayer == null)
            {
                state.MainLayer =
                    CreateLayer(
                        "HFX_ClothingChange_Main",
                        characterRenderer,
                        MainLocalZ,
                        calibration);
            }

            if (state.AccentLayer == null)
            {
                state.AccentLayer =
                    CreateLayer(
                        "HFX_ClothingChange_Accent",
                        characterRenderer,
                        AccentLocalZ,
                        calibration);
            }

            bool parentChanged =
                state.Parent != characterRenderer;

            if (parentChanged)
            {
                state.Parent = characterRenderer;
                Reparent(
                    state.MainLayer,
                    characterRenderer);
                Reparent(
                    state.AccentLayer,
                    characterRenderer);
            }

            if (parentChanged ||
                !state.CalibrationInitialized ||
                state.CalibrationDirection != direction)
            {
                ApplyLayerCalibration(
                    state.MainLayer,
                    MainLocalZ,
                    calibration);
                ApplyLayerCalibration(
                    state.AccentLayer,
                    AccentLocalZ,
                    calibration);

                state.CalibrationInitialized = true;
                state.CalibrationDirection = direction;
            }

            return true;
        }

        private static bool TryGetCalibration(
            EffectState state,
            Direction direction,
            out EffectCalibration calibration)
        {
            calibration = new EffectCalibration();

            if (state != null &&
                state.Seated)
            {
                return TryGetSeatedCalibration(
                    direction,
                    out calibration);
            }

            if (state != null &&
                state.Lying)
            {
                return TryGetLyingCalibration(
                    direction,
                    out calibration);
            }

            // Standing patients and standalone HSH staff share the same
            // standing artwork, so both use the same directional calibration.
            return TryGetStandingCalibration(
                direction,
                out calibration);
        }

        private static bool TryGetLyingCalibration(
            Direction direction,
            out EffectCalibration calibration)
        {
            if (direction == Direction.LyingNE)
            {
                calibration =
                    new EffectCalibration(
                        LyingNeWidth,
                        LyingNeHeight,
                        LyingNeXOffset,
                        LyingNeYOffset,
                        LyingNeZOffset,
                        GetLocalRotationZ(direction) +
                            LyingNeRotationDeltaDegrees,
                        false,
                        true,
                        false);
                return true;
            }

            if (direction == Direction.LyingNW)
            {
                calibration =
                    new EffectCalibration(
                        LyingNwWidth,
                        LyingNwHeight,
                        LyingNwXOffset,
                        LyingNwYOffset,
                        LyingNwZOffset,
                        GetLocalRotationZ(direction) +
                            LyingNwRotationDeltaDegrees,
                        true,
                        true,
                        true);
                return true;
            }

            if (direction == Direction.LyingSW)
            {
                calibration =
                    new EffectCalibration(
                        LyingSwWidth,
                        LyingSwHeight,
                        LyingSwXOffset,
                        LyingSwYOffset,
                        LyingSwZOffset,
                        GetLocalRotationZ(direction) +
                            LyingSwRotationDeltaDegrees,
                        true,
                        true,
                        false);
                return true;
            }

            if (direction == Direction.LyingSE)
            {
                calibration =
                    new EffectCalibration(
                        LyingSeWidth,
                        LyingSeHeight,
                        LyingSeXOffset,
                        LyingSeYOffset,
                        LyingSeZOffset,
                        GetLocalRotationZ(direction) +
                            LyingSeRotationDeltaDegrees,
                        true,
                        true,
                        true);
                return true;
            }

            calibration = new EffectCalibration();
            return false;
        }
        private static bool TryGetStandingCalibration(
            Direction direction,
            out EffectCalibration calibration)
        {
            if (direction == Direction.NE)
            {
                calibration =
                    new EffectCalibration(
                        StandingNeWidth,
                        StandingNeHeight,
                        StandingNeXOffset,
                        StandingNeYOffset,
                        0f,
                        0f,
                        false,
                        false,
                        false);
                return true;
            }

            if (direction == Direction.NW)
            {
                calibration =
                    new EffectCalibration(
                        StandingNwWidth,
                        StandingNwHeight,
                        StandingNwXOffset,
                        StandingNwYOffset,
                        0f,
                        0f,
                        false,
                        false,
                        false);
                return true;
            }

            if (direction == Direction.SW)
            {
                calibration =
                    new EffectCalibration(
                        StandingSwWidth,
                        StandingSwHeight,
                        StandingSwXOffset,
                        StandingSwYOffset,
                        0f,
                        0f,
                        false,
                        false,
                        false);
                return true;
            }

            if (direction == Direction.SE)
            {
                calibration =
                    new EffectCalibration(
                        StandingSeWidth,
                        StandingSeHeight,
                        StandingSeXOffset,
                        StandingSeYOffset,
                        0f,
                        0f,
                        false,
                        false,
                        false);
                return true;
            }

            calibration = new EffectCalibration();
            return false;
        }

        private static bool TryGetSeatedCalibration(
            Direction direction,
            out EffectCalibration calibration)
        {
            if (direction == Direction.NE)
            {
                calibration =
                    new EffectCalibration(
                        SeatedNeWidth,
                        SeatedNeHeight,
                        SeatedNeXOffset,
                        SeatedNeYOffset,
                        0f,
                        0f,
                        false,
                        false,
                        false);
                return true;
            }

            if (direction == Direction.NW)
            {
                calibration =
                    new EffectCalibration(
                        SeatedNwWidth,
                        SeatedNwHeight,
                        SeatedNwXOffset,
                        SeatedNwYOffset,
                        0f,
                        0f,
                        false,
                        false,
                        false);
                return true;
            }

            if (direction == Direction.SW)
            {
                calibration =
                    new EffectCalibration(
                        SeatedSwWidth,
                        SeatedSwHeight,
                        SeatedSwXOffset,
                        SeatedSwYOffset,
                        0f,
                        0f,
                        false,
                        false,
                        false);
                return true;
            }

            if (direction == Direction.SE)
            {
                calibration =
                    new EffectCalibration(
                        SeatedSeWidth,
                        SeatedSeHeight,
                        SeatedSeXOffset,
                        SeatedSeYOffset,
                        0f,
                        0f,
                        false,
                        false,
                        false);
                return true;
            }

            calibration = new EffectCalibration();
            return false;
        }

        private static RendererObject CreateLayer(
            string name,
            GameObject parent,
            float localZ,
            EffectCalibration calibration)
        {
            RendererObject layer =
                new RendererObject(
                    name,
                    "Materials/walls",
                    null,
                    1,
                    parent);

            if (layer == null ||
                layer.m_gameObject == null ||
                layer.m_meshData == null)
            {
                return null;
            }

            ApplyUv(
                layer,
                mirrored: false);

            ApplyLayerCalibration(
                layer,
                localZ,
                calibration);

            layer.m_gameObject.SetActive(false);
            return layer;
        }

        private static void Reparent(
            RendererObject layer,
            GameObject parent)
        {
            if (layer == null ||
                layer.m_gameObject == null ||
                parent == null)
            {
                return;
            }

            layer.m_gameObject.transform.SetParent(
                parent.transform);
        }

        private static void ApplyLayerCalibration(
            RendererObject layer,
            float localZ,
            EffectCalibration calibration)
        {
            if (layer == null ||
                layer.m_gameObject == null ||
                layer.m_meshData == null)
            {
                return;
            }

            float halfWidth =
                calibration.Width / 2f;
            float bottomY =
                calibration.CenterPivot
                    ? -(calibration.Height / 2f)
                    : 0f;
            float topY =
                calibration.CenterPivot
                    ? calibration.Height / 2f
                    : calibration.Height;

            layer.m_meshData.m_vertices[0] =
                new Vector3(
                    -halfWidth,
                    topY,
                    0f);
            layer.m_meshData.m_vertices[1] =
                new Vector3(
                    halfWidth,
                    topY,
                    0f);
            layer.m_meshData.m_vertices[2] =
                new Vector3(
                    -halfWidth,
                    bottomY,
                    0f);
            layer.m_meshData.m_vertices[3] =
                new Vector3(
                    halfWidth,
                    bottomY,
                    0f);

            layer.m_meshData.m_indices[0] = 0;
            layer.m_meshData.m_indices[1] = 1;
            layer.m_meshData.m_indices[2] = 2;
            layer.m_meshData.m_indices[3] = 2;
            layer.m_meshData.m_indices[4] = 1;
            layer.m_meshData.m_indices[5] = 3;

            layer.m_gameObject.transform.localPosition =
                new Vector3(
                    calibration.XOffset,
                    calibration.YOffset,
                    localZ + calibration.ZOffset);
            layer.m_gameObject.transform.localRotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    calibration.RotationZ);

            layer.UpdateMesh();
        }

        private static float GetLocalRotationZ(
            Direction direction)
        {
            float nativeRotationRadians;
            if (direction == Direction.LyingNE ||
                direction == Direction.LyingNW)
            {
                nativeRotationRadians =
                    LyingBodyRotationNeRadians;
            }
            else if (direction == Direction.LyingSW ||
                direction == Direction.LyingSE)
            {
                nativeRotationRadians =
                    LyingBodyRotationSwRadians;
            }
            else
            {
                return 0f;
            }

            // GLib's positive 2D rotation uses x'=x*cos+y*sin and
            // y'=-x*sin+y*cos, the opposite sign of Unity's positive
            // Quaternion.Euler Z rotation.
            return -nativeRotationRadians *
                RadiansToDegrees;
        }

        private static void ApplyUv(
            RendererObject layer,
            bool mirrored)
        {
            if (layer == null ||
                layer.m_meshData == null)
            {
                return;
            }

            float left =
                mirrored ? 1f : 0f;
            float right =
                mirrored ? 0f : 1f;

            layer.m_meshData.m_texCoords[0] =
                new Vector2(left, 1f);
            layer.m_meshData.m_texCoords[1] =
                new Vector2(right, 1f);
            layer.m_meshData.m_texCoords[2] =
                new Vector2(left, 0f);
            layer.m_meshData.m_texCoords[3] =
                new Vector2(right, 0f);

            layer.UpdateMesh();
        }

        private static void SetFrame(
            RendererObject layer,
            Texture2D[] textures,
            int frame,
            ref int currentFrame)
        {
            if (layer == null ||
                layer.m_gameObject == null)
            {
                return;
            }

            if (frame < 0 ||
                textures == null ||
                frame >= textures.Length ||
                textures[frame] == null)
            {
                currentFrame = -1;
                layer.m_gameObject.SetActive(false);
                return;
            }

            if (currentFrame != frame)
            {
                Renderer renderer =
                    layer.m_gameObject.GetComponent<Renderer>();

                if (renderer != null)
                {
                    renderer.material.mainTexture =
                        textures[frame];
                }

                currentFrame = frame;
            }

            if (!layer.m_gameObject.activeSelf)
            {
                layer.m_gameObject.SetActive(true);
            }
        }

        private static void HideLayer(
            RendererObject layer)
        {
            if (layer != null &&
                layer.m_gameObject != null)
            {
                layer.m_gameObject.SetActive(false);
            }
        }

        private static bool EnsureAssets(
            bool seated,
            bool lying)
        {
            string assetDirectory =
                GetAssetDirectory();

            if (string.IsNullOrEmpty(assetDirectory))
            {
                return false;
            }

            if (lying)
            {
                if (!LyingAssetsLoadAttempted)
                {
                    LyingAssetsLoadAttempted = true;
                    LyingAssetsAvailable =
                        LoadTextureProfile(
                            assetDirectory,
                            LyingMainFiles,
                            LyingMainTextures,
                            LyingAccentFiles,
                            LyingAccentTextures);
                }

                return LyingAssetsAvailable;
            }

            if (seated)
            {
                if (!SeatedAssetsLoadAttempted)
                {
                    SeatedAssetsLoadAttempted = true;
                    SeatedAssetsAvailable =
                        LoadTextureProfile(
                            assetDirectory,
                            SeatedMainFiles,
                            SeatedMainTextures,
                            SeatedAccentFiles,
                            SeatedAccentTextures);
                }

                return SeatedAssetsAvailable;
            }

            if (!StandingAssetsLoadAttempted)
            {
                StandingAssetsLoadAttempted = true;
                StandingAssetsAvailable =
                    LoadTextureProfile(
                        assetDirectory,
                        MainFiles,
                        MainTextures,
                        AccentFiles,
                        AccentTextures);
            }

            return StandingAssetsAvailable;
        }

        private static string GetAssetDirectory()
        {
            string assemblyPath =
                Assembly.GetExecutingAssembly().Location;
            string pluginDirectory =
                Path.GetDirectoryName(assemblyPath);

            if (string.IsNullOrEmpty(pluginDirectory))
            {
                Plugin.Log.LogError(
                    "[ClothingChange] Plugin directory could not be resolved.");
                return null;
            }

            return Path.Combine(
                pluginDirectory,
                Path.Combine(
                    "AnimatedModels",
                    "Textures"));
        }

        private static bool LoadTextureProfile(
            string directory,
            string[] mainFiles,
            Texture2D[] mainTextures,
            string[] accentFiles,
            Texture2D[] accentTextures)
        {
            bool available =
                LoadTextures(
                    directory,
                    mainFiles,
                    mainTextures) &&
                LoadTextures(
                    directory,
                    accentFiles,
                    accentTextures);

            if (!available)
            {
                DestroyTextures(mainTextures);
                DestroyTextures(accentTextures);
            }

            return available;
        }

        private static bool LoadTextures(
            string directory,
            string[] files,
            Texture2D[] destination)
        {
            for (int i = 0;
                i < files.Length;
                i++)
            {
                string path =
                    Path.Combine(
                        directory,
                        files[i]);

                if (!File.Exists(path))
                {
                    Plugin.Log.LogError(
                        "[ClothingChange] Missing effect asset: " +
                        files[i]);
                    return false;
                }

                Texture2D texture =
                    LoadTexture(path);

                if (texture == null)
                {
                    return false;
                }

                if (texture.width != ExpectedTextureSize ||
                    texture.height != ExpectedTextureSize)
                {
                    Plugin.Log.LogError(
                        "[ClothingChange] Effect asset must be 128x128: " +
                        files[i] +
                        " (" +
                        texture.width +
                        "x" +
                        texture.height +
                        ").");

                    UnityEngine.Object.Destroy(texture);
                    return false;
                }

                texture.filterMode =
                    FilterMode.Bilinear;
                texture.wrapMode =
                    TextureWrapMode.Clamp;
                texture.Apply(
                    updateMipmaps: false,
                    makeNoLongerReadable: true);

                destination[i] = texture;
            }

            return true;
        }

        private static Texture2D LoadTexture(
            string path)
        {
            Texture2D texture =
                new Texture2D(
                    2,
                    2,
                    TextureFormat.ARGB32,
                    mipChain: false);

            WWW loader = null;

            try
            {
                loader =
                    new WWW(
                        new Uri(path).AbsoluteUri);
                loader.LoadImageIntoTexture(texture);
                return texture;
            }
            catch (Exception exception)
            {
                UnityEngine.Object.Destroy(texture);

                Plugin.Log.LogError(
                    "[ClothingChange] PNG load failed: " +
                    exception.Message);

                return null;
            }
            finally
            {
                if (loader != null)
                {
                    loader.Dispose();
                }
            }
        }

        private static void DestroyState(
            EffectState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.MainLayer != null)
            {
                state.MainLayer.Destroy();
            }

            if (state.AccentLayer != null)
            {
                state.AccentLayer.Destroy();
            }

            state.MainLayer = null;
            state.AccentLayer = null;
            state.Parent = null;
            state.Animation = null;
            state.Procedure = null;
        }

        private static void DestroyTextures(
            Texture2D[] textures)
        {
            for (int i = 0;
                i < textures.Length;
                i++)
            {
                if (textures[i] != null)
                {
                    UnityEngine.Object.Destroy(
                        textures[i]);
                    textures[i] = null;
                }
            }
        }
    }
}
