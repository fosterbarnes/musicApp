using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using musicApp.Constants;
using musicApp.Dialogs;
using musicApp.Helpers;
using musicApp.Playback;
using musicApp.Views;

namespace musicApp
{
    /// <summary>Connects the UI to <see cref="Player"/>. Playback and queue rules live in Playback/.</summary>
    public partial class MainWindow
    {
        private const string QueueUndoStatusMessage = "Previous queue available - Ctrl+Z or miniplayer";

        private readonly Player _player = new(
            new AudioEngine(
                AudioOutputDeviceFactory.Create,
                path => new FileAudioSource(path),
                new DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher)),
            UILayoutConstants.PreviousTrackEdgeThresholdSeconds);

        private string? _queueUndoStatusMessage;

        private Song? CurrentTrack => _player.CurrentTrack;

        private void InitializePlayer()
        {
            _player.TrackChanged += OnPlayerTrackChanged;
            _player.TrackPlayed += AddToRecentlyPlayed;
            _player.PlaybackStateChanged += OnPlayerStateChanged;
            _player.QueueChanged += OnPlayerQueueChanged;
            _player.Failed += OnPlayerFailed;
            titleBarPlayer.AttachPlayer(_player);
        }

        private void PlayTrack(Song track, object? requestSource = null)
        {
            if (!IsValidTrackWithPath(track) || !File.Exists(track.FilePath))
                return;

            var context = BuildPlayContext(requestSource, track);
            if (context == null && !_player.Queue.Contains(track))
                context = filteredTracks.ToList();
            _player.Play(track, context);
        }

        /// <summary>The ordered tracks for a play request, or null when it comes from the queue.</summary>
        private List<Song>? BuildPlayContext(object? requestSource, Song track)
        {
            List<Song>? ordered = null;
            if (requestSource == null)
                return null;

            if (ReferenceEquals(requestSource, artistsViewControl) && !string.IsNullOrWhiteSpace(track.Artist))
                ordered = ArtistPlaybackOrder.BuildOrderedArtistTracks(allTracks, track.Artist);
            else if (ReferenceEquals(requestSource, genresViewControl) && !string.IsNullOrWhiteSpace(track.Genre))
                ordered = GenrePlaybackOrder.BuildOrderedGenreTracks(allTracks, track.Genre);
            else if (ReferenceEquals(requestSource, songsView))
                ordered = allTracks.ToList();
            else if (ReferenceEquals(requestSource, playlistsViewControl))
                ordered = playlistsViewControl?.SelectedPlaylist?.Tracks.ToList();
            else if (ReferenceEquals(requestSource, recentlyPlayedViewControl))
                ordered = recentlyPlayed.ToList();
            else if (ReferenceEquals(requestSource, albumsViewControl))
                ordered = BuildAlbumsPlayContext(track);

            return ordered is { Count: > 0 } ? ordered : null;
        }

