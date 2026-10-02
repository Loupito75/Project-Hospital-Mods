using System;
using System.Collections.Generic;
using System.Globalization;

namespace HospitalModUpdateChecker
{
    internal sealed class NewsManifest
    {
        internal const int SupportedFormatVersion = 1;
        internal const int MaximumEntries = 64;
        internal const int MaximumTitleLength = 160;
        internal const int MaximumTextLength = 2048;

        internal List<NewsManifestEntry> Entries { get; private set; }

        private NewsManifest()
        {
            Entries = new List<NewsManifestEntry>();
        }

        internal static bool TryParse(
            string text,
            out NewsManifest manifest,
            out string error)
        {
            manifest = null;
            error = null;

            if (string.IsNullOrEmpty(text))
            {
                error = "News manifest is empty.";
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
                error = "News manifest root must be a JSON object.";
                return false;
            }

            if (!HasOnlyKeys(
                    root,
                    new string[]
                    {
                        "formatVersion",
                        "manifestRevision",
                        "updatedUtc",
                        "items"
                    },
                    out error))
            {
                return false;
            }

            int formatVersion;
            if (!TryGetRequiredInt(root, "formatVersion", out formatVersion) ||
                formatVersion != SupportedFormatVersion)
            {
                error = "Unsupported or invalid news formatVersion.";
                return false;
            }

            int manifestRevision;
            if (!TryGetRequiredInt(root, "manifestRevision", out manifestRevision) ||
                manifestRevision < 1)
            {
                error = "Invalid news manifestRevision.";
                return false;
            }

            string updatedUtcText;
            if (!TryGetRequiredString(root, "updatedUtc", out updatedUtcText))
            {
                error = "Invalid news updatedUtc value.";
                return false;
            }

            DateTime updatedUtc;
            if (!DateTime.TryParseExact(
                    updatedUtcText,
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                    out updatedUtc) ||
                updatedUtc.Kind != DateTimeKind.Utc)
            {
                error = "Invalid news updatedUtc value.";
                return false;
            }

            object itemsValue;
            if (!root.TryGetValue("items", out itemsValue))
            {
                error = "News items are missing.";
                return false;
            }

            List<object> items = itemsValue as List<object>;
            if (items == null)
            {
                error = "News items must be a JSON array.";
                return false;
            }

            if (items.Count > MaximumEntries)
            {
                error = "News manifest contains too many entries.";
                return false;
            }

            NewsManifest parsed = new NewsManifest();

            HashSet<int> seenIds =
                new HashSet<int>();

            for (int i = 0; i < items.Count; i++)
            {
                Dictionary<string, object> item =
                    items[i] as Dictionary<string, object>;

                if (item == null)
                {
                    error = "News entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) +
                        " must be a JSON object.";
                    return false;
                }

                if (!HasOnlyKeys(
                        item,
                        new string[]
                        {
                            "id",
                            "title",
                            "text"
                        },
                        out error))
                {
                    error = "Invalid news entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) +
                        ": " + error;
                    return false;
                }

                int id;
                string title;
                string body;

                if (!TryGetRequiredInt(item, "id", out id) ||
                    !TryGetRequiredString(item, "title", out title) ||
                    !TryGetRequiredString(item, "text", out body))
                {
                    error = "Invalid news entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }

                if (id < 0 ||
                    !seenIds.Add(id))
                {
                    error = "News entry IDs must be unique non-negative integers.";
                    return false;
                }

                NewsManifestEntry entry;
                if (!NewsManifestEntry.TryCreate(
                        id,
                        title,
                        body,
                        out entry))
                {
                    error = "Invalid news entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }

                parsed.Entries.Add(entry);
            }

            parsed.Entries.Sort(delegate(
                NewsManifestEntry left,
                NewsManifestEntry right)
            {
                return left.Id.CompareTo(right.Id);
            });

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

    internal sealed class NewsManifestEntry
    {
        internal int Id { get; private set; }
        internal string Title { get; private set; }
        internal string Text { get; private set; }

        private NewsManifestEntry()
        {
        }

        internal static bool TryCreate(
            int id,
            string title,
            string text,
            out NewsManifestEntry entry)
        {
            entry = null;

            title = title == null
                ? string.Empty
                : title.Trim();

            text = text == null
                ? string.Empty
                : text.Trim();

            if (title.Length == 0 ||
                title.Length > NewsManifest.MaximumTitleLength ||
                text.Length == 0 ||
                text.Length > NewsManifest.MaximumTextLength)
            {
                return false;
            }

            if (!IsSafeTitle(title) ||
                !IsSafeText(text))
            {
                return false;
            }

            entry = new NewsManifestEntry
            {
                Id = id,
                Title = title,
                Text = text
            };

            return true;
        }

        private static bool IsSafeTitle(string value)
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

        private static bool IsSafeText(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];

                if (char.IsControl(c) &&
                    c != '\n' &&
                    c != '\r' &&
                    c != '\t')
                {
                    return false;
                }

                if (c == '<' ||
                    c == '>')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
