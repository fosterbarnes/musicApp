using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using NAudio.Wave;
using musicApp;
using musicApp.Helpers;

namespace musicApp.Views;

public partial class MiniPlayerWindow : Window
{
    private IWavePlayer? _waveOut;
    private AudioFileReader? _audioFileReader;
    private DispatcherTimer? _seekBarTimer;
    private TimeSpan _totalDuration;
    private bool _isUpdatingAudioObjects;
    private bool _isDragging;
    private double _currentSeekBarWidth;
    private TimeSpan _dragTargetPosition;
    private DateTime _lastMouseDownTime;
    private const int MouseMoveDelayMs = 50;
    private const double MousePositionTolerance = 100;

    public MiniPlayerWindow()
    {
        InitializeComponent();
        WireQueue(UpcomingQueue);
        _seekBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _seekBarTimer.Tick += SeekBarTimer_Tick;
        Closed += (_, _) =>
        {
            _seekBarTimer?.Stop();
            _seekBarTimer = null;
        };
    }

    private void WireQueue(CompactUpcomingQueueList q)
    {
        q.SongPlayRequested += (s, t) => SongPlayRequested?.Invoke(this, t);
        q.QueueRemoveRequested += (s, e) => QueueRemoveRequested?.Invoke(this, e);
        q.TracksReordered += (s, e) => TracksReordered?.Invoke(this, e);
        q.PlayNextRequested += (s, t) => PlayNextRequested?.Invoke(this, t);
        q.AddToQueueRequested += (s, t) => AddToQueueRequested?.Invoke(this, t);
        q.AddTrackToPlaylistRequested += (s, a) => AddTrackToPlaylistRequested?.Invoke(this, a);
        q.CreateNewPlaylistWithTrackRequested += (s, t) => CreateNewPlaylistWithTrackRequested?.Invoke(this, t);
        q.InfoRequested += (s, t) => InfoRequested?.Invoke(this, t);
        q.ShowInArtistsRequested += (s, t) => ShowInArtistsRequested?.Invoke(this, t);
        q.ShowInSongsRequested += (s, t) => ShowInSongsRequested?.Invoke(this, t);
        q.ShowInAlbumsRequested += (s, t) => ShowInAlbumsRequested?.Invoke(this, t);
        q.ShowInQueueRequested += (s, t) => ShowInQueueRequested?.Invoke(this, t);
        q.ShowInExplorerRequested += (s, t) => ShowInExplorerRequested?.Invoke(this, t);
        q.RemoveFromLibraryRequested += (s, t) => RemoveFromLibraryRequested?.Invoke(this, t);
        q.DeleteRequested += (s, t) => DeleteRequested?.Invoke(this, t);
    }

    public event EventHandler? PlayPauseRequested;
    public event EventHandler? PreviousTrackRequested;
    public event EventHandler? NextTrackRequested;
    public event EventHandler? PlaybackPositionCommitted;
    public event EventHandler<Song>? SongPlayRequested;
    public event EventHandler? QueueRemoveRequested;
    public event EventHandler<(int fromViewIndex, int toViewIndex)>? TracksReordered;
    public event EventHandler<Song>? PlayNextRequested;
    public event EventHandler<Song>? AddToQueueRequested;
    public event EventHandler<(Song track, Playlist playlist)>? AddTrackToPlaylistRequested;
    public event EventHandler<Song>? CreateNewPlaylistWithTrackRequested;
    public event EventHandler<Song>? InfoRequested;
    public event EventHandler<Song>? ShowInArtistsRequested;
    public event EventHandler<Song>? ShowInSongsRequested;
    public event EventHandler<Song>? ShowInAlbumsRequested;
    public event EventHandler<Song>? ShowInQueueRequested;
    public event EventHandler<Song>? ShowInExplorerRequested;
    public event EventHandler<IReadOnlyList<Song>>? RemoveFromLibraryRequested;
    public event EventHandler<Song>? DeleteRequested;

