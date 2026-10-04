using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    [HarmonyPatch(typeof(PorterHiringUi), "SelectPorters")]
    internal static class PorterManualHiringRefreshPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(HiringPanelController controller)
        {
            if (controller == null || PorterHiringOpenState.Depth > 0 ||
                !PorterHiringState.Active)
            {
                return;
            }

            // SetCharacterType() does not rebuild candidate cards.
            // Refresh through the native Update() path so stale Nurse cards are not displayed as Porters.
            controller.Update();
            PorterHiringUi.UpdateStaffingVisual(controller);
        }
    }
}
