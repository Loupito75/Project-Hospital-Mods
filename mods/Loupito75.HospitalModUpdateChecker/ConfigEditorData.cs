using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using BepInEx;
using BepInEx.Bootstrap;

namespace HospitalModUpdateChecker
{
    // HMUC only stages user edits. The bootstrap core owns activation at next launch.
    internal sealed class ConfigEditOption
    {
        internal string Path;
        internal string Name;
        internal string Label;
        internal string Section;
        internal string Description;
        internal string Control;
        internal string Value;
        internal string OriginalValue;
        internal decimal Min;
        internal decimal Max;
        internal decimal Step;
        internal string[] Choices;
        internal string ElementPath;
        internal string ElementName;
        internal string MatchAttributeName;
        internal string MatchAttributeValue;
        internal string ValueAttributeName;
        internal bool CompactRow;
    }

    internal sealed class ConfigEditSession
    {
        internal string ConfigPath;
        internal string PluginName;
        internal XmlDocument Document;
        internal readonly List<ConfigEditOption> Options =
            new List<ConfigEditOption>();
    }

    internal static class ConfigEditorData
    {
        internal const string NoEditableSettingsError =
            "HMUC_NO_EDITABLE_SETTINGS";
        private static readonly Regex MetadataAttribute = new Regex(
            "([a-zA-Z][a-zA-Z0-9]*)\\s*=\\s*\"([^\"]*)\"",
            RegexOptions.CultureInvariant);

