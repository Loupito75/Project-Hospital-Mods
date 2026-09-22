using HarmonyLib;

namespace HospitalModUpdateChecker
{
    [HarmonyPatch(
        typeof(TitleScreenController),
        nameof(TitleScreenController.Start))]
    internal static class TitleScreenStartPatch
    {
        private static void Postfix(
            TitleScreenController __instance)
        {
            TitleScreenUpdatePanel.Attach(__instance);
        }
    }
}
