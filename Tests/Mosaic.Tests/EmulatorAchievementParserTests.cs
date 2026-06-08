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

    [Fact]
    public void Goldberg_FlatMap_OfBoolNumberOrString_IsEarned()
    {
        // Some emulator builds write a flat key -> truthy map with no nested object / timestamp.
        var json = """
        {
          "ACH_A": true,
          "ACH_B": 1,
          "ACH_C": "1",
          "ACH_D": false,
          "ACH_E": 0
        }
        """;

        var unlocks = EmulatorAchievementParser.ParseGoldbergJson(json);

        Assert.Equal(3, unlocks.Count);
        Assert.Contains(unlocks, u => u.ApiName == "ACH_A");
        Assert.Contains(unlocks, u => u.ApiName == "ACH_B");
        Assert.Contains(unlocks, u => u.ApiName == "ACH_C");
        Assert.DoesNotContain(unlocks, u => u.ApiName == "ACH_D");
        Assert.DoesNotContain(unlocks, u => u.ApiName == "ACH_E");
        Assert.All(unlocks, u => Assert.Null(u.UnlockedAt)); // flat form carries no timestamp
    }

    [Fact]
    public void Goldberg_UnlockTime_IsHonoredAsEarnedTimeAlias()
    {
        var json = """{ "ACH_X": { "earned": true, "unlock_time": 1609459200 } }""";

        var win = Assert.Single(EmulatorAchievementParser.ParseGoldbergJson(json));
        Assert.Equal("ACH_X", win.ApiName);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1609459200), win.UnlockedAt);
    }

    [Fact]
    public void Ini_Ali213Style_HaveAchievedAndHaveAchievedTime()
    {
        // ALI213-style per-achievement section: HaveAchieved / HaveAchievedTime instead of Achieved/UnlockTime.
        var ini = """
        [ACH_WIN_ONE_GAME]
        HaveAchieved=1
        HaveAchievedTime=1609459200

        [ACH_LOSE]
        HaveAchieved=0
        """;

        var win = Assert.Single(EmulatorAchievementParser.ParseIni(ini));
        Assert.Equal("ACH_WIN_ONE_GAME", win.ApiName);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1609459200), win.UnlockedAt);
    }

    [Fact]
    public void Ini_FlatAchievements_LuaTableValues_SteamDataStyle()
    {
        // ALI213/3DM "SteamData\user_stats.ini": [ACHIEVEMENTS] with quoted keys and Lua-table values.
        var ini = """
        [ACHIEVEMENTS]
        "area1" = {unlocked = true, time = 1766756089}
        "oo" = {unlocked = true, time = 1766756102}
        "locked_one" = {unlocked = false, time = 0}
        "no_time" = {unlocked = true}
        """;

        var unlocks = EmulatorAchievementParser.ParseIni(ini);

        Assert.Equal(3, unlocks.Count);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1766756089),
            unlocks.Single(u => u.ApiName == "area1").UnlockedAt);          // quotes stripped, time parsed
        Assert.Contains(unlocks, u => u.ApiName == "oo");
        Assert.Null(unlocks.Single(u => u.ApiName == "no_time").UnlockedAt); // unlocked, no timestamp
        Assert.DoesNotContain(unlocks, u => u.ApiName == "locked_one");      // unlocked = false
    }

    [Fact]
    public void ParseFailure_IsReportedAsError_NotConfusedWithEmpty()
    {
        // Malformed JSON: distinguishable from a valid-but-empty file via the out error.
        Assert.Empty(EmulatorAchievementParser.ParseGoldbergJson("{ not valid json", out var error));
        Assert.NotNull(error);

        // Valid JSON with nothing earned: no error, just empty.
        Assert.Empty(EmulatorAchievementParser.ParseGoldbergJson("{}", out var noError));
        Assert.Null(noError);
    }
}
