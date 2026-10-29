using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(WalkComponent), "UpdateLookingForPath")]
    internal static class DiagonalRouteSmoothingPatch
    {
        [HarmonyPrepare]
        private static bool Prepare()
        {
            return TrafficControlConfig.ExperimentalDiagonalCornerSmoothing;
        }

        private static void Prefix(
            WalkComponent __instance,
            PathfinderJob ___m_pathfinderJob)
        {
            if (__instance == null ||
                ___m_pathfinderJob == null ||
                ___m_pathfinderJob.IsRunning ||
                !___m_pathfinderJob.IsDone ||
                ___m_pathfinderJob.m_result == null ||
                ___m_pathfinderJob.m_result.m_route == null)
            {
                return;
            }

            DiagonalRouteSmoothingManager.TrySmooth(
                __instance,
                ___m_pathfinderJob.m_result.m_route);
        }
    }
}
