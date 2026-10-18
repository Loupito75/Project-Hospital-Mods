using System;
using System.Globalization;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalModUpdateChecker
{
    // Modal editor displayed only on the title screen. No settings are applied live.
    internal static class ConfigEditorPanel
    {
        private const float Width = 700f;
        private const float Height = 680f;
        private static GameObject _overlay;
        private static Text _status;
        private static Button _saveButton;
        private static RectTransform _windowRect;
        private static GameObject _optionTooltip;
        private static Text _optionTooltipText;
        private static bool _closeConfirmationPending;
        private static ConfigEditSession _session;
        private static Text _source;

        internal static bool IsOpen
        {
            get { return _overlay != null; }
        }

        internal static void Open(
            PluginInfo plugin, Transform canvas, Text textTemplate)
        {
            Close();
            if (plugin == null || canvas == null || textTemplate == null)
                return;

            string error;
            bool canEdit = ConfigEditorData.TryOpen(
                plugin, out _session, out error);
            bool noEditableSettings =
                !canEdit &&
                string.Equals(
                    error,
                    ConfigEditorData.NoEditableSettingsError,
                    StringComparison.Ordinal);
            if (!canEdit)
            {
                if (noEditableSettings)
                {
                    Plugin.Log.LogInfo(
                        "HMUC plugin settings contain no editable leaf settings: " +
                        plugin.Metadata.Name + ".");
                }
                else
                {
                    Plugin.Log.LogWarning(
                        "HMUC could not open plugin settings: " + error);
                }

                _session = new ConfigEditSession();
                _session.PluginName = plugin.Metadata.Name;
            }

            _source = textTemplate;
            _overlay = new GameObject("HMUC.ConfigOverlay");
            _overlay.transform.SetParent(canvas, false);
            RectTransform backdrop = _overlay.AddComponent<RectTransform>();
            Stretch(backdrop);
            Image shadow = _overlay.AddComponent<Image>();
            shadow.color = new Color(0f, 0f, 0f, .70f);
            shadow.raycastTarget = true;
            _overlay.transform.SetAsLastSibling();

            GameObject window = NewBox(
                "ConfigWindow", _overlay.transform,
                new Color(.14f, .17f, .20f, .99f));
            RectTransform panel = window.GetComponent<RectTransform>();
            _windowRect = panel;
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
            panel.pivot = new Vector2(.5f, .5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(Width, Height);

            GameObject header = NewBox(
                "Header", window.transform, HeadingColor());
            RectTransform head = header.GetComponent<RectTransform>();
            TopRect(head, -8f, 44f, 8f, 8f);

            Text title = NewText(
                "Title", header.transform, _session.PluginName, 20,
                TextAnchor.MiddleLeft);
            RectTransform titleRect = title.rectTransform;
            Stretch(titleRect);
            titleRect.offsetMin = new Vector2(14f, 0f);
            titleRect.offsetMax = new Vector2(-50f, 0f);
            MakeButton("Close", header.transform, "X",
                new Vector2(32f, 30f), new Vector2(-7f, -7f),
                delegate { RequestClose(); });

            GameObject infoPanel = NewBox(
                "InfoPanel", window.transform, new Color(.06f, .08f, .10f, .5f));
            RectTransform infoRect = infoPanel.GetComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0f, 0f);
            infoRect.anchorMax = new Vector2(1f, 0f);
            infoRect.pivot = new Vector2(.5f, 0f);
            infoRect.anchoredPosition = new Vector2(0f, 50f);
            infoRect.sizeDelta = new Vector2(-24f, 62f);

            _status = NewText(
                "Status", infoPanel.transform, Loc("hover_help"), 14,
                TextAnchor.MiddleLeft);
            RectTransform statusRect = _status.rectTransform;
            Stretch(statusRect);
            statusRect.offsetMin = new Vector2(10f, 5f);
            statusRect.offsetMax = new Vector2(-10f, -5f);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _status.verticalOverflow = VerticalWrapMode.Truncate;

            if (canEdit)
            {
                _saveButton = MakeButton("Save", window.transform, Loc("save"),
                    new Vector2(105f, 30f), new Vector2(-12f, 9f),
                    delegate { Save(); }, false);
                _saveButton.interactable = false;
            }
            else
            {
                if (noEditableSettings)
                {
                    _status.color = new Color(.72f, .85f, 1f, 1f);
                    _status.text = Loc("info") + ": " +
                        Loc("no_editable");
                }
                else
                {
                    _status.color = new Color(1f, .48f, .48f, 1f);
                    _status.text = Loc("error") + ": " + Loc("processing_error");
                }
            }
            MakeButton("Close", window.transform, Loc("close"),
                new Vector2(105f, 30f), new Vector2(-125f, 9f),
                delegate { RequestClose(); }, false);

            GameObject viewport = new GameObject("SettingsViewport");
            viewport.transform.SetParent(window.transform, false);
            RectTransform viewportRect =
                viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = new Vector2(0f, 0f);
            viewportRect.anchorMax = new Vector2(1f, 1f);
            viewportRect.offsetMin = new Vector2(17f, 121f);
            viewportRect.offsetMax = new Vector2(-39f, -62f);
            viewport.AddComponent<RectMask2D>();
            Image hit = viewport.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            GameObject contents = new GameObject("Rows");
            contents.transform.SetParent(viewport.transform, false);
            RectTransform rows = contents.AddComponent<RectTransform>();
            rows.anchorMin = new Vector2(0f, 1f);
            rows.anchorMax = new Vector2(1f, 1f);
            rows.pivot = new Vector2(.5f, 1f);
            rows.anchoredPosition = Vector2.zero;
            const float normalRowHeight = 69f;
            const float compactRowHeight = 40f;
            const float multilineRowHeight = 150f;
            const float sectionHeight = 30f;

            ScrollRect scrolling = viewport.AddComponent<ScrollRect>();
            scrolling.viewport = viewportRect;
            scrolling.content = rows;
            scrolling.horizontal = false;
            scrolling.vertical = true;
            scrolling.movementType = ScrollRect.MovementType.Clamped;
            scrolling.scrollSensitivity = 22f;

            GameObject scrollbarRoot = NewBox(
                "SettingsScrollbar", window.transform,
                new Color(.08f, .10f, .13f, .92f));
            RectTransform scrollbarRect =
                scrollbarRoot.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, .5f);
            scrollbarRect.offsetMin = new Vector2(-34f, 121f);
            scrollbarRect.offsetMax = new Vector2(-17f, -62f);

            GameObject slidingArea =
                new GameObject("SlidingArea");
            slidingArea.transform.SetParent(
                scrollbarRoot.transform, false);
            RectTransform slidingRect =
                slidingArea.AddComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.pivot = new Vector2(.5f, .5f);
            slidingRect.anchoredPosition = Vector2.zero;
            slidingRect.sizeDelta = new Vector2(-4f, -4f);

            GameObject handle = NewBox(
                "Handle", slidingArea.transform, HeadingColor());
            RectTransform handleRect =
                handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.pivot = new Vector2(.5f, .5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = Vector2.zero;

            Scrollbar scrollbar =
                scrollbarRoot.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.handleRect = handleRect;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.numberOfSteps = 0;
            scrolling.verticalScrollbar = scrollbar;
            scrolling.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHide;

            float top = 0f;
            string currentSection = null;
            for (int i = 0; i < _session.Options.Count; i++)
            {
                ConfigEditOption option = _session.Options[i];
                string section = option.Section ?? string.Empty;
                if (!string.Equals(
                        currentSection,
                        section,
                        StringComparison.Ordinal))
                {
                    currentSection = section;
                    if (section.Length > 0)
                    {
                        AddSectionHeader(rows, section, top, sectionHeight);
                        top += sectionHeight;
                    }
                }

                float rowHeight =
                    option.Control == "multiline-list"
                        ? multilineRowHeight
                        : (option.CompactRow
                            ? compactRowHeight
                            : normalRowHeight);
                AddRow(rows, option, top, rowHeight);
                top += rowHeight;
            }

            rows.sizeDelta = new Vector2(
                0f, Math.Max(8f, top + 8f));

            scrolling.verticalNormalizedPosition = 1f;
            UpdateSaveButton();
        }

        private static void AddSectionHeader(
            Transform rows, string section, float top, float height)
        {
            GameObject header = NewBox(
                "Section_" + section.Replace(" > ", "_"),
                rows, HeadingColor());
            RectTransform rect = header.GetComponent<RectTransform>();
            TopRect(rect, -top, height - 4f, 0f, 0f);

            Text text = NewText(
                "SectionName", header.transform,
                TruncateLabel(section, 46), 15,
                TextAnchor.MiddleLeft);
            RectTransform textRect = text.rectTransform;
            Stretch(textRect);
            textRect.offsetMin = new Vector2(10f, 0f);
            textRect.offsetMax = new Vector2(-10f, 0f);
        }

        private static void AddRow(
            Transform rows, ConfigEditOption option,
            float top, float rowHeight)
        {
            GameObject row = NewBox(
                "Setting_" + option.Name, rows,
                new Color(.22f, .25f, .28f, .7f));
            RectTransform rect = row.GetComponent<RectTransform>();
            TopRect(
                rect, -top,
                option.CompactRow ? rowHeight : rowHeight - 5f,
                0f, 0f);

            string fullLabel = option.Label ?? option.Name ?? string.Empty;
            bool multiline =
                option.Control == "multiline-list";
            string visibleLabel = TruncateLabel(
                fullLabel,
                multiline ? 26 :
                (option.CompactRow ? 42 : 34));
            Text name = NewText(
                "Name", row.transform, visibleLabel,
                multiline ? 15 :
                (option.CompactRow ? 14 : 16),
                TextAnchor.MiddleLeft);
            name.supportRichText = false;
            name.raycastTarget = true;
            RectTransform nameRect = name.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(
                multiline ? .31f : .67f, 1f);
            nameRect.offsetMin = new Vector2(10f, 2f);
            nameRect.offsetMax = new Vector2(-6f, -2f);

            string tooltipDescription =
                option.Description;
            if (multiline &&
                string.IsNullOrEmpty(tooltipDescription))
                tooltipDescription = Loc("list_help");

            if (!string.IsNullOrEmpty(tooltipDescription) ||
                !string.Equals(fullLabel, visibleLabel, StringComparison.Ordinal))
            {
                string tooltip = fullLabel;
                if (!string.IsNullOrEmpty(tooltipDescription))
                    tooltip += "\n\n" + tooltipDescription.Trim();

                name.gameObject.AddComponent<HoverTooltipDelay>();
                ConfigOptionTooltipHover hover =
                    name.gameObject.AddComponent<ConfigOptionTooltipHover>();
                hover.Initialize(tooltip);
            }

            if (multiline)
            {
                AddMultilineList(
                    row.transform, option, rowHeight);
                return;
            }

            if (option.Control == "checkbox")
            {
                GameObject button = MakeValueButton(row.transform);
                Text text = NewText(
                    "Check", button.transform, "",
                    option.CompactRow ? 18 : 20,
                    TextAnchor.MiddleCenter);
                Stretch(text.rectTransform);
                bool enabled = string.Equals(
                    option.Value, "true", StringComparison.OrdinalIgnoreCase);
                text.text = enabled ? "[X]" : "[ ]";
                button.GetComponent<Button>().onClick.AddListener(delegate
                {
                    enabled = !enabled;
                    option.Value = enabled ? "true" : "false";
                    text.text = enabled ? "[X]" : "[ ]";
                    UpdateSaveButton();
                });
                return;
            }

            if (option.Control == "select")
            {
                GameObject button = MakeValueButton(row.transform);
                Text text = NewText(
                    "Selection", button.transform, option.Value + "  >",
                    15, TextAnchor.MiddleCenter);
                Stretch(text.rectTransform);
                button.GetComponent<Button>().onClick.AddListener(delegate
                {
                    int index = Array.IndexOf(option.Choices, option.Value);
                    option.Value = option.Choices[
                        (index + 1) % option.Choices.Length];
                    text.text = option.Value + "  >";
                    UpdateSaveButton();
                });
                return;
            }

            if (option.Control == "slider")
            {
                AddSlider(row.transform, option);
                return;
            }

            GameObject inputRoot = NewBox(
                "Value", row.transform, new Color(.10f, .13f, .17f, 1f));
            RectTransform inputRect = inputRoot.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(.72f, .5f);
            inputRect.anchorMax = new Vector2(.97f, .5f);
            inputRect.pivot = new Vector2(.5f, .5f);
            inputRect.anchoredPosition = Vector2.zero;
            inputRect.sizeDelta = new Vector2(0f, 36f);
            InputField input = inputRoot.AddComponent<InputField>();
            input.targetGraphic = inputRoot.GetComponent<Image>();
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 2048;
            Text textField = NewText(
                "Value", inputRoot.transform, option.Value, 15,
                TextAnchor.MiddleLeft);
            textField.supportRichText = false;
            Stretch(textField.rectTransform);
            textField.rectTransform.offsetMin = new Vector2(8f, 0f);
            textField.rectTransform.offsetMax = new Vector2(-8f, 0f);
            input.textComponent = textField;
            input.text = option.Value;
            if (option.Control == "number")
                input.contentType = InputField.ContentType.DecimalNumber;
            input.onValueChanged.AddListener(delegate(string value)
            {
                option.Value = value;
                UpdateSaveButton();
            });
        }

        private static void AddMultilineList(
            Transform row,
            ConfigEditOption option,
            float rowHeight)
        {
            GameObject inputRoot = NewBox(
                "ValueList", row,
                new Color(.10f, .13f, .17f, 1f));
            RectTransform inputRect =
                inputRoot.GetComponent<RectTransform>();
            inputRect.anchorMin =
                new Vector2(.34f, .5f);
            inputRect.anchorMax =
                new Vector2(.97f, .5f);
            inputRect.pivot =
                new Vector2(.5f, .5f);
            inputRect.anchoredPosition = Vector2.zero;
            inputRect.sizeDelta =
                new Vector2(0f, rowHeight - 18f);

            inputRoot.AddComponent<RectMask2D>();

            InputField input =
                inputRoot.AddComponent<InputField>();
            input.targetGraphic =
                inputRoot.GetComponent<Image>();
            input.lineType =
                InputField.LineType.MultiLineNewline;
            input.characterLimit = 16384;

            Text textField = NewText(
                "Value", inputRoot.transform,
                option.Value, 13,
                TextAnchor.UpperLeft);
            textField.supportRichText = false;
            textField.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            textField.verticalOverflow =
                VerticalWrapMode.Overflow;
            Stretch(textField.rectTransform);
            textField.rectTransform.offsetMin =
                new Vector2(8f, 6f);
            textField.rectTransform.offsetMax =
                new Vector2(-8f, -6f);

            input.textComponent = textField;
            input.text = option.Value ?? string.Empty;
            input.onValueChanged.AddListener(
                delegate(string value)
                {
                    option.Value = value;
                    UpdateSaveButton();
                });
        }

        private static void AddSlider(
            Transform row, ConfigEditOption option)
        {
            GameObject root = NewBox(
                "Slider", row, new Color(.20f, .22f, .25f, .9f));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.72f, .5f);
            rect.anchorMax = new Vector2(.87f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 6f);

            GameObject fill = NewBox(
                "Fill", root.transform, new Color(.60f, .76f, .88f, 1f));
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            Stretch(fillRect);
            GameObject handle = NewBox(
                "Handle", root.transform, Color.white);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = handleRect.anchorMax =
                new Vector2(0f, .5f);
            handleRect.pivot = new Vector2(.5f, .5f);
            handleRect.sizeDelta = new Vector2(10f, 21f);

            Slider slider = root.AddComponent<Slider>();
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = (float)option.Min;
            slider.maxValue = (float)option.Max;
            slider.wholeNumbers = false;

            GameObject valueRoot = new GameObject("Number");
            valueRoot.transform.SetParent(row, false);
            RectTransform vr = valueRoot.AddComponent<RectTransform>();
            vr.anchorMin = new Vector2(.88f, 0f);
            vr.anchorMax = new Vector2(.99f, 1f);
            vr.offsetMin = vr.offsetMax = Vector2.zero;

            Image valueHitArea = valueRoot.AddComponent<Image>();
            valueHitArea.color = new Color(0f, 0f, 0f, 0f);

            InputField valueInput = valueRoot.AddComponent<InputField>();
            valueInput.targetGraphic = valueHitArea;
            valueInput.lineType = InputField.LineType.SingleLine;
            valueInput.contentType = InputField.ContentType.DecimalNumber;

            Text valueLabel = NewText(
                "Value", valueRoot.transform, option.Value, 15,
                TextAnchor.MiddleCenter);
            Stretch(valueLabel.rectTransform);
            valueLabel.supportRichText = false;
            valueInput.textComponent = valueLabel;
            valueInput.text = option.Value;

            decimal initial;
            if (!decimal.TryParse(option.Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out initial))
                initial = option.Min;
            slider.value = (float)initial;
            slider.onValueChanged.AddListener(delegate(float v)
            {
                decimal clamped = Math.Max(option.Min,
                    Math.Min(option.Max, (decimal)v));
                decimal steps = Math.Round(
                    (clamped - option.Min) / option.Step,
                    0, MidpointRounding.AwayFromZero);
                decimal rounded = Math.Min(option.Max,
                    option.Min + steps * option.Step);
                option.Value = rounded.ToString(CultureInfo.InvariantCulture);
                valueInput.text = option.Value;
                UpdateSaveButton();
            });

            valueInput.onEndEdit.AddListener(delegate(string value)
            {
                decimal entered;
                if (!decimal.TryParse(value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out entered))
                {
                    valueInput.text = option.Value;
                    return;
                }

                decimal clamped = Math.Max(option.Min,
                    Math.Min(option.Max, entered));
                decimal steps = Math.Round(
                    (clamped - option.Min) / option.Step,
                    0, MidpointRounding.AwayFromZero);
                decimal rounded = Math.Min(option.Max,
                    option.Min + steps * option.Step);
                slider.value = (float)rounded;
                option.Value = rounded.ToString(CultureInfo.InvariantCulture);
                valueInput.text = option.Value;
                UpdateSaveButton();
            });
        }

        private static void Save()
        {
            if (_session == null)
                return;
            string error;
            if (ConfigEditorData.Stage(_session, out error))
            {
                Close();
            }
            else
            {
                Plugin.Log.LogWarning(
                    "HMUC could not stage plugin settings for " +
                    (_session.PluginName ?? "unknown plugin") +
                    ": " +
                    error);
                _status.color = new Color(1f, .48f, .48f, 1f);
                _status.text = Loc("error") + ": " +
                    Loc("processing_error");
            }
        }

        private static void RequestClose()
        {
            if (!_closeConfirmationPending && HasUnsavedChanges())
            {
                _closeConfirmationPending = true;
                if (_status != null)
                {
                    _status.color = new Color(1f, .72f, .28f, 1f);
                    _status.text = Loc("discard_confirm");
                }
                return;
            }

            Close();
        }

        internal static void Close()
        {
            if (_overlay != null)
                UnityEngine.Object.Destroy(_overlay);
            _overlay = null;
            _source = null;
            _session = null;
            _status = null;
            _saveButton = null;
            _windowRect = null;
            _optionTooltip = null;
            _optionTooltipText = null;
            _closeConfirmationPending = false;
        }

        private static string TruncateLabel(
            string value, int maximumCharacters)
        {
            string text = value ?? string.Empty;
            if (maximumCharacters < 4 ||
                text.Length <= maximumCharacters)
                return text;

            return text.Substring(0, maximumCharacters - 3).TrimEnd() + "...";
        }

        internal static void ShowOptionTooltip(string value)
        {
            if (_overlay == null || _windowRect == null ||
                _source == null || string.IsNullOrEmpty(value))
            {
                HideOptionTooltip();
                return;
            }

            if (_optionTooltip == null)
            {
                _optionTooltip = NewBox(
                    "OptionTooltip", _windowRect,
                    new Color(.09f, .11f, .14f, .98f));
                RectTransform root =
                    _optionTooltip.GetComponent<RectTransform>();
                root.anchorMin = root.anchorMax =
                    new Vector2(.5f, .5f);
                root.pivot = new Vector2(.5f, .5f);
                root.sizeDelta = new Vector2(360f, 48f);

                _optionTooltipText = NewText(
                    "TooltipText", _optionTooltip.transform, "", 14,
                    TextAnchor.MiddleLeft);
                RectTransform textRect =
                    _optionTooltipText.rectTransform;
                Stretch(textRect);
                textRect.offsetMin = new Vector2(10f, 7f);
                textRect.offsetMax = new Vector2(-10f, -7f);
                _optionTooltipText.horizontalOverflow =
                    HorizontalWrapMode.Wrap;
                _optionTooltipText.verticalOverflow =
                    VerticalWrapMode.Truncate;
                _optionTooltipText.raycastTarget = false;
            }

            _optionTooltipText.text = value;
            Canvas.ForceUpdateCanvases();

            RectTransform tooltipRect =
                _optionTooltip.GetComponent<RectTransform>();
            float width = Mathf.Clamp(
                _optionTooltipText.preferredWidth + 28f,
                220f, Width - 32f);
            tooltipRect.sizeDelta = new Vector2(width, 48f);
            Canvas.ForceUpdateCanvases();
            float height = Mathf.Clamp(
                _optionTooltipText.preferredHeight + 20f,
                42f, Height - 90f);
            tooltipRect.sizeDelta = new Vector2(width, height);

            Canvas canvas = _windowRect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null &&
                canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;

            Vector2 pointer;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _windowRect, Input.mousePosition, camera, out pointer))
            {
                float halfWidth = _windowRect.rect.width * .5f;
                float halfHeight = _windowRect.rect.height * .5f;
                float tooltipHalfWidth = tooltipRect.rect.width * .5f;
                float tooltipHalfHeight = tooltipRect.rect.height * .5f;

                tooltipRect.anchoredPosition = new Vector2(
                    Mathf.Clamp(
                        pointer.x,
                        -halfWidth + tooltipHalfWidth + 8f,
                        halfWidth - tooltipHalfWidth - 8f),
                    Mathf.Clamp(
                        pointer.y + tooltipHalfHeight + 18f,
                        -halfHeight + tooltipHalfHeight + 8f,
                        halfHeight - tooltipHalfHeight - 8f));
            }

            _optionTooltip.SetActive(true);
            _optionTooltip.transform.SetAsLastSibling();
        }

        internal static void HideOptionTooltip()
        {
            if (_optionTooltip != null)
                _optionTooltip.SetActive(false);
        }

        private static bool HasUnsavedChanges()
        {
            if (_session == null)
                return false;

            for (int i = 0; i < _session.Options.Count; i++)
            {
                ConfigEditOption option = _session.Options[i];
                if (!string.Equals(
                        option.Value ?? string.Empty,
                        option.OriginalValue ?? string.Empty,
                        StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void UpdateSaveButton()
        {
            if (_saveButton == null)
                return;

            bool changed = HasUnsavedChanges();

            if (changed && _closeConfirmationPending)
                _closeConfirmationPending = false;

            _saveButton.interactable = changed;

            if (_status != null)
            {
                _status.color = Color.white;
                _status.text = changed
                    ? Loc("restart")
                    : Loc("hover_help");
            }
        }

        private static Color HeadingColor()
        {
            if (UISettings.Instance != null)
                return UISettings.Instance.THEME_COLOR_PANEL_HEADING;
            return new Color(.25f, .29f, .34f, 1f);
        }

        private static GameObject NewBox(
            string name, Transform parent, Color color)
        {
            GameObject result = new GameObject(name);
            result.transform.SetParent(parent, false);
            result.AddComponent<RectTransform>();
            Image image = result.AddComponent<Image>();
            image.color = color;
            return result;
        }

        private static Text NewText(
            string name, Transform parent, string text,
            int fontSize, TextAnchor alignment)
        {
            GameObject go = UnityEngine.Object.Instantiate(_source.gameObject);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.SetActive(true);
            LocalizedTextController localized =
                go.GetComponent<LocalizedTextController>();
            if (localized != null)
                localized.enabled = false;
            Text label = go.GetComponent<Text>();
            label.text = text ?? "";
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.supportRichText = false;
            return label;
        }

        private static GameObject MakeValueButton(Transform parent)
        {
            GameObject root = NewBox(
                "Value", parent, new Color(.10f, .13f, .17f, 1f));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.72f, .5f);
            rect.anchorMax = new Vector2(.97f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 36f);
            Button button = root.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            return root;
        }

        private static Button MakeButton(
            string name, Transform parent, string label,
            Vector2 size, Vector2 offset, Action action)
        {
            return MakeButton(name, parent, label, size, offset, action, true);
        }

        private static Button MakeButton(
            string name, Transform parent, string label,
            Vector2 size, Vector2 offset, Action action, bool top)
        {
            GameObject root = NewBox(name, parent, HeadingColor());
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax =
                top ? new Vector2(1f, 1f) : new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, top ? 1f : 0f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            Button button = root.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            button.onClick.AddListener(delegate { action(); });
            Text text = NewText(
                "Text", root.transform, label, 15,
                TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void TopRect(
            RectTransform rect, float top, float height,
            float left, float right)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2((left - right) / 2f, top);
            rect.sizeDelta = new Vector2(-left - right, height);
        }

        private static string Loc(string key)
        {
            string stringId = null;

            switch (key)
            {
                case "hover_help":
                    stringId = UpdatePanelLocalization.ConfigHoverHelpId;
                    break;
                case "list_help":
                    stringId = UpdatePanelLocalization.ConfigListHelpId;
                    break;
                case "restart":
                    stringId = UpdatePanelLocalization.ConfigRestartId;
                    break;
                case "info":
                    stringId = UpdatePanelLocalization.ConfigInfoId;
                    break;
                case "save":
                    stringId = UpdatePanelLocalization.ConfigSaveId;
                    break;
                case "close":
                    stringId = UpdatePanelLocalization.ConfigCloseId;
                    break;
                case "discard_confirm":
                    stringId = UpdatePanelLocalization.ConfigDiscardConfirmId;
                    break;
                case "error":
                    stringId = UpdatePanelLocalization.ConfigErrorId;
                    break;
                case "no_editable":
                    stringId = UpdatePanelLocalization.ConfigNoEditableId;
                    break;
                case "processing_error":
                    stringId = UpdatePanelLocalization.ConfigProcessingErrorId;
                    break;
            }

            return stringId == null
                ? key
                : UpdatePanelLocalization.Get(stringId);
        }

    }

    internal sealed class ConfigOptionTooltipHover : MonoBehaviour
    {
        private HoverTooltipDelay _hover;
        private string _text;
        private bool _showing;

        internal void Initialize(string text)
        {
            _text = text;
        }

        private void Awake()
        {
            _hover = GetComponent<HoverTooltipDelay>();
        }

        private void Update()
        {
            if (_hover != null && _hover.IsMouseOver() &&
                _hover.ShouldShowToolTip())
            {
                ConfigEditorPanel.ShowOptionTooltip(_text);
                _showing = true;
            }
            else if (_showing)
            {
                ConfigEditorPanel.HideOptionTooltip();
                _showing = false;
            }
        }

        private void OnDisable()
        {
            ConfigEditorPanel.HideOptionTooltip();
            _showing = false;
        }

        private void OnDestroy()
        {
            ConfigEditorPanel.HideOptionTooltip();
        }
    }

}
