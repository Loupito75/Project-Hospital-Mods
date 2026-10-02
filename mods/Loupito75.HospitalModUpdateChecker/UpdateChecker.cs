using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.Networking;

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
        private const string ManifestUrl =
            "https://raw.githubusercontent.com/Loupito75/Project-Hospital-Mods/main/mod-updates.json";

        private const string NewsManifestUrl =
            "https://raw.githubusercontent.com/Loupito75/Project-Hospital-Mods/main/mod-news.json";

        private const int RequestTimeoutSeconds = 10;
        private const ulong MaximumManifestBytes = 65536UL;
        private const ulong MaximumNewsManifestBytes = 32768UL;
        private const double CheckIntervalHours = 24.0;
        private const string LastSuccessfulCheckKey =
            "Loupito75.HospitalModUpdateChecker.LastSuccessfulCheckUtcTicks";
        private const string LastSuccessfulNewsCheckKey =
            "Loupito75.HospitalModUpdateChecker.LastSuccessfulNewsCheckUtcTicks";
        private const string CachedNewsManifestKey =
            "Loupito75.HospitalModUpdateChecker.CachedNewsManifest";

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
            "    {\"guid\":\"debug.HospitalModUpdateChecker\",\"name\":\"Hospital Mod Update Checker\",\"author\":\"Loupito75\",\"version\":\"1.1.0\"},\n" +
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
                    UpdateState = RemoteContentState.Error;
                    NewsState = RemoteContentState.Error;
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
                LoadDebugUpdateManifest();
                TitleScreenUpdatePanel.Refresh();
            }
            else if (ShouldCheckNow())
            {
                IEnumerator updateRoutine = CheckUpdateManifest();

                while (updateRoutine.MoveNext())
                {
                    yield return updateRoutine.Current;
                }
            }
            else
            {
                _availableUpdates.Clear();
                UpdateState = RemoteContentState.Cached;
                TitleScreenUpdatePanel.Refresh();
            }

            if (!UpdateCheckerConfig.ShowNews)
            {
                _newsItems.Clear();
                NewsState = RemoteContentState.Empty;
                TitleScreenUpdatePanel.Refresh();
                yield break;
            }

            if (UpdateCheckerConfig.Debug)
            {
                LoadDebugNewsManifest();
                TitleScreenUpdatePanel.Refresh();
                yield break;
            }

            if (UpdateCheckerConfig.CheckEveryLaunch)
            {
                InvalidateSuccessfulNewsCheckTime();
            }
            else if (TryLoadFreshCachedNews())
            {
                TitleScreenUpdatePanel.Refresh();
                yield break;
            }

            IEnumerator newsRoutine = CheckNewsManifest();

            while (newsRoutine.MoveNext())
            {
                yield return newsRoutine.Current;
            }
        }

        private static IEnumerator CheckUpdateManifest()
        {
            UpdateState = RemoteContentState.Loading;
            TitleScreenUpdatePanel.Refresh();

            UnityWebRequest request = null;

            try
            {
                request = UnityWebRequest.Get(ManifestUrl);
                request.timeout = RequestTimeoutSeconds;

                yield return request.SendWebRequest();

                if (request.isNetworkError || request.isHttpError)
                {
                    UpdateState = RemoteContentState.Error;
                    Plugin.Log.LogWarning(
                        "Update check failed: HTTP " +
                        request.responseCode.ToString(
                            CultureInfo.InvariantCulture) +
                        " | " +
                        (request.error ?? "unknown error"));
                    yield break;
                }

                if (request.downloadedBytes > MaximumManifestBytes)
                {
                    UpdateState = RemoteContentState.Error;
                    Plugin.Log.LogWarning(
                        "Update manifest was rejected because it is too large.");
                    yield break;
                }

                string text = request.downloadHandler == null
                    ? null
                    : request.downloadHandler.text;

                UpdateManifest manifest;
                string parseError;

                if (!UpdateManifest.TryParse(
                        text,
                        out manifest,
                        out parseError))
                {
                    UpdateState = RemoteContentState.Error;
                    Plugin.Log.LogWarning(
                        "Update manifest was rejected: " + parseError);
                    yield break;
                }

                _availableUpdates.Clear();

                for (int i = 0; i < manifest.Entries.Count; i++)
                {
                    UpdateManifestEntry remote = manifest.Entries[i];
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

                    Version localVersion = installed.Metadata.Version;
                    if (remote.Version.CompareTo(localVersion) <= 0)
                    {
                        continue;
                    }

                    _availableUpdates.Add(
                        new AvailableUpdate(
                            remote.Name,
                            remote.Author,
                            localVersion,
                            remote.Version));
                }

                SortAvailableUpdates();

                UpdateState = _availableUpdates.Count == 0
                    ? RemoteContentState.Empty
                    : RemoteContentState.Ready;

                if (!UpdateCheckerConfig.CheckEveryLaunch)
                {
                    if (_availableUpdates.Count == 0)
                    {
                        SaveSuccessfulCheckTime();
                    }
                    else
                    {
                        InvalidateSuccessfulCheckTime();
                    }
                }
            }
            finally
            {
                if (request != null)
                {
                    request.Dispose();
                }

                TitleScreenUpdatePanel.Refresh();
            }
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
                        entry.Name,
                        entry.Author,
                        installedVersion,
                        entry.Version));
            }

            SortAvailableUpdates();

            UpdateState = _availableUpdates.Count == 0
                ? RemoteContentState.Empty
                : RemoteContentState.Ready;

            Plugin.Log.LogInfo(
                "HMUC debug update manifest enabled with " +
                _availableUpdates.Count.ToString(
                    CultureInfo.InvariantCulture) +
                " test entries.");
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

            Plugin.Log.LogInfo(
                "HMUC debug news manifest enabled with " +
                _newsItems.Count.ToString(
                    CultureInfo.InvariantCulture) +
                " test entries.");
        }

        private static IEnumerator CheckNewsManifest()
        {
            NewsState = RemoteContentState.Loading;
            TitleScreenUpdatePanel.Refresh();

            UnityWebRequest request = null;

            try
            {
                request = UnityWebRequest.Get(NewsManifestUrl);
                request.timeout = RequestTimeoutSeconds;

                yield return request.SendWebRequest();

                if (request.isNetworkError || request.isHttpError)
                {
                    string failure =
                        "News check failed: HTTP " +
                        request.responseCode.ToString(
                            CultureInfo.InvariantCulture) +
                        " | " +
                        (request.error ?? "unknown error");

                    if (TryUseCachedNewsFallback(failure))
                    {
                        yield break;
                    }

                    NewsState = RemoteContentState.Error;
                    Plugin.Log.LogWarning(failure);
                    yield break;
                }

                if (request.downloadedBytes > MaximumNewsManifestBytes)
                {
                    const string failure =
                        "News manifest was rejected because it is too large.";

                    if (TryUseCachedNewsFallback(failure))
                    {
                        yield break;
                    }

                    NewsState = RemoteContentState.Error;
                    Plugin.Log.LogWarning(failure);
                    yield break;
                }

                string text = request.downloadHandler == null
                    ? null
                    : request.downloadHandler.text;

                NewsManifest manifest;
                string parseError;

                if (!NewsManifest.TryParse(
                        text,
                        out manifest,
                        out parseError))
                {
                    string failure =
                        "News manifest was rejected: " + parseError;

                    if (TryUseCachedNewsFallback(failure))
                    {
                        yield break;
                    }

                    NewsState = RemoteContentState.Error;
                    Plugin.Log.LogWarning(failure);
                    yield break;
                }

                ApplyNewsManifest(
                    manifest,
                    false);

                SaveNewsCache(text);
            }
            finally
            {
                if (request != null)
                {
                    request.Dispose();
                }

                TitleScreenUpdatePanel.Refresh();
            }
        }

        private static bool TryLoadFreshCachedNews()
        {
            if (!PlayerPrefs.HasKey(
                    LastSuccessfulNewsCheckKey))
            {
                return false;
            }

            long ticks;
            if (!long.TryParse(
                    PlayerPrefs.GetString(
                        LastSuccessfulNewsCheckKey),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out ticks) ||
                ticks <= 0)
            {
                return false;
            }

            try
            {
                DateTime lastCheckUtc =
                    new DateTime(
                        ticks,
                        DateTimeKind.Utc);

                DateTime nowUtc = DateTime.UtcNow;

                if (lastCheckUtc > nowUtc)
                {
                    Plugin.Log.LogWarning(
                        "Cached news-check timestamp is in the future; ignoring it.");

                    InvalidateSuccessfulNewsCheckTime();
                    return false;
                }

                if (nowUtc
                    .Subtract(lastCheckUtc)
                    .TotalHours >= CheckIntervalHours)
                {
                    return false;
                }
            }
            catch
            {
                InvalidateSuccessfulNewsCheckTime();
                return false;
            }

            NewsManifest manifest;

            if (!TryLoadCachedNewsManifest(
                    out manifest))
            {
                ClearNewsCache();
                return false;
            }

            ApplyNewsManifest(
                manifest,
                true);

            return true;
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

            if (!UpdateCheckerConfig.CheckEveryLaunch)
            {
                PlayerPrefs.SetString(
                    LastSuccessfulNewsCheckKey,
                    DateTime.UtcNow.Ticks.ToString(
                        CultureInfo.InvariantCulture));
            }

            PlayerPrefs.Save();
        }

        private static void InvalidateSuccessfulNewsCheckTime()
        {
            if (!PlayerPrefs.HasKey(
                    LastSuccessfulNewsCheckKey))
            {
                return;
            }

            if (PlayerPrefs.GetString(
                    LastSuccessfulNewsCheckKey) == "0")
            {
                return;
            }

            PlayerPrefs.SetString(
                LastSuccessfulNewsCheckKey,
                "0");

            PlayerPrefs.Save();
        }

        private static void ClearNewsCache()
        {
            PlayerPrefs.SetString(
                LastSuccessfulNewsCheckKey,
                "0");

            PlayerPrefs.SetString(
                CachedNewsManifestKey,
                string.Empty);

            PlayerPrefs.Save();
        }

        private static bool ShouldCheckNow()
        {
            if (UpdateCheckerConfig.CheckEveryLaunch)
            {
                InvalidateSuccessfulCheckTime();
                return true;
            }

            if (!PlayerPrefs.HasKey(LastSuccessfulCheckKey))
            {
                return true;
            }

            long ticks;
            if (!long.TryParse(
                    PlayerPrefs.GetString(LastSuccessfulCheckKey),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out ticks) ||
                ticks <= 0)
            {
                return true;
            }

            try
            {
                DateTime lastCheckUtc =
                    new DateTime(ticks, DateTimeKind.Utc);
                DateTime nowUtc = DateTime.UtcNow;

                if (lastCheckUtc > nowUtc)
                {
                    Plugin.Log.LogWarning(
                        "Cached update-check timestamp is in the future; ignoring it.");
                    return true;
                }

                return nowUtc
                    .Subtract(lastCheckUtc)
                    .TotalHours >= CheckIntervalHours;
            }
            catch
            {
                return true;
            }
        }

        private static void SortAvailableUpdates()
        {
            _availableUpdates.Sort(delegate(
                AvailableUpdate left,
                AvailableUpdate right)
            {
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

        private static void SaveSuccessfulCheckTime()
        {
            PlayerPrefs.SetString(
                LastSuccessfulCheckKey,
                DateTime.UtcNow.Ticks.ToString(
                    CultureInfo.InvariantCulture));

            PlayerPrefs.Save();
        }

        private static void InvalidateSuccessfulCheckTime()
        {
            if (!PlayerPrefs.HasKey(LastSuccessfulCheckKey))
            {
                return;
            }

            if (PlayerPrefs.GetString(LastSuccessfulCheckKey) == "0")
            {
                return;
            }

            PlayerPrefs.SetString(
                LastSuccessfulCheckKey,
                "0");

            PlayerPrefs.Save();
        }
    }

    internal sealed class AvailableUpdate
    {
        internal string Name { get; private set; }
        internal string Author { get; private set; }
        internal Version InstalledVersion { get; private set; }
        internal Version AvailableVersion { get; private set; }

        internal AvailableUpdate(
            string name,
            string author,
            Version installedVersion,
            Version availableVersion)
        {
            Name = name;
            Author = author;
            InstalledVersion = installedVersion;
            AvailableVersion = availableVersion;
        }
    }
}
