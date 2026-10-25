using System;
using System.IO;
using System.Reflection;
using System.Xml;

namespace HospitalRuntimeOptimizer
{
    internal static class OptimizerConfig
    {
        private const string FileName = "Loupito75.HospitalRuntimeOptimizer.Config.xml";

        internal static bool Enabled { get; private set; }
        internal static bool OptimizeHiddenFloors { get; private set; }
        internal static bool HideCharactersOnLowerFloors { get; private set; }
        internal static bool OptimizeCharactersByFloor { get; private set; }
        internal static bool OptimizeNativeHiddenCharacters { get; private set; }
        internal static bool OptimizeRoomRendererCleanup { get; private set; }
        internal static bool OptimizeHiddenDoorVisualInvalidation { get; private set; }
        internal static bool OptimizeMoodComponentLookups { get; private set; }
        internal static bool OptimizeWalkComponentLookups { get; private set; }
        internal static bool OptimizeDuplicateStatisticsRefresh { get; private set; }
        internal static bool OptimizeHospitalizationComponentLookups { get; private set; }
        internal static bool OptimizeOffShiftStaffLookups { get; private set; }
        internal static bool OptimizeIdleStaffLookups { get; private set; }
        internal static bool OptimizeOpeningClosingLightRefresh { get; private set; }
        internal static bool OptimizeOpeningClosingObjectScan { get; private set; }
        internal static bool OptimizeShiftChangeMaintenance { get; private set; }
        internal static bool OptimizeDailyHiringCandidateLookups { get; private set; }
        internal static bool OptimizeHourlyRoleValidation { get; private set; }
        internal static bool OptimizeProcedureSceneDistanceQueries { get; private set; }
        internal static bool OptimizePlayerStatisticsSaveWrites { get; private set; }
        internal static bool EnableDiagnostics { get; private set; }

