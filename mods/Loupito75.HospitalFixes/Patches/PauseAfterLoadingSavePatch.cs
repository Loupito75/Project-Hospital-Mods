using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    // Vanilla persists GameSavePlayerData.m_gameSpeed and restores it during Load().
    // Pause only after a successful user save-load; leave scenarios and save files unchanged.
    [HarmonyPatch(typeof(InGameMenuController), nameof(InGameMenuController.Load))]
    internal static class PauseAfterLoadingSavePatch
    {
        [HarmonyPrefix]
        private static void Prefix(InGameMenuController __instance, string loadFileName, out bool __state)
        {
            // The dialog type is reset to None during a successful load, so capture it first.
            __state = FixConfig.PauseAfterLoadingSave &&
                __instance != null &&
                !string.IsNullOrEmpty(loadFileName) &&
                __instance.m_activeDialogType == DialogType.Load;
        }

        [HarmonyPostfix]
        private static void Postfix(InGameMenuController __instance, bool __state)
        {
            if (!__state || __instance == null ||
                __instance.m_activeDialogType != DialogType.None ||
                MapEditorController.sm_instance == null ||
                MapEditorController.sm_instance.m_unloading ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                Hospital.Instance.m_floors.Count == 0 ||
                Database.sm_loadErrors.Count != 0)
            {
                // Includes failed/cancelled loads, scenarios and unavailable game state.
                return;
            }

            GameTimeController time = GameTimeController.Instance;
            if (time == null || SoundEngine.Instance == null)
            {
                return;
            }

            // Setter(0) preserves m_lastTimeMultiplier populated by vanilla's
            // restoration of a positive saved speed; space resumes that speed.
            time.TimeMultiplier = 0;
            SoundEngine.Instance.SetPaused(paused: true);

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Result(
                    "PauseAfterLoadingSave",
                    "InGameMenuController.Load",
                    "save-load-paused",
                    "loadSucceeded=true paused=true");
            }
        }
    }
}
