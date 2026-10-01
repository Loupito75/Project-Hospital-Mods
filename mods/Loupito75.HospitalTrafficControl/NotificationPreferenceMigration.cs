using Lopital;
using UnityEngine;

namespace HospitalTrafficControl
{
    internal static class NotificationPreferenceMigration
    {
        private const string LevelKey = "HTC_V2_NOTIF_NO_PATH_LEVEL";
        private const string ColorKey = "HTC_V2_NOTIF_NO_PATH_COLOR";
        private const string RedDefaultMigrationKey = "HTC_V3_NOTIF_NO_PATH_RED_DEFAULT_MIGRATED";
        private const string NativeMigrationKey = "HTC_V4_NOTIF_NATIVE_PROFILE_MIGRATED";
        private const string LegacyDefaultColorId = "NOTIF_COLOR_DEFAULT";
        private const string RedDefaultColorId = "NOTIF_COLOR_RED";

        internal static void MigrateLegacyPreferences()
        {
            if (PlayerPrefs.HasKey(NativeMigrationKey) ||
                Database.Instance.GetEntry<GameDBNotification>(
                    LocalizationManager.NoPathTitleId) == null)
            {
                return;
            }

            PlayerProfile profile = PlayerProfile.Instance;

            if (!PlayerPrefs.HasKey(LocalizationManager.NoPathTitleId) &&
                PlayerPrefs.HasKey(LevelKey))
            {
                NotificationLevel level = ReadLegacyLevel();
                profile.SetNotificationLevel(
                    LocalizationManager.NoPathTitleId,
                    level);
                PlayerPrefs.SetString(
                    LocalizationManager.NoPathTitleId,
                    level.ToString());
            }

            string nativeColorKey = LocalizationManager.NoPathTitleId + "Color";
            if (!PlayerPrefs.HasKey(nativeColorKey) &&
                PlayerPrefs.HasKey(ColorKey))
            {
                GameDBNotificationColor color = ReadLegacyColor();
                if (color != null)
                {
                    profile.SetNotificationColorLevel(
                        LocalizationManager.NoPathTitleId,
                        color);
                    PlayerPrefs.SetString(
                        nativeColorKey,
                        color.DatabaseID.ToString());
                }
            }

            PlayerPrefs.SetInt(NativeMigrationKey, 1);
            PlayerPrefs.Save();
        }

        private static NotificationLevel ReadLegacyLevel()
        {
            string stored = PlayerPrefs.GetString(LevelKey);

            if (stored == NotificationLevel.NONE.ToString())
            {
                return NotificationLevel.NONE;
            }

            if (stored == NotificationLevel.LOG.ToString())
            {
                return NotificationLevel.LOG;
            }

            return NotificationLevel.POPUP;
        }

        private static GameDBNotificationColor ReadLegacyColor()
        {
            string colorId = PlayerPrefs.GetString(ColorKey);

            if (colorId == LegacyDefaultColorId &&
                !PlayerPrefs.HasKey(RedDefaultMigrationKey))
            {
                colorId = RedDefaultColorId;
            }

            GameDBNotificationColor color =
                Database.Instance.GetEntry<GameDBNotificationColor>(colorId);

            if (color != null)
            {
                return color;
            }

            color = Database.Instance.GetEntry<GameDBNotificationColor>(RedDefaultColorId);
            if (color != null)
            {
                return color;
            }

            return Database.Instance.GetEntry<GameDBNotificationColor>(LegacyDefaultColorId);
        }
    }
}
