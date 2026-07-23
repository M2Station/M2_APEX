using System.IO;

using Listly.Models;

namespace Listly.Services;

/// <summary>
/// Orchestrates a search across apps, files/folders, system commands and the web,
/// then ranks the combined results by fuzzy score, usage habits and result kind.
/// </summary>
public sealed class SearchEngine
{
    private readonly FileIndexService _files;
    private readonly AppIndexService _apps;
    private readonly CommandProvider _commands;
    private readonly UsageTracker _usage;
    private readonly AppSettings _settings;

    public SearchEngine(
        FileIndexService files,
        AppIndexService apps,
        CommandProvider commands,
        UsageTracker usage,
        AppSettings settings)
    {
        _files = files;
        _apps = apps;
        _commands = commands;
        _usage = usage;
        _settings = settings;
    }

    public List<SearchResult> Search(string rawQuery)
    {
        var query = (rawQuery ?? string.Empty).Trim();
        int max = _settings.MaxResults;

        if (query.Length == 0)
            return EmptyQueryResults(max);

        var queryLower = query.ToLowerInvariant();
        var locations = ResolvePriorityLocations();
        var candidates = new List<SearchResult>(64);

        // User-pinned quick picks that match the query pin to the top of the list.
        AddQuickLinkMatches(queryLower, candidates);

        // Direct path entry (e.g. the user pasted a full path).
        if (query.Length >= 3 && (File.Exists(query) || Directory.Exists(query)))
            candidates.Add(BuildPathResult(query));

        AddAppMatches(queryLower, candidates);
        AddCommandMatches(queryLower, candidates);

        // Apply location priority to the small app/command/quick-pick set here; file matches fold the
        // same boost into their score below so only the surviving top results are ever materialized.
        ApplyLocationPriority(candidates, locations);
        AddFileMatches(queryLower, candidates, max, locations);

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        var top = candidates.Count > max ? candidates.GetRange(0, max) : candidates;

        foreach (var result in top)
            result.MatchedIndices = FuzzyMatcher.GetMatchedIndices(queryLower, result.Title);

        top.Add(BuildWebResult(query));
        return top;
    }

    /// <summary>Score added per priority tier; the top location adds the most.</summary>
    private const double PriorityStep = 3.0;

    /// <summary>
    /// Boosts results that live under the user's configured priority locations so the order
    /// set in Settings (top = highest) floats those matches up the list. Additive, so a much
    /// stronger name match elsewhere can still surface.
    /// </summary>
    private void ApplyLocationPriority(List<SearchResult> candidates, string[] locations)
    {
        if (locations.Length == 0)
            return;

        foreach (var result in candidates)
        {
            int rank = PriorityRank(result.Path, locations);
            if (rank >= 0)
                result.Score += (locations.Length - rank) * PriorityStep;
        }
    }

    /// <summary>The additive priority boost for a path (0 when it is under no priority location).</summary>
    private static double PriorityBonus(string path, string[] locations)
    {
        if (locations.Length == 0)
            return 0;

        int rank = PriorityRank(path, locations);
        return rank >= 0 ? (locations.Length - rank) * PriorityStep : 0;
    }

    /// <summary>Resolves the configured entries (tokens and paths) into full directory paths.</summary>
    private string[] ResolvePriorityLocations()
    {
        var raw = _settings.PriorityLocations;
        if (raw is null || raw.Count == 0)
            return Array.Empty<string>();

        var resolved = new List<string>(raw.Count);
        foreach (var entry in raw)
        {
            var path = ResolveLocationEntry(entry);
            if (path is not null)
                resolved.Add(path);
        }

        return resolved.ToArray();
    }

