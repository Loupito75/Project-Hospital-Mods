using System;
using System.Collections.Generic;
using System.Globalization;

namespace HospitalModUpdateChecker
{
    internal sealed class PackageManifest
    {
        internal const int SupportedFormatVersion = 1;
        internal const int MaximumEntries = 256;
        internal const long MaximumPackageBytes = 536870912L;
        internal const long MaximumUnpackedBytes = 1073741824L;

        internal long ManifestRevision { get; private set; }
        internal DateTime UpdatedUtc { get; private set; }
        internal DateTime ExpiresUtc { get; private set; }
        internal List<PackageManifestEntry> Entries { get; private set; }

        private PackageManifest()
        {
            Entries = new List<PackageManifestEntry>();
        }

        internal static bool TryParse(
            string text,
            out PackageManifest manifest,
            out string error)
        {
            manifest = null;
            error = null;

            if (string.IsNullOrEmpty(text))
            {
                error = "Package manifest is empty.";
                return false;
            }

            object rootValue;
            if (!SimpleJsonParser.TryParse(text, out rootValue, out error))
            {
                return false;
            }

            Dictionary<string, object> root =
                rootValue as Dictionary<string, object>;

            if (root == null)
            {
                error = "Package manifest root must be a JSON object.";
                return false;
            }

            if (!HasOnlyKeys(
                    root,
                    new string[]
                    {
                        "formatVersion",
                        "manifestRevision",
                        "updatedUtc",
                        "expiresUtc",
                        "packages"
                    },
                    out error))
            {
                return false;
            }

            int formatVersion;
            if (!TryGetRequiredInt(root, "formatVersion", out formatVersion) ||
                formatVersion != SupportedFormatVersion)
            {
                error = "Unsupported or invalid package formatVersion.";
                return false;
            }

            long manifestRevision;
            if (!TryGetRequiredLong(root, "manifestRevision", out manifestRevision) ||
                manifestRevision < 1L)
            {
                error = "Invalid package manifestRevision.";
                return false;
            }

            string updatedUtcText;
            if (!TryGetRequiredString(root, "updatedUtc", out updatedUtcText))
            {
                error = "Invalid package updatedUtc value.";
                return false;
            }

            DateTime updatedUtc;
            if (!DateTime.TryParseExact(
                    updatedUtcText,
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out updatedUtc) ||
                updatedUtc.Kind != DateTimeKind.Utc)
            {
                error = "Invalid package updatedUtc value.";
                return false;
            }

            string expiresUtcText;
            if (!TryGetRequiredString(root, "expiresUtc", out expiresUtcText))
            {
                error = "Invalid package expiresUtc value.";
                return false;
            }

            DateTime expiresUtc;
            if (!DateTime.TryParseExact(
                    expiresUtcText,
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out expiresUtc) ||
                expiresUtc.Kind != DateTimeKind.Utc ||
                expiresUtc <= updatedUtc)
            {
                error = "Invalid package expiresUtc value.";
                return false;
            }

            object packagesValue;
            if (!root.TryGetValue("packages", out packagesValue))
            {
                error = "packages is missing.";
                return false;
            }

            List<object> packages = packagesValue as List<object>;
            if (packages == null)
            {
                error = "packages must be a JSON array.";
                return false;
            }

            if (packages.Count > MaximumEntries)
            {
                error = "Package manifest contains too many entries.";
                return false;
            }

            PackageManifest parsed = new PackageManifest();
            parsed.ManifestRevision = manifestRevision;
            parsed.UpdatedUtc = updatedUtc;
            parsed.ExpiresUtc = expiresUtc;

            HashSet<string> seenGuids =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < packages.Count; i++)
            {
                Dictionary<string, object> package =
                    packages[i] as Dictionary<string, object>;

                if (package == null)
                {
                    error = "Package entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) +
                        " must be a JSON object.";
                    return false;
                }

                if (!HasOnlyKeys(
                        package,
                        new string[]
                        {
                            "guid",
                            "name",
                            "version",
                            "downloadUrl",
                            "sha256",
                            "packageBytes",
                            "maximumUnpackedBytes",
                            "installDirectory",
                            "archiveRoot",
                            "pluginFile",
                            "configFile",
                            "runtimeDirectories",
                            "runtimeFiles",
                            "removeFiles"
                        },
                        out error))
                {
                    error = "Invalid package entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) +
                        ": " + error;
                    return false;
                }

                string guid;
                string name;
                string versionText;
                string downloadUrl;
                string sha256;
                string installDirectory;
                string archiveRoot;
                string pluginFile;
                string configFile;
                long packageBytes;
                long maximumUnpackedBytes;
                List<string> runtimeDirectories;
                List<string> runtimeFiles;
                List<string> removeFiles;

                if (!TryGetRequiredString(package, "guid", out guid) ||
                    !TryGetRequiredString(package, "name", out name) ||
                    !TryGetRequiredString(package, "version", out versionText) ||
                    !TryGetRequiredString(package, "downloadUrl", out downloadUrl) ||
                    !TryGetRequiredString(package, "sha256", out sha256) ||
                    !TryGetRequiredLong(package, "packageBytes", out packageBytes) ||
                    !TryGetRequiredLong(package, "maximumUnpackedBytes", out maximumUnpackedBytes) ||
                    !TryGetRequiredString(package, "installDirectory", out installDirectory) ||
                    !TryGetRequiredString(package, "archiveRoot", out archiveRoot) ||
                    !TryGetRequiredString(package, "pluginFile", out pluginFile) ||
                    !TryGetRequiredString(package, "configFile", out configFile) ||
                    !TryGetStringArray(package, "runtimeDirectories", out runtimeDirectories) ||
                    !TryGetStringArray(package, "runtimeFiles", out runtimeFiles) ||
                    !TryGetStringArray(package, "removeFiles", out removeFiles))
                {
                    error = "Invalid package entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }

                PackageManifestEntry entry;
                if (!PackageManifestEntry.TryCreate(
                        guid,
                        name,
                        versionText,
                        downloadUrl,
                        sha256,
                        packageBytes,
                        maximumUnpackedBytes,
                        installDirectory,
                        archiveRoot,
                        pluginFile,
                        configFile,
                        runtimeDirectories,
                        runtimeFiles,
                        removeFiles,
                        out entry,
                        out error))
                {
                    error = "Invalid package entry at index " +
                        i.ToString(CultureInfo.InvariantCulture) +
                        ": " + error;
                    return false;
                }

                if (!seenGuids.Add(entry.Guid))
                {
                    error = "Duplicate package GUID: " + entry.Guid;
                    return false;
                }

                parsed.Entries.Add(entry);
            }

            manifest = parsed;
            return true;
        }

