using System;
using System.IO;
using System.IO.Compression;
using MozBackupSharp.Core;

namespace MozBackupSharp.Services
{
    public sealed class RestoreEngine
    {
        public bool IsArchiveEncrypted(string archivePath)
        {
            return ProtectedArchive.IsEncrypted(archivePath) || ZipCryptoArchive.IsEncryptedZip(archivePath);
        }

        public BackupManifest ReadManifest(string archivePath)
        {
            return ReadManifest(archivePath, null);
        }

        public BackupManifest ReadManifest(string archivePath, string password)
        {
            ValidateArchivePath(archivePath);

            if (ProtectedArchive.IsEncrypted(archivePath))
            {
                if (string.IsNullOrEmpty(password))
                {
                    return new BackupManifest
                    {
                        Format = "MozBackupSharp encrypted PCV",
                        Tool = "MozBackupSharp",
                        ProfileName = "Password required"
                    };
                }

                string tempZip = CreateTempFileName();
                try
                {
                    ProtectedArchive.DecryptFile(archivePath, tempZip, password, null);
                    return ReadManifestFromZip(tempZip);
                }
                finally
                {
                    PathHelpers.TryDeleteFile(tempZip);
                }
            }

            if (ZipCryptoArchive.IsEncryptedZip(archivePath))
            {
                if (string.IsNullOrEmpty(password))
                {
                    return new BackupManifest
                    {
                        Format = "MozBackup/ZipCrypto protected PCV",
                        Tool = "MozBackup-compatible ZIP",
                        ProfileName = "Password required"
                    };
                }

                string manifestJson = ZipCryptoArchive.ReadTextEntry(archivePath, BackupEngine.ManifestEntryName, password);
                if (string.IsNullOrEmpty(manifestJson))
                    return new BackupManifest { Format = "MozBackup/ZipCrypto protected PCV", Tool = "MozBackup-compatible ZIP" };
                return JsonLite.FromJson(manifestJson);
            }

            return ReadManifestFromZip(archivePath);
        }

        public int RestoreArchive(string archivePath, MozillaProfile targetProfile, bool overwriteExisting, IProgress<BackupProgress> progress)
        {
            return RestoreArchive(archivePath, targetProfile, overwriteExisting, null, progress);
        }

        public int RestoreArchive(string archivePath, MozillaProfile targetProfile, bool overwriteExisting, string password, IProgress<BackupProgress> progress)
        {
            if (targetProfile == null)
                throw new ArgumentNullException("targetProfile");
            ValidateArchivePath(archivePath);

            string targetRoot = targetProfile.FullPath;
            if (!Directory.Exists(targetRoot))
                Directory.CreateDirectory(targetRoot);

            string zipPath = archivePath;
            string tempZip = null;
            try
            {
                if (ProtectedArchive.IsEncrypted(archivePath))
                {
                    tempZip = CreateTempFileName();
                    ProtectedArchive.DecryptFile(archivePath, tempZip, password, progress);
                    zipPath = tempZip;
                }
                else if (ZipCryptoArchive.IsEncryptedZip(archivePath))
                {
                    int compatibleRestored = ZipCryptoArchive.ExtractToDirectory(archivePath, targetRoot, overwriteExisting, password, progress);
                    Report(progress, "Restore completed.", 100);
                    return compatibleRestored;
                }

                int restored = RestoreZipArchive(zipPath, targetRoot, overwriteExisting, progress);
                Report(progress, "Restore completed.", 100);
                return restored;
            }
            finally
            {
                if (tempZip != null)
                    PathHelpers.TryDeleteFile(tempZip);
            }
        }

        private static BackupManifest ReadManifestFromZip(string archivePath)
        {
            using (var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                ZipArchiveEntry entry = archive.GetEntry(BackupEngine.ManifestEntryName);
                if (entry == null)
                    return new BackupManifest { Format = "Unknown zip/pcv", Tool = "Unknown" };

                using (var reader = new StreamReader(entry.Open()))
                    return JsonLite.FromJson(reader.ReadToEnd());
            }
        }

        private static int RestoreZipArchive(string archivePath, string targetRoot, bool overwriteExisting, IProgress<BackupProgress> progress)
        {
            int restored = 0;
            using (var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                int total = archive.Entries.Count;
                int index = 0;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    index++;
                    if (IsMetadataEntry(entry.FullName))
                        continue;
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                        continue;

                    string destinationPath = GetSafeDestinationPath(targetRoot, entry.FullName);
                    string destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (!Directory.Exists(destinationDirectory))
                        Directory.CreateDirectory(destinationDirectory);

                    int percent = total == 0 ? 100 : (int)(((long)index * 100L) / total);
                    Report(progress, "Restoring " + entry.FullName, percent);

                    if (File.Exists(destinationPath) && !overwriteExisting)
                        continue;

                    using (Stream input = entry.Open())
                    using (Stream output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                    if (entry.LastWriteTime != DateTimeOffset.MinValue)
                        File.SetLastWriteTime(destinationPath, entry.LastWriteTime.LocalDateTime);
                    restored++;
                }
            }
            return restored;
        }

        private static bool IsMetadataEntry(string entryName)
        {
            string normalized = (entryName ?? string.Empty).Replace('\\', '/').TrimStart('/');
            return string.Equals(normalized, BackupEngine.ManifestEntryName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "Backup.ini", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateArchivePath(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
                throw new ArgumentException("Archive path is required.", "archivePath");
            if (!File.Exists(archivePath))
                throw new FileNotFoundException("Archive file not found.", archivePath);
        }

        private static string CreateTempFileName()
        {
            string path = Path.Combine(Path.GetTempPath(), "MozBackupSharp-" + Guid.NewGuid().ToString("N") + ".zip");
            return path;
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

        private static void Report(IProgress<BackupProgress> progress, string message, int percent)
        {
            if (progress != null)
                progress.Report(new BackupProgress { Message = message, Percent = percent });
        }
    }
}
