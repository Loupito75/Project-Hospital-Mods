using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine.UI;

namespace HospitalFixes.Patches
{
    // Update() has already selected the current department/category candidate list.
    [HarmonyPatch(typeof(HiringPanelController), "Update")]
    internal static class HiringPerkRevealPatch
    {
        [HarmonyPostfix]
        private static void Postfix(HiringPanelController __instance, List<Entity> ___m_characters)
        {
            if (!FixConfig.FixHiringPerkRevealCost || __instance.m_buttonUncoverPerks == null)
            {
                return;
            }

            bool hasHidden = false;
            if (___m_characters != null)
            {
                foreach (Entity candidate in ___m_characters)
                {
                    if (candidate == null)
                    {
                        continue;
                    }
                    PerkComponent perks = candidate.GetComponent<PerkComponent>();
                    if (perks != null && perks.m_perkSet != null &&
                        perks.m_perkSet.GetHiddenPerkCount() > 0)
                    {
                        hasHidden = true;
                        break;
                    }
                }
            }

            IconButtonController iconButton =
                __instance.m_buttonUncoverPerks
                    .GetComponent<IconButtonController>();
            Button button =
                __instance.m_buttonUncoverPerks
                    .GetComponent<Button>();

            if (iconButton != null)
            {
                // Keep the normal Vanilla icon. IconButtonController's
                // inactive state swaps to a separate inactive sprite and
                // plays the forbidden sound when clicked, which is not the
                // desired behavior here.
                iconButton.SetInactive(false);
            }

            if (button != null)
            {
                // Use Unity's normal disabled-button presentation so the
                // control remains visible, grayed, and completely non-
                // clickable (therefore no click sound) when nothing remains
                // to reveal.
                bool changed =
                    button.interactable != hasHidden;
                button.interactable = hasHidden;

                if (changed && HfxDiagnostics.Enabled)
                {
                    HfxDiagnostics.Result(
                        "FixHiringPerkRevealCost",
                        "HiringPanelController.Update",
                        hasHidden ? "reveal-button-enabled" : "reveal-button-disabled",
                        "hiddenPerksAvailable=" + hasHidden);
                }
            }
        }
    }
}
