using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using musicApp.Helpers;

namespace musicApp.Views
{
    public partial class SongsView : TrackListHostBase
    {
        public static readonly DependencyProperty IsLibraryEmptyProperty = DependencyProperty.Register(
            nameof(IsLibraryEmpty), typeof(bool), typeof(SongsView), new PropertyMetadata(true));

        private INotifyCollectionChanged? _itemsSourceCollection;

        protected override TrackListView TrackList => trackList;

        public SongsView()
        {
            InitializeComponent();
            WireTrackList();
            emptyOverlay.AddMusicFolderRequested += (_, __) => AddMusicFolderRequested?.Invoke(this, EventArgs.Empty);
        }

        public bool IsLibraryEmpty
        {
            get => (bool)GetValue(IsLibraryEmptyProperty);
            set => SetValue(IsLibraryEmptyProperty, value);
        }

        public new System.Collections.IEnumerable? ItemsSource
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

        public event EventHandler? AddMusicFolderRequested;

        public void ScrollToSong(Song song) => trackList.ScrollToSong(song);

        public void SelectTrack(Song track, bool grabFocus = false)
        {
            if (track == null)
                return;

            if (trackList.ItemsSource == null)
            {
                trackList.ScrollToSong(track, grabFocus);
                return;
            }

            var matched = SongIdentity.FindInEnumerable(trackList.ItemsSource, track) ?? track;
            trackList.ScrollToSong(matched, grabFocus);
        }

        private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            UpdateIsLibraryEmpty(trackList.ItemsSource);

        private void UpdateIsLibraryEmpty(System.Collections.IEnumerable? source) =>
            IsLibraryEmpty = EmptyLibraryAddOverlay.IsTrackLibraryEmpty(source);
    }
}
