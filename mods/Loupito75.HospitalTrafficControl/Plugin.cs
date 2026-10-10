using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HospitalTrafficControl.Patches;
using Lopital;

namespace HospitalTrafficControl
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "loupito75.HospitalTrafficControl";
        public const string HarmonyId = "Loupito75:HospitalTrafficControl";
        public const string PluginName = "Hospital Traffic Control";
        public const string PluginAuthor = "Loupito75";
        public const string PluginVersion = "1.5.0";

        internal static ManualLogSource Log { get; private set; }

        private static Harmony s_harmony;

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
            s_harmony = _harmony;
            _harmony.PatchAll();

            Log.LogInfo($"{PluginName} {PluginVersion} by {PluginAuthor} loaded.");

            bool debugEnabled =
                TrafficControlConfig.PathfindingDebug ||
                TrafficControlConfig.JanitorCartDebug ||
                TrafficControlConfig.GraphPerformanceDebug ||
                TrafficControlConfig.DoorDebug ||
                TrafficControlConfig.BathroomFlowDebug;

            if (debugEnabled)
            {
                Log.LogInfo("Navigation graph mode: EXACT (six access levels).");
                Log.LogInfo(
                    "Janitor cleaning settings: ActiveProcedureRooms=" +
                    TrafficControlConfig.AvoidCleaningActiveProcedureRooms +
                    ", OccupiedBathrooms=" +
                    TrafficControlConfig.AvoidCleaningOccupiedBathrooms +
                    ", ProtectedRoomWaitChance=" +
                    TrafficControlConfig.JanitorOccupiedRoomWaitChance +
                    ", ProtectedRoomWaitCooldownMinutes=" +
                    TrafficControlConfig.JanitorOccupiedRoomWaitCooldownMinutes +
                    ", ProtectedRoomWaitMinutes=" +
                    TrafficControlConfig.JanitorOccupiedRoomWaitMinutes +
                    ", ProtectedRoomWaitRandomnessMinutes=" +
                    TrafficControlConfig.JanitorOccupiedRoomWaitRandomnessMinutes +
                    ", ReduceOccupiedHospitalizationAtNight=" +
                    TrafficControlConfig.ReduceOccupiedHospitalizationCleaningAtNight +
                    ".");
            }
            if (TrafficControlConfig.PathfindingDebug)
            {
                Log.LogInfo("[PathDebug] Pathfinding diagnostics are ENABLED.");
            }
            if (TrafficControlConfig.JanitorCartDebug)
            {
                Log.LogInfo("[JanitorDebug] Focused janitor/cart diagnostics are ENABLED.");
            }
            if (TrafficControlConfig.GraphPerformanceDebug)
            {
                Log.LogInfo("[GraphPerf] Navigation profiling enabled (20s summaries).");
            }
            if (TrafficControlConfig.DoorDebug)
            {
                Log.LogInfo("[DoorDebug] Door and OneWay arrow diagnostics are ENABLED.");
            }
            if (TrafficControlConfig.BathroomFlowDebug)
            {
                Log.LogInfo("[BathroomDebug] Bathroom flow diagnostics are ENABLED.");
            }
        }

        internal static void EnsureDeferredGridMapPatches(Floor initializedFloor)
        {
            DeferredGridMapPatches.Apply(s_harmony, initializedFloor);
        }

        private void Update()
        {
            GraphPerformanceDiagnostics.UpdateFrame();
            AccessZoneRecoveryManager.UpdatePendingPhysicalExits();

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
            GraphPerformanceDiagnostics.Reset();
            RuntimeStateManager.Reset();
            _harmony?.UnpatchSelf();
            DeferredGridMapPatches.Reset();
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
