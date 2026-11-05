using HarmonyLib;
using UnityEngine;

namespace HospitalFixes.Patches
{
    internal static class TransparencyPreferenceState
    {
        private const string Prefix = "Loupito75.HospitalFixes.Transparency.";
        private const string GameStageKey = Prefix + "GameStage";
        private const string LogisticsWindowsKey = Prefix + "LogisticsWindows";
        private const string LogisticsObjectsHiddenKey = Prefix + "LogisticsObjectsHidden";
        private static bool _loaded;
        private static bool _hasGameStage;
        private static bool _hasLogisticsWindows;
        private static bool _hasLogisticsObjectsHidden;
        private static int _gameStage;
        private static bool _logisticsWindows;
        private static bool _logisticsObjectsHidden;

        private static void Load()
        {
            if (_loaded)
            {
                return;
            }

            _hasGameStage = PlayerPrefs.HasKey(GameStageKey);
            _hasLogisticsWindows =
                PlayerPrefs.HasKey(LogisticsWindowsKey);
            _hasLogisticsObjectsHidden =
                PlayerPrefs.HasKey(LogisticsObjectsHiddenKey);

            if (_hasGameStage)
            {
                _gameStage = Mathf.Clamp(
                    PlayerPrefs.GetInt(GameStageKey),
                    0,
                    2);
            }

            if (_hasLogisticsWindows)
            {
                _logisticsWindows =
                    PlayerPrefs.GetInt(LogisticsWindowsKey) != 0;
            }

            if (_hasLogisticsObjectsHidden)
            {
                _logisticsObjectsHidden =
                    PlayerPrefs.GetInt(
                        LogisticsObjectsHiddenKey) != 0;
            }

            _loaded = true;
        }

        internal static void Restore()
        {
            if (!FixConfig.RememberTransparencyPreferences ||
                SettingsManager.Instance == null ||
                ViewModeController.Instance == null)
            {
                return;
            }

            Load();
            ViewSettings view = SettingsManager.Instance.m_viewSettings;
            if (view == null)
            {
                return;
            }

            ViewModes mode =
                ViewModeController.Instance.m_currentMode;
            bool restored = false;

            if (IsGameMode(mode))
            {
                // A missing HFX key means "never captured". Do not turn that
                // into an implicit false/default and overwrite the live game
                // state on first activation of the mod.
                if (_hasGameStage)
                {
                    view.m_transparentWindows.m_value =
                        _gameStage >= 1;
                    view.m_transparentObjects.m_value =
                        _gameStage == 2;
                    restored = true;
                }
            }
            else if (mode == ViewModes.LOGISTICS)
            {
                if (_hasLogisticsWindows)
                {
                    view.m_transparentWindows.m_value =
                        _logisticsWindows;
                    restored = true;
                }

                if (_hasLogisticsObjectsHidden)
                {
                    view.m_transparentObjectsHidden.m_value =
                        _logisticsObjectsHidden;
                    restored = true;
                }

                // The logistics mode itself controls transparentObjects.
            }
            // Building mode's temporary transparency remains vanilla-managed.

            if (restored && HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.ResultRateLimited(
                    "RememberTransparencyPreferences",
                    "ViewSettings.Load / ViewModeController.SwitchMode",
                    "preferences-restored",
                    "mode=" + mode +
                    " gameStage=" + _gameStage +
                    " logisticsWindows=" + _logisticsWindows +
                    " logisticsObjectsHidden=" + _logisticsObjectsHidden,
                    "RememberTransparencyPreferences|restore|" + mode,
                    3000);
            }
        }

        internal static void Capture(IconButtonController button)
        {
            if (!FixConfig.RememberTransparencyPreferences || button == null ||
                SettingsManager.Instance == null || ViewModeController.Instance == null ||
                MainPanelController.sm_instance == null)
            {
                return;
            }

            Load();
            MainPanelController panel = MainPanelController.sm_instance;
            ViewSettings view = SettingsManager.Instance.m_viewSettings;
            ViewModes mode = ViewModeController.Instance.m_currentMode;

            if (panel.m_transparentObjectsButton != null &&
                button == panel.m_transparentObjectsButton
                    .GetComponent<IconButtonController>())
            {
                if (mode != ViewModes.LOGISTICS)
                {
                    return;
                }

                _logisticsObjectsHidden =
                    view.m_transparentObjectsHidden.m_value;
                _hasLogisticsObjectsHidden = true;
                PlayerPrefs.SetInt(
                    LogisticsObjectsHiddenKey,
                    _logisticsObjectsHidden ? 1 : 0);
            }
            else if (panel.m_transparentWindowsButton != null &&
                button == panel.m_transparentWindowsButton
                    .GetComponent<IconButtonController>())
            {
                if (mode == ViewModes.LOGISTICS)
                {
                    _logisticsWindows =
                        view.m_transparentWindows.m_value;
                    _hasLogisticsWindows = true;
                    PlayerPrefs.SetInt(
                        LogisticsWindowsKey,
                        _logisticsWindows ? 1 : 0);
                }
                else if (IsGameMode(mode))
                {
                    _gameStage =
                        !view.m_transparentWindows.m_value
                            ? 0
                            : (!view.m_transparentObjects.m_value
                                ? 1
                                : 2);
                    _hasGameStage = true;
                    PlayerPrefs.SetInt(
                        GameStageKey,
                        _gameStage);
                }
                else
                {
                    return;
                }
            }
            else
            {
                return;
            }

            // Do not call PlayerPrefs.Save() here. It flushes Unity's
            // entire global PlayerPrefs store, including Vanilla notification,
            // profile and options keys unrelated to HFX. Our three private
            // keys can follow the game's normal persistence lifecycle.
            _loaded = true;

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Result(
                    "RememberTransparencyPreferences",
                    "IconButtonController.OnClick",
                    "preferences-captured",
                    "mode=" + mode +
                    " gameStage=" + _gameStage +
                    " logisticsWindows=" + _logisticsWindows +
                    " logisticsObjectsHidden=" + _logisticsObjectsHidden);
            }
        }

        private static bool IsGameMode(ViewModes mode)
        {
            return mode == ViewModes.GAME_WALLS ||
                mode == ViewModes.GAME_NO_WALLS ||
                mode == ViewModes.DOCTOR_MODE;
        }
    }

    [HarmonyPatch(typeof(ViewSettings), "Load")]
    internal static class TransparencyLoadPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            TransparencyPreferenceState.Restore();
        }
    }

    [HarmonyPatch(typeof(ViewModeController), "SwitchMode")]
    internal static class TransparencyModePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            TransparencyPreferenceState.Restore();
        }
    }

    [HarmonyPatch(typeof(MainPanelController), "Start")]
    internal static class TransparencyUiReadyPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            TransparencyPreferenceState.Restore();
        }
    }

    [HarmonyPatch(typeof(IconButtonController), "OnClick")]
    internal static class TransparencyClickPatch
    {
        [HarmonyPostfix]
        private static void Postfix(IconButtonController __instance)
        {
            TransparencyPreferenceState.Capture(__instance);
        }
    }
}
