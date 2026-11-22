using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using GLib;
using Lopital;

namespace HospitalPorters
{
    internal static class HfxWaitingVisualInterop
    {
        private const string HfxPluginGuid =
            "loupito75.HospitalFixes";
        private const string ApiTypeName =
            "HospitalFixes.HospitalFixesApi";
        private const string ApiVersionFieldName =
            "ApiVersion";
        private const string ApiMethodName =
            "TrySetExternalWaitingState";
        private const int MinimumApiVersion = 2;

        internal const string SampleDestinationFridgeSourceId =
            "loupito75.HospitalPorters:sample-destination-fridge";

        private delegate bool TrySetExternalWaitingStateDelegate(
            Entity character,
            string sourceId,
            bool waiting,
            TileObject[] interactionObjects);

        private delegate bool TrySetExternalWaitingStateExtendedDelegate(
            Entity character,
            string sourceId,
            bool waiting,
            TileObject[] interactionObjects,
            TileObject unavailableEquipment,
            bool showEquipmentShortagePopup);

        private static bool ResolutionAttempted;
        private static TrySetExternalWaitingStateDelegate
            TrySetExternalWaitingStateCallback;
        private static TrySetExternalWaitingStateExtendedDelegate
            TrySetExternalWaitingStateExtendedCallback;
        private static bool InvocationFailureLogged;

        internal static bool SetSampleDestinationFridgeWaiting(
            Entity porter,
            bool waiting,
            TileObject[] interactionObjects,
            TileObject unavailableEquipment,
            bool showEquipmentShortagePopup)
        {
            if (porter == null)
            {
                return false;
            }

            EnsureResolved();

            TrySetExternalWaitingStateDelegate stagingCallback =
                TrySetExternalWaitingStateCallback;
            if (stagingCallback == null)
            {
                return false;
            }

            if (waiting &&
                showEquipmentShortagePopup)
            {
                TrySetExternalWaitingStateExtendedDelegate extendedCallback =
                    TrySetExternalWaitingStateExtendedCallback;
                if (extendedCallback != null)
                {
                    try
                    {
                        // HFX's bool is integration/UI status only. HPO owns
                        // the Sample mission and never branches gameplay on it.
                        extendedCallback(
                            porter,
                            SampleDestinationFridgeSourceId,
                            true,
                            interactionObjects,
                            unavailableEquipment,
                            true);

                        // This return value describes only whether HPO actually
                        // dispatched the optional popup request through the
                        // extended signature. It never exposes HFX's bool.
                        return true;
                    }
                    catch (Exception exception)
                    {
                        DisableIntegration();
                        LogInvocationFailureOnce(
                            "extended waiting/popup",
                            exception);
                        return false;
                    }
                }
            }

            try
            {
                stagingCallback(
                    porter,
                    SampleDestinationFridgeSourceId,
                    waiting,
                    waiting
                        ? interactionObjects
                        : null);
            }
            catch (Exception exception)
            {
                DisableIntegration();
                LogInvocationFailureOnce(
                    "waiting staging",
                    exception);
            }

            return false;
        }

        internal static void Reset()
        {
            ResolutionAttempted = false;
            TrySetExternalWaitingStateCallback = null;
            TrySetExternalWaitingStateExtendedCallback = null;
            InvocationFailureLogged = false;
        }

        private static void DisableIntegration()
        {
            TrySetExternalWaitingStateCallback = null;
            TrySetExternalWaitingStateExtendedCallback = null;
        }

