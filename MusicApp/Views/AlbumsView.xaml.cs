using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MusicApp.Helpers;

namespace MusicApp.Views
{
    public partial class AlbumsView : UserControl
    {
        private const int AlbumArtSizePx = 158;

        private enum AlbumSortMode
        {
            Album,
            Artist
        }

        private AlbumSortMode _sortMode = AlbumSortMode.Album;
        private IEnumerable? _itemsSource;
        private ObservableCollection<AlbumGridItem> _albumItems = new();

        public AlbumsView()
        {
            InitializeComponent();
            AlbumGrid.ItemsSource = _albumItems;
        }

        public IEnumerable? ItemsSource
        {
            get => _itemsSource;
            set
            {
                _itemsSource = value;
                RebuildAlbumItems();
            }
        }

        public event System.EventHandler<AlbumGridItem>? AlbumClicked;

        public event System.EventHandler<Song>? PlayTrackRequested;
        public event System.EventHandler<Song>? AddToPlaylistRequested;
        public event System.EventHandler<(Song track, Playlist playlist)>? AddTrackToPlaylistRequested;
        public event System.EventHandler<Song>? CreateNewPlaylistWithTrackRequested;
        public event System.EventHandler<Song>? PlayNextRequested;
        public event System.EventHandler<Song>? AddToQueueRequested;
        public event System.EventHandler<Song>? InfoRequested;
        public event System.EventHandler<Song>? ShowInExplorerRequested;
        public event System.EventHandler<Song>? RemoveFromLibraryRequested;
        public event System.EventHandler<Song>? DeleteRequested;

        public void RebuildColumns() { /* No-op for grid; kept for API compatibility */ }

        private void RebuildAlbumItems()
        {
            _albumItems.Clear();

            if (_itemsSource is not IEnumerable<Song> songs)
                return;

            var grouped = songs
                .Where(t => !string.IsNullOrWhiteSpace(t.Album) && t.Album != "Unknown Album")
                .GroupBy(t => (Album: t.Album ?? "", Artist: t.Artist ?? ""))
                .Select(g => new AlbumGridItem(g.Key.Album, g.Key.Artist, g.First()));

            // Apply sort mode
            grouped = _sortMode switch
            {
                AlbumSortMode.Artist => grouped
                    .OrderBy(a => a.Artist, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(a => a.AlbumTitle, StringComparer.OrdinalIgnoreCase),
                _ => grouped
                    .OrderBy(a => a.AlbumTitle, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(a => a.Artist, StringComparer.OrdinalIgnoreCase)
            };

            foreach (var item in grouped)
                _albumItems.Add(item);

            _ = LoadAlbumArtWhenReadyAsync();
        }

        private async System.Threading.Tasks.Task LoadAlbumArtWhenReadyAsync()
        {
            foreach (var item in _albumItems)
            {
                var img = await System.Threading.Tasks.Task.Run(() =>
                    AlbumArtThumbnailHelper.LoadForTrack(item.RepresentativeTrack, AlbumArtSizePx))
                    .ConfigureAwait(false);
                if (img != null)
                    await Dispatcher.InvokeAsync(() => item.AlbumArtSource = img);
            }
        }

        private void AlbumItem_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AlbumGridItem item)
                AlbumClicked?.Invoke(this, item);
        }

        private void SortModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
                return;

            if (SortModeCombo.SelectedIndex == 1)
                _sortMode = AlbumSortMode.Artist;
            else
                _sortMode = AlbumSortMode.Album;

            RebuildAlbumItems();
        }
    }
}
