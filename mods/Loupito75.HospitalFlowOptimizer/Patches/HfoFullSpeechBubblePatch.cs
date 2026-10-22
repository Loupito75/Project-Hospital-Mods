using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFlowOptimizer.Patches
{
    // Patient-wait PNG is a complete speech bubble, not a center icon.
    // Retain Vanilla positioning/lifecycle and use the HPO full-bubble technique.
    [HarmonyPatch(
        typeof(CharacterRenderer),
        "UpdateSpeechBubble",
        new Type[] { typeof(Entity), typeof(LightMap2DUnity) })]
    internal static class HfoFullSpeechBubblePatch
    {
        private const float VanillaBubbleScale = 0.333f;
        private const string NativeIconsAsset = "ASSET_TEX_UI_ICONS";

        private static readonly FieldInfo BubbleField =
            AccessTools.Field(typeof(CharacterRenderer), "m_characterSpeechBubbles");
        private static readonly FieldInfo IconField =
            AccessTools.Field(typeof(CharacterRenderer), "m_characterSpeechBubbleIcons");

        private static readonly HashSet<Entity> ModifiedCharacters =
            new HashSet<Entity>();

        internal static void Reset()
        {
            ModifiedCharacters.Clear();
        }

        private static void Prefix(CharacterRenderer __instance, Entity character)
        {
            if (character == null ||
                !ModifiedCharacters.Contains(character) ||
                SmartTransportWaitRuntime.IsShowingOwnWaitingBubble(character))
            {
                return;
            }

            Dictionary<Entity, RendererObject> icons = GetMap(__instance, IconField);
            RendererObject icon;
            if (icons == null || !icons.TryGetValue(character, out icon) ||
                icon == null || icon.m_gameObject == null)
            {
                return;
            }

            Renderer iconRenderer = icon.m_gameObject.GetComponent<Renderer>();
            if (iconRenderer == null)
            {
                return;
            }

            if (!object.ReferenceEquals(
                    iconRenderer.material.mainTexture,
                    GetTexture(SmartTransportWaitRuntime.WaitingBubbleAsset)))
            {
                ModifiedCharacters.Remove(character);
                return;
            }

            Texture native = GetTexture(NativeIconsAsset);
            if (native == null)
            {
                return;
            }

            iconRenderer.material.mainTexture = native;
            if (UISettings.Instance != null)
            {
                iconRenderer.material.SetColor(
                    "_TintColor", UISettings.Instance.SYMPTOM_COLOR_DARK_GRAY);
            }

            ModifiedCharacters.Remove(character);
        }

        private static void Postfix(CharacterRenderer __instance, Entity character)
        {
            if (!SmartTransportWaitRuntime.IsShowingOwnWaitingBubble(character))
            {
                return;
            }

            Dictionary<Entity, RendererObject> bubbles = GetMap(__instance, BubbleField);
            Dictionary<Entity, RendererObject> icons = GetMap(__instance, IconField);
            RendererObject bubble;
            RendererObject icon;
            if (bubbles == null || icons == null ||
                !bubbles.TryGetValue(character, out bubble) ||
                !icons.TryGetValue(character, out icon) ||
                bubble == null || icon == null ||
                bubble.m_gameObject == null || icon.m_gameObject == null ||
                icon.m_meshData == null)
            {
                return;
            }

            Texture texture = GetTexture(SmartTransportWaitRuntime.WaitingBubbleAsset);
            Renderer iconRenderer = icon.m_gameObject.GetComponent<Renderer>();
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

            vertices[0] = (-Vector3.right + Vector3.up) * VanillaBubbleScale;
            vertices[1] = (Vector3.right + Vector3.up) * VanillaBubbleScale;
            vertices[2] = (-Vector3.right - Vector3.up) * VanillaBubbleScale;
            vertices[3] = (Vector3.right - Vector3.up) * VanillaBubbleScale;

            Vector2[] uv = icon.m_meshData.m_texCoords;
            if (uv != null && uv.Length >= 4)
            {
                uv[0] = new Vector2(0f, 1f);
                uv[1] = new Vector2(1f, 1f);
                uv[2] = new Vector2(0f, 0f);
                uv[3] = new Vector2(1f, 0f);
            }

            icon.m_gameObject.transform.localPosition =
                bubble.m_gameObject.transform.localPosition;
            icon.UpdateMesh();

            bubble.m_gameObject.SetActive(false);
            icon.m_gameObject.SetActive(true);
            ModifiedCharacters.Add(character);
        }

        private static Dictionary<Entity, RendererObject> GetMap(
            CharacterRenderer renderer,
            FieldInfo field)
        {
            return renderer == null || field == null
                ? null
                : field.GetValue(renderer) as Dictionary<Entity, RendererObject>;
        }

        private static Texture GetTexture(string id)
        {
            StreamingAssetManager assets = StreamingAssetManager.GetInstance();
            if (assets == null || assets.m_textureAssets == null ||
                string.IsNullOrEmpty(id))
            {
                return null;
            }

            Texture texture;
            return assets.m_textureAssets.TryGetValue(id, out texture)
                ? texture
                : null;
        }
    }
}
