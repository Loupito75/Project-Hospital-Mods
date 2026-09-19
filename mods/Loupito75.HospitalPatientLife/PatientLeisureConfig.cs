using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HospitalPatientLife
{
    internal static class PatientLeisureConfig
    {
        private const string ConfigFileName = "Loupito75.HospitalPatientLife.Config.xml";

        private const int DefaultRoomRestActivityWeight = 40;
        private const int DefaultRoomEducationActivityWeight = 30;
        private const int DefaultRoomVisualActivityWeight = 30;
        private const int DefaultRoomTelevisionActivityWeight = 25;
        private const int DefaultLyingRestActivityWeight = 50;
        private const int DefaultLyingNurseBookActivityWeight = 25;
        private const int DefaultLyingTelevisionActivityWeight = 25;
        private const bool DefaultAllowYellowLeisureObjectInteraction = true;

        private static bool s_loaded;

        internal static int RoomRestActivityWeight { get; private set; }
        internal static int RoomEducationActivityWeight { get; private set; }
        internal static int RoomVisualActivityWeight { get; private set; }
        internal static int RoomTelevisionActivityWeight { get; private set; }
        internal static int LyingRestActivityWeight { get; private set; }
        internal static int LyingNurseBookActivityWeight { get; private set; }
        internal static int LyingTelevisionActivityWeight { get; private set; }
        internal static bool AllowYellowLeisureObjectInteraction { get; private set; }

        internal static void EnsureLoaded()
        {
            if (s_loaded)
            {
                return;
            }

            s_loaded = true;
            UseDefaults();

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

                string xml = Regex.Replace(
                    File.ReadAllText(configPath),
                    "<!--.*?-->",
                    string.Empty,
                    RegexOptions.Singleline | RegexOptions.CultureInvariant);

                string hospitalizedPatients = GetSectionContent(
                    xml,
                    "HospitalizedPatients");
                string freeTime = GetSectionContent(
                    hospitalizedPatients,
                    "FreeTime");

                if (!string.IsNullOrEmpty(freeTime))
                {
                    RoomRestActivityWeight = ParseOptionalInt(
                        freeTime,
                        "RoomRestActivityWeight",
                        DefaultRoomRestActivityWeight,
                        0,
                        100);
                    RoomEducationActivityWeight = ParseOptionalInt(
                        freeTime,
                        "RoomEducationActivityWeight",
                        DefaultRoomEducationActivityWeight,
                        0,
                        100);
                    RoomVisualActivityWeight = ParseOptionalInt(
                        freeTime,
                        "RoomVisualActivityWeight",
                        DefaultRoomVisualActivityWeight,
                        0,
                        100);
                    RoomTelevisionActivityWeight = ParseOptionalInt(
                        freeTime,
                        "RoomTelevisionActivityWeight",
                        DefaultRoomTelevisionActivityWeight,
                        0,
                        100);

                    LyingRestActivityWeight = ParseOptionalInt(
                        freeTime,
                        "LyingRestActivityWeight",
                        DefaultLyingRestActivityWeight,
                        0,
                        100);
                    LyingNurseBookActivityWeight = ParseOptionalInt(
                        freeTime,
                        "LyingNurseBookActivityWeight",
                        DefaultLyingNurseBookActivityWeight,
                        0,
                        100);
                    LyingTelevisionActivityWeight = ParseOptionalInt(
                        freeTime,
                        "LyingTelevisionActivityWeight",
                        DefaultLyingTelevisionActivityWeight,
                        0,
                        100);
                }

                string patientLeisure = GetSectionContent(
                    xml,
                    "PatientLeisure");
                if (!string.IsNullOrEmpty(patientLeisure))
                {
                    AllowYellowLeisureObjectInteraction = ParseOptionalBool(
                        patientLeisure,
                        "AllowYellowLeisureObjectInteraction",
                        DefaultAllowYellowLeisureObjectInteraction);
                }

                if (HospitalPatientLifeConfig.DebugLogging && Plugin.Log != null)
                {
                    Plugin.Log.LogInfo(
                        "Hospital Patient Life patient-leisure configuration loaded: room weights rest/education/visual/television " +
                        RoomRestActivityWeight + "/" +
                        RoomEducationActivityWeight + "/" +
                        RoomVisualActivityWeight + "/" +
                        RoomTelevisionActivityWeight +
                        ", lying weights rest/nurse-book/television " +
                        LyingRestActivityWeight + "/" +
                        LyingNurseBookActivityWeight + "/" +
                        LyingTelevisionActivityWeight +
                        ", yellow leisure interaction " +
                        AllowYellowLeisureObjectInteraction + ".");
                }
            }
            catch (Exception exception)
            {
                UseDefaults();
                Plugin.Log.LogWarning(
                    "Could not load patient-leisure settings from " +
                    ConfigFileName + "; using defaults: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void UseDefaults()
        {
            RoomRestActivityWeight = DefaultRoomRestActivityWeight;
            RoomEducationActivityWeight = DefaultRoomEducationActivityWeight;
            RoomVisualActivityWeight = DefaultRoomVisualActivityWeight;
            RoomTelevisionActivityWeight =
                DefaultRoomTelevisionActivityWeight;
            LyingRestActivityWeight = DefaultLyingRestActivityWeight;
            LyingNurseBookActivityWeight =
                DefaultLyingNurseBookActivityWeight;
            LyingTelevisionActivityWeight =
                DefaultLyingTelevisionActivityWeight;
            AllowYellowLeisureObjectInteraction =
                DefaultAllowYellowLeisureObjectInteraction;
        }

        private static string GetSectionContent(
            string xml,
            string sectionName)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return null;
            }

            string escapedName = Regex.Escape(sectionName);
            Match match = Regex.Match(
                xml,
                @"<\s*" + escapedName +
                @"(?:\s[^>]*)?>\s*(?<value>.*?)\s*<\s*/\s*" +
                escapedName + @"\s*>",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline |
                RegexOptions.CultureInvariant);

            return match.Success ? match.Groups["value"].Value : null;
        }

        private static string GetElementValue(
            string content,
            string elementName)
        {
            string escapedName = Regex.Escape(elementName);
            Match match = Regex.Match(
                content ?? string.Empty,
                @"<\s*" + escapedName +
                @"\s*>\s*(?<value>.*?)\s*<\s*/\s*" +
                escapedName + @"\s*>",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline |
                RegexOptions.CultureInvariant);

            return match.Success ? match.Groups["value"].Value : null;
        }

        private static int ParseOptionalInt(
            string content,
            string elementName,
            int defaultValue,
            int minimum,
            int maximum)
        {
            string value = GetElementValue(content, elementName);
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            int parsed;
            if (!int.TryParse(value.Trim(), out parsed) ||
                parsed < minimum || parsed > maximum)
            {
                throw new FormatException(
                    elementName + " must be an integer from " +
                    minimum + " to " + maximum + ".");
            }

            return parsed;
        }

        private static bool ParseOptionalBool(
            string content,
            string elementName,
            bool defaultValue)
        {
            string value = GetElementValue(content, elementName);
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            bool parsed;
            if (!bool.TryParse(value.Trim(), out parsed))
            {
                throw new FormatException(
                    elementName + " must be true or false.");
            }

            return parsed;
        }
    }
}
