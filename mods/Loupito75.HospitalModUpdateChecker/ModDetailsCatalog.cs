using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HospitalModUpdateChecker
{
    // Version-bound display metadata comes from the already verified unified
    // catalog. This class only provides the existing UI lookup surface.
    internal static class ModDetailsCatalog
    {
        private sealed class PublishedDetails
        {
            internal Version Version;
            internal string Description;
            internal string WhatsNew;
        }

        private static readonly Dictionary<string, PublishedDetails>
            _byGuid =
                new Dictionary<string, PublishedDetails>(
                    StringComparer.OrdinalIgnoreCase);

        internal static string Description(
            string guid,
            Version version)
        {
            PublishedDetails data;

            return TryFind(
                    guid,
                    version,
                    out data)
                ? data.Description
                : string.Empty;
        }

        internal static string WhatsNew(
            string guid,
            Version version)
        {
            PublishedDetails data;

            return TryFind(
                    guid,
                    version,
                    out data)
                ? data.WhatsNew
                : string.Empty;
        }

        internal static void Clear()
        {
            _byGuid.Clear();
        }

        internal static void ApplyUnifiedCatalog(
            UnifiedModCatalog catalog)
        {
            _byGuid.Clear();

            if (catalog == null)
            {
                return;
            }

            for (int i = 0;
                i < catalog.Mods.Count;
                i++)
            {
                UnifiedCatalogModEntry entry =
                    catalog.Mods[i];

                if (entry == null ||
                    entry.Update == null ||
                    entry.Update.Version == null)
                {
                    continue;
                }

                _byGuid[entry.Update.Guid] =
                    new PublishedDetails
                    {
                        Version =
                            entry.Update.Version,
                        Description =
                            entry.Description ??
                            string.Empty,
                        WhatsNew =
                            entry.WhatsNew ??
                            string.Empty
                    };
            }
        }

        private static bool TryFind(
            string guid,
            Version version,
            out PublishedDetails data)
        {
            data = null;

            if (string.IsNullOrEmpty(
                    guid) ||
                version == null ||
                !_byGuid.TryGetValue(
                    guid,
                    out data) ||
                data == null ||
                data.Version == null ||
                !data.Version.Equals(
                    version))
            {
                data = null;
                return false;
            }

            return true;
        }

        // Text is always rendered with rich-text DISABLED. This additional
        // normalization removes tag-like markup, controls and excessive
        // lines/length. Unknown signed fields are never rendered or evaluated.
        internal static string SanitizeDisplayText(
            string value,
            int maxLength)
        {
            if (string.IsNullOrEmpty(
                    value) ||
                maxLength <= 0)
            {
                return string.Empty;
            }

            StringBuilder output =
                new StringBuilder(
                    Math.Min(
                        maxLength,
                        value.Length));

            bool insideMarkup =
                false;

            int lines =
                1;

            for (int i = 0;
                i < value.Length &&
                output.Length < maxLength;
                i++)
            {
                char c =
                    value[i];

                if (insideMarkup)
                {
                    if (c == '>')
                    {
                        insideMarkup =
                            false;
                    }

                    continue;
                }

                if (c == '<')
                {
                    insideMarkup =
                        true;
                    continue;
                }

                if (c == '>')
                {
                    continue;
                }

                if (c == '\r')
                {
                    if (i + 1 <
                            value.Length &&
                        value[i + 1] ==
                            '\n')
                    {
                        continue;
                    }

                    c = '\n';
                }

                if (c == '\n')
                {
                    if (lines >= 8)
                    {
                        break;
                    }

                    lines++;
                    output.Append(c);
                }
                else if (char.IsHighSurrogate(
                             c))
                {
                    if (i + 1 <
                            value.Length &&
                        char.IsLowSurrogate(
                            value[i + 1]) &&
                        output.Length + 2 <=
                            maxLength)
                    {
                        output.Append(c);
                        output.Append(
                            value[++i]);
                    }
                }
                else if (char.IsLowSurrogate(
                             c) ||
                         char.IsControl(
                             c) ||
                         char.GetUnicodeCategory(
                             c) ==
                             UnicodeCategory.Format)
                {
                    continue;
                }
                else
                {
                    output.Append(c);
                }
            }

            return output
                .ToString()
                .Trim();
        }
    }
}
