using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using System.Xml;
using BepInEx;
using BepInEx.Bootstrap;
using Mono.Cecil;
using UnityEngine;
using UnityEngine.Networking;

namespace HospitalModUpdateChecker
{
    internal enum PackageCatalogState
    {
        NotLoaded,
        Loading,
        Ready,
        Error
    }

    internal enum PackageOperationState
    {
        Idle,
        Downloading,
        Verifying,
        Extracting,
        PendingRestart,
        Error,
        Cancelled
    }

    internal sealed class PackageOperation
    {
        internal string Guid;
        internal PackageOperationState State;
        internal float Progress;
        internal int Attempt;
        internal string Error;
        internal bool CancelRequested;
        internal UnityWebRequest Request;
    }

    internal sealed class InstallResultDisplay
    {
        internal string Guid;
        internal string Name;
        internal string Version;
        internal string Status;
        internal string ConfigWarning;
        internal readonly List<string> AddedSettings =
            new List<string>();
    }

    internal static class BootstrapProvisioner
    {
        internal const string BootstrapFileName =
            "Loupito75.HospitalModUpdateChecker.Bootstrap.dll";

        internal const string BootstrapCoreFileName =
            "Loupito75.HospitalModUpdateChecker.Bootstrap.Core.dll";

        private static readonly Version BootstrapLoaderVersion =
            new Version(1, 0, 0, 0);

        internal static bool EnsureInstalled()
        {
            try
            {
                string pluginDirectory =
                    Path.GetDirectoryName(
                        typeof(Plugin).Assembly.Location);

                string source =
                    Path.Combine(
                        pluginDirectory ?? string.Empty,
                        BootstrapFileName);

                string core =
                    Path.Combine(
                        pluginDirectory ?? string.Empty,
                        BootstrapCoreFileName);

                if (!File.Exists(source) ||
                    !File.Exists(core))
                {
                    Plugin.Log.LogWarning(
                        "HMUC bootstrap loader/core payload is missing; automatic installation is disabled.");
                    return false;
                }

                Directory.CreateDirectory(
                    Paths.PatcherPluginPath);

                string destination =
                    Path.Combine(
                        Paths.PatcherPluginPath,
                        BootstrapFileName);

                if (File.Exists(destination) &&
                    IsCompatibleBootstrapLoader(
                        destination))
                {
                    return true;
                }

                try
                {
                    File.Copy(
                        source,
                        destination,
                        true);

                    if (!IsCompatibleBootstrapLoader(
                            destination))
                    {
                        Plugin.Log.LogWarning(
                            "HMUC bootstrap loader installation did not produce a compatible loader; automatic installation is disabled.");
                        return false;
                    }

                    Plugin.Log.LogInfo(
                        "HMUC stable update bootstrap loader installed in BepInEx/patchers. It will load the current bootstrap core on the next game start.");
                    return true;
                }
                catch (Exception exception)
                {
                    if (File.Exists(destination))
                    {
                        Plugin.Log.LogWarning(
                            "HMUC detected a legacy bootstrap already loaded in BepInEx/patchers. Automatic installation is disabled for this session. Close Project Hospital once, delete BepInEx/patchers/" +
                            BootstrapFileName +
                            ", then start the game again so HMUC can install the stable loader. Details: " +
                            exception.Message);
                        return false;
                    }

                    Plugin.Log.LogWarning(
                        "HMUC could not install its stable bootstrap loader: " +
                        exception.Message);
                    return false;
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    "HMUC bootstrap provisioning failed: " +
                    exception.Message);
                return false;
            }
        }

        private static bool IsCompatibleBootstrapLoader(
            string path)
        {
            try
            {
                AssemblyName name =
                    AssemblyName.GetAssemblyName(
                        path);

                return name != null &&
                    name.Version != null &&
                    name.Version.Equals(
                        BootstrapLoaderVersion);
            }
            catch
            {
                return false;
            }
        }

        internal static string ToHex(
            byte[] bytes)
        {
            char[] chars =
                new char[bytes.Length * 2];

            const string hex =
                "0123456789abcdef";

            for (int i = 0;
                i < bytes.Length;
                i++)
            {
                chars[i * 2] =
                    hex[(bytes[i] >> 4) & 0x0F];

                chars[i * 2 + 1] =
                    hex[bytes[i] & 0x0F];
            }

            return new string(chars);
        }
    }

    internal static class PackageUpdateManager
    {
        private const string PackageManifestUrl =
            "https://raw.githubusercontent.com/Loupito75/Project-Hospital-Mods/main/mod-packages.json";

        private const string PackageManifestSignatureUrl =
            "https://raw.githubusercontent.com/Loupito75/Project-Hospital-Mods/main/mod-packages.json.sig";

        private const string PackageManifestRevisionKey =
            "L75_HMUC_PACKAGE_MANIFEST_REVISION";

        private const string PackageManifestHashKey =
            "L75_HMUC_PACKAGE_MANIFEST_HASH";

        private const int ManifestTimeoutSeconds = 10;
        private const int MaximumManifestLifetimeDays = 120;
        private const int MaximumManifestFutureHours = 24;
        private const int DownloadTimeoutSeconds = 900;
        private const float DownloadStallSeconds = 30f;
        private const ulong MaximumManifestBytes = 131072UL;
        private const int MaximumDownloadAttempts = 3;

        private const string CacheFolderName =
            "Loupito75.HospitalModUpdateChecker";

