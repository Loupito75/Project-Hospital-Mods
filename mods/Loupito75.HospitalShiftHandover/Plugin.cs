using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HospitalShiftHandover
{
    internal sealed class ReleaseLog
    {
        private readonly ManualLogSource _source;

        internal ReleaseLog(ManualLogSource source)
        {
            _source = source;
        }

        internal void LogInfo(object data)
        {
            if (_source == null || !ShiftHandoverConfig.DiagnosticsEnabled || ShouldSuppressPublicDiagnostic(data))
            {
                return;
            }

            _source.LogInfo(data);
        }

        internal void LogWarning(object data)
        {
            if (_source != null)
            {
                _source.LogWarning(data);
            }
        }

        internal void LogError(object data)
        {
            if (_source != null)
            {
                _source.LogError(data);
            }
        }

        private static bool ShouldSuppressPublicDiagnostic(object data)
        {
            string text = data as string;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            // Suppress high-frequency internal traces from normal troubleshooting logs.
            if (text.IndexOf("[STAGING_SELECTION]") >= 0)
            {
                return true;
            }

            if (text.IndexOf("PRE_SHIFT_ACTIVITY | type=need-wait") >= 0 ||
                text.IndexOf("PRE_SHIFT_ACTIVITY | type=need-check") >= 0 ||
                text.IndexOf("PRE_SHIFT_ACTIVITY | type=need-result") >= 0 ||
                text.IndexOf("PRE_SHIFT_ACTIVITY | type=need-assistance-decision") >= 0 ||
                text.IndexOf("PRE_SHIFT_ACTIVITY | type=return-handover-check") >= 0)
            {
                return true;
            }

            return false;
        }
    }

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "loupito75.HospitalShiftHandover";
        public const string PluginName = "Hospital Shift Handover";
        public const string PluginVersion = "1.1.1";
        public const string PluginAuthor = "Loupito75";
        public const string HarmonyId = "Loupito75:HospitalShiftHandover";
        public const string PluginDescription = "Improves staff arrivals, preparation, and shift handovers.";

        internal static ReleaseLog Log { get; private set; }

        private Harmony _harmony;
        private ManualLogSource _formattedLog;

        private void Awake()
        {
            _formattedLog = BepInEx.Logging.Logger.CreateLogSource("   " + HarmonyId);
            Log = new ReleaseLog(_formattedLog);

            ShiftHandoverConfig.Load();

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll();

            _formattedLog.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " loaded.");
        }

        private void Update()
        {
            PreShiftLockerInteraction.UpdatePostUseReservations();
        }

        private void OnDestroy()
        {
            PreShiftCommonAreaController.Shutdown();
            OutgoingHandover.Shutdown();
            PreShiftCoordinator.Shutdown();
            PreShiftCoordinatorStaleChairGuardPatch.Shutdown();
            PreShiftArrival.Shutdown();

            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }

            if (_formattedLog != null)
            {
                BepInEx.Logging.Logger.Sources.Remove(_formattedLog);
                _formattedLog = null;
                Log = null;
            }
        }
    }
}
