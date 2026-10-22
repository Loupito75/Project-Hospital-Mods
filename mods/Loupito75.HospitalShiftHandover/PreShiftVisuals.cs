using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalShiftHandover
{
    internal static class PreShiftVisuals
    {
        internal const string HandoverWaitingBubbleAsset =
            "HSH_ASSET_SHIFT_HANDOVER_WAIT_BUBBLE";

        private const int DressingBubbleTextureIndex = 23;
        private const float DressingBubbleDisplaySeconds = 1f;
        private const float DressingBubbleRefreshThresholdSeconds = 0.25f;

        private static readonly HashSet<Entity> DressingBubbleCharacters =
            new HashSet<Entity>();
        private static readonly HashSet<Entity> HandoverBubbleCharacters =
            new HashSet<Entity>();
        private static bool s_missingHandoverAssetLogged;

        internal static void UpdateDressingBubble(
            EmployeeComponent employee,
            bool shouldShow)
        {
            Entity character = employee != null ? employee.m_entity : null;
            SpeechComponent speech = character != null
                ? character.GetComponent<SpeechComponent>()
                : null;
            if (character == null || speech == null)
            {
                return;
            }

            bool tracked = DressingBubbleCharacters.Contains(character);
            bool exact = IsExactDressingBubble(speech);

            if (!shouldShow)
            {
                if (tracked && exact)
                {
                    speech.HideBubble();
                }

                DressingBubbleCharacters.Remove(character);
                return;
            }

            if (tracked && exact)
            {
                SpeechComponentPersistentData state = GetState(speech);
                if (state != null &&
                    state.m_displayTime <= DressingBubbleRefreshThresholdSeconds)
                {
                    speech.SetBubble(
                        DressingBubbleTextureIndex,
                        DressingBubbleDisplaySeconds);
                }
                return;
            }

            if (tracked)
            {
                // Another speech replaced HSH's bubble. Drop ownership without
                // hiding the replacement, then wait for the speech slot to free.
                DressingBubbleCharacters.Remove(character);
            }

            if (speech.IsActive())
            {
                return;
            }

            speech.SetBubble(
                DressingBubbleTextureIndex,
                DressingBubbleDisplaySeconds);
            DressingBubbleCharacters.Add(character);
        }

        internal static void UpdateHandoverWaitingBubble(
            EmployeeComponent employee,
            bool shouldShow)
        {
            Entity character = employee != null ? employee.m_entity : null;
            SpeechComponent speech = character != null
                ? character.GetComponent<SpeechComponent>()
                : null;
            if (character == null || speech == null)
            {
                return;
            }

            bool exact = IsExactHandoverWaitingBubble(speech);

            if (!shouldShow)
            {
                if (exact)
                {
                    speech.HideBubble();
                }

                HandoverBubbleCharacters.Remove(character);
                return;
            }

            if (exact)
            {
                HandoverBubbleCharacters.Add(character);
                return;
            }

            HandoverBubbleCharacters.Remove(character);
            if (speech.IsActive())
            {
                return;
            }

            if (!HasLoadedHandoverWaitingTexture())
            {
                return;
            }

            speech.SetBubble(
                "BUBBLE_EMPTY",
                HandoverWaitingBubbleAsset);
            HandoverBubbleCharacters.Add(character);
        }

        internal static bool IsShowingHandoverWaitingBubble(Entity character)
        {
            SpeechComponent speech = character != null
                ? character.GetComponent<SpeechComponent>()
                : null;
            return IsExactHandoverWaitingBubble(speech);
        }

        internal static void ClearPersistedHandoverBubble(
            SpeechComponentPersistentData state)
        {
            if (state == null ||
                !string.Equals(
                    state.m_bubbleIconAssetID,
                    HandoverWaitingBubbleAsset,
                    StringComparison.Ordinal))
            {
                return;
            }

            state.m_bubbleTextureIndex = -1;
            state.m_bubbleIconIndex = -1;
            state.m_bubbleIconAssetID = null;
            state.m_speechBubble = null;
            state.m_displayTime = -1f;
        }

        internal static void ResetForDatabaseReload()
        {
            DressingBubbleCharacters.Clear();
            HandoverBubbleCharacters.Clear();
            s_missingHandoverAssetLogged = false;
            PreShiftFullSpeechBubblePatch.Reset();
        }

        internal static void Shutdown()
        {
            foreach (Entity character in DressingBubbleCharacters)
            {
                SpeechComponent speech = character != null
                    ? character.GetComponent<SpeechComponent>()
                    : null;
                if (IsExactDressingBubble(speech))
                {
                    speech.HideBubble();
                }
            }

            foreach (Entity character in HandoverBubbleCharacters)
            {
                SpeechComponent speech = character != null
                    ? character.GetComponent<SpeechComponent>()
                    : null;
                if (IsExactHandoverWaitingBubble(speech))
                {
                    speech.HideBubble();
                }
            }

            ResetForDatabaseReload();
        }

        private static bool IsExactDressingBubble(SpeechComponent speech)
        {
            SpeechComponentPersistentData state = GetState(speech);
            return state != null &&
                state.m_speechBubble == null &&
                state.m_bubbleTextureIndex == DressingBubbleTextureIndex &&
                state.m_bubbleIconIndex == -1 &&
                state.m_bubbleIconAssetID == null &&
                state.m_displayTime >= 0f &&
                state.m_displayTime <= DressingBubbleDisplaySeconds;
        }

        private static bool IsExactHandoverWaitingBubble(SpeechComponent speech)
        {
            SpeechComponentPersistentData state = GetState(speech);
            return state != null &&
                state.m_speechBubble != null &&
                state.m_bubbleIconIndex == -1 &&
                string.Equals(
                    state.m_bubbleIconAssetID,
                    HandoverWaitingBubbleAsset,
                    StringComparison.Ordinal);
        }

        private static SpeechComponentPersistentData GetState(
            SpeechComponent speech)
        {
            return speech == null
                ? null
                : speech.GetPersistentData() as SpeechComponentPersistentData;
        }

        private static bool HasLoadedHandoverWaitingTexture()
        {
            StreamingAssetManager assets = StreamingAssetManager.GetInstance();
            if (assets != null &&
                assets.m_textureAssets != null &&
                assets.m_textureAssets.ContainsKey(HandoverWaitingBubbleAsset))
            {
                return true;
            }

            if (!s_missingHandoverAssetLogged &&
                assets != null &&
                assets.m_textureAssets != null)
            {
                s_missingHandoverAssetLogged = true;
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "Shift handover waiting bubble asset is missing; " +
                        "native speech remains unchanged.");
                }
            }

            return false;
        }
    }

    internal static class PreShiftVisualDatabase
    {
        private static bool s_loading;
        private static bool s_loaded;

        internal static bool IsLoading
        {
            get { return s_loading; }
        }

        internal static void ResetForBaseLoad()
        {
            s_loaded = false;
            PreShiftVisuals.ResetForDatabaseReload();
        }

        internal static void Load(Database database)
        {
            if (database == null || s_loading || s_loaded)
            {
                return;
            }

            string pluginRoot = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);
            string databaseDirectory = string.IsNullOrEmpty(pluginRoot)
                ? null
                : Path.Combine(pluginRoot, "Database");

            if (string.IsNullOrEmpty(databaseDirectory) ||
                !Directory.Exists(databaseDirectory))
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogError(
                        "Hospital Shift Handover Database directory is missing; " +
                        "the custom shift-handover bubble cannot be loaded.");
                }
                return;
            }

            s_loading = true;
            try
            {
                database.ReadFiles(pluginRoot, true);
                GameDBAsset asset = database.GetEntry<GameDBAsset>(
                    PreShiftVisuals.HandoverWaitingBubbleAsset);
                s_loaded = asset != null;

                if (!s_loaded && Plugin.Log != null)
                {
                    Plugin.Log.LogError(
                        "Hospital Shift Handover visual database was read, " +
                        "but the shift-handover bubble asset is missing.");
                }
            }
            catch (Exception exception)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogError(
                        "Failed to load Hospital Shift Handover visual database: " +
                        exception);
                }
            }
            finally
            {
                s_loading = false;
            }
        }
    }

    [HarmonyPatch(
        typeof(Database),
        nameof(Database.ReadFiles),
        new Type[] { typeof(string), typeof(bool) })]
    internal static class PreShiftVisualDatabaseReadFilesPatch
    {
        [HarmonyPrefix]
        private static void Prefix(bool add)
        {
            if (!add && !PreShiftVisualDatabase.IsLoading)
            {
                PreShiftVisualDatabase.ResetForBaseLoad();
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Database __instance, bool add)
        {
            if (!add && !PreShiftVisualDatabase.IsLoading)
            {
                PreShiftVisualDatabase.Load(__instance);
            }
        }
    }

    [HarmonyPatch]
    internal static class PreShiftSpeechLoadCleanupPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Constructor(
                typeof(SpeechComponent),
                new Type[]
                {
                    typeof(Entity),
                    typeof(SpeechComponentPersistentData)
                });
        }

        [HarmonyPostfix]
        private static void Postfix(SpeechComponentPersistentData state)
        {
            PreShiftVisuals.ClearPersistedHandoverBubble(state);
        }
    }

    [HarmonyPatch(
        typeof(CharacterRenderer),
        "UpdateSpeechBubble",
        new Type[] { typeof(Entity), typeof(LightMap2DUnity) })]
    internal static class PreShiftFullSpeechBubblePatch
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
        private static readonly HashSet<Entity> ModifiedCharacters =
            new HashSet<Entity>();

        internal static void Reset()
        {
            ModifiedCharacters.Clear();
        }

        [HarmonyPrefix]
        private static void Prefix(
            CharacterRenderer __instance,
            Entity character)
        {
            if (character == null ||
                !ModifiedCharacters.Contains(character) ||
                PreShiftVisuals.IsShowingHandoverWaitingBubble(character))
            {
                return;
            }

            RestoreNativeIconTextureIfNeeded(__instance, character);
        }

        [HarmonyPostfix]
        private static void Postfix(
            CharacterRenderer __instance,
            Entity character)
        {
            if (!PreShiftVisuals.IsShowingHandoverWaitingBubble(character))
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

            Texture texture = GetTexture(
                PreShiftVisuals.HandoverWaitingBubbleAsset);
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
            bubble.m_gameObject.SetActive(false);
            icon.m_gameObject.SetActive(true);
            ModifiedCharacters.Add(character);
        }

        private static void RestoreNativeIconTextureIfNeeded(
            CharacterRenderer renderer,
            Entity character)
        {
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

            if (!object.ReferenceEquals(
                    iconRenderer.material.mainTexture,
                    GetTexture(PreShiftVisuals.HandoverWaitingBubbleAsset)))
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

        private static Texture GetTexture(string assetId)
        {
            StreamingAssetManager assets = StreamingAssetManager.GetInstance();
            if (assets == null ||
                assets.m_textureAssets == null ||
                string.IsNullOrEmpty(assetId))
            {
                return null;
            }

            Texture texture;
            return assets.m_textureAssets.TryGetValue(assetId, out texture)
                ? texture
                : null;
        }

        private static Dictionary<Entity, RendererObject> GetRendererMap(
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