        private static readonly Dictionary<string, PackageManifestEntry>
            _packages =
                new Dictionary<string, PackageManifestEntry>(
                    StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, PackageOperation>
            _operations =
                new Dictionary<string, PackageOperation>(
                    StringComparer.OrdinalIgnoreCase);

        private static readonly List<InstallResultDisplay>
            _lastResults =
                new List<InstallResultDisplay>();

        private static string _activeGuid;
        private static bool _bootstrapReady;

        internal static PackageCatalogState CatalogState
        {
            get;
            private set;
        }

        internal static IList<InstallResultDisplay> LastResults
        {
            get { return _lastResults.AsReadOnly(); }
        }

        static PackageUpdateManager()
        {
            CatalogState =
                PackageCatalogState.NotLoaded;
        }

        internal static void Initialize()
        {
            _bootstrapReady =
                BootstrapProvisioner.EnsureInstalled();

            string trustError;
            if (!PackageTrust.IsConfigured(
                    out trustError))
            {
                Plugin.Log.LogWarning(
                    "HMUC automatic package installation is disabled until the package signing public key is configured: " +
                    trustError);
            }

            LoadInstallResults();
            CleanupOrphanDownloads();
        }

        internal static IEnumerator RefreshCatalog()
        {
            IEnumerator core =
                RefreshCatalogCore();

            try
            {
                while (true)
                {
                    bool hasNext = false;
                    object current = null;
                    Exception failure = null;

                    try
                    {
                        hasNext =
                            core.MoveNext();

                        if (hasNext)
                        {
                            current =
                                core.Current;
                        }
                    }
                    catch (Exception exception)
                    {
                        failure =
                            exception;
                    }

                    if (failure != null)
                    {
                        CatalogState =
                            PackageCatalogState.Error;

                        Plugin.Log.LogWarning(
                            "Package catalog check failed: " +
                            failure.GetType().Name +
                            ": " +
                            failure.Message);

                        TitleScreenUpdatePanel.Refresh();
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    yield return current;
                }
            }
            finally
            {
                IDisposable disposable =
                    core as IDisposable;

                if (disposable != null)
                {
                    disposable.Dispose();
                }
            }
        }

        private static IEnumerator RefreshCatalogCore()
        {
            _packages.Clear();

            if (UpdateChecker.AvailableUpdates.Count == 0)
            {
                CatalogState =
                    PackageCatalogState.NotLoaded;
                yield break;
            }

            if (UpdateCheckerConfig.Debug)
            {
                CatalogState =
                    PackageCatalogState.NotLoaded;
                yield break;
            }

            string trustConfigurationError;
            if (!PackageTrust.IsConfigured(
                    out trustConfigurationError))
            {
                CatalogState =
                    PackageCatalogState.Error;

                Plugin.Log.LogWarning(
                    "Package catalog trust is not configured: " +
                    trustConfigurationError);

                TitleScreenUpdatePanel.Refresh();
                yield break;
            }

            CatalogState =
                PackageCatalogState.Loading;

            TitleScreenUpdatePanel.Refresh();

            UnityWebRequest request = null;
            UnityWebRequest signatureRequest = null;

            try
            {
                request =
                    UnityWebRequest.Get(
                        PackageManifestUrl);

                request.timeout =
                    ManifestTimeoutSeconds;

                yield return
                    request.SendWebRequest();

                if (request.isNetworkError ||
                    request.isHttpError)
                {
                    CatalogState =
                        PackageCatalogState.Error;

                    Plugin.Log.LogWarning(
                        "Package catalog unavailable: HTTP " +
                        request.responseCode.ToString(
                            CultureInfo.InvariantCulture) +
                        " | " +
                        (request.error ??
                         "unknown network error"));

                    yield break;
                }

                if (request.downloadedBytes >
                    MaximumManifestBytes)
                {
                    CatalogState =
                        PackageCatalogState.Error;

                    Plugin.Log.LogWarning(
                        "Package catalog was rejected because it is too large.");
                    yield break;
                }

                byte[] manifestBytes =
                    request.downloadHandler == null
                        ? null
                        : request.downloadHandler.data;

                if (manifestBytes == null ||
                    manifestBytes.Length == 0)
                {
                    CatalogState =
                        PackageCatalogState.Error;

                    Plugin.Log.LogWarning(
                        "Package catalog was rejected because its body is empty.");
                    yield break;
                }

                signatureRequest =
                    UnityWebRequest.Get(
                        PackageManifestSignatureUrl);

                signatureRequest.timeout =
                    ManifestTimeoutSeconds;

                yield return
                    signatureRequest.SendWebRequest();

                if (signatureRequest.isNetworkError ||
                    signatureRequest.isHttpError)
                {
                    CatalogState =
                        PackageCatalogState.Error;

                    Plugin.Log.LogWarning(
                        "Package catalog signature unavailable: HTTP " +
                        signatureRequest.responseCode.ToString(
                            CultureInfo.InvariantCulture) +
                        " | " +
                        (signatureRequest.error ??
                         "unknown network error"));

                    yield break;
                }

                if (signatureRequest.downloadedBytes >
                    (ulong)PackageTrust.SignatureFileMaximumBytes)
                {
                    CatalogState =
                        PackageCatalogState.Error;

                    Plugin.Log.LogWarning(
                        "Package catalog signature was rejected because it is too large.");
                    yield break;
                }

                byte[] signatureBytes =
                    signatureRequest.downloadHandler == null
                        ? null
                        : signatureRequest.downloadHandler.data;

                string trustError;

                if (!PackageTrust.TryVerify(
                        manifestBytes,
                        signatureBytes,
                        out trustError))
                {
                    CatalogState =
                        PackageCatalogState.Error;

                    Plugin.Log.LogWarning(
                        "Package catalog signature was rejected: " +
                        trustError);
                    yield break;
                }

                string text;
                string utf8Error;

                if (!TryDecodeUtf8(
                        manifestBytes,
                        out text,
                        out utf8Error))
                {
                    CatalogState =
                        PackageCatalogState.Error;

                    Plugin.Log.LogWarning(
                        "Signed package catalog is not valid UTF-8: " +
                        utf8Error);

                    yield break;
                }

                PackageManifest manifest;
                string parseError;

                if (!PackageManifest.TryParse(
                        text,
                        out manifest,
                        out parseError))
                {
                    CatalogState =
                        PackageCatalogState.Error;

                    Plugin.Log.LogWarning(
                        "Signed package catalog was rejected: " +
                        parseError);
                    yield break;
                }

                string freshnessError;
                if (!ValidateManifestFreshness(
                        manifest,
                        out freshnessError))
                {
                    CatalogState =
                        PackageCatalogState.Error;

                    Plugin.Log.LogWarning(
                        "Signed package catalog freshness check failed: " +
                        freshnessError);
                    yield break;
                }

                string rollbackError;
                if (!AcceptManifestRevision(
                        manifest,
                        manifestBytes,
                        out rollbackError))
                {
                    CatalogState =
                        PackageCatalogState.Error;

                    Plugin.Log.LogWarning(
                        "Signed package catalog anti-rollback check failed: " +
                        rollbackError);
                    yield break;
                }

                for (int i = 0;
                    i < manifest.Entries.Count;
                    i++)
                {
                    PackageManifestEntry entry =
                        manifest.Entries[i];

                    _packages[entry.Guid] =
                        entry;
                }

                CatalogState =
                    PackageCatalogState.Ready;
            }
            finally
            {
                if (request != null)
                {
                    request.Dispose();
                }

                if (signatureRequest != null)
                {
                    signatureRequest.Dispose();
                }

                TitleScreenUpdatePanel.Refresh();
            }
        }

        private static bool TryDecodeUtf8(
            byte[] bytes,
            out string text,
            out string error)
        {
            text = null;
            error = null;

            try
            {
                UTF8Encoding utf8 =
                    new UTF8Encoding(
                        false,
                        true);

                text =
                    utf8.GetString(
                        bytes);

                return true;
            }
            catch (Exception exception)
            {
                error =
                    exception.GetType().Name;

                return false;
            }
        }

        private static bool ValidateManifestFreshness(
            PackageManifest manifest,
            out string error)
        {
            error = null;

            if (manifest == null)
            {
                error =
                    "Package manifest is missing.";
                return false;
            }

            DateTime now =
                DateTime.UtcNow;

            if (manifest.ExpiresUtc <= now)
            {
                error =
                    "Package manifest expired at " +
                    manifest.ExpiresUtc.ToString(
                        "yyyy-MM-ddTHH:mm:ssZ",
                        CultureInfo.InvariantCulture) +
                    ".";
                return false;
            }

            if (manifest.UpdatedUtc >
                now.AddHours(
                    MaximumManifestFutureHours))
            {
                error =
                    "Package manifest updatedUtc is too far in the future.";
                return false;
            }

            if (manifest.ExpiresUtc >
                manifest.UpdatedUtc.AddDays(
                    MaximumManifestLifetimeDays))
            {
                error =
                    "Package manifest validity period is too long.";
                return false;
            }

            return true;
        }

        private static bool AcceptManifestRevision(
            PackageManifest manifest,
            byte[] manifestBytes,
            out string error)
        {
            error = null;

            long previousRevision = 0L;
            string previousRevisionText =
                PlayerPrefs.HasKey(
                    PackageManifestRevisionKey)
                    ? PlayerPrefs.GetString(
                        PackageManifestRevisionKey)
                    : string.Empty;

            if (!string.IsNullOrEmpty(
                    previousRevisionText))
            {
                long.TryParse(
                    previousRevisionText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out previousRevision);
            }

            string currentHash;

            using (SHA256Managed sha =
                new SHA256Managed())
            {
                currentHash =
                    BootstrapProvisioner.ToHex(
                        sha.ComputeHash(
                            manifestBytes));
            }

            string previousHash =
                PlayerPrefs.HasKey(
                    PackageManifestHashKey)
                    ? PlayerPrefs.GetString(
                        PackageManifestHashKey)
                    : string.Empty;

            if (previousRevision >
                manifest.ManifestRevision)
            {
                error =
                    "Package manifest revision " +
                    manifest.ManifestRevision.ToString(
                        CultureInfo.InvariantCulture) +
                    " is older than trusted revision " +
                    previousRevision.ToString(
                        CultureInfo.InvariantCulture) +
                    ".";
                return false;
            }

            if (previousRevision ==
                    manifest.ManifestRevision &&
                !string.IsNullOrEmpty(
                    previousHash) &&
                !string.Equals(
                    previousHash,
                    currentHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "Package manifest content changed without increasing manifestRevision.";
                return false;
            }

            if (previousRevision !=
                    manifest.ManifestRevision ||
                !string.Equals(
                    previousHash,
                    currentHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                PlayerPrefs.SetString(
                    PackageManifestRevisionKey,
                    manifest.ManifestRevision.ToString(
                        CultureInfo.InvariantCulture));

                PlayerPrefs.SetString(
                    PackageManifestHashKey,
                    currentHash);

                PlayerPrefs.Save();
            }

            return true;
        }

        internal static bool IsPackageAvailable(
            AvailableUpdate update)
        {
            PackageManifestEntry entry;

            return update != null &&
                _bootstrapReady &&
                CatalogState ==
                    PackageCatalogState.Ready &&
                _packages.TryGetValue(
                    update.Guid,
                    out entry) &&
                entry != null &&
                entry.Version != null &&
                entry.Version.Equals(
                    update.AvailableVersion) &&
                IsExpectedInstalledLocation(
                    update,
                    entry);
        }

        private static bool IsExpectedInstalledLocation(
            AvailableUpdate update,
            PackageManifestEntry package)
        {
            if (update == null ||
                package == null)
            {
                return false;
            }

            PluginInfo installed;

            if (!Chainloader.PluginInfos.TryGetValue(
                    update.Guid,
                    out installed) ||
                installed == null ||
                string.IsNullOrEmpty(
                    installed.Location))
            {
                return false;
            }

            try
            {
                string canonicalPluginRoot =
                    GetCanonicalPluginRoot();

                string configuredPluginRoot =
                    Path.GetFullPath(
                        Paths.PluginPath);

                if (!string.Equals(
                        canonicalPluginRoot,
                        configuredPluginRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string expected =
                    Path.GetFullPath(
                        Path.Combine(
                            Path.Combine(
                                canonicalPluginRoot,
                                package.InstallDirectory),
                            package.PluginFile.Replace(
                                '/',
                                Path.DirectorySeparatorChar)));

                string actual =
                    Path.GetFullPath(
                        installed.Location);

                return string.Equals(
                    expected,
                    actual,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static PackageOperation GetOperation(
            string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            PackageOperation operation;

            return _operations.TryGetValue(
                    guid,
                    out operation)
                ? operation
                : null;
        }

        internal static bool IsAnotherOperationRunning(
            string guid)
        {
            return !string.IsNullOrEmpty(_activeGuid) &&
                !string.Equals(
                    _activeGuid,
                    guid,
                    StringComparison.OrdinalIgnoreCase);
        }

        internal static void StartOrCancel(
            AvailableUpdate update)
        {
            if (update == null ||
                Plugin.Instance == null)
            {
                return;
            }

            PackageOperation existing =
                GetOperation(
                    update.Guid);

            if (existing != null &&
                existing.State ==
                    PackageOperationState.Downloading)
            {
                existing.CancelRequested = true;

                if (existing.Request != null)
                {
                    existing.Request.Abort();
                }

                return;
            }

            if (IsAnotherOperationRunning(
                    update.Guid))
            {
                return;
            }

            if (CatalogState ==
                    PackageCatalogState.Error ||
                CatalogState ==
                    PackageCatalogState.NotLoaded)
            {
                CatalogState =
                    PackageCatalogState.Loading;

                TitleScreenUpdatePanel.Refresh();

                Plugin.Instance.StartCoroutine(
                    RetryCatalogAndStart(
                        update));
                return;
            }

            PackageManifestEntry package;

            if (!_packages.TryGetValue(
                    update.Guid,
                    out package) ||
                package == null ||
                !package.Version.Equals(
                    update.AvailableVersion) ||
                !IsExpectedInstalledLocation(
                    update,
                    package))
            {
                return;
            }

            Plugin.Instance.StartCoroutine(
                DownloadAndStage(
                    update,
                    package));
        }

        private static IEnumerator RetryCatalogAndStart(
            AvailableUpdate update)
        {
            if (update == null)
            {
                yield break;
            }

            IEnumerator catalog =
                RefreshCatalog();

            while (catalog.MoveNext())
            {
                yield return catalog.Current;
            }

            if (CatalogState !=
                    PackageCatalogState.Ready)
            {
                yield break;
            }

            PackageManifestEntry package;

            if (!_packages.TryGetValue(
                    update.Guid,
                    out package) ||
                package == null ||
                package.Version == null ||
                !package.Version.Equals(
                    update.AvailableVersion) ||
                !IsExpectedInstalledLocation(
                    update,
                    package))
            {
                yield break;
            }

            IEnumerator download =
                DownloadAndStage(
                    update,
                    package);

            while (download.MoveNext())
            {
                yield return download.Current;
            }
        }

        private static IEnumerator DownloadAndStage(
            AvailableUpdate update,
            PackageManifestEntry package)
        {
            IEnumerator core =
                DownloadAndStageCore(
                    update,
                    package);

            try
            {
                while (true)
                {
                    bool hasNext = false;
                    object current = null;
                    Exception failure = null;

                    try
                    {
                        hasNext =
                            core.MoveNext();

                        if (hasNext)
                        {
                            current =
                                core.Current;
                        }
                    }
                    catch (Exception exception)
                    {
                        failure =
                            exception;
                    }

                    if (failure != null)
                    {
                        Plugin.Log.LogWarning(
                            "HMUC package preparation failed for " +
                            (package == null
                                ? "unknown package"
                                : package.Name) +
                            ": " +
                            failure);

                        PackageOperation operation;

                        if (update != null &&
                            _operations.TryGetValue(
                                update.Guid,
                                out operation) &&
                            operation != null)
                        {
                            Fail(
                                operation,
                                UpdatePanelLocalization.Get(
                                    UpdatePanelLocalization.InstallPreparationErrorId));
                        }

                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    yield return current;
                }
            }
            finally
            {
                IDisposable disposable =
                    core as IDisposable;

                if (disposable != null)
                {
                    disposable.Dispose();
                }
            }
        }

        private static IEnumerator DownloadAndStageCore(
            AvailableUpdate update,
            PackageManifestEntry package)
        {
            PackageOperation operation =
                new PackageOperation();

            operation.Guid = update.Guid;
            operation.State =
                PackageOperationState.Downloading;
            operation.Progress = 0f;

            _operations[update.Guid] =
                operation;

            _activeGuid =
                update.Guid;

            string cacheRoot =
                GetCacheRoot();

            string downloads =
                Path.Combine(
                    cacheRoot,
                    "downloads");

            string stagedRoot =
                Path.Combine(
                    cacheRoot,
                    "staged");

            Directory.CreateDirectory(
                downloads);

            Directory.CreateDirectory(
                stagedRoot);

            string token =
                MakeSafeToken(
                    package.Guid) +
                "-" +
                package.Version;

            string zipPath =
                Path.Combine(
                    downloads,
                    token + ".zip");

            string stagingDirectory =
                Path.Combine(
                    stagedRoot,
                    token);

            try
            {
                string diskError;
                if (!HasEnoughDiskSpace(
                        cacheRoot,
                        package,
                        out diskError))
                {
                    Fail(
                        operation,
                        diskError);
                    yield break;
                }

                bool downloaded = false;

                for (int attempt = 1;
                    attempt <= MaximumDownloadAttempts;
                    attempt++)
                {
                    operation.Attempt =
                        attempt;

                    operation.State =
                        PackageOperationState.Downloading;

                    operation.Progress = 0f;
                    operation.Error = null;
                    operation.CancelRequested = false;

                    DeleteFileSafe(zipPath);

                    UnityWebRequest request =
                        new UnityWebRequest(
                            package.DownloadUrl,
                            UnityWebRequest.kHttpVerbGET);

                    DownloadHandlerFile handler =
                        new DownloadHandlerFile(
                            zipPath);

                    handler.removeFileOnAbort =
                        true;

                    request.downloadHandler =
                        handler;

                    request.timeout =
                        DownloadTimeoutSeconds;
                    request.redirectLimit = 5;

                    operation.Request =
                        request;

                    UnityWebRequestAsyncOperation async =
                        request.SendWebRequest();

                    bool oversized = false;
                    bool stalled = false;
                    ulong lastDownloadedBytes = 0UL;
                    float lastDownloadActivity =
                        Time.realtimeSinceStartup;

                    while (!async.isDone)
                    {
                        if (operation.CancelRequested)
                        {
                            request.Abort();
                        }

                        ulong currentDownloadedBytes =
                            request.downloadedBytes;

                        if (currentDownloadedBytes >
                            lastDownloadedBytes)
                        {
                            lastDownloadedBytes =
                                currentDownloadedBytes;
                            lastDownloadActivity =
                                Time.realtimeSinceStartup;
                        }
                        else if (!operation.CancelRequested &&
                            !stalled &&
                            Time.realtimeSinceStartup -
                                lastDownloadActivity >=
                                DownloadStallSeconds)
                        {
                            stalled = true;
                            request.Abort();

                            Plugin.Log.LogWarning(
                                "HMUC package download stalled for " +
                                package.Name +
                                "; retrying.");
                        }

                        if (currentDownloadedBytes >
                            (ulong)package.PackageBytes)
                        {
                            oversized = true;
                            request.Abort();
                        }

                        float progress =
                            request.downloadProgress;

                        if (progress < 0f)
                        {
                            progress = 0f;
                        }

                        operation.Progress =
                            Math.Min(
                                0.72f,
                                progress * 0.72f);

                        TitleScreenUpdatePanel
                            .RefreshInstallProgress();

                        yield return null;
                    }

                    operation.Request = null;

                    if (operation.CancelRequested)
                    {
                        request.Dispose();
                        operation.State =
                            PackageOperationState.Cancelled;
                        operation.Progress = 0f;

                        DeleteFileSafe(zipPath);

                        TitleScreenUpdatePanel.Refresh();
                        yield break;
                    }

                    bool failed =
                        oversized ||
                        stalled ||
                        request.isNetworkError ||
                        request.isHttpError ||
                        !File.Exists(zipPath) ||
                        !IsApprovedDownloadResponseUrl(
                            request.url);

                    string requestError =
                        stalled
                            ? "download stalled"
                            : request.error;

                    long downloadedBytes =
                        File.Exists(zipPath)
                            ? new FileInfo(zipPath).Length
                            : 0L;

                    request.Dispose();

                    if (!failed &&
                        downloadedBytes ==
                            package.PackageBytes)
                    {
                        downloaded = true;
                        break;
                    }

                    Plugin.Log.LogWarning(
                        "HMUC package download attempt " +
                        attempt.ToString(
                            CultureInfo.InvariantCulture) +
                        "/" +
                        MaximumDownloadAttempts.ToString(
                            CultureInfo.InvariantCulture) +
                        " failed for " +
                        package.Name +
                        ": " +
                        (requestError ??
                         "unexpected package size"));

                    DeleteFileSafe(zipPath);

                    if (attempt <
                        MaximumDownloadAttempts)
                    {
                        yield return null;
                    }
                }

                if (!downloaded)
                {
                    Fail(
                        operation,
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.DownloadErrorId));
                    yield break;
                }

                operation.State =
                    PackageOperationState.Verifying;
                operation.Progress = 0.76f;

                TitleScreenUpdatePanel.Refresh();

                PackagePreparationWorker worker =
                    new PackagePreparationWorker(
                        package,
                        zipPath,
                        stagingDirectory);

                worker.Start();

                while (!worker.IsDone)
                {
                    if (worker.Phase == 1)
                    {
                        operation.State =
                            PackageOperationState.Verifying;
                        operation.Progress = 0.80f;
                    }
                    else if (worker.Phase >= 2)
                    {
                        operation.State =
                            PackageOperationState.Extracting;
                        operation.Progress = 0.90f;
                    }

                    TitleScreenUpdatePanel
                        .RefreshInstallProgress();

                    yield return null;
                }

                if (!worker.Success)
                {
                    if (!string.IsNullOrEmpty(worker.Error))
                    {
                        Plugin.Log.LogWarning(
                            "Package preparation failed for " +
                            package.Name +
                            ": " +
                            worker.Error);
                    }

                    try
                    {
                        if (Directory.Exists(stagingDirectory))
                        {
                            Directory.Delete(
                                stagingDirectory,
                                true);
                        }
                    }
                    catch
                    {
                    }

                    Fail(
                        operation,
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.InstallPreparationErrorId));
                    yield break;
                }

                string relativeStaging =
                    MakeRelativeToCache(
                        cacheRoot,
                        stagingDirectory);

                PendingInstallQueue.Queue(
                    package,
                    relativeStaging);

                operation.State =
                    PackageOperationState.PendingRestart;
                operation.Progress = 1f;

                TitleScreenUpdatePanel.Refresh();
            }
            finally
            {
                operation.Request = null;

                if (string.Equals(
                        _activeGuid,
                        update.Guid,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _activeGuid = null;
                }

                DeleteFileSafe(zipPath);

                TitleScreenUpdatePanel.Refresh();
            }
        }

        private static void Fail(
            PackageOperation operation,
            string error)
        {
            operation.State =
                PackageOperationState.Error;

            operation.Error =
                error ?? string.Empty;

            operation.Progress = 0f;

            TitleScreenUpdatePanel.Refresh();
        }

        private static bool IsApprovedDownloadResponseUrl(
            string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            Uri uri;

            if (!Uri.TryCreate(
                    value,
                    UriKind.Absolute,
                    out uri) ||
                !string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string host =
                (uri.Host ?? string.Empty)
                .ToLowerInvariant();

            return host == "github.com" ||
                host == "release-assets.githubusercontent.com" ||
                host.EndsWith(
                    ".githubusercontent.com",
                    StringComparison.Ordinal);
        }

        private static bool HasEnoughDiskSpace(
            string cacheRoot,
            PackageManifestEntry package,
            out string error)
        {
            error = null;

            try
            {
                string root =
                    Path.GetPathRoot(
                        Path.GetFullPath(
                            cacheRoot));

                if (string.IsNullOrEmpty(root))
                {
                    return true;
                }

                long availableFreeSpace;
                string freeSpaceError;

                if (!TryGetAvailableFreeSpace(
                        root,
                        out availableFreeSpace,
                        out freeSpaceError))
                {
                    Plugin.Log.LogWarning(
                        "HMUC could not query free disk space; download will continue: " +
                        freeSpaceError);

                    return true;
                }

                long currentInstallBytes =
                    GetDirectorySizeSafe(
                        Path.Combine(
                            GetCanonicalPluginRoot(),
                            package.InstallDirectory));

                long candidateBytes =
                    Math.Max(
                        currentInstallBytes,
                        package.MaximumUnpackedBytes);

                long required =
                    package.PackageBytes +
                    package.MaximumUnpackedBytes +
                    candidateBytes +
                    16777216L;

                if (availableFreeSpace <
                    required)
                {
                    error =
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.NotEnoughSpaceId);

                    return false;
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    "HMUC could not query free disk space; download will continue: " +
                    exception.Message);
            }

            return true;
        }

        private static bool TryGetAvailableFreeSpace(
            string root,
            out long availableBytes,
            out string error)
        {
            availableBytes = 0L;
            error = null;

            if (Environment.OSVersion.Platform ==
                PlatformID.Win32NT)
            {
                try
                {
                    ulong freeBytesAvailable;
                    ulong totalNumberOfBytes;
                    ulong totalNumberOfFreeBytes;

                    if (!NativeMethods.GetDiskFreeSpaceEx(
                            root,
                            out freeBytesAvailable,
                            out totalNumberOfBytes,
                            out totalNumberOfFreeBytes))
                    {
                        error =
                            "GetDiskFreeSpaceEx failed with Win32 error " +
                            Marshal.GetLastWin32Error().ToString(
                                CultureInfo.InvariantCulture) +
                            ".";
                        return false;
                    }

                    availableBytes =
                        freeBytesAvailable >
                            (ulong)long.MaxValue
                            ? long.MaxValue
                            : (long)freeBytesAvailable;

                    return true;
                }
                catch (Exception exception)
                {
                    error =
                        exception.GetType().Name +
                        ": " +
                        exception.Message;
                    return false;
                }
            }

            try
            {
                DriveInfo drive =
                    new DriveInfo(root);

                availableBytes =
                    drive.AvailableFreeSpace;

                return true;
            }
            catch (Exception exception)
            {
                error =
                    exception.GetType().Name +
                    ": " +
                    exception.Message;
                return false;
            }
        }

        private static class NativeMethods
        {
            [DllImport(
                "kernel32.dll",
                CharSet = CharSet.Unicode,
                SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetDiskFreeSpaceEx(
                string directoryName,
                out ulong freeBytesAvailable,
                out ulong totalNumberOfBytes,
                out ulong totalNumberOfFreeBytes);
        }

        private static string GetCanonicalPluginRoot()
        {
            return Path.GetFullPath(
                Path.Combine(
                    Paths.BepInExRootPath,
                    "plugins"));
        }

        private static long GetDirectorySizeSafe(
            string directory)
        {
            if (string.IsNullOrEmpty(directory) ||
                !Directory.Exists(directory))
            {
                return 0L;
            }

            try
            {
                long total = 0L;

                string[] files =
                    Directory.GetFiles(
                        directory,
                        "*",
                        SearchOption.AllDirectories);

                for (int i = 0; i < files.Length; i++)
                {
                    total +=
                        new FileInfo(
                            files[i]).Length;

                    if (total < 0)
                    {
                        return long.MaxValue / 4L;
                    }
                }

                return total;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    "HMUC could not measure the current installation size; using the package bounds only: " +
                    exception.Message);

                return 0L;
            }
        }

        private static string GetCacheRoot()
        {
            string root =
                Path.Combine(
                    Paths.CachePath,
                    CacheFolderName);

            Directory.CreateDirectory(root);
            return root;
        }

        private static string MakeRelativeToCache(
            string cacheRoot,
            string path)
        {
            string root =
                Path.GetFullPath(cacheRoot)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            string full =
                Path.GetFullPath(path);

            if (!full.StartsWith(
                    root +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Staging path is outside the HMUC cache.");
            }

            return full
                .Substring(root.Length + 1)
                .Replace(
                    Path.DirectorySeparatorChar,
                    '/');
        }

        private static string MakeSafeToken(
            string value)
        {
            char[] chars =
                (value ?? "package")
                .ToCharArray();

            for (int i = 0;
                i < chars.Length;
                i++)
            {
                char c = chars[i];

                if (!(
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') ||
                    c == '.' ||
                    c == '-' ||
                    c == '_'))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private static void DeleteFileSafe(
            string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) &&
                    File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void CleanupOrphanDownloads()
        {
            try
            {
                string downloads =
                    Path.Combine(
                        GetCacheRoot(),
                        "downloads");

                if (!Directory.Exists(downloads))
                {
                    return;
                }

                string[] files =
                    Directory.GetFiles(
                        downloads,
                        "*.zip",
                        SearchOption.TopDirectoryOnly);

                for (int i = 0;
                    i < files.Length;
                    i++)
                {
                    DeleteFileSafe(
                        files[i]);
                }
            }
            catch
            {
            }
        }

        private static void LoadInstallResults()
        {
            _lastResults.Clear();

            string path =
                Path.Combine(
                    GetCacheRoot(),
                    "install-results.xml");

            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                XmlDocument document =
                    new XmlDocument();

                document.XmlResolver = null;
                document.Load(path);

                if (document.DocumentType != null)
                {
                    throw new FormatException(
                        "DTD declarations are not allowed.");
                }

                XmlElement root =
                    document.DocumentElement;

                if (root == null ||
                    root.Name !=
                        "HospitalModUpdateResults" ||
                    root.GetAttribute(
                        "formatVersion") != "1")
                {
                    throw new FormatException(
                        "Unsupported install result format.");
                }

                for (int i = 0;
                    i < root.ChildNodes.Count;
                    i++)
                {
                    XmlElement node =
                        root.ChildNodes[i]
                        as XmlElement;

                    if (node == null)
                    {
                        continue;
                    }

                    if (node.Name != "Result")
                    {
                        continue;
                    }

                    InstallResultDisplay result =
                        new InstallResultDisplay();

                    result.Guid =
                        node.GetAttribute("guid");

                    result.Name =
                        node.GetAttribute("name");

                    result.Version =
                        node.GetAttribute("version");

                    result.Status =
                        node.GetAttribute("status");

                    result.ConfigWarning =
                        node.GetAttribute(
                            "configWarning");

                    for (int j = 0;
                        j < node.ChildNodes.Count;
                        j++)
                    {
                        XmlElement setting =
                            node.ChildNodes[j]
                            as XmlElement;

                        if (setting != null &&
                            setting.Name ==
                                "AddedSetting")
                        {
                            string value =
                                (setting.InnerText ??
                                 string.Empty)
                                .Trim();

                            if (value.Length > 0)
                            {
                                result.AddedSettings.Add(
                                    value);
                            }
                        }
                    }

                    _lastResults.Add(
                        result);
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    "HMUC could not read the previous update result: " +
                    exception.Message);
            }
            finally
            {
                DeleteFileSafe(path);
            }
        }

        private sealed class PackagePreparationWorker
        {
            private readonly PackageManifestEntry _package;
            private readonly string _zipPath;
            private readonly string _stagingDirectory;

            private volatile bool _done;
            private volatile bool _success;
            private volatile int _phase;
            private string _error;

            internal bool IsDone
            {
                get { return _done; }
            }

            internal bool Success
            {
                get { return _success; }
            }

            internal int Phase
            {
                get { return _phase; }
            }

            internal string Error
            {
                get { return _error; }
            }

            internal PackagePreparationWorker(
                PackageManifestEntry package,
                string zipPath,
                string stagingDirectory)
            {
                _package = package;
                _zipPath = zipPath;
                _stagingDirectory =
                    stagingDirectory;
            }

            internal void Start()
            {
                Thread thread =
                    new Thread(Run);

                thread.IsBackground = true;
                thread.Name =
                    "HMUC package verification";

                thread.Start();
            }

            private void Run()
            {
                try
                {
                    _phase = 1;

                    string hash =
                        ComputeSha256(
                            _zipPath);

                    if (!string.Equals(
                            hash,
                            _package.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _error =
                            "Downloaded package SHA-256 does not match the release manifest.";
                        return;
                    }

                    _phase = 2;

                    string extractionError;

                    if (!SafeZipExtractor.Extract(
                            _zipPath,
                            _stagingDirectory,
                            _package.ArchiveRoot,
                            _package.MaximumUnpackedBytes,
                            out extractionError))
                    {
                        _error =
                            extractionError;
                        return;
                    }

                    if (!VerifyStagedOwnership(
                            _package,
                            _stagingDirectory,
                            out _error))
                    {
                        return;
                    }

                    _success = true;
                }
                catch (Exception exception)
                {
                    _error =
                        exception.GetType().Name +
                        ": " +
                        exception.Message;
                }
                finally
                {
                    _done = true;
                }
            }

            private static string ComputeSha256(
                string path)
            {
                using (FileStream stream =
                    new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read))
                using (SHA256Managed sha =
                    new SHA256Managed())
                {
                    byte[] hash =
                        sha.ComputeHash(stream);

                    return BootstrapProvisioner
                        .ToHex(hash);
                }
            }

            private static bool VerifyStagedOwnership(
                PackageManifestEntry package,
                string stagingDirectory,
                out string error)
            {
                error = null;

                for (int i = 0;
                    i <
                    package.RuntimeDirectories.Count;
                    i++)
                {
                    string path =
                        SafeChild(
                            stagingDirectory,
                            package.RuntimeDirectories[i]);

                    if (path == null ||
                        !Directory.Exists(path))
                    {
                        error =
                            "Package is missing runtime directory: " +
                            package.RuntimeDirectories[i];
                        return false;
                    }
                }

                for (int i = 0;
                    i < package.RuntimeFiles.Count;
                    i++)
                {
                    string path =
                        SafeChild(
                            stagingDirectory,
                            package.RuntimeFiles[i]);

                    if (path == null ||
                        !File.Exists(path))
                    {
                        error =
                            "Package is missing runtime file: " +
                            package.RuntimeFiles[i];
                        return false;
                    }
                }

                string config =
                    SafeChild(
                        stagingDirectory,
                        package.ConfigFile);

                if (config == null ||
                    !File.Exists(config))
                {
                    error =
                        "Package is missing its configuration template.";
                    return false;
                }

                string pluginPath =
                    SafeChild(
                        stagingDirectory,
                        package.PluginFile);

                if (pluginPath == null ||
                    !File.Exists(pluginPath))
                {
                    error =
                        "Package is missing its declared plugin DLL.";
                    return false;
                }

                if (!VerifyPluginIdentity(
                        package,
                        pluginPath,
                        out error))
                {
                    return false;
                }

                return true;
            }

            private static bool VerifyPluginIdentity(
                PackageManifestEntry package,
                string pluginPath,
                out string error)
            {
                error = null;

                try
                {
                    using (AssemblyDefinition assembly =
                        AssemblyDefinition.ReadAssembly(
                            pluginPath))
                    {
                        if (FindMatchingPluginType(
                                assembly.MainModule.Types,
                                package.Guid,
                                package.Version))
                        {
                            return true;
                        }
                    }
                }
                catch (Exception exception)
                {
                    error =
                        "Plugin identity verification failed: " +
                        exception.GetType().Name +
                        ": " +
                        exception.Message;

                    return false;
                }

                error =
                    "The staged DLL does not declare the expected BepInEx GUID/version.";
                return false;
            }

            private static bool FindMatchingPluginType(
                IEnumerable<TypeDefinition> types,
                string expectedGuid,
                Version expectedVersion)
            {
                foreach (TypeDefinition type in types)
                {
                    for (int i = 0;
                        i < type.CustomAttributes.Count;
                        i++)
                    {
                        CustomAttribute attribute =
                            type.CustomAttributes[i];

                        if (attribute == null ||
                            attribute.AttributeType == null ||
                            attribute.AttributeType.FullName !=
                                "BepInEx.BepInPlugin" ||
                            attribute.ConstructorArguments.Count < 3)
                        {
                            continue;
                        }

                        string guid =
                            attribute.ConstructorArguments[0]
                                .Value as string;

                        string versionText =
                            attribute.ConstructorArguments[2]
                                .Value as string;

                        Version version = null;

                        try
                        {
                            if (!string.IsNullOrEmpty(
                                    versionText))
                            {
                                version =
                                    new Version(
                                        versionText);
                            }
                        }
                        catch
                        {
                            version = null;
                        }

                        if (string.Equals(
                                guid,
                                expectedGuid,
                                StringComparison.OrdinalIgnoreCase) &&
                            version != null &&
                            version.Equals(
                                expectedVersion))
                        {
                            return true;
                        }
                    }

                    if (type.HasNestedTypes &&
                        FindMatchingPluginType(
                            type.NestedTypes,
                            expectedGuid,
                            expectedVersion))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static string SafeChild(
                string root,
                string relative)
            {
                string fullRoot =
                    Path.GetFullPath(root)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

                string full =
                    Path.GetFullPath(
                        Path.Combine(
                            fullRoot,
                            relative.Replace(
                                '/',
                                Path.DirectorySeparatorChar)));

                if (!full.StartsWith(
                        fullRoot +
                        Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return full;
            }
        }
    }

    internal static class PendingInstallQueue
    {
        private const string CacheFolderName =
            "Loupito75.HospitalModUpdateChecker";

        internal static void Queue(
            PackageManifestEntry package,
            string stagingDirectory)
        {
            string cacheRoot =
                Path.Combine(
                    Paths.CachePath,
                    CacheFolderName);

            Directory.CreateDirectory(
                cacheRoot);

            string path =
                Path.Combine(
                    cacheRoot,
                    "pending-install.xml");

            XmlDocument document =
                new XmlDocument();

            document.XmlResolver = null;

            XmlElement root;

            if (File.Exists(path))
            {
                document.Load(path);

                if (document.DocumentType != null)
                {
                    throw new FormatException(
                        "DTD declarations are not allowed.");
                }

                root =
                    document.DocumentElement;

                if (root == null ||
                    root.Name !=
                        "HospitalModUpdateTransaction" ||
                    root.GetAttribute(
                        "formatVersion") != "1")
                {
                    throw new FormatException(
                        "Existing pending install file has an unsupported format.");
                }
            }
            else
            {
                root =
                    document.CreateElement(
                        "HospitalModUpdateTransaction");

                root.SetAttribute(
                    "formatVersion",
                    "1");

                document.AppendChild(root);
            }

            for (int i =
                    root.ChildNodes.Count - 1;
                i >= 0;
                i--)
            {
                XmlElement existing =
                    root.ChildNodes[i]
                    as XmlElement;

                if (existing != null &&
                    existing.Name ==
                        "Operation" &&
                    string.Equals(
                        existing.GetAttribute(
                            "guid"),
                        package.Guid,
                        StringComparison.OrdinalIgnoreCase))
                {
                    root.RemoveChild(
                        existing);
                }
            }

            XmlElement operation =
                document.CreateElement(
                    "Operation");

            operation.SetAttribute(
                "guid",
                package.Guid);

            operation.SetAttribute(
                "name",
                package.Name);

            operation.SetAttribute(
                "version",
                package.Version.ToString());

            operation.SetAttribute(
                "installDirectory",
                package.InstallDirectory);

            operation.SetAttribute(
                "pluginFile",
                package.PluginFile);

            operation.SetAttribute(
                "configFile",
                package.ConfigFile);

            operation.SetAttribute(
                "stagingDirectory",
                stagingDirectory);

            XmlElement runtimeDirectories =
                document.CreateElement(
                    "RuntimeDirectories");

            for (int i = 0;
                i <
                package.RuntimeDirectories.Count;
                i++)
            {
                XmlElement item =
                    document.CreateElement(
                        "Directory");

                item.InnerText =
                    package.RuntimeDirectories[i];

                runtimeDirectories.AppendChild(
                    item);
            }

            operation.AppendChild(
                runtimeDirectories);

            XmlElement runtimeFiles =
                document.CreateElement(
                    "RuntimeFiles");

            for (int i = 0;
                i <
                package.RuntimeFiles.Count;
                i++)
            {
                XmlElement item =
                    document.CreateElement(
                        "File");

                item.InnerText =
                    package.RuntimeFiles[i];

                runtimeFiles.AppendChild(
                    item);
            }

            operation.AppendChild(
                runtimeFiles);

            XmlElement removeFiles =
                document.CreateElement(
                    "RemoveFiles");

            for (int i = 0;
                i < package.RemoveFiles.Count;
                i++)
            {
                XmlElement item =
                    document.CreateElement(
                        "File");

                item.InnerText =
                    package.RemoveFiles[i];

                removeFiles.AppendChild(
                    item);
            }

            operation.AppendChild(
                removeFiles);

            root.AppendChild(operation);

            string temporary =
                path + ".tmp";

            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            XmlWriterSettings settings =
                new XmlWriterSettings();

            settings.Encoding =
                new System.Text.UTF8Encoding(false);
            settings.Indent = true;

            using (XmlWriter writer =
                XmlWriter.Create(
                    temporary,
                    settings))
            {
                document.Save(writer);
            }

            string previous =
                path + ".previous";

            if (File.Exists(previous))
            {
                File.Delete(previous);
            }

            if (File.Exists(path))
            {
                File.Move(
                    path,
                    previous);
            }

            try
            {
                File.Move(
                    temporary,
                    path);

                if (File.Exists(previous))
                {
                    File.Delete(previous);
                }
            }
            catch
            {
                if (!File.Exists(path) &&
                    File.Exists(previous))
                {
                    File.Move(
                        previous,
                        path);
                }

                throw;
            }
        }
    }
}
