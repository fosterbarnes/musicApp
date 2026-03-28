using System;
using System.Collections.Generic;
using System.Windows.Data;
using musicApp.Converters;

namespace musicApp.Helpers
{
    /// <summary>
    /// Shared column definitions and default visible columns for track list views.
    /// Used by TrackListView and for settings persistence keys.
    /// </summary>
    public static class TrackListColumnConfig
    {
        public class ColumnDefinition
        {
            public string DisplayName { get; set; } = "";
            public string PropertyName { get; set; } = "";
            public string SortPropertyName { get; set; } = "";
            public double DefaultWidth { get; set; } = 150;
            public IValueConverter? Converter { get; set; }
        }

        private static Dictionary<string, ColumnDefinition>? _columnDefinitions;
        private static Dictionary<string, List<string>>? _defaultVisibleColumns;

        public static Dictionary<string, ColumnDefinition> ColumnDefinitions
        {
            get
            {
                if (_columnDefinitions == null)
                    Initialize();
                return _columnDefinitions!;
            }
        }

        public static Dictionary<string, List<string>> DefaultVisibleColumns
        {
            get
            {
                if (_defaultVisibleColumns == null)
                    Initialize();
                return _defaultVisibleColumns!;
            }
        }

        public static void Initialize()
        {
            _columnDefinitions = new Dictionary<string, ColumnDefinition>();
            _defaultVisibleColumns = new Dictionary<string, List<string>>();

            _columnDefinitions["#"] = new ColumnDefinition
            {
                DisplayName = "",
                PropertyName = "",
                SortPropertyName = "",
                DefaultWidth = 40
            };

            _columnDefinitions["Title"] = new ColumnDefinition
            {
                DisplayName = "Title",
                PropertyName = "Title",
                SortPropertyName = "Title",
                DefaultWidth = 300
            };

            _columnDefinitions["Album"] = new ColumnDefinition
            {
                DisplayName = "Album",
                PropertyName = "Album",
                SortPropertyName = "Album",
                DefaultWidth = 200
            };

            _columnDefinitions["Album Artist"] = new ColumnDefinition
            {
                DisplayName = "Album Artist",
                PropertyName = "AlbumArtist",
                SortPropertyName = "AlbumArtist",
                DefaultWidth = 200
            };

            _columnDefinitions["Artist"] = new ColumnDefinition
            {
                DisplayName = "Artist",
                PropertyName = "Artist",
                SortPropertyName = "Artist",
                DefaultWidth = 200
            };

            _columnDefinitions["Beats Per Minute"] = new ColumnDefinition
            {
                DisplayName = "Beats Per Minute",
                PropertyName = "BeatsPerMinute",
                SortPropertyName = "BeatsPerMinute",
                DefaultWidth = 120,
                Converter = new IntConverter()
            };

            _columnDefinitions["Bit Rate"] = new ColumnDefinition
            {
                DisplayName = "Bit Rate",
                PropertyName = "Bitrate",
                SortPropertyName = "Bitrate",
                DefaultWidth = 100
            };

            _columnDefinitions["Category"] = new ColumnDefinition
            {
                DisplayName = "Category",
                PropertyName = "Category",
                SortPropertyName = "Category",
                DefaultWidth = 150
            };

            _columnDefinitions["Composer"] = new ColumnDefinition
            {
                DisplayName = "Composer",
                PropertyName = "Composer",
                SortPropertyName = "Composer",
                DefaultWidth = 200
            };

            _columnDefinitions["Date Added"] = new ColumnDefinition
            {
                DisplayName = "Date Added",
                PropertyName = "DateAdded",
                SortPropertyName = "DateAdded",
                DefaultWidth = 120,
                Converter = new DateConverter()
            };

            _columnDefinitions["Date Modified"] = new ColumnDefinition
            {
                DisplayName = "Date Modified",
                PropertyName = "DateModified",
                SortPropertyName = "DateModified",
                DefaultWidth = 120,
                Converter = new DateConverter()
            };

            _columnDefinitions["Disc Number"] = new ColumnDefinition
            {
                DisplayName = "Disc Number",
                PropertyName = "DiscNumber",
                SortPropertyName = "DiscNumber",
                DefaultWidth = 100
            };

            _columnDefinitions["File Type"] = new ColumnDefinition
            {
                DisplayName = "File Type",
                PropertyName = "FileType",
                SortPropertyName = "FileType",
                DefaultWidth = 100
            };

            _columnDefinitions["Genre"] = new ColumnDefinition
            {
                DisplayName = "Genre",
                PropertyName = "Genre",
                SortPropertyName = "Genre",
                DefaultWidth = 150
            };

            _columnDefinitions["Last Played"] = new ColumnDefinition
            {
                DisplayName = "Last Played",
                PropertyName = "LastPlayed",
                SortPropertyName = "LastPlayed",
                DefaultWidth = 120,
                Converter = new DateConverter()
            };

            _columnDefinitions["Plays"] = new ColumnDefinition
            {
                DisplayName = "Plays",
                PropertyName = "PlayCount",
                SortPropertyName = "PlayCount",
                DefaultWidth = 80,
                Converter = new IntConverter()
            };

            _columnDefinitions["Release Date"] = new ColumnDefinition
            {
                DisplayName = "Release Date",
                PropertyName = "ReleaseDate",
                SortPropertyName = "ReleaseDate",
                DefaultWidth = 120,
                Converter = new NullableDateConverter()
            };

            _columnDefinitions["Sample Rate"] = new ColumnDefinition
            {
                DisplayName = "Sample Rate",
                PropertyName = "SampleRate",
                SortPropertyName = "SampleRate",
                DefaultWidth = 120
            };

            _columnDefinitions["Size"] = new ColumnDefinition
            {
                DisplayName = "Size",
                PropertyName = "FileSize",
                SortPropertyName = "FileSize",
                DefaultWidth = 100,
                Converter = new FileSizeConverter()
            };

            _columnDefinitions["Time"] = new ColumnDefinition
            {
                DisplayName = "Time",
                PropertyName = "Duration",
                SortPropertyName = "DurationTimeSpan",
                DefaultWidth = 80
            };

            _columnDefinitions["Track Number"] = new ColumnDefinition
            {
                DisplayName = "Track Number",
                PropertyName = "TrackNumber",
                SortPropertyName = "TrackNumber",
                DefaultWidth = 100,
                Converter = new IntConverter()
            };

            _columnDefinitions["Year"] = new ColumnDefinition
            {
                DisplayName = "Year",
                PropertyName = "Year",
                SortPropertyName = "Year",
                DefaultWidth = 80,
                Converter = new IntConverter()
            };

            _defaultVisibleColumns["Songs"] = new List<string> { "Title", "Artist", "Album", "Time" };
            _defaultVisibleColumns["Queue"] = new List<string> { "#", "Title", "Artist", "Album", "Time" };
            _defaultVisibleColumns["Artists"] = new List<string> { "Title", "Artist", "Album", "Time" };
            _defaultVisibleColumns["Albums"] = new List<string> { "Title", "Artist", "Album", "Time" };
            _defaultVisibleColumns["Genres"] = new List<string> { "Title", "Artist", "Album", "Time" };
            _defaultVisibleColumns["Recently Played"] = new List<string> { "Title", "Artist", "Album", "Time" };
            _defaultVisibleColumns["Playlist"] = new List<string> { "Title", "Artist", "Album", "Time" };
        }
    }
}
