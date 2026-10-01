using HarmonyLib;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.Load))]
    internal static class HtcPlayerProfileLoadPatch
    {
        private static void Postfix()
        {
            NotificationPreferenceMigration.MigrateLegacyPreferences();
        }
    }
}
