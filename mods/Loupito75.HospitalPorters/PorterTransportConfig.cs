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
        private const int DefaultSampleFallbackRetryMinutes = 10;
        private const int DefaultSampleFallbackRetryRandomnessMinutes = 3;
        private const int DefaultPatientFallbackRetries = 1;
        private const int DefaultPatientFallbackRetryMinutes = 2;
        private const int DefaultSampleTourMaxSamples = 5;
        private const int DefaultSampleTourSearchDistance = 20;
        private const int DefaultSampleOccupiedRoomTimeoutMinutes = 40;
        private const int DefaultSampleOccupiedRoomTimeoutRandomnessMinutes = 5;
        private const int DefaultSampleOccupiedRoomPreAssignmentWaitChance = 30;
        private const int DefaultSampleOccupiedRoomRetryCooldownMinMinutes = 15;
        private const int DefaultSampleOccupiedRoomRetryCooldownMaxMinutes = 30;
        private const bool DefaultSampleCrossDepartmentHelp = true;
        private const int DefaultSampleCrossDepartmentHelpBaseChance = 30;
        private const int DefaultSampleCrossDepartmentStationFloorBonus = 20;
        private const int DefaultSampleCrossDepartmentCurrentFloorBonus = 20;
        private const int DefaultSampleCrossDepartmentWaitingBonus = 15;
        private const bool DefaultEnableDiagnostics = false;

        internal static int SampleFallbackRetries { get; private set; }
        internal static int SampleFallbackRetryMinutes { get; private set; }
        internal static int SampleFallbackRetryRandomnessMinutes { get; private set; }
        internal static int PatientFallbackRetries { get; private set; }
        internal static int PatientFallbackRetryMinutes { get; private set; }
        internal static int SampleTourMaxSamples { get; private set; }
        internal static int SampleTourSearchDistance { get; private set; }
        internal static int SampleOccupiedRoomTimeoutMinutes { get; private set; }
        internal static int SampleOccupiedRoomTimeoutRandomnessMinutes { get; private set; }
        internal static int SampleOccupiedRoomPreAssignmentWaitChance { get; private set; }
        internal static int SampleOccupiedRoomRetryCooldownMinMinutes { get; private set; }
        internal static int SampleOccupiedRoomRetryCooldownMaxMinutes { get; private set; }
        internal static bool SampleCrossDepartmentHelp { get; private set; }
        internal static int SampleCrossDepartmentHelpBaseChance { get; private set; }
        internal static int SampleCrossDepartmentStationFloorBonus { get; private set; }
        internal static int SampleCrossDepartmentCurrentFloorBonus { get; private set; }
        internal static int SampleCrossDepartmentWaitingBonus { get; private set; }
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
                SampleFallbackRetryRandomnessMinutes = ReadOptionalInt(
                    settings,
                    "SampleFallbackRetryRandomnessMinutes",
                    DefaultSampleFallbackRetryRandomnessMinutes,
                    0,
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
                SampleOccupiedRoomTimeoutMinutes = ReadOptionalInt(
                    settings,
                    "SampleOccupiedRoomTimeoutMinutes",
                    DefaultSampleOccupiedRoomTimeoutMinutes,
                    0,
                    240);
                SampleOccupiedRoomTimeoutRandomnessMinutes = ReadOptionalInt(
                    settings,
                    "SampleOccupiedRoomTimeoutRandomnessMinutes",
                    DefaultSampleOccupiedRoomTimeoutRandomnessMinutes,
                    0,
                    60);
                SampleOccupiedRoomPreAssignmentWaitChance = ReadOptionalInt(
                    settings,
                    "SampleOccupiedRoomPreAssignmentWaitChance",
                    DefaultSampleOccupiedRoomPreAssignmentWaitChance, 0, 100);
                SampleOccupiedRoomRetryCooldownMinMinutes = ReadOptionalInt(
                    settings,
                    "SampleOccupiedRoomRetryCooldownMinMinutes",
                    DefaultSampleOccupiedRoomRetryCooldownMinMinutes, 0, 240);
                SampleOccupiedRoomRetryCooldownMaxMinutes = ReadOptionalInt(
                    settings,
                    "SampleOccupiedRoomRetryCooldownMaxMinutes",
                    DefaultSampleOccupiedRoomRetryCooldownMaxMinutes, 0, 240);
                if (SampleOccupiedRoomRetryCooldownMaxMinutes <
                    SampleOccupiedRoomRetryCooldownMinMinutes)
                {
                    throw new FormatException(
                        "The occupied-room retry cooldown maximum must be >= its minimum.");
                }
                SampleCrossDepartmentHelp = ReadOptionalBool(
                    settings,
                    "SampleCrossDepartmentHelp",
                    DefaultSampleCrossDepartmentHelp);
                SampleCrossDepartmentHelpBaseChance = ReadOptionalInt(
                    settings,
                    "SampleCrossDepartmentHelpBaseChance",
                    DefaultSampleCrossDepartmentHelpBaseChance,
                    0,
                    100);
                SampleCrossDepartmentStationFloorBonus = ReadOptionalInt(
                    settings,
                    "SampleCrossDepartmentStationFloorBonus",
                    DefaultSampleCrossDepartmentStationFloorBonus,
                    0,
                    50);
                SampleCrossDepartmentCurrentFloorBonus = ReadOptionalInt(
                    settings,
                    "SampleCrossDepartmentCurrentFloorBonus",
                    DefaultSampleCrossDepartmentCurrentFloorBonus,
                    0,
                    50);
                SampleCrossDepartmentWaitingBonus = ReadOptionalInt(
                    settings,
                    "SampleCrossDepartmentWaitingBonus",
                    DefaultSampleCrossDepartmentWaitingBonus,
                    0,
                    50);
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
            SampleFallbackRetryRandomnessMinutes =
                DefaultSampleFallbackRetryRandomnessMinutes;
            PatientFallbackRetries =
                DefaultPatientFallbackRetries;
            PatientFallbackRetryMinutes =
                DefaultPatientFallbackRetryMinutes;
            SampleTourMaxSamples = DefaultSampleTourMaxSamples;
            SampleTourSearchDistance = DefaultSampleTourSearchDistance;
            SampleOccupiedRoomTimeoutMinutes =
                DefaultSampleOccupiedRoomTimeoutMinutes;
            SampleOccupiedRoomTimeoutRandomnessMinutes =
                DefaultSampleOccupiedRoomTimeoutRandomnessMinutes;
            SampleOccupiedRoomPreAssignmentWaitChance =
                DefaultSampleOccupiedRoomPreAssignmentWaitChance;
            SampleOccupiedRoomRetryCooldownMinMinutes =
                DefaultSampleOccupiedRoomRetryCooldownMinMinutes;
            SampleOccupiedRoomRetryCooldownMaxMinutes =
                DefaultSampleOccupiedRoomRetryCooldownMaxMinutes;
            SampleCrossDepartmentHelp =
                DefaultSampleCrossDepartmentHelp;
            SampleCrossDepartmentHelpBaseChance =
                DefaultSampleCrossDepartmentHelpBaseChance;
            SampleCrossDepartmentStationFloorBonus =
                DefaultSampleCrossDepartmentStationFloorBonus;
            SampleCrossDepartmentCurrentFloorBonus =
                DefaultSampleCrossDepartmentCurrentFloorBonus;
            SampleCrossDepartmentWaitingBonus =
                DefaultSampleCrossDepartmentWaitingBonus;
            EnableDiagnostics = DefaultEnableDiagnostics;
        }

        private static bool IsKnownSetting(
            string name)
        {
            return
                name == "SampleFallbackRetries" ||
                name == "SampleFallbackRetryMinutes" ||
                name == "SampleFallbackRetryRandomnessMinutes" ||
                name == "PatientFallbackRetries" ||
                name == "PatientFallbackRetryMinutes" ||
                name == "SampleTourMaxSamples" ||
                name == "SampleTourSearchDistance" ||
                name == "SampleOccupiedRoomTimeoutMinutes" ||
                name == "SampleOccupiedRoomTimeoutRandomnessMinutes" ||
                name == "SampleOccupiedRoomPreAssignmentWaitChance" ||
                name == "SampleOccupiedRoomRetryCooldownMinMinutes" ||
                name == "SampleOccupiedRoomRetryCooldownMaxMinutes" ||
                name == "SampleCrossDepartmentHelp" ||
                name == "SampleCrossDepartmentHelpBaseChance" ||
                name == "SampleCrossDepartmentStationFloorBonus" ||
                name == "SampleCrossDepartmentCurrentFloorBonus" ||
                name == "SampleCrossDepartmentWaitingBonus" ||
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
                "; sampleRetryRandomnessMinutes=" +
                SampleFallbackRetryRandomnessMinutes +
                "; patientRetries=" +
                PatientFallbackRetries +
                "; patientRetryMinutes=" +
                PatientFallbackRetryMinutes +
                "; sampleTourMaxSamples=" +
                SampleTourMaxSamples +
                "; sampleTourSearchDistance=" +
                SampleTourSearchDistance +
                "; sampleOccupiedRoomTimeoutMinutes=" +
                SampleOccupiedRoomTimeoutMinutes +
                "; sampleOccupiedRoomTimeoutRandomnessMinutes=" +
                SampleOccupiedRoomTimeoutRandomnessMinutes +
                "; samplePreBlockedWaitChance=" +
                SampleOccupiedRoomPreAssignmentWaitChance +
                "; sampleRoomRetryCooldownMinutes=" +
                SampleOccupiedRoomRetryCooldownMinMinutes + "-" +
                SampleOccupiedRoomRetryCooldownMaxMinutes +
                "; sampleCrossDepartmentHelp=" +
                SampleCrossDepartmentHelp +
                "; sampleCrossDepartmentBaseChance=" +
                SampleCrossDepartmentHelpBaseChance +
                "; sampleCrossDepartmentStationFloorBonus=" +
                SampleCrossDepartmentStationFloorBonus +
                "; sampleCrossDepartmentCurrentFloorBonus=" +
                SampleCrossDepartmentCurrentFloorBonus +
                "; sampleCrossDepartmentWaitingBonus=" +
                SampleCrossDepartmentWaitingBonus +
                "; diagnostics=" +
                EnableDiagnostics +
                ".");
        }
    }
}
