using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MozBackupSharp.Core;

namespace MozBackupSharp.Services
{
    public sealed class BackupEngine
    {
        public const string ManifestEntryName = "_mozbackupsharp/manifest.json";

        public BackupManifest CreateBackup(BackupOptions options, IProgress<BackupProgress> progress)
        {
            if (options == null)
                throw new ArgumentNullException("options");
            if (options.Profile == null)
                throw new ArgumentException("A profile must be selected.", "options");
            if (string.IsNullOrWhiteSpace(options.ArchivePath))
                throw new ArgumentException("An archive path must be selected.", "options");

            string profilePath = options.Profile.FullPath;
            if (!Directory.Exists(profilePath))
                throw new DirectoryNotFoundException("Profile folder not found: " + profilePath);

            string archiveDirectory = Path.GetDirectoryName(options.ArchivePath);
            if (!string.IsNullOrEmpty(archiveDirectory) && !Directory.Exists(archiveDirectory))
                Directory.CreateDirectory(archiveDirectory);

            Report(progress, "Scanning profile files...", 0);
            List<string> files = EnumerateSelectedFiles(profilePath, options.Components, options.IncludeUnknownFiles).ToList();

            var manifest = new BackupManifest
            {
                Format = "MozBackupSharp.PCV.Zip.v1",
                Tool = "MozBackupSharp",
                Application = options.Profile.ApplicationKind.ToString(),
                ProfileName = options.Profile.Name,
                SourcePath = profilePath,
                Components = options.Components
            };

            bool passwordProtected = !string.IsNullOrEmpty(options.Password);
            string finalTempPath = options.ArchivePath + ".tmp";
            string zipTempPath = passwordProtected ? options.ArchivePath + ".ziptmp" : finalTempPath;
            PathHelpers.TryDeleteFile(finalTempPath);
            if (passwordProtected)
                PathHelpers.TryDeleteFile(zipTempPath);

            try
            {
                using (var stream = new FileStream(zipTempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    AddTextEntry(archive, ManifestEntryName, JsonLite.ToJson(manifest));

                    for (int i = 0; i < files.Count; i++)
                    {
                        string file = files[i];
                        string relativePath = PathHelpers.GetSafeRelativePath(profilePath, file);
                        string zipPath = PathHelpers.NormalizeZipPath(relativePath);
                        int percent = files.Count == 0 ? 98 : 1 + (int)(((long)i * 96L) / files.Count);
                        Report(progress, "Adding " + relativePath, percent);

                        try
                        {
                            using (Stream input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            {
                                var entry = archive.CreateEntry(zipPath, CompressionLevel.Optimal);
                                entry.LastWriteTime = File.GetLastWriteTime(file);
                                using (Stream output = entry.Open())
                                {
                                    input.CopyTo(output);
                                }
                            }
                            manifest.Files.Add(zipPath);
                        }
                        catch (IOException)
                        {
                            // Mozilla keeps some files locked while running. The original
                            // application asked users to close the browser; this port skips
                            // files that cannot be read and continues.
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Continue with other files.
                        }
                    }
                }

                // Rewrite the archive so the manifest contains the final file list.
                UpdateManifest(zipTempPath, manifest);

                if (passwordProtected)
                {
                    if (options.UseAesEncryption)
                        ProtectedArchive.EncryptFile(zipTempPath, finalTempPath, options.Password, progress);
                    else
                        ZipCryptoArchive.EncryptZipFile(zipTempPath, finalTempPath, options.Password, progress);
                    PathHelpers.TryDeleteFile(zipTempPath);
                }

                if (File.Exists(options.ArchivePath))
                    File.Delete(options.ArchivePath);
                File.Move(finalTempPath, options.ArchivePath);
                Report(progress, passwordProtected ? "Password-protected backup completed." : "Backup completed.", 100);
                return manifest;
            }
            catch
            {
                PathHelpers.TryDeleteFile(finalTempPath);
                if (passwordProtected)
                    PathHelpers.TryDeleteFile(zipTempPath);
                throw;
            }
        }

        public IEnumerable<string> EnumerateSelectedFiles(string profilePath, BackupComponent components, bool includeUnknownFiles)
        {
            foreach (string file in EnumerateFilesSafe(profilePath))
            {
                string relativePath = PathHelpers.GetSafeRelativePath(profilePath, file);
                if (IsExcluded(relativePath))
                    continue;

                if (includeUnknownFiles || MatchesAnyComponent(relativePath, components))
                    yield return file;
            }
        }

        private static IEnumerable<string> EnumerateFilesSafe(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] files = new string[0];
                string[] children = new string[0];

                try { files = Directory.GetFiles(directory); }
                catch { }

                for (int i = 0; i < files.Length; i++)
                    yield return files[i];

                try { children = Directory.GetDirectories(directory); }
                catch { }

                for (int i = 0; i < children.Length; i++)
                    pending.Push(children[i]);
            }
        }