    private static string? ResolveLocationEntry(string entry)
    {
        var text = entry?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;

        string? special = text.ToLowerInvariant() switch
        {
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "documents" or "my documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "pictures" or "my pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "music" or "my music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" or "my videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            "home" or "user" or "userprofile" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            _ => null
        };

        try
        {
            return Path.GetFullPath(special ?? Environment.ExpandEnvironmentVariables(text));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Index of the first (highest-priority) location the path lives under, or -1.</summary>
    private static int PriorityRank(string path, string[] locations)
    {
        if (string.IsNullOrEmpty(path))
            return -1;

        for (int i = 0; i < locations.Length; i++)
        {
            if (IsUnder(path, locations[i]))
                return i;
        }

        return -1;
    }

    private static bool IsUnder(string path, string baseDir)
    {
        if (path.Equals(baseDir, StringComparison.OrdinalIgnoreCase))
            return true;

        bool hasSep = baseDir.EndsWith(Path.DirectorySeparatorChar) ||
                      baseDir.EndsWith(Path.AltDirectorySeparatorChar);
        string prefix = hasSep ? baseDir : baseDir + Path.DirectorySeparatorChar;

        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private void AddAppMatches(string queryLower, List<SearchResult> candidates)
    {
        double kindBonus = _settings.ShowFilesFirst ? 0 : 2.0;

        foreach (var app in _apps.Apps)
        {
            double score = FuzzyMatcher.Score(queryLower, app.Name);
            if (double.IsNegativeInfinity(score))
                continue;

            candidates.Add(new SearchResult
            {
                Title = app.Name,
                Subtitle = "Application",
                Path = app.Path,
                Kind = ResultKind.Application,
                Glyph = IconGlyph.App,
                Score = score + kindBonus + _usage.GetBonus(app.Path)
            });
        }
    }

    private void AddCommandMatches(string queryLower, List<SearchResult> candidates)
    {
        foreach (var command in _commands.Commands)
        {
            double score = FuzzyMatcher.Score(queryLower, command.Title);
            if (double.IsNegativeInfinity(score))
                continue;

            candidates.Add(new SearchResult
            {
                Title = command.Title,
                Subtitle = command.Subtitle,
                Path = command.Path,
                Kind = ResultKind.Command,
                Glyph = command.Glyph,
                Activate = command.Activate,
                Score = score + 1.0 + _usage.GetBonus(command.Path)
            });
        }
    }

    private void AddFileMatches(string queryLower, List<SearchResult> candidates, int max, string[] locations)
    {
        var items = _files.Items;
        int n = items.Length;
        if (n == 0)
            return;

        int k = Math.Max(max * 3, 40);
        int procs = Math.Min(Environment.ProcessorCount, Math.Max(1, n / 2000 + 1));
        int chunk = (n + procs - 1) / procs;
        var partials = new List<(double Score, int Index)>[procs];

        Parallel.For(0, procs, p =>
        {
            int start = p * chunk;
            int end = Math.Min(start + chunk, n);
            var heap = new PriorityQueue<int, double>();

            for (int i = start; i < end; i++)
            {
                // Score against a slice of the path (no substring allocation) across the whole index.
                double s = FuzzyMatcher.Score(queryLower, items[i].NameSpan);
                if (double.IsNegativeInfinity(s))
                    continue;

                heap.Enqueue(i, s);
                if (heap.Count > k)
                    heap.Dequeue();
            }

            var list = new List<(double, int)>(heap.Count);
            while (heap.TryDequeue(out int idx, out double pr))
                list.Add((pr, idx));
            partials[p] = list;
        });

        double folderBonus = 0.5 + (_settings.ShowFilesFirst ? 2.0 : 0);
        double fileBonus = _settings.ShowFilesFirst ? 2.0 : 0;

        // Merge the per-partition fuzzy hits, fold in kind / usage / location bonuses, and keep only the
        // top `max` overall — so Subtitle (GetDirectoryName) and the name substring are built ~max times,
        // not for every one of the ~k*procs candidates.
        var winners = new PriorityQueue<int, double>();
        foreach (var partial in partials)
        {
            if (partial is null)
                continue;

            foreach (var (fuzzy, index) in partial)
            {
                var item = items[index];
                double score = fuzzy
                    + (item.IsDirectory ? folderBonus : fileBonus)
                    + _usage.GetBonus(item.Path)
                    + PriorityBonus(item.Path, locations);

                winners.Enqueue(index, score);
                if (winners.Count > max)
                    winners.Dequeue();
            }
        }

        while (winners.TryDequeue(out int index, out double score))
        {
            var item = items[index];
            candidates.Add(new SearchResult
            {
                Title = item.Name,
                Subtitle = Path.GetDirectoryName(item.Path) ?? item.Path,
                Path = item.Path,
                Kind = item.IsDirectory ? ResultKind.Folder : ResultKind.File,
                Glyph = IconGlyph.ForFile(item.Path, item.IsDirectory),
                Score = score
            });
        }
    }

    /// <summary>Score added to a matching quick pick so it pins to the very top of the list.</summary>
    private const double QuickLinkBonus = 1000.0;

    /// <summary>User-pinned quick picks that fuzzy-match the query, ranked above everything else.</summary>
    private void AddQuickLinkMatches(string queryLower, List<SearchResult> candidates)
    {
        var links = _settings.SearchLinks;
        if (links is null)
            return;

        foreach (var link in links)
        {
            if (string.IsNullOrWhiteSpace(link.Target))
                continue;

            string name = string.IsNullOrWhiteSpace(link.Name) ? link.Target : link.Name;
            double score = FuzzyMatcher.Score(queryLower, name);
            if (double.IsNegativeInfinity(score))
                continue;

            candidates.Add(BuildQuickLink(link, score + QuickLinkBonus));
        }
    }

    /// <summary>Every configured quick pick, used to seed the empty-query (initial) list.</summary>
    private IEnumerable<SearchResult> AllQuickLinks()
    {
        var links = _settings.SearchLinks;
        if (links is null)
            yield break;

        foreach (var link in links)
            if (!string.IsNullOrWhiteSpace(link.Target))
                yield return BuildQuickLink(link, 0);
    }

    private SearchResult BuildQuickLink(SearchLink link, double score)
    {
        string target = (link.Target ?? string.Empty).Trim();
        string title = string.IsNullOrWhiteSpace(link.Name) ? target : link.Name.Trim();
        string subtitle = string.IsNullOrWhiteSpace(link.Arguments)
            ? target
            : target + " " + link.Arguments.Trim();

        return new SearchResult
        {
            Title = title,
            Subtitle = subtitle,
            Path = target,
            Kind = ResultKind.QuickLink,
            Glyph = QuickLinkGlyph(target),
            Score = score,
            Link = link
        };
    }

    private static string QuickLinkGlyph(string target)
    {
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return IconGlyph.Web;

        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return IconGlyph.App;

        bool isDir = target.StartsWith(@"\\") || Directory.Exists(target) || !Path.HasExtension(target);
        return IconGlyph.ForFile(target, isDir);
    }

    private List<SearchResult> EmptyQueryResults(int max)
    {
        var results = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // User-pinned quick picks always sit at the very top.
        foreach (var link in AllQuickLinks())
        {
            results.Add(link);
            if (!string.IsNullOrEmpty(link.Path))
                seen.Add(link.Path);
        }

        // Then the recent / frequently launched items (skipping anything already pinned).
        foreach (var key in _usage.TopItems(max))
        {
            var resolved = ResolveKey(key);
            if (resolved is null || !seen.Add(resolved.Path))
                continue;

            results.Add(resolved);
        }

        return results;
    }

    private SearchResult? ResolveKey(string key)
    {
        if (key.StartsWith("command:", StringComparison.Ordinal))
        {
            var title = key["command:".Length..];
            var cmd = _commands.Commands.FirstOrDefault(c => c.Title == title);
            return cmd is null ? null : new SearchResult
            {
                Title = cmd.Title,
                Subtitle = cmd.Subtitle,
                Path = cmd.Path,
                Kind = ResultKind.Command,
                Glyph = cmd.Glyph,
                Activate = cmd.Activate
            };
        }

        if (File.Exists(key))
        {
            bool isApp = key.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                         key.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ||
                         key.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase);
            return BuildPathResult(key, isApp);
        }

        return Directory.Exists(key) ? BuildPathResult(key) : null;
    }

    private static SearchResult BuildPathResult(string path, bool asApp = false)
    {
        bool isDir = Directory.Exists(path);
        var name = asApp
            ? Path.GetFileNameWithoutExtension(path)
            : (Path.GetFileName(path) is { Length: > 0 } n ? n : path);

        return new SearchResult
        {
            Title = name,
            Subtitle = asApp ? "Application" : (Path.GetDirectoryName(path) ?? path),
            Path = path,
            Kind = asApp ? ResultKind.Application : isDir ? ResultKind.Folder : ResultKind.File,
            Glyph = asApp ? IconGlyph.App : IconGlyph.ForFile(path, isDir)
        };
    }

    private SearchResult BuildWebResult(string query)
    {
        bool looksLikeUrl = query.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            query.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        if (looksLikeUrl)
        {
            return new SearchResult
            {
                Title = query,
                Subtitle = "Open URL",
                Path = query,
                Kind = ResultKind.WebSearch,
                Glyph = IconGlyph.Web,
                Argument = query
            };
        }

        var url = string.Format(_settings.WebSearchUrl, Uri.EscapeDataString(query));
        return new SearchResult
        {
            Title = $"Search the web for \u201C{query}\u201D",
            Subtitle = "Web search",
            Path = url,
            Kind = ResultKind.WebSearch,
            Glyph = IconGlyph.Web,
            Argument = url
        };
    }
}
