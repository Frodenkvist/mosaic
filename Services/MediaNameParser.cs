using System.IO;
using System.Text.RegularExpressions;

namespace Mosaic.Services;

/// <summary>An episode parsed from a path: the show name plus its season and episode numbers.</summary>
public readonly record struct EpisodeInfo(string ShowName, int Season, int Episode);

/// <summary>A movie parsed from a name: a cleaned title and an optional release year.</summary>
public readonly record struct MovieInfo(string Title, int? Year);

/// <summary>
/// Heuristic, I/O-free parsing of media file/folder names. Recognizes the common episode
/// conventions (<c>SxxExx</c>, <c>NxNN</c>, and a <c>Season N</c> ancestor folder) and parses a
/// movie title + year, stripping the usual scene/quality tags. Anything not recognized as an
/// episode is treated as a movie (the confirmation step lets the user fix a misclassification).
/// </summary>
public static class MediaNameParser
{
    private static readonly Regex SxxExx =
        new(@"[Ss](\d{1,2})[\s._\-]*[Ee](\d{1,3})", RegexOptions.Compiled);
    private static readonly Regex NxNN =
        new(@"(?<!\d)(\d{1,2})[xX](\d{1,3})(?!\d)", RegexOptions.Compiled);
    // A season/arc ancestor folder: "Season 2", "S01", "Series 3", and the anime equivalents
    // "Arc 1", "Part 2", "Cour 1", "Chapter 4" — with an optional trailing title ("Arc 1 - Unwavering Resolve").
    private static readonly Regex SeasonFolder =
        new(@"^(?:season|series|saison|arc|part|cour|chapter|s)[\s._\-]*(\d{1,2})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // An "E05"/"Ep05"/"Episode 5" marker; the E must sit on a word boundary so it isn't matched
    // inside a word (e.g. the "e 100" in "The 100th").
    private static readonly Regex EpisodeNumberInName =
        new(@"\b[Ee](?:p|pisode)?[\s._\-]*(\d{1,3})", RegexOptions.Compiled);
    // An "absolute" / dash-delimited episode number, common for anime: "Show - 01 Title", "Show Ep01",
    // "[Grp] Show - 012 [1080p]", "Show #01". The number (1-3 digits, not a 4-digit year) must be
    // delimited by a dash, an E/Ep marker on a word boundary, or '#', so movie titles that merely end in
    // a number ("Apollo 13", "Toy Story 3") aren't mistaken for episodes.
    private static readonly Regex AbsoluteEpisode =
        new(@"(?:[-–—]\s*|(?<=[\s._\-])[Ee](?:p|pisode)?\.?\s*|#\s*)(\d{1,3})(?!\d)", RegexOptions.Compiled);
    // A leading episode number — the common "<episode> <title>" convention ("36 1.28 (January 28)",
    // "1 Rebirth"). Only when the name *starts* with a 1-3 digit number followed by a separator, so a
    // number that appears later in the title (a date, a part number) can't out-rank the real prefix.
    private static readonly Regex LeadingNumber =
        new(@"^(\d{1,3})(?=[\s._\-]|$)", RegexOptions.Compiled);
    private static readonly Regex TrailingNumber =
        new(@"(?<!\d)(\d{1,3})(?!\d)", RegexOptions.Compiled);
    private static readonly Regex YearRx =
        new(@"(?:^|[^\d])((?:19|20)\d{2})(?:[^\d]|$)", RegexOptions.Compiled);

    // Scene/quality tags: a movie title is cut at the first one of these.
    private static readonly Regex TagRx = new(
        @"[\s._\-]+(?:1080p|720p|2160p|480p|4k|bluray|blu-ray|web-?dl|web-?rip|hdrip|brrip|bdrip|dvdrip|x264|x265|h\.?264|h\.?265|hevc|xvid|aac|ac3|dts|remux|proper|repack|extended|unrated|imax|multi|dual)\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Parses an episode from a full file path, or null if it does not look like an episode.</summary>
    public static EpisodeInfo? TryParseEpisode(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var dir = Path.GetDirectoryName(filePath);

        // 1. SxxExx / NxNN embedded in the file name.
        foreach (var rx in new[] { SxxExx, NxNN })
        {
            var m = rx.Match(fileName);
            if (!m.Success)
                continue;
            var season = int.Parse(m.Groups[1].Value);
            var episode = int.Parse(m.Groups[2].Value);
            return new EpisodeInfo(ResolveShowName(fileName[..m.Index], dir), season, episode);
        }

        // 2. A "Season N" / "Arc N" ancestor folder + an episode number in the file name.
        var season2 = FindSeasonFolder(dir, out var showFolder);
        if (season2 is int s)
        {
            var episode = ParseEpisodeNumber(fileName);
            if (episode is int ep)
            {
                var show = !string.IsNullOrWhiteSpace(showFolder)
                    ? CleanTitle(showFolder!)
                    : ResolveShowName(string.Empty, dir);
                return new EpisodeInfo(show, s, ep);
            }
        }

        // 3. An "absolute" / dash-delimited episode number in the file name (common for anime, e.g.
        //    "Show - 01 Title"). The season comes from an Arc/Season ancestor folder when present, else 1.
        var am = AbsoluteEpisode.Match(fileName);
        if (am.Success)
        {
            var episode = int.Parse(am.Groups[1].Value);
            var season = FindSeasonFolder(dir, out _) ?? 1;
            return new EpisodeInfo(ResolveShowName(fileName[..am.Index], dir), season, episode);
        }

        return null;
    }

    /// <summary>Parses a movie title and optional year from a file or folder name.</summary>
    public static MovieInfo ParseMovie(string fileOrFolderName)
    {
        var name = Path.GetFileNameWithoutExtension(fileOrFolderName);

        int? year = null;
        var ym = YearRx.Match(name);
        string title;
        if (ym.Success)
        {
            year = int.Parse(ym.Groups[1].Value);
            title = name[..ym.Groups[1].Index]; // drop the year and everything after it
        }
        else
        {
            title = TagRx.Replace(name, string.Empty); // no year: cut at the first quality tag
        }

        return new MovieInfo(CleanTitle(title), year);
    }

    private static int? ParseEpisodeNumber(string fileName)
    {
        var m = EpisodeNumberInName.Match(fileName);
        if (m.Success)
            return int.Parse(m.Groups[1].Value);
        // A dash/'#'-delimited number ("Show - 05") before falling back to a bare leading/trailing number.
        var a = AbsoluteEpisode.Match(fileName);
        if (a.Success)
            return int.Parse(a.Groups[1].Value);
        // A leading number is the episode in the "<episode> <title>" convention — preferred over a
        // number embedded later in the title (e.g. "36 1.28 (January 28)" is episode 36, not 28).
        var lead = LeadingNumber.Match(fileName);
        if (lead.Success)
            return int.Parse(lead.Groups[1].Value);
        // Fall back to the last standalone number (we already know we're inside a Season folder).
        var matches = TrailingNumber.Matches(fileName);
        return matches.Count > 0 ? int.Parse(matches[^1].Groups[1].Value) : null;
    }

    /// <summary>
    /// Resolves the show name from the text preceding the episode marker; when that is empty,
    /// walks up the folders (skipping a "Season N" folder) to the show folder.
    /// </summary>
    private static string ResolveShowName(string prefix, string? dir)
    {
        var cleaned = CleanTitle(prefix);
        if (!string.IsNullOrWhiteSpace(cleaned))
            return cleaned;

        var cur = dir;
        for (var depth = 0; depth < 4 && cur is not null; depth++)
        {
            var name = FolderName(cur);
            if (string.IsNullOrWhiteSpace(name))
                break;
            if (SeasonFolder.IsMatch(name))
            {
                cur = Path.GetDirectoryName(cur);
                continue;
            }
            return CleanTitle(name);
        }
        return "Unknown";
    }

    private static int? FindSeasonFolder(string? dir, out string? showFolder)
    {
        showFolder = null;
        var cur = dir;
        for (var depth = 0; depth < 4 && cur is not null; depth++)
        {
            var m = SeasonFolder.Match(FolderName(cur));
            if (m.Success)
            {
                var parent = Path.GetDirectoryName(cur);
                showFolder = parent is not null ? FolderName(parent) : null;
                return int.Parse(m.Groups[1].Value);
            }
            cur = Path.GetDirectoryName(cur);
        }
        return null;
    }

    private static string FolderName(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>Turns a raw file/folder fragment into a display title (separators → spaces, tags removed).</summary>
    internal static string CleanTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var s = TagRx.Replace(raw, string.Empty);
        s = s.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        s = Regex.Replace(s, @"[\(\)\[\]\{\}]", " ");
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }
}
