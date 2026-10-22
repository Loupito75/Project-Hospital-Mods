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
            try
            {
                ApplyRequestedChanges(
                    original.DocumentElement,
                    requested.DocumentElement,
                    current.DocumentElement,
                    original.DocumentElement.Name,
                    ref changed);
            }
            catch (FormatException)
            {
                // A signed mod update may have relocated uniquely named XML
                // options after the user staged edits against the old schema.
                // Restart with an untouched copy: no partial rebase survives.
                current = LoadSafe(config);
                changed = 0;
                XmlDocument trustedTemplate =
                    LoadTrustedTemplate(config);

                ApplyRelocatedScalarEdits(
                    original.DocumentElement,
                    requested.DocumentElement,
                    current.DocumentElement,
                    trustedTemplate == null
                        ? null
                        : trustedTemplate.DocumentElement,
                    ref changed);
            }

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

        private sealed class EditedScalar
        {
            internal string Path;
            internal string Name;
            internal string OriginalValue;
            internal string DesiredValue;
            internal bool EnabledAttribute;
            internal string NamespaceUri;
        }

        // This conservative fallback is only used if the normal three-way
        // rebase rejects a schema change. Edited lists, repeated elements
        // and type changes remain unsupported and the pending request stays.
        private static void ApplyRelocatedScalarEdits(
            XmlElement original, XmlElement requested,
            XmlElement current, XmlElement trustedTemplate,
            ref int changed)
        {
            List<EditedScalar> edits = new List<EditedScalar>();
            CollectScalarEdits(original, requested,
                string.Empty, edits);

            Dictionary<string, int> originalCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            CountLeafNames(original, originalCounts);

            int relocated = 0;
            int retired = 0;
            for (int i = 0; i < edits.Count; i++)
            {
                EditedScalar edit = edits[i];
                XmlElement actual = FindUniquePath(current, edit.Path);
                if (actual == null)
                {
                    if (IsRetiredScalarEdit(
                            trustedTemplate,
                            edit))
                    {
                        retired++;
                        continue;
                    }

                    int number;
                    if (!originalCounts.TryGetValue(edit.Name, out number))
                        throw new FormatException(
                            "Edited option disappeared: " + edit.Name);
                    actual = number == 1
                        ? FindUniqueNamedScalar(current, edit.Name)
                        : FindByUniqueParentSuffix(
                            original, current, edit.Path, edit.Name);
                    relocated++;
                }

                string actualValue;
                bool attribute;
                if (!TryScalarValue(actual, out actualValue, out attribute) ||
                    attribute != edit.EnabledAttribute ||
                    actual.NamespaceURI != edit.NamespaceUri)
                    throw new FormatException(
                        "Edited option changed its type: " + edit.Path);

                if (!string.Equals(actualValue, edit.OriginalValue,
                        StringComparison.Ordinal) &&
                    !string.Equals(actualValue, edit.DesiredValue,
                        StringComparison.Ordinal))
                    throw new FormatException(
                        "Conflicting XML edit: " + edit.Path);

                if (string.Equals(actualValue, edit.DesiredValue,
                        StringComparison.Ordinal))
                    continue;

                if (attribute)
                    actual.SetAttribute("Enabled", edit.DesiredValue);
                else
                    SetLeafText(actual, edit.DesiredValue);
                changed++;
            }

            if (relocated == 0 && retired == 0)
                throw new FormatException(
                    "XML rebase failed without an unambiguous schema relocation.");
        }

        private static XmlDocument LoadTrustedTemplate(
            string config)
        {
            try
            {
                ConfigTemplateSafety.Snapshot state;

                if (!ConfigTemplateSafety.TryReadState(
                        config,
                        out state) ||
                    !ConfigTemplateSafety.IsTrustedSnapshot(
                        config,
                        state))
                {
                    return null;
                }

                return LoadSafe(
                    ConfigTemplateSafety.TemplatePath(
                        config));
            }
            catch
            {
                return null;
            }
        }

        private static bool IsRetiredScalarEdit(
            XmlElement trustedTemplateRoot,
            EditedScalar edit)
        {
            if (trustedTemplateRoot == null ||
                edit == null)
            {
                return false;
            }

            return CountScalarLeavesByName(
                    trustedTemplateRoot,
                    edit.Name) == 0;
        }

        private static int CountScalarLeavesByName(
            XmlElement root,
            string name)
        {
            if (root == null ||
                string.IsNullOrEmpty(name))
            {
                return 0;
            }

            int count = 0;

            foreach (XmlNode node in root.ChildNodes)
            {
                XmlElement element =
                    node as XmlElement;

                if (element == null)
                    continue;

                if (HasElementChildren(element))
                {
                    count += CountScalarLeavesByName(
                        element,
                        name);
                    continue;
                }

                if (IsCollectionLeafEntry(element) ||
                    !string.Equals(
                        element.Name,
                        name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string value;
                bool attribute;

                if (TryScalarValue(
                        element,
                        out value,
                        out attribute))
                {
                    count++;
                }
            }

            return count;
        }

        private static void CollectScalarEdits(
            XmlElement original, XmlElement requested,
            string path, List<EditedScalar> edits)
        {
            if (original == null || requested == null ||
                original.Name != requested.Name ||
                original.NamespaceURI != requested.NamespaceURI ||
                original.Attributes.Count != requested.Attributes.Count)
                throw new FormatException(
                    "The staged XML schema was modified unexpectedly.");

            string currentPath = path.Length == 0
                ? original.Name : path + "/" + original.Name;
            foreach (XmlAttribute attr in original.Attributes)
                if (requested.Attributes[attr.Name] == null)
                    throw new FormatException(
                        "The staged XML attributes changed unexpectedly.");

            List<XmlElement> before = ElementChildren(original);
            List<XmlElement> after = ElementChildren(requested);
            if (before.Count != after.Count)
                throw new FormatException(
                    "Edited XML collections cannot be relocated.");

            if (before.Count != 0)
            {
                foreach (XmlAttribute attr in original.Attributes)
                    if (attr.Value != requested.GetAttribute(attr.Name))
                        throw new FormatException(
                            "Complex XML attributes cannot be relocated.");

                Dictionary<string, int> counts =
                    new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < before.Count; i++)
                {
                    if (before[i].Name != after[i].Name)
                        throw new FormatException(
                            "Edited XML collections cannot be relocated.");
                    int count;
                    counts.TryGetValue(before[i].Name, out count);
                    counts[before[i].Name] = count + 1;
                }
                for (int i = 0; i < before.Count; i++)
                {
                    if (counts[before[i].Name] > 1)
                    {
                        if (before[i].OuterXml != after[i].OuterXml)
                            throw new FormatException(
                                "Edited repeated XML cannot be relocated.");
                        continue;
                    }
                    CollectScalarEdits(before[i], after[i],
                        currentPath, edits);
                }
                return;
            }

            string oldValue, newValue;
            bool oldAttribute, newAttribute;
            bool oldValid = TryScalarValue(
                original, out oldValue, out oldAttribute);
            bool newValid = TryScalarValue(
                requested, out newValue, out newAttribute);
            if (!oldValid || !newValid ||
                oldAttribute != newAttribute)
            {
                if (original.OuterXml != requested.OuterXml)
                    throw new FormatException(
                        "Unsupported edited XML structure.");
                return;
            }
            if (!string.Equals(oldValue, newValue,
                StringComparison.Ordinal))
                edits.Add(new EditedScalar
                {
                    Path = currentPath,
                    Name = original.Name,
                    OriginalValue = oldValue,
                    DesiredValue = newValue,
                    EnabledAttribute = oldAttribute,
                    NamespaceUri = original.NamespaceURI
                });
        }

        private static List<XmlElement> ElementChildren(XmlElement element)
        {
            List<XmlElement> result = new List<XmlElement>();
            foreach (XmlNode child in element.ChildNodes)
            {
                XmlElement entry = child as XmlElement;
                if (entry != null)
                    result.Add(entry);
            }
            return result;
        }

        private static bool TryScalarValue(
            XmlElement element, out string value, out bool attribute)
        {
            value = null;
            attribute = false;
            if (element == null || HasElementChildren(element))
                return false;

            if (element.Attributes.Count == 1 &&
                element.HasAttribute("Enabled"))
            {
                bool enabled;
                if (!bool.TryParse(element.GetAttribute("Enabled"), out enabled))
                    return false;
                foreach (XmlNode node in element.ChildNodes)
                {
                    if (node.NodeType == XmlNodeType.Text &&
                        !string.IsNullOrEmpty(node.Value) &&
                        node.Value.Trim().Length > 0)
                        return false;
                    if (node.NodeType != XmlNodeType.Text &&
                        node.NodeType != XmlNodeType.Whitespace &&
                        node.NodeType != XmlNodeType.SignificantWhitespace &&
                        node.NodeType != XmlNodeType.Comment)
                        return false;
                }
                attribute = true;
                value = enabled ? "true" : "false";
                return true;
            }
            if (element.Attributes.Count != 0)
                return false;
            foreach (XmlNode node in element.ChildNodes)
                if (node.NodeType != XmlNodeType.Text &&
                    node.NodeType != XmlNodeType.Whitespace &&
                    node.NodeType != XmlNodeType.SignificantWhitespace &&
                    node.NodeType != XmlNodeType.Comment)
                    return false;
            value = element.InnerText;
            return true;
        }

        private static bool IsCollectionLeafEntry(
            XmlElement element)
        {
            if (element == null)
                return false;

            XmlElement parent =
                element.ParentNode as XmlElement;

            if (parent == null)
                return false;

            if (HasHmucMultilineListMetadata(parent))
                return true;

            int sameName = 0;
            int elementCount = 0;
            bool onlySameLeafName = true;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement sibling =
                    node as XmlElement;

                if (sibling == null)
                    continue;

                elementCount++;

                if (HasElementChildren(sibling) ||
                    !string.Equals(
                        sibling.Name,
                        element.Name,
                        StringComparison.Ordinal))
                {
                    onlySameLeafName = false;
                }

                if (string.Equals(
                        sibling.Name,
                        element.Name,
                        StringComparison.Ordinal))
                {
                    sameName++;
                }
            }

            if (sameName > 1)
                return true;

            if (elementCount > 0 &&
                onlySameLeafName &&
                (element.Name == "Value" ||
                 element.Name == "Id" ||
                 element.Name == "Tag" ||
                 element.Name == "Room"))
            {
                return true;
            }

            return false;
        }

        private static bool HasHmucMultilineListMetadata(
            XmlElement element)
        {
            if (element == null)
                return false;

            for (XmlNode node = element.PreviousSibling;
                 node != null &&
                 node.NodeType != XmlNodeType.Element;
                 node = node.PreviousSibling)
            {
                XmlComment comment =
                    node as XmlComment;

                if (comment == null)
                    continue;

                string text =
                    (comment.Value ??
                        string.Empty).Trim();

                if (!text.StartsWith(
                        "@HMUC:set",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return text.IndexOf(
                        "control=\"multiline-list\"",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf(
                        "control='multiline-list'",
                        StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }

        private static void CountLeafNames(
            XmlElement parent, Dictionary<string, int> counts)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                XmlElement element = child as XmlElement;
                if (element == null)
                    continue;
                if (HasElementChildren(element))
                {
                    CountLeafNames(element, counts);
                    continue;
                }
                if (IsCollectionLeafEntry(element))
                    continue;

                string unused;
                bool attribute;
                if (!TryScalarValue(element, out unused, out attribute))
                    continue;
                int count;
                counts.TryGetValue(element.Name, out count);
                counts[element.Name] = count + 1;
            }
        }

        private static XmlElement FindUniquePath(
            XmlElement root, string path)
        {
            string[] parts = path.Split('/');
            if (parts.Length == 0 || root.Name != parts[0])
                return null;
            XmlElement current = root;
            for (int i = 1; i < parts.Length; i++)
            {
                XmlElement found = null;
                foreach (XmlNode child in current.ChildNodes)
                {
                    XmlElement candidate = child as XmlElement;
                    if (candidate == null || candidate.Name != parts[i])
                        continue;
                    if (found != null)
                        throw new FormatException(
                            "Ambiguous XML option path: " + path);
                    found = candidate;
                }
                if (found == null)
                    return null;
                current = found;
            }
            return current;
        }

        private sealed class ScalarLocation
        {
            internal string Path;
            internal XmlElement Element;
        }

        private static XmlElement FindByUniqueParentSuffix(
            XmlElement oldRoot, XmlElement currentRoot,
            string oldPath, string name)
        {
            List<ScalarLocation> oldSettings =
                new List<ScalarLocation>();
            List<ScalarLocation> currentSettings =
                new List<ScalarLocation>();
            CollectNamedLocations(
                oldRoot, oldRoot.Name, name, oldSettings);
            CollectNamedLocations(
                currentRoot, currentRoot.Name, name, currentSettings);

            string[] pieces = oldPath.Split('/');
            // Keep at least the parent and unchanged variable name.
            // Suffix matching is allowed only when exactly one option
            // matches in the source AND in the updated configuration.
            for (int start = 1; start < pieces.Length - 1; start++)
            {
                string suffix = string.Join(
                    "/", pieces, start, pieces.Length - start);
                int oldMatches = 0;
                int newMatches = 0;
                XmlElement found = null;
                for (int i = 0; i < oldSettings.Count; i++)
                    if (HasPathSuffix(oldSettings[i].Path, suffix))
                        oldMatches++;
                for (int i = 0; i < currentSettings.Count; i++)
                    if (HasPathSuffix(currentSettings[i].Path, suffix))
                    {
                        newMatches++;
                        found = currentSettings[i].Element;
                    }
                if (oldMatches == 1 && newMatches == 1)
                    return found;
            }

            throw new FormatException(
                "Ambiguous XML option relocation: " + oldPath);
        }

        private static bool HasPathSuffix(string path, string suffix)
        {
            return string.Equals(path, suffix, StringComparison.Ordinal) ||
                path.EndsWith("/" + suffix, StringComparison.Ordinal);
        }

        private static void CollectNamedLocations(
            XmlElement root, string path, string name,
            List<ScalarLocation> result)
        {
            foreach (XmlNode child in root.ChildNodes)
            {
                XmlElement element = child as XmlElement;
                if (element == null)
                    continue;
                string location = path + "/" + element.Name;
                if (HasElementChildren(element))
                {
                    CollectNamedLocations(
                        element, location, name, result);
                    continue;
                }
                if (!IsCollectionLeafEntry(element) &&
                    element.Name == name)
                    result.Add(new ScalarLocation
                    {
                        Path = location,
                        Element = element
                    });
            }
        }

        private static XmlElement FindUniqueNamedScalar(
            XmlElement root, string name)
        {
            XmlElement match = null;
            FindScalarMatches(root, name, ref match);
            if (match == null)
                throw new FormatException(
                    "Edited XML option no longer exists: " + name);
            return match;
        }

        private static void FindScalarMatches(
            XmlElement root, string name, ref XmlElement match)
        {
            foreach (XmlNode child in root.ChildNodes)
            {
                XmlElement element = child as XmlElement;
                if (element == null)
                    continue;
                if (HasElementChildren(element))
                {
                    FindScalarMatches(element, name, ref match);
                    continue;
                }
                if (IsCollectionLeafEntry(element) ||
                    element.Name != name)
                    continue;
                if (match != null)
                    throw new FormatException(
                        "Ambiguous XML option name: " + name);
                match = element;
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
