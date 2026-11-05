using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
    // HFX pause PNGs are complete speech bubbles, not small center icons.
    // Vanilla keeps ownership of speech, positioning and lifecycle; this
    // patch only expands the exact HFX-owned asset and fully restores the
    // icon renderer after HFX relinquishes ownership.
    [HarmonyPatch(
        typeof(CharacterRenderer),
        "UpdateSpeechBubble",
        new Type[]
        {
            typeof(Entity),
            typeof(LightMap2DUnity)
        })]
    internal static class ReservedStaffWaitingBubbleRendererPatch
    {
        private const float VanillaBubbleScale = 0.333f;
        private const string NativeIconsAsset =
            "ASSET_TEX_UI_ICONS";

        private static readonly FieldInfo BubbleField =
            AccessTools.Field(
                typeof(CharacterRenderer),
                "m_characterSpeechBubbles");

        private static readonly FieldInfo IconField =
            AccessTools.Field(
                typeof(CharacterRenderer),
                "m_characterSpeechBubbleIcons");

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

        private static readonly Dictionary<Entity, ReplacedIcon>
            Modified =
                new Dictionary<Entity, ReplacedIcon>();

        internal static void Reset()
        {
            Entity[] characters =
                new Entity[Modified.Count];
            Modified.Keys.CopyTo(
                characters,
                0);

            for (int i = 0;
                 i < characters.Length;
                 i++)
            {
                Restore(characters[i]);
            }

            Modified.Clear();
        }

        [HarmonyPrefix]
        private static void Prefix(
            CharacterRenderer __instance,
            Entity character)
        {
            if (character == null)
            {
                return;
            }

            bool showingOwnBubble =
                ReservedStaffWaitingVisuals
                    .IsShowingOwnWaitingBubble(character);
            bool eligibleOwner =
                showingOwnBubble &&
                ReservedStaffWaitingVisuals
                    .IsEligibleBubbleOwner(character);

            if (showingOwnBubble &&
                !eligibleOwner)
            {
                // The staff state can change after HFX SetBubble() but before
                // CharacterRenderer sees the first frame. Without this guard,
                // Vanilla renders BUBBLE_EMPTY plus our full PNG as a small
                // dark-gray center icon: visually a bubble inside a bubble.
                // Remove only HFX's own speech before Vanilla renders.
                ReservedStaffWaitingVisuals
                    .HideOwnWaitingBubble(character);

                if (Modified.ContainsKey(character))
                {
                    Restore(character);
                }

                return;
            }

            if (Modified.ContainsKey(character) &&
                !showingOwnBubble)
            {
                Restore(character);
            }
        }

        [HarmonyPostfix]
        private static void Postfix(
            CharacterRenderer __instance,
            Entity character)
        {
            if (__instance == null ||
                !ReservedStaffWaitingVisuals
                    .IsEligibleBubbleOwner(character) ||
                !ReservedStaffWaitingVisuals
                    .IsShowingOwnWaitingBubble(character))
            {
                return;
            }

            Dictionary<Entity, RendererObject> bubbles =
                GetMap(__instance, BubbleField);
            Dictionary<Entity, RendererObject> icons =
                GetMap(__instance, IconField);

            RendererObject bubble;
            RendererObject icon;

            if (bubbles == null ||
                icons == null ||
                !bubbles.TryGetValue(
                    character,
                    out bubble) ||
                !icons.TryGetValue(
                    character,
                    out icon) ||
                bubble == null ||
                icon == null ||
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

            string bubbleAsset =
                ReservedStaffWaitingVisuals
                    .GetOwnedBubbleAsset(character);
            Texture texture =
                GetTexture(bubbleAsset);
            Renderer iconRenderer =
                icon.m_gameObject
                    .GetComponent<Renderer>();

            if (texture == null ||
                iconRenderer == null)
            {
                return;
            }

            ReplacedIcon previous;
            bool needsSnapshot =
                !Modified.TryGetValue(
                    character,
                    out previous) ||
                !object.ReferenceEquals(
                    previous.Object,
                    icon);

            if (needsSnapshot)
            {
                if (previous != null)
                {
                    Restore(character);
                }

                previous =
                    new ReplacedIcon();
                previous.Object = icon;
                previous.PreviousVertices =
                    new Vector3[4];
                previous.PreviousUvs =
                    new Vector2[4];

                Material originalMaterial =
                    iconRenderer.material;
                previous.PreviousTexture =
                    originalMaterial.mainTexture;
                previous.AppliedTexture =
                    texture;
                previous.PreviousTint =
                    originalMaterial.GetColor("_TintColor");
                previous.PreviousPosition =
                    icon.m_gameObject
                        .transform.localPosition;

                Array.Copy(
                    icon.m_meshData.m_vertices,
                    previous.PreviousVertices,
                    4);
                Array.Copy(
                    icon.m_meshData.m_texCoords,
                    previous.PreviousUvs,
                    4);

                Modified[character] = previous;
            }

            Material material =
                iconRenderer.material;
            previous.AppliedTexture =
                texture;
            material.mainTexture =
                texture;
            material.SetColor(
                "_TintColor",
                Color.white);

            Vector3[] vertices =
                icon.m_meshData.m_vertices;
            vertices[0] =
                (-Vector3.right + Vector3.up) *
                VanillaBubbleScale;
            vertices[1] =
                (Vector3.right + Vector3.up) *
                VanillaBubbleScale;
            vertices[2] =
                (-Vector3.right - Vector3.up) *
                VanillaBubbleScale;
            vertices[3] =
                (Vector3.right - Vector3.up) *
                VanillaBubbleScale;

            Vector2[] uv =
                icon.m_meshData.m_texCoords;
            uv[0] =
                new Vector2(0f, 1f);
            uv[1] =
                new Vector2(1f, 1f);
            uv[2] =
                new Vector2(0f, 0f);
            uv[3] =
                new Vector2(1f, 0f);

            icon.m_gameObject
                .transform.localPosition =
                bubble.m_gameObject
                    .transform.localPosition;
            icon.UpdateMesh();

            bubble.m_gameObject.SetActive(false);
            icon.m_gameObject.SetActive(true);
            Modified[character] = previous;

            ReservedStaffWaitingVisuals
                .MarkBubbleRendered(character);
        }

        internal static void Restore(
            Entity character)
        {
            if (character == null)
            {
                return;
            }

            ReplacedIcon previous;
            if (!Modified.TryGetValue(
                    character,
                    out previous))
            {
                return;
            }

            RendererObject icon =
                previous.Object;
            if (icon == null ||
                icon.m_gameObject == null)
            {
                Modified.Remove(character);
                return;
            }

            Renderer renderer =
                icon.m_gameObject
                    .GetComponent<Renderer>();
            if (renderer == null)
            {
                Modified.Remove(character);
                return;
            }

            if (!object.ReferenceEquals(
                    renderer.material.mainTexture,
                    previous.AppliedTexture))
            {
                // Another renderer owner has already replaced the material.
                // Drop HFX tracking without overwriting that newer state.
                Modified.Remove(character);
                return;
            }

            Texture restoreTexture =
                previous.PreviousTexture;
            if (object.ReferenceEquals(
                    restoreTexture,
                    previous.AppliedTexture))
            {
                restoreTexture =
                    GetTexture(NativeIconsAsset);
            }

            if (restoreTexture == null)
            {
                return;
            }

            renderer.material.mainTexture =
                restoreTexture;
            renderer.material.SetColor(
                "_TintColor",
                previous.PreviousTint);

            if (icon.m_meshData != null)
            {
                if (icon.m_meshData.m_vertices != null &&
                    icon.m_meshData.m_vertices.Length >= 4 &&
                    previous.PreviousVertices != null)
                {
                    Array.Copy(
                        previous.PreviousVertices,
                        icon.m_meshData.m_vertices,
                        4);
                }

                if (icon.m_meshData.m_texCoords != null &&
                    icon.m_meshData.m_texCoords.Length >= 4 &&
                    previous.PreviousUvs != null)
                {
                    Array.Copy(
                        previous.PreviousUvs,
                        icon.m_meshData.m_texCoords,
                        4);
                }

                icon.m_gameObject
                    .transform.localPosition =
                    previous.PreviousPosition;
                icon.UpdateMesh();
            }

            SpeechComponent speech =
                character
                    .GetComponent<SpeechComponent>();
            if (speech == null ||
                !speech.IsActive())
            {
                icon.m_gameObject
                    .SetActive(false);
            }

            Modified.Remove(character);
        }

        private static Dictionary<Entity, RendererObject>
            GetMap(
                CharacterRenderer renderer,
                FieldInfo field)
        {
            return renderer == null ||
                field == null
                    ? null
                    : field.GetValue(renderer)
                        as Dictionary<
                            Entity,
                            RendererObject>;
        }

        private static Texture GetTexture(
            string id)
        {
            StreamingAssetManager assets =
                StreamingAssetManager
                    .GetInstance();

            if (assets == null ||
                assets.m_textureAssets == null ||
                string.IsNullOrEmpty(id))
            {
                return null;
            }

            Texture texture;
            return assets.m_textureAssets
                    .TryGetValue(
                        id,
                        out texture)
                ? texture
                : null;
        }
    }
}
