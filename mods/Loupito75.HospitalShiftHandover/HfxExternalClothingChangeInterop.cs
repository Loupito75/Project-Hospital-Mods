using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using GLib;
using Lopital;

namespace HospitalShiftHandover
{
    internal static class HfxExternalClothingChangeInterop
    {
        private const string HfxPluginGuid = "loupito75.HospitalFixes";
        private const string ApiTypeName = "HospitalFixes.HospitalFixesApi";
        private const string SourceId =
            "loupito75.HospitalShiftHandover:locker-clothing-change";
        private const int MinimumApiVersion = 1;

        private delegate int GetExternalClothingChangeApiVersionDelegate();

        private delegate bool TrySetExternalClothingChangeStateDelegate(
            Entity character,
            string sourceId,
            bool changing);

        private static readonly Dictionary<EmployeeComponent, Entity>
            ActiveCharacters =
                new Dictionary<EmployeeComponent, Entity>();

        private static bool s_resolutionAttempted;
        private static bool s_available;
        private static bool s_warningLogged;
        private static GetExternalClothingChangeApiVersionDelegate s_getVersion;
        private static TrySetExternalClothingChangeStateDelegate s_setState;

        internal static void Begin(EmployeeComponent employee)
        {
            if (employee == null || employee.m_entity == null)
            {
                return;
            }

            Entity character = employee.m_entity;
            Entity previousCharacter;
            if (ActiveCharacters.TryGetValue(
                    employee,
                    out previousCharacter))
            {
                if (previousCharacter == character)
                {
                    return;
                }

                ActiveCharacters.Remove(employee);
                Publish(previousCharacter, false);
            }

            ActiveCharacters[employee] = character;
            Publish(character, true);
        }

        internal static void Clear(EmployeeComponent employee)
        {
            if (employee == null)
            {
                return;
            }

            Entity character;
            if (!ActiveCharacters.TryGetValue(employee, out character))
            {
                return;
            }

            ActiveCharacters.Remove(employee);
            Publish(character, false);
        }

        internal static void Reset()
        {
            if (ActiveCharacters.Count == 0)
            {
                return;
            }

            List<Entity> characters =
                new List<Entity>(ActiveCharacters.Values);
            ActiveCharacters.Clear();

            for (int i = 0; i < characters.Count; i++)
            {
                Publish(characters[i], false);
            }
        }

        private static void Publish(Entity character, bool changing)
        {
            if (character == null || !EnsureAvailable())
            {
                return;
            }

            try
            {
                s_setState(character, SourceId, changing);
            }
            catch (Exception exception)
            {
                DisableForSession(exception);
            }
        }

        private static bool EnsureAvailable()
        {
            if (s_resolutionAttempted)
            {
                return s_available;
            }

            s_resolutionAttempted = true;

            try
            {
                PluginInfo pluginInfo;
                if (Chainloader.PluginInfos == null ||
                    !Chainloader.PluginInfos.TryGetValue(
                        HfxPluginGuid,
                        out pluginInfo) ||
                    pluginInfo == null ||
                    pluginInfo.Instance == null)
                {
                    return false;
                }

                Assembly assembly =
                    pluginInfo.Instance.GetType().Assembly;
                Type apiType =
                    assembly.GetType(ApiTypeName, false);
                if (apiType == null || !apiType.IsPublic)
                {
                    return false;
                }

                MethodInfo versionMethod = apiType.GetMethod(
                    "GetExternalClothingChangeApiVersion",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    Type.EmptyTypes,
                    null);
                if (versionMethod == null ||
                    versionMethod.ReturnType != typeof(int))
                {
                    return false;
                }

                s_getVersion =
                    (GetExternalClothingChangeApiVersionDelegate)
                        Delegate.CreateDelegate(
                            typeof(
                                GetExternalClothingChangeApiVersionDelegate),
                            versionMethod);

                if (s_getVersion() < MinimumApiVersion)
                {
                    return false;
                }

                MethodInfo setStateMethod = apiType.GetMethod(
                    "TrySetExternalClothingChangeState",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[]
                    {
                        typeof(Entity),
                        typeof(string),
                        typeof(bool)
                    },
                    null);
                if (setStateMethod == null ||
                    setStateMethod.ReturnType != typeof(bool))
                {
                    return false;
                }

                s_setState =
                    (TrySetExternalClothingChangeStateDelegate)
                        Delegate.CreateDelegate(
                            typeof(
                                TrySetExternalClothingChangeStateDelegate),
                            setStateMethod);
                s_available = s_setState != null;
                return s_available;
            }
            catch (Exception)
            {
                s_available = false;
                s_getVersion = null;
                s_setState = null;
                return false;
            }
        }

        private static void DisableForSession(Exception exception)
        {
            s_available = false;
            s_getVersion = null;
            s_setState = null;

            if (s_warningLogged || Plugin.Log == null)
            {
                return;
            }

            s_warningLogged = true;
            Plugin.Log.LogWarning(
                "Optional HFX clothing-change visual integration was " +
                "disabled for this session after an interop error: " +
                exception.GetType().Name +
                (string.IsNullOrEmpty(exception.Message)
                    ? string.Empty
                    : " - " + exception.Message));
        }
    }
}
