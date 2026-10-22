using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;

namespace HospitalModUpdateChecker
{
    internal static class UpdateCheckerConfig
    {
        private const string ConfigFileName =
            "Loupito75.HospitalModUpdateChecker.Config.xml";

        private const bool DefaultShowNews = true;
        private const bool DefaultDebug = false;
        private const bool DefaultShowTestModInRecommendations = false;

        internal static bool ShowNews { get; private set; }
        internal static bool Debug { get; private set; }
        internal static bool ShowTestModInRecommendations { get; private set; }

        internal static void Load()
        {
            ShowNews = DefaultShowNews;
            Debug = DefaultDebug;
            ShowTestModInRecommendations = DefaultShowTestModInRecommendations;

            try
            {
                string pluginDirectory = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                string configPath = Path.Combine(
                    pluginDirectory ?? string.Empty,
                    ConfigFileName);

                // Recover an interrupted one-time self-config swap before
                // deciding that the active configuration is missing.
                MigratePublic130ConfigIfNeeded(
                    configPath);

                if (!File.Exists(configPath))
                {
                    Plugin.Log.LogWarning(
                        "Could not find " + ConfigFileName +
                        "; using defaults.");
                    return;
                }

                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                document.Load(configPath);

                if (document.DocumentType != null)
                {
                    throw new FormatException(
                        "DTD declarations are not allowed.");
                }

                XmlElement root = document.DocumentElement;
                if (root == null ||
                    root.Name != "HospitalModUpdateChecker" ||
                    root.Attributes.Count != 0)
                {
                    throw new FormatException(
                        "The XML root element must be <HospitalModUpdateChecker>.");
                }

                Dictionary<string, XmlElement> settings =
                    new Dictionary<string, XmlElement>(
                        StringComparer.Ordinal);

                for (int i = 0; i < root.ChildNodes.Count; i++)
                {
                    XmlNode node = root.ChildNodes[i];

                    if (node.NodeType == XmlNodeType.Comment ||
                        node.NodeType == XmlNodeType.Whitespace ||
                        node.NodeType == XmlNodeType.SignificantWhitespace)
                    {
                        continue;
                    }

                    XmlElement element = node as XmlElement;
                    if (element == null ||
                        element.Attributes.Count != 0 ||
                        (element.Name != "ShowNews" &&
                         element.Name != "Debug" &&
                         element.Name != "ShowTestModInRecommendations") ||
                        settings.ContainsKey(element.Name))
                    {
                        throw new FormatException(
                            "Only one ShowNews, Debug and ShowTestModInRecommendations setting is allowed.");
                    }

                    settings.Add(element.Name, element);
                }

                XmlElement showNewsElement;
                settings.TryGetValue(
                    "ShowNews",
                    out showNewsElement);

                XmlElement debugElement;
                settings.TryGetValue(
                    "Debug",
                    out debugElement);

                ShowNews = showNewsElement == null
                    ? DefaultShowNews
                    : ParseBooleanSetting(
                        showNewsElement,
                        "ShowNews");

                Debug = debugElement == null
                    ? DefaultDebug
                    : ParseBooleanSetting(
                        debugElement,
                        "Debug");

                XmlElement showTestModElement;
                settings.TryGetValue(
                    "ShowTestModInRecommendations",
                    out showTestModElement);

                ShowTestModInRecommendations = showTestModElement == null
                    ? DefaultShowTestModInRecommendations
                    : ParseBooleanSetting(
                        showTestModElement,
                        "ShowTestModInRecommendations");
            }
            catch (Exception exception)
            {
                ShowNews = DefaultShowNews;
                Debug = DefaultDebug;
                ShowTestModInRecommendations = DefaultShowTestModInRecommendations;

                Plugin.Log.LogWarning(
                    "Could not load " + ConfigFileName +
                    "; using defaults: " +
                    exception.GetType().Name + ": " +
                    exception.Message);
            }
        }

