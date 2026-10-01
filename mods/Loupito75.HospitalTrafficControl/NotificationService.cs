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

            GameDBNotification notification =
                Database.Instance.GetEntry<GameDBNotification>(
                    LocalizationManager.NoPathTitleId);

            if (notification == null)
            {
                Plugin.Log?.LogWarning(
                    "Blocked-path notification skipped because the HTC notification database entry is missing.");
                return;
            }

            NotificationLevel level =
                PlayerProfile.Instance.GetNotificationLevel(
                    LocalizationManager.NoPathTitleId);

            if (level == NotificationLevel.NONE)
            {
                return;
            }

            NotificationManager manager = NotificationManager.GetInstance();
            if (manager == null)
            {
                Plugin.Log?.LogWarning(
                    "Blocked-path notification skipped because NotificationManager is not ready.");
                return;
            }

            if (level == NotificationLevel.POPUP &&
                (DayTime.Instance == null || GameTimeController.Instance == null))
            {
                Plugin.Log?.LogWarning(
                    "Blocked-path popup skipped because the game-time UI is not ready.");
                return;
            }

            string characterName = entity.Name?.Trim() ?? string.Empty;
            Vector2i position = walk.GetCurrentTile();

            manager.AddMessage(
                entity,
                LocalizationManager.NoPathTitleId,
                characterName,
                string.Empty,
                string.Empty,
                0,
                position.m_x,
                position.m_y,
                walk.GetFloorIndex());
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
