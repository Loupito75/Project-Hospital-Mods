using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    [HarmonyPatch(typeof(PorterHiringUi), "SelectPorters")]
    internal static class PorterHiringStationGuardPatch
    {
        private static bool Prefix()
        {
            // Suppress automatic Porter selections until the native
            // OpenHiringCard transaction has fully completed. Do not emit locker warnings for
            // those deliberately skipped intermediate attempts. The final authoritative
            // selection runs with Depth == 0 and therefore uses this guard normally.
            if (Porter048HiringOpenState.Depth > 0)
            {
                return true;
            }

            if (Hospital.Instance == null || Hospital.Instance.m_activeDepartment.GetEntity() == null)
            {
                return true;
            }

            Department department = Hospital.Instance.m_activeDepartment.GetEntity();
            if (PorterStationRegistry.HasValidStation(department) &&
                PorterStationRegistry.HasAvailableLocker(department, HiringManager.Instance.m_shift))
            {
                return true;
            }

            PorterHiringState.Active = false;
            Plugin.Log?.LogWarning(
                "Porter hiring blocked: the active hospitalization department needs a valid Porter station with a free locker for the selected shift.");

            if (UISoundManager.sm_instance != null)
            {
                UISoundManager.sm_instance.PlaySoundEvent("SFX_UI_FORBIDDEN");
            }
            return false;
        }
    }
}
