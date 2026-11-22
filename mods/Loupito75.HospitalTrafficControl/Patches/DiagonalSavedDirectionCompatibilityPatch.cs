using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    // Development builds with the experimental cardinal-walk prototype could
    // persist N/E/S/W directly in AnimModelComponentPersistentData. Public HTC
    // does not install those runtime animation instances, so normalize the old
    // development state before vanilla rebuilds its four-direction controller.
    [HarmonyPatch(
        typeof(AnimModelComponent),
        MethodType.Constructor,
        new System.Type[]
        {
            typeof(Entity),
            typeof(AnimModelComponentPersistentData)
        })]
    internal static class DiagonalSavedDirectionCompatibilityPatch
    {
        private static void Prefix(AnimModelComponentPersistentData __1)
        {
            if (__1 == null)
            {
                return;
            }

            switch (__1.m_direction)
            {
                case Direction.N:
                    __1.m_direction = Direction.NE;
                    break;
                case Direction.E:
                    __1.m_direction = Direction.SE;
                    break;
                case Direction.S:
                    __1.m_direction = Direction.SW;
                    break;
                case Direction.W:
                    __1.m_direction = Direction.NW;
                    break;
            }
        }
    }
}
