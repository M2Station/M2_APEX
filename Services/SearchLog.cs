namespace Listly.Services;

/// <summary>
/// Opt-in timing for each executed search (query, result count, elapsed). The search already runs on a
/// background thread behind a debounce and the write is off-loaded, so enabling this does not slow
/// typing. Disabled by default. Use it to spot slow queries and validate index responsiveness.
/// </summary>
public static class SearchLog
{
    private static readonly DebugChannel Channel = new("search.log", "M2_APEX search log");

    public static bool Enabled
    {
        get => Channel.Enabled;
        set => Channel.Enabled = value;
    }

    public static string Folder => Channel.Folder;
    public static string FilePath => Channel.FilePath;

    /// <summary>Best-effort clear of the search log file; the next write starts a fresh session header.</summary>
    public static void Clear() => Channel.Clear();

    /// <summary>Records one executed search. An empty <paramref name="query"/> = the initial (recent) list.</summary>
    public static void LogQuery(string query, int resultCount, double elapsedMs)
    {
        if (!Enabled)
            return;

        string q = string.IsNullOrEmpty(query) ? "<initial list>" : $"\"{query}\" (len {query.Length})";
        Channel.AppendAsync($"[{DateTime.Now:HH:mm:ss}] {q} \u2192 {resultCount} results in {elapsedMs:F1} ms");
    }
}
