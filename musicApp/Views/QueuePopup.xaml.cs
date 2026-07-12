using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using musicApp;
using musicApp.Constants;
using musicApp.Helpers;

namespace musicApp.Views;

public partial class QueuePopupView : UserControl
{
    private int _heightAdjustGeneration;

    public QueuePopupView()
    {
        InitializeComponent();
        PopupBorder.MinHeight = UILayoutConstants.CompactPopupMinHeight;
        PopupBorder.Height = UILayoutConstants.CompactPopupDefaultHeight;
        PopupBorder.MaxHeight = UILayoutConstants.CompactPopupMaxHeight;
        WireQueue(UpcomingQueue);
    }

    private void WireQueue(CompactUpcomingQueueList q)
    {
        q.QueueChanged += (_, _) => ScheduleAdjustHeight();
        q.SongPlayRequested += (s, t) => SongPlayRequested?.Invoke(this, t);
        q.QueueRemoveRequested += (s, e) => QueueToolbarRemoveRequested?.Invoke(this, e);
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

    public IEnumerable? QueueTracks
    {
        get => (IEnumerable?)GetValue(QueueTracksProperty);
        set => SetValue(QueueTracksProperty, value);
    }

    public static readonly DependencyProperty QueueTracksProperty =
        DependencyProperty.Register(nameof(QueueTracks), typeof(IEnumerable), typeof(QueuePopupView),
            new PropertyMetadata(null, OnQueueTracksChanged));

    private static void OnQueueTracksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not QueuePopupView view)
            return;
        view.UpcomingQueue.SetQueue(e.NewValue as IEnumerable);
    }

    public void RefreshHeight() => ScheduleAdjustHeight();

    private void ScheduleAdjustHeight()
    {
        int gen = ++_heightAdjustGeneration;
        CompactPopupHeightHelper.ScheduleAdjust(Dispatcher, gen, () => _heightAdjustGeneration, AdjustPopupHeight);
    }

    private void AdjustPopupHeight()
    {
        CompactPopupHeightHelper.AdjustBorderHeightToContent(
            PopupBorder, UpcomingQueue.ScrollViewer, UpcomingQueue.ContentPanel, this);
    }

    public event EventHandler<Song>? SongPlayRequested;
    public event EventHandler? QueueToolbarRemoveRequested;
    public event EventHandler? QueueToolbarMoveUpRequested;
    public event EventHandler? QueueToolbarMoveDownRequested;
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

    public int GetSelectedViewIndex() => UpcomingQueue.GetSelectedViewIndex();
    public Song? GetPrimarySelectedSong() => UpcomingQueue.GetPrimarySelectedSong();

    private void QueueToolbarRemove_Click(object sender, RoutedEventArgs e) =>
        QueueToolbarRemoveRequested?.Invoke(this, EventArgs.Empty);

    private void QueueToolbarMoveUp_Click(object sender, RoutedEventArgs e) =>
        QueueToolbarMoveUpRequested?.Invoke(this, EventArgs.Empty);

    private void QueueToolbarMoveDown_Click(object sender, RoutedEventArgs e) =>
        QueueToolbarMoveDownRequested?.Invoke(this, EventArgs.Empty);

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var currentHeight = !double.IsNaN(PopupBorder.Height) ? PopupBorder.Height : PopupBorder.ActualHeight;
        if (currentHeight <= 0)
            currentHeight = UILayoutConstants.CompactPopupMinHeight;

        var maxHeight = CompactPopupHeightHelper.GetAvailableMaxHeight(this);
        var newHeight = Math.Clamp(currentHeight + e.VerticalChange, UILayoutConstants.CompactPopupMinHeight, maxHeight);
        PopupBorder.Height = newHeight;
    }
}
