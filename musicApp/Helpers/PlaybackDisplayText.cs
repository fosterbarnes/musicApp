using System;

namespace musicApp.Helpers;

public static class PlaybackDisplayText
{
    public static string FormatTimeSpan(TimeSpan timeSpan) =>
        string.Format("{0}:{1:D2}", (int)timeSpan.TotalMinutes, timeSpan.Seconds);

    public static string FormatArtistAlbum(string? artist, string? album)
    {
        bool hasArtist = !string.IsNullOrWhiteSpace(artist);
        bool hasAlbum = !string.IsNullOrWhiteSpace(album);
        if (hasArtist && hasAlbum)
            return artist + " — " + album;
        if (hasArtist)
            return artist!;
        if (hasAlbum)
            return album!;
        return string.Empty;
    }
}
