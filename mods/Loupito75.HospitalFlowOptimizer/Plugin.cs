using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Lopital;

namespace HospitalFlowOptimizer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "loupito75.HospitalFlowOptimizer";
        public const string HarmonyId = "Loupito75:HospitalFlowOptimizer";
        public const string PluginName = "Hospital Flow Optimizer";
        public const string PluginAuthor = "Loupito75";
        public const string PluginVersion = "1.1.0";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private ManualLogSource _formattedLog;

        private void Awake()
        {
            _formattedLog = BepInEx.Logging.Logger.CreateLogSource("   " + HarmonyId);
            Log = _formattedLog;

            FlowOptimizerConfig.Load();

            _harmony = new Harmony(HarmonyId);
            try
            {
                _harmony.PatchAll();
            }
            catch (Exception exception)
            {
                _harmony.UnpatchSelf();
                Log.LogError(
                    PluginName + " could not apply its Harmony patches and was disabled: " +
                    exception.GetType().Name + ": " + exception.Message);
                return;
            }

            if (Database.Instance != null && Database.Instance.Loaded)
            {
                HfoDatabase.Load(Database.Instance);
            }

            Log.LogInfo(
                PluginName + " " + PluginVersion + " by " + PluginAuthor + " loaded.");
        }

        internal static void ClearRuntimeState()
        {
            HfoDiagnostics.Clear();
            Patches.SmartStaffNeedResourcePatch.ClearCachedProcedures();
            Patches.SmartTransportWaitRuntime.Clear();
            Patches.SmartTransportContinuationRuntime.Clear();
            Patches.SmartTransportSampleContinuationRuntime.Clear();
            Patches.SmartRoomSelectionRuntime.Clear();
            Patches.OutpatientLabRoutingRuntime.Clear();
            Patches.LabStaleGuardRuntime.Clear();
            Patches.SmartLabTaskAssignmentPatch.ClearDiagnosticState();
            Patches.TraumaCenterProtectionRuntime.Clear();
        }

        private void OnDestroy()
        {
            ClearRuntimeState();

            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }

            if (_formattedLog != null)
            {
                BepInEx.Logging.Logger.Sources.Remove(_formattedLog);
                _formattedLog = null;
                Log = null;
            }
        }
    }

    [HarmonyPatch(typeof(Hospital), nameof(Hospital.Destroy), new Type[] { })]
    internal static class HfoHospitalDestroyPatch
    {
        private static void Prefix()
        {
            Plugin.ClearRuntimeState();
        }
    }

    [HarmonyPatch(typeof(Hospital), nameof(Hospital.Reset), new Type[] { })]
    internal static class HfoHospitalResetPatch
    {
        private static void Prefix()
        {
            Plugin.ClearRuntimeState();
        }
    }
}
