using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using GLib;
using Lopital;

namespace HospitalFlowOptimizer
{
    internal static class HtcCompatibility
    {
        private const string HtcPluginGuid =
            "loupito75.HospitalTrafficControl";
        private const string HtcApiTypeName =
            "HospitalTrafficControl.HospitalTrafficControlApi";
        private const string BathroomApiVersionMethodName =
            "GetBathroomCompatibilityApiVersion";
        private const string BathroomMethodName =
            "IsBathroomAllowedForCharacter";
        private const string FindBathroomMethodName =
            "FindAllowedBathroomForCharacter";
        private const int MinimumBathroomApiVersion = 1;

        private delegate int BathroomApiVersionCall();
        private delegate bool BathroomAllowedCall(
            Entity character,
            TileObject bathroom);
        private delegate TileObject FindBathroomCall(
            Entity character,
            GameDBProcedure procedure,
            AccessRights rights);

        private static BathroomAllowedCall _bathroomAllowedCall;
        private static FindBathroomCall _findBathroomCall;
        private static bool _resolutionComplete;
        private static bool _bathroomApiDisabled;
        private static bool _failureLogged;

        internal static bool IsBathroomAllowedForCharacter(
            Entity character,
            TileObject bathroom,
            out bool apiAvailable)
        {
            Resolve();

            apiAvailable = IsBathroomApiAvailable();
            if (!apiAvailable)
            {
                return true;
            }

            try
            {
                return _bathroomAllowedCall(
                    character,
                    bathroom);
            }
            catch (Exception exception)
            {
                DisableBathroomApi(
                    exception,
                    "HTC bathroom compatibility API failed");
                apiAvailable = false;
                return true;
            }
        }

        internal static TileObject FindAllowedBathroomForCharacter(
            Entity character,
            GameDBProcedure procedure,
            AccessRights accessRights,
            out bool apiAvailable)
        {
            Resolve();

            apiAvailable = IsBathroomApiAvailable();
            if (!apiAvailable)
            {
                return null;
            }

            try
            {
                return _findBathroomCall(
                    character,
                    procedure,
                    accessRights);
            }
            catch (Exception exception)
            {
                DisableBathroomApi(
                    exception,
                    "HTC allowed-bathroom lookup failed");
                apiAvailable = false;
                return null;
            }
        }

        private static bool IsBathroomApiAvailable()
        {
            return !_bathroomApiDisabled &&
                _bathroomAllowedCall != null &&
                _findBathroomCall != null;
        }

        private static void Resolve()
        {
            if (_resolutionComplete)
            {
                return;
            }

            _resolutionComplete = true;

            PluginInfo pluginInfo;
            if (!Chainloader.PluginInfos.TryGetValue(
                    HtcPluginGuid,
                    out pluginInfo) ||
                pluginInfo == null ||
                pluginInfo.Instance == null)
            {
                return;
            }

            Type apiType =
                pluginInfo.Instance
                    .GetType()
                    .Assembly
                    .GetType(
                        HtcApiTypeName,
                        false);

            if (apiType == null)
            {
                return;
            }

            MethodInfo versionMethod =
                apiType.GetMethod(
                    BathroomApiVersionMethodName,
                    BindingFlags.Public |
                    BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);

            if (versionMethod != null)
            {
                if (versionMethod.ReturnType != typeof(int))
                {
                    return;
                }

                BathroomApiVersionCall versionCall;
                try
                {
                    versionCall =
                        (BathroomApiVersionCall)
                            Delegate.CreateDelegate(
                                typeof(BathroomApiVersionCall),
                                versionMethod);
                }
                catch
                {
                    return;
                }

                int apiVersion;
                try
                {
                    apiVersion = versionCall();
                }
                catch (Exception exception)
                {
                    DisableBathroomApi(
                        exception,
                        "HTC bathroom compatibility capability failed");
                    return;
                }

                if (apiVersion < MinimumBathroomApiVersion)
                {
                    return;
                }

                TryBindBathroomMethods(apiType);
                return;
            }

            // A public getter with the expected name but an incompatible
            // signature is not legacy. Legacy is accepted only when the
            // capability getter is genuinely absent.
            if (HasPublicMethodNamed(
                    apiType,
                    BathroomApiVersionMethodName))
            {
                return;
            }

            // Temporary legacy compatibility for HTC releases predating the
            // bathroom capability version. Only the two historical public
            // signatures are accepted; no private HTC internals are used.
            TryBindBathroomMethods(apiType);
        }

        private static bool TryBindBathroomMethods(
            Type apiType)
        {
            MethodInfo bathroomAllowedMethod =
                apiType.GetMethod(
                    BathroomMethodName,
                    BindingFlags.Public |
                    BindingFlags.Static,
                    null,
                    new Type[]
                    {
                        typeof(Entity),
                        typeof(TileObject)
                    },
                    null);

            MethodInfo findAllowedBathroomMethod =
                apiType.GetMethod(
                    FindBathroomMethodName,
                    BindingFlags.Public |
                    BindingFlags.Static,
                    null,
                    new Type[]
                    {
                        typeof(Entity),
                        typeof(GameDBProcedure),
                        typeof(AccessRights)
                    },
                    null);

            if (bathroomAllowedMethod == null ||
                bathroomAllowedMethod.ReturnType != typeof(bool) ||
                findAllowedBathroomMethod == null ||
                findAllowedBathroomMethod.ReturnType !=
                    typeof(TileObject))
            {
                return false;
            }

            BathroomAllowedCall bathroomAllowedCall;
            FindBathroomCall findBathroomCall;

            try
            {
                bathroomAllowedCall =
                    (BathroomAllowedCall)
                        Delegate.CreateDelegate(
                            typeof(BathroomAllowedCall),
                            bathroomAllowedMethod);

                findBathroomCall =
                    (FindBathroomCall)
                        Delegate.CreateDelegate(
                            typeof(FindBathroomCall),
                            findAllowedBathroomMethod);
            }
            catch
            {
                return false;
            }

            _bathroomAllowedCall = bathroomAllowedCall;
            _findBathroomCall = findBathroomCall;
            return true;
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

        private static void DisableBathroomApi(
            Exception exception,
            string message)
        {
            _bathroomApiDisabled = true;
            _bathroomAllowedCall = null;
            _findBathroomCall = null;

            if (_failureLogged || Plugin.Log == null)
            {
                return;
            }

            _failureLogged = true;
            Exception root =
                exception == null
                    ? null
                    : (exception.InnerException ?? exception);

            Plugin.Log.LogWarning(
                message +
                "; HFO will keep its normal toilet fallback for " +
                "the rest of this session" +
                (root == null
                    ? "."
                    : ": " +
                      root.GetType().Name + ": " +
                      root.Message));
        }
    }
}
