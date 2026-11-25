using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace musicApp.Helpers
{
    public static class LibraryPathHelper
    {
        public static string? TryNormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return null;
            }
        }

        public static bool PathsEqual(string? a, string? b)
        {
            var na = TryNormalizePath(a);
            var nb = TryNormalizePath(b);
            if (na == null || nb == null) return false;
            return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsFolderUnderOrEqual(string folder, string ancestor)
        {
            var na = TryNormalizePath(ancestor);
            var nb = TryNormalizePath(folder);
            if (na == null || nb == null) return false;
            if (string.Equals(nb, na, StringComparison.OrdinalIgnoreCase)) return true;
            // GetFullPath keeps the separator on a drive root ("D:\"), so don't add a second one.
            var prefix = Path.EndsInDirectorySeparator(na) ? na : na + Path.DirectorySeparatorChar;
            return nb.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Recursive file scan of a music folder that skips subfolders it cannot read (for example
        /// "System Volume Information" on a music drive) instead of failing the whole scan.
        /// </summary>
        public static IEnumerable<string> EnumerateFilesRecursive(string folderPath)
        {
            // IgnoreInaccessible also turns an unreadable root into 0 files, which would make every track in
            // the folder look deleted. Open the root without it first so that case still throws.
            using (var root = Directory.EnumerateFileSystemEntries(folderPath).GetEnumerator())
                root.MoveNext();

            return Directory.EnumerateFiles(folderPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0
            });
        }

        public static bool IsFileUnderMusicFolder(string filePath, string folderPath)
        {
            return IsFolderUnderOrEqual(filePath, folderPath);
        }

        public static string? FindCanonicalMusicRoot(string path, IReadOnlyList<string> roots)
        {
            string? best = null;
            foreach (var r in roots)
            {
                if (!IsFolderUnderOrEqual(path, r)) continue;
                if (best == null || r.Length > best.Length)
                    best = r;
            }
            return best;
        }

        public static List<string> CollapseOverlappingMusicRoots(IEnumerable<string> paths)
        {
            var normalized = new List<string>();
            foreach (var raw in paths)
            {
                var p = TryNormalizePath(raw);
                if (string.IsNullOrEmpty(p))
                    continue;
                if (!normalized.Exists(n => string.Equals(n, p, StringComparison.OrdinalIgnoreCase)))
                    normalized.Add(p);
            }

            normalized.Sort((a, b) => a.Length.CompareTo(b.Length));

            var result = new List<string>();
            foreach (var p in normalized)
            {
                if (result.Any(r => IsFolderUnderOrEqual(p, r)))
                    continue;
                result.Add(p);
            }
            return result;
        }
    }
}
