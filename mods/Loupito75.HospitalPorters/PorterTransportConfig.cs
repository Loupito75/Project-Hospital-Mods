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
        private const bool DefaultPatientCrossDepartmentHelp = true;
        private const int DefaultPatientCrossDepartmentHelpBaseChance = 70;
        private const int DefaultPatientCrossDepartmentStationFloorBonus = 10;
        private const int DefaultPatientCrossDepartmentCurrentFloorBonus = 20;
        private const int DefaultPatientCrossDepartmentWaitingBonus = 20;
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
        internal static bool PatientCrossDepartmentHelp { get; private set; }
        internal static int PatientCrossDepartmentHelpBaseChance { get; private set; }
        internal static int PatientCrossDepartmentStationFloorBonus { get; private set; }
        internal static int PatientCrossDepartmentCurrentFloorBonus { get; private set; }
        internal static int PatientCrossDepartmentWaitingBonus { get; private set; }
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

                ValidateChildren(
                    root,
                    "PatientTransport",
                    "SampleTransport",
                    "Diagnostics");

                XmlElement patientTransport =
                    GetRequiredSection(root, "PatientTransport");
                XmlElement patientFallback =
                    GetRequiredSection(patientTransport, "Fallback");
                XmlElement patientCrossDepartmentHelp =
                    GetRequiredSection(
                        patientTransport,
                        "CrossDepartmentHelp");

                ValidateChildren(
                    patientTransport,
                    "Fallback",
                    "CrossDepartmentHelp");
                ValidateChildren(
                    patientFallback,
                    "PatientFallbackRetries",
                    "PatientFallbackRetryMinutes");
                ValidateChildren(
                    patientCrossDepartmentHelp,
                    "PatientCrossDepartmentHelp",
                    "PatientCrossDepartmentHelpBaseChance",
                    "PatientCrossDepartmentStationFloorBonus",
                    "PatientCrossDepartmentCurrentFloorBonus",
                    "PatientCrossDepartmentWaitingBonus");

                PatientFallbackRetries = ReadInt(
                    patientFallback,
                    "PatientFallbackRetries",
                    0,
                    10);
                PatientFallbackRetryMinutes = ReadInt(
                    patientFallback,
                    "PatientFallbackRetryMinutes",
                    1,
                    60);
                PatientCrossDepartmentHelp = ReadBool(
                    patientCrossDepartmentHelp,
                    "PatientCrossDepartmentHelp");
                PatientCrossDepartmentHelpBaseChance = ReadInt(
                    patientCrossDepartmentHelp,
                    "PatientCrossDepartmentHelpBaseChance",
                    0,
                    100);
                PatientCrossDepartmentStationFloorBonus = ReadInt(
                    patientCrossDepartmentHelp,
                    "PatientCrossDepartmentStationFloorBonus",
                    0,
                    50);
                PatientCrossDepartmentCurrentFloorBonus = ReadInt(
                    patientCrossDepartmentHelp,
                    "PatientCrossDepartmentCurrentFloorBonus",
                    0,
                    50);
                PatientCrossDepartmentWaitingBonus = ReadInt(
                    patientCrossDepartmentHelp,
                    "PatientCrossDepartmentWaitingBonus",
                    0,
                    50);

                XmlElement sampleTransport =
                    GetRequiredSection(root, "SampleTransport");
                XmlElement sampleFallback =
                    GetRequiredSection(sampleTransport, "Fallback");
                XmlElement sampleTours =
                    GetRequiredSection(sampleTransport, "Tours");
                XmlElement sampleOccupiedRooms =
                    GetRequiredSection(
                        sampleTransport,
                        "OccupiedRooms");
                XmlElement sampleCrossDepartmentHelp =
                    GetRequiredSection(
                        sampleTransport,
                        "CrossDepartmentHelp");

                ValidateChildren(
                    sampleTransport,
                    "Fallback",
                    "Tours",
                    "OccupiedRooms",
                    "CrossDepartmentHelp");
                ValidateChildren(
                    sampleFallback,
                    "SampleFallbackRetries",
                    "SampleFallbackRetryMinutes",
                    "SampleFallbackRetryRandomnessMinutes");
                ValidateChildren(
                    sampleTours,
                    "SampleTourMaxSamples",
                    "SampleTourSearchDistance");
                ValidateChildren(
                    sampleOccupiedRooms,
                    "SampleOccupiedRoomTimeoutMinutes",
                    "SampleOccupiedRoomTimeoutRandomnessMinutes",
                    "SampleOccupiedRoomPreAssignmentWaitChance",
                    "SampleOccupiedRoomRetryCooldownMinMinutes",
                    "SampleOccupiedRoomRetryCooldownMaxMinutes");
                ValidateChildren(
                    sampleCrossDepartmentHelp,
                    "SampleCrossDepartmentHelp",
                    "SampleCrossDepartmentHelpBaseChance",
                    "SampleCrossDepartmentStationFloorBonus",
                    "SampleCrossDepartmentCurrentFloorBonus",
                    "SampleCrossDepartmentWaitingBonus");

                SampleFallbackRetries = ReadInt(
                    sampleFallback,
                    "SampleFallbackRetries",
                    0,
                    10);
                SampleFallbackRetryMinutes = ReadInt(
                    sampleFallback,
                    "SampleFallbackRetryMinutes",
                    1,
                    60);
                SampleFallbackRetryRandomnessMinutes = ReadInt(
                    sampleFallback,
                    "SampleFallbackRetryRandomnessMinutes",
                    0,
                    60);
                SampleTourMaxSamples = ReadInt(
                    sampleTours,
                    "SampleTourMaxSamples",
                    1,
                    10);
                SampleTourSearchDistance = ReadInt(
                    sampleTours,
                    "SampleTourSearchDistance",
                    1,
                    200);
                SampleOccupiedRoomTimeoutMinutes = ReadInt(
                    sampleOccupiedRooms,
                    "SampleOccupiedRoomTimeoutMinutes",
                    0,
                    240);
                SampleOccupiedRoomTimeoutRandomnessMinutes = ReadInt(
                    sampleOccupiedRooms,
                    "SampleOccupiedRoomTimeoutRandomnessMinutes",
                    0,
                    60);
                SampleOccupiedRoomPreAssignmentWaitChance = ReadInt(
                    sampleOccupiedRooms,
                    "SampleOccupiedRoomPreAssignmentWaitChance",
                    0,
                    100);
                SampleOccupiedRoomRetryCooldownMinMinutes = ReadInt(
                    sampleOccupiedRooms,
                    "SampleOccupiedRoomRetryCooldownMinMinutes",
                    0,
                    240);
                SampleOccupiedRoomRetryCooldownMaxMinutes = ReadInt(
                    sampleOccupiedRooms,
                    "SampleOccupiedRoomRetryCooldownMaxMinutes",
                    0,
                    240);
                if (SampleOccupiedRoomRetryCooldownMaxMinutes <
                    SampleOccupiedRoomRetryCooldownMinMinutes)
                {
                    throw new FormatException(
                        "The occupied-room retry cooldown maximum must be >= its minimum.");
                }

                SampleCrossDepartmentHelp = ReadBool(
                    sampleCrossDepartmentHelp,
                    "SampleCrossDepartmentHelp");
                SampleCrossDepartmentHelpBaseChance = ReadInt(
                    sampleCrossDepartmentHelp,
                    "SampleCrossDepartmentHelpBaseChance",
                    0,
                    100);
                SampleCrossDepartmentStationFloorBonus = ReadInt(
                    sampleCrossDepartmentHelp,
                    "SampleCrossDepartmentStationFloorBonus",
                    0,
                    50);
                SampleCrossDepartmentCurrentFloorBonus = ReadInt(
                    sampleCrossDepartmentHelp,
                    "SampleCrossDepartmentCurrentFloorBonus",
                    0,
                    50);
                SampleCrossDepartmentWaitingBonus = ReadInt(
                    sampleCrossDepartmentHelp,
                    "SampleCrossDepartmentWaitingBonus",
                    0,
                    50);

                XmlElement diagnostics =
                    GetRequiredSection(root, "Diagnostics");
                ValidateChildren(
                    diagnostics,
                    "EnableDiagnostics");
                EnableDiagnostics = ReadBool(
                    diagnostics,
                    "EnableDiagnostics");

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
            PatientCrossDepartmentHelp =
                DefaultPatientCrossDepartmentHelp;
            PatientCrossDepartmentHelpBaseChance =
                DefaultPatientCrossDepartmentHelpBaseChance;
            PatientCrossDepartmentStationFloorBonus =
                DefaultPatientCrossDepartmentStationFloorBonus;
            PatientCrossDepartmentCurrentFloorBonus =
                DefaultPatientCrossDepartmentCurrentFloorBonus;
            PatientCrossDepartmentWaitingBonus =
                DefaultPatientCrossDepartmentWaitingBonus;
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

        private static XmlElement GetRequiredSection(
            XmlElement parent,
            string name)
        {
            XmlElement result = null;
            for (int i = 0; i < parent.ChildNodes.Count; i++)
            {
                XmlElement element =
                    parent.ChildNodes[i] as XmlElement;
                if (element == null ||
                    element.Name != name)
                {
                    continue;
                }

                if (result != null)
                {
                    throw new FormatException(
                        "Duplicate <" + name + "> section.");
                }

                if (element.Attributes.Count != 0)
                {
                    throw new FormatException(
                        "<" + name + "> must not have attributes.");
                }

                result = element;
            }

            if (result == null)
            {
                throw new FormatException(
                    "Missing <" + name + "> section.");
            }

            return result;
        }

        private static void ValidateChildren(
            XmlElement parent,
            params string[] allowedNames)
        {
            Dictionary<string, bool> allowed =
                new Dictionary<string, bool>(
                    StringComparer.Ordinal);
            for (int i = 0; i < allowedNames.Length; i++)
            {
                allowed.Add(allowedNames[i], false);
            }

            for (int i = 0; i < parent.ChildNodes.Count; i++)
            {
                XmlNode node = parent.ChildNodes[i];
                if (node.NodeType == XmlNodeType.Comment ||
                    node.NodeType == XmlNodeType.Whitespace ||
                    node.NodeType == XmlNodeType.SignificantWhitespace)
                {
                    continue;
                }

                XmlElement element = node as XmlElement;
                if (element == null ||
                    element.Attributes.Count != 0 ||
                    !allowed.ContainsKey(element.Name) ||
                    allowed[element.Name])
                {
                    throw new FormatException(
                        "The <" + parent.Name +
                        "> section contains an unknown or duplicate element.");
                }

                allowed[element.Name] = true;
            }

            foreach (KeyValuePair<string, bool> entry in allowed)
            {
                if (!entry.Value)
                {
                    throw new FormatException(
                        "Missing <" + entry.Key +
                        "> in <" + parent.Name + ">.");
                }
            }
        }

        private static XmlElement GetRequiredValue(
            XmlElement parent,
            string name)
        {
            XmlElement element = GetRequiredSection(parent, name);

            for (int i = 0; i < element.ChildNodes.Count; i++)
            {
                XmlNode node = element.ChildNodes[i];
                if (node.NodeType != XmlNodeType.Text &&
                    node.NodeType != XmlNodeType.Whitespace &&
                    node.NodeType != XmlNodeType.SignificantWhitespace &&
                    node.NodeType != XmlNodeType.Comment)
                {
                    throw new FormatException(
                        name + " must contain only a scalar value.");
                }
            }

            return element;
        }

        private static int ReadInt(
            XmlElement parent,
            string name,
            int minimum,
            int maximum)
        {
            XmlElement element =
                GetRequiredValue(parent, name);

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

        private static bool ReadBool(
            XmlElement parent,
            string name)
        {
            XmlElement element =
                GetRequiredValue(parent, name);

            bool value;
            if (!bool.TryParse(
                    element.InnerText.Trim(),
                    out value))
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
                "; patientCrossDepartmentHelp=" +
                PatientCrossDepartmentHelp +
                "; patientCrossDepartmentBaseChance=" +
                PatientCrossDepartmentHelpBaseChance +
                "; patientCrossDepartmentStationFloorBonus=" +
                PatientCrossDepartmentStationFloorBonus +
                "; patientCrossDepartmentCurrentFloorBonus=" +
                PatientCrossDepartmentCurrentFloorBonus +
                "; patientCrossDepartmentWaitingBonus=" +
                PatientCrossDepartmentWaitingBonus +
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
