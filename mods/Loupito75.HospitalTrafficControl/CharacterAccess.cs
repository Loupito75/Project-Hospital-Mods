using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class CharacterAccess
    {
        internal static Entity GetEntity(WalkComponent walk)
        {
            return walk == null ? null : walk.m_entity;
        }

        internal static bool MustRespectStaffOnly(WalkComponent walk)
        {
            Entity entity = GetEntity(walk);
            Behavior behavior = entity?.GetComponent<Behavior>();

            return behavior != null &&
                   (int)behavior.GetAccessRights() < (int)AccessRights.STAFF;
        }

        internal static bool CanBeRestrictedByAccessChange(WalkComponent walk)
        {
            Entity entity = GetEntity(walk);
            Behavior behavior = entity?.GetComponent<Behavior>();

            return behavior != null &&
                   (int)behavior.GetAccessRights() < (int)AccessRights.STAFF_ONLY;
        }
    }
}
