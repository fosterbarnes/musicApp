using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using ATL;
using musicApp;
using NAudio.Wave;

namespace musicApp.Helpers;

public static class TrackMetadataLoader
{
    public static Song? LoadSong(string filePath, Song? preserveLibraryFieldsFrom = null)
    {
        try
        {
            var track = new Song
            {
                Title = "Unknown Title",
                Artist = "Unknown Artist",
                Album = "Unknown Album",
                DurationTimeSpan = TimeSpan.Zero,
                Duration = "00:00",
                FilePath = filePath,
                TrackNumber = 0,
                Year = 0,
                Genre = "",
                DateAdded = preserveLibraryFieldsFrom?.DateAdded ?? DateTime.Now,
                PlayCount = preserveLibraryFieldsFrom?.PlayCount ?? 0,
                LastPlayed = preserveLibraryFieldsFrom?.LastPlayed ?? DateTime.MinValue,
                IsFavorite = preserveLibraryFieldsFrom?.IsFavorite ?? false
            };

            PopulateFileType(track, filePath);

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var atlTrack = new Track(fs);
                CopyAtlMetadataMerge(atlTrack, track);

                if (atlTrack.Duration > 0)
                {
                    track.DurationTimeSpan = TimeSpan.FromSeconds(atlTrack.Duration);
                    track.Duration = track.DurationTimeSpan.ToString(@"mm\:ss");
                }
                else
                {
                    using var audioFile = new AudioFileReader(filePath);
                    track.DurationTimeSpan = audioFile.TotalTime;
                    track.Duration = audioFile.TotalTime.ToString(@"mm\:ss");
                }

                if (atlTrack.EmbeddedPictures != null && atlTrack.EmbeddedPictures.Count > 0)
                {
                    track.AlbumArtPath = "embedded";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ATL failed for {filePath}: {ex.Message}");
                try
                {
                    using var audioFile = new AudioFileReader(filePath);
                    track.DurationTimeSpan = audioFile.TotalTime;
                    track.Duration = audioFile.TotalTime.ToString(@"mm\:ss");

                    if (string.IsNullOrEmpty(track.SampleRate))
                    {
                        var sampleRate = audioFile.WaveFormat.SampleRate;
                        if (sampleRate > 0)
                        {
                            track.SampleRate = $"{sampleRate / 1000.0:F1} kHz";
                        }
                    }

                    TryFillBitrateFromFileSize(track);
                }
                catch (Exception audioEx)
                {
                    Debug.WriteLine($"NAudio failed for {filePath}: {audioEx.Message}");
                }
            }

            EnsureFileStatsFromDisk(track, filePath);
            return track;
        }
        catch
        {
            return null;
        }
    }

    private static void TryFillBitrateFromFileSize(Song track)
    {
        if (!string.IsNullOrEmpty(track.Bitrate) ||
            track.DurationTimeSpan.TotalSeconds <= 0)
            return;

        var size = track.FileSize;
        if (size <= 0 && !TryGetFileSizeLength(track.FilePath, out size))
            return;
        track.FileSize = size;

        var bitrateKbps = (int)((track.FileSize * 8) / (track.DurationTimeSpan.TotalSeconds * 1000));
        if (bitrateKbps > 0)
            track.Bitrate = $"{bitrateKbps} kbps";
    }

    private static bool TryGetFileSizeLength(string filePath, out long length)
    {
        length = 0;
        try
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists)
                return false;
            length = fi.Length;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureFileStatsFromDisk(Song track, string filePath)
    {
        if (track.FileSize > 0 && track.DateModified != DateTime.MinValue)
            return;

        try
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists)
                return;

