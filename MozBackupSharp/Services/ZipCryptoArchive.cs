using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MozBackupSharp.Core;

namespace MozBackupSharp.Services
{
    /// <summary>
    /// Compatibility layer for original MozBackup password-protected PCV files.
    /// MozBackup used ZipMaster AddEncrypt, which creates standard ZIP entries
    /// protected with traditional PKWARE/ZipCrypto encryption.
    /// </summary>
    public static class ZipCryptoArchive
    {
        private const uint LocalHeaderSignature = 0x04034b50;
        private const uint CentralHeaderSignature = 0x02014b50;
        private const uint EndOfCentralDirectorySignature = 0x06054b50;
        private const ushort FlagEncrypted = 0x0001;
        private const ushort FlagDataDescriptor = 0x0008;
        private const ushort FlagUtf8 = 0x0800;
        private const ushort CompressionStored = 0;
        private const ushort CompressionDeflated = 8;

        public static bool IsEncryptedZip(string zipPath)
        {
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath) || ProtectedArchive.IsEncrypted(zipPath))
                return false;

            try
            {
                List<ZipEntryInfo> entries = ReadCentralDirectory(zipPath);
                foreach (ZipEntryInfo entry in entries)
                {
                    if ((entry.Flags & FlagEncrypted) != 0)
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public static void EncryptZipFile(string plainZipPath, string outputZipPath, string password, IProgress<BackupProgress> progress)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password is required.", "password");

            Report(progress, "Creating MozBackup-compatible protected archive...", 99);
            List<ZipEntryInfo> entries = ReadCentralDirectory(plainZipPath);
            EndRecord endRecord = ReadEndRecord(plainZipPath);

            PathHelpers.TryDeleteFile(outputZipPath);
            using (var input = new FileStream(plainZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(outputZipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(output))
            {
                foreach (ZipEntryInfo entry in entries)
                    WriteEncryptedOrPlainLocalEntry(input, writer, entry, password);

                long centralDirectoryOffset = output.Position;
                foreach (ZipEntryInfo entry in entries)
                    WriteCentralDirectoryEntry(writer, entry);

                long centralDirectorySize = output.Position - centralDirectoryOffset;
                if (centralDirectoryOffset > uint.MaxValue || centralDirectorySize > uint.MaxValue || entries.Count > ushort.MaxValue)
                    throw new NotSupportedException("Zip64 archives are not supported by the MozBackup-compatible password mode yet.");

                writer.Write(EndOfCentralDirectorySignature);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)entries.Count);
                writer.Write((ushort)entries.Count);
                writer.Write((uint)centralDirectorySize);
                writer.Write((uint)centralDirectoryOffset);
                writer.Write((ushort)(endRecord.Comment == null ? 0 : endRecord.Comment.Length));
                if (endRecord.Comment != null && endRecord.Comment.Length > 0)
                    writer.Write(endRecord.Comment);
            }
        }

        public static string ReadTextEntry(string zipPath, string entryName, string password)
        {
            List<ZipEntryInfo> entries = ReadCentralDirectory(zipPath);
            foreach (ZipEntryInfo entry in entries)
            {
                if (!string.Equals(entry.Name, entryName, StringComparison.OrdinalIgnoreCase))
                    continue;

                using (MemoryStream memory = new MemoryStream())
                {
                    ExtractEntryToStream(zipPath, entry, memory, password);
                    memory.Position = 0;
                    using (var reader = new StreamReader(memory))
                        return reader.ReadToEnd();
                }
            }

            return null;
        }

        public static int ExtractToDirectory(string zipPath, string targetRoot, bool overwriteExisting, string password, IProgress<BackupProgress> progress)
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidArchivePasswordException("Password is required to restore this protected backup.");

            List<ZipEntryInfo> entries = ReadCentralDirectory(zipPath);
            int restored = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                ZipEntryInfo entry = entries[i];
                if (IsMetadataEntry(entry.Name))
                    continue;

                string destinationPath = GetSafeDestinationPath(targetRoot, entry.Name);
                int percent = entries.Count == 0 ? 100 : (int)(((long)(i + 1) * 100L) / entries.Count);
                Report(progress, "Restoring " + entry.Name, percent);

                if (entry.IsDirectory)
                {
                    if (!Directory.Exists(destinationPath))
                        Directory.CreateDirectory(destinationPath);
                    continue;
                }

                if (File.Exists(destinationPath) && !overwriteExisting)
                    continue;

                string destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!Directory.Exists(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                ExtractEntryToFile(zipPath, entry, destinationPath, password);
                restored++;
            }

            return restored;
        }

