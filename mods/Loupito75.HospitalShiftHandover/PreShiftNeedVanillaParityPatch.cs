using HarmonyLib;
using Lopital;

namespace HospitalShiftHandover
{
    // Vanilla BehaviorDoctor / BehaviorNurse / BehaviorLabSpecialist callers hide the
    // need procedure bubble immediately after a successful CheckNeeds() before switching
    // to the fulfilling-needs state. HSH calls CheckNeeds() directly, so reproduce that
    // native caller-side behavior at the single HSH transition point.
    [HarmonyPatch(typeof(PreShiftNeedEngine), "SwitchToNeeds")]
    internal static class PreShiftNeedVanillaParityPatch
    {
        private static void Prefix(Behavior behavior)
        {
            if (behavior == null)
            {
                return;
            }

            SpeechComponent speech = behavior.GetComponent<SpeechComponent>();
            if (speech != null)
            {
                speech.HideBubble();
            }
        }
    }
}
