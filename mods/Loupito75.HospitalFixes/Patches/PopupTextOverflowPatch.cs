using HarmonyLib;
using UnityEngine.UI;

namespace HospitalFixes.Patches
{
    [HarmonyPatch(typeof(PopupPanelController), nameof(PopupPanelController.Set))]
    internal static class PopupTextOverflowPatch
    {
        private const int MinimumBodyFontSize = 6;

        private static void Prefix(PopupPanelController __instance)
        {
            if (!FixConfig.FixPopupTextOverflow ||
                __instance == null ||
                __instance.m_textMessage == null)
            {
                return;
            }

            Text body = __instance.m_textMessage.GetComponentInChildren<Text>();
            if (body == null || !body.resizeTextForBestFit)
            {
                return;
            }

            // Vanilla already uses BestFit (16 -> 12) for popup bodies.
            // Lowering only the minimum to 6 keeps normal popups unchanged while
            // allowing unusually long localized text to shrink instead of
            // overflowing the fixed MessageText rectangle.
            if (body.resizeTextMinSize > MinimumBodyFontSize)
            {
                int previousMinimum = body.resizeTextMinSize;
                body.resizeTextMinSize = MinimumBodyFontSize;
                if (HfxDiagnostics.Enabled)
                {
                    HfxDiagnostics.InterventionRateLimited(
                        "FixPopupTextOverflow",
                        "PopupPanelController.Set",
                        "best-fit-minimum-expanded",
                        "minFont=" + previousMinimum + "->" + MinimumBodyFontSize,
                        "FixPopupTextOverflow",
                        3000);
                }
            }
        }
    }
}
