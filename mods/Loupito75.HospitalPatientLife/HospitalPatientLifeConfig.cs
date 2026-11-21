using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;

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

        private const int DefaultRoomRestActivityWeight = 40;
        private const int DefaultRoomEducationActivityWeight = 30;
        private const int DefaultRoomVisualActivityWeight = 30;
        private const int DefaultRoomTelevisionActivityWeight = 25;
        private const int DefaultLyingRestActivityWeight = 50;
        private const int DefaultLyingNurseBookActivityWeight = 25;
        private const int DefaultLyingTelevisionActivityWeight = 25;

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

        private const bool DefaultAllowYellowLeisureObjectInteraction = true;
        private const bool DefaultDebugLogging = false;

        private static bool s_loaded;

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

        internal static int RoomRestActivityWeight { get; private set; }
        internal static int RoomEducationActivityWeight { get; private set; }
        internal static int RoomVisualActivityWeight { get; private set; }
        internal static int RoomTelevisionActivityWeight { get; private set; }
        internal static int LyingRestActivityWeight { get; private set; }
        internal static int LyingNurseBookActivityWeight { get; private set; }
        internal static int LyingTelevisionActivityWeight { get; private set; }

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

        internal static bool AllowYellowLeisureObjectInteraction { get; private set; }
        internal static bool DebugLogging { get; private set; }

        internal static void EnsureLoaded()
        {
            if (!s_loaded)
            {
                Load();
            }
        }

        internal static void Load()
        {
            ConfigValues defaults = CreateDefaultValues();

            try
            {
                string pluginDirectory = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                ConfigPath = Path.Combine(
                    pluginDirectory ?? string.Empty,
                    ConfigFileName);

                if (!File.Exists(ConfigPath))
                {
                    Apply(defaults);
                    s_loaded = true;
                    LogWarning(
                        ConfigFileName +
                        " was not found. Using built-in defaults.");
                    return;
                }

                ConfigValues values = CreateDefaultValues();
                XmlDocument document = LoadDocument(ConfigPath);
                LoadFromDocument(document, values);
                Validate(values);
                Apply(values);
                s_loaded = true;

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
                        ", external leisure weights rest/education/visual " +
                        RestActivityWeight + "/" +
                        EducationActivityWeight + "/" +
                        VisualActivityWeight +
                        ", room weights rest/education/visual/television " +
                        RoomRestActivityWeight + "/" +
                        RoomEducationActivityWeight + "/" +
                        RoomVisualActivityWeight + "/" +
                        RoomTelevisionActivityWeight +
                        ", lying weights rest/nurse-book/television " +
                        LyingRestActivityWeight + "/" +
                        LyingNurseBookActivityWeight + "/" +
                        LyingTelevisionActivityWeight +
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
                        "%, yellow leisure interaction " +
                        AllowYellowLeisureObjectInteraction +
                        ", debug logging " + DebugLogging + ".");
                }
            }
            catch (Exception exception)
            {
                Apply(defaults);
                s_loaded = true;
                LogWarning(
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

        private static XmlDocument LoadDocument(string path)
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.ProhibitDtd = true;

            XmlDocument document = new XmlDocument();
            using (XmlReader reader = XmlReader.Create(path, settings))
            {
                document.Load(reader);
            }

            if (document.DocumentType != null)
            {
                throw new FormatException("DTD declarations are not allowed.");
            }

            return document;
        }

        private static void LoadFromDocument(
            XmlDocument document,
            ConfigValues values)
        {
            XmlElement root = document == null ? null : document.DocumentElement;
            if (root == null ||
                !NamesEqual(root.Name, "HospitalPatientLife"))
            {
                throw new FormatException(
                    "The XML root element must be <HospitalPatientLife>...</HospitalPatientLife>.");
            }

            XmlElement hospitalized =
                GetUniqueChildElement(root, "HospitalizedPatients");
            if (hospitalized != null)
            {
                LoadHospitalizedCafeteria(
                    GetUniqueChildElement(hospitalized, "Cafeteria"),
                    values);
                LoadHospitalizedFreeTime(
                    GetUniqueChildElement(hospitalized, "FreeTime"),
                    values);
                LoadHospitalizedSleep(
                    GetUniqueChildElement(hospitalized, "Sleep"),
                    values);
                LoadHospitalizedNeeds(
                    GetUniqueChildElement(hospitalized, "Needs"),
                    values);
            }

            XmlElement clinic =
                GetUniqueChildElement(root, "ClinicPatients");
            if (clinic != null)
            {
                LoadClinicCafeteria(
                    GetUniqueChildElement(clinic, "Cafeteria"),
                    values);
            }

            XmlElement patientLeisure =
                GetUniqueChildElement(root, "PatientLeisure");
            if (patientLeisure != null)
            {
                values.AllowYellowLeisureObjectInteraction = ParseOptionalBool(
                    patientLeisure,
                    "AllowYellowLeisureObjectInteraction",
                    values.AllowYellowLeisureObjectInteraction);
            }

            XmlElement diagnostics =
                GetUniqueChildElement(root, "Diagnostics");
            if (diagnostics != null)
            {
                values.DebugLogging = ParseOptionalBool(
                    diagnostics,
                    "DebugLogging",
                    values.DebugLogging);
            }
        }

        private static void LoadHospitalizedCafeteria(
            XmlElement cafeteria,
            ConfigValues values)
        {
            if (cafeteria == null)
            {
                return;
            }

            values.ScheduledMealCafeteriaChancePercent = ParseOptionalInt(
                cafeteria,
                "ScheduledMealCafeteriaChancePercent",
                values.ScheduledMealCafeteriaChancePercent,
                0,
                100);
            values.FreeTimeCafeteriaSnackChancePercent = ParseOptionalInt(
                cafeteria,
                "FreeTimeCafeteriaSnackChancePercent",
                values.FreeTimeCafeteriaSnackChancePercent,
                0,
                100);
            values.OtherFloorCafeteriaChanceMultiplierPercentPerFloor =
                ParseOptionalInt(
                    cafeteria,
                    "OtherFloorCafeteriaChanceMultiplierPercentPerFloor",
                    values.OtherFloorCafeteriaChanceMultiplierPercentPerFloor,
                    0,
                    100);
        }

        private static void LoadHospitalizedFreeTime(
            XmlElement freeTime,
            ConfigValues values)
        {
            if (freeTime == null)
            {
                return;
            }

            values.SameFloorLeisureChancePercent = ParseOptionalInt(
                freeTime,
                "SameFloorLeisureChancePercent",
                values.SameFloorLeisureChancePercent,
                0,
                100);
            values.OutdoorLeisureChancePercent = ParseOptionalInt(
                freeTime,
                "OutdoorLeisureChancePercent",
                values.OutdoorLeisureChancePercent,
                0,
                100);
            values.OtherFloorLoungeChanceMultiplierPercentPerFloor =
                ParseOptionalInt(
                    freeTime,
                    "OtherFloorLoungeChanceMultiplierPercentPerFloor",
                    values.OtherFloorLoungeChanceMultiplierPercentPerFloor,
                    0,
                    100);
            values.NearbyRoomsToSearch = ParseOptionalInt(
                freeTime,
                "NearbyRoomsToSearch",
                values.NearbyRoomsToSearch,
                1,
                96);
            values.CandidatePoolSize = ParseOptionalInt(
                freeTime,
                "CandidatePoolSize",
                values.CandidatePoolSize,
                1,
                16);
            values.RestActivityWeight = ParseOptionalInt(
                freeTime,
                "RestActivityWeight",
                values.RestActivityWeight,
                0,
                100);
            values.EducationActivityWeight = ParseOptionalInt(
                freeTime,
                "EducationActivityWeight",
                values.EducationActivityWeight,
                0,
                100);
            values.VisualActivityWeight = ParseOptionalInt(
                freeTime,
                "VisualActivityWeight",
                values.VisualActivityWeight,
                0,
                100);

            values.RoomRestActivityWeight = ParseOptionalInt(
                freeTime,
                "RoomRestActivityWeight",
                values.RoomRestActivityWeight,
                0,
                100);
            values.RoomEducationActivityWeight = ParseOptionalInt(
                freeTime,
                "RoomEducationActivityWeight",
                values.RoomEducationActivityWeight,
                0,
                100);
            values.RoomVisualActivityWeight = ParseOptionalInt(
                freeTime,
                "RoomVisualActivityWeight",
                values.RoomVisualActivityWeight,
                0,
                100);
            values.RoomTelevisionActivityWeight = ParseOptionalInt(
                freeTime,
                "RoomTelevisionActivityWeight",
                values.RoomTelevisionActivityWeight,
                0,
                100);
            values.LyingRestActivityWeight = ParseOptionalInt(
                freeTime,
                "LyingRestActivityWeight",
                values.LyingRestActivityWeight,
                0,
                100);
            values.LyingNurseBookActivityWeight = ParseOptionalInt(
                freeTime,
                "LyingNurseBookActivityWeight",
                values.LyingNurseBookActivityWeight,
                0,
                100);
            values.LyingTelevisionActivityWeight = ParseOptionalInt(
                freeTime,
                "LyingTelevisionActivityWeight",
                values.LyingTelevisionActivityWeight,
                0,
                100);
        }

        private static void LoadHospitalizedSleep(
            XmlElement sleep,
            ConfigValues values)
        {
            if (sleep == null)
            {
                return;
            }

            values.BedtimeStaggerMinutes = ParseOptionalInt(
                sleep,
                "BedtimeStaggerMinutes",
                values.BedtimeStaggerMinutes,
                0,
                120);
            values.WakeUpStaggerMinutes = ParseOptionalInt(
                sleep,
                "WakeUpStaggerMinutes",
                values.WakeUpStaggerMinutes,
                0,
                120);
        }

        private static void LoadClinicCafeteria(
            XmlElement cafeteria,
            ConfigValues values)
        {
            if (cafeteria == null)
            {
                return;
            }

            values.ClinicCafeteriaVisitChancePercent = ParseOptionalInt(
                cafeteria,
                "CafeteriaVisitChancePercent",
                values.ClinicCafeteriaVisitChancePercent,
                0,
                100);
            values.ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor =
                ParseOptionalInt(
                    cafeteria,
                    "OtherFloorCafeteriaChanceMultiplierPercentPerFloor",
                    values.ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor,
                    0,
                    100);
            values.ClinicFullMealPrice = ParseOptionalInt(
                cafeteria,
                "FullMealPrice",
                values.ClinicFullMealPrice,
                0,
                25);
            values.ClinicFullMealHungerReductionMin = ParseOptionalInt(
                cafeteria,
                "FullMealHungerReductionMin",
                values.ClinicFullMealHungerReductionMin,
                0,
                100);
            values.ClinicFullMealHungerReductionMax = ParseOptionalInt(
                cafeteria,
                "FullMealHungerReductionMax",
                values.ClinicFullMealHungerReductionMax,
                0,
                100);
        }

        private static void LoadHospitalizedNeeds(
            XmlElement needs,
            ConfigValues values)
        {
            if (needs == null)
            {
                return;
            }

            values.AllowPersonalNeedChaining = ParseOptionalBool(
                needs,
                "AllowPersonalNeedChaining",
                values.AllowPersonalNeedChaining);
            values.UrgentHungerThreshold = ParseOptionalInt(
                needs,
                "UrgentHungerThreshold",
                values.UrgentHungerThreshold,
                0,
                100);
            values.BedsideMealHungerReductionMin = ParseOptionalInt(
                needs,
                "BedsideMealHungerReductionMin",
                values.BedsideMealHungerReductionMin,
                0,
                100);
            values.BedsideMealHungerReductionMax = ParseOptionalInt(
                needs,
                "BedsideMealHungerReductionMax",
                values.BedsideMealHungerReductionMax,
                0,
                100);
            values.CafeteriaMealHungerReductionMin = ParseOptionalInt(
                needs,
                "CafeteriaMealHungerReductionMin",
                values.CafeteriaMealHungerReductionMin,
                0,
                100);
            values.CafeteriaMealHungerReductionMax = ParseOptionalInt(
                needs,
                "CafeteriaMealHungerReductionMax",
                values.CafeteriaMealHungerReductionMax,
                0,
                100);
            values.UrgentBladderThreshold = ParseOptionalInt(
                needs,
                "UrgentBladderThreshold",
                values.UrgentBladderThreshold,
                0,
                100);
            values.BedsideBladderRemainingMin = ParseOptionalInt(
                needs,
                "BedsideBladderRemainingMin",
                values.BedsideBladderRemainingMin,
                0,
                79);
            values.BedsideBladderRemainingMax = ParseOptionalInt(
                needs,
                "BedsideBladderRemainingMax",
                values.BedsideBladderRemainingMax,
                0,
                79);
            values.AllowNightBathroomTrips = ParseOptionalBool(
                needs,
                "AllowNightBathroomTrips",
                values.AllowNightBathroomTrips);
            values.NightBathroomTripChancePercent = ParseOptionalInt(
                needs,
                "NightBathroomTripChancePercent",
                values.NightBathroomTripChancePercent,
                0,
                100);
        }

        private static XmlElement GetUniqueChildElement(
            XmlElement parent,
            string elementName)
        {
            if (parent == null)
            {
                return null;
            }

            XmlElement result = null;
            for (int i = 0; i < parent.ChildNodes.Count; i++)
            {
                XmlElement child = parent.ChildNodes[i] as XmlElement;
                if (child == null || !NamesEqual(child.Name, elementName))
                {
                    continue;
                }

                if (result != null)
                {
                    throw new FormatException(
                        "Duplicate <" + elementName + "> element under <" +
                        parent.Name + ">.");
                }

                result = child;
            }

            return result;
        }

        private static string GetOptionalScalarText(
            XmlElement parent,
            string elementName)
        {
            XmlElement element = GetUniqueChildElement(parent, elementName);
            if (element == null)
            {
                return null;
            }

            if (element.HasAttributes)
            {
                throw new FormatException(
                    "<" + elementName + "> must not have attributes.");
            }

            for (int i = 0; i < element.ChildNodes.Count; i++)
            {
                if (element.ChildNodes[i].NodeType == XmlNodeType.Element)
                {
                    throw new FormatException(
                        "<" + elementName + "> must contain a scalar value.");
                }
            }

            return element.InnerText == null ? string.Empty : element.InnerText.Trim();
        }

        private static int ParseOptionalInt(
            XmlElement parent,
            string elementName,
            int defaultValue,
            int minimum,
            int maximum)
        {
            string value = GetOptionalScalarText(parent, elementName);
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            int parsed;
            if (!int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out parsed) ||
                parsed < minimum ||
                parsed > maximum)
            {
                throw new FormatException(
                    elementName + " must be an integer from " +
                    minimum + " to " + maximum + ".");
            }

            return parsed;
        }

        private static bool ParseOptionalBool(
            XmlElement parent,
            string elementName,
            bool defaultValue)
        {
            string value = GetOptionalScalarText(parent, elementName);
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            bool parsed;
            if (!bool.TryParse(value, out parsed))
            {
                throw new FormatException(
                    elementName + " must be true or false.");
            }

            return parsed;
        }

        private static bool NamesEqual(string left, string right)
        {
            return string.Equals(
                left,
                right,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void Validate(ConfigValues values)
        {
            if (values.BedsideMealHungerReductionMin >
                values.BedsideMealHungerReductionMax)
            {
                throw new FormatException(
                    "BedsideMealHungerReductionMin must be less than or equal to " +
                    "BedsideMealHungerReductionMax.");
            }

            if (values.CafeteriaMealHungerReductionMin >
                values.CafeteriaMealHungerReductionMax)
            {
                throw new FormatException(
                    "CafeteriaMealHungerReductionMin must be less than or equal to " +
                    "CafeteriaMealHungerReductionMax.");
            }

            if (values.BedsideBladderRemainingMin >
                values.BedsideBladderRemainingMax)
            {
                throw new FormatException(
                    "BedsideBladderRemainingMin must be less than or equal to " +
                    "BedsideBladderRemainingMax.");
            }

            if (values.ClinicFullMealHungerReductionMin >
                values.ClinicFullMealHungerReductionMax)
            {
                throw new FormatException(
                    "Clinic FullMealHungerReductionMin must be less than or equal to " +
                    "FullMealHungerReductionMax.");
            }
        }

        private static ConfigValues CreateDefaultValues()
        {
            ConfigValues values = new ConfigValues();

            values.ScheduledMealCafeteriaChancePercent =
                DefaultScheduledMealCafeteriaChancePercent;
            values.FreeTimeCafeteriaSnackChancePercent =
                DefaultFreeTimeCafeteriaSnackChancePercent;
            values.OtherFloorCafeteriaChanceMultiplierPercentPerFloor =
                DefaultOtherFloorCafeteriaChanceMultiplierPercentPerFloor;

            values.SameFloorLeisureChancePercent =
                DefaultSameFloorLeisureChancePercent;
            values.OutdoorLeisureChancePercent =
                DefaultOutdoorLeisureChancePercent;
            values.OtherFloorLoungeChanceMultiplierPercentPerFloor =
                DefaultOtherFloorLoungeChanceMultiplierPercentPerFloor;
            values.NearbyRoomsToSearch = DefaultNearbyRoomsToSearch;
            values.CandidatePoolSize = DefaultCandidatePoolSize;
            values.RestActivityWeight = DefaultRestActivityWeight;
            values.EducationActivityWeight = DefaultEducationActivityWeight;
            values.VisualActivityWeight = DefaultVisualActivityWeight;

            values.RoomRestActivityWeight = DefaultRoomRestActivityWeight;
            values.RoomEducationActivityWeight = DefaultRoomEducationActivityWeight;
            values.RoomVisualActivityWeight = DefaultRoomVisualActivityWeight;
            values.RoomTelevisionActivityWeight = DefaultRoomTelevisionActivityWeight;
            values.LyingRestActivityWeight = DefaultLyingRestActivityWeight;
            values.LyingNurseBookActivityWeight =
                DefaultLyingNurseBookActivityWeight;
            values.LyingTelevisionActivityWeight =
                DefaultLyingTelevisionActivityWeight;

            values.BedtimeStaggerMinutes = DefaultBedtimeStaggerMinutes;
            values.WakeUpStaggerMinutes = DefaultWakeUpStaggerMinutes;

            values.ClinicCafeteriaVisitChancePercent =
                DefaultClinicCafeteriaVisitChancePercent;
            values.ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor =
                DefaultClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor;
            values.ClinicFullMealPrice = DefaultClinicFullMealPrice;
            values.ClinicFullMealHungerReductionMin =
                DefaultClinicFullMealHungerReductionMin;
            values.ClinicFullMealHungerReductionMax =
                DefaultClinicFullMealHungerReductionMax;

            values.AllowPersonalNeedChaining = DefaultAllowPersonalNeedChaining;
            values.UrgentHungerThreshold = DefaultUrgentHungerThreshold;
            values.BedsideMealHungerReductionMin =
                DefaultBedsideMealHungerReductionMin;
            values.BedsideMealHungerReductionMax =
                DefaultBedsideMealHungerReductionMax;
            values.CafeteriaMealHungerReductionMin =
                DefaultCafeteriaMealHungerReductionMin;
            values.CafeteriaMealHungerReductionMax =
                DefaultCafeteriaMealHungerReductionMax;
            values.UrgentBladderThreshold = DefaultUrgentBladderThreshold;
            values.BedsideBladderRemainingMin =
                DefaultBedsideBladderRemainingMin;
            values.BedsideBladderRemainingMax =
                DefaultBedsideBladderRemainingMax;
            values.AllowNightBathroomTrips = DefaultAllowNightBathroomTrips;
            values.NightBathroomTripChancePercent =
                DefaultNightBathroomTripChancePercent;

            values.AllowYellowLeisureObjectInteraction =
                DefaultAllowYellowLeisureObjectInteraction;
            values.DebugLogging = DefaultDebugLogging;

            return values;
        }

        private static void Apply(ConfigValues values)
        {
            ScheduledMealCafeteriaChancePercent =
                values.ScheduledMealCafeteriaChancePercent;
            FreeTimeCafeteriaSnackChancePercent =
                values.FreeTimeCafeteriaSnackChancePercent;
            OtherFloorCafeteriaChanceMultiplierPercentPerFloor =
                values.OtherFloorCafeteriaChanceMultiplierPercentPerFloor;

            SameFloorLeisureChancePercent =
                values.SameFloorLeisureChancePercent;
            OutdoorLeisureChancePercent =
                values.OutdoorLeisureChancePercent;
            OtherFloorLoungeChanceMultiplierPercentPerFloor =
                values.OtherFloorLoungeChanceMultiplierPercentPerFloor;
            NearbyRoomsToSearch = values.NearbyRoomsToSearch;
            CandidatePoolSize = values.CandidatePoolSize;
            RestActivityWeight = values.RestActivityWeight;
            EducationActivityWeight = values.EducationActivityWeight;
            VisualActivityWeight = values.VisualActivityWeight;

            RoomRestActivityWeight = values.RoomRestActivityWeight;
            RoomEducationActivityWeight = values.RoomEducationActivityWeight;
            RoomVisualActivityWeight = values.RoomVisualActivityWeight;
            RoomTelevisionActivityWeight = values.RoomTelevisionActivityWeight;
            LyingRestActivityWeight = values.LyingRestActivityWeight;
            LyingNurseBookActivityWeight = values.LyingNurseBookActivityWeight;
            LyingTelevisionActivityWeight = values.LyingTelevisionActivityWeight;

            BedtimeStaggerMinutes = values.BedtimeStaggerMinutes;
            WakeUpStaggerMinutes = values.WakeUpStaggerMinutes;

            ClinicCafeteriaVisitChancePercent =
                values.ClinicCafeteriaVisitChancePercent;
            ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor =
                values.ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor;
            ClinicFullMealPrice = values.ClinicFullMealPrice;
            ClinicFullMealHungerReductionMin =
                values.ClinicFullMealHungerReductionMin;
            ClinicFullMealHungerReductionMax =
                values.ClinicFullMealHungerReductionMax;

            AllowPersonalNeedChaining = values.AllowPersonalNeedChaining;
            UrgentHungerThreshold = values.UrgentHungerThreshold;
            BedsideMealHungerReductionMin =
                values.BedsideMealHungerReductionMin;
            BedsideMealHungerReductionMax =
                values.BedsideMealHungerReductionMax;
            CafeteriaMealHungerReductionMin =
                values.CafeteriaMealHungerReductionMin;
            CafeteriaMealHungerReductionMax =
                values.CafeteriaMealHungerReductionMax;
            UrgentBladderThreshold = values.UrgentBladderThreshold;
            BedsideBladderRemainingMin = values.BedsideBladderRemainingMin;
            BedsideBladderRemainingMax = values.BedsideBladderRemainingMax;
            AllowNightBathroomTrips = values.AllowNightBathroomTrips;
            NightBathroomTripChancePercent =
                values.NightBathroomTripChancePercent;

            AllowYellowLeisureObjectInteraction =
                values.AllowYellowLeisureObjectInteraction;
            DebugLogging = values.DebugLogging;
        }

        private static void LogWarning(string message)
        {
            if (Plugin.Log != null)
            {
                Plugin.Log.LogWarning(message);
            }
        }

        private sealed class ConfigValues
        {
            internal int ScheduledMealCafeteriaChancePercent;
            internal int FreeTimeCafeteriaSnackChancePercent;
            internal int OtherFloorCafeteriaChanceMultiplierPercentPerFloor;

            internal int SameFloorLeisureChancePercent;
            internal int OutdoorLeisureChancePercent;
            internal int OtherFloorLoungeChanceMultiplierPercentPerFloor;
            internal int NearbyRoomsToSearch;
            internal int CandidatePoolSize;
            internal int RestActivityWeight;
            internal int EducationActivityWeight;
            internal int VisualActivityWeight;

            internal int RoomRestActivityWeight;
            internal int RoomEducationActivityWeight;
            internal int RoomVisualActivityWeight;
            internal int RoomTelevisionActivityWeight;
            internal int LyingRestActivityWeight;
            internal int LyingNurseBookActivityWeight;
            internal int LyingTelevisionActivityWeight;

            internal int BedtimeStaggerMinutes;
            internal int WakeUpStaggerMinutes;

            internal int ClinicCafeteriaVisitChancePercent;
            internal int ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor;
            internal int ClinicFullMealPrice;
            internal int ClinicFullMealHungerReductionMin;
            internal int ClinicFullMealHungerReductionMax;

            internal bool AllowPersonalNeedChaining;
            internal int UrgentHungerThreshold;
            internal int BedsideMealHungerReductionMin;
            internal int BedsideMealHungerReductionMax;
            internal int CafeteriaMealHungerReductionMin;
            internal int CafeteriaMealHungerReductionMax;
            internal int UrgentBladderThreshold;
            internal int BedsideBladderRemainingMin;
            internal int BedsideBladderRemainingMax;
            internal bool AllowNightBathroomTrips;
            internal int NightBathroomTripChancePercent;

            internal bool AllowYellowLeisureObjectInteraction;
            internal bool DebugLogging;
        }
    }
}