        private static void UpdateManifest(string archivePath, BackupManifest manifest)
        {
            string tempPath = archivePath + ".rewrite";
            PathHelpers.TryDeleteFile(tempPath);

            using (var sourceStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var source = new ZipArchive(sourceStream, ZipArchiveMode.Read))
            using (var destinationStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var destination = new ZipArchive(destinationStream, ZipArchiveMode.Create))
            {
                AddTextEntry(destination, ManifestEntryName, JsonLite.ToJson(manifest));

                foreach (ZipArchiveEntry sourceEntry in source.Entries)
                {
                    if (string.Equals(sourceEntry.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    ZipArchiveEntry destinationEntry = destination.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                    destinationEntry.LastWriteTime = sourceEntry.LastWriteTime;
                    using (Stream input = sourceEntry.Open())
                    using (Stream output = destinationEntry.Open())
                    {
                        input.CopyTo(output);
                    }
                }
            }

            File.Delete(archivePath);
            File.Move(tempPath, archivePath);
        }

        private static void AddTextEntry(ZipArchive archive, string name, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write(content ?? string.Empty);
            }
        }

        private static bool MatchesAnyComponent(string relativePath, BackupComponent components)
        {
            if ((components & BackupComponent.BookmarksAndHistory) != 0 && IsBookmarksAndHistory(relativePath)) return true;
            if ((components & BackupComponent.Passwords) != 0 && IsPasswords(relativePath)) return true;
            if ((components & BackupComponent.Cookies) != 0 && IsCookies(relativePath)) return true;
            if ((components & BackupComponent.FormHistory) != 0 && IsFormHistory(relativePath)) return true;
            if ((components & BackupComponent.Preferences) != 0 && IsPreferences(relativePath)) return true;
            if ((components & BackupComponent.Extensions) != 0 && IsExtensions(relativePath)) return true;
            if ((components & BackupComponent.Certificates) != 0 && IsCertificates(relativePath)) return true;
            if ((components & BackupComponent.Mail) != 0 && IsMail(relativePath)) return true;
            if ((components & BackupComponent.AddressBooks) != 0 && IsAddressBooks(relativePath)) return true;
            if ((components & BackupComponent.OtherImportantFiles) != 0 && IsOtherImportant(relativePath)) return true;
            return false;
        }

        private static bool IsExcluded(string relativePath)
        {
            string path = Normalize(relativePath);
            string name = Path.GetFileName(path).ToLowerInvariant();

            if (name == "parent.lock" || name == "lock" || name == ".parentlock") return true;
            if (StartsWithSegment(path, "cache") || StartsWithSegment(path, "cache2")) return true;
            if (StartsWithSegment(path, "startupcache") || StartsWithSegment(path, "offlinecache")) return true;
            if (StartsWithSegment(path, "safebrowsing") || StartsWithSegment(path, "shader-cache")) return true;
            if (StartsWithSegment(path, "thumbnails") || StartsWithSegment(path, "minidumps")) return true;
            if (StartsWithSegment(path, "crashes")) return true;
            return false;
        }

        private static bool IsBookmarksAndHistory(string path)
        {
            path = Normalize(path);
            string name = FileName(path);
            return name == "places.sqlite" || name.StartsWith("places.sqlite-") ||
                   name == "favicons.sqlite" || name.StartsWith("favicons.sqlite-") ||
                   StartsWithSegment(path, "bookmarkbackups");
        }

        private static bool IsPasswords(string path)
        {
            path = Normalize(path);
            string name = FileName(path);
            return name == "logins.json" || name == "key4.db" || name == "key3.db" ||
                   name == "signons.sqlite" || name.StartsWith("signons") || name == "pwd.db";
        }

        private static bool IsCookies(string path)
        {
            string name = FileName(Normalize(path));
            return name == "cookies.sqlite" || name.StartsWith("cookies.sqlite-");
        }

        private static bool IsFormHistory(string path)
        {
            string name = FileName(Normalize(path));
            return name == "formhistory.sqlite" || name.StartsWith("formhistory.sqlite-");
        }

        private static bool IsPreferences(string path)
        {
            path = Normalize(path);
            string name = FileName(path);
            return name == "prefs.js" || name == "user.js" || name == "mimetypes.rdf" ||
                   name == "handlers.json" || name == "search.json.mozlz4" ||
                   StartsWithSegment(path, "chrome");
        }

        private static bool IsExtensions(string path)
        {
            path = Normalize(path);
            string name = FileName(path);
            return StartsWithSegment(path, "extensions") || StartsWithSegment(path, "browser-extension-data") ||
                   name == "extensions.json" || name == "extension-preferences.json" || name == "addons.json" ||
                   name == "addonstartup.json.lz4";
        }

        private static bool IsCertificates(string path)
        {
            string name = FileName(Normalize(path));
            return name == "cert9.db" || name == "cert8.db" || name == "secmod.db" ||
                   name == "pkcs11.txt" || name == "cert_override.txt";
        }

        private static bool IsMail(string path)
        {
            path = Normalize(path);
            string name = FileName(path);
            return StartsWithSegment(path, "mail") || StartsWithSegment(path, "imapmail") ||
                   StartsWithSegment(path, "news") || StartsWithSegment(path, "local folders") ||
                   name == "panacea.dat" || name == "foldertree.json" || name == "virtualfolders.dat" ||
                   name == "msgfilterrules.dat";
        }

        private static bool IsAddressBooks(string path)
        {
            string name = FileName(Normalize(path));
            return (name.StartsWith("abook") || name.StartsWith("history") || name.StartsWith("impab")) &&
                   (name.EndsWith(".mab") || name.EndsWith(".sqlite"));
        }

        private static bool IsOtherImportant(string path)
        {
            path = Normalize(path);
            string name = FileName(path);
            return name == "compatibility.ini" || name == "containers.json" || name == "permissions.sqlite" ||
                   name == "content-prefs.sqlite" || name == "xulstore.json" || name == "sessionstore.jsonlz4" ||
                   name == "persdict.dat" || StartsWithSegment(path, "sessionstore-backups") ||
                   StartsWithSegment(path, "storage") || StartsWithSegment(path, "weave");
        }

        private static bool StartsWithSegment(string normalizedPath, string segment)
        {
            return normalizedPath.Equals(segment, StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(segment + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        }

        private static string FileName(string normalizedPath)
        {
            int slash = normalizedPath.LastIndexOf('/');
            return slash >= 0 ? normalizedPath.Substring(slash + 1) : normalizedPath;
        }

        private static void Report(IProgress<BackupProgress> progress, string message, int percent)
        {
            if (progress != null)
                progress.Report(new BackupProgress { Message = message, Percent = percent });
        }
    }
}
