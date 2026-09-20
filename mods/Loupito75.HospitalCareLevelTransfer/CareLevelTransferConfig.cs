using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HospitalCareLevelTransfer
{
    internal static class CareLevelTransferConfig
    {
        private const string ConfigFileName = "Loupito75.HospitalCareLevelTransfer.Config.xml";
        private const int DefaultHduToRegularChancePercent = 75;

        private static readonly Regex XmlCommentRegex = new Regex(
            "<!--.*?-->",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static readonly Regex RootOpenRegex = new Regex(
            @"<\s*HospitalCareLevelTransfer(?:\s[^>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RootCloseRegex = new Regex(
            @"<\s*/\s*HospitalCareLevelTransfer\s*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex AutomationOpenRegex = new Regex(
            @"<\s*Automation(?:\s[^>]*)?>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex AutomationCloseRegex = new Regex(
            @"<\s*/\s*Automation\s*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static int HduToRegularChancePercent { get; private set; }

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
                        ConfigFileName + " was not found. Using the built-in 75% automation default.");
                    return;
                }

                LoadFromText(File.ReadAllText(configPath));
            }
            catch (Exception exception)
            {
                UseDefaults();
                Plugin.Log?.LogWarning(
                    "Could not load " + ConfigFileName +
                    "; using the built-in 75% automation default: " +
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static bool ShouldAttemptAutomaticTransfer()
        {
            if (HduToRegularChancePercent <= 0)
            {
                return false;
            }

            if (HduToRegularChancePercent >= 100)
            {
                return true;
            }

            return UnityEngine.Random.Range(0, 100) < HduToRegularChancePercent;
        }

        private static void LoadFromText(string content)
        {
            string xml = XmlCommentRegex.Replace(content ?? string.Empty, string.Empty);

            if (!RootOpenRegex.IsMatch(xml) || !RootCloseRegex.IsMatch(xml))
            {
                throw new FormatException(
                    "The XML root element must be <HospitalCareLevelTransfer>...</HospitalCareLevelTransfer>.");
            }

            Match automationOpenMatch = AutomationOpenRegex.Match(xml);
            Match automationCloseMatch = AutomationCloseRegex.Match(xml);
            if (!automationOpenMatch.Success ||
                !automationCloseMatch.Success ||
                automationCloseMatch.Index <= automationOpenMatch.Index)
            {
                throw new FormatException(
                    "The XML must contain an <Automation>...</Automation> element under <HospitalCareLevelTransfer>.");
            }

            string automationContent = xml.Substring(
                automationOpenMatch.Index + automationOpenMatch.Length,
                automationCloseMatch.Index - (automationOpenMatch.Index + automationOpenMatch.Length));

            string value = GetElementValue(automationContent, "HduToRegularChancePercent");
            if (string.IsNullOrEmpty(value))
            {
                throw new FormatException(
                    "The <Automation> element must contain <HduToRegularChancePercent>0..100</HduToRegularChancePercent>.");
            }

            int parsed;
            if (!int.TryParse(value.Trim(), out parsed) || parsed < 0 || parsed > 100)
            {
                throw new FormatException(
                    "HduToRegularChancePercent must be an integer from 0 to 100.");
            }

            HduToRegularChancePercent = parsed;
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

        private static void UseDefaults()
        {
            HduToRegularChancePercent = DefaultHduToRegularChancePercent;
        }
    }
}
