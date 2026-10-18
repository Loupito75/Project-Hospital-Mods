using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using BepInEx;

namespace HospitalModUpdateChecker.Bootstrap
{
    // Runs before BepInEx loads user plugins. Separate from update-package backups.
    internal static class PendingConfigApplier
    {
        private const int MaxBackups = 5;
        private static readonly StringComparer PathComparer =
            StringComparer.OrdinalIgnoreCase;

        internal static void Recover()
        {
            foreach (string directory in GetPluginDirectories())
            {
                string config = GetConfigPath(directory);
                // Recover a config-editor write interrupted between
                // renaming the old pending file and publishing the new one.
                string pending = config + ".newconfig";
                string pendingPrevious = pending + ".previous";
                try
                {
                    if (!File.Exists(pending) && File.Exists(pendingPrevious))
                        File.Move(pendingPrevious, pending);
                    else if (File.Exists(pending) && File.Exists(pendingPrevious))
                        File.Delete(pendingPrevious);
                    string pendingTemporary = pending + ".tmp";
                    if (File.Exists(pendingTemporary))
                        File.Delete(pendingTemporary);
                }
                catch (Exception exception)
                {
                    BootstrapInstaller.LogWarning(
                        "HMUC could not recover pending XML staging for " +
                        Path.GetFileName(directory) + ": " + exception.Message);
                }
                string previous = config + ".hmuc-previous";
                if (!File.Exists(previous))
                    continue;
                try
                {
                    // The pending request is retained until replacement is confirmed.
                    // An interrupted swap is always rolled back and retried.
                    if (File.Exists(config + ".newconfig") ||
                        !File.Exists(config) || !IsValidXml(config))
                    {
                        if (File.Exists(config))
                            File.Delete(config);
                        File.Move(previous, config);
                        BootstrapInstaller.LogWarning(
                            "HMUC restored an interrupted XML configuration swap for " +
                            Path.GetFileName(directory) + ".");
                    }
                    else
                    {
                        File.Delete(previous);
                    }
                }
                catch (Exception exception)
                {
                    BootstrapInstaller.LogWarning(
                        "HMUC could not recover the previous configuration for " +
                        Path.GetFileName(directory) + ": " + exception.Message);
                }
            }
        }

        internal static void Apply()
        {
            foreach (string directory in GetPluginDirectories())
            {
                string config = GetConfigPath(directory);
                string pending = config + ".newconfig";
                if (!File.Exists(pending))
                    continue;
                try
                {
                    ApplyOne(config);
                }
                catch (Exception exception)
                {
                    BootstrapInstaller.LogWarning(
                        "HMUC left the pending XML configuration unchanged for " +
                        Path.GetFileName(directory) + ": " + exception.Message);
                }
            }
        }

        private static string[] GetPluginDirectories()
        {
            try
            {
                string root = Path.GetFullPath(
                    Path.Combine(Paths.BepInExRootPath, "plugins"));
                if (!Directory.Exists(root))
                    return new string[0];
                return Directory.GetDirectories(root);
            }
            catch (Exception exception)
            {
                BootstrapInstaller.LogWarning(
                    "HMUC cannot enumerate config directories: " + exception.Message);
                return new string[0];
            }
        }

        private static string GetConfigPath(string directory)
        {
            // The basename is forced to the canonical direct-child directory name.
            // HAL keeps its confirmed legacy Rooms.xml configuration name.
            string name = Path.GetFileName(directory);
            string fileName =
                string.Equals(
                    name,
                    "Loupito75.HospitalAlwaysLit",
                    StringComparison.Ordinal)
                    ? name + ".Rooms.xml"
                    : name + ".Config.xml";
            return Path.Combine(directory, fileName);
        }

        private static void SetLeafText(XmlElement element, string value)
        {
            XmlText text = null;
            foreach (XmlNode child in element.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element ||
                    child.NodeType == XmlNodeType.CDATA)
                    throw new FormatException("Complex XML leaf cannot be edited.");
                if (child.NodeType == XmlNodeType.Text)
                {
                    if (text != null)
                        throw new FormatException("Split text nodes cannot be edited.");
                    text = (XmlText)child;
                }
            }
            if (text != null)
                text.Value = value;
            else
                element.AppendChild(element.OwnerDocument.CreateTextNode(value));
        }

        private static bool IsValidXml(string file)
        {
            try { return LoadSafe(file) != null; }
            catch { return false; }
        }

