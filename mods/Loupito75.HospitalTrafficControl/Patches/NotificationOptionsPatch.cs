using System.Collections.Generic;
using HarmonyLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(InGameMenuController), nameof(InGameMenuController.OpenOptions))]
    internal static class NotificationOptionsOpenPatch
    {
        private static void Postfix(InGameMenuController __instance)
        {
            GameObject optionsPanel = __instance?.m_optionsPanel;
            if (optionsPanel == null)
            {
                Plugin.Log?.LogWarning(
                    "HTC notification options OpenOptions hook ran, but m_optionsPanel is null.");
                return;
            }

            OptionsMessagesController[] controllers =
                optionsPanel.GetComponentsInChildren<OptionsMessagesController>(true);

            if (controllers.Length == 0)
            {
                Plugin.Log?.LogWarning(
                    "HTC notification options found no OptionsMessagesController under m_optionsPanel.");
                return;
            }

            foreach (OptionsMessagesController controller in controllers)
            {
                if (controller == null)
                {
                    continue;
                }

                int nativeRows = controller.m_messagePanels != null
                    ? controller.m_messagePanels.Count
                    : -1;

                if (nativeRows > 0)
                {
                    NotificationOptionsInstaller.TryInstallAndRefresh(
                        controller,
                        "OpenOptions");
                }
            }
        }
    }

    [HarmonyPatch(typeof(OptionsMessagesController), nameof(OptionsMessagesController.Update))]
    internal static class NotificationOptionsUpdatePatch
    {
        private static void Postfix(OptionsMessagesController __instance)
        {
            if (__instance == null ||
                __instance.m_messagePanels == null ||
                __instance.m_messagePanels.Count == 0)
            {
                return;
            }

            NotificationOptionsInstaller.TryInstallAndRefresh(
                __instance,
                "OptionsMessagesController.Update");
        }
    }

    internal static class NotificationOptionsInstaller
    {
        private const float RowHeight = 30f;
        private const string RowObjectName = "HTC_NotificationOptionsRow";

        private static readonly HashSet<int> LoggedFailures = new HashSet<int>();
        private static readonly Dictionary<int, HtcNotificationOptionsRowState> Rows =
            new Dictionary<int, HtcNotificationOptionsRowState>();

        internal static bool TryInstallAndRefresh(
            OptionsMessagesController controller,
            string trigger)
        {
            if (controller == null)
            {
                return false;
            }

            int controllerId = controller.GetInstanceID();

            if (Rows.TryGetValue(controllerId, out HtcNotificationOptionsRowState existingState))
            {
                if (existingState != null && existingState.IsAlive)
                {
                    try
                    {
                        existingState.Refresh(force: false);
                    }
                    catch (System.Exception ex)
                    {
                        WarnOnce(
                            controller,
                            $"HTC notification options refresh failed: " +
                            $"{ex.GetType().Name}: {ex.Message}");
                    }

                    return true;
                }

                Rows.Remove(controllerId);
            }

            if (controller.transform.Find(RowObjectName) != null)
            {
                return true;
            }

            if (controller.m_messagePanels == null ||
                controller.m_messagePanels.Count == 0)
            {
                return false;
            }

            return TryInstall(controller, trigger);
        }

        private static bool TryInstall(
            OptionsMessagesController controller,
            string trigger)
        {
            string stage = "source validation";
            GameObject panel = null;

            try
            {
                MessageLevelPanel source =
                    controller.m_messagePanels[controller.m_messagePanels.Count - 1];

                if (source == null ||
                    source.m_panelGameObject == null ||
                    source.m_boolFlagPanelText == null ||
                    source.m_boolFlagPanelButton == null ||
                    source.m_colorLevelText == null)
                {
                    WarnOnce(
                        controller,
                        "HTC notification options source row is incomplete.");
                    return false;
                }

                OptionsMessageLevelController sourceOptions =
                    source.m_panelGameObject
                        .GetComponentInChildren<OptionsMessageLevelController>(true);

                Text sourceNameText =
                    source.m_boolFlagPanelText.GetComponentInChildren<Text>(true);
                Text sourceLevelText =
                    source.m_panelGameObject.GetComponentInChildren<Text>(true);
                Text sourceColorText =
                    source.m_colorLevelText.GetComponentInChildren<Text>(true);

                GameObject sourceColorButton = sourceOptions?.m_buttonMessageLevel;

                if (sourceOptions == null ||
                    sourceNameText == null ||
                    sourceLevelText == null ||
                    sourceColorText == null ||
                    sourceColorButton == null ||
                    sourceNameText == sourceLevelText ||
                    sourceNameText == sourceColorText ||
                    sourceLevelText == sourceColorText)
                {
                    WarnOnce(
                        controller,
                        "HTC notification options could not resolve distinct controls " +
                        "from the initialized native row.");
                    return false;
                }

                Transform sourceRoot = source.m_panelGameObject.transform;

                int[] namePath = GetSiblingPath(sourceRoot, sourceNameText.transform);
                int[] levelPath = GetSiblingPath(sourceRoot, sourceLevelText.transform);
                int[] colorPath = GetSiblingPath(sourceRoot, sourceColorText.transform);
                int[] levelButtonPath = GetSiblingPath(
                    sourceRoot,
                    source.m_boolFlagPanelButton.transform);
                int[] colorButtonPath = GetSiblingPath(
                    sourceRoot,
                    sourceColorButton.transform);
                int[] optionsPath = GetSiblingPath(sourceRoot, sourceOptions.transform);

                if (namePath == null ||
                    levelPath == null ||
                    colorPath == null ||
                    levelButtonPath == null ||
                    colorButtonPath == null ||
                    optionsPath == null)
                {
                    WarnOnce(
                        controller,
                        "HTC notification options could not map the initialized native row hierarchy.");
                    return false;
                }

                stage = "clone creation";
                panel = UnityEngine.Object.Instantiate(source.m_panelGameObject);
                panel.name = RowObjectName;
                panel.SetActive(false);

                stage = "clone parenting";
                panel.transform.SetParent(controller.transform, false);
                panel.transform.SetAsLastSibling();
                panel.transform.localScale = Vector3.one;
                panel.transform.localPosition =
                    source.m_panelGameObject.transform.localPosition +
                    new Vector3(0f, -RowHeight, 0f);

                stage = "localization neutralization";
                foreach (LocalizedTextController localizedText in
                         panel.GetComponentsInChildren<LocalizedTextController>(true))
                {
                    localizedText.enabled = false;
                }

                stage = "clone control binding";
                Text nameText =
                    ResolveBySiblingPath(panel.transform, namePath)?.GetComponent<Text>();
                Text levelText =
                    ResolveBySiblingPath(panel.transform, levelPath)?.GetComponent<Text>();
                Text colorText =
                    ResolveBySiblingPath(panel.transform, colorPath)?.GetComponent<Text>();

                Button levelButton =
                    ResolveBySiblingPath(panel.transform, levelButtonPath)
                        ?.GetComponent<Button>();
                Button colorButton =
                    ResolveBySiblingPath(panel.transform, colorButtonPath)
                        ?.GetComponent<Button>();

                OptionsMessageLevelController options =
                    ResolveBySiblingPath(panel.transform, optionsPath)
                        ?.GetComponent<OptionsMessageLevelController>();

                if (options == null ||
                    nameText == null ||
                    levelText == null ||
                    colorText == null ||
                    levelButton == null ||
                    colorButton == null ||
                    nameText == levelText ||
                    nameText == colorText ||
                    levelText == colorText)
                {
                    WarnOnce(
                        controller,
                        $"HTC notification options clone binding failed: " +
                        $"options={options != null}, name={nameText != null}, " +
                        $"level={levelText != null}, color={colorText != null}, " +
                        $"levelButton={levelButton != null}, colorButton={colorButton != null}.");

                    UnityEngine.Object.Destroy(panel);
                    return false;
                }

                stage = "container binding";
                RectTransform container = controller.GetComponent<RectTransform>();
                if (container == null)
                {
                    WarnOnce(
                        controller,
                        "HTC notification options controller has no RectTransform.");

                    UnityEngine.Object.Destroy(panel);
                    return false;
                }

                stage = "cloned text cleanup";
                foreach (Text text in panel.GetComponentsInChildren<Text>(true))
                {
                    if (text != nameText && text != levelText && text != colorText)
                    {
                        text.text = string.Empty;
                    }
                }

                options.m_messageTextID = LocalizationManager.NoPathTextId;

                stage = "native listener cleanup";
                levelButton.onClick.RemoveAllListeners();
                colorButton.onClick.RemoveAllListeners();

                stage = "HTC row state creation";
                var row = new HtcNotificationOptionsRowState(
                    panel,
                    nameText,
                    levelText,
                    colorText,
                    levelButton,
                    colorButton);

                Rows[controller.GetInstanceID()] = row;

                stage = "HTC listener binding";
                row.BindButtons();

                stage = "initial row refresh";
                row.Refresh(force: true);

                stage = "content resize";
                container.sizeDelta = new Vector2(
                    container.sizeDelta.x,
                    container.sizeDelta.y + RowHeight);

                stage = "row activation";
                panel.SetActive(true);

                LoggedFailures.Remove(controller.GetInstanceID());
                return true;
            }
            catch (System.Exception ex)
            {
                if (panel != null)
                {
                    UnityEngine.Object.Destroy(panel);
                }

                Rows.Remove(controller.GetInstanceID());

                Plugin.Log?.LogError(
                    $"HTC notification options failed during '{stage}' from {trigger}: " +
                    $"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");

                return false;
            }
        }

        private static void WarnOnce(
            OptionsMessagesController controller,
            string message)
        {
            int key = controller != null ? controller.GetInstanceID() : 0;
            if (LoggedFailures.Add(key))
            {
                Plugin.Log?.LogWarning(message);
            }
        }

        private static int[] GetSiblingPath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return null;
            }

            if (root == target)
            {
                return new int[0];
            }

            var path = new List<int>();
            Transform current = target;

            while (current != null && current != root)
            {
                path.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            if (current != root)
            {
                return null;
            }

            path.Reverse();
            return path.ToArray();
        }

        private static Transform ResolveBySiblingPath(Transform root, int[] path)
        {
            if (root == null || path == null)
            {
                return null;
            }

            Transform current = root;

            foreach (int childIndex in path)
            {
                if (childIndex < 0 || childIndex >= current.childCount)
                {
                    return null;
                }

                current = current.GetChild(childIndex);
            }

            return current;
        }
    }

    internal sealed class HtcNotificationOptionsRowState
    {
        private static readonly string[] NoLocalizationParameters = new string[0];

        private readonly GameObject _panel;
        private readonly Text _nameText;
        private readonly Text _levelText;
        private readonly Text _colorText;
        private readonly Button _levelButton;
        private readonly Button _colorButton;

        private string _lastLanguage;
        private NotificationLevel _lastLevel;
        private string _lastColorId;

        internal HtcNotificationOptionsRowState(
            GameObject panel,
            Text nameText,
            Text levelText,
            Text colorText,
            Button levelButton,
            Button colorButton)
        {
            _panel = panel;
            _nameText = nameText;
            _levelText = levelText;
            _colorText = colorText;
            _levelButton = levelButton;
            _colorButton = colorButton;
        }

        internal bool IsAlive =>
            _panel != null &&
            _nameText != null &&
            _levelText != null &&
            _colorText != null &&
            _levelButton != null &&
            _colorButton != null;

        internal void BindButtons()
        {
            _levelButton.onClick.AddListener(CycleLevel);
            _colorButton.onClick.AddListener(CycleColor);
        }

        internal void Refresh(bool force)
        {
            if (!IsAlive || StringTable.GetInstance() == null)
            {
                return;
            }

            string language = StringTable.GetInstance().GetCurrentLanguage();
            NotificationLevel level = NotificationPreferences.Level;
            string colorId = NotificationPreferences.ColorId;

            if (!force &&
                language == _lastLanguage &&
                level == _lastLevel &&
                colorId == _lastColorId)
            {
                return;
            }

            _lastLanguage = language;
            _lastLevel = level;
            _lastColorId = colorId;

            _nameText.text = StringTable.GetInstance().GetLocalizedText(
                LocalizationManager.NoPathTitleId,
                NoLocalizationParameters);

            _levelText.text = StringTable.GetInstance().GetLocalizedText(
                level.ToString(),
                NoLocalizationParameters);

            GameDBNotificationColor color = NotificationPreferences.GetColor();
            if (color != null)
            {
                _colorText.color = new Color(
                    color.ColorText.R / 256f,
                    color.ColorText.G / 256f,
                    color.ColorText.B / 256f);

                _colorText.text = StringTable.GetInstance().GetLocalizedText(
                    color.DatabaseID.ToString(),
                    NoLocalizationParameters);
            }
        }

        private void CycleLevel()
        {
            NotificationLevel current =
                PlayerProfile.Instance.GetNotificationLevel(
                    NotificationPreferences.CategoryId);

            NotificationLevel next = current switch
            {
                NotificationLevel.POPUP => NotificationLevel.LOG,
                NotificationLevel.LOG => NotificationLevel.NONE,
                _ => NotificationLevel.POPUP
            };

            PlayerProfile.Instance.SetNotificationLevel(
                NotificationPreferences.CategoryId,
                next);

            UISoundManager.sm_instance?.PlaySoundEvent("SFX_UI_BEEP");
            Refresh(force: true);
        }

        private void CycleColor()
        {
            NotificationManager.CycleNotificationColor(
                NotificationPreferences.CategoryId);

            UISoundManager.sm_instance?.PlaySoundEvent("SFX_UI_BEEP");
            Refresh(force: true);
        }
    }
}
