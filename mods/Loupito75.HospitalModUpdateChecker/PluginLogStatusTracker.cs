using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;

namespace HospitalModUpdateChecker
{
    internal enum PluginLogStatus
    {
        Loaded,
        Warning,
        Error
    }

    internal static class PluginLogStatusTracker
    {
        private sealed class StatusListener : ILogListener
        {
            public void LogEvent(
                object sender,
                LogEventArgs eventArgs)
            {
                if (eventArgs == null ||
                    eventArgs.Source == null)
                {
                    return;
                }

                Record(
                    eventArgs.Source.SourceName,
                    eventArgs.Level);
            }

            public void Dispose()
            {
            }
        }

        private static readonly object SyncRoot =
            new object();

        private static readonly Dictionary<string, PluginLogStatus>
            SourceStatuses =
                new Dictionary<string, PluginLogStatus>(
                    StringComparer.OrdinalIgnoreCase);

        private static StatusListener _listener;
        private static bool _changed;

        internal static void Start()
        {
            lock (SyncRoot)
            {
                if (_listener != null)
                {
                    return;
                }

                _listener = new StatusListener();
                BepInEx.Logging.Logger.Listeners.Add(_listener);
            }
        }

        internal static void Stop()
        {
            StatusListener listener = null;

            lock (SyncRoot)
            {
                if (_listener == null)
                {
                    return;
                }

                listener = _listener;
                _listener = null;
            }

            BepInEx.Logging.Logger.Listeners.Remove(listener);
            listener.Dispose();
        }

        internal static void LoadExistingLog()
        {
            string rootPath = Paths.BepInExRootPath;

            if (string.IsNullOrEmpty(rootPath))
            {
                return;
            }

            FlushDiskListeners();

            string path =
                FindCurrentLogPath(rootPath);

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                List<string> lines =
                    new List<string>();

                using (FileStream stream =
                    new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite))
                using (StreamReader reader =
                    new StreamReader(stream))
                {
                    string line;

                    while ((line = reader.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }
                }

                int startIndex = 0;

                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    if (IsChainloaderReadyMarker(lines[i]))
                    {
                        startIndex = i;
                        break;
                    }
                }

