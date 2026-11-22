using System;
using System.Collections.Generic;
using System.Linq;
using musicApp.Helpers;

namespace musicApp.Playback;

/// <summary>
/// One play order and one position: entries before the index are played, the entry at it is
/// current, and entries after it are upcoming. Entries (not songs) carry identity so a playlist
/// can hold the same song twice.
/// </summary>
public sealed class PlaybackQueue
{
    private const int ShuffleAttempts = 16;

    private sealed class Entry(Song song)
    {
        public Song Song { get; } = song;
        public bool UserQueued { get; set; }
    }

    private sealed record Snapshot(List<Entry> Order, int Index);

    private readonly Random _random;
    private List<Entry> _natural = new();
    private List<Entry> _order = new();
    private int _index = -1;
    private Snapshot? _undo;

    public PlaybackQueue(Random? random = null) => _random = random ?? new Random();

    public bool IsShuffled { get; private set; }
    public bool CanUndo => _undo is { Order.Count: > 0 };
    public bool HasItems => _order.Count > 0;
    public bool CanRewind => _index > 0 && _index < _order.Count;
    public Song? Current => _index >= 0 && _index < _order.Count ? _order[_index].Song : null;

    private int ViewStart => Math.Max(_index, 0);

    /// <summary>Current track followed by upcoming tracks. When idle, just the upcoming tracks.</summary>
    public List<Song> View(int? maxItems = null)
    {
        var count = Math.Max(0, _order.Count - ViewStart);
        if (maxItems is int max)
            count = Math.Min(count, max);
        return _order.GetRange(Math.Min(ViewStart, _order.Count), count).ConvertAll(e => e.Song);
    }

    public bool Contains(Song? song) => song != null && IndexInView(song) >= 0;

    /// <summary>Position in <see cref="View"/>, matching by file. Tags are not identity.</summary>
    public int IndexInView(Song song)
    {
        int index = Find(_order, ViewStart, song);
        return index < 0 ? -1 : index - ViewStart;
    }

    public bool Start(IReadOnlyList<Song> ordered, Song selected)
    {
        var entries = ordered.Where(s => s != null && !string.IsNullOrWhiteSpace(s.FilePath))
            .Select(s => new Entry(s)).ToList();
        int selectedIndex = Find(entries, 0, selected);
        if (selectedIndex < 0)
            return false;

        Clear(offerUndo: true);
        _natural = entries;
        if (IsShuffled)
        {
            var chosen = entries[selectedIndex];
            _order = new List<Entry>(entries.Count) { chosen };
            _order.AddRange(entries.Where(e => !ReferenceEquals(e, chosen)));
            ShuffleTail(1);
            _index = 0;
        }
        else
        {
            _order = new List<Entry>(entries);
            _index = selectedIndex;
        }
        return true;
    }

    public void Clear(bool offerUndo)
    {
        if (offerUndo && _index < _order.Count)
            _undo = new Snapshot(new List<Entry>(_order), _index);

        ClearSongFlags();
        _natural = new List<Entry>();
        _order = new List<Entry>();
        _index = -1;
    }

    public void ClearUndo() => _undo = null;

    /// <summary>Next track without moving. Repeat One counts as off here: Next always moves on.</summary>
    public Song? PeekNext(RepeatMode repeat)
    {
        if (_index + 1 < _order.Count)
            return _order[_index + 1].Song;
        return repeat == RepeatMode.All && HasItems ? _order[0].Song : null;
    }

    public Song? Advance(RepeatMode repeat)
    {
        if (_index >= 0 && _index < _order.Count)
            Consume(_order[_index]);

        if (_index + 1 < _order.Count)
            _index++;
        else if (repeat == RepeatMode.All && HasItems)
            _index = 0;
        else
            return null;
        Consume(_order[_index]);
        return Current;
    }

    public Song? Rewind()
    {
        if (!CanRewind)
            return null;
        _index--;
        return Current;
    }

    /// <summary>Moves forward to the song, marking anything skipped as played.</summary>
    public bool JumpTo(Song song)
    {
        int target = Find(_order, ViewStart, song);
        if (target < 0)
            return false;
        for (int i = ViewStart; i <= target; i++)
            Consume(_order[i]);
        _index = target;
        return true;
    }

    public void SetShuffle(bool enabled)
    {
        if (IsShuffled == enabled)
            return;
        IsShuffled = enabled;

        int tailStart = _index + 1;
        if (tailStart >= _order.Count)
            return;

        if (enabled)
        {
            ShuffleTail(tailStart);
            return;
        }

        var tail = _order.Skip(tailStart).ToHashSet();
        _order.RemoveRange(tailStart, _order.Count - tailStart);
        _order.AddRange(_natural.Where(tail.Contains));
    }

    public void PlayNext(Song song) => Inject(song, playNext: true);

    public void Add(Song song) => Inject(song, playNext: false);

    /// <summary>Moves an upcoming track using queue view indexes.</summary>
    public bool Move(int fromView, int toView)
    {
        int first = Current != null ? 1 : 0;
        int count = _order.Count - ViewStart;
        if (fromView < first || fromView >= count || toView < first || toView >= count || fromView == toView)
            return false;

        int from = ViewStart + fromView;
        var entry = _order[from];
        _order.RemoveAt(from);
        _order.Insert(ViewStart + toView, entry);
        SyncNaturalIfUnshuffled();
        return true;
    }

