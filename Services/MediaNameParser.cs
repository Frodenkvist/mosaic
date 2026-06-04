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
    private static readonly Regex SeasonFolder =
        new(@"^(?:season|series|s)[\s._\-]*(\d{1,2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EpisodeNumberInName =
        new(@"[Ee](?:p|pisode)?[\s._\-]*(\d{1,3})", RegexOptions.Compiled);
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

        // 2. A "Season N" ancestor folder + an episode number in the file name.
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
