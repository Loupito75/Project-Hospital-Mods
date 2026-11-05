using System;
using HarmonyLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalFixes.Patches
{
    internal static class WaitingRoomNotificationText
    {
        internal static bool IsWaitingRoomNotificationText(
            string textLocId)
        {
            return textLocId == "NOTIF_NO_WAITING_ROOM_TEXT" ||
                textLocId == "NOTIF_NO_WAITING_ROOM_LAB_TEXT" ||
                textLocId == "NOTIF_NO_WAITING_ROOM_RADIOLOGY_TEXT";
        }

        internal static string GetGuidance()
        {
            return LocalizationManager.Get(
                HfxLocalizationIds.WaitingRoomGuidance);
        }
    }

    [HarmonyPatch(
        typeof(NotificationMessage),
        nameof(NotificationMessage.GetLocalizedMessage),
        new Type[] { })]
    internal static class WaitingRoomNotificationTextPatch
    {
        private static void Postfix(
            NotificationMessage __instance,
            ref string __result)
        {
            if (!FixConfig.ClarifyWaitingRoomNotifications ||
                __instance == null ||
                !WaitingRoomNotificationText.IsWaitingRoomNotificationText(
                    __instance.m_textLocID))
            {
                return;
            }

            string guidance = WaitingRoomNotificationText.GetGuidance();
            if (string.IsNullOrEmpty(guidance) ||
                string.IsNullOrEmpty(__result) ||
                __result.IndexOf(guidance, StringComparison.Ordinal) >= 0)
            {
                return;
            }

            __result = __result.Trim() + " " + guidance;
            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Result(
                    "ClarifyWaitingRoomNotifications",
                    "NotificationMessage.GetLocalizedMessage",
                    "guidance-appended",
                    "textLocId=" + __instance.m_textLocID);
            }
        }
    }

    [HarmonyPatch(typeof(PopupPanelController), nameof(PopupPanelController.Set))]
    internal static class WaitingRoomNotificationCompactLayoutPatch
    {
        private const float CompactBodyMargin = 6f;

        private static void Postfix(
            PopupPanelController __instance,
            NotificationMessage message)
        {
            if (!FixConfig.ClarifyWaitingRoomNotifications ||
                __instance == null ||
                message == null ||
                !WaitingRoomNotificationText.IsWaitingRoomNotificationText(
                    message.m_textLocID) ||
                __instance.m_textMessage == null ||
                __instance.m_textMessageTitle == null)
            {
                return;
            }

            RectTransform bodyRect =
                __instance.m_textMessage.GetComponent<RectTransform>();
            Text titleText =
                __instance.m_textMessageTitle.GetComponent<Text>();

            if (bodyRect == null ||
                titleText == null)
            {
                return;
            }

            string title = titleText.text ?? string.Empty;
            float titleHeight =
                UIManager.GetUIScale() > 1f && title.Length > 33
                    ? 45f
                    : titleText.preferredHeight;

            Vector2 position = bodyRect.anchoredPosition;
            bodyRect.anchoredPosition =
                new Vector2(
                    position.x,
                    0f - titleHeight - CompactBodyMargin);
        }
    }

}
