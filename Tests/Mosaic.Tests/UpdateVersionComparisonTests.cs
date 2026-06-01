using Mosaic.Services;

namespace Mosaic.Tests;

public class UpdateVersionComparisonTests
{
    [Theory]
    // Strictly newer remote → update.
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.0.0", "2.0.0", true)]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.2.3", "1.2.10", true)]   // numeric, not lexicographic (10 > 3)
    // Equal or older remote → no update.
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.1.0", "1.0.0", false)]
    [InlineData("2.0.0", "1.9.9", false)]
    // The Version("1.0.0") vs Version("1.0.0.0") revision pitfall must NOT report a phantom update.
    [InlineData("1.0.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.0.0", false)]
    public void TryGetNewerVersion_ComparesNormalizedVersions(string running, string tag, bool expectUpdate)
    {
        var isNewer = UpdateService.TryGetNewerVersion(tag, Version.Parse(running), out var remote);

        Assert.Equal(expectUpdate, isNewer);
        if (expectUpdate)
            Assert.Equal(UpdateService.Normalize(Version.Parse(tag.TrimStart('v'))), UpdateService.Normalize(remote));
    }

    [Theory]
    [InlineData("v1.2.0", true, "1.2.0")]   // leading 'v' stripped
    [InlineData("V1.2.0", true, "1.2.0")]   // upper-case 'V' too
    [InlineData("1.2.0", true, "1.2.0")]    // bare version
    [InlineData("1.2", true, "1.2")]        // two-part is fine
    [InlineData("not-a-version", false, null)]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    public void TryParseTag_HandlesPrefixAndGarbage(string? tag, bool expectParsed, string? expectedVersion)
    {
        var parsed = UpdateService.TryParseTag(tag, out var version);

        Assert.Equal(expectParsed, parsed);
        if (expectParsed)
            Assert.Equal(Version.Parse(expectedVersion!), version);
    }

    [Fact]
    public void TryGetNewerVersion_UnparseableTag_IsNotAnUpdate()
    {
        // A release with a non-version tag must never be treated as newer than the running build.
        Assert.False(UpdateService.TryGetNewerVersion("nightly", new Version(1, 0, 0), out _));
        Assert.False(UpdateService.TryGetNewerVersion(null, new Version(1, 0, 0), out _));
    }
}
