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
    private readonly List<Song> contextualShuffledFuture = new();
    private readonly List<Song> contextualPlaybackHistoryMru = new();
    private List<Song>? contextualSessionOrderedFull;
    private readonly HashSet<Song> userQueuedSongs = new();
    private List<string>? contextualShuffleWrapPathOrder;

    private sealed class QueueUndoSnapshot
    {
        public List<Song> SessionOrder { get; set; } = new();
        public List<Song> ShuffledFuture { get; set; } = new();
        public List<Song> History { get; set; } = new();
        public List<Song> UserQueued { get; set; } = new();
        public List<Song> Upcoming { get; set; } = new();
        public List<string>? WrapPaths { get; set; }
        public Song? Current { get; set; }
    }

    private QueueUndoSnapshot? _queueUndoSnapshot;
    private string? _queueUndoStatusMessage;

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

    private static bool SongListsIdenticalOrderByPath(IReadOnlyList<Song> linear, IList<Song> other)
    {
        if (linear.Count != other.Count)
            return false;
        for (int i = 0; i < linear.Count; i++)
        {
            if (!SongIdentity.SamePath(linear[i], other[i]))
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

    private Dictionary<string, int> ContextualHistoryPathCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in contextualPlaybackHistoryMru)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.FilePath))
                continue;
            counts.TryGetValue(t.FilePath, out var n);
            counts[t.FilePath] = n + 1;
        }
        return counts;
    }

    private static bool TryConsumeHistoryPath(Dictionary<string, int> histCounts, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !histCounts.TryGetValue(path, out var rem) || rem <= 0)
            return false;
        histCounts[path] = rem - 1;
        return true;
    }

    /// <summary>
    /// Returns the natural-order future from <paramref name="anchor"/> onward, dropping anything
    /// already in history (unless the user explicitly queued it).
    /// </summary>
    private List<Song> DeriveNaturalFutureFromAnchor(Song? anchor)
    {
        var result = new List<Song>();
        if (contextualSessionOrderedFull == null || contextualSessionOrderedFull.Count == 0)
            return result;

        var histCounts = ContextualHistoryPathCounts();
        int idx = anchor != null
            ? SongIdentity.IndexOf(contextualSessionOrderedFull, anchor)
            : -1;
        int start = idx >= 0 ? idx : 0;

        for (int i = start; i < contextualSessionOrderedFull.Count; i++)
        {
            var t = contextualSessionOrderedFull[i];
            if (t == null || string.IsNullOrWhiteSpace(t.FilePath))
                continue;
            bool isAnchor = i == idx;
            bool isInjected = userQueuedSongs.Contains(t);
            if (!isAnchor && !isInjected && TryConsumeHistoryPath(histCounts, t.FilePath))
                continue;
            result.Add(t);
        }
        return result;
    }

    /// <summary>
    /// Builds <see cref="contextualShuffledFuture"/> with <paramref name="anchor"/> as head and a
    /// fresh Fisher-Yates of the remaining unplayed (and injected) tracks.
    /// </summary>
    private void BuildShuffledFutureForAnchor(Song? anchor)
    {
        contextualShuffledFuture.Clear();
        if (contextualSessionOrderedFull == null || contextualSessionOrderedFull.Count == 0)
            return;

        var histCounts = ContextualHistoryPathCounts();
        var pool = new List<Song>();
        int anchorIdx = anchor != null
            ? SongIdentity.IndexOf(contextualSessionOrderedFull, anchor)
            : -1;

        for (int i = 0; i < contextualSessionOrderedFull.Count; i++)
        {
            var t = contextualSessionOrderedFull[i];
            if (t == null || string.IsNullOrWhiteSpace(t.FilePath))
                continue;
            if (i == anchorIdx)
                continue;
            bool isInjected = userQueuedSongs.Contains(t);
            if (!isInjected && TryConsumeHistoryPath(histCounts, t.FilePath))
                continue;
            pool.Add(t);
        }

        if (anchor != null)
            contextualShuffledFuture.Add(anchor);

        if (pool.Count > 1)
            FisherYatesRange(pool, 0, pool.Count - 1);

        foreach (var t in pool)
            contextualShuffledFuture.Add(t);
    }

    private void CaptureContextualShuffleWrapPathOrder()
    {
        if (contextualShuffledFuture.Count == 0)
        {
            contextualShuffleWrapPathOrder = null;
            return;
        }

        var paths = new List<string>(contextualShuffledFuture.Count);
        foreach (var t in contextualShuffledFuture)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.FilePath))
                continue;
            paths.Add(t.FilePath);
        }

        contextualShuffleWrapPathOrder = paths.Count == 0 ? null : paths;
    }

    private static Dictionary<string, Queue<Song>> ContextualShufflePathConsumptionPools(IReadOnlyList<Song> session)
    {
        var d = new Dictionary<string, Queue<Song>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in session)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.FilePath))
                continue;
            if (!d.TryGetValue(t.FilePath, out var q))
            {
                q = new Queue<Song>();
                d[t.FilePath] = q;
            }
            q.Enqueue(t);
        }
        return d;
    }

    private bool ContextualShuffleWrapPathsMatchSessionMultiset(IReadOnlyList<string> wrapPaths)
    {
        if (contextualSessionOrderedFull == null)
            return false;

        var sessionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in contextualSessionOrderedFull)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.FilePath))
                continue;
            sessionCounts.TryGetValue(t.FilePath, out var c);
            sessionCounts[t.FilePath] = c + 1;
        }

        var wrapCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in wrapPaths)
        {
            if (string.IsNullOrWhiteSpace(p))
                return false;
            wrapCounts.TryGetValue(p, out var c);
            wrapCounts[p] = c + 1;
        }

        if (sessionCounts.Count != wrapCounts.Count)
            return false;
        foreach (var kv in sessionCounts)
        {
            if (!wrapCounts.TryGetValue(kv.Key, out var wc) || wc != kv.Value)
                return false;
        }
        return true;
    }

    private bool TryRebuildContextualShuffledFutureFromWrapPathOrder(Song anchor)
    {
        var order = contextualShuffleWrapPathOrder;
        if (order == null || order.Count == 0 || contextualSessionOrderedFull == null || anchor == null)
            return false;

        string anchorPath = anchor.FilePath ?? "";
        if (string.IsNullOrWhiteSpace(anchorPath))
            return false;

        if (!ContextualShuffleWrapPathsMatchSessionMultiset(order))
            return false;

        int rot = -1;
        for (int i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i], anchorPath, StringComparison.OrdinalIgnoreCase))
            {
                rot = i;
                break;
            }
        }
        if (rot < 0)
            return false;

        var rotated = new List<string>(order.Count);
        for (int i = 0; i < order.Count; i++)
            rotated.Add(order[(rot + i) % order.Count]);

        if (!string.Equals(rotated[0], anchorPath, StringComparison.OrdinalIgnoreCase))
            return false;

        var pools = ContextualShufflePathConsumptionPools(contextualSessionOrderedFull);
        var rebuilt = new List<Song>(rotated.Count);

        foreach (var p in rotated)
        {
            if (!pools.TryGetValue(p, out var deck) || deck.Count == 0)
                return false;
            rebuilt.Add(deck.Dequeue());
        }

        foreach (var q in pools.Values)
        {
            if (q.Count != 0)
                return false;
        }

        contextualShuffledFuture.Clear();
        foreach (var t in rebuilt)
            contextualShuffledFuture.Add(t);

        return true;
    }

    /// <summary>
    /// Repopulates <see cref="contextualPlaybackFuture"/> from the active source (shuffled tail or
    /// natural derivation), with <paramref name="anchor"/> as the implied head.
    /// </summary>
    private void SetActivePlaybackFuture(Song? anchor)
    {
        contextualPlaybackFuture ??= new ObservableCollection<Song>();
        contextualPlaybackFuture.Clear();

        if (titleBarPlayer.IsShuffleEnabled)
        {
            foreach (var t in contextualShuffledFuture)
                contextualPlaybackFuture.Add(t);
        }
        else
        {
            var nat = DeriveNaturalFutureFromAnchor(anchor);
            foreach (var t in nat)
                contextualPlaybackFuture.Add(t);
        }
    }

    private void InitializeContextualSession(IReadOnlyList<Song> ordered, Song selected)
    {
        if (ordered.Count == 0 || selected == null)
            return;

        var src = ordered.Where(t => t != null).ToList();
        if (src.Count == 0)
            return;

        int idx = SongIdentity.IndexOf(src, selected);
        if (idx < 0)
            return;

        ResetUserQueuedFlagsForCurrentSession();

        contextualSessionOrderedFull = new List<Song>(src);
        contextualPlaybackHistoryMru.Clear();
        contextualShuffledFuture.Clear();
        userQueuedSongs.Clear();
        contextualShuffleWrapPathOrder = null;

        for (int i = idx - 1; i >= 0; i--)
            contextualPlaybackHistoryMru.Add(src[i]);

        if (titleBarPlayer.IsShuffleEnabled)
        {
            BuildShuffledFutureForAnchor(selected);
            CaptureContextualShuffleWrapPathOrder();
        }

        SetActivePlaybackFuture(selected);
        if (contextualPlaybackFuture == null || contextualPlaybackFuture.Count == 0)
            ClearContextualPlaybackQueue(offerUndo: false);
    }

    private void TryInitializeContextFromPlayTrack(object? requestSource, Song selectedTrack)
    {
        TryInitializeContextQueue(
            requestSource,
            selectedTrack,
            () => artistsViewControl != null &&
                  ReferenceEquals(requestSource, artistsViewControl) &&
                  string.Equals(artistsViewControl.ViewName, "Artists", StringComparison.OrdinalIgnoreCase) &&
                  !string.IsNullOrWhiteSpace(selectedTrack.Artist),
            () => ArtistPlaybackOrder.BuildOrderedArtistTracks(allTracks, selectedTrack.Artist));

        TryInitializeContextQueue(
            requestSource,
            selectedTrack,
            () => genresViewControl != null &&
                  ReferenceEquals(requestSource, genresViewControl) &&
                  string.Equals(genresViewControl.ViewName, "Genres", StringComparison.OrdinalIgnoreCase) &&
                  !string.IsNullOrWhiteSpace(selectedTrack.Genre),
            () => GenrePlaybackOrder.BuildOrderedGenreTracks(allTracks, selectedTrack.Genre));

        TryInitializeContextQueue(
            requestSource,
            selectedTrack,
            () => ReferenceEquals(requestSource, songsView),
            () => allTracks.ToList());

        TryInitializeContextQueue(
            requestSource,
            selectedTrack,
            () => playlistsViewControl != null &&
                  ReferenceEquals(requestSource, playlistsViewControl) &&
                  playlistsViewControl.SelectedPlaylist != null,
            () => playlistsViewControl!.SelectedPlaylist!.Tracks.ToList());

        if (!ReferenceEquals(requestSource, albumsViewControl) || albumsViewControl == null)
            return;

        if (albumsViewControl.BrowseMode == AlbumsBrowseMode.RecentlyAdded)
        {
            TryInitializeContextQueue(
                requestSource,
                selectedTrack,
                () => true,
                () => RecentlyAddedPlaybackOrder.BuildOrderedTracks(allTracks));
            return;
        }

        string albumTitle = selectedTrack.Album ?? string.Empty;
        if (string.IsNullOrWhiteSpace(albumTitle))
            return;

        string selectedAlbumArtist = !string.IsNullOrWhiteSpace(selectedTrack.AlbumArtist)
            ? selectedTrack.AlbumArtist
            : selectedTrack.Artist ?? string.Empty;

        TryInitializeContextQueue(
            requestSource,
            selectedTrack,
            () => true,
            () => AlbumTrackOrder.SortByAlbumSequence(
                allTracks.Where(s =>
                    string.Equals(s.Album, albumTitle, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        !string.IsNullOrWhiteSpace(s.AlbumArtist) ? s.AlbumArtist : s.Artist,
                        selectedAlbumArtist,
                        StringComparison.OrdinalIgnoreCase))).ToList());
    }

    private void TryInitializeContextQueue(
        object? requestSource,
        Song selectedTrack,
        Func<bool> isMatch,
        Func<List<Song>> buildOrder)
    {
        if (!isMatch())
            return;

        ClearContextualPlaybackQueue(offerUndo: true);
        var ordered = buildOrder();
        if (ordered.Count == 0)
            return;
        InitializeContextualSession(ordered, selectedTrack);
    }

    private bool HasContextualPlaybackQueue()
    {
        return contextualPlaybackFuture != null && contextualPlaybackFuture.Count > 0;
    }

    /// <summary>
    /// Returns the mutable contextual session list for queue edits, promoting the library
    /// playhead into a session when needed (preserves shuffle order when ON).
    /// When idle, <paramref name="seedTrack"/> starts an empty user-queue session.
    /// </summary>
    private List<Song>? GetOrPromoteContextualSessionForQueueEdit(Song? seedTrack = null)
    {
        if (contextualSessionOrderedFull != null &&
            (HasContextualPlaybackQueue() || currentTrack == null))
            return contextualSessionOrderedFull;

        if (currentTrack == null)
        {
            if (seedTrack == null || string.IsNullOrWhiteSpace(seedTrack.FilePath))
                return null;

            ResetUserQueuedFlagsForCurrentSession();
            contextualSessionOrderedFull = new List<Song>();
            contextualPlaybackHistoryMru.Clear();
            contextualShuffledFuture.Clear();
            userQueuedSongs.Clear();
            contextualShuffleWrapPathOrder = null;
            contextualPlaybackFuture ??= new ObservableCollection<Song>();
            contextualPlaybackFuture.Clear();
            return contextualSessionOrderedFull;
        }

        if (filteredTracks == null || filteredTracks.Count == 0)
            return null;

        var natural = new List<Song>();
        foreach (var t in filteredTracks)
        {
            if (t != null && !string.IsNullOrWhiteSpace(t.FilePath))
                natural.Add(t);
        }
        if (natural.Count == 0)
            return null;

        int idx = SongIdentity.IndexOf(natural, currentTrack);
        if (idx < 0)
            return null;

        ResetUserQueuedFlagsForCurrentSession();
        var session = new List<Song>(natural);
        contextualSessionOrderedFull = session;
        contextualPlaybackHistoryMru.Clear();
        contextualShuffledFuture.Clear();
        userQueuedSongs.Clear();
        contextualShuffleWrapPathOrder = null;

        if (titleBarPlayer.IsShuffleEnabled && shuffledTracks.Count > 0)
        {
            int si = SongIdentity.IndexOf(shuffledTracks, currentTrack);
            if (si < 0)
                si = 0;

            for (int i = si - 1; i >= 0; i--)
            {
                var t = shuffledTracks[i];
                if (t != null)
                    contextualPlaybackHistoryMru.Add(t);
            }

            for (int i = si; i < shuffledTracks.Count; i++)
            {
                var t = shuffledTracks[i];
                if (t != null)
                    contextualShuffledFuture.Add(t);
            }

            CaptureContextualShuffleWrapPathOrder();
        }
        else
        {
            for (int i = idx - 1; i >= 0; i--)
                contextualPlaybackHistoryMru.Add(natural[i]);
        }

        SetActivePlaybackFuture(currentTrack);
        if (!HasContextualPlaybackQueue())
            return null;

        return session;
    }

    private void ResetUserQueuedFlagsForCurrentSession()
    {
        foreach (var s in userQueuedSongs)
        {
            if (s != null)
                s.IsUserQueued = false;
        }
    }

    private void ClearContextualPlaybackQueue(bool offerUndo = true)
    {
        bool hadContent =
            (contextualSessionOrderedFull != null && contextualSessionOrderedFull.Count > 0) ||
            (contextualPlaybackFuture != null && contextualPlaybackFuture.Count > 0) ||
            userQueuedSongs.Count > 0;

        if (offerUndo && hadContent)
            CaptureQueueUndoSnapshot();

        ResetUserQueuedFlagsForCurrentSession();
        userQueuedSongs.Clear();

        contextualPlaybackFuture = null;
        contextualShuffledFuture.Clear();
        contextualPlaybackHistoryMru.Clear();
        contextualSessionOrderedFull = null;
        contextualShuffleWrapPathOrder = null;

        if (offerUndo && hadContent && _queueUndoSnapshot != null)
            OfferQueueUndoPrompt();
    }

    private void CaptureQueueUndoSnapshot()
    {
        var snap = new QueueUndoSnapshot
        {
            Current = currentTrack,
            WrapPaths = contextualShuffleWrapPathOrder != null
                ? new List<string>(contextualShuffleWrapPathOrder)
                : null
        };

        if (contextualSessionOrderedFull != null)
            snap.SessionOrder.AddRange(contextualSessionOrderedFull.Where(t => t != null));
        snap.ShuffledFuture.AddRange(contextualShuffledFuture.Where(t => t != null));
        snap.History.AddRange(contextualPlaybackHistoryMru.Where(t => t != null));
        snap.UserQueued.AddRange(userQueuedSongs.Where(t => t != null));

        if (contextualPlaybackFuture != null)
        {
            for (int i = 1; i < contextualPlaybackFuture.Count; i++)
            {
                var t = contextualPlaybackFuture[i];
                if (t != null)
                    snap.Upcoming.Add(t);
            }
        }

        _queueUndoSnapshot = snap;
    }

    private void OfferQueueUndoPrompt()
    {
        _queueUndoStatusMessage = "Previous queue available - Ctrl+Z or miniplayer";
        if (statusBarText != null)
            statusBarText.Text = _queueUndoStatusMessage;
        UpdateMiniPlayerQueueUndoAvailability();
    }

    private void ClearQueueUndoOffer()
    {
        _queueUndoSnapshot = null;
        _queueUndoStatusMessage = null;
        UpdateMiniPlayerQueueUndoAvailability();
    }

    private bool CanUndoPreviousQueue() =>
        _queueUndoSnapshot != null &&
        (_queueUndoSnapshot.Upcoming.Count > 0 ||
         _queueUndoSnapshot.SessionOrder.Count > 0 ||
         _queueUndoSnapshot.ShuffledFuture.Count > 0);

    private void UpdateMiniPlayerQueueUndoAvailability() =>
        _miniPlayerWindow?.SetQueueUndoAvailable(CanUndoPreviousQueue());

    /// <summary>
    /// Restores the last replaced/cleared queue. Keeps the current track playing and only
    /// puts the previous upcoming list after it. Idle (no current) does a full session restore.
    /// </summary>
    private bool TryRestorePreviousQueue()
    {
        var snap = _queueUndoSnapshot;
        if (!CanUndoPreviousQueue() || snap == null)
            return false;

        _queueUndoSnapshot = null;
        _queueUndoStatusMessage = null;

        var upcoming = new List<Song>(snap.Upcoming);
        var anchor = currentTrack;

        if (anchor == null)
        {
            ResetUserQueuedFlagsForCurrentSession();
            userQueuedSongs.Clear();

            contextualSessionOrderedFull = snap.SessionOrder.Count > 0
                ? new List<Song>(snap.SessionOrder)
                : new List<Song>(snap.ShuffledFuture);
            contextualShuffledFuture.Clear();
            foreach (var t in snap.ShuffledFuture)
                contextualShuffledFuture.Add(t);
            contextualPlaybackHistoryMru.Clear();
            foreach (var t in snap.History)
                contextualPlaybackHistoryMru.Add(t);
            contextualShuffleWrapPathOrder = snap.WrapPaths != null
                ? new List<string>(snap.WrapPaths)
                : null;

            foreach (var t in snap.UserQueued)
                MarkUserQueued(t);

            SetActivePlaybackFuture(snap.Current ?? contextualSessionOrderedFull.FirstOrDefault());
        }
        else
        {
            for (int i = upcoming.Count - 1; i >= 0; i--)
            {
                if (SongIdentity.SamePath(upcoming[i], anchor))
                    upcoming.RemoveAt(i);
            }

            ResetUserQueuedFlagsForCurrentSession();
            userQueuedSongs.Clear();

            contextualSessionOrderedFull = new List<Song> { anchor };
            foreach (var t in upcoming)
                contextualSessionOrderedFull.Add(t);

            contextualPlaybackHistoryMru.Clear();
            contextualShuffledFuture.Clear();
            contextualShuffleWrapPathOrder = null;

            if (titleBarPlayer.IsShuffleEnabled)
            {
                contextualShuffledFuture.Add(anchor);
                foreach (var t in upcoming)
                    contextualShuffledFuture.Add(t);
                CaptureContextualShuffleWrapPathOrder();
            }

            foreach (var t in snap.UserQueued)
            {
                if (t == null || SongIdentity.SamePath(t, anchor))
                    continue;
                if (SongIdentity.IndexOf(upcoming, t) < 0)
                    continue;
                MarkUserQueued(t);
            }

            SetActivePlaybackFuture(anchor);
        }

        UpdateQueueView();
        RefreshVisibleViews();
        UpdateMiniPlayerQueueUndoAvailability();
        UpdateStatusBar();
        return true;
    }

    /// <summary>
    /// Wraps the contextual session for Repeat-All: clears history, regenerates the active future
    /// rooted at the first track of the session, and yields the new starting track.
    /// </summary>
    private bool TryWrapContextualForRepeatAll(out Song? startTrack)
    {
        startTrack = null;
        if (contextualSessionOrderedFull == null || contextualSessionOrderedFull.Count == 0)
            return false;

        var first = contextualSessionOrderedFull[0];
        if (first == null)
            return false;

        contextualPlaybackHistoryMru.Clear();

        if (titleBarPlayer.IsShuffleEnabled)
        {
            if (!TryRebuildContextualShuffledFutureFromWrapPathOrder(first))
            {
                BuildShuffledFutureForAnchor(first);
                CaptureContextualShuffleWrapPathOrder();
            }
        }
        else
        {
            contextualShuffleWrapPathOrder = null;
            contextualShuffledFuture.Clear();
        }

        SetActivePlaybackFuture(first);

        if (contextualPlaybackFuture == null || contextualPlaybackFuture.Count == 0)
            return false;

        startTrack = contextualPlaybackFuture[0];
        return startTrack != null;
    }

    private void ClearInjectedFlagFor(Song? song)
    {
        if (song == null) return;
        if (userQueuedSongs.Remove(song))
            song.IsUserQueued = false;
    }

    private Song? FindNaturalNextAfter(Song finished)
    {
        if (contextualSessionOrderedFull == null || finished == null)
            return null;

        int idx = SongIdentity.IndexOf(contextualSessionOrderedFull, finished);
        var histCounts = ContextualHistoryPathCounts();
        int start = idx >= 0 ? idx + 1 : 0;

        for (int i = start; i < contextualSessionOrderedFull.Count; i++)
        {
            var t = contextualSessionOrderedFull[i];
            if (t == null || string.IsNullOrWhiteSpace(t.FilePath))
                continue;
            bool isInjected = userQueuedSongs.Contains(t);
            if (!isInjected && TryConsumeHistoryPath(histCounts, t.FilePath))
                continue;
            return t;
        }
        return null;
    }

    private bool TryAdvanceContextualSessionMovingFinishedToHistory(out Song? nextTrack)
    {
        nextTrack = null;
        if (!HasContextualPlaybackQueue() || contextualPlaybackFuture == null)
            return false;
        if (contextualPlaybackFuture.Count < 2)
            return false;

        var finished = contextualPlaybackFuture[0];
        if (finished == null)
            return false;

        contextualPlaybackHistoryMru.Insert(0, finished);
        ClearInjectedFlagFor(finished);

        if (titleBarPlayer.IsShuffleEnabled && contextualShuffledFuture.Count > 0)
            contextualShuffledFuture.RemoveAt(0);

        Song? next = titleBarPlayer.IsShuffleEnabled
            ? (contextualShuffledFuture.Count > 0 ? contextualShuffledFuture[0] : null)
            : FindNaturalNextAfter(finished);

        SetActivePlaybackFuture(next);
        nextTrack = next;
        return nextTrack != null;
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

        if (titleBarPlayer.IsShuffleEnabled)
        {
            int existing = SongIdentity.IndexOfByPath(contextualShuffledFuture, prev);
            if (existing >= 0)
                contextualShuffledFuture.RemoveAt(existing);
            contextualShuffledFuture.Insert(0, prev);
        }

        SetActivePlaybackFuture(prev);
        trackToPlay = prev;
        return trackToPlay != null;
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

        int idx = SongIdentity.IndexOf(list, t);
        if (idx < 0)
            return false;

        if (HasContextualPlaybackQueue())
        {
            RepairContextualFutureHeadToMatchTrack(t);

            if (!HasContextualPlaybackQueue() || contextualPlaybackFuture == null || contextualPlaybackFuture.Count == 0)
                return false;

            var head = contextualPlaybackFuture[0];
            bool headMatches = head != null && SongIdentity.SamePath(head, t);

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

        int j = SongIdentity.IndexOfByPath(contextualPlaybackFuture, track);
        if (j < 0)
        {
            ClearContextualPlaybackQueue();
            return;
        }

        for (int k = 0; k < j; k++)
        {
            var head = contextualPlaybackFuture[0];
            if (head == null) break;

            contextualPlaybackHistoryMru.Insert(0, head);
            if (titleBarPlayer.IsShuffleEnabled && contextualShuffledFuture.Count > 0)
                contextualShuffledFuture.RemoveAt(0);
            contextualPlaybackFuture.RemoveAt(0);

            ClearInjectedFlagFor(head);
        }

        SetActivePlaybackFuture(track);
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
                bool headMatches = head != null && SongIdentity.SamePath(head, t);

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

    /// <summary>
    /// Removes <paramref name="song"/> from <see cref="contextualSessionOrderedFull"/> by path,
    /// skipping the current track. Used to dedupe before injecting a Play Next / Add to Queue.
    /// </summary>
    private void RemoveFromSessionOrderedFullSkippingCurrent(Song song)
    {
        if (contextualSessionOrderedFull == null || song == null) return;
        for (int i = contextualSessionOrderedFull.Count - 1; i >= 0; i--)
        {
            var t = contextualSessionOrderedFull[i];
            if (t == null) continue;
            if (currentTrack != null && SongIdentity.SamePath(t, currentTrack)) continue;
            if (SongIdentity.SamePath(t, song))
                contextualSessionOrderedFull.RemoveAt(i);
        }
    }

    private void RemoveFromShuffledFutureSkippingHead(Song song)
    {
        if (song == null) return;
        for (int i = contextualShuffledFuture.Count - 1; i >= 1; i--)
        {
            if (SongIdentity.SamePath(contextualShuffledFuture[i], song))
                contextualShuffledFuture.RemoveAt(i);
        }
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

        Song fromTrack = queue[fromQ];
        Song toTrack = queue[toQ];
        if (fromTrack == null || toTrack == null)
            return;

        if (HasContextualPlaybackQueue() && contextualSessionOrderedFull != null)
        {
            if (titleBarPlayer.IsShuffleEnabled)
            {
                if (fromQ < contextualShuffledFuture.Count && toQ < contextualShuffledFuture.Count)
                {
                    var movedShuffle = contextualShuffledFuture[fromQ];
                    contextualShuffledFuture.RemoveAt(fromQ);
                    contextualShuffledFuture.Insert(toQ, movedShuffle);
                    CaptureContextualShuffleWrapPathOrder();
                }
            }
            else
            {
                int absFrom = SongIdentity.IndexOf(contextualSessionOrderedFull, fromTrack);
                int absTo = SongIdentity.IndexOf(contextualSessionOrderedFull, toTrack);
                if (absFrom >= 0 && absTo >= 0 && absFrom != absTo)
                {
                    var movedNatural = contextualSessionOrderedFull[absFrom];
                    contextualSessionOrderedFull.RemoveAt(absFrom);
                    contextualSessionOrderedFull.Insert(absTo, movedNatural);
                }
            }

            SetActivePlaybackFuture(currentTrack);
        }
        else
        {
            try
            {
                queue.Move(fromQ, toQ);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnQueueTracksReordered Move: {ex.Message}");
                return;
            }
        }

        UpdateQueueView();
        RefreshVisibleViews();
    }
}