        internal static bool TryFindConfig(
            PluginInfo plugin,
            out string path)
        {
            path = null;
            if (plugin == null || plugin.Metadata == null ||
                string.IsNullOrEmpty(plugin.Location))
                return false;

            try
            {
                string root = Path.GetFullPath(Paths.PluginPath).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                string canonicalRoot = Path.GetFullPath(
                    Path.Combine(Paths.BepInExRootPath, "plugins")).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(root, canonicalRoot,
                    StringComparison.OrdinalIgnoreCase))
                    return false;
                string assembly = Path.GetFullPath(plugin.Location);
                string directory = Path.GetDirectoryName(assembly);
                if (string.IsNullOrEmpty(directory) ||
                    !directory.StartsWith(
                        root + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    return false;

                // Only canonical plugin directories, not arbitrary XML paths.
                if (!string.Equals(
                    Path.GetDirectoryName(directory), root,
                    StringComparison.OrdinalIgnoreCase))
                    return false;

                string name = Path.GetFileName(directory);
                if (string.IsNullOrEmpty(name) ||
                    name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return false;

                // Explicit legacy exception confirmed by the HMUC package
                // catalog; do not scan arbitrary game or Workshop XML files.
                string fileName = name == "Loupito75.HospitalAlwaysLit"
                    ? name + ".Rooms.xml" : name + ".Config.xml";
                string candidate = Path.Combine(directory, fileName);
                if (!File.Exists(candidate))
                    return false;

                path = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryOpen(
            PluginInfo plugin,
            out ConfigEditSession session,
            out string error)
        {
            session = null;
            error = null;
            string path;
            if (!TryFindConfig(plugin, out path))
            {
                error = "No supported configuration file was found.";
                return false;
            }

            try
            {
                string staged = path + ".newconfig";
                XmlDocument document = LoadSafe(
                    File.Exists(staged) ? staged : path);
                ConfigEditSession result = new ConfigEditSession();
                result.ConfigPath = path;
                result.PluginName = plugin.Metadata.Name;
                result.Document = document;
                Collect(document.DocumentElement, "", result.Options);

                if (result.Options.Count == 0)
                {
                    error = NoEditableSettingsError;
                    return false;
                }

                session = result;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static XmlDocument LoadSafe(string path)
        {
            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            document.PreserveWhitespace = true;
            document.Load(path);
            if (document.DocumentType != null ||
                document.DocumentElement == null)
                throw new FormatException("Unsupported XML document or DTD.");
            return document;
        }

        private static void Collect(
            XmlElement parent, string path, List<ConfigEditOption> result)
        {
            ConfigEditOption simpleValueList;
            if (TryCreateSimpleValueListOption(
                    parent, path, out simpleValueList))
            {
                result.Add(simpleValueList);
                return;
            }

            // Repeated sibling names cannot be addressed safely by the
            // editor's path format. Skip those branches, but keep collecting
            // unrelated unique settings instead of rejecting the whole file.
            Dictionary<string, int> counts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null)
                    continue;
                int count;
                counts.TryGetValue(element.Name, out count);
                counts[element.Name] = count + 1;
            }

            HashSet<string> handledRepeated =
                new HashSet<string>(StringComparer.Ordinal);

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement child = node as XmlElement;
                if (child == null)
                    continue;

                bool repeated = counts[child.Name] > 1;
                if (repeated)
                {
                    if (handledRepeated.Add(child.Name))
                    {
                        ConfigEditOption listOption;
                        if (TryCreateEnabledIdListOption(
                                parent, child.Name, path,
                                out listOption))
                            result.Add(listOption);
                    }
                    continue;
                }

                ConfigEditOption attributeOption;
                if (TryCreateEnabledAttributeOption(
                        parent, child, path, false,
                        out attributeOption))
                {
                    result.Add(attributeOption);
                    continue;
                }

                string childPath = path.Length == 0
                    ? child.Name : path + "/" + child.Name;
                bool complex = false;
                foreach (XmlNode inner in child.ChildNodes)
                    if (inner.NodeType == XmlNodeType.Element)
                        complex = true;
                if (complex)
                {
                    Collect(child, childPath, result);
                    continue;
                }

                if (child.Attributes.Count > 0)
                    continue;
                bool plain = true;
                foreach (XmlNode inner in child.ChildNodes)
                    if (inner.NodeType != XmlNodeType.Text &&
                        inner.NodeType != XmlNodeType.Whitespace &&
                        inner.NodeType != XmlNodeType.SignificantWhitespace &&
                        inner.NodeType != XmlNodeType.Comment)
                        plain = false;
                if (!plain)
                    continue;

                ConfigEditOption option = new ConfigEditOption();
                option.Path = childPath;
                option.ElementPath = childPath;
                option.Name = child.Name;
                option.Label = child.Name;
                option.Section = path.Length == 0
                    ? string.Empty
                    : path.Replace("/", " > ");
                option.Control = "text";
                option.Value = child.InnerText.Trim();
                option.OriginalValue = option.Value;
                option.Step = 1m;
                ParseMetadata(child, option);
                if (option.Control == "multiline-list")
                    option.Control = "text";
                result.Add(option);
            }
        }

        private static bool TryCreateSimpleValueListOption(
            XmlElement parent,
            string path,
            out ConfigEditOption option)
        {
            option = null;
            List<string> values = new List<string>();
            HashSet<string> known =
                new HashSet<string>(StringComparer.Ordinal);
            int elementCount = 0;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null)
                    continue;

                elementCount++;
                if (!string.Equals(
                        element.Name, "Value",
                        StringComparison.Ordinal) ||
                    element.Attributes.Count != 0)
                    return false;

                foreach (XmlNode child in element.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Text ||
                        child.NodeType == XmlNodeType.Comment ||
                        child.NodeType == XmlNodeType.Whitespace ||
                        child.NodeType == XmlNodeType.SignificantWhitespace)
                        continue;
                    return false;
                }

                string value = (element.InnerText ??
                    string.Empty).Trim();
                if (value.Length == 0 || !known.Add(value))
                    return false;
                values.Add(value);
            }

            ConfigEditOption result =
                new ConfigEditOption();
            result.Path = path.Length == 0
                ? parent.Name : path;
            result.ElementPath = path;
            result.ElementName = "Value";
            result.Name = parent.Name;
            result.Label = parent.Name;

            int separator = path.LastIndexOf('/');
            result.Section = separator < 0
                ? string.Empty
                : path.Substring(0, separator)
                    .Replace("/", " > ");
            result.Control = elementCount == 0
                ? "text"
                : "multiline-list";
            result.Value = JoinLines(values);
            result.OriginalValue = result.Value;
            result.Step = 1m;

            ParseMetadata(parent, result);

            // A non-empty conventional <Value> container can be inferred.
            // Empty containers require explicit multiline-list metadata so
            // reopening a staged empty list remains editable.
            if (elementCount == 0 &&
                result.Control != "multiline-list")
                return false;
            if (elementCount > 0 &&
                result.Control != "multiline-list")
                return false;

            option = result;
            return true;
        }

