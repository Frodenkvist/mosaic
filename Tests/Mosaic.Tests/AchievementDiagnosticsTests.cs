using Mosaic.Services;

namespace Mosaic.Tests;

/// <summary>
/// Covers the <see cref="ScanDiagnostic.Summary"/> branches that explain a "found nothing" scan,
/// especially the distinction between an emulator save folder that exists but holds no achievements
/// file (missing schema) and no save folder at all (never ran under a supported emulator).
/// </summary>
public class AchievementDiagnosticsTests
{
    [Fact]
    public void Summary_SaveFolderExistsButNoFile_PointsToSchemaGeneration()
    {
        var diag = new ScanDiagnostic
        {
            Candidates = new[]
            {
                new ScanCandidateInfo(@"C:\Users\me\AppData\Roaming\GSE Saves\123\achievements.json",
                    Existed: false, ParsedKeyCount: 0, Error: null) { DirectoryExisted = true },
            },
            FoundSaveFolder = @"C:\Users\me\AppData\Roaming\GSE Saves\123",
        };

        Assert.True(diag.SaveFolderFound);
        Assert.Equal(0, diag.LocationsFound);
        Assert.Contains(@"C:\Users\me\AppData\Roaming\GSE Saves\123", diag.Summary);
        Assert.Contains("no achievement schema", diag.Summary);
        Assert.Contains("Generate emulator schema", diag.Summary);
    }

    [Fact]
    public void Summary_NoSaveFolder_ReportsNoFolderFound()
    {
        var diag = new ScanDiagnostic
        {
            Candidates = new[]
            {
                new ScanCandidateInfo(@"C:\Games\Foo\achievements.json",
                    Existed: false, ParsedKeyCount: 0, Error: null) { DirectoryExisted = false },
            },
            // FoundSaveFolder left null: nothing emulator-specific existed.
        };

        Assert.False(diag.SaveFolderFound);
        Assert.Contains("No recognized achievement file", diag.Summary);   // kept for back-compat wording
        Assert.Contains("No emulator save folder", diag.Summary);
    }

    [Fact]
    public void Summary_FileFound_ReportsParsedAndMatchedCounts()
    {
        var diag = new ScanDiagnostic
        {
            Candidates = new[]
            {
                new ScanCandidateInfo(@"C:\Games\Foo\achievements.json",
                    Existed: true, ParsedKeyCount: 3, Error: null) { DirectoryExisted = true },
            },
            ParsedKeyCount = 3,
            MatchedCount = 2,
            UnmatchedCount = 1,
            SampleUnmatchedKeys = new[] { "ZZ_UNKNOWN" },
        };

        Assert.False(diag.SaveFolderFound);
        Assert.Contains("Found 1 file", diag.Summary);
        Assert.Contains("2 matched the achievement list", diag.Summary);
        Assert.Contains("matched no definition", diag.Summary);   // the unmatched-key hint
    }
}
