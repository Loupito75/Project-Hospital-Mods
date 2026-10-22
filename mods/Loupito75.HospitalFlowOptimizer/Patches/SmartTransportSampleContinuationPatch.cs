using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFlowOptimizer.Patches
{
    internal static class HospitalPortersSampleContinuationCompatibility
    {
        private const string ApiTypeName =
            "HospitalPorters.HospitalPortersApi";
        private const string HpoPluginGuid =
            "loupito75.HospitalPorters";

        private delegate bool PorterBoolDelegate(Entity porter);
        private delegate LabProcedure[] PorterCandidatesDelegate(Entity porter);
        private delegate bool PorterProcedureBoolDelegate(
            Entity porter,
            LabProcedure procedure);

        private static Type ApiType;
        private static MethodInfo GetSampleContinuationApiVersionMethod;
        private static MethodInfo LegacyGetApiVersionMethod;
        private static MethodInfo IsWindowOpenMethod;
        private static MethodInfo GetCandidatesMethod;
        private static MethodInfo TryStartMethod;
        private static MethodInfo FinishWindowMethod;
        private static PorterBoolDelegate IsWindowOpenCallback;
        private static PorterCandidatesDelegate GetCandidatesCallback;
        private static PorterProcedureBoolDelegate TryStartCallback;
        private static PorterBoolDelegate FinishWindowCallback;
        private static int SampleApiVersion;
        private static int ResolvedAssemblyCount = -1;

        internal static int GetApiVersion()
        {
            return Resolve()
                ? SampleApiVersion
                : 0;
        }

        internal static bool IsWindowOpen(Entity porter)
        {
            if (porter == null || !Resolve())
            {
                return false;
            }

            try
            {
                if (IsWindowOpenCallback != null)
                {
                    return IsWindowOpenCallback(porter);
                }

                object result =
                    IsWindowOpenMethod.Invoke(
                        null,
                        new object[] { porter });
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        internal static LabProcedure[] GetCandidates(
            Entity porter)
        {
            if (porter == null || !Resolve())
            {
                return new LabProcedure[0];
            }

            try
            {
                LabProcedure[] result =
                    GetCandidatesCallback != null
                        ? GetCandidatesCallback(porter)
                        : GetCandidatesMethod.Invoke(
                            null,
                            new object[] { porter }) as LabProcedure[];
                return result ?? new LabProcedure[0];
            }
            catch
            {
                return new LabProcedure[0];
            }
        }

        internal static bool TryStart(
            Entity porter,
            LabProcedure procedure)
        {
            if (porter == null ||
                procedure == null ||
                !Resolve())
            {
                return false;
            }

            try
            {
                if (TryStartCallback != null)
                {
                    return TryStartCallback(porter, procedure);
                }

                object result =
                    TryStartMethod.Invoke(
                        null,
                        new object[] { porter, procedure });
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        internal static bool FinishWindow(
            Entity porter)
        {
            if (porter == null ||
                !Resolve() ||
                SampleApiVersion < 2 ||
                FinishWindowMethod == null)
            {
                return false;
            }

            try
            {
                if (FinishWindowCallback != null)
                {
                    return FinishWindowCallback(porter);
                }

                object result =
                    FinishWindowMethod.Invoke(
                        null,
                        new object[] { porter });
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private static bool Resolve()
        {
            if (!Chainloader.PluginInfos.ContainsKey(HpoPluginGuid))
            {
                return false;
            }

            if (ApiType != null &&
                (GetSampleContinuationApiVersionMethod != null ||
                 LegacyGetApiVersionMethod != null) &&
                IsWindowOpenMethod != null &&
                GetCandidatesMethod != null &&
                TryStartMethod != null)
            {
                return true;
            }

            Assembly[] assemblies =
                AppDomain.CurrentDomain.GetAssemblies();
            if (assemblies == null ||
                assemblies.Length == ResolvedAssemblyCount)
            {
                return false;
            }

            ResolvedAssemblyCount = assemblies.Length;
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type apiType =
                    assemblies[i].GetType(
                        ApiTypeName,
                        false);
                if (apiType == null)
                {
                    continue;
                }

                MethodInfo specificVersionMethod =
                    apiType.GetMethod(
                        "GetSampleContinuationApiVersion",
                        BindingFlags.Public |
                        BindingFlags.Static);
                MethodInfo legacyVersionMethod =
                    apiType.GetMethod(
                        "GetApiVersion",
                        BindingFlags.Public |
                        BindingFlags.Static);
                MethodInfo isWindowOpenMethod =
                    apiType.GetMethod(
                        "IsSampleContinuationWindowOpen",
                        BindingFlags.Public |
                        BindingFlags.Static);
                MethodInfo getCandidatesMethod =
                    apiType.GetMethod(
                        "GetSampleContinuationCandidates",
                        BindingFlags.Public |
                        BindingFlags.Static);
                MethodInfo tryStartMethod =
                    apiType.GetMethod(
                        "TryStartSampleContinuation",
                        BindingFlags.Public |
                        BindingFlags.Static);
                MethodInfo finishWindowMethod =
                    apiType.GetMethod(
                        "FinishSampleContinuationWindow",
                        BindingFlags.Public |
                        BindingFlags.Static);

                MethodInfo versionMethod =
                    specificVersionMethod ??
                    legacyVersionMethod;
                int version;
                try
                {
                    object versionResult =
                        versionMethod == null
                            ? null
                            : versionMethod.Invoke(null, null);
                    if (!(versionResult is int))
                    {
                        continue;
                    }

                    version = (int)versionResult;
                    if (version < 1)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if (isWindowOpenMethod == null ||
                    getCandidatesMethod == null ||
                    tryStartMethod == null)
                {
                    return false;
                }

                ApiType = apiType;
                SampleApiVersion = version;
                GetSampleContinuationApiVersionMethod =
                    specificVersionMethod;
                LegacyGetApiVersionMethod =
                    legacyVersionMethod;
                IsWindowOpenMethod =
                    isWindowOpenMethod;
                GetCandidatesMethod =
                    getCandidatesMethod;
                TryStartMethod =
                    tryStartMethod;
                FinishWindowMethod =
                    finishWindowMethod;

                try
                {
                    IsWindowOpenCallback =
                        (PorterBoolDelegate)Delegate.CreateDelegate(
                            typeof(PorterBoolDelegate),
                            isWindowOpenMethod);
                    GetCandidatesCallback =
                        (PorterCandidatesDelegate)Delegate.CreateDelegate(
                            typeof(PorterCandidatesDelegate),
                            getCandidatesMethod);
                    TryStartCallback =
                        (PorterProcedureBoolDelegate)Delegate.CreateDelegate(
                            typeof(PorterProcedureBoolDelegate),
                            tryStartMethod);
                    FinishWindowCallback =
                        finishWindowMethod == null
                            ? null
                            : (PorterBoolDelegate)Delegate.CreateDelegate(
                                typeof(PorterBoolDelegate),
                                finishWindowMethod);
                }
                catch
                {
                    // Keep the reflection fallback for older or unusual
                    // Hospital Porters builds whose public signatures do not
                    // bind exactly to the typed delegates.
                    IsWindowOpenCallback = null;
                    GetCandidatesCallback = null;
                    TryStartCallback = null;
                    FinishWindowCallback = null;
                }

                return true;
            }

            return false;
        }
    }

    internal static class SmartTransportSampleContinuationRuntime
    {
        private static readonly HashSet<Entity> DecidedWindows =
            new HashSet<Entity>();

        internal static void Update(
            BehaviorNurse nurse)
        {
            if (!FlowOptimizerConfig.SmartTransportSampleContinuation ||
                nurse == null)
            {
                return;
            }

            EmployeeComponent employee =
                nurse.GetComponent<EmployeeComponent>();
            Entity porter =
                employee == null
                    ? null
                    : employee.m_entity;
            if (porter == null)
            {
                return;
            }

            bool windowOpen =
                HospitalPortersSampleContinuationCompatibility
                    .IsWindowOpen(porter);
            if (!windowOpen)
            {
                DecidedWindows.Remove(porter);
                return;
            }

            if (DecidedWindows.Contains(porter))
            {
                return;
            }

            LabProcedure[] candidates =
                HospitalPortersSampleContinuationCompatibility
                    .GetCandidates(porter);
            LabProcedure target =
                SelectBestCandidate(
                    porter,
                    candidates);
            if (target == null)
            {
                // The current HPO return window can gain a real candidate
                // later. Do not consume the one decision until one exists.
                return;
            }

            // One decision per HPO Sample return window. Mark it before
            // either the probability gate or HPO's atomic revalidation.
            DecidedWindows.Add(porter);

            int chanceRoll;
            if (!PassesChance(
                    FlowOptimizerConfig
                        .SmartTransportSampleContinuationChance,
                    out chanceRoll))
            {
                bool finished =
                    HospitalPortersSampleContinuationCompatibility
                        .FinishWindow(porter);
                Log(
                    porter,
                    target,
                    finished
                        ? "declined by HFO chance; HPO window closed"
                        : "declined by HFO chance; runtime guard retained",
                    chanceRoll);
                return;
            }

            bool started =
                HospitalPortersSampleContinuationCompatibility
                    .TryStart(
                        porter,
                        target);
            if (!started)
            {
                HospitalPortersSampleContinuationCompatibility
                    .FinishWindow(porter);
            }

            Log(
                porter,
                target,
                started
                    ? "continuation started"
                    : "rejected by HPO revalidation; return left intact",
                chanceRoll);
        }

        internal static void Clear()
        {
            DecidedWindows.Clear();
        }

        private static LabProcedure SelectBestCandidate(
            Entity porter,
            LabProcedure[] candidates)
        {
            if (porter == null ||
                candidates == null ||
                candidates.Length == 0)
            {
                return null;
            }

            WalkComponent porterWalk =
                porter.GetComponent<WalkComponent>();
            GridMap gridMap =
                GridMap.GetInstance();

            LabProcedure best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < candidates.Length; i++)
            {
                LabProcedure candidate =
                    candidates[i];
                if (candidate == null ||
                    candidate.m_state == null ||
                    candidate.m_state.m_usedEquipment == null)
                {
                    continue;
                }

                TileObject source =
                    candidate.m_state
                        .m_usedEquipment
                        .GetEntity();
                if (source == null ||
                    source.m_state == null)
                {
                    continue;
                }

                float score = float.MaxValue;
                if (porterWalk != null &&
                    gridMap != null)
                {
                    float route =
                        gridMap.GetDistance(
                            porterWalk.GetFloorIndex(),
                            porterWalk.GetCurrentTile(),
                            source.GetFloorIndex(),
                            source.m_state.m_position,
                            AccessRights.STAFF);
                    if (route >= 0f &&
                        route < float.MaxValue)
                    {
                        score =
                            route +
                            Math.Abs(
                                porterWalk.GetFloorIndex() -
                                source.GetFloorIndex()) *
                            100f;
                    }
                }

                if (best == null ||
                    score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private static bool PassesChance(
            int chance,
            out int roll)
        {
            if (chance <= 0)
            {
                roll = 100;
                return false;
            }

            if (chance >= 100)
            {
                roll = 0;
                return true;
            }

            roll =
                UnityEngine.Random.Range(
                    0,
                    100);
            return roll < chance;
        }

        private static void Log(
            Entity porter,
            LabProcedure procedure,
            string outcome,
            int chanceRoll)
        {
            if (!FlowOptimizerConfig.TransportDiagnosticLogging ||
                Plugin.Log == null)
            {
                return;
            }

            Entity patient =
                procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_patient == null
                    ? null
                    : procedure.m_state.m_patient.GetEntity();

            Plugin.Log.LogInfo(
                "[TransportSampleContinuation] " +
                outcome +
                "; chance=" +
                FlowOptimizerConfig
                    .SmartTransportSampleContinuationChance +
                "; roll=" +
                chanceRoll +
                "; porter=" +
                HfoDiagnostics.EntityName(porter) +
                "; patient=" +
                HfoDiagnostics.EntityName(patient));
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.Update),
        new Type[] { typeof(float) })]
    [HarmonyAfter("Loupito75:HospitalPorters")]
    internal static class SmartTransportSampleContinuationUpdatePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            BehaviorNurse __instance)
        {
            SmartTransportSampleContinuationRuntime
                .Update(__instance);
        }
    }
}
