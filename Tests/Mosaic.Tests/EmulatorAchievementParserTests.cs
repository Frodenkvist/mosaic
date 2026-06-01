using Mosaic.Services;

namespace Mosaic.Tests;

public class EmulatorAchievementParserTests
{
    [Fact]
    public void Goldberg_ReturnsOnlyEarned_WithUnixTimestamp()
    {
        // Goldberg/GSE save format: a map of key -> { earned, earned_time }.
        var json = """
        {
          "ACH_WIN_ONE_GAME":   { "earned": true,  "earned_time": 1609459200 },
          "ACH_DIE_FIRST":      { "earned": false, "earned_time": 0 },
          "ACH_NO_TIME":        { "earned": true,  "earned_time": 0 }
        }
        """;

        var unlocks = EmulatorAchievementParser.ParseGoldbergJson(json);

        Assert.Equal(2, unlocks.Count); // only the two earned ones
        var win = unlocks.Single(u => u.ApiName == "ACH_WIN_ONE_GAME");
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1609459200), win.UnlockedAt);

        var noTime = unlocks.Single(u => u.ApiName == "ACH_NO_TIME");
        Assert.Null(noTime.UnlockedAt); // earned but no usable timestamp

        Assert.DoesNotContain(unlocks, u => u.ApiName == "ACH_DIE_FIRST");
    }

    [Fact]
    public void Goldberg_SchemaArrayOrGarbage_IsIgnored()
    {
        // The steam_settings schema file is a JSON array, not the unlock map — must yield nothing.
        Assert.Empty(EmulatorAchievementParser.ParseGoldbergJson("""[{"name":"ACH_X","displayName":"X"}]"""));
        Assert.Empty(EmulatorAchievementParser.ParseGoldbergJson("not json at all"));
        Assert.Empty(EmulatorAchievementParser.ParseGoldbergJson(""));
    }

    [Fact]
    public void Ini_PerAchievementSections_WithAchievedAndUnlockTime()
    {
        // CODEX/ALI213-style: one section per achievement.
        var ini = """
        [SteamAchievements]
        Count=2

        [ACH_WIN_ONE_GAME]
        Achieved=1
        UnlockTime=1609459200

        [ACH_LOSE]
        Achieved=0
        """;

        var unlocks = EmulatorAchievementParser.ParseIni(ini);

        var win = Assert.Single(unlocks);
        Assert.Equal("ACH_WIN_ONE_GAME", win.ApiName);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1609459200), win.UnlockedAt);
    }

    [Fact]
    public void Ini_FlatAchievementsSection_KeyEqualsOne()
    {
        // SmartSteamEmu/flat style: [Achievements] with KEY=1 lines.
        var ini = """
        [Achievements]
        Count=3
        ACH_A=1
        ACH_B=0
        ACH_C=true
        """;

        var unlocks = EmulatorAchievementParser.ParseIni(ini);

        Assert.Equal(2, unlocks.Count);
        Assert.Contains(unlocks, u => u.ApiName == "ACH_A");
        Assert.Contains(unlocks, u => u.ApiName == "ACH_C");
        Assert.DoesNotContain(unlocks, u => u.ApiName == "ACH_B"); // =0
        Assert.DoesNotContain(unlocks, u => u.ApiName == "Count"); // meta key skipped
    }

    [Fact]
    public void Ini_EmptyOrGarbage_IsIgnored()
    {
        Assert.Empty(EmulatorAchievementParser.ParseIni(""));
        Assert.Empty(EmulatorAchievementParser.ParseIni("just some text\nwith no sections"));
    }
}
