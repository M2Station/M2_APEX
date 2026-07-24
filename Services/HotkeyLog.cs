namespace Listly.Services;

/// <summary>
/// Opt-in diagnostics for the global hotkey hook (double-Ctrl / Alt+Space). Records every double-Ctrl
/// decision together with the foreground app, so a gesture that silently fails to open the search bar —
/// a key pressed mid-hold, foreign/injected input, taps just over the threshold, or an elevated
/// foreground window (e.g. VS Code run as administrator) that starves a non-elevated hook — can be
/// diagnosed from real data instead of guesswork. Disabled by default; every call is a cheap no-op
/// until enabled from the Settings → Debug tab, and each write is off-loaded so the hook never blocks.
/// </summary>
public static class HotkeyLog
{
    private static readonly DebugChannel Channel = new("hotkey.log", "M2_APEX hotkey log");

    public static bool Enabled
    {
        get => Channel.Enabled;
        set => Channel.Enabled = value;
    }

    public static string Folder => Channel.Folder;
    public static string FilePath => Channel.FilePath;

    /// <summary>Appends one timestamped hotkey diagnostic line (no-op while disabled).</summary>
    public static void Log(string line)
    {
        if (!Enabled)
            return;

        Channel.AppendAsync($"[{DateTime.Now:HH:mm:ss}] {line}");
    }

    /// <summary>Best-effort clear of the hotkey log file; the next write starts a fresh session header.</summary>
    public static void Clear() => Channel.Clear();
}
