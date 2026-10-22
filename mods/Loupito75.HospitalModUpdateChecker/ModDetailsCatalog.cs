using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;

namespace HospitalModUpdateChecker
{
    // Optional, separately signed metadata. The established v1 update and
    // package catalogs remain byte-for-byte backward compatible.
    internal static class ModDetailsCatalog
    {
        private const string CatalogUrl =
            "https://raw.githubusercontent.com/Loupito75/Project-Hospital-Mods/main/mod-details.json";
        private const string SignatureUrl =
            "https://raw.githubusercontent.com/Loupito75/Project-Hospital-Mods/main/mod-details.json.sig";
        private const int MaximumCatalogBytes = 262144;
        private const int MaximumEntries = 256;
        private const int TimeoutSeconds = 10;
        private const string RevisionKey =
            "Loupito75.HMUC.ModDetailsRevision";
        private const string HashKey =
            "Loupito75.HMUC.ModDetailsHash";

        private sealed class PublishedDetails
        {
            internal Version Version;
            internal string Description;
            internal string WhatsNew;
        }

        private static readonly Dictionary<string, PublishedDetails>
            _byGuid = new Dictionary<string, PublishedDetails>(
                StringComparer.OrdinalIgnoreCase);

        internal static string Description(string guid, Version version)
        {
            PublishedDetails data;
            return TryFind(guid, version, out data)
                ? data.Description : string.Empty;
        }

        internal static string WhatsNew(string guid, Version version)
        {
            PublishedDetails data;
            return TryFind(guid, version, out data)
                ? data.WhatsNew : string.Empty;
        }

        private static bool TryFind(string guid, Version version,
            out PublishedDetails data)
        {
            data = null;
            if (string.IsNullOrEmpty(guid) || version == null ||
                !_byGuid.TryGetValue(guid, out data) ||
                !data.Version.Equals(version))
            {
                data = null;
                return false;
            }
            return true;
        }