        private static bool TryGetRequiredInt(
            Dictionary<string, object> values,
            string key,
            out int value)
        {
            value = 0;
            long longValue;

            if (!TryGetRequiredLong(values, key, out longValue) ||
                longValue < int.MinValue ||
                longValue > int.MaxValue)
            {
                return false;
            }

            value = (int)longValue;
            return true;
        }

        private static bool TryGetRequiredLong(
            Dictionary<string, object> values,
            string key,
            out long value)
        {
            value = 0L;

            object raw;
            if (!values.TryGetValue(key, out raw) || !(raw is long))
            {
                return false;
            }

            value = (long)raw;
            return true;
        }

        private static bool TryGetRequiredString(
            Dictionary<string, object> values,
            string key,
            out string value)
        {
            value = null;

            object raw;
            if (!values.TryGetValue(key, out raw))
            {
                return false;
            }

            value = raw as string;
            return value != null;
        }

        private static bool TryGetStringArray(
            Dictionary<string, object> values,
            string key,
            out List<string> result)
        {
            result = null;

            object raw;
            if (!values.TryGetValue(key, out raw))
            {
                return false;
            }

            List<object> items = raw as List<object>;
            if (items == null || items.Count > 64)
            {
                return false;
            }

            List<string> parsed = new List<string>();
            HashSet<string> seen =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < items.Count; i++)
            {
                string value = items[i] as string;
                if (value == null)
                {
                    return false;
                }

                value =
                    value.Trim().Replace('\\', '/');

                if (!PackageManifestEntry.IsSafeRelativePath(value) ||
                    !seen.Add(value))
                {
                    return false;
                }

                parsed.Add(value);
            }

