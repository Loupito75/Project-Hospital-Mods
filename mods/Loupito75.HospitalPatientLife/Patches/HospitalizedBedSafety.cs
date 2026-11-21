using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class HospitalizedBedSafety
    {
        internal static bool CanUseAssignedBed(
            HospitalizationComponent hospitalization)
        {
            if (hospitalization == null ||
                hospitalization.m_state == null ||
                hospitalization.m_state.m_bed == null)
            {
                return false;
            }

            TileObject bed = hospitalization.m_state.m_bed.GetEntity();
            if (bed == null ||
                bed.m_state == null ||
                bed.m_state.m_compositeParent == null ||
                bed.m_state.m_compositeParent.GetEntity() == null)
            {
                return false;
            }

            return true;
        }
    }
}
