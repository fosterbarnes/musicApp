using musicApp;
using musicApp.Helpers;
using Xunit;

namespace musicApp.Tests;

public class RecentlyAddedOrderTests
{
    private static Song S(string album, string artist, string title, int hour) => new()
    {
        Album = album, Artist = artist, Title = title, FilePath = $@"C:\m\{album}\{title}.mp3",
        DateAdded = new DateTime(2026, 11, 24, hour, 0, 0)
    };

    [Fact]
    public void SameDayAlbumsAreNewestFirstNotAlphabetical()
    {
        // Was sorted by day only, then A-Z: "Abbey" came before the newer "Zebra".
        var songs = new[] { S("Abbey", "A", "a1", 9), S("Zebra", "Z", "z1", 12), S("Middle", "M", "m1", 10) };
        Assert.Equal(new[] { "Zebra", "Middle", "Abbey" },
            RecentlyAddedPlaybackOrder.BuildGroups(songs).Select(g => g.Tracks[0].Album));
    }

    [Fact]
    public void CaseVariantsAreOneAlbumAndAlbumlessSongsGetTheirOwnGroup()
    {
        var songs = new[]
        {
            S("OK Computer", "Radiohead", "t1", 9), S("Ok Computer", "radiohead", "t2", 9),
            S("Unknown Album", "Unknown Artist", "loose", 11)
        };
        var groups = RecentlyAddedPlaybackOrder.BuildGroups(songs);
        Assert.Equal(2, groups.Count);
        Assert.True(groups[0].IsSingleSong);
        Assert.Equal(2, groups[1].Tracks.Count);
        Assert.Equal(new[] { "loose", "t1", "t2" }, RecentlyAddedPlaybackOrder.BuildOrderedTracks(songs).Select(s => s.Title));
    }
}
