using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace HospitalModUpdateChecker
{
    // Shared by the pre-plugin bootstrap and the title-screen editor.
    // A template is created ONLY from an HMUC-managed signed update package.
    // No sidecar means legacy installation: do nothing.
    internal static class ConfigTemplateSafety
    {
        // Both assemblies carry synchronized AssemblyVersion values. This
        // automatically invalidates the cache on every HMUC update; no
        // separately maintained version constant can be forgotten.
        internal static string EngineVersion
        {
            get
            {
                return typeof(ConfigTemplateSafety).Assembly
                    .GetName().Version.ToString();
            }
        }
        private const int MaxXmlBytes = 4 * 1024 * 1024;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(7);

        internal sealed class Snapshot
        {
            internal string ModVersion;
            internal string PluginFile;
            internal string PluginHash;
            internal string TemplateHash;
            internal string LastConfigHash;
            internal string LastEngineVersion;
            internal DateTime LastCheckUtc;
            internal string LastResult;
        }

        internal static string TemplatePath(string config)
        {
            return config + ".hmuc-template";
        }

        internal static string StatePath(string config)
        {
            return config + ".hmuc-template-state";
        }

        private static string LegacyTemplatePath(string config)
        {
            return config + ".hmuc-template.xml";
        }

        private static string LegacyStatePath(string config)
        {
            return config + ".hmuc-template-state.xml";
        }

        internal static string WarningPath(string config)
        {
            return config + ".hmuc-migration-warning";
        }

        internal static string GetConfigPath(string directory)
        {
            string name = Path.GetFileName(directory);
            return Path.Combine(directory,
                name == "Loupito75.HospitalAlwaysLit"
                ? name + ".Rooms.xml" : name + ".Config.xml");
        }

        internal static void RecoverSnapshotState(string config)
        {
            // Internal development builds used misleading .xml suffixes for
            // HMUC-owned template/state sidecars. Move those metadata files
            // to the current names; never touch the user's real Config.xml.
            MigrateLegacySidecar(
                LegacyTemplatePath(config),
                TemplatePath(config));

            RecoverStateFile(
                LegacyStatePath(config));

            MigrateLegacySidecar(
                LegacyStatePath(config),
                StatePath(config));

            // Interrupted atomic writes to HMUC-owned metadata are safe
            // to recover without changing the user's XML configuration.
            RecoverStateFile(
                StatePath(config));
        }

        private static void MigrateLegacySidecar(
            string legacyPath,
            string currentPath)
        {
            if (!File.Exists(legacyPath))
                return;

            if (!File.Exists(currentPath))
            {
                File.Move(
                    legacyPath,
                    currentPath);
                return;
            }

            File.Delete(
                legacyPath);
        }

        private static void RecoverStateFile(
            string path)
        {
            string previous =
                path + ".previous";
            string temporary =
                path + ".tmp";

            if (!File.Exists(path) &&
                File.Exists(previous))
            {
                File.Move(
                    previous,
                    path);
            }
            else if (File.Exists(path) &&
                     File.Exists(previous))
            {
                File.Delete(
                    previous);
            }

            if (File.Exists(temporary))
                File.Delete(temporary);
        }

        internal static bool HasSnapshot(string config)
        {
            return File.Exists(TemplatePath(config)) &&
                File.Exists(StatePath(config));
        }

        // Use only on the disposable installation candidate if the new
        // signed release contains no trustworthy config template.
        internal static void DropCandidateSnapshot(string config)
        {
            string[] paths = new string[]
            {
                TemplatePath(config),
                StatePath(config),
                LegacyTemplatePath(config),
                LegacyStatePath(config),
                LegacyStatePath(config) + ".previous",
                LegacyStatePath(config) + ".tmp",
                WarningPath(config)
            };
            for (int i = 0; i < paths.Length; i++)
                if (File.Exists(paths[i]))
                    File.Delete(paths[i]);
        }

        internal static void FlagWarning(string config)
        {
            if (HasSnapshot(config))
                WriteWarning(config);
        }

        private static XmlDocument LoadSafe(string path)
        {
            if (new FileInfo(path).Length > MaxXmlBytes)
                throw new FormatException("XML exceeds HMUC safety limit.");
            XmlDocument doc = new XmlDocument();
            doc.XmlResolver = null;
            doc.PreserveWhitespace = true;
            doc.Load(path);
            if (doc.DocumentType != null || doc.DocumentElement == null)
                throw new FormatException("XML DTD or empty document.");
            return doc;
        }

        internal static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder result = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    result.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static string ResolvePluginFile(string config, string relative)
        {
            if (string.IsNullOrEmpty(relative) ||
                Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
                return null;
            string[] sections = relative.Replace('\\', '/').Split('/');
            for (int i = 0; i < sections.Length; i++)
                if (sections[i].Length == 0 || sections[i] == "." ||
                    sections[i] == "..")
                    return null;
            string directory = Path.GetFullPath(Path.GetDirectoryName(config));
            string full = Path.GetFullPath(Path.Combine(
                directory, relative.Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar)));
            return full.StartsWith(directory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        internal static Snapshot Capture(string sourceTemplate,
            string config, string pluginFile, string modVersion)
        {
            LoadSafe(sourceTemplate);
            if (!File.Exists(config))
                throw new IOException("Installed XML was not found.");

            // Installation candidates may inherit HMUC sidecars from an
            // internal development build. Normalize those metadata names
            // before capturing the new signed template so the installed
            // candidate never carries both old and current sidecar names.
            RecoverSnapshotState(config);
            string pluginPath = ResolvePluginFile(config, pluginFile);
            if (pluginPath == null || !File.Exists(pluginPath))
                throw new IOException("Cannot identify installed mod DLL.");

            // Candidate directories are transactional; snapshots are not
            // installed until the enclosing package swap succeeds.
            string snapshotPath = TemplatePath(config);
            File.Copy(sourceTemplate, snapshotPath, true);
            LoadSafe(snapshotPath);
            Snapshot state = new Snapshot();
            state.ModVersion = modVersion ?? string.Empty;
            state.PluginFile = pluginFile;
            state.PluginHash = HashFile(pluginPath);
            state.TemplateHash = HashFile(snapshotPath);
            state.LastResult = string.Empty;
            SaveState(config, state);
            return state;
        }

        internal static bool TryReadState(string config,
            out Snapshot snapshot)
        {
            snapshot = null;

            try
            {
                RecoverSnapshotState(config);
            }
            catch
            {
                return false;
            }

            if (!HasSnapshot(config))
                return false;
            try
            {
                XmlDocument doc = LoadSafe(StatePath(config));
                XmlElement e = doc.DocumentElement;
                if (e.Name != "HMUCConfigTemplate" ||
                    e.GetAttribute("format") != "1")
                    return false;
                Snapshot item = new Snapshot();
                item.ModVersion = e.GetAttribute("modVersion");
                item.PluginFile = e.GetAttribute("pluginFile");
                item.PluginHash = e.GetAttribute("pluginHash");
                item.TemplateHash = e.GetAttribute("templateHash");
                item.LastConfigHash = e.GetAttribute("lastConfigHash");
                item.LastEngineVersion = e.GetAttribute("lastEngine");
                item.LastResult = e.GetAttribute("lastResult");
                long ticks;
                if (long.TryParse(e.GetAttribute("lastCheckUtcTicks"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out ticks) && ticks > 0)
                    item.LastCheckUtc = new DateTime(ticks, DateTimeKind.Utc);
                if (item.TemplateHash.Length != 64 ||
                    item.PluginHash.Length != 64)
                    return false;
                snapshot = item;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsTrustedSnapshot(string config,
            Snapshot state)
        {
            if (state == null)
                return false;
            string plugin = ResolvePluginFile(config, state.PluginFile);
            if (plugin == null || !File.Exists(plugin))
                return false;
            try
            {
                return string.Equals(HashFile(plugin), state.PluginHash,
                    StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(HashFile(TemplatePath(config)),
                        state.TemplateHash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static bool NeedsCheck(string config,
            Snapshot state, string currentHash)
        {
            if (state.LastCheckUtc == DateTime.MinValue ||
                state.LastCheckUtc > DateTime.UtcNow ||
                DateTime.UtcNow - state.LastCheckUtc >= CheckInterval)
                return true;
            return !string.Equals(state.LastConfigHash, currentHash,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.LastEngineVersion, EngineVersion,
                    StringComparison.Ordinal);
        }

        internal static void Stamp(string config, Snapshot state,
            string result)
        {
            state.LastConfigHash = HashFile(config);
            state.LastEngineVersion = EngineVersion;
            state.LastCheckUtc = DateTime.UtcNow;
            state.LastResult = result;
            SaveState(config, state);
            if (result == "valid")
                ClearWarning(config);
            else
                WriteWarning(config);
        }

        private static void SaveState(string config, Snapshot state)
        {
            XmlDocument doc = new XmlDocument();
            XmlElement e = doc.CreateElement("HMUCConfigTemplate");
            e.SetAttribute("format", "1");
            e.SetAttribute("modVersion", state.ModVersion ?? string.Empty);
            e.SetAttribute("pluginFile", state.PluginFile ?? string.Empty);
            e.SetAttribute("pluginHash", state.PluginHash ?? string.Empty);
            e.SetAttribute("templateHash", state.TemplateHash ?? string.Empty);
            e.SetAttribute("lastConfigHash", state.LastConfigHash ?? string.Empty);
            e.SetAttribute("lastEngine", state.LastEngineVersion ?? string.Empty);
            e.SetAttribute("lastResult", state.LastResult ?? string.Empty);
            e.SetAttribute("lastCheckUtcTicks",
                state.LastCheckUtc == DateTime.MinValue ? "0" :
                state.LastCheckUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            doc.AppendChild(e);
            string path = StatePath(config);
            string temporary = path + ".tmp";
            string previous = path + ".previous";
            if (File.Exists(temporary))
                File.Delete(temporary);
            using (XmlWriter writer = XmlWriter.Create(temporary,
                new XmlWriterSettings { Encoding = new UTF8Encoding(false),
                    Indent = false }))
                doc.Save(writer);
            if (File.Exists(previous))
                File.Delete(previous);
            if (File.Exists(path))
                File.Move(path, previous);
            try
            {
                File.Move(temporary, path);
                if (File.Exists(previous))
                    File.Delete(previous);
            }
            catch
            {
                if (!File.Exists(path) && File.Exists(previous))
                    File.Move(previous, path);
                throw;
            }
        }

        private static void WriteWarning(string config)
        {
            // A fixed marker, not user XML text, is displayed by the UI.
            File.WriteAllText(WarningPath(config), "schema", new UTF8Encoding(false));
        }

        internal static void ClearWarning(string config)
        {
            string warning = WarningPath(config);
            if (File.Exists(warning))
                File.Delete(warning);
        }

        internal static bool MatchesTemplate(string config,
            out string reason)
        {
            return MatchesDocuments(
                config, TemplatePath(config), out reason);
        }

        internal static bool MatchesDocuments(string config,
            string templateFile, out string reason)
        {
            reason = null;
            try
            {
                XmlDocument current = LoadSafe(config);
                XmlDocument template = LoadSafe(templateFile);
                return MatchElement(current.DocumentElement,
                    template.DocumentElement, out reason);
            }
            catch (Exception exception)
            {
                reason = "XML verification failed: " + exception.GetType().Name;
                return false;
            }
        }

        internal static bool MatchesDocument(XmlDocument document,
            string templatePath, out string reason)
        {
            reason = null;
            try
            {
                XmlDocument template = LoadSafe(templatePath);
                if (document == null || document.DocumentElement == null ||
                    document.DocumentType != null)
                {
                    reason = "Invalid XML editor document.";
                    return false;
                }
                return MatchElement(document.DocumentElement,
                    template.DocumentElement, out reason);
            }
            catch (Exception exception)
            {
                reason = "XML verification failed: " + exception.GetType().Name;
                return false;
            }
        }

        // Verify tree shape and scalar representation, NEVER user values.
        // Repeated collection members are allowed to have different counts.
        private static bool MatchElement(XmlElement actual,
            XmlElement expected, out string reason)
        {
            reason = null;
            if (actual.Name != expected.Name ||
                actual.NamespaceURI != expected.NamespaceURI)
            {
                reason = "XML section or root differs from the template.";
                return false;
            }

            Dictionary<string, List<XmlElement>> oldChildren = Children(actual);
            Dictionary<string, List<XmlElement>> newChildren = Children(expected);
            if (newChildren.Count == 0)
            {
                if (IsDeclaredEmptyValueList(expected))
                {
                    return MatchDeclaredEmptyValueList(
                        actual,
                        expected,
                        oldChildren,
                        out reason);
                }

                if (oldChildren.Count != 0)
                {
                    reason = "XML scalar/list format differs from template.";
                    return false;
                }
                if (!SameAttributeShape(actual, expected))
                {
                    reason = "XML setting attributes differ from template.";
                    return false;
                }
                return true;
            }

            if (oldChildren.Count == 0)
            {
                // Empty collections are legitimate when the template uses
                // repeated Value/ID elements; leave list handling to mod.
                if (newChildren.Count == 1 &&
                    (newChildren.ContainsKey("Value") ||
                     newChildren.ContainsKey("Id") ||
                     newChildren.ContainsKey("Tag")))
                    return true;
                reason = "Required XML section is missing.";
                return false;
            }

            if (!SameAttributeShape(actual, expected))
            {
                reason = "XML section attributes differ from template.";
                return false;
            }
            foreach (KeyValuePair<string, List<XmlElement>> pair in newChildren)
            {
                List<XmlElement> existing;
                if (!oldChildren.TryGetValue(pair.Key, out existing))
                {
                    reason = "Required XML setting/section is missing: " + pair.Key;
                    return false;
                }

                // Lists allow different lengths; other nodes must be unique.
                bool list = pair.Key == "Value" || pair.Key == "Id" ||
                    pair.Key == "Tag" || pair.Value.Count > 1;
                if (!list && (pair.Value.Count != 1 || existing.Count != 1))
                {
                    reason = "Duplicate XML setting/section: " + pair.Key;
                    return false;
                }
                for (int i = 0; i < existing.Count; i++)
                {
                    XmlElement reference = pair.Value[
                        Math.Min(i, pair.Value.Count - 1)];
                    if (!MatchElement(existing[i], reference, out reason))
                        return false;
                }
            }
            foreach (KeyValuePair<string, List<XmlElement>> pair in oldChildren)
            {
                if (!newChildren.ContainsKey(pair.Key))
                {
                    reason = "An old or unknown XML setting remains: " + pair.Key;
                    return false;
                }
            }
            return true;
        }

        private static bool IsDeclaredEmptyValueList(
            XmlElement expected)
        {
            if (expected == null)
                return false;

            for (XmlNode node = expected.PreviousSibling;
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

        private static bool MatchDeclaredEmptyValueList(
            XmlElement actual,
            XmlElement expected,
            Dictionary<string, List<XmlElement>> actualChildren,
            out string reason)
        {
            reason = null;

            if (!SameAttributeShape(actual, expected))
            {
                reason = "XML collection attributes differ from template.";
                return false;
            }

            foreach (
                KeyValuePair<string, List<XmlElement>> pair
                in actualChildren)
            {
                if (!string.Equals(
                        pair.Key,
                        "Value",
                        StringComparison.Ordinal))
                {
                    reason =
                        "XML collection entry differs from target Value schema.";
                    return false;
                }

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    XmlElement value =
                        pair.Value[i];

                    if (value == null ||
                        value.Attributes.Count != 0 ||
                        value.NamespaceURI != expected.NamespaceURI ||
                        Children(value).Count != 0)
                    {
                        reason =
                            "XML Value entry differs from target list schema.";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool SameAttributeShape(
            XmlElement actual, XmlElement expected)
        {
            if (actual.Attributes.Count != expected.Attributes.Count)
                return false;
            foreach (XmlAttribute attr in expected.Attributes)
                if (actual.Attributes[attr.Name] == null)
                    return false;
            return true;
        }

        private static Dictionary<string, List<XmlElement>> Children(
            XmlElement element)
        {
            Dictionary<string, List<XmlElement>> map =
                new Dictionary<string, List<XmlElement>>(
                    StringComparer.Ordinal);
            foreach (XmlNode node in element.ChildNodes)
            {
                XmlElement child = node as XmlElement;
                if (child == null)
                    continue;
                List<XmlElement> list;
                if (!map.TryGetValue(child.Name, out list))
                {
                    list = new List<XmlElement>();
                    map.Add(child.Name, list);
                }
                list.Add(child);
            }
            return map;
        }

        internal static IList<string> ReadWarningModNames(string pluginRoot)
        {
            List<string> names = new List<string>();
            if (!Directory.Exists(pluginRoot))
                return names;
            foreach (string directory in Directory.GetDirectories(pluginRoot))
            {
                if ((File.GetAttributes(directory) &
                    FileAttributes.ReparsePoint) != 0)
                    continue;
                string config = GetConfigPath(directory);
                if (HasSnapshot(config) && File.Exists(WarningPath(config)))
                    names.Add(Path.GetFileName(directory));
                if (names.Count >= 12)
                    break;
            }
            return names;
        }
    }
}

