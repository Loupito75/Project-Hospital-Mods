using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HospitalPorters
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "loupito75.HospitalPorters";
        public const string PluginName = "Hospital Porters";
        public const string PluginVersion = "1.0.0";
        public const string PluginAuthor = "Loupito75";
        public const string HarmonyId = "Loupito75:HospitalPorters";

        internal static ManualLogSource Log { get; private set; }

        private static Harmony s_harmony;

        private Harmony _harmony;
        private ManualLogSource _formattedLog;

        private void Awake()
        {
            _formattedLog = BepInEx.Logging.Logger.CreateLogSource("   " + HarmonyId);
            Log = _formattedLog;
            PorterTransportConfig.Load();

            _harmony = new Harmony(HarmonyId);
            s_harmony = _harmony;

            try
            {
                // HiringManager patches are deliberately NOT part of PatchAll().
                // HiringManager owns a static singleton whose constructor calls Reset();
                // touching it before Database.ReadFiles() finishes can poison the type initializer.
                _harmony.PatchAll();
            }
            catch (Exception exception)
            {
                Log.LogError("Early Harmony patching failed. Hospital Porters will stay disabled for this session: " + exception);
                return;
            }

            // Normally Database.ReadFiles() loads our database entries and then applies the
            // deferred HiringManager patches. If BepInEx starts after database loading, do it now.
            if (Database.Instance != null && Database.Instance.Loaded)
            {
                ModDatabase.Load(Database.Instance);
            }

            Log.LogInfo($"{PluginName} {PluginVersion} by {PluginAuthor} loaded.");
        }

        internal static void EnsureDeferredHiringPatches()
        {
            DeferredHiringPatches.Apply(s_harmony);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            DeferredHiringPatches.Reset();
            s_harmony = null;

            if (_formattedLog != null)
            {
                BepInEx.Logging.Logger.Sources.Remove(_formattedLog);
                _formattedLog = null;
                Log = null;
            }
        }
    }
}