        private static void EnsureResolved()
        {
            if (ResolutionAttempted)
            {
                return;
            }

            ResolutionAttempted = true;

            try
            {
                if (Chainloader.PluginInfos == null)
                {
                    return;
                }

                PluginInfo pluginInfo;
                if (!Chainloader.PluginInfos.TryGetValue(
                        HfxPluginGuid,
                        out pluginInfo))
                {
                    return;
                }

                if (pluginInfo == null ||
                    pluginInfo.Instance == null)
                {
                    Plugin.Log?.LogWarning(
                        "Optional HFX plugin is registered without a live instance; " +
                        "HPO will continue its destination-fridge workflow without HFX staging/popup.");
                    return;
                }

                Assembly providerAssembly =
                    pluginInfo.Instance.GetType().Assembly;
                if (providerAssembly == null)
                {
                    Plugin.Log?.LogWarning(
                        "Optional HFX plugin assembly is unavailable; " +
                        "HPO will continue its destination-fridge workflow without HFX staging/popup.");
                    return;
                }

                Type apiType =
                    providerAssembly.GetType(
                        ApiTypeName,
                        false);
                if (apiType == null)
                {
                    Plugin.Log?.LogWarning(
                        "Optional HFX External Waiting v2 API type is unavailable in the loaded HFX assembly; " +
                        "HPO will continue its destination-fridge workflow without HFX staging/popup.");
                    return;
                }

                FieldInfo versionField =
                    apiType.GetField(
                        ApiVersionFieldName,
                        BindingFlags.Public |
                        BindingFlags.Static);
                object versionValue =
                    versionField == null
                        ? null
                        : versionField.IsLiteral
                            ? versionField.GetRawConstantValue()
                            : versionField.GetValue(null);

                if (!(versionValue is int) ||
                    (int)versionValue < MinimumApiVersion)
                {
                    Plugin.Log?.LogWarning(
                        "Optional HFX External Waiting v2 API is unavailable; " +
                        "HPO will continue its destination-fridge workflow without HFX staging/popup.");
                    return;
                }

                MethodInfo stagingMethod =
                    apiType.GetMethod(
                        ApiMethodName,
                        BindingFlags.Public |
                        BindingFlags.Static,
                        null,
                        new Type[]
                        {
                            typeof(Entity),
                            typeof(string),
                            typeof(bool),
                            typeof(TileObject[])
                        },
                        null);

                if (stagingMethod != null &&
                    stagingMethod.ReturnType == typeof(bool))
                {
                    TrySetExternalWaitingStateCallback =
                        (TrySetExternalWaitingStateDelegate)
                        Delegate.CreateDelegate(
                            typeof(TrySetExternalWaitingStateDelegate),
                            stagingMethod);
                }

                MethodInfo extendedMethod =
                    apiType.GetMethod(
                        ApiMethodName,
                        BindingFlags.Public |
                        BindingFlags.Static,
                        null,
                        new Type[]
                        {
                            typeof(Entity),
                            typeof(string),
                            typeof(bool),
                            typeof(TileObject[]),
                            typeof(TileObject),
                            typeof(bool)
                        },
                        null);

                if (extendedMethod != null &&
                    extendedMethod.ReturnType == typeof(bool))
                {
                    TrySetExternalWaitingStateExtendedCallback =
                        (TrySetExternalWaitingStateExtendedDelegate)
                        Delegate.CreateDelegate(
                            typeof(TrySetExternalWaitingStateExtendedDelegate),
                            extendedMethod);
                }

                if (TrySetExternalWaitingStateCallback == null)
                {
                    TrySetExternalWaitingStateExtendedCallback = null;
                    Plugin.Log?.LogWarning(
                        "Optional HFX External Waiting v2 staging signature is incompatible; " +
                        "HPO will continue its destination-fridge workflow without HFX staging/popup.");
                    return;
                }

                PorterDiagnostics.Log(
                    "HFX External Waiting v2 integration enabled: staging=" +
                    (TrySetExternalWaitingStateCallback != null) +
                    "; extendedPopup=" +
                    (TrySetExternalWaitingStateExtendedCallback != null) +
                    ".");
            }
            catch (Exception exception)
            {
                DisableIntegration();
                Plugin.Log?.LogWarning(
                    "Optional HFX External Waiting v2 API resolution failed safely; " +
                    "HPO will continue its destination-fridge workflow without HFX staging/popup: " +
                    exception.GetType().Name + ": " +
                    exception.Message);
            }
        }

        private static void LogInvocationFailureOnce(
            string capability,
            Exception exception)
        {
            if (InvocationFailureLogged ||
                Plugin.Log == null)
            {
                return;
            }

            InvocationFailureLogged = true;
            Plugin.Log.LogWarning(
                "Optional HFX " +
                capability +
                " call failed; HPO will continue its destination-fridge workflow: " +
                exception.GetType().Name + ": " +
                exception.Message);
        }
    }
}
