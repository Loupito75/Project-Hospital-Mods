using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HospitalTrafficControl
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "loupito75.HospitalTrafficControl";
        public const string HarmonyId = "Loupito75:HospitalTrafficControl";
        public const string PluginName = "Hospital Traffic Control";
        public const string PluginAuthor = "Loupito75";
        public const string PluginVersion = "1.2.0";

        internal static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private ManualLogSource _formattedLog;
        private bool _indicatorErrorLogged;
        private bool _pathDebugMarkerErrorLogged;

        private void Awake()
        {
            _formattedLog = BepInEx.Logging.Logger.CreateLogSource("   " + HarmonyId);
            Log = _formattedLog;

            TrafficControlConfig.Load();

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll();

            Log.LogInfo($"{PluginName} {PluginVersion} by {PluginAuthor} loaded.");
            if (TrafficControlConfig.PathfindingDebug)
            {
                Log.LogWarning("[PathDebug] Pathfinding diagnostics are ENABLED.");
            }
            if (TrafficControlConfig.DoorDebug)
            {
                Log.LogWarning("[DoorDebug] Door and OneWay arrow diagnostics are ENABLED.");
            }
            if (TrafficControlConfig.BathroomFlowDebug)
            {
                Log.LogWarning("[BathroomDebug] Bathroom flow diagnostics are ENABLED.");
            }
        }

        private void Update()
        {
            try
            {
                OneWayIndicatorRenderer.Update();
            }
            catch (Exception exception)
            {
                if (!_indicatorErrorLogged)
                {
                    _indicatorErrorLogged = true;
                    Exception root = exception.InnerException ?? exception;
                    Log?.LogError(
                        "HTC one-way indicator update failed: " +
                        $"{root.GetType().FullName}: {root.Message}");
                }
            }

            if (TrafficControlConfig.PathfindingDebug)
            {
                try
                {
                    PathfindingDebugMarkerRenderer.Update();
                }
                catch (Exception exception)
                {
                    if (!_pathDebugMarkerErrorLogged)
                    {
                        _pathDebugMarkerErrorLogged = true;
                        Exception root = exception.InnerException ?? exception;
                        Log?.LogError(
                            "HTC pathfinding debug marker update failed: " +
                            $"{root.GetType().FullName}: {root.Message}");
                    }
                }
            }
        }

        private void OnDestroy()
        {
            OneWayIndicatorRenderer.Reset();
            PathfindingDebugMarkerRenderer.Reset();
            AccessZoneRecoveryManager.Reset();
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
