using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace HospitalModUpdateChecker
{
    internal sealed class UnifiedCatalogModEntry
    {
        internal UpdateManifestEntry Update { get; private set; }
        internal PackageManifestEntry Package { get; private set; }
        internal string Description { get; private set; }
        internal string WhatsNew { get; private set; }

        internal UnifiedCatalogModEntry(
            UpdateManifestEntry update,
            PackageManifestEntry package,
            string description,
            string whatsNew)
        {
            Update = update;
            Package = package;
            Description = description ?? string.Empty;
            WhatsNew = whatsNew ?? string.Empty;
        }
    }

    internal sealed class UnifiedModCatalog
    {
        internal const int SupportedFormatVersion = 1;
        internal const int MaximumMods = 256;
        internal const int MaximumNews = 64;

        internal long ManifestRevision { get; private set; }
        internal DateTime UpdatedUtc { get; private set; }
        internal DateTime ExpiresUtc { get; private set; }
        internal List<UnifiedCatalogModEntry> Mods { get; private set; }
        internal List<NewsManifestEntry> News { get; private set; }

        private UnifiedModCatalog()
        {
            Mods = new List<UnifiedCatalogModEntry>();
            News = new List<NewsManifestEntry>();
        }

        internal static bool TryParse(
            string text,
            out UnifiedModCatalog catalog,
            out string error)
        {
            catalog = null;
            error = null;

            if (string.IsNullOrEmpty(text))
            {
                error = "Unified catalog is empty.";
                return false;
            }

            object rootValue;
            if (!SimpleJsonParser.TryParse(
                    text,
                    out rootValue,
                    out error))
            {
                return false;
            }

            Dictionary<string, object> root =
                rootValue as Dictionary<string, object>;

            if (root == null)
            {
                error =
                    "Unified catalog root must be a JSON object.";
                return false;
            }

            long formatVersion;
            long manifestRevision;
            string updatedUtcText;
            string expiresUtcText;
            object modsValue;
            object newsValue;

            if (!TryGetRequiredLong(
                    root,
                    "formatVersion",
                    out formatVersion) ||
                formatVersion != SupportedFormatVersion)
            {
                error =
                    "Unsupported or invalid unified catalog formatVersion.";
                return false;
            }

            if (!TryGetRequiredLong(
                    root,
                    "manifestRevision",
                    out manifestRevision) ||
                manifestRevision < 1L)
            {
                error =
                    "Invalid unified catalog manifestRevision.";
                return false;
            }

            if (!TryGetRequiredString(
                    root,
                    "updatedUtc",
                    out updatedUtcText) ||
                !TryGetRequiredString(
                    root,
                    "expiresUtc",
                    out expiresUtcText))
            {
                error =
                    "Unified catalog timestamps are missing or invalid.";
                return false;
            }

            DateTime updatedUtc;
            DateTime expiresUtc;

            if (!TryParseUtc(
                    updatedUtcText,
                    out updatedUtc) ||
                !TryParseUtc(
                    expiresUtcText,
                    out expiresUtc) ||
                expiresUtc <= updatedUtc)
            {
                error =
                    "Unified catalog timestamps are invalid.";
                return false;
            }

            if (!root.TryGetValue(
                    "mods",
                    out modsValue) ||
                !root.TryGetValue(
                    "news",
                    out newsValue))
            {
                error =
                    "Unified catalog mods or news collection is missing.";
                return false;
            }

            List<object> mods =
                modsValue as List<object>;

            List<object> news =
                newsValue as List<object>;

            if (mods == null ||
                mods.Count > MaximumMods ||
                news == null ||
                news.Count > MaximumNews)
            {
                error =
                    "Unified catalog collection limits are invalid.";
                return false;
            }

            UnifiedModCatalog parsed =
                new UnifiedModCatalog();

            parsed.ManifestRevision =
                manifestRevision;

            parsed.UpdatedUtc =
                updatedUtc;

            parsed.ExpiresUtc =
                expiresUtc;

            HashSet<string> seenGuids =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            for (int i = 0;
                i < mods.Count;
                i++)
            {
                Dictionary<string, object> mod =
                    mods[i]
                    as Dictionary<string, object>;

                if (mod == null)
                {
                    error =
                        "Unified catalog mod entry " +
                        i.ToString(
                            CultureInfo.InvariantCulture) +
                        " is not an object.";
                    return false;
                }

                string guid;
                string name;
                string author;
                string versionText;

                if (!TryGetRequiredString(
                        mod,
                        "guid",
                        out guid) ||
                    !TryGetRequiredString(
                        mod,
                        "name",
                        out name) ||
                    !TryGetRequiredString(
                        mod,
                        "author",
                        out author) ||
                    !TryGetRequiredString(
                        mod,
                        "version",
                        out versionText))
                {
                    error =
                        "Unified catalog mod identity is invalid at index " +
                        i.ToString(
                            CultureInfo.InvariantCulture) +
                        ".";
                    return false;
                }

                UpdateManifestEntry update;
                if (!UpdateManifestEntry.TryCreate(
                        guid,
                        name,
                        author,
                        versionText,
                        out update))
                {
                    error =
                        "Unified catalog mod identity is invalid at index " +
                        i.ToString(
                            CultureInfo.InvariantCulture) +
                        ".";
                    return false;
                }

                if (!seenGuids.Add(
                        update.Guid))
                {
                    error =
                        "Duplicate unified catalog GUID: " +
                        update.Guid;
                    return false;
                }

                object packageValue;
                if (!mod.TryGetValue(
                        "package",
                        out packageValue))
                {
                    error =
                        "Unified catalog package is missing for " +
                        update.Guid +
                        ".";
                    return false;
                }

                Dictionary<string, object> packageObject =
                    packageValue
                    as Dictionary<string, object>;

                if (packageObject == null)
                {
                    error =
                        "Unified catalog package is invalid for " +
                        update.Guid +
                        ".";
                    return false;
                }

                PackageManifestEntry package;
                string packageError;

                if (!TryParsePackage(
                        update,
                        versionText,
                        packageObject,
                        out package,
                        out packageError))
                {
                    error =
                        "Unified catalog package for " +
                        update.Guid +
                        " is invalid: " +
                        packageError;
                    return false;
                }

                string description =
                    ModDetailsCatalog.SanitizeDisplayText(
                        OptionalString(
                            mod,
                            "description"),
                        700);

                string whatsNew =
                    ModDetailsCatalog.SanitizeDisplayText(
                        OptionalString(
                            mod,
                            "whatsNew"),
                        900);

                parsed.Mods.Add(
                    new UnifiedCatalogModEntry(
                        update,
                        package,
                        description,
                        whatsNew));
            }

            HashSet<int> seenNewsIds =
                new HashSet<int>();

            for (int i = 0;
                i < news.Count;
                i++)
            {
                Dictionary<string, object> item =
                    news[i]
                    as Dictionary<string, object>;

                if (item == null)
                {
                    error =
                        "Unified catalog news entry " +
                        i.ToString(
                            CultureInfo.InvariantCulture) +
                        " is not an object.";
                    return false;
                }

                int id;
                string title;
                string body;

                if (!TryGetRequiredInt(
                        item,
                        "id",
                        out id) ||
                    id < 0 ||
                    !seenNewsIds.Add(id) ||
                    !TryGetRequiredString(
                        item,
                        "title",
                        out title) ||
                    !TryGetRequiredString(
                        item,
                        "text",
                        out body))
                {
                    error =
                        "Unified catalog news entry is invalid at index " +
                        i.ToString(
                            CultureInfo.InvariantCulture) +
                        ".";
                    return false;
                }

                NewsManifestEntry newsEntry;
                if (!NewsManifestEntry.TryCreate(
                        id,
                        title,
                        body,
                        out newsEntry))
                {
                    error =
                        "Unified catalog news content is invalid at index " +
                        i.ToString(
                            CultureInfo.InvariantCulture) +
                        ".";
                    return false;
                }

                parsed.News.Add(
                    newsEntry);
            }

            parsed.News.Sort(
                delegate(
                    NewsManifestEntry left,
                    NewsManifestEntry right)
                {
                    return left.Id.CompareTo(
                        right.Id);
                });

            catalog = parsed;
            return true;
        }

        internal string CreateNewsCacheManifest()
        {
            StringBuilder builder =
                new StringBuilder();

            builder.Append(
                "{\"formatVersion\":1,\"manifestRevision\":");

            long cacheRevision =
                ManifestRevision > int.MaxValue
                    ? int.MaxValue
                    : ManifestRevision;

            builder.Append(
                cacheRevision.ToString(
                    CultureInfo.InvariantCulture));

            builder.Append(
                ",\"updatedUtc\":");

            AppendJsonString(
                builder,
                UpdatedUtc.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture));

            builder.Append(
                ",\"items\":[");

            for (int i = 0;
                i < News.Count;
                i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                NewsManifestEntry entry =
                    News[i];

                builder.Append(
                    "{\"id\":");

                builder.Append(
                    entry.Id.ToString(
                        CultureInfo.InvariantCulture));

                builder.Append(
                    ",\"title\":");

                AppendJsonString(
                    builder,
                    entry.Title);

                builder.Append(
                    ",\"text\":");

                AppendJsonString(
                    builder,
                    entry.Text);

                builder.Append('}');
            }

            builder.Append(
                "]}");

            return builder.ToString();
        }

        private static bool TryParsePackage(
            UpdateManifestEntry update,
            string versionText,
            Dictionary<string, object> values,
            out PackageManifestEntry package,
            out string error)
        {
            package = null;
            error = null;

            string downloadUrl;
            string sha256;
            long packageBytes;
            long maximumUnpackedBytes;
            string installDirectory;
            string archiveRoot;
            string pluginFile;
            string configFile;
            List<string> runtimeDirectories;
            List<string> runtimeFiles;
            List<string> removeFiles;

            if (!TryGetRequiredString(
                    values,
                    "downloadUrl",
                    out downloadUrl) ||
                !TryGetRequiredString(
                    values,
                    "sha256",
                    out sha256) ||
                !TryGetRequiredLong(
                    values,
                    "packageBytes",
                    out packageBytes) ||
                !TryGetRequiredLong(
                    values,
                    "maximumUnpackedBytes",
                    out maximumUnpackedBytes) ||
                !TryGetRequiredString(
                    values,
                    "installDirectory",
                    out installDirectory) ||
                !TryGetRequiredString(
                    values,
                    "archiveRoot",
                    out archiveRoot) ||
                !TryGetRequiredString(
                    values,
                    "pluginFile",
                    out pluginFile) ||
                !TryGetRequiredString(
                    values,
                    "configFile",
                    out configFile) ||
                !TryGetPathArray(
                    values,
                    "runtimeDirectories",
                    out runtimeDirectories) ||
                !TryGetPathArray(
                    values,
                    "runtimeFiles",
                    out runtimeFiles) ||
                !TryGetPathArray(
                    values,
                    "removeFiles",
                    out removeFiles))
            {
                error =
                    "Required package metadata is missing.";
                return false;
            }

            return PackageManifestEntry.TryCreate(
                update.Guid,
                update.Name,
                versionText,
                downloadUrl,
                sha256,
                packageBytes,
                maximumUnpackedBytes,
                installDirectory,
                archiveRoot,
                pluginFile,
                configFile,
                runtimeDirectories,
                runtimeFiles,
                removeFiles,
                out package,
                out error);
        }

        private static bool TryGetPathArray(
            Dictionary<string, object> values,
            string key,
            out List<string> result)
        {
            result = null;

            object raw;
            if (!values.TryGetValue(
                    key,
                    out raw))
            {
                return false;
            }

            List<object> items =
                raw as List<object>;

            if (items == null ||
                items.Count >
                    PackageManifest.MaximumPathEntries)
            {
                return false;
            }

            List<string> parsed =
                new List<string>();

            HashSet<string> seen =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            for (int i = 0;
                i < items.Count;
                i++)
            {
                string value =
                    items[i] as string;

                if (value == null)
                {
                    return false;
                }

                value =
                    value.Trim()
                    .Replace(
                        '\\',
                        '/');

                if (!PackageManifestEntry.IsSafeRelativePath(
                        value) ||
                    !seen.Add(
                        value))
                {
                    return false;
                }

                parsed.Add(
                    value);
            }

            result = parsed;
            return true;
        }

        private static bool TryGetRequiredLong(
            Dictionary<string, object> values,
            string key,
            out long value)
        {
            value = 0L;

            object raw;
            if (!values.TryGetValue(
                    key,
                    out raw) ||
                !(raw is long))
            {
                return false;
            }

            value = (long)raw;
            return true;
        }

        private static bool TryGetRequiredInt(
            Dictionary<string, object> values,
            string key,
            out int value)
        {
            value = 0;

            long raw;
            if (!TryGetRequiredLong(
                    values,
                    key,
                    out raw) ||
                raw < int.MinValue ||
                raw > int.MaxValue)
            {
                return false;
            }

            value = (int)raw;
            return true;
        }

        private static bool TryGetRequiredString(
            Dictionary<string, object> values,
            string key,
            out string value)
        {
            value = null;

            object raw;
            if (!values.TryGetValue(
                    key,
                    out raw))
            {
                return false;
            }

            value =
                raw as string;

            return value != null;
        }

        private static string OptionalString(
            Dictionary<string, object> values,
            string key)
        {
            object raw;

            if (!values.TryGetValue(
                    key,
                    out raw))
            {
                return string.Empty;
            }

            return raw as string ??
                string.Empty;
        }

        private static bool TryParseUtc(
            string text,
            out DateTime value)
        {
            return DateTime.TryParseExact(
                text,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                out value) &&
                value.Kind ==
                    DateTimeKind.Utc;
        }

        private static void AppendJsonString(
            StringBuilder builder,
            string value)
        {
            builder.Append('"');

            string text =
                value ?? string.Empty;

            for (int i = 0;
                i < text.Length;
                i++)
            {
                char c =
                    text[i];

                switch (c)
                {
                    case '"':
                        builder.Append(
                            "\\\"");
                        break;
                    case '\\':
                        builder.Append(
                            "\\\\");
                        break;
                    case '\b':
                        builder.Append(
                            "\\b");
                        break;
                    case '\f':
                        builder.Append(
                            "\\f");
                        break;
                    case '\n':
                        builder.Append(
                            "\\n");
                        break;
                    case '\r':
                        builder.Append(
                            "\\r");
                        break;
                    case '\t':
                        builder.Append(
                            "\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append(
                                "\\u");

                            builder.Append(
                                ((int)c).ToString(
                                    "x4",
                                    CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(
                                c);
                        }
                        break;
                }
            }

            builder.Append('"');
        }
    }

    internal static class UnifiedCatalogClient
    {
        private const string CatalogUrl =
            "https://raw.githubusercontent.com/Loupito75/Project-Hospital-Mods/main/catalog/mod-catalog.json";

        private const string SignatureUrl =
            "https://raw.githubusercontent.com/Loupito75/Project-Hospital-Mods/main/catalog/mod-catalog.json.sig";

        private const string RevisionKey =
            "L75_HMUC_UNIFIED_CATALOG_REVISION";

        private const string HashKey =
            "L75_HMUC_UNIFIED_CATALOG_HASH";

        private const int RequestTimeoutSeconds = 10;
        private const int MaximumFutureHours = 24;
        private const int MaximumLifetimeDays = 120;
        private const ulong MaximumCatalogBytes = 524288UL;

        internal static UnifiedModCatalog Current
        {
            get;
            private set;
        }

        internal static string LastError
        {
            get;
            private set;
        }

        internal static IEnumerator Refresh()
        {
            Current = null;
            LastError = string.Empty;

            IEnumerator core =
                RefreshCore();

            try
            {
                while (true)
                {
                    bool hasNext =
                        false;

                    object current =
                        null;

                    Exception failure =
                        null;

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
                        LastError =
                            "Unexpected unified catalog failure: " +
                            failure.GetType().Name +
                            ": " +
                            failure.Message;

                        yield break;
                    }

                    if (!hasNext)
                    {
                        yield break;
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

        private static IEnumerator RefreshCore()
        {
            string trustError;
            if (!PackageTrust.IsConfigured(
                    out trustError))
            {
                LastError =
                    "Catalog trust is not configured: " +
                    trustError;
                yield break;
            }

            UnityWebRequest catalogRequest =
                null;

            UnityWebRequest signatureRequest =
                null;

            try
            {
                catalogRequest =
                    UnityWebRequest.Get(
                        CatalogUrl);

                catalogRequest.timeout =
                    RequestTimeoutSeconds;

                yield return
                    catalogRequest.SendWebRequest();

                if (catalogRequest.isNetworkError ||
                    catalogRequest.isHttpError)
                {
                    LastError =
                        "Unified catalog unavailable: HTTP " +
                        catalogRequest.responseCode.ToString(
                            CultureInfo.InvariantCulture) +
                        " | " +
                        (catalogRequest.error ??
                         "unknown network error");
                    yield break;
                }

                if (catalogRequest.downloadedBytes >
                    MaximumCatalogBytes)
                {
                    LastError =
                        "Unified catalog was rejected because it is too large.";
                    yield break;
                }

                byte[] catalogBytes =
                    catalogRequest.downloadHandler ==
                        null
                        ? null
                        : catalogRequest.downloadHandler.data;

                if (catalogBytes == null ||
                    catalogBytes.Length == 0 ||
                    (ulong)catalogBytes.Length >
                        MaximumCatalogBytes)
                {
                    LastError =
                        "Unified catalog body is empty or too large.";
                    yield break;
                }

                signatureRequest =
                    UnityWebRequest.Get(
                        SignatureUrl);

                signatureRequest.timeout =
                    RequestTimeoutSeconds;

                yield return
                    signatureRequest.SendWebRequest();

                if (signatureRequest.isNetworkError ||
                    signatureRequest.isHttpError)
                {
                    LastError =
                        "Unified catalog signature unavailable: HTTP " +
                        signatureRequest.responseCode.ToString(
                            CultureInfo.InvariantCulture) +
                        " | " +
                        (signatureRequest.error ??
                         "unknown network error");
                    yield break;
                }

                if (signatureRequest.downloadedBytes >
                    (ulong)
                    PackageTrust.SignatureFileMaximumBytes)
                {
                    LastError =
                        "Unified catalog signature is too large.";
                    yield break;
                }

                byte[] signatureBytes =
                    signatureRequest.downloadHandler ==
                        null
                        ? null
                        : signatureRequest.downloadHandler.data;

                string signatureError;
                if (!PackageTrust.TryVerify(
                        catalogBytes,
                        signatureBytes,
                        out signatureError))
                {
                    LastError =
                        "Unified catalog signature was rejected: " +
                        signatureError;
                    yield break;
                }

                string text;
                try
                {
                    UTF8Encoding utf8 =
                        new UTF8Encoding(
                            false,
                            true);

                    text =
                        utf8.GetString(
                            catalogBytes);
                }
                catch (Exception exception)
                {
                    LastError =
                        "Unified catalog is not valid UTF-8: " +
                        exception.GetType().Name;
                    yield break;
                }

                UnifiedModCatalog parsed;
                string parseError;

                if (!UnifiedModCatalog.TryParse(
                        text,
                        out parsed,
                        out parseError))
                {
                    LastError =
                        "Unified catalog was rejected: " +
                        parseError;
                    yield break;
                }

                string freshnessError;
                if (!ValidateFreshness(
                        parsed,
                        out freshnessError))
                {
                    LastError =
                        "Unified catalog freshness check failed: " +
                        freshnessError;
                    yield break;
                }

                string rollbackError;
                if (!AcceptRevision(
                        parsed.ManifestRevision,
                        catalogBytes,
                        out rollbackError))
                {
                    LastError =
                        "Unified catalog anti-rollback check failed: " +
                        rollbackError;
                    yield break;
                }

                Current = parsed;
            }
            finally
            {
                if (catalogRequest != null)
                {
                    catalogRequest.Dispose();
                }

                if (signatureRequest != null)
                {
                    signatureRequest.Dispose();
                }
            }
        }

        private static bool ValidateFreshness(
            UnifiedModCatalog catalog,
            out string error)
        {
            error = null;

            if (catalog == null)
            {
                error =
                    "Catalog is missing.";
                return false;
            }

            DateTime now =
                DateTime.UtcNow;

            if (catalog.ExpiresUtc <= now)
            {
                error =
                    "Catalog has expired.";
                return false;
            }

            if (catalog.UpdatedUtc >
                now.AddHours(
                    MaximumFutureHours))
            {
                error =
                    "Catalog timestamp is too far in the future.";
                return false;
            }

            if (catalog.ExpiresUtc >
                catalog.UpdatedUtc.AddDays(
                    MaximumLifetimeDays))
            {
                error =
                    "Catalog lifetime is too long.";
                return false;
            }

            return true;
        }

        private static bool AcceptRevision(
            long revision,
            byte[] catalogBytes,
            out string error)
        {
            error = null;

            string currentHash;

            using (SHA256Managed sha =
                new SHA256Managed())
            {
                currentHash =
                    Convert.ToBase64String(
                        sha.ComputeHash(
                            catalogBytes));
            }

            long previousRevision =
                0L;

            if (PlayerPrefs.HasKey(
                    RevisionKey))
            {
                string previousText =
                    PlayerPrefs.GetString(
                        RevisionKey);

                if (!long.TryParse(
                        previousText,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out previousRevision) ||
                    previousRevision < 0L)
                {
                    error =
                        "Stored catalog revision is invalid.";
                    return false;
                }
            }

            string previousHash =
                PlayerPrefs.HasKey(
                    HashKey)
                    ? PlayerPrefs.GetString(
                        HashKey)
                    : string.Empty;

            if (revision <
                previousRevision)
            {
                error =
                    "Catalog revision is older than the trusted revision.";
                return false;
            }

            if (revision ==
                    previousRevision &&
                previousHash.Length != 0 &&
                !string.Equals(
                    previousHash,
                    currentHash,
                    StringComparison.Ordinal))
            {
                error =
                    "Catalog content changed without increasing manifestRevision.";
                return false;
            }

            if (revision !=
                    previousRevision ||
                !string.Equals(
                    previousHash,
                    currentHash,
                    StringComparison.Ordinal))
            {
                PlayerPrefs.SetString(
                    RevisionKey,
                    revision.ToString(
                        CultureInfo.InvariantCulture));

                PlayerPrefs.SetString(
                    HashKey,
                    currentHash);

                PlayerPrefs.Save();
            }

            return true;
        }
    }
}
