using System;
using System.Globalization;
using System.IO;
using System.Xml;

namespace HospitalRadio
{
    internal static class HospitalRadioConfig
    {
        internal const string FileName = "Loupito75.HospitalRadio.Config.xml";

        private const bool DefaultDiagnosticsEnabled = false;
        private const float DefaultOurMusicVolumeDb = -22f;
        private const float DefaultMyMusicVolumeDb = -22f;
        private const float DefaultAnnouncementVolumeDb = -8f;
        private const float MinimumMusicVolumeDb = -30f;
        private const float MaximumMusicVolumeDb = -16f;
        private const float MinimumAnnouncementVolumeDb = -14f;
        private const float MaximumAnnouncementVolumeDb = -4f;
        private const int DefaultAnnouncementMinSongsBetween = 3;
        private const int DefaultAnnouncementMaxSongsBetween = 5;
        private const int DefaultAnnouncementRecentHistory = 10;
        private const int DefaultMorningStartHour = 6;
        private const int DefaultAfternoonStartHour = 12;
        private const int DefaultEveningStartHour = 17;
        private const int DefaultNightStartHour = 21;

        internal static bool OurMusicEnabled { get; private set; }
        internal static bool MyMusicEnabled { get; private set; }
        internal static bool VanillaMusicEnabled { get; private set; }
        internal static bool AnnouncementsEnabled { get; private set; }
        internal static bool DiagnosticsEnabled { get; private set; }

        internal static float OurMusicVolumeDb { get; private set; }
        internal static float MyMusicVolumeDb { get; private set; }
        internal static float AnnouncementVolumeDb { get; private set; }

        internal static int AnnouncementMinSongsBetween { get; private set; }
        internal static int AnnouncementMaxSongsBetween { get; private set; }
        internal static int AnnouncementRecentHistory { get; private set; }
        internal static int MorningStartHour { get; private set; }
        internal static int AfternoonStartHour { get; private set; }
        internal static int EveningStartHour { get; private set; }
        internal static int NightStartHour { get; private set; }

        internal static bool RadioEnabled
        {
            get { return DiagnosticsEnabled || OurMusicEnabled || MyMusicEnabled || VanillaMusicEnabled; }
        }

        internal static bool EffectiveAnnouncementsEnabled
        {
            get { return RadioEnabled && AnnouncementsEnabled; }
        }

