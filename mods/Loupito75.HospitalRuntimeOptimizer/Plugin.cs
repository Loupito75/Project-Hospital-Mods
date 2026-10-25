using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HospitalRuntimeOptimizer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "loupito75.HospitalRuntimeOptimizer";
        public const string PluginName = "Hospital Runtime Optimizer";
        public const string PluginVersion = "1.0.0";
        public const string PluginWhatsNew = "Faster hidden-floor rendering, lighter character and room updates, lower staff overhead, optimized shift/opening/closing work, fewer distance recalculations, fewer statistics writes";
        public const string HarmonyId = "Loupito75:HospitalRuntimeOptimizer";

        internal static HroLog Log { get; private set; }

        private ManualLogSource _formattedLog;
        private Harmony _harmony;

        private void Awake()
        {
            _formattedLog = BepInEx.Logging.Logger.CreateLogSource("   " + HarmonyId);
            Log = new HroLog(_formattedLog);

            OptimizerConfig.Load();

            if (!OptimizerConfig.Enabled)
            {
                _formattedLog.LogInfo(
                    PluginName + " " + PluginVersion +
                    " by Loupito75 loaded.");
                Log.LogInfo(
                    "Master switch OFF; no HRO feature patches installed.");
                return;
            }

            _harmony = new Harmony(HarmonyId);

            try
            {
                int featureCount = 0;

                if (OptimizerConfig.OptimizeHiddenFloors)
                {
                    HiddenFloorRenderPatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.HideCharactersOnLowerFloors)
                {
                    LowerFloorCharacterCullingPatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeCharactersByFloor)
                {
                    CharacterFloorListReusePatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeNativeHiddenCharacters)
                {
                    NativeHiddenCharacterRenderPatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeRoomRendererCleanup)
                {
                    RoomRendererCleanupPatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeHiddenDoorVisualInvalidation)
                {
                    HiddenDoorVisualInvalidationPatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeMoodComponentLookups)
                {
                    MoodComponentLookupReusePatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeWalkComponentLookups)
                {
                    WalkComponentLookupReusePatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeDuplicateStatisticsRefresh)
                {
                    DuplicateStatisticsRefreshPatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeHospitalizationComponentLookups)
                {
                    HospitalizationComponentLookupReusePatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeOffShiftStaffLookups)
                {
                    OffShiftStaffLookupReusePatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeIdleStaffLookups)
                {
                    IdleStaffLookupReusePatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeOpeningClosingLightRefresh)
                {
                    OpeningClosingLightRefreshPatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeOpeningClosingObjectScan)
                {
                    OpeningClosingObjectScanPatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeShiftChangeMaintenance)
                {
                    ShiftChangeMaintenancePatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeDailyHiringCandidateLookups)
                {
                    DailyHiringCandidateLookupReusePatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeHourlyRoleValidation)
                {
                    HourlyRoleValidationPatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizeProcedureSceneDistanceQueries)
                {
                    ProcedureSceneDistanceQueryReusePatch.Install(_harmony);
                    featureCount++;
                }

                if (OptimizerConfig.OptimizePlayerStatisticsSaveWrites)
                {
                    PlayerStatisticsSaveWritePatch.Install(_harmony);
                    featureCount++;
                }

                HroDiagnostics.Initialize();

                _formattedLog.LogInfo(
                    PluginName + " " + PluginVersion +
                    " by Loupito75 loaded.");

                Log.LogInfo("Active features=" +
                    featureCount + ", OptimizeHiddenFloors=" + OptimizerConfig.OptimizeHiddenFloors +
                    ", HideCharactersOnLowerFloors=" + OptimizerConfig.HideCharactersOnLowerFloors +
                    ", OptimizeCharactersByFloor=" + OptimizerConfig.OptimizeCharactersByFloor +
                    ", OptimizeNativeHiddenCharacters=" + OptimizerConfig.OptimizeNativeHiddenCharacters +
                    ", OptimizeRoomRendererCleanup=" + OptimizerConfig.OptimizeRoomRendererCleanup +
                    ", OptimizeHiddenDoorVisualInvalidation=" + OptimizerConfig.OptimizeHiddenDoorVisualInvalidation +
                    ", OptimizeMoodComponentLookups=" + OptimizerConfig.OptimizeMoodComponentLookups +
                    ", OptimizeWalkComponentLookups=" + OptimizerConfig.OptimizeWalkComponentLookups +
                    ", OptimizeDuplicateStatisticsRefresh=" + OptimizerConfig.OptimizeDuplicateStatisticsRefresh +
                    ", OptimizeHospitalizationComponentLookups=" + OptimizerConfig.OptimizeHospitalizationComponentLookups +
                    ", OptimizeOffShiftStaffLookups=" + OptimizerConfig.OptimizeOffShiftStaffLookups +
                    ", OptimizeIdleStaffLookups=" + OptimizerConfig.OptimizeIdleStaffLookups +
                    ", OptimizeOpeningClosingLightRefresh=" + OptimizerConfig.OptimizeOpeningClosingLightRefresh +
                    ", OptimizeOpeningClosingObjectScan=" + OptimizerConfig.OptimizeOpeningClosingObjectScan +
                    ", OptimizeShiftChangeMaintenance=" + OptimizerConfig.OptimizeShiftChangeMaintenance +
                    ", OptimizeDailyHiringCandidateLookups=" + OptimizerConfig.OptimizeDailyHiringCandidateLookups +
                    ", OptimizeHourlyRoleValidation=" + OptimizerConfig.OptimizeHourlyRoleValidation +
                    ", OptimizeProcedureSceneDistanceQueries=" + OptimizerConfig.OptimizeProcedureSceneDistanceQueries +
                    ", OptimizePlayerStatisticsSaveWrites=" + OptimizerConfig.OptimizePlayerStatisticsSaveWrites + ".");
            }
            catch (Exception exception)
            {
                HroDiagnostics.Shutdown();
                HiddenFloorRenderPatch.Reset();
                LowerFloorCharacterCullingPatch.Reset();
                RoomRendererCleanupPatch.Reset();
                OpeningClosingLightRefreshPatch.Reset();
                DailyHiringCandidateLookupReusePatch.Reset();
                HourlyRoleValidationPatch.Reset();
                ProcedureSceneDistanceQueryReusePatch.Reset();
                PlayerStatisticsSaveWritePatch.Reset();

                if (_harmony != null)
                {
                    _harmony.UnpatchSelf();
                    _harmony = null;
                }

                Log.LogError("Harmony patch installation failed; HRO disabled for this session: " + exception);
            }
        }

        private void OnDestroy()
        {
            HroDiagnostics.DumpSummary();
            HroDiagnostics.Shutdown();

            HiddenFloorRenderPatch.Reset();
            LowerFloorCharacterCullingPatch.Reset();
            RoomRendererCleanupPatch.Reset();
            OpeningClosingLightRefreshPatch.Reset();
            DailyHiringCandidateLookupReusePatch.Reset();
            HourlyRoleValidationPatch.Reset();
            ProcedureSceneDistanceQueryReusePatch.Reset();
            PlayerStatisticsSaveWritePatch.Reset();

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

    internal sealed class HroLog
    {
        private readonly ManualLogSource _source;

        internal HroLog(ManualLogSource source)
        {
            _source = source;
        }

        internal void LogInfo(object data)
        {
            if (OptimizerConfig.EnableDiagnostics)
            {
                _source.LogInfo(data);
            }
        }

        internal void LogWarning(object data)
        {
            _source.LogWarning(data);
        }

        internal void LogError(object data)
        {
            _source.LogError(data);
        }
    }
}
