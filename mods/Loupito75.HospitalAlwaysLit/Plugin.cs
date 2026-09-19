using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HospitalAlwaysLit
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "loupito75.HospitalAlwaysLit";
        public const string PluginName = "Hospital Always Lit";
        public const string PluginVersion = "1.2.0";
        public const string PluginAuthor = "Loupito75";
        public const string HarmonyId = "Loupito75:HospitalAlwaysLit";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private ManualLogSource _formattedLog;

        private void Awake()
        {
            _formattedLog = BepInEx.Logging.Logger.CreateLogSource("   " + HarmonyId);
            Log = _formattedLog;

            AlwaysLitConfig.Load();

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll();

            Log.LogInfo($"{PluginName} {PluginVersion} by {PluginAuthor} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();

            if (_formattedLog != null)
            {
                BepInEx.Logging.Logger.Sources.Remove(_formattedLog);
                _formattedLog = null;
                Log = null;
            }
        }
    }
}