        private static void WriteEncryptedOrPlainLocalEntry(FileStream input, BinaryWriter writer, ZipEntryInfo entry, string password)
        {
            entry.OutputLocalHeaderOffset = writer.BaseStream.Position;

            bool encrypt = !entry.IsDirectory;
            ushort outputFlags = encrypt ? (ushort)((entry.Flags | FlagEncrypted) & ~FlagDataDescriptor) : (ushort)(entry.Flags & ~FlagDataDescriptor);
            uint outputCompressedSize = encrypt ? checked(entry.CompressedSize + 12U) : entry.CompressedSize;

            writer.Write(LocalHeaderSignature);
            writer.Write(entry.VersionNeeded < 20 && encrypt ? (ushort)20 : entry.VersionNeeded);
            writer.Write(outputFlags);
            writer.Write(entry.CompressionMethod);
            writer.Write(entry.LastModTime);
            writer.Write(entry.LastModDate);
            writer.Write(entry.Crc32);
            writer.Write(outputCompressedSize);
            writer.Write(entry.UncompressedSize);
            writer.Write((ushort)entry.NameBytes.Length);
            writer.Write((ushort)entry.LocalExtraBytes.Length);
            writer.Write(entry.NameBytes);
            writer.Write(entry.LocalExtraBytes);

            input.Position = entry.DataOffset;
            if (encrypt)
            {
                ZipCryptoTransform crypto = new ZipCryptoTransform(password);
                byte[] header = CreateEncryptionHeader(entry.Crc32);
                EncryptAndWriteBytes(writer.BaseStream, crypto, header, 0, header.Length);
                CopyEncryptedBytes(input, writer.BaseStream, entry.CompressedSize, crypto);
            }
            else
            {
                CopyBytes(input, writer.BaseStream, entry.CompressedSize);
            }

            entry.OutputFlags = outputFlags;
            entry.OutputCompressedSize = outputCompressedSize;
        }

        private static byte[] CreateEncryptionHeader(uint crc32)
        {
            byte[] header = new byte[12];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(header);
            header[11] = (byte)((crc32 >> 24) & 0xff);
            return header;
        }

        private static void EncryptAndWriteBytes(Stream output, ZipCryptoTransform crypto, byte[] buffer, int offset, int count)
        {
            byte[] encrypted = new byte[count];
            for (int i = 0; i < count; i++)
                encrypted[i] = crypto.EncryptByte(buffer[offset + i]);
            output.Write(encrypted, 0, encrypted.Length);
        }

