using System;
using System.Collections.Generic;
using System.Linq;

namespace musicApp.Helpers;

internal static class AlbumTrackOrder
{
    public static int ParseDiscNumber(string? disc)
    {
        if (string.IsNullOrWhiteSpace(disc))
            return 0;

        var s = disc.Trim();
        int slash = s.IndexOf('/');
        if (slash >= 0)
            s = s[..slash].Trim();

        if (int.TryParse(s, out int v))
            return v;

        int i = 0;
        while (i < s.Length && !char.IsDigit(s[i]))
            i++;
        int j = i;
        while (j < s.Length && char.IsDigit(s[j]))
            j++;
        if (j > i && int.TryParse(s.AsSpan(i, j - i), out v))
            return v;

        return 0;
    }

    public static List<Song> SortByAlbumSequence(IEnumerable<Song> tracks)
    {
        var list = tracks.ToList();
        if (list.Count <= 1)
            return list;

        int maxDisc = 0;
        foreach (var t in list)
        {
            int d = ParseDiscNumber(t.DiscNumber);
            if (d > maxDisc)
                maxDisc = d;
        }

        bool mergeLooseDisc = maxDisc <= 1;

        return list
            .OrderBy(s =>
            {
                int d = ParseDiscNumber(s.DiscNumber);
                if (d == 0 && mergeLooseDisc)
                    return 1;
                return d;
            })
            .ThenBy(s => s.TrackNumber > 0 ? s.TrackNumber : int.MaxValue)
            .ThenBy(s => s.Title ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FilePath ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
