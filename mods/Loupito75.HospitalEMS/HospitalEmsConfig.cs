using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HospitalEMS
{
    internal static class HospitalEmsConfig
    {
        private const string ConfigFileName = "Loupito75.HospitalEMS.Config.xml";

        private const int DefaultBasicAssessmentChancePercent = 100;
        private const int DefaultFocusedAssessmentChancePercent = 80;
        private const int DefaultAdvancedDiagnosticsChancePercent = 25;
        private const int DefaultFirstAidChancePercent = 100;
        private const int DefaultAdvancedCareChancePercent = 75;
        private const int DefaultRareCareChancePercent = 10;

        private static readonly Regex XmlCommentRegex = new Regex(
            "<!--.*?-->",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static readonly Regex RootOpenRegex = new Regex(
            @"<\s*HospitalEMS(?:\s[^>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RootCloseRegex = new Regex(
            @"<\s*/\s*HospitalEMS\s*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static string ConfigPath { get; private set; }

        internal static int BasicAssessmentChancePercent { get; private set; }
        internal static int FocusedAssessmentChancePercent { get; private set; }
        internal static int AdvancedDiagnosticsChancePercent { get; private set; }
        internal static int FirstAidChancePercent { get; private set; }
        internal static int AdvancedCareChancePercent { get; private set; }
        internal static int RareCareChancePercent { get; private set; }

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
                        " was not found. Using built-in Hospital EMS defaults.");
                    return;
                }

                LoadFromText(File.ReadAllText(ConfigPath));
            }
            catch (Exception exception)
            {
                UseDefaults();
                Plugin.Log.LogWarning(
                    "Could not load " + ConfigFileName +
                    "; using built-in Hospital EMS defaults: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static bool ShouldPerformBasicAssessment()
        {
            return RollChance(BasicAssessmentChancePercent);
        }

        internal static bool ShouldPerformFocusedAssessment()
        {
            return RollChance(FocusedAssessmentChancePercent);
        }

        internal static bool ShouldPerformAdvancedDiagnostics()
        {
            return RollChance(AdvancedDiagnosticsChancePercent);
        }

        internal static bool ShouldPerformFirstAid()
        {
            return RollChance(FirstAidChancePercent);
        }

        internal static bool ShouldPerformAdvancedCare()
        {
            return RollChance(AdvancedCareChancePercent);
        }

        internal static bool ShouldPerformRareCare()
        {
            return RollChance(RareCareChancePercent);
        }

        private static bool RollChance(int chancePercent)
        {
            if (chancePercent <= 0)
            {
                return false;
            }

            if (chancePercent >= 100)
            {
                return true;
            }

            return UnityEngine.Random.Range(0, 100) < chancePercent;
        }

        private static void LoadFromText(string content)
        {
            string xml = XmlCommentRegex.Replace(
                content ?? string.Empty,
                string.Empty);

            if (!RootOpenRegex.IsMatch(xml) || !RootCloseRegex.IsMatch(xml))
            {
                throw new FormatException(
                    "The XML root element must be <HospitalEMS>...</HospitalEMS>.");
            }

            string examinationContent = GetSectionContent(
                xml,
                "PrehospitalExaminations");
            string careContent = GetSectionContent(
                xml,
                "PrehospitalCare");

            BasicAssessmentChancePercent = ParseChance(
                examinationContent,
                "BasicAssessmentChancePercent");
            FocusedAssessmentChancePercent = ParseChance(
                examinationContent,
                "FocusedAssessmentChancePercent");
            AdvancedDiagnosticsChancePercent = ParseChance(
                examinationContent,
                "AdvancedDiagnosticsChancePercent");

            FirstAidChancePercent = ParseChance(
                careContent,
                "FirstAidChancePercent");
            AdvancedCareChancePercent = ParseChance(
                careContent,
                "AdvancedCareChancePercent");
            RareCareChancePercent = ParseChance(
                careContent,
                "RareCareChancePercent");
        }

        private static string GetSectionContent(
            string xml,
            string sectionName)
        {
            string escapedName = Regex.Escape(sectionName);
            Match match = Regex.Match(
                xml ?? string.Empty,
                @"<\s*" + escapedName +
                @"(?:\s[^>]*)?>\s*(?<content>.*?)\s*<\s*/\s*" +
                escapedName + @"\s*>",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline |
                RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                throw new FormatException(
                    "The XML must contain <" + sectionName +
                    ">...</" + sectionName + "> under <HospitalEMS>.");
            }

            return match.Groups["content"].Value;
        }

        private static int ParseChance(
            string content,
            string elementName)
        {
            string value = GetElementValue(content, elementName);
            if (string.IsNullOrEmpty(value))
            {
                throw new FormatException(
                    "Missing <" + elementName + ">0..100</" +
                    elementName + ">.");
            }

            int parsed;
            if (!int.TryParse(value.Trim(), out parsed) ||
                parsed < 0 ||
                parsed > 100)
            {
                throw new FormatException(
                    elementName + " must be an integer from 0 to 100.");
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

            return match.Success
                ? match.Groups["value"].Value
                : null;
        }

        private static void UseDefaults()
        {
            BasicAssessmentChancePercent =
                DefaultBasicAssessmentChancePercent;
            FocusedAssessmentChancePercent =
                DefaultFocusedAssessmentChancePercent;
            AdvancedDiagnosticsChancePercent =
                DefaultAdvancedDiagnosticsChancePercent;
            FirstAidChancePercent =
                DefaultFirstAidChancePercent;
            AdvancedCareChancePercent =
                DefaultAdvancedCareChancePercent;
            RareCareChancePercent =
                DefaultRareCareChancePercent;
        }
    }
}