        private List<Song>? BuildAlbumsPlayContext(Song track)
        {
            if (albumsViewControl?.BrowseMode == AlbumsBrowseMode.RecentlyAdded)
                return RecentlyAddedPlaybackOrder.BuildOrderedTracks(allTracks).ToList();

            if (string.IsNullOrWhiteSpace(track.Album))
                return null;

            var albumArtist = !string.IsNullOrWhiteSpace(track.AlbumArtist) ? track.AlbumArtist : track.Artist ?? "";
            return AlbumTrackOrder.SortByAlbumSequence(allTracks.Where(s =>
                string.Equals(s.Album, track.Album, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(!string.IsNullOrWhiteSpace(s.AlbumArtist) ? s.AlbumArtist : s.Artist,
                    albumArtist, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        private void OnPlayerTrackChanged(Song? track)
        {
            if (track == null)
            {
                titleBarPlayer.SetTrackInfo("No track selected", "", "");
                ClearMiniPlayerTrack();
            }
            else
            {
                var art = AlbumArtLoader.LoadAlbumArt(track, GetTitleBarAlbumArtTargetPixelSize());
                titleBarPlayer.SetTrackInfo(track.Title, track.Artist, track.Album, art);
                PushMiniPlayerTrack(track);
            }
            RefreshPlayerControls();
        }

        private void OnPlayerStateChanged(bool isPlaying)
        {
            titleBarPlayer.IsPlaying = isPlaying;
            RefreshPlayerControls();
        }

        private void RefreshPlayerControls()
        {
            titleBarPlayer.RefreshPlaybackState();
            _miniPlayerWindow?.RefreshPlaybackState();
        }

        private void OnPlayerQueueChanged()
        {
            if (ReferenceEquals(contentHost?.Content, queueViewControl) || queuePopup.IsOpen)
                UpdateQueueView();
            else
                PushMiniPlayerQueue();

            bool canUndo = _player.Queue.CanUndo;
            _miniPlayerWindow?.SetQueueUndoAvailable(canUndo);
            var message = canUndo ? QueueUndoStatusMessage : null;
            if (message == _queueUndoStatusMessage)
                return;
            _queueUndoStatusMessage = message;
            UpdateStatusBar();
        }

        private void OnPlayerFailed(Exception ex) =>
            MessageDialog.Show(this, "Error", $"Error playing track: {ex.Message}", MessageDialog.Buttons.Ok);

        private void AddToRecentlyPlayed(Song track)
        {
            var existing = recentlyPlayed.FirstOrDefault(t => t.FilePath == track.FilePath);
            if (existing != null)
                recentlyPlayed.Remove(existing);

            recentlyPlayed.Insert(0, track);
            while (recentlyPlayed.Count > 20)
                recentlyPlayed.RemoveAt(recentlyPlayed.Count - 1);
        }

        private void TitleBarPlayer_PlayPauseRequested(object? sender, EventArgs e)
        {
            if (CurrentTrack != null || _player.Queue.HasItems)
            {
                _player.PlayPause();
                return;
            }

            if (TryResolveTrackToPlayFromActiveView(out var selected, out var requestSource) && selected != null)
                PlayTrack(selected, requestSource);
            else if (filteredTracks.Count > 0)
                PlayTrack(filteredTracks[0]);
        }

        private void TitleBarPlayer_PreviousTrackRequested(object? sender, EventArgs e)
        {
            _player.Previous();
            SelectCurrentTrackInActiveView(false);
        }

        private void TitleBarPlayer_NextTrackRequested(object? sender, EventArgs e)
        {
            _player.Next();
            SelectCurrentTrackInActiveView(false);
        }

        private void TitleBarPlayer_ShuffleStateChanged(object? sender, bool isShuffleEnabled) =>
            _player.SetShuffle(isShuffleEnabled);

        private void TitleBarPlayer_RepeatModeChanged(object? sender, RepeatMode mode) => _player.Repeat = mode;

        private void TitleBarPlayer_VolumeChanged(object? sender, double volume0To100)
        {
            if (_syncingVolume) return;
            _syncingVolume = true;
            try
            {
                bool isMuted = sender is TitleBarPlus.TitleBar tb ? tb.IsMuted
                    : sender is MiniPlayerWindow mp ? mp.IsMuted : titleBarPlayer.IsMuted;
                _player.Volume = isMuted ? 0f : (float)(volume0To100 / 100.0);

                if (sender is not TitleBarPlus.TitleBar)
                    titleBarPlayer.Volume = volume0To100;
                if (sender is not MiniPlayerWindow)
                    _miniPlayerWindow?.SetVolumeWithoutNotify(volume0To100);

                // Mute moves the slider to 0; saving that would bring the app back silent after a restart.
                if (isMuted)
                    return;
                _pendingVolume0To100 = volume0To100;
                if (_volumeSaveTimer == null)
                {
                    _volumeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    _volumeSaveTimer.Tick += async (_, _) =>
                    {
                        _volumeSaveTimer.Stop();
                        try
                        {
                            await SettingsManager.Instance.SetTitleBarVolume0To100Async(_pendingVolume0To100);
                        }
                        catch { }
                    };
                }
                _volumeSaveTimer.Stop();
                _volumeSaveTimer.Start();
            }
            finally { _syncingVolume = false; }
        }

        private void OnPlayNextRequested(object? sender, Song track)
        {
            if (IsValidTrackWithPath(track))
                _player.PlayNext(track);
        }

        private void OnAddToQueueRequested(object? sender, Song track)
        {
            if (IsValidTrackWithPath(track))
                _player.AddToQueue(track);
        }

        private void OnQueueTracksReordered(object? sender, (int fromViewIndex, int toViewIndex) e) =>
            _player.MoveInQueue(e.fromViewIndex, e.toViewIndex);

        private void OnQueueToolbarRemoveRequested(object? sender, EventArgs e)
        {
            int viewIndex = GetQueueToolbarSelectedViewIndex(sender);
            if (viewIndex >= 0)
                _player.RemoveFromQueue(viewIndex);
        }

        private void OnQueueToolbarMoveUpRequested(object? sender, EventArgs e) => MoveQueueSelection(sender, -1);

        private void OnQueueToolbarMoveDownRequested(object? sender, EventArgs e) => MoveQueueSelection(sender, 1);

        private void MoveQueueSelection(object? sender, int step)
        {
            // Row 0 is the current track only while something is loaded; idle queues start with upcoming.
            int firstMovable = _player.Queue.Current != null ? 1 : 0;
            int index = GetQueueToolbarSelectedViewIndex(sender);
            if (index < firstMovable || index + step < firstMovable)
                return;

            var moved = GetQueueToolbarSelectedSong(sender);
            _player.MoveInQueue(index, index + step);
            // The refresh replaces the list and drops the selection, which stops a held Alt+Up/Down.
            if (moved != null && sender is QueueView queueView)
                queueView.SelectTrack(moved, grabFocus: true);
        }

        private Song? GetQueueToolbarSelectedSong(object? sender)
        {
            if (queuePopupView != null && ReferenceEquals(sender, queuePopupView))
                return queuePopupView.GetPrimarySelectedSong();
            if (_miniPlayerWindow != null && ReferenceEquals(sender, _miniPlayerWindow))
                return _miniPlayerWindow.GetPrimarySelectedSong();
            return queueViewControl?.SelectedTrack;
        }

        private int GetQueueToolbarSelectedViewIndex(object? sender)
        {
            if (GetQueueToolbarSelectedSong(sender) is { } selected)
            {
                int index = _player.Queue.IndexInView(selected);
                if (index >= 0)
                    return index;
            }

            if (queuePopupView != null && ReferenceEquals(sender, queuePopupView))
                return queuePopupView.GetSelectedViewIndex();
            if (_miniPlayerWindow != null && ReferenceEquals(sender, _miniPlayerWindow))
                return _miniPlayerWindow.GetSelectedViewIndex();
            return queueViewControl?.GetSelectedViewIndex() ?? -1;
        }

        /// <summary>True when the queue has a current track (Play Next inserts after it rather than appending).</summary>
        public bool HasCurrentQueueTrack => _player.Queue.Current != null;

        /// <summary>True when the track is the current song or upcoming.</summary>
        public bool IsTrackInQueue(Song? track) => _player.Queue.Contains(track);

        private bool TryRestorePreviousQueue() => _player.UndoQueue();

        private ObservableCollection<Song> BuildQueueView(int? maxItems = null) =>
            new(_player.Queue.View(maxItems));

        private void UpdateQueueView()
        {
            var queueView = BuildQueueView();
            if (queueViewControl != null)
                queueViewControl.ItemsSource = queueView;

            if (queuePopup.IsOpen && queuePopupView != null)
            {
                queuePopupView.QueueTracks = new ObservableCollection<Song>(
                    queueView.Take(UILayoutConstants.CompactQueueListMaxItems));
                queuePopupView.RefreshHeight();
                _ = Dispatcher.BeginInvoke(new Action(CenterQueuePopupUnderPlacementButton), DispatcherPriority.ContextIdle);
            }

            PushMiniPlayerQueue();
        }

        private const double ShortcutVolumeStep = 5;

        /// <summary>Runs a keyboard shortcut. Returns false when it did not apply here, so the key passes through.</summary>
        internal bool RunShortcut(Hotkey.ShortcutAction action)
        {
            switch (action)
            {
                case Hotkey.ShortcutAction.PlayPause:
                    TitleBarPlayer_PlayPauseRequested(this, EventArgs.Empty);
                    return true;
                case Hotkey.ShortcutAction.PreviousTrack:
                    TitleBarPlayer_PreviousTrackRequested(this, EventArgs.Empty);
                    SelectCurrentTrackInActiveView(true);
                    return true;
                case Hotkey.ShortcutAction.NextTrack:
                    TitleBarPlayer_NextTrackRequested(this, EventArgs.Empty);
                    SelectCurrentTrackInActiveView(true);
                    return true;
                case Hotkey.ShortcutAction.PlaySelected:
                    // Nothing to play: let Enter reach the focused control.
                    if (!TryResolveTrackToPlayFromActiveView(out var selected, out var requestSource) || selected == null)
                        return false;
                    PlayTrack(selected, requestSource);
                    return true;
                case Hotkey.ShortcutAction.PlayNext:
                case Hotkey.ShortcutAction.AddToQueue:
                    return InjectSelectedTracksIntoQueue(action == Hotkey.ShortcutAction.PlayNext);
                case Hotkey.ShortcutAction.RemoveFromQueue:
                case Hotkey.ShortcutAction.MoveQueueItemUp:
                case Hotkey.ShortcutAction.MoveQueueItemDown:
                    return RunQueueShortcut(action);
                case Hotkey.ShortcutAction.UndoQueueReplace:
                    return TryRestorePreviousQueue();
                case Hotkey.ShortcutAction.ToggleShuffle:
                    titleBarPlayer.ToggleShuffle();
                    return true;
                case Hotkey.ShortcutAction.CycleRepeat:
                    titleBarPlayer.CycleRepeat();
                    return true;
                case Hotkey.ShortcutAction.VolumeUp:
                case Hotkey.ShortcutAction.VolumeDown:
                    titleBarPlayer.StepVolume(action == Hotkey.ShortcutAction.VolumeUp ? ShortcutVolumeStep : -ShortcutVolumeStep);
                    return true;
                case Hotkey.ShortcutAction.ToggleMute:
                    titleBarPlayer.ToggleMute();
                    return true;
                default:
                    return false;
            }
        }

        internal void ApplyShortcutPreferences() => _localHotkeys.Reload();

        private bool InjectSelectedTracksIntoQueue(bool playNext)
        {
            var tracks = GetSelectedTracksForQueueShortcut();
            if (tracks.Count == 0)
                return false;
            _player.InjectTracks(tracks, playNext);
            return true;
        }

        private IReadOnlyList<Song> GetSelectedTracksForQueueShortcut()
        {
            if (QueuePopupHasFocus())
                return queuePopupView!.GetPrimarySelectedSong() is { } popupSong ? new[] { popupSong } : Array.Empty<Song>();

            return contentHost?.Content switch
            {
                TrackListHostBase host => host.GetSelectedTracks(),
                AlbumsView albums => albums.GetSelectedTracks(),
                _ => Array.Empty<Song>()
            };
        }

        private bool QueuePopupHasFocus() =>
            queuePopup.IsOpen && queuePopupView != null && queuePopupView.IsKeyboardFocusWithin;

        // Queue edits only where the queue is on screen, so Delete never touches the library
        private bool RunQueueShortcut(Hotkey.ShortcutAction action)
        {
            object? sender = QueuePopupHasFocus() ? queuePopupView
                : ReferenceEquals(contentHost?.Content, queueViewControl) ? queueViewControl
                : null;
            if (sender == null)
                return false;

            switch (action)
            {
                case Hotkey.ShortcutAction.RemoveFromQueue:
                    OnQueueToolbarRemoveRequested(sender, EventArgs.Empty);
                    break;
                case Hotkey.ShortcutAction.MoveQueueItemUp:
                    OnQueueToolbarMoveUpRequested(sender, EventArgs.Empty);
                    break;
                default:
                    OnQueueToolbarMoveDownRequested(sender, EventArgs.Empty);
                    break;
            }
            return true;
        }

        internal MetadataAudioReleaseResult ReleasePlaybackForMetadataWrite(string filePath) =>
            string.IsNullOrWhiteSpace(filePath) ? default : _player.ReleaseFile(filePath);

        internal void RestorePlaybackAfterMetadataWrite(MetadataAudioReleaseResult release) =>
            _player.RestoreFile(release);
    }
}
