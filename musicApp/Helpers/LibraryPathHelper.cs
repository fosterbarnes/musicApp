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
            return nb.StartsWith(na + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || nb.StartsWith(na + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
            var normalized = paths
                .Select(TryNormalizePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p!.Length)
                .ToList();

            var result = new List<string>();
            foreach (var p in normalized!)
            {
                if (result.Any(r => IsFolderUnderOrEqual(p!, r)))
                    continue;
                result.Add(p!);
            }
            return result;
        }
    }
}
