using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    [HarmonyPatch(typeof(PorterHiringUi), "SelectPorters")]
    internal static class PorterHiringStationGuardPatch
    {
        private static bool Prefix()
        {
            // Ignore intermediate Porter selections while OpenHiringCard is still running.
            // The final selection runs at Depth == 0 and performs the normal station guard.
            if (PorterHiringOpenState.Depth > 0)
            {
                return true;
            }

            if (Hospital.Instance == null || Hospital.Instance.m_activeDepartment.GetEntity() == null)
            {
                return true;
            }

            Department department = Hospital.Instance.m_activeDepartment.GetEntity();
            if (PorterStationRegistry.HasValidStation(department))
            {
                return true;
            }

            PorterHiringState.Active = false;
            Plugin.Log?.LogWarning(
                "Porter hiring blocked: the active hospitalization department has no valid Porter station.");

            if (UISoundManager.sm_instance != null)
            {
                UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_FORBIDDEN");
            }
            return false;
        }
    }
}
