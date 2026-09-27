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

            try
            {
                string configPath = Path.Combine(pluginDirectory ?? string.Empty, FileName);
                if (!File.Exists(configPath))
                {
                    Plugin.Log.LogWarning(FileName + " was not found. Using built-in defaults.");
                    LogEffectiveSettings();
                    return;
                }

                XmlDocument document = new XmlDocument();
                document.Load(configPath);

                XmlElement root = document.DocumentElement;
                if (root == null || !string.Equals(root.Name, "HospitalRadio", StringComparison.Ordinal))
                {
                    throw new FormatException("The XML root element must be <HospitalRadio>.");
                }

                OurMusicEnabled = ReadEnabled(root, "OurMusic", OurMusicEnabled);
                MyMusicEnabled = ReadEnabled(root, "MyMusic", MyMusicEnabled);
                VanillaMusicEnabled = ReadEnabled(root, "VanillaMusic", VanillaMusicEnabled);
                AnnouncementsEnabled = ReadEnabled(root, "Announcements", AnnouncementsEnabled);
                DiagnosticsEnabled = ReadEnabled(root, "Diagnostics", DiagnosticsEnabled);

                OurMusicVolumeDb = ReadFloatAttribute(
                    root.SelectSingleNode("OurMusic") as XmlElement,
                    "VolumeDb",
                    OurMusicVolumeDb,
                    MinimumMusicVolumeDb,
                    MaximumMusicVolumeDb);

                MyMusicVolumeDb = ReadFloatAttribute(
                    root.SelectSingleNode("MyMusic") as XmlElement,
                    "VolumeDb",
                    MyMusicVolumeDb,
                    MinimumMusicVolumeDb,
                    MaximumMusicVolumeDb);

                AnnouncementVolumeDb = ReadFloatAttribute(
                    root.SelectSingleNode("Announcements") as XmlElement,
                    "VolumeDb",
                    AnnouncementVolumeDb,
                    MinimumAnnouncementVolumeDb,
                    MaximumAnnouncementVolumeDb);

                ReadAnnouncementSettings(root);
                ValidateAnnouncementSettings();
                LogEffectiveSettings();
            }
            catch (Exception exception)
            {
                UseDefaults();
                Plugin.Log.LogError(
                    "Could not load " + FileName + ": " +
                    exception.GetType().Name + ": " + exception.Message);
                Plugin.Log.LogWarning("Using built-in Hospital Radio defaults.");
                LogEffectiveSettings();
            }
        }

        private static bool ReadEnabled(XmlElement root, string elementName, bool fallback)
        {
            XmlNode node = root.SelectSingleNode(elementName);
            XmlElement element = node as XmlElement;
            if (element == null)
            {
                Plugin.Log.LogWarning(
                    "Missing <" + elementName + "> in " + FileName +
                    "; using default " + fallback.ToString().ToLowerInvariant() + ".");
                return fallback;
            }

            string value = element.GetAttribute("Enabled");
            bool enabled;
            if (!bool.TryParse(value, out enabled))
            {
                Plugin.Log.LogWarning(
                    "<" + elementName + "> Enabled must be true or false; using default " +
                    fallback.ToString().ToLowerInvariant() + ".");
                return fallback;
            }

            return enabled;
        }

        private static void ReadAnnouncementSettings(XmlElement root)
        {
            XmlElement announcements = root.SelectSingleNode("Announcements") as XmlElement;
            if (announcements == null)
            {
                return;
            }

            AnnouncementMinSongsBetween = ReadIntAttribute(
                announcements,
                "MinSongsBetween",
                AnnouncementMinSongsBetween,
                1,
                100);

            AnnouncementMaxSongsBetween = ReadIntAttribute(
                announcements,
                "MaxSongsBetween",
                AnnouncementMaxSongsBetween,
                1,
                100);

            AnnouncementRecentHistory = ReadIntAttribute(
                announcements,
                "RecentHistory",
                AnnouncementRecentHistory,
                0,
                100);

            XmlElement periods = announcements.SelectSingleNode("Periods") as XmlElement;
            if (periods == null)
            {
                Plugin.Log.LogWarning(
                    "Missing <Periods> inside <Announcements>; using default announcement period hours.");
                return;
            }

            MorningStartHour = ReadIntAttribute(
                periods,
                "MorningStart",
                MorningStartHour,
                0,
                23);

            AfternoonStartHour = ReadIntAttribute(
                periods,
                "AfternoonStart",
                AfternoonStartHour,
                0,
                23);

            EveningStartHour = ReadIntAttribute(
                periods,
                "EveningStart",
                EveningStartHour,
                0,
                23);

            NightStartHour = ReadIntAttribute(
                periods,
                "NightStart",
                NightStartHour,
                0,
                23);
        }

        private static float ReadFloatAttribute(
            XmlElement element,
            string attributeName,
            float fallback,
            float minimum,
            float maximum)
        {
            if (element == null)
            {
                return fallback;
            }

            string value = element.GetAttribute(attributeName);
            float parsed;
            if (string.IsNullOrEmpty(value)
                || !float.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out parsed)
                || parsed < minimum
                || parsed > maximum)
            {
                Plugin.Log.LogWarning(
                    "<" + element.Name + "> " + attributeName +
                    " must be from " + minimum.ToString("0.##", CultureInfo.InvariantCulture) +
                    " to " + maximum.ToString("0.##", CultureInfo.InvariantCulture) +
                    " dB; using default " +
                    fallback.ToString("0.##", CultureInfo.InvariantCulture) + " dB.");
                return fallback;
            }

            return parsed;
        }

        private static int ReadIntAttribute(
            XmlElement element,
            string attributeName,
            int fallback,
            int minimum,
            int maximum)
        {
            string value = element.GetAttribute(attributeName);
            int parsed;
            if (string.IsNullOrEmpty(value)
                || !int.TryParse(value, out parsed)
                || parsed < minimum
                || parsed > maximum)
            {
                Plugin.Log.LogWarning(
                    "<" + element.Name + "> " + attributeName +
                    " must be an integer from " + minimum + " to " + maximum +
                    "; using default " + fallback + ".");
                return fallback;
            }

            return parsed;
        }

        private static void ValidateAnnouncementSettings()
        {
            if (AnnouncementMaxSongsBetween < AnnouncementMinSongsBetween)
            {
                Plugin.Log.LogWarning(
                    "Announcements MaxSongsBetween cannot be lower than MinSongsBetween; using defaults 3-5.");

                AnnouncementMinSongsBetween = DefaultAnnouncementMinSongsBetween;
                AnnouncementMaxSongsBetween = DefaultAnnouncementMaxSongsBetween;
            }

            if (!(MorningStartHour < AfternoonStartHour
                && AfternoonStartHour < EveningStartHour
                && EveningStartHour < NightStartHour))
            {
                Plugin.Log.LogWarning(
                    "Announcement period start hours must be ordered Morning < Afternoon < Evening < Night; using defaults.");

                MorningStartHour = DefaultMorningStartHour;
                AfternoonStartHour = DefaultAfternoonStartHour;
                EveningStartHour = DefaultEveningStartHour;
                NightStartHour = DefaultNightStartHour;
            }
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
            Plugin.Log.LogInfo(
                "Settings: OurMusic=" + OurMusicEnabled +
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
                ", Periods=" + MorningStartHour +
                "/" + AfternoonStartHour +
                "/" + EveningStartHour +
                "/" + NightStartHour + ".");

            if (!RadioEnabled && AnnouncementsEnabled)
            {
                Plugin.Log.LogInfo(
                    "Announcements are effectively disabled because all Hospital Radio music sources are disabled; vanilla music remains active.");
            }
        }
    }
}
