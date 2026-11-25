using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace musicApp.Helpers
{
    public static class M3uPlaylistHelper
    {
        public readonly record struct M3uEntry(string FilePath, string? Title, TimeSpan? Duration);

        /// <summary>
        /// Parses an M3U or M3U8 playlist file into a sequence of entries.
        /// Supports #EXTM3U header and #EXTINF lines, but also tolerates plain path-only lists.
        /// </summary>
        public static IEnumerable<M3uEntry> Parse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path must not be null or empty.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("M3U file not found.", filePath);

            string? pendingTitle = null;
            TimeSpan? pendingDuration = null;

            using var reader = new StringReader(ReadPlaylistText(filePath));
            for (var rawLine = reader.ReadLine(); rawLine != null; rawLine = reader.ReadLine())
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    // Format: #EXTINF:<seconds>,Artist - Title
                    try
                    {
                        var colonIndex = line.IndexOf(':');
                        var commaIndex = line.IndexOf(',');
                        if (colonIndex >= 0 && commaIndex > colonIndex)
                        {
                            var durationPart = line.Substring(colonIndex + 1, commaIndex - colonIndex - 1);
                            if (int.TryParse(durationPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                            {
                                pendingDuration = TimeSpan.FromSeconds(seconds);
                            }
                            else
                            {
                                pendingDuration = null;
                            }

                            var titlePart = line.Substring(commaIndex + 1).Trim();
                            pendingTitle = string.IsNullOrWhiteSpace(titlePart) ? null : titlePart;
                        }
                    }
                    catch
                    {
                        // Malformed EXTINF; ignore and move on
                        pendingTitle = null;
                        pendingDuration = null;
                    }

                    continue;
                }

                if (line.StartsWith("#"))
                {
                    // Other comment/metadata lines are ignored
                    continue;
                }

                // Treat as path line
                var entry = new M3uEntry(line, pendingTitle, pendingDuration);
                yield return entry;

                // Reset pending metadata once consumed
                pendingTitle = null;
                pendingDuration = null;
            }
        }

        /// <summary>
        /// Decodes each line as UTF-8 when it is valid UTF-8 (.m3u8, this app's exports), otherwise in the Windows
        /// ANSI code page that plain .m3u files from older players use; reading those as UTF-8 garbled accented
        /// paths so they never matched the library. Per line, so one odd title byte doesn't recode the paths.
        /// </summary>
        private static string ReadPlaylistText(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var ansi = CodePagesEncodingProvider.Instance.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage)
                       ?? Encoding.Latin1;
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;

            var text = new StringBuilder(bytes.Length);
            while (start < bytes.Length)
            {
                int end = Array.IndexOf(bytes, (byte)'\n', start);
                if (end < 0)
                    end = bytes.Length;
                var line = bytes.AsSpan(start, end - start);
                try
                {
                    text.Append(utf8.GetString(line));
                }
                catch (DecoderFallbackException)
                {
                    text.Append(ansi.GetString(line));
                }
                text.Append('\n');
                start = end + 1;
            }
            return text.ToString();
        }

        /// <summary>
        /// Writes a playlist to an M3U file using Song metadata where available.
        /// </summary>
        public static void Write(string filePath, IEnumerable<Song> tracks)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path must not be null or empty.", nameof(filePath));
            if (tracks == null)
                throw new ArgumentNullException(nameof(tracks));

            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            writer.WriteLine("#EXTM3U");

            foreach (var track in tracks)
            {
                if (track == null || string.IsNullOrWhiteSpace(track.FilePath))
                    continue;

                int durationSeconds = 0;
                if (track.DurationTimeSpan != TimeSpan.Zero)
                    durationSeconds = (int)track.DurationTimeSpan.TotalSeconds;

                var artist = string.IsNullOrWhiteSpace(track.Artist) ? "Unknown Artist" : track.Artist;
                var title = string.IsNullOrWhiteSpace(track.Title) ? Path.GetFileNameWithoutExtension(track.FilePath) : track.Title;

                writer.WriteLine($"#EXTINF:{durationSeconds},{artist} - {title}");
                writer.WriteLine(track.FilePath);
            }
        }
    }
}

