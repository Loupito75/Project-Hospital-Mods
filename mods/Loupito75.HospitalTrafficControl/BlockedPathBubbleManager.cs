using System;
using System.Collections.Generic;
using GLib;
using Lopital;
using UnityEngine;

namespace HospitalTrafficControl
{
    // Visual state only. Tracks characters that actually hit NoPath or are
    // physically standing on a tile forbidden by their current access rights.
    // Native speech always wins; HTC retries its own bubble only after native
    // speech has ended and removes it as soon as the blockage is resolved.
    internal static class BlockedPathBubbleManager
    {
        internal const string BubbleAssetId =
            "HTC_ASSET_PATH_BLOCKED_BUBBLE";

        private sealed class BlockedBubbleState
        {
            internal Entity Entity;
            internal bool WaitingForRoute;
            internal bool RestrictedTile;
        }

        private static readonly Dictionary<WalkComponent, BlockedBubbleState>
            Active = new Dictionary<WalkComponent, BlockedBubbleState>();

        private static readonly List<WalkComponent> Scratch =
            new List<WalkComponent>();

        internal static void OnWalkStateChanged(
            WalkComponent walk,
            WalkState state)
        {
            if (walk == null || walk.m_state == null)
            {
                return;
            }

            BlockedBubbleState entry;
            if (state == WalkState.NoPath)
            {
                entry = GetOrCreate(walk);
                if (entry == null)
                {
                    return;
                }

                entry.WaitingForRoute = true;
                Refresh(walk, entry);
                return;
            }

            if (Active.TryGetValue(walk, out entry))
            {
                Refresh(walk, entry);
            }
        }

        internal static void TrackRestricted(WalkComponent walk)
        {
            if (walk == null || walk.m_state == null ||
                walk.m_state.m_lying)
            {
                return;
            }

            BlockedBubbleState entry = GetOrCreate(walk);
            if (entry == null)
            {
                return;
            }

            entry.RestrictedTile = true;
            Refresh(walk, entry);
        }

        internal static void Update()
        {
            if (Active.Count == 0)
            {
                return;
            }

            Scratch.Clear();
            foreach (WalkComponent walk in Active.Keys)
            {
                Scratch.Add(walk);
            }

            for (int i = 0; i < Scratch.Count; i++)
            {
                WalkComponent walk = Scratch[i];
                BlockedBubbleState entry;
                if (Active.TryGetValue(walk, out entry))
                {
                    Refresh(walk, entry);
                }
            }

            Scratch.Clear();
        }

        // Called from CharacterRenderer before native rebuilds its bubble.
        // Besides keeping the renderer synchronized, this safely adopts or
        // removes a transient HTC bubble restored by a save/load.
        internal static void RefreshCharacter(Entity entity)
        {
            if (entity == null)
            {
                return;
            }

            WalkComponent walk = entity.GetComponent<WalkComponent>();
            SpeechComponent speech = entity.GetComponent<SpeechComponent>();
            bool ownBubble = IsOwnBubble(speech);

            if (walk == null || walk.m_state == null)
            {
                if (ownBubble)
                {
                    speech.HideBubble();
                    HtcFullSpeechBubblePatch.Restore(entity);
                }
                return;
            }

            WalkState walkState = walk.m_state.m_walkState;
            bool noPath = walkState == WalkState.NoPath;
            bool restricted =
                !walk.m_state.m_lying &&
                AccessZoneRecoveryManager.IsCurrentTileRestricted(walk);

            BlockedBubbleState entry;
            if (!Active.TryGetValue(walk, out entry))
            {
                if (!ownBubble && !noPath && !restricted)
                {
                    return;
                }

                entry = GetOrCreate(walk);
                if (entry == null)
                {
                    return;
                }
            }

            if (noPath ||
                (ownBubble && IsPathSearchState(walkState)))
            {
                entry.WaitingForRoute = true;
            }
            if (restricted)
            {
                entry.RestrictedTile = true;
            }

            Refresh(walk, entry);
        }

        internal static bool IsShowingBlockedPathBubble(Entity entity)
        {
            if (entity == null)
            {
                return false;
            }

            WalkComponent walk = entity.GetComponent<WalkComponent>();
            return walk != null &&
                Active.ContainsKey(walk) &&
                IsOwnBubble(entity.GetComponent<SpeechComponent>());
        }