    public int GetAlbumArtTargetPixelSize()
    {
        double dip = ArtHost.ActualWidth > 0 ? ArtHost.ActualWidth : Width;
        double scale = 1.0;
        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
                scale = source.CompositionTarget.TransformToDevice.M11;
        }
        catch
        {
        }

        return Math.Max(64, (int)Math.Ceiling(dip * scale));
    }

    public void SetTrackInfo(string title, string artist, string? album = null, ImageSource? albumArt = null)
    {
        txtCurrentTrack.Text = string.IsNullOrWhiteSpace(title) ? "No track selected" : title;
        txtArtistAlbum.Text = PlaybackDisplayText.FormatArtistAlbum(artist, album);
        imgAlbumArt.Source = albumArt;
    }

    public void SetAudioObjects(IWavePlayer? waveOut, AudioFileReader? audioFileReader)
    {
        _isUpdatingAudioObjects = true;
        try
        {
            _seekBarTimer?.Stop();
            _waveOut = waveOut;
            _audioFileReader = audioFileReader;

            if (waveOut == null || audioFileReader == null)
            {
                _totalDuration = TimeSpan.Zero;
                UpdateSeekBar(TimeSpan.Zero, TimeSpan.Zero);
                UpdatePlayPauseIcon(false);
            }
            else
            {
                try
                {
                    _totalDuration = audioFileReader.TotalTime;
                    var pos = audioFileReader.CurrentTime;
                    if (pos < TimeSpan.Zero)
                        pos = TimeSpan.Zero;
                    if (_totalDuration > TimeSpan.Zero && pos > _totalDuration)
                        pos = _totalDuration;
                    UpdateSeekBar(pos, _totalDuration);
                    UpdatePlayPauseIcon(waveOut.PlaybackState == PlaybackState.Playing);
                }
                catch (ObjectDisposedException)
                {
                    _totalDuration = TimeSpan.Zero;
                    UpdateSeekBar(TimeSpan.Zero, TimeSpan.Zero);
                    UpdatePlayPauseIcon(false);
                }
            }
        }
        finally
        {
            _isUpdatingAudioObjects = false;
            UpdateSeekBarTimer();
        }
    }

    public int GetSelectedViewIndex() => UpcomingQueue.GetSelectedViewIndex();
    public Song? GetPrimarySelectedSong() => UpcomingQueue.GetPrimarySelectedSong();
    public void SetQueue(IEnumerable? tracks) => UpcomingQueue.SetQueue(tracks);

    private void ArtHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ArtHost.ActualWidth > 0)
            ArtHost.Height = ArtHost.ActualWidth;
    }

    private void ArtHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (e.OriginalSource is DependencyObject src && IsDescendantOf(src, btnClose))
            return;

        if (e.ClickCount == 1)
        {
            try { DragMove(); }
            catch { }
        }
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor))
                return true;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    private void BtnPlayPause_Click(object sender, RoutedEventArgs e) =>
        PlayPauseRequested?.Invoke(this, EventArgs.Empty);
    private void BtnPrevious_Click(object sender, RoutedEventArgs e) =>
        PreviousTrackRequested?.Invoke(this, EventArgs.Empty);
    private void BtnNext_Click(object sender, RoutedEventArgs e) =>
        NextTrackRequested?.Invoke(this, EventArgs.Empty);

    private void SeekBarTrack_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _currentSeekBarWidth = seekBarTrack.ActualWidth;
        if (_audioFileReader != null && _totalDuration > TimeSpan.Zero && !_isDragging)
        {
            try { UpdateSeekBar(_audioFileReader.CurrentTime, _totalDuration); }
            catch { }
        }
    }

    private void UpdateSeekBar(TimeSpan currentTime, TimeSpan totalTime)
    {
        txtCurrentTime.Text = PlaybackDisplayText.FormatTimeSpan(currentTime);
        _totalDuration = totalTime;

        if (totalTime > TimeSpan.Zero)
        {
            var remaining = totalTime - currentTime;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
            txtRemainingTime.Text = "-" + PlaybackDisplayText.FormatTimeSpan(remaining);

            if (_currentSeekBarWidth <= 0)
                _currentSeekBarWidth = seekBarTrack.ActualWidth;

            double progress = currentTime.TotalSeconds / totalTime.TotalSeconds;
            progressFill.Width = SeekBarInteractionHelper.ProgressFillWidth(currentTime, totalTime, _currentSeekBarWidth);
        }
        else
        {
            txtRemainingTime.Text = "-0:00";
            progressFill.Width = 0;
        }
    }

    private void UpdatePlayPauseIcon(bool isPlaying) =>
        iconPlayPause.Kind = isPlaying ? PackIconKind.Pause : PackIconKind.Play;

    private bool AreAudioObjectsValid()
    {
        try
        {
            if (_audioFileReader == null)
                return false;
            _ = _audioFileReader.TotalTime;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateSeekBarTimer()
    {
        if (_isUpdatingAudioObjects || _seekBarTimer == null)
            return;
        _seekBarTimer.Stop();
        if (AreAudioObjectsValid())
            _seekBarTimer.Start();
    }

    private void SeekBarTimer_Tick(object? sender, EventArgs e)
    {
        if (_isUpdatingAudioObjects || _isDragging)
            return;

        if (!AreAudioObjectsValid())
        {
            _seekBarTimer?.Stop();
            UpdatePlayPauseIcon(false);
            return;
        }

        try
        {
            bool playing = _waveOut?.PlaybackState == PlaybackState.Playing;
            UpdatePlayPauseIcon(playing);
            if (_audioFileReader != null)
                UpdateSeekBar(_audioFileReader.CurrentTime, _totalDuration);
        }
        catch (ObjectDisposedException)
        {
            _seekBarTimer?.Stop();
        }
        catch
        {
            _seekBarTimer?.Stop();
        }
    }

    private void SeekBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_audioFileReader == null || _totalDuration.TotalSeconds <= 0)
            return;

        if (_currentSeekBarWidth <= 0)
            _currentSeekBarWidth = seekBarTrack.ActualWidth;

        var clickPoint = e.GetPosition(seekBarTrack);
        double clickPosition = SeekBarInteractionHelper.Clamp(clickPoint.X, 0, _currentSeekBarWidth);
        _lastMouseDownTime = DateTime.Now;

        _dragTargetPosition = SeekBarInteractionHelper.TimeFromSeekX(clickPosition, _currentSeekBarWidth, _totalDuration);
        UpdateSeekBar(_dragTargetPosition, _totalDuration);

        _isDragging = true;
        seekBarTrack.CaptureMouse();
        e.Handled = true;
    }

    private void SeekBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _audioFileReader == null || _totalDuration.TotalSeconds <= 0)
            return;

        if (!SeekBarInteractionHelper.HasPassedMoveDelay(_lastMouseDownTime, MouseMoveDelayMs))
            return;

        double currentPosition = e.GetPosition(seekBarTrack).X;
        if (!SeekBarInteractionHelper.IsWithinDragTolerance(currentPosition, _currentSeekBarWidth, MousePositionTolerance))
            return;

        currentPosition = SeekBarInteractionHelper.Clamp(currentPosition, 0, _currentSeekBarWidth);
        _dragTargetPosition = SeekBarInteractionHelper.TimeFromSeekX(currentPosition, _currentSeekBarWidth, _totalDuration);
        UpdateSeekBar(_dragTargetPosition, _totalDuration);
    }

    private void SeekBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;
        EndDragOperation();
        e.Handled = true;
    }

    private void SeekBar_MouseLeave(object sender, MouseEventArgs e) { }

    private void EndDragOperation()
    {
        if (_audioFileReader != null && _totalDuration.TotalSeconds > 0)
        {
            try
            {
                _audioFileReader.CurrentTime = _dragTargetPosition;
                UpdateSeekBar(_dragTargetPosition, _totalDuration);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _isDragging = false;
        seekBarTrack.ReleaseMouseCapture();
        PlaybackPositionCommitted?.Invoke(this, EventArgs.Empty);
    }
}
