using System;
using System.Collections.Generic;
using System.IO;
using ATL;

namespace MusicApp.Helpers;

public sealed class TrackMetadataEdit
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Composer { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Comment { get; set; } = "";
    public int Year { get; set; }
    public int TrackNumber { get; set; }
    public int TrackTotal { get; set; }
    public int DiscNumber { get; set; }
    public int DiscTotal { get; set; }
    public int BeatsPerMinute { get; set; }
    public bool Compilation { get; set; }

    public byte[]? EmbeddedFrontCoverPictureData { get; set; }
}

public static class TrackMetadataSaver
{
    public static bool TrySave(string filePath, TrackMetadataEdit edit, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            error = "No file path.";
            return false;
        }

        try
        {
            if (!File.Exists(filePath))
            {
                error = "File not found.";
                return false;
            }

            var t = new Track(filePath);

            t.Title = edit.Title ?? "";
            t.Artist = edit.Artist ?? "";
            t.Album = edit.Album ?? "";
            t.AlbumArtist = edit.AlbumArtist ?? "";
            t.Composer = edit.Composer ?? "";
            t.Genre = edit.Genre ?? "";
            t.Comment = edit.Comment ?? "";

            if (edit.Year > 0)
                t.Year = edit.Year;
            else
                t.Year = null;

            if (edit.TrackNumber > 0)
                t.TrackNumber = edit.TrackNumber;
            else
                t.TrackNumber = null;

            if (edit.TrackTotal > 0)
                t.TrackTotal = edit.TrackTotal;
            else
                t.TrackTotal = null;

            if (edit.DiscNumber > 0)
                t.DiscNumber = edit.DiscNumber;
            else
                t.DiscNumber = null;

            if (edit.DiscTotal > 0)
                t.DiscTotal = edit.DiscTotal;
            else
                t.DiscTotal = null;

            if (edit.BeatsPerMinute > 0)
                t.BPM = edit.BeatsPerMinute;
            else
                t.BPM = null;

            SetCompilationTag(t, edit.Compilation);

            if (edit.EmbeddedFrontCoverPictureData is { Length: > 0 } picBytes)
            {
                // Touch EmbeddedPictures to load initial snapshot; then clear all so Save() marks every
                // prior picture deleted. Removing only PicType.Front misses covers stored as Generic/other.
                var pics = t.EmbeddedPictures;
                for (int i = pics.Count - 1; i >= 0; i--)
                    pics.RemoveAt(i);

                var newPicture = PictureInfo.fromBinaryData(picBytes, PictureInfo.PIC_TYPE.Front);
                pics.Add(newPicture);
            }

            if (!t.Save())
            {
                error = "The tag writer could not save changes to this file.";
                return false;
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = "Access denied. The file may be read-only or in use.";
            return false;
        }
        catch (IOException ex)
        {
            error = $"Could not write the file: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TrySaveLyrics(string filePath, string? lyricsText, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            error = "No file path.";
            return false;
        }

        try
        {
            if (!File.Exists(filePath))
            {
                error = "File not found.";
                return false;
            }

            var t = new Track(filePath);
            var trimmed = lyricsText?.Trim() ?? "";

            if (string.IsNullOrEmpty(trimmed))
            {
                t.Lyrics = new List<LyricsInfo> { LyricsInfo.ForRemoval() };
            }
            else
            {
                var li = new LyricsInfo
                {
                    ContentType = LyricsInfo.LyricsType.LYRICS,
                    Format = LyricsInfo.LyricsFormat.UNSYNCHRONIZED,
                    UnsynchronizedLyrics = trimmed,
                    LanguageCode = "eng"
                };
                t.Lyrics = new List<LyricsInfo> { li };
            }

            if (!t.Save())
            {
                error = "The tag writer could not save lyrics to this file.";
                return false;
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = "Access denied. The file may be read-only or in use.";
            return false;
        }
        catch (IOException ex)
        {
            error = $"Could not write the file: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void SetCompilationTag(Track t, bool compilation)
    {
        if (t.AdditionalFields == null)
            return;

        var value = compilation ? "1" : "0";
        t.AdditionalFields["TCMP"] = value;
        t.AdditionalFields["cpil"] = value;
    }
}
