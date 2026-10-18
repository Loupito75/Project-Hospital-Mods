using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalModUpdateChecker
{
    internal static class TitleScreenUpdatePanel
    {
        private const float PanelWidth = 725f;
        private const float PanelHeightWithNews = 875f;
        private const float PanelHeightWithoutNews = 637.5f;
        private const float HeaderHeight = 44f;
        private const float OuterPadding = 14f;
        private const float SectionGap = 12f;
        private const float SectionHeaderHeight = 34f;
        private const float SectionContentGap = 8f;
        private const float ScrollbarWidth = 16f;
        private const float ContentPadding = 8f;

        private const float NewsSectionHeight = 230f;
        private const float UpdatesSectionHeight = 272f;
        private const float PluginsSectionHeight = 272f;

        private const float UpdateRowHeight = 88f;
        private const float UpdateRowGap = 8f;
        private const float ActionButtonWidth = 112f;
        private const float ActionButtonHeight = 30f;
        private const float ProgressBarHeight = 9f;

        private sealed class SectionView
        {
            internal GameObject Root;
            internal Text Content;
            internal RectTransform Viewport;
            internal RectTransform ContentRect;
            internal ScrollRect ScrollRect;
        }

        private sealed class UpdateRowView
        {
            internal AvailableUpdate Update;
            internal GameObject Root;
            internal Text Text;
            internal Button Button;
            internal Text ButtonText;
            internal GameObject ProgressRoot;
            internal RectTransform ProgressFill;
        }

        private sealed class LoadedPluginDisplayEntry
        {
            internal string Name;
            internal string Author;
            internal Version Version;
            internal PluginLogStatus Status;
            internal PluginInfo Plugin;
        }

        private static readonly List<LoadedPluginDisplayEntry> _pluginEntries =
            new List<LoadedPluginDisplayEntry>();
        private static readonly List<GameObject> _pluginRows =
            new List<GameObject>();
        private static GameObject _configGearTooltip;
        private static Text _configGearTooltipText;

        private static TitleScreenController _controller;
        private static GameObject _panel;
        private static Text _sourceText;
        private static SectionView _newsSection;
        private static SectionView _updatesSection;
        private static SectionView _pluginsSection;
        private static readonly List<UpdateRowView> _updateRows =
            new List<UpdateRowView>();
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

            PluginLogStatusTracker.LoadExistingLog();

            DestroyPanel();
            Refresh();
        }

        internal static void Refresh()
        {
            if (_controller == null ||
                _controller.m_canvas == null ||
                _controller.m_modErrorText == null ||
                _dismissedForSession)
            {
                return;
            }

            if (!EnsurePanel())
            {
                return;
            }

            SetSectionText(
                _newsSection,
                BuildNewsText());

            RefreshUpdatesSection();

            SetSectionText(
                _pluginsSection,
                BuildLoadedPluginsText());
            RefreshPluginRows();

            _panel.SetActive(true);
        }

        internal static void RefreshInstallProgress()
        {
            if (_panel == null ||
                !_panel.activeSelf)
            {
                return;
            }

            for (int i = 0; i < _updateRows.Count; i++)
            {
                UpdateRowView row = _updateRows[i];

                if (row != null &&
                    row.Update != null)
                {
                    ApplyUpdateRowState(
                        row,
                        row.Update);
                }
            }
            RecommendedModsPanel.UpdateState();
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
                    "HMUC dashboard could not use m_modErrorText because it has no Text component.");
                return false;
            }

            _sourceText = sourceText;

            _panel = new GameObject(
                "Loupito75.HospitalModUpdateChecker.Dashboard");

            _panel.transform.SetParent(
                _controller.m_canvas.transform,
                false);

            RectTransform panelRect =
                _panel.AddComponent<RectTransform>();

            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta =
                new Vector2(
                    PanelWidth,
                    UpdateCheckerConfig.ShowNews
                        ? PanelHeightWithNews
                        : PanelHeightWithoutNews);

            Image panelImage = _panel.AddComponent<Image>();
            panelImage.color = GetPanelColor();
            panelImage.raycastTarget = true;

            CreateHeader(sourceText);

            float y = -HeaderHeight - OuterPadding;

            if (UpdateCheckerConfig.ShowNews)
            {
                _newsSection = CreateSection(
                    sourceText,
                    "NewsSection",
                    UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.NewsTitleId),
                    y,
                    NewsSectionHeight);

                y -= NewsSectionHeight + SectionGap;
            }

            _updatesSection = CreateSection(
                sourceText,
                "UpdatesSection",
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.UpdatesTitleId),
                y,
                UpdatesSectionHeight);

            y -= UpdatesSectionHeight + SectionGap;

            _pluginsSection = CreateSection(
                sourceText,
                "PluginsSection",
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.PluginsTitleId),
                y,
                PluginsSectionHeight);

            if ((UpdateCheckerConfig.ShowNews &&
                 _newsSection == null) ||
                _updatesSection == null ||
                _pluginsSection == null)
            {
                DestroyPanel();
                return false;
            }

            _panel.SetActive(false);
            return true;
        }

        private static void CreateHeader(Text sourceText)
        {
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

            RectTransform headingTextRect =
                headingText.GetComponent<RectTransform>();

            headingTextRect.anchorMin = Vector2.zero;
            headingTextRect.anchorMax = Vector2.one;
            headingTextRect.pivot = new Vector2(0.5f, 0.5f);
            headingTextRect.anchoredPosition =
                new Vector2(14f, 0f);
            headingTextRect.sizeDelta =
                new Vector2(-58f, 0f);

            headingText.text =
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.DashboardTitleId);

            headingText.alignment = TextAnchor.MiddleLeft;
            headingText.fontStyle = FontStyle.Bold;
            headingText.fontSize =
                Math.Max(sourceText.fontSize, 16);
            headingText.raycastTarget = false;

            CreateCloseButton(
                sourceText,
                heading.transform);
        }

        private static SectionView CreateSection(
            Text sourceText,
            string name,
            string title,
            float y,
            float height)
        {
            GameObject root =
                new GameObject(name);

            root.transform.SetParent(
                _panel.transform,
                false);

            RectTransform rootRect =
                root.AddComponent<RectTransform>();

            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition =
                new Vector2(OuterPadding, y);
            rootRect.sizeDelta =
                new Vector2(
                    PanelWidth - OuterPadding * 2f,
                    height);

            Image background =
                root.AddComponent<Image>();

            background.color = GetSectionColor();
            background.raycastTarget = true;

            GameObject sectionHeader =
                new GameObject("SectionHeader");

            sectionHeader.transform.SetParent(
                root.transform,
                false);

            RectTransform sectionHeaderRect =
                sectionHeader.AddComponent<RectTransform>();

            sectionHeaderRect.anchorMin = new Vector2(0f, 1f);
            sectionHeaderRect.anchorMax = new Vector2(1f, 1f);
            sectionHeaderRect.pivot = new Vector2(0.5f, 1f);
            sectionHeaderRect.anchoredPosition = Vector2.zero;
            sectionHeaderRect.sizeDelta =
                new Vector2(0f, SectionHeaderHeight);

            Image sectionHeaderImage =
                sectionHeader.AddComponent<Image>();

            sectionHeaderImage.color =
                GetSectionHeaderColor();
            sectionHeaderImage.raycastTarget = false;

            Text titleText =
                CreateTextClone(
                    sourceText,
                    "Title",
                    sectionHeader.transform);

            RectTransform titleRect =
                titleText.GetComponent<RectTransform>();

            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition =
                new Vector2(ContentPadding, 0f);
            titleRect.sizeDelta =
                new Vector2(-ContentPadding * 2f, 0f);

            titleText.text = title;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.fontStyle = FontStyle.Bold;
            titleText.fontSize =
                Math.Max(sourceText.fontSize + 1, 15);
            titleText.raycastTarget = false;

            GameObject viewportObject =
                new GameObject("Viewport");

            viewportObject.transform.SetParent(
                root.transform,
                false);

            RectTransform viewportRect =
                viewportObject.AddComponent<RectTransform>();

            viewportRect.anchorMin = new Vector2(0f, 1f);
            viewportRect.anchorMax = new Vector2(1f, 1f);
            viewportRect.pivot = new Vector2(0.5f, 1f);
            // Keep the content axis aligned with the section title while
            // preserving the existing gap before the scrollbar.
            viewportRect.anchoredPosition =
                new Vector2(
                    -ScrollbarWidth * 0.5f + ContentPadding * 0.5f,
                    -(SectionHeaderHeight + SectionContentGap));

            viewportRect.sizeDelta =
                new Vector2(
                    -ScrollbarWidth - ContentPadding * 3f,
                    height -
                    SectionHeaderHeight -
                    SectionContentGap -
                    ContentPadding);

            viewportObject.AddComponent<RectMask2D>();

            Text content =
                CreateTextClone(
                    sourceText,
                    "Content",
                    viewportObject.transform);

            RectTransform contentRect =
                content.GetComponent<RectTransform>();

            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            content.alignment = TextAnchor.UpperLeft;
            content.horizontalOverflow =
                HorizontalWrapMode.Wrap;
            content.verticalOverflow =
                VerticalWrapMode.Overflow;
            content.supportRichText = true;
            content.raycastTarget = false;
            content.fontSize =
                Math.Max(sourceText.fontSize, 13);

            Scrollbar scrollbar =
                CreateScrollbar(
                    root.transform,
                    height -
                    SectionHeaderHeight -
                    SectionContentGap -
                    ContentPadding,
                    SectionHeaderHeight + SectionContentGap);

            ScrollRect scrollRect =
                root.AddComponent<ScrollRect>();

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType =
                ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 24f;
            scrollRect.verticalScrollbar = scrollbar;

            return new SectionView
            {
                Root = root,
                Content = content,
                Viewport = viewportRect,
                ContentRect = contentRect,
                ScrollRect = scrollRect
            };
        }

        private static Scrollbar CreateScrollbar(
            Transform parent,
            float height,
            float topOffset)
        {
            GameObject scrollbarObject =
                new GameObject("Scrollbar");

            scrollbarObject.transform.SetParent(
                parent,
                false);

            RectTransform scrollbarRect =
                scrollbarObject.AddComponent<RectTransform>();

            scrollbarRect.anchorMin = new Vector2(1f, 1f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 1f);
            scrollbarRect.anchoredPosition =
                new Vector2(-ContentPadding, -topOffset);
            scrollbarRect.sizeDelta =
                new Vector2(ScrollbarWidth, height);

            Image background =
                scrollbarObject.AddComponent<Image>();

            Color backgroundColor = Color.black;
            backgroundColor.a = 0.20f;
            background.color = backgroundColor;

            GameObject slidingArea =
                new GameObject("SlidingArea");

            slidingArea.transform.SetParent(
                scrollbarObject.transform,
                false);

            RectTransform slidingRect =
                slidingArea.AddComponent<RectTransform>();

            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.pivot = new Vector2(0.5f, 0.5f);
            slidingRect.anchoredPosition = Vector2.zero;
            slidingRect.sizeDelta = new Vector2(-4f, -4f);

            GameObject handle =
                new GameObject("Handle");

            handle.transform.SetParent(
                slidingArea.transform,
                false);

            RectTransform handleRect =
                handle.AddComponent<RectTransform>();

            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = Vector2.zero;

            Image handleImage =
                handle.AddComponent<Image>();

            Color handleColor = Color.white;
            handleColor.a = 0.60f;
            handleImage.color = handleColor;

            Scrollbar scrollbar =
                scrollbarObject.AddComponent<Scrollbar>();

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction =
                Scrollbar.Direction.BottomToTop;

            return scrollbar;
        }

        private static void SetSectionText(
            SectionView section,
            string text)
        {
            if (section == null ||
                section.Content == null ||
                section.ContentRect == null ||
                section.Viewport == null)
            {
                return;
            }

            section.Content.text = text ?? string.Empty;

            Canvas.ForceUpdateCanvases();

            float height = Math.Max(
                section.Viewport.rect.height,
                section.Content.preferredHeight + ContentPadding);

            section.ContentRect.sizeDelta =
                new Vector2(0f, height);

            section.ScrollRect.verticalNormalizedPosition = 1f;
        }

        private static void RefreshUpdatesSection()
        {
            DestroyUpdateRows();
            RecommendedModsPanel.Destroy();

            if (_updatesSection == null)
                return;

            IList<AvailableUpdate> updates = UpdateChecker.AvailableUpdates;
            bool hasUpdates =
                UpdateChecker.UpdateState == RemoteContentState.Ready &&
                updates.Count > 0;

            string summary = hasUpdates
                ? BuildInstallResultsText()
                : BuildUpdatesText();

            _updatesSection.Content.text = summary ?? string.Empty;
            Canvas.ForceUpdateCanvases();

            float summaryHeight =
                string.IsNullOrEmpty(summary) ? 0f
                : _updatesSection.Content.preferredHeight + 8f;

            float rowsHeight = hasUpdates
                ? updates.Count * (UpdateRowHeight + UpdateRowGap)
                : 0f;

            // The recommendations are part of the existing Updates scroll
            // area, below the regular update rows or the empty-state text.
            float recommendationsTop = summaryHeight + rowsHeight + 6f;
            float recommendedHeight = RecommendedModsPanel.Create(
                _sourceText, _updatesSection.ContentRect,
                _updatesSection.Viewport, recommendationsTop);

            _updatesSection.ContentRect.sizeDelta =
                new Vector2(0f, Math.Max(
                    _updatesSection.Viewport.rect.height,
                    recommendationsTop + recommendedHeight + 8f));

            if (hasUpdates)
            {
                for (int i = 0; i < updates.Count; i++)
                    CreateUpdateRow(updates[i],
                        summaryHeight +
                        i * (UpdateRowHeight + UpdateRowGap));
            }

            _updatesSection.ScrollRect.verticalNormalizedPosition = 1f;
        }

        private static void CreateUpdateRow(
            AvailableUpdate update,
            float top)
        {
            if (update == null ||
                _sourceText == null)
            {
                return;
            }

            GameObject root =
                new GameObject(
                    "Update_" + update.Guid);

            root.transform.SetParent(
                _updatesSection.ContentRect,
                false);

            RectTransform rect =
                root.AddComponent<RectTransform>();

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(0f, UpdateRowHeight);

            Image background =
                root.AddComponent<Image>();

            Color rowColor =
                GetHeadingColor();
            rowColor.a = 0.22f;
            background.color = rowColor;
            background.raycastTarget = false;

            Text text =
                CreateTextClone(
                    _sourceText,
                    "UpdateText",
                    root.transform);

            RectTransform textRect =
                text.GetComponent<RectTransform>();

            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            float textLeftInset = 0f;
            float textRightInset =
                ActionButtonWidth +
                ContentPadding * 2f;

            textRect.anchoredPosition =
                new Vector2(
                    (textLeftInset - textRightInset) * 0.5f,
                    ProgressBarHeight * 0.5f);
            textRect.sizeDelta =
                new Vector2(
                    -(textLeftInset + textRightInset),
                    -(ProgressBarHeight + 8f));

            text.alignment = TextAnchor.UpperLeft;
            text.fontSize =
                Math.Max(_sourceText.fontSize, 13);
            text.supportRichText = true;

            Button button;
            Text buttonText;

            CreateActionButton(
                root.transform,
                update,
                out button,
                out buttonText);

            GameObject progressRoot =
                new GameObject("Progress");

            progressRoot.transform.SetParent(
                root.transform,
                false);

            RectTransform progressRect =
                progressRoot.AddComponent<RectTransform>();

            progressRect.anchorMin = new Vector2(0f, 0f);
            progressRect.anchorMax = new Vector2(1f, 0f);
            progressRect.pivot = new Vector2(0.5f, 0f);
            progressRect.anchoredPosition = new Vector2(0f, 3f);
            progressRect.sizeDelta =
                new Vector2(0f, ProgressBarHeight);

            Image progressBackground =
                progressRoot.AddComponent<Image>();

            Color progressBackgroundColor =
                GetHeadingColor();
            progressBackgroundColor.a = 0.34f;
            progressBackground.color = progressBackgroundColor;
            progressBackground.raycastTarget = false;

            GameObject fill =
                new GameObject("Fill");

            fill.transform.SetParent(
                progressRoot.transform,
                false);

            RectTransform fillRect =
                fill.AddComponent<RectTransform>();

            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = Vector2.zero;

            Image fillImage =
                fill.AddComponent<Image>();

            Color fillColor =
                GetSectionHeaderColor();
            fillColor.a = 1f;
            fillImage.color = fillColor;
            fillImage.raycastTarget = false;

            UpdateRowView view =
                new UpdateRowView
                {
                    Update = update,
                    Root = root,
                    Text = text,
                    Button = button,
                    ButtonText = buttonText,
                    ProgressRoot = progressRoot,
                    ProgressFill = fillRect
                };

            _updateRows.Add(view);

            ApplyUpdateRowState(
                view,
                update);
        }

        private static void CreateActionButton(
            Transform parent,
            AvailableUpdate update,
            out Button button,
            out Text buttonText)
        {
            GameObject buttonObject =
                new GameObject("ActionButton");

            buttonObject.transform.SetParent(
                parent,
                false);

            RectTransform rect =
                buttonObject.AddComponent<RectTransform>();

            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition =
                new Vector2(
                    -ContentPadding,
                    -ContentPadding);
            rect.sizeDelta =
                new Vector2(
                    ActionButtonWidth,
                    ActionButtonHeight);

            Image image =
                buttonObject.AddComponent<Image>();

            Color color =
                GetSectionHeaderColor();
            color.a = 0.95f;
            image.color = color;

            button =
                buttonObject.AddComponent<Button>();

            button.targetGraphic = image;

            AvailableUpdate captured =
                update;

            button.onClick.AddListener(
                delegate
                {
                    PackageUpdateManager.StartOrCancel(
                        captured);
                });

            buttonText =
                CreateTextClone(
                    _sourceText,
                    "Text",
                    buttonObject.transform);

            RectTransform textRect =
                buttonText.GetComponent<RectTransform>();

            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = Vector2.zero;

            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.fontStyle = FontStyle.Bold;
            buttonText.fontSize =
                Math.Max(_sourceText.fontSize, 12);
            buttonText.raycastTarget = false;
        }

        private static void ApplyUpdateRowState(
            UpdateRowView row,
            AvailableUpdate update)
        {
            PackageOperation operation =
                PackageUpdateManager.GetOperation(
                    update.Guid);

            string status =
                GetUpdateStatus(
                    update,
                    operation);

            StringBuilder builder =
                new StringBuilder();

            builder.Append("\u2022 ");
            builder.Append(update.Name);
            builder.Append(" (");
            builder.Append(update.Author);
            builder.Append(")\n   v");
            builder.Append(update.InstalledVersion);
            builder.Append(" \u2192 <b>v");
            builder.Append(update.AvailableVersion);
            builder.Append("</b>");

            if (!string.IsNullOrEmpty(status))
            {
                builder.Append("\n<i>");
                builder.Append(status);
                builder.Append("</i>");
            }

            row.Text.text =
                builder.ToString();

            bool showProgress =
                operation != null &&
                (operation.State ==
                    PackageOperationState.Downloading ||
                 operation.State ==
                    PackageOperationState.Verifying ||
                 operation.State ==
                    PackageOperationState.Extracting ||
                 operation.State ==
                    PackageOperationState.PendingRestart);

            row.ProgressRoot.SetActive(
                showProgress);

            float progress =
                operation == null
                    ? 0f
                    : Mathf.Clamp01(
                        operation.Progress);

            row.ProgressFill.anchorMax =
                new Vector2(
                    progress,
                    1f);

            string buttonLabel =
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.UpdateActionId);

            bool interactable = false;

            if (operation != null)
            {
                if (operation.State ==
                    PackageOperationState.Downloading)
                {
                    buttonLabel =
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.CancelActionId);

                    interactable = true;
                }
                else if (operation.State ==
                    PackageOperationState.Error ||
                    operation.State ==
                    PackageOperationState.Cancelled)
                {
                    buttonLabel =
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.RetryActionId);

                    interactable =
                        PackageUpdateManager.IsPackageAvailable(
                            update) &&
                        !PackageUpdateManager.IsAnotherOperationRunning(
                            update.Guid);
                }
                else if (operation.State ==
                    PackageOperationState.PendingRestart)
                {
                    buttonLabel =
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.QueuedActionId);
                }
                else
                {
                    buttonLabel =
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.WorkingActionId);
                }
            }
            else
            {
                if ((PackageUpdateManager.CatalogState ==
                        PackageCatalogState.Error ||
                     PackageUpdateManager.CatalogState ==
                        PackageCatalogState.NotLoaded) &&
                    !UpdateCheckerConfig.Debug)
                {
                    buttonLabel =
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.RetryActionId);

                    interactable =
                        !PackageUpdateManager.IsAnotherOperationRunning(
                            update.Guid);
                }
                else
                {
                    interactable =
                        PackageUpdateManager.IsPackageAvailable(
                            update) &&
                        !PackageUpdateManager.IsAnotherOperationRunning(
                            update.Guid);
                }
            }

            row.Button.interactable =
                interactable;
            row.ButtonText.text =
                buttonLabel;
        }

        private static string GetUpdateStatus(
            AvailableUpdate update,
            PackageOperation operation)
        {
            if (operation != null)
            {
                if (operation.State ==
                    PackageOperationState.Downloading)
                {
                    int percent =
                        (int)Math.Round(
                            Math.Min(
                                1f,
                                operation.Progress / 0.72f) *
                            100f);

                    return
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.DownloadingId) +
                        " " +
                        percent.ToString() +
                        "%";
                }

                if (operation.State ==
                    PackageOperationState.Verifying)
                {
                    return UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.VerifyingId);
                }

                if (operation.State ==
                    PackageOperationState.Extracting)
                {
                    return UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.PreparingInstallId);
                }

                if (operation.State ==
                    PackageOperationState.PendingRestart)
                {
                    return "<b>" +
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.RestartRequiredId) +
                        "</b>";
                }

                if (operation.State ==
                    PackageOperationState.Cancelled)
                {
                    return UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.CancelledId);
                }

                if (operation.State ==
                    PackageOperationState.Error)
                {
                    return string.IsNullOrEmpty(
                            operation.Error)
                        ? UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.DownloadErrorId)
                        : operation.Error;
                }
            }

            if (PackageUpdateManager.CatalogState ==
                PackageCatalogState.Loading)
            {
                return UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.PackageCatalogLoadingId);
            }

            if (PackageUpdateManager.CatalogState ==
                    PackageCatalogState.Error &&
                !UpdateCheckerConfig.Debug)
            {
                return UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.PackageCatalogErrorId);
            }

            if (PackageUpdateManager.IsAnotherOperationRunning(
                    update.Guid))
            {
                return UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.UpdateBusyId);
            }

            if (!PackageUpdateManager.IsPackageAvailable(
                    update))
            {
                return UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.PackageUnavailableId);
            }

            return UpdatePanelLocalization.Get(
                UpdatePanelLocalization.ReadyToUpdateId);
        }

        private static string BuildInstallResultsText()
        {
            IList<InstallResultDisplay> results =
                PackageUpdateManager.LastResults;

            if (results.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder =
                new StringBuilder();

            for (int i = 0; i < results.Count; i++)
            {
                InstallResultDisplay result =
                    results[i];

                if (result == null)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("\n\n");
                }

                builder.Append("\u2022 <b>");
                builder.Append(result.Name);
                builder.Append(" ");
                builder.Append(result.Version);
                builder.Append("</b>");

                if (string.Equals(
                        result.Status,
                        "failure",
                        StringComparison.Ordinal))
                {
                    builder.Append("\n   <i>");
                    builder.Append(
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.InstallFailedId));
                    builder.Append("</i>");
                    continue;
                }

                builder.Append("\n   <i>");
                builder.Append(
                    UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.InstallSucceededId));
                builder.Append("</i>");

                if (result.AddedSettings.Count > 0)
                {
                    builder.Append("\n   ");
                    builder.Append(
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.ConfigAddedId));

                    for (int j = 0;
                        j < result.AddedSettings.Count;
                        j++)
                    {
                        builder.Append("\n      \u2022 ");
                        builder.Append(
                            result.AddedSettings[j]);
                    }
                }

                if (!string.IsNullOrEmpty(
                        result.ConfigWarning))
                {
                    builder.Append("\n   <i>");
                    builder.Append(
                        UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.ConfigWarningId));
                    builder.Append("</i>");
                }
            }

            return builder.ToString();
        }

        private static void DestroyUpdateRows()
        {
            for (int i = 0; i < _updateRows.Count; i++)
            {
                if (_updateRows[i] != null &&
                    _updateRows[i].Root != null)
                {
                    UnityEngine.Object.Destroy(
                        _updateRows[i].Root);
                }
            }

            _updateRows.Clear();
        }

        private static string BuildNewsText()
        {
            if (UpdateChecker.NewsState == RemoteContentState.Loading)
            {
                return UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.LoadingId);
            }

            if (UpdateChecker.NewsState == RemoteContentState.Error)
            {
                return UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.NewsErrorId);
            }

            if (UpdateChecker.NewsState == RemoteContentState.Empty)
            {
                return UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.NoNewsId);
            }

            StringBuilder builder =
                new StringBuilder();

            IList<NewsManifestEntry> items =
                UpdateChecker.NewsItems;

            for (int i = 0; i < items.Count; i++)
            {
                NewsManifestEntry item = items[i];

                if (item == null)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("\n\n");
                }

                builder.Append("\u2022 <b>");
                builder.Append(item.Title);
                builder.Append("</b>\n   ");
                AppendIndentedMultilineText(
                    builder,
                    item.Text);
            }

            return builder.Length == 0
                ? UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.NoNewsId)
                : builder.ToString();
        }

        private static string BuildUpdatesText()
        {
            StringBuilder builder =
                new StringBuilder();

            string results =
                BuildInstallResultsText();

            if (!string.IsNullOrEmpty(results))
            {
                builder.Append(results);
                builder.Append("\n\n");
            }

            if (UpdateChecker.UpdateState ==
                RemoteContentState.Loading)
            {
                builder.Append("<i>");
                builder.Append(
                    UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.LoadingId));
                builder.Append("</i>");
                return builder.ToString();
            }

            if (UpdateChecker.UpdateState ==
                RemoteContentState.Error)
            {
                builder.Append("<i>");
                builder.Append(
                    UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.UpdateErrorId));
                builder.Append("</i>");
                return builder.ToString();
            }

            if (UpdateChecker.UpdateState ==
                RemoteContentState.Empty)
            {
                builder.Append("<i>");
                builder.Append(
                    UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.NoUpdatesId));
                builder.Append("</i>");
                return builder.ToString();
            }

            if (UpdateChecker.UpdateState ==
                RemoteContentState.Cached)
            {
                builder.Append("<i>");
                builder.Append(
                    UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.NoUpdatesCachedId));
                builder.Append("</i>");
                return builder.ToString();
            }

            if (builder.Length == 0)
            {
                builder.Append("<i>");
                builder.Append(
                    UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.NoUpdatesId));
                builder.Append("</i>");
            }

            return builder.ToString();
        }

        private static string BuildLoadedPluginsText()
        {
            List<LoadedPluginDisplayEntry> entries =
                new List<LoadedPluginDisplayEntry>();

            if (UpdateCheckerConfig.Debug)
            {
                entries.Add(
                    new LoadedPluginDisplayEntry
                    {
                        Name = "HMUC Test Mod Alpha",
                        Author = "Test Author",
                        Version = new Version(0, 9, 0),
                        Status = PluginLogStatus.Warning
                    });

                entries.Add(
                    new LoadedPluginDisplayEntry
                    {
                        Name = "HMUC Test Mod Beta",
                        Author = UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.UnknownAuthorId),
                        Version = new Version(0, 4, 2),
                        Status = PluginLogStatus.Error
                    });
            }

            foreach (PluginInfo plugin in
                Chainloader.PluginInfos.Values)
            {
                if (plugin == null ||
                    plugin.Metadata == null)
                {
                    continue;
                }

                entries.Add(
                    new LoadedPluginDisplayEntry
                    {
                        Name = plugin.Metadata.Name,
                        Author = GetPluginAuthor(plugin),
                        Plugin = plugin,
                        Version = plugin.Metadata.Version,
                        Status =
                            PluginLogStatusTracker.GetStatus(plugin)
                    });
            }

            entries.Sort(delegate(
                LoadedPluginDisplayEntry left,
                LoadedPluginDisplayEntry right)
            {
                int statusComparison =
                    GetStatusSortOrder(left.Status)
                    .CompareTo(
                        GetStatusSortOrder(right.Status));

                if (statusComparison != 0)
                {
                    return statusComparison;
                }

                int nameComparison =
                    string.Compare(
                        left.Name ?? string.Empty,
                        right.Name ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase);

                if (nameComparison != 0)
                {
                    return nameComparison;
                }

                return string.Compare(
                    left.Author ?? string.Empty,
                    right.Author ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            });

            _pluginEntries.Clear();
            _pluginEntries.AddRange(entries);

            StringBuilder builder =
                new StringBuilder();

            for (int i = 0; i < entries.Count; i++)
            {
                LoadedPluginDisplayEntry entry =
                    entries[i];

                AppendLoadedPluginLine(
                    builder,
                    entry.Name,
                    entry.Author,
                    entry.Version,
                    entry.Status);
            }

            return builder.Length == 0
                ? UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.NoPluginsId)
                : builder.ToString();
        }

        private static void RefreshPluginRows()
        {
            for (int i = 0; i < _pluginRows.Count; i++)
                if (_pluginRows[i] != null)
                    UnityEngine.Object.Destroy(_pluginRows[i]);
            _pluginRows.Clear();

            if (_pluginsSection == null || _sourceText == null ||
                _pluginEntries.Count == 0)
                return;

            // One stable UI row per plugin, independent of text line wrapping.
            _pluginsSection.Content.text = string.Empty;
            const float rowHeight = 57f;
            const float gap = 5f;
            float height = Math.Max(
                _pluginsSection.Viewport.rect.height,
                _pluginEntries.Count * (rowHeight + gap));
            _pluginsSection.ContentRect.sizeDelta =
                new Vector2(0f, height);

            for (int i = 0; i < _pluginEntries.Count; i++)
            {
                LoadedPluginDisplayEntry entry = _pluginEntries[i];
                GameObject row = new GameObject("Plugin_" + i);
                row.transform.SetParent(_pluginsSection.ContentRect, false);
                RectTransform rect = row.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(.5f, 1f);
                rect.anchoredPosition =
                    new Vector2(0f, -i * (rowHeight + gap));
                rect.sizeDelta = new Vector2(0f, rowHeight);
                Image background = row.AddComponent<Image>();
                Color rowColor = GetHeadingColor();
                rowColor.a = .23f;
                background.color = rowColor;
                background.raycastTarget = false;

                string configPath;
                bool configurable = entry.Plugin != null &&
                    ConfigEditorData.TryFindConfig(
                        entry.Plugin, out configPath);
                Text description = CreateTextClone(
                    _sourceText, "PluginDescription", row.transform);
                RectTransform textRect = description.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(0f, 2f);
                textRect.offsetMax =
                    new Vector2(configurable ? -118f : -8f, -2f);
                description.alignment = TextAnchor.MiddleLeft;
                description.supportRichText = true;
                description.fontSize = Math.Max(
                    _sourceText.fontSize, 12);
                description.raycastTarget = false;

                StringBuilder text = new StringBuilder();
                AppendLoadedPluginLine(text, entry.Name, entry.Author,
                    entry.Version, entry.Status);
                description.text = text.ToString();

                if (configurable)
                {
                    GameObject buttonRoot =
                        new GameObject("Configure");
                    buttonRoot.transform.SetParent(row.transform, false);
                    RectTransform buttonRect =
                        buttonRoot.AddComponent<RectTransform>();
                    buttonRect.anchorMin =
                        buttonRect.anchorMax = new Vector2(1f, .5f);
                    buttonRect.pivot = new Vector2(1f, .5f);
                    buttonRect.anchoredPosition = new Vector2(-7f, 0f);
                    buttonRect.sizeDelta = new Vector2(104f, 30f);
                    Image image = buttonRoot.AddComponent<Image>();
                    image.color = GetSectionHeaderColor();
                    Button button = buttonRoot.AddComponent<Button>();
                    button.targetGraphic = image;
                    // Native delay is safe on the title-screen; the tooltip
                    // itself uses the HMUC canvas rather than a gameplay prefab.
                    buttonRoot.AddComponent<HoverTooltipDelay>();
                    buttonRoot.AddComponent<ConfigGearTooltipHover>();
                    PluginInfo selected = entry.Plugin;
                    button.onClick.AddListener(delegate
                    {
                        HideConfigGearTooltip();
                        ConfigEditorPanel.Open(selected,
                            _controller.m_canvas.transform,
                            _sourceText);
                    });
                    Text caption = CreateTextClone(
                        _sourceText, "ConfigLabel", buttonRoot.transform);
                    RectTransform captionRect = caption.rectTransform;
                    captionRect.anchorMin = Vector2.zero;
                    captionRect.anchorMax = Vector2.one;
                    captionRect.offsetMin = captionRect.offsetMax =
                        Vector2.zero;
                    caption.text = UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.ConfigureActionId);
                    caption.alignment = TextAnchor.MiddleCenter;
                    caption.fontSize = Math.Max(_sourceText.fontSize, 13);
                    caption.raycastTarget = false;
                }
                _pluginRows.Add(row);
            }

            _pluginsSection.ScrollRect.verticalNormalizedPosition = 1f;
        }

        internal static void ShowConfigGearTooltip()
        {
            if (_panel == null || !_panel.activeSelf ||
                _sourceText == null || _controller == null ||
                _controller.m_canvas == null || ConfigEditorPanel.IsOpen)
            {
                HideConfigGearTooltip();
                return;
            }

            if (_configGearTooltip == null)
            {
                _configGearTooltip = new GameObject("ConfigGearTooltip");
                _configGearTooltip.transform.SetParent(_panel.transform, false);

                RectTransform root =
                    _configGearTooltip.AddComponent<RectTransform>();
                root.anchorMin = new Vector2(.5f, .5f);
                root.anchorMax = new Vector2(.5f, .5f);
                root.pivot = new Vector2(.5f, .5f);
                root.sizeDelta = new Vector2(200f, 36f);

                Image image = _configGearTooltip.AddComponent<Image>();
                image.color = GetHeadingColor();
                image.raycastTarget = false;

                _configGearTooltipText = CreateTextClone(
                    _sourceText, "TooltipText", _configGearTooltip.transform);
                RectTransform textRect =
                    _configGearTooltipText.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.pivot = new Vector2(.5f, .5f);
                textRect.offsetMin = new Vector2(10f, 5f);
                textRect.offsetMax = new Vector2(-10f, -5f);
                _configGearTooltipText.alignment = TextAnchor.MiddleCenter;
                _configGearTooltipText.fontSize =
                    Math.Max(_sourceText.fontSize, 13);
                _configGearTooltipText.supportRichText = false;
                _configGearTooltipText.horizontalOverflow =
                    HorizontalWrapMode.Wrap;
                _configGearTooltipText.verticalOverflow =
                    VerticalWrapMode.Overflow;
                _configGearTooltipText.raycastTarget = false;
            }

            string label = UpdatePanelLocalization.Get(
                UpdatePanelLocalization.ConfigureTooltipId);

            RectTransform tooltipRect =
                _configGearTooltip.GetComponent<RectTransform>();
            if (_configGearTooltipText.text != label)
            {
                _configGearTooltipText.text = label;
                float width = Mathf.Clamp(
                    _configGearTooltipText.preferredWidth + 24f,
                    160f, PanelWidth - 24f);
                tooltipRect.sizeDelta = new Vector2(width, 36f);
                float height = Mathf.Max(
                    36f, _configGearTooltipText.preferredHeight + 14f);
                tooltipRect.sizeDelta = new Vector2(width, height);
            }

            Canvas canvas = _controller.m_canvas.GetComponent<Canvas>();
            Camera camera = canvas != null &&
                canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;

            RectTransform panelRect =
                _panel.GetComponent<RectTransform>();
            Vector2 pointer;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                panelRect, Input.mousePosition, camera, out pointer))
            {
                float halfWidth = panelRect.rect.width * .5f;
                float halfHeight = panelRect.rect.height * .5f;
                float tooltipHalfWidth = tooltipRect.rect.width * .5f;
                float tooltipHalfHeight = tooltipRect.rect.height * .5f;

                tooltipRect.anchoredPosition = new Vector2(
                    Mathf.Clamp(pointer.x,
                        -halfWidth + tooltipHalfWidth + 6f,
                        halfWidth - tooltipHalfWidth - 6f),
                    Mathf.Clamp(pointer.y + tooltipHalfHeight + 18f,
                        -halfHeight + tooltipHalfHeight + 6f,
                        halfHeight - tooltipHalfHeight - 6f));
            }

            _configGearTooltip.SetActive(true);
            _configGearTooltip.transform.SetAsLastSibling();
        }

        internal static void HideConfigGearTooltip()
        {
            if (_configGearTooltip != null)
                _configGearTooltip.SetActive(false);
        }

        private static int GetStatusSortOrder(
            PluginLogStatus status)
        {
            if (status == PluginLogStatus.Error)
            {
                return 0;
            }

            if (status == PluginLogStatus.Warning)
            {
                return 1;
            }

            return 2;
        }

        private static void AppendIndentedMultilineText(
            StringBuilder builder,
            string text)
        {
            string normalized =
                (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            builder.Append(
                normalized.Replace(
                    "\n",
                    "\n   "));
        }

        private static void AppendLoadedPluginLine(
            StringBuilder builder,
            string name,
            string author,
            Version version,
            PluginLogStatus status)
        {
            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append("\u2022 ");
            builder.Append(name ?? string.Empty);
            builder.Append(" (");
            builder.Append(author ?? string.Empty);
            builder.Append(")");
            builder.Append("\n   v");
            builder.Append(version);
            builder.Append(" : ");
            builder.Append(FormatLogStatus(status));
        }

        private static string FormatLogStatus(
            PluginLogStatus status)
        {
            if (status == PluginLogStatus.Error)
            {
                return "<b><color=#FF7777>" +
                    UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.StatusErrorId) +
                    "</color></b>";
            }

            if (status == PluginLogStatus.Warning)
            {
                return "<b><color=#FFD166>" +
                    UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.StatusWarningId) +
                    "</color></b>";
            }

            return "<b><color=#A7E8A1>" +
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.StatusLoadedId) +
                "</color></b>";
        }

        private static string GetPluginAuthor(
            PluginInfo plugin)
        {
            if (plugin == null ||
                plugin.Metadata == null)
            {
                return UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.UnknownAuthorId);
            }

            string guid = plugin.Metadata.GUID ?? string.Empty;

            if (guid.StartsWith(
                    "loupito75.",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Loupito75";
            }

            try
            {
                if (plugin.Instance != null)
                {
                    object[] attributes =
                        plugin.Instance
                            .GetType()
                            .Assembly
                            .GetCustomAttributes(
                                typeof(AssemblyCompanyAttribute),
                                false);

                    if (attributes != null &&
                        attributes.Length > 0)
                    {
                        AssemblyCompanyAttribute company =
                            attributes[0] as AssemblyCompanyAttribute;

                        string author =
                            company == null
                                ? string.Empty
                                : company.Company;

                        if (!string.IsNullOrEmpty(author))
                        {
                            author = author.Trim();

                            if (author.Length > 0 &&
                                author.Length <= 128 &&
                                author.IndexOf('<') < 0 &&
                                author.IndexOf('>') < 0)
                            {
                                return author;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return UpdatePanelLocalization.Get(
                UpdatePanelLocalization.UnknownAuthorId);
        }

        private static Text CreateTextClone(
            Text sourceText,
            string name,
            Transform parent)
        {
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

            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition =
                new Vector2(-6f, 0f);
            rect.sizeDelta =
                new Vector2(34f, 34f);

            Image image =
                buttonObject.AddComponent<Image>();

            Color buttonColor = GetHeadingColor();
            buttonColor.a =
                Math.Min(1f, buttonColor.a + 0.15f);
            image.color = buttonColor;

            Button button =
                buttonObject.AddComponent<Button>();

            button.onClick.AddListener(delegate
            {
                _dismissedForSession = true;
                ConfigEditorPanel.Close();

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
            xText.fontStyle = FontStyle.Bold;
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

                color.a = 0.97f;
                return color;
            }

            return new Color(
                0.08f,
                0.08f,
                0.10f,
                0.97f);
        }

        private static Color GetHeadingColor()
        {
            if (UISettings.Instance != null)
            {
                return UISettings.Instance
                    .THEME_COLOR_PANEL_HEADING;
            }

            return new Color(
                0.16f,
                0.16f,
                0.20f,
                1f);
        }

        private static Color GetSectionColor()
        {
            Color color = GetHeadingColor();
            color.a = 0.38f;
            return color;
        }

        private static Color GetSectionHeaderColor()
        {
            Color color = GetHeadingColor();
            color.a = 0.82f;
            return color;
        }

        internal static void BringNativeScreenToFront(
            GameObject screen)
        {
            if (screen == null)
            {
                return;
            }

            if (_controller != null &&
                _controller.m_canvas != null)
            {
                Transform canvas =
                    _controller.m_canvas.transform;

                Transform current =
                    screen.transform;

                while (current.parent != null &&
                    current.parent != canvas)
                {
                    current = current.parent;
                }

                if (current.parent == canvas)
                {
                    current.SetAsLastSibling();
                    return;
                }
            }

            screen.transform.SetAsLastSibling();
        }

        private static void DestroyPanel()
        {
            RecommendedModsPanel.Destroy();
            HideConfigGearTooltip();
            _configGearTooltip = null;
            _configGearTooltipText = null;
            ConfigEditorPanel.Close();
            DestroyUpdateRows();
            for (int i = 0; i < _pluginRows.Count; i++)
                if (_pluginRows[i] != null)
                    UnityEngine.Object.Destroy(_pluginRows[i]);
            _pluginRows.Clear();
            _pluginEntries.Clear();

            if (_panel != null)
            {
                UnityEngine.Object.Destroy(_panel);
            }

            _panel = null;
            _sourceText = null;
            _newsSection = null;
            _updatesSection = null;
            _pluginsSection = null;
        }
    }
    // Shared tooltip is rendered outside the masked plugin scroll area.
    // HoverTooltipDelay is the native game's delay and pointer tracker.
    internal sealed class ConfigGearTooltipHover : MonoBehaviour
    {
        private HoverTooltipDelay _hover;
        private bool _showing;

        private void Awake()
        {
            _hover = GetComponent<HoverTooltipDelay>();
        }

        private void Update()
        {
            if (_hover != null && _hover.IsMouseOver() &&
                _hover.ShouldShowToolTip())
            {
                TitleScreenUpdatePanel.ShowConfigGearTooltip();
                _showing = true;
            }
            else if (_showing)
            {
                TitleScreenUpdatePanel.HideConfigGearTooltip();
                _showing = false;
            }
        }

        private void OnDisable()
        {
            TitleScreenUpdatePanel.HideConfigGearTooltip();
            _showing = false;
        }

        private void OnDestroy()
        {
            TitleScreenUpdatePanel.HideConfigGearTooltip();
        }
    }
}
