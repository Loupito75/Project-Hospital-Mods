using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    /// <summary>
    /// Save/load recovery for NEED_BLADDER procedures that were saved after the
    /// physical WC step. The normal BathroomFlowPatch releases the WC owner when
    /// UpdateStateUsingObject transitions away from USING_OBJECT. A loaded script
    /// can resume directly in a later state, so that transition is not replayed.
    /// </summary>
    [HarmonyPatch(typeof(ProcedureScriptNeedBladder), "ScriptUpdate")]
    internal static class BathroomLoadedToiletOwnerRecoveryPatch
    {
        private static void Postfix(ProcedureScriptNeedBladder __instance)
        {
            if (!TrafficControlConfig.ReleaseToiletOwnerAfterUse ||
                __instance == null ||
                __instance.m_stateData == null ||
                __instance.m_stateData.m_procedureScene == null)
            {
                return;
            }

            string state = __instance.m_stateData.m_state;
            if (!IsAfterPhysicalToiletUse(state))
            {
                return;
            }

            TileObject toilet = __instance.GetEquipment(0);
            if (toilet == null ||
                toilet.User != null ||
                toilet.Owner != __instance)
            {
                return;
            }

            toilet.Owner = null;

            if (TrafficControlConfig.BathroomFlowDebug)
            {
                Plugin.Log?.LogInfo(
                    "[BathroomDebug] Recovered WC owner after load/late state" +
                    " | character=" +
                    BathroomFlowDiagnostics.CharacterName(
                        __instance.m_stateData.m_procedureScene.MainCharacter) +
                    " | wc=" + BathroomFlowDiagnostics.ObjectName(toilet) +
                    " | state=" + state);
            }
        }

        private static bool IsAfterPhysicalToiletUse(string state)
        {
            return state == ProcedureScriptNeedBladder.STATE_GOING_TO_SINK ||
                   state == ProcedureScriptNeedBladder.STATE_USING_SINK ||
                   state == ProcedureScriptNeedBladder.STATE_USING_SINK_GERMAPHOBE ||
                   state == ProcedureScriptNeedBladder.STATE_GOING_TO_DRYER ||
                   state == ProcedureScriptNeedBladder.STATE_USING_DRYER ||
                   state == ProcedureScriptNeedBladder.STATE_IDLE;
        }
    }
}
