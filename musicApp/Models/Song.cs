using System;
using System.Text.Json.Serialization;

namespace musicApp
{
    public class Song
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string Duration { get; set; } = "";
        public string FilePath { get; set; } = "";
        
        [JsonIgnore]
        public TimeSpan DurationTimeSpan { get; set; }
        
        public string AlbumArtPath { get; set; } = "";
        public string ThumbnailCachePath { get; set; } = "";
        public int TrackNumber { get; set; }
        public int Year { get; set; }
        public string Genre { get; set; } = "";

        // Additional fields for future categorization
        public string Composer { get; set; } = "";
        public string AlbumArtist { get; set; } = "";
        public string DiscNumber { get; set; } = "";
        public string Bitrate { get; set; } = "";
        public string SampleRate { get; set; } = "";
        public long FileSize { get; set; }
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public DateTime DateModified { get; set; } = DateTime.MinValue;
        public DateTime LastPlayed { get; set; } = DateTime.MinValue;
        public int PlayCount { get; set; } = 0;
        public string Category { get; set; } = "";
        public string FileType { get; set; } = "";
        public DateTime? ReleaseDate { get; set; }
        public int BeatsPerMinute { get; set; } = 0;
        public string Comment { get; set; } = "";
        [JsonIgnore]
        public string Lyrics { get; set; } = "";
        public bool IsCompilation { get; set; }
        public bool IsFavorite { get; set; }
        public int TrackTotal { get; set; }
        public int DiscTotal { get; set; }

        public override string ToString()
        {
            return $"{Title} - {Artist}";
        }

        // Helper method to update play count and last played
        public void MarkAsPlayed()
        {
            PlayCount++;
            LastPlayed = DateTime.Now;
        }
    }
}
