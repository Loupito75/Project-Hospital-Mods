using GLib;
using Lopital;
using UnityEngine;

namespace HospitalTrafficControl
{
    internal static class NotificationService
    {
        internal static void AddBlockedPathMessage(Entity entity, WalkComponent walk)
        {
            if (entity == null || walk == null)
            {
                return;
            }

            MapEditorController editor = MapEditorController.sm_instance;
            if (editor == null || editor.m_unloading || IsLoadingScreenVisible())
            {
                return;
            }

            NotificationLevel level = NotificationPreferences.Level;
            if (level == NotificationLevel.NONE)
            {
                return;
            }

            NotificationManager manager = NotificationManager.GetInstance();
            DayTime dayTime = DayTime.Instance;
            GameTimeController gameTime = GameTimeController.Instance;
            GameDBNotificationColor color = NotificationPreferences.GetColor();

            if (manager == null || dayTime == null || gameTime == null || color == null)
            {
                Plugin.Log?.LogWarning(
                    "Blocked-path notification skipped because native notification UI is not ready.");
                return;
            }

            string characterName = entity.Name?.Trim() ?? string.Empty;
            bool isPatient = entity.GetComponent<BehaviorPatient>() != null;

            var message = new NotificationMessage
            {
                m_character = entity,
                m_textTitleLocID = LocalizationManager.NoPathTitleId,
                m_textLocID = isPatient
                    ? LocalizationManager.NoPathPatientTextId
                    : LocalizationManager.NoPathTextId,
                m_textParameter = characterName,
                m_textParameter2 = string.Empty,
                m_textParameter3 = string.Empty,
                m_iconOverride = 0,
                m_actionButtonA = PopupButtonAction.POPUP_GOTO,
                m_actionButtonB = PopupButtonAction.POPUP_HIDDEN_BUTTON,
                m_actionButtonC = PopupButtonAction.POPUP_OK,
                m_color = color,
                m_position = walk.GetCurrentTile(),
                m_floorIndex = walk.GetFloorIndex(),
                m_pauseGame = level == NotificationLevel.POPUP,
                m_unClosable = false,
                m_iconOverrideAssetID = null
            };

            if (level == NotificationLevel.LOG)
            {
                manager.m_messages.Add(message);
            }
            else
            {
                manager.m_popups.Add(message);
                manager.m_messages.Add(message);
                dayTime.ResetFastForwardTime();

                if (gameTime.TimeMultiplier == 0 &&
                    manager.m_pauseState != NotificationPauseState.GAME_PAUSED_BY_POPUP)
                {
                    manager.m_pauseState = NotificationPauseState.GAME_PAUSED;
                }
                else
                {
                    manager.m_pauseState = NotificationPauseState.GAME_PAUSED_BY_POPUP;
                }

                gameTime.TimeMultiplier = 0;
            }

            manager.m_lastShownMessageIndex = manager.m_messages.Count - 1;

            if (UISoundManager.sm_instance != null)
            {
                UISoundManager.sm_instance.PlayPopup();
            }
        }

        private static bool IsLoadingScreenVisible()
        {
            LoadingTipController loading = LoadingTipController.Instance;
            if (loading == null)
            {
                return false;
            }

            return IsActive(loading.m_loadingTipEscBackGround) ||
                   IsActive(loading.m_loadingTipBackGround) ||
                   IsActive(loading.m_loadingTipLogo) ||
                   IsActive(loading.m_loadingTip) ||
                   IsActive(loading.m_loadingTipText) ||
                   IsActive(loading.m_loadingText);
        }

        private static bool IsActive(GameObject gameObject)
        {
            return gameObject != null && gameObject.activeSelf;
        }
    }
}