            if (track.DateModified == DateTime.MinValue)
                track.DateModified = fi.LastWriteTime;
            if (track.FileSize == 0)
                track.FileSize = fi.Length;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading file stats for {filePath}: {ex.Message}");
        }
    }

    public static void ReloadTagFieldsFromFile(Song track)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.FilePath) || !File.Exists(track.FilePath))
            return;

        var prevAlbum = track.Album ?? "";
        var prevArtist = track.Artist ?? "";
        var prevAlbumArtist = track.AlbumArtist ?? "";
        var playCount = track.PlayCount;
        var lastPlayed = track.LastPlayed;
        var dateAdded = track.DateAdded;
        var isFavorite = track.IsFavorite;

        try
        {
            using var fs = new FileStream(track.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var atlTrack = new Track(fs);
            ApplyAtlMetadataFull(atlTrack, track);

            if (atlTrack.Duration > 0)
            {
                track.DurationTimeSpan = TimeSpan.FromSeconds(atlTrack.Duration);
                track.Duration = track.DurationTimeSpan.ToString(@"mm\:ss");
            }
            else
            {
                using var audioFile = new AudioFileReader(track.FilePath);
                track.DurationTimeSpan = audioFile.TotalTime;
                track.Duration = audioFile.TotalTime.ToString(@"mm\:ss");
            }

            if (atlTrack.EmbeddedPictures != null && atlTrack.EmbeddedPictures.Count > 0)
                track.AlbumArtPath = "embedded";
            else
                track.AlbumArtPath = "";

            try
            {
                var fi = new FileInfo(track.FilePath);
                if (fi.Exists)
                {
                    track.DateModified = fi.LastWriteTime;
                    track.FileSize = fi.Length;
                }
            }
            catch
            {
                // ignore
            }

            if (!string.Equals(prevAlbum, track.Album, StringComparison.Ordinal) ||
                !string.Equals(prevArtist, track.Artist, StringComparison.Ordinal) ||
                !string.Equals(prevAlbumArtist, track.AlbumArtist ?? "", StringComparison.Ordinal))
            {
                AlbumArtCacheManager.InvalidateAlbum(prevAlbum, prevArtist);
                var prevCanon = !string.IsNullOrWhiteSpace(prevAlbumArtist) ? prevAlbumArtist : prevArtist;
                AlbumArtCacheManager.InvalidateAlbum(prevAlbum, prevCanon);
            }

            var currentCanon = AlbumArtCacheManager.GetThumbnailCacheArtistKey(track);
            AlbumArtCacheManager.InvalidateAlbum(track.Album ?? "", currentCanon);

            track.ThumbnailCachePath = AlbumArtCacheManager.GenerateAndCache(track);

            track.PlayCount = playCount;
            track.LastPlayed = lastPlayed;
            track.DateAdded = dateAdded;
            track.IsFavorite = isFavorite;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ReloadTagFieldsFromFile failed for {track.FilePath}: {ex.Message}");
        }
    }

    private static void PopulateFileType(Song track, string filePath)
    {
        if (!string.IsNullOrEmpty(track.FileType))
            return;

        var extension = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(extension))
        {
            track.FileType = extension.TrimStart('.').ToUpperInvariant();
        }
    }

    private static void CopyAtlMetadataMerge(Track atlTrack, Song track)
    {
        if (!string.IsNullOrEmpty(atlTrack.Title)) track.Title = atlTrack.Title;
        if (!string.IsNullOrEmpty(atlTrack.Artist)) track.Artist = atlTrack.Artist;
        if (!string.IsNullOrEmpty(atlTrack.Album)) track.Album = atlTrack.Album;
        if (atlTrack.TrackNumber.HasValue && atlTrack.TrackNumber.Value > 0) track.TrackNumber = atlTrack.TrackNumber.Value;
        if (atlTrack.Year.HasValue && atlTrack.Year.Value > 0) track.Year = atlTrack.Year.Value;
        if (!string.IsNullOrEmpty(atlTrack.Genre)) track.Genre = atlTrack.Genre;
        if (!string.IsNullOrEmpty(atlTrack.AlbumArtist)) track.AlbumArtist = atlTrack.AlbumArtist;
        if (!string.IsNullOrEmpty(atlTrack.Composer)) track.Composer = atlTrack.Composer;
        if (atlTrack.DiscNumber.HasValue && atlTrack.DiscNumber.Value > 0) track.DiscNumber = atlTrack.DiscNumber.Value.ToString();
        if (atlTrack.Bitrate > 0) track.Bitrate = $"{atlTrack.Bitrate} kbps";
        if (atlTrack.SampleRate > 0) track.SampleRate = $"{atlTrack.SampleRate / 1000.0:F1} kHz";

        if (!string.IsNullOrEmpty(atlTrack.Comment)) track.Comment = atlTrack.Comment;

        var lyricsMerge = LyricsMetadataHelper.ExtractDisplayText(atlTrack);
        if (!string.IsNullOrEmpty(lyricsMerge))
            track.Lyrics = lyricsMerge;

        if (atlTrack.BPM.HasValue && atlTrack.BPM.Value > 0)
            track.BeatsPerMinute = (int)Math.Round(atlTrack.BPM.Value);

        if (atlTrack.TrackTotal.HasValue && atlTrack.TrackTotal.Value > 0)
            track.TrackTotal = atlTrack.TrackTotal.Value;
        if (atlTrack.DiscTotal.HasValue && atlTrack.DiscTotal.Value > 0)
            track.DiscTotal = atlTrack.DiscTotal.Value;

        ApplyCompilationFromTags(atlTrack, track, merge: true);

        if (TryGetAdditional(atlTrack, "BPM", out var bpmStr) && int.TryParse(bpmStr, out int bpm))
            track.BeatsPerMinute = bpm;
        else if (TryGetAdditional(atlTrack, "TBPM", out var tbpm) && int.TryParse(tbpm, out bpm))
            track.BeatsPerMinute = bpm;

        if (TryGetAdditional(atlTrack, "Category", out var cat) && !string.IsNullOrEmpty(cat))
            track.Category = cat;
        else if (TryGetAdditional(atlTrack, "TCON", out var tcon) && !string.IsNullOrEmpty(tcon))
            track.Category = tcon;

        if (atlTrack.Date.HasValue)
        {
            track.ReleaseDate = atlTrack.Date.Value;
        }
        else if (TryGetAdditional(atlTrack, "TDRC", out var tdrc) &&
                 DateTime.TryParse(tdrc, out DateTime releaseDate))
        {
            track.ReleaseDate = releaseDate;
        }
    }

    private static void ApplyAtlMetadataFull(Track atlTrack, Song track)
    {
        track.Title = string.IsNullOrEmpty(atlTrack.Title) ? "" : atlTrack.Title;
        track.Artist = string.IsNullOrEmpty(atlTrack.Artist) ? "" : atlTrack.Artist;
        track.Album = string.IsNullOrEmpty(atlTrack.Album) ? "" : atlTrack.Album;
        track.Genre = string.IsNullOrEmpty(atlTrack.Genre) ? "" : atlTrack.Genre;
        track.AlbumArtist = string.IsNullOrEmpty(atlTrack.AlbumArtist) ? "" : atlTrack.AlbumArtist;
        track.Composer = string.IsNullOrEmpty(atlTrack.Composer) ? "" : atlTrack.Composer;
        track.Comment = string.IsNullOrEmpty(atlTrack.Comment) ? "" : atlTrack.Comment;
        track.Lyrics = LyricsMetadataHelper.ExtractDisplayText(atlTrack);

        track.TrackNumber = atlTrack.TrackNumber ?? 0;
        track.Year = atlTrack.Year ?? 0;

        track.DiscNumber = atlTrack.DiscNumber.HasValue && atlTrack.DiscNumber.Value > 0
            ? atlTrack.DiscNumber.Value.ToString()
            : "";

        track.TrackTotal = atlTrack.TrackTotal ?? 0;
        track.DiscTotal = atlTrack.DiscTotal ?? 0;

        if (atlTrack.BPM.HasValue && atlTrack.BPM.Value > 0)
            track.BeatsPerMinute = (int)Math.Round(atlTrack.BPM.Value);
        else
            track.BeatsPerMinute = 0;

        if (atlTrack.Bitrate > 0) track.Bitrate = $"{atlTrack.Bitrate} kbps";
        else track.Bitrate = "";

        if (atlTrack.SampleRate > 0) track.SampleRate = $"{atlTrack.SampleRate / 1000.0:F1} kHz";
        else track.SampleRate = "";

        if (TryGetAdditional(atlTrack, "BPM", out var bpmStr) && int.TryParse(bpmStr, out int bpm))
            track.BeatsPerMinute = bpm;
        else if (TryGetAdditional(atlTrack, "TBPM", out var tbpm) && int.TryParse(tbpm, out bpm))
            track.BeatsPerMinute = bpm;

        if (TryGetAdditional(atlTrack, "Category", out var cat))
            track.Category = cat ?? "";
        else if (TryGetAdditional(atlTrack, "TCON", out var tcon))
            track.Category = tcon ?? "";
        else
            track.Category = "";

        if (atlTrack.Date.HasValue)
            track.ReleaseDate = atlTrack.Date.Value;
        else if (TryGetAdditional(atlTrack, "TDRC", out var tdrc) &&
                 DateTime.TryParse(tdrc, out DateTime releaseDate))
            track.ReleaseDate = releaseDate;
        else
            track.ReleaseDate = null;

        ApplyCompilationFromTags(atlTrack, track, merge: false);
    }

    private static void ApplyCompilationFromTags(Track atlTrack, Song track, bool merge)
    {
        var flag = ReadCompilationFlag(atlTrack);
        if (!merge)
        {
            track.IsCompilation = flag == true;
            return;
        }

        if (flag.HasValue)
            track.IsCompilation = flag.Value;
    }

    private static bool? ReadCompilationFlag(Track atlTrack)
    {
        foreach (var key in new[] { "TCMP", "cpil" })
        {
            if (!TryGetAdditional(atlTrack, key, out var raw) || string.IsNullOrEmpty(raw))
                continue;
            if (raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (raw == "0" || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return null;
    }

    private static bool TryGetAdditional(Track atl, string key, out string? value)
    {
        value = null;
        var f = atl.AdditionalFields;
        if (f == null)
            return false;

        if (f is IDictionary<string, string> gen)
            return gen.TryGetValue(key, out value);

        if (f is IDictionary legacy)
        {
            if (!legacy.Contains(key))
                return false;
            value = legacy[key]?.ToString();
            return true;
        }

        return false;
    }
}

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
