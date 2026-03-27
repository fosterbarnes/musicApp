using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using musicApp;
using musicApp.Constants;
using musicApp.Dialogs;
using musicApp.Helpers;
using NAudio.Wave;

namespace musicApp.Views;

public partial class InfoMetadataView : Window
{
    private static readonly string[] InfoSectionOrder =
    {
        "Details", "Artwork", "Lyrics", "Options", "Sorting", "File"
    };

    private const string FileInfoEmpty = "\u2014";

    private Song? _track;
    private List<Song>? _libraryTracks;
    private bool _syncingPrimaryFields;
    private byte[]? _pendingFrontCoverPicture;
    private string? _lyricsExternalTempPath;
    private byte[]? _lyricsExternalBaselineHash;
    private DispatcherTimer? _lyricsPollTimer;

    public event EventHandler<Song>? ShowInSongsRequested;
    public event EventHandler<Song>? ShowInArtistsRequested;
    public event EventHandler<Song>? ShowInAlbumsRequested;

    public event EventHandler<Song>? SavedMetadataToDisk;

    public Func<string, MetadataAudioReleaseResult>? ReleasePlaybackForFile { get; set; }
    public Action<MetadataAudioReleaseResult>? RestorePlaybackAfterFile { get; set; }

    public bool MetadataSavedOnClose { get; private set; }

    public bool HostNotifyRefreshDone { get; private set; }

