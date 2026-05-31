using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Mosaic.Data;
using Mosaic.Models;

namespace Mosaic.Services;

public class ArtworkService : IArtworkService
{
    private static readonly (ArtworkKind Kind, string Endpoint)[] Kinds =
    {
        (ArtworkKind.Grid, "grids"),
        (ArtworkKind.Hero, "heroes"),
        (ArtworkKind.Logo, "logos"),
    };

    // Folder names that are containers/build artifacts, not the game's title.
    private static readonly HashSet<string> GenericFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "bin64", "binaries", "binary", "win", "win32", "win64", "windows", "x64", "x86",
        "release", "debug", "build", "builds", "dist", "redist", "app", "application", "game",
        "games", "data", "content", "program files", "program files (x86)", "steamlibrary",
        "steamapps", "common", "gog galaxy", "gog games", "epic games", "downloads",
    };

    // Minimum name-similarity required to accept a SteamGridDB match.
    private const double MatchThreshold = 0.5;

    // Looser bar used only by the CamelCase last-resort pass (still requires cover art).
    private const double RelaxedMatchThreshold = 0.3;

    // Serializes SteamGridDB access so a batch add (e.g. after a scan) isn't rate-limited.
    private static readonly SemaphoreSlim FetchGate = new(1, 1);

    private readonly IDbContextFactory<MosaicDbContext> _contextFactory;
    private readonly AppPaths _paths;
    private readonly ISettingsService _settings;
    private readonly SteamGridDbClient _client;

    public ArtworkService(
        IDbContextFactory<MosaicDbContext> contextFactory,
        AppPaths paths,
        ISettingsService settings,
        SteamGridDbClient client)
    {
        _contextFactory = contextFactory;
        _paths = paths;
        _settings = settings;
        _client = client;
    }

    public event EventHandler<int>? ArtworkUpdated;

    public async Task FetchArtworkAsync(int gameId, bool refetch = false, CancellationToken cancellationToken = default)
    {
        var apiKey = _settings.Current.SteamGridDbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return; // Graceful degradation: no key, no auto-fetch.

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var game = await db.Games
            .Include(g => g.Artwork)
            .FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
        if (game is null)
            return;

        // On a normal fetch, skip kinds that already have art. On a refetch, redo every
        // kind except the user's manual overrides.
        var needed = Kinds
            .Where(k => refetch ? !HasManualOverride(game, k.Kind) : !HasUsableArtwork(game, k.Kind))
            .ToList();
        if (needed.Count == 0)
            return;

        var changed = false;

        // One game at a time hits SteamGridDB, so a batch add doesn't trip the rate limit.
        await FetchGate.WaitAsync(cancellationToken);
        try
        {
            var match = await ResolveGameMatchAsync(game, apiKey, cancellationToken);
            if (match is null)
                return; // No confident match: leave placeholders.

            // Adopt the matched title when the name was auto-derived from the path (the
            // executable or a folder name, e.g. a scan), but leave a name the user typed.
            // Not on refetch.
            if (!refetch && NameIsAutoDerived(game) && !string.IsNullOrWhiteSpace(match.Name)
                && !string.Equals(game.Name, match.Name, StringComparison.Ordinal))
            {
                game.Name = match.Name;
                changed = true;
            }

            foreach (var (kind, endpoint) in needed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var url = await _client.GetFirstAssetUrlAsync(endpoint, match.Id, apiKey, cancellationToken);
                if (url is null)
                    continue;

                var bytes = await _client.DownloadAsync(url, cancellationToken);
                if (bytes is null || bytes.Length == 0)
                    continue;

                var localPath = BuildCachePath(gameId, kind, url);
                await File.WriteAllBytesAsync(localPath, bytes, cancellationToken);

                UpsertArtworkRow(game, kind, localPath, isManual: false, sourceId: match.Id);
                changed = true;
            }
        }
        finally
        {
            FetchGate.Release();
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
            ArtworkUpdated?.Invoke(this, gameId);
        }
    }

    public async Task FetchMissingForAllAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.Current.SteamGridDbApiKey))
            return;

        List<int> ids;
        await using (var db = await _contextFactory.CreateDbContextAsync(cancellationToken))
            ids = await db.Games.Select(g => g.Id).ToListAsync(cancellationToken);

        // Sequential; each call also takes the throttle gate, so SteamGridDB isn't flooded.
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await FetchArtworkAsync(id, refetch: false, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Best effort per game; keep going.
            }
        }
    }

    /// <summary>
    /// True when the game's name looks auto-derived from its path — it matches the
    /// executable's file name or any folder in the path (e.g. a scan-folder name).
    /// A name the user typed that matches no path component returns false.
    /// </summary>
    internal static bool NameIsAutoDerived(Game game)
    {
        var normName = Normalize(game.Name ?? string.Empty);
        if (normName.Length == 0)
            return true;
        if (normName == Normalize(Path.GetFileNameWithoutExtension(game.ExecutablePath)))
            return true;

        var dir = Path.GetDirectoryName(game.ExecutablePath);
        for (var depth = 0; depth < 6 && !string.IsNullOrEmpty(dir); depth++)
        {
            var folder = Path.GetFileName(dir);
            if (!string.IsNullOrWhiteSpace(folder) && normName == Normalize(folder))
                return true;
            dir = Path.GetDirectoryName(dir);
        }
        return false;
    }

    /// <summary>
    /// Resolves the SteamGridDB game by trying name candidates in priority order
    /// (custom name, then install-folder names, then the executable name), collecting
    /// every result that is a close enough name match, and preferring the one that
    /// actually has cover art — SteamGridDB often has duplicate entries where only one
    /// carries artwork (e.g. two "Dispatch" entries, only the second has grids).
    /// </summary>
    private async Task<SgdbGame?> ResolveGameMatchAsync(Game game, string apiKey, CancellationToken ct)
    {
        var ranked = await RankCandidatesAsync(BuildSearchTerms(game), MatchThreshold, apiKey, ct);

        if (ranked.Count == 0)
        {
            // Last resort: split CamelCase/PascalCase names ("HatinTime" -> "Hatin Time")
            // and accept a looser match — the folder/executable names found nothing.
            ranked = await RankCandidatesAsync(CamelCaseTerms(game), RelaxedMatchThreshold, apiKey, ct);
        }

        if (ranked.Count == 0)
            return null;

        // Best name matches first; cap how many we probe for art.
        var ordered = ranked
            .OrderByDescending(c => c.Score)
            .Select(c => c.Game)
            .Take(8)
            .ToList();

        // Prefer the first match that actually has a cover (grid).
        foreach (var candidate in ordered)
        {
            ct.ThrowIfCancellationRequested();
            if (await _client.GetFirstAssetUrlAsync("grids", candidate.Id, apiKey, ct) is not null)
                return candidate;
        }
        return ordered[0]; // none have a grid: fall back to the best name match
    }

    private async Task<List<(SgdbGame Game, double Score)>> RankCandidatesAsync(
        IEnumerable<string> terms, double threshold, string apiKey, CancellationToken ct)
    {
        var ranked = new List<(SgdbGame, double)>();
        var seen = new HashSet<long>();

        foreach (var term in terms)
        {
            foreach (var r in await _client.SearchGamesAsync(term, apiKey, ct))
            {
                if (!seen.Add(r.Id))
                    continue;
                var score = Similarity(term, r.Name);
                if (score >= threshold)
                    ranked.Add((r, score));
            }
        }
        return ranked;
    }

    /// <summary>CamelCase/PascalCase-split variants of the normal search terms (only those that change).</summary>
    internal static List<string> CamelCaseTerms(Game game)
    {
        var result = new List<string>();
        foreach (var term in BuildSearchTerms(game))
        {
            var split = SplitCamelCase(term);
            if (split.Contains(' ')
                && !string.Equals(split, term, StringComparison.OrdinalIgnoreCase)
                && !result.Contains(split, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(split);
            }
        }
        return result;
    }

    internal static string SplitCamelCase(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;
        var spaced = Regex.Replace(s, @"(?<=[a-z0-9])(?=[A-Z])", " ");      // hatinTime -> hatin Time
        spaced = Regex.Replace(spaced, @"(?<=[A-Z])(?=[A-Z][a-z])", " ");   // HTMLParser -> HTML Parser
        spaced = Regex.Replace(spaced, @"(?<=[A-Za-z])(?=[0-9])", " ");      // Quake3 -> Quake 3
        return Regex.Replace(spaced, @"\s{2,}", " ").Trim();
    }

    /// <summary>Builds search terms in priority order from the game's name and folder path.</summary>
    internal static List<string> BuildSearchTerms(Game game)
    {
        var terms = new List<string>();
        var exeBase = Path.GetFileNameWithoutExtension(game.ExecutablePath);

        void Add(string? raw)
        {
            var c = CleanTerm(raw);
            if (c.Length >= 2 && !terms.Contains(c, StringComparer.OrdinalIgnoreCase))
                terms.Add(c);
        }

        // 1. A user-given name that differs from the executable's file name.
        var name = game.Name?.Trim();
        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, exeBase, StringComparison.OrdinalIgnoreCase))
            Add(name);

        // 2. Meaningful folder names walking up from the executable (closest first).
        var dir = Path.GetDirectoryName(game.ExecutablePath);
        for (var depth = 0; depth < 5 && !string.IsNullOrEmpty(dir); depth++)
        {
            var folder = Path.GetFileName(dir);
            if (!string.IsNullOrWhiteSpace(folder) && !GenericFolders.Contains(folder.Trim()))
                Add(folder);
            dir = Path.GetDirectoryName(dir);
        }

        // 3. Fall back to the (possibly exe-derived) name and the executable base name.
        Add(name);
        Add(exeBase);
        return terms;
    }

    private static string CleanTerm(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var s = Regex.Replace(raw.Trim(), @"[._\-]+", " ");
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }

    /// <summary>
    /// Similarity in [0,1] between a search <paramref name="term"/> and a candidate title.
    /// Blends how much of the term the candidate covers (recall) with overall token overlap
    /// (Jaccard), so a complete sequel title outranks the shorter base game it contains.
    /// Diacritics are ignored, e.g. "Ragnarök" matches "Ragnarok".
    /// </summary>
    internal static double Similarity(string term, string candidate)
    {
        var na = Normalize(term);
        var nb = Normalize(candidate);
        if (na.Length == 0 || nb.Length == 0)
            return 0;
        if (na == nb)
            return 1.0;

        var ta = na.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tb = nb.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (ta.Count == 0 || tb.Count == 0)
            return 0;

        var intersection = ta.Intersect(tb).Count();
        if (intersection == 0)
            return 0;

        var recall = (double)intersection / ta.Count;             // how much of the term the candidate covers
        var jaccard = (double)intersection / ta.Union(tb).Count(); // overall token overlap
        return 0.5 * recall + 0.5 * jaccard;
    }

    private static string Normalize(string s)
    {
        // Strip diacritics: decompose, drop combining marks (ö -> o), then keep [a-z0-9].
        var decomposed = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        var stripped = Regex.Replace(sb.ToString(), @"[^a-z0-9]+", " ");
        return Regex.Replace(stripped, @"\s{2,}", " ").Trim();
    }

    public async Task<string> SetManualOverrideAsync(int gameId, ArtworkKind kind, string sourceImagePath)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var game = await db.Games
            .Include(g => g.Artwork)
            .FirstOrDefaultAsync(g => g.Id == gameId)
            ?? throw new InvalidOperationException($"Game {gameId} not found.");

        var localPath = BuildCachePath(gameId, kind, sourceImagePath);
        File.Copy(sourceImagePath, localPath, overwrite: true);

        UpsertArtworkRow(game, kind, localPath, isManual: true, sourceId: null);
        await db.SaveChangesAsync();
        ArtworkUpdated?.Invoke(this, gameId);
        return localPath;
    }

    private static bool HasUsableArtwork(Game game, ArtworkKind kind)
    {
        var existing = game.Artwork.FirstOrDefault(a => a.Kind == kind);
        if (existing is null)
            return false;
        // Manual overrides are always kept; cached files are reused if still present.
        return existing.IsManualOverride || File.Exists(existing.LocalPath);
    }

    private static bool HasManualOverride(Game game, ArtworkKind kind) =>
        game.Artwork.FirstOrDefault(a => a.Kind == kind)?.IsManualOverride == true;

    private static void UpsertArtworkRow(Game game, ArtworkKind kind, string localPath, bool isManual, long? sourceId)
    {
        var existing = game.Artwork.FirstOrDefault(a => a.Kind == kind);
        if (existing is null)
        {
            game.Artwork.Add(new Artwork
            {
                GameId = game.Id,
                Kind = kind,
                LocalPath = localPath,
                SourceId = sourceId,
                IsManualOverride = isManual,
            });
        }
        else
        {
            existing.LocalPath = localPath;
            existing.SourceId = sourceId;
            existing.IsManualOverride = isManual;
        }
    }

    private string BuildCachePath(int gameId, ArtworkKind kind, string sourceUrlOrPath)
    {
        var ext = Path.GetExtension(new Uri(sourceUrlOrPath, UriKind.RelativeOrAbsolute).IsAbsoluteUri
            ? new Uri(sourceUrlOrPath).AbsolutePath
            : sourceUrlOrPath);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5)
            ext = ".png";
        return Path.Combine(_paths.ArtworkDirectory, $"{gameId}_{kind}{ext}".ToLowerInvariant());
    }
}
