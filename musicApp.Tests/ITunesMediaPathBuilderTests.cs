using musicApp;
using musicApp.Helpers;
using Xunit;

namespace musicApp.Tests;

// Rules checked against a real iTunes-organized library (99% of 8863 paths matched).
public class ITunesMediaPathBuilderTests
{
    private static string Build(Song s) => ITunesMediaPathBuilder.BuildRelativePath(s, ".mp3");

    [Theory]
    [InlineData("", 0, @"01 T.mp3")]      // no disc tag
    [InlineData("1", 1, @"01 T.mp3")]     // disc 1 of 1
    [InlineData("1", 0, @"1-01 T.mp3")]   // disc 1, count unknown: iTunes still prefixes
    [InlineData("2", 2, @"2-01 T.mp3")]
    [InlineData("1/2", 0, @"1-01 T.mp3")] // count inside the disc tag
    public void DiscPrefixFollowsITunes(string disc, int discTotal, string expectedFile)
    {
        var s = new Song { Title = "T", Artist = "A", Album = "B", TrackNumber = 1, DiscNumber = disc, DiscTotal = discTotal };
        Assert.Equal(Path.Combine("A", "B", expectedFile), Build(s));
    }

    [Fact]
    public void FolderFallbacksAndIllegalCharacters()
    {
        Assert.Equal(Path.Combine("Compilations", "Mix", "T.mp3"),
            Build(new Song { Title = "T", Artist = "A", AlbumArtist = "AA", Album = "Mix", IsCompilation = true }));
        Assert.Equal(Path.Combine("AA", "Mix", "T.mp3"),
            Build(new Song { Title = "T", Artist = "A", AlbumArtist = "AA", Album = "Mix" }));
        Assert.Equal(Path.Combine("Unknown Artist", "Unknown Album", "T.mp3"), Build(new Song { Title = "T" }));
        Assert.Equal(Path.Combine("Queen_Bowie", "Side C_ Seal", "01 God_.mp3"),
            Build(new Song { Title = "God?", Artist = "Queen;Bowie", Album = "Side C: Seal", TrackNumber = 1 }));
    }

    [Fact]
    public void LongNamesAreCutLikeITunes()
    {
        var s = new Song
        {
            Title = "Now and at the Hour of Our Death (feat. Someone)",
            Artist = "Gunther & the Sunshine Girls & The Sunshine Boys",
            Album = "Vol.",
            TrackNumber = 6
        };
        // Folders 40 characters, file stem 36 with trailing spaces trimmed; a folder's trailing dot becomes "_".
        Assert.Equal(Path.Combine("Gunther & the Sunshine Girls & The Sunsh", "Vol_", "06 Now and at the Hour of Our Death.mp3"),
            Build(s));
    }

    [Fact]
    public void WindowsDeviceNamesGetPrefixed()
    {
        Assert.Equal(Path.Combine("_Con", "_nul.x", "_AUX.mp3"),
            Build(new Song { Title = "Unknown Title", Artist = "Con", Album = "nul.x", FilePath = @"C:\drop\AUX.mp3" }));
        Assert.Equal(Path.Combine("Console", "COM10", "01 Nul.mp3"),
            Build(new Song { Title = "Nul", Artist = "Console", Album = "COM10", TrackNumber = 1 }));
    }

    [Fact]
    public void MissingTitleKeepsTheOriginalFileName()
    {
        var s = new Song { Title = "Unknown Title", Artist = "A", Album = "B", TrackNumber = 8, FilePath = @"C:\drop\Vision.mp3" };
        Assert.Equal(Path.Combine("A", "B", "Vision.mp3"), Build(s));
    }

    [Fact]
    public void CollisionAddsNumberBeforeExtension()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "01 T.mp3");
            Assert.Equal(path, ITunesMediaPathBuilder.ResolveCollision(path));
            File.WriteAllText(path, "");
            File.WriteAllText(Path.Combine(dir, "01 T 1.mp3"), "");
            Assert.Equal(Path.Combine(dir, "01 T 2.mp3"), ITunesMediaPathBuilder.ResolveCollision(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