    public InfoMetadataView(string? launchSection = null)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowsTitleBarTheme.ApplyImmersiveDarkMode(this);
        Closing += InfoMetadataView_Closing;
        Activated += (_, _) => TryApplyLyricsFromTempIfChanged();
        GenreComboBox.ItemsSource = Array.Empty<string>();
        WirePrimarySortingFieldSync();
        WireSortAsFields();
        PopulatePlaceholderValues();
        ShowSection(launchSection ?? "Details");
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        WindowFocusHelper.ScheduleActivateOwner(this);
    }

    private void WirePrimarySortingFieldSync()
    {
        void Pair(TextBox details, TextBox sorting)
        {
            details.TextChanged += PrimaryMetadata_TextChanged;
            sorting.TextChanged += PrimaryMetadata_TextChanged;
        }

        Pair(SongTitleTextBox, SortingSongTitleTextBox);
        Pair(AlbumTextBox, SortingAlbumTextBox);
        Pair(AlbumArtistTextBox, SortingAlbumArtistTextBox);
        Pair(ArtistTextBox, SortingArtistTextBox);
        Pair(ComposerTextBox, SortingComposerTextBox);
    }

    private void WireSortAsFields()
    {
        SortAsTitleTextBox.TextChanged += SortAsField_TextChanged;
        SortAsAlbumTextBox.TextChanged += SortAsField_TextChanged;
        SortAsAlbumArtistTextBox.TextChanged += SortAsField_TextChanged;
        SortAsArtistTextBox.TextChanged += SortAsField_TextChanged;
        SortAsComposerTextBox.TextChanged += SortAsField_TextChanged;
    }

    private void PrimaryMetadata_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingPrimaryFields)
            return;

        _syncingPrimaryFields = true;
        try
        {
            if (ReferenceEquals(sender, SongTitleTextBox))
                SortingSongTitleTextBox.Text = SongTitleTextBox.Text;
            else if (ReferenceEquals(sender, SortingSongTitleTextBox))
                SongTitleTextBox.Text = SortingSongTitleTextBox.Text;
            else if (ReferenceEquals(sender, AlbumTextBox))
                SortingAlbumTextBox.Text = AlbumTextBox.Text;
            else if (ReferenceEquals(sender, SortingAlbumTextBox))
                AlbumTextBox.Text = SortingAlbumTextBox.Text;
            else if (ReferenceEquals(sender, AlbumArtistTextBox))
                SortingAlbumArtistTextBox.Text = AlbumArtistTextBox.Text;
            else if (ReferenceEquals(sender, SortingAlbumArtistTextBox))
                AlbumArtistTextBox.Text = SortingAlbumArtistTextBox.Text;
            else if (ReferenceEquals(sender, ArtistTextBox))
                SortingArtistTextBox.Text = ArtistTextBox.Text;
            else if (ReferenceEquals(sender, SortingArtistTextBox))
                ArtistTextBox.Text = SortingArtistTextBox.Text;
            else if (ReferenceEquals(sender, ComposerTextBox))
                SortingComposerTextBox.Text = ComposerTextBox.Text;
            else if (ReferenceEquals(sender, SortingComposerTextBox))
                ComposerTextBox.Text = SortingComposerTextBox.Text;

            RefreshHeaderFromPrimaryEditors();
            UpdateAllSortAsPlaceholders();
        }
        finally
        {
            _syncingPrimaryFields = false;
        }
    }

    private void RefreshHeaderFromPrimaryEditors()
    {
        var title = SongTitleTextBox.Text ?? "";
        var artist = ArtistTextBox.Text ?? "";
        var album = AlbumTextBox.Text ?? "";
        TopSongNameText.Text = string.IsNullOrWhiteSpace(title) ? "Song Name" : SongTitleTextBox.Text;
        TopArtistNameText.Text = string.IsNullOrWhiteSpace(artist) ? "Artist Name" : ArtistTextBox.Text;
        TopAlbumNameText.Text = string.IsNullOrWhiteSpace(album) ? "Album Name" : AlbumTextBox.Text;
    }

    private void SortAsField_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateAllSortAsPlaceholders();
    }

    private void UpdateAllSortAsPlaceholders()
    {
        var bg = (Brush)FindResource("ControlBackground-brush");

        void Row(TextBox sortAs, TextBlock placeholder)
        {
            var empty = string.IsNullOrEmpty(sortAs.Text);
            placeholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            sortAs.Background = empty ? Brushes.Transparent : bg;
        }

        Row(SortAsTitleTextBox, SortAsTitlePlaceholder);
        Row(SortAsAlbumTextBox, SortAsAlbumPlaceholder);
        Row(SortAsAlbumArtistTextBox, SortAsAlbumArtistPlaceholder);
        Row(SortAsArtistTextBox, SortAsArtistPlaceholder);
        Row(SortAsComposerTextBox, SortAsComposerPlaceholder);
    }

    private void ClearSortAsFields()
    {
        SortAsTitleTextBox.Text = "";
        SortAsAlbumTextBox.Text = "";
        SortAsAlbumArtistTextBox.Text = "";
        SortAsArtistTextBox.Text = "";
        SortAsComposerTextBox.Text = "";
    }

    private static List<string> BuildGenreList(IEnumerable<Song> libraryTracks, string? currentGenre)
    {
        var list = libraryTracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Genre))
            .Select(t => t.Genre)
            .Distinct()
            .ToList();

        if (!string.IsNullOrWhiteSpace(currentGenre) &&
            !list.Any(g => string.Equals(g, currentGenre, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(currentGenre);
        }

        return list.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void PopulatePlaceholderValues()
    {
        TopSongNameText.Text = "Take On Me";
        TopArtistNameText.Text = "a-ha";
        TopAlbumNameText.Text = "Hunting High and Low";

        SongTitleTextBox.Text = "Take On Me";
        ArtistTextBox.Text = "a-ha";
        AlbumTextBox.Text = "Hunting High and Low";
        PlayCountTextBox.Text = "0";
        ClearSortAsFields();
        UpdateAllSortAsPlaceholders();
        ClearFileInfoPanel();
    }

    public void LoadTrack(Song track, IEnumerable<Song>? libraryTracks = null)
    {
        if (track == null)
        {
            return;
        }

        _track = track;
        _pendingFrontCoverPicture = null;
        _libraryTracks = libraryTracks?.ToList();

        var genres = BuildGenreList(libraryTracks ?? Array.Empty<Song>(), track.Genre);
        GenreComboBox.ItemsSource = genres;

        TopSongNameText.Text = string.IsNullOrWhiteSpace(track.Title) ? "Song Name" : track.Title;
        TopArtistNameText.Text = string.IsNullOrWhiteSpace(track.Artist) ? "Artist Name" : track.Artist;
        TopAlbumNameText.Text = string.IsNullOrWhiteSpace(track.Album) ? "Album Name" : track.Album;

        SongTitleTextBox.Text = track.Title ?? string.Empty;
        ArtistTextBox.Text = track.Artist ?? string.Empty;
        AlbumTextBox.Text = track.Album ?? string.Empty;
        AlbumArtistTextBox.Text = track.AlbumArtist ?? string.Empty;
        ComposerTextBox.Text = track.Composer ?? string.Empty;
        YearTextBox.Text = track.Year > 0 ? track.Year.ToString() : string.Empty;
        TrackNumberTextBox.Text = track.TrackNumber > 0 ? track.TrackNumber.ToString() : string.Empty;
        TrackTotalTextBox.Text = track.TrackTotal > 0 ? track.TrackTotal.ToString() : string.Empty;
        DiscNumberTextBox.Text = track.DiscNumber ?? string.Empty;
        DiscTotalTextBox.Text = track.DiscTotal > 0 ? track.DiscTotal.ToString() : string.Empty;
        BpmTextBox.Text = track.BeatsPerMinute > 0 ? track.BeatsPerMinute.ToString() : string.Empty;
        PlayCountTextBox.Text = track.PlayCount.ToString();
        CommentsTextBox.Text = track.Comment ?? string.Empty;
        CompilationCheckBox.IsChecked = track.IsCompilation;
        FavoriteCheckBox.IsChecked = track.IsFavorite;

        if (!string.IsNullOrWhiteSpace(track.Genre))
        {
            string? match = null;
            foreach (var g in genres)
            {
                if (string.Equals(g, track.Genre, StringComparison.OrdinalIgnoreCase))
                {
                    match = g;
                    break;
                }
            }

            if (match != null)
            {
                GenreComboBox.SelectedItem = match;
                GenreComboBox.Text = match;
            }
            else
            {
                GenreComboBox.SelectedItem = null;
                GenreComboBox.Text = track.Genre;
            }
        }
        else
        {
            GenreComboBox.SelectedItem = null;
            GenreComboBox.Text = string.Empty;
        }

        LoadAlbumArt(track);
        RefreshLyricsUi();

        ClearSortAsFields();
        UpdateAllSortAsPlaceholders();
        RefreshFileInfoPanel(track);
    }

    private void ClearFileInfoPanel()
    {
        FileKindValueText.Text = FileInfoEmpty;
        FileDurationValueText.Text = FileInfoEmpty;
        FileSizeValueText.Text = FileInfoEmpty;
        FileBitrateValueText.Text = FileInfoEmpty;
        FileSampleSizeValueText.Text = FileInfoEmpty;
        FileSampleRateValueText.Text = FileInfoEmpty;
        FileChannelsValueText.Text = FileInfoEmpty;
        FileVolumeValueText.Text = FileInfoEmpty;
        FileDateModifiedValueText.Text = FileInfoEmpty;
        FileDateAddedValueText.Text = FileInfoEmpty;
        FileLocationValueText.Text = FileInfoEmpty;
    }

    private void RefreshFileInfoPanel(Song track)
    {
        var path = track.FilePath ?? "";
        FileLocationValueText.Text = string.IsNullOrWhiteSpace(path) ? FileInfoEmpty : path;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            FileKindValueText.Text = string.IsNullOrWhiteSpace(track.FileType) ? FileInfoEmpty : DescribeFileKindFromExtensionOnly(track.FileType);
            FileDurationValueText.Text = string.IsNullOrWhiteSpace(track.Duration) ? FileInfoEmpty : track.Duration;
            FileSizeValueText.Text = track.FileSize > 0 ? FormatFileSize(track.FileSize) : FileInfoEmpty;
            FileBitrateValueText.Text = string.IsNullOrWhiteSpace(track.Bitrate) ? FileInfoEmpty : track.Bitrate;
            FileSampleSizeValueText.Text = FileInfoEmpty;
            FileSampleRateValueText.Text = string.IsNullOrWhiteSpace(track.SampleRate) ? FileInfoEmpty : track.SampleRate;
            FileChannelsValueText.Text = FileInfoEmpty;
            FileVolumeValueText.Text = FileInfoEmpty;
            FileDateModifiedValueText.Text = track.DateModified != DateTime.MinValue
                ? track.DateModified.ToString("g", CultureInfo.CurrentCulture)
                : FileInfoEmpty;
            FileDateAddedValueText.Text = track.DateAdded != DateTime.MinValue
                ? track.DateAdded.ToString("g", CultureInfo.CurrentCulture)
                : FileInfoEmpty;
            return;
        }

        long sizeBytes = track.FileSize;
        DateTime dateModified = track.DateModified;
        try
        {
            var fi = new FileInfo(path);
            sizeBytes = fi.Length;
            dateModified = fi.LastWriteTime;
        }
        catch
        {
            // keep track values
        }

        TryReadWaveFormat(path, out int channels, out int bitsPerSample, out int sampleRateHz);

        FileKindValueText.Text = DescribeFileKind(path);
        FileDurationValueText.Text = string.IsNullOrWhiteSpace(track.Duration) ? FileInfoEmpty : track.Duration;
        FileSizeValueText.Text = sizeBytes > 0 ? FormatFileSize(sizeBytes) : FileInfoEmpty;
        FileBitrateValueText.Text = string.IsNullOrWhiteSpace(track.Bitrate) ? FileInfoEmpty : track.Bitrate;
        FileSampleSizeValueText.Text = bitsPerSample > 0 ? $"{bitsPerSample} bit" : FileInfoEmpty;
        FileSampleRateValueText.Text = sampleRateHz > 0
            ? $"{sampleRateHz / 1000.0:F3} kHz"
            : (string.IsNullOrWhiteSpace(track.SampleRate) ? FileInfoEmpty : track.SampleRate);
        FileChannelsValueText.Text = FormatChannels(channels);
        FileVolumeValueText.Text = FileInfoEmpty;

        FileDateModifiedValueText.Text = dateModified != DateTime.MinValue
            ? dateModified.ToString("g", CultureInfo.CurrentCulture)
            : FileInfoEmpty;
        FileDateAddedValueText.Text = track.DateAdded != DateTime.MinValue
            ? track.DateAdded.ToString("g", CultureInfo.CurrentCulture)
            : FileInfoEmpty;
    }

    private static string DescribeFileKindFromExtensionOnly(string fileType)
    {
        var t = fileType.Trim();
        if (string.IsNullOrEmpty(t))
            return FileInfoEmpty;
        return $"{t.ToUpperInvariant()} audio file";
    }

    private static string DescribeFileKind(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp3" => "MP3 audio file",
            ".m4a" => "Apple MPEG-4 audio file",
            ".aac" => "AAC audio file",
            ".flac" => "FLAC audio file",
            ".wav" => "WAV audio file",
            ".ogg" or ".oga" => "Ogg audio file",
            ".wma" => "Windows Media Audio file",
            ".opus" => "Opus audio file",
            ".aiff" or ".aif" => "AIFF audio file",
            ".mpc" => "Musepack audio file",
            _ => string.IsNullOrEmpty(ext)
                ? "Audio file"
                : $"{ext.TrimStart('.').ToUpperInvariant()} audio file"
        };
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024)
            return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024)
            return $"{mb:F1} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }

    private static string FormatChannels(int channels)
    {
        return channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            > 2 => $"{channels} channels",
            _ => FileInfoEmpty
        };
    }

    private static void TryReadWaveFormat(string path, out int channels, out int bitsPerSample, out int sampleRate)
    {
        channels = 0;
        bitsPerSample = 0;
        sampleRate = 0;
        try
        {
            using var audio = new AudioFileReader(path);
            var wf = audio.WaveFormat;
            channels = wf.Channels;
            bitsPerSample = wf.BitsPerSample;
            sampleRate = wf.SampleRate;
        }
        catch
        {
            // NAudio cannot open this format or file is locked
        }
    }

    private void LoadAlbumArt(Song track)
    {
        TopAlbumArtImage.Source = null;
        ArtworkPreviewImage.Source = null;

        var thumb = AlbumArtThumbnailHelper.LoadForTrack(track, UILayoutConstants.InfoMetadataAlbumArtSize);
        TopAlbumArtImage.Source = thumb;

        var fullPreview = AlbumArtThumbnailHelper.LoadFullSizeForTrack(track);
        if (fullPreview != null)
        {
            ArtworkPreviewImage.Source = fullPreview;
            return;
        }

        var albumArtPath = track.AlbumArtPath;
        if (string.IsNullOrWhiteSpace(albumArtPath) || !File.Exists(albumArtPath))
            return;

        try
        {
            var full = new BitmapImage();
            full.BeginInit();
            full.CacheOption = BitmapCacheOption.OnLoad;
            full.UriSource = new Uri(albumArtPath, UriKind.Absolute);
            full.EndInit();
            full.Freeze();
            ArtworkPreviewImage.Source = full;

            if (thumb == null)
            {
                var header = new BitmapImage();
                header.BeginInit();
                header.CacheOption = BitmapCacheOption.OnLoad;
                header.UriSource = new Uri(albumArtPath, UriKind.Absolute);
                header.DecodePixelWidth = UILayoutConstants.InfoMetadataAlbumArtSize;
                header.EndInit();
                header.Freeze();
                TopAlbumArtImage.Source = header;
            }
        }
        catch
        {
            TopAlbumArtImage.Source = null;
            ArtworkPreviewImage.Source = null;
        }
    }

    private static BitmapImage? BitmapImageFromBytes(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = ms;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void ApplyHeaderFromSong()
    {
        if (_track == null)
            return;
        TopSongNameText.Text = string.IsNullOrWhiteSpace(_track.Title) ? "Song Name" : _track.Title;
        TopArtistNameText.Text = string.IsNullOrWhiteSpace(_track.Artist) ? "Artist Name" : _track.Artist;
        TopAlbumNameText.Text = string.IsNullOrWhiteSpace(_track.Album) ? "Album Name" : _track.Album;
    }

    private string GetGenreForSave()
    {
        if (GenreComboBox.SelectedItem is string sel && !string.IsNullOrWhiteSpace(sel))
            return sel.Trim();
        var text = GenreComboBox.Text?.Trim();
        return text ?? string.Empty;
    }

    private bool TryBuildEdit(out TrackMetadataEdit edit, out string errorMessage)
    {
        edit = new TrackMetadataEdit();
        errorMessage = "";

        if (!TryParseOptionalNonNegativeInt(YearTextBox.Text, 9999, "Year", out int year, out errorMessage))
            return false;
        if (!TryParseOptionalNonNegativeInt(TrackNumberTextBox.Text, 999, "Track number", out int trackNum, out errorMessage))
            return false;
        if (!TryParseOptionalNonNegativeInt(TrackTotalTextBox.Text, 999, "Track total", out int trackTot, out errorMessage))
            return false;
        if (!TryParseOptionalNonNegativeInt(DiscNumberTextBox.Text, 999, "Disc number", out int discNum, out errorMessage))
            return false;
        if (!TryParseOptionalNonNegativeInt(DiscTotalTextBox.Text, 999, "Disc total", out int discTot, out errorMessage))
            return false;
        if (!TryParseOptionalNonNegativeInt(BpmTextBox.Text, 9999, "BPM", out int bpm, out errorMessage))
            return false;
        if (!TryParseOptionalNonNegativeInt(PlayCountTextBox.Text, int.MaxValue, "Play count", out int playCount, out errorMessage))
            return false;

        edit.Title = SongTitleTextBox.Text.Trim();
        edit.Artist = ArtistTextBox.Text.Trim();
        edit.Album = AlbumTextBox.Text.Trim();
        edit.AlbumArtist = AlbumArtistTextBox.Text.Trim();
        edit.Composer = ComposerTextBox.Text.Trim();
        edit.Genre = GetGenreForSave();
        edit.Comment = CommentsTextBox.Text.Trim();
        edit.Year = year;
        edit.TrackNumber = trackNum;
        edit.TrackTotal = trackTot;
        edit.DiscNumber = discNum;
        edit.DiscTotal = discTot;
        edit.BeatsPerMinute = bpm;
        edit.Compilation = CompilationCheckBox.IsChecked == true;
        edit.EmbeddedFrontCoverPictureData = _pendingFrontCoverPicture;

        _pendingPlayCount = playCount;
        _pendingFavorite = FavoriteCheckBox.IsChecked == true;

        return true;
    }

    private int _pendingPlayCount;
    private bool _pendingFavorite;

    private static bool TryParseOptionalNonNegativeInt(string? raw, int maxInclusive, string fieldName, out int value, out string error)
    {
        value = 0;
        error = "";
        var t = (raw ?? "").Trim();
        if (string.IsNullOrEmpty(t))
            return true;

        if (!int.TryParse(t, out value) || value < 0 || value > maxInclusive)
        {
            error = $"{fieldName} must be a number between 0 and {maxInclusive}.";
            return false;
        }

        return true;
    }

    private void ApplyPendingAppFieldsToSong()
    {
        if (_track == null)
            return;
        _track.PlayCount = _pendingPlayCount;
        _track.IsFavorite = _pendingFavorite;
    }

    private void SectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string sectionName)
        {
            return;
        }

        ShowSection(sectionName);
    }

    private void ShowSection(string sectionName)
    {
        DetailsSectionPanel.Visibility = sectionName == "Details" ? Visibility.Visible : Visibility.Collapsed;
        ArtworkSectionPanel.Visibility = sectionName == "Artwork" ? Visibility.Visible : Visibility.Collapsed;
        LyricsSectionPanel.Visibility = sectionName == "Lyrics" ? Visibility.Visible : Visibility.Collapsed;
        OptionsSectionPanel.Visibility = sectionName == "Options" ? Visibility.Visible : Visibility.Collapsed;
        SortingSectionPanel.Visibility = sectionName == "Sorting" ? Visibility.Visible : Visibility.Collapsed;
        FileSectionPanel.Visibility = sectionName == "File" ? Visibility.Visible : Visibility.Collapsed;

        var idx = Array.IndexOf(InfoSectionOrder, sectionName);
        SectionSegmentUi.ApplySegmentStates(
            this,
            new[]
            {
                DetailsSectionButton,
                ArtworkSectionButton,
                LyricsSectionButton,
                OptionsSectionButton,
                SortingSectionButton,
                FileSectionButton
            },
            idx < 0 ? 0 : idx);

        var artVis = sectionName == "Artwork" ? Visibility.Visible : Visibility.Collapsed;
        FetchArtworkButton.Visibility = artVis;
        AddArtworkButton.Visibility = artVis;
    }

    private IReadOnlyList<Song> FindOtherLibraryTracksInSameAlbumGroup(string currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath) || _libraryTracks == null || _libraryTracks.Count == 0)
            return Array.Empty<Song>();

        var album = (AlbumTextBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(album) || string.Equals(album, "Unknown Album", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<Song>();

        var albumArtist = (AlbumArtistTextBox.Text ?? "").Trim();
        var artist = (ArtistTextBox.Text ?? "").Trim();
        var artistKey = !string.IsNullOrWhiteSpace(albumArtist) ? albumArtist : artist;

        var list = new List<Song>();
        foreach (var s in _libraryTracks)
        {
            if (string.IsNullOrWhiteSpace(s.FilePath) || !File.Exists(s.FilePath))
                continue;
            if (LibraryPathHelper.PathsEqual(s.FilePath, currentPath))
                continue;

            var sa = (s.Album ?? "").Trim();
            if (!string.Equals(sa, album, StringComparison.OrdinalIgnoreCase))
                continue;

            var albumArtistTrim = (s.AlbumArtist ?? "").Trim();
            var sKey = albumArtistTrim.Length > 0
                ? albumArtistTrim
                : (s.Artist ?? "").Trim();
            if (!string.Equals(sKey, artistKey, StringComparison.OrdinalIgnoreCase))
                continue;

            list.Add(s);
        }

        return list;
    }

    private async void FetchArtworkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_track == null)
            return;

        var path = _track.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageDialog.Show(Owner, "Download artwork", "The file is missing or has no path.", MessageDialog.Buttons.Ok);
            return;
        }

        var album = AlbumTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(album))
        {
            MessageDialog.Show(Owner, "Download artwork", "Album is required to look up cover art.", MessageDialog.Buttons.Ok);
            return;
        }

        var year = 0;
        if (int.TryParse((YearTextBox.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) &&
            y is > 0 and <= 9999)
            year = y;

        var wasEnabledFetch = FetchArtworkButton.IsEnabled;
        var wasEnabledAdd = AddArtworkButton.IsEnabled;
        FetchArtworkButton.IsEnabled = false;
        AddArtworkButton.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var progress = new Progress<string>(p => Debug.WriteLine("[AlbumArt] " + p));

            var fetch = await AlbumArtRemoteFetcher.FetchCoverBytesAsync(
                path,
                album,
                AlbumArtistTextBox.Text?.Trim() ?? "",
                ArtistTextBox.Text?.Trim() ?? "",
                year,
                progress,
                CancellationToken.None).ConfigureAwait(true);

            if (!fetch.Ok || fetch.ImageBytes is not { Length: > 0 } pic)
            {
                MessageDialog.Show(Owner, "Download artwork",
                    fetch.ErrorMessage ?? "Could not download artwork.",
                    MessageDialog.Buttons.Ok);
                return;
            }

            var snap = ReleasePlaybackForFile?.Invoke(path) ?? default;
            try
            {
                if (!TrackMetadataSaver.TrySaveEmbeddedCoverOnly(path, pic, fetch.MusicBrainzReleaseId, out var saveErr))
                {
                    MessageDialog.Show(Owner, "Download artwork",
                        saveErr ?? "Could not save artwork to the file.",
                        MessageDialog.Buttons.Ok);
                    return;
                }
            }
            finally
            {
                RestorePlaybackAfterFile?.Invoke(snap);
            }

            var otherInAlbum = FindOtherLibraryTracksInSameAlbumGroup(path);
            var albumOthersOk = 0;
            var albumOthersFailed = 0;
            if (otherInAlbum.Count > 0)
            {
                var applyAll = MessageDialog.Show(Owner, "Download artwork",
                    $"Apply this artwork to the other {otherInAlbum.Count} track(s) from this album in your library?",
                    MessageDialog.Buttons.YesNo);
                if (applyAll == true)
                {
                    var picForAlbum = (byte[])pic.Clone();
                    foreach (var os in otherInAlbum)
                    {
                        var op = os.FilePath;
                        if (string.IsNullOrWhiteSpace(op) || !File.Exists(op))
                        {
                            albumOthersFailed++;
                            continue;
                        }

                        var oSnap = ReleasePlaybackForFile?.Invoke(op) ?? default;
                        try
                        {
                            if (TrackMetadataSaver.TrySaveEmbeddedCoverOnly(op, picForAlbum, fetch.MusicBrainzReleaseId,
                                    out _))
                            {
                                albumOthersOk++;
                                TrackMetadataLoader.ReloadTagFieldsFromFile(os);
                                AlbumArtThumbnailHelper.InvalidateFullSizeCache(op);
                            }
                            else
                                albumOthersFailed++;
                        }
                        finally
                        {
                            RestorePlaybackAfterFile?.Invoke(oSnap);
                        }
                    }
                }
            }

            TrackMetadataLoader.ReloadTagFieldsFromFile(_track);
            AlbumArtThumbnailHelper.InvalidateFullSizeCache(path);
            _pendingFrontCoverPicture = (byte[])pic.Clone();
            LoadAlbumArt(_track);
            MetadataSavedOnClose = true;

            var src = fetch.Source switch
            {
                AlbumArtFetchSource.TagMbidCoverArtArchive => "Cover Art Archive (tag MBID)",
                AlbumArtFetchSource.MusicBrainzSearchCoverArtArchive => "MusicBrainz + Cover Art Archive",
                AlbumArtFetchSource.FruitAppSearch => "fruitApp (fallback)",
                AlbumArtFetchSource.DeezerSearch => "Deezer (fallback)",
                _ => "Unknown source"
            };
            var doneMsg = $"Artwork embedded in this file ({src}).";
            if (albumOthersOk > 0)
                doneMsg += $"\n\nAlso embedded in {albumOthersOk} other album track(s).";
            if (albumOthersFailed > 0)
                doneMsg += $"\n\nCould not update {albumOthersFailed} album track(s) (file in use, permission, or format).";
            MessageDialog.Show(Owner, "Download artwork", doneMsg, MessageDialog.Buttons.Ok);
            Activate();
            _ = Focus();
            HostNotifyRefreshDone = true;
            SavedMetadataToDisk?.Invoke(this, _track);
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Owner, "Download artwork", ex.Message, MessageDialog.Buttons.Ok);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            FetchArtworkButton.IsEnabled = wasEnabledFetch;
            AddArtworkButton.IsEnabled = wasEnabledAdd;
        }
    }

    private void AddArtworkButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|All files|*.*"
        };
        var picked = dlg.ShowDialog(this) == true;
        WindowFocusHelper.ScheduleActivate(this);
        if (!picked)
            return;

        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            if (bytes.Length == 0)
            {
                MessageDialog.Show(Owner, "Add Artwork", "The file is empty.", MessageDialog.Buttons.Ok);
                return;
            }

            var img = BitmapImageFromBytes(bytes);
            if (img == null)
            {
                MessageDialog.Show(Owner, "Add Artwork", "Could not read that image.", MessageDialog.Buttons.Ok);
                return;
            }

            _pendingFrontCoverPicture = bytes;
            ArtworkPreviewImage.Source = img;
            var headerThumb = AlbumArtThumbnailHelper.ScaleToBitmapImage(bytes, UILayoutConstants.InfoMetadataAlbumArtSize);
            TopAlbumArtImage.Source = headerThumb ?? img;
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Owner, "Add Artwork", ex.Message, MessageDialog.Buttons.Ok);
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_track == null)
            return;

        var path = _track.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageDialog.Show(Owner, "Error", "The file is missing or has no path.", MessageDialog.Buttons.Ok);
            return;
        }

        if (!TryBuildEdit(out var edit, out var validationError))
        {
            MessageDialog.Show(Owner, "Invalid fields", validationError, MessageDialog.Buttons.Ok);
            return;
        }

        var snap = ReleasePlaybackForFile?.Invoke(path) ?? default;

        try
        {
            HostNotifyRefreshDone = false;
            if (!TrackMetadataSaver.TrySave(path, edit, out var saveError))
            {
                RestorePlaybackAfterFile?.Invoke(snap);
                MessageDialog.Show(Owner, "Could not save metadata", saveError ?? "Unknown error.", MessageDialog.Buttons.Ok);
                return;
            }

            TrackMetadataLoader.ReloadTagFieldsFromFile(_track);
            ApplyPendingAppFieldsToSong();
            ApplyHeaderFromSong();
            AlbumArtThumbnailHelper.InvalidateFullSizeCache(path);
            _pendingFrontCoverPicture = null;
            LoadAlbumArt(_track);
            RestorePlaybackAfterFile?.Invoke(snap);
            MetadataSavedOnClose = true;
            Close();
        }
        catch (Exception ex)
        {
            RestorePlaybackAfterFile?.Invoke(snap);
            MessageDialog.Show(Owner, "Error", ex.Message, MessageDialog.Buttons.Ok);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void InfoMetadataView_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopLyricsPolling();
        CleanupLyricsTempFile();
    }

    private void OpenLyricsInExternalEditor_Click(object sender, RoutedEventArgs e)
    {
        BeginLyricsExternalEdit();
    }

    private void OpenInMp3TagButton_Click(object sender, RoutedEventArgs e)
    {
        if (_track == null || string.IsNullOrWhiteSpace(_track.FilePath) || !File.Exists(_track.FilePath))
        {
            MessageDialog.Show(Owner, "Mp3tag", "The file is missing or has no path.", MessageDialog.Buttons.Ok);
            return;
        }

        if (Mp3tagLauncher.TryOpenFile(_track.FilePath, out var err))
            return;

        if (!string.IsNullOrEmpty(err))
        {
            MessageDialog.Show(Owner, "Mp3tag", err, MessageDialog.Buttons.Ok);
            return;
        }

        MessageDialog.Show(Owner, "Mp3tag", "Mp3tag wasn't found. Opening the download page.", MessageDialog.Buttons.Ok);
        Mp3tagLauncher.OpenDownloadPage();
    }

    private void BeginLyricsExternalEdit()
    {
        if (_track == null || string.IsNullOrWhiteSpace(_track.FilePath) || !File.Exists(_track.FilePath))
        {
            MessageDialog.Show(Owner, "Lyrics", "The file is missing or has no path.", MessageDialog.Buttons.Ok);
            return;
        }

        StopLyricsPolling();
        CleanupLyricsTempFile();

        var path = Path.Combine(Path.GetTempPath(), "musicapp-lyrics-" + Guid.NewGuid().ToString("N") + ".txt");
        var initial = _track.Lyrics ?? "";
        try
        {
            File.WriteAllText(path, initial, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Owner, "Lyrics", ex.Message, MessageDialog.Buttons.Ok);
            return;
        }

        _lyricsExternalTempPath = path;
        _lyricsExternalBaselineHash = ComputeUtf8Hash(NormalizeNewlines(initial));

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageDialog.Show(Owner, "Lyrics", ex.Message, MessageDialog.Buttons.Ok);
            CleanupLyricsTempFile();
            return;
        }

        StartLyricsPolling();
    }

    private void StartLyricsPolling()
    {
        if (_lyricsPollTimer == null)
        {
            _lyricsPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _lyricsPollTimer.Tick += LyricsPollTimer_Tick;
        }
        _lyricsPollTimer.Start();
    }

    private void StopLyricsPolling()
    {
        if (_lyricsPollTimer == null) return;
        _lyricsPollTimer.Stop();
        _lyricsPollTimer.Tick -= LyricsPollTimer_Tick;
        _lyricsPollTimer = null;
    }

    private void LyricsPollTimer_Tick(object? sender, EventArgs e)
    {
        TryApplyLyricsFromTempIfChanged();
    }

    private void CleanupLyricsTempFile()
    {
        if (string.IsNullOrEmpty(_lyricsExternalTempPath))
            return;
        try
        {
            if (File.Exists(_lyricsExternalTempPath))
                File.Delete(_lyricsExternalTempPath);
        }
        catch
        {
        }
        _lyricsExternalTempPath = null;
        _lyricsExternalBaselineHash = null;
    }

    private void TryApplyLyricsFromTempIfChanged()
    {
        if (_track == null || string.IsNullOrWhiteSpace(_track.FilePath) || !File.Exists(_track.FilePath))
            return;
        if (string.IsNullOrEmpty(_lyricsExternalTempPath) || !File.Exists(_lyricsExternalTempPath))
            return;

        string raw;
        try
        {
            raw = File.ReadAllText(_lyricsExternalTempPath);
        }
        catch
        {
            return;
        }

        var norm = NormalizeNewlines(raw);
        if (_lyricsExternalBaselineHash != null)
        {
            var curHash = ComputeUtf8Hash(norm);
            if (curHash.AsSpan().SequenceEqual(_lyricsExternalBaselineHash))
                return;
        }

        var snap = ReleasePlaybackForFile?.Invoke(_track.FilePath) ?? default;
        try
        {
            if (!TrackMetadataSaver.TrySaveLyrics(_track.FilePath, raw, out var err))
            {
                RestorePlaybackAfterFile?.Invoke(snap);
                MessageDialog.Show(Owner, "Lyrics", err ?? "Could not save lyrics.", MessageDialog.Buttons.Ok);
                return;
            }

            TrackMetadataLoader.ReloadTagFieldsFromFile(_track);
            RestorePlaybackAfterFile?.Invoke(snap);
            RefreshLyricsUi();

            try
            {
                var n2 = NormalizeNewlines(File.ReadAllText(_lyricsExternalTempPath));
                _lyricsExternalBaselineHash = ComputeUtf8Hash(n2);
            }
            catch
            {
                _lyricsExternalBaselineHash = ComputeUtf8Hash(norm);
            }
        }
        catch (Exception ex)
        {
            RestorePlaybackAfterFile?.Invoke(snap);
            MessageDialog.Show(Owner, "Lyrics", ex.Message, MessageDialog.Buttons.Ok);
        }
    }

    private static string NormalizeNewlines(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static byte[] ComputeUtf8Hash(string text)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(text));
    }

    private void RefreshLyricsUi()
    {
        var has = _track != null && !string.IsNullOrWhiteSpace(_track.Lyrics);
        LyricsEmptyPanel.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        LyricsContentPanel.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        LyricsBodyTextBlock.Text = has ? (_track!.Lyrics ?? "") : "";
    }

    private void TopSongNameText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_track == null)
            return;
        ShowInSongsRequested?.Invoke(this, _track);
        Close();
    }

    private void TopArtistNameText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_track == null || string.IsNullOrWhiteSpace(_track.Artist))
            return;
        ShowInArtistsRequested?.Invoke(this, _track);
        Close();
    }

    private void TopAlbumNameText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_track == null || string.IsNullOrWhiteSpace(_track.Album))
            return;
        ShowInAlbumsRequested?.Invoke(this, _track);
        Close();
    }
}
