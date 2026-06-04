using Mosaic.Services;

namespace Mosaic.Tests;

public class MediaNameParserTests
{
    [Fact]
    public void TryParseEpisode_SxxExx_InFileName()
    {
        var ep = MediaNameParser.TryParseEpisode(@"C:\TV\Show.Name.S01E03.Title.mkv");
        Assert.NotNull(ep);
        Assert.Equal("Show Name", ep!.Value.ShowName);
        Assert.Equal(1, ep.Value.Season);
        Assert.Equal(3, ep.Value.Episode);
    }

    [Fact]
    public void TryParseEpisode_NxNN_InFileName()
    {
        var ep = MediaNameParser.TryParseEpisode(@"C:\TV\Breaking Bad 1x02.mkv");
        Assert.NotNull(ep);
        Assert.Equal("Breaking Bad", ep!.Value.ShowName);
        Assert.Equal(1, ep.Value.Season);
        Assert.Equal(2, ep.Value.Episode);
    }

    [Fact]
    public void TryParseEpisode_SeasonFolder_WithEpisodeWord()
    {
        var ep = MediaNameParser.TryParseEpisode(@"C:\TV\The Office\Season 2\Episode 05.mkv");
        Assert.NotNull(ep);
        Assert.Equal("The Office", ep!.Value.ShowName);
        Assert.Equal(2, ep.Value.Season);
        Assert.Equal(5, ep.Value.Episode);
    }

    [Fact]
    public void TryParseEpisode_SeasonFolder_BareTrailingNumber()
    {
        var ep = MediaNameParser.TryParseEpisode(@"C:\TV\The Office\Season 2\The Office - 05.mkv");
        Assert.NotNull(ep);
        Assert.Equal("The Office", ep!.Value.ShowName);
        Assert.Equal(2, ep.Value.Season);
        Assert.Equal(5, ep.Value.Episode);
    }

    [Theory]
    [InlineData(@"C:\Movies\Inception (2010).mkv")]
    [InlineData(@"C:\Movies\The Matrix 1999 1080p BluRay.mkv")]
    [InlineData(@"C:\Movies\Some Random Movie.mkv")]
    public void TryParseEpisode_Movies_ReturnNull(string path)
    {
        Assert.Null(MediaNameParser.TryParseEpisode(path));
    }

    [Fact]
    public void ParseMovie_TitleAndYear_FromParenthesizedYear()
    {
        var movie = MediaNameParser.ParseMovie("Inception (2010)");
        Assert.Equal("Inception", movie.Title);
        Assert.Equal(2010, movie.Year);
    }

    [Fact]
    public void ParseMovie_StripsSceneTags_AndKeepsYear()
    {
        var movie = MediaNameParser.ParseMovie("The.Matrix.1999.1080p.BluRay.x264");
        Assert.Equal("The Matrix", movie.Title);
        Assert.Equal(1999, movie.Year);
    }

    [Fact]
    public void ParseMovie_NoYear_StripsQualityTags()
    {
        var movie = MediaNameParser.ParseMovie("Some Movie 720p WEB-DL");
        Assert.Equal("Some Movie", movie.Title);
        Assert.Null(movie.Year);
    }

    [Fact]
    public void ParseMovie_PlainTitle_Unchanged()
    {
        var movie = MediaNameParser.ParseMovie("Some Random Movie");
        Assert.Equal("Some Random Movie", movie.Title);
        Assert.Null(movie.Year);
    }
}
