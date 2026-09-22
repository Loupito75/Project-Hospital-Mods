using System;
using System.IO;
using System.Reflection;
using System.Xml;

namespace HospitalModUpdateChecker
{
    internal static class UpdateCheckerConfig
    {
        private const string ConfigFileName =
            "Loupito75.HospitalModUpdateChecker.Config.xml";
        private const bool DefaultCheckEveryLaunch = false;

        internal static bool CheckEveryLaunch { get; private set; }

        internal static void Load()
        {
            CheckEveryLaunch = DefaultCheckEveryLaunch;

            try
            {
                string pluginDirectory = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                string configPath = Path.Combine(
                    pluginDirectory ?? string.Empty,
                    ConfigFileName);

                if (!File.Exists(configPath))
                {
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

                XmlElement checkEveryLaunchElement = null;

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
                        element.Name != "CheckEveryLaunch" ||
                        element.Attributes.Count != 0 ||
                        checkEveryLaunchElement != null)
                    {
                        throw new FormatException(
                            "Only one <CheckEveryLaunch>true|false</CheckEveryLaunch> setting is allowed.");
                    }

                    checkEveryLaunchElement = element;
                }

                if (checkEveryLaunchElement == null)
                {
                    throw new FormatException(
                        "Missing <CheckEveryLaunch>true|false</CheckEveryLaunch>.");
                }

                for (int i = 0;
                    i < checkEveryLaunchElement.ChildNodes.Count;
                    i++)
                {
                    XmlNode node =
                        checkEveryLaunchElement.ChildNodes[i];

                    if (node.NodeType != XmlNodeType.Text &&
                        node.NodeType != XmlNodeType.Whitespace &&
                        node.NodeType != XmlNodeType.SignificantWhitespace &&
                        node.NodeType != XmlNodeType.Comment)
                    {
                        throw new FormatException(
                            "CheckEveryLaunch must contain only true or false.");
                    }
                }

                bool parsed;
                if (!bool.TryParse(
                    checkEveryLaunchElement.InnerText.Trim(),
                    out parsed))
                {
                    throw new FormatException(
                        "CheckEveryLaunch must be true or false.");
                }

                CheckEveryLaunch = parsed;
            }
            catch (Exception exception)
            {
                CheckEveryLaunch = DefaultCheckEveryLaunch;
                Plugin.Log.LogWarning(
                    "Could not load " + ConfigFileName +
                    "; using CheckEveryLaunch=false: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }
    }
}