        internal static IEnumerator Refresh()
        {
            _byGuid.Clear();
            // Optional metadata must never prevent update checks or installs.
            if (UpdateCheckerConfig.Debug)
                yield break;

            UnityWebRequest catalog = null;
            UnityWebRequest signature = null;
            try
            {
                catalog = UnityWebRequest.Get(CatalogUrl);
                catalog.timeout = TimeoutSeconds;
                yield return catalog.SendWebRequest();
                if (catalog.isNetworkError || catalog.isHttpError ||
                    catalog.downloadedBytes > MaximumCatalogBytes ||
                    catalog.downloadHandler == null)
                    yield break;

                byte[] bytes = catalog.downloadHandler.data;
                if (bytes == null || bytes.Length == 0 ||
                    bytes.Length > MaximumCatalogBytes)
                    yield break;

                signature = UnityWebRequest.Get(SignatureUrl);
                signature.timeout = TimeoutSeconds;
                yield return signature.SendWebRequest();
                if (signature.isNetworkError || signature.isHttpError ||
                    signature.downloadedBytes >
                        (ulong)PackageTrust.SignatureFileMaximumBytes ||
                    signature.downloadHandler == null)
                    yield break;

                string error;
                if (!PackageTrust.TryVerify(
                    bytes, signature.downloadHandler.data, out error))
                {
                    Plugin.Log.LogWarning(
                        "HMUC signed mod details rejected: " + error);
                    yield break;
                }

                string text;
                try
                {
                    text = new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (Exception)
                {
                    yield break;
                }

                long revision;
                DateTime updated, expires;
                Dictionary<string, PublishedDetails> parsed;
                if (!TryParse(text, out revision, out updated,
                    out expires, out parsed))
                {
                    Plugin.Log.LogWarning(
                        "HMUC signed mod details have invalid format.");
                    yield break;
                }

                DateTime now = DateTime.UtcNow;
                if (expires <= now || updated > now.AddHours(24) ||
                    expires > updated.AddDays(120))
                    yield break;

                string hash;
                using (SHA256Managed sha = new SHA256Managed())
                    hash = Convert.ToBase64String(sha.ComputeHash(bytes));

                long previous = 0;
                if (PlayerPrefs.HasKey(RevisionKey))
                    long.TryParse(PlayerPrefs.GetString(RevisionKey),
                        NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out previous);

                string knownHash = PlayerPrefs.HasKey(HashKey)
                    ? PlayerPrefs.GetString(HashKey) : string.Empty;
                if (revision < previous ||
                    (revision == previous &&
                     knownHash.Length != 0 && knownHash != hash))
                {
                    Plugin.Log.LogWarning(
                        "HMUC signed mod details anti-rollback check failed.");
                    yield break;
                }

                if (revision != previous || knownHash != hash)
                {
                    PlayerPrefs.SetString(RevisionKey,
                        revision.ToString(CultureInfo.InvariantCulture));
                    PlayerPrefs.SetString(HashKey, hash);
                    PlayerPrefs.Save();
                }

                foreach (KeyValuePair<string, PublishedDetails> item in parsed)
                    _byGuid[item.Key] = item.Value;
            }
            finally
            {
                if (catalog != null)
                    catalog.Dispose();
                if (signature != null)
                    signature.Dispose();
                TitleScreenUpdatePanel.Refresh();
            }
        }

        private static bool TryParse(string json,
            out long revision, out DateTime updated, out DateTime expires,
            out Dictionary<string, PublishedDetails> items)
        {
            revision = 0;
            updated = DateTime.MinValue;
            expires = DateTime.MinValue;
            items = null;
            object root;
            string error;
            if (!SimpleJsonParser.TryParse(json, out root, out error))
                return false;
            Dictionary<string, object> obj =
                root as Dictionary<string, object>;
            // Forward-compatible metadata: only consume fields that this
            // HMUC version understands. Unknown signed fields are ignored.
            if (obj == null)
                return false;

            long format;
            string updatedText, expiresText;
            object rawMods;
            if (!TryLong(obj, "formatVersion", out format) ||
                format != 1 ||
                !TryLong(obj, "manifestRevision", out revision) ||
                revision < 1 ||
                !TryString(obj, "updatedUtc", out updatedText) ||
                !TryString(obj, "expiresUtc", out expiresText) ||
                !TryUtc(updatedText, out updated) ||
                !TryUtc(expiresText, out expires) ||
                !obj.TryGetValue("mods", out rawMods))
                return false;

            List<object> mods = rawMods as List<object>;
            if (mods == null || mods.Count > MaximumEntries)
                return false;
            Dictionary<string, PublishedDetails> result =
                new Dictionary<string, PublishedDetails>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (object item in mods)
            {
                Dictionary<string, object> entry =
                    item as Dictionary<string, object>;
                if (entry == null)
                    return false;

                string guid, versionText;
                if (!TryString(entry, "guid", out guid) ||
                    !TryString(entry, "version", out versionText) ||
                    !IsGuid(guid))
                    return false;

                // Optional display fields are plain text only. Invalid or
                // unsafe content is normalized without rejecting an entire
                // signed catalog, while identity/version remain strict.
                string description = SanitizeDisplayText(
                    OptionalString(entry, "description"), 700);
                string whatsNew = SanitizeDisplayText(
                    OptionalString(entry, "whatsNew"), 900);
                if (!TryAddDetails(result, guid, versionText,
                        description, whatsNew))
                    return false;
            }
            items = result;
            return true;
        }

        private static bool TryUtc(string text, out DateTime value)
        {
            return DateTime.TryParseExact(text,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal, out value);
        }

        private static bool TryLong(Dictionary<string, object> obj,
            string name, out long value)
        {
            object raw;
            value = 0;
            if (!obj.TryGetValue(name, out raw) || !(raw is long))
                return false;
            value = (long)raw;
            return true;
        }

        private static bool TryString(Dictionary<string, object> obj,
            string name, out string value)
        {
            value = null;
            object raw;
            return obj.TryGetValue(name, out raw) &&
                (value = raw as string) != null;
        }

        private static string OptionalString(
            Dictionary<string, object> values, string key)
        {
            object raw;
            return values.TryGetValue(key, out raw)
                ? raw as string ?? string.Empty : string.Empty;
        }

        // Text is always rendered with rich-text DISABLED. This additional
        // normalization removes tag-like markup, controls and excessive
        // lines/length. Unknown fields are never rendered or evaluated.
        internal static string SanitizeDisplayText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            StringBuilder output = new StringBuilder(
                Math.Min(maxLength, value.Length));
            bool insideMarkup = false;
            int lines = 1;
            for (int i = 0;
                i < value.Length && output.Length < maxLength; i++)
            {
                char c = value[i];
                if (insideMarkup)
                {
                    if (c == '>')
                        insideMarkup = false;
                    continue;
                }
                if (c == '<')
                {
                    insideMarkup = true;
                    continue;
                }
                if (c == '>')
                    continue;
                if (c == '\r')
                {
                    // Treat CRLF as one line break.
                    if (i + 1 < value.Length && value[i + 1] == '\n')
                        continue;
                    c = '\n';
                }
                if (c == '\n')
                {
                    if (lines >= 8)
                        break;
                    lines++;
                    output.Append(c);
                }
                else if (char.IsHighSurrogate(c))
                {
                    // Keep valid Unicode supplementary characters as
                    // intact UTF-16 pairs, never emit an orphan surrogate.
                    if (i + 1 < value.Length &&
                        char.IsLowSurrogate(value[i + 1]) &&
                        output.Length + 2 <= maxLength)
                    {
                        output.Append(c);
                        output.Append(value[++i]);
                    }
                }
                else if (char.IsLowSurrogate(c) ||
                         char.IsControl(c) ||
                         char.GetUnicodeCategory(c) ==
                             UnicodeCategory.Format)
                {
                    // Remove invalid controls and hidden direction tricks.
                    continue;
                }
                else
                {
                    output.Append(c);
                }
            }
            return output.ToString().Trim();
        }

        private static bool IsGuid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128 ||
                !value.StartsWith("loupito75.",
                    StringComparison.OrdinalIgnoreCase))
                return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') &&
                    !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') &&
                    c != '.' && c != '_' && c != '-')
                    return false;
            return true;
        }

        // No Dictionary.TryAdd (.NET 4.0+) in our Mono CLR 2 target.
        private static bool TryAddDetails(
            Dictionary<string, PublishedDetails> result,
            string guid, string versionText, string description,
            string whatsNew)
        {
            Version version;
            try { version = new Version(versionText); }
            catch { return false; }
            if (result.ContainsKey(guid))
                return false;
            result.Add(guid, new PublishedDetails {
                Version = version,
                Description = description.Trim(),
                WhatsNew = whatsNew.Trim()
            });
            return true;
        }
    }
}
