using musicApp;
using musicApp.Playback;
using Xunit;

namespace musicApp.Tests;

public class PlaybackQueueTests
{
    private static List<Song> Songs(string names) =>
        names.Select(c => new Song { Title = c.ToString(), FilePath = $@"C:\music\{c}.wav" }).ToList();

    private static string Titles(PlaybackQueue q) => string.Concat(q.View().Select(s => s.Title));

    [Fact]
    public void ShuffleKeepsEveryEntryOnceIncludingDuplicates()
    {
        var songs = Songs("abcd");
        songs.Add(songs[1]);
        var q = new PlaybackQueue(new Random(1));
        q.SetShuffle(true);
        q.Start(songs, songs[0]);

        Assert.Equal("a", q.Current?.Title);
        Assert.Equal("abbcd", string.Concat(Titles(q).OrderBy(c => c)));
    }

    [Fact]
    public void ShuffleStartedMidListIncludesEarlierTracks()
    {
        var songs = Songs("abcdef");
        var q = new PlaybackQueue(new Random(2));
        q.SetShuffle(true);
        q.Start(songs, songs[3]);

        Assert.Equal("d", q.Current?.Title);
        Assert.Equal(6, q.View().Count);
    }

    [Fact]
    public void ShuffleOffRestoresNaturalOrderWithoutReplayingPlayedTracks()
    {
        var songs = Songs("abcdef");
        var q = new PlaybackQueue(new Random(3));
        q.Start(songs, songs[0]);
        q.Advance(RepeatMode.Off);
        q.SetShuffle(true);
        q.SetShuffle(false);

        Assert.Equal("bcdef", Titles(q));
    }

    [Theory]
    [InlineData(RepeatMode.Off, null)]
    [InlineData(RepeatMode.One, null)]
    [InlineData(RepeatMode.All, "a")]
    public void AdvancePastEndFollowsRepeatMode(RepeatMode repeat, string? expected)
    {
        var songs = Songs("ab");
        var q = new PlaybackQueue();
        q.Start(songs, songs[1]);

        Assert.Equal(expected, q.Advance(repeat)?.Title);
    }

    [Fact]
    public void RewindStopsAtFirstTrack()
    {
        var songs = Songs("ab");
        var q = new PlaybackQueue();
        q.Start(songs, songs[1]);

        Assert.Equal("a", q.Rewind()?.Title);
        Assert.Null(q.Rewind());
    }

    [Fact]
    public void QueueEditsKeepCurrentTrack()
    {
        var songs = Songs("abcdef");
        var q = new PlaybackQueue();
        q.Start(songs, songs[0]);

        q.PlayNext(songs[4]);
        q.Add(songs[1]);
        Assert.Equal("aecdfb", Titles(q));

        q.Move(3, 1);
        Assert.Equal("adecfb", Titles(q));

        Assert.False(q.RemoveAt(2));
        Assert.Equal("adcfb", Titles(q));
        Assert.Equal("a", q.Current?.Title);
    }

    [Fact]
    public void QueueEditsDoNotChangeTheSourceList()
    {
        var songs = Songs("abcd");
        var q = new PlaybackQueue();
        q.Start(songs, songs[0]);
        q.Move(3, 1);
        q.RemoveAt(2);

        Assert.Equal("abcd", string.Concat(songs.Select(s => s.Title)));
    }

    [Fact]
    public void DifferentFilesWithSameTagsAreDifferentTracks()
    {
        var first = new Song { Title = "Unknown Title", Artist = "Unknown Artist", Album = "Unknown Album", FilePath = @"C:\music\1.wav" };
        var second = new Song { Title = "Unknown Title", Artist = "Unknown Artist", Album = "Unknown Album", FilePath = @"C:\music\2.wav" };
        var q = new PlaybackQueue();
        q.Start(new[] { first }, first);

        Assert.False(q.Contains(second));
        Assert.Equal(-1, q.IndexInView(second));
    }

    [Fact]
    public void QueuedMarkClearsWhenTheTrackBecomesCurrent()
    {
        var songs = Songs("abc");
        var q = new PlaybackQueue();
        q.Start(songs, songs[0]);
        q.PlayNext(songs[2]);
        Assert.True(songs[2].IsUserQueued);

        q.Advance(RepeatMode.Off);

        Assert.Equal("c", q.Current?.Title);
        Assert.False(songs[2].IsUserQueued);
    }

    [Fact]
    public void RemovedTracksDoNotComeBackThroughUndo()
    {
        var songs = Songs("abcd");
        var q = new PlaybackQueue();
        q.Start(songs, songs[0]);
        q.Start(songs, songs[3]);
        q.RemoveTracks(new HashSet<string> { songs[2].FilePath });

        Assert.True(q.TryUndo());
        Assert.Equal("db", Titles(q));
    }

    [Fact]
    public void UndoRestoresUpcomingAndKeepsCurrent()
    {
        var songs = Songs("abcdef");
        var q = new PlaybackQueue();
        q.Start(songs, songs[0]);
        q.Start(songs.Take(4).ToList(), songs[3]);

        Assert.True(q.TryUndo());
        Assert.Equal("dbcef", Titles(q));
        Assert.False(q.CanUndo);
    }
}
