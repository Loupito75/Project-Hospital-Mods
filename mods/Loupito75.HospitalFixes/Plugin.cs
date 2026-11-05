using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HospitalFixes
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(
        "loupito75.HospitalShiftHandover",
        BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "loupito75.HospitalFixes";
        public const string PluginName = "Hospital Fixes";
        public const string PluginVersion = "1.0.0";
        public const string PluginWhatsNew =
            "29 targeted Vanilla fixes, workflow corrections, interface and construction fixes, visual improvements";

        public const string HarmonyId = "Loupito75:HospitalFixes";

        internal static ManualLogSource Log { get; private set; }
        private ManualLogSource _formattedLog;
        private Harmony _harmony;

        private void Awake()
        {
            _formattedLog = BepInEx.Logging.Logger.CreateLogSource("   " + HarmonyId);
            Log = _formattedLog;
            FixConfig.Load();
            HfxDiagnostics.StartSession();

            _harmony = new Harmony(HarmonyId);
            try
            {
                _harmony.PatchAll();
                Patches.HshStaffClothingChangeIntegration.Install(
                    _harmony);

                // Normally HfxDatabaseReadFilesPatch loads the local Database
                // after Project Hospital completes its base Database.ReadFiles().
                // Handle late BepInEx startup as well.
                if (Database.Instance != null &&
                    Database.Instance.Loaded)
                {
                    HfxDatabase.Load(Database.Instance);
                }

                Log.LogInfo(PluginName + " " + PluginVersion + " by Loupito75 loaded.");
            }
            catch (Exception exception)
            {
                _harmony.UnpatchSelf();
                Log.LogError("Harmony patches failed; Hospital Fixes disabled: " + exception);
            }
        }

        private void OnDestroy()
        {
            HfxDiagnostics.DumpSummary();

            Patches.HshStaffClothingChangeIntegration
                .ClearTransientState();

            Patches.ReservedStaffWaitingVisuals
                .ClearTransientState();

            Patches.ClothingChangeEffectRenderer.ClearAll(
                destroyAssets: true);

            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
            HfxDiagnostics.Reset();

            if (_formattedLog != null)
            {
                BepInEx.Logging.Logger.Sources.Remove(_formattedLog);
                _formattedLog = null;
                Log = null;
            }
        }
    }
}
