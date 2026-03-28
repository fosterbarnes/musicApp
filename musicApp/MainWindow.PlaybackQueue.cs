using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using musicApp.Helpers;
using musicApp.Views;

namespace musicApp;

public partial class MainWindow
{
    private ObservableCollection<Song>? contextualPlaybackFuture;
    private readonly List<Song> contextualLinearFuture = new();
    private readonly List<Song> contextualPlaybackHistoryMru = new();
    private List<Song>? contextualSessionOrderedFull;
    private bool contextualSessionWholeSourceShuffleLinear;

    private static void FisherYatesRange(IList<Song> list, int loInclusive, int hiInclusive, Random? rnd = null)
    {
        if (list == null || loInclusive >= hiInclusive)
            return;

        rnd ??= new Random();
        for (int i = hiInclusive; i > loInclusive; i--)
        {
            int j = rnd.Next(loInclusive, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static bool SameSongPath(Song? a, Song? b)
    {
        if (a == null || b == null)
            return ReferenceEquals(a, b);
        if (!string.IsNullOrWhiteSpace(a.FilePath) && !string.IsNullOrWhiteSpace(b.FilePath))
            return string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
        return ReferenceEquals(a, b);
    }

    private static bool SongListsIdenticalOrderByPath(IReadOnlyList<Song> linear, IList<Song> other)
    {
        if (linear.Count != other.Count)
            return false;
        for (int i = 0; i < linear.Count; i++)
        {
            if (!SameSongPath(linear[i], other[i]))
                return false;
        }
        return true;
    }

    private const int ShuffleDiffersMaxAttempts = 64;

    /// <summary>
    /// Shuffles <paramref name="mutableOrder"/> in [rangeLo, rangeHi] until it differs from
    /// <paramref name="linearOrder"/> by position, when linear has more than 2 items.
    /// </summary>
    private static void ShuffleRangeUntilOrderDiffersFromLinear(
        IList<Song> mutableOrder,
        IReadOnlyList<Song> linearOrder,
        int rangeLoInclusive,
        int rangeHiInclusive)
    {
        if (mutableOrder == null || linearOrder == null || mutableOrder.Count != linearOrder.Count)
            return;

        if (rangeLoInclusive >= rangeHiInclusive)
            return;

        if (linearOrder.Count <= 2)
        {
            FisherYatesRange(mutableOrder, rangeLoInclusive, rangeHiInclusive);
            return;
        }

        var rnd = new Random();
        for (int attempt = 0; attempt < ShuffleDiffersMaxAttempts; attempt++)
        {
            FisherYatesRange(mutableOrder, rangeLoInclusive, rangeHiInclusive, rnd);
            if (!SongListsIdenticalOrderByPath(linearOrder, mutableOrder))
                return;
        }
    }

    private void RebuildContextualDisplayFromLinear()
    {
        if (contextualLinearFuture.Count == 0)
            return;

        if (!titleBarPlayer.IsShuffleEnabled &&
            contextualSessionWholeSourceShuffleLinear &&
            contextualSessionOrderedFull != null &&
            contextualSessionOrderedFull.Count > 0)
        {
            var head = contextualLinearFuture[0];
            int curIdx = ArtistPlaybackOrder.IndexOfTrackInOrderedList(contextualSessionOrderedFull, head);
            if (curIdx >= 0)
            {
                contextualLinearFuture.Clear();
                for (int i = curIdx; i < contextualSessionOrderedFull.Count; i++)
                {
                    var t = contextualSessionOrderedFull[i];
                    if (t != null)
                        contextualLinearFuture.Add(t);
                }
            }

            contextualSessionWholeSourceShuffleLinear = false;
        }

        contextualPlaybackFuture ??= new ObservableCollection<Song>();
        contextualPlaybackFuture.Clear();
        foreach (var t in contextualLinearFuture)
            contextualPlaybackFuture.Add(t);

        if (titleBarPlayer.IsShuffleEnabled && contextualPlaybackFuture.Count > 1)
            ShuffleRangeUntilOrderDiffersFromLinear(
                contextualPlaybackFuture,
                contextualLinearFuture,
                1,
                contextualPlaybackFuture.Count - 1);
    }

    private void InitializeContextualSession(IReadOnlyList<Song> ordered, Song selected)
    {
        if (ordered.Count == 0 || selected == null)
            return;

        var src = ordered.Where(t => t != null).ToList();
        if (src.Count == 0)
            return;

        int idx = ArtistPlaybackOrder.IndexOfTrackInOrderedList(src, selected);
        if (idx < 0)
            return;

        contextualSessionOrderedFull = new List<Song>(src);

        contextualPlaybackHistoryMru.Clear();
        contextualLinearFuture.Clear();

        bool shuffleOnAtStart = titleBarPlayer.IsShuffleEnabled;

        if (shuffleOnAtStart)
        {
            contextualSessionWholeSourceShuffleLinear = true;
            for (int i = idx; i < src.Count; i++)
                contextualLinearFuture.Add(src[i]);

            for (int i = 0; i < idx; i++)
                contextualLinearFuture.Add(src[i]);
        }
        else
        {
            contextualSessionWholeSourceShuffleLinear = false;
            contextualPlaybackHistoryMru.AddRange(src.Take(idx).Reverse());
            for (int i = idx; i < src.Count; i++)
                contextualLinearFuture.Add(src[i]);
        }

        if (contextualLinearFuture.Count == 0)
        {
            ClearContextualPlaybackQueue();
            return;
        }

        RebuildContextualDisplayFromLinear();
        if (contextualPlaybackFuture == null || contextualPlaybackFuture.Count == 0)
            ClearContextualPlaybackQueue();
    }

    private void TryInitializeContextFromPlayTrack(object? requestSource, Song selectedTrack)
    {
        TryInitializeArtistContextQueue(requestSource, selectedTrack);
        TryInitializeGenreContextQueue(requestSource, selectedTrack);
        TryInitializeSongsContextQueue(requestSource, selectedTrack);
        TryInitializePlaylistContextQueue(requestSource, selectedTrack);
        TryInitializeAlbumContextQueue(requestSource, selectedTrack);
    }

    private void TryInitializeArtistContextQueue(object? requestSource, Song selectedTrack)
    {
        if (artistsViewControl == null ||
            !ReferenceEquals(requestSource, artistsViewControl) ||
            !string.Equals(artistsViewControl.ViewName, "Artists", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(selectedTrack.Artist))
            return;

        ClearContextualPlaybackQueue();

        var ordered = ArtistPlaybackOrder.BuildOrderedArtistTracks(allTracks, selectedTrack.Artist);
        InitializeContextualSession(ordered, selectedTrack);
    }

    private void TryInitializeGenreContextQueue(object? requestSource, Song selectedTrack)
    {
        if (genresViewControl == null ||
            !ReferenceEquals(requestSource, genresViewControl) ||
            !string.Equals(genresViewControl.ViewName, "Genres", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(selectedTrack.Genre))
            return;

        ClearContextualPlaybackQueue();

        var ordered = GenrePlaybackOrder.BuildOrderedGenreTracks(allTracks, selectedTrack.Genre);
        InitializeContextualSession(ordered, selectedTrack);
    }

    private void TryInitializeSongsContextQueue(object? requestSource, Song selectedTrack)
    {
        if (!ReferenceEquals(requestSource, songsView))
            return;

        ClearContextualPlaybackQueue();

        var ordered = allTracks.ToList();
        InitializeContextualSession(ordered, selectedTrack);
    }

    private void TryInitializePlaylistContextQueue(object? requestSource, Song selectedTrack)
    {
        if (playlistsViewControl == null || !ReferenceEquals(requestSource, playlistsViewControl))
            return;

        var pl = playlistsViewControl.SelectedPlaylist;
        if (pl == null)
            return;

        ClearContextualPlaybackQueue();

        var ordered = pl.Tracks.ToList();
        InitializeContextualSession(ordered, selectedTrack);
    }

    private void TryInitializeAlbumContextQueue(object? requestSource, Song selectedTrack)
    {
        if (!ReferenceEquals(requestSource, albumsViewControl) || albumsViewControl == null)
            return;

        if (albumsViewControl.BrowseMode == AlbumsBrowseMode.RecentlyAdded)
        {
            ClearContextualPlaybackQueue();

            var ordered = RecentlyAddedPlaybackOrder.BuildOrderedTracks(allTracks);
            if (ordered.Count == 0)
                return;

            InitializeContextualSession(ordered, selectedTrack);
            return;
        }

        string albumTitle = selectedTrack.Album ?? string.Empty;
        if (string.IsNullOrWhiteSpace(albumTitle))
            return;

        string selectedAlbumArtist = !string.IsNullOrWhiteSpace(selectedTrack.AlbumArtist)
            ? selectedTrack.AlbumArtist
            : selectedTrack.Artist ?? string.Empty;

        ClearContextualPlaybackQueue();

        var albumTracks = AlbumTrackOrder.SortByAlbumSequence(
            allTracks.Where(s =>
                string.Equals(s.Album, albumTitle, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    !string.IsNullOrWhiteSpace(s.AlbumArtist) ? s.AlbumArtist : s.Artist,
                    selectedAlbumArtist,
                    StringComparison.OrdinalIgnoreCase)));

        if (albumTracks.Count == 0)
            return;

        InitializeContextualSession(albumTracks, selectedTrack);
    }

    private bool HasContextualPlaybackQueue()
    {
        return contextualPlaybackFuture != null && contextualPlaybackFuture.Count > 0;
    }

    private void ClearContextualPlaybackQueue()
    {
        contextualPlaybackFuture = null;
        contextualLinearFuture.Clear();
        contextualPlaybackHistoryMru.Clear();
        contextualSessionOrderedFull = null;
        contextualSessionWholeSourceShuffleLinear = false;
    }

    private static void RemoveFirstMatchingSongFromList(IList<Song> list, Song song)
    {
        if (list == null || song == null)
            return;

        if (!string.IsNullOrWhiteSpace(song.FilePath))
        {
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t != null && string.Equals(t.FilePath, song.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                    return;
                }
            }
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], song))
            {
                list.RemoveAt(i);
                return;
            }
        }
    }

    private bool TryAdvanceContextualSessionMovingFinishedToHistory(out Song? nextTrack)
    {
        nextTrack = null;
        if (!HasContextualPlaybackQueue() || contextualPlaybackFuture == null)
            return false;
        if (contextualPlaybackFuture.Count < 2)
            return false;

        var finished = contextualPlaybackFuture[0];
        contextualPlaybackHistoryMru.Insert(0, finished);
        contextualPlaybackFuture.RemoveAt(0);
        RemoveFirstMatchingSongFromList(contextualLinearFuture, finished);
        nextTrack = contextualPlaybackFuture[0];
        return nextTrack != null;
    }

    private bool TryManualAdvanceContextualSession()
    {
        if (!HasContextualPlaybackQueue() || contextualPlaybackFuture == null)
            return false;
        if (contextualPlaybackFuture.Count < 2)
            return false;

        var skipped = contextualPlaybackFuture[0];
        contextualPlaybackHistoryMru.Insert(0, skipped);
        contextualPlaybackFuture.RemoveAt(0);
        RemoveFirstMatchingSongFromList(contextualLinearFuture, skipped);
        return true;
    }

    private bool TryRewindContextualSessionOne(out Song? trackToPlay)
    {
        trackToPlay = null;
        if (!HasContextualPlaybackQueue() || contextualPlaybackFuture == null)
            return false;
        if (contextualPlaybackHistoryMru.Count == 0)
            return false;

        var prev = contextualPlaybackHistoryMru[0];
        contextualPlaybackHistoryMru.RemoveAt(0);
        contextualPlaybackFuture.Insert(0, prev);
        contextualLinearFuture.Insert(0, prev);
        trackToPlay = prev;
        return trackToPlay != null;
    }

    private static int FindTrackIndexInPlayQueue(IList<Song> queue, Song track)
    {
        if (queue == null || track == null)
            return -1;

        if (!string.IsNullOrWhiteSpace(track.FilePath))
        {
            for (int i = 0; i < queue.Count; i++)
            {
                var t = queue[i];
                if (t != null && !string.IsNullOrWhiteSpace(t.FilePath) &&
                    string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        for (int i = 0; i < queue.Count; i++)
        {
            if (ReferenceEquals(queue[i], track))
                return i;
        }

        return -1;
    }

    private bool TrySyncPlaybackIndicesFromQueueView(Song track)
    {
        if (track == null)
            return false;

        Song t = track;

        if (queueViewControl == null)
            return false;

        var queue = GetCurrentPlayQueue();
        if (queue == null || queue.Count == 0)
            return false;

        if (queue is not IList<Song> list)
            return false;

        int idx = FindTrackIndexInPlayQueue(list, t);
        if (idx < 0)
            return false;

        if (HasContextualPlaybackQueue())
        {
            RepairContextualFutureHeadToMatchTrack(t);

            if (!HasContextualPlaybackQueue() || contextualPlaybackFuture == null || contextualPlaybackFuture.Count == 0)
                return false;

            var head = contextualPlaybackFuture[0];
            bool headMatches = head != null &&
                ((!string.IsNullOrWhiteSpace(t.FilePath) &&
                  string.Equals(head.FilePath, t.FilePath, StringComparison.OrdinalIgnoreCase)) ||
                 ReferenceEquals(head, t));

            if (!headMatches)
                return false;

            currentTrackIndex = filteredTracks.IndexOf(t);
            currentShuffledIndex = shuffledTracks.IndexOf(t);
            return true;
        }

        if (titleBarPlayer.IsShuffleEnabled)
        {
            currentShuffledIndex = idx;
            currentTrackIndex = filteredTracks.IndexOf(t);
        }
        else
        {
            currentTrackIndex = idx;
            currentShuffledIndex = shuffledTracks.IndexOf(t);
        }

        return true;
    }

    private void RepairContextualFutureHeadToMatchTrack(Song track)
    {
        if (track == null)
            return;

        if (!HasContextualPlaybackQueue() || contextualPlaybackFuture == null)
            return;

        int j = -1;
        for (int i = 0; i < contextualPlaybackFuture.Count; i++)
        {
            var t = contextualPlaybackFuture[i];
            if (t != null && string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                j = i;
                break;
            }
        }

        if (j < 0)
        {
            for (int i = 0; i < contextualPlaybackFuture.Count; i++)
            {
                if (ReferenceEquals(contextualPlaybackFuture[i], track))
                {
                    j = i;
                    break;
                }
            }
        }

        if (j < 0)
        {
            ClearContextualPlaybackQueue();
            return;
        }

        for (int k = 0; k < j; k++)
        {
            var head = contextualPlaybackFuture[0];
            contextualPlaybackHistoryMru.Insert(0, head);
            contextualPlaybackFuture.RemoveAt(0);
            RemoveFirstMatchingSongFromList(contextualLinearFuture, head);
        }
    }

    private void SyncCurrentTrackIndices(Song track, object? requestSource = null)
    {
        if (track == null)
            return;

        Song t = track;

        if (queueViewControl != null &&
            (ReferenceEquals(requestSource, queueViewControl) || ReferenceEquals(requestSource, queuePopupView)) &&
            TrySyncPlaybackIndicesFromQueueView(t))
            return;

        if (HasContextualPlaybackQueue())
        {
            RepairContextualFutureHeadToMatchTrack(t);

            if (HasContextualPlaybackQueue() &&
                contextualPlaybackFuture != null &&
                contextualPlaybackFuture.Count > 0)
            {
                var head = contextualPlaybackFuture[0];
                bool headMatches = head != null &&
                    ((!string.IsNullOrWhiteSpace(t.FilePath) &&
                      string.Equals(head.FilePath, t.FilePath, StringComparison.OrdinalIgnoreCase)) ||
                     ReferenceEquals(head, t));

                if (headMatches)
                {
                    currentTrackIndex = filteredTracks.IndexOf(t);
                    currentShuffledIndex = shuffledTracks.IndexOf(t);
                    return;
                }
            }

            ClearContextualPlaybackQueue();
        }

        currentTrackIndex = filteredTracks.IndexOf(t);
        currentShuffledIndex = shuffledTracks.IndexOf(t);
    }

    private void OnQueueTracksReordered(object? sender, (int fromViewIndex, int toViewIndex) e)
    {
        if (e.fromViewIndex < 1)
            return;

        var queue = GetCurrentPlayQueue();
        int baseIdx = GetCurrentTrackIndex();
        if (queue == null || baseIdx < 0 || queue.Count == 0)
            return;

        int fromQ = baseIdx + e.fromViewIndex;
        int toQ = baseIdx + e.toViewIndex;

        if (fromQ < 0 || fromQ >= queue.Count || toQ < 0 || toQ >= queue.Count)
            return;

        if (fromQ == toQ)
            return;

        try
        {
            queue.Move(fromQ, toQ);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OnQueueTracksReordered Move: {ex.Message}");
            return;
        }

        if (HasContextualPlaybackQueue() &&
            contextualLinearFuture.Count == queue.Count &&
            fromQ >= 0 &&
            fromQ < contextualLinearFuture.Count)
        {
            var moved = contextualLinearFuture[fromQ];
            contextualLinearFuture.RemoveAt(fromQ);
            contextualLinearFuture.Insert(toQ, moved);
        }

        UpdateQueueView();
        RefreshVisibleViews();
    }
}