        internal static void Load(string pluginDirectory)
        {
            UseDefaults();

            string configPath = Path.Combine(pluginDirectory ?? string.Empty, FileName);
            if (!File.Exists(configPath))
            {
                Plugin.Log.LogWarning(FileName + " was not found. Using built-in defaults.");
                LogInactiveAnnouncementNotice();
                return;
            }

            try
            {
                XmlDocument document = LoadDocument(configPath);
                XmlElement root = document.DocumentElement;
                if (root == null || !string.Equals(root.Name, "HospitalRadio", StringComparison.Ordinal))
                {
                    throw new FormatException("The XML root element must be <HospitalRadio>.");
                }

                RequireAttributes(root, new string[0]);
                RequireOnlyChildElements(
                    root,
                    new string[] { "OurMusic", "MyMusic", "VanillaMusic", "Announcements", "Diagnostics" });

                XmlElement ourMusic = RequireSection(
                    root, "OurMusic", new string[] { "Enabled", "VolumeDb" });
                XmlElement myMusic = RequireSection(
                    root, "MyMusic", new string[] { "Enabled", "VolumeDb" });
                XmlElement vanillaMusic = RequireSection(
                    root, "VanillaMusic", new string[] { "Enabled" });
                XmlElement announcements = RequireSection(
                    root,
                    "Announcements",
                    new string[]
                    {
                        "Enabled",
                        "VolumeDb",
                        "MinSongsBetween",
                        "MaxSongsBetween",
                        "RecentHistory",
                        "Periods"
                    });
                XmlElement periods = RequireSection(
                    announcements,
                    "Periods",
                    new string[] { "MorningStart", "AfternoonStart", "EveningStart", "NightStart" });
                XmlElement diagnostics = RequireSection(
                    root, "Diagnostics", new string[] { "Enabled" });

                bool ourMusicEnabled = ReadBoolElement(ourMusic, "Enabled");
                bool myMusicEnabled = ReadBoolElement(myMusic, "Enabled");
                bool vanillaMusicEnabled = ReadBoolElement(vanillaMusic, "Enabled");
                bool announcementsEnabled = ReadBoolElement(announcements, "Enabled");
                bool diagnosticsEnabled = ReadBoolElement(diagnostics, "Enabled");

                float ourMusicVolumeDb = ReadFloatElement(
                    ourMusic, "VolumeDb", MinimumMusicVolumeDb, MaximumMusicVolumeDb);
                float myMusicVolumeDb = ReadFloatElement(
                    myMusic, "VolumeDb", MinimumMusicVolumeDb, MaximumMusicVolumeDb);
                float announcementVolumeDb = ReadFloatElement(
                    announcements, "VolumeDb", MinimumAnnouncementVolumeDb, MaximumAnnouncementVolumeDb);

                int announcementMinSongsBetween =
                    ReadIntElement(announcements, "MinSongsBetween", 1, 100);
                int announcementMaxSongsBetween =
                    ReadIntElement(announcements, "MaxSongsBetween", 1, 100);
                int announcementRecentHistory =
                    ReadIntElement(announcements, "RecentHistory", 0, 100);

                int morningStartHour = ReadIntElement(periods, "MorningStart", 0, 23);
                int afternoonStartHour = ReadIntElement(periods, "AfternoonStart", 0, 23);
                int eveningStartHour = ReadIntElement(periods, "EveningStart", 0, 23);
                int nightStartHour = ReadIntElement(periods, "NightStart", 0, 23);

                if (announcementMaxSongsBetween < announcementMinSongsBetween)
                {
                    throw new FormatException(
                        "Announcements MaxSongsBetween cannot be lower than MinSongsBetween.");
                }

                if (!(morningStartHour < afternoonStartHour
                    && afternoonStartHour < eveningStartHour
                    && eveningStartHour < nightStartHour))
                {
                    throw new FormatException(
                        "Announcement period start hours must be ordered Morning < Afternoon < Evening < Night.");
                }

                OurMusicEnabled = ourMusicEnabled;
                MyMusicEnabled = myMusicEnabled;
                VanillaMusicEnabled = vanillaMusicEnabled;
                DiagnosticsEnabled = diagnosticsEnabled;
                AnnouncementsEnabled = announcementsEnabled;

                OurMusicVolumeDb = ourMusicVolumeDb;
                MyMusicVolumeDb = myMusicVolumeDb;
                AnnouncementVolumeDb = announcementVolumeDb;

                AnnouncementMinSongsBetween = announcementMinSongsBetween;
                AnnouncementMaxSongsBetween = announcementMaxSongsBetween;
                AnnouncementRecentHistory = announcementRecentHistory;
                MorningStartHour = morningStartHour;
                AfternoonStartHour = afternoonStartHour;
                EveningStartHour = eveningStartHour;
                NightStartHour = nightStartHour;

                LogEffectiveSettings();
                LogInactiveAnnouncementNotice();
            }
            catch (Exception exception)
            {
                UseDefaults();
                Plugin.Log.LogError(
                    "Could not load " + FileName + ": " +
                    exception.GetType().Name + ": " + exception.Message);
                Plugin.Log.LogWarning("Using built-in Hospital Radio defaults.");
                LogInactiveAnnouncementNotice();
            }
        }

        private static XmlDocument LoadDocument(string configPath)
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.ProhibitDtd = true;
            settings.IgnoreComments = false;

            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            using (XmlReader reader = XmlReader.Create(configPath, settings))
            {
                document.Load(reader);
            }
            return document;
        }

        private static XmlElement RequireUniqueChild(XmlElement parent, string elementName)
        {
            XmlElement found = null;
            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null ||
                    !string.Equals(element.Name, elementName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (found != null)
                {
                    throw new FormatException(
                        "Duplicate <" + elementName + "> inside <" + parent.Name + ">.");
                }

                found = element;
            }

            if (found == null)
            {
                throw new FormatException(
                    "Missing <" + elementName + "> inside <" + parent.Name + ">.");
            }

            return found;
        }

        private static void RequireOnlyChildElements(XmlElement parent, string[] allowedNames)
        {
            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null)
                {
                    if ((node.NodeType == XmlNodeType.Text || node.NodeType == XmlNodeType.CDATA)
                        && !string.IsNullOrEmpty(node.Value)
                        && node.Value.Trim().Length > 0)
                    {
                        throw new FormatException(
                            "Unexpected text inside <" + parent.Name + ">.");
                    }
                    continue;
                }