        private static void MigratePublic130ConfigIfNeeded(
            string configPath)
        {
            string temporary =
                configPath + ".hmuc-self-new";

            string previous =
                configPath + ".hmuc-self-old";

            RecoverSelfMigration(
                configPath,
                temporary,
                previous);

            if (!File.Exists(configPath))
                return;

            XmlDocument document =
                new XmlDocument();

            document.XmlResolver = null;
            document.PreserveWhitespace = true;
            document.Load(configPath);

            if (document.DocumentType != null)
                return;

            XmlElement root =
                document.DocumentElement;

            if (root == null ||
                root.Name != "HospitalModUpdateChecker" ||
                root.Attributes.Count != 0)
            {
                return;
            }

            Dictionary<string, XmlElement> settings =
                new Dictionary<string, XmlElement>(
                    StringComparer.Ordinal);

            for (int i = 0;
                 i < root.ChildNodes.Count;
                 i++)
            {
                XmlNode node =
                    root.ChildNodes[i];

                if (node.NodeType == XmlNodeType.Comment ||
                    node.NodeType == XmlNodeType.Whitespace ||
                    node.NodeType == XmlNodeType.SignificantWhitespace)
                {
                    continue;
                }

                XmlElement element =
                    node as XmlElement;

                if (element == null ||
                    element.Attributes.Count != 0 ||
                    (element.Name != "CheckEveryLaunch" &&
                     element.Name != "ShowNews" &&
                     element.Name != "Debug" &&
                     element.Name != "ShowTestModInRecommendations") ||
                    settings.ContainsKey(element.Name))
                {
                    return;
                }

                settings.Add(
                    element.Name,
                    element);
            }

            // This bridge is intentionally limited to the exact public
            // 1.3.0 four-setting schema. It is a one-time migration for
            // HMUC's own self-update, not ongoing reader compatibility.
            if (settings.Count != 4 ||
                !settings.ContainsKey("CheckEveryLaunch") ||
                !settings.ContainsKey("ShowNews") ||
                !settings.ContainsKey("Debug") ||
                !settings.ContainsKey("ShowTestModInRecommendations"))
            {
                return;
            }

            ParseBooleanSetting(
                settings["CheckEveryLaunch"],
                "CheckEveryLaunch");

            ParseBooleanSetting(
                settings["ShowNews"],
                "ShowNews");

            ParseBooleanSetting(
                settings["Debug"],
                "Debug");

            ParseBooleanSetting(
                settings["ShowTestModInRecommendations"],
                "ShowTestModInRecommendations");

            XmlElement retired =
                settings["CheckEveryLaunch"];

            // CheckEveryLaunch is the first public 1.3.0 setting. Remove its
            // leading explanatory/HMUC comments and whitespace together so
            // no retired help text is left orphaned in the current schema.
            XmlNode nodeToRemove =
                retired.PreviousSibling;

            while (nodeToRemove != null &&
                nodeToRemove.NodeType != XmlNodeType.Element)
            {
                XmlNode preceding =
                    nodeToRemove.PreviousSibling;

                root.RemoveChild(
                    nodeToRemove);

                nodeToRemove =
                    preceding;
            }

            root.RemoveChild(
                retired);

            string backup =
                configPath + "." +
                DateTime.UtcNow.ToString(
                    "yyyyMMddTHHmmssfffffffZ",
                    System.Globalization.CultureInfo.InvariantCulture) +
                ".backup";

            File.Copy(
                configPath,
                backup,
                false);

            if (File.Exists(temporary))
                File.Delete(temporary);

            document.Save(
                temporary);

            // Prove the candidate is readable before touching the active XML.
            XmlDocument validation =
                new XmlDocument();

            validation.XmlResolver = null;
            validation.Load(
                temporary);

            if (validation.DocumentType != null)
            {
                File.Delete(
                    temporary);

                throw new FormatException(
                    "Migrated HMUC configuration contains a DTD.");
            }

            if (File.Exists(previous))
                File.Delete(previous);

            File.Move(
                configPath,
                previous);

            try
            {
                File.Move(
                    temporary,
                    configPath);

                // Prove the active file survived the swap before deleting
                // the recoverable previous copy.
                validation =
                    new XmlDocument();

                validation.XmlResolver = null;
                validation.Load(
                    configPath);

                // The swap is now valid. Cleanup failure must not roll back
                // a verified current configuration; recovery will retry stale
                // transient cleanup on the next launch.
                try
                {
                    if (File.Exists(previous))
                        File.Delete(previous);
                }
                catch (Exception cleanup)
                {
                    Plugin.Log.LogWarning(
                        "HMUC self-config migration succeeded but old-file cleanup is incomplete: " +
                        cleanup.GetType().Name);
                }

                Plugin.Log.LogInfo(
                    "Migrated the retired CheckEveryLaunch setting from the public HMUC 1.3.0 configuration.");
            }
            catch
            {
                if (File.Exists(configPath))
                    File.Delete(configPath);

                if (File.Exists(previous))
                {
                    File.Move(
                        previous,
                        configPath);
                }

                throw;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch (Exception cleanup)
                {
                    Plugin.Log.LogWarning(
                        "HMUC self-config migration temporary cleanup is incomplete: " +
                        cleanup.GetType().Name);
                }
            }
        }

        private static void RecoverSelfMigration(
            string configPath,
            string temporary,
            string previous)
        {
            if (!File.Exists(configPath) &&
                File.Exists(previous))
            {
                File.Move(
                    previous,
                    configPath);
            }
            else if (File.Exists(configPath) &&
                     File.Exists(previous))
            {
                if (IsSafeReadableXml(configPath))
                {
                    try
                    {
                        File.Delete(
                            previous);
                    }
                    catch (Exception cleanup)
                    {
                        Plugin.Log.LogWarning(
                            "HMUC self-config recovery kept a valid active XML but could not remove the previous copy: " +
                            cleanup.GetType().Name);
                    }
                }
                else
                {
                    File.Delete(
                        configPath);

                    File.Move(
                        previous,
                        configPath);
                }
            }

            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (Exception cleanup)
            {
                Plugin.Log.LogWarning(
                    "HMUC self-config recovery could not remove a stale temporary file: " +
                    cleanup.GetType().Name);
            }
        }

        private static bool IsSafeReadableXml(
            string path)
        {
            try
            {
                XmlDocument document =
                    new XmlDocument();

                document.XmlResolver = null;
                document.Load(path);

                return document.DocumentType == null &&
                    document.DocumentElement != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool ParseBooleanSetting(
            XmlElement element,
            string name)
        {
            for (int i = 0; i < element.ChildNodes.Count; i++)
            {
                XmlNode node = element.ChildNodes[i];

                if (node.NodeType != XmlNodeType.Text &&
                    node.NodeType != XmlNodeType.Whitespace &&
                    node.NodeType != XmlNodeType.SignificantWhitespace &&
                    node.NodeType != XmlNodeType.Comment)
                {
                    throw new FormatException(
                        name + " must contain only true or false.");
                }
            }

            bool parsed;
            if (!bool.TryParse(
                    element.InnerText.Trim(),
                    out parsed))
            {
                throw new FormatException(
                    name + " must be true or false.");
            }

            return parsed;
        }
    }
}
