using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace Listly.Models;

/// <summary>
/// A support-app definition — its label, default <c>{path}</c> arguments, where to look for its
/// executable, and its GitHub repo — loaded from the embedded <c>Assets/default-app-list.json</c>.
/// This catalog is shared by both M2_APEX (search quick picks) and M2 Commander (custom commands):
/// adding or tuning an app is done by editing that one file, so it can grow without touching code.
/// </summary>
public sealed class SupportApp
{
    public string Label { get; set; } = string.Empty;
    public string Arguments { get; set; } = "\"{path}\"";
    public List<string> ExeNames { get; set; } = new();
    public List<string> SearchDirs { get; set; } = new();

    /// <summary>
    /// GitHub <c>owner/name</c> this tool is published under (e.g. <c>M2Station/M2_LOG</c>), enabling the
    /// Settings screen's per-app <b>Check Update</b> / <b>Install</b> button. Empty for third-party tools
    /// with no known repository.
    /// </summary>
    public string Repo { get; set; } = string.Empty;

    /// <summary>
    /// True when this catalog entry is a first-party M2 app (identified by a non-empty <see cref="Repo"/>),
    /// so it seeds a default quick pick and shows the Install / Check Update action in Settings.
    /// </summary>
    [JsonIgnore]
    public bool IsApp => !string.IsNullOrWhiteSpace(Repo);

    private static List<SupportApp>? _catalog;

    /// <summary>The support-app catalog from <c>Assets/default-app-list.json</c> (cached, never null).</summary>
    public static IReadOnlyList<SupportApp> Catalog =>
        _catalog ??= EmbeddedCatalog.Load<SupportApp>("default-app-list.json");

    /// <summary>The catalog tool whose <see cref="Label"/> matches <paramref name="label"/>
    /// (case-insensitive), or null when there is none.</summary>
    public static SupportApp? ByLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;
        var name = label.Trim();
        return Catalog.FirstOrDefault(t => string.Equals(t.Label, name, StringComparison.OrdinalIgnoreCase));
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
