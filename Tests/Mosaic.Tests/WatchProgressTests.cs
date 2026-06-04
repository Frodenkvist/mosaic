using Mosaic.Services;

namespace Mosaic.Tests;

public class WatchProgressTests
{
    [Theory]
    [InlineData(95, 100)] // past 90%
    [InlineData(90, 100)] // exactly at the threshold
    [InlineData(100, 100)] // the very end
    public void Evaluate_AtOrPastThreshold_MarksWatched(double position, double end)
    {
        var result = WatchProgress.Evaluate(position, end, alreadyWatched: false);
        Assert.True(result.MarkWatched);
        Assert.Null(result.ResumePositionSeconds);
    }

    [Fact]
    public void Evaluate_BelowThreshold_ReturnsResumePosition_NotWatched()
    {
        var result = WatchProgress.Evaluate(50, 100, alreadyWatched: false);
        Assert.False(result.MarkWatched);
        Assert.Equal(50, result.ResumePositionSeconds);
    }

    [Fact]
    public void Evaluate_AlreadyWatched_StaysWatched_AndNeverClears()
    {
        // A later partial watch of an already-watched item must not un-watch it.
        var result = WatchProgress.Evaluate(10, 100, alreadyWatched: true);
        Assert.True(result.MarkWatched);
        Assert.Null(result.ResumePositionSeconds);
    }

    [Fact]
    public void Evaluate_ShortClip_AtThreshold_MarksWatched()
    {
        // A 5-second clip watched to 5s is finished (5 >= 5 * 0.9).
        Assert.True(WatchProgress.Evaluate(5, 5, alreadyWatched: false).MarkWatched);
    }

    [Theory]
    [InlineData(0, 0)]   // no runtime reported
    [InlineData(10, 0)]  // unknown end time
    [InlineData(-1, 100)] // nonsensical position
    public void Evaluate_WithoutUsableRuntime_DoesNothing(double position, double end)
    {
        var result = WatchProgress.Evaluate(position, end, alreadyWatched: false);
        Assert.False(result.MarkWatched);
        Assert.Null(result.ResumePositionSeconds);
    }
}
