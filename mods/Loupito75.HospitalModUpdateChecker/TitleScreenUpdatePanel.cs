using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalModUpdateChecker
{
    internal static class TitleScreenUpdatePanel
    {
        private const float PanelWidth = 500f;
        private const float HeaderHeight = 42f;
        private const float TopMargin = 24f;
        private const float BodyPadding = 18f;
        private const float UpdateBlockHeight = 48f;
        private const int MaximumDisplayedUpdates = 8;

        private static TitleScreenController _controller;
        private static GameObject _panel;
        private static Text _bodyText;
        private static bool _dismissedForSession;

        internal static void Attach(TitleScreenController controller)
        {
            if (controller == null ||
                controller.m_canvas == null ||
                controller.m_modErrorText == null)
            {
                return;
            }

            _controller = controller;

            if (_panel != null)
            {
                UnityEngine.Object.Destroy(_panel);
                _panel = null;
                _bodyText = null;
            }

            Refresh(UpdateChecker.AvailableUpdates);
        }

        internal static void Refresh(IList<AvailableUpdate> updates)
        {
            if (_controller == null ||
                _controller.m_canvas == null ||
                _controller.m_modErrorText == null)
            {
                return;
            }

            if (_dismissedForSession ||
                updates == null ||
                updates.Count == 0)
            {
                if (_panel != null)
                {
                    _panel.SetActive(false);
                }

                return;
            }

            if (!EnsurePanel())
            {
                return;
            }

            int displayedCount =
                Math.Min(updates.Count, MaximumDisplayedUpdates);

            float bodyHeight =
                BodyPadding * 2f +
                UpdateBlockHeight * displayedCount;

            if (updates.Count > displayedCount)
            {
                bodyHeight += 28f;
            }

            RectTransform panelRect =
                _panel.GetComponent<RectTransform>();

            panelRect.sizeDelta = new Vector2(
                PanelWidth,
                HeaderHeight + bodyHeight);

            _bodyText.text =
                BuildBodyText(updates, displayedCount);

            RectTransform bodyRect =
                _bodyText.GetComponent<RectTransform>();

            bodyRect.anchoredPosition = new Vector2(
                BodyPadding,
                -HeaderHeight - BodyPadding);

            bodyRect.sizeDelta = new Vector2(
                PanelWidth - BodyPadding * 2f,
                bodyHeight - BodyPadding * 2f);

            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
        }

        private static bool EnsurePanel()
        {
            if (_panel != null)
            {
                return true;
            }

            Text sourceText =
                _controller.m_modErrorText.GetComponent<Text>();

            if (sourceText == null)
            {
                Plugin.Log.LogWarning(
                    "Title-screen update panel could not use m_modErrorText because it has no Text component.");
                return false;
            }

            _panel = new GameObject(
                "Loupito75.HospitalModUpdateChecker.UpdatePanel");

            _panel.transform.SetParent(
                _controller.m_canvas.transform,
                false);

            RectTransform panelRect =
                _panel.AddComponent<RectTransform>();

            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition =
                new Vector2(0f, -TopMargin);

            Image panelImage = _panel.AddComponent<Image>();
            panelImage.color = GetPanelColor();
            panelImage.raycastTarget = true;

            GameObject heading =
                new GameObject("Heading");

            heading.transform.SetParent(
                _panel.transform,
                false);

            RectTransform headingRect =
                heading.AddComponent<RectTransform>();

            headingRect.anchorMin = new Vector2(0f, 1f);
            headingRect.anchorMax = new Vector2(0f, 1f);
            headingRect.pivot = new Vector2(0f, 1f);
            headingRect.anchoredPosition = Vector2.zero;
            headingRect.sizeDelta =
                new Vector2(PanelWidth, HeaderHeight);

            Image headingImage =
                heading.AddComponent<Image>();

            headingImage.color = GetHeadingColor();
            headingImage.raycastTarget = true;

            Text headingText =
                CreateTextClone(
                    sourceText,
                    "HeadingText",
                    heading.transform);

            if (headingText == null)
            {
                DestroyPanel();
                return false;
            }

            RectTransform headingTextRect =
                headingText.GetComponent<RectTransform>();

            headingTextRect.anchorMin = Vector2.zero;
            headingTextRect.anchorMax = Vector2.one;
            headingTextRect.pivot =
                new Vector2(0.5f, 0.5f);
            headingTextRect.anchoredPosition =
                new Vector2(14f, 0f);
            headingTextRect.sizeDelta =
                new Vector2(-58f, 0f);

            headingText.text =
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.TitleId);

            headingText.alignment =
                TextAnchor.MiddleLeft;
            headingText.fontStyle = FontStyle.Bold;
            headingText.fontSize =
                Math.Max(sourceText.fontSize, 16);
            headingText.raycastTarget = false;

            _bodyText =
                CreateTextClone(
                    sourceText,
                    "Updates",
                    _panel.transform);

            if (_bodyText == null)
            {
                DestroyPanel();
                return false;
            }

            RectTransform bodyRect =
                _bodyText.GetComponent<RectTransform>();

            bodyRect.anchorMin =
                new Vector2(0f, 1f);
            bodyRect.anchorMax =
                new Vector2(0f, 1f);
            bodyRect.pivot =
                new Vector2(0f, 1f);

            _bodyText.alignment =
                TextAnchor.UpperLeft;
            _bodyText.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            _bodyText.verticalOverflow =
                VerticalWrapMode.Overflow;
            _bodyText.raycastTarget = false;
            _bodyText.supportRichText = true;
            _bodyText.fontSize =
                Math.Max(sourceText.fontSize, 14);

            CreateCloseButton(
                sourceText,
                heading.transform);

            _panel.SetActive(false);
            return true;
        }

        private static Text CreateTextClone(
            Text sourceText,
            string name,
            Transform parent)
        {
            if (sourceText == null)
            {
                return null;
            }

            GameObject clone =
                UnityEngine.Object.Instantiate(
                    sourceText.gameObject);

            clone.name = name;
            clone.transform.SetParent(parent, false);
            clone.transform.localScale = Vector3.one;
            clone.SetActive(true);

            LocalizedTextController localized =
                clone.GetComponent<LocalizedTextController>();

            if (localized != null)
            {
                localized.enabled = false;
            }

            Text text = clone.GetComponent<Text>();
            if (text == null)
            {
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            text.text = string.Empty;
            text.color = Color.white;
            return text;
        }

        private static void CreateCloseButton(
            Text sourceText,
            Transform heading)
        {
            GameObject buttonObject =
                new GameObject("CloseButton");

            buttonObject.transform.SetParent(
                heading,
                false);

            RectTransform rect =
                buttonObject.AddComponent<RectTransform>();

            rect.anchorMin =
                new Vector2(1f, 0.5f);
            rect.anchorMax =
                new Vector2(1f, 0.5f);
            rect.pivot =
                new Vector2(1f, 0.5f);
            rect.anchoredPosition =
                new Vector2(-6f, 0f);
            rect.sizeDelta =
                new Vector2(34f, 34f);

            Image image =
                buttonObject.AddComponent<Image>();

            Color buttonColor =
                GetHeadingColor();

            buttonColor.a =
                Math.Min(1f, buttonColor.a + 0.15f);

            image.color = buttonColor;

            Button button =
                buttonObject.AddComponent<Button>();

            button.onClick.AddListener(delegate
            {
                _dismissedForSession = true;

                if (_panel != null)
                {
                    _panel.SetActive(false);
                }

                if (UISoundManager.sm_instance != null)
                {
                    UISoundManager.sm_instance.PlaySoundEvent(
                        "SFX_UI_WINDOW_CLOSED");
                }
            });

            Text xText =
                CreateTextClone(
                    sourceText,
                    "CloseText",
                    buttonObject.transform);

            if (xText == null)
            {
                return;
            }

            RectTransform xRect =
                xText.GetComponent<RectTransform>();

            xRect.anchorMin = Vector2.zero;
            xRect.anchorMax = Vector2.one;
            xRect.pivot =
                new Vector2(0.5f, 0.5f);
            xRect.anchoredPosition =
                Vector2.zero;
            xRect.sizeDelta =
                Vector2.zero;

            xText.text = "X";
            xText.alignment =
                TextAnchor.MiddleCenter;
            xText.fontStyle =
                FontStyle.Bold;
            xText.fontSize =
                Math.Max(sourceText.fontSize, 16);
            xText.raycastTarget = false;
        }

        private static Color GetPanelColor()
        {
            if (UISettings.Instance != null)
            {
                Color color =
                    UISettings.Instance.THEME_COLOR_MAIN;

                color.a = 0.95f;
                return color;
            }

            return new Color(
                0.08f,
                0.08f,
                0.10f,
                0.95f);
        }

        private static Color GetHeadingColor()
        {
            if (UISettings.Instance != null)
            {
                return
                    UISettings.Instance
                        .THEME_COLOR_PANEL_HEADING;
            }

            return new Color(
                0.16f,
                0.16f,
                0.20f,
                1f);
        }

        private static void DestroyPanel()
        {
            if (_panel != null)
            {
                UnityEngine.Object.Destroy(_panel);
            }

            _panel = null;
            _bodyText = null;
        }

        private static string BuildModLabel(
            AvailableUpdate update)
        {
            string prefix = "\u2022 ";
            string suffix =
                " (" + update.Author + ")";
            string name =
                update.Name ?? string.Empty;

            int fontSize =
                _bodyText == null
                    ? 14
                    : _bodyText.fontSize;

            float maximumWidth =
                PanelWidth - BodyPadding * 2f;

            string fullLabel =
                prefix + name + suffix;

            if (UIFactory.GetTextWidthEstimate(
                    fullLabel,
                    fontSize) <= maximumWidth)
            {
                return fullLabel;
            }

            string ellipsis = "...";

            while (name.Length > 0)
            {
                name =
                    name.Substring(
                        0,
                        name.Length - 1)
                    .TrimEnd();

                string shortenedLabel =
                    prefix +
                    name +
                    ellipsis +
                    suffix;

                if (UIFactory.GetTextWidthEstimate(
                        shortenedLabel,
                        fontSize) <= maximumWidth)
                {
                    return shortenedLabel;
                }
            }

            return prefix + ellipsis + suffix;
        }

        private static string GetVersionLineIndent()
        {
            int fontSize =
                _bodyText == null
                    ? 14
                    : _bodyText.fontSize;

            float targetWidth =
                UIFactory.GetTextWidthEstimate(
                    "\u2022 ",
                    fontSize);

            StringBuilder indent =
                new StringBuilder();

            while (indent.Length < 8 &&
                UIFactory.GetTextWidthEstimate(
                    indent.ToString(),
                    fontSize) < targetWidth)
            {
                indent.Append(" ");
            }

            return indent.ToString();
        }

        private static string BuildBodyText(
            IList<AvailableUpdate> updates,
            int displayedCount)
        {
            StringBuilder builder =
                new StringBuilder();
            string versionLineIndent =
                GetVersionLineIndent();

            for (int i = 0;
                i < displayedCount;
                i++)
            {
                AvailableUpdate update =
                    updates[i];

                if (i > 0)
                {
                    builder.Append("\n\n");
                }

                builder.Append(
                    BuildModLabel(update));
                builder.Append("\n");
                builder.Append(versionLineIndent);
                builder.Append("v");
                builder.Append(update.InstalledVersion);
                builder.Append(" \u2192 <b>v");
                builder.Append(update.AvailableVersion);
                builder.Append("</b>");
            }

            if (updates.Count > displayedCount)
            {
                builder.Append("\n\n");
                builder.Append("+");
                builder.Append(
                    updates.Count - displayedCount);

                builder.Append(" ");

                builder.Append(
                    UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.MoreUpdatesId));
            }

            return builder.ToString();
        }
    }
}
