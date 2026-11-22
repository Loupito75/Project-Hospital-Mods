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

        private delegate int VersionDelegate();
        private delegate bool PorterBoolDelegate(Entity porter);
        private delegate LabProcedure[] PorterCandidatesDelegate(
            Entity porter);
        private delegate bool PorterProcedureBoolDelegate(
            Entity porter,
            LabProcedure procedure);

        private static PorterBoolDelegate IsWindowOpenCallback;
        private static PorterCandidatesDelegate GetCandidatesCallback;
        private static PorterProcedureBoolDelegate TryStartCallback;
        private static PorterBoolDelegate FinishWindowCallback;
        private static int SampleApiVersion;
        private static bool ResolutionComplete;
        private static bool IntegrationDisabled;
        private static bool FailureLogged;

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
                return IsWindowOpenCallback(porter);
            }
            catch (Exception exception)
            {
                Disable(
                    exception,
                    "HPO Sample continuation window API failed");
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
                    GetCandidatesCallback(porter);
                return result ?? new LabProcedure[0];
            }
            catch (Exception exception)
            {
                Disable(
                    exception,
                    "HPO Sample continuation candidate API failed");
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
                return TryStartCallback(
                    porter,
                    procedure);
            }
            catch (Exception exception)
            {
                Disable(
                    exception,
                    "HPO Sample continuation start API failed");
                return false;
            }
        }

        internal static bool FinishWindow(
            Entity porter)
        {
            if (porter == null ||
                !Resolve() ||
                SampleApiVersion < 2 ||
                FinishWindowCallback == null)
            {
                return false;
            }

            try
            {
                return FinishWindowCallback(porter);
            }
            catch (Exception exception)
            {
                Disable(
                    exception,
                    "HPO Sample continuation finish API failed");
                return false;
            }
        }

        private static bool Resolve()
        {
            if (ResolutionComplete)
            {
                return !IntegrationDisabled &&
                    IsWindowOpenCallback != null &&
                    GetCandidatesCallback != null &&
                    TryStartCallback != null &&
                    (SampleApiVersion < 2 ||
                     FinishWindowCallback != null);
            }

            ResolutionComplete = true;

            BepInEx.PluginInfo pluginInfo;
            if (!Chainloader.PluginInfos.TryGetValue(
                    HpoPluginGuid,
                    out pluginInfo) ||
                pluginInfo == null ||
                pluginInfo.Instance == null)
            {
                return false;
            }

            Type apiType =
                pluginInfo.Instance
                    .GetType()
                    .Assembly
                    .GetType(
                        ApiTypeName,
                        false);
            if (apiType == null)
            {
                return false;
            }

            MethodInfo specificVersionMethod =
                GetExactPublicStaticMethod(
                    apiType,
                    "GetSampleContinuationApiVersion",
                    typeof(int),
                    Type.EmptyTypes);

            MethodInfo versionMethod =
                specificVersionMethod;

            if (versionMethod == null)
            {
                if (HasPublicMethodNamed(
                        apiType,
                        "GetSampleContinuationApiVersion"))
                {
                    return false;
                }

                // Temporary public legacy compatibility for HPO releases
                // predating the dedicated Sample capability version.
                versionMethod =
                    GetExactPublicStaticMethod(
                        apiType,
                        "GetApiVersion",
                        typeof(int),
                        Type.EmptyTypes);
            }

            if (versionMethod == null)
            {
                return false;
            }

            VersionDelegate versionCallback;
            try
            {
                versionCallback =
                    (VersionDelegate)Delegate.CreateDelegate(
                        typeof(VersionDelegate),
                        versionMethod);
            }
            catch
            {
                return false;
            }

            int version;
            try
            {
                version = versionCallback();
            }
            catch (Exception exception)
            {
                Disable(
                    exception,
                    "HPO Sample continuation capability API failed");
                return false;
            }

            if (version < 1)
            {
                return false;
            }

            MethodInfo isWindowOpenMethod =
                GetExactPublicStaticMethod(
                    apiType,
                    "IsSampleContinuationWindowOpen",
                    typeof(bool),
                    new Type[] { typeof(Entity) });
            MethodInfo getCandidatesMethod =
                GetExactPublicStaticMethod(
                    apiType,
                    "GetSampleContinuationCandidates",
                    typeof(LabProcedure[]),
                    new Type[] { typeof(Entity) });
            MethodInfo tryStartMethod =
                GetExactPublicStaticMethod(
                    apiType,
                    "TryStartSampleContinuation",
                    typeof(bool),
                    new Type[]
                    {
                        typeof(Entity),
                        typeof(LabProcedure)
                    });

            if (isWindowOpenMethod == null ||
                getCandidatesMethod == null ||
                tryStartMethod == null)
            {
                return false;
            }

            MethodInfo finishWindowMethod = null;
            if (version >= 2)
            {
                finishWindowMethod =
                    GetExactPublicStaticMethod(
                        apiType,
                        "FinishSampleContinuationWindow",
                        typeof(bool),
                        new Type[] { typeof(Entity) });
                if (finishWindowMethod == null)
                {
                    return false;
                }
            }

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
                ClearCallbacks();
                return false;
            }

            SampleApiVersion = version;
            return true;
        }

        private static MethodInfo GetExactPublicStaticMethod(
            Type apiType,
            string methodName,
            Type returnType,
            Type[] parameterTypes)
        {
            MethodInfo method =
                apiType.GetMethod(
                    methodName,
                    BindingFlags.Public |
                    BindingFlags.Static,
                    null,
                    parameterTypes,
                    null);

            return method != null &&
                method.ReturnType == returnType
                    ? method
                    : null;
        }

        private static bool HasPublicMethodNamed(
            Type apiType,
            string methodName)
        {
            MethodInfo[] methods =
                apiType.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.Instance);

            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i] != null &&
                    methods[i].Name == methodName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Disable(
            Exception exception,
            string message)
        {
            IntegrationDisabled = true;
            ClearCallbacks();

            if (FailureLogged || Plugin.Log == null)
            {
                return;
            }

            FailureLogged = true;
            Exception root =
                exception == null
                    ? null
                    : (exception.InnerException ?? exception);

            Plugin.Log.LogWarning(
                message +
                "; HFO will keep its normal transport fallback for " +
                "the rest of this session" +
                (root == null
                    ? "."
                    : ": " +
                      root.GetType().Name + ": " +
                      root.Message));
        }

        private static void ClearCallbacks()
        {
            IsWindowOpenCallback = null;
            GetCandidatesCallback = null;
            TryStartCallback = null;
            FinishWindowCallback = null;
        }
    }

    internal static class SmartTransportSampleContinuationRuntime
    {
        private static readonly HashSet<Entity> DecidedWindows =
            new HashSet<Entity>();

        internal static void Update(
            Entity porter)
        {
            if (!FlowOptimizerConfig.SmartTransportSampleContinuation ||
                porter == null)
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

}
