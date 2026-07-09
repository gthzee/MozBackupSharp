using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MozBackupSharp.Core;

namespace MozBackupSharp.Services
{
    public sealed class InvalidArchivePasswordException : Exception
    {
        public InvalidArchivePasswordException(string message) : base(message) { }
        public InvalidArchivePasswordException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Legacy reader/writer for AES-protected .pcv files created by earlier
    /// MozBackupSharp prototype builds. New password-protected backups are written
    /// with ZipCryptoArchive so they are compatible with original MozBackup-style
    /// protected ZIP/PCV files.
    /// </summary>
    public static class ProtectedArchive
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MBSPCVENC1");
        private const int Version = 1;
        private const int SaltSize = 16;
        private const int IvSize = 16;
        private const int KeySize = 32;
        private const int HmacSize = 32;
        private const int DefaultIterations = 100000;

        public static bool IsEncrypted(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length < Magic.Length)
                        return false;

                    byte[] buffer = new byte[Magic.Length];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    return read == buffer.Length && ConstantTimeEquals(buffer, Magic);
                }
            }
            catch
            {
                return false;
            }
        }

        public static void EncryptFile(string inputPath, string outputPath, string password, IProgress<BackupProgress> progress)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password is required.", "password");

            string cipherTemp = outputPath + ".cipher";
            PathHelpers.TryDeleteFile(cipherTemp);
            PathHelpers.TryDeleteFile(outputPath);

            byte[] salt = CreateRandomBytes(SaltSize);
            byte[] iv = CreateRandomBytes(IvSize);
            byte[] keys = DeriveKeyBytes(password, salt, DefaultIterations, KeySize + KeySize);
            byte[] aesKey = Slice(keys, 0, KeySize);
            byte[] hmacKey = Slice(keys, KeySize, KeySize);
            byte[] header = BuildHeader(DefaultIterations, salt, iv);

            try
            {
                Report(progress, "Encrypting backup archive...", 99);
                EncryptPayloadToTempFile(inputPath, cipherTemp, aesKey, iv);

                using (var hmac = new HMACSHA256(hmacKey))
                using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(header, 0, header.Length);
                    hmac.TransformBlock(header, 0, header.Length, null, 0);

                    using (var cipher = new FileStream(cipherTemp, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        byte[] buffer = new byte[1024 * 128];
                        int read;
                        while ((read = cipher.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                            hmac.TransformBlock(buffer, 0, read, null, 0);
                        }
                    }

                    hmac.TransformFinalBlock(new byte[0], 0, 0);
                    byte[] mac = hmac.Hash;
                    output.Write(mac, 0, mac.Length);
                }
            }
            finally
            {
                PathHelpers.TryDeleteFile(cipherTemp);
                Clear(keys);
                Clear(aesKey);
                Clear(hmacKey);
            }
        }

        public static void DecryptFile(string inputPath, string outputPath, string password, IProgress<BackupProgress> progress)
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidArchivePasswordException("Password is required to restore this protected backup.");

            Header header;
            byte[] headerBytes;
            using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                header = ReadHeader(input, out headerBytes);
                if (input.Length < headerBytes.Length + HmacSize)
                    throw new InvalidDataException("The encrypted backup file is incomplete or damaged.");
            }

            byte[] keys = DeriveKeyBytes(password, header.Salt, header.Iterations, KeySize + KeySize);
            byte[] aesKey = Slice(keys, 0, KeySize);
            byte[] hmacKey = Slice(keys, KeySize, KeySize);

            try
            {
                Report(progress, "Checking backup password...", 0);
                VerifyHmac(inputPath, headerBytes, hmacKey);

                Report(progress, "Decrypting backup archive...", 0);
                PathHelpers.TryDeleteFile(outputPath);
                using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    input.Position = headerBytes.Length;
                    long cipherBytes = input.Length - headerBytes.Length - HmacSize;
                    DecryptPayload(input, cipherBytes, outputPath, aesKey, header.Iv);
                }
            }
            catch (CryptographicException ex)
            {
                PathHelpers.TryDeleteFile(outputPath);
                throw new InvalidArchivePasswordException("The password is incorrect or the encrypted backup is damaged.", ex);
            }
            finally
            {
                Clear(keys);
                Clear(aesKey);
                Clear(hmacKey);
            }
        }

        private static void EncryptPayloadToTempFile(string inputPath, string outputPath, byte[] aesKey, byte[] iv)
        {
            using (AesManaged aes = CreateAes(aesKey, iv))
            using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                CopyStream(input, crypto);
            }
        }

        private static void DecryptPayload(Stream input, long cipherBytes, string outputPath, byte[] aesKey, byte[] iv)
        {
            using (AesManaged aes = CreateAes(aesKey, iv))
            using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var crypto = new CryptoStream(output, aes.CreateDecryptor(), CryptoStreamMode.Write))
            {
                CopyBytes(input, crypto, cipherBytes);
            }
        }

        private static AesManaged CreateAes(byte[] key, byte[] iv)
        {
            var aes = new AesManaged();
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;
            return aes;
        }

        private static byte[] BuildHeader(int iterations, byte[] salt, byte[] iv)
        {
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(iterations);
                writer.Write(salt.Length);
                writer.Write(iv.Length);
                writer.Write(salt);
                writer.Write(iv);
                writer.Flush();
                return memory.ToArray();
            }
        }

        private static Header ReadHeader(Stream input, out byte[] headerBytes)
        {
            byte[] magic = new byte[Magic.Length];
            if (input.Read(magic, 0, magic.Length) != magic.Length || !ConstantTimeEquals(magic, Magic))
                throw new InvalidDataException("This file is not a MozBackupSharp encrypted archive.");

            using (var headerMemory = new MemoryStream())
            {
                headerMemory.Write(magic, 0, magic.Length);

                int version = ReadInt32AndCopy(input, headerMemory);
                if (version != Version)
                    throw new InvalidDataException("Unsupported encrypted backup format version.");

                int iterations = ReadInt32AndCopy(input, headerMemory);
                int saltLength = ReadInt32AndCopy(input, headerMemory);
                int ivLength = ReadInt32AndCopy(input, headerMemory);

                if (iterations < 10000 || saltLength < 8 || saltLength > 64 || ivLength != IvSize)
                    throw new InvalidDataException("The encrypted backup header is invalid.");

                byte[] salt = ReadBytesAndCopy(input, headerMemory, saltLength);
                byte[] iv = ReadBytesAndCopy(input, headerMemory, ivLength);
                headerBytes = headerMemory.ToArray();

                return new Header
                {
                    Iterations = iterations,
                    Salt = salt,
                    Iv = iv
                };
            }
        }

        private static int ReadInt32AndCopy(Stream input, Stream copy)
        {
            byte[] buffer = ReadBytesAndCopy(input, copy, 4);
            return BitConverter.ToInt32(buffer, 0);
        }

        private static byte[] ReadBytesAndCopy(Stream input, Stream copy, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = input.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of encrypted backup header.");
                offset += read;
            }
            copy.Write(buffer, 0, buffer.Length);
            return buffer;
        }

        private static void VerifyHmac(string inputPath, byte[] headerBytes, byte[] hmacKey)
        {
            byte[] expected = new byte[HmacSize];
            byte[] actual;

            using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length < headerBytes.Length + HmacSize)
                    throw new InvalidDataException("The encrypted backup file is incomplete or damaged.");

                input.Position = input.Length - HmacSize;
                if (input.Read(expected, 0, expected.Length) != expected.Length)
                    throw new InvalidDataException("The encrypted backup file is incomplete or damaged.");

                input.Position = 0;
                long bytesToMac = input.Length - HmacSize;
                using (var hmac = new HMACSHA256(hmacKey))
                {
                    byte[] buffer = new byte[1024 * 128];
                    while (bytesToMac > 0)
                    {
                        int wanted = (int)Math.Min(buffer.Length, bytesToMac);
                        int read = input.Read(buffer, 0, wanted);
                        if (read <= 0)
                            throw new EndOfStreamException("Unexpected end of encrypted backup file.");
                        hmac.TransformBlock(buffer, 0, read, null, 0);
                        bytesToMac -= read;
                    }
                    hmac.TransformFinalBlock(new byte[0], 0, 0);
                    actual = hmac.Hash;
                }
            }

            if (!ConstantTimeEquals(expected, actual))
                throw new InvalidArchivePasswordException("The password is incorrect or the encrypted backup is damaged.");
        }

        private static byte[] CreateRandomBytes(int count)
        {
            byte[] bytes = new byte[count];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(bytes);
            }
            return bytes;
        }

        private static byte[] DeriveKeyBytes(string password, byte[] salt, int iterations, int bytes)
        {
            using (var derive = new Rfc2898DeriveBytes(password, salt, iterations))
            {
                return derive.GetBytes(bytes);
            }
        }

        private static byte[] Slice(byte[] source, int offset, int count)
        {
            byte[] result = new byte[count];
            Buffer.BlockCopy(source, offset, result, 0, count);
            return result;
        }

        private static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[1024 * 128];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                output.Write(buffer, 0, read);
        }

        private static void CopyBytes(Stream input, Stream output, long bytes)
        {
            byte[] buffer = new byte[1024 * 128];
            while (bytes > 0)
            {
                int wanted = (int)Math.Min(buffer.Length, bytes);
                int read = input.Read(buffer, 0, wanted);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of encrypted backup file.");
                output.Write(buffer, 0, read);
                bytes -= read;
            }
        }

        private static bool ConstantTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < left.Length; i++)
                diff |= left[i] ^ right[i];
            return diff == 0;
        }

        private static void Clear(byte[] bytes)
        {
            if (bytes == null)
                return;
            Array.Clear(bytes, 0, bytes.Length);
        }

        private static void Report(IProgress<BackupProgress> progress, string message, int percent)
        {
            if (progress != null)
                progress.Report(new BackupProgress { Message = message, Percent = percent });
        }

        private sealed class Header
        {
            public int Iterations { get; set; }
            public byte[] Salt { get; set; }
            public byte[] Iv { get; set; }
        }
    }
}