        internal static void Load()
        {
            SetDefaults();

            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = Path.Combine(directory ?? string.Empty, FileName);

            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning(FileName +
                    " was not found; HRO is using built-in defaults.");
                return;
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                document.Load(path);

                if (document.DocumentType != null ||
                    document.DocumentElement == null ||
                    document.DocumentElement.Name != "HospitalRuntimeOptimizerConfig")
                {
                    throw new FormatException("Invalid config root or DTD.");
                }

                XmlElement root = document.DocumentElement;
                Enabled = ReadBool(root, "Enabled", true);

                XmlElement rendering = root.SelectSingleNode("Rendering") as XmlElement;
                OptimizeHiddenFloors = rendering == null
                    ? true
                    : ReadBool(rendering, "OptimizeHiddenFloors", true);
                HideCharactersOnLowerFloors = rendering == null
                    ? true
                    : ReadBool(rendering, "HideCharactersOnLowerFloors", true);
                OptimizeCharactersByFloor = rendering == null
                    ? true
                    : ReadBool(rendering, "OptimizeCharactersByFloor", true);
                OptimizeNativeHiddenCharacters = rendering == null
                    ? true
                    : ReadBool(rendering, "OptimizeNativeHiddenCharacters", true);
                OptimizeRoomRendererCleanup = rendering == null
                    ? true
                    : ReadBool(rendering, "OptimizeRoomRendererCleanup", true);
                OptimizeHiddenDoorVisualInvalidation = rendering == null
                    ? true
                    : ReadBool(rendering, "OptimizeHiddenDoorVisualInvalidation", true);

                XmlElement simulation = root.SelectSingleNode("Simulation") as XmlElement;
                OptimizeMoodComponentLookups = simulation == null
                    ? true
                    : ReadBool(simulation, "OptimizeMoodComponentLookups", true);
                OptimizeWalkComponentLookups = simulation == null
                    ? true
                    : ReadBool(simulation, "OptimizeWalkComponentLookups", true);
                OptimizeDuplicateStatisticsRefresh = simulation == null
                    ? true
                    : ReadBool(simulation, "OptimizeDuplicateStatisticsRefresh", true);
                OptimizeHospitalizationComponentLookups = simulation == null
                    ? true
                    : ReadBool(simulation, "OptimizeHospitalizationComponentLookups", true);
                OptimizeOffShiftStaffLookups = simulation == null
                    ? true
                    : ReadBool(simulation, "OptimizeOffShiftStaffLookups", true);
                OptimizeIdleStaffLookups = simulation == null
                    ? true
                    : ReadBool(simulation, "OptimizeIdleStaffLookups", true);

                XmlElement temporalSpikes = root.SelectSingleNode("TemporalSpikes") as XmlElement;
                OptimizeOpeningClosingLightRefresh = temporalSpikes == null
                    ? true
                    : ReadBool(temporalSpikes, "OptimizeOpeningClosingLightRefresh", true);
                OptimizeOpeningClosingObjectScan = temporalSpikes == null
                    ? true
                    : ReadBool(temporalSpikes, "OptimizeOpeningClosingObjectScan", true);
                OptimizeShiftChangeMaintenance = temporalSpikes == null
                    ? true
                    : ReadBool(temporalSpikes, "OptimizeShiftChangeMaintenance", true);
                OptimizeDailyHiringCandidateLookups = temporalSpikes == null
                    ? true
                    : ReadBool(temporalSpikes, "OptimizeDailyHiringCandidateLookups", true);
                OptimizeHourlyRoleValidation = temporalSpikes == null
                    ? true
                    : ReadBool(temporalSpikes, "OptimizeHourlyRoleValidation", true);

                OptimizeProcedureSceneDistanceQueries = simulation == null
                    ? true
                    : ReadBool(simulation, "OptimizeProcedureSceneDistanceQueries", true);
                OptimizePlayerStatisticsSaveWrites = simulation == null
                    ? true
                    : ReadBool(simulation, "OptimizePlayerStatisticsSaveWrites", true);

                XmlElement diagnostics =
                    root.SelectSingleNode("Diagnostics") as XmlElement;
                EnableDiagnostics = diagnostics == null
                    ? false
                    : ReadBool(diagnostics, "EnableDiagnostics", false);

                Plugin.Log.LogInfo("Config: Enabled=" + Enabled +
                    ", OptimizeHiddenFloors=" + OptimizeHiddenFloors +
                    ", HideCharactersOnLowerFloors=" + HideCharactersOnLowerFloors +
                    ", OptimizeCharactersByFloor=" + OptimizeCharactersByFloor +
                    ", OptimizeNativeHiddenCharacters=" + OptimizeNativeHiddenCharacters +
                    ", OptimizeRoomRendererCleanup=" + OptimizeRoomRendererCleanup +
                    ", OptimizeHiddenDoorVisualInvalidation=" + OptimizeHiddenDoorVisualInvalidation +
                    ", OptimizeMoodComponentLookups=" + OptimizeMoodComponentLookups +
                    ", OptimizeWalkComponentLookups=" + OptimizeWalkComponentLookups +
                    ", OptimizeDuplicateStatisticsRefresh=" + OptimizeDuplicateStatisticsRefresh +
                    ", OptimizeHospitalizationComponentLookups=" + OptimizeHospitalizationComponentLookups +
                    ", OptimizeOffShiftStaffLookups=" + OptimizeOffShiftStaffLookups +
                    ", OptimizeIdleStaffLookups=" + OptimizeIdleStaffLookups +
                    ", OptimizeOpeningClosingLightRefresh=" + OptimizeOpeningClosingLightRefresh +
                    ", OptimizeOpeningClosingObjectScan=" + OptimizeOpeningClosingObjectScan +
                    ", OptimizeShiftChangeMaintenance=" + OptimizeShiftChangeMaintenance +
                    ", OptimizeDailyHiringCandidateLookups=" + OptimizeDailyHiringCandidateLookups +
                    ", OptimizeHourlyRoleValidation=" + OptimizeHourlyRoleValidation +
                    ", OptimizeProcedureSceneDistanceQueries=" + OptimizeProcedureSceneDistanceQueries +
                    ", OptimizePlayerStatisticsSaveWrites=" + OptimizePlayerStatisticsSaveWrites +
                    ", EnableDiagnostics=" + EnableDiagnostics + ".");
            }
            catch (Exception exception)
            {
                SetDefaults();
                Plugin.Log.LogWarning("Invalid " + FileName +
                    "; HRO is using built-in defaults: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void SetDefaults()
        {
            Enabled = true;
            OptimizeHiddenFloors = true;
            HideCharactersOnLowerFloors = true;
            OptimizeCharactersByFloor = true;
            OptimizeNativeHiddenCharacters = true;
            OptimizeRoomRendererCleanup = true;
            OptimizeHiddenDoorVisualInvalidation = true;
            OptimizeMoodComponentLookups = true;
            OptimizeWalkComponentLookups = true;
            OptimizeDuplicateStatisticsRefresh = true;
            OptimizeHospitalizationComponentLookups = true;
            OptimizeOffShiftStaffLookups = true;
            OptimizeIdleStaffLookups = true;
            OptimizeOpeningClosingLightRefresh = true;
            OptimizeOpeningClosingObjectScan = true;
            OptimizeShiftChangeMaintenance = true;
            OptimizeDailyHiringCandidateLookups = true;
            OptimizeHourlyRoleValidation = true;
            OptimizeProcedureSceneDistanceQueries = true;
            OptimizePlayerStatisticsSaveWrites = true;
            EnableDiagnostics = false;
        }

        private static bool ReadBool(XmlElement root, string name, bool defaultValue)
        {
            XmlNode node = root.SelectSingleNode(name);
            if (node == null)
            {
                return defaultValue;
            }

            bool parsed;
            if (!bool.TryParse(node.InnerText.Trim(), out parsed))
            {
                throw new FormatException(name + " must be true or false.");
            }

            return parsed;
        }
    }
}
