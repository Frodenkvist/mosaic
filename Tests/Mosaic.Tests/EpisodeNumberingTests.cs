using Mosaic.Services;

namespace Mosaic.Tests;

public class EpisodeNumberingTests
{
    /// <summary>Normalizes a list of (season, episode) pairs keyed by their index in the list.</summary>
    private static (List<(int Season, int Episode)> Input, IReadOnlyDictionary<int, int> Corrected) Normalize(
        IEnumerable<(int Season, int Episode)> eps)
    {
        var input = eps.ToList();
        var entries = input.Select((e, i) => (Key: i, e.Season, e.Episode));
        return (input, EpisodeNumbering.NormalizeWithinSeason(entries));
    }

    private static IEnumerable<(int Season, int Episode)> Range(int season, int from, int to)
    {
        for (var e = from; e <= to; e++)
            yield return (season, e);
    }

    [Fact]
    public void AbsoluteNumbering_AcrossThreeSeasons_RestartsEachSeasonAtOne()
    {
        // S1: 1..8, S2: 9..16, S3: 17..25 (a single running count across seasons).
        var (input, corrected) = Normalize(
            Range(1, 1, 8).Concat(Range(2, 9, 16)).Concat(Range(3, 17, 25)));

        for (var i = 0; i < input.Count; i++)
        {
            var (season, episode) = input[i];
            var expected = season switch
            {
                1 => episode,        // base season unchanged
                2 => episode - 8,    // 9..16 -> 1..8
                3 => episode - 16,   // 17..25 -> 1..9
                _ => episode,
            };
            Assert.Equal(expected, corrected[i]);
        }
    }

    [Fact]
    public void WithinSeasonNumbering_IsLeftUnchanged()
    {
        // S1: 1..10, S2: 1..10 (each season already restarts at 1).
        var (input, corrected) = Normalize(Range(1, 1, 10).Concat(Range(2, 1, 10)));

        for (var i = 0; i < input.Count; i++)
            Assert.Equal(input[i].Episode, corrected[i]);
    }

    [Fact]
    public void AlreadyNormalized_ReRun_IsIdempotent()
    {
        // Feed the corrected result of the absolute case back in: S1 1..8, S2 1..8, S3 1..9.
        var (input, corrected) = Normalize(
            Range(1, 1, 8).Concat(Range(2, 1, 8)).Concat(Range(3, 1, 9)));

        for (var i = 0; i < input.Count; i++)
            Assert.Equal(input[i].Episode, corrected[i]);
    }

    [Fact]
    public void GapInEarlierSeason_StillDetectsContinuation_ViaPreviousSeasonMax()
    {
        // S1 is missing episode 5 (1,2,3,4,6,7,8 — max is still 8); S2 continues at 9.
        var s1 = new[] { (1, 1), (1, 2), (1, 3), (1, 4), (1, 6), (1, 7), (1, 8) };
        var (input, corrected) = Normalize(s1.Concat(Range(2, 9, 16)));

        for (var i = 0; i < input.Count; i++)
        {
            var (season, episode) = input[i];
            var expected = season == 2 ? episode - 8 : episode; // S2 9..16 -> 1..8
            Assert.Equal(expected, corrected[i]);
        }
    }

    [Fact]
    public void NoPriorSeason_LaterSeasonLeftAsIs()
    {
        // Only season 2 is present (e.g. imported before season 1) — nothing to anchor to.
        var (input, corrected) = Normalize(Range(2, 9, 16));

        for (var i = 0; i < input.Count; i++)
            Assert.Equal(input[i].Episode, corrected[i]);
    }

    [Fact]
    public void EntriesWithoutBothNumbers_AreCallerFiltered_EmptyInputYieldsEmpty()
    {
        var corrected = EpisodeNumbering.NormalizeWithinSeason(
            Array.Empty<(string Key, int Season, int Episode)>());
        Assert.Empty(corrected);
    }
}
