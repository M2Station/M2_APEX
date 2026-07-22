using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Listly.Models;

/// <summary>
/// A launcher tool definition — its label, default <c>{path}</c> arguments, and where to look for
/// its executable — loaded from the embedded <c>Assets/commander-tools.json</c>. Adding or tuning a
/// tool (name, search folders, arguments) is done by editing that file, so it can grow over time
/// without touching code. Drives the F11 "Custom commands" seed and the Auto Detect button.
/// </summary>
public sealed class CommanderTool
{
    public string Label { get; set; } = string.Empty;
    public string Arguments { get; set; } = "\"{path}\"";
    public List<string> ExeNames { get; set; } = new();
    public List<string> SearchDirs { get; set; } = new();

    private static List<CommanderTool>? _catalog;

    /// <summary>The tool catalog from <c>Assets/commander-tools.json</c> (cached, never null).</summary>
    public static IReadOnlyList<CommanderTool> Catalog => _catalog ??= Load();

    private static List<CommanderTool> Load()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("commander-tools.json", StringComparison.OrdinalIgnoreCase));

            if (resource is not null)
            {
                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream is not null)
                {
                    var tools = JsonSerializer.Deserialize<List<CommanderTool>>(stream,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (tools is { Count: > 0 })
                        return tools;
                }
            }
        }
        catch
        {
            // Fall through to a minimal built-in default so the app still seeds something.
        }

        return new List<CommanderTool>
        {
            new() { Label = "M2_LOG" },
            new() { Label = "M2_ST4" },
            new() { Label = "Beyond Compare" },
            new() { Label = "VS Code" },
        };
    }

    /// <summary>
    /// Best-effort path to this tool's executable: probes each <see cref="SearchDirs"/> entry
    /// (environment variables expanded) for each <see cref="ExeNames"/> + ".exe". Null if none found.
    /// </summary>
    public string? DetectPath()
    {
        foreach (var dir in SearchDirs)
        {
            string expanded;
            try { expanded = Environment.ExpandEnvironmentVariables(dir); }
            catch { continue; }

            if (string.IsNullOrWhiteSpace(expanded) || !Directory.Exists(expanded))
                continue;

            foreach (var exe in ExeNames)
            {
                try
                {
                    var candidate = Path.Combine(expanded, exe + ".exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                    // Ignore a malformed name and keep probing.
                }
            }
        }

        return null;
    }
}
