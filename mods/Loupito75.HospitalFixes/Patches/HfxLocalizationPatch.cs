using System;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    [HarmonyPatch(
        typeof(StringTable),
        nameof(StringTable.GetLocalizedText),
        new Type[]
        {
            typeof(string),
            typeof(string[])
        })]
    internal static class HfxStringTableLocalizationPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            StringTable __instance,
            string stringID,
            string[] parameters,
            ref string __result)
        {
            string languageCode =
                __instance == null
                    ? null
                    : __instance.GetCurrentLanguage();

            if (string.IsNullOrEmpty(languageCode) &&
                PlayerProfile.Instance != null)
            {
                languageCode =
                    PlayerProfile.Instance.GetCurrentLanguage();
            }

            string localizedText;
            if (!LocalizationManager.TryGetLocalizedText(
                    languageCode,
                    stringID,
                    parameters,
                    out localizedText))
            {
                return true;
            }

            __result =
                localizedText;
            return false;
        }
    }
}
