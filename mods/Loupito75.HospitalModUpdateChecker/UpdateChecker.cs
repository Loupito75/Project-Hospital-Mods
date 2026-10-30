using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace HospitalModUpdateChecker
{
    internal enum RemoteContentState
    {
        Loading,
        Ready,
        Empty,
        Error,
        Cached
    }

    internal static class UpdateChecker
    {
        private const string CachedNewsManifestKey =
            "Loupito75.HospitalModUpdateChecker.CachedUnifiedNewsManifest";

        private const string DebugUpdateManifest =
            "{\n" +
            "  \"formatVersion\": 1,\n" +
            "  \"manifestRevision\": 1,\n" +
            "  \"updatedUtc\": \"2026-10-01T00:00:00Z\",\n" +
            "  \"mods\": [\n" +
            "    {\"guid\":\"debug.HospitalAlwaysLit\",\"name\":\"Hospital Always Lit\",\"author\":\"Loupito75\",\"version\":\"1.2.0\"},\n" +
            "    {\"guid\":\"debug.HospitalCareLevelTransfer\",\"name\":\"Hospital Care Level Transfer\",\"author\":\"Loupito75\",\"version\":\"1.1.0\"},\n" +
            "    {\"guid\":\"debug.HospitalEMS\",\"name\":\"Hospital EMS\",\"author\":\"Loupito75\",\"version\":\"1.0.1\"},\n" +
            "    {\"guid\":\"debug.HospitalPatientLife\",\"name\":\"Hospital Patient Life\",\"author\":\"Loupito75\",\"version\":\"1.0.0\"},\n" +
            "    {\"guid\":\"debug.HospitalTrafficControl\",\"name\":\"Hospital Traffic Control\",\"author\":\"Loupito75\",\"version\":\"1.3.0\"},\n" +
            "    {\"guid\":\"debug.HospitalShiftHandover\",\"name\":\"Hospital Shift Handover\",\"author\":\"Loupito75\",\"version\":\"1.1.1\"},\n" +
            "    {\"guid\":\"debug.HospitalRadio\",\"name\":\"Hospital Radio\",\"author\":\"Loupito75\",\"version\":\"1.0.0\"},\n" +
            "    {\"guid\":\"debug.HospitalPorters\",\"name\":\"Hospital Porters\",\"author\":\"Loupito75\",\"version\":\"1.0.1\"},\n" +
            "    {\"guid\":\"debug.HospitalVeryLongModNameForLayoutTesting\",\"name\":\"Hospital Very Long Mod Name For Layout And Scrolling Tests\",\"author\":\"Loupito75\",\"version\":\"2.4.7\"}\n" +
            "  ]\n" +
            "}";

        private const string DebugNewsManifest =
            "{\n" +
            "  \"formatVersion\": 1,\n" +
            "  \"manifestRevision\": 1,\n" +
            "  \"updatedUtc\": \"2026-10-01T00:00:00Z\",\n" +
            "  \"items\": [\n" +
            "    {\"id\":0,\"title\":\"Hospital Always Lit 1.2.0\",\"text\":\"Improved lighting behavior.\"},\n" +
            "    {\"id\":1,\"title\":\"Hospital Care Level Transfer 1.1.0\",\"text\":\"New transfer improvements.\"},\n" +
            "    {\"id\":2,\"title\":\"Hospital EMS 1.0.1\",\"text\":\"Emergency workflow fixes.\"},\n" +
            "    {\"id\":3,\"title\":\"Hospital Patient Life 1.0.0\",\"text\":\"Patient-life features are now available.\"},\n" +
            "    {\"id\":4,\"title\":\"Hospital Traffic Control 1.3.0\",\"text\":\"Traffic and pathing improvements.\"},\n" +
            "    {\"id\":5,\"title\":\"Hospital Shift Handover 1.1.1\",\"text\":\"Handover workflow refinements.\"},\n" +
            "    {\"id\":6,\"title\":\"Hospital Radio 1.0.0\",\"text\":\"Hospital radio is now available.\"},\n" +
            "    {\"id\":7,\"title\":\"Hospital Porters 1.0.1\",\"text\":\"Porter workflow improvements.\"},\n" +
            "    {\"id\":8,\"title\":\"New experimental mod preview\",\"text\":\"Example announcement for UI testing.\\nSecond line for newline testing.\"},\n" +
            "    {\"id\":9,\"title\":\"Paragraph spacing test\",\"text\":\"First paragraph.\\n\\nSecond paragraph verifies double line breaks and scrolling inside the News block.\"}\n" +
            "  ]\n" +
            "}";

        private static readonly List<AvailableUpdate> _availableUpdates =
            new List<AvailableUpdate>();

        private static readonly List<NewsManifestEntry> _newsItems =
            new List<NewsManifestEntry>();

        internal static RemoteContentState UpdateState { get; private set; }
        internal static RemoteContentState NewsState { get; private set; }

        internal static IList<AvailableUpdate> AvailableUpdates
        {
            get { return _availableUpdates.AsReadOnly(); }
        }

        internal static IList<NewsManifestEntry> NewsItems
        {
            get { return _newsItems.AsReadOnly(); }
        }

        static UpdateChecker()
        {
            UpdateState = RemoteContentState.Loading;
            NewsState = RemoteContentState.Loading;
        }

        internal static IEnumerator CheckForUpdates()
        {
            IEnumerator routine = CheckForUpdatesCore();

            while (true)
            {
                bool moveNext;
                object current = null;

                try
                {
                    moveNext = routine.MoveNext();

                    if (moveNext)
                    {
                        current = routine.Current;
                    }
                }
                catch (Exception exception)
                {
                    UpdateState =
                        RemoteContentState.Error;

                    NewsState =
                        UpdateCheckerConfig.ShowNews
                            ? RemoteContentState.Error
                            : RemoteContentState.Empty;

                    PackageUpdateManager
                        .SetUnifiedCatalogError();

                    ModDetailsCatalog.Clear();

                    TitleScreenUpdatePanel.Refresh();

                    Plugin.Log.LogWarning(
                        "HMUC remote content check stopped after an unexpected error: " +
                        exception);
                    yield break;
                }

                if (!moveNext)
                {
                    yield break;
                }

                yield return current;
            }
        }

        private static IEnumerator CheckForUpdatesCore()
        {
            if (UpdateCheckerConfig.Debug)
            {
                PackageUpdateManager.BeginUnifiedCatalogRefresh();
                ModDetailsCatalog.Clear();

                LoadDebugUpdateManifest();

                if (UpdateCheckerConfig.ShowNews)
                {
                    LoadDebugNewsManifest();
                }
                else
                {
                    _newsItems.Clear();
                    NewsState =
                        RemoteContentState.Empty;
                }

                TitleScreenUpdatePanel.Refresh();
                yield break;
            }

            _availableUpdates.Clear();
            _newsItems.Clear();

            UpdateState =
                RemoteContentState.Loading;

            NewsState =
                UpdateCheckerConfig.ShowNews
                    ? RemoteContentState.Loading
                    : RemoteContentState.Empty;

            PackageUpdateManager.BeginUnifiedCatalogRefresh();
            ModDetailsCatalog.Clear();
            TitleScreenUpdatePanel.Refresh();

            IEnumerator catalogRoutine =
                UnifiedCatalogClient.Refresh();

            try
            {
                while (catalogRoutine.MoveNext())
                {
                    yield return
                        catalogRoutine.Current;
                }
            }
            finally
            {
                IDisposable disposable =
                    catalogRoutine as IDisposable;

                if (disposable != null)
                {
                    disposable.Dispose();
                }
            }

            UnifiedModCatalog catalog =
                UnifiedCatalogClient.Current;

            if (catalog == null)
            {
                UpdateState =
                    RemoteContentState.Error;

                PackageUpdateManager.SetUnifiedCatalogError();
                ModDetailsCatalog.Clear();

                string failure =
                    "HMUC unified catalog check failed: " +
                    (string.IsNullOrEmpty(
                         UnifiedCatalogClient.LastError)
                        ? "unknown error"
                        : UnifiedCatalogClient.LastError);

                bool usedCachedNews =
                    false;

                if (UpdateCheckerConfig.ShowNews)
                {
                    usedCachedNews =
                        TryUseCachedNewsFallback(
                            failure);

                    if (!usedCachedNews)
                    {
                        _newsItems.Clear();
                        NewsState =
                            RemoteContentState.Error;
                    }
                }
                else
                {
                    _newsItems.Clear();
                    NewsState =
                        RemoteContentState.Empty;
                }

                if (!usedCachedNews)
                {
                    Plugin.Log.LogWarning(
                        failure);
                }

                TitleScreenUpdatePanel.Refresh();
                yield break;
            }

            ApplyUnifiedCatalogUpdates(
                catalog);

            PackageUpdateManager.ApplyUnifiedCatalog(
                catalog);

            ModDetailsCatalog.ApplyUnifiedCatalog(
                catalog);

            if (UpdateCheckerConfig.ShowNews)
            {
                ApplyUnifiedCatalogNews(
                    catalog);

                SaveNewsCache(
                    catalog.CreateNewsCacheManifest());
            }
            else
            {
                _newsItems.Clear();
                NewsState =
                    RemoteContentState.Empty;
            }

            TitleScreenUpdatePanel.Refresh();
        }

        private static void ApplyUnifiedCatalogUpdates(
            UnifiedModCatalog catalog)
        {
            _availableUpdates.Clear();

            if (catalog == null)
            {
                UpdateState =
                    RemoteContentState.Error;
                return;
            }

            for (int i = 0;
                i < catalog.Mods.Count;
                i++)
            {
                UnifiedCatalogModEntry mod =
                    catalog.Mods[i];

                UpdateManifestEntry remote =
                    mod == null
                        ? null
                        : mod.Update;

                if (remote == null)
                {
                    continue;
                }

                PluginInfo installed;

                if (!Chainloader.PluginInfos.TryGetValue(
                        remote.Guid,
                        out installed) ||
                    installed == null ||
                    installed.Metadata == null ||
                    installed.Metadata.Version == null)
                {
                    continue;
                }

                Version localVersion =
                    installed.Metadata.Version;

                if (remote.Version.CompareTo(
                        localVersion) <= 0)
                {
                    continue;
                }

                _availableUpdates.Add(
                    new AvailableUpdate(
                        remote.Guid,
                        remote.Name,
                        remote.Author,
                        localVersion,
                        remote.Version));
            }

            SortAvailableUpdates();

            UpdateState =
                _availableUpdates.Count == 0
                    ? RemoteContentState.Empty
                    : RemoteContentState.Ready;
        }

        private static void ApplyUnifiedCatalogNews(
            UnifiedModCatalog catalog)
        {
            _newsItems.Clear();

            if (catalog != null)
            {
                _newsItems.AddRange(
                    catalog.News);
            }

            NewsState =
                _newsItems.Count == 0
                    ? RemoteContentState.Empty
                    : RemoteContentState.Ready;
        }

        private static void LoadDebugUpdateManifest()
        {
            UpdateManifest manifest;
            string parseError;

            if (!UpdateManifest.TryParse(
                    DebugUpdateManifest,
                    out manifest,
                    out parseError))
            {
                _availableUpdates.Clear();
                UpdateState = RemoteContentState.Error;

                Plugin.Log.LogWarning(
                    "Built-in debug update manifest was rejected: " +
                    parseError);
                return;
            }

            _availableUpdates.Clear();

            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                UpdateManifestEntry entry = manifest.Entries[i];

                Version installedVersion = new Version(
                    Math.Max(0, entry.Version.Major - 1),
                    0,
                    0);

                _availableUpdates.Add(
                    new AvailableUpdate(
                        entry.Guid,
                        entry.Name,
                        entry.Author,
                        installedVersion,
                        entry.Version));
            }

            // Debug preview for the updater itself. Use the real HMUC GUID so
            // sorting and the update-HMUC-first banner follow the exact
            // production path, while using the actually loaded version as
            // the starting point.
            Version hmucInstalledVersion = null;
            PluginInfo hmucPlugin;
            if (Chainloader.PluginInfos.TryGetValue(
                    Plugin.PluginGuid,
                    out hmucPlugin) &&
                hmucPlugin != null &&
                hmucPlugin.Metadata != null)
            {
                hmucInstalledVersion =
                    hmucPlugin.Metadata.Version;
            }

            if (hmucInstalledVersion == null)
            {
                try
                {
                    hmucInstalledVersion =
                        new Version(
                            Plugin.PluginVersion);
                }
                catch
                {
                    hmucInstalledVersion = null;
                }
            }

            if (hmucInstalledVersion != null)
            {
                _availableUpdates.Add(
                    new AvailableUpdate(
                        Plugin.PluginGuid,
                        Plugin.PluginName,
                        Plugin.PluginAuthor,
                        hmucInstalledVersion,
                        CreateDebugHmucAvailableVersion(
                            hmucInstalledVersion)));
            }

            SortAvailableUpdates();

            UpdateState = _availableUpdates.Count == 0
                ? RemoteContentState.Empty
                : RemoteContentState.Ready;

        }

        private static Version CreateDebugHmucAvailableVersion(
            Version installedVersion)
        {
            int build = Math.Max(0, installedVersion.Build);
            int revision = Math.Max(0, installedVersion.Revision);

            if (revision < int.MaxValue)
            {
                return new Version(
                    installedVersion.Major,
                    installedVersion.Minor,
                    build,
                    revision + 1);
            }

            if (build < int.MaxValue)
            {
                return new Version(
                    installedVersion.Major,
                    installedVersion.Minor,
                    build + 1,
                    0);
            }

            return new Version(
                installedVersion.Major,
                installedVersion.Minor + 1,
                0,
                0);
        }

        private static void LoadDebugNewsManifest()
        {
            NewsManifest manifest;
            string parseError;

            if (!NewsManifest.TryParse(
                    DebugNewsManifest,
                    out manifest,
                    out parseError))
            {
                _newsItems.Clear();
                NewsState = RemoteContentState.Error;

                Plugin.Log.LogWarning(
                    "Built-in debug news manifest was rejected: " +
                    parseError);
                return;
            }

            _newsItems.Clear();
            _newsItems.AddRange(manifest.Entries);
            NewsState = _newsItems.Count == 0
                ? RemoteContentState.Empty
                : RemoteContentState.Ready;

        }

        private static bool TryUseCachedNewsFallback(
            string failure)
        {
            NewsManifest manifest;

            if (!TryLoadCachedNewsManifest(
                    out manifest) ||
                manifest.Entries.Count == 0)
            {
                return false;
            }

            ApplyNewsManifest(
                manifest,
                true);

            Plugin.Log.LogWarning(
                failure +
                " Using the last valid cached news manifest.");

            return true;
        }

        private static bool TryLoadCachedNewsManifest(
            out NewsManifest manifest)
        {
            manifest = null;

            if (!PlayerPrefs.HasKey(
                    CachedNewsManifestKey))
            {
                return false;
            }

            string text =
                PlayerPrefs.GetString(
                    CachedNewsManifestKey);

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string parseError;

            if (!NewsManifest.TryParse(
                    text,
                    out manifest,
                    out parseError))
            {
                Plugin.Log.LogWarning(
                    "Cached news manifest was rejected: " +
                    parseError);

                PlayerPrefs.SetString(
                    CachedNewsManifestKey,
                    string.Empty);
                PlayerPrefs.Save();

                manifest = null;
                return false;
            }

            return true;
        }

        private static void ApplyNewsManifest(
            NewsManifest manifest,
            bool cached)
        {
            _newsItems.Clear();

            if (manifest != null)
            {
                _newsItems.AddRange(
                    manifest.Entries);
            }

            if (_newsItems.Count == 0)
            {
                NewsState = RemoteContentState.Empty;
                return;
            }

            NewsState = cached
                ? RemoteContentState.Cached
                : RemoteContentState.Ready;
        }

        private static void SaveNewsCache(
            string manifestText)
        {
            PlayerPrefs.SetString(
                CachedNewsManifestKey,
                manifestText ?? string.Empty);

            PlayerPrefs.Save();
        }

        private static void SortAvailableUpdates()
        {
            _availableUpdates.Sort(delegate(
                AvailableUpdate left,
                AvailableUpdate right)
            {
                // The updater must be updated and the game restarted before
                // users are advised to stage other mod updates. Identify it
                // by the real BepInEx GUID, never by its display name.
                bool leftIsHmuc = left != null &&
                    string.Equals(left.Guid, Plugin.PluginGuid,
                        StringComparison.OrdinalIgnoreCase);
                bool rightIsHmuc = right != null &&
                    string.Equals(right.Guid, Plugin.PluginGuid,
                        StringComparison.OrdinalIgnoreCase);
                if (leftIsHmuc != rightIsHmuc)
                    return leftIsHmuc ? -1 : 1;

                string leftName =
                    left == null
                        ? string.Empty
                        : left.Name ?? string.Empty;

                string rightName =
                    right == null
                        ? string.Empty
                        : right.Name ?? string.Empty;

                int nameComparison =
                    string.Compare(
                        leftName,
                        rightName,
                        StringComparison.OrdinalIgnoreCase);

                if (nameComparison != 0)
                {
                    return nameComparison;
                }

                string leftAuthor =
                    left == null
                        ? string.Empty
                        : left.Author ?? string.Empty;

                string rightAuthor =
                    right == null
                        ? string.Empty
                        : right.Author ?? string.Empty;

                return string.Compare(
                    leftAuthor,
                    rightAuthor,
                    StringComparison.OrdinalIgnoreCase);
            });
        }

    }

    internal sealed class AvailableUpdate
    {
        internal string Guid { get; private set; }
        internal string Name { get; private set; }
        internal string Author { get; private set; }
        internal Version InstalledVersion { get; private set; }
        internal Version AvailableVersion { get; private set; }

        internal AvailableUpdate(
            string guid,
            string name,
            string author,
            Version installedVersion,
            Version availableVersion)
        {
            Guid = guid;
            Name = name;
            Author = author;
            InstalledVersion = installedVersion;
            AvailableVersion = availableVersion;
        }
    }
}
