using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace HospitalModUpdateChecker
{
    internal static class DiskSpaceHelper
    {
        internal static bool TryGetAvailableFreeSpace(
            string path,
            out long availableBytes,
            out string error)
        {
            availableBytes = 0L;
            error = null;

            string fullPath;

            try
            {
                fullPath =
                    Path.GetFullPath(
                        path);
            }
            catch (Exception exception)
            {
                error =
                    exception.GetType().Name +
                    ": " +
                    exception.Message;

                return false;
            }

            if (Environment.OSVersion.Platform ==
                PlatformID.Win32NT)
            {
                return TryGetWindowsAvailableFreeSpace(
                    fullPath,
                    out availableBytes,
                    out error);
            }

            DriveInfo drive;

            if (TryFindDriveForPath(
                    fullPath,
                    out drive))
            {
                try
                {
                    availableBytes =
                        drive.AvailableFreeSpace;

                    return true;
                }
                catch (Exception exception)
                {
                    error =
                        exception.GetType().Name +
                        ": " +
                        exception.Message;

                    return false;
                }
            }

            // Older Mono implementations may not enumerate every mount point.
            // Keep the former syntax-root behavior as a conservative fallback
            // instead of failing a package preparation solely because the
            // improved volume lookup is unavailable.
            try
            {
                string root =
                    Path.GetPathRoot(
                        fullPath);

                if (string.IsNullOrEmpty(root))
                {
                    error =
                        "Unable to determine the filesystem root.";

                    return false;
                }

                drive =
                    new DriveInfo(
                        root);

                availableBytes =
                    drive.AvailableFreeSpace;

                return true;
            }
            catch (Exception exception)
            {
                error =
                    exception.GetType().Name +
                    ": " +
                    exception.Message;

                return false;
            }
        }

        internal static bool TryFindDriveForPath(
            string path,
            out DriveInfo bestDrive)
        {
            bestDrive = null;

            string fullPath;

            try
            {
                fullPath =
                    Path.GetFullPath(
                        path);
            }
            catch
            {
                return false;
            }

            DriveInfo[] drives;

            try
            {
                drives =
                    DriveInfo.GetDrives();
            }
            catch
            {
                return false;
            }

            int bestLength =
                -1;

            for (int i = 0;
                i < drives.Length;
                i++)
            {
                DriveInfo drive =
                    drives[i];

                string root;

                try
                {
                    if (drive == null ||
                        !drive.IsReady)
                    {
                        continue;
                    }

                    root =
                        NormalizeRoot(
                            drive.RootDirectory.FullName);
                }
                catch
                {
                    continue;
                }

                if (!IsPathUnderRoot(
                        fullPath,
                        root))
                {
                    continue;
                }

                if (root.Length >
                    bestLength)
                {
                    bestDrive =
                        drive;

                    bestLength =
                        root.Length;
                }
            }

            return bestDrive != null;
        }

        internal static string NormalizeRoot(
            string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string full =
                Path.GetFullPath(
                    path);

            string root =
                Path.GetPathRoot(
                    full);

            if (string.Equals(
                    full,
                    root,
                    StringComparison.Ordinal))
            {
                return full;
            }

            return full.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static bool IsPathUnderRoot(
            string fullPath,
            string normalizedRoot)
        {
            if (string.IsNullOrEmpty(fullPath) ||
                string.IsNullOrEmpty(normalizedRoot))
            {
                return false;
            }

            if (string.Equals(
                    fullPath,
                    normalizedRoot,
                    StringComparison.Ordinal))
            {
                return true;
            }

            string rootOfRoot =
                Path.GetPathRoot(
                    normalizedRoot);

            if (string.Equals(
                    normalizedRoot,
                    rootOfRoot,
                    StringComparison.Ordinal))
            {
                return fullPath.StartsWith(
                    normalizedRoot,
                    StringComparison.Ordinal);
            }

            return fullPath.StartsWith(
                normalizedRoot +
                    Path.DirectorySeparatorChar,
                StringComparison.Ordinal);
        }

        private static bool TryGetWindowsAvailableFreeSpace(
            string path,
            out long availableBytes,
            out string error)
        {
            availableBytes = 0L;
            error = null;

            try
            {
                ulong freeBytesAvailable;
                ulong totalNumberOfBytes;
                ulong totalNumberOfFreeBytes;

                if (!NativeMethods.GetDiskFreeSpaceEx(
                        path,
                        out freeBytesAvailable,
                        out totalNumberOfBytes,
                        out totalNumberOfFreeBytes))
                {
                    error =
                        "GetDiskFreeSpaceEx failed with Win32 error " +
                        Marshal.GetLastWin32Error().ToString(
                            CultureInfo.InvariantCulture) +
                        ".";

                    return false;
                }

                availableBytes =
                    freeBytesAvailable >
                        (ulong)long.MaxValue
                        ? long.MaxValue
                        : (long)freeBytesAvailable;

                return true;
            }
            catch (Exception exception)
            {
                error =
                    exception.GetType().Name +
                    ": " +
                    exception.Message;

                return false;
            }
        }

        private static class NativeMethods
        {
            [DllImport(
                "kernel32.dll",
                CharSet = CharSet.Unicode,
                SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetDiskFreeSpaceEx(
                string directoryName,
                out ulong freeBytesAvailable,
                out ulong totalNumberOfBytes,
                out ulong totalNumberOfFreeBytes);
        }
    }
}
