using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalPorters
{
    // HPO's occupied-room wait image is a complete 64x64 speech bubble, not a center icon.
    // SpeechComponent normally draws a vanilla bubble behind a smaller custom
    // icon. Keep CharacterRenderer's native positioning and lifecycle, but
    // render this exact asset at the same footprint as the vanilla bubble.
    [HarmonyPatch(
        typeof(CharacterRenderer),
        "UpdateSpeechBubble",
        new Type[] { typeof(Entity), typeof(LightMap2DUnity) })]
    internal static class PorterFullSpeechBubblePatch
    {
        private const float VanillaBubbleScale = 0.333f;
        private const string NativeIconAtlasAsset = "ASSET_TEX_UI_ICONS";

        private static readonly FieldInfo SpeechBubblesField =
            AccessTools.Field(
                typeof(CharacterRenderer),
                "m_characterSpeechBubbles");

        private static readonly FieldInfo SpeechBubbleIconsField =
            AccessTools.Field(
                typeof(CharacterRenderer),
                "m_characterSpeechBubbleIcons");

        // Contains only characters whose icon renderer was actually switched
        // to the HPO full-bubble texture. It is render state, not gameplay
        // state, and is cleared whenever the base game database reloads.
        private static readonly HashSet<Entity> ModifiedCharacters =
            new HashSet<Entity>();

        internal static void Reset()
        {
            ModifiedCharacters.Clear();
        }

        private static void Prefix(
            CharacterRenderer __instance,
            Entity character)
        {
            if (character == null ||
                !ModifiedCharacters.Contains(character) ||
                PorterSampleTransportRuntime
                    .IsShowingSampleRoomWaitingBubble(character))
            {
                return;
            }

            RestoreNativeIconTextureIfNeeded(
                __instance,
                character);
        }

        private static void Postfix(
            CharacterRenderer __instance,
            Entity character)
        {
            if (!PorterSampleTransportRuntime
                    .IsShowingSampleRoomWaitingBubble(character))
            {
                return;
            }

            string assetId;
            if (!TryGetFullBubbleAsset(character, out assetId))
            {
                return;
            }

            Dictionary<Entity, RendererObject> bubbles =
                GetRendererMap(__instance, SpeechBubblesField);
            Dictionary<Entity, RendererObject> icons =
                GetRendererMap(__instance, SpeechBubbleIconsField);
            if (bubbles == null || icons == null)
            {
                return;
            }

            RendererObject bubble;
            RendererObject icon;
            if (!bubbles.TryGetValue(character, out bubble) ||
                !icons.TryGetValue(character, out icon) ||
                bubble == null ||
                icon == null ||
                bubble.m_gameObject == null ||
                icon.m_gameObject == null ||
                icon.m_meshData == null)
            {
                return;
            }

            Texture texture = GetTexture(assetId);
            Renderer iconRenderer =
                icon.m_gameObject.GetComponent<Renderer>();
            if (texture == null || iconRenderer == null)
            {
                return;
            }

            iconRenderer.material.mainTexture = texture;
            iconRenderer.material.SetColor("_TintColor", Color.white);

            Vector3[] vertices = icon.m_meshData.m_vertices;
            if (vertices == null || vertices.Length < 4)
            {
                return;
            }

            vertices[0] =
                (-Vector3.right + Vector3.up) * VanillaBubbleScale;
            vertices[1] =
                (Vector3.right + Vector3.up) * VanillaBubbleScale;
            vertices[2] =
                (-Vector3.right - Vector3.up) * VanillaBubbleScale;
            vertices[3] =
                (Vector3.right - Vector3.up) * VanillaBubbleScale;

            Vector2[] texCoords = icon.m_meshData.m_texCoords;
            if (texCoords != null && texCoords.Length >= 4)
            {
                texCoords[0] = new Vector2(0f, 1f);
                texCoords[1] = new Vector2(1f, 1f);
                texCoords[2] = new Vector2(0f, 0f);
                texCoords[3] = new Vector2(1f, 0f);
            }

            icon.m_gameObject.transform.localPosition =
                bubble.m_gameObject.transform.localPosition;
            icon.UpdateMesh();

            // Native CharacterRenderer still owns creation, positioning and
            // visibility. Only the exact HPO occupied-room full-bubble composition changes.
            bubble.m_gameObject.SetActive(false);
            icon.m_gameObject.SetActive(true);
            ModifiedCharacters.Add(character);
        }

        private static bool TryGetFullBubbleAsset(
            Entity character,
            out string assetId)
        {
            assetId = null;
            SpeechComponent speech =
                character == null
                    ? null
                    : character.GetComponent<SpeechComponent>();
            if (speech == null ||
                speech.GetBubbleTextureIndex() < 0)
            {
                return false;
            }

            string current = speech.GetBubbleIconAssetID();
            if (!string.Equals(
                    current,
                    PorterVisuals.SampleRoomOccupiedBubbleAsset,
                    StringComparison.Ordinal))
            {
                return false;
            }

            assetId = current;
            return true;
        }

        private static void RestoreNativeIconTextureIfNeeded(
            CharacterRenderer renderer,
            Entity character)
        {
            if (renderer == null ||
                character == null)
            {
                return;
            }

            Dictionary<Entity, RendererObject> icons =
                GetRendererMap(renderer, SpeechBubbleIconsField);
            RendererObject icon;
            if (icons == null ||
                !icons.TryGetValue(character, out icon) ||
                icon == null ||
                icon.m_gameObject == null)
            {
                return;
            }

            Renderer iconRenderer =
                icon.m_gameObject.GetComponent<Renderer>();
            if (iconRenderer == null)
            {
                return;
            }

            if (!IsHpoFullBubbleTexture(
                    iconRenderer.material.mainTexture))
            {
                ModifiedCharacters.Remove(character);
                return;
            }

            Texture nativeIcons = GetTexture(NativeIconAtlasAsset);
            if (nativeIcons == null)
            {
                return;
            }

            iconRenderer.material.mainTexture = nativeIcons;
            if (UISettings.Instance != null)
            {
                iconRenderer.material.SetColor(
                    "_TintColor",
                    UISettings.Instance.SYMPTOM_COLOR_DARK_GRAY);
            }

            ModifiedCharacters.Remove(character);
        }

        private static bool IsHpoFullBubbleTexture(Texture texture)
        {
            if (texture == null)
            {
                return false;
            }

            return object.ReferenceEquals(
                texture,
                GetTexture(
                    PorterVisuals.SampleRoomOccupiedBubbleAsset));
        }

        private static Texture GetTexture(string assetId)
        {
            StreamingAssetManager assets =
                StreamingAssetManager.GetInstance();
            if (assets == null ||
                assets.m_textureAssets == null ||
                string.IsNullOrEmpty(assetId))
            {
                return null;
            }

            Texture texture;
            return assets.m_textureAssets.TryGetValue(
                    assetId,
                    out texture)
                ? texture
                : null;
        }

        private static Dictionary<Entity, RendererObject> GetRendererMap(
            CharacterRenderer renderer,
            FieldInfo field)
        {
            return renderer == null ||
                   object.ReferenceEquals(field, null)
                ? null
                : field.GetValue(renderer)
                    as Dictionary<Entity, RendererObject>;
        }
    }
}
