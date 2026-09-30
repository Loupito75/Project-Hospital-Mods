using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    [HarmonyPatch(typeof(PorterHiringUi), "SelectPorters")]
    internal static class Porter0414ManualHiringRefreshPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(HiringPanelController controller)
        {
            if (controller == null || Porter048HiringOpenState.Depth > 0 ||
                !PorterHiringState.Active)
            {
                return;
            }

            // SetCharacterType() changes the title/category but does not rebuild candidate cards.
            // A manual Porter-filter click must refresh through the same native Update() path used
            // by the authoritative OpenHiringCard finalization, otherwise stale Nurse cards remain
            // under the "Porter candidates" title.
            controller.Update();
            PorterHiringUi.UpdateStaffingVisual(controller);
        }
    }
}