                bool allowed = false;
                for (int i = 0; i < allowedNames.Length; i++)
                {
                    if (string.Equals(element.Name, allowedNames[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw new FormatException(
                        "Unexpected <" + element.Name + "> inside <" + parent.Name + ">.");
                }
            }
        }

        private static XmlElement RequireSection(
            XmlElement parent,
            string elementName,
            string[] allowedChildNames)
        {
            XmlElement section = RequireUniqueChild(parent, elementName);
            RequireAttributes(section, new string[0]);
            RequireOnlyChildElements(section, allowedChildNames);
            return section;
        }

        private static XmlElement RequireScalarElement(
            XmlElement parent,
            string elementName)
        {
            XmlElement element = RequireUniqueChild(parent, elementName);
            RequireAttributes(element, new string[0]);

            foreach (XmlNode node in element.ChildNodes)
            {
                if (node.NodeType == XmlNodeType.Text ||
                    node.NodeType == XmlNodeType.Whitespace ||
                    node.NodeType == XmlNodeType.SignificantWhitespace ||
                    node.NodeType == XmlNodeType.Comment)
                {
                    continue;
                }

                throw new FormatException(
                    "<" + elementName + "> inside <" + parent.Name +
                    "> must contain only a scalar text value.");
            }

            return element;
        }

        private static void RequireAttributes(XmlElement element, string[] attributeNames)
        {
            if (element.Attributes.Count != attributeNames.Length)
            {
                throw new FormatException(
                    "<" + element.Name + "> has an unexpected attribute set.");
            }

            for (int i = 0; i < attributeNames.Length; i++)
            {
                if (!element.HasAttribute(attributeNames[i]))
                {
                    throw new FormatException(
                        "<" + element.Name + "> is missing the " +
                        attributeNames[i] + " attribute.");
                }
            }
        }

        private static bool ReadBoolElement(XmlElement parent, string elementName)
        {
            XmlElement element = RequireScalarElement(parent, elementName);
            bool parsed;
            if (!bool.TryParse(element.InnerText.Trim(), out parsed))
            {
                throw new FormatException(
                    "<" + elementName + "> inside <" + parent.Name +
                    "> must be true or false.");
            }
            return parsed;
        }

        private static float ReadFloatElement(
            XmlElement parent,
            string elementName,
            float minimum,
            float maximum)
        {
            XmlElement element = RequireScalarElement(parent, elementName);
            float parsed;
            if (!float.TryParse(
                    element.InnerText.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out parsed)
                || parsed < minimum
                || parsed > maximum)
            {
                throw new FormatException(
                    "<" + elementName + "> inside <" + parent.Name +
                    "> must be from " +
                    minimum.ToString("0.##", CultureInfo.InvariantCulture) +
                    " to " +
                    maximum.ToString("0.##", CultureInfo.InvariantCulture) +
                    " dB.");
            }
            return parsed;
        }

        private static int ReadIntElement(
            XmlElement parent,
            string elementName,
            int minimum,
            int maximum)
        {
            XmlElement element = RequireScalarElement(parent, elementName);
            int parsed;
            if (!int.TryParse(
                    element.InnerText.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out parsed)
                || parsed < minimum
                || parsed > maximum)
            {
                throw new FormatException(
                    "<" + elementName + "> inside <" + parent.Name +
                    "> must be an integer from " + minimum +
                    " to " + maximum + ".");
            }
            return parsed;
        }

        private static void UseDefaults()
        {
            OurMusicEnabled = true;
            MyMusicEnabled = false;
            VanillaMusicEnabled = true;
            AnnouncementsEnabled = true;
            DiagnosticsEnabled = DefaultDiagnosticsEnabled;

            OurMusicVolumeDb = DefaultOurMusicVolumeDb;
            MyMusicVolumeDb = DefaultMyMusicVolumeDb;
            AnnouncementVolumeDb = DefaultAnnouncementVolumeDb;

            AnnouncementMinSongsBetween = DefaultAnnouncementMinSongsBetween;
            AnnouncementMaxSongsBetween = DefaultAnnouncementMaxSongsBetween;
            AnnouncementRecentHistory = DefaultAnnouncementRecentHistory;
            MorningStartHour = DefaultMorningStartHour;
            AfternoonStartHour = DefaultAfternoonStartHour;
            EveningStartHour = DefaultEveningStartHour;
            NightStartHour = DefaultNightStartHour;
        }

        private static void LogEffectiveSettings()
        {
            if (!DiagnosticsEnabled)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[Diag] Settings: OurMusic=" + OurMusicEnabled +
                ", MyMusic=" + MyMusicEnabled +
                ", VanillaMusic=" + VanillaMusicEnabled +
                ", Announcements=" + AnnouncementsEnabled +
                ", Diagnostics=" + DiagnosticsEnabled +
                ", VolumeDb=Our:" + OurMusicVolumeDb.ToString("0.##", CultureInfo.InvariantCulture) +
                "/My:" + MyMusicVolumeDb.ToString("0.##", CultureInfo.InvariantCulture) +
                "/Voice:" + AnnouncementVolumeDb.ToString("0.##", CultureInfo.InvariantCulture) +
                ", SongsBetweenAnnouncements=" + AnnouncementMinSongsBetween +
                "-" + AnnouncementMaxSongsBetween +
                ", RecentHistory=" + AnnouncementRecentHistory +
                ", Periods=" + MorningStartHour + "/" + AfternoonStartHour +
                "/" + EveningStartHour + "/" + NightStartHour + ".");
        }

        private static void LogInactiveAnnouncementNotice()
        {
            if (!RadioEnabled && AnnouncementsEnabled)
            {
                Plugin.Log.LogInfo(
                    "Announcements are effectively disabled because all Hospital Radio music sources are disabled; vanilla music remains active.");
            }
        }
    }
}
