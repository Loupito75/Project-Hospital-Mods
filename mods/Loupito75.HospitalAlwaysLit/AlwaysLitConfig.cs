using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HospitalAlwaysLit
{
    internal static class AlwaysLitConfig
    {
        private const string ConfigFileName = "Loupito75.HospitalAlwaysLit.Rooms.xml";

        private static readonly string[] DefaultRoomTypeIds =
        {
            "ROOM_TYPE_CORRIDOR",
            "ROOM_TYPE_RECEPTION",
            "ROOM_TYPE_WAITING",
            "ROOM_TYPE_WC"
        };

        private static readonly Regex XmlCommentRegex = new Regex(
            "<!--.*?-->",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static readonly Regex RootOpenRegex = new Regex(
            @"<\s*HospitalAlwaysLit(?:\s[^>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RootCloseRegex = new Regex(
            @"<\s*/\s*HospitalAlwaysLit\s*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RoomsOpenRegex = new Regex(
            @"<\s*Rooms(?:\s[^>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RoomsCloseRegex = new Regex(
            @"<\s*/\s*Rooms\s*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RoomElementRegex = new Regex(
            @"<\s*Room\b(?<attributes>[^>]*)/?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex KeepPatientRoomsDarkAtNightElementRegex = new Regex(
            @"<\s*KeepPatientRoomsDarkAtNight\b(?<attributes>[^>]*)/?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex LegacyNightPatientRoomLightingElementRegex = new Regex(
            @"<\s*NightPatientRoomLighting\b(?<attributes>[^>]*)/?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static HashSet<string> _enabledRoomTypeIds = CreateDefaultSet();

        internal static bool KeepPatientRoomsDarkAtNightEnabled { get; private set; }

        internal static void Load()
        {
            UseDefaults();

            try
            {
                string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string configPath = Path.Combine(pluginDirectory ?? string.Empty, ConfigFileName);

                if (!File.Exists(configPath))
                {
                    Plugin.Log?.LogWarning($"{ConfigFileName} was not found. Using the built-in default room list.");
                    return;
                }

                string content = File.ReadAllText(configPath);
                LoadFromText(content);
            }
            catch (Exception exception)
            {
                UseDefaults();
                Plugin.Log?.LogError($"Could not load {ConfigFileName}: {exception.GetType().Name}: {exception.Message}");
                Plugin.Log?.LogWarning("Using the built-in default room list instead.");
            }
        }

        internal static bool IsEnabled(string roomTypeId)
        {
            return !string.IsNullOrEmpty(roomTypeId) && _enabledRoomTypeIds.Contains(roomTypeId);
        }

        private static void LoadFromText(string content)
        {
            string xml = XmlCommentRegex.Replace(content ?? string.Empty, string.Empty);

            if (!RootOpenRegex.IsMatch(xml) || !RootCloseRegex.IsMatch(xml))
            {
                throw new FormatException("The XML root element must be <HospitalAlwaysLit>...</HospitalAlwaysLit>.");
            }

            Match roomsOpenMatch = RoomsOpenRegex.Match(xml);
            Match roomsCloseMatch = RoomsCloseRegex.Match(xml);
            if (!roomsOpenMatch.Success || !roomsCloseMatch.Success || roomsCloseMatch.Index <= roomsOpenMatch.Index)
            {
                throw new FormatException("The XML must contain a <Rooms>...</Rooms> element under <HospitalAlwaysLit>.");
            }

            string roomsContent = xml.Substring(
                roomsOpenMatch.Index + roomsOpenMatch.Length,
                roomsCloseMatch.Index - (roomsOpenMatch.Index + roomsOpenMatch.Length));

            MatchCollection roomMatches = RoomElementRegex.Matches(roomsContent);
            HashSet<string> enabledRoomTypeIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> configuredRoomTypeIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match roomMatch in roomMatches)
            {
                string attributes = roomMatch.Groups["attributes"].Value;
                string roomTypeId = GetAttributeValue(attributes, "ID")?.Trim();
                string enabledValue = GetAttributeValue(attributes, "Enabled")?.Trim();

                if (string.IsNullOrEmpty(roomTypeId))
                {
                    Plugin.Log?.LogWarning("Ignoring a <Room> entry with a missing or empty ID attribute.");
                    continue;
                }

                if (!bool.TryParse(enabledValue, out bool enabled))
                {
                    Plugin.Log?.LogWarning($"Ignoring {roomTypeId}: Enabled must be true or false.");
                    continue;
                }

                if (!configuredRoomTypeIds.Add(roomTypeId))
                {
                    Plugin.Log?.LogWarning($"Duplicate room ID {roomTypeId} found in {ConfigFileName}; the last valid entry wins.");
                }

                if (enabled)
                {
                    enabledRoomTypeIds.Add(roomTypeId);
                }
                else
                {
                    enabledRoomTypeIds.Remove(roomTypeId);
                }
            }

            _enabledRoomTypeIds = enabledRoomTypeIds;
            LoadKeepPatientRoomsDarkAtNight(xml);
        }

        private static void LoadKeepPatientRoomsDarkAtNight(string xml)
        {
            string source = xml ?? string.Empty;
            MatchCollection matches =
                KeepPatientRoomsDarkAtNightElementRegex.Matches(source);
            bool legacySetting = false;

            if (matches.Count == 0)
            {
                matches = LegacyNightPatientRoomLightingElementRegex.Matches(source);
                legacySetting = matches.Count > 0;

                if (legacySetting)
                {
                    Plugin.Log?.LogWarning(
                        "Legacy <NightPatientRoomLighting> setting detected. It is treated as <KeepPatientRoomsDarkAtNight> for compatibility.");
                }
            }

            if (matches.Count == 0)
            {
                return;
            }

            bool foundValidSetting = false;

            foreach (Match match in matches)
            {
                string attributes = match.Groups["attributes"].Value;
                string enabledValue =
                    GetAttributeValue(attributes, "Enabled")?.Trim();

                if (!bool.TryParse(enabledValue, out bool enabled))
                {
                    Plugin.Log?.LogWarning(
                        "Ignoring <" +
                        (legacySetting ? "NightPatientRoomLighting" : "KeepPatientRoomsDarkAtNight") +
                        ">: Enabled must be true or false.");
                    continue;
                }

                if (foundValidSetting)
                {
                    Plugin.Log?.LogWarning(
                        "Duplicate <" +
                        (legacySetting ? "NightPatientRoomLighting" : "KeepPatientRoomsDarkAtNight") +
                        "> setting found in " + ConfigFileName +
                        "; the last valid entry wins.");
                }

                KeepPatientRoomsDarkAtNightEnabled = enabled;
                foundValidSetting = true;
            }
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
            _enabledRoomTypeIds = CreateDefaultSet();
            KeepPatientRoomsDarkAtNightEnabled = false;
        }

        private static HashSet<string> CreateDefaultSet()
        {
            return new HashSet<string>(DefaultRoomTypeIds, StringComparer.Ordinal);
        }
    }
}
