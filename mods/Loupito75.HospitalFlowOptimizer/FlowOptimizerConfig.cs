using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace HospitalFlowOptimizer
{
    internal static class FlowOptimizerConfig
    {
        private const string ConfigFileName = "Loupito75.HospitalFlowOptimizer.Config.xml";

        private const bool DefaultSmartDoctorAssignment = true;
        private const float DefaultWorkloadWeight = 1.0f;
        private const float DefaultClinicalFitWeight = 0.4f;
        private const bool DefaultProtectSurgicalCapacity = true;

        private const bool DefaultSmartClinicDoctorAssignment = true;
        private const float DefaultClinicLoadBalancingWeight = 1.0f;
        private const float DefaultClinicDistanceWeight = 0.3f;
        private const float DefaultClinicClinicalFitWeight = 0.0f;

        private const bool DefaultSmartRoomSelection = true;
        private const float DefaultSmartRoomWorkloadWeight = 1.0f;
        private const float DefaultSmartRoomDistanceWeight = 0.3f;

        private const bool DefaultSmartStaffNeedResourceSelection = true;
        private const bool DefaultLabQueueBalancing = true;
        private const bool DefaultSmartLabTaskAssignment = true;
        private const int DefaultLabTaskPriorityTimeMinutes = 30;
        private const int DefaultLabTaskPriorityRandomnessMinutes = 5;
        private const int DefaultLabWaitingRoomRerouteChance = 70;
        private const bool DefaultSmartTransportWait = true;
        private const bool DefaultSmartTransportContinuation = true;
        private const int DefaultSmartTransportContinuationChance = 65;
        private const bool DefaultSmartTransportPressureRescue = true;
        private const int DefaultSmartTransportPressureRescueChance = 70;
        private const bool DefaultSmartTransportSampleContinuation = true;
        private const int DefaultSmartTransportSampleContinuationChance = 75;
        private const bool DefaultFollowUpQueuePriority = true;
        private const int DefaultFollowUpQueueBaseBonusMinutes = 20;
        private const int DefaultFollowUpQueueRandomVariationMinutes = 8;
        private const bool DefaultProtectTraumaCenterForObservationExams = true;
        private const int DefaultTraumaCenterProtectionFallbackRetries = 2;
        private const int DefaultTraumaCenterProtectionRetryMinutes = 5;
        private const int DefaultSmartTransportWaitChance = 65;
        private const int DefaultSmartTransportWaitMaxProcedureMinutes = 16;
        private const int DefaultSmartTransportWaitMaxProcedureVariationMinutes = 4;
        private const bool DefaultDoctorDiagnosticLogging = false;
        private const bool DefaultStaffNeedsDiagnosticLogging = false;
        private const bool DefaultLabDiagnosticLogging = false;
        private const bool DefaultTransportDiagnosticLogging = false;
        private const bool DefaultTraumaCenterDiagnosticLogging = false;

        private const float MinimumPublicWeight = 0.0f;
        private const float MaximumPublicWeight = 5.0f;

        private static readonly Regex XmlCommentRegex = new Regex(
            "<!--.*?-->",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        internal static bool SmartDoctorAssignment { get; private set; }
        internal static float WorkloadWeight { get; private set; }
        internal static float ClinicalFitWeight { get; private set; }
        internal static bool ProtectSurgicalCapacity { get; private set; }

        internal static bool SmartClinicDoctorAssignment { get; private set; }
        internal static float ClinicLoadBalancingWeight { get; private set; }
        internal static float ClinicDistanceWeight { get; private set; }
        internal static float ClinicClinicalFitWeight { get; private set; }

        internal static bool SmartRoomSelection { get; private set; }
        internal static float SmartRoomWorkloadWeight { get; private set; }
        internal static float SmartRoomDistanceWeight { get; private set; }

        internal static bool SmartStaffNeedResourceSelection { get; private set; }
        internal static bool LabQueueBalancing { get; private set; }
        internal static bool SmartLabTaskAssignment { get; private set; }
        internal static int LabTaskPriorityTimeMinutes { get; private set; }
        internal static int LabTaskPriorityRandomnessMinutes { get; private set; }
        internal static int LabWaitingRoomRerouteChance { get; private set; }
        internal static bool SmartTransportWait { get; private set; }
        internal static bool SmartTransportContinuation { get; private set; }
        internal static int SmartTransportContinuationChance { get; private set; }
        internal static bool SmartTransportPressureRescue { get; private set; }
        internal static int SmartTransportPressureRescueChance { get; private set; }
        internal static bool SmartTransportSampleContinuation { get; private set; }
        internal static int SmartTransportSampleContinuationChance { get; private set; }
        internal static bool FollowUpQueuePriority { get; private set; }
        internal static int FollowUpQueueBaseBonusMinutes { get; private set; }
        internal static int FollowUpQueueRandomVariationMinutes { get; private set; }
        internal static bool ProtectTraumaCenterForObservationExams { get; private set; }
        internal static int TraumaCenterProtectionFallbackRetries { get; private set; }
        internal static int TraumaCenterProtectionRetryMinutes { get; private set; }
        internal static int SmartTransportWaitChance { get; private set; }
        internal static int SmartTransportWaitMaxProcedureMinutes { get; private set; }
        internal static int SmartTransportWaitMaxProcedureVariationMinutes { get; private set; }
        internal static bool DoctorDiagnosticLogging { get; private set; }
        internal static bool StaffNeedsDiagnosticLogging { get; private set; }
        internal static bool LabDiagnosticLogging { get; private set; }
        internal static bool TransportDiagnosticLogging { get; private set; }
        internal static bool TraumaCenterDiagnosticLogging { get; private set; }

        internal static void Load()
        {
            UseDefaults();

            try
            {
                string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string configPath = Path.Combine(pluginDirectory ?? string.Empty, ConfigFileName);

                if (!File.Exists(configPath))
                {
                    Plugin.Log?.LogWarning(
                        ConfigFileName + " was not found. Using built-in defaults.");
                    return;
                }

                string rawXml = File.ReadAllText(configPath);
                XmlDocument validationDocument = new XmlDocument();
                validationDocument.LoadXml(rawXml);

                string xml = XmlCommentRegex.Replace(rawXml, string.Empty);

                SmartDoctorAssignment =
                    ReadBoolInSection(xml, "SmartDoctor", "Enabled", DefaultSmartDoctorAssignment);
                WorkloadWeight =
                    ReadWeightInSection(xml, "SmartDoctor", "WorkloadWeight", DefaultWorkloadWeight);
                ClinicalFitWeight =
                    ReadWeightInSection(xml, "SmartDoctor", "ClinicalFitWeight", DefaultClinicalFitWeight);
                ProtectSurgicalCapacity =
                    ReadBoolInSection(xml, "SmartDoctor", "ProtectSurgicalCapacity", DefaultProtectSurgicalCapacity);

                SmartClinicDoctorAssignment =
                    ReadBoolInSection(xml, "SmartClinicDoctor", "Enabled", DefaultSmartClinicDoctorAssignment);
                ClinicLoadBalancingWeight =
                    ReadWeightInSection(xml, "SmartClinicDoctor", "LoadBalancingWeight", DefaultClinicLoadBalancingWeight);
                ClinicDistanceWeight =
                    ReadWeightInSection(xml, "SmartClinicDoctor", "DistanceWeight", DefaultClinicDistanceWeight);
                ClinicClinicalFitWeight =
                    ReadWeightInSection(xml, "SmartClinicDoctor", "ClinicalFitWeight", DefaultClinicClinicalFitWeight);

                SmartRoomSelection =
                    ReadBoolInSection(xml, "SmartRoomSelection", "Enabled", DefaultSmartRoomSelection);
                SmartRoomWorkloadWeight =
                    ReadWeightInSection(xml, "SmartRoomSelection", "WorkloadWeight", DefaultSmartRoomWorkloadWeight);
                SmartRoomDistanceWeight =
                    ReadWeightInSection(xml, "SmartRoomSelection", "DistanceWeight", DefaultSmartRoomDistanceWeight);

                SmartStaffNeedResourceSelection =
                    ReadBoolInSection(xml, "SmartStaffNeeds", "Enabled", DefaultSmartStaffNeedResourceSelection);

                LabQueueBalancing =
                    ReadBoolInSection(xml, "LabOptimization", "QueueBalancing", DefaultLabQueueBalancing);
                SmartLabTaskAssignment =
                    ReadBoolInSection(xml, "LabOptimization", "SmartTaskAssignment", DefaultSmartLabTaskAssignment);
                LabTaskPriorityTimeMinutes =
                    ReadIntInRangeInSection(xml, "LabOptimization", "TaskPriorityTimeMinutes",
                        DefaultLabTaskPriorityTimeMinutes, 0, 240);
                LabTaskPriorityRandomnessMinutes =
                    ReadIntInRangeInSection(xml, "LabOptimization", "TaskPriorityRandomnessMinutes",
                        DefaultLabTaskPriorityRandomnessMinutes, 0, 120);
                LabWaitingRoomRerouteChance =
                    ReadPercentInSection(xml, "LabOptimization", "WaitingRoomRerouteChance",
                        DefaultLabWaitingRoomRerouteChance);

                FollowUpQueuePriority =
                    ReadBoolInSection(xml, "FollowUpQueue", "Enabled", DefaultFollowUpQueuePriority);
                FollowUpQueueBaseBonusMinutes =
                    ReadIntInRangeInSection(xml, "FollowUpQueue", "PriorityTimeMinutes",
                        DefaultFollowUpQueueBaseBonusMinutes, 0, 240);
                FollowUpQueueRandomVariationMinutes =
                    ReadIntInRangeInSection(xml, "FollowUpQueue", "PriorityRandomnessMinutes",
                        DefaultFollowUpQueueRandomVariationMinutes, 0, 120);

                SmartTransportWait =
                    ReadBoolInSection(xml, "SmartTransport", "WaitEnabled", DefaultSmartTransportWait);
                SmartTransportContinuation =
                    ReadBoolInSection(xml, "SmartTransport", "ContinuationEnabled", DefaultSmartTransportContinuation);
                SmartTransportContinuationChance =
                    ReadPercentInSection(
                        xml,
                        "SmartTransport",
                        "ContinuationChance",
                        DefaultSmartTransportContinuationChance);
                SmartTransportPressureRescue =
                    ReadBoolInSection(
                        xml,
                        "SmartTransport",
                        "PressureRescueEnabled",
                        DefaultSmartTransportPressureRescue);
                SmartTransportPressureRescueChance =
                    ReadPercentInSection(
                        xml,
                        "SmartTransport",
                        "PressureRescueChance",
                        DefaultSmartTransportPressureRescueChance);
                SmartTransportSampleContinuation =
                    ReadBoolInSection(
                        xml,
                        "SmartTransport",
                        "SampleContinuationEnabled",
                        DefaultSmartTransportSampleContinuation);
                SmartTransportSampleContinuationChance =
                    ReadPercentInSection(
                        xml,
                        "SmartTransport",
                        "SampleContinuationChance",
                        DefaultSmartTransportSampleContinuationChance);
                SmartTransportWaitChance =
                    ReadPercentInSection(xml, "SmartTransport", "WaitChance", DefaultSmartTransportWaitChance);
                SmartTransportWaitMaxProcedureMinutes =
                    ReadIntInRangeInSection(xml, "SmartTransport", "WaitMaxProcedureMinutes",
                        DefaultSmartTransportWaitMaxProcedureMinutes, 1, 240);
                SmartTransportWaitMaxProcedureVariationMinutes =
                    ReadIntInRangeInSection(
                        xml,
                        "SmartTransport",
                        "WaitMaxProcedureVariationMinutes",
                        DefaultSmartTransportWaitMaxProcedureVariationMinutes,
                        0,
                        120);

                ProtectTraumaCenterForObservationExams =
                    ReadBoolInSection(xml, "TraumaCenterProtection", "Enabled",
                        DefaultProtectTraumaCenterForObservationExams);
                TraumaCenterProtectionFallbackRetries =
                    ReadIntInRangeInSection(xml, "TraumaCenterProtection", "FallbackRetries",
                        DefaultTraumaCenterProtectionFallbackRetries, 0, 10);
                TraumaCenterProtectionRetryMinutes =
                    ReadIntInRangeInSection(xml, "TraumaCenterProtection", "RetryMinutes",
                        DefaultTraumaCenterProtectionRetryMinutes, 1, 60);

                DoctorDiagnosticLogging =
                    ReadBoolInSection(xml, "Diagnostics", "Doctor", DefaultDoctorDiagnosticLogging);
                StaffNeedsDiagnosticLogging =
                    ReadBoolInSection(xml, "Diagnostics", "StaffNeeds", DefaultStaffNeedsDiagnosticLogging);
                LabDiagnosticLogging =
                    ReadBoolInSection(xml, "Diagnostics", "Lab", DefaultLabDiagnosticLogging);
                TransportDiagnosticLogging =
                    ReadBoolInSection(xml, "Diagnostics", "Transport", DefaultTransportDiagnosticLogging);
                TraumaCenterDiagnosticLogging =
                    ReadBoolInSection(xml, "Diagnostics", "TraumaCenter", DefaultTraumaCenterDiagnosticLogging);
            }
            catch (Exception exception)
            {
                UseDefaults();
                Plugin.Log?.LogWarning(
                    "Could not load " + ConfigFileName + "; using built-in defaults: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }
        private static bool ReadBoolInSection(
            string xml,
            string sectionName,
            string elementName,
            bool defaultValue)
        {
            string value;
            if (!TryReadElementInSection(xml, sectionName, elementName, out value))
            {
                return defaultValue;
            }

            return ParseBool(sectionName + "/" + elementName, value, defaultValue);
        }

        private static bool ParseBool(
            string settingName,
            string value,
            bool defaultValue)
        {
            bool parsed;
            if (!bool.TryParse(value, out parsed))
            {
                Plugin.Log?.LogWarning(
                    settingName + " must be true or false. Using default " +
                    defaultValue.ToString().ToLowerInvariant() + ".");
                return defaultValue;
            }

            return parsed;
        }
        private static int ReadIntInRangeInSection(
            string xml,
            string sectionName,
            string elementName,
            int defaultValue,
            int minimum,
            int maximum)
        {
            string value;
            if (!TryReadElementInSection(xml, sectionName, elementName, out value))
            {
                return defaultValue;
            }

            int parsed;
            if (!int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out parsed))
            {
                Plugin.Log?.LogWarning(
                    sectionName + "/" + elementName +
                    " must be an integer between " +
                    minimum.ToString(CultureInfo.InvariantCulture) + " and " +
                    maximum.ToString(CultureInfo.InvariantCulture) +
                    ". Using default " +
                    defaultValue.ToString(CultureInfo.InvariantCulture) + ".");
                return defaultValue;
            }

            if (parsed < minimum)
            {
                Plugin.Log?.LogWarning(
                    sectionName + "/" + elementName +
                    " was below the supported range and was clamped to " +
                    minimum.ToString(CultureInfo.InvariantCulture) + ".");
                return minimum;
            }

            if (parsed > maximum)
            {
                Plugin.Log?.LogWarning(
                    sectionName + "/" + elementName +
                    " was above the supported range and was clamped to " +
                    maximum.ToString(CultureInfo.InvariantCulture) + ".");
                return maximum;
            }

            return parsed;
        }

        private static int ReadPercentInSection(
            string xml, string sectionName, string elementName, int defaultValue)
        {
            return ReadIntInRangeInSection(
                xml, sectionName, elementName, defaultValue, 0, 100);
        }
        private static float ReadWeightInSection(
            string xml,
            string sectionName,
            string elementName,
            float defaultValue)
        {
            string value;
            if (!TryReadElementInSection(xml, sectionName, elementName, out value))
            {
                return defaultValue;
            }

            return ParseWeight(sectionName + "/" + elementName, value, defaultValue);
        }

        private static float ParseWeight(
            string settingName,
            string value,
            float defaultValue)
        {
            float parsed;
            if (!float.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out parsed) ||
                float.IsNaN(parsed) ||
                float.IsInfinity(parsed))
            {
                Plugin.Log?.LogWarning(
                    settingName + " must be a number between " +
                    MinimumPublicWeight.ToString(CultureInfo.InvariantCulture) + " and " +
                    MaximumPublicWeight.ToString(CultureInfo.InvariantCulture) +
                    ". Using default " +
                    defaultValue.ToString(CultureInfo.InvariantCulture) + ".");
                return defaultValue;
            }

            if (parsed < MinimumPublicWeight)
            {
                Plugin.Log?.LogWarning(
                    settingName + " was below the supported range and was clamped to " +
                    MinimumPublicWeight.ToString(CultureInfo.InvariantCulture) + ".");
                return MinimumPublicWeight;
            }

            if (parsed > MaximumPublicWeight)
            {
                Plugin.Log?.LogWarning(
                    settingName + " was above the supported range and was clamped to " +
                    MaximumPublicWeight.ToString(CultureInfo.InvariantCulture) + ".");
                return MaximumPublicWeight;
            }

            return parsed;
        }

        private static bool TryReadElementInSection(
            string xml,
            string sectionName,
            string elementName,
            out string value)
        {
            string section;
            if (!TryReadElement(xml, sectionName, out section))
            {
                value = null;
                return false;
            }

            return TryReadElement(section, elementName, out value);
        }

        private static bool TryReadElement(
            string xml,
            string elementName,
            out string value)
        {
            string escapedName = Regex.Escape(elementName);
            Match match = Regex.Match(
                xml ?? string.Empty,
                @"<\s*" + escapedName + @"\s*>\s*(?<value>.*?)\s*<\s*/\s*" +
                escapedName + @"\s*>",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline |
                RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                value = null;
                return false;
            }

            value = match.Groups["value"].Value.Trim();
            return true;
        }

        private static void UseDefaults()
        {
            SmartDoctorAssignment = DefaultSmartDoctorAssignment;
            WorkloadWeight = DefaultWorkloadWeight;
            ClinicalFitWeight = DefaultClinicalFitWeight;
            ProtectSurgicalCapacity = DefaultProtectSurgicalCapacity;

            SmartClinicDoctorAssignment = DefaultSmartClinicDoctorAssignment;
            ClinicLoadBalancingWeight = DefaultClinicLoadBalancingWeight;
            ClinicDistanceWeight = DefaultClinicDistanceWeight;
            ClinicClinicalFitWeight = DefaultClinicClinicalFitWeight;

            SmartRoomSelection = DefaultSmartRoomSelection;
            SmartRoomWorkloadWeight = DefaultSmartRoomWorkloadWeight;
            SmartRoomDistanceWeight = DefaultSmartRoomDistanceWeight;

            SmartStaffNeedResourceSelection = DefaultSmartStaffNeedResourceSelection;
            LabQueueBalancing = DefaultLabQueueBalancing;
            SmartLabTaskAssignment = DefaultSmartLabTaskAssignment;
            LabTaskPriorityTimeMinutes = DefaultLabTaskPriorityTimeMinutes;
            LabTaskPriorityRandomnessMinutes = DefaultLabTaskPriorityRandomnessMinutes;
            LabWaitingRoomRerouteChance = DefaultLabWaitingRoomRerouteChance;
            SmartTransportWait = DefaultSmartTransportWait;
            SmartTransportContinuation = DefaultSmartTransportContinuation;
            SmartTransportContinuationChance =
                DefaultSmartTransportContinuationChance;
            SmartTransportPressureRescue =
                DefaultSmartTransportPressureRescue;
            SmartTransportPressureRescueChance =
                DefaultSmartTransportPressureRescueChance;
            SmartTransportSampleContinuation =
                DefaultSmartTransportSampleContinuation;
            SmartTransportSampleContinuationChance =
                DefaultSmartTransportSampleContinuationChance;
            FollowUpQueuePriority = DefaultFollowUpQueuePriority;
            FollowUpQueueBaseBonusMinutes = DefaultFollowUpQueueBaseBonusMinutes;
            FollowUpQueueRandomVariationMinutes =
                DefaultFollowUpQueueRandomVariationMinutes;
            ProtectTraumaCenterForObservationExams =
                DefaultProtectTraumaCenterForObservationExams;
            TraumaCenterProtectionFallbackRetries =
                DefaultTraumaCenterProtectionFallbackRetries;
            TraumaCenterProtectionRetryMinutes =
                DefaultTraumaCenterProtectionRetryMinutes;
            SmartTransportWaitChance = DefaultSmartTransportWaitChance;
            SmartTransportWaitMaxProcedureMinutes =
                DefaultSmartTransportWaitMaxProcedureMinutes;
            SmartTransportWaitMaxProcedureVariationMinutes =
                DefaultSmartTransportWaitMaxProcedureVariationMinutes;
            DoctorDiagnosticLogging = DefaultDoctorDiagnosticLogging;
            StaffNeedsDiagnosticLogging = DefaultStaffNeedsDiagnosticLogging;
            LabDiagnosticLogging = DefaultLabDiagnosticLogging;
            TransportDiagnosticLogging = DefaultTransportDiagnosticLogging;
            TraumaCenterDiagnosticLogging =
                DefaultTraumaCenterDiagnosticLogging;
        }
    }
}
