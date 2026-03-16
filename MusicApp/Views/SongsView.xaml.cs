using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MusicApp.Views
{
    public partial class SongsView : UserControl
    {
        public static readonly DependencyProperty IsLibraryEmptyProperty = DependencyProperty.Register(
            nameof(IsLibraryEmpty), typeof(bool), typeof(SongsView), new PropertyMetadata(true));

        private INotifyCollectionChanged? _itemsSourceCollection;

        public SongsView()
        {
            InitializeComponent();

            trackList.AddToPlaylistRequested += (s, track) => AddToPlaylistRequested?.Invoke(this, track);
            trackList.AddTrackToPlaylistRequested += (s, args) => AddTrackToPlaylistRequested?.Invoke(this, args);
            trackList.CreateNewPlaylistWithTrackRequested += (s, track) => CreateNewPlaylistWithTrackRequested?.Invoke(this, track);
            trackList.PlayNextRequested += (s, track) => PlayNextRequested?.Invoke(this, track);
            trackList.AddToQueueRequested += (s, track) => AddToQueueRequested?.Invoke(this, track);
            trackList.InfoRequested += (s, track) => InfoRequested?.Invoke(this, track);
            trackList.ShowInExplorerRequested += (s, track) => ShowInExplorerRequested?.Invoke(this, track);
            trackList.RemoveFromLibraryRequested += (s, track) => RemoveFromLibraryRequested?.Invoke(this, track);
            trackList.DeleteRequested += (s, track) => DeleteRequested?.Invoke(this, track);
        }

        public bool IsLibraryEmpty
        {
            get => (bool)GetValue(IsLibraryEmptyProperty);
            set => SetValue(IsLibraryEmptyProperty, value);
        }

        public System.Collections.IEnumerable? ItemsSource
        {
            get => trackList.ItemsSource;
            set
            {
                if (_itemsSourceCollection != null)
                {
                    _itemsSourceCollection.CollectionChanged -= OnItemsSourceCollectionChanged;
                    _itemsSourceCollection = null;
                }
                trackList.ItemsSource = value;
                UpdateIsLibraryEmpty(value);
                if (value is INotifyCollectionChanged incc)
                {
                    _itemsSourceCollection = incc;
                    incc.CollectionChanged += OnItemsSourceCollectionChanged;
                }
            }
        }

        public event System.EventHandler? AddMusicFolderRequested;

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

        public void RebuildColumns() => trackList.RebuildColumns();

        public Song? SelectedTrack => trackList.SelectedTrack;

        private void TrackList_PlayTrackRequested(object? sender, Song e)
        {
            PlayTrackRequested?.Invoke(this, e);
        }

        private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateIsLibraryEmpty(trackList.ItemsSource);
        }

        private void UpdateIsLibraryEmpty(System.Collections.IEnumerable? source)
        {
            bool empty = true;
            if (source != null)
            {
                if (source is ICollection col)
                    empty = col.Count == 0;
                else
                {
                    var e = source.GetEnumerator();
                    try { empty = !e.MoveNext(); }
                    finally { if (e is IDisposable d) d.Dispose(); }
                }
            }
            IsLibraryEmpty = empty;
        }

        private void EmptyOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            AddMusicFolderRequested?.Invoke(this, System.EventArgs.Empty);
        }
    }
}
