using System;
using System.Reflection;
using System.Text;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal enum HroDiagnosticFeature
    {
        OptimizeHiddenFloors = 0,
        HideCharactersOnLowerFloors = 1,
        OptimizeCharactersByFloor = 2,
        OptimizeNativeHiddenCharacters = 3,
        OptimizeRoomRendererCleanup = 4,
        OptimizeHiddenDoorVisualInvalidation = 5,
        OptimizeMoodComponentLookups = 6,
        OptimizeWalkComponentLookups = 7,
        OptimizeDuplicateStatisticsRefresh = 8,
        OptimizeHospitalizationComponentLookups = 9,
        OptimizeOffShiftStaffLookups = 10,
        OptimizeIdleStaffLookups = 11,
        OptimizeOpeningClosingLightRefresh = 12,
        OptimizeOpeningClosingObjectScan = 13,
        OptimizeShiftChangeMaintenance = 14,
        OptimizeDailyHiringCandidateLookups = 15,
        OptimizeHourlyRoleValidation = 16,
        OptimizeProcedureSceneDistanceQueries = 17,
        OptimizePlayerStatisticsSaveWrites = 18
    }

    internal static class HroDiagnostics
    {
        private const string DiagnosticsHarmonyId =
            Plugin.HarmonyId + ".Diagnostics";

        private static Harmony s_harmony;
        private static int s_expectedMask;
        private static int s_observedMask;
        private static bool s_initialized;
        private static bool s_hiringProbeInstalled;

        internal static void Initialize()
        {
            Shutdown();

            if (!OptimizerConfig.EnableDiagnostics)
            {
                return;
            }

            try
            {
                BuildExpectedMask();

                s_harmony = new Harmony(
                    DiagnosticsHarmonyId);

                InstallRuntimeProbes();

                s_initialized = true;

                Plugin.Log.LogInfo(
                    "[DIAG] HRO feature validation enabled. Each enabled feature logs one HIT when its representative optimized target or scenario is observed. Missing HIT means not observed during this run, not automatically broken.");
                Plugin.Log.LogInfo(
                    "[DIAG] No timing, collection scan, pathfinding query or per-frame report is added by this probe.");
            }
            catch (Exception exception)
            {
                Shutdown();
                Plugin.Log.LogWarning(
                    "[DIAG] Validation probe could not be installed; HRO features remain active: " +
                    exception.GetType().Name + ": " +
                    exception.Message);
            }
        }

        internal static void DumpSummary()
        {
            if (!s_initialized)
            {
                return;
            }

            int expected = 0;
            int observed = 0;
            StringBuilder missing =
                new StringBuilder();

            for (int i = 0; i <=
                (int)HroDiagnosticFeature.OptimizePlayerStatisticsSaveWrites;
                i++)
            {
                int bit = 1 << i;
                if ((s_expectedMask & bit) == 0)
                {
                    continue;
                }

                expected++;

                if ((s_observedMask & bit) != 0)
                {
                    observed++;
                    continue;
                }

                if (missing.Length > 0)
                {
                    missing.Append(", ");
                }

                missing.Append(
                    GetName((HroDiagnosticFeature)i));
            }

            Plugin.Log.LogInfo(
                "[DIAG] SUMMARY HIT=" +
                observed + "/" + expected +
                (missing.Length == 0
                    ? "; all enabled feature probes were observed."
                    : "; not observed=" +
                        missing.ToString() + "."));
        }

        internal static void Shutdown()
        {
            if (s_harmony != null)
            {
                s_harmony.UnpatchSelf();
                s_harmony = null;
            }

            s_expectedMask = 0;
            s_observedMask = 0;
            s_initialized = false;
            s_hiringProbeInstalled = false;
        }

        private static void InstallRuntimeProbes()
        {
            if (OptimizerConfig.OptimizeHiddenFloors ||
                OptimizerConfig.OptimizeNativeHiddenCharacters)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(MapRenderer),
                        "RenderMap",
                        Type.EmptyTypes),
                    "RenderMapPostfix");
            }

            if (OptimizerConfig.HideCharactersOnLowerFloors ||
                OptimizerConfig.OptimizeCharactersByFloor)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(CharacterRenderer),
                        "RenderCharacters",
                        new Type[]
                        {
                            typeof(FloorRenderData),
                            typeof(Floor),
                            typeof(LightMap2DUnity)
                        }),
                    "RenderCharactersPostfix");
            }

            if (OptimizerConfig.OptimizeRoomRendererCleanup)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(RoomRenderer),
                        "RenderRooms",
                        new Type[]
                        {
                            typeof(Floor),
                            typeof(FloorRenderData),
                            typeof(LightMap2DUnity),
                            typeof(float)
                        }),
                    "RoomRendererPostfix");
            }

            if (OptimizerConfig.OptimizeHiddenDoorVisualInvalidation)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(Door),
                        "Update",
                        new Type[]
                        {
                            typeof(TileWalls),
                            typeof(int),
                            typeof(float)
                        }),
                    "DoorPostfix");
            }

            if (OptimizerConfig.OptimizeMoodComponentLookups)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(MoodComponent),
                        "Update",
                        new Type[] { typeof(float) }),
                    "MoodPostfix");
            }

            if (OptimizerConfig.OptimizeWalkComponentLookups)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(WalkComponent),
                        "Update",
                        new Type[] { typeof(float) }),
                    "WalkPostfix");
            }

            if (OptimizerConfig.OptimizeDuplicateStatisticsRefresh)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(HospitalizationComponent),
                        "CheckSurvivedCollapseCount",
                        Type.EmptyTypes),
                    "DuplicateStatisticsPostfix");
                PatchPostfix(
                    RequireMethod(
                        typeof(HospitalizationComponent),
                        "SendHome",
                        Type.EmptyTypes),
                    "DuplicateStatisticsPostfix");
            }

            if (OptimizerConfig.OptimizeHospitalizationComponentLookups)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(HospitalizationComponent),
                        "Update",
                        new Type[] { typeof(float) }),
                    "HospitalizationPostfix");
            }

            if (OptimizerConfig.OptimizeOffShiftStaffLookups)
            {
                PatchOffShiftType(
                    typeof(BehaviorDoctor));
                PatchOffShiftType(
                    typeof(BehaviorNurse));
                PatchOffShiftType(
                    typeof(BehaviorLabSpecialist));
            }

            if (OptimizerConfig.OptimizeIdleStaffLookups)
            {
                PatchIdleType(
                    typeof(BehaviorDoctor));
                PatchIdleType(
                    typeof(BehaviorNurse));
                PatchIdleType(
                    typeof(BehaviorLabSpecialist));
            }

            if (OptimizerConfig.OptimizeOpeningClosingLightRefresh ||
                OptimizerConfig.OptimizeOpeningClosingObjectScan)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(Floor),
                        "OnOpenForPatients",
                        Type.EmptyTypes),
                    "OpeningClosingPostfix");
                PatchPostfix(
                    RequireMethod(
                        typeof(Floor),
                        "OnClosedForDayShift",
                        Type.EmptyTypes),
                    "OpeningClosingPostfix");
            }

            if (OptimizerConfig.OptimizeShiftChangeMaintenance)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(Hospital),
                        "ReportRoomUsage",
                        new Type[] { typeof(Shift) }),
                    "ShiftPostfix");
            }

            if (OptimizerConfig.OptimizeDailyHiringCandidateLookups)
            {
                Database database =
                    Database.Instance;

                if (database != null &&
                    database.Loaded)
                {
                    InstallHiringProbe();
                }
                else
                {
                    PatchPostfix(
                        RequireMethod(
                            typeof(Database),
                            "ReadFiles",
                            new Type[]
                            {
                                typeof(string),
                                typeof(bool)
                            }),
                        "DatabaseReadFilesPostfix");
                }
            }

            if (OptimizerConfig.OptimizeHourlyRoleValidation)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(MapEditorController),
                        "ValidateRoles",
                        Type.EmptyTypes),
                    "RoleValidationPostfix");
            }

            if (OptimizerConfig.OptimizeProcedureSceneDistanceQueries)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(ProcedureComponent),
                        "UpdateAllExaminationsForMedicalCondition",
                        new Type[]
                        {
                            typeof(MedicalCondition),
                            typeof(int),
                            typeof(bool)
                        }),
                    "ProcedureDistancePostfix");
            }

            if (OptimizerConfig.OptimizePlayerStatisticsSaveWrites)
            {
                PatchPostfix(
                    RequireMethod(
                        typeof(PlayerStatistics),
                        "IncrementStatistic",
                        new Type[]
                        {
                            typeof(string),
                            typeof(int)
                        }),
                    "PlayerStatisticsPostfix");
            }
        }

        private static void PatchOffShiftType(
            Type behaviorType)
        {
            PatchPostfix(
                RequireMethod(
                    behaviorType,
                    "UpdateStateAtHome",
                    Type.EmptyTypes),
                "OffShiftPostfix");
            PatchPostfix(
                RequireMethod(
                    behaviorType,
                    "UpdateStateCommuting",
                    Type.EmptyTypes),
                "OffShiftPostfix");
        }

        private static void PatchIdleType(
            Type behaviorType)
        {
            PatchPostfix(
                RequireMethod(
                    behaviorType,
                    "UpdateStateIdle",
                    new Type[] { typeof(float) }),
                "IdlePostfix");
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters)
        {
            MethodInfo method =
                AccessTools.Method(
                    type,
                    name,
                    parameters);

            if (method == null)
            {
                throw new MissingMethodException(
                    type.FullName,
                    name);
            }

            return method;
        }

        private static void PatchPostfix(
            MethodInfo target,
            string postfixName)
        {
            MethodInfo postfix =
                AccessTools.Method(
                    typeof(HroDiagnostics),
                    postfixName);

            if (postfix == null)
            {
                throw new MissingMethodException(
                    typeof(HroDiagnostics).FullName,
                    postfixName);
            }

            HarmonyMethod harmonyPostfix =
                new HarmonyMethod(postfix);
            harmonyPostfix.after =
                new string[] { Plugin.HarmonyId };

            s_harmony.Patch(
                target,
                postfix: harmonyPostfix);
        }

        private static void BuildExpectedMask()
        {
            AddExpected(
                HroDiagnosticFeature.OptimizeHiddenFloors,
                OptimizerConfig.OptimizeHiddenFloors);
            AddExpected(
                HroDiagnosticFeature.HideCharactersOnLowerFloors,
                OptimizerConfig.HideCharactersOnLowerFloors);
            AddExpected(
                HroDiagnosticFeature.OptimizeCharactersByFloor,
                OptimizerConfig.OptimizeCharactersByFloor);
            AddExpected(
                HroDiagnosticFeature.OptimizeNativeHiddenCharacters,
                OptimizerConfig.OptimizeNativeHiddenCharacters);
            AddExpected(
                HroDiagnosticFeature.OptimizeRoomRendererCleanup,
                OptimizerConfig.OptimizeRoomRendererCleanup);
            AddExpected(
                HroDiagnosticFeature.OptimizeHiddenDoorVisualInvalidation,
                OptimizerConfig.OptimizeHiddenDoorVisualInvalidation);
            AddExpected(
                HroDiagnosticFeature.OptimizeMoodComponentLookups,
                OptimizerConfig.OptimizeMoodComponentLookups);
            AddExpected(
                HroDiagnosticFeature.OptimizeWalkComponentLookups,
                OptimizerConfig.OptimizeWalkComponentLookups);
            AddExpected(
                HroDiagnosticFeature.OptimizeDuplicateStatisticsRefresh,
                OptimizerConfig.OptimizeDuplicateStatisticsRefresh);
            AddExpected(
                HroDiagnosticFeature.OptimizeHospitalizationComponentLookups,
                OptimizerConfig.OptimizeHospitalizationComponentLookups);
            AddExpected(
                HroDiagnosticFeature.OptimizeOffShiftStaffLookups,
                OptimizerConfig.OptimizeOffShiftStaffLookups);
            AddExpected(
                HroDiagnosticFeature.OptimizeIdleStaffLookups,
                OptimizerConfig.OptimizeIdleStaffLookups);
            AddExpected(
                HroDiagnosticFeature.OptimizeOpeningClosingLightRefresh,
                OptimizerConfig.OptimizeOpeningClosingLightRefresh);
            AddExpected(
                HroDiagnosticFeature.OptimizeOpeningClosingObjectScan,
                OptimizerConfig.OptimizeOpeningClosingObjectScan);
            AddExpected(
                HroDiagnosticFeature.OptimizeShiftChangeMaintenance,
                OptimizerConfig.OptimizeShiftChangeMaintenance);
            AddExpected(
                HroDiagnosticFeature.OptimizeDailyHiringCandidateLookups,
                OptimizerConfig.OptimizeDailyHiringCandidateLookups);
            AddExpected(
                HroDiagnosticFeature.OptimizeHourlyRoleValidation,
                OptimizerConfig.OptimizeHourlyRoleValidation);
            AddExpected(
                HroDiagnosticFeature.OptimizeProcedureSceneDistanceQueries,
                OptimizerConfig.OptimizeProcedureSceneDistanceQueries);
            AddExpected(
                HroDiagnosticFeature.OptimizePlayerStatisticsSaveWrites,
                OptimizerConfig.OptimizePlayerStatisticsSaveWrites);
        }

        private static void AddExpected(
            HroDiagnosticFeature feature,
            bool enabled)
        {
            if (enabled)
            {
                s_expectedMask |=
                    1 << (int)feature;
            }
        }

        private static void Mark(
            HroDiagnosticFeature feature)
        {
            if (!s_initialized)
            {
                return;
            }

            int bit =
                1 << (int)feature;

            if ((s_expectedMask & bit) == 0 ||
                (s_observedMask & bit) != 0)
            {
                return;
            }

            s_observedMask |= bit;

            Plugin.Log.LogInfo(
                "[DIAG] HIT " +
                GetName(feature));
        }

        private static void RenderMapPostfix(
            MapRenderer __instance,
            bool __runOriginal)
        {
            if (__instance == null ||
                __instance.m_floorRenderData == null)
            {
                return;
            }

            if (OptimizerConfig.OptimizeHiddenFloors &&
                !__runOriginal &&
                __instance.m_floorRenderData.m_floorRenderMode ==
                    FloorRenderMode.ABOVE_HIDDEN)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeHiddenFloors);
            }

            if (OptimizerConfig.OptimizeNativeHiddenCharacters &&
                __runOriginal &&
                SettingsManager.Instance != null &&
                SettingsManager.Instance.m_viewSettings != null &&
                SettingsManager.Instance.m_viewSettings.m_hideCharacters.m_value)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeNativeHiddenCharacters);
            }
        }

        private static void RenderCharactersPostfix(
            CharacterRenderer __instance,
            FloorRenderData __0,
            bool __runOriginal)
        {
            if (OptimizerConfig.OptimizeCharactersByFloor &&
                __runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeCharactersByFloor);
            }

            if (!OptimizerConfig.HideCharactersOnLowerFloors ||
                __instance == null ||
                __0 == null)
            {
                return;
            }

            if (!__runOriginal &&
                __0.m_floorRenderMode ==
                    FloorRenderMode.BELOW)
            {
                Mark(
                    HroDiagnosticFeature.HideCharactersOnLowerFloors);
            }
        }

        private static void RoomRendererPostfix(
            bool __runOriginal)
        {
            if (__runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeRoomRendererCleanup);
            }
        }

        private static void DoorPostfix(
            Door __instance)
        {
            if (__instance == null ||
                __instance.m_state == null ||
                Hospital.Instance == null)
            {
                return;
            }

            if (__instance.m_state.m_floorIndex !=
                Hospital.Instance.m_currentFloorIndex)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeHiddenDoorVisualInvalidation);
            }
        }

        private static void MoodPostfix(
            bool __runOriginal)
        {
            if (__runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeMoodComponentLookups);
            }
        }

        private static void WalkPostfix(
            bool __runOriginal)
        {
            if (__runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeWalkComponentLookups);
            }
        }

        private static void DuplicateStatisticsPostfix(
            bool __runOriginal)
        {
            if (__runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeDuplicateStatisticsRefresh);
            }
        }

        private static void HospitalizationPostfix(
            bool __runOriginal)
        {
            if (__runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeHospitalizationComponentLookups);
            }
        }

        private static void OffShiftPostfix(
            bool __runOriginal)
        {
            if (__runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeOffShiftStaffLookups);
            }
        }

        private static void IdlePostfix(
            bool __runOriginal)
        {
            if (__runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeIdleStaffLookups);
            }
        }

        private static void OpeningClosingPostfix()
        {
            Mark(
                HroDiagnosticFeature.OptimizeOpeningClosingLightRefresh);
            Mark(
                HroDiagnosticFeature.OptimizeOpeningClosingObjectScan);
        }

        private static void ShiftPostfix()
        {
            Mark(
                HroDiagnosticFeature.OptimizeShiftChangeMaintenance);
        }

        private static void DatabaseReadFilesPostfix(
            Database __instance,
            bool add)
        {
            if (!add &&
                __instance != null &&
                __instance.Loaded)
            {
                InstallHiringProbe();
            }
        }

        private static void InstallHiringProbe()
        {
            if (s_harmony == null ||
                s_hiringProbeInstalled)
            {
                return;
            }

            HiringManager manager =
                HiringManager.Instance;

            if (manager == null)
            {
                return;
            }

            PatchPostfix(
                RequireMethod(
                    typeof(HiringManager),
                    "GenerateAvailableCharacters",
                    new Type[]
                    {
                        typeof(AvailableCharacters),
                        typeof(GameDBDepartment),
                        typeof(LopitalTypes),
                        typeof(bool)
                    }),
                "HiringPostfix");

            s_hiringProbeInstalled = true;
        }

        private static void HiringPostfix(
            bool __runOriginal)
        {
            if (__runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeDailyHiringCandidateLookups);
            }
        }

        private static void RoleValidationPostfix(
            bool __runOriginal)
        {
            if (__runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeHourlyRoleValidation);
            }
        }

        private static void ProcedureDistancePostfix(
            bool __runOriginal)
        {
            if (__runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizeProcedureSceneDistanceQueries);
            }
        }

        private static void PlayerStatisticsPostfix(
            bool __runOriginal)
        {
            if (__runOriginal)
            {
                Mark(
                    HroDiagnosticFeature.OptimizePlayerStatisticsSaveWrites);
            }
        }

        private static string GetName(
            HroDiagnosticFeature feature)
        {
            return feature.ToString();
        }
    }
}
