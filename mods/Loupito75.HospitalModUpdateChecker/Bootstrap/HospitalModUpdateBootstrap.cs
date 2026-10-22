using System;
using System.Collections.Generic;
using System.Globalization;
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
                {
                    PendingConfigApplier.Apply();
                    VerifyInstalledConfigTemplates();
                }
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
                                record.FreshInstall,
                                record.PreviousVersion,
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
                        record.Version,
                        record.FreshInstall,
                        record.PreviousVersion));

                _log.LogWarning(
                    (record.FreshInstall
                        ? "HMUC interrupted new installation was safely cancelled for "
                        : "HMUC restored the previous installation after an interrupted update for ") +
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
                        operation.FreshInstall,
                        operation.PreviousVersion,
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
                        operation.FreshInstall,
                        operation.PreviousVersion,
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
                        operation.Version,
                        operation.FreshInstall,
                        operation.PreviousVersion));

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
            string template = GetSafeChildPath(
                operation.StagingDirectory, operation.ConfigFile);
            string destination = GetSafeChildPath(
                candidate, operation.ConfigFile);
            if (destination == null)
                throw new FormatException("Unsafe configuration destination.");
            if (template == null || !File.Exists(template))
            {
                // An old snapshot may have been copied from the previous
                // installation into this disposable candidate. Never use
                // it with a new mod DLL whose template was not supplied.
                ConfigTemplateSafety.DropCandidateSnapshot(destination);
                warning = "The signed update has no XML template; HMUC cannot validate this schema.";
                return;
            }

            string alreadyValidReason;
            bool schemaWasAlreadyValid =
                File.Exists(destination) &&
                ConfigTemplateSafety.MatchesDocuments(
                    destination, template, out alreadyValidReason);

            MigrateConfigCore(template, destination, addedSettings,
                out warning, true);

            // The generic additive metadata merger has stricter rules for
            // repeated ID+Enabled/list entries than structural validation.
            // If their schema already matches, failure to refresh optional
            // HMUC comments must not falsely report a broken configuration.
            if (schemaWasAlreadyValid &&
                warning.IndexOf("(FormatException)",
                    StringComparison.Ordinal) >= 0)
            {
                addedSettings.Clear();
                warning = string.Empty;
            }

            try
            {
                ConfigTemplateSafety.Snapshot snapshot =
                    ConfigTemplateSafety.Capture(
                        template, destination, operation.PluginFile,
                        operation.Version);
                string reason;
                if (ConfigTemplateSafety.MatchesTemplate(
                        destination, out reason))
                {
                    ConfigTemplateSafety.Stamp(
                        destination, snapshot, "valid");
                }
                else
                {
                    ConfigTemplateSafety.Stamp(
                        destination, snapshot, "warning");
                    if (string.IsNullOrEmpty(warning))
                        warning = "The new XML format could not be migrated safely; check the mod configuration.";
                }
            }
            catch (Exception exception)
            {
                try
                {
                    ConfigTemplateSafety.DropCandidateSnapshot(destination);
                }
                catch { }
                if (string.IsNullOrEmpty(warning))
                    warning = "Unable to keep or verify the signed XML template (" +
                        exception.GetType().Name + ").";
                _log.LogWarning(operation.Name + ": " + warning);
            }
        }

        // Also used by the periodic verifier, but ONLY on a private
        // disposable XML candidate, never directly on the live config.
        private static void MigrateConfigCore(
            string template, string destination,
            List<string> addedSettings, out string warning,
            bool keepCandidateBackup)
        {
            warning = string.Empty;
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
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        current.DocumentElement.NamespaceURI,
                        defaults.DocumentElement.NamespaceURI,
                        StringComparison.Ordinal))
                {
                    warning =
                        "The existing configuration root does not match the new template; it was left unchanged.";
                    return;
                }

                // Convert only an audited legacy collection shape, before
                // the scalar and section merger sees its repeated entries.
                bool listMigrated = MigrateKnownEnabledIdLists(
                    current.DocumentElement, defaults.DocumentElement);

                // The signed new template owns collection representation.
                // If a surviving collection branch now uses the canonical
                // <Value> tag, normalize older simple-list tags to <Value>.
                // Unsupported legacy shapes fall back to the new template's
                // collection defaults instead of preserving an obsolete schema.
                listMigrated |= NormalizeCollectionsToTemplate(
                    current.DocumentElement,
                    defaults.DocumentElement);

                // Resolve unambiguous scalar relocations against the signed
                // new-version template before adding its missing elements.
                // The old leaf is removed only from the transactional candidate
                // after the new destination is populated.
                List<ScalarRelocation> relocations =
                    FindScalarRelocations(
                        current.DocumentElement,
                        defaults.DocumentElement);

                MergeMissingElements(
                    current,
                    current.DocumentElement,
                    defaults.DocumentElement,
                    string.Empty,
                    addedSettings);

                bool relocated = ApplyScalarRelocations(
                    current.DocumentElement,
                    defaults.DocumentElement,
                    relocations,
                    addedSettings);

                // Retire obsolete scalar leaves by the structure of the
                // signed new template, not by optional @HMUC:set metadata.
                // Ordinary leaves compare by element name. Generic collection
                // leaves (Value/Id/Tag/Room/repeated rows) are handled through
                // their parent branch so user list contents remain opaque.
                bool retiredRemoved = RemoveRetiredScalarLeaves(
                    current.DocumentElement,
                    defaults.DocumentElement);

                // Copy new HMUC comments to existing settings without touching
                // user values, existing descriptions or unrelated comments.
                bool metadataAdded = MergeHmucMetadata(
                    current,
                    current.DocumentElement,
                    defaults.DocumentElement);

                if (addedSettings.Count == 0 &&
                    !metadataAdded && !relocated && !listMigrated &&
                    !retiredRemoved)
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
                LoadSafeXml(temporary);

                // Keep a recoverable copy inside the transactional candidate.
                // The installer also retains the original directory until
                // the complete update has been verified.
                string xmlBackup = destination + "." +
                    DateTime.UtcNow.ToString(
                        "yyyyMMddTHHmmssfffffffZ",
                        CultureInfo.InvariantCulture) + ".backup";
                if (keepCandidateBackup)
                    File.Copy(destination, xmlBackup, false);

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
                    Path.GetFileName(destination) +
                    ": " +
                    warning +
                    " Existing values were not changed.");
            }
        }

        private static void VerifyInstalledConfigTemplates()
        {
            // Recheck on the first startup after seven days, whenever the
            // configuration changes, or when HMUC's verification engine changes.
            // No snapshot on older/manual installations: silent skip.
            string root = GetCanonicalPluginRoot();
            if (!Directory.Exists(root))
                return;

            foreach (string directory in Directory.GetDirectories(root))
            {
                // No monitoring through junctions or symlinked mod folders.
                try
                {
                    if ((File.GetAttributes(directory) &
                        FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch
                {
                    continue;
                }
                string config = ConfigTemplateSafety.GetConfigPath(directory);
                if (!File.Exists(config))
                    continue;

                try
                {
                    ConfigTemplateSafety.RecoverSnapshotState(config);
                }
                catch (Exception exception)
                {
                    _log.LogWarning("HMUC could not recover XML template state for " +
                        Path.GetFileName(directory) + ": " +
                        exception.GetType().Name);
                    continue;
                }

                if (!ConfigTemplateSafety.HasSnapshot(config))
                    continue;

                try
                {
                    ConfigTemplateSafety.Snapshot state;
                    if (!ConfigTemplateSafety.TryReadState(config, out state))
                    {
                        ConfigTemplateSafety.FlagWarning(config);
                        _log.LogWarning("HMUC cannot read XML template metadata for " +
                            Path.GetFileName(directory) + ".");
                        continue;
                    }

                    // Do not apply an old signed template after a mod DLL was
                    // replaced by another installer or changed outside HMUC.
                    if (!ConfigTemplateSafety.IsTrustedSnapshot(config, state))
                    {
                        _log.LogWarning("HMUC ignored a stale or changed XML template for " +
                            Path.GetFileName(directory) + ".");
                        continue;
                    }

                    string currentHash = ConfigTemplateSafety.HashFile(config);
                    if (!ConfigTemplateSafety.NeedsCheck(
                            config, state, currentHash))
                        continue;

                    string reason;
                    if (ConfigTemplateSafety.MatchesTemplate(config, out reason))
                    {
                        ConfigTemplateSafety.Stamp(config, state, "valid");
                        continue;
                    }

                    if (File.Exists(config + ".newconfig"))
                    {
                        reason = "Pending settings must be resolved before a schema repair.";
                    }
                    else
                    {
                        string repairReason;
                        if (TryRepairConfigFromTemplate(config, out repairReason))
                        {
                            ConfigTemplateSafety.Stamp(config, state, "valid");
                            _log.LogInfo("HMUC restored the current XML schema for " +
                                Path.GetFileName(directory) + ".");
                            continue;
                        }
                        reason = repairReason;
                    }

                    ConfigTemplateSafety.Stamp(config, state, "warning");
                    _log.LogWarning("HMUC detected a newer XML settings format for " +
                        Path.GetFileName(directory) +
                        " but could not safely migrate it: " + reason);
                }
                catch (Exception exception)
                {
                    try
                    {
                        ConfigTemplateSafety.Snapshot state;
                        if (ConfigTemplateSafety.TryReadState(config, out state))
                            ConfigTemplateSafety.Stamp(config, state, "warning");
                    }
                    catch { }
                    _log.LogWarning("HMUC XML verification failed for " +
                        Path.GetFileName(directory) + ": " + exception.Message);
                }
            }
        }

        private static bool TryRepairConfigFromTemplate(
            string config, out string reason)
        {
            reason = string.Empty;
            string template = ConfigTemplateSafety.TemplatePath(config);
            string candidate = config + ".hmuc-monitor-working";
            string previous = config + ".hmuc-previous";
            if (File.Exists(previous))
            {
                reason = "An earlier XML swap requires recovery.";
                return false;
            }
            try
            {
                // A private file is merged and structurally checked before
                // any live data is touched; it is discarded on all failures.
                if (File.Exists(candidate))
                    File.Delete(candidate);
                File.Copy(config, candidate, false);
                List<string> added = new List<string>();
                string migrationWarning;
                MigrateConfigCore(
                    template, candidate, added, out migrationWarning, false);
                if (!string.IsNullOrEmpty(migrationWarning))
                {
                    reason = migrationWarning;
                    return false;
                }
                if (!ConfigTemplateSafety.MatchesDocuments(
                        candidate, template, out reason))
                    return false;

                string backup = config + "." +
                    DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ",
                        CultureInfo.InvariantCulture) + ".backup";
                File.Copy(config, backup, false);
                File.Move(config, previous);
                try
                {
                    File.Move(candidate, config);
                    if (!ConfigTemplateSafety.MatchesTemplate(
                            config, out reason))
                        throw new FormatException(reason);
                    File.Delete(previous);
                    return true;
                }
                catch
                {
                    if (File.Exists(previous))
                    {
                        if (File.Exists(config))
                            File.Delete(config);
                        File.Move(previous, config);
                    }
                    throw;
                }
            }
            catch (Exception exception)
            {
                reason = "XML repair failed: " + exception.GetType().Name;
                return false;
            }
            finally
            {
                // These names belong exclusively to the disposable monitor
                // candidate, never to the user's canonical XML or backup.
                string[] transientFiles = new string[]
                {
                    candidate,
                    candidate + ".hmuc-new",
                    candidate + ".hmuc-old"
                };
                for (int i = 0; i < transientFiles.Length; i++)
                {
                    try
                    {
                        if (File.Exists(transientFiles[i]))
                            File.Delete(transientFiles[i]);
                    }
                    catch { }
                }
            }
        }

        // Only real scalar options identified by a globally unique name or
        // a unique unchanged parent/name suffix may follow a relocated parent.
        // Collection rows are excluded here because their schema conversion is
        // handled earlier from the signed target collection parent path.
        // No automatic scalar renaming or type conversion.
        private sealed class ScalarRelocation
        {
            internal string Name;
            internal string SourcePath;
            internal string DestinationPath;
            internal string Value;
            internal bool EnabledAttribute;
            internal string NamespaceUri;
        }

        private static List<ScalarRelocation> FindScalarRelocations(
            XmlElement current, XmlElement defaults)
        {
            Dictionary<string, List<ScalarRelocation>> source =
                new Dictionary<string, List<ScalarRelocation>>(
                    StringComparer.Ordinal);
            Dictionary<string, List<ScalarRelocation>> target =
                new Dictionary<string, List<ScalarRelocation>>(
                    StringComparer.Ordinal);
            Dictionary<string, bool> sourcePaths =
                new Dictionary<string, bool>(StringComparer.Ordinal);

            IndexScalarSettings(
                current, string.Empty, source, sourcePaths);
            IndexScalarSettings(
                defaults, string.Empty, target, null);

            HashSet<string> usedPaths =
                new HashSet<string>(StringComparer.Ordinal);
            List<ScalarRelocation> migrations =
                new List<ScalarRelocation>();
            foreach (KeyValuePair<string, List<ScalarRelocation>> entry
                in target)
            {
                List<ScalarRelocation> candidates;
                if (!source.TryGetValue(entry.Key, out candidates))
                    continue;

                for (int i = 0; i < entry.Value.Count; i++)
                {
                    ScalarRelocation desired = entry.Value[i];
                    if (sourcePaths.ContainsKey(desired.DestinationPath))
                        continue;

                    ScalarRelocation previous = null;
                    if (candidates.Count == 1 && entry.Value.Count == 1)
                    {
                        previous = candidates[0];
                    }
                    else
                    {
                        // Common names such as Enabled require a stable
                        // parent/option suffix, unique in BOTH schemas.
                        // Never infer the mapping from list order.
                        string[] segments = desired.DestinationPath.Split('/');
                        for (int start = 0; start < segments.Length - 1; start++)
                        {
                            string suffix = string.Join(
                                "/", segments, start, segments.Length - start);
                            ScalarRelocation match = null;
                            int sourceCount = 0;
                            int targetCount = 0;
                            for (int j = 0; j < candidates.Count; j++)
                            {
                                if (PathHasSuffix(
                                    candidates[j].DestinationPath, suffix))
                                {
                                    sourceCount++;
                                    match = candidates[j];
                                }
                            }
                            for (int j = 0; j < entry.Value.Count; j++)
                                if (PathHasSuffix(
                                    entry.Value[j].DestinationPath, suffix))
                                    targetCount++;
                            if (sourceCount == 1 && targetCount == 1)
                            {
                                previous = match;
                                break;
                            }
                        }
                    }

                    if (previous == null ||
                        previous.EnabledAttribute != desired.EnabledAttribute ||
                        !string.Equals(previous.NamespaceUri, desired.NamespaceUri,
                            StringComparison.Ordinal) ||
                        !usedPaths.Add(previous.DestinationPath))
                        continue;

                    migrations.Add(new ScalarRelocation
                    {
                        Name = desired.Name,
                        SourcePath = previous.DestinationPath,
                        DestinationPath = desired.DestinationPath,
                        Value = previous.Value,
                        EnabledAttribute = previous.EnabledAttribute,
                        NamespaceUri = previous.NamespaceUri
                    });
                }
            }
            return migrations;
        }

        private static bool PathHasSuffix(string path, string suffix)
        {
            return string.Equals(path, suffix, StringComparison.Ordinal) ||
                path.EndsWith("/" + suffix, StringComparison.Ordinal);
        }

        private static void IndexScalarSettings(
            XmlElement parent, string path,
            Dictionary<string, List<ScalarRelocation>> names,
            Dictionary<string, bool> paths)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                XmlElement element = child as XmlElement;
                if (element == null)
                    continue;

                string childPath = path.Length == 0
                    ? element.Name : path + "/" + element.Name;
                if (HasElementChildren(element))
                {
                    IndexScalarSettings(element, childPath, names, paths);
                    continue;
                }

                // Collection entries are representation rows, not ordinary
                // scalar settings. Collection migration is handled earlier
                // against the signed target parent path.
                if (IsCollectionLeafEntry(element))
                    continue;

                if (paths != null)
                    paths[childPath] = true;

                string value;
                bool attribute;
                if (!TryGetScalarValue(element, out value, out attribute))
                    continue;

                List<ScalarRelocation> list;
                if (!names.TryGetValue(element.Name, out list))
                {
                    list = new List<ScalarRelocation>();
                    names.Add(element.Name, list);
                }
                list.Add(new ScalarRelocation
                {
                    Name = element.Name,
                    DestinationPath = childPath,
                    Value = value,
                    EnabledAttribute = attribute,
                    NamespaceUri = element.NamespaceURI
                });
            }
        }

        private static bool TryGetScalarValue(
            XmlElement element, out string value, out bool attribute)
        {
            value = null;
            attribute = false;
            if (element == null || HasElementChildren(element))
                return false;

            if (element.Attributes.Count == 1 &&
                element.HasAttribute("Enabled"))
            {
                bool enabled;
                if (!bool.TryParse(element.GetAttribute("Enabled"), out enabled))
                    return false;
                foreach (XmlNode node in element.ChildNodes)
                {
                    if (node.NodeType == XmlNodeType.Text &&
                        !string.IsNullOrEmpty(node.Value) &&
                        node.Value.Trim().Length > 0)
                        return false;
                    if (node.NodeType != XmlNodeType.Text &&
                        node.NodeType != XmlNodeType.Whitespace &&
                        node.NodeType != XmlNodeType.SignificantWhitespace &&
                        node.NodeType != XmlNodeType.Comment)
                        return false;
                }
                attribute = true;
                value = enabled ? "true" : "false";
                return true;
            }
            if (element.Attributes.Count != 0)
                return false;

            foreach (XmlNode node in element.ChildNodes)
                if (node.NodeType != XmlNodeType.Text &&
                    node.NodeType != XmlNodeType.Whitespace &&
                    node.NodeType != XmlNodeType.SignificantWhitespace &&
                    node.NodeType != XmlNodeType.Comment)
                    return false;
            value = element.InnerText;
            return true;
        }

        private static XmlElement FindScalarByPath(
            XmlElement root, string path)
        {
            XmlElement current = root;
            string[] components = path.Split('/');
            for (int i = 0; i < components.Length; i++)
            {
                XmlElement match = null;
                foreach (XmlNode child in current.ChildNodes)
                {
                    XmlElement element = child as XmlElement;
                    if (element == null || element.Name != components[i])
                        continue;
                    if (match != null)
                        throw new FormatException(
                            "Ambiguous configuration path: " + path);
                    match = element;
                }
                if (match == null)
                    return null;
                current = match;
            }
            return current;
        }

        private static bool ApplyScalarRelocations(
            XmlElement current,
            XmlElement defaults,
            List<ScalarRelocation> relocations,
            List<string> addedSettings)
        {
            bool modified = false;
            for (int i = 0; i < relocations.Count; i++)
            {
                ScalarRelocation migration = relocations[i];
                XmlElement destination = FindScalarByPath(
                    current, migration.DestinationPath);
                string previous;
                bool attribute;
                if (!TryGetScalarValue(
                        destination, out previous, out attribute) ||
                    attribute != migration.EnabledAttribute ||
                    !string.Equals(destination.NamespaceURI, migration.NamespaceUri,
                        StringComparison.Ordinal))
                    throw new FormatException(
                        "Relocated XML option is no longer scalar: " +
                        migration.DestinationPath);

                if (attribute)
                    destination.SetAttribute("Enabled", migration.Value);
                else
                    SetScalarText(destination, migration.Value);

                // The newly installed schema owns the destination. Remove
                // only this uniquely identified old leaf from the candidate;
                // the original on-disk configuration is still protected by
                // the enclosing installation transaction and its rollback.
                XmlElement old = FindScalarByPath(
                    current, migration.SourcePath);
                string oldValue;
                bool oldAttribute;
                if (!TryGetScalarValue(old, out oldValue, out oldAttribute) ||
                    oldAttribute != migration.EnabledAttribute ||
                    old.ParentNode == null ||
                    !string.Equals(oldValue, migration.Value,
                        StringComparison.Ordinal))
                    throw new FormatException(
                        "Relocated XML source changed during migration: " +
                        migration.SourcePath);
                XmlElement oldParent =
                    old.ParentNode as XmlElement;

                XmlNode before = old.PreviousSibling;
                while (before != null && before.NodeType != XmlNodeType.Element)
                {
                    XmlNode preceding = before.PreviousSibling;
                    XmlComment comment = before as XmlComment;
                    if (comment != null &&
                        HmucCommentKind(comment.Value) != null)
                        old.ParentNode.RemoveChild(before);
                    before = preceding;
                }
                old.ParentNode.RemoveChild(old);

                // A moved scalar can leave its old structural branch empty.
                // Remove only empty ancestors that are absent from the signed
                // target template; expected empty sections remain untouched.
                PruneEmptyRetiredAncestors(
                    current,
                    defaults,
                    oldParent);

                // Migrated options are not newly added default settings.
                for (int j = addedSettings.Count - 1; j >= 0; j--)
                {
                    string description = addedSettings[j];
                    if (description == migration.DestinationPath ||
                        description.StartsWith(
                            migration.DestinationPath + " = ",
                            StringComparison.Ordinal))
                        addedSettings.RemoveAt(j);
                }
                modified = true;
            }
            return modified;
        }

        private static void SetScalarText(XmlElement element, string value)
        {
            XmlText text = null;
            foreach (XmlNode node in element.ChildNodes)
            {
                if (node.NodeType == XmlNodeType.Text)
                {
                    if (text != null)
                        throw new FormatException(
                            "Split XML text cannot be migrated.");
                    text = (XmlText)node;
                }
                else if (node.NodeType != XmlNodeType.Whitespace &&
                         node.NodeType != XmlNodeType.SignificantWhitespace &&
                         node.NodeType != XmlNodeType.Comment)
                    throw new FormatException(
                        "Complex XML content cannot be migrated.");
            }
            if (text == null)
                element.AppendChild(element.OwnerDocument.CreateTextNode(value));
            else
                text.Value = value;
        }

        // Only audited old Room ID+Enabled lists may use this conversion.
        // HTC has already adopted Value IDs. HAL's published reader still
        // requires Room elements: convert HAL only when a future signed
        // template explicitly switches HospitalAlwaysLit/Rooms to Value.
        // Both published old readers apply the last valid Enabled per ID.
        private static bool MigrateKnownEnabledIdLists(
            XmlElement current, XmlElement defaults)
        {
            XmlElement oldList = null;
            XmlElement newList = null;
            if (current.Name == "HospitalTrafficControl" &&
                defaults.Name == "HospitalTrafficControl")
            {
                XmlElement currentTransit = current["RoomTransit"];
                XmlElement newTransit = defaults["RoomTransit"];
                if (currentTransit != null && newTransit != null)
                {
                    oldList = currentTransit["Exceptions"];
                    newList = newTransit["Exceptions"];
                }
            }
            else if (current.Name == "HospitalAlwaysLit" &&
                     defaults.Name == "HospitalAlwaysLit")
            {
                oldList = current["Rooms"];
                newList = defaults["Rooms"];
            }
            else
            {
                return false;
            }

            // An old HAL template containing Room rows NEVER triggers this:
            // conversion is permitted only when the signed new template
            // contains simple Value entries.
            if (oldList == null || newList == null ||
                !IsTargetValueCollection(newList) ||
                IsSimpleValueList(oldList) ||
                IsEmptyCollection(oldList))
                return false;

            List<XmlElement> rows = new List<XmlElement>();
            Dictionary<string, bool> effective =
                new Dictionary<string, bool>(StringComparer.Ordinal);
            List<string> order = new List<string>();
            foreach (XmlNode node in oldList.ChildNodes)
            {
                XmlElement row = node as XmlElement;
                if (row == null)
                {
                    if (node.NodeType != XmlNodeType.Comment &&
                        node.NodeType != XmlNodeType.Whitespace &&
                        node.NodeType != XmlNodeType.SignificantWhitespace &&
                        (node.NodeType != XmlNodeType.Text ||
                         node.InnerText.Trim().Length != 0))
                        return false;
                    continue;
                }
                if (row.Name != "Room" || row.Attributes.Count != 2 ||
                    !row.HasAttribute("ID") || !row.HasAttribute("Enabled") ||
                    HasElementChildren(row))
                    return false;

                foreach (XmlNode child in row.ChildNodes)
                    if (child.NodeType != XmlNodeType.Comment &&
                        child.NodeType != XmlNodeType.Whitespace &&
                        child.NodeType != XmlNodeType.SignificantWhitespace &&
                        (child.NodeType != XmlNodeType.Text ||
                         child.InnerText.Trim().Length != 0))
                        return false;

                string id = row.GetAttribute("ID").Trim();
                bool enabled;
                if (id.Length == 0 ||
                    !bool.TryParse(row.GetAttribute("Enabled").Trim(),
                        out enabled))
                    return false;
                // Preserve old reader semantics: the last duplicate wins.
                if (effective.ContainsKey(id))
                    order.Remove(id);
                effective[id] = enabled;
                order.Add(id);
                rows.Add(row);
            }
            if (rows.Count == 0)
                return false;

            // No mutation until every source entry is validated.
            XmlElement templateValue =
                FirstElementChild(
                    newList);

            if (templateValue == null &&
                !HasHmucMultilineListMetadata(
                    newList))
            {
                return false;
            }

            XmlNode sourceIndentation =
                rows[0].PreviousSibling;

            for (int i = 0; i < rows.Count; i++)
            {
                // Remove obsolete indentation attached to each old row,
                // leaving comments and the container's closing whitespace.
                XmlNode before = rows[i].PreviousSibling;
                if (IsWhitespaceNode(before))
                    oldList.RemoveChild(before);
                oldList.RemoveChild(rows[i]);
            }

            // Preserve user comments; append retained IDs in the source
            // format's stable last-occurrence order, not template defaults.
            XmlNode closing = oldList.LastChild;
            if (!IsWhitespaceNode(closing))
                closing = null;
            XmlNode indentation =
                templateValue == null
                    ? sourceIndentation
                    : templateValue.PreviousSibling;

            if (!IsWhitespaceNode(indentation))
                indentation = null;
            for (int i = 0; i < order.Count; i++)
            {
                if (!effective[order[i]])
                    continue;
                if (indentation != null)
                    oldList.InsertBefore(
                        current.OwnerDocument.ImportNode(indentation, true),
                        closing);
                string prefix =
                    templateValue == null
                        ? newList.Prefix
                        : templateValue.Prefix;

                string namespaceUri =
                    templateValue == null
                        ? newList.NamespaceURI
                        : templateValue.NamespaceURI;

                XmlElement value = current.OwnerDocument.CreateElement(
                    prefix, "Value",
                    namespaceUri);
                value.InnerText = order[i];
                oldList.InsertBefore(value, closing);
            }
            return true;
        }

        private static bool NormalizeCollectionsToTemplate(
            XmlElement currentRoot,
            XmlElement defaultsRoot)
        {
            if (currentRoot == null || defaultsRoot == null)
                return false;

            Dictionary<string, XmlElement> targetCollections =
                new Dictionary<string, XmlElement>(
                    StringComparer.Ordinal);

            CollectTargetValueCollectionsByPath(
                defaultsRoot,
                string.Empty,
                targetCollections);

            bool changed = false;

            foreach (KeyValuePair<string, XmlElement> entry
                in targetCollections)
            {
                XmlElement source;

                try
                {
                    source =
                        FindScalarByPath(
                            currentRoot,
                            entry.Key);
                }
                catch (FormatException)
                {
                    continue;
                }

                if (source == null)
                    continue;

                XmlElement target =
                    entry.Value;

                if (!IsTargetValueCollection(target) ||
                    IsEmptyCollection(source) ||
                    IsSimpleValueList(source))
                {
                    continue;
                }

                List<string> values;

                if (TryReadSimpleTextCollection(
                        source,
                        out values))
                {
                    RewriteCollectionAsValues(
                        source,
                        target,
                        values);
                    changed = true;
                    continue;
                }

                // Complex/unsupported old collection representation: the
                // updated mod no longer promises backward compatibility.
                // Replace only this collection's contents with the signed
                // template defaults; the enclosing transaction still keeps
                // the user's pre-update backup and full rollback path.
                ReplaceCollectionContentsFromTemplate(
                    source,
                    target);
                changed = true;
            }

            return changed;
        }

        private static void CollectTargetValueCollectionsByPath(
            XmlElement parent,
            string parentPath,
            Dictionary<string, XmlElement> result)
        {
            if (parent == null || result == null)
                return;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element =
                    node as XmlElement;

                if (element == null)
                    continue;

                string path =
                    parentPath.Length == 0
                        ? element.Name
                        : parentPath + "/" + element.Name;

                if (IsTargetValueCollection(element))
                {
                    if (result.ContainsKey(path))
                    {
                        throw new FormatException(
                            "Ambiguous target collection path: " +
                            path);
                    }

                    result.Add(
                        path,
                        element);
                    continue;
                }

                CollectTargetValueCollectionsByPath(
                    element,
                    path,
                    result);
            }
        }

        private static bool TryReadSimpleTextCollection(
            XmlElement source,
            out List<string> values)
        {
            values =
                new List<string>();

            if (source == null)
                return false;

            string childName = null;

            foreach (XmlNode node in source.ChildNodes)
            {
                XmlElement element =
                    node as XmlElement;

                if (element == null)
                {
                    if (node.NodeType != XmlNodeType.Comment &&
                        node.NodeType != XmlNodeType.Whitespace &&
                        node.NodeType != XmlNodeType.SignificantWhitespace &&
                        (node.NodeType != XmlNodeType.Text ||
                         node.InnerText.Trim().Length != 0))
                    {
                        return false;
                    }

                    continue;
                }

                if (HasElementChildren(element) ||
                    element.Attributes.Count != 0)
                {
                    return false;
                }

                if (childName == null)
                {
                    childName =
                        element.Name;
                }
                else if (!string.Equals(
                        childName,
                        element.Name,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                string value =
                    (element.InnerText ??
                        string.Empty).Trim();

                if (value.Length == 0)
                    return false;

                values.Add(value);
            }

            return values.Count > 0;
        }

        private static void RewriteCollectionAsValues(
            XmlElement source,
            XmlElement targetTemplate,
            List<string> values)
        {
            if (source == null ||
                targetTemplate == null ||
                values == null)
            {
                return;
            }

            XmlElement templateValue =
                FirstElementChild(
                    targetTemplate);

            if (templateValue != null &&
                (templateValue.Name != "Value" ||
                 templateValue.Attributes.Count != 0 ||
                 HasElementChildren(templateValue)))
            {
                throw new FormatException(
                    "Signed collection template is not a simple Value list.");
            }

            if (templateValue == null &&
                !HasHmucMultilineListMetadata(
                    targetTemplate))
            {
                throw new FormatException(
                    "Empty signed collection template has no multiline-list contract.");
            }

            XmlElement sourceFirst =
                FirstElementChild(
                    source);

            XmlNode sourceIndentation =
                sourceFirst == null
                    ? null
                    : sourceFirst.PreviousSibling;

            RemoveElementChildrenAndTheirIndentation(
                source);

            XmlNode closing =
                source.LastChild;

            if (!IsWhitespaceNode(closing))
                closing = null;

            XmlNode indentation =
                templateValue == null
                    ? sourceIndentation
                    : templateValue.PreviousSibling;

            if (!IsWhitespaceNode(indentation))
                indentation = null;

            for (int i = 0; i < values.Count; i++)
            {
                if (indentation != null)
                {
                    source.InsertBefore(
                        source.OwnerDocument.ImportNode(
                            indentation,
                            true),
                        closing);
                }

                string prefix =
                    templateValue == null
                        ? targetTemplate.Prefix
                        : templateValue.Prefix;

                string namespaceUri =
                    templateValue == null
                        ? targetTemplate.NamespaceURI
                        : templateValue.NamespaceURI;

                XmlElement value =
                    source.OwnerDocument.CreateElement(
                        prefix,
                        "Value",
                        namespaceUri);

                value.InnerText =
                    values[i];

                source.InsertBefore(
                    value,
                    closing);
            }
        }

        private static void ReplaceCollectionContentsFromTemplate(
            XmlElement source,
            XmlElement template)
        {
            if (source == null || template == null)
                return;

            while (source.HasChildNodes)
                source.RemoveChild(source.FirstChild);

            foreach (XmlNode node in template.ChildNodes)
            {
                source.AppendChild(
                    source.OwnerDocument.ImportNode(
                        node,
                        true));
            }
        }

        private static XmlElement FirstElementChild(
            XmlElement parent)
        {
            if (parent == null)
                return null;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element =
                    node as XmlElement;

                if (element != null)
                    return element;
            }

            return null;
        }

        private static void RemoveElementChildrenAndTheirIndentation(
            XmlElement parent)
        {
            if (parent == null)
                return;

            List<XmlNode> remove =
                new List<XmlNode>();

            foreach (XmlNode node in parent.ChildNodes)
            {
                if (!(node is XmlElement))
                    continue;

                XmlNode before =
                    node.PreviousSibling;

                if (IsWhitespaceNode(before) &&
                    !remove.Contains(before))
                {
                    remove.Add(before);
                }

                remove.Add(node);
            }

            for (int i = 0; i < remove.Count; i++)
            {
                XmlNode node =
                    remove[i];

                if (node.ParentNode == parent)
                    parent.RemoveChild(node);
            }
        }

        private static bool IsMatchingOpaqueCollection(
            XmlElement current, XmlElement defaults)
        {
            if (IsTargetValueCollection(defaults))
                return IsSimpleValueList(current) ||
                    IsEmptyCollection(current);
            if (IsLegacyRoomList(defaults))
                return IsLegacyRoomList(current) ||
                    IsEmptyCollection(current);
            return false;
        }

        private static bool IsTargetValueCollection(
            XmlElement element)
        {
            return IsSimpleValueList(element) ||
                (IsEmptyCollection(element) &&
                 HasHmucMultilineListMetadata(element));
        }

        private static bool IsSimpleValueList(XmlElement element)
        {
            return HasOnlyEntries(element, "Value", false);
        }

        private static bool IsLegacyRoomList(XmlElement element)
        {
            return HasOnlyEntries(element, "Room", true);
        }

        private static bool HasOnlyEntries(
            XmlElement container, string tag, bool hasIdAndEnabled)
        {
            if (container == null)
                return false;
            bool found = false;
            foreach (XmlNode child in container.ChildNodes)
            {
                XmlElement item = child as XmlElement;
                if (item == null)
                {
                    if (child.NodeType != XmlNodeType.Comment &&
                        child.NodeType != XmlNodeType.Whitespace &&
                        child.NodeType != XmlNodeType.SignificantWhitespace &&
                        (child.NodeType != XmlNodeType.Text ||
                         child.InnerText.Trim().Length != 0))
                        return false;
                    continue;
                }
                if (item.Name != tag || HasElementChildren(item))
                    return false;
                if (hasIdAndEnabled)
                {
                    if (item.Attributes.Count != 2 ||
                        !item.HasAttribute("ID") ||
                        !item.HasAttribute("Enabled"))
                        return false;
                }
                else if (item.Attributes.Count != 0)
                    return false;
                found = true;
            }
            return found;
        }

        private static bool IsEmptyCollection(XmlElement element)
        {
            if (element == null)
                return false;
            foreach (XmlNode node in element.ChildNodes)
            {
                if (node.NodeType == XmlNodeType.Element)
                    return false;
                if (node.NodeType != XmlNodeType.Whitespace &&
                    node.NodeType != XmlNodeType.SignificantWhitespace &&
                    node.NodeType != XmlNodeType.Comment &&
                    (node.NodeType != XmlNodeType.Text ||
                     node.InnerText.Trim().Length != 0))
                    return false;
            }
            return true;
        }

        private static void MergeMissingElements(
            XmlDocument owner,
            XmlElement current,
            XmlElement defaults,
            string parentPath,
            List<string> addedSettings)
        {
            // Collections are indivisible user data, not defaults to append.
            if (IsMatchingOpaqueCollection(current, defaults))
                return;

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

        private static bool RemoveRetiredScalarLeaves(
            XmlElement currentRoot,
            XmlElement defaultsRoot)
        {
            if (currentRoot == null || defaultsRoot == null)
                return false;

            HashSet<string> templateLeafNames =
                new HashSet<string>(StringComparer.Ordinal);

            CollectScalarLeafNames(
                defaultsRoot,
                templateLeafNames);

            HashSet<string> templateCollectionPaths =
                new HashSet<string>(StringComparer.Ordinal);

            CollectTargetCollectionPaths(
                defaultsRoot,
                string.Empty,
                templateCollectionPaths);

            List<XmlElement> collectionParents =
                new List<XmlElement>();

            CollectCollectionParents(
                currentRoot,
                collectionParents);

            List<XmlElement> leaves =
                new List<XmlElement>();

            CollectRetirableScalarLeaves(
                currentRoot,
                leaves);

            bool changed = false;

            for (int i = collectionParents.Count - 1; i >= 0; i--)
            {
                XmlElement parent = collectionParents[i];

                if (parent == null ||
                    parent.ParentNode == null)
                {
                    continue;
                }

                string collectionPath =
                    GetRelativeElementPath(
                        currentRoot,
                        parent);

                if (!string.IsNullOrEmpty(collectionPath) &&
                    templateCollectionPaths.Contains(
                        collectionPath))
                {
                    continue;
                }

                XmlElement ancestor =
                    parent.ParentNode as XmlElement;

                RemoveElementAndHmucMetadata(
                    parent);

                changed = true;

                PruneEmptyRetiredAncestors(
                    currentRoot,
                    defaultsRoot,
                    ancestor);
            }

            for (int i = leaves.Count - 1; i >= 0; i--)
            {
                XmlElement element = leaves[i];

                if (element == null ||
                    element.ParentNode == null ||
                    templateLeafNames.Contains(
                        element.Name))
                {
                    continue;
                }

                XmlElement parent =
                    element.ParentNode as XmlElement;

                RemoveElementAndHmucMetadata(
                    element);

                changed = true;

                // A retired leaf may leave behind a structural section that
                // existed only to contain that obsolete setting. Prune only
                // those now-empty ancestors and only when the new signed
                // template has no matching path. Never prune a populated
                // branch or an expected empty collection/section.
                PruneEmptyRetiredAncestors(
                    currentRoot,
                    defaultsRoot,
                    parent);
            }

            return changed;
        }

        private static void CollectScalarLeafNames(
            XmlElement parent,
            HashSet<string> names)
        {
            if (parent == null || names == null)
                return;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element =
                    node as XmlElement;

                if (element == null)
                    continue;

                if (HasElementChildren(element))
                {
                    CollectScalarLeafNames(
                        element,
                        names);
                    continue;
                }

                if (IsCollectionLeafEntry(element))
                    continue;

                string value;
                bool attribute;

                if (TryGetScalarValue(
                        element,
                        out value,
                        out attribute))
                {
                    names.Add(
                        element.Name);
                }
            }
        }

        private static void CollectRetirableScalarLeaves(
            XmlElement parent,
            List<XmlElement> result)
        {
            if (parent == null || result == null)
                return;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element =
                    node as XmlElement;

                if (element == null)
                    continue;

                if (HasElementChildren(element))
                {
                    CollectRetirableScalarLeaves(
                        element,
                        result);
                    continue;
                }

                if (IsCollectionLeafEntry(element))
                    continue;

                string value;
                bool attribute;

                if (TryGetScalarValue(
                        element,
                        out value,
                        out attribute))
                {
                    result.Add(
                        element);
                }
            }
        }

        private static void CollectTargetCollectionPaths(
            XmlElement parent,
            string parentPath,
            HashSet<string> paths)
        {
            if (parent == null || paths == null)
                return;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element =
                    node as XmlElement;

                if (element == null)
                    continue;

                string path =
                    parentPath.Length == 0
                        ? element.Name
                        : parentPath + "/" + element.Name;

                if (IsCollectionContainer(element) ||
                    IsTargetValueCollection(element))
                {
                    paths.Add(path);
                    continue;
                }

                CollectTargetCollectionPaths(
                    element,
                    path,
                    paths);
            }
        }

        private static void CollectCollectionParents(
            XmlElement parent,
            List<XmlElement> result)
        {
            if (parent == null || result == null)
                return;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element =
                    node as XmlElement;

                if (element == null)
                    continue;

                if (IsCollectionContainer(element))
                {
                    result.Add(element);
                    continue;
                }

                CollectCollectionParents(
                    element,
                    result);
            }
        }

        private static bool IsCollectionContainer(
            XmlElement element)
        {
            if (element == null)
                return false;

            if (HasHmucMultilineListMetadata(element))
                return true;

            if (IsLegacyRoomList(element))
                return true;

            Dictionary<string, int> counts =
                new Dictionary<string, int>(StringComparer.Ordinal);

            int elementCount = 0;

            foreach (XmlNode node in element.ChildNodes)
            {
                XmlElement child =
                    node as XmlElement;

                if (child == null)
                    continue;

                if (HasElementChildren(child))
                    return false;

                elementCount++;

                int count;
                counts.TryGetValue(
                    child.Name,
                    out count);

                counts[child.Name] =
                    count + 1;
            }

            if (elementCount == 0)
                return false;

            if (counts.Count == 1)
            {
                foreach (KeyValuePair<string, int> pair in counts)
                {
                    if (pair.Key == "Value" ||
                        pair.Key == "Id" ||
                        pair.Key == "Tag" ||
                        pair.Key == "Room" ||
                        pair.Value > 1)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsCollectionLeafEntry(
            XmlElement element)
        {
            if (element == null)
                return false;

            XmlElement parent =
                element.ParentNode as XmlElement;

            if (parent == null)
                return false;

            if (HasHmucMultilineListMetadata(parent))
                return true;

            if (IsLegacyRoomList(parent))
                return true;

            if ((element.Name == "Value" ||
                 element.Name == "Id" ||
                 element.Name == "Tag") &&
                HasOnlyLeafEntriesNamed(
                    parent,
                    element.Name))
            {
                return true;
            }

            int sameName = 0;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement sibling =
                    node as XmlElement;

                if (sibling == null ||
                    !string.Equals(
                        sibling.Name,
                        element.Name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                sameName++;

                if (sameName > 1)
                    return true;
            }

            return false;
        }

        private static bool HasOnlyLeafEntriesNamed(
            XmlElement parent,
            string name)
        {
            if (parent == null ||
                string.IsNullOrEmpty(name))
            {
                return false;
            }

            bool found = false;

            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element =
                    node as XmlElement;

                if (element == null)
                    continue;

                if (!string.Equals(
                        element.Name,
                        name,
                        StringComparison.Ordinal) ||
                    HasElementChildren(element))
                {
                    return false;
                }

                found = true;
            }

            return found;
        }

        private static void PruneEmptyRetiredAncestors(
            XmlElement currentRoot,
            XmlElement defaultsRoot,
            XmlElement candidate)
        {
            XmlElement current =
                candidate;

            while (current != null &&
                !object.ReferenceEquals(
                    current,
                    currentRoot))
            {
                if (HasElementChildren(current) ||
                    HasMeaningfulScalarContent(current))
                {
                    return;
                }

                string path =
                    GetRelativeElementPath(
                        currentRoot,
                        current);

                if (string.IsNullOrEmpty(path))
                    return;

                bool expected = false;

                try
                {
                    expected =
                        FindScalarByPath(
                            defaultsRoot,
                            path) != null;
                }
                catch (FormatException)
                {
                    return;
                }

                if (expected)
                    return;

                XmlElement parent =
                    current.ParentNode as XmlElement;

                RemoveElementAndHmucMetadata(
                    current);

                current = parent;
            }
        }

        private static bool HasMeaningfulScalarContent(
            XmlElement element)
        {
            if (element == null)
                return false;

            if (element.Attributes.Count != 0)
                return true;

            foreach (XmlNode node in element.ChildNodes)
            {
                if (node.NodeType == XmlNodeType.Text &&
                    !string.IsNullOrEmpty(node.Value) &&
                    node.Value.Trim().Length > 0)
                {
                    return true;
                }

                if (node.NodeType != XmlNodeType.Text &&
                    node.NodeType != XmlNodeType.Whitespace &&
                    node.NodeType != XmlNodeType.SignificantWhitespace &&
                    node.NodeType != XmlNodeType.Comment)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetRelativeElementPath(
            XmlElement root,
            XmlElement element)
        {
            if (root == null || element == null ||
                object.ReferenceEquals(root, element))
            {
                return string.Empty;
            }

            List<string> parts =
                new List<string>();

            XmlNode current = element;

            while (current != null &&
                !object.ReferenceEquals(current, root))
            {
                XmlElement currentElement =
                    current as XmlElement;

                if (currentElement == null)
                    return string.Empty;

                parts.Add(
                    currentElement.Name);

                current =
                    current.ParentNode;
            }

            if (!object.ReferenceEquals(current, root))
                return string.Empty;

            parts.Reverse();

            return string.Join(
                "/",
                parts.ToArray());
        }

        private static void RemoveElementAndHmucMetadata(
            XmlElement element)
        {
            if (element == null ||
                element.ParentNode == null)
            {
                return;
            }

            XmlNode parent =
                element.ParentNode;

            XmlNode previous =
                element.PreviousSibling;

            while (previous != null &&
                previous.NodeType != XmlNodeType.Element)
            {
                XmlNode preceding =
                    previous.PreviousSibling;

                XmlComment comment =
                    previous as XmlComment;

                if (comment != null &&
                    HmucCommentKind(comment.Value) != null)
                {
                    parent.RemoveChild(
                        previous);
                }

                previous = preceding;
            }

            parent.RemoveChild(
                element);
        }

        private static bool MergeHmucMetadata(
            XmlDocument owner, XmlElement current, XmlElement defaults)
        {
            // Section header metadata belongs to the parent. Repeated
            // Value/Room entries themselves carry no HMUC option metadata.
            if (IsMatchingOpaqueCollection(current, defaults))
                return false;

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

                bool nestedSection = HasElementChildren(pair.Value);
                if (nestedSection && !HasElementChildren(existing) &&
                    !IsMatchingOpaqueCollection(existing, pair.Value))
                    continue;

                // Update HMUC annotations on structural section parents
                // as well as editable leaves, so newly supplied section
                // descriptions are available to the editor after migration.
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
                if (nestedSection)
                    changed |= MergeHmucMetadata(owner, existing, pair.Value);
            }
            return changed;
        }

        private static bool HasHmucMultilineListMetadata(
            XmlElement element)
        {
            if (element == null)
                return false;

            for (XmlNode node = element.PreviousSibling;
                 node != null &&
                 node.NodeType != XmlNodeType.Element;
                 node = node.PreviousSibling)
            {
                XmlComment comment =
                    node as XmlComment;

                if (comment == null)
                    continue;

                string text =
                    (comment.Value ??
                        string.Empty).Trim();

                if (!text.StartsWith(
                        "@HMUC:set",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return text.IndexOf(
                        "control=\"multiline-list\"",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf(
                        "control='multiline-list'",
                        StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
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
        internal string PreviousVersion;
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

                // Optional in queues created by older HMUC versions.
                // This is display-only and must not invalidate the queue.
                string previous = element.GetAttribute("previousVersion");
                operation.PreviousVersion = string.Empty;
                if (previous.Length > 0 && previous.Length <= 32)
                {
                    try
                    {
                        operation.PreviousVersion =
                            new Version(previous).ToString();
                    }
                    catch (Exception)
                    {
                        // Invalid informational metadata is ignored.
                    }
                }

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
        internal bool FreshInstall;
        internal string PreviousVersion;
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
            bool freshInstall,
            string previousVersion,
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
            FreshInstall = freshInstall;
            PreviousVersion = previousVersion ?? string.Empty;
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
                "mode",
                FreshInstall ? "install" : "update");

            if (!FreshInstall &&
                !string.IsNullOrEmpty(PreviousVersion))
            {
                root.SetAttribute(
                    "previousVersion",
                    PreviousVersion);
            }

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

            // Older recovery records lack mode and previousVersion.
            // "install-ready" was always an initial-install phase;
            // otherwise legacy records keep the update label.
            string phase = root.GetAttribute("phase");
            string mode = root.GetAttribute("mode");
            if (mode.Length > 0 && mode != "install" && mode != "update")
                throw new FormatException("Invalid recovery install mode.");
            bool freshInstall = mode == "install" ||
                (mode.Length == 0 && phase == "install-ready");

            string previousVersion = string.Empty;
            string previous = root.GetAttribute("previousVersion");
            if (!freshInstall && previous.Length > 0 &&
                previous.Length <= 32)
            {
                try
                {
                    previousVersion = new Version(previous).ToString();
                }
                catch (Exception)
                {
                    // Nonessential display metadata is ignored.
                }
            }

            return new RecoveryRecord(
                root.GetAttribute("guid"),
                root.GetAttribute("name"),
                root.GetAttribute("version"),
                freshInstall,
                previousVersion,
                installDirectory,
                candidate,
                backup,
                phase,
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
        internal string Mode;
        internal string PreviousVersion;
        internal string Status;
        internal readonly List<string> AddedSettings =
            new List<string>();
        internal string ConfigWarning;

        internal static InstallResult Success(
            string guid,
            string name,
            string version,
            bool freshInstall,
            string previousVersion,
            IList<string> addedSettings,
            string configWarning)
        {
            InstallResult result =
                new InstallResult();

            result.Guid = guid;
            result.Name = name;
            result.Version = version;
            result.Mode = freshInstall ? "install" : "update";
            result.PreviousVersion =
                freshInstall ? string.Empty : previousVersion ?? string.Empty;
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
            string version,
            bool freshInstall,
            string previousVersion)
        {
            InstallResult result =
                new InstallResult();

            result.Guid = guid;
            result.Name = name;
            result.Version = version;
            result.Mode = freshInstall ? "install" : "update";
            result.PreviousVersion =
                freshInstall ? string.Empty : previousVersion ?? string.Empty;
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
                    "mode",
                    result.Mode == "install" ? "install" : "update");

                if (!string.IsNullOrEmpty(result.PreviousVersion) &&
                    result.Mode != "install")
                {
                    node.SetAttribute(
                        "previousVersion",
                        result.PreviousVersion);
                }

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
