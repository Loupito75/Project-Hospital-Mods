using System;
using HarmonyLib;

namespace HospitalTrafficControl.Patches
{
    // Intercept only HTC-owned IDs on the string/parameters overload.
    [HarmonyPatch(
        typeof(StringTable),
        nameof(StringTable.GetLocalizedText),
        new System.Type[] { typeof(string), typeof(string[]) })]
    internal static class StringTablePatch
    {
        private static bool Prefix(
            StringTable __instance,
            string stringID,
            string[] parameters,
            ref string __result)
        {
            string languageCode = __instance != null
                ? __instance.GetCurrentLanguage()
                : null;

            if (string.IsNullOrEmpty(languageCode))
            {
                languageCode = PlayerProfile.Instance.GetCurrentLanguage();
            }

            string localizedText;
            if (!LocalizationManager.TryGetLocalizedText(
                languageCode,
                stringID,
                parameters,
                out localizedText))
            {
                // Let the game resolve every non-HTC string normally.
                return true;
            }

            __result = localizedText;
            return false;
        }
    }
}
