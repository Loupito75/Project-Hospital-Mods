using System;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    internal static class RuntimeNotificationProfile
    {
        internal static bool IsHtcCategory(string category)
        {
            return string.Equals(
                category,
                NotificationPreferences.CategoryId,
                StringComparison.Ordinal);
        }
    }

    [HarmonyPatch(
        typeof(PlayerProfile),
        nameof(PlayerProfile.GetNotificationLevel),
        new Type[] { typeof(string) })]
    internal static class HtcGetNotificationLevelPatch
    {
        private static bool Prefix(string category, ref NotificationLevel __result)
        {
            if (!RuntimeNotificationProfile.IsHtcCategory(category))
            {
                return true;
            }

            __result = NotificationPreferences.Level;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(PlayerProfile),
        nameof(PlayerProfile.GetNotificationColorLevel),
        new Type[] { typeof(string) })]
    internal static class HtcGetNotificationColorPatch
    {
        private static bool Prefix(string category, ref GameDBNotificationColor __result)
        {
            if (!RuntimeNotificationProfile.IsHtcCategory(category))
            {
                return true;
            }

            __result = NotificationPreferences.GetColor();
            return false;
        }
    }

    [HarmonyPatch(
        typeof(PlayerProfile),
        nameof(PlayerProfile.SetNotificationLevel),
        new Type[] { typeof(string), typeof(NotificationLevel) })]
    internal static class HtcSetNotificationLevelPatch
    {
        private static bool Prefix(string category, NotificationLevel level)
        {
            if (!RuntimeNotificationProfile.IsHtcCategory(category))
            {
                return true;
            }

            NotificationPreferences.SetLevel(level);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(PlayerProfile),
        nameof(PlayerProfile.SetNotificationColorLevel),
        new Type[] { typeof(string), typeof(GameDBNotificationColor) })]
    internal static class HtcSetNotificationColorPatch
    {
        private static bool Prefix(string category, GameDBNotificationColor level)
        {
            if (!RuntimeNotificationProfile.IsHtcCategory(category))
            {
                return true;
            }

            NotificationPreferences.SetColor(level);
            return false;
        }
    }
}
