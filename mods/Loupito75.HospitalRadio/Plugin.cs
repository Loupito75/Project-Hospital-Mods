using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace HospitalRadio
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "loupito75.HospitalRadio";
        public const string PluginName = "Hospital Radio";
        public const string PluginVersion = "1.0.0";
        public const string PluginAuthor = "Loupito75";
        public const string HarmonyId = "Loupito75:HospitalRadio";

        internal static ManualLogSource Log { get; private set; }

        private ManualLogSource _formattedLog;
        private GameObject _controllerObject;

        private void Awake()
        {
            _formattedLog = BepInEx.Logging.Logger.CreateLogSource("   " + HarmonyId);
            Log = _formattedLog;

            string pluginDirectory = Path.GetDirectoryName(Info.Location);
            HospitalRadioConfig.Load(pluginDirectory);
            HospitalRadioController.Configure(pluginDirectory);

            _controllerObject = new GameObject("Loupito75.HospitalRadio.Controller");
            DontDestroyOnLoad(_controllerObject);
            _controllerObject.AddComponent<HospitalRadioController>();

            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " loaded.");
        }

        private void OnDestroy()
        {
            if (_controllerObject != null)
            {
                Destroy(_controllerObject);
                _controllerObject = null;
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