        private static XmlDocument LoadSafe(string file)
        {
            XmlDocument doc = new XmlDocument();
            doc.XmlResolver = null;
            doc.PreserveWhitespace = true;
            doc.Load(file);
            if (doc.DocumentType != null || doc.DocumentElement == null)
                throw new FormatException("DTD or empty XML is not supported.");
            return doc;
        }

        private static void ApplyOne(string config)
        {
            string pending = config + ".newconfig";
            string baseline = pending + ".base";
            string previous = config + ".hmuc-previous";
            string temp = config + ".hmuc-swapping";
            if (!File.Exists(config) || !File.Exists(baseline))
                throw new IOException("Active configuration or baseline is missing.");
            if (File.Exists(previous))
                throw new IOException("A previous config transaction needs recovery.");

            XmlDocument original = LoadSafe(baseline);
            XmlDocument requested = LoadSafe(pending);
            XmlDocument current = LoadSafe(config);
            if (original.DocumentElement.Name != requested.DocumentElement.Name ||
                original.DocumentElement.Name != current.DocumentElement.Name)
                throw new FormatException("The configuration root has changed.");

            int changed = 0;
            ApplyRequestedChanges(
                original.DocumentElement,
                requested.DocumentElement,
                current.DocumentElement,
                original.DocumentElement.Name,
                ref changed);

            if (changed == 0)
            {
                File.Delete(pending);
                File.Delete(baseline);
                return;
            }

            if (File.Exists(temp))
                File.Delete(temp);
            SaveXml(current, temp);
            LoadSafe(temp);

            // Keep the previous active XML as a timestamped user history entry.
            // A backup must succeed before the active file is ever moved.
            string backup = config + "." +
                DateTime.UtcNow.ToString(
                    "yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture) +
                ".backup";
            File.Copy(config, backup, false);

            try
            {
                File.Move(config, previous);
                File.Move(temp, config);
                LoadSafe(config);

                // Clear the request only after the new active file is valid.
                // If interrupted, Recover() will restore the previous file.
                // The swap is now complete. Cleanup failures must not
                // trigger rollback of a verified active configuration.
                try
                {
                    File.Delete(pending);
                    File.Delete(baseline);
                    if (!File.Exists(pending))
                        File.Delete(previous);
                    PruneBackups(config);
                }
                catch (Exception cleanup)
                {
                    BootstrapInstaller.LogWarning(
                        "HMUC applied settings but cleanup is incomplete: " +
                        cleanup.Message);
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(previous))
                    {
                        if (File.Exists(config))
                            File.Delete(config);
                        File.Move(previous, config);
                    }
                }
                catch (Exception rollback)
                {
                    BootstrapInstaller.LogWarning(
                        "HMUC XML rollback requires recovery: " + rollback.Message);
                }
                throw;
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        private static void ApplyRequestedChanges(
            XmlElement original,
            XmlElement requested,
            XmlElement current,
            string path,
            ref int changed)
        {
            if (original == null || requested == null || current == null ||
                !string.Equals(original.Name, requested.Name, StringComparison.Ordinal) ||
                !string.Equals(original.Name, current.Name, StringComparison.Ordinal))
                throw new FormatException(
                    "The configuration structure changed at " + path + ".");

            ApplyAttributeChanges(
                original, requested, current, path, ref changed);

            Dictionary<string, List<XmlElement>> originalGroups =
                GetChildGroups(original);
            Dictionary<string, List<XmlElement>> requestedGroups =
                GetChildGroups(requested);
            Dictionary<string, List<XmlElement>> currentGroups =
                GetChildGroups(current);

            if (originalGroups.Count == 0 &&
                requestedGroups.Count == 0)
            {
                string oldValue = original.InnerText;
                string desiredValue = requested.InnerText;

                if (currentGroups.Count != 0)
                {
                    List<XmlElement> currentValues;
                    if (string.Equals(
                            oldValue, desiredValue,
                            StringComparison.Ordinal) &&
                        currentGroups.Count == 1 &&
                        currentGroups.TryGetValue(
                            "Value", out currentValues) &&
                        IsSimpleValueGroup(
                            "Value", currentValues))
                        return;

                    throw new FormatException(
                        "Setting became complex during mod update: " + path);
                }

                if (!string.Equals(
                        oldValue, desiredValue,
                        StringComparison.Ordinal))
                {
                    if (current.InnerText != oldValue &&
                        current.InnerText != desiredValue)
                        throw new FormatException(
                            "Conflicting edit to " + path);
                    SetLeafText(current, desiredValue);
                    changed++;
                }
                return;
            }

            foreach (KeyValuePair<string, List<XmlElement>> entry
                in originalGroups)
            {
                List<XmlElement> requestedChildren;
                if (!requestedGroups.TryGetValue(
                        entry.Key, out requestedChildren))
                {
                    if (IsSimpleValueGroup(
                            entry.Key, entry.Value))
                        requestedChildren =
                            new List<XmlElement>();
                    else
                        throw new FormatException(
                            "The pending XML structure changed at " +
                            path + "/" + entry.Key + ".");
                }

                List<XmlElement> currentChildren;
                if (!currentGroups.TryGetValue(
                        entry.Key, out currentChildren))
                {
                    if (string.Equals(
                            entry.Key, "Value",
                            StringComparison.Ordinal))
                        currentChildren =
                            new List<XmlElement>();
                    else
                        throw new FormatException(
                            "Setting removed during mod update: " +
                            path + "/" + entry.Key);
                }

                if (IsSimpleValueGroup(
                        entry.Key, entry.Value) &&
                    IsSimpleValueGroup(
                        entry.Key, requestedChildren) &&
                    IsSimpleValueGroup(
                        entry.Key, currentChildren))
                {
                    ApplySimpleValueGroup(
                        entry.Value,
                        requestedChildren,
                        currentChildren,
                        current,
                        path + "/" + entry.Key,
                        ref changed);
                    continue;
                }

                if (entry.Value.Count == 1 &&
                    requestedChildren.Count == 1)
                {
                    if (currentChildren.Count != 1)
                        throw new FormatException(
                            "The configuration structure changed at " +
                            path + "/" + entry.Key + ".");

                    ApplyRequestedChanges(
                        entry.Value[0],
                        requestedChildren[0],
                        currentChildren[0],
                        path + "/" + entry.Key,
                        ref changed);
                    continue;
                }

                ApplyRepeatedIdGroup(
                    entry.Value,
                    requestedChildren,
                    currentChildren,
                    current,
                    path + "/" + entry.Key,
                    ref changed);
            }

            foreach (KeyValuePair<string, List<XmlElement>> entry
                in requestedGroups)
            {
                if (originalGroups.ContainsKey(entry.Key))
                    continue;

                if (IsSimpleValueGroup(
                        entry.Key, entry.Value))
                {
                    List<XmlElement> currentChildren;
                    if (!currentGroups.TryGetValue(
                            entry.Key, out currentChildren))
                        currentChildren =
                            new List<XmlElement>();

                    if (!IsSimpleValueGroup(
                            entry.Key, currentChildren))
                        throw new FormatException(
                            "The configuration structure changed at " +
                            path + "/" + entry.Key + ".");

                    ApplySimpleValueGroup(
                        new List<XmlElement>(),
                        entry.Value,
                        currentChildren,
                        current,
                        path + "/" + entry.Key,
                        ref changed);
                    continue;
                }

                throw new FormatException(
                    "The pending XML structure changed at " +
                    path + "/" + entry.Key + ".");
            }
        }

        private static void ApplyAttributeChanges(
            XmlElement original,
            XmlElement requested,
            XmlElement current,
            string path,
            ref int changed)
        {
            foreach (XmlAttribute oldAttribute in original.Attributes)
            {
                XmlAttribute desiredAttribute =
                    requested.Attributes[oldAttribute.Name];
                if (desiredAttribute == null)
                    throw new FormatException(
                        "The pending XML structure changed at " +
                        path + "/@" + oldAttribute.Name + ".");

                if (string.Equals(
                        oldAttribute.Value,
                        desiredAttribute.Value,
                        StringComparison.Ordinal))
                    continue;

                XmlAttribute currentAttribute =
                    current.Attributes[oldAttribute.Name];
                if (currentAttribute == null)
                    throw new FormatException(
                        "Setting removed during mod update: " +
                        path + "/@" + oldAttribute.Name);

                if (!string.Equals(
                        currentAttribute.Value,
                        oldAttribute.Value,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        currentAttribute.Value,
                        desiredAttribute.Value,
                        StringComparison.Ordinal))
                    throw new FormatException(
                        "Conflicting edit to " +
                        path + "/@" + oldAttribute.Name);

                currentAttribute.Value = desiredAttribute.Value;
                changed++;
            }

            foreach (XmlAttribute desiredAttribute in requested.Attributes)
            {
                if (original.Attributes[desiredAttribute.Name] == null)
                    throw new FormatException(
                        "The pending XML structure changed at " +
                        path + "/@" + desiredAttribute.Name + ".");
            }
        }

        private static Dictionary<string, List<XmlElement>>
            GetChildGroups(XmlElement parent)
        {
            Dictionary<string, List<XmlElement>> groups =
                new Dictionary<string, List<XmlElement>>(
                    StringComparer.Ordinal);

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement child = node as XmlElement;
                if (child == null)
                    continue;

                List<XmlElement> group;
                if (!groups.TryGetValue(child.Name, out group))
                {
                    group = new List<XmlElement>();
                    groups.Add(child.Name, group);
                }
                group.Add(child);
            }

            return groups;
        }

        private static bool IsSimpleValueGroup(
            string elementName,
            List<XmlElement> elements)
        {
            if (!string.Equals(
                    elementName, "Value",
                    StringComparison.Ordinal))
                return false;

            HashSet<string> values =
                new HashSet<string>(
                    StringComparer.Ordinal);
            for (int i = 0; i < elements.Count; i++)
            {
                XmlElement element = elements[i];
                if (element.Attributes.Count != 0 ||
                    HasElementChildren(element))
                    return false;

                string value = (element.InnerText ??
                    string.Empty).Trim();
                if (value.Length == 0 ||
                    !values.Add(value))
                    return false;
            }

            return true;
        }

        private static void ApplySimpleValueGroup(
            List<XmlElement> original,
            List<XmlElement> requested,
            List<XmlElement> current,
            XmlElement currentParent,
            string path,
            ref int changed)
        {
            Dictionary<string, XmlElement> originalValues =
                BuildValueMap(original, path);
            Dictionary<string, XmlElement> requestedValues =
                BuildValueMap(requested, path);
            Dictionary<string, XmlElement> currentValues =
                BuildValueMap(current, path);

            for (int i = 0; i < original.Count; i++)
            {
                string value = (original[i].InnerText ??
                    string.Empty).Trim();
                if (requestedValues.ContainsKey(value))
                    continue;

                XmlElement actual;
                if (!currentValues.TryGetValue(
                        value, out actual))
                    continue;

                currentParent.RemoveChild(actual);
                currentValues.Remove(value);
                changed++;
            }

            for (int i = 0; i < requested.Count; i++)
            {
                XmlElement desired = requested[i];
                string value = (desired.InnerText ??
                    string.Empty).Trim();
                if (originalValues.ContainsKey(value) ||
                    currentValues.ContainsKey(value))
                    continue;

                AppendRepeatedElement(
                    currentParent, desired);
                currentValues.Add(
                    value, desired);
                changed++;
            }
        }

        private static Dictionary<string, XmlElement> BuildValueMap(
            List<XmlElement> elements,
            string path)
        {
            Dictionary<string, XmlElement> map =
                new Dictionary<string, XmlElement>(
                    StringComparer.Ordinal);

            for (int i = 0; i < elements.Count; i++)
            {
                XmlElement element = elements[i];
                if (element.Attributes.Count != 0 ||
                    HasElementChildren(element))
                    throw new FormatException(
                        "Unsupported value list at " +
                        path + ".");

                string value = (element.InnerText ??
                    string.Empty).Trim();
                if (value.Length == 0 ||
                    map.ContainsKey(value))
                    throw new FormatException(
                        "Unsupported value list at " +
                        path + ".");

                map.Add(value, element);
            }

            return map;
        }

        private static void ApplyRepeatedIdGroup(
            List<XmlElement> original,
            List<XmlElement> requested,
            List<XmlElement> current,
            XmlElement currentParent,
            string path,
            ref int changed)
        {
            Dictionary<string, XmlElement> originalById =
                BuildIdMap(original, path, true);
            Dictionary<string, XmlElement> requestedById =
                BuildIdMap(requested, path, true);
            Dictionary<string, XmlElement> currentById =
                BuildIdMap(current, path, false);

            foreach (KeyValuePair<string, XmlElement> entry
                in originalById)
            {
                XmlElement desired;
                if (!requestedById.TryGetValue(entry.Key, out desired))
                    throw new FormatException(
                        "The pending XML structure changed at " +
                        path + "[@ID='" + entry.Key + "'].");

                XmlElement actual;
                if (!currentById.TryGetValue(entry.Key, out actual))
                    throw new FormatException(
                        "Setting removed during mod update: " +
                        path + "[@ID='" + entry.Key + "']");

                ApplyRequestedChanges(
                    entry.Value,
                    desired,
                    actual,
                    path + "[@ID='" + entry.Key + "']",
                    ref changed);
            }

            foreach (KeyValuePair<string, XmlElement> entry
                in requestedById)
            {
                if (originalById.ContainsKey(entry.Key))
                    continue;

                XmlElement actual;
                if (currentById.TryGetValue(entry.Key, out actual))
                {
                    XmlAttribute desiredEnabled =
                        entry.Value.Attributes["Enabled"];
                    XmlAttribute actualEnabled =
                        actual.Attributes["Enabled"];
                    if (desiredEnabled == null || actualEnabled == null)
                        throw new FormatException(
                            "Unsupported repeated setting at " +
                            path + "[@ID='" + entry.Key + "'].");

                    if (!string.Equals(
                            actualEnabled.Value,
                            desiredEnabled.Value,
                            StringComparison.Ordinal))
                    {
                        actualEnabled.Value = desiredEnabled.Value;
                        changed++;
                    }
                    continue;
                }

                AppendRepeatedElement(currentParent, entry.Value);
                changed++;
            }
        }

        private static Dictionary<string, XmlElement> BuildIdMap(
            List<XmlElement> elements,
            string path,
            bool requireEnabled)
        {
            Dictionary<string, XmlElement> map =
                new Dictionary<string, XmlElement>(
                    StringComparer.Ordinal);

            for (int i = 0; i < elements.Count; i++)
            {
                XmlElement element = elements[i];
                if (HasElementChildren(element))
                    throw new FormatException(
                        "Unsupported repeated setting at " + path + ".");

                XmlAttribute idAttribute =
                    element.Attributes["ID"];
                XmlAttribute enabledAttribute =
                    element.Attributes["Enabled"];
                string id = idAttribute == null ||
                    idAttribute.Value == null
                        ? string.Empty
                        : idAttribute.Value.Trim();

                bool enabled;
                if (id.Length == 0 ||
                    (requireEnabled &&
                     (enabledAttribute == null ||
                      !bool.TryParse(
                          enabledAttribute.Value, out enabled))) ||
                    map.ContainsKey(id))
                    throw new FormatException(
                        "Unsupported repeated setting at " + path + ".");

                map.Add(id, element);
            }

            return map;
        }

        private static bool HasElementChildren(
            XmlElement element)
        {
            foreach (XmlNode child in element.ChildNodes)
                if (child.NodeType == XmlNodeType.Element)
                    return true;
            return false;
        }

        private static void AppendRepeatedElement(
            XmlElement parent,
            XmlElement requested)
        {
            XmlDocument document = parent.OwnerDocument;
            if (document == null)
                throw new FormatException(
                    "Configuration document is unavailable.");

            XmlElement clone = (XmlElement)
                document.ImportNode(requested, true);

            XmlNode trailingWhitespace = null;
            XmlNode cursor = parent.LastChild;
            while (cursor != null &&
                (cursor.NodeType == XmlNodeType.Whitespace ||
                 cursor.NodeType == XmlNodeType.SignificantWhitespace))
            {
                trailingWhitespace = cursor;
                cursor = cursor.PreviousSibling;
            }

            if (trailingWhitespace != null)
            {
                XmlWhitespace spacing =
                    document.CreateWhitespace(
                        trailingWhitespace.Value);
                parent.InsertBefore(spacing, trailingWhitespace);
                parent.InsertBefore(clone, trailingWhitespace);
            }
            else
            {
                parent.AppendChild(clone);
            }
        }

        private static void SaveXml(XmlDocument document, string file)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            settings.Indent = false;
            using (XmlWriter writer = XmlWriter.Create(file, settings))
                document.Save(writer);
        }

        private static void PruneBackups(string config)
        {
            try
            {
                string directory = Path.GetDirectoryName(config);
                string pattern = Path.GetFileName(config) + ".*.backup";
                List<string> files = new List<string>();
                foreach (string path in Directory.GetFiles(directory, pattern))
                {
                    string name = Path.GetFileName(path);
                    string prefix = Path.GetFileName(config) + ".";
                    string stamp = name.Substring(prefix.Length,
                        name.Length - prefix.Length - ".backup".Length);
                    DateTime parsed;
                    if (DateTime.TryParseExact(stamp,
                        "yyyyMMddTHHmmssfffffffZ",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal |
                        DateTimeStyles.AdjustToUniversal, out parsed))
                        files.Add(path);
                }
                files.Sort(PathComparer);
                for (int i = 0; i < files.Count - MaxBackups; i++)
                    File.Delete(files[i]);
            }
            catch (Exception exception)
            {
                BootstrapInstaller.LogWarning(
                    "HMUC could not prune XML backups: " + exception.Message);
            }
        }
    }
}
