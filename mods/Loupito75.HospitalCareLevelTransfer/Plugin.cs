using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HospitalCareLevelTransfer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "loupito75.HospitalCareLevelTransfer";
        public const string HarmonyId = "Loupito75:HospitalCareLevelTransfer";
        public const string PluginName = "Hospital Care Level Transfer";
        public const string PluginAuthor = "Loupito75";
        public const string PluginVersion = "1.1.0";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private ManualLogSource _formattedLog;

        private void Awake()
        {
            _formattedLog = BepInEx.Logging.Logger.CreateLogSource("   " + HarmonyId);
            Log = _formattedLog;

            CareLevelTransferConfig.Load();

            _harmony = new Harmony(HarmonyId);

            try
            {
                _harmony.PatchAll();
            }
            catch (Exception exception)
            {
                _harmony.UnpatchSelf();
                Log.LogError(
                    "Hospital Care Level Transfer could not apply its Harmony patches and was disabled: " +
                    exception.GetType().Name + ": " + exception.Message);
                return;
            }

            Log.LogInfo(
                PluginName + " " + PluginVersion + " by " + PluginAuthor + " loaded.");
        }

        private void OnDestroy()
        {
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
}
