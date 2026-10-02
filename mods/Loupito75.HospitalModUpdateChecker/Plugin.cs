using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HospitalModUpdateChecker
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid =
            "loupito75.HospitalModUpdateChecker";
        public const string PluginName =
            "Hospital Mod Update Checker";
        public const string PluginVersion = "1.1.0";
        public const string PluginAuthor = "Loupito75";
        public const string HarmonyId =
            "Loupito75:HospitalModUpdateChecker";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private ManualLogSource _formattedLog;

        private void Awake()
        {
            _formattedLog =
                BepInEx.Logging.Logger.CreateLogSource(
                    "   " + HarmonyId);

            Log = _formattedLog;

            PluginLogStatusTracker.Start();
            UpdateCheckerConfig.Load();

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll();

            Log.LogInfo(
                PluginName +
                " " +
                PluginVersion +
                " by " +
                PluginAuthor +
                " loaded.");
        }

        private void Start()
        {
            StartCoroutine(UpdateChecker.CheckForUpdates());
        }

        private void Update()
        {
            if (PluginLogStatusTracker.ConsumeChanged())
            {
                TitleScreenUpdatePanel.Refresh();
            }
        }

        private void OnDestroy()
        {
            PluginLogStatusTracker.Stop();

            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }

            if (_formattedLog != null)
            {
                BepInEx.Logging.Logger.Sources.Remove(
                    _formattedLog);

                _formattedLog = null;
                Log = null;
            }
        }
    }
}
