using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(BehaviorPatient), nameof(BehaviorPatient.GoToWaitingRoom))]
    internal static class WaitingRoomGoToDiagnosticsPatch
    {
        private static void Prefix(BehaviorPatient __instance)
        {
            WaitingRoomDiagnostics.OnGoToWaitingRoomPrefix(__instance);
        }

        private static void Postfix(BehaviorPatient __instance)
        {
            WaitingRoomDiagnostics.OnGoToWaitingRoomPostfix(__instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorPatient),
        nameof(BehaviorPatient.Leave),
        new System.Type[]
        {
            typeof(bool),
            typeof(bool),
            typeof(bool)
        })]
    internal static class WaitingRoomLeaveDiagnosticsPatch
    {
        private static void Prefix(
            BehaviorPatient __instance,
            bool pay,
            bool leaveAfterHours,
            bool leavingHospitalizationPatient)
        {
            WaitingRoomDiagnostics.OnLeavePrefix(
                __instance,
                pay,
                leaveAfterHours,
                leavingHospitalizationPatient);
        }
    }
}
