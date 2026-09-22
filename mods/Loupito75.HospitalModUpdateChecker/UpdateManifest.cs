using System;
using System.Collections.Generic;
using System.Globalization;

namespace HospitalModUpdateChecker
{
    internal sealed class UpdateManifest
    {
        internal const int SupportedFormatVersion = 1;
        internal const int MaximumEntries = 256;

        internal List<UpdateManifestEntry> Entries { get; private set; }

        private UpdateManifest()
        {
            Entries = new List<UpdateManifestEntry>();
        }

        internal static bool TryParse(string text, out UpdateManifest manifest, out string error)
        {
            manifest = null;
            error = null;

            if (string.IsNullOrEmpty(text))
            {
                error = "Manifest is empty.";
                return false;
            }

            object rootValue;
            if (!SimpleJsonParser.TryParse(text, out rootValue, out error))
            {
                return false;
            }

            Dictionary<string, object> root =
                rootValue as Dictionary<string, object>;
            if (root == null)
            {
                error = "Manifest root must be a JSON object.";
                return false;
            }

            if (!HasOnlyKeys(
                root,
                new string[]
                {
                    "formatVersion",
                    "manifestRevision",
                    "updatedUtc",
                    "mods"
                },
                out error))
            {
                return false;
            }

            int formatVersion;
            if (!TryGetRequiredInt(root, "formatVersion", out formatVersion) ||
                formatVersion != SupportedFormatVersion)
            {
                error = "Unsupported or invalid formatVersion.";
                return false;
            }

            int manifestRevision;
            if (!TryGetRequiredInt(root, "manifestRevision", out manifestRevision) ||
                manifestRevision < 1)
            {
                error = "Invalid manifestRevision.";
                return false;
            }

            string updatedUtcText;
            if (!TryGetRequiredString(root, "updatedUtc", out updatedUtcText))
            {
                error = "Invalid updatedUtc value.";
                return false;
            }

            DateTime updatedUtc;
            if (!DateTime.TryParseExact(
                updatedUtcText,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out updatedUtc) ||
                updatedUtc.Kind != DateTimeKind.Utc)
            {
                error = "Invalid updatedUtc value.";
                return false;
            }

            object modsValue;
            if (!root.TryGetValue("mods", out modsValue))
            {
                error = "mods is missing.";
                return false;
            }

            List<object> mods = modsValue as List<object>;
            if (mods == null)
            {
                error = "mods must be a JSON array.";
                return false;
            }

            if (mods.Count > MaximumEntries)
            {
                error = "Manifest contains too many mod entries.";
                return false;
            }

            UpdateManifest parsed = new UpdateManifest();

            HashSet<string> seenGuids =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < mods.Count; i++)
            {
                Dictionary<string, object> mod =
                    mods[i] as Dictionary<string, object>;
                if (mod == null)
                {
                    error = "Mod entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) +
                        " must be a JSON object.";
                    return false;
                }

                if (!HasOnlyKeys(
                    mod,
                    new string[]
                    {
                        "guid",
                        "name",
                        "author",
                        "version"
                    },
                    out error))
                {
                    error = "Invalid mod entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) +
                        ": " + error;
                    return false;
                }

                string guid;
                string name;
                string author;
                string versionText;

                if (!TryGetRequiredString(mod, "guid", out guid) ||
                    !TryGetRequiredString(mod, "name", out name) ||
                    !TryGetRequiredString(mod, "author", out author) ||
                    !TryGetRequiredString(mod, "version", out versionText))
                {
                    error = "Invalid mod entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }

                UpdateManifestEntry entry;
                if (!UpdateManifestEntry.TryCreate(
                    guid,
                    name,
                    author,
                    versionText,
                    out entry))
                {
                    error = "Invalid mod entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }

                if (!seenGuids.Add(entry.Guid))
                {
                    error = "Duplicate mod GUID: " + entry.Guid;
                    return false;
                }

                parsed.Entries.Add(entry);
            }

            manifest = parsed;
            return true;
        }

        private static bool TryGetRequiredInt(
            Dictionary<string, object> values,
            string key,
            out int value)
        {
            value = 0;

            object raw;
            if (!values.TryGetValue(key, out raw) || !(raw is long))
            {
                return false;
            }

            long longValue = (long)raw;
            if (longValue < int.MinValue || longValue > int.MaxValue)
            {
                return false;
            }

            value = (int)longValue;
            return true;
        }

        private static bool TryGetRequiredString(
            Dictionary<string, object> values,
            string key,
            out string value)
        {
            value = null;

            object raw;
            if (!values.TryGetValue(key, out raw))
            {
                return false;
            }

            value = raw as string;
            return value != null;
        }

        private static bool HasOnlyKeys(
            Dictionary<string, object> values,
            string[] allowedKeys,
            out string error)
        {
            error = null;

            foreach (string key in values.Keys)
            {
                bool allowed = false;

                for (int i = 0; i < allowedKeys.Length; i++)
                {
                    if (string.Equals(
                        key,
                        allowedKeys[i],
                        StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    error = "Unknown JSON property '" + key + "'.";
                    return false;
                }
            }

            return true;
        }
    }

    internal sealed class UpdateManifestEntry
    {
        internal string Guid { get; private set; }
        internal string Name { get; private set; }
        internal string Author { get; private set; }
        internal Version Version { get; private set; }

        private UpdateManifestEntry()
        {
        }

        private static bool IsSafeDisplayText(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                if (char.IsControl(c) ||
                    c == '<' ||
                    c == '>')
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool TryCreate(
            string guid,
            string name,
            string author,
            string versionText,
            out UpdateManifestEntry entry)
        {
            entry = null;

            guid = guid == null ? string.Empty : guid.Trim();
            name = name == null ? string.Empty : name.Trim();
            author = author == null ? string.Empty : author.Trim();
            versionText = versionText == null
                ? string.Empty
                : versionText.Trim();

            if (guid.Length == 0 || guid.Length > 128 ||
                name.Length == 0 || name.Length > 128 ||
                author.Length == 0 || author.Length > 128 ||
                versionText.Length == 0 || versionText.Length > 32)
            {
                return false;
            }

            if (!IsSafeDisplayText(name) ||
                !IsSafeDisplayText(author))
            {
                return false;
            }

            for (int i = 0; i < guid.Length; i++)
            {
                char c = guid[i];
                bool asciiLetterOrDigit =
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9');

                if (!(asciiLetterOrDigit ||
                    c == '.' ||
                    c == '_' ||
                    c == '-'))
                {
                    return false;
                }
            }

            Version version;
            try
            {
                version = new Version(versionText);
            }
            catch
            {
                return false;
            }

            entry = new UpdateManifestEntry
            {
                Guid = guid,
                Name = name,
                Author = author,
                Version = version
            };

            return true;
        }
    }
}