        internal static void Reset()
        {
            if (Active.Count == 0)
            {
                Scratch.Clear();
                return;
            }

            Scratch.Clear();
            foreach (WalkComponent walk in Active.Keys)
            {
                Scratch.Add(walk);
            }

            for (int i = 0; i < Scratch.Count; i++)
            {
                WalkComponent walk = Scratch[i];
                BlockedBubbleState entry;
                if (Active.TryGetValue(walk, out entry))
                {
                    Clear(walk, entry);
                }
            }

            Active.Clear();
            Scratch.Clear();
        }

        private static BlockedBubbleState GetOrCreate(
            WalkComponent walk)
        {
            if (walk == null)
            {
                return null;
            }

            BlockedBubbleState entry;
            if (Active.TryGetValue(walk, out entry))
            {
                return entry;
            }

            Entity entity = CharacterAccess.GetEntity(walk);
            if (entity == null)
            {
                return null;
            }

            entry = new BlockedBubbleState
            {
                Entity = entity
            };
            Active.Add(walk, entry);
            return entry;
        }

        private static void Refresh(
            WalkComponent walk,
            BlockedBubbleState entry)
        {
            if (walk == null || entry == null ||
                walk.m_state == null)
            {
                Clear(walk, entry);
                return;
            }

            Entity entity =
                CharacterAccess.GetEntity(walk) ?? entry.Entity;
            if (entity == null)
            {
                Clear(walk, entry);
                return;
            }
            entry.Entity = entity;

            WalkState state = walk.m_state.m_walkState;
            bool restricted =
                !walk.m_state.m_lying &&
                AccessZoneRecoveryManager.IsCurrentTileRestricted(walk);

            // A NoPath stays visible through native/HTC replanning. It ends
            // only when a usable route starts or movement is cancelled.
            if (state == WalkState.NoPath)
            {
                entry.WaitingForRoute = true;
            }
            else if (entry.WaitingForRoute &&
                     !IsPathSearchState(state))
            {
                entry.WaitingForRoute = false;
            }

            // During access-change recovery, the visual ends as soon as the
            // character has physically crossed back onto a legal tile.
            entry.RestrictedTile = restricted;

            if (!entry.WaitingForRoute &&
                !entry.RestrictedTile)
            {
                Clear(walk, entry);
                return;
            }

            ShowIfAvailable(entity);
        }

        private static bool IsPathSearchState(WalkState state)
        {
            return state == WalkState.DestinationSet ||
                   state == WalkState.LookingForPath ||
                   state == WalkState.LookingForPathFallback ||
                   state == WalkState.LookingForPathFallback2;
        }

        private static void ShowIfAvailable(Entity entity)
        {
            SpeechComponent speech =
                entity == null
                    ? null
                    : entity.GetComponent<SpeechComponent>();
            if (speech == null || IsOwnBubble(speech))
            {
                return;
            }

            // Never replace a native/gameplay speech bubble. If native takes
            // over after ours, Update() waits for it to end and retries only
            // when the path is still blocked.
            if (speech.IsActive())
            {
                return;
            }

            StreamingAssetManager assets =
                StreamingAssetManager.GetInstance();
            Texture texture;
            if (assets == null ||
                assets.m_textureAssets == null ||
                !assets.m_textureAssets.TryGetValue(
                    BubbleAssetId, out texture) ||
                texture == null ||
                Database.Instance == null ||
                Database.Instance.GetEntry<GameDBSpeechBubble>(
                    "BUBBLE_EMPTY") == null)
            {
                return;
            }

            speech.SetBubble(
                "BUBBLE_EMPTY",
                BubbleAssetId);
        }

        private static bool IsOwnBubble(SpeechComponent speech)
        {
            return speech != null &&
                speech.GetBubbleTextureIndex() >= 0 &&
                string.Equals(
                    speech.GetBubbleIconAssetID(),
                    BubbleAssetId,
                    StringComparison.Ordinal);
        }

        private static void Clear(
            WalkComponent walk,
            BlockedBubbleState entry)
        {
            Entity entity =
                entry == null
                    ? null
                    : entry.Entity;
            if (entity != null)
            {
                SpeechComponent speech =
                    entity.GetComponent<SpeechComponent>();
                if (IsOwnBubble(speech))
                {
                    speech.HideBubble();
                }

                HtcFullSpeechBubblePatch.Restore(entity);
            }

            if (walk != null)
            {
                Active.Remove(walk);
            }
        }
    }
}
