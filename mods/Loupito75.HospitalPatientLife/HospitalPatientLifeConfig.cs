using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HospitalPatientLife
{
    internal static class HospitalPatientLifeConfig
    {
        private const string ConfigFileName = "Loupito75.HospitalPatientLife.Config.xml";

        private const int DefaultScheduledMealCafeteriaChancePercent = 35;
        private const int DefaultFreeTimeCafeteriaSnackChancePercent = 20;
        private const int DefaultOtherFloorCafeteriaChanceMultiplierPercentPerFloor = 50;

        private const int DefaultSameFloorLeisureChancePercent = 40;
        private const int DefaultOutdoorLeisureChancePercent = 35;
        private const int DefaultOtherFloorLoungeChanceMultiplierPercentPerFloor = 75;
        private const int DefaultNearbyRoomsToSearch = 24;
        private const int DefaultCandidatePoolSize = 6;
        private const int DefaultRestActivityWeight = 40;
        private const int DefaultEducationActivityWeight = 30;
        private const int DefaultVisualActivityWeight = 30;

        private const int DefaultBedtimeStaggerMinutes = 60;
        private const int DefaultWakeUpStaggerMinutes = 60;

        private const int DefaultClinicCafeteriaVisitChancePercent = 35;
        private const int DefaultClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor = 50;
        private const int DefaultClinicFullMealPrice = 25;
        private const int DefaultClinicFullMealHungerReductionMin = 65;
        private const int DefaultClinicFullMealHungerReductionMax = 100;

        private const bool DefaultAllowPersonalNeedChaining = true;
        private const int DefaultUrgentHungerThreshold = 70;
        private const int DefaultBedsideMealHungerReductionMin = 50;
        private const int DefaultBedsideMealHungerReductionMax = 100;
        private const int DefaultCafeteriaMealHungerReductionMin = 65;
        private const int DefaultCafeteriaMealHungerReductionMax = 100;
        private const int DefaultUrgentBladderThreshold = 80;
        private const int DefaultBedsideBladderRemainingMin = 0;
        private const int DefaultBedsideBladderRemainingMax = 25;
        private const bool DefaultAllowNightBathroomTrips = true;
        private const int DefaultNightBathroomTripChancePercent = 50;
        private const bool DefaultDebugLogging = false;

        private static readonly Regex XmlCommentRegex = new Regex(
            "<!--.*?-->",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static readonly Regex RootOpenRegex = new Regex(
            @"<\s*HospitalPatientLife(?:\s[^>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RootCloseRegex = new Regex(
            @"<\s*/\s*HospitalPatientLife\s*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static string ConfigPath { get; private set; }

        internal static int ScheduledMealCafeteriaChancePercent { get; private set; }
        internal static int FreeTimeCafeteriaSnackChancePercent { get; private set; }
        internal static int OtherFloorCafeteriaChanceMultiplierPercentPerFloor { get; private set; }

        internal static int SameFloorLeisureChancePercent { get; private set; }
        internal static int OutdoorLeisureChancePercent { get; private set; }
        internal static int OtherFloorLoungeChanceMultiplierPercentPerFloor { get; private set; }
        internal static int NearbyRoomsToSearch { get; private set; }
        internal static int CandidatePoolSize { get; private set; }
        internal static int RestActivityWeight { get; private set; }
        internal static int EducationActivityWeight { get; private set; }
        internal static int VisualActivityWeight { get; private set; }

        internal static int BedtimeStaggerMinutes { get; private set; }
        internal static int WakeUpStaggerMinutes { get; private set; }

        internal static int ClinicCafeteriaVisitChancePercent { get; private set; }
        internal static int ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor { get; private set; }
        internal static int ClinicFullMealPrice { get; private set; }
        internal static int ClinicFullMealHungerReductionMin { get; private set; }
        internal static int ClinicFullMealHungerReductionMax { get; private set; }

        internal static bool AllowPersonalNeedChaining { get; private set; }
        internal static int UrgentHungerThreshold { get; private set; }
        internal static int BedsideMealHungerReductionMin { get; private set; }
        internal static int BedsideMealHungerReductionMax { get; private set; }
        internal static int CafeteriaMealHungerReductionMin { get; private set; }
        internal static int CafeteriaMealHungerReductionMax { get; private set; }
        internal static int UrgentBladderThreshold { get; private set; }
        internal static int BedsideBladderRemainingMin { get; private set; }
        internal static int BedsideBladderRemainingMax { get; private set; }
        internal static bool AllowNightBathroomTrips { get; private set; }
        internal static int NightBathroomTripChancePercent { get; private set; }
        internal static bool DebugLogging { get; private set; }

        internal static void Load()
        {
            UseDefaults();

            try
            {
                string pluginDirectory = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                ConfigPath = Path.Combine(
                    pluginDirectory ?? string.Empty,
                    ConfigFileName);

                if (!File.Exists(ConfigPath))
                {
                    Plugin.Log.LogWarning(
                        ConfigFileName +
                        " was not found. Using built-in defaults.");
                    return;
                }

                string content = File.ReadAllText(ConfigPath);
                LoadFromText(content);

                if (DebugLogging && Plugin.Log != null)
                {
                    Plugin.Log.LogInfo(
                        "Hospital Patient Life configuration loaded: hospitalized scheduled cafeteria " +
                        ScheduledMealCafeteriaChancePercent +
                        "%, hospitalized free-time cafeteria snack " +
                        FreeTimeCafeteriaSnackChancePercent +
                        "%, hospitalized other-floor multiplier " +
                        OtherFloorCafeteriaChanceMultiplierPercentPerFloor +
                        "%/floor, same-floor leisure " +
                        SameFloorLeisureChancePercent +
                        "%, outdoor leisure " +
                        OutdoorLeisureChancePercent +
                        "%, lounge other-floor multiplier " +
                        OtherFloorLoungeChanceMultiplierPercentPerFloor +
                        "%/floor, nearby leisure rooms " + NearbyRoomsToSearch +
                        ", leisure candidate pool " + CandidatePoolSize +
                        ", leisure weights rest/education/visual " +
                        RestActivityWeight + "/" +
                        EducationActivityWeight + "/" +
                        VisualActivityWeight +
                        ", bedtime stagger 0-" + BedtimeStaggerMinutes + "m" +
                        ", wake-up stagger 0-" + WakeUpStaggerMinutes + "m" +
                        ", clinic cafeteria visit " +
                        ClinicCafeteriaVisitChancePercent +
                        "%, clinic other-floor multiplier " +
                        ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor +
                        "%/floor, clinic full-meal price $" + ClinicFullMealPrice +
                        ", clinic full-meal hunger reduction " +
                        ClinicFullMealHungerReductionMin + "-" +
                        ClinicFullMealHungerReductionMax +
                        ", personal need chaining " + AllowPersonalNeedChaining +
                        ", urgent hunger " + UrgentHungerThreshold +
                        ", bedside meal hunger reduction " +
                        BedsideMealHungerReductionMin + "-" + BedsideMealHungerReductionMax +
                        ", hospitalized cafeteria meal hunger reduction " +
                        CafeteriaMealHungerReductionMin + "-" + CafeteriaMealHungerReductionMax +
                        ", urgent bladder " + UrgentBladderThreshold +
                        ", bedside bladder remaining " + BedsideBladderRemainingMin +
                        "-" + BedsideBladderRemainingMax +
                        ", night bathroom trips " + AllowNightBathroomTrips +
                        ", night bathroom chance " + NightBathroomTripChancePercent +
                        "%, debug logging " + DebugLogging + ".");
                }
            }
            catch (Exception exception)
            {
                UseDefaults();
                Plugin.Log.LogWarning(
                    "Could not load " + ConfigFileName +
                    "; using built-in defaults: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static bool RollScheduledMealCafeteria(out int roll)
        {
            roll = UnityEngine.Random.Range(0, 100);
            return roll < ScheduledMealCafeteriaChancePercent;
        }

        internal static bool RollFreeTimeCafeteriaSnack(out int roll)
        {
            roll = UnityEngine.Random.Range(0, 100);
            return roll < FreeTimeCafeteriaSnackChancePercent;
        }

        internal static bool RollSameFloorLeisure(out int roll)
        {
            roll = UnityEngine.Random.Range(0, 100);
            return roll < SameFloorLeisureChancePercent;
        }

        internal static bool RollOutdoorLeisure(out int roll)
        {
            roll = UnityEngine.Random.Range(0, 100);
            return roll < OutdoorLeisureChancePercent;
        }

        internal static bool RollNightBathroomTrip(out int roll)
        {
            roll = UnityEngine.Random.Range(0, 100);
            return roll < NightBathroomTripChancePercent;
        }

        internal static bool RollClinicCafeteriaVisit(out int roll)
        {
            roll = UnityEngine.Random.Range(0, 100);
            return roll < ClinicCafeteriaVisitChancePercent;
        }

        internal static bool RollOtherFloorCafeteria(
            int floorDifference,
            out int roll,
            out int retainedChancePercent)
        {
            return RollFloorMultiplier(
                floorDifference,
                OtherFloorCafeteriaChanceMultiplierPercentPerFloor,
                out roll,
                out retainedChancePercent);
        }

        internal static bool RollOtherFloorLounge(
            int floorDifference,
            out int roll,
            out int retainedChancePercent)
        {
            return RollFloorMultiplier(
                floorDifference,
                OtherFloorLoungeChanceMultiplierPercentPerFloor,
                out roll,
                out retainedChancePercent);
        }

        internal static bool RollClinicOtherFloorCafeteria(
            int floorDifference,
            out int roll,
            out int retainedChancePercent)
        {
            return RollFloorMultiplier(
                floorDifference,
                ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor,
                out roll,
                out retainedChancePercent);
        }

        internal static float RollMealHungerReduction(int minimum, int maximum)
        {
            if (maximum <= minimum)
            {
                return minimum;
            }

            return UnityEngine.Random.Range((float)minimum, (float)maximum);
        }

        private static bool RollFloorMultiplier(
            int floorDifference,
            int perFloorMultiplierPercent,
            out int roll,
            out int retainedChancePercent)
        {
            retainedChancePercent = 100;
            int steps = Math.Max(0, floorDifference);

            for (int i = 0; i < steps; i++)
            {
                retainedChancePercent =
                    (retainedChancePercent * perFloorMultiplierPercent + 50) / 100;
            }

            roll = UnityEngine.Random.Range(0, 100);
            return roll < retainedChancePercent;
        }

        private static void LoadFromText(string content)
        {
            string xml = XmlCommentRegex.Replace(
                content ?? string.Empty,
                string.Empty);

            if (!RootOpenRegex.IsMatch(xml) || !RootCloseRegex.IsMatch(xml))
            {
                throw new FormatException(
                    "The XML root element must be <HospitalPatientLife>...</HospitalPatientLife>.");
            }

            string hospitalizedContent = GetSectionContent(xml, "HospitalizedPatients");
            if (!string.IsNullOrEmpty(hospitalizedContent))
            {
                LoadHospitalizedCafeteria(
                    GetSectionContent(hospitalizedContent, "Cafeteria"));
                LoadHospitalizedFreeTime(
                    GetSectionContent(hospitalizedContent, "FreeTime"));
                LoadHospitalizedSleep(
                    GetSectionContent(hospitalizedContent, "Sleep"));
                LoadHospitalizedNeeds(
                    GetSectionContent(hospitalizedContent, "Needs"));
            }

            string clinicContent = GetSectionContent(xml, "ClinicPatients");
            if (!string.IsNullOrEmpty(clinicContent))
            {
                LoadClinicCafeteria(GetSectionContent(clinicContent, "Cafeteria"));
            }

            string diagnosticsContent = GetSectionContent(xml, "Diagnostics");
            if (!string.IsNullOrEmpty(diagnosticsContent))
            {
                DebugLogging = ParseOptionalBool(
                    diagnosticsContent,
                    "DebugLogging",
                    DefaultDebugLogging);
            }
        }

        private static void LoadHospitalizedCafeteria(string cafeteriaContent)
        {
            if (string.IsNullOrEmpty(cafeteriaContent))
            {
                return;
            }

            ScheduledMealCafeteriaChancePercent = ParseOptionalInt(
                cafeteriaContent,
                "ScheduledMealCafeteriaChancePercent",
                DefaultScheduledMealCafeteriaChancePercent,
                0,
                100);
            FreeTimeCafeteriaSnackChancePercent = ParseOptionalInt(
                cafeteriaContent,
                "FreeTimeCafeteriaSnackChancePercent",
                DefaultFreeTimeCafeteriaSnackChancePercent,
                0,
                100);
            OtherFloorCafeteriaChanceMultiplierPercentPerFloor = ParseOptionalInt(
                cafeteriaContent,
                "OtherFloorCafeteriaChanceMultiplierPercentPerFloor",
                DefaultOtherFloorCafeteriaChanceMultiplierPercentPerFloor,
                0,
                100);
        }

        private static void LoadHospitalizedFreeTime(string freeTimeContent)
        {
            if (string.IsNullOrEmpty(freeTimeContent))
            {
                return;
            }

            SameFloorLeisureChancePercent = ParseOptionalInt(
                freeTimeContent,
                "SameFloorLeisureChancePercent",
                DefaultSameFloorLeisureChancePercent,
                0,
                100);
            OutdoorLeisureChancePercent = ParseOptionalInt(
                freeTimeContent,
                "OutdoorLeisureChancePercent",
                DefaultOutdoorLeisureChancePercent,
                0,
                100);
            OtherFloorLoungeChanceMultiplierPercentPerFloor = ParseOptionalInt(
                freeTimeContent,
                "OtherFloorLoungeChanceMultiplierPercentPerFloor",
                DefaultOtherFloorLoungeChanceMultiplierPercentPerFloor,
                0,
                100);
            NearbyRoomsToSearch = ParseOptionalInt(
                freeTimeContent,
                "NearbyRoomsToSearch",
                DefaultNearbyRoomsToSearch,
                1,
                96);
            CandidatePoolSize = ParseOptionalInt(
                freeTimeContent,
                "CandidatePoolSize",
                DefaultCandidatePoolSize,
                1,
                16);
            RestActivityWeight = ParseOptionalInt(
                freeTimeContent,
                "RestActivityWeight",
                DefaultRestActivityWeight,
                0,
                100);
            EducationActivityWeight = ParseOptionalInt(
                freeTimeContent,
                "EducationActivityWeight",
                DefaultEducationActivityWeight,
                0,
                100);
            VisualActivityWeight = ParseOptionalInt(
                freeTimeContent,
                "VisualActivityWeight",
                DefaultVisualActivityWeight,
                0,
                100);
        }

        private static void LoadHospitalizedSleep(string sleepContent)
        {
            if (string.IsNullOrEmpty(sleepContent))
            {
                return;
            }

            BedtimeStaggerMinutes = ParseOptionalInt(
                sleepContent,
                "BedtimeStaggerMinutes",
                DefaultBedtimeStaggerMinutes,
                0,
                120);
            WakeUpStaggerMinutes = ParseOptionalInt(
                sleepContent,
                "WakeUpStaggerMinutes",
                DefaultWakeUpStaggerMinutes,
                0,
                120);
        }

        private static void LoadClinicCafeteria(string cafeteriaContent)
        {
            if (string.IsNullOrEmpty(cafeteriaContent))
            {
                return;
            }

            ClinicCafeteriaVisitChancePercent = ParseOptionalInt(
                cafeteriaContent,
                "CafeteriaVisitChancePercent",
                DefaultClinicCafeteriaVisitChancePercent,
                0,
                100);
            ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor = ParseOptionalInt(
                cafeteriaContent,
                "OtherFloorCafeteriaChanceMultiplierPercentPerFloor",
                DefaultClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor,
                0,
                100);
            ClinicFullMealPrice = ParseOptionalInt(
                cafeteriaContent,
                "FullMealPrice",
                DefaultClinicFullMealPrice,
                0,
                25);
            ClinicFullMealHungerReductionMin = ParseOptionalInt(
                cafeteriaContent,
                "FullMealHungerReductionMin",
                DefaultClinicFullMealHungerReductionMin,
                0,
                100);
            ClinicFullMealHungerReductionMax = ParseOptionalInt(
                cafeteriaContent,
                "FullMealHungerReductionMax",
                DefaultClinicFullMealHungerReductionMax,
                0,
                100);

            if (ClinicFullMealHungerReductionMin > ClinicFullMealHungerReductionMax)
            {
                throw new FormatException(
                    "Clinic FullMealHungerReductionMin must be less than or equal to " +
                    "FullMealHungerReductionMax.");
            }
        }

        private static void LoadHospitalizedNeeds(string needsContent)
        {
            if (string.IsNullOrEmpty(needsContent))
            {
                return;
            }

            AllowPersonalNeedChaining = ParseOptionalBool(
                needsContent,
                "AllowPersonalNeedChaining",
                DefaultAllowPersonalNeedChaining);
            UrgentHungerThreshold = ParseOptionalInt(
                needsContent,
                "UrgentHungerThreshold",
                DefaultUrgentHungerThreshold,
                0,
                100);
            BedsideMealHungerReductionMin = ParseOptionalInt(
                needsContent,
                "BedsideMealHungerReductionMin",
                DefaultBedsideMealHungerReductionMin,
                0,
                100);
            BedsideMealHungerReductionMax = ParseOptionalInt(
                needsContent,
                "BedsideMealHungerReductionMax",
                DefaultBedsideMealHungerReductionMax,
                0,
                100);
            CafeteriaMealHungerReductionMin = ParseOptionalInt(
                needsContent,
                "CafeteriaMealHungerReductionMin",
                DefaultCafeteriaMealHungerReductionMin,
                0,
                100);
            CafeteriaMealHungerReductionMax = ParseOptionalInt(
                needsContent,
                "CafeteriaMealHungerReductionMax",
                DefaultCafeteriaMealHungerReductionMax,
                0,
                100);
            UrgentBladderThreshold = ParseOptionalInt(
                needsContent,
                "UrgentBladderThreshold",
                DefaultUrgentBladderThreshold,
                0,
                100);
            BedsideBladderRemainingMin = ParseOptionalInt(
                needsContent,
                "BedsideBladderRemainingMin",
                DefaultBedsideBladderRemainingMin,
                0,
                79);
            BedsideBladderRemainingMax = ParseOptionalInt(
                needsContent,
                "BedsideBladderRemainingMax",
                DefaultBedsideBladderRemainingMax,
                0,
                79);
            AllowNightBathroomTrips = ParseOptionalBool(
                needsContent,
                "AllowNightBathroomTrips",
                DefaultAllowNightBathroomTrips);
            NightBathroomTripChancePercent = ParseOptionalInt(
                needsContent,
                "NightBathroomTripChancePercent",
                AllowNightBathroomTrips ? DefaultNightBathroomTripChancePercent : 0,
                0,
                100);

            if (BedsideMealHungerReductionMin > BedsideMealHungerReductionMax)
            {
                throw new FormatException(
                    "BedsideMealHungerReductionMin must be less than or equal to " +
                    "BedsideMealHungerReductionMax.");
            }

            if (CafeteriaMealHungerReductionMin > CafeteriaMealHungerReductionMax)
            {
                throw new FormatException(
                    "CafeteriaMealHungerReductionMin must be less than or equal to " +
                    "CafeteriaMealHungerReductionMax.");
            }

            if (BedsideBladderRemainingMin > BedsideBladderRemainingMax)
            {
                throw new FormatException(
                    "BedsideBladderRemainingMin must be less than or equal to " +
                    "BedsideBladderRemainingMax.");
            }
        }

        private static string GetSectionContent(string xml, string sectionName)
        {
            string escapedName = Regex.Escape(sectionName);
            Match match = Regex.Match(
                xml ?? string.Empty,
                @"<\s*" + escapedName +
                @"(?:\s[^>]*)?>\s*(?<value>.*?)\s*<\s*/\s*" +
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

        private static void UseDefaults()
        {
            ScheduledMealCafeteriaChancePercent =
                DefaultScheduledMealCafeteriaChancePercent;
            FreeTimeCafeteriaSnackChancePercent =
                DefaultFreeTimeCafeteriaSnackChancePercent;
            OtherFloorCafeteriaChanceMultiplierPercentPerFloor =
                DefaultOtherFloorCafeteriaChanceMultiplierPercentPerFloor;

            SameFloorLeisureChancePercent = DefaultSameFloorLeisureChancePercent;
            OutdoorLeisureChancePercent = DefaultOutdoorLeisureChancePercent;
            OtherFloorLoungeChanceMultiplierPercentPerFloor =
                DefaultOtherFloorLoungeChanceMultiplierPercentPerFloor;
            NearbyRoomsToSearch = DefaultNearbyRoomsToSearch;
            CandidatePoolSize = DefaultCandidatePoolSize;
            RestActivityWeight = DefaultRestActivityWeight;
            EducationActivityWeight = DefaultEducationActivityWeight;
            VisualActivityWeight = DefaultVisualActivityWeight;

            BedtimeStaggerMinutes = DefaultBedtimeStaggerMinutes;
            WakeUpStaggerMinutes = DefaultWakeUpStaggerMinutes;

            ClinicCafeteriaVisitChancePercent =
                DefaultClinicCafeteriaVisitChancePercent;
            ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor =
                DefaultClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor;
            ClinicFullMealPrice = DefaultClinicFullMealPrice;
            ClinicFullMealHungerReductionMin =
                DefaultClinicFullMealHungerReductionMin;
            ClinicFullMealHungerReductionMax =
                DefaultClinicFullMealHungerReductionMax;

            AllowPersonalNeedChaining = DefaultAllowPersonalNeedChaining;
            UrgentHungerThreshold = DefaultUrgentHungerThreshold;
            BedsideMealHungerReductionMin = DefaultBedsideMealHungerReductionMin;
            BedsideMealHungerReductionMax = DefaultBedsideMealHungerReductionMax;
            CafeteriaMealHungerReductionMin = DefaultCafeteriaMealHungerReductionMin;
            CafeteriaMealHungerReductionMax = DefaultCafeteriaMealHungerReductionMax;
            UrgentBladderThreshold = DefaultUrgentBladderThreshold;
            BedsideBladderRemainingMin = DefaultBedsideBladderRemainingMin;
            BedsideBladderRemainingMax = DefaultBedsideBladderRemainingMax;
            AllowNightBathroomTrips = DefaultAllowNightBathroomTrips;
            NightBathroomTripChancePercent = DefaultNightBathroomTripChancePercent;
            DebugLogging = DefaultDebugLogging;
        }
    }
}
