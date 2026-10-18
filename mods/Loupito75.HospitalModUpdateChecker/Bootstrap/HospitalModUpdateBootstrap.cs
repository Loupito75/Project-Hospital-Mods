using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace HospitalModUpdateChecker.Bootstrap
{
    public static class HospitalModUpdateBootstrapCore
    {
        public static void Run()
        {
            BootstrapInstaller.Run();
        }
    }

    internal static class BootstrapInstaller
    {
        private const string CacheFolderName =
            "Loupito75.HospitalModUpdateChecker";
        private const string PendingFileName =
            "pending-install.xml";
        private const string RecoveryFileName =
            "install-recovery.xml";
        private const string ResultsFileName =
            "install-results.xml";

        private static ManualLogSource _log;

        internal static void Run()
        {
            _log = Logger.CreateLogSource(
                "   Loupito75:HospitalModUpdateCheckerBootstrap");

            try
            {
                string cacheRoot =
                    Path.Combine(
                        Paths.CachePath,
                        CacheFolderName);

                Directory.CreateDirectory(cacheRoot);

                RecoverAtomicFile(
                    Path.Combine(
                        cacheRoot,
                        RecoveryFileName));

                RecoverAtomicFile(
                    Path.Combine(
                        cacheRoot,
                        PendingFileName));

                RecoverAtomicFile(
                    Path.Combine(
                        cacheRoot,
                        ResultsFileName));

                RecoverInterruptedInstall(cacheRoot);
                // Recover any incomplete XML swap before package updates.
                PendingConfigApplier.Recover();
                ApplyPendingOperations(cacheRoot);
                // Never apply settings while a package rollback is unresolved.
                if (!File.Exists(Path.Combine(cacheRoot, RecoveryFileName)) &&
                    !File.Exists(Path.Combine(cacheRoot, PendingFileName)))
                    PendingConfigApplier.Apply();
            }
            catch (Exception exception)
            {
                _log.LogError(
                    "HMUC bootstrap failed safely before plugin loading: " +
                    exception);
            }
            finally
            {
                if (_log != null)
                {
                    Logger.Sources.Remove(_log);
                    _log = null;
                }
            }
        }

        private static void RecoverAtomicFile(
            string path)
        {
            string previous =
                path + ".previous";

            string temporary =
                path + ".tmp";

            try
            {
                if (!File.Exists(path) &&
                    File.Exists(previous))
                {
                    File.Move(
                        previous,
                        path);
                }
                else if (File.Exists(path) &&
                    File.Exists(previous))
                {
                    File.Delete(previous);
                }

                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception exception)
            {
                _log.LogWarning(
                    "HMUC could not normalize an interrupted metadata write for " +
                    Path.GetFileName(path) +
                    ": " +
                    exception.Message);
            }
        }

        private static void CommitAtomicFile(
            string temporary,
            string path)
        {
            string previous =
                path + ".previous";

            if (File.Exists(previous))
            {
                File.Delete(previous);
            }

            if (File.Exists(path))
            {
                File.Move(
                    path,
                    previous);
            }

            try
            {
                File.Move(
                    temporary,
                    path);

                if (File.Exists(previous))
                {
                    File.Delete(previous);
                }
            }
            catch
            {
                if (!File.Exists(path) &&
                    File.Exists(previous))
                {
                    File.Move(
                        previous,
                        path);
                }

                throw;
            }
        }

        internal static void LogWarning(
            string message)
        {
            if (_log != null)
            {
                _log.LogWarning(message);
            }
        }

        private static string GetCanonicalPluginRoot()
        {
            return Path.GetFullPath(
                Path.Combine(
                    Paths.BepInExRootPath,
                    "plugins"));
        }

        private static void RecoverInterruptedInstall(
            string cacheRoot)
        {
            string recoveryPath =
                Path.Combine(
                    cacheRoot,
                    RecoveryFileName);

            if (!File.Exists(recoveryPath))
            {
                return;
            }

            RecoveryRecord record;

            try
            {
                record = RecoveryRecord.Load(
                    recoveryPath,
                    cacheRoot);
            }
            catch (Exception exception)
            {
                _log.LogError(
                    "HMUC found an unreadable recovery record; no files were changed: " +
                    exception.Message);
                return;
            }

            string target =
                Path.Combine(
                    GetCanonicalPluginRoot(),
                    record.InstallDirectory);

            try
            {
                if (string.Equals(
                        record.Phase,
                        "applied",
                        StringComparison.Ordinal))
                {
                    if (Directory.Exists(target))
                    {
                        InstallResultWriter.Append(
                            cacheRoot,
                            InstallResult.Success(
                                record.Guid,
                                record.Name,
                                record.Version,
                                record.AddedSettings,
                                record.ConfigWarning));

                        try
                        {
                            File.Delete(recoveryPath);
                        }
                        catch
                        {
                        }

                        DeleteDirectorySafe(record.BackupDirectory);
                        DeleteDirectorySafe(record.CandidateDirectory);

                        return;
                    }
                }

                if (string.Equals(
                        record.Phase,
                        "candidate-ready",
                        StringComparison.Ordinal))
                {
                    DeleteDirectorySafe(
                        record.CandidateDirectory);
                }
                else if (string.Equals(
                        record.Phase,
                        "install-ready",
                        StringComparison.Ordinal))
                {
                    // The new installation has no previous folder to restore.
                    // If its target appears unexpectedly, retain it and the
                    // recovery record for manual inspection.
                    if (Directory.Exists(target) || File.Exists(target))
                        throw new IOException(
                            "Interrupted initial install needs manual inspection.");
                    DeleteDirectorySafe(record.CandidateDirectory);
                }
                else if (string.Equals(
                        record.Phase,
                        "backup-moved",
                        StringComparison.Ordinal))
                {
                    if (!Directory.Exists(
                            record.BackupDirectory))
                    {
                        throw new IOException(
                            "Recovery backup is missing after the original installation was moved.");
                    }

                    if (Directory.Exists(target))
                    {
                        DeleteDirectorySafe(target);
                    }

                    Directory.Move(
                        record.BackupDirectory,
                        target);

                    DeleteDirectorySafe(
                        record.CandidateDirectory);
                }
                else if (string.Equals(
                        record.Phase,
                        "swapped",
                        StringComparison.Ordinal))
                {
                    if (Directory.Exists(
                            record.BackupDirectory))
                    {
                        if (Directory.Exists(target))
                        {
                            DeleteDirectorySafe(target);
                        }

                        Directory.Move(
                            record.BackupDirectory,
                            target);
                    }
                    else if (Directory.Exists(target))
                    {
                        DeleteDirectorySafe(target);
                    }

                    DeleteDirectorySafe(
                        record.CandidateDirectory);
                }
                else
                {
                    throw new FormatException(
                        "Unknown HMUC recovery phase: " +
                        record.Phase);
                }

                File.Delete(recoveryPath);

                InstallResultWriter.Append(
                    cacheRoot,
                    InstallResult.Failure(
                        record.Guid,
                        record.Name,
                        record.Version));

                _log.LogWarning(
                    "HMUC restored the previous installation after an interrupted update for " +
                    record.Name + ".");
            }
            catch (Exception exception)
            {
                _log.LogError(
                    "HMUC could not finish recovery for " +
                    record.Name +
                    "; the backup was left intact: " +
                    exception);
            }
        }

        private static void ApplyPendingOperations(
            string cacheRoot)
        {
            string pendingPath =
                Path.Combine(
                    cacheRoot,
                    PendingFileName);

            if (!File.Exists(pendingPath))
            {
                return;
            }

            List<InstallOperation> operations;

            try
            {
                operations =
                    InstallOperation.LoadAll(
                        pendingPath,
                        cacheRoot);
            }
            catch (Exception exception)
            {
                _log.LogError(
                    "HMUC pending update list was rejected; no files were changed: " +
                    exception);
                return;
            }

            for (int i = 0; i < operations.Count; i++)
            {
                InstallOperation operation =
                    operations[i];

                if (!ApplyOperation(
                        cacheRoot,
                        operation))
                {
                    _log.LogWarning(
                        "HMUC stopped the update queue because recovery is still pending for " +
                        operation.Name +
                        ".");
                    return;
                }

                if (!RemovePendingOperation(
                        pendingPath,
                        operation.Guid))
                {
                    _log.LogWarning(
                        "HMUC kept the staged package because the pending-update queue could not be advanced safely.");
                    return;
                }

                try
                {
                    DeleteDirectorySafe(
                        operation.StagingDirectory);
                }
                catch (Exception cleanupException)
                {
                    _log.LogWarning(
                        "HMUC could not remove processed staging data for " +
                        operation.Name +
                        ": " +
                        cleanupException.Message);
                }
            }
        }

        private static bool RemovePendingOperation(
            string pendingPath,
            string guid)
        {
            try
            {
                XmlDocument document =
                    new XmlDocument();

                document.XmlResolver = null;
                document.Load(pendingPath);

                if (document.DocumentType != null)
                {
                    throw new FormatException(
                        "DTD declarations are not allowed.");
                }

                XmlElement root =
                    document.DocumentElement;

                if (root == null ||
                    root.Name != "HospitalModUpdateTransaction" ||
                    root.GetAttribute("formatVersion") != "1")
                {
                    throw new FormatException(
                        "Unsupported pending update format.");
                }

                XmlElement match = null;
                int remaining = 0;

                for (int i = 0;
                    i < root.ChildNodes.Count;
                    i++)
                {
                    XmlElement operation =
                        root.ChildNodes[i] as XmlElement;

                    if (operation == null)
                    {
                        continue;
                    }

                    if (operation.Name != "Operation")
                    {
                        throw new FormatException(
                            "Unknown pending update element.");
                    }

                    if (match == null &&
                        string.Equals(
                            operation.GetAttribute("guid"),
                            guid,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        match = operation;
                    }
                    else
                    {
                        remaining++;
                    }
                }

                if (match == null)
                {
                    throw new InvalidOperationException(
                        "Processed operation is missing from the pending-update queue.");
                }

                root.RemoveChild(match);

                if (remaining == 0)
                {
                    File.Delete(pendingPath);
                    return true;
                }

                string temporary =
                    pendingPath + ".tmp";

                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }

                XmlWriterSettings settings =
                    new XmlWriterSettings();

                settings.Encoding =
                    new System.Text.UTF8Encoding(false);
                settings.Indent = true;

                using (XmlWriter writer =
                    XmlWriter.Create(
                        temporary,
                        settings))
                {
                    document.Save(writer);
                }

                CommitAtomicFile(
                    temporary,
                    pendingPath);

                return true;
            }
            catch (Exception exception)
            {
                _log.LogError(
                    "HMUC could not advance the pending-update queue: " +
                    exception);
                return false;
            }
        }

        private static bool ApplyOperation(
            string cacheRoot,
            InstallOperation operation)
        {
            string transactionId =
                DateTime.UtcNow.Ticks.ToString() +
                "-" +
                MakeSafeToken(operation.Guid);

            string workRoot =
                Path.Combine(
                    cacheRoot,
                    "apply");

            string backupRoot =
                Path.Combine(
                    cacheRoot,
                    "backup");

            Directory.CreateDirectory(workRoot);
            Directory.CreateDirectory(backupRoot);

            string candidate =
                Path.Combine(
                    workRoot,
                    transactionId);

            string backup =
                Path.Combine(
                    backupRoot,
                    transactionId);

            string target =
                Path.Combine(
                    GetCanonicalPluginRoot(),
                    operation.InstallDirectory);

            string recoveryPath =
                Path.Combine(
                    cacheRoot,
                    RecoveryFileName);

            List<string> addedSettings =
                new List<string>();

            string configWarning = string.Empty;
            bool targetMoved = false;
            bool candidateMovedToTarget = false;

            try
            {
                if (operation.FreshInstall &&
                    (Directory.Exists(target) || File.Exists(target)))
                    throw new IOException("Fresh install destination already exists.");

                DeleteDirectorySafe(candidate);
                DeleteDirectorySafe(backup);

                if (Directory.Exists(target))
                {
                    CopyDirectory(
                        target,
                        candidate);
                }
                else
                {
                    Directory.CreateDirectory(candidate);
                }

                for (int i = 0;
                    i < operation.RemoveFiles.Count;
                    i++)
                {
                    string obsolete =
                        GetSafeChildPath(
                            candidate,
                            operation.RemoveFiles[i]);

                    if (obsolete == null)
                    {
                        throw new FormatException(
                            "Unsafe obsolete runtime file path.");
                    }

                    if (Directory.Exists(obsolete))
                    {
                        throw new FormatException(
                            "removeFiles may only contain explicit files.");
                    }

                    if (File.Exists(obsolete))
                    {
                        File.Delete(obsolete);
                    }
                }

                for (int i = 0;
                    i < operation.RuntimeDirectories.Count;
                    i++)
                {
                    string relative =
                        operation.RuntimeDirectories[i];

                    string source =
                        GetSafeChildPath(
                            operation.StagingDirectory,
                            relative);

                    string destination =
                        GetSafeChildPath(
                            candidate,
                            relative);

                    if (source == null ||
                        destination == null ||
                        !Directory.Exists(source))
                    {
                        throw new IOException(
                            "Required runtime directory is missing: " +
                            relative);
                    }

                    DeleteDirectorySafe(destination);
                    CopyDirectory(source, destination);
                }

                for (int i = 0;
                    i < operation.RuntimeFiles.Count;
                    i++)
                {
                    string relative =
                        operation.RuntimeFiles[i];

                    string source =
                        GetSafeChildPath(
                            operation.StagingDirectory,
                            relative);

                    string destination =
                        GetSafeChildPath(
                            candidate,
                            relative);

                    if (source == null ||
                        destination == null ||
                        !File.Exists(source))
                    {
                        throw new IOException(
                            "Required runtime file is missing: " +
                            relative);
                    }

                    string parent =
                        Path.GetDirectoryName(destination);

                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    File.Copy(
                        source,
                        destination,
                        true);
                }

                MigrateConfig(
                    operation,
                    candidate,
                    addedSettings,
                    out configWarning);

                VerifyCandidate(
                    operation,
                    candidate);

                RecoveryRecord record =
                    new RecoveryRecord(
                        operation.Guid,
                        operation.Name,
                        operation.Version,
                        operation.InstallDirectory,
                        candidate,
                        backup,
                        "candidate-ready",
                        addedSettings,
                        configWarning);

                record.Save(
                    recoveryPath,
                    cacheRoot);

                if (operation.FreshInstall &&
                    (Directory.Exists(target) || File.Exists(target)))
                    throw new IOException("Fresh install target became occupied.");

                if (Directory.Exists(target))
                {
                    Directory.Move(target, backup);
                    targetMoved = true;
                }

                record.Phase = operation.FreshInstall
                    ? "install-ready" : "backup-moved";
                record.Save(
                    recoveryPath,
                    cacheRoot);

                Directory.Move(
                    candidate,
                    target);

                candidateMovedToTarget = true;

                record.Phase = "swapped";
                record.Save(
                    recoveryPath,
                    cacheRoot);

                VerifyCandidate(
                    operation,
                    target);

                record.Phase = "applied";
                record.Save(
                    recoveryPath,
                    cacheRoot);

                InstallResultWriter.Append(
                    cacheRoot,
                    InstallResult.Success(
                        operation.Guid,
                        operation.Name,
                        operation.Version,
                        addedSettings,
                        configWarning));

                try
                {
                    if (File.Exists(recoveryPath))
                    {
                        File.Delete(recoveryPath);
                    }
                }
                catch (Exception cleanupException)
                {
                    _log.LogWarning(
                        "HMUC could not remove a completed recovery marker for " +
                        operation.Name +
                        ": " +
                        cleanupException.Message);
                }

                try
                {
                    DeleteDirectorySafe(backup);
                }
                catch (Exception cleanupException)
                {
                    _log.LogWarning(
                        "HMUC could not remove an obsolete backup for " +
                        operation.Name +
                        ": " +
                        cleanupException.Message);
                }

                return true;
            }
            catch (Exception exception)
            {
                _log.LogError(
                    "HMUC update installation failed for " +
                    operation.Name +
                    ": " +
                    exception);

                try
                {
                    if (targetMoved &&
                        Directory.Exists(backup))
                    {
                        if (candidateMovedToTarget &&
                            Directory.Exists(target))
                        {
                            string failed =
                                candidate + ".failed";

                            DeleteDirectorySafe(failed);
                            Directory.Move(target, failed);
                            DeleteDirectorySafe(failed);
                        }

                        if (!Directory.Exists(target))
                        {
                            Directory.Move(
                                backup,
                                target);
                        }

                        _log.LogWarning(
                            "HMUC restored the previous version of " +
                            operation.Name + ".");
                    }
                    else if (!targetMoved &&
                        candidateMovedToTarget &&
                        Directory.Exists(target))
                    {
                        DeleteDirectorySafe(target);
                    }
                }
                catch (Exception rollbackException)
                {
                    _log.LogError(
                        "HMUC rollback also failed for " +
                        operation.Name +
                        "; backup was preserved at its recovery location: " +
                        rollbackException);
                }

                DeleteDirectorySafe(candidate);

                try
                {
                    if (File.Exists(recoveryPath) &&
                        (!targetMoved ||
                         !Directory.Exists(backup)))
                    {
                        File.Delete(recoveryPath);
                    }
                }
                catch
                {
                }

                InstallResultWriter.Append(
                    cacheRoot,
                    InstallResult.Failure(
                        operation.Guid,
                        operation.Name,
                        operation.Version));

                return !File.Exists(
                    recoveryPath);
            }
        }

        private static void MigrateConfig(
            InstallOperation operation,
            string candidate,
            List<string> addedSettings,
            out string warning)
        {
            warning = string.Empty;

            string template =
                GetSafeChildPath(
                    operation.StagingDirectory,
                    operation.ConfigFile);

            if (template == null ||
                !File.Exists(template))
            {
                warning =
                    "The update package has no configuration template.";
                return;
            }

            string destination =
                GetSafeChildPath(
                    candidate,
                    operation.ConfigFile);

            if (destination == null)
            {
                throw new FormatException(
                    "Unsafe configuration destination.");
            }

            if (!File.Exists(destination))
            {
                File.Copy(
                    template,
                    destination,
                    false);

                XmlDocument created =
                    LoadSafeXml(template);

                CollectElementPaths(
                    created.DocumentElement,
                    string.Empty,
                    addedSettings);

                return;
            }

            try
            {
                XmlDocument current =
                    LoadSafeXml(destination);

                XmlDocument defaults =
                    LoadSafeXml(template);

                if (current.DocumentElement == null ||
                    defaults.DocumentElement == null ||
                    !string.Equals(
                        current.DocumentElement.Name,
                        defaults.DocumentElement.Name,
                        StringComparison.Ordinal))
                {
                    warning =
                        "The existing configuration root does not match the new template; it was left unchanged.";
                    return;
                }

                MergeMissingElements(
                    current,
                    current.DocumentElement,
                    defaults.DocumentElement,
                    string.Empty,
                    addedSettings);

                // Copy new HMUC comments to existing settings without touching
                // user values, existing descriptions or unrelated comments.
                bool metadataAdded = MergeHmucMetadata(
                    current,
                    current.DocumentElement,
                    defaults.DocumentElement);

                if (addedSettings.Count == 0 && !metadataAdded)
                    return;

                string temporary =
                    destination + ".hmuc-new";

                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }

                SaveXml(
                    current,
                    temporary);

                string old =
                    destination + ".hmuc-old";

                if (File.Exists(old))
                {
                    File.Delete(old);
                }

                File.Move(destination, old);

                try
                {
                    File.Move(
                        temporary,
                        destination);

                    File.Delete(old);
                }
                catch
                {
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }

                    if (File.Exists(old))
                    {
                        File.Move(old, destination);
                    }

                    throw;
                }
            }
            catch (Exception exception)
            {
                warning =
                    "Configuration migration was skipped because the existing XML could not be merged safely (" +
                    exception.GetType().Name +
                    ").";

                addedSettings.Clear();

                _log.LogWarning(
                    operation.Name +
                    ": " +
                    warning +
                    " Existing values were not changed.");
            }
        }

        private static void MergeMissingElements(
            XmlDocument owner,
            XmlElement current,
            XmlElement defaults,
            string parentPath,
            List<string> addedSettings)
        {
            Dictionary<string, XmlElement> currentChildren =
                GetUniqueElementChildren(current);

            Dictionary<string, XmlElement> defaultChildren =
                GetUniqueElementChildren(defaults);

            foreach (
                KeyValuePair<string, XmlElement> pair
                in defaultChildren)
            {
                string path =
                    parentPath.Length == 0
                        ? pair.Key
                        : parentPath + "/" + pair.Key;

                XmlElement existing;
                if (!currentChildren.TryGetValue(
                        pair.Key,
                        out existing))
                {
                    AppendImportedElement(
                        owner,
                        current,
                        pair.Value);

                    if (HasElementChildren(pair.Value))
                    {
                        CollectElementPaths(
                            pair.Value,
                            path,
                            addedSettings);
                    }
                    else
                    {
                        addedSettings.Add(
                            FormatAddedSetting(
                                path,
                                pair.Value));
                    }

                    continue;
                }

                if (HasElementChildren(pair.Value))
                {
                    MergeMissingElements(
                        owner,
                        existing,
                        pair.Value,
                        path,
                        addedSettings);
                }
            }
        }

        private static bool MergeHmucMetadata(
            XmlDocument owner, XmlElement current, XmlElement defaults)
        {
            bool changed = false;
            Dictionary<string, XmlElement> currentChildren =
                GetUniqueElementChildren(current);
            Dictionary<string, XmlElement> defaultChildren =
                GetUniqueElementChildren(defaults);

            foreach (KeyValuePair<string, XmlElement> pair in defaultChildren)
            {
                XmlElement existing;
                if (!currentChildren.TryGetValue(pair.Key, out existing))
                    continue; // The missing option was already imported.

                if (HasElementChildren(pair.Value))
                {
                    if (HasElementChildren(existing))
                        changed |= MergeHmucMetadata(owner, existing, pair.Value);
                    continue;
                }

                Dictionary<string, bool> present =
                    new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                XmlNode prev = existing.PreviousSibling;
                while (prev != null && prev.NodeType != XmlNodeType.Element)
                {
                    if (prev.NodeType == XmlNodeType.Comment)
                    {
                        string marker = HmucCommentKind(prev.Value);
                        if (marker != null)
                            present[marker] = true;
                    }
                    prev = prev.PreviousSibling;
                }

                Dictionary<string, XmlComment> existingMetadata =
                    new Dictionary<string, XmlComment>(StringComparer.OrdinalIgnoreCase);
                prev = existing.PreviousSibling;
                while (prev != null && prev.NodeType != XmlNodeType.Element)
                {
                    XmlComment comment = prev as XmlComment;
                    if (comment != null)
                    {
                        string marker = HmucCommentKind(comment.Value);
                        if (marker != null && !existingMetadata.ContainsKey(marker))
                            existingMetadata.Add(marker, comment);
                    }
                    prev = prev.PreviousSibling;
                }

                List<XmlNode> toAdd = new List<XmlNode>();
                prev = pair.Value.PreviousSibling;
                while (prev != null && prev.NodeType != XmlNodeType.Element)
                {
                    XmlComment comment = prev as XmlComment;
                    if (comment != null)
                    {
                        string marker = HmucCommentKind(comment.Value);
                        if (marker != null)
                        {
                            XmlComment oldComment;
                            if (existingMetadata.TryGetValue(marker, out oldComment))
                            {
                                if (oldComment.Value != comment.Value)
                                {
                                    oldComment.Value = comment.Value;
                                    changed = true;
                                }
                            }
                            else if (!present.ContainsKey(marker))
                            {
                                toAdd.Insert(0, comment);
                                present[marker] = true;
                            }
                        }
                    }
                    prev = prev.PreviousSibling;
                }

                XmlNode indentation = existing.PreviousSibling;
                if (indentation != null &&
                    !IsWhitespaceNode(indentation))
                    indentation = FindChildIndentationWhitespace(current);
                foreach (XmlNode comment in toAdd)
                {
                    current.InsertBefore(
                        owner.ImportNode(comment, true), existing);
                    if (indentation != null)
                        current.InsertBefore(
                            owner.ImportNode(indentation, true), existing);
                    changed = true;
                }
            }
            return changed;
        }

        private static string HmucCommentKind(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.StartsWith("@HMUC:set", StringComparison.OrdinalIgnoreCase))
                return "set";
            if (text.StartsWith("@HMUC:desc", StringComparison.OrdinalIgnoreCase))
                return "desc";
            return null;
        }

        private static void AppendImportedElement(
            XmlDocument owner,
            XmlElement current,
            XmlElement source)
        {
            XmlNode insertionPoint = null;

            for (int i =
                    current.ChildNodes.Count - 1;
                i >= 0;
                i--)
            {
                XmlNode node =
                    current.ChildNodes[i];

                if (!IsWhitespaceNode(node))
                {
                    break;
                }

                insertionPoint = node;
            }

            XmlNode indentation =
                FindChildIndentationWhitespace(
                    current);

            if (indentation == null &&
                source.PreviousSibling != null &&
                IsWhitespaceNode(
                    source.PreviousSibling))
            {
                indentation =
                    source.PreviousSibling;
            }

            if (indentation != null)
            {
                XmlNode importedIndentation =
                    owner.ImportNode(
                        indentation,
                        true);

                if (insertionPoint != null)
                {
                    current.InsertBefore(
                        importedIndentation,
                        insertionPoint);
                }
                else
                {
                    current.AppendChild(
                        importedIndentation);
                }
            }

            XmlNode imported =
                owner.ImportNode(
                    source,
                    true);

            if (insertionPoint != null)
            {
                current.InsertBefore(
                    imported,
                    insertionPoint);
            }
            else
            {
                current.AppendChild(imported);

                XmlNode sourceClosingWhitespace =
                    source.ParentNode == null
                        ? null
                        : source.ParentNode.LastChild;

                if (sourceClosingWhitespace != null &&
                    IsWhitespaceNode(
                        sourceClosingWhitespace))
                {
                    current.AppendChild(
                        owner.ImportNode(
                            sourceClosingWhitespace,
                            true));
                }
            }
        }

        private static XmlNode
            FindChildIndentationWhitespace(
                XmlElement parent)
        {
            for (int i = 0;
                i < parent.ChildNodes.Count;
                i++)
            {
                XmlElement child =
                    parent.ChildNodes[i]
                    as XmlElement;

                if (child == null ||
                    child.PreviousSibling == null ||
                    !IsWhitespaceNode(
                        child.PreviousSibling))
                {
                    continue;
                }

                return child.PreviousSibling;
            }

            return null;
        }

        private static bool IsWhitespaceNode(
            XmlNode node)
        {
            return node != null &&
                (node.NodeType ==
                    XmlNodeType.Whitespace ||
                 node.NodeType ==
                    XmlNodeType.SignificantWhitespace);
        }

        private static Dictionary<string, XmlElement>
            GetUniqueElementChildren(
                XmlElement parent)
        {
            Dictionary<string, XmlElement> result =
                new Dictionary<string, XmlElement>(
                    StringComparer.Ordinal);

            for (int i = 0;
                i < parent.ChildNodes.Count;
                i++)
            {
                XmlElement element =
                    parent.ChildNodes[i] as XmlElement;

                if (element == null)
                {
                    continue;
                }

                if (result.ContainsKey(element.Name))
                {
                    throw new FormatException(
                        "Repeated XML elements require an explicit migration: " +
                        element.Name);
                }

                result.Add(
                    element.Name,
                    element);
            }

            return result;
        }

        private static bool HasElementChildren(
            XmlElement element)
        {
            for (int i = 0;
                i < element.ChildNodes.Count;
                i++)
            {
                if (element.ChildNodes[i] is XmlElement)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectElementPaths(
            XmlElement element,
            string parentPath,
            List<string> result)
        {
            if (element == null)
            {
                return;
            }

            Dictionary<string, XmlElement> children =
                GetUniqueElementChildren(element);

            foreach (
                KeyValuePair<string, XmlElement> pair
                in children)
            {
                string path =
                    parentPath.Length == 0
                        ? pair.Key
                        : parentPath + "/" + pair.Key;

                if (HasElementChildren(pair.Value))
                {
                    CollectElementPaths(
                        pair.Value,
                        path,
                        result);
                }
                else
                {
                    result.Add(
                        FormatAddedSetting(
                            path,
                            pair.Value));
                }
            }
        }

        private static string FormatAddedSetting(
            string path,
            XmlElement element)
        {
            string value =
                element == null
                    ? string.Empty
                    : (element.InnerText ?? string.Empty).Trim();

            return value.Length == 0
                ? path
                : path + " = " + value;
        }

        private static XmlDocument LoadSafeXml(
            string path)
        {
            XmlDocument document =
                new XmlDocument();

            document.XmlResolver = null;
            document.PreserveWhitespace = true;
            document.Load(path);

            if (document.DocumentType != null)
            {
                throw new FormatException(
                    "DTD declarations are not allowed.");
            }

            return document;
        }

        private static void SaveXml(
            XmlDocument document,
            string path)
        {
            RemoveDocumentLevelWhitespace(
                document);

            XmlWriterSettings settings =
                new XmlWriterSettings();

            settings.Encoding =
                new System.Text.UTF8Encoding(false);

            settings.Indent = true;
            settings.NewLineChars =
                Environment.NewLine;

            using (XmlWriter writer =
                XmlWriter.Create(path, settings))
            {
                document.Save(writer);
            }
        }

        private static void RemoveDocumentLevelWhitespace(
            XmlDocument document)
        {
            if (document == null)
            {
                return;
            }

            for (int i =
                    document.ChildNodes.Count - 1;
                i >= 0;
                i--)
            {
                XmlNode node =
                    document.ChildNodes[i];

                if (IsWhitespaceNode(node))
                {
                    document.RemoveChild(node);
                }
            }
        }

        private static void VerifyCandidate(
            InstallOperation operation,
            string candidate)
        {
            for (int i = 0;
                i < operation.RuntimeDirectories.Count;
                i++)
            {
                string path =
                    GetSafeChildPath(
                        candidate,
                        operation.RuntimeDirectories[i]);

                if (path == null ||
                    !Directory.Exists(path))
                {
                    throw new IOException(
                        "Installed runtime directory is missing.");
                }
            }

            for (int i = 0;
                i < operation.RuntimeFiles.Count;
                i++)
            {
                string path =
                    GetSafeChildPath(
                        candidate,
                        operation.RuntimeFiles[i]);

                if (path == null ||
                    !File.Exists(path))
                {
                    throw new IOException(
                        "Installed runtime file is missing.");
                }
            }

            string pluginPath =
                GetSafeChildPath(
                    candidate,
                    operation.PluginFile);

            if (pluginPath == null ||
                !File.Exists(pluginPath))
            {
                throw new IOException(
                    "Installed plugin DLL is missing.");
            }

            VerifyPluginIdentity(
                operation,
                pluginPath);
        }

        private static void VerifyPluginIdentity(
            InstallOperation operation,
            string pluginPath)
        {
            Version expectedVersion;

            try
            {
                expectedVersion =
                    new Version(
                        operation.Version);
            }
            catch (Exception exception)
            {
                throw new FormatException(
                    "Pending plugin version is invalid.",
                    exception);
            }

            using (AssemblyDefinition assembly =
                AssemblyDefinition.ReadAssembly(
                    pluginPath))
            {
                if (FindMatchingPluginType(
                        assembly.MainModule.Types,
                        operation.Guid,
                        expectedVersion))
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                "Installed DLL does not declare the expected BepInEx GUID/version.");
        }

        private static bool FindMatchingPluginType(
            IEnumerable<TypeDefinition> types,
            string expectedGuid,
            Version expectedVersion)
        {
            foreach (TypeDefinition type in types)
            {
                for (int i = 0;
                    i < type.CustomAttributes.Count;
                    i++)
                {
                    CustomAttribute attribute =
                        type.CustomAttributes[i];

                    if (attribute == null ||
                        attribute.AttributeType == null ||
                        attribute.AttributeType.FullName !=
                            "BepInEx.BepInPlugin" ||
                        attribute.ConstructorArguments.Count < 3)
                    {
                        continue;
                    }

                    string guid =
                        attribute.ConstructorArguments[0]
                            .Value as string;

                    string versionText =
                        attribute.ConstructorArguments[2]
                            .Value as string;

                    Version version = null;

                    try
                    {
                        if (!string.IsNullOrEmpty(
                                versionText))
                        {
                            version =
                                new Version(
                                    versionText);
                        }
                    }
                    catch
                    {
                        version = null;
                    }

                    if (string.Equals(
                            guid,
                            expectedGuid,
                            StringComparison.OrdinalIgnoreCase) &&
                        version != null &&
                        version.Equals(
                            expectedVersion))
                    {
                        return true;
                    }
                }

                if (type.HasNestedTypes &&
                    FindMatchingPluginType(
                        type.NestedTypes,
                        expectedGuid,
                        expectedVersion))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CopyDirectory(
            string source,
            string destination)
        {
            Directory.CreateDirectory(destination);

            string[] directories =
                Directory.GetDirectories(
                    source,
                    "*",
                    SearchOption.AllDirectories);

            for (int i = 0;
                i < directories.Length;
                i++)
            {
                string relative =
                    directories[i].Substring(
                        source.Length)
                    .TrimStart(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

                Directory.CreateDirectory(
                    Path.Combine(
                        destination,
                        relative));
            }

            string[] files =
                Directory.GetFiles(
                    source,
                    "*",
                    SearchOption.AllDirectories);

            for (int i = 0;
                i < files.Length;
                i++)
            {
                string relative =
                    files[i].Substring(
                        source.Length)
                    .TrimStart(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

                string target =
                    Path.Combine(
                        destination,
                        relative);

                string parent =
                    Path.GetDirectoryName(target);

                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(
                    files[i],
                    target,
                    true);
            }
        }

        private static string GetSafeChildPath(
            string root,
            string relative)
        {
            if (string.IsNullOrEmpty(root) ||
                string.IsNullOrEmpty(relative) ||
                relative.IndexOf(':') >= 0 ||
                relative.IndexOf('\0') >= 0)
            {
                return null;
            }

            string fullRoot =
                Path.GetFullPath(root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            string full =
                Path.GetFullPath(
                    Path.Combine(
                        fullRoot,
                        relative.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));

            if (!full.StartsWith(
                    fullRoot +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return full;
        }

        private static void DeleteDirectorySafe(
            string path)
        {
            if (!string.IsNullOrEmpty(path) &&
                Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private static string MakeSafeToken(
            string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "update";
            }

            char[] chars =
                value.ToCharArray();

            for (int i = 0;
                i < chars.Length;
                i++)
            {
                char c = chars[i];

                if (!(
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') ||
                    c == '.' ||
                    c == '-' ||
                    c == '_'))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }
    }

    internal sealed class InstallOperation
    {
        internal string Guid;
        internal string Name;
        internal string Version;
        internal string InstallDirectory;
        internal string PluginFile;
        internal string ConfigFile;
        internal bool FreshInstall;
        internal string StagingDirectory;
        internal readonly List<string> RuntimeDirectories =
            new List<string>();
        internal readonly List<string> RuntimeFiles =
            new List<string>();
        internal readonly List<string> RemoveFiles =
            new List<string>();

        internal static List<InstallOperation> LoadAll(
            string path,
            string cacheRoot)
        {
            XmlDocument document =
                LoadSafe(path);

            XmlElement root =
                document.DocumentElement;

            if (root == null ||
                root.Name != "HospitalModUpdateTransaction" ||
                root.GetAttribute("formatVersion") != "1")
            {
                throw new FormatException(
                    "Unsupported pending update format.");
            }

            List<InstallOperation> result =
                new List<InstallOperation>();

            for (int i = 0;
                i < root.ChildNodes.Count;
                i++)
            {
                XmlElement element =
                    root.ChildNodes[i] as XmlElement;

                if (element == null)
                {
                    continue;
                }

                if (element.Name != "Operation")
                {
                    throw new FormatException(
                        "Unknown pending update element.");
                }

                InstallOperation operation =
                    new InstallOperation();

                operation.Guid =
                    RequireAttribute(
                        element,
                        "guid");

                operation.Name =
                    RequireAttribute(
                        element,
                        "name");

                operation.Version =
                    RequireAttribute(
                        element,
                        "version");

                operation.InstallDirectory =
                    RequireSafeSegment(
                        element,
                        "installDirectory");

                operation.PluginFile =
                    RequireSafeRelativeAttribute(
                        element,
                        "pluginFile");

                operation.ConfigFile =
                    RequireSafeSegment(
                        element,
                        "configFile");
                string mode = element.GetAttribute("mode");
                if (mode.Length > 0 && mode != "update" && mode != "install")
                    throw new FormatException("Unsupported pending install mode.");
                operation.FreshInstall = mode == "install";

                string stagingRelative =
                    RequireAttribute(
                        element,
                        "stagingDirectory");

                operation.StagingDirectory =
                    GetSafeChild(
                        cacheRoot,
                        stagingRelative);

                if (operation.StagingDirectory == null ||
                    !Directory.Exists(
                        operation.StagingDirectory))
                {
                    throw new DirectoryNotFoundException(
                        "Staged update is missing.");
                }

                ReadPaths(
                    element,
                    "RuntimeDirectories",
                    "Directory",
                    operation.RuntimeDirectories);

                ReadPaths(
                    element,
                    "RuntimeFiles",
                    "File",
                    operation.RuntimeFiles);

                ReadPaths(
                    element,
                    "RemoveFiles",
                    "File",
                    operation.RemoveFiles);

                if (operation.RuntimeFiles.Count == 0)
                {
                    throw new FormatException(
                        "Pending update has no runtime files.");
                }

                bool pluginOwned = false;

                for (int fileIndex = 0;
                    fileIndex < operation.RuntimeFiles.Count;
                    fileIndex++)
                {
                    if (string.Equals(
                            operation.RuntimeFiles[fileIndex],
                            operation.PluginFile,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        pluginOwned = true;
                        break;
                    }
                }

                if (!pluginOwned)
                {
                    throw new FormatException(
                        "Pending pluginFile is not owned by runtimeFiles.");
                }

                for (int removeIndex = 0;
                    removeIndex < operation.RemoveFiles.Count;
                    removeIndex++)
                {
                    if (string.Equals(
                            operation.RemoveFiles[removeIndex],
                            operation.ConfigFile,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new FormatException(
                            "Pending update may not remove the user configuration.");
                    }
                }

                result.Add(operation);
            }

            return result;
        }

        private static XmlDocument LoadSafe(
            string path)
        {
            XmlDocument document =
                new XmlDocument();

            document.XmlResolver = null;
            document.Load(path);

            if (document.DocumentType != null)
            {
                throw new FormatException(
                    "DTD declarations are not allowed.");
            }

            return document;
        }

        private static void ReadPaths(
            XmlElement operation,
            string containerName,
            string itemName,
            List<string> result)
        {
            XmlElement container =
                operation[containerName];

            if (container == null)
            {
                throw new FormatException(
                    "Missing " +
                    containerName +
                    ".");
            }

            for (int i = 0;
                i < container.ChildNodes.Count;
                i++)
            {
                XmlElement item =
                    container.ChildNodes[i] as XmlElement;

                if (item == null)
                {
                    continue;
                }

                if (item.Name != itemName)
                {
                    throw new FormatException(
                        "Invalid " +
                        containerName +
                        " entry.");
                }

                string value =
                    (item.InnerText ?? string.Empty)
                    .Trim();

                if (!IsSafeRelativePath(value))
                {
                    throw new FormatException(
                        "Unsafe pending update path.");
                }

                result.Add(value);
            }
        }

        private static string RequireAttribute(
            XmlElement element,
            string name)
        {
            string value =
                element.GetAttribute(name);

            if (string.IsNullOrEmpty(value) ||
                value.Length > 256)
            {
                throw new FormatException(
                    "Missing pending update attribute: " +
                    name);
            }

            return value;
        }

        private static string RequireSafeRelativeAttribute(
            XmlElement element,
            string name)
        {
            string value =
                RequireAttribute(
                    element,
                    name);

            if (!IsSafeRelativePath(value))
            {
                throw new FormatException(
                    "Unsafe pending update path.");
            }

            return value;
        }

        private static string RequireSafeSegment(
            XmlElement element,
            string name)
        {
            string value =
                RequireAttribute(
                    element,
                    name);

            if (value == "." ||
                value == ".." ||
                value.IndexOf('/') >= 0 ||
                value.IndexOf('\\') >= 0 ||
                value.IndexOf(':') >= 0 ||
                value.IndexOf('\0') >= 0)
            {
                throw new FormatException(
                    "Unsafe pending update segment.");
            }

            return value;
        }

        private static bool IsSafeRelativePath(
            string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value[0] == '/' ||
                value[0] == '\\' ||
                value.IndexOf(':') >= 0 ||
                value.IndexOf('\0') >= 0)
            {
                return false;
            }

            string[] parts =
                value.Replace('\\', '/')
                .Split('/');

            for (int i = 0;
                i < parts.Length;
                i++)
            {
                if (parts[i].Length == 0 ||
                    parts[i] == "." ||
                    parts[i] == "..")
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetSafeChild(
            string root,
            string relative)
        {
            if (!IsSafeRelativePath(relative))
            {
                return null;
            }

            string fullRoot =
                Path.GetFullPath(root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            string full =
                Path.GetFullPath(
                    Path.Combine(
                        fullRoot,
                        relative.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));

            if (!full.StartsWith(
                    fullRoot +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return full;
        }
    }

    internal sealed class RecoveryRecord
    {
        internal string Guid;
        internal string Name;
        internal string Version;
        internal string InstallDirectory;
        internal string CandidateDirectory;
        internal string BackupDirectory;
        internal string Phase;
        internal List<string> AddedSettings;
        internal string ConfigWarning;

        internal RecoveryRecord(
            string guid,
            string name,
            string version,
            string installDirectory,
            string candidateDirectory,
            string backupDirectory,
            string phase,
            List<string> addedSettings,
            string configWarning)
        {
            Guid = guid;
            Name = name;
            Version = version;
            InstallDirectory = installDirectory;
            CandidateDirectory = candidateDirectory;
            BackupDirectory = backupDirectory;
            Phase = phase;
            AddedSettings =
                new List<string>(
                    addedSettings ?? new List<string>());
            ConfigWarning =
                configWarning ?? string.Empty;
        }

        internal void Save(
            string path,
            string cacheRoot)
        {
            XmlDocument document =
                new XmlDocument();

            XmlElement root =
                document.CreateElement(
                    "HospitalModUpdateRecovery");

            root.SetAttribute(
                "formatVersion",
                "1");

            root.SetAttribute(
                "guid",
                Guid);

            root.SetAttribute(
                "name",
                Name);

            root.SetAttribute(
                "version",
                Version);

            root.SetAttribute(
                "installDirectory",
                InstallDirectory);

            root.SetAttribute(
                "candidateDirectory",
                MakeRelative(
                    cacheRoot,
                    CandidateDirectory));

            root.SetAttribute(
                "backupDirectory",
                MakeRelative(
                    cacheRoot,
                    BackupDirectory));

            root.SetAttribute(
                "phase",
                Phase);

            root.SetAttribute(
                "configWarning",
                ConfigWarning ?? string.Empty);

            for (int i = 0;
                i < AddedSettings.Count;
                i++)
            {
                XmlElement setting =
                    document.CreateElement(
                        "AddedSetting");

                setting.InnerText =
                    AddedSettings[i];

                root.AppendChild(setting);
            }

            document.AppendChild(root);

            SaveAtomic(
                document,
                path);
        }

        internal static RecoveryRecord Load(
            string path,
            string cacheRoot)
        {
            XmlDocument document =
                new XmlDocument();

            document.XmlResolver = null;
            document.Load(path);

            if (document.DocumentType != null)
            {
                throw new FormatException(
                    "DTD declarations are not allowed.");
            }

            XmlElement root =
                document.DocumentElement;

            if (root == null ||
                root.Name != "HospitalModUpdateRecovery" ||
                root.GetAttribute("formatVersion") != "1")
            {
                throw new FormatException(
                    "Invalid recovery format.");
            }

            string candidate =
                SafeCachePath(
                    cacheRoot,
                    root.GetAttribute(
                        "candidateDirectory"));

            string backup =
                SafeCachePath(
                    cacheRoot,
                    root.GetAttribute(
                        "backupDirectory"));

            if (candidate == null ||
                backup == null)
            {
                throw new FormatException(
                    "Unsafe recovery path.");
            }

            List<string> settings =
                new List<string>();

            for (int i = 0;
                i < root.ChildNodes.Count;
                i++)
            {
                XmlElement setting =
                    root.ChildNodes[i] as XmlElement;

                if (setting == null)
                {
                    continue;
                }

                if (setting.Name != "AddedSetting")
                {
                    throw new FormatException(
                        "Unknown recovery element.");
                }

                settings.Add(
                    setting.InnerText ?? string.Empty);
            }

            string installDirectory =
                root.GetAttribute(
                    "installDirectory");

            if (string.IsNullOrEmpty(installDirectory) ||
                installDirectory == "." ||
                installDirectory == ".." ||
                installDirectory.IndexOf('/') >= 0 ||
                installDirectory.IndexOf('\\') >= 0 ||
                installDirectory.IndexOf(':') >= 0 ||
                installDirectory.IndexOf('\0') >= 0)
            {
                throw new FormatException(
                    "Unsafe recovery install directory.");
            }

            return new RecoveryRecord(
                root.GetAttribute("guid"),
                root.GetAttribute("name"),
                root.GetAttribute("version"),
                installDirectory,
                candidate,
                backup,
                root.GetAttribute("phase"),
                settings,
                root.GetAttribute("configWarning"));
        }

        private static string MakeRelative(
            string root,
            string path)
        {
            string fullRoot =
                Path.GetFullPath(root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            string fullPath =
                Path.GetFullPath(path);

            if (!fullPath.StartsWith(
                    fullRoot +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Recovery path is outside HMUC cache.");
            }

            return fullPath
                .Substring(fullRoot.Length + 1)
                .Replace(
                    Path.DirectorySeparatorChar,
                    '/');
        }

        private static string SafeCachePath(
            string root,
            string relative)
        {
            if (string.IsNullOrEmpty(relative) ||
                relative.IndexOf(':') >= 0 ||
                relative.IndexOf('\0') >= 0)
            {
                return null;
            }

            string fullRoot =
                Path.GetFullPath(root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            string full =
                Path.GetFullPath(
                    Path.Combine(
                        fullRoot,
                        relative.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));

            if (!full.StartsWith(
                    fullRoot +
                    Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return full;
        }

        private static void SaveAtomic(
            XmlDocument document,
            string path)
        {
            string temporary =
                path + ".tmp";

            string previous =
                path + ".previous";

            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            XmlWriterSettings settings =
                new XmlWriterSettings();

            settings.Encoding =
                new System.Text.UTF8Encoding(false);
            settings.Indent = true;

            using (XmlWriter writer =
                XmlWriter.Create(
                    temporary,
                    settings))
            {
                document.Save(writer);
            }

            if (File.Exists(previous))
            {
                File.Delete(previous);
            }

            if (File.Exists(path))
            {
                File.Move(
                    path,
                    previous);
            }

            try
            {
                File.Move(
                    temporary,
                    path);

                if (File.Exists(previous))
                {
                    File.Delete(previous);
                }
            }
            catch
            {
                if (!File.Exists(path) &&
                    File.Exists(previous))
                {
                    File.Move(
                        previous,
                        path);
                }

                throw;
            }
        }
    }

    internal sealed class InstallResult
    {
        internal string Guid;
        internal string Name;
        internal string Version;
        internal string Status;
        internal readonly List<string> AddedSettings =
            new List<string>();
        internal string ConfigWarning;

        internal static InstallResult Success(
            string guid,
            string name,
            string version,
            IList<string> addedSettings,
            string configWarning)
        {
            InstallResult result =
                new InstallResult();

            result.Guid = guid;
            result.Name = name;
            result.Version = version;
            result.Status =
                string.IsNullOrEmpty(configWarning)
                    ? "success"
                    : "warning";

            if (addedSettings != null)
            {
                for (int i = 0;
                    i < addedSettings.Count;
                    i++)
                {
                    result.AddedSettings.Add(
                        addedSettings[i]);
                }
            }

            result.ConfigWarning =
                configWarning ?? string.Empty;

            return result;
        }

        internal static InstallResult Failure(
            string guid,
            string name,
            string version)
        {
            InstallResult result =
                new InstallResult();

            result.Guid = guid;
            result.Name = name;
            result.Version = version;
            result.Status = "failure";
            result.ConfigWarning = string.Empty;

            return result;
        }
    }

    internal static class InstallResultWriter
    {
        internal static void Append(
            string cacheRoot,
            InstallResult result)
        {
            string path =
                Path.Combine(
                    cacheRoot,
                    "install-results.xml");

            try
            {
                XmlDocument document =
                    new XmlDocument();

                document.XmlResolver = null;

                if (File.Exists(path))
                {
                    document.Load(path);

                    if (document.DocumentType != null)
                    {
                        throw new FormatException(
                            "DTD declarations are not allowed.");
                    }
                }
                else
                {
                    XmlElement newRoot =
                        document.CreateElement(
                            "HospitalModUpdateResults");

                    newRoot.SetAttribute(
                        "formatVersion",
                        "1");

                    document.AppendChild(newRoot);
                }

                XmlElement root =
                    document.DocumentElement;

                if (root == null ||
                    root.Name != "HospitalModUpdateResults" ||
                    root.GetAttribute("formatVersion") != "1")
                {
                    throw new FormatException(
                        "Invalid update result format.");
                }

                XmlElement node =
                    document.CreateElement(
                        "Result");

                node.SetAttribute(
                    "guid",
                    result.Guid ?? string.Empty);

                node.SetAttribute(
                    "name",
                    result.Name ?? string.Empty);

                node.SetAttribute(
                    "version",
                    result.Version ?? string.Empty);

                node.SetAttribute(
                    "status",
                    result.Status ?? "failure");

                node.SetAttribute(
                    "configWarning",
                    result.ConfigWarning ?? string.Empty);

                for (int i = 0;
                    i < result.AddedSettings.Count;
                    i++)
                {
                    XmlElement setting =
                        document.CreateElement(
                            "AddedSetting");

                    setting.InnerText =
                        result.AddedSettings[i];

                    node.AppendChild(setting);
                }

                root.AppendChild(node);

                string temporary =
                    path + ".tmp";

                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }

                XmlWriterSettings settings =
                    new XmlWriterSettings();

                settings.Encoding =
                    new System.Text.UTF8Encoding(false);
                settings.Indent = true;

                using (XmlWriter writer =
                    XmlWriter.Create(
                        temporary,
                        settings))
                {
                    document.Save(writer);
                }

                string previous =
                    path + ".previous";

                if (File.Exists(previous))
                {
                    File.Delete(previous);
                }

                if (File.Exists(path))
                {
                    File.Move(
                        path,
                        previous);
                }

                try
                {
                    File.Move(
                        temporary,
                        path);

                    if (File.Exists(previous))
                    {
                        File.Delete(previous);
                    }
                }
                catch
                {
                    if (!File.Exists(path) &&
                        File.Exists(previous))
                    {
                        File.Move(
                            previous,
                            path);
                    }

                    throw;
                }
            }
            catch (Exception exception)
            {
                BootstrapInstaller.LogWarning(
                    "HMUC could not write the update result file: " +
                    exception.Message);
            }
        }
    }
}
