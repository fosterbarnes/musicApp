using System;
using System.Collections.Generic;
using System.Linq;

namespace musicApp.Helpers;

internal static class AlbumMetadataText
{
    public static string BuildAlbumSummary(IReadOnlyCollection<Song> tracks)
    {
        int trackCount = tracks.Count;
        double totalSeconds = tracks.Sum(t =>
        {
            if (t.DurationTimeSpan > TimeSpan.Zero)
                return t.DurationTimeSpan.TotalSeconds;

            if (string.IsNullOrWhiteSpace(t.Duration))
                return 0d;

            return TimeSpan.TryParseExact(t.Duration, @"mm\:ss", null, out var parsed)
                ? parsed.TotalSeconds
                : 0d;
        });

        TimeSpan totalDuration = TimeSpan.FromSeconds(totalSeconds);
        string songLabel = trackCount == 1 ? "song" : "songs";
        return $"{trackCount} {songLabel}, {FormatAlbumDuration(totalDuration)}";
    }

    public static string FormatAlbumDuration(TimeSpan duration)
    {
        int totalMinutes = (int)Math.Floor(duration.TotalMinutes);
        if (totalMinutes <= 0)
            return "0 minutes";

        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;

        if (hours <= 0)
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";

        if (minutes <= 0)
            return hours == 1 ? "1 hour" : $"{hours} hours";

        string hourPart = hours == 1 ? "1 hour" : $"{hours} hours";
        string minutePart = minutes == 1 ? "1 minute" : $"{minutes} minutes";
        return $"{hourPart}, {minutePart}";
    }
}
