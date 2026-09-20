using System;
using Lopital;
using UnityEngine;

namespace HospitalTrafficControl
{
    internal static class NotificationPreferences
    {
        internal const string CategoryId = LocalizationManager.NoPathTitleId;
        internal const NotificationLevel DefaultLevel = NotificationLevel.POPUP;

        private const string LevelKey = "HTC_V2_NOTIF_NO_PATH_LEVEL";
        private const string ColorKey = "HTC_V2_NOTIF_NO_PATH_COLOR";
        private const string RedDefaultMigrationKey = "HTC_V3_NOTIF_NO_PATH_RED_DEFAULT_MIGRATED";
        private const string LegacyDefaultColorId = "NOTIF_COLOR_DEFAULT";

        private static string _resolvedRedColorId;

        internal static NotificationLevel Level
        {
            get
            {
                if (!PlayerPrefs.HasKey(LevelKey))
                {
                    return DefaultLevel;
                }

                string stored = PlayerPrefs.GetString(LevelKey);

                if (stored == NotificationLevel.NONE.ToString())
                {
                    return NotificationLevel.NONE;
                }

                if (stored == NotificationLevel.LOG.ToString())
                {
                    return NotificationLevel.LOG;
                }

                if (stored == NotificationLevel.POPUP.ToString())
                {
                    return NotificationLevel.POPUP;
                }

                return DefaultLevel;
            }
        }

        internal static string ColorId
        {
            get
            {
                string defaultColorId = ResolveRedDefaultColorId();
                EnsureRedDefaultMigration(defaultColorId);

                string stored = PlayerPrefs.HasKey(ColorKey)
                    ? PlayerPrefs.GetString(ColorKey)
                    : defaultColorId;

                if (Database.Instance == null)
                {
                    return defaultColorId;
                }

                if (!string.IsNullOrEmpty(stored) &&
                    Database.Instance.GetEntry<GameDBNotificationColor>(stored) != null)
                {
                    return stored;
                }

                if (!string.IsNullOrEmpty(defaultColorId) &&
                    Database.Instance.GetEntry<GameDBNotificationColor>(defaultColorId) != null)
                {
                    return defaultColorId;
                }

                return LegacyDefaultColorId;
            }
        }

        internal static GameDBNotificationColor GetColor()
        {
            if (Database.Instance == null)
            {
                return null;
            }

            GameDBNotificationColor color =
                Database.Instance.GetEntry<GameDBNotificationColor>(ColorId);

            if (color != null)
            {
                return color;
            }

            color = Database.Instance.GetEntry<GameDBNotificationColor>(LegacyDefaultColorId);
            if (color != null)
            {
                return color;
            }

            GameDBNotificationColor[] colors =
                Database.Instance.GetEntries<GameDBNotificationColor>();

            return colors != null && colors.Length > 0
                ? colors[0]
                : null;
        }

        internal static void SetLevel(NotificationLevel level)
        {
            if (level != NotificationLevel.NONE &&
                level != NotificationLevel.LOG &&
                level != NotificationLevel.POPUP)
            {
                level = DefaultLevel;
            }

            PlayerPrefs.SetString(LevelKey, level.ToString());
            PlayerPrefs.Save();
        }

        internal static void SetColor(GameDBNotificationColor color)
        {
            string defaultColorId = ResolveRedDefaultColorId();
            string colorId = color != null
                ? color.DatabaseID.ToString()
                : defaultColorId;

            if (Database.Instance != null &&
                Database.Instance.GetEntry<GameDBNotificationColor>(colorId) == null)
            {
                colorId = defaultColorId;
            }

            PlayerPrefs.SetString(ColorKey, colorId);
            PlayerPrefs.Save();
        }

        private static string ResolveRedDefaultColorId()
        {
            if (!string.IsNullOrEmpty(_resolvedRedColorId))
            {
                return _resolvedRedColorId;
            }

            if (Database.Instance == null)
            {
                return LegacyDefaultColorId;
            }

            GameDBNotificationColor[] colors =
                Database.Instance.GetEntries<GameDBNotificationColor>();

            if (colors == null || colors.Length == 0)
            {
                return LegacyDefaultColorId;
            }

            GameDBNotificationColor bestRed = null;
            float bestScore = float.MinValue;

            foreach (GameDBNotificationColor color in colors)
            {
                if (color == null)
                {
                    continue;
                }

                string colorId = color.DatabaseID.ToString();
                if (!string.IsNullOrEmpty(colorId) &&
                    colorId.IndexOf("RED", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return RememberRedDefault(colorId);
                }

                float score = GetRedScore(color.ColorDark) + GetRedScore(color.ColorLight);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRed = color;
                }
            }

            if (bestRed != null && bestScore > 0f)
            {
                return RememberRedDefault(bestRed.DatabaseID.ToString());
            }

            return LegacyDefaultColorId;
        }

        private static float GetRedScore(GameDBColor color)
        {
            if (color == null)
            {
                return 0f;
            }

            float strongestOther = color.G > color.B ? color.G : color.B;
            return color.R - strongestOther;
        }

        private static string RememberRedDefault(string colorId)
        {
            _resolvedRedColorId = colorId;
            return colorId;
        }

        private static void EnsureRedDefaultMigration(string redColorId)
        {
            if (PlayerPrefs.HasKey(RedDefaultMigrationKey) ||
                Database.Instance == null ||
                string.IsNullOrEmpty(redColorId) ||
                redColorId == LegacyDefaultColorId ||
                Database.Instance.GetEntry<GameDBNotificationColor>(redColorId) == null)
            {
                return;
            }

            if (!PlayerPrefs.HasKey(ColorKey) ||
                PlayerPrefs.GetString(ColorKey) == LegacyDefaultColorId)
            {
                PlayerPrefs.SetString(ColorKey, redColorId);
            }

            PlayerPrefs.SetInt(RedDefaultMigrationKey, 1);
            PlayerPrefs.Save();
        }
    }
}
