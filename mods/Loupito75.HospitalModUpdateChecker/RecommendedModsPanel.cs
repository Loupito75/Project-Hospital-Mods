using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalModUpdateChecker
{
    // Simple expandable, scrollable name selector. Uses only UI components
    // available on the Project Hospital title screen.
    internal static class RecommendedModsPanel
    {
        private const float RowHeight = 34f;
        private const float ClosedHeight = 112f;
        private const float LeftPadding = 0f;

        private static GameObject _root;
        private static GameObject _listViewport;
        private static RectTransform _rootRect;
        private static RectTransform _contentRect;
        private static RectTransform _viewportRect;
        private static RectTransform _listRect;
        private static RectTransform _statusRect;
        private static Text _source;
        private static Text _selectorLabel;
        private static Text _actionLabel;
        private static Text _statusLabel;
        private static Button _action;
        private static float _top;
        private static float _listHeight;
        private static string _selectedGuid;
        private static bool _expanded;
        private static readonly List<PackageManifestEntry> _options =
            new List<PackageManifestEntry>();

        internal static void Destroy()
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            _root = null;
            _listViewport = null;
            _rootRect = null;
            _contentRect = null;
            _viewportRect = null;
            _listRect = null;
            _statusRect = null;
            _source = null;
            _selectorLabel = null;
            _actionLabel = null;
            _statusLabel = null;
            _action = null;
            _options.Clear();
            _expanded = false;
        }

        internal static float Create(
            Text source,
            RectTransform parent,
            RectTransform sectionViewport,
            float top)
        {
            Destroy();
            _source = source;
            _contentRect = parent;
            _viewportRect = sectionViewport;
            _top = top;
            _options.AddRange(PackageUpdateManager.GetInstallablePackages());

            bool selectedExists = false;
            for (int i = 0; i < _options.Count; i++)
                if (_options[i].Guid == _selectedGuid)
                    selectedExists = true;
            if (!selectedExists)
                _selectedGuid = _options.Count == 0
                    ? null : _options[0].Guid;

            _root = new GameObject("RecommendedMods");
            _root.transform.SetParent(parent, false);
            _rootRect = _root.AddComponent<RectTransform>();
            _rootRect.anchorMin = new Vector2(0f, 1f);
            _rootRect.anchorMax = new Vector2(1f, 1f);
            _rootRect.pivot = new Vector2(.5f, 1f);
            _rootRect.anchoredPosition = new Vector2(0f, -top);
            _rootRect.sizeDelta = new Vector2(0f, ClosedHeight);

            Text heading = NewText(
                "RecommendedTitle", _root.transform,
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.RecommendedTitleId) + ":",
                TextAnchor.MiddleLeft, true);
            Place(heading.rectTransform, -4f, 27f, LeftPadding, 8f);

            if (_options.Count == 0)
            {
                string reason = UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.RecommendedEmptyId);
                if (PackageUpdateManager.CatalogState ==
                    PackageCatalogState.Loading)
                    reason = UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.PackageCatalogLoadingId);
                else if (PackageUpdateManager.CatalogState ==
                    PackageCatalogState.Error)
                    reason = UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.PackageCatalogErrorId);
                else if (PackageUpdateManager.CatalogState ==
                    PackageCatalogState.NotLoaded)
                    reason = UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.PackageUnavailableId);

                Text empty = NewText("Empty", _root.transform,
                    reason, TextAnchor.MiddleLeft, false);
                empty.fontStyle = FontStyle.Italic;
                Place(empty.rectTransform, -31f, 30f, LeftPadding, 8f);
                _rootRect.sizeDelta = new Vector2(0f, 66f);
                return 66f;
            }

            GameObject selector = NewButton(
                "SelectMod", _root.transform, -38f, 37f, LeftPadding, 129f);
            _selectorLabel = NewText(
                "SelectedName", selector.transform, "",
                TextAnchor.MiddleLeft, false);
            Stretch(_selectorLabel.rectTransform, 10f, 42f);

            Text selectorArrow = NewText(
                "SelectorArrow", selector.transform, "v",
                TextAnchor.MiddleCenter, true);
            RectTransform arrowRect = selectorArrow.rectTransform;
            arrowRect.anchorMin = new Vector2(1f, 0f);
            arrowRect.anchorMax = new Vector2(1f, 1f);
            arrowRect.pivot = new Vector2(.5f, .5f);
            arrowRect.anchoredPosition = new Vector2(-18f, 0f);
            arrowRect.sizeDelta = new Vector2(30f, 0f);

            selector.GetComponent<Button>().onClick.AddListener(
                delegate { SetExpanded(!_expanded); });

            GameObject action = NewButton(
                "Install", _root.transform, -38f, 37f, 0f, 8f);
            RectTransform actionRect = action.GetComponent<RectTransform>();
            actionRect.anchorMin = actionRect.anchorMax =
                new Vector2(1f, 1f);
            actionRect.pivot = new Vector2(1f, 1f);
            actionRect.anchoredPosition = new Vector2(-8f, -38f);
            actionRect.sizeDelta = new Vector2(113f, 37f);
            _action = action.GetComponent<Button>();
            _actionLabel = NewText(
                "InstallText", action.transform, "",
                TextAnchor.MiddleCenter, true);
            Stretch(_actionLabel.rectTransform, 4f, 4f);
            _action.onClick.AddListener(delegate
            {
                if (!string.IsNullOrEmpty(_selectedGuid))
                    PackageUpdateManager.StartInstall(_selectedGuid);
                UpdateState();
            });

            _statusLabel = NewText(
                "InstallState", _root.transform, "",
                TextAnchor.MiddleLeft, false);
            _statusLabel.fontStyle = FontStyle.Italic;
            _statusRect = _statusLabel.rectTransform;
            Place(_statusRect, -81f, 27f, LeftPadding, 8f);

            _listViewport = new GameObject("DropdownViewport");
            _listViewport.transform.SetParent(_root.transform, false);
            RectTransform listViewportRect =
                _listViewport.AddComponent<RectTransform>();
            listViewportRect.anchorMin = new Vector2(0f, 1f);
            listViewportRect.anchorMax = new Vector2(1f, 1f);
            listViewportRect.pivot = new Vector2(.5f, 1f);
            listViewportRect.anchoredPosition = new Vector2(-60f, -78f);
            listViewportRect.sizeDelta = new Vector2(-132f, 0f);
            Image background = _listViewport.AddComponent<Image>();
            background.color = PanelColor();
            background.raycastTarget = true;
            _listViewport.AddComponent<RectMask2D>();

            GameObject list = new GameObject("Choices");
            list.transform.SetParent(_listViewport.transform, false);
            _listRect = list.AddComponent<RectTransform>();
            _listRect.anchorMin = new Vector2(0f, 1f);
            _listRect.anchorMax = new Vector2(1f, 1f);
            _listRect.pivot = new Vector2(.5f, 1f);
            _listRect.anchoredPosition = Vector2.zero;
            _listRect.sizeDelta =
                new Vector2(0f, _options.Count * RowHeight);

            ScrollRect scroll = _listViewport.AddComponent<ScrollRect>();
            scroll.viewport = listViewportRect;
            scroll.content = _listRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            for (int i = 0; i < _options.Count; i++)
            {
                PackageManifestEntry option = _options[i];
                GameObject item = NewButton(
                    "Choice", _listRect, -i * RowHeight, RowHeight,
                    3f, 3f);
                Text caption = NewText(
                    "Name", item.transform, option.Name,
                    TextAnchor.MiddleLeft, false);
                Stretch(caption.rectTransform, 8f, 8f);
                string guid = option.Guid;
                item.GetComponent<Button>().onClick.AddListener(delegate
                {
                    _selectedGuid = guid;
                    SetExpanded(false);
                    UpdateState();
                });
            }

            _listHeight = Math.Min(6, _options.Count) * RowHeight;
            SetExpanded(false);
            UpdateState();
            return ClosedHeight;
        }

        private static void SetExpanded(bool expanded)
        {
            if (_root == null || _listViewport == null)
                return;
            _expanded = expanded;
            _listViewport.SetActive(expanded);
            RectTransform viewport = _listViewport.GetComponent<RectTransform>();
            viewport.sizeDelta = new Vector2(-132f,
                expanded ? _listHeight : 0f);
            float extra = expanded ? _listHeight + 5f : 0f;
            _rootRect.sizeDelta = new Vector2(
                0f, ClosedHeight + extra);
            if (_statusRect != null)
                Place(_statusRect, -81f - extra, 27f, LeftPadding, 8f);

            if (_contentRect != null && _viewportRect != null)
                _contentRect.sizeDelta = new Vector2(0f,
                    Math.Max(_viewportRect.rect.height,
                        _top + ClosedHeight + extra + 8f));
        }

        internal static void UpdateState()
        {
            if (_root == null || _action == null)
                return;

            PackageManifestEntry selected = null;
            for (int i = 0; i < _options.Count; i++)
                if (_options[i].Guid == _selectedGuid)
                    selected = _options[i];

            if (selected == null)
            {
                _action.interactable = false;
                return;
            }

            _selectorLabel.text = selected.Name;
            PackageOperation operation =
                PackageUpdateManager.GetOperation(selected.Guid);

            string button = UpdatePanelLocalization.Get(
                UpdatePanelLocalization.RecommendedInstallId);
            string status = "";
            bool active =
                !PackageUpdateManager.IsAnotherOperationRunning(
                    selected.Guid);

            if (operation != null)
            {
                if (operation.State == PackageOperationState.Downloading)
                {
                    button = UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.CancelActionId);
                    status = UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.DownloadingId) + " " +
                        Math.Round(Mathf.Clamp01(operation.Progress / .72f) *
                            100f).ToString() + "%";
                }
                else if (operation.State == PackageOperationState.Verifying)
                {
                    status = UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.VerifyingId);
                    active = false;
                }
                else if (operation.State == PackageOperationState.Extracting)
                {
                    status = UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.PreparingInstallId);
                    active = false;
                }
                else if (operation.State == PackageOperationState.PendingRestart)
                {
                    button = UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.QueuedActionId);
                    status = UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.RestartRequiredId);
                    active = false;
                }
                else if (operation.State == PackageOperationState.Cancelled ||
                         operation.State == PackageOperationState.Error)
                {
                    button = UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.RetryActionId);
                    status = operation.State == PackageOperationState.Error
                        ? operation.Error ?? ""
                        : UpdatePanelLocalization.Get(
                            UpdatePanelLocalization.CancelledId);
                }
            }

            _actionLabel.text = button;
            _action.interactable = active;
            _statusLabel.text = status;
        }

        private static GameObject NewButton(
            string name, Transform parent, float top,
            float height, float left, float right)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            RectTransform rect = root.AddComponent<RectTransform>();
            Place(rect, top, height, left, right);
            Image image = root.AddComponent<Image>();
            image.color = PanelColor();
            Button button = root.AddComponent<Button>();
            button.targetGraphic = image;
            return root;
        }

        private static Color PanelColor()
        {
            if (UISettings.Instance != null)
                return UISettings.Instance.THEME_COLOR_PANEL_HEADING;
            return new Color(.17f, .21f, .25f, 1f);
        }

        private static Text NewText(string name,
            Transform parent, string value,
            TextAnchor align, bool bold)
        {
            GameObject go = UnityEngine.Object.Instantiate(
                _source.gameObject);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.SetActive(true);
            LocalizedTextController localized =
                go.GetComponent<LocalizedTextController>();
            if (localized != null)
                localized.enabled = false;
            Text result = go.GetComponent<Text>();
            result.text = value ?? "";
            result.alignment = align;
            result.color = Color.white;
            result.fontSize = Math.Max(_source.fontSize, 12);
            result.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            result.supportRichText = false;
            result.raycastTarget = false;
            result.horizontalOverflow = HorizontalWrapMode.Wrap;
            result.verticalOverflow = VerticalWrapMode.Overflow;
            return result;
        }

        private static void Stretch(RectTransform rect,
            float left, float right)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
        }

        private static void Place(RectTransform rect,
            float top, float height,
            float left, float right)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(
                (left - right) / 2f, top);
            rect.sizeDelta = new Vector2(-left - right, height);
        }
    }
}