        private static void CopyEncryptedBytes(Stream input, Stream output, uint count, ZipCryptoTransform crypto)
        {
            byte[] buffer = new byte[1024 * 128];
            long remaining = count;
            while (remaining > 0)
            {
                int wanted = (int)Math.Min(buffer.Length, remaining);
                int read = input.Read(buffer, 0, wanted);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of ZIP entry.");

                for (int i = 0; i < read; i++)
                    buffer[i] = crypto.EncryptByte(buffer[i]);

                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static void WriteCentralDirectoryEntry(BinaryWriter writer, ZipEntryInfo entry)
        {
            if (entry.OutputLocalHeaderOffset > uint.MaxValue)
                throw new NotSupportedException("Zip64 archives are not supported by the MozBackup-compatible password mode yet.");

            writer.Write(CentralHeaderSignature);
            writer.Write(entry.VersionMadeBy);
            writer.Write(entry.VersionNeeded < 20 && (entry.OutputFlags & FlagEncrypted) != 0 ? (ushort)20 : entry.VersionNeeded);
            writer.Write(entry.OutputFlags);
            writer.Write(entry.CompressionMethod);
            writer.Write(entry.LastModTime);
            writer.Write(entry.LastModDate);
            writer.Write(entry.Crc32);
            writer.Write(entry.OutputCompressedSize);
            writer.Write(entry.UncompressedSize);
            writer.Write((ushort)entry.NameBytes.Length);
            writer.Write((ushort)entry.CentralExtraBytes.Length);
            writer.Write((ushort)entry.CommentBytes.Length);
            writer.Write((ushort)0);
            writer.Write(entry.InternalAttributes);
            writer.Write(entry.ExternalAttributes);
            writer.Write((uint)entry.OutputLocalHeaderOffset);
            writer.Write(entry.NameBytes);
            writer.Write(entry.CentralExtraBytes);
            writer.Write(entry.CommentBytes);
        }

        private static void ExtractEntryToFile(string zipPath, ZipEntryInfo entry, string destinationPath, string password)
        {
            using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                ExtractEntryToStream(zipPath, entry, output, password);
            }

            try
            {
                File.SetLastWriteTime(destinationPath, DosDateTimeToDateTime(entry.LastModDate, entry.LastModTime));
            }
            catch
            {
                // Best effort only.
            }
        }

        private static void ExtractEntryToStream(string zipPath, ZipEntryInfo entry, Stream output, string password)
        {
            using (var input = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                input.Position = entry.DataOffset;
                Stream compressedStream = input;
                long compressedBytes = entry.CompressedSize;
                ZipCryptoReadStream cryptoStream = null;
                BoundedReadStream boundedStream = null;

                try
                {
                    if ((entry.Flags & FlagEncrypted) != 0)
                    {
                        if (string.IsNullOrEmpty(password))
                            throw new InvalidArchivePasswordException("Password is required to restore this protected backup.");

                        ZipCryptoTransform crypto = new ZipCryptoTransform(password);
                        byte[] encryptedHeader = ReadExact(input, 12);
                        byte[] plainHeader = new byte[12];
                        for (int i = 0; i < encryptedHeader.Length; i++)
                            plainHeader[i] = crypto.DecryptByte(encryptedHeader[i]);

                        byte expected = GetPasswordCheckByte(entry);
                        if (plainHeader[11] != expected)
                            throw new InvalidArchivePasswordException("The password is incorrect or the encrypted backup is damaged.");

                        compressedBytes -= 12;
                        cryptoStream = new ZipCryptoReadStream(input, crypto, compressedBytes);
                        compressedStream = cryptoStream;
                    }
                    else
                    {
                        boundedStream = new BoundedReadStream(input, compressedBytes);
                        compressedStream = boundedStream;
                    }

                    Crc32 crc = new Crc32();
                    if (entry.CompressionMethod == CompressionStored)
                    {
                        CopyWithCrc(compressedStream, output, entry.UncompressedSize, crc);
                    }
                    else if (entry.CompressionMethod == CompressionDeflated)
                    {
                        using (var deflate = new DeflateStream(compressedStream, CompressionMode.Decompress, true))
                            CopyWithCrc(deflate, output, -1, crc);
                    }
                    else
                    {
                        throw new NotSupportedException("Unsupported ZIP compression method: " + entry.CompressionMethod);
                    }

                    if (crc.Value != entry.Crc32)
                        throw new InvalidArchivePasswordException("The password is incorrect or the encrypted backup is damaged.");
                }
                catch (InvalidDataException ex)
                {
                    if ((entry.Flags & FlagEncrypted) != 0)
                        throw new InvalidArchivePasswordException("The password is incorrect or the encrypted backup is damaged.", ex);
                    throw;
                }
                finally
                {
                    if (cryptoStream != null)
                        cryptoStream.Dispose();
                    if (boundedStream != null)
                        boundedStream.Dispose();
                }
            }
        }

        private static byte GetPasswordCheckByte(ZipEntryInfo entry)
        {
            if ((entry.Flags & FlagDataDescriptor) != 0)
                return (byte)((entry.LastModTime >> 8) & 0xff);
            return (byte)((entry.Crc32 >> 24) & 0xff);
        }

        private static List<ZipEntryInfo> ReadCentralDirectory(string zipPath)
        {
            EndRecord endRecord = ReadEndRecord(zipPath);
            var entries = new List<ZipEntryInfo>();

            using (var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = endRecord.CentralDirectoryOffset;
                for (int i = 0; i < endRecord.TotalEntries; i++)
                {
                    uint signature = reader.ReadUInt32();
                    if (signature != CentralHeaderSignature)
                        throw new InvalidDataException("Invalid ZIP central directory.");

                    var entry = new ZipEntryInfo();
                    entry.VersionMadeBy = reader.ReadUInt16();
                    entry.VersionNeeded = reader.ReadUInt16();
                    entry.Flags = reader.ReadUInt16();
                    entry.CompressionMethod = reader.ReadUInt16();
                    entry.LastModTime = reader.ReadUInt16();
                    entry.LastModDate = reader.ReadUInt16();
                    entry.Crc32 = reader.ReadUInt32();
                    entry.CompressedSize = reader.ReadUInt32();
                    entry.UncompressedSize = reader.ReadUInt32();
                    ushort nameLength = reader.ReadUInt16();
                    ushort extraLength = reader.ReadUInt16();
                    ushort commentLength = reader.ReadUInt16();
                    entry.DiskNumberStart = reader.ReadUInt16();
                    entry.InternalAttributes = reader.ReadUInt16();
                    entry.ExternalAttributes = reader.ReadUInt32();
                    entry.LocalHeaderOffset = reader.ReadUInt32();
                    entry.NameBytes = reader.ReadBytes(nameLength);
                    entry.CentralExtraBytes = reader.ReadBytes(extraLength);
                    entry.CommentBytes = reader.ReadBytes(commentLength);
                    entry.Name = DecodeZipString(entry.NameBytes, entry.Flags);
                    entry.IsDirectory = entry.Name.EndsWith("/", StringComparison.Ordinal) || entry.Name.EndsWith("\\", StringComparison.Ordinal);

                    if (entry.CompressedSize == uint.MaxValue || entry.UncompressedSize == uint.MaxValue || entry.LocalHeaderOffset == uint.MaxValue)
                        throw new NotSupportedException("Zip64 archives are not supported by this compatibility reader yet.");

                    ReadLocalHeaderInfo(stream, reader, entry);
                    entries.Add(entry);
                }
            }

            return entries;
        }

        private static void ReadLocalHeaderInfo(FileStream stream, BinaryReader reader, ZipEntryInfo entry)
        {
            long returnPosition = stream.Position;
            stream.Position = entry.LocalHeaderOffset;

            if (reader.ReadUInt32() != LocalHeaderSignature)
                throw new InvalidDataException("Invalid ZIP local file header.");

            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadUInt32();
            ushort localNameLength = reader.ReadUInt16();
            ushort localExtraLength = reader.ReadUInt16();
            reader.ReadBytes(localNameLength);
            entry.LocalExtraBytes = reader.ReadBytes(localExtraLength);
            entry.DataOffset = stream.Position;

            stream.Position = returnPosition;
        }

        private static EndRecord ReadEndRecord(string zipPath)
        {
            using (var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 22)
                    throw new InvalidDataException("Invalid ZIP file.");

                int searchSize = (int)Math.Min(stream.Length, 65557);
                byte[] buffer = new byte[searchSize];
                stream.Position = stream.Length - searchSize;
                stream.Read(buffer, 0, buffer.Length);

                int eocdIndex = -1;
                for (int i = buffer.Length - 22; i >= 0; i--)
                {
                    if (buffer[i] == 0x50 && buffer[i + 1] == 0x4b && buffer[i + 2] == 0x05 && buffer[i + 3] == 0x06)
                    {
                        eocdIndex = i;
                        break;
                    }
                }

                if (eocdIndex < 0)
                    throw new InvalidDataException("ZIP end record was not found.");

                stream.Position = stream.Length - searchSize + eocdIndex + 4;
                ushort diskNumber = reader.ReadUInt16();
                ushort centralDisk = reader.ReadUInt16();
                ushort entriesOnDisk = reader.ReadUInt16();
                ushort totalEntries = reader.ReadUInt16();
                uint centralSize = reader.ReadUInt32();
                uint centralOffset = reader.ReadUInt32();
                ushort commentLength = reader.ReadUInt16();
                byte[] comment = reader.ReadBytes(commentLength);

                if (diskNumber != 0 || centralDisk != 0 || entriesOnDisk != totalEntries)
                    throw new NotSupportedException("Multi-disk ZIP archives are not supported.");
                if (totalEntries == ushort.MaxValue || centralSize == uint.MaxValue || centralOffset == uint.MaxValue)
                    throw new NotSupportedException("Zip64 archives are not supported by this compatibility reader yet.");

                return new EndRecord
                {
                    TotalEntries = totalEntries,
                    CentralDirectoryOffset = centralOffset,
                    CentralDirectorySize = centralSize,
                    Comment = comment
                };
            }
        }

