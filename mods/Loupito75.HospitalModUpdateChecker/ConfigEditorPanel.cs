using System;
using System.Globalization;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
        private static Image _selectedRow;
        private static readonly Color NormalRowColor =
            new Color(.15686275f, .15686275f, .21568628f, 1f);

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
                PanelColor());
            RectTransform panel = window.GetComponent<RectTransform>();
            _windowRect = panel;
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
            panel.pivot = new Vector2(.5f, .5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(Width, Height);

            GameObject header = NewBox(
                "Header", window.transform, HeadingColor());
            RectTransform head = header.GetComponent<RectTransform>();
            // The title strip is the window header itself: no light frame
            // around it. Match the HMUC dashboard/native dialog treatment.
            TopRect(head, 0f, 44f, 0f, 0f);

            Text title = NewText(
                "Title", header.transform,
                (_session.PluginName ?? string.Empty).ToUpperInvariant(),
                HmucUiStyle.TitleFontSize,
                TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            RectTransform titleRect = title.rectTransform;
            Stretch(titleRect);
            titleRect.offsetMin = new Vector2(14f, 0f);
            titleRect.offsetMax = new Vector2(-50f, 0f);
            TitleScreenUpdatePanel.CreateHomeCloseButton(
                _source,
                header.transform,
                delegate
                {
                    TitleScreenUpdatePanel.PlayNativeClick();
                    RequestClose();
                });

            GameObject infoPanel = NewBox(
                "InfoPanel", window.transform, DarkPanelColor());
            RectTransform infoRect = infoPanel.GetComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0f, 0f);
            infoRect.anchorMax = new Vector2(1f, 0f);
            infoRect.pivot = new Vector2(.5f, 0f);
            infoRect.anchoredPosition = new Vector2(0f, 62f);
            infoRect.sizeDelta = new Vector2(-24f, 50f);

            _status = NewText(
                "Status", infoPanel.transform, Loc("hover_help"),
                HmucUiStyle.BodyFontSize,
                TextAnchor.MiddleLeft);
            RectTransform statusRect = _status.rectTransform;
            Stretch(statusRect);
            statusRect.offsetMin = new Vector2(10f, 5f);
            statusRect.offsetMax = new Vector2(-10f, -5f);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _status.verticalOverflow = VerticalWrapMode.Truncate;

            GameObject actionPanel = NewBox(
                "ActionPanel", window.transform, DarkPanelColor());
            RectTransform actionRect =
                actionPanel.GetComponent<RectTransform>();
            actionRect.anchorMin = actionRect.anchorMax =
                new Vector2(1f, 0f);
            actionRect.pivot = new Vector2(1f, 0f);
            actionRect.anchoredPosition = new Vector2(-12f, 8f);
            actionRect.sizeDelta = new Vector2(238f, 46f);

            if (canEdit)
            {
                _saveButton = MakeButton("Save", actionPanel.transform, Loc("save"),
                    new Vector2(105f, 30f), new Vector2(-8f, 8f),
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
            MakeButton("Close", actionPanel.transform, Loc("close"),
                new Vector2(105f, 30f), new Vector2(-121f, 8f),
                delegate { RequestClose(); }, false);

            GameObject viewport = new GameObject("SettingsViewport");
            viewport.transform.SetParent(window.transform, false);
            RectTransform viewportRect =
                viewport.AddComponent<RectTransform>();
            viewportRect.anchorMin = new Vector2(0f, 0f);
            viewportRect.anchorMax = new Vector2(1f, 1f);
            // Align the viewport with the 12 px outer margins used by
            // the adjacent info and action panels.
            viewportRect.offsetMin = new Vector2(12f, 121f);
            viewportRect.offsetMax = new Vector2(-44f, -62f);
            viewport.AddComponent<RectMask2D>();
            Image hit = viewport.AddComponent<Image>();
            hit.color = DarkPanelColor();
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
            const float sectionHeight = 34f;

            ScrollRect scrolling = viewport.AddComponent<ScrollRect>();
            scrolling.viewport = viewportRect;
            scrolling.content = rows;
            scrolling.horizontal = false;
            scrolling.vertical = true;
            scrolling.movementType = ScrollRect.MovementType.Clamped;
            scrolling.scrollSensitivity = 22f;

            GameObject scrollbarRoot = NewBox(
                "SettingsScrollbar", window.transform,
                ScrollbarTrackColor());
            RectTransform scrollbarRect =
                scrollbarRoot.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, .5f);
            scrollbarRect.offsetMin = new Vector2(-36f, 121f);
            scrollbarRect.offsetMax = new Vector2(-12f, -62f);

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
            // Same native geometry as WorkshopUploadPanel: inset the
            // sliding area, then expand the handle by the same amount.
            // This removes the visible border while preserving scrollbar math.
            slidingRect.sizeDelta = new Vector2(-20f, -20f);

            GameObject handle = NewBox(
                "Handle", slidingArea.transform,
                Color.white);
            RectTransform handleRect =
                handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.pivot = new Vector2(.5f, .5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(20f, 20f);

            Scrollbar scrollbar =
                scrollbarRoot.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.handleRect = handleRect;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.numberOfSteps = 0;
            ApplyMenuScrollbarColors(scrollbar);
            scrolling.verticalScrollbar = scrollbar;
            // Keep the native-width scrollbar visible even when all
            // settings fit in the viewport so the right-hand layout never
            // shifts or leaves an empty reserved gutter.
            scrolling.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;

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
                    // Separate grouped settings from ungrouped rows as well
                    // as two adjacent groups, without changing XML order.
                    if (currentSection != null)
                    {
                        AddSectionSeparator(rows, top);
                        top += 14f;
                    }
                    currentSection = section;
                    if (section.Length > 0)
                    {
                        AddSectionHeader(
                            rows, section, BuildSectionTooltip(section),
                            top, sectionHeight);
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

        private static string BuildSectionTooltip(string section)
        {
            if (_session == null)
                return null;

            string[] parts = section.Split(
                new string[] { " > " }, StringSplitOptions.None);
            string path = string.Empty;
            string tooltip = section;
            bool hasDescription = false;
            for (int i = 0; i < parts.Length; i++)
            {
                path = path.Length == 0 ? parts[i] : path + " > " + parts[i];
                string description;
                if (_session.SectionDescriptions.TryGetValue(
                    path, out description) &&
                    !string.IsNullOrEmpty(description))
                {
                    tooltip += "\n\n" + parts[i] + ": " +
                        description.Trim();
                    hasDescription = true;
                }
            }
            return hasDescription ? tooltip : null;
        }

        private static void AddSectionSeparator(Transform rows, float top)
        {
            // Distinct mauve-grey separator between configuration categories.
            // The previous translucent heading colour blended into the dark
            // viewport and made the separators effectively disappear.
            Color color =
                new Color(.3137255f, .3137255f, .43137255f, 1f);
            GameObject separator = NewBox(
                "SectionSeparator", rows, color);
            TopRect(separator.GetComponent<RectTransform>(),
                -top - 6f, 2f, 8f, 8f);
        }

        private static void AddSectionHeader(
            Transform rows, string section, string description,
            float top, float height)
        {
            GameObject header = NewBox(
                "Section_" + section.Replace(" > ", "_"),
                rows, HeadingColor());
            RectTransform rect = header.GetComponent<RectTransform>();
            TopRect(rect, -top, height - 4f, 0f, 0f);

            Text text = NewText(
                "SectionName", header.transform,
                TruncateLabel(section.ToUpperInvariant(), 44),
                HmucUiStyle.SectionFontSize,
                TextAnchor.MiddleLeft);
            text.fontStyle = FontStyle.Bold;
            RectTransform textRect = text.rectTransform;
            Stretch(textRect);
            textRect.offsetMin = new Vector2(10f, 0f);
            textRect.offsetMax = new Vector2(-10f, 0f);
            if (!string.IsNullOrEmpty(description))
            {
                text.raycastTarget = true;
                text.gameObject.AddComponent<HoverTooltipDelay>();
                ConfigOptionTooltipHover hover =
                    text.gameObject.AddComponent<ConfigOptionTooltipHover>();
                hover.Initialize(description);
            }
        }

        private static void AddRow(
            Transform rows, ConfigEditOption option,
            float top, float rowHeight)
        {
            GameObject row = NewBox(
                "Setting_" + option.Name, rows, NormalRowColor);
            Image rowImage = row.GetComponent<Image>();
            AttachRowSelection(row, rowImage);
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
                option.CompactRow
                    ? HmucUiStyle.RowSecondaryFontSize
                    : HmucUiStyle.RowPrimaryFontSize,
                TextAnchor.MiddleLeft);
            name.supportRichText = false;
            name.raycastTarget = true;
            AttachRowSelection(name.gameObject, rowImage);
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
                    row.transform, option, rowHeight, rowImage);
                return;
            }

            if (option.Control == "checkbox")
            {
                GameObject button = MakeValueButton(row.transform);
                AttachRowSelection(button, rowImage);
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
                AttachRowSelection(button, rowImage);
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
                AddSlider(row.transform, option, rowImage);
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
            AttachRowSelection(inputRoot, rowImage);
            InputField input = inputRoot.AddComponent<InputField>();
            input.targetGraphic = inputRoot.GetComponent<Image>();
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 2048;
            Text textField = NewText(
                "Value", inputRoot.transform, option.Value, 16,
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
            float rowHeight,
            Image rowImage)
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
            AttachRowSelection(inputRoot, rowImage);

            InputField input =
                inputRoot.AddComponent<InputField>();
            input.targetGraphic =
                inputRoot.GetComponent<Image>();
            input.lineType =
                InputField.LineType.MultiLineNewline;
            input.characterLimit = 16384;

            Text textField = NewText(
                "Value", inputRoot.transform,
                option.Value, 14,
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
            Transform row, ConfigEditOption option, Image rowImage)
        {
            GameObject root = NewBox(
                "Slider", row, GaugeBackColor());
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.72f, .5f);
            rect.anchorMax = new Vector2(.87f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, 12f);

            AttachRowSelection(root, rowImage);
            GameObject fill = NewBox(
                "Fill", root.transform, GaugeFrontColor());
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            Stretch(fillRect);
            GameObject handle = NewBox(
                "Handle", root.transform,
                new Color(.95f, .95f, .95f, .82f));
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = handleRect.anchorMax =
                new Vector2(0f, .5f);
            handleRect.pivot = new Vector2(.5f, .5f);
            handleRect.sizeDelta = new Vector2(5f, 14f);

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

            AttachRowSelection(valueRoot, rowImage);
            InputField valueInput = valueRoot.AddComponent<InputField>();
            valueInput.targetGraphic = valueHitArea;
            valueInput.lineType = InputField.LineType.SingleLine;
            valueInput.contentType = InputField.ContentType.DecimalNumber;

            Text valueLabel = NewText(
                "Value", valueRoot.transform, option.Value, 16,
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
                _status.text = string.Equals(error,
                        "HMUC_NEW_CONFIG_FORMAT", StringComparison.Ordinal)
                    ? UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.ConfigSchemaMigrationAlertId)
                    : Loc("error") + ": " + Loc("processing_error");
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
            _selectedRow = null;
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
                    HmucUiStyle.TooltipColor());
                Image tooltipBackground =
                    _optionTooltip.GetComponent<Image>();
                if (tooltipBackground != null)
                    tooltipBackground.raycastTarget = false;

                RectTransform root =
                    _optionTooltip.GetComponent<RectTransform>();
                root.anchorMin = root.anchorMax =
                    new Vector2(.5f, .5f);
                root.pivot = new Vector2(.5f, .5f);
                root.sizeDelta = new Vector2(360f, 48f);

                _optionTooltipText = NewText(
                    "TooltipText", _optionTooltip.transform, "",
                    HmucUiStyle.TooltipFontSize,
                    TextAnchor.MiddleLeft);
                RectTransform textRect =
                    _optionTooltipText.rectTransform;
                Stretch(textRect);
                textRect.offsetMin = new Vector2(
                    HmucUiStyle.TooltipPaddingHorizontal,
                    HmucUiStyle.TooltipPaddingVertical);
                textRect.offsetMax = new Vector2(
                    -HmucUiStyle.TooltipPaddingHorizontal,
                    -HmucUiStyle.TooltipPaddingVertical);
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
            float width =
                HmucUiStyle.TooltipWidth(
                    _optionTooltipText.preferredWidth);
            tooltipRect.sizeDelta = new Vector2(
                width,
                HmucUiStyle.TooltipMinHeight);
            Canvas.ForceUpdateCanvases();
            float height =
                HmucUiStyle.TooltipHeight(
                    _optionTooltipText.preferredHeight);
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

                float minY =
                    -halfHeight +
                    tooltipHalfHeight +
                    HmucUiStyle.TooltipEdgeMargin;
                float maxY =
                    halfHeight -
                    tooltipHalfHeight -
                    HmucUiStyle.TooltipEdgeMargin;

                // Prefer above the pointer. For the first section there may
                // not be enough room; in that case place the tooltip below
                // instead of clamping it back across the hovered header.
                float aboveY =
                    pointer.y +
                    tooltipHalfHeight +
                    HmucUiStyle.TooltipPointerGap;
                float belowY =
                    pointer.y -
                    tooltipHalfHeight -
                    HmucUiStyle.TooltipPointerGap;
                float tooltipY =
                    aboveY <= maxY
                        ? aboveY
                        : belowY >= minY
                            ? belowY
                            : Mathf.Clamp(aboveY, minY, maxY);

                tooltipRect.anchoredPosition = new Vector2(
                    Mathf.Clamp(
                        pointer.x,
                        -halfWidth +
                            tooltipHalfWidth +
                            HmucUiStyle.TooltipEdgeMargin,
                        halfWidth -
                            tooltipHalfWidth -
                            HmucUiStyle.TooltipEdgeMargin),
                    tooltipY);
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

        internal static void SelectRow(Image row)
        {
            if (row == null || row == _selectedRow)
                return;

            if (_selectedRow != null)
                _selectedRow.color = NormalRowColor;

            _selectedRow = row;
            _selectedRow.color =
                new Color(
                    .3137255f, .3137255f, .43137255f, .88235295f);
        }

        private static void AttachRowSelection(GameObject target, Image row)
        {
            ConfigRowSelection tracker =
                target.AddComponent<ConfigRowSelection>();
            tracker.Initialize(row);
        }

        // Confirmed from the exported vanilla Options, Save/Load,
        // FileLabel, TooltipSmall and Gauge prefabs.
        private static Color PanelColor()
        {
            return new Color(
                .93333334f, .93333334f, .93333334f, 1f);
        }

        private static Color DarkPanelColor()
        {
            return new Color(
                .15294118f, .16078432f, .21176471f, 1f);
        }

        private static Color ScrollbarTrackColor()
        {
            // Exact dark scrollbar background from the exported UI:
            // RGB 50, 50, 70 (#323246).
            return new Color(
                .19607843f, .19607843f, .27450982f, 1f);
        }

        private static void ApplyMenuScrollbarColors(
            Scrollbar scrollbar)
        {
            if (scrollbar == null)
                return;
            ColorBlock colors = scrollbar.colors;
            colors.normalColor =
                new Color(.3137255f, .3137255f, .43137255f, 1f);
            colors.highlightedColor =
                new Color(.47058824f, .47058824f, .64705884f, 1f);
            colors.pressedColor =
                new Color(.47058824f, .47058824f, .64705884f, 1f);
            // Permanent scrollbar: keep the same handle colour if Unity
            // disables interaction because the content already fits.
            colors.disabledColor =
                new Color(.3137255f, .3137255f, .43137255f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = .1f;
            scrollbar.colors = colors;
        }

        private static Color HeadingColor()
        {
            return DarkPanelColor();
        }

        private static Color GaugeBackColor()
        {
            // OptionsSliderPanel/GaugeBgr: light unused portion.
            return new Color(
                .75881094f, .78753847f, .8207547f, 1f);
        }

        private static Color GaugeFrontColor()
        {
            // OptionsSliderPanel/GaugePanel: dark filled portion.
            return new Color(
                .3137255f, .3137255f, .43137255f, 1f);
        }

        private static Color ButtonTextColor()
        {
            return DarkPanelColor();
        }

        private static void ApplyLightButtonColors(Button button)
        {
            if (button == null)
                return;
            ColorBlock colors = button.colors;
            colors.normalColor =
                new Color(.88235295f, .88235295f, .88235295f, 1f);
            colors.highlightedColor =
                new Color(.9607843f, .9607843f, .9607843f, 1f);
            colors.pressedColor =
                new Color(.78431374f, .78431374f, .78431374f, 1f);
            colors.disabledColor =
                new Color(.78431374f, .78431374f, .78431374f, .5019608f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
        }

        private static void ApplyTransparentButtonColors(Button button)
        {
            if (button == null)
                return;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.clear;
            colors.highlightedColor = Color.clear;
            colors.pressedColor = Color.clear;
            colors.disabledColor = Color.clear;
            colors.colorMultiplier = 1f;
            button.colors = colors;
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
            Text label =
                TitleScreenUpdatePanel.CreateHomeTextClone(
                    _source, name, parent);
            GameObject go = label.gameObject;
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
            button.onClick.AddListener(delegate
            {
                TitleScreenUpdatePanel.PlayNativeClick();
            });
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
            GameObject root = NewBox(
                name, parent, Color.white);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax =
                top ? new Vector2(1f, 1f) : new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, top ? 1f : 0f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            Button button = root.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            ApplyLightButtonColors(button);
            button.onClick.AddListener(delegate
            {
                TitleScreenUpdatePanel.PlayNativeClick();
                action();
            });
            Text text = NewText(
                "Text", root.transform, label, 16,
                TextAnchor.MiddleCenter);
            text.color = ButtonTextColor();
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

    internal sealed class ConfigRowSelection : MonoBehaviour, IPointerDownHandler
    {
        private Image _row;

        internal void Initialize(Image row)
        {
            _row = row;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                ConfigEditorPanel.SelectRow(_row);
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