            result = parsed;
            return true;
        }

        private static bool HasOnlyKeys(
            Dictionary<string, object> values,
            string[] allowedKeys,
            out string error)
        {
            error = null;

            foreach (string key in values.Keys)
            {
                bool allowed = false;

                for (int i = 0; i < allowedKeys.Length; i++)
                {
                    if (string.Equals(
                            key,
                            allowedKeys[i],
                            StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    error = "Unknown JSON property '" + key + "'.";
                    return false;
                }
            }

            return true;
        }
    }

    internal sealed class PackageManifestEntry
    {
        private const string TrustedReleasePrefix =
            "https://github.com/Loupito75/Project-Hospital-Mods/releases/download/";

        internal string Guid { get; private set; }
        internal string Name { get; private set; }
        internal Version Version { get; private set; }
        internal string DownloadUrl { get; private set; }
        internal string Sha256 { get; private set; }
        internal long PackageBytes { get; private set; }
        internal long MaximumUnpackedBytes { get; private set; }
        internal string InstallDirectory { get; private set; }
        internal string ArchiveRoot { get; private set; }
        internal string PluginFile { get; private set; }
        internal string ConfigFile { get; private set; }
        internal List<string> RuntimeDirectories { get; private set; }
        internal List<string> RuntimeFiles { get; private set; }
        internal List<string> RemoveFiles { get; private set; }

        private PackageManifestEntry()
        {
        }

        internal static bool TryCreate(
            string guid,
            string name,
            string versionText,
            string downloadUrl,
            string sha256,
            long packageBytes,
            long maximumUnpackedBytes,
            string installDirectory,
            string archiveRoot,
            string pluginFile,
            string configFile,
            List<string> runtimeDirectories,
            List<string> runtimeFiles,
            List<string> removeFiles,
            out PackageManifestEntry entry,
            out string error)
        {
            entry = null;
            error = null;

            guid = (guid ?? string.Empty).Trim();
            name = (name ?? string.Empty).Trim();
            versionText = (versionText ?? string.Empty).Trim();
            downloadUrl = (downloadUrl ?? string.Empty).Trim();
            sha256 = (sha256 ?? string.Empty).Trim().ToLowerInvariant();
            installDirectory = (installDirectory ?? string.Empty).Trim();
            archiveRoot = (archiveRoot ?? string.Empty).Trim().Replace('\\', '/');
            pluginFile = (pluginFile ?? string.Empty).Trim().Replace('\\', '/');
            configFile = (configFile ?? string.Empty).Trim();

            if (!IsSafeGuid(guid) ||
                name.Length == 0 ||
                name.Length > 128 ||
                name.IndexOf('<') >= 0 ||
                name.IndexOf('>') >= 0)
            {
                error = "Invalid package identity.";
                return false;
            }

            Version version;
            try
            {
                version = new Version(versionText);
            }
            catch
            {
                error = "Invalid package version.";
                return false;
            }

            if (!IsTrustedReleaseUrl(downloadUrl))
            {
                error = "Package download URL is not an approved GitHub Release asset.";
                return false;
            }

            if (!IsSha256(sha256))
            {
                error = "Invalid SHA-256.";
                return false;
            }

            if (packageBytes <= 0 ||
                packageBytes > PackageManifest.MaximumPackageBytes ||
                maximumUnpackedBytes <= 0 ||
                maximumUnpackedBytes > PackageManifest.MaximumUnpackedBytes)
            {
                error = "Invalid package size limits.";
                return false;
            }

            if (!IsSafeDirectoryName(installDirectory) ||
                !installDirectory.StartsWith(
                    "Loupito75.",
                    StringComparison.Ordinal))
            {
                error = "Invalid install directory.";
                return false;
            }

            if (!guid.StartsWith(
                    "loupito75.",
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "Only official Loupito75 plugin GUIDs are supported.";
                return false;
            }

            if (archiveRoot.Length > 0 &&
                !IsSafeRelativePath(archiveRoot))
            {
                error = "Invalid archive root.";
                return false;
            }

            if (!IsSafeRelativePath(pluginFile) ||
                !pluginFile.EndsWith(
                    ".dll",
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "Invalid plugin filename.";
                return false;
            }

            string expectedPluginFile =
                installDirectory + ".dll";

            if (!string.Equals(
                    pluginFile,
                    expectedPluginFile,
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "pluginFile must match the official Loupito75 plugin filename.";
                return false;
            }

            string configPrefix =
                installDirectory + ".";

            if (!IsSafeFileName(configFile) ||
                !configFile.StartsWith(
                    configPrefix,
                    StringComparison.OrdinalIgnoreCase) ||
                !configFile.EndsWith(
                    ".xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "Invalid configuration filename.";
                return false;
            }

            if (runtimeDirectories == null ||
                runtimeFiles == null ||
                runtimeFiles.Count == 0 ||
                removeFiles == null)
            {
                error = "Package ownership metadata is incomplete.";
                return false;
            }

            bool pluginOwned = false;

            for (int i = 0; i < runtimeFiles.Count; i++)
            {
                if (string.Equals(
                        runtimeFiles[i],
                        configFile,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error =
                        "runtimeFiles must never overwrite the user configuration.";
                    return false;
                }

                if (string.Equals(
                        runtimeFiles[i],
                        pluginFile,
                        StringComparison.OrdinalIgnoreCase))
                {
                    pluginOwned = true;
                }
            }

            if (!pluginOwned)
            {
                error = "pluginFile must be present in runtimeFiles.";
                return false;
            }

            for (int i = 0; i < removeFiles.Count; i++)
            {
                if (string.Equals(
                        removeFiles[i],
                        configFile,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "removeFiles must never include the user configuration.";
                    return false;
                }
            }

            entry = new PackageManifestEntry
            {
                Guid = guid,
                Name = name,
                Version = version,
                DownloadUrl = downloadUrl,
                Sha256 = sha256,
                PackageBytes = packageBytes,
                MaximumUnpackedBytes = maximumUnpackedBytes,
                InstallDirectory = installDirectory,
                ArchiveRoot = archiveRoot,
                PluginFile = pluginFile,
                ConfigFile = configFile,
                RuntimeDirectories = runtimeDirectories,
                RuntimeFiles = runtimeFiles,
                RemoveFiles = removeFiles
            };

            return true;
        }

        internal static bool IsSafeRelativePath(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.Length > 240 ||
                value[0] == '/' ||
                value[0] == '\\' ||
                value.IndexOf(':') >= 0 ||
                value.IndexOf('\0') >= 0)
            {
                return false;
            }

            string normalized = value.Replace('\\', '/');
            string[] parts = normalized.Split('/');

            for (int i = 0; i < parts.Length; i++)
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

        private static bool IsSafeFileName(string value)
        {
            return IsSafeDirectoryName(value) &&
                value.IndexOf('.') >= 0;
        }

        private static bool IsSafeDirectoryName(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.Length > 128 ||
                value == "." ||
                value == ".." ||
                value.IndexOf('/') >= 0 ||
                value.IndexOf('\\') >= 0 ||
                value.IndexOf(':') >= 0 ||
                value.IndexOf('\0') >= 0)
            {
                return false;
            }

            return true;
        }

        private static bool IsSafeGuid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool alphaNumeric =
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9');

                if (!(alphaNumeric || c == '.' || c == '_' || c == '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hex =
                    (c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'f');

                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsTrustedReleaseUrl(string value)
        {
            return value != null &&
                value.StartsWith(
                    TrustedReleasePrefix,
                    StringComparison.Ordinal) &&
                value.IndexOf('?') < 0 &&
                value.IndexOf('#') < 0;
        }
    }
}
