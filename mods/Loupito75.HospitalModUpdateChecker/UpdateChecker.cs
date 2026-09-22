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
    internal static class UpdateChecker
    {
        private const string ManifestUrl =
            "https://raw.githubusercontent.com/Loupito75/Project-Hospital-Mods/main/mod-updates.json";

        private const int RequestTimeoutSeconds = 10;
        private const ulong MaximumManifestBytes = 65536UL;
        private const double CheckIntervalHours = 24.0;
        private const string LastSuccessfulCheckKey =
            "Loupito75.HospitalModUpdateChecker.LastSuccessfulCheckUtcTicks";

        private static readonly List<AvailableUpdate> _availableUpdates =
            new List<AvailableUpdate>();

        internal static IList<AvailableUpdate> AvailableUpdates
        {
            get { return _availableUpdates.AsReadOnly(); }
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
                    Plugin.Log.LogWarning(
                        "Update check stopped after an unexpected error: " +
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
            if (!ShouldCheckNow())
            {
                yield break;
            }

            UnityWebRequest request = null;

            try
            {
                request = UnityWebRequest.Get(ManifestUrl);
                request.timeout = RequestTimeoutSeconds;

                yield return request.SendWebRequest();

                if (request.isNetworkError || request.isHttpError)
                {
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

                    AvailableUpdate update = new AvailableUpdate(
                        remote.Name,
                        remote.Author,
                        localVersion,
                        remote.Version);

                    _availableUpdates.Add(update);
                }

                TitleScreenUpdatePanel.Refresh(AvailableUpdates);

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
            }
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
