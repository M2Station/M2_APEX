using System.IO;
using System.Linq;

namespace Listly.Services;

/// <summary>
/// Locates the PowerShell hosts installed on this machine. Windows always ships "Windows PowerShell"
/// (<c>powershell.exe</c>, v5.1); users may additionally install cross-platform "PowerShell 7+"
/// (<c>pwsh.exe</c>). Both can coexist, so a caller that needs a single one may have to let the user
/// pick between them (see the auto-detect flows in Settings quick-picks and M2 Commander's F11 editor).
/// </summary>
public static class PowerShellLocator
{
    /// <summary>A located PowerShell host: a display <see cref="Label"/> and its executable <see cref="Path"/>.</summary>
    public readonly record struct Option(string Label, string Path);

    /// <summary>
    /// Every PowerShell host found, PowerShell 7 first (preferred) then Windows PowerShell, de-duplicated
    /// by full path. The optional <paramref name="index"/> widens the pwsh.exe search to the app's file
    /// index when it is not in a known install folder.
    /// </summary>
    public static IReadOnlyList<Option> FindAll(FileIndexService? index = null)
    {
        var found = new List<Option>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfNew(string label, string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            string full;
            try { full = Path.GetFullPath(path); }
            catch { return; }

            if (seen.Add(full))
                found.Add(new Option(label, full));
        }

        // PowerShell 7+ (pwsh.exe) — known install roots first, then the file index as a fallback.
        foreach (var dir in Pwsh7Dirs())
            AddIfNew("PowerShell 7", TryCombine(dir, "pwsh.exe"));

        if (found.Count == 0 && index is not null)
            AddIfNew("PowerShell 7", ProgramDetector.FindInIndex(index, "pwsh"));

        // Windows PowerShell (powershell.exe) — always present under System32.
        AddIfNew("Windows PowerShell", TryCombine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe"));

        return found;
    }

    /// <summary>The single best PowerShell (PowerShell 7 when present, else Windows PowerShell), or null.</summary>
    public static Option? Best(FileIndexService? index = null)
    {
        var all = FindAll(index);
        return all.Count > 0 ? all[0] : null;
    }

    private static IEnumerable<string> Pwsh7Dirs()
    {
        string[] roots =
        {
            @"%ProgramFiles%\PowerShell\7",
            @"%ProgramFiles(x86)%\PowerShell\7",
            @"%ProgramW6432%\PowerShell\7",
            @"%LOCALAPPDATA%\Microsoft\PowerShell\7",
        };

        foreach (var root in roots)
        {
            string expanded;
            try { expanded = Environment.ExpandEnvironmentVariables(root); }
            catch { continue; }

            // Skip an unresolved variable (e.g. ProgramFiles(x86) on a pure-ARM64 SKU leaves the token).
            if (!string.IsNullOrWhiteSpace(expanded) && !expanded.Contains('%'))
                yield return expanded;
        }
    }

    private static string? TryCombine(string dir, string file)
    {
        try { return Path.Combine(dir, file); }
        catch { return null; }
    }
}
