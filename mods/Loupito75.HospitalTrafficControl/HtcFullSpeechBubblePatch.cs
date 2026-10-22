using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalTrafficControl
{
    // HTC supplies complete 64x64 bubbles, as HPO does. Vanilla still owns
    // speech, position and visibility; this patch only expands exact HTC
    // custom assets to the native full-bubble footprint.
    [HarmonyPatch(
        typeof(CharacterRenderer),
        "UpdateSpeechBubble",
        new Type[] { typeof(Entity), typeof(LightMap2DUnity) })]
    internal static class HtcFullSpeechBubblePatch
    {
        private const string JanitorWaitAsset =
            "HTC_ASSET_JANITOR_ROOM_OCCUPIED_BUBBLE";
        private const string NativeIconAtlas = "ASSET_TEX_UI_ICONS";
        private const float NativeBubbleScale = 0.333f;
        private static readonly FieldInfo BubbleField =
            AccessTools.Field(typeof(CharacterRenderer), "m_characterSpeechBubbles");
        private static readonly FieldInfo IconField =
            AccessTools.Field(typeof(CharacterRenderer), "m_characterSpeechBubbleIcons");

        private sealed class ReplacedIcon
        {
            internal RendererObject Object;
            internal Texture PreviousTexture;
            internal Texture AppliedTexture;
            internal Color PreviousTint;
            internal Vector3 PreviousPosition;
            internal Vector3[] PreviousVertices;
            internal Vector2[] PreviousUvs;
        }

        private static readonly Dictionary<Entity, ReplacedIcon> Modified =
            new Dictionary<Entity, ReplacedIcon>();

        internal static void Reset()
        {
            Entity[] entities = new Entity[Modified.Count];
            Modified.Keys.CopyTo(entities, 0);
            foreach (Entity entity in entities)
            {
                Restore(entity);
            }
            Modified.Clear();
        }

        internal static void Restore(Entity entity)
        {
            if (entity == null)
            {
                return;
            }

            ReplacedIcon previous;
            if (!Modified.TryGetValue(entity, out previous))
            {
                return;
            }

            RendererObject icon = previous.Object;
            if (icon == null || icon.m_gameObject == null)
            {
                Modified.Remove(entity);
                return;
            }

            Renderer renderer = icon.m_gameObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                Modified.Remove(entity);
                return;
            }

            if (!object.ReferenceEquals(
                    renderer.material.mainTexture, previous.AppliedTexture))
            {
                Modified.Remove(entity);
                return;
            }

            Texture restoreTexture = previous.PreviousTexture;
            if (object.ReferenceEquals(
                    restoreTexture, previous.AppliedTexture))
            {
                restoreTexture = GetTexture(NativeIconAtlas);
            }
            if (restoreTexture == null)
            {
                return;
            }

            renderer.material.mainTexture = restoreTexture;
            renderer.material.SetColor("_TintColor", previous.PreviousTint);
            if (icon.m_meshData != null)
            {
                if (icon.m_meshData.m_vertices != null &&
                    icon.m_meshData.m_vertices.Length >= 4)
                {
                    Array.Copy(
                        previous.PreviousVertices,
                        icon.m_meshData.m_vertices, 4);
                }
                if (icon.m_meshData.m_texCoords != null &&
                    icon.m_meshData.m_texCoords.Length >= 4)
                {
                    Array.Copy(
                        previous.PreviousUvs,
                        icon.m_meshData.m_texCoords, 4);
                }
                icon.m_gameObject.transform.localPosition =
                    previous.PreviousPosition;
                icon.UpdateMesh();
            }
            SpeechComponent speech =
                entity.GetComponent<SpeechComponent>();
            if (speech == null || !speech.IsActive())
            {
                icon.m_gameObject.SetActive(false);
            }
            Modified.Remove(entity);
        }

        private static void Prefix(Entity character)
        {
            BlockedPathBubbleManager.RefreshCharacter(character);

            if (character != null &&
                Modified.ContainsKey(character) &&
                !IsShowingHtcFullBubble(character))
            {
                Restore(character);
            }
        }

        private static void Postfix(
            CharacterRenderer __instance,
            Entity character)
        {
            string assetId;
            if (__instance == null ||
                !TryGetActiveFullBubbleAsset(
                    character,
                    out assetId))
            {
                return;
            }

            Dictionary<Entity, RendererObject> bubbles =
                GetMap(__instance, BubbleField);
            Dictionary<Entity, RendererObject> icons =
                GetMap(__instance, IconField);
            RendererObject bubble;
            RendererObject icon;
            if (bubbles == null || icons == null ||
                !bubbles.TryGetValue(character, out bubble) ||
                !icons.TryGetValue(character, out icon) ||
                bubble == null || icon == null ||
                bubble.m_gameObject == null ||
                icon.m_gameObject == null ||
                icon.m_meshData == null ||
                icon.m_meshData.m_vertices == null ||
                icon.m_meshData.m_vertices.Length < 4 ||
                icon.m_meshData.m_texCoords == null ||
                icon.m_meshData.m_texCoords.Length < 4)
            {
                return;
            }

            Texture texture = GetTexture(assetId);
            Renderer renderer = icon.m_gameObject.GetComponent<Renderer>();
            if (texture == null || renderer == null)
            {
                return;
            }

            ReplacedIcon previous;
            if (!Modified.TryGetValue(character, out previous) ||
                !object.ReferenceEquals(previous.Object, icon))
            {
                if (previous != null)
                {
                    Restore(character);
                }
                previous = new ReplacedIcon();
                previous.Object = icon;
                previous.PreviousVertices = new Vector3[4];
                previous.PreviousUvs = new Vector2[4];
                Modified[character] = previous;
            }

            Material material = renderer.material;
            previous.PreviousTexture = material.mainTexture;
            previous.AppliedTexture = texture;
            previous.PreviousTint = material.GetColor("_TintColor");
            previous.PreviousPosition =
                icon.m_gameObject.transform.localPosition;
            Array.Copy(
                icon.m_meshData.m_vertices,
                previous.PreviousVertices, 4);
            Array.Copy(
                icon.m_meshData.m_texCoords,
                previous.PreviousUvs, 4);

            material.mainTexture = texture;
            material.SetColor("_TintColor", Color.white);
            Vector3[] v = icon.m_meshData.m_vertices;
            v[0] = (-Vector3.right + Vector3.up) * NativeBubbleScale;
            v[1] = (Vector3.right + Vector3.up) * NativeBubbleScale;
            v[2] = (-Vector3.right - Vector3.up) * NativeBubbleScale;
            v[3] = (Vector3.right - Vector3.up) * NativeBubbleScale;
            Vector2[] uv = icon.m_meshData.m_texCoords;
            uv[0] = new Vector2(0f, 1f);
            uv[1] = new Vector2(1f, 1f);
            uv[2] = new Vector2(0f, 0f);
            uv[3] = new Vector2(1f, 0f);
            icon.m_gameObject.transform.localPosition =
                bubble.m_gameObject.transform.localPosition;
            icon.UpdateMesh();
            bubble.m_gameObject.SetActive(false);
            icon.m_gameObject.SetActive(true);
            Modified[character] = previous;
        }

        private static bool IsShowingHtcFullBubble(Entity character)
        {
            return JanitorCleaningManager
                       .IsShowingProtectedRoomWaitingBubble(character) ||
                   BlockedPathBubbleManager
                       .IsShowingBlockedPathBubble(character);
        }

        private static bool TryGetActiveFullBubbleAsset(
            Entity character,
            out string assetId)
        {
            assetId = null;
            if (character == null)
            {
                return false;
            }

            SpeechComponent speech =
                character.GetComponent<SpeechComponent>();
            if (speech == null ||
                speech.GetBubbleTextureIndex() < 0)
            {
                return false;
            }

            string current = speech.GetBubbleIconAssetID();
            if (string.Equals(
                    current,
                    JanitorWaitAsset,
                    StringComparison.Ordinal) &&
                JanitorCleaningManager
                    .IsShowingProtectedRoomWaitingBubble(character))
            {
                assetId = current;
                return true;
            }

            if (string.Equals(
                    current,
                    BlockedPathBubbleManager.BubbleAssetId,
                    StringComparison.Ordinal) &&
                BlockedPathBubbleManager
                    .IsShowingBlockedPathBubble(character))
            {
                assetId = current;
                return true;
            }

            return false;
        }

        private static Texture GetTexture(string assetId)
        {
            StreamingAssetManager assets =
                StreamingAssetManager.GetInstance();
            Texture texture;
            return assets != null &&
                   assets.m_textureAssets != null &&
                   assets.m_textureAssets.TryGetValue(assetId, out texture)
                ? texture : null;
        }

        private static Dictionary<Entity, RendererObject> GetMap(
            CharacterRenderer renderer,
            FieldInfo field)
        {
            return renderer == null || object.ReferenceEquals(field, null)
                ? null
                : field.GetValue(renderer)
                    as Dictionary<Entity, RendererObject>;
        }
    }
}
