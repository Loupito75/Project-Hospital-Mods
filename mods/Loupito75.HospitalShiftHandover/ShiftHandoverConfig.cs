using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HospitalShiftHandover
{
    internal static class ShiftHandoverConfig
    {
        private const string ConfigFileName = "Loupito75.HospitalShiftHandover.Config.xml";

        private const int DefaultPlannedCommuteLeadMinMinutes = 65;
        private const int DefaultPlannedCommuteLeadMaxMinutes = 80;
        private const int DefaultNativeEarlyExtensionMinutes = 15;
        private const bool DefaultDressingEnabled = false;
        private const int DefaultArrivalDressingChancePercent = 70;
        private const int DefaultOutgoingDressingChancePercent = 40;
        private const int DefaultCasualClothesPercent = 65;
        private const bool DefaultAssistedNeedsEnabled = true;
        private const int DefaultAssistedNeedChancePercent = 65;
        private const bool DefaultFreeTimeEnabled = true;
        private const bool DefaultLateReliefEnabled = true;
        private const int DefaultLateReliefMinimumMinutes = 15;
        private const int DefaultLateReliefMaximumMinutes = 30;
        private const bool DefaultDiagnosticsEnabled = false;

        private static readonly Regex XmlCommentRegex = new Regex(
            "<!--.*?-->",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        internal static string ConfigPath { get; private set; }
        internal static int PlannedCommuteLeadMinMinutes { get; private set; }
        internal static int PlannedCommuteLeadMaxMinutes { get; private set; }
        internal static int NativeEarlyExtensionMinutes { get; private set; }
        internal static bool DressingEnabled { get; private set; }
        internal static int ArrivalDressingChancePercent { get; private set; }
        internal static int OutgoingDressingChancePercent { get; private set; }
        internal static int CasualClothesPercent { get; private set; }
        internal static bool AssistedNeedsEnabled { get; private set; }
        internal static int AssistedNeedChancePercent { get; private set; }
        internal static bool FreeTimeEnabled { get; private set; }
        internal static bool LateReliefEnabled { get; private set; }
        internal static int LateReliefMinimumMinutes { get; private set; }
        internal static int LateReliefMaximumMinutes { get; private set; }
        internal static bool DiagnosticsEnabled { get; private set; }

        internal static void Load()
        {
            UseDefaults();

            try
            {
                string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                ConfigPath = Path.Combine(pluginDirectory ?? string.Empty, ConfigFileName);

                if (!File.Exists(ConfigPath))
                {
                    Plugin.Log.LogWarning(
                        ConfigFileName + " was not found. Using the built-in validated defaults.");
                    LogCurrentValues("defaults");
                    return;
                }

                string content = File.ReadAllText(ConfigPath);
                LoadFromText(content);
                LogCurrentValues("xml");
            }
            catch (Exception exception)
            {
                UseDefaults();
                Plugin.Log.LogError(
                    "Could not load " + ConfigFileName + "; using built-in defaults: " +
                    exception.GetType().Name + ": " + exception.Message);
                LogCurrentValues("fallback-defaults");
            }
        }

        private static void LoadFromText(string content)
        {
            string xml = XmlCommentRegex.Replace(content ?? string.Empty, string.Empty);
            if (!ContainsRoot(xml, "HospitalShiftHandover"))
            {
                throw new FormatException(
                    "The XML root element must be <HospitalShiftHandover>...</HospitalShiftHandover>.");
            }

            PlannedCommuteLeadMinMinutes = ReadOptionalInt(
                xml,
                "PlannedCommuteLeadMinMinutes",
                PlannedCommuteLeadMinMinutes,
                15,
                180);

            PlannedCommuteLeadMaxMinutes = ReadOptionalInt(
                xml,
                "PlannedCommuteLeadMaxMinutes",
                PlannedCommuteLeadMaxMinutes,
                15,
                180);

            if (PlannedCommuteLeadMinMinutes > PlannedCommuteLeadMaxMinutes)
            {
                throw new FormatException(
                    "PlannedCommuteLeadMinMinutes cannot be greater than PlannedCommuteLeadMaxMinutes.");
            }

            NativeEarlyExtensionMinutes = ReadOptionalInt(
                xml,
                "NativeEarlyExtensionMinutes",
                NativeEarlyExtensionMinutes,
                0,
                120);

            DressingEnabled = ReadOptionalBool(
                xml,
                "DressingEnabled",
                DressingEnabled);

            ArrivalDressingChancePercent = ReadOptionalInt(
                xml,
                "ArrivalDressingChancePercent",
                ArrivalDressingChancePercent,
                0,
                100);

            OutgoingDressingChancePercent = ReadOptionalInt(
                xml,
                "OutgoingDressingChancePercent",
                OutgoingDressingChancePercent,
                0,
                100);

            CasualClothesPercent = ReadOptionalInt(
                xml,
                "CasualClothesPercent",
                CasualClothesPercent,
                0,
                100);

            AssistedNeedsEnabled = ReadOptionalBool(
                xml,
                "AssistedNeedsEnabled",
                AssistedNeedsEnabled);

            AssistedNeedChancePercent = ReadOptionalInt(
                xml,
                "AssistedNeedChancePercent",
                AssistedNeedChancePercent,
                0,
                100);

            FreeTimeEnabled = ReadOptionalBool(
                xml,
                "FreeTimeEnabled",
                FreeTimeEnabled);

            LateReliefEnabled = ReadOptionalBool(
                xml,
                "LateReliefEnabled",
                LateReliefEnabled);

            LateReliefMinimumMinutes = ReadOptionalInt(
                xml,
                "LateReliefMinimumMinutes",
                LateReliefMinimumMinutes,
                0,
                120);

            LateReliefMaximumMinutes = ReadOptionalInt(
                xml,
                "LateReliefMaximumMinutes",
                LateReliefMaximumMinutes,
                0,
                120);

            if (LateReliefMinimumMinutes > LateReliefMaximumMinutes)
            {
                throw new FormatException(
                    "LateReliefMinimumMinutes cannot be greater than LateReliefMaximumMinutes.");
            }

            DiagnosticsEnabled = ReadOptionalBool(
                xml,
                "DiagnosticsEnabled",
                DiagnosticsEnabled);
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

        private static int ReadOptionalInt(
            string xml,
            string elementName,
            int fallback,
            int minimum,
            int maximum)
        {
            string text = GetElementValue(xml, elementName);
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }

            int value;
            if (!int.TryParse(
                    text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value) ||
                value < minimum ||
                value > maximum)
            {
                throw new FormatException(
                    elementName + " must be an integer from " +
                    minimum.ToString(CultureInfo.InvariantCulture) + " to " +
                    maximum.ToString(CultureInfo.InvariantCulture) + ".");
            }

            return value;
        }

        private static bool ReadOptionalBool(string xml, string elementName, bool fallback)
        {
            string text = GetElementValue(xml, elementName);
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }

            bool value;
            if (!bool.TryParse(text.Trim(), out value))
            {
                throw new FormatException(elementName + " must be true or false.");
            }

            return value;
        }

        private static void UseDefaults()
        {
            PlannedCommuteLeadMinMinutes = DefaultPlannedCommuteLeadMinMinutes;
            PlannedCommuteLeadMaxMinutes = DefaultPlannedCommuteLeadMaxMinutes;
            NativeEarlyExtensionMinutes = DefaultNativeEarlyExtensionMinutes;
            DressingEnabled = DefaultDressingEnabled;
            ArrivalDressingChancePercent = DefaultArrivalDressingChancePercent;
            OutgoingDressingChancePercent = DefaultOutgoingDressingChancePercent;
            CasualClothesPercent = DefaultCasualClothesPercent;
            AssistedNeedsEnabled = DefaultAssistedNeedsEnabled;
            AssistedNeedChancePercent = DefaultAssistedNeedChancePercent;
            FreeTimeEnabled = DefaultFreeTimeEnabled;
            LateReliefEnabled = DefaultLateReliefEnabled;
            LateReliefMinimumMinutes = DefaultLateReliefMinimumMinutes;
            LateReliefMaximumMinutes = DefaultLateReliefMaximumMinutes;
            DiagnosticsEnabled = DefaultDiagnosticsEnabled;
        }

        private static void LogCurrentValues(string source)
        {
            if (Plugin.Log == null)
            {
                return;
            }

            Plugin.Log.LogInfo(
                "Configuration loaded (" + source + "): " +
                "arrival=" + PlannedCommuteLeadMinMinutes.ToString(CultureInfo.InvariantCulture) +
                "-" + PlannedCommuteLeadMaxMinutes.ToString(CultureInfo.InvariantCulture) + "m" +
                ", nativeExtension=" + NativeEarlyExtensionMinutes.ToString(CultureInfo.InvariantCulture) + "m" +
                ", dressing=" + (DressingEnabled ? "on" : "off") +
                ", dressingArrival=" + ArrivalDressingChancePercent.ToString(CultureInfo.InvariantCulture) + "%" +
                ", dressingOutgoing=" + OutgoingDressingChancePercent.ToString(CultureInfo.InvariantCulture) + "%" +
                ", casualClothes=" + CasualClothesPercent.ToString(CultureInfo.InvariantCulture) + "%" +
                ", assistedNeeds=" + (AssistedNeedsEnabled ? "on" : "off") +
                "@" + AssistedNeedChancePercent.ToString(CultureInfo.InvariantCulture) + "%" +
                ", freeTime=" + (FreeTimeEnabled ? "on" : "off") +
                ", lateRelief=" + (LateReliefEnabled ? "on" : "off") +
                "@" + LateReliefMinimumMinutes.ToString(CultureInfo.InvariantCulture) +
                "-" + LateReliefMaximumMinutes.ToString(CultureInfo.InvariantCulture) + "m" +
                ", diagnostics=" + (DiagnosticsEnabled ? "on" : "off") + ".");
        }
    }
}
