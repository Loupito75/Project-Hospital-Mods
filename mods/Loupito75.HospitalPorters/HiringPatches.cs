using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalPorters
{
    internal sealed class PorterHiringUiState
    {
        internal GameObject Button;
        internal GameObject Background;
        internal bool BackgroundIsChild;
        internal IconButtonController IconButton;
        internal Text CountText;
    }

    internal static class PorterHiringUi
    {
        private const float SortingStep = 30f;

        private static readonly Dictionary<HiringPanelController, PorterHiringUiState> States =
            new Dictionary<HiringPanelController, PorterHiringUiState>();

        internal static bool IsPorterWorkspace(EntityIDPointer<TileObject> workspace)
        {
            TileObject tileObject = workspace.GetEntity();
            return PorterLogisticsUi.IsPorterLocker(tileObject);
        }

        internal static bool TryGetState(
            HiringPanelController controller,
            out PorterHiringUiState state)
        {
            state = null;
            return controller != null &&
                States.TryGetValue(controller, out state);
        }

        internal static void EnsureButton(HiringPanelController controller)
        {
            if (controller == null || States.ContainsKey(controller) ||
                controller.m_administrationCharacters == null || controller.m_administrationCharacters.Count == 0 ||
                controller.m_administrationCharactersBackGround == null || controller.m_administrationCharactersBackGround.Count == 0)
            {
                return;
            }

            // Porters stay CharacterNurse technically, but are presented as hospital support staff.
            // Clone the native Janitor filter so geometry and interaction remain native-style.
            GameObject sourceButton = controller.m_administrationCharacters[0];
            GameObject sourceBackground = controller.m_administrationCharactersBackGround[0];
            if (sourceButton == null || sourceBackground == null)
            {
                return;
            }

            GameObject button = UnityEngine.Object.Instantiate(sourceButton);
            button.name = "HospitalPorters_PorterFilter";
            button.transform.SetParent(sourceButton.transform.parent, worldPositionStays: false);
            button.transform.localScale = sourceButton.transform.localScale;

            IconButtonController iconButton = button.GetComponentInChildren<IconButtonController>();
            Text countText = button.GetComponentInChildren<Text>(includeInactive: true);
            if (iconButton == null || countText == null)
            {
                Plugin.Log?.LogError("Porter hiring filter could not resolve the cloned Janitor IconButtonController or staff-count Text.");
                UnityEngine.Object.Destroy(button);
                return;
            }

            LocalizedTextController localizedText = countText.GetComponent<LocalizedTextController>();
            if (localizedText != null)
            {
                localizedText.enabled = false;
            }
            countText.text = "0";
            PorterVisuals.ApplyCategoryIcons(iconButton);
            iconButton.SetOnClickedDelegate(null);

            GameObject background;
            bool backgroundIsChild = sourceBackground.transform.IsChildOf(sourceButton.transform);
            if (backgroundIsChild)
            {
                string relativePath = GetRelativePath(sourceButton.transform, sourceBackground.transform);
                Transform clonedBackground = string.IsNullOrEmpty(relativePath) ? null : button.transform.Find(relativePath);
                if (clonedBackground == null)
                {
                    Plugin.Log?.LogError("Porter hiring filter could not resolve the cloned Janitor staffing background.");
                    UnityEngine.Object.Destroy(button);
                    return;
                }
                background = clonedBackground.gameObject;
            }
            else
            {
                background = UnityEngine.Object.Instantiate(sourceBackground);
                background.name = "HospitalPorters_PorterFilterBackground";
                background.transform.SetParent(sourceBackground.transform.parent, worldPositionStays: false);
                background.transform.localScale = sourceBackground.transform.localScale;
                Image backgroundImage = background.GetComponent<Image>();
                if (backgroundImage != null)
                {
                    backgroundImage.raycastTarget = false;
                }
            }

            button.SetActive(false);
            if (!backgroundIsChild)
            {
                background.SetActive(false);
            }

            States.Add(
                controller,
                new PorterHiringUiState
                {
                    Button = button,
                    Background = background,
                    BackgroundIsChild = backgroundIsChild,
                    IconButton = iconButton,
                    CountText = countText
                });

            PorterDiagnostics.Log("Porter hiring filter created next to the native Janitor filter with dedicated Porter artwork.");
        }

        private static string GetRelativePath(Transform root, Transform child)
        {
            Stack<string> names = new Stack<string>();
            Transform current = child;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return null;
            }

            return string.Join("/", names.ToArray());
        }

        internal static void ConfigureNurseDelegate(
            HiringPanelController controller,
            EntityIDPointer<TileObject> workspace,
            bool forceReception)
        {
            EnsureButton(controller);
            if (!States.TryGetValue(controller, out PorterHiringUiState state) || state.Button == null)
            {
                return;
            }

            bool porterWorkspace = IsPorterWorkspace(workspace);
            state.Button.SetActive(porterWorkspace && !forceReception);
            if (!state.BackgroundIsChild && state.Background != null && !porterWorkspace)
            {
                state.Background.SetActive(false);
            }

            if (!porterWorkspace || forceReception)
            {
                state.IconButton.SetInactive(true);
                state.IconButton.SetOnClickedDelegate(null);
                PorterHiringState.Active = false;
                return;
            }

            state.IconButton.SetInactive(false);
            state.IconButton.SetOnClickedDelegate(delegate
            {
                if (HiringManager.Instance.m_workspace == workspace)
                {
                    SelectPorters(controller, state.IconButton);
                }
                else
                {
                    Plugin.Log?.LogWarning("Porter hiring filter click ignored because the active hiring workspace changed.");
                }
            });
            UpdateStaffingVisual(controller);
        }

        internal static void SyncAvailability(HiringPanelController controller)
        {
            EnsureButton(controller);
            if (!States.TryGetValue(controller, out PorterHiringUiState state) || state.Button == null)
            {
                return;
            }

            bool porterWorkspace = IsPorterWorkspace(HiringManager.Instance.m_workspace);
            if (!porterWorkspace)
            {
                state.Button.SetActive(false);
                state.IconButton.SetInactive(true);
                state.IconButton.SetOnClickedDelegate(null);
                if (!state.BackgroundIsChild && state.Background != null)
                {
                    state.Background.SetActive(false);
                }
                PorterHiringState.Active = false;
            }
        }

        private static void SelectPorters(HiringPanelController controller, IconButtonController iconButton)
        {
            try
            {
                if (Hospital.Instance == null || Hospital.Instance.m_activeDepartment.GetEntity() == null)
                {
                    Plugin.Log?.LogWarning("Porter hiring filter clicked without an active department.");
                    return;
                }

                GameDBDepartment department = Hospital.Instance.m_activeDepartment.GetEntity().GetDepartmentType();
                PorterCandidatePool.Ensure(department);

                PorterHiringState.Active = true;
                PorterHiringState.SelectingPorter = true;
                try
                {
                    // Deliberately keep CharacterNurse: candidates carry BehaviorNurse so native
                    // stretcher/wheelchair reservations and state machines remain untouched.
                    controller.SetCharacterType(
                        LopitalTypes.CharacterNurse,
                        iconButton.m_iconIndex,
                        LocalizationManager.Get(PorterIds.PorterCandidates));
                }
                finally
                {
                    PorterHiringState.SelectingPorter = false;
                }
            }
            catch (Exception exception)
            {
                PorterHiringState.Active = false;
                PorterHiringState.SelectingPorter = false;
                Plugin.Log?.LogError("Porter hiring filter click failed: " + exception);
            }
        }

        internal static void UpdateLayout(HiringPanelController controller, bool clinic)
        {
            EnsureButton(controller);
            if (!States.TryGetValue(controller, out PorterHiringUiState state) || state.Button == null)
            {
                return;
            }

            bool show = !clinic &&
                Database.Instance.GetEntry<GameDBOccupation>(PorterIds.Occupation) != null &&
                IsPorterWorkspace(HiringManager.Instance.m_workspace);
            state.Button.SetActive(show);
            if (!show)
            {
                if (!state.BackgroundIsChild && state.Background != null)
                {
                    state.Background.SetActive(false);
                }
                PorterHiringState.Active = false;
                return;
            }

            RectTransform porterRect = state.Button.GetComponent<RectTransform>();
            if (porterRect == null)
            {
                return;
            }

            RectTransform sourceButtonRect =
                controller.m_administrationCharacters[0].GetComponent<RectTransform>();
            if (sourceButtonRect == null)
            {
                return;
            }

            Vector2 oldPosition = porterRect.anchoredPosition;
            Vector2 newPosition =
                sourceButtonRect.anchoredPosition + new Vector2(SortingStep, 0f);
            porterRect.anchoredPosition = newPosition;

            if (!state.BackgroundIsChild && state.Background != null)
            {
                RectTransform backgroundRect = state.Background.GetComponent<RectTransform>();
                RectTransform sourceBackgroundRect =
                    controller.m_administrationCharactersBackGround[0].GetComponent<RectTransform>();
                if (backgroundRect != null && sourceBackgroundRect != null)
                {
                    Vector2 sourceOffset =
                        sourceBackgroundRect.anchoredPosition - sourceButtonRect.anchoredPosition;
                    backgroundRect.anchoredPosition = newPosition + sourceOffset;
                }
                else if (backgroundRect != null)
                {
                    backgroundRect.anchoredPosition += newPosition - oldPosition;
                }
            }

            UpdateStaffingVisual(controller);
        }

        internal static void UpdateStaffingVisual(HiringPanelController controller)
        {
            EnsureButton(controller);
            if (!States.TryGetValue(controller, out PorterHiringUiState state) ||
                state.Button == null || state.Background == null || state.CountText == null ||
                Hospital.Instance == null || Hospital.Instance.m_activeDepartment.GetEntity() == null)
            {
                return;
            }

            Department department = Hospital.Instance.m_activeDepartment.GetEntity();
            int hired = PorterStaffing.CountPorters(department, HiringManager.Instance.m_shift);

            state.CountText.text = hired.ToString();
            state.CountText.color = UISettings.Instance.EXAMINATION_COLOR_SUCCESSFUL;

            bool showOptionalBackground = state.Button.activeSelf && hired == 0;
            state.Background.SetActive(showOptionalBackground);
            if (showOptionalBackground)
            {
                Image image = state.Background.GetComponent<Image>();
                if (image != null)
                {
                    image.color = UISettings.Instance.HIRING_OPTIONAL;
                }
            }
        }

        internal static void ShowTooltipIfNeeded(HiringPanelController controller)
        {
            if (controller == null || !States.TryGetValue(controller, out PorterHiringUiState state) ||
                state.Button == null || !state.Button.activeInHierarchy)
            {
                return;
            }

            HoverTooltipDelay hover = state.Button.GetComponentInChildren<HoverTooltipDelay>();
            if (hover == null || !hover.ShouldShowToolTip())
            {
                return;
            }

            TooltipManager.Instance
                .GetTooltipComponent<TooltipPerks>()
                .UpdateData(
                    LocalizationManager.Get(PorterIds.PorterCandidates),
                    LocalizationManager.Get(PorterIds.PorterTooltip),
                    PorterVisuals.CategoryLightAsset,
                    TextAnchor.UpperLeft);
        }
    }

    internal sealed class NurseListSwapState
    {
        internal AvailableCharacters AvailableCharacters;
        internal List<Entity> OriginalNurses;
    }

    [HarmonyPatch(typeof(HiringPanelController), nameof(HiringPanelController.Start))]
    internal static class HiringPanelStartPatch
    {
        private static void Postfix(HiringPanelController __instance)
        {
            PorterHiringUi.EnsureButton(__instance);
        }
    }

    [HarmonyPatch(
        typeof(HiringPanelController),
        nameof(HiringPanelController.SortHiringSpecializationsButtons),
        new Type[] { typeof(bool) })]
    internal static class HiringPanelSortPatch
    {
        private static void Postfix(HiringPanelController __instance, bool clinic)
        {
            PorterHiringUi.UpdateLayout(__instance, clinic);
        }
    }

    [HarmonyPatch(
        typeof(HiringPanelController),
        nameof(HiringPanelController.SetupNursesFiltersDelegates),
        new Type[] { typeof(EntityIDPointer<TileObject>), typeof(bool) })]
    internal static class HiringPanelNurseDelegatesPatch
    {
        private static void Postfix(
            HiringPanelController __instance,
            EntityIDPointer<TileObject> workspace,
            bool forceReception)
        {
            PorterHiringUi.ConfigureNurseDelegate(__instance, workspace, forceReception);
        }
    }

    [HarmonyPatch(typeof(HiringPanelController), nameof(HiringPanelController.SetBackGrounds))]
    internal static class HiringPanelBackgroundStatePatch
    {
        private static void Postfix(HiringPanelController __instance)
        {
            PorterHiringUi.SyncAvailability(__instance);
        }
    }

    [HarmonyPatch(
        typeof(HiringPanelController),
        nameof(HiringPanelController.SetupNurseNumbers),
        new Type[]
        {
            typeof(bool), typeof(bool), typeof(int), typeof(int),
            typeof(int), typeof(int), typeof(int), typeof(int)
        })]
    internal static class HiringPanelNurseNumbersPatch
    {
        private static void Postfix(HiringPanelController __instance)
        {
            PorterHiringUi.UpdateStaffingVisual(__instance);
        }
    }

    [HarmonyPatch(
        typeof(HiringPanelController),
        nameof(HiringPanelController.ShowHiringTooltips),
        new Type[] { typeof(Department) })]
    internal static class HiringPanelTooltipPatch
    {
        private static void Postfix(HiringPanelController __instance)
        {
            PorterHiringUi.ShowTooltipIfNeeded(__instance);
        }
    }

    [HarmonyPatch(
        typeof(HiringPanelController),
        nameof(HiringPanelController.SetCharacterType),
        new Type[] { typeof(LopitalTypes), typeof(int), typeof(string) })]
    internal static class HiringSetCharacterTypePatch
    {
        private static void Prefix()
        {
            if (!PorterHiringState.SelectingPorter)
            {
                PorterHiringState.Active = false;
            }
        }
    }

    [HarmonyPatch(typeof(HiringPanelController), nameof(HiringPanelController.Update))]
    internal static class HiringPanelUpdatePatch
    {
        private static void Prefix(ref NurseListSwapState __state)
        {
            if (!PorterHiringState.Active || Hospital.Instance == null || Hospital.Instance.m_activeDepartment.GetEntity() == null)
            {
                return;
            }

            GameDBDepartment department = Hospital.Instance.m_activeDepartment.GetEntity().GetDepartmentType();
            AvailableCharacters availableCharacters = HiringManager.Instance.m_availableCharacters[department];
            if (availableCharacters == null)
            {
                return;
            }

            __state = new NurseListSwapState
            {
                AvailableCharacters = availableCharacters,
                OriginalNurses = availableCharacters.m_availableNormalNurses
            };

            availableCharacters.m_availableNormalNurses = PorterCandidatePool.Ensure(department);
        }

        private static Exception Finalizer(Exception __exception, NurseListSwapState __state)
        {
            if (__state?.AvailableCharacters != null)
            {
                __state.AvailableCharacters.m_availableNormalNurses = __state.OriginalNurses;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(IconButtonController), nameof(IconButtonController.OnClick))]
    internal static class PorterHiringRevealPerksPatch
    {
        private static void Prefix(
            IconButtonController __instance,
            ref NurseListSwapState __state)
        {
            if (!PorterHiringState.Active ||
                __instance == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_activeDepartment.GetEntity() == null)
            {
                return;
            }

            GameObject hiringPanel = MapEditorUIController.Instance?.m_hiringPanel;
            HiringPanelController controller =
                hiringPanel == null ? null : hiringPanel.GetComponent<HiringPanelController>();
            if (controller == null || controller.m_buttonUncoverPerks == null)
            {
                return;
            }

            IconButtonController revealButton =
                controller.m_buttonUncoverPerks.GetComponent<IconButtonController>();
            if (!object.ReferenceEquals(__instance, revealButton))
            {
                return;
            }

            GameDBDepartment department =
                Hospital.Instance.m_activeDepartment.GetEntity().GetDepartmentType();
            AvailableCharacters availableCharacters =
                HiringManager.Instance.m_availableCharacters[department];
            if (availableCharacters == null)
            {
                return;
            }

            __state = new NurseListSwapState
            {
                AvailableCharacters = availableCharacters,
                OriginalNurses = availableCharacters.m_availableNormalNurses
            };

            availableCharacters.m_availableNormalNurses =
                PorterCandidatePool.Ensure(department);
        }

        private static Exception Finalizer(
            Exception __exception,
            NurseListSwapState __state)
        {
            if (__state?.AvailableCharacters != null)
            {
                __state.AvailableCharacters.m_availableNormalNurses =
                    __state.OriginalNurses;
            }

            return __exception;
        }
    }

    internal static class DeferredHiringPatches
    {
        private static bool s_applied;
        private static bool s_applying;

        internal static void Apply(Harmony harmony)
        {
            if (s_applied || s_applying || harmony == null)
            {
                return;
            }

            if (Database.Instance == null || !Database.Instance.Loaded)
            {
                Plugin.Log?.LogWarning("HiringManager patches were requested before Database.Loaded; deferring them.");
                return;
            }

            s_applying = true;
            try
            {
                HiringManager nativeManager = HiringManager.Instance;
                if (nativeManager == null)
                {
                    throw new InvalidOperationException("HiringManager.Instance returned null after database loading.");
                }

                Patch(
                    harmony,
                    AccessTools.Method(
                        typeof(HiringManager),
                        nameof(HiringManager.DestroyAvailableCharacters),
                        new Type[] { typeof(AvailableCharacters), typeof(LopitalTypes), typeof(bool) }),
                    typeof(HiringDestroyCandidatesPatch),
                    prefixName: nameof(HiringDestroyCandidatesPatch.Prefix));

                Patch(
                    harmony,
                    AccessTools.Method(
                        typeof(HiringManager),
                        nameof(HiringManager.GenerateAvailableCharacters),
                        new Type[] { typeof(AvailableCharacters), typeof(GameDBDepartment), typeof(LopitalTypes), typeof(bool) }),
                    typeof(HiringGenerateCandidatesPatch),
                    prefixName: nameof(HiringGenerateCandidatesPatch.Prefix));

                Patch(
                    harmony,
                    AccessTools.Method(typeof(HiringManager), nameof(HiringManager.Reset), Type.EmptyTypes),
                    typeof(HiringResetPatch),
                    postfixName: nameof(HiringResetPatch.Postfix));

                Patch(
                    harmony,
                    AccessTools.Method(
                        typeof(HiringManager),
                        nameof(HiringManager.Hire),
                        new Type[] { typeof(Department), typeof(Floor), typeof(Vector2i) }),
                    typeof(HiringHirePatch),
                    prefixName: nameof(HiringHirePatch.Prefix),
                    finalizerName: nameof(HiringHirePatch.Finalizer));

                s_applied = true;
                PorterDiagnostics.Log("Deferred HiringManager patches applied after native HiringManager initialization.");
            }
            catch (TypeInitializationException exception)
            {
                Plugin.Log?.LogError("HiringManager type initialization failed after database loading: " + exception);
                if (exception.InnerException != null)
                {
                    Plugin.Log?.LogError("HiringManager inner exception: " + exception.InnerException);
                }
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogError("Failed to apply deferred HiringManager patches: " + exception);
            }
            finally
            {
                s_applying = false;
            }
        }

        internal static void Reset()
        {
            s_applied = false;
            s_applying = false;
        }

        private static void Patch(
            Harmony harmony,
            MethodBase original,
            Type patchType,
            string prefixName = null,
            string postfixName = null,
            string finalizerName = null)
        {
            if (object.ReferenceEquals(original, null))
            {
                throw new MissingMethodException("Could not resolve a deferred HiringManager method.");
            }

            HarmonyMethod prefix = CreateHarmonyMethod(patchType, prefixName);
            HarmonyMethod postfix = CreateHarmonyMethod(patchType, postfixName);
            HarmonyMethod finalizer = CreateHarmonyMethod(patchType, finalizerName);

            harmony.Patch(original, prefix, postfix, null, finalizer, null);
        }

        private static HarmonyMethod CreateHarmonyMethod(Type patchType, string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            MethodInfo method = AccessTools.Method(patchType, methodName);
            if (object.ReferenceEquals(method, null))
            {
                throw new MissingMethodException(patchType.FullName, methodName);
            }
            return new HarmonyMethod(method);
        }
    }

    internal static class HiringDestroyCandidatesPatch
    {
        internal static bool Prefix(LopitalTypes characterType, bool forceAllcharacters)
        {
            if (!PorterHiringState.Active || forceAllcharacters || characterType != LopitalTypes.CharacterNurse)
            {
                return true;
            }

            GameDBDepartment department = Hospital.Instance.m_activeDepartment.GetEntity().GetDepartmentType();
            PorterCandidatePool.DestroyForDepartment(department);
            return false;
        }
    }

    internal static class HiringGenerateCandidatesPatch
    {
        internal static bool Prefix(GameDBDepartment gameDBDepartment, LopitalTypes characterType, bool forceAllcharacters)
        {
            if ((!PorterHiringState.Active && !PorterHiringState.HiringPorter) ||
                forceAllcharacters ||
                characterType != LopitalTypes.CharacterNurse)
            {
                return true;
            }

            PorterCandidatePool.Ensure(gameDBDepartment);
            return false;
        }
    }

    internal static class HiringResetPatch
    {
        internal static void Postfix()
        {
            PorterHiringState.Reset();
            PorterCandidatePool.Clear(destroyEntities: true);
        }
    }

    internal static class HiringHirePatch
    {
        internal static void Prefix(ref GameDBDepartment __state)
        {
            Entity hired = HiringManager.Instance.HiredCharacter;
            if (!PorterIdentity.IsPorter(hired))
            {
                return;
            }

            __state = hired.GetComponent<EmployeeComponent>().m_state.m_hiredForDepartment.Entry;
            PorterCandidatePool.Remove(hired);
            PorterHiringState.HiringPorter = true;
        }

        internal static Exception Finalizer(Exception __exception, GameDBDepartment __state)
        {
            if (__state != null)
            {
                PorterCandidatePool.Ensure(__state);
            }
            PorterHiringState.HiringPorter = false;
            return __exception;
        }
    }
}
