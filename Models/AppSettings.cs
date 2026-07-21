using System.IO;
using System.Text.Json;

namespace Listly.Models;

/// <summary>User-configurable settings, persisted as JSON in %AppData%\Listly.</summary>
public sealed class AppSettings
{
    public bool EnableDoubleCtrl { get; set; } = true;

    public bool EnableAltSpace { get; set; } = true;

    /// <summary>Type-to-jump inside Windows File Explorer (Listary's Quick Switch).</summary>
    public bool EnableQuickSwitch { get; set; } = true;

    /// <summary>Max milliseconds between the two Ctrl taps to trigger the search bar.</summary>
    public int DoubleCtrlThresholdMs { get; set; } = 400;

    public int MaxResults { get; set; } = 12;

    /// <summary>Drives to index. Empty means "all fixed drives".</summary>
    public List<string> IndexedDrives { get; set; } = new();

    /// <summary>Folder names skipped while indexing (case-insensitive).</summary>
    public List<string> ExcludedFolders { get; set; } = new()
    {
        "$Recycle.Bin", "System Volume Information", "Windows",
        "node_modules", ".git", "obj", "bin", "AppData"
    };

    public bool IndexHiddenFiles { get; set; }

    public string WebSearchUrl { get; set; } = "https://www.google.com/search?q={0}";

    public bool LaunchAtStartup { get; set; }

    public bool ShowFilesFirst { get; set; }

    /// <summary>Active colour theme id (see <c>Listly.Services.ThemeManager</c>).</summary>
    public string Theme { get; set; } = "low_key";

    private static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M2_APEX");

    private static string ConfigPath => Path.Combine(ConfigDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                    return loaded;
            }
        }
        catch
        {
            // Corrupt or unreadable config: fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Best effort; ignore write failures.
        }
    }
}
