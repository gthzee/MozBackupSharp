using System;
using System.IO;

namespace MozBackupSharp.Services
{
    public static class PathHelpers
    {
        public static bool IsChildPathOf(string childPath, string parentPath)
        {
            if (string.IsNullOrEmpty(childPath) || string.IsNullOrEmpty(parentPath))
                return false;

            string child = EnsureTrailingSeparator(Path.GetFullPath(childPath));
            string parent = EnsureTrailingSeparator(Path.GetFullPath(parentPath));
            return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }

        public static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            char last = path[path.Length - 1];
            if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
                return path;
            return path + Path.DirectorySeparatorChar;
        }

        public static string NormalizeZipPath(string relativePath)
        {
            return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        public static string GetSafeRelativePath(string root, string fullPath)
        {
            string rootFull = EnsureTrailingSeparator(Path.GetFullPath(root));
            string fileFull = Path.GetFullPath(fullPath);
            if (!fileFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path is outside the selected profile folder: " + fullPath);
            return fileFull.Substring(rootFull.Length);
        }

        public static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
