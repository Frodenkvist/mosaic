using System.Text.Json;
using Mosaic.Models;
using Mosaic.Services;

namespace Mosaic.Tests;

/// <summary>
/// Covers the gbe_fork/Goldberg schema serialization and target-path helpers on
/// <see cref="SteamEmulatorUnlockSource"/> used by schema generation.
/// </summary>
public class EmulatorSchemaTests
{
    [Fact]
    public void BuildSchemaJson_UsesGbeForkFieldShape_WithHiddenAsString()
    {
        var defs = new[]
        {
            new Achievement { ApiName = "ACH_1", DisplayName = "Café déjà vu", Description = "Win the café", Hidden = false },
            new Achievement { ApiName = "ACH_2", DisplayName = "Hidden one", Description = null, Hidden = true },
        };

        var json = SteamEmulatorUnlockSource.BuildSchemaJson(defs);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());

        var first = doc.RootElement[0];
        Assert.Equal("ACH_1", first.GetProperty("name").GetString());
        Assert.Equal("Café déjà vu", first.GetProperty("displayName").GetString());
        Assert.Equal("Win the café", first.GetProperty("description").GetString());
        // hidden MUST be the JSON string "0"/"1", not a boolean or number — gbe_fork expects a string.
        Assert.Equal(JsonValueKind.String, first.GetProperty("hidden").ValueKind);
        Assert.Equal("0", first.GetProperty("hidden").GetString());
        Assert.Equal("", first.GetProperty("icon").GetString());
        Assert.Equal("", first.GetProperty("icongray").GetString());

        var second = doc.RootElement[1];
        Assert.Equal("1", second.GetProperty("hidden").GetString());     // Hidden = true -> "1"
        Assert.Equal("", second.GetProperty("description").GetString()); // null -> empty string

        // Non-ASCII display names are written literally (relaxed encoder), not \uXXXX-escaped.
        Assert.Contains("Café déjà vu", json);
    }

    [Fact]
    public void SchemaTargetPath_IsSteamSettingsAchievementsJson_NextToExecutable()
    {
        var game = new Game { ExecutablePath = @"F:\Games\007 First Light\Retail\007FirstLight.exe" };

        Assert.Equal(
            @"F:\Games\007 First Light\Retail\steam_settings\achievements.json",
            SteamEmulatorUnlockSource.SchemaTargetPath(game));
    }

    [Fact]
    public void SchemaTargetPath_IsNull_WhenExecutablePathIsBlank()
    {
        Assert.Null(SteamEmulatorUnlockSource.SchemaTargetPath(new Game { ExecutablePath = "" }));
    }
}
