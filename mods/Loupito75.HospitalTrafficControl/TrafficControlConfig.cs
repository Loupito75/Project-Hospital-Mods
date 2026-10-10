using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HospitalTrafficControl
{
    internal static class TrafficControlConfig
    {
        private const string ConfigFileName = "Loupito75.HospitalTrafficControl.Config.xml";
        private const bool DefaultAvoidRoomShortcuts = false;
        private const float DefaultRoomTransitPenalty = 8f;
        private const bool DefaultAvoidCleaningActiveProcedureRooms = false;
        private const bool DefaultAvoidCleaningOccupiedBathrooms = false;
        private const int DefaultJanitorOccupiedRoomWaitChance = 30;
        private const int DefaultJanitorOccupiedRoomWaitCooldownMinutes = 30;
        private const int DefaultJanitorOccupiedRoomWaitMinutes = 15;
        private const int DefaultJanitorOccupiedRoomWaitRandomnessMinutes = 3;
        private const bool DefaultReduceOccupiedHospitalizationCleaningAtNight = false;
        private const bool DefaultReleaseToiletOwnerAfterUse = false;
        private const bool DefaultPathfindingDebug = false;
        private const bool DefaultJanitorCartDebug = false;
        private const bool DefaultGraphPerformanceDebug = false;
        private const bool DefaultDoorDebug = false;
        private const bool DefaultBathroomFlowDebug = false;

        private static readonly string[] DefaultTransitExceptionRoomTypeIds =
        {
            "ROOM_TYPE_CORRIDOR",
            "ROOM_TYPE_WAITING",
            "ROOM_TYPE_RECEPTION"
        };

        private static readonly Regex XmlCommentRegex = new Regex(
            "<!--.*?-->",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static readonly Regex ExceptionsOpenRegex = new Regex(
            @"<\s*Exceptions(?:\s[^>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex ExceptionsCloseRegex = new Regex(
            @"<\s*/\s*Exceptions\s*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RoomElementRegex = new Regex(
            @"<\s*Room\b(?<attributes>[^>]*)/?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static HashSet<string> _transitExceptionRoomTypeIds = CreateDefaultExceptionSet();

        internal static string ConfigPath { get; private set; }
        internal static bool AvoidRoomShortcuts { get; private set; }
        internal static float RoomTransitPenalty { get; private set; }
        internal static bool AvoidCleaningActiveProcedureRooms { get; private set; }
        internal static bool AvoidCleaningOccupiedBathrooms { get; private set; }
        internal static int JanitorOccupiedRoomWaitChance { get; private set; }
        internal static int JanitorOccupiedRoomWaitCooldownMinutes { get; private set; }
        internal static int JanitorOccupiedRoomWaitMinutes { get; private set; }
        internal static int JanitorOccupiedRoomWaitRandomnessMinutes { get; private set; }
        internal static bool ReduceOccupiedHospitalizationCleaningAtNight { get; private set; }
        internal static bool ReleaseToiletOwnerAfterUse { get; private set; }
        internal static bool PathfindingDebug { get; private set; }
        internal static bool JanitorCartDebug { get; private set; }
        internal static bool GraphPerformanceDebug { get; private set; }
        internal static bool DoorDebug { get; private set; }
        internal static bool BathroomFlowDebug { get; private set; }
        internal static int TransitExceptionCount => _transitExceptionRoomTypeIds.Count;

        internal static void Load()
        {
            UseDefaults();

            try
            {
                string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                ConfigPath = Path.Combine(pluginDirectory ?? string.Empty, ConfigFileName);

                if (!File.Exists(ConfigPath))
                {
                    Plugin.Log?.LogWarning(
                        ConfigFileName + " was not found. Built-in traffic-control defaults will be used.");
                    return;
                }

                string content = File.ReadAllText(ConfigPath);
                LoadFromText(content);
            }
            catch (Exception exception)
            {
                UseDefaults();
                Plugin.Log?.LogWarning(
                    "Could not load " + ConfigFileName +
                    "; using built-in defaults: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static bool IsTransitException(string roomTypeId)
        {
            return !string.IsNullOrEmpty(roomTypeId) &&
                   _transitExceptionRoomTypeIds.Contains(roomTypeId);
        }

        private static void LoadFromText(string content)
        {
            string xml = XmlCommentRegex.Replace(content ?? string.Empty, string.Empty);

            if (!ContainsRoot(xml, "HospitalTrafficControl"))
            {
                throw new FormatException(
                    "The XML root element must be <HospitalTrafficControl>...</HospitalTrafficControl>.");
            }

            string enabledText = GetElementValue(xml, "AvoidRoomShortcuts");
            if (!string.IsNullOrEmpty(enabledText))
            {
                bool parsedEnabled;
                if (!bool.TryParse(enabledText.Trim(), out parsedEnabled))
                {
                    throw new FormatException("AvoidRoomShortcuts must be true or false.");
                }

                AvoidRoomShortcuts = parsedEnabled;
            }

            string penaltyText = GetElementValue(xml, "RoomTransitPenalty");
            if (!string.IsNullOrEmpty(penaltyText))
            {
                float parsedPenalty;
                if (!float.TryParse(
                        penaltyText.Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out parsedPenalty) ||
                    parsedPenalty < 0f ||
                    parsedPenalty > 1000f)
                {
                    throw new FormatException(
                        "RoomTransitPenalty must be a number from 0 to 1000 using '.' as decimal separator.");
                }

                RoomTransitPenalty = parsedPenalty;
            }

            string janitorText = GetElementValue(xml, "AvoidCleaningActiveProcedureRooms");
            if (!string.IsNullOrEmpty(janitorText))
            {
                bool parsedJanitorSetting;
                if (!bool.TryParse(janitorText.Trim(), out parsedJanitorSetting))
                {
                    throw new FormatException(
                        "AvoidCleaningActiveProcedureRooms must be true or false.");
                }

                AvoidCleaningActiveProcedureRooms = parsedJanitorSetting;
            }

            string occupiedBathroomText = GetElementValue(xml, "AvoidCleaningOccupiedBathrooms");
            if (!string.IsNullOrEmpty(occupiedBathroomText))
            {
                bool parsedOccupiedBathroomSetting;
                if (!bool.TryParse(occupiedBathroomText.Trim(), out parsedOccupiedBathroomSetting))
                {
                    throw new FormatException(
                        "AvoidCleaningOccupiedBathrooms must be true or false.");
                }

                AvoidCleaningOccupiedBathrooms = parsedOccupiedBathroomSetting;
            }

            string janitorChanceText =
                GetElementValue(xml, "JanitorOccupiedRoomWaitChance");
            if (!string.IsNullOrEmpty(janitorChanceText))
            {
                int parsedJanitorChance;
                if (!int.TryParse(janitorChanceText.Trim(), out parsedJanitorChance) ||
                    parsedJanitorChance < 0 ||
                    parsedJanitorChance > 100)
                {
                    throw new FormatException(
                        "JanitorOccupiedRoomWaitChance must be an integer from 0 to 100.");
                }

                JanitorOccupiedRoomWaitChance = parsedJanitorChance;
            }

            string cooldownText = GetElementValue(
                xml, "JanitorOccupiedRoomWaitCooldownMinutes");
            if (!string.IsNullOrEmpty(cooldownText))
            {
                int cooldown;
                if (!int.TryParse(cooldownText.Trim(), out cooldown) ||
                    cooldown < 0 || cooldown > 240)
                {
                    throw new FormatException(
                        "JanitorOccupiedRoomWaitCooldownMinutes must be an integer from 0 to 240.");
                }
                JanitorOccupiedRoomWaitCooldownMinutes = cooldown;
            }

            string janitorWaitText = GetElementValue(xml, "JanitorOccupiedRoomWaitMinutes");
            if (!string.IsNullOrEmpty(janitorWaitText))
            {
                int parsedJanitorWait;
                if (!int.TryParse(janitorWaitText.Trim(), out parsedJanitorWait) ||
                    parsedJanitorWait < 0 ||
                    parsedJanitorWait > 240)
                {
                    throw new FormatException(
                        "JanitorOccupiedRoomWaitMinutes must be an integer from 0 to 240.");
                }

                JanitorOccupiedRoomWaitMinutes = parsedJanitorWait;
            }

            string janitorWaitRandomnessText =
                GetElementValue(xml, "JanitorOccupiedRoomWaitRandomnessMinutes");
            if (!string.IsNullOrEmpty(janitorWaitRandomnessText))
            {
                int parsedJanitorWaitRandomness;
                if (!int.TryParse(
                        janitorWaitRandomnessText.Trim(),
                        out parsedJanitorWaitRandomness) ||
                    parsedJanitorWaitRandomness < 0 ||
                    parsedJanitorWaitRandomness > 60)
                {
                    throw new FormatException(
                        "JanitorOccupiedRoomWaitRandomnessMinutes must be an integer from 0 to 60.");
                }

                JanitorOccupiedRoomWaitRandomnessMinutes =
                    parsedJanitorWaitRandomness;
            }

            string nightCleaningText = GetElementValue(xml, "ReduceOccupiedHospitalizationCleaningAtNight");
            if (!string.IsNullOrEmpty(nightCleaningText))
            {
                bool parsedNightCleaning;
                if (!bool.TryParse(nightCleaningText.Trim(), out parsedNightCleaning))
                {
                    throw new FormatException(
                        "ReduceOccupiedHospitalizationCleaningAtNight must be true or false.");
                }

                ReduceOccupiedHospitalizationCleaningAtNight = parsedNightCleaning;
            }

            string bathroomFlowText = GetElementValue(xml, "ReleaseToiletOwnerAfterUse");
            if (!string.IsNullOrEmpty(bathroomFlowText))
            {
                bool parsedBathroomFlow;
                if (!bool.TryParse(bathroomFlowText.Trim(), out parsedBathroomFlow))
                {
                    throw new FormatException("ReleaseToiletOwnerAfterUse must be true or false.");
                }

                ReleaseToiletOwnerAfterUse = parsedBathroomFlow;
            }

            string pathfindingDebugText = GetElementValue(xml, "PathfindingDebug");
            if (!string.IsNullOrEmpty(pathfindingDebugText))
            {
                bool parsedPathfindingDebug;
                if (!bool.TryParse(pathfindingDebugText.Trim(), out parsedPathfindingDebug))
                {
                    throw new FormatException("PathfindingDebug must be true or false.");
                }

                PathfindingDebug = parsedPathfindingDebug;
            }

            string janitorCartDebugText =
                GetElementValue(xml, "JanitorCartDebug");
            if (!string.IsNullOrEmpty(janitorCartDebugText))
            {
                bool parsedJanitorCartDebug;
                if (!bool.TryParse(
                        janitorCartDebugText.Trim(),
                        out parsedJanitorCartDebug))
                {
                    throw new FormatException(
                        "JanitorCartDebug must be true or false.");
                }

                JanitorCartDebug = parsedJanitorCartDebug;
            }

            string graphPerformanceDebugText =
                GetElementValue(xml, "GraphPerformanceDebug");
            if (!string.IsNullOrEmpty(graphPerformanceDebugText))
            {
                bool enabled;
                if (!bool.TryParse(graphPerformanceDebugText.Trim(), out enabled))
                {
                    throw new FormatException("GraphPerformanceDebug must be true or false.");
                }

                GraphPerformanceDebug = enabled;
            }

            string doorDebugText = GetElementValue(xml, "DoorDebug");
            if (!string.IsNullOrEmpty(doorDebugText))
            {
                bool parsedDoorDebug;
                if (!bool.TryParse(doorDebugText.Trim(), out parsedDoorDebug))
                {
                    throw new FormatException("DoorDebug must be true or false.");
                }

                DoorDebug = parsedDoorDebug;
            }

            string bathroomFlowDebugText = GetElementValue(xml, "BathroomFlowDebug");
            if (!string.IsNullOrEmpty(bathroomFlowDebugText))
            {
                bool parsedBathroomFlowDebug;
                if (!bool.TryParse(bathroomFlowDebugText.Trim(), out parsedBathroomFlowDebug))
                {
                    throw new FormatException("BathroomFlowDebug must be true or false.");
                }

                BathroomFlowDebug = parsedBathroomFlowDebug;
            }

            LoadTransitExceptions(xml);
        }

        private static void LoadTransitExceptions(string xml)
        {
            Match exceptionsOpenMatch = ExceptionsOpenRegex.Match(xml);
            Match exceptionsCloseMatch = ExceptionsCloseRegex.Match(xml);
            if (!exceptionsOpenMatch.Success ||
                !exceptionsCloseMatch.Success ||
                exceptionsCloseMatch.Index <= exceptionsOpenMatch.Index)
            {
                throw new FormatException(
                    "The XML must contain an <Exceptions>...</Exceptions> element under <RoomTransit>.");
            }

            string exceptionsContent = xml.Substring(
                exceptionsOpenMatch.Index + exceptionsOpenMatch.Length,
                exceptionsCloseMatch.Index - (exceptionsOpenMatch.Index + exceptionsOpenMatch.Length));

            MatchCollection roomMatches = RoomElementRegex.Matches(exceptionsContent);
            HashSet<string> exceptions = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> configuredIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match roomMatch in roomMatches)
            {
                string attributes = roomMatch.Groups["attributes"].Value;
                string roomTypeId = GetAttributeValue(attributes, "ID");
                string enabledValue = GetAttributeValue(attributes, "Enabled");

                roomTypeId = roomTypeId == null ? null : roomTypeId.Trim();
                enabledValue = enabledValue == null ? null : enabledValue.Trim();

                if (string.IsNullOrEmpty(roomTypeId))
                {
                    Plugin.Log?.LogWarning(
                        "Ignoring a transit-exception <Room> entry with a missing or empty ID attribute.");
                    continue;
                }

                bool enabled;
                if (!bool.TryParse(enabledValue, out enabled))
                {
                    Plugin.Log?.LogWarning(
                        "Ignoring transit exception " + roomTypeId + ": Enabled must be true or false.");
                    continue;
                }

                if (!configuredIds.Add(roomTypeId))
                {
                    Plugin.Log?.LogWarning(
                        "Duplicate transit-exception room ID " + roomTypeId +
                        " found in " + ConfigFileName + "; the last valid entry wins.");
                }

                if (enabled)
                {
                    exceptions.Add(roomTypeId);
                }
                else
                {
                    exceptions.Remove(roomTypeId);
                }
            }

            _transitExceptionRoomTypeIds = exceptions;
        }

        private static bool ContainsRoot(string xml, string rootName)
        {
            string escapedName = Regex.Escape(rootName);
            return Regex.IsMatch(
                       xml ?? string.Empty,
                       @"<\s*" + escapedName + @"(?:\s[^>]*)?>",
                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                   Regex.IsMatch(
                       xml ?? string.Empty,
                       @"<\s*/\s*" + escapedName + @"\s*>",
                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string GetElementValue(string content, string elementName)
        {
            string escapedName = Regex.Escape(elementName);
            Match match = Regex.Match(
                content ?? string.Empty,
                @"<\s*" + escapedName + @"\s*>\s*(?<value>.*?)\s*<\s*/\s*" + escapedName + @"\s*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

            return match.Success ? match.Groups["value"].Value : null;
        }

        private static string GetAttributeValue(string attributes, string attributeName)
        {
            string escapedName = Regex.Escape(attributeName);

            Match doubleQuoted = Regex.Match(
                attributes ?? string.Empty,
                "(?:^|\\s)" + escapedName + "\\s*=\\s*\"(?<value>[^\"]*)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (doubleQuoted.Success)
            {
                return doubleQuoted.Groups["value"].Value;
            }

            Match singleQuoted = Regex.Match(
                attributes ?? string.Empty,
                @"(?:^|\s)" + escapedName + @"\s*=\s*'(?<value>[^']*)'",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return singleQuoted.Success ? singleQuoted.Groups["value"].Value : null;
        }

        private static void UseDefaults()
        {
            AvoidRoomShortcuts = DefaultAvoidRoomShortcuts;
            RoomTransitPenalty = DefaultRoomTransitPenalty;
            AvoidCleaningActiveProcedureRooms = DefaultAvoidCleaningActiveProcedureRooms;
            AvoidCleaningOccupiedBathrooms = DefaultAvoidCleaningOccupiedBathrooms;
            JanitorOccupiedRoomWaitChance = DefaultJanitorOccupiedRoomWaitChance;
            JanitorOccupiedRoomWaitCooldownMinutes = DefaultJanitorOccupiedRoomWaitCooldownMinutes;
            JanitorOccupiedRoomWaitMinutes = DefaultJanitorOccupiedRoomWaitMinutes;
            JanitorOccupiedRoomWaitRandomnessMinutes =
                DefaultJanitorOccupiedRoomWaitRandomnessMinutes;
            ReduceOccupiedHospitalizationCleaningAtNight = DefaultReduceOccupiedHospitalizationCleaningAtNight;
            ReleaseToiletOwnerAfterUse = DefaultReleaseToiletOwnerAfterUse;
            PathfindingDebug = DefaultPathfindingDebug;
            JanitorCartDebug = DefaultJanitorCartDebug;
            GraphPerformanceDebug = DefaultGraphPerformanceDebug;
            DoorDebug = DefaultDoorDebug;
            BathroomFlowDebug = DefaultBathroomFlowDebug;
            _transitExceptionRoomTypeIds = CreateDefaultExceptionSet();
        }

        private static HashSet<string> CreateDefaultExceptionSet()
        {
            return new HashSet<string>(DefaultTransitExceptionRoomTypeIds, StringComparer.Ordinal);
        }
    }
}
