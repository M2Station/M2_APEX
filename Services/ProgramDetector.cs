using System.IO;
using System.Linq;

using Listly.Models;

namespace Listly.Services;

/// <summary>
/// Resolves a catalog tool (<c>Assets/default-app-list.json</c>) to an installed executable path:
/// first the tool's known install folders, then the app's file index (by exe name, then label).
/// Shared by M2 Commander's F11 "Auto detect" and the Settings quick-picks "Auto detect".
/// </summary>
public static class ProgramDetector
{
    /// <summary>Detects <paramref name="tool"/>'s executable, or null when not found.</summary>
    public static string? Detect(SupportApp tool, FileIndexService index)
    {
        // 1) Direct probe of the tool's known install folders.
        if (tool.DetectPath() is { } direct)
            return direct;

        // 2) Fall back to the file index, matching each known exe name, then the label itself.
        foreach (var exe in tool.ExeNames)
            if (FindInIndex(index, exe) is { } hit)
                return hit;

        return FindInIndex(index, tool.Label);
    }

    /// <summary>
    /// Best executable in the file index whose base name matches <paramref name="name"/> (spaces,
    /// underscores and hyphens ignored). Prefers .exe over .bat/.cmd, then the shortest path.
    /// </summary>
    public static string? FindInIndex(FileIndexService index, string name)
    {
        var items = index.Items;
        if (items.Length == 0 || string.IsNullOrWhiteSpace(name))
            return null;

        string[] exts = { ".exe", ".bat", ".cmd" };
        string wanted = NormalizeName(name);

        string? best = null;
        int bestRank = int.MaxValue;
        foreach (var item in items)
        {
            if (item.IsDirectory)
                continue;

            int extRank = Array.IndexOf(exts, Path.GetExtension(item.Name).ToLowerInvariant());
            if (extRank < 0)
                continue;

            if (!NormalizeName(Path.GetFileNameWithoutExtension(item.Name)).Equals(wanted, StringComparison.Ordinal))
                continue;

            int rank = extRank * 100_000 + item.Path.Length;
            if (rank < bestRank)
            {
                bestRank = rank;
                best = item.Path;
            }
        }

        return best;
    }

    private static string NormalizeName(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
