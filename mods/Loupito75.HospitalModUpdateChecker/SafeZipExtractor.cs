using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HospitalModUpdateChecker
{
    internal static class SafeZipExtractor
    {
        private const uint EndOfCentralDirectorySignature = 0x06054b50U;
        private const uint CentralDirectorySignature = 0x02014b50U;
        private const uint LocalFileHeaderSignature = 0x04034b50U;
        private const int MaximumEntries = 4096;
        private const int CopyBufferSize = 32768;

        private sealed class ZipEntryInfo
        {
            internal string Name;
            internal ushort Flags;
            internal ushort Method;
            internal uint Crc32;
            internal uint CompressedSize;
            internal uint UncompressedSize;
            internal uint LocalHeaderOffset;
            internal uint ExternalAttributes;
            internal byte HostSystem;
            internal bool IsDirectory;
        }

        internal static bool Extract(
            string zipPath,
            string destinationDirectory,
            string archiveRoot,
            long maximumUnpackedBytes,
            out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(zipPath) ||
                !File.Exists(zipPath))
            {
                error = "Package ZIP is missing.";
                return false;
            }

            if (string.IsNullOrEmpty(destinationDirectory))
            {
                error = "Package extraction destination is missing.";
                return false;
            }

            try
            {
                List<ZipEntryInfo> entries;

                using (FileStream stream = new FileStream(
                    zipPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (!TryReadCentralDirectory(
                            stream,
                            reader,
                            maximumUnpackedBytes,
                            out entries,
                            out error))
                    {
                        return false;
                    }

                    string fullDestination =
                        Path.GetFullPath(destinationDirectory)
                        .TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar);

                    if (Directory.Exists(fullDestination))
                    {
                        Directory.Delete(fullDestination, true);
                    }

                    Directory.CreateDirectory(fullDestination);

                    string normalizedRoot =
                        NormalizeArchiveRoot(archiveRoot);

                    HashSet<string> extractedPaths =
                        new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase);

                    for (int i = 0; i < entries.Count; i++)
                    {
                        ZipEntryInfo entry = entries[i];
                        string relativePath;

                        if (!TryGetInstallRelativePath(
                                entry.Name,
                                normalizedRoot,
                                out relativePath))
                        {
                            continue;
                        }

                        if (relativePath.Length == 0)
                        {
                            continue;
                        }

                        if (!extractedPaths.Add(relativePath))
                        {
                            error =
                                "Package ZIP contains duplicate paths: " +
                                relativePath;
                            Directory.Delete(
                                fullDestination,
                                true);
                            return false;
                        }

                        string outputPath =
                            GetSafeDestinationPath(
                                fullDestination,
                                relativePath);

                        if (outputPath == null)
                        {
                            error = "Package contains an unsafe path: " +
                                entry.Name;
                            Directory.Delete(fullDestination, true);
                            return false;
                        }

                        if (entry.IsDirectory)
                        {
                            Directory.CreateDirectory(outputPath);
                            continue;
                        }

                        string parent = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            Directory.CreateDirectory(parent);
                        }

                        if (!ExtractEntry(
                                stream,
                                reader,
                                entry,
                                outputPath,
                                out error))
                        {
                            Directory.Delete(fullDestination, true);
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error =
                    exception.GetType().Name +
                    ": " +
                    exception.Message;

                try
                {
                    if (Directory.Exists(destinationDirectory))
                    {
                        Directory.Delete(destinationDirectory, true);
                    }
                }
                catch
                {
                }

                return false;
            }
        }

        private static bool TryReadCentralDirectory(
            FileStream stream,
            BinaryReader reader,
            long maximumUnpackedBytes,
            out List<ZipEntryInfo> entries,
            out string error)
        {
            entries = null;
            error = null;

            if (stream.Length < 22)
            {
                error = "Package ZIP is too small.";
                return false;
            }

            long searchLength = Math.Min(stream.Length, 65557L);
            long searchStart = stream.Length - searchLength;

            stream.Position = searchStart;
            byte[] tail = reader.ReadBytes((int)searchLength);

            int eocdIndex = -1;

            for (int i = tail.Length - 22; i >= 0; i--)
            {
                if (ReadUInt32(tail, i) ==
                    EndOfCentralDirectorySignature)
                {
                    eocdIndex = i;
                    break;
                }
            }

            if (eocdIndex < 0)
            {
                error = "Package ZIP has no central directory.";
                return false;
            }

            int offset = eocdIndex + 4;

            ushort diskNumber = ReadUInt16(tail, offset);
            offset += 2;
            ushort centralDiskNumber = ReadUInt16(tail, offset);
            offset += 2;
            ushort entriesOnDisk = ReadUInt16(tail, offset);
            offset += 2;
            ushort totalEntries = ReadUInt16(tail, offset);
            offset += 2;
            uint centralSize = ReadUInt32(tail, offset);
            offset += 4;
            uint centralOffset = ReadUInt32(tail, offset);
            offset += 4;
            ushort commentLength = ReadUInt16(tail, offset);

            if (diskNumber != 0 ||
                centralDiskNumber != 0 ||
                entriesOnDisk != totalEntries)
            {
                error = "Multi-disk ZIP packages are not supported.";
                return false;
            }

            if (totalEntries == ushort.MaxValue ||
                centralSize == uint.MaxValue ||
                centralOffset == uint.MaxValue)
            {
                error = "ZIP64 packages are not supported.";
                return false;
            }

            if (totalEntries > MaximumEntries)
            {
                error = "Package ZIP contains too many entries.";
                return false;
            }

            if ((long)eocdIndex + searchStart + 22L + commentLength >
                stream.Length)
            {
                error = "Package ZIP end record is invalid.";
                return false;
            }

            if ((long)centralOffset + centralSize > stream.Length)
            {
                error = "Package ZIP central directory is invalid.";
                return false;
            }

            List<ZipEntryInfo> parsed =
                new List<ZipEntryInfo>();

            HashSet<string> seenPaths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            long totalUnpacked = 0L;

            stream.Position = centralOffset;

            for (int i = 0; i < totalEntries; i++)
            {
                if (reader.ReadUInt32() !=
                    CentralDirectorySignature)
                {
                    error = "Package ZIP central directory is corrupt.";
                    return false;
                }

                ushort versionMadeBy = reader.ReadUInt16();
                reader.ReadUInt16();
                ushort flags = reader.ReadUInt16();
                ushort method = reader.ReadUInt16();
                reader.ReadUInt16();
                reader.ReadUInt16();
                uint crc32 = reader.ReadUInt32();
                uint compressedSize = reader.ReadUInt32();
                uint uncompressedSize = reader.ReadUInt32();
                ushort nameLength = reader.ReadUInt16();
                ushort extraLength = reader.ReadUInt16();
                ushort entryCommentLength = reader.ReadUInt16();
                ushort diskStart = reader.ReadUInt16();
                reader.ReadUInt16();
                uint externalAttributes = reader.ReadUInt32();
                uint localHeaderOffset = reader.ReadUInt32();

                if (diskStart != 0 ||
                    compressedSize == uint.MaxValue ||
                    uncompressedSize == uint.MaxValue ||
                    localHeaderOffset == uint.MaxValue)
                {
                    error = "ZIP64 or multi-disk entries are not supported.";
                    return false;
                }

                if ((flags & 0x0001) != 0)
                {
                    error = "Encrypted ZIP entries are not supported.";
                    return false;
                }

                if (method != 0 && method != 8)
                {
                    error = "Unsupported ZIP compression method.";
                    return false;
                }

                byte[] nameBytes = reader.ReadBytes(nameLength);
                if (nameBytes.Length != nameLength)
                {
                    error = "Package ZIP entry name is truncated.";
                    return false;
                }

                string name = DecodeEntryName(nameBytes, flags);

                if (!IsSafeArchiveEntryName(name))
                {
                    error = "Package ZIP contains an unsafe path.";
                    return false;
                }

                string uniqueName =
                    name.TrimEnd('/');

                if (!seenPaths.Add(uniqueName))
                {
                    error = "Package ZIP contains duplicate paths.";
                    return false;
                }

                if (IsUnixSymlink(
                        (byte)(versionMadeBy >> 8),
                        externalAttributes))
                {
                    error = "Package ZIP contains a symbolic link.";
                    return false;
                }

                if (extraLength > 0)
                {
                    reader.BaseStream.Seek(
                        extraLength,
                        SeekOrigin.Current);
                }

                if (entryCommentLength > 0)
                {
                    reader.BaseStream.Seek(
                        entryCommentLength,
                        SeekOrigin.Current);
                }

                bool isDirectory =
                    name.EndsWith("/", StringComparison.Ordinal);

                if (!isDirectory)
                {
                    totalUnpacked += uncompressedSize;

                    if (totalUnpacked < 0 ||
                        totalUnpacked > maximumUnpackedBytes)
                    {
                        error = "Package expands beyond the allowed size.";
                        return false;
                    }
                }

                parsed.Add(
                    new ZipEntryInfo
                    {
                        Name = name,
                        Flags = flags,
                        Method = method,
                        Crc32 = crc32,
                        CompressedSize = compressedSize,
                        UncompressedSize = uncompressedSize,
                        LocalHeaderOffset = localHeaderOffset,
                        ExternalAttributes = externalAttributes,
                        HostSystem = (byte)(versionMadeBy >> 8),
                        IsDirectory = isDirectory
                    });
            }

            entries = parsed;
            return true;
        }

        private static bool ExtractEntry(
            FileStream stream,
            BinaryReader reader,
            ZipEntryInfo entry,
            string outputPath,
            out string error)
        {
            error = null;

            stream.Position = entry.LocalHeaderOffset;

            if (reader.ReadUInt32() != LocalFileHeaderSignature)
            {
                error = "Package ZIP local header is corrupt.";
                return false;
            }

            reader.ReadUInt16();
            ushort flags = reader.ReadUInt16();
            ushort method = reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadUInt32();
            ushort nameLength = reader.ReadUInt16();
            ushort extraLength = reader.ReadUInt16();

            if (flags != entry.Flags ||
                method != entry.Method)
            {
                error = "Package ZIP entry headers disagree.";
                return false;
            }

            long dataStart =
                stream.Position +
                nameLength +
                extraLength;

            if (dataStart < 0 ||
                dataStart + entry.CompressedSize > stream.Length)
            {
                error = "Package ZIP entry data is truncated.";
                return false;
            }

            stream.Position = dataStart;

            Crc32 crc = new Crc32();
            long written = 0L;

            using (FileStream output = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                if (entry.Method == 0)
                {
                    written = CopyStored(
                        stream,
                        output,
                        entry.CompressedSize,
                        crc);
                }
                else
                {
                    using (LimitedReadStream limited =
                        new LimitedReadStream(
                            stream,
                            entry.CompressedSize))
                    using (DeflateStream deflate =
                        new DeflateStream(
                            limited,
                            CompressionMode.Decompress,
                            true))
                    {
                        written = CopyInflated(
                            deflate,
                            output,
                            crc,
                            entry.UncompressedSize);
                    }
                }
            }

            if (written != entry.UncompressedSize)
            {
                error = "Package ZIP entry size verification failed.";
                return false;
            }

            if (crc.Value != entry.Crc32)
            {
                error = "Package ZIP entry CRC verification failed.";
                return false;
            }

            return true;
        }

        private static long CopyStored(
            Stream input,
            Stream output,
            uint bytes,
            Crc32 crc)
        {
            byte[] buffer = new byte[CopyBufferSize];
            long remaining = bytes;
            long written = 0L;

            while (remaining > 0)
            {
                int count = (int)Math.Min(
                    (long)buffer.Length,
                    remaining);

                int read = input.Read(buffer, 0, count);

                if (read <= 0)
                {
                    throw new EndOfStreamException();
                }

                output.Write(buffer, 0, read);
                crc.Update(buffer, 0, read);
                remaining -= read;
                written += read;
            }

            return written;
        }

        private static long CopyInflated(
            Stream input,
            Stream output,
            Crc32 crc,
            uint expectedBytes)
        {
            byte[] buffer = new byte[CopyBufferSize];
            long written = 0L;

            while (true)
            {
                int read = input.Read(
                    buffer,
                    0,
                    buffer.Length);

                if (read <= 0)
                {
                    break;
                }

                written += read;

                if (written > expectedBytes)
                {
                    throw new IOException(
                        "Inflated ZIP entry is larger than declared.");
                }

                output.Write(buffer, 0, read);
                crc.Update(buffer, 0, read);
            }

            return written;
        }

        private static string DecodeEntryName(
            byte[] bytes,
            ushort flags)
        {
            if ((flags & 0x0800) != 0)
            {
                return Encoding.UTF8.GetString(bytes)
                    .Replace('\\', '/');
            }

            try
            {
                return Encoding.GetEncoding(437)
                    .GetString(bytes)
                    .Replace('\\', '/');
            }
            catch
            {
                return Encoding.ASCII.GetString(bytes)
                    .Replace('\\', '/');
            }
        }

        private static bool IsSafeArchiveEntryName(string name)
        {
            if (string.IsNullOrEmpty(name) ||
                name.Length > 512 ||
                name[0] == '/' ||
                name[0] == '\\' ||
                name.IndexOf(':') >= 0 ||
                name.IndexOf('\0') >= 0)
            {
                return false;
            }

            string[] parts =
                name.Replace('\\', '/').Split('/');

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                {
                    if (i == parts.Length - 1)
                    {
                        continue;
                    }

                    return false;
                }

                if (parts[i] == "." ||
                    parts[i] == "..")
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsUnixSymlink(
            byte hostSystem,
            uint externalAttributes)
        {
            if (hostSystem != 3)
            {
                return false;
            }

            uint mode =
                (externalAttributes >> 16) & 0xFFFFU;

            return (mode & 0xF000U) == 0xA000U;
        }

        private static string NormalizeArchiveRoot(
            string archiveRoot)
        {
            string root =
                (archiveRoot ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .Trim('/');

            return root.Length == 0
                ? string.Empty
                : root + "/";
        }

        private static bool TryGetInstallRelativePath(
            string entryName,
            string normalizedRoot,
            out string relativePath)
        {
            string normalized =
                entryName.Replace('\\', '/');

            if (normalizedRoot.Length == 0)
            {
                relativePath =
                    normalized.TrimEnd('/');
                return true;
            }

            if (string.Equals(
                    normalized.TrimEnd('/'),
                    normalizedRoot.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                relativePath = string.Empty;
                return true;
            }

            if (!normalized.StartsWith(
                    normalizedRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                relativePath = null;
                return false;
            }

            relativePath =
                normalized.Substring(normalizedRoot.Length)
                .TrimEnd('/');

            return true;
        }

        private static string GetSafeDestinationPath(
            string destinationRoot,
            string relativePath)
        {
            string normalized =
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar);

            string fullPath =
                Path.GetFullPath(
                    Path.Combine(
                        destinationRoot,
                        normalized));

            string prefix =
                destinationRoot +
                Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return fullPath;
        }

        private static ushort ReadUInt16(
            byte[] buffer,
            int offset)
        {
            return (ushort)(
                buffer[offset] |
                (buffer[offset + 1] << 8));
        }

        private static uint ReadUInt32(
            byte[] buffer,
            int offset)
        {
            return (uint)(
                buffer[offset] |
                (buffer[offset + 1] << 8) |
                (buffer[offset + 2] << 16) |
                (buffer[offset + 3] << 24));
        }

        private sealed class LimitedReadStream : Stream
        {
            private readonly Stream _inner;
            private long _remaining;

            internal LimitedReadStream(
                Stream inner,
                long length)
            {
                _inner = inner;
                _remaining = length;
            }

            public override bool CanRead
            {
                get { return true; }
            }

            public override bool CanSeek
            {
                get { return false; }
            }

            public override bool CanWrite
            {
                get { return false; }
            }

            public override long Length
            {
                get { throw new NotSupportedException(); }
            }

            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override void Flush()
            {
            }

            public override int Read(
                byte[] buffer,
                int offset,
                int count)
            {
                if (_remaining <= 0)
                {
                    return 0;
                }

                int allowed =
                    (int)Math.Min(
                        (long)count,
                        _remaining);

                int read =
                    _inner.Read(
                        buffer,
                        offset,
                        allowed);

                _remaining -= read;
                return read;
            }

            public override long Seek(
                long offset,
                SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(
                byte[] buffer,
                int offset,
                int count)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class Crc32
        {
            private static readonly uint[] Table =
                CreateTable();

            private uint _value = 0xFFFFFFFFU;

            internal uint Value
            {
                get { return _value ^ 0xFFFFFFFFU; }
            }

            internal void Update(
                byte[] buffer,
                int offset,
                int count)
            {
                uint value = _value;

                for (int i = 0; i < count; i++)
                {
                    value =
                        Table[
                            (value ^ buffer[offset + i]) &
                            0xFFU] ^
                        (value >> 8);
                }

                _value = value;
            }

            private static uint[] CreateTable()
            {
                uint[] table = new uint[256];

                for (uint i = 0; i < table.Length; i++)
                {
                    uint value = i;

                    for (int bit = 0; bit < 8; bit++)
                    {
                        value =
                            (value & 1U) != 0
                                ? 0xEDB88320U ^ (value >> 1)
                                : value >> 1;
                    }

                    table[i] = value;
                }

                return table;
            }
        }
    }
}
