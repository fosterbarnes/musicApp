using System.Collections.Generic;
using System.Text;
using ATL;

namespace musicApp.Helpers;

public static class LyricsMetadataHelper
{
    public static string ExtractDisplayText(Track atlTrack)
    {
        if (atlTrack?.Lyrics == null || atlTrack.Lyrics.Count == 0)
            return "";
        return ExtractDisplayText(atlTrack.Lyrics);
    }

    public static string ExtractDisplayText(IList<LyricsInfo>? lyrics)
    {
        if (lyrics == null || lyrics.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var li in lyrics)
        {
            if (li == null || !li.Exists())
                continue;

            string part;
            if (!string.IsNullOrWhiteSpace(li.UnsynchronizedLyrics))
            {
                part = li.UnsynchronizedLyrics.TrimEnd();
            }
            else if (li.SynchronizedLyrics != null && li.SynchronizedLyrics.Count > 0)
            {
                part = li.FormatSynch();
            }
            else
                continue;

            if (string.IsNullOrWhiteSpace(part))
                continue;

            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append(part);
        }

        return sb.ToString();
    }
}
