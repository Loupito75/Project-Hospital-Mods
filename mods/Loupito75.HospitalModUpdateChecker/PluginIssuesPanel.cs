using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalModUpdateChecker
{
    internal static class PluginIssuesPanel
    {
        // Deliberately identical to ConfigEditorPanel. The log viewer is a
        // sibling HMUC modal, not a second visual language.
        private const float Width = 700f;
        private const float Height = 680f;

        private static GameObject _overlay;
        private static Text _body;

        internal static bool IsOpen
        {
            get { return _overlay != null; }
        }

        internal static void Open(
            string pluginName,
            string author,
            Version version,
            IList<PluginLogIssue> issues,
            Transform canvas,
            Text textTemplate)
        {
            Close();

            if (canvas == null ||
                textTemplate == null)
            {
                return;
            }

            HmucTooltipPresenter.HideAll();
            ConfigEditorPanel.Close();
            TitleScreenUpdatePanel.HideModMetadataTooltip(null);
            TitleScreenUpdatePanel.HideConfigGearTooltip();

            _overlay =
                new GameObject(
                    "HMUC.PluginIssuesOverlay");

            _overlay.transform.SetParent(
                canvas,
                false);

            RectTransform backdrop =
                _overlay.AddComponent<RectTransform>();

            Stretch(
                backdrop);

            Image shadow =
                _overlay.AddComponent<Image>();

            shadow.color =
                new Color(
                    0f,
                    0f,
                    0f,
                    .70f);

            shadow.raycastTarget =
                true;

            _overlay.AddComponent<
                PluginIssuesPanelEscapeHandler>();

            _overlay.transform.SetAsLastSibling();

            GameObject window =
                NewBox(
                    "PluginIssuesWindow",
                    _overlay.transform,
                    PanelColor());

            RectTransform panel =
                window.GetComponent<RectTransform>();

            panel.anchorMin =
                panel.anchorMax =
                    new Vector2(.5f, .5f);

            panel.pivot =
                new Vector2(.5f, .5f);

            panel.anchoredPosition =
                Vector2.zero;

            panel.sizeDelta =
                new Vector2(
                    Width,
                    Height);

            CreateHeader(
                window.transform,
                textTemplate,
                pluginName);

            CreateBottomInfo(
                window.transform,
                textTemplate,
                pluginName,
                author,
                version);

            CreateActions(
                window.transform,
                textTemplate);

            CreateReadOnlyLog(
                window.transform,
                textTemplate,
                issues);
        }

        internal static void Close()
        {
            HmucTooltipPresenter.HideAll();

            if (_overlay != null)
            {
                UnityEngine.Object.Destroy(
                    _overlay);
            }

            _overlay = null;
            _body = null;
        }

        private static void CreateHeader(
            Transform parent,
            Text source,
            string pluginName)
        {
            GameObject header =
                NewBox(
                    "Header",
                    parent,
                    HeadingColor());

            RectTransform head =
                header.GetComponent<RectTransform>();

            TopRect(
                head,
                0f,
                44f,
                0f,
                0f);

            string safeName =
                ModDetailsCatalog.SanitizeDisplayText(
                    pluginName,
                    128);

            string titleText =
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.PluginIssuesTitleId);

            if (safeName.Length > 0)
            {
                titleText +=
                    " - " +
                    safeName;
            }

            Text title =
                NewText(
                    source,
                    "Title",
                    header.transform,
                    titleText.ToUpperInvariant(),
                    HmucUiStyle.TitleFontSize,
                    TextAnchor.MiddleLeft);

            title.fontStyle =
                FontStyle.Bold;

            RectTransform titleRect =
                title.rectTransform;

            Stretch(
                titleRect);

            titleRect.offsetMin =
                new Vector2(
                    14f,
                    0f);

            titleRect.offsetMax =
                new Vector2(
                    -50f,
                    0f);

            TitleScreenUpdatePanel.CreateHomeCloseButton(
                source,
                header.transform,
                delegate
                {
                    TitleScreenUpdatePanel.PlayNativeClick();
                    Close();
                });
        }

        private static void CreateBottomInfo(
            Transform parent,
            Text source,
            string pluginName,
            string author,
            Version version)
        {
            GameObject infoPanel =
                NewBox(
                    "InfoPanel",
                    parent,
                    DarkPanelColor());

            RectTransform infoRect =
                infoPanel.GetComponent<RectTransform>();

            infoRect.anchorMin =
                new Vector2(
                    0f,
                    0f);

            infoRect.anchorMax =
                new Vector2(
                    1f,
                    0f);

            infoRect.pivot =
                new Vector2(
                    .5f,
                    0f);

            infoRect.anchoredPosition =
                new Vector2(
                    0f,
                    62f);

            infoRect.sizeDelta =
                new Vector2(
                    -24f,
                    50f);

            string safeName =
                ModDetailsCatalog.SanitizeDisplayText(
                    pluginName,
                    128);

            string safeAuthor =
                ModDetailsCatalog.SanitizeDisplayText(
                    author,
                    128);

            StringBuilder info =
                new StringBuilder();

            if (safeName.Length > 0)
            {
                info.Append(
                    safeName);
            }

            if (safeAuthor.Length > 0)
            {
                info.Append(
                    " (");

                info.Append(
                    safeAuthor);

                info.Append(
                    ")");
            }

            if (version != null)
            {
                info.Append(
                    " - v");

                info.Append(
                    version);
            }

            if (info.Length > 0)
            {
                info.Append(
                    "\n");
            }

            info.Append(
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.PluginIssuesHelpId));

            Text status =
                NewText(
                    source,
                    "Status",
                    infoPanel.transform,
                    info.ToString(),
                    HmucUiStyle.BodyFontSize,
                    TextAnchor.MiddleLeft);

            RectTransform statusRect =
                status.rectTransform;

            Stretch(
                statusRect);

            statusRect.offsetMin =
                new Vector2(
                    10f,
                    5f);

            statusRect.offsetMax =
                new Vector2(
                    -10f,
                    -5f);

            status.horizontalOverflow =
                HorizontalWrapMode.Wrap;

            status.verticalOverflow =
                VerticalWrapMode.Truncate;
        }

        private static void CreateReadOnlyLog(
            Transform parent,
            Text source,
            IList<PluginLogIssue> issues)
        {
            GameObject viewport =
                new GameObject(
                    "LogViewport");

            viewport.transform.SetParent(
                parent,
                false);

            RectTransform viewportRect =
                viewport.AddComponent<RectTransform>();

            viewportRect.anchorMin =
                new Vector2(
                    0f,
                    0f);

            viewportRect.anchorMax =
                new Vector2(
                    1f,
                    1f);

            // Same geometry as ConfigEditorPanel.SettingsViewport.
            viewportRect.offsetMin =
                new Vector2(
                    12f,
                    121f);

            viewportRect.offsetMax =
                new Vector2(
                    -44f,
                    -62f);

            viewport.AddComponent<RectMask2D>();

            Image hit =
                viewport.AddComponent<Image>();

            hit.color =
                DarkPanelColor();

            hit.raycastTarget =
                true;

            GameObject contents =
                new GameObject(
                    "LogContents");

            contents.transform.SetParent(
                viewport.transform,
                false);

            RectTransform contentRect =
                contents.AddComponent<RectTransform>();

            contentRect.anchorMin =
                new Vector2(
                    0f,
                    1f);

            contentRect.anchorMax =
                new Vector2(
                    1f,
                    1f);

            contentRect.pivot =
                new Vector2(
                    .5f,
                    1f);

            contentRect.anchoredPosition =
                Vector2.zero;

            _body =
                NewText(
                    source,
                    "ReadOnlyLogText",
                    contents.transform,
                    BuildIssueText(
                        issues),
                    HmucUiStyle.BodyFontSize,
                    TextAnchor.UpperLeft);

            RectTransform bodyRect =
                _body.rectTransform;

            bodyRect.anchorMin =
                new Vector2(
                    0f,
                    1f);

            bodyRect.anchorMax =
                new Vector2(
                    1f,
                    1f);

            bodyRect.pivot =
                new Vector2(
                    .5f,
                    1f);

            bodyRect.anchoredPosition =
                new Vector2(
                    0f,
                    -8f);

            bodyRect.sizeDelta =
                new Vector2(
                    -20f,
                    1f);

            _body.supportRichText =
                false;

            _body.horizontalOverflow =
                HorizontalWrapMode.Wrap;

            _body.verticalOverflow =
                VerticalWrapMode.Overflow;

            _body.raycastTarget =
                false;

            ScrollRect scrolling =
                viewport.AddComponent<ScrollRect>();

            scrolling.viewport =
                viewportRect;

            scrolling.content =
                contentRect;

            scrolling.horizontal =
                false;

            scrolling.vertical =
                true;

            scrolling.movementType =
                ScrollRect.MovementType.Clamped;

            scrolling.scrollSensitivity =
                22f;

            GameObject scrollbarRoot =
                NewBox(
                    "LogScrollbar",
                    parent,
                    ScrollbarTrackColor());

            RectTransform scrollbarRect =
                scrollbarRoot.GetComponent<RectTransform>();

            scrollbarRect.anchorMin =
                new Vector2(
                    1f,
                    0f);

            scrollbarRect.anchorMax =
                new Vector2(
                    1f,
                    1f);

            scrollbarRect.pivot =
                new Vector2(
                    1f,
                    .5f);

            scrollbarRect.offsetMin =
                new Vector2(
                    -36f,
                    121f);

            scrollbarRect.offsetMax =
                new Vector2(
                    -12f,
                    -62f);

            GameObject slidingArea =
                new GameObject(
                    "SlidingArea");

            slidingArea.transform.SetParent(
                scrollbarRoot.transform,
                false);

            RectTransform slidingRect =
                slidingArea.AddComponent<RectTransform>();

            slidingRect.anchorMin =
                Vector2.zero;

            slidingRect.anchorMax =
                Vector2.one;

            slidingRect.pivot =
                new Vector2(
                    .5f,
                    .5f);

            slidingRect.anchoredPosition =
                Vector2.zero;

            slidingRect.sizeDelta =
                new Vector2(
                    -20f,
                    -20f);

            GameObject handle =
                NewBox(
                    "Handle",
                    slidingArea.transform,
                    Color.white);

            RectTransform handleRect =
                handle.GetComponent<RectTransform>();

            handleRect.anchorMin =
                Vector2.zero;

            handleRect.anchorMax =
                Vector2.one;

            handleRect.pivot =
                new Vector2(
                    .5f,
                    .5f);

            handleRect.anchoredPosition =
                Vector2.zero;

            handleRect.sizeDelta =
                new Vector2(
                    20f,
                    20f);

            Scrollbar scrollbar =
                scrollbarRoot.AddComponent<Scrollbar>();

            scrollbar.targetGraphic =
                handle.GetComponent<Image>();

            scrollbar.handleRect =
                handleRect;

            scrollbar.direction =
                Scrollbar.Direction.BottomToTop;

            scrollbar.numberOfSteps =
                0;

            ApplyMenuScrollbarColors(
                scrollbar);

            scrolling.verticalScrollbar =
                scrollbar;

            scrolling.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;

            Canvas.ForceUpdateCanvases();

            float contentHeight =
                Math.Max(
                    viewportRect.rect.height,
                    _body.preferredHeight +
                    16f);

            contentRect.sizeDelta =
                new Vector2(
                    0f,
                    contentHeight);

            bodyRect.sizeDelta =
                new Vector2(
                    -20f,
                    Math.Max(
                        1f,
                        _body.preferredHeight));

            scrolling.verticalNormalizedPosition =
                1f;
        }

        private static void CreateActions(
            Transform parent,
            Text source)
        {
            GameObject actionPanel =
                NewBox(
                    "ActionPanel",
                    parent,
                    DarkPanelColor());

            RectTransform actionRect =
                actionPanel.GetComponent<RectTransform>();

            actionRect.anchorMin =
                actionRect.anchorMax =
                    new Vector2(
                        1f,
                        0f);

            actionRect.pivot =
                new Vector2(
                    1f,
                    0f);

            actionRect.anchoredPosition =
                new Vector2(
                    -12f,
                    8f);

            actionRect.sizeDelta =
                new Vector2(
                    238f,
                    46f);

            MakeButton(
                source,
                "Copy",
                actionPanel.transform,
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.PluginIssuesCopyId),
                new Vector2(
                    105f,
                    30f),
                new Vector2(
                    -8f,
                    8f),
                delegate
                {
                    GUIUtility.systemCopyBuffer =
                        _body == null
                            ? string.Empty
                            : _body.text;
                });

            MakeButton(
                source,
                "Close",
                actionPanel.transform,
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.PluginIssuesCloseId),
                new Vector2(
                    105f,
                    30f),
                new Vector2(
                    -121f,
                    8f),
                delegate
                {
                    Close();
                });
        }

        private static string BuildIssueText(
            IList<PluginLogIssue> issues)
        {
            if (issues == null ||
                issues.Count == 0)
            {
                return UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.PluginIssuesEmptyId);
            }

            StringBuilder builder =
                new StringBuilder();

            for (int i = 0;
                i < issues.Count;
                i++)
            {
                PluginLogIssue issue =
                    issues[i];

                if (issue == null)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(
                        "\n\n");
                }

                builder.Append(
                    GetIssueLevelText(
                        issue.Status));

                if (!string.IsNullOrEmpty(
                        issue.Source))
                {
                    builder.Append(
                        " - ");

                    builder.Append(
                        ModDetailsCatalog.SanitizeDisplayText(
                            issue.Source,
                            128));
                }

                builder.Append(
                    "\n");

                builder.Append(
                    string.IsNullOrEmpty(
                        issue.Message)
                        ? UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.PluginIssuesEmptyId)
                        : issue.Message);
            }

            return builder.Length == 0
                ? UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.PluginIssuesEmptyId)
                : builder.ToString();
        }

        private static string GetIssueLevelText(
            PluginLogStatus status)
        {
            if (status ==
                PluginLogStatus.Error)
            {
                return UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.StatusErrorId);
            }

            if (status ==
                PluginLogStatus.Warning)
            {
                return UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.StatusWarningId);
            }

            return UpdatePanelLocalization.Get(
                UpdatePanelLocalization.PluginIssuesInfoId);
        }

        private static Button MakeButton(
            Text source,
            string name,
            Transform parent,
            string label,
            Vector2 size,
            Vector2 offset,
            Action action)
        {
            GameObject root =
                NewBox(
                    name,
                    parent,
                    Color.white);

            RectTransform rect =
                root.GetComponent<RectTransform>();

            rect.anchorMin =
                rect.anchorMax =
                    new Vector2(
                        1f,
                        0f);

            rect.pivot =
                new Vector2(
                    1f,
                    0f);

            rect.anchoredPosition =
                offset;

            rect.sizeDelta =
                size;

            Button button =
                root.AddComponent<Button>();

            button.targetGraphic =
                root.GetComponent<Image>();

            ApplyLightButtonColors(
                button);

            button.onClick.AddListener(
                delegate
                {
                    TitleScreenUpdatePanel.PlayNativeClick();

                    if (action != null)
                    {
                        action();
                    }
                });

            Text text =
                NewText(
                    source,
                    "Text",
                    root.transform,
                    label,
                    HmucUiStyle.ButtonFontSize,
                    TextAnchor.MiddleCenter);

            text.color =
                ButtonTextColor();

            Stretch(
                text.rectTransform);

            return button;
        }

        private static GameObject NewBox(
            string name,
            Transform parent,
            Color color)
        {
            GameObject result =
                new GameObject(
                    name);

            result.transform.SetParent(
                parent,
                false);

            result.AddComponent<RectTransform>();

            Image image =
                result.AddComponent<Image>();

            image.color =
                color;

            return result;
        }

        private static Text NewText(
            Text source,
            string name,
            Transform parent,
            string text,
            int fontSize,
            TextAnchor alignment)
        {
            Text label =
                TitleScreenUpdatePanel.CreateHomeTextClone(
                    source,
                    name,
                    parent);

            label.text =
                text ?? string.Empty;

            label.fontSize =
                fontSize;

            label.color =
                Color.white;

            label.alignment =
                alignment;

            label.raycastTarget =
                false;

            label.supportRichText =
                false;

            return label;
        }

        private static void Stretch(
            RectTransform rect)
        {
            rect.anchorMin =
                Vector2.zero;

            rect.anchorMax =
                Vector2.one;

            rect.pivot =
                new Vector2(
                    .5f,
                    .5f);

            rect.offsetMin =
                Vector2.zero;

            rect.offsetMax =
                Vector2.zero;
        }

        private static void TopRect(
            RectTransform rect,
            float top,
            float height,
            float left,
            float right)
        {
            rect.anchorMin =
                new Vector2(
                    0f,
                    1f);

            rect.anchorMax =
                new Vector2(
                    1f,
                    1f);

            rect.pivot =
                new Vector2(
                    .5f,
                    1f);

            rect.anchoredPosition =
                new Vector2(
                    (left - right) / 2f,
                    top);

            rect.sizeDelta =
                new Vector2(
                    -left - right,
                    height);
        }

        private static Color PanelColor()
        {
            return new Color(
                .93333334f,
                .93333334f,
                .93333334f,
                1f);
        }

        private static Color DarkPanelColor()
        {
            return new Color(
                .15294118f,
                .16078432f,
                .21176471f,
                1f);
        }

        private static Color HeadingColor()
        {
            return DarkPanelColor();
        }

        private static Color ScrollbarTrackColor()
        {
            return new Color(
                .19607843f,
                .19607843f,
                .27450982f,
                1f);
        }

        private static Color ButtonTextColor()
        {
            return DarkPanelColor();
        }

        private static void ApplyLightButtonColors(
            Button button)
        {
            if (button == null)
            {
                return;
            }

            ColorBlock colors =
                button.colors;

            colors.normalColor =
                new Color(
                    .88235295f,
                    .88235295f,
                    .88235295f,
                    1f);

            colors.highlightedColor =
                new Color(
                    .9607843f,
                    .9607843f,
                    .9607843f,
                    1f);

            colors.pressedColor =
                new Color(
                    .78431374f,
                    .78431374f,
                    .78431374f,
                    1f);

            colors.disabledColor =
                new Color(
                    .78431374f,
                    .78431374f,
                    .78431374f,
                    .5019608f);

            colors.colorMultiplier =
                1f;

            button.colors =
                colors;
        }

        private static void ApplyMenuScrollbarColors(
            Scrollbar scrollbar)
        {
            if (scrollbar == null)
            {
                return;
            }

            ColorBlock colors =
                scrollbar.colors;

            colors.normalColor =
                new Color(
                    .3137255f,
                    .3137255f,
                    .43137255f,
                    1f);

            colors.highlightedColor =
                new Color(
                    .47058824f,
                    .47058824f,
                    .64705884f,
                    1f);

            colors.pressedColor =
                colors.highlightedColor;

            colors.disabledColor =
                colors.normalColor;

            colors.colorMultiplier =
                1f;

            colors.fadeDuration =
                .1f;

            scrollbar.colors =
                colors;
        }
    }

    internal sealed class PluginIssuesPanelEscapeHandler :
        MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(
                    KeyCode.Escape))
            {
                TitleScreenUpdatePanel.PlayNativeClick();
                PluginIssuesPanel.Close();
            }
        }
    }
}
