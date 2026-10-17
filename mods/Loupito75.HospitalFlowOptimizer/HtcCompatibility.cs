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
        private const string BathroomMethodName =
            "IsBathroomAllowedForCharacter";
        private const string FindBathroomMethodName =
            "FindAllowedBathroomForCharacter";

        private static MethodInfo _bathroomAllowedMethod;
        private static MethodInfo _findAllowedBathroomMethod;
        private static bool _resolutionComplete;
        private static bool _invokeFailureLogged;
        private static bool _findFailureLogged;

        internal static bool IsBathroomAllowedForCharacter(
            Entity character,
            TileObject bathroom,
            out bool apiAvailable)
        {
            Resolve();

            apiAvailable = _bathroomAllowedMethod != null;
            if (!apiAvailable)
            {
                return true;
            }

            try
            {
                object result = _bathroomAllowedMethod.Invoke(
                    null,
                    new object[] { character, bathroom });

                return result is bool && (bool)result;
            }
            catch (Exception exception)
            {
                if (!_invokeFailureLogged && Plugin.Log != null)
                {
                    _invokeFailureLogged = true;
                    Exception root =
                        exception.InnerException ?? exception;
                    Plugin.Log.LogWarning(
                        "HTC bathroom compatibility API failed; " +
                        "HFO will keep its normal toilet fallback: " +
                        root.GetType().Name + ": " + root.Message);
                }

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

            apiAvailable = _findAllowedBathroomMethod != null;
            if (!apiAvailable)
            {
                return null;
            }

            try
            {
                object result = _findAllowedBathroomMethod.Invoke(
                    null,
                    new object[]
                    {
                        character,
                        procedure,
                        accessRights
                    });

                return result as TileObject;
            }
            catch (Exception exception)
            {
                if (!_findFailureLogged && Plugin.Log != null)
                {
                    _findFailureLogged = true;
                    Exception root =
                        exception.InnerException ?? exception;
                    Plugin.Log.LogWarning(
                        "HTC allowed-bathroom lookup failed; " +
                        "HFO will keep its normal toilet fallback: " +
                        root.GetType().Name + ": " + root.Message);
                }

                return null;
            }
        }

        private static void Resolve()
        {
            if (_resolutionComplete)
            {
                return;
            }

            PluginInfo pluginInfo;
            if (!Chainloader.PluginInfos.TryGetValue(
                    HtcPluginGuid,
                    out pluginInfo))
            {
                return;
            }

            if (pluginInfo == null || pluginInfo.Instance == null)
            {
                return;
            }

            Type apiType = pluginInfo.Instance
                .GetType()
                .Assembly
                .GetType(HtcApiTypeName, false);

            if (apiType != null)
            {
                _bathroomAllowedMethod = apiType.GetMethod(
                    BathroomMethodName,
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Entity), typeof(TileObject) },
                    null);

                _findAllowedBathroomMethod = apiType.GetMethod(
                    FindBathroomMethodName,
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[]
                    {
                        typeof(Entity),
                        typeof(GameDBProcedure),
                        typeof(AccessRights)
                    },
                    null);
            }

            _resolutionComplete = true;
        }
    }
}
