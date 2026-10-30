using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalModUpdateChecker
{
    [HarmonyPatch(
        typeof(TitleScreenController),
        nameof(TitleScreenController.Start))]
    internal static class TitleScreenStartPatch
    {
        private static void Postfix(
            TitleScreenController __instance)
        {
            TitleScreenUpdatePanel.Attach(__instance);
        }
    }

    [HarmonyPatch(
        typeof(TitleScreenMenuController),
        nameof(TitleScreenMenuController.Start))]
    internal static class TitleScreenMenuStartPatch
    {
        private const string HmucMenuButtonName =
            "Loupito75.HospitalModUpdateChecker.MainMenuButton";

        private static GameObject _hmucMenuButton;

        private static void Postfix(
            TitleScreenMenuController __instance)
        {
            if (__instance == null)
            {
                return;
            }

            AddBringToFrontHandler(
                __instance.m_newGameButton,
                __instance.m_newGameScreen);

            AddBringToFrontHandler(
                __instance.m_sandboxButton,
                __instance.m_sandboxScreen);

            AddBringToFrontHandler(
                __instance.m_campaignButton,
                __instance.m_campaignScreen);

            AddBringToFrontHandler(
                __instance.m_loadButton,
                __instance.m_loadScreen);

            AddBringToFrontHandler(
                __instance.m_optionsButton,
                __instance.m_optionsScreen);

            AddBringToFrontHandler(
                __instance.m_creditsButton,
                __instance.m_creditsScreen);

            AddBringToFrontHandler(
                __instance.m_testworkshopUploadButton,
                __instance.m_testworkshopUploadPanel);

            CreateHmucMenuButton(
                __instance);
        }

        private static void CreateHmucMenuButton(
            TitleScreenMenuController controller)
        {
            // Credits provides the canonical visible 400x50 main-menu
            // geometry; the Workshop upload control uses a compact layout.
            GameObject template =
                controller.m_creditsButton != null
                    ? controller.m_creditsButton
                    : controller.m_optionsButton;

            if (template == null ||
                template.transform.parent == null)
            {
                return;
            }

            Transform parent =
                template.transform.parent;

            for (int i = 0;
                i < parent.childCount;
                i++)
            {
                Transform child =
                    parent.GetChild(i);

                if (child != null &&
                    child.gameObject != null &&
                    string.Equals(
                        child.gameObject.name,
                        HmucMenuButtonName,
                        StringComparison.Ordinal))
                {
                    _hmucMenuButton =
                        child.gameObject;

                    RefreshHmucMenuButtonLabel();
                    return;
                }
            }

            GameObject buttonObject =
                UnityEngine.Object.Instantiate(
                    template);

            buttonObject.name =
                HmucMenuButtonName;

            buttonObject.transform.SetParent(
                parent,
                false);

            buttonObject.transform.localScale =
                Vector3.one;

            ArrangeLowerMenu(
                controller,
                buttonObject);

            LocalizedTextController[] localized =
                buttonObject.GetComponentsInChildren<
                    LocalizedTextController>(
                        true);

            for (int i = 0;
                i < localized.Length;
                i++)
            {
                if (localized[i] != null)
                {
                    localized[i].enabled =
                        false;
                }
            }

            _hmucMenuButton =
                buttonObject;

            RefreshHmucMenuButtonLabel();

            Button button =
                buttonObject.GetComponent<Button>();

            if (button == null)
            {
                UnityEngine.Object.Destroy(
                    buttonObject);

                _hmucMenuButton = null;
                return;
            }

            button.onClick.RemoveAllListeners();

            Navigation navigation =
                button.navigation;

            navigation.mode =
                Navigation.Mode.Automatic;

            button.navigation =
                navigation;

            button.onClick.AddListener(
                delegate
                {
                    if (TitleScreenUpdatePanel
                        .IsOpenAndFront)
                    {
                        return;
                    }

                    CloseNativeScreens(
                        controller);

                    TitleScreenUpdatePanel
                        .PlayNativeClick();

                    TitleScreenUpdatePanel
                        .OpenFromMainMenu();
                });

            HoverTooltipDelay hover =
                buttonObject.GetComponent<HoverTooltipDelay>();

            if (hover == null)
            {
                hover =
                    buttonObject.AddComponent<HoverTooltipDelay>();
            }

            HmucTooltipHover tooltip =
                buttonObject.GetComponent<HmucTooltipHover>();

            if (tooltip == null)
            {
                tooltip =
                    buttonObject.AddComponent<HmucTooltipHover>();
            }

            tooltip.StringId =
                UpdatePanelLocalization.DashboardTitleId;

            tooltip.AppendAuthor =
                true;

            buttonObject.SetActive(
                true);
        }

        private static void ArrangeLowerMenu(
            TitleScreenMenuController controller,
            GameObject hmucButton)
        {
            RectTransform newGame =
                GetRect(
                    controller.m_newGameButton);

            RectTransform load =
                GetRect(
                    controller.m_loadButton);

            RectTransform options =
                GetRect(
                    controller.m_optionsButton);

            RectTransform workshop =
                GetRect(
                    controller.m_testworkshopUploadButton);

            RectTransform hmuc =
                GetRect(
                    hmucButton);

            RectTransform credits =
                GetRect(
                    controller.m_creditsButton);

            RectTransform quit =
                GetRect(
                    controller.m_quitButton);

            if (newGame == null ||
                load == null ||
                options == null ||
                workshop == null ||
                hmuc == null ||
                credits == null ||
                quit == null)
            {
                return;
            }

            // Keep the original Vanilla menu bounds but redistribute the
            // seven visible rows evenly inside them. Vanilla uses -300 for
            // New Game and -700 for Quit in the exported title scene; using
            // the live RectTransforms keeps this robust to UI/scene changes.
            float topY =
                newGame.anchoredPosition.y;

            float bottomY =
                quit.anchoredPosition.y;

            const int visibleRowCount =
                7;

            float spacing =
                (bottomY - topY) /
                (visibleRowCount - 1);

            RectTransform[] rows =
            {
                newGame,
                load,
                options,
                workshop,
                hmuc,
                credits,
                quit
            };

            for (int i = 0;
                i < rows.Length;
                i++)
            {
                RectTransform row =
                    rows[i];

                row.anchoredPosition =
                    new Vector2(
                        row.anchoredPosition.x,
                        topY +
                            spacing * i);
            }

            // HMUC intentionally adopts the same full-size geometry as the
            // neighbouring native main-menu rows.
            hmuc.anchorMin =
                credits.anchorMin;

            hmuc.anchorMax =
                credits.anchorMax;

            hmuc.pivot =
                credits.pivot;

            hmuc.sizeDelta =
                credits.sizeDelta;

            int creditsIndex =
                controller.m_creditsButton
                    .transform
                    .GetSiblingIndex();

            hmucButton.transform.SetSiblingIndex(
                creditsIndex);
        }

        private static RectTransform GetRect(
            GameObject gameObject)
        {
            return gameObject == null
                ? null
                : gameObject.GetComponent<RectTransform>();
        }

        internal static void RefreshHmucMenuButtonLabel()
        {
            if (_hmucMenuButton == null)
            {
                return;
            }

            Text[] labels =
                _hmucMenuButton.GetComponentsInChildren<Text>(
                    true);

            string label =
                UpdatePanelLocalization.Get(
                    UpdatePanelLocalization.MainMenuActionId);

            for (int i = 0;
                i < labels.Length;
                i++)
            {
                if (labels[i] != null)
                {
                    labels[i].text =
                        label;
                }
            }
        }

        private static void CloseNativeScreens(
            TitleScreenMenuController controller)
        {
            SetInactive(
                controller.m_newGameScreen);

            SetInactive(
                controller.m_sandboxScreen);

            SetInactive(
                controller.m_campaignScreen);

            SetInactive(
                controller.m_loadScreen);

            SetInactive(
                controller.m_optionsScreen);

            SetInactive(
                controller.m_creditsScreen);

            SetInactive(
                controller.m_testworkshopUploadPanel);

            if (PlayerProfile.Instance != null)
            {
                PlayerProfile.Instance.m_campaingPanel =
                    false;

                PlayerProfile.Instance.m_sandboxPanel =
                    false;
            }
        }

        private static void SetInactive(
            GameObject screen)
        {
            if (screen != null)
            {
                screen.SetActive(
                    false);
            }
        }

        private static void AddBringToFrontHandler(
            GameObject buttonObject,
            GameObject screen)
        {
            if (buttonObject == null ||
                screen == null)
            {
                return;
            }

            Button button =
                buttonObject.GetComponent<Button>();

            if (button == null)
            {
                return;
            }

            button.onClick.AddListener(
                delegate
                {
                    TitleScreenUpdatePanel
                        .BringNativeScreenToFront(
                            screen);
                });
        }
    }

    // Shared delayed tooltip used by title-menu and plugin status hovers.
    // All visual metrics come from HmucUiStyle, the same source used by
    // configuration and metadata tooltips.
    internal static class HmucTooltipPresenter
    {

        private static GameObject _tooltip;
        private static Text _text;
        private static Canvas _canvas;
        private static HmucTooltipHover _owner;

        internal static void Show(
            HmucTooltipHover owner,
            GameObject target,
            string value)
        {
            if (owner == null ||
                target == null ||
                string.IsNullOrEmpty(
                    value))
            {
                Hide(
                    owner);
                return;
            }

            Canvas canvas =
                target.GetComponentInParent<Canvas>();

            if (canvas == null)
            {
                Hide(
                    owner);
                return;
            }

            Text source =
                target.GetComponent<Text>();

            if (source == null)
            {
                source =
                    target.GetComponentInChildren<Text>(
                        true);
            }

            if (source == null)
            {
                Hide(
                    owner);
                return;
            }

            if (_tooltip == null ||
                _canvas != canvas)
            {
                DestroyTooltip();

                _canvas =
                    canvas;

                _tooltip =
                    new GameObject(
                        "HMUC.SharedTooltip");

                _tooltip.transform.SetParent(
                    canvas.transform,
                    false);

                RectTransform root =
                    _tooltip.AddComponent<RectTransform>();

                root.anchorMin =
                    root.anchorMax =
                        new Vector2(
                            .5f,
                            .5f);

                root.pivot =
                    new Vector2(
                        .5f,
                        .5f);

                Image background =
                    _tooltip.AddComponent<Image>();

                background.color =
                    HmucUiStyle.TooltipColor();

                background.raycastTarget =
                    false;

                GameObject textObject =
                    new GameObject(
                        "TooltipText");

                textObject.transform.SetParent(
                    _tooltip.transform,
                    false);

                _text =
                    textObject.AddComponent<Text>();

                _text.font =
                    source.font;

                _text.material =
                    source.material;

                _text.fontStyle =
                    FontStyle.Normal;

                _text.fontSize =
                    HmucUiStyle.TooltipFontSize;

                _text.lineSpacing =
                    source.lineSpacing;

                _text.color =
                    Color.white;

                _text.alignment =
                    TextAnchor.MiddleLeft;

                _text.supportRichText =
                    false;

                _text.horizontalOverflow =
                    HorizontalWrapMode.Wrap;

                _text.verticalOverflow =
                    VerticalWrapMode.Truncate;

                _text.raycastTarget =
                    false;

                RectTransform textRect =
                    _text.rectTransform;

                textRect.anchorMin =
                    Vector2.zero;

                textRect.anchorMax =
                    Vector2.one;

                textRect.pivot =
                    new Vector2(
                        .5f,
                        .5f);

                textRect.offsetMin =
                    new Vector2(
                        HmucUiStyle.TooltipPaddingHorizontal,
                        HmucUiStyle.TooltipPaddingVertical);

                textRect.offsetMax =
                    new Vector2(
                        -HmucUiStyle.TooltipPaddingHorizontal,
                        -HmucUiStyle.TooltipPaddingVertical);
            }

            _owner =
                owner;

            _text.text =
                value;

            Canvas.ForceUpdateCanvases();

            RectTransform tooltipRect =
                _tooltip.GetComponent<RectTransform>();

            float width =
                HmucUiStyle.TooltipWidth(
                    _text.preferredWidth);

            tooltipRect.sizeDelta =
                new Vector2(
                    width,
                    HmucUiStyle.TooltipMinHeight);

            Canvas.ForceUpdateCanvases();

            float height =
                HmucUiStyle.TooltipHeight(
                    _text.preferredHeight);

            tooltipRect.sizeDelta =
                new Vector2(
                    width,
                    height);

            RectTransform canvasRect =
                canvas.transform as RectTransform;

            Camera camera =
                canvas.renderMode !=
                    RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;

            Vector2 pointer;

            if (canvasRect != null &&
                RectTransformUtility
                    .ScreenPointToLocalPointInRectangle(
                        canvasRect,
                        Input.mousePosition,
                        camera,
                        out pointer))
            {
                float halfWidth =
                    canvasRect.rect.width *
                    .5f;

                float halfHeight =
                    canvasRect.rect.height *
                    .5f;

                float tooltipHalfWidth =
                    width *
                    .5f;

                float tooltipHalfHeight =
                    height *
                    .5f;

                float minY =
                    -halfHeight +
                    tooltipHalfHeight +
                    HmucUiStyle.TooltipEdgeMargin;

                float maxY =
                    halfHeight -
                    tooltipHalfHeight -
                    HmucUiStyle.TooltipEdgeMargin;

                float aboveY =
                    pointer.y +
                    tooltipHalfHeight +
                    HmucUiStyle.TooltipPointerGap;

                float belowY =
                    pointer.y -
                    tooltipHalfHeight -
                    HmucUiStyle.TooltipPointerGap;

                float y =
                    aboveY <= maxY
                        ? aboveY
                        : belowY >= minY
                            ? belowY
                            : Mathf.Clamp(
                                aboveY,
                                minY,
                                maxY);

                tooltipRect.anchoredPosition =
                    new Vector2(
                        Mathf.Clamp(
                            pointer.x,
                            -halfWidth +
                                tooltipHalfWidth +
                                HmucUiStyle.TooltipEdgeMargin,
                            halfWidth -
                                tooltipHalfWidth -
                                HmucUiStyle.TooltipEdgeMargin),
                        y);
            }

            _tooltip.SetActive(
                true);

            _tooltip.transform.SetAsLastSibling();
        }

        internal static void Hide(
            HmucTooltipHover owner)
        {
            if (_tooltip == null)
            {
                return;
            }

            if (owner != null &&
                _owner != owner)
            {
                return;
            }

            _tooltip.SetActive(
                false);

            _owner = null;
        }

        internal static void HideAll()
        {
            if (_tooltip != null)
            {
                _tooltip.SetActive(
                    false);
            }

            _owner = null;
        }

        private static void DestroyTooltip()
        {
            if (_tooltip != null)
            {
                UnityEngine.Object.Destroy(
                    _tooltip);
            }

            _tooltip = null;
            _text = null;
            _canvas = null;
            _owner = null;
        }
    }

    internal sealed class HmucTooltipHover :
        MonoBehaviour
    {
        internal string StringId;
        internal bool AppendAuthor;

        private HoverTooltipDelay _hover;
        private bool _showing;

        private void Awake()
        {
            _hover =
                GetComponent<HoverTooltipDelay>();
        }

        private void Update()
        {
            bool show =
                _hover != null &&
                _hover.IsMouseOver() &&
                _hover.ShouldShowToolTip();

            if (!show)
            {
                if (_showing)
                {
                    HmucTooltipPresenter.Hide(
                        this);

                    _showing =
                        false;
                }

                return;
            }

            string text =
                UpdatePanelLocalization.Get(
                    StringId);

            if (AppendAuthor)
            {
                text +=
                    " (" +
                    UpdatePanelLocalization.Get(
                        UpdatePanelLocalization.AuthorCreditId) +
                    " " +
                    Plugin.PluginAuthor +
                    ")";
            }

            HmucTooltipPresenter.Show(
                this,
                gameObject,
                text);

            _showing =
                true;
        }

        private void OnDisable()
        {
            HmucTooltipPresenter.Hide(
                this);

            _showing =
                false;
        }

        private void OnDestroy()
        {
            HmucTooltipPresenter.Hide(
                this);
        }
    }
}