    /// <summary>Removes by queue view index. Returns true if the current track was removed.</summary>
    public bool RemoveAt(int viewIndex)
    {
        int i = ViewStart + viewIndex;
        if (viewIndex < 0 || i >= _order.Count)
            return false;

        bool wasCurrent = i == _index;
        var entry = _order[i];
        Consume(entry);
        _order.RemoveAt(i);
        _natural.Remove(entry);
        if (i < _index)
            _index--;
        return wasCurrent;
    }

    public Song? SwapCurrentAndNext()
    {
        if (Current == null || _index + 1 >= _order.Count)
            return null;
        (_order[_index], _order[_index + 1]) = (_order[_index + 1], _order[_index]);
        Consume(_order[_index]);
        SyncNaturalIfUnshuffled();
        return Current;
    }

    /// <summary>Drops the files from the queue and from anything undo could bring back.</summary>
    public void RemoveTracks(IReadOnlySet<string> paths)
    {
        bool IsRemoved(Entry e) => paths.Contains(e.Song.FilePath);

        foreach (var removed in _order.Where(IsRemoved).ToList())
            Consume(removed);
        _natural.RemoveAll(IsRemoved);
        _index = RemoveKeepingPosition(_order, _index, IsRemoved);

        if (_undo is not { } undo)
            return;
        bool currentRemoved = undo.Index >= 0 && undo.Index < undo.Order.Count && IsRemoved(undo.Order[undo.Index]);
        int index = RemoveKeepingPosition(undo.Order, undo.Index, IsRemoved);
        _undo = undo with { Index = currentRemoved ? index - 1 : index };
    }

    private static int RemoveKeepingPosition(List<Entry> order, int index, Predicate<Entry> isRemoved)
    {
        int removedBefore = order.Take(Math.Max(index, 0)).Count(e => isRemoved(e));
        order.RemoveAll(isRemoved);
        return index < 0 ? -1 : index - removedBefore;
    }

    /// <summary>Keeps the current track and restores what was upcoming. Idle restores the queue.</summary>
    public bool TryUndo()
    {
        if (_undo is not { Order.Count: > 0 } snapshot)
            return false;

        _undo = null;
        var current = Current;
        ClearSongFlags();
        var upcoming = snapshot.Order.Skip(snapshot.Index + 1);

        if (current != null)
        {
            _order = new List<Entry> { new(current) };
            _order.AddRange(upcoming.Where(e => !SongIdentity.SamePath(e.Song, current)));
            _natural = new List<Entry>(_order);
            _index = 0;
        }
        else
        {
            // Nothing is loaded, so the snapshot's current track goes back to the front.
            _order = snapshot.Order.Skip(Math.Max(snapshot.Index, 0)).ToList();
            _natural = new List<Entry>(_order);
            _index = -1;
        }

        foreach (var entry in _order.Where(e => e.UserQueued))
            entry.Song.IsUserQueued = true;
        return true;
    }

    private void Inject(Song song, bool playNext)
    {
        if (Current != null && SongIdentity.SamePath(Current, song))
            return;

        // A queued song replaces its other upcoming copies.
        var duplicates = _order.Skip(_index + 1).Where(e => SongIdentity.SamePath(e.Song, song)).ToList();
        foreach (var duplicate in duplicates)
        {
            _order.Remove(duplicate);
            _natural.Remove(duplicate);
        }

        var entry = new Entry(song) { UserQueued = true };
        song.IsUserQueued = true;
        if (playNext && Current != null)
        {
            _natural.Insert(_natural.IndexOf(_order[_index]) + 1, entry);
            _order.Insert(_index + 1, entry);
        }
        else
        {
            _natural.Add(entry);
            _order.Add(entry);
        }
    }

    private void ShuffleTail(int start)
    {
        if (_order.Count - start < 2)
            return;

        var before = _order.Skip(start).ToList();
        for (int attempt = 0; attempt < ShuffleAttempts; attempt++)
        {
            for (int i = _order.Count - 1; i > start; i--)
            {
                int j = _random.Next(start, i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
            if (before.Count < 3 || !_order.Skip(start).SequenceEqual(before))
                return;
        }
    }

    private static int Find(List<Entry> entries, int start, Song song)
    {
        for (int i = start; i < entries.Count; i++)
        {
            if (ReferenceEquals(entries[i].Song, song))
                return i;
        }
        for (int i = start; i < entries.Count; i++)
        {
            if (SongIdentity.SamePath(entries[i].Song, song))
                return i;
        }
        return -1;
    }

    private void Consume(Entry entry)
    {
        if (!entry.UserQueued)
            return;
        entry.UserQueued = false;
        entry.Song.IsUserQueued = _order.Any(e => e.UserQueued && ReferenceEquals(e.Song, entry.Song));
    }

    private void ClearSongFlags()
    {
        foreach (var entry in _order)
            entry.Song.IsUserQueued = false;
    }

    private void SyncNaturalIfUnshuffled()
    {
        if (!IsShuffled)
            _natural = new List<Entry>(_order);
    }
}