        private static bool TryCreateEnabledIdListOption(
            XmlElement parent,
            string repeatedElementName,
            string path,
            out ConfigEditOption option)
        {
            option = null;
            List<string> enabledIds = new List<string>();
            HashSet<string> knownIds =
                new HashSet<string>(StringComparer.Ordinal);
            int matched = 0;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null ||
                    !string.Equals(
                        element.Name,
                        repeatedElementName,
                        StringComparison.Ordinal))
                    continue;

                matched++;
                foreach (XmlNode child in element.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Comment ||
                        child.NodeType == XmlNodeType.Whitespace ||
                        child.NodeType == XmlNodeType.SignificantWhitespace)
                        continue;
                    return false;
                }

                XmlAttribute idAttribute = element.Attributes["ID"];
                XmlAttribute enabledAttribute =
                    element.Attributes["Enabled"];
                if (idAttribute == null ||
                    enabledAttribute == null)
                    return false;

                string id = (idAttribute.Value ??
                    string.Empty).Trim();
                bool enabled;
                if (id.Length == 0 ||
                    !bool.TryParse(
                        (enabledAttribute.Value ??
                            string.Empty).Trim(),
                        out enabled) ||
                    !knownIds.Add(id))
                    return false;

                if (enabled)
                    enabledIds.Add(id);
            }

            if (matched < 2)
                return false;

            ConfigEditOption result =
                new ConfigEditOption();
            result.Path = path.Length == 0
                ? repeatedElementName
                : path + "/" + repeatedElementName;
            result.ElementPath = path;
            result.ElementName = repeatedElementName;
            result.MatchAttributeName = "ID";
            result.ValueAttributeName = "Enabled";
            result.Name = parent.Name;
            result.Label = parent.Name;

            int separator = path.LastIndexOf('/');
            result.Section = separator < 0
                ? string.Empty
                : path.Substring(0, separator)
                    .Replace("/", " > ");
            result.Control = "multiline-list";
            result.Value = JoinLines(enabledIds);
            result.OriginalValue = result.Value;
            result.Step = 1m;
            option = result;
            return true;
        }

        private static string JoinLines(
            List<string> values)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                    builder.Append("\n");
                builder.Append(values[i]);
            }
            return builder.ToString();
        }

        private static bool TryNormalizeLineList(
            string value,
            out string normalized,
            out List<string> values)
        {
            values = new List<string>();
            HashSet<string> seen =
                new HashSet<string>(StringComparer.Ordinal);
            string[] lines = (value ?? string.Empty).Split(
                new char[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                string item =
                    (lines[i] ?? string.Empty).Trim();
                if (item.Length == 0 || !seen.Add(item))
                    continue;
                values.Add(item);
            }

            normalized = JoinLines(values);
            return true;
        }

        private static bool TryCreateEnabledAttributeOption(
            XmlElement parent,
            XmlElement child,
            string path,
            bool repeated,
            out ConfigEditOption option)
        {
            option = null;

            foreach (XmlNode inner in child.ChildNodes)
                if (inner.NodeType == XmlNodeType.Element)
                    return false;

            XmlAttribute enabledAttribute =
                child.Attributes["Enabled"];
            if (enabledAttribute == null)
                return false;

            bool enabled;
            if (!bool.TryParse(
                    enabledAttribute.Value == null
                        ? string.Empty
                        : enabledAttribute.Value.Trim(),
                    out enabled))
                return false;

            string id = null;
            XmlAttribute idAttribute = child.Attributes["ID"];
            if (idAttribute != null)
                id = idAttribute.Value == null
                    ? string.Empty
                    : idAttribute.Value.Trim();

            if (repeated)
            {
                if (string.IsNullOrEmpty(id) ||
                    CountAttributeMatches(
                        parent, child.Name, "ID", id) != 1)
                    return false;
            }

            string childPath = path.Length == 0
                ? child.Name : path + "/" + child.Name;

            ConfigEditOption result =
                new ConfigEditOption();
            result.ElementPath = repeated ? path : childPath;
            result.ElementName = repeated ? child.Name : null;
            result.MatchAttributeName = repeated ? "ID" : null;
            result.MatchAttributeValue = repeated ? id : null;
            result.ValueAttributeName = "Enabled";
            result.Path = repeated
                ? childPath + "[@ID='" + id + "']/@Enabled"
                : childPath + "/@Enabled";
            result.Name = repeated
                ? child.Name + "_" + id
                : child.Name;
            result.Label = !string.IsNullOrEmpty(id)
                ? id : child.Name;
            result.Section = path.Length == 0
                ? string.Empty
                : path.Replace("/", " > ");
            result.Control = "checkbox";
            result.Value = enabled ? "true" : "false";
            result.OriginalValue = result.Value;
            result.Step = 1m;
            result.CompactRow = repeated;

            if (!repeated)
                ParseMetadata(child, result);

            option = result;
            return true;
        }

        private static int CountAttributeMatches(
            XmlElement parent,
            string elementName,
            string attributeName,
            string attributeValue)
        {
            int count = 0;
            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null ||
                    element.Name != elementName)
                    continue;

                XmlAttribute attribute =
                    element.Attributes[attributeName];
                string value = attribute == null ||
                    attribute.Value == null
                        ? string.Empty
                        : attribute.Value.Trim();
                if (string.Equals(
                        value, attributeValue,
                        StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        private static void ParseMetadata(
            XmlElement element, ConfigEditOption option)
        {
            // Comments belong to the following element until another element.
            List<string> comments = new List<string>();
            XmlNode current = element.PreviousSibling;
            while (current != null)
            {
                if (current.NodeType == XmlNodeType.Element)
                    break;
                if (current.NodeType == XmlNodeType.Comment)
                    comments.Insert(0, current.Value.Trim());
                current = current.PreviousSibling;
            }

            foreach (string comment in comments)
            {
                if (comment.StartsWith("@HMUC:desc", StringComparison.OrdinalIgnoreCase))
                {
                    option.Description = comment.Substring(10).Trim();
                    continue;
                }
                if (!comment.StartsWith("@HMUC:set", StringComparison.OrdinalIgnoreCase))
                {
                    // Preserve legacy help comments until an owned description
                    // is present in the installed configuration.
                    if (!comment.StartsWith("@HMUC:", StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrEmpty(option.Description))
                        option.Description = comment.Trim();
                    continue;
                }

                string attributes = comment.Substring(9);
                foreach (Match match in MetadataAttribute.Matches(attributes))
                {
                    string key = match.Groups[1].Value.ToLowerInvariant();
                    string value = match.Groups[2].Value;
                    decimal number;
                    if (key == "label" && value.Trim().Length > 0)
                        option.Label = value.Trim();
                    else if (key == "control" &&
                        (value == "checkbox" || value == "slider" ||
                         value == "number" || value == "text" ||
                         value == "select" ||
                         value == "multiline-list"))
                        option.Control = value;
                    else if (key == "min" &&
                        decimal.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out number))
                        option.Min = number;
                    else if (key == "max" &&
                        decimal.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out number))
                        option.Max = number;
                    else if (key == "step" &&
                        decimal.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out number) &&
                        number > 0m)
                        option.Step = number;
                    else if (key == "choices")
                        option.Choices = value.Split('|');
                }
            }
            if (option.Control == "slider" &&
                (option.Max <= option.Min || option.Step <= 0m))
                option.Control = "text";
            if (option.Control == "select" &&
                (option.Choices == null || option.Choices.Length == 0))
                option.Control = "text";
        }

        internal static bool Validate(
            ConfigEditOption option, out string normalized)
        {
            normalized = option.Value ?? "";
            if (option.Control == "checkbox")
            {
                bool value;
                if (!bool.TryParse(normalized, out value))
                    return false;
                normalized = value ? "true" : "false";
            }
            else if (option.Control == "slider" ||
                     option.Control == "number")
            {
                decimal value;
                if (!decimal.TryParse(normalized, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value))
                    return false;
                if (option.Control == "slider" &&
                    (value < option.Min || value > option.Max))
                    return false;
                if (option.Control == "number" &&
                    option.Max > option.Min &&
                    (value < option.Min || value > option.Max))
                    return false;
                normalized = value.ToString(CultureInfo.InvariantCulture);
            }
            else if (option.Control == "multiline-list")
            {
                List<string> values;
                return TryNormalizeLineList(
                    normalized, out normalized, out values);
            }
            else if (option.Control == "select")
            {
                bool known = false;
                foreach (string choice in option.Choices)
                    if (string.Equals(choice, normalized, StringComparison.Ordinal))
                        known = true;
                if (!known)
                    return false;
            }
            return true;
        }

        internal static bool Stage(
            ConfigEditSession session, out string error)
        {
            error = null;
            if (session == null || session.Document == null)
            {
                error = "No configuration is open.";
                return false;
            }
            try
            {
                string path = session.ConfigPath;
                if (!File.Exists(path))
                    throw new IOException("The original configuration is missing.");
                string pending = path + ".newconfig";
                string baseline = path + ".newconfig.base";
                if (!File.Exists(pending))
                {
                    // The baseline must exist before publishing the pending config.
                    CopyAtomic(path, baseline);
                }
                else if (!File.Exists(baseline))
                {
                    throw new IOException("Pending configuration baseline is missing.");
                }

                foreach (ConfigEditOption option in session.Options)
                {
                    string normalized;
                    if (!Validate(option, out normalized))
                        throw new FormatException("Invalid setting: " + option.Path);

                    if (option.Control == "multiline-list")
                    {
                        if (!string.Equals(
                                normalized,
                                option.OriginalValue,
                                StringComparison.Ordinal))
                        {
                            XmlElement listParent =
                                string.IsNullOrEmpty(option.ElementPath)
                                    ? session.Document.DocumentElement
                                    : FindElement(
                                        session.Document.DocumentElement,
                                        option.ElementPath);
                            if (listParent == null)
                                throw new FormatException(
                                    "Setting no longer exists: " +
                                    option.Path);
                            if (!string.IsNullOrEmpty(
                                    option.MatchAttributeName))
                                ApplyEnabledIdList(
                                    listParent, option, normalized);
                            else
                                ApplySimpleValueList(
                                    listParent, option, normalized);
                        }
                        option.Value = normalized;
                        continue;
                    }

                    XmlElement element = FindOptionElement(
                        session.Document.DocumentElement, option);
                    if (element == null)
                        throw new FormatException("Setting no longer exists: " + option.Path);
                    if (!string.Equals(option.Value, option.OriginalValue,
                        StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrEmpty(
                                option.ValueAttributeName))
                            element.SetAttribute(
                                option.ValueAttributeName,
                                normalized);
                        else
                            SetLeafText(element, normalized);
                    }
                }
                SaveAtomic(session.Document, pending);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static void ApplySimpleValueList(
            XmlElement parent,
            ConfigEditOption option,
            string normalized)
        {
            string canonical;
            List<string> values;
            TryNormalizeLineList(
                normalized, out canonical, out values);

            List<XmlElement> existing =
                new List<XmlElement>();
            HashSet<string> known =
                new HashSet<string>(StringComparer.Ordinal);

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null)
                    continue;
                if (!string.Equals(
                        element.Name,
                        option.ElementName,
                        StringComparison.Ordinal) ||
                    element.Attributes.Count != 0)
                    throw new FormatException(
                        "List structure changed: " +
                        option.Path);

                foreach (XmlNode child in element.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Text ||
                        child.NodeType == XmlNodeType.Comment ||
                        child.NodeType == XmlNodeType.Whitespace ||
                        child.NodeType == XmlNodeType.SignificantWhitespace)
                        continue;
                    throw new FormatException(
                        "List structure changed: " +
                        option.Path);
                }

                string value = (element.InnerText ??
                    string.Empty).Trim();
                if (value.Length == 0 || !known.Add(value))
                    throw new FormatException(
                        "List values are no longer unique: " +
                        option.Path);
                existing.Add(element);
            }

            int shared = Math.Min(
                existing.Count, values.Count);
            for (int i = 0; i < shared; i++)
                SetLeafText(existing[i], values[i]);

            for (int i = existing.Count - 1;
                i >= values.Count; i--)
                parent.RemoveChild(existing[i]);

            if (values.Count <= existing.Count)
                return;

            XmlDocument document = parent.OwnerDocument;
            if (document == null)
                throw new FormatException(
                    "List document is unavailable: " +
                    option.Path);

            XmlNode insertAfter =
                existing.Count == 0
                    ? null
                    : existing[existing.Count - 1];
            string indent = "\n    ";
            if (insertAfter != null &&
                insertAfter.PreviousSibling != null &&
                (insertAfter.PreviousSibling.NodeType ==
                    XmlNodeType.Whitespace ||
                 insertAfter.PreviousSibling.NodeType ==
                    XmlNodeType.SignificantWhitespace))
                indent = insertAfter.PreviousSibling.Value;

            for (int i = existing.Count;
                i < values.Count; i++)
            {
                XmlElement element =
                    document.CreateElement(
                        option.ElementName);
                element.AppendChild(
                    document.CreateTextNode(values[i]));

                if (insertAfter == null)
                {
                    parent.AppendChild(element);
                    insertAfter = element;
                }
                else
                {
                    XmlWhitespace spacing =
                        document.CreateWhitespace(indent);
                    parent.InsertAfter(
                        spacing, insertAfter);
                    parent.InsertAfter(
                        element, spacing);
                    insertAfter = element;
                }
            }
        }

        private static void ApplyEnabledIdList(
            XmlElement parent,
            ConfigEditOption option,
            string normalized)
        {
            string canonical;
            List<string> enabledIds;
            TryNormalizeLineList(
                normalized, out canonical, out enabledIds);

            HashSet<string> enabled =
                new HashSet<string>(
                    enabledIds, StringComparer.Ordinal);
            HashSet<string> existing =
                new HashSet<string>(StringComparer.Ordinal);
            XmlElement lastMatching = null;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null ||
                    !string.Equals(
                        element.Name,
                        option.ElementName,
                        StringComparison.Ordinal))
                    continue;

                XmlAttribute idAttribute =
                    element.Attributes[
                        option.MatchAttributeName];
                XmlAttribute enabledAttribute =
                    element.Attributes[
                        option.ValueAttributeName];
                if (idAttribute == null ||
                    enabledAttribute == null)
                    throw new FormatException(
                        "List structure changed: " +
                        option.Path);

                string id = (idAttribute.Value ??
                    string.Empty).Trim();
                if (id.Length == 0 || !existing.Add(id))
                    throw new FormatException(
                        "List identifiers are no longer unique: " +
                        option.Path);

                enabledAttribute.Value =
                    enabled.Contains(id)
                        ? "true" : "false";
                lastMatching = element;
            }

            XmlDocument document = parent.OwnerDocument;
            if (document == null)
                throw new FormatException(
                    "List document is unavailable: " +
                    option.Path);

            XmlNode insertAfter = lastMatching;
            string indent = "\n    ";
            if (lastMatching != null &&
                lastMatching.PreviousSibling != null &&
                (lastMatching.PreviousSibling.NodeType ==
                    XmlNodeType.Whitespace ||
                 lastMatching.PreviousSibling.NodeType ==
                    XmlNodeType.SignificantWhitespace))
            {
                indent =
                    lastMatching.PreviousSibling.Value;
            }

            for (int i = 0; i < enabledIds.Count; i++)
            {
                string id = enabledIds[i];
                if (existing.Contains(id))
                    continue;

                XmlElement element =
                    document.CreateElement(
                        option.ElementName);
                element.SetAttribute(
                    option.MatchAttributeName, id);
                element.SetAttribute(
                    option.ValueAttributeName, "true");

                if (insertAfter == null)
                {
                    parent.AppendChild(element);
                    insertAfter = element;
                }
                else
                {
                    XmlWhitespace spacing =
                        document.CreateWhitespace(indent);
                    parent.InsertAfter(
                        spacing, insertAfter);
                    parent.InsertAfter(
                        element, spacing);
                    insertAfter = element;
                }

                existing.Add(id);
            }
        }

        private static void SetLeafText(XmlElement element, string value)
        {
            XmlText text = null;
            foreach (XmlNode child in element.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.CDATA ||
                    child.NodeType == XmlNodeType.Element)
                    throw new FormatException("Complex XML leaf cannot be edited.");
                if (child.NodeType == XmlNodeType.Text)
                {
                    if (text != null)
                        throw new FormatException("Split text nodes are not editable.");
                    text = (XmlText)child;
                }
            }
            if (text == null)
                element.AppendChild(element.OwnerDocument.CreateTextNode(value));
            else
                text.Value = value;
        }

        private static XmlElement FindOptionElement(
            XmlElement root, ConfigEditOption option)
        {
            if (option == null)
                return null;

            if (string.IsNullOrEmpty(option.ElementName))
                return FindElement(
                    root,
                    option.ElementPath ?? option.Path);

            XmlElement parent = string.IsNullOrEmpty(
                option.ElementPath)
                    ? root
                    : FindElement(root, option.ElementPath);
            if (parent == null)
                return null;

            XmlElement match = null;
            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement candidate = node as XmlElement;
                if (candidate == null ||
                    candidate.Name != option.ElementName)
                    continue;

                XmlAttribute attribute =
                    candidate.Attributes[
                        option.MatchAttributeName];
                string value = attribute == null ||
                    attribute.Value == null
                        ? string.Empty
                        : attribute.Value.Trim();
                if (!string.Equals(
                        value,
                        option.MatchAttributeValue,
                        StringComparison.Ordinal))
                    continue;

                if (match != null)
                    return null;
                match = candidate;
            }

            return match;
        }

        private static XmlElement FindElement(
            XmlElement root, string path)
        {
            if (root == null)
                return null;
            if (string.IsNullOrEmpty(path))
                return root;

            string[] segments = path.Split('/');
            XmlElement element = root;
            foreach (string segment in segments)
            {
                XmlElement next = null;
                foreach (XmlNode child in element.ChildNodes)
                {
                    XmlElement candidate = child as XmlElement;
                    if (candidate == null || candidate.Name != segment)
                        continue;
                    if (next != null)
                        return null;
                    next = candidate;
                }
                if (next == null)
                    return null;
                element = next;
            }
            return element;
        }

        private static void CopyAtomic(string from, string to)
        {
            string temp = to + ".tmp";
            if (File.Exists(temp))
                File.Delete(temp);
            File.Copy(from, temp);
            if (File.Exists(to))
                File.Delete(to);
            File.Move(temp, to);
        }

        private static void SaveAtomic(XmlDocument document, string to)
        {
            string temp = to + ".tmp";
            string previous = to + ".previous";
            if (File.Exists(temp))
                File.Delete(temp);
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            settings.Indent = false;
            using (XmlWriter writer = XmlWriter.Create(temp, settings))
                document.Save(writer);
            // Verify the candidate before replacing the previous pending file.
            LoadSafe(temp);
            if (File.Exists(previous))
                File.Delete(previous);
            if (File.Exists(to))
                File.Move(to, previous);
            try
            {
                File.Move(temp, to);
                if (File.Exists(previous))
                    File.Delete(previous);
            }
            catch
            {
                if (!File.Exists(to) && File.Exists(previous))
                    File.Move(previous, to);
                throw;
            }
        }
    }
}
