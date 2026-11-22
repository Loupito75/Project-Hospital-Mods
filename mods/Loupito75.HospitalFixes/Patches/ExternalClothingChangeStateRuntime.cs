using System;
using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalFixes.Patches
{
    internal static class ExternalClothingChangeStateRuntime
    {
        // HFX owns the visual timing. Producers publish only the semantic
        // clothing-change state and never need to know this lead time.
        internal const float ExternalEffectLeadTime = 0.30f;

        private sealed class ChangeState
        {
            internal readonly HashSet<string> Sources =
                new HashSet<string>(
                    StringComparer.Ordinal);

            internal Clothes ClothesBefore;
            internal bool EffectStarted;
        }

        private static readonly Dictionary<Entity, ChangeState>
            States =
                new Dictionary<Entity, ChangeState>();

        private static readonly List<Entity>
            RefreshBuffer =
                new List<Entity>();

        private static bool RuntimeActive;

        internal static void SetRuntimeActive(
            bool active)
        {
            RuntimeActive = active;

            if (!active)
            {
                ClearAll();
            }
        }

        internal static bool TrySetChangingState(
            Entity character,
            string sourceId,
            bool changing)
        {
            if (character == null ||
                string.IsNullOrEmpty(sourceId))
            {
                return false;
            }

            string normalizedSourceId =
                sourceId.Trim();

            if (normalizedSourceId.Length == 0)
            {
                return false;
            }

            if (!changing)
            {
                return ClearSource(
                    character,
                    normalizedSourceId);
            }

            if (!RuntimeActive ||
                !FixConfig.ExternalClothingChangeVisualsEnabled ||
                character.DEBUG_destroyed)
            {
                return false;
            }

            AnimModelComponent animation =
                character.GetComponent<AnimModelComponent>();

            if (animation == null ||
                animation.m_state == null)
            {
                return false;
            }

            ChangeState state;
            if (!States.TryGetValue(
                    character,
                    out state))
            {
                state =
                    new ChangeState();
                state.ClothesBefore =
                    animation.m_state.m_clothes;

                States.Add(
                    character,
                    state);
            }

            bool added =
                state.Sources.Add(
                    normalizedSourceId);

            if (added &&
                HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Result(
                    "ExternalClothingChanges",
                    "external clothing-change source",
                    "registered",
                    "character={" +
                    HfxDiagnostics.EntityContext(character) +
                    "} sourceId=" +
                    normalizedSourceId +
                    " sourceCount=" +
                    state.Sources.Count);
            }

            return true;
        }

        internal static bool HasChangingState(
            Entity character)
        {
            if (character == null)
            {
                return false;
            }

            ChangeState state;
            return States.TryGetValue(
                    character,
                    out state) &&
                state != null &&
                state.Sources.Count > 0;
        }

        internal static void EmergencyClearChangingState(
            Entity character,
            string sourceId)
        {
            if (character == null ||
                string.IsNullOrEmpty(sourceId))
            {
                return;
            }

            string normalizedSourceId =
                sourceId.Trim();

            if (normalizedSourceId.Length == 0)
            {
                return;
            }

            ChangeState state;
            if (!States.TryGetValue(
                    character,
                    out state) ||
                state == null)
            {
                return;
            }

            state.Sources.Remove(
                normalizedSourceId);

            if (state.Sources.Count != 0)
            {
                return;
            }

            States.Remove(
                character);

            if (state.EffectStarted)
            {
                ClothingChangeEffectRenderer.Stop(
                    character);
            }
        }

        internal static void RefreshAll()
        {
            if (!RuntimeActive ||
                States.Count == 0)
            {
                return;
            }

            RefreshBuffer.Clear();

            foreach (KeyValuePair<Entity, ChangeState> pair
                     in States)
            {
                RefreshBuffer.Add(
                    pair.Key);
            }

            for (int i = 0;
                 i < RefreshBuffer.Count;
                 i++)
            {
                Entity character =
                    RefreshBuffer[i];

                ChangeState state;
                if (character == null ||
                    !States.TryGetValue(
                        character,
                        out state) ||
                    state == null)
                {
                    continue;
                }

                if (character.DEBUG_destroyed)
                {
                    States.Remove(
                        character);

                    if (state.EffectStarted)
                    {
                        ClothingChangeEffectRenderer.Stop(
                            character);
                    }

                    continue;
                }

                if (state.EffectStarted)
                {
                    continue;
                }

                TryStartEffect(
                    character,
                    state);
            }

            RefreshBuffer.Clear();
        }

        internal static void ClearEntity(
            Entity character)
        {
            if (character == null)
            {
                return;
            }

            ChangeState state;
            if (!States.TryGetValue(
                    character,
                    out state))
            {
                return;
            }

            States.Remove(
                character);

            if (state != null &&
                state.EffectStarted)
            {
                ClothingChangeEffectRenderer.Stop(
                    character);
            }
        }

        internal static void ClearAll()
        {
            if (States.Count != 0)
            {
                foreach (KeyValuePair<Entity, ChangeState> pair
                         in States)
                {
                    if (pair.Key != null &&
                        pair.Value != null &&
                        pair.Value.EffectStarted)
                    {
                        ClothingChangeEffectRenderer.Stop(
                            pair.Key);
                    }
                }

                States.Clear();
            }

            RefreshBuffer.Clear();
        }

        private static bool ClearSource(
            Entity character,
            string sourceId)
        {
            ChangeState state;
            if (!States.TryGetValue(
                    character,
                    out state) ||
                state == null)
            {
                return true;
            }

            bool removed =
                state.Sources.Remove(
                    sourceId);

            if (state.Sources.Count != 0)
            {
                return true;
            }

            States.Remove(
                character);

            bool clothesChanged =
                DidClothesChange(
                    character,
                    state);

            if (state.EffectStarted)
            {
                if (!clothesChanged)
                {
                    ClothingChangeEffectRenderer.Stop(
                        character);
                }
            }
            else if (clothesChanged &&
                     !character.DEBUG_destroyed)
            {
                // A low frame rate or a very short use animation may skip
                // the preferred lead window entirely. Once the producer
                // confirms the sequence ended and HFX observes a real clothes
                // replacement, start the same visual at confirmation time
                // rather than silently losing the optional effect.
                AnimModelComponent animation =
                    character.GetComponent<AnimModelComponent>();

                ClothingChangeEffectRenderer
                    .TryStartStandalone(
                        character,
                        animation);
            }

            if (removed &&
                HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Result(
                    "ExternalClothingChanges",
                    "external clothing-change source",
                    clothesChanged
                        ? "cleared-after-clothing-change"
                        : "cleared-without-clothing-change",
                    "character={" +
                    HfxDiagnostics.EntityContext(character) +
                    "} sourceId=" +
                    sourceId);
            }

            return true;
        }

        private static void TryStartEffect(
            Entity character,
            ChangeState state)
        {
            if (character == null ||
                state == null ||
                state.EffectStarted)
            {
                return;
            }

            WalkComponent walk =
                character.GetComponent<WalkComponent>();

            if ((walk != null &&
                 walk.IsOnBiohazard()) ||
                (WorldEventManager.Instance != null &&
                 WorldEventManager.Instance
                     .HasEventForcingBiohazardClothes()))
            {
                return;
            }

            AnimModelComponent animation =
                character.GetComponent<AnimModelComponent>();

            if (animation == null ||
                animation.m_state == null)
            {
                return;
            }

            float duration =
                animation.GetCurrentAnimationDuration();
            float position =
                animation.GetCurrentAnimationPlaybackPosition();

            if (duration <= 0f ||
                position < 0f)
            {
                return;
            }

            float remaining =
                duration - position;

            if (remaining <= 0f ||
                remaining > ExternalEffectLeadTime)
            {
                return;
            }

            if (!ClothingChangeEffectRenderer
                    .TryStartStandalone(
                        character,
                        animation))
            {
                return;
            }

            state.EffectStarted = true;

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Observed(
                    "ExternalClothingChanges",
                    "ExternalClothingChangeStateRuntime.TryStartEffect",
                    "effect-started",
                    "character=" +
                    HfxDiagnostics.EntityLabel(character) +
                    " remaining=" +
                    remaining.ToString("0.000"));
            }
        }

        private static bool DidClothesChange(
            Entity character,
            ChangeState state)
        {
            if (character == null ||
                state == null)
            {
                return false;
            }

            AnimModelComponent animation =
                character.GetComponent<AnimModelComponent>();

            return animation != null &&
                animation.m_state != null &&
                !object.ReferenceEquals(
                    state.ClothesBefore,
                    animation.m_state.m_clothes);
        }
    }
}
