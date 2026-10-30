using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
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

    internal sealed class PluginLogIssue
    {
        internal long Sequence;
        internal PluginLogStatus Status;
        internal string Source;
        internal string Message;
    }

    internal static class PluginLogStatusTracker
    {
        private const int MaximumIssuesPerSourceLevel = 20;
        private const int MaximumIssuesPerPlugin = 60;
        private const int MaximumMessageCharacters = 4000;

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
                    eventArgs.Level,
                    eventArgs.Data == null
                        ? string.Empty
                        : eventArgs.Data.ToString());
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

        private static readonly Dictionary<string, List<PluginLogIssue>>
            SourceIssues =
                new Dictionary<string, List<PluginLogIssue>>(
                    StringComparer.OrdinalIgnoreCase);

        private static StatusListener _listener;
        private static long _nextSequence;

        // Written under SyncRoot by log listeners (including worker threads).
        // Volatile allows Update() to skip the lock on frames with no events.
        private static volatile bool _changed;

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
                        if (IsChainloaderReadyMarker(line))
                        {
                            lines.Clear();
                        }

                        lines.Add(line);
                    }
                }

                ParseLogLines(
                    lines,
                    0);
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

        internal static List<PluginLogIssue> GetIssues(
            PluginInfo plugin)
        {
            List<PluginLogIssue> result =
                new List<PluginLogIssue>();

            if (plugin == null ||
                plugin.Metadata == null)
            {
                return result;
            }

            List<string> sources =
                new List<string>();

            AddSourceAlias(
                sources,
                plugin.Metadata.Name);

            AddSourceAlias(
                sources,
                plugin.Metadata.GUID);

            string guid =
                plugin.Metadata.GUID;

            if (!string.IsNullOrEmpty(guid) &&
                guid.StartsWith(
                    "loupito75.",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSourceAlias(
                    sources,
                    "Loupito75:" +
                    guid.Substring(
                        "loupito75.".Length));
            }

            lock (SyncRoot)
            {
                for (int i = 0;
                    i < sources.Count;
                    i++)
                {
                    List<PluginLogIssue> issues;

                    if (!SourceIssues.TryGetValue(
                            sources[i],
                            out issues) ||
                        issues == null)
                    {
                        continue;
                    }

                    for (int j = 0;
                        j < issues.Count;
                        j++)
                    {
                        PluginLogIssue issue =
                            issues[j];

                        if (issue == null)
                        {
                            continue;
                        }

                        result.Add(
                            new PluginLogIssue
                            {
                                Sequence =
                                    issue.Sequence,
                                Status =
                                    issue.Status,
                                Source =
                                    issue.Source,
                                Message =
                                    issue.Message
                            });
                    }
                }
            }

            result.Sort(
                delegate(
                    PluginLogIssue left,
                    PluginLogIssue right)
                {
                    long leftSequence =
                        left == null
                            ? long.MinValue
                            : left.Sequence;

                    long rightSequence =
                        right == null
                            ? long.MinValue
                            : right.Sequence;

                    return leftSequence.CompareTo(
                        rightSequence);
                });

            if (result.Count >
                MaximumIssuesPerPlugin)
            {
                result.RemoveRange(
                    0,
                    result.Count -
                    MaximumIssuesPerPlugin);
            }

            return result;
        }

        internal static bool ConsumeChanged()
        {
            // Called every frame. Logging state changes are rare; avoid a
            // monitor acquisition in the normal unchanged case. The lock is
            // still mandatory when consuming the shared flag.
            if (!_changed)
                return false;

            lock (SyncRoot)
            {
                bool changed = _changed;
                _changed = false;
                return changed;
            }
        }

        private static void ParseLogLines(
            IList<string> lines,
            int startIndex)
        {
            if (lines == null)
            {
                return;
            }

            for (int i = Math.Max(
                    0,
                    startIndex);
                i < lines.Count;
                i++)
            {
                string sourceName;
                LogLevel level;
                string firstMessage;

                if (!TryParseTrackedLogLine(
                        lines[i],
                        out sourceName,
                        out level,
                        out firstMessage))
                {
                    continue;
                }

                StringBuilder message =
                    new StringBuilder(
                        firstMessage ??
                        string.Empty);

                int next =
                    i + 1;

                while (next <
                    lines.Count &&
                    !LooksLikeLogEventLine(
                        lines[next]))
                {
                    string continuation =
                        lines[next] ??
                        string.Empty;

                    if (message.Length > 0)
                    {
                        message.Append(
                            "\n");
                    }

                    message.Append(
                        continuation);

                    next++;
                }

                Record(
                    sourceName,
                    level,
                    message.ToString());

                i =
                    next - 1;
            }
        }

        private static bool TryParseTrackedLogLine(
            string line,
            out string sourceName,
            out LogLevel level,
            out string message)
        {
            sourceName =
                string.Empty;

            level =
                LogLevel.None;

            message =
                string.Empty;

            if (string.IsNullOrEmpty(line) ||
                line[0] != '[')
            {
                return false;
            }

            int separator =
                line.IndexOf(':');

            int closingBracket =
                line.IndexOf(']');

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

            if (string.Equals(
                    levelText,
                    "Fatal",
                    StringComparison.OrdinalIgnoreCase))
            {
                level =
                    LogLevel.Fatal;
            }
            else if (string.Equals(
                    levelText,
                    "Error",
                    StringComparison.OrdinalIgnoreCase))
            {
                level =
                    LogLevel.Error;
            }
            else if (string.Equals(
                    levelText,
                    "Warning",
                    StringComparison.OrdinalIgnoreCase))
            {
                level =
                    LogLevel.Warning;
            }
            else if (string.Equals(
                    levelText,
                    "Info",
                    StringComparison.OrdinalIgnoreCase))
            {
                level =
                    LogLevel.Info;
            }
            else if (string.Equals(
                    levelText,
                    "Message",
                    StringComparison.OrdinalIgnoreCase))
            {
                level =
                    LogLevel.Message;
            }
            else
            {
                return false;
            }

            sourceName =
                line.Substring(
                    separator + 1,
                    closingBracket -
                        separator - 1)
                    .Trim();

            message =
                line.Substring(
                    closingBracket + 1)
                    .Trim();

            return true;
        }

        private static bool LooksLikeLogEventLine(
            string line)
        {
            if (string.IsNullOrEmpty(line) ||
                line[0] != '[')
            {
                return false;
            }

            int separator =
                line.IndexOf(':');

            int closingBracket =
                line.IndexOf(']');

            return separator > 1 &&
                closingBracket >
                    separator;
        }

        private static void Record(
            string sourceName,
            LogLevel level,
            string message)
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
            else if ((level &
                (LogLevel.Info | LogLevel.Message)) != 0)
            {
                // PluginLogStatus.Loaded is also used as the neutral
                // Info/Message bucket for per-plugin log viewing. It never
                // downgrades the displayed plugin status.
                newStatus = PluginLogStatus.Loaded;
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

            string normalizedMessage =
                NormalizeMessage(message);

            lock (SyncRoot)
            {
                bool changed = false;
                PluginLogStatus current;

                if (newStatus !=
                        PluginLogStatus.Loaded &&
                    (!SourceStatuses.TryGetValue(
                            normalized,
                            out current) ||
                     newStatus > current))
                {
                    SourceStatuses[normalized] =
                        newStatus;

                    changed = true;
                }

                if (normalizedMessage.Length > 0)
                {
                    List<PluginLogIssue> issues;

                    if (!SourceIssues.TryGetValue(
                            normalized,
                            out issues) ||
                        issues == null)
                    {
                        issues =
                            new List<PluginLogIssue>();

                        SourceIssues[normalized] =
                            issues;
                    }

                    bool duplicate =
                        false;

                    for (int i = issues.Count - 1;
                        i >= 0;
                        i--)
                    {
                        PluginLogIssue issue =
                            issues[i];

                        if (issue != null &&
                            issue.Status ==
                                newStatus &&
                            string.Equals(
                                issue.Message,
                                normalizedMessage,
                                StringComparison.Ordinal))
                        {
                            duplicate =
                                true;
                            break;
                        }
                    }

                    if (!duplicate)
                    {
                        _nextSequence++;

                        issues.Add(
                            new PluginLogIssue
                            {
                                Sequence =
                                    _nextSequence,
                                Status =
                                    newStatus,
                                Source =
                                    sourceName == null
                                        ? normalized
                                        : sourceName.Trim(),
                                Message =
                                    normalizedMessage
                            });

                        int sameLevelCount =
                            0;

                        for (int i =
                                issues.Count - 1;
                            i >= 0;
                            i--)
                        {
                            PluginLogIssue candidate =
                                issues[i];

                            if (candidate == null ||
                                candidate.Status !=
                                    newStatus)
                            {
                                continue;
                            }

                            sameLevelCount++;

                            if (sameLevelCount >
                                MaximumIssuesPerSourceLevel)
                            {
                                issues.RemoveAt(i);
                            }
                        }

                        if (newStatus !=
                            PluginLogStatus.Loaded)
                        {
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    _changed = true;
                }
            }
        }

        private static void AddSourceAlias(
            List<string> aliases,
            string sourceName)
        {
            string normalized =
                NormalizeSourceName(
                    sourceName);

            if (normalized.Length == 0)
            {
                return;
            }

            for (int i = 0;
                i < aliases.Count;
                i++)
            {
                if (string.Equals(
                        aliases[i],
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            aliases.Add(
                normalized);
        }

        private static string NormalizeMessage(
            string message)
        {
            if (string.IsNullOrEmpty(
                    message))
            {
                return string.Empty;
            }

            string normalized =
                message
                .Replace(
                    "\r\n",
                    "\n")
                .Replace(
                    '\r',
                    '\n')
                .Trim();

            StringBuilder builder =
                new StringBuilder(
                    Math.Min(
                        normalized.Length,
                        MaximumMessageCharacters));

            for (int i = 0;
                i < normalized.Length &&
                builder.Length <
                    MaximumMessageCharacters;
                i++)
            {
                char c =
                    normalized[i];

                if (c == '\n' ||
                    c == '\t')
                {
                    builder.Append(c);
                    continue;
                }

                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 <
                            normalized.Length &&
                        char.IsLowSurrogate(
                            normalized[i + 1]) &&
                        builder.Length + 2 <=
                            MaximumMessageCharacters)
                    {
                        builder.Append(c);
                        builder.Append(
                            normalized[++i]);
                    }

                    continue;
                }

                if (char.IsLowSurrogate(c) ||
                    char.IsControl(c) ||
                    char.GetUnicodeCategory(c) ==
                        UnicodeCategory.Format)
                {
                    continue;
                }

                builder.Append(c);
            }

            return builder
                .ToString()
                .Trim();
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