        private static string DecodeZipString(byte[] bytes, ushort flags)
        {
            Encoding encoding;
            if ((flags & FlagUtf8) != 0)
            {
                encoding = Encoding.UTF8;
            }
            else
            {
                try { encoding = Encoding.GetEncoding(437); }
                catch { encoding = Encoding.Default; }
            }
            return encoding.GetString(bytes);
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of ZIP entry.");
                offset += read;
            }
            return buffer;
        }

        private static void CopyBytes(Stream input, Stream output, uint count)
        {
            byte[] buffer = new byte[1024 * 128];
            long remaining = count;
            while (remaining > 0)
            {
                int wanted = (int)Math.Min(buffer.Length, remaining);
                int read = input.Read(buffer, 0, wanted);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of ZIP entry.");
                output.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static void CopyWithCrc(Stream input, Stream output, long limit, Crc32 crc)
        {
            byte[] buffer = new byte[1024 * 128];
            long remaining = limit;
            while (limit < 0 || remaining > 0)
            {
                int wanted = limit < 0 ? buffer.Length : (int)Math.Min(buffer.Length, remaining);
                int read = input.Read(buffer, 0, wanted);
                if (read <= 0)
                    break;
                crc.Update(buffer, 0, read);
                output.Write(buffer, 0, read);
                if (limit >= 0)
                    remaining -= read;
            }
        }

        private static bool IsMetadataEntry(string entryName)
        {
            string normalized = (entryName ?? string.Empty).Replace('\\', '/').TrimStart('/');
            return string.Equals(normalized, BackupEngine.ManifestEntryName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Backup.ini", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetSafeDestinationPath(string root, string entryName)
        {
            string relative = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            relative = relative.TrimStart(Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(root, relative));
            string rootFull = PathHelpers.EnsureTrailingSeparator(Path.GetFullPath(root));
            if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe path in archive: " + entryName);
            return fullPath;
        }

        private static DateTime DosDateTimeToDateTime(ushort date, ushort time)
        {
            int year = 1980 + ((date >> 9) & 0x7f);
            int month = (date >> 5) & 0x0f;
            int day = date & 0x1f;
            int hour = (time >> 11) & 0x1f;
            int minute = (time >> 5) & 0x3f;
            int second = (time & 0x1f) * 2;
            try { return new DateTime(year, month, day, hour, minute, second); }
            catch { return DateTime.Now; }
        }

        private static void Report(IProgress<BackupProgress> progress, string message, int percent)
        {
            if (progress != null)
                progress.Report(new BackupProgress { Message = message, Percent = percent });
        }

        private sealed class ZipEntryInfo
        {
            public ushort VersionMadeBy;
            public ushort VersionNeeded;
            public ushort Flags;
            public ushort OutputFlags;
            public ushort CompressionMethod;
            public ushort LastModTime;
            public ushort LastModDate;
            public uint Crc32;
            public uint CompressedSize;
            public uint OutputCompressedSize;
            public uint UncompressedSize;
            public ushort DiskNumberStart;
            public ushort InternalAttributes;
            public uint ExternalAttributes;
            public uint LocalHeaderOffset;
            public long OutputLocalHeaderOffset;
            public long DataOffset;
            public byte[] NameBytes;
            public byte[] LocalExtraBytes;
            public byte[] CentralExtraBytes;
            public byte[] CommentBytes;
            public string Name;
            public bool IsDirectory;
        }

        private sealed class EndRecord
        {
            public ushort TotalEntries;
            public uint CentralDirectoryOffset;
            public uint CentralDirectorySize;
            public byte[] Comment;
        }

        private sealed class BoundedReadStream : Stream
        {
            private readonly Stream _baseStream;
            private long _remaining;

            public BoundedReadStream(Stream baseStream, long remaining)
            {
                _baseStream = baseStream;
                _remaining = remaining;
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return _remaining; } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_remaining <= 0)
                    return 0;

                int wanted = (int)Math.Min(count, _remaining);
                int read = _baseStream.Read(buffer, offset, wanted);
                if (read > 0)
                    _remaining -= read;
                return read;
            }
        }

        private sealed class ZipCryptoReadStream : Stream
        {
            private readonly Stream _baseStream;
            private readonly ZipCryptoTransform _crypto;
            private long _remaining;

            public ZipCryptoReadStream(Stream baseStream, ZipCryptoTransform crypto, long remaining)
            {
                _baseStream = baseStream;
                _crypto = crypto;
                _remaining = remaining;
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return _remaining; } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_remaining <= 0)
                    return 0;

                int wanted = (int)Math.Min(count, _remaining);
                int read = _baseStream.Read(buffer, offset, wanted);
                if (read <= 0)
                    return read;

                for (int i = 0; i < read; i++)
                    buffer[offset + i] = _crypto.DecryptByte(buffer[offset + i]);

                _remaining -= read;
                return read;
            }
        }

        private sealed class ZipCryptoTransform
        {
            private uint _key0 = 0x12345678;
            private uint _key1 = 0x23456789;
            private uint _key2 = 0x34567890;

            public ZipCryptoTransform(string password)
            {
                byte[] bytes = Encoding.Default.GetBytes(password ?? string.Empty);
                for (int i = 0; i < bytes.Length; i++)
                    UpdateKeys(bytes[i]);
            }

            public byte EncryptByte(byte plain)
            {
                byte encrypted = (byte)(plain ^ MagicByte());
                UpdateKeys(plain);
                return encrypted;
            }

            public byte DecryptByte(byte encrypted)
            {
                byte plain = (byte)(encrypted ^ MagicByte());
                UpdateKeys(plain);
                return plain;
            }

            private byte MagicByte()
            {
                uint temp = (_key2 & 0xffff) | 2;
                return (byte)(((temp * (temp ^ 1)) >> 8) & 0xff);
            }

            private void UpdateKeys(byte value)
            {
                _key0 = Crc32.UpdateValue(_key0, value);
                _key1 = _key1 + (_key0 & 0xff);
                _key1 = _key1 * 134775813U + 1;
                _key2 = Crc32.UpdateValue(_key2, (byte)(_key1 >> 24));
            }
        }

        private sealed class Crc32
        {
            private static readonly uint[] Table = CreateTable();
            private uint _crc = 0xffffffff;

            public uint Value { get { return _crc ^ 0xffffffff; } }

            public void Update(byte[] buffer, int offset, int count)
            {
                for (int i = 0; i < count; i++)
                    _crc = UpdateValue(_crc, buffer[offset + i]);
            }

            public static uint UpdateValue(uint crc, byte value)
            {
                return Table[(int)((crc ^ value) & 0xff)] ^ (crc >> 8);
            }

            private static uint[] CreateTable()
            {
                uint[] table = new uint[256];
                for (uint i = 0; i < table.Length; i++)
                {
                    uint value = i;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        if ((value & 1) != 0)
                            value = 0xedb88320U ^ (value >> 1);
                        else
                            value >>= 1;
                    }
                    table[i] = value;
                }
                return table;
            }
        }
    }
}