                for (int i = startIndex; i < lines.Count; i++)
                {
                    ParseLogLine(lines[i]);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void FlushDiskListeners()
        {
            foreach (ILogListener listener in
                BepInEx.Logging.Logger.Listeners)
            {
                DiskLogListener disk =
                    listener as DiskLogListener;

                if (disk == null ||
                    disk.LogWriter == null)
                {
                    continue;
                }

                try
                {
                    disk.LogWriter.Flush();
                }
                catch (IOException)
                {
                }
            }
        }

        private static string FindCurrentLogPath(
            string rootPath)
        {
            try
            {
                string[] files =
                    Directory.GetFiles(
                        rootPath,
                        "LogOutput.log*");

                string newestPath = null;
                DateTime newestWriteUtc =
                    DateTime.MinValue;

                for (int i = 0; i < files.Length; i++)
                {
                    DateTime writeUtc =
                        File.GetLastWriteTimeUtc(files[i]);

                    if (newestPath == null ||
                        writeUtc > newestWriteUtc)
                    {
                        newestPath = files[i];
                        newestWriteUtc = writeUtc;
                    }
                }

                return newestPath;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool IsChainloaderReadyMarker(
            string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            int separator = line.IndexOf(':');
            int closingBracket = line.IndexOf(']');

            if (separator <= 1 ||
                closingBracket <= separator)
            {
                return false;
            }

            string levelText =
                line.Substring(
                    1,
                    separator - 1)
                    .Trim();

            string sourceName =
                line.Substring(
                    separator + 1,
                    closingBracket - separator - 1)
                    .Trim();

            string message =
                line.Substring(closingBracket + 1)
                    .Trim();

            return string.Equals(
                    levelText,
                    "Message",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    sourceName,
                    "BepInEx",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    message,
                    "Chainloader ready",
                    StringComparison.Ordinal);
        }

        internal static PluginLogStatus GetStatus(
            PluginInfo plugin)
        {
            if (plugin == null ||
                plugin.Metadata == null)
            {
                return PluginLogStatus.Loaded;
            }

            PluginLogStatus status =
                PluginLogStatus.Loaded;

            MergeStatus(
                ref status,
                GetSourceStatus(plugin.Metadata.Name));

            MergeStatus(
                ref status,
                GetSourceStatus(plugin.Metadata.GUID));

            string guid = plugin.Metadata.GUID;

            if (!string.IsNullOrEmpty(guid) &&
                guid.StartsWith(
                    "loupito75.",
                    StringComparison.OrdinalIgnoreCase))
            {
                string technicalName =
                    guid.Substring("loupito75.".Length);

                MergeStatus(
                    ref status,
                    GetSourceStatus(
                        "Loupito75:" + technicalName));
            }

            return status;
        }

        internal static bool ConsumeChanged()
        {
            lock (SyncRoot)
            {
                bool changed = _changed;
                _changed = false;
                return changed;
            }
        }

        private static void ParseLogLine(string line)
        {
            if (string.IsNullOrEmpty(line) ||
                line[0] != '[')
            {
                return;
            }

            int separator = line.IndexOf(':');
            int closingBracket = line.IndexOf(']');

            if (separator <= 1 ||
                closingBracket <= separator)
            {
                return;
            }

            string levelText =
                line.Substring(
                    1,
                    separator - 1)
                    .Trim();

            LogLevel level;

            if (string.Equals(
                    levelText,
                    "Fatal",
                    StringComparison.OrdinalIgnoreCase))
            {
                level = LogLevel.Fatal;
            }
            else if (string.Equals(
                    levelText,
                    "Error",
                    StringComparison.OrdinalIgnoreCase))
            {
                level = LogLevel.Error;
            }
            else if (string.Equals(
                    levelText,
                    "Warning",
                    StringComparison.OrdinalIgnoreCase))
            {
                level = LogLevel.Warning;
            }
            else
            {
                return;
            }

            string sourceName =
                line.Substring(
                    separator + 1,
                    closingBracket - separator - 1)
                    .Trim();

            Record(sourceName, level);
        }

        private static void Record(
            string sourceName,
            LogLevel level)
        {
            PluginLogStatus newStatus;

            if ((level &
                (LogLevel.Fatal | LogLevel.Error)) != 0)
            {
                newStatus = PluginLogStatus.Error;
            }
            else if ((level & LogLevel.Warning) != 0)
            {
                newStatus = PluginLogStatus.Warning;
            }
            else
            {
                return;
            }

            string normalized =
                NormalizeSourceName(sourceName);

            if (normalized.Length == 0)
            {
                return;
            }

            lock (SyncRoot)
            {
                PluginLogStatus current;

                if (!SourceStatuses.TryGetValue(
                        normalized,
                        out current) ||
                    newStatus > current)
                {
                    SourceStatuses[normalized] = newStatus;
                    _changed = true;
                }
            }
        }

        private static PluginLogStatus GetSourceStatus(
            string sourceName)
        {
            string normalized =
                NormalizeSourceName(sourceName);

            if (normalized.Length == 0)
            {
                return PluginLogStatus.Loaded;
            }

            lock (SyncRoot)
            {
                PluginLogStatus status;

                return SourceStatuses.TryGetValue(
                    normalized,
                    out status)
                    ? status
                    : PluginLogStatus.Loaded;
            }
        }

        private static string NormalizeSourceName(
            string sourceName)
        {
            return string.IsNullOrEmpty(sourceName)
                ? string.Empty
                : sourceName.Trim();
        }

        private static void MergeStatus(
            ref PluginLogStatus target,
            PluginLogStatus candidate)
        {
            if (candidate > target)
            {
                target = candidate;
            }
        }
    }
}
