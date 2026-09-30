using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;

namespace HospitalPorters
{
    internal static class PorterTransportConfig
    {
        private const string ConfigFileName =
            "Loupito75.HospitalPorters.Config.xml";

        private const int DefaultSampleFallbackRetries = 2;
        private const int DefaultSampleFallbackRetryMinutes = 5;
        private const int DefaultPatientFallbackRetries = 1;
        private const int DefaultPatientFallbackRetryMinutes = 2;
        private const int DefaultSampleTourMaxSamples = 5;
        private const int DefaultSampleTourSearchDistance = 20;
        private const bool DefaultEnableDiagnostics = false;

        internal static int SampleFallbackRetries { get; private set; }
        internal static int SampleFallbackRetryMinutes { get; private set; }
        internal static int PatientFallbackRetries { get; private set; }
        internal static int PatientFallbackRetryMinutes { get; private set; }
        internal static int SampleTourMaxSamples { get; private set; }
        internal static int SampleTourSearchDistance { get; private set; }
        internal static bool EnableDiagnostics { get; private set; }

        internal static void Load()
        {
            SetDefaults();

            string pluginDirectory = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);
            string configPath = Path.Combine(
                pluginDirectory ?? string.Empty,
                ConfigFileName);

            if (!File.Exists(configPath))
            {
                Plugin.Log?.LogWarning(
                    ConfigFileName +
                    " is missing; using built-in defaults.");
                LogCurrentValues("Built-in defaults");
                return;
            }

            try
            {
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
                    root.Name != "HospitalPorters" ||
                    root.Attributes.Count != 0)
                {
                    throw new FormatException(
                        "The XML root element must be <HospitalPorters>.");
                }

                Dictionary<string, XmlElement> settings =
                    new Dictionary<string, XmlElement>();

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
                        !IsKnownSetting(element.Name) ||
                        settings.ContainsKey(element.Name))
                    {
                        throw new FormatException(
                            "The Hospital Porters configuration contains an unknown or duplicate setting.");
                    }

                    settings.Add(element.Name, element);
                }

                SampleFallbackRetries = ReadInt(
                    settings,
                    "SampleFallbackRetries",
                    0,
                    10);
                SampleFallbackRetryMinutes = ReadInt(
                    settings,
                    "SampleFallbackRetryMinutes",
                    1,
                    60);
                PatientFallbackRetries = ReadInt(
                    settings,
                    "PatientFallbackRetries",
                    0,
                    10);
                PatientFallbackRetryMinutes = ReadInt(
                    settings,
                    "PatientFallbackRetryMinutes",
                    1,
                    60);
                SampleTourMaxSamples = ReadOptionalInt(
                    settings,
                    "SampleTourMaxSamples",
                    DefaultSampleTourMaxSamples,
                    1,
                    10);
                SampleTourSearchDistance = ReadOptionalInt(
                    settings,
                    "SampleTourSearchDistance",
                    DefaultSampleTourSearchDistance,
                    1,
                    200);
                EnableDiagnostics = ReadOptionalBool(
                    settings,
                    "EnableDiagnostics",
                    DefaultEnableDiagnostics);

                LogCurrentValues("Loaded configuration");
            }
            catch (Exception exception)
            {
                SetDefaults();
                Plugin.Log?.LogWarning(
                    "Could not load " +
                    ConfigFileName +
                    "; using defaults: " +
                    exception.GetType().Name +
                    ": " +
                    exception.Message);
                LogCurrentValues("Default settings");
            }
        }

        private static void SetDefaults()
        {
            SampleFallbackRetries =
                DefaultSampleFallbackRetries;
            SampleFallbackRetryMinutes =
                DefaultSampleFallbackRetryMinutes;
            PatientFallbackRetries =
                DefaultPatientFallbackRetries;
            PatientFallbackRetryMinutes =
                DefaultPatientFallbackRetryMinutes;
            SampleTourMaxSamples = DefaultSampleTourMaxSamples;
            SampleTourSearchDistance = DefaultSampleTourSearchDistance;
            EnableDiagnostics = DefaultEnableDiagnostics;
        }

        private static bool IsKnownSetting(
            string name)
        {
            return
                name == "SampleFallbackRetries" ||
                name == "SampleFallbackRetryMinutes" ||
                name == "PatientFallbackRetries" ||
                name == "PatientFallbackRetryMinutes" ||
                name == "SampleTourMaxSamples" ||
                name == "SampleTourSearchDistance" ||
                name == "EnableDiagnostics";
        }

        private static int ReadInt(
            Dictionary<string, XmlElement> settings,
            string name,
            int minimum,
            int maximum)
        {
            XmlElement element;
            if (!settings.TryGetValue(name, out element))
            {
                throw new FormatException(
                    "Missing <" + name + "> setting.");
            }

            for (int i = 0; i < element.ChildNodes.Count; i++)
            {
                XmlNode node = element.ChildNodes[i];
                if (node.NodeType != XmlNodeType.Text &&
                    node.NodeType != XmlNodeType.Whitespace &&
                    node.NodeType != XmlNodeType.SignificantWhitespace &&
                    node.NodeType != XmlNodeType.Comment)
                {
                    throw new FormatException(
                        name + " must contain only an integer value.");
                }
            }

            int value;
            if (!int.TryParse(
                element.InnerText.Trim(),
                out value) ||
                value < minimum ||
                value > maximum)
            {
                throw new FormatException(
                    name +
                    " must be an integer from " +
                    minimum +
                    " to " +
                    maximum +
                    ".");
            }

            return value;
        }

        private static int ReadOptionalInt(
            Dictionary<string, XmlElement> settings,
            string name,
            int defaultValue,
            int minimum,
            int maximum)
        {
            XmlElement element;
            if (!settings.TryGetValue(name, out element))
            {
                return defaultValue;
            }

            for (int i = 0; i < element.ChildNodes.Count; i++)
            {
                XmlNode node = element.ChildNodes[i];
                if (node.NodeType != XmlNodeType.Text &&
                    node.NodeType != XmlNodeType.Whitespace &&
                    node.NodeType != XmlNodeType.SignificantWhitespace &&
                    node.NodeType != XmlNodeType.Comment)
                {
                    throw new FormatException(name + " must contain only an integer value.");
                }
            }

            int value;
            if (!int.TryParse(element.InnerText.Trim(), out value) ||
                value < minimum || value > maximum)
            {
                throw new FormatException(
                    name + " must be an integer from " + minimum + " to " + maximum + ".");
            }
            return value;
        }

        private static bool ReadOptionalBool(
            Dictionary<string, XmlElement> settings,
            string name,
            bool defaultValue)
        {
            XmlElement element;
            if (!settings.TryGetValue(name, out element))
            {
                return defaultValue;
            }

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

            bool value;
            if (!bool.TryParse(element.InnerText.Trim(), out value))
            {
                throw new FormatException(
                    name + " must be true or false.");
            }

            return value;
        }

        private static void LogCurrentValues(
            string prefix)
        {
            PorterDiagnostics.Log(
                prefix +
                ": sampleRetries=" +
                SampleFallbackRetries +
                "; sampleRetryMinutes=" +
                SampleFallbackRetryMinutes +
                "; patientRetries=" +
                PatientFallbackRetries +
                "; patientRetryMinutes=" +
                PatientFallbackRetryMinutes +
                "; sampleTourMaxSamples=" +
                SampleTourMaxSamples +
                "; sampleTourSearchDistance=" +
                SampleTourSearchDistance +
                "; diagnostics=" +
                EnableDiagnostics +
                ".");
        }
    }
}
