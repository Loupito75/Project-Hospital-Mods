using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal static class CleaningTileProtectionRegistry
    {
        private delegate bool SupportsRoomCallback(Room room);

        private delegate bool TryIsTileProtectedCallback(
            Room room,
            Vector2i tile,
            out bool protectedTile);

        private sealed class Provider
        {
            internal string SourceId;
            internal SupportsRoomCallback SupportsRoom;
            internal TryIsTileProtectedCallback TryIsTileProtected;
            internal readonly Dictionary<Room, bool> RoomSupportCache =
                new Dictionary<Room, bool>();
            internal volatile bool Disabled;
            internal bool FailureLogged;
        }

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, Provider>
            ProvidersBySource =
                new Dictionary<string, Provider>(
                    StringComparer.Ordinal);
        private static volatile Provider[] s_snapshot =
            new Provider[0];

        internal static bool IsAvailable
        {
            get
            {
                Provider[] providers = s_snapshot;
                for (int i = 0; i < providers.Length; i++)
                {
                    if (!providers[i].Disabled)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal static bool TryRegister(
            string sourceId,
            Delegate supportsRoom,
            Delegate tryIsTileProtected)
        {
            string normalized =
                NormalizeSourceId(sourceId);
            SupportsRoomCallback supports;
            TryIsTileProtectedCallback tileProtection;
            if (normalized == null ||
                !TryCreateSupportsRoomCallback(
                    supportsRoom,
                    out supports) ||
                !TryCreateTileProtectionCallback(
                    tryIsTileProtected,
                    out tileProtection))
            {
                return false;
            }

            Provider provider =
                new Provider
                {
                    SourceId = normalized,
                    SupportsRoom = supports,
                    TryIsTileProtected = tileProtection
                };

            lock (SyncRoot)
            {
                Provider previous;
                if (ProvidersBySource.TryGetValue(
                        normalized,
                        out previous))
                {
                    previous.Disabled = true;
                    previous.RoomSupportCache.Clear();
                }

                ProvidersBySource[normalized] =
                    provider;
                RebuildSnapshot();
            }

            JanitorCleaningManager
                .OnCleaningProtectionProvidersChanged();
            return true;
        }

        internal static bool TryUnregister(
            string sourceId)
        {
            string normalized =
                NormalizeSourceId(sourceId);
            if (normalized == null)
            {
                return false;
            }

            bool removed = false;
            lock (SyncRoot)
            {
                Provider provider;
                if (ProvidersBySource.TryGetValue(
                        normalized,
                        out provider))
                {
                    provider.Disabled = true;
                    provider.RoomSupportCache.Clear();
                    removed =
                        ProvidersBySource.Remove(
                            normalized);
                    RebuildSnapshot();
                }
            }

            if (removed)
            {
                JanitorCleaningManager
                    .OnCleaningProtectionProvidersChanged();
            }

            return removed;
        }

        internal static bool SupportsCleaningTileProtection(
            Room room)
        {
            if (!TrafficControlConfig
                    .AvoidCleaningActiveProcedureRooms ||
                room == null)
            {
                return false;
            }

            Provider[] providers = s_snapshot;
            bool anySupported = false;

            for (int i = 0; i < providers.Length; i++)
            {
                Provider provider = providers[i];
                if (provider.Disabled)
                {
                    continue;
                }

                bool supported;
                if (!TrySupportsRoom(
                        provider,
                        room,
                        out supported))
                {
                    continue;
                }

                if (supported)
                {
                    anySupported = true;
                }
            }

            return anySupported;
        }

        internal static bool TryIsCleaningTileProtected(
            Room room,
            Vector2i tile,
            out bool protectedTile)
        {
            protectedTile = false;

            if (!TrafficControlConfig
                    .AvoidCleaningActiveProcedureRooms ||
                room == null)
            {
                return false;
            }

            Provider[] providers = s_snapshot;
            bool anyApplicable = false;
            bool unreliable = false;

            for (int i = 0; i < providers.Length; i++)
            {
                Provider provider = providers[i];
                if (provider.Disabled)
                {
                    continue;
                }

                bool supported;
                if (!TrySupportsRoom(
                        provider,
                        room,
                        out supported))
                {
                    unreliable = true;
                    continue;
                }

                if (!supported)
                {
                    continue;
                }

                anyApplicable = true;

                bool providerProtected;
                bool decided;
                try
                {
                    decided =
                        provider.TryIsTileProtected(
                            room,
                            tile,
                            out providerProtected);
                }
                catch (Exception exception)
                {
                    DisableProvider(
                        provider,
                        "TryIsTileProtected",
                        exception);
                    unreliable = true;
                    continue;
                }

                if (!decided)
                {
                    unreliable = true;
                    continue;
                }

                if (providerProtected)
                {
                    protectedTile = true;
                    return true;
                }
            }

            if (!anyApplicable || unreliable)
            {
                return false;
            }

            return true;
        }

        internal static void ResetRuntime()
        {
            Provider[] providers = s_snapshot;
            for (int i = 0; i < providers.Length; i++)
            {
                providers[i].RoomSupportCache.Clear();
            }
        }

        internal static void Shutdown()
        {
            lock (SyncRoot)
            {
                foreach (Provider provider in
                         ProvidersBySource.Values)
                {
                    provider.Disabled = true;
                    provider.RoomSupportCache.Clear();
                }

                ProvidersBySource.Clear();
                s_snapshot = new Provider[0];
            }
        }

        private static bool TrySupportsRoom(
            Provider provider,
            Room room,
            out bool supported)
        {
            supported = false;
            if (provider == null ||
                provider.Disabled ||
                room == null)
            {
                return true;
            }

            if (provider.RoomSupportCache.TryGetValue(
                    room,
                    out supported))
            {
                return true;
            }

            try
            {
                supported =
                    provider.SupportsRoom(room);
                provider.RoomSupportCache[room] =
                    supported;
                return true;
            }
            catch (Exception exception)
            {
                DisableProvider(
                    provider,
                    "SupportsRoom",
                    exception);
                supported = false;
                return false;
            }
        }

        private static void DisableProvider(
            Provider provider,
            string callback,
            Exception exception)
        {
            if (provider == null)
            {
                return;
            }

            provider.Disabled = true;
            provider.RoomSupportCache.Clear();

            if (provider.FailureLogged)
            {
                return;
            }

            provider.FailureLogged = true;
            Exception root =
                exception == null
                    ? null
                    : (exception.InnerException ??
                       exception);

            Plugin.Log?.LogWarning(
                "Cleaning-tile protection provider '" +
                provider.SourceId +
                "' was disabled after " +
                callback +
                " failed" +
                (root == null
                    ? "."
                    : ": " +
                      root.GetType().FullName +
                      ": " +
                      root.Message));
        }

        private static string NormalizeSourceId(
            string sourceId)
        {
            if (sourceId == null)
            {
                return null;
            }

            string normalized =
                sourceId.Trim();
            return normalized.Length == 0
                ? null
                : normalized;
        }

        private static bool TryCreateSupportsRoomCallback(
            Delegate callback,
            out SupportsRoomCallback typed)
        {
            typed = null;
            if (!HasSingleInvocation(callback) ||
                !IsExactSupportsRoomSignature(
                    callback.GetType().GetMethod("Invoke")) ||
                !IsExactSupportsRoomSignature(
                    callback.Method))
            {
                return false;
            }

            try
            {
                typed =
                    callback.Target == null
                        ? (SupportsRoomCallback)
                            Delegate.CreateDelegate(
                                typeof(SupportsRoomCallback),
                                callback.Method)
                        : (SupportsRoomCallback)
                            Delegate.CreateDelegate(
                                typeof(SupportsRoomCallback),
                                callback.Target,
                                callback.Method);
                return typed != null;
            }
            catch
            {
                typed = null;
                return false;
            }
        }

        private static bool TryCreateTileProtectionCallback(
            Delegate callback,
            out TryIsTileProtectedCallback typed)
        {
            typed = null;
            if (!HasSingleInvocation(callback) ||
                !IsExactTileProtectionSignature(
                    callback.GetType().GetMethod("Invoke")) ||
                !IsExactTileProtectionSignature(
                    callback.Method))
            {
                return false;
            }

            try
            {
                typed =
                    callback.Target == null
                        ? (TryIsTileProtectedCallback)
                            Delegate.CreateDelegate(
                                typeof(TryIsTileProtectedCallback),
                                callback.Method)
                        : (TryIsTileProtectedCallback)
                            Delegate.CreateDelegate(
                                typeof(TryIsTileProtectedCallback),
                                callback.Target,
                                callback.Method);
                return typed != null;
            }
            catch
            {
                typed = null;
                return false;
            }
        }

        private static bool HasSingleInvocation(
            Delegate callback)
        {
            return callback != null &&
                callback.GetInvocationList().Length == 1;
        }

        private static bool IsExactSupportsRoomSignature(
            MethodInfo method)
        {
            if (method == null ||
                method.ReturnType != typeof(bool))
            {
                return false;
            }

            ParameterInfo[] parameters =
                method.GetParameters();
            return parameters.Length == 1 &&
                parameters[0].ParameterType ==
                    typeof(Room);
        }

        private static bool IsExactTileProtectionSignature(
            MethodInfo method)
        {
            if (method == null ||
                method.ReturnType != typeof(bool))
            {
                return false;
            }

            ParameterInfo[] parameters =
                method.GetParameters();
            return parameters.Length == 3 &&
                parameters[0].ParameterType ==
                    typeof(Room) &&
                parameters[1].ParameterType ==
                    typeof(Vector2i) &&
                parameters[2].ParameterType ==
                    typeof(bool).MakeByRefType() &&
                parameters[2].IsOut;
        }

        private static void RebuildSnapshot()
        {
            Provider[] snapshot =
                new Provider[
                    ProvidersBySource.Count];
            ProvidersBySource.Values.CopyTo(
                snapshot,
                0);
            s_snapshot = snapshot;
        }
    }
}
