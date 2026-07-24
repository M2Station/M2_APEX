namespace Listly.Services;

/// <summary>
/// Opt-in per-drive index scan timing. Records how long scanning each drive took and how many items it
/// contributed during an index (re)build, so a slow drive (network share, spinning disk, huge tree) can
/// be spotted. Written only while the index rebuilds, off the UI thread, so it has no effect on
/// interactive performance. Disabled by default.
/// </summary>
public static class IndexLog
{
    private static readonly DebugChannel Channel = new("index.log", "M2_APEX index log");

    public static bool Enabled
    {
        get => Channel.Enabled;
        set => Channel.Enabled = value;
    }

    public static string Folder => Channel.Folder;
    public static string FilePath => Channel.FilePath;

    /// <summary>Records that the index was served from the on-disk cache — the fast path, no rebuild.</summary>
    public static void LogCacheLoaded(int itemCount, double elapsedMs)
    {
        if (!Enabled)
            return;

        Channel.AppendAsync($"[{DateTime.Now:HH:mm:ss}] CACHE LOADED (fast path) — {itemCount:N0} items in {elapsedMs:F0} ms; no rebuild");
    }

    /// <summary>Records that no usable cache was found, so a full rebuild will run — the slow path.</summary>
    public static void LogNoCache()
    {
        if (!Enabled)
            return;

        Channel.AppendAsync($"[{DateTime.Now:HH:mm:ss}] NO CACHE — full rebuild needed (slow path)");
    }

    /// <summary>Records one drive's total scan time and item count during a rebuild.</summary>
    public static void LogDrive(string drive, int itemCount, double elapsedMs)
    {
        if (!Enabled)
            return;

        Channel.AppendAsync($"[{DateTime.Now:HH:mm:ss}]   rebuild drive {drive,-6} {itemCount,10:N0} items in {elapsedMs,8:F0} ms");
    }

    /// <summary>Records the combined total for a rebuild across all drives — the slow path.</summary>
    public static void LogRebuildTotal(int itemCount, double elapsedMs)
    {
        if (!Enabled)
            return;

        Channel.AppendAsync($"[{DateTime.Now:HH:mm:ss}] REBUILD DONE (slow path) — {itemCount:N0} items in {elapsedMs:F0} ms");
    }
}
