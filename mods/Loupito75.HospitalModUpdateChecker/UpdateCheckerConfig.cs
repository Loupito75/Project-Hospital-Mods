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

        private const bool DefaultCheckEveryLaunch = false;
        private const bool DefaultShowNews = true;
        private const bool DefaultDebug = false;

        internal static bool CheckEveryLaunch { get; private set; }
        internal static bool ShowNews { get; private set; }
        internal static bool Debug { get; private set; }

        internal static void Load()
        {
            CheckEveryLaunch = DefaultCheckEveryLaunch;
            ShowNews = DefaultShowNews;
            Debug = DefaultDebug;

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
                        (element.Name != "CheckEveryLaunch" &&
                         element.Name != "ShowNews" &&
                         element.Name != "Debug") ||
                        settings.ContainsKey(element.Name))
                    {
                        throw new FormatException(
                            "Only one CheckEveryLaunch, ShowNews and Debug setting is allowed.");
                    }

                    settings.Add(element.Name, element);
                }

                XmlElement checkEveryLaunchElement;
                if (!settings.TryGetValue(
                        "CheckEveryLaunch",
                        out checkEveryLaunchElement))
                {
                    throw new FormatException(
                        "Missing <CheckEveryLaunch>true|false</CheckEveryLaunch>.");
                }

                XmlElement showNewsElement;
                settings.TryGetValue(
                    "ShowNews",
                    out showNewsElement);

                XmlElement debugElement;
                settings.TryGetValue(
                    "Debug",
                    out debugElement);

                CheckEveryLaunch =
                    ParseBooleanSetting(
                        checkEveryLaunchElement,
                        "CheckEveryLaunch");

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
            }
            catch (Exception exception)
            {
                CheckEveryLaunch = DefaultCheckEveryLaunch;
                ShowNews = DefaultShowNews;
                Debug = DefaultDebug;

                Plugin.Log.LogWarning(
                    "Could not load " + ConfigFileName +
                    "; using defaults: " +
                    exception.GetType().Name + ": " +
                    exception.Message);
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
