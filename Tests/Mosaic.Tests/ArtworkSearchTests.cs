using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.Tests;

public class ArtworkSearchTests
{
    [Fact]
    public void BuildSearchTerms_PrefersFolderNamesOverAmbiguousExeName()
    {
        // The reported case: exe is "mio.exe" but the real title is the install folder.
        var game = new Game
        {
            Name = "mio", // default name from the executable
            ExecutablePath = @"E:\Games\Memories in Orbit\MIO Memories in Orbit\mio.exe",
        };

        var terms = ArtworkService.BuildSearchTerms(game);

        // Folder-derived titles must be tried before the ambiguous "mio".
        Assert.Contains("MIO Memories in Orbit", terms);
        Assert.Contains("Memories in Orbit", terms);
        Assert.True(terms.IndexOf("Memories in Orbit") < terms.IndexOf("mio"),
            "Folder name should be searched before the executable name.");
        // The generic "Games" container is not a search term.
        Assert.DoesNotContain("Games", terms);
    }

    [Fact]
    public void BuildSearchTerms_UsesCustomNameFirst()
    {
        var game = new Game
        {
            Name = "Hollow Knight",
            ExecutablePath = @"C:\Games\hk\bin\hk.exe",
        };

        var terms = ArtworkService.BuildSearchTerms(game);

        Assert.Equal("Hollow Knight", terms[0]);
        Assert.DoesNotContain("bin", terms); // generic build folder excluded
    }

    [Fact]
    public void BuildSearchTerms_CleansSeparators()
    {
        var game = new Game { Name = "x", ExecutablePath = @"C:\Games\Memories.in.Orbit\game.exe" };
        Assert.Contains("Memories in Orbit", ArtworkService.BuildSearchTerms(game));
    }

    [Theory]
    [InlineData("MIO Memories in Orbit", "Memories in Orbit", true)]   // term mostly covered
    [InlineData("Memories in Orbit", "Memories in Orbit", true)]       // exact
    [InlineData("mio", "Minecraft", false)]                            // unrelated
    [InlineData("Memories in Orbit", "Halo Infinite", false)]
    public void Similarity_MatchesExpectedThreshold(string term, string candidate, bool shouldMatch)
    {
        var score = ArtworkService.Similarity(term, candidate);
        Assert.Equal(shouldMatch, score >= 0.5);
    }

    [Theory]
    [InlineData("HatinTime", "Hatin Time")]
    [InlineData("HollowKnight", "Hollow Knight")]
    [InlineData("DOOMTheDarkAges", "DOOM The Dark Ages")]
    [InlineData("Quake3", "Quake 3")]
    [InlineData("Celeste", "Celeste")] // single word: unchanged
    public void SplitCamelCase_InsertsWordBoundaries(string input, string expected)
    {
        Assert.Equal(expected, ArtworkService.SplitCamelCase(input));
    }

    [Fact]
    public void NameIsAutoDerived_TrueForScanFolderAndExeNames_FalseForCustom()
    {
        // Scan-derived: name equals a folder in the path -> adopt the matched title later.
        var scanned = new Game { Name = "HatinTime", ExecutablePath = @"E:\Games\HatinTime\Binaries\Win64\HatinTimeGame.exe" };
        Assert.True(ArtworkService.NameIsAutoDerived(scanned));

        // Exe-derived default name.
        var exeNamed = new Game { Name = "GoWR", ExecutablePath = @"E:\Games\God of War Ragnarok\GoWR.exe" };
        Assert.True(ArtworkService.NameIsAutoDerived(exeNamed));

        // A name the user typed that matches no path component is left alone.
        var custom = new Game { Name = "My Favourite RPG", ExecutablePath = @"E:\Games\rpg_build\game.exe" };
        Assert.False(ArtworkService.NameIsAutoDerived(custom));
    }

    [Fact]
    public void CamelCaseTerms_ProducesSplitVariantsForLastResort()
    {
        var game = new Game { Name = "HatinTime", ExecutablePath = @"E:\Games\HatinTime\Binaries\Win64\HatinTimeGame.exe" };
        var terms = ArtworkService.CamelCaseTerms(game);
        Assert.Contains("Hatin Time", terms);
        Assert.Contains("Hatin Time Game", terms);
    }

    [Fact]
    public void Similarity_IgnoresDiacritics_AndPrefersTheFullSequelTitle()
    {
        // The reported bug: "God of War Ragnarok" must match "Ragnarök" (umlaut) better
        // than the base "God of War" that it happens to contain.
        var ragnarok = ArtworkService.Similarity("God of War Ragnarok", "God of War Ragnarök");
        var baseGame = ArtworkService.Similarity("God of War Ragnarok", "God of War");

        Assert.True(ragnarok >= 0.99, $"diacritic-insensitive exact match expected, got {ragnarok}");
        Assert.True(ragnarok > baseGame, $"Ragnarök ({ragnarok}) should outrank base God of War ({baseGame})");
    }
}
