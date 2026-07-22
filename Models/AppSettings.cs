using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Listly.Models;

/// <summary>Vertical anchor for a pop-up bar (horizontal is always centred).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BarPosition
{
    Top,
    Center,
    Bottom
}

/// <summary>User-configurable settings, persisted as JSON in %AppData%\Listly.</summary>
public sealed class AppSettings
{
    public bool EnableDoubleCtrl { get; set; } = true;

    public bool EnableAltSpace { get; set; } = true;

    /// <summary>Type-to-jump inside Windows File Explorer (Listary's Quick Switch).</summary>
    public bool EnableQuickSwitch { get; set; } = true;

    /// <summary>
    /// Ignore keystrokes not meant for this PC: injected/synthetic input, and — when a KVM tool
    /// (Synergy / Barrier / Deskflow) is running — keys typed while the shared pointer has moved
    /// to another computer. Stops M2_APEX reacting to input meant for the other machine.
    /// </summary>
    public bool IgnoreForeignInput { get; set; } = true;

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

    /// <summary>
    /// Ordered list of locations that rank search results higher (earlier entries win).
    /// Each entry is a folder path (e.g. <c>C:\</c>, <c>D:\Projects</c>, <c>%USERPROFILE%\src</c>)
    /// or a well-known token (<c>Desktop</c>, <c>Documents</c>, <c>Downloads</c>,
    /// <c>Pictures</c>, <c>Music</c>, <c>Videos</c>, <c>Home</c>). Results living under a
    /// listed location float toward the top when the search bar is opened.
    /// </summary>
    public List<string> PriorityLocations { get; set; } = new() { "Desktop" };

    /// <summary>Active colour theme id (see <c>Listly.Services.ThemeManager</c>).</summary>
    public string Theme { get; set; } = "low_key";

    /// <summary>UI language id (e.g. "en", "zh-TW"); empty = follow the system UI language.</summary>
    public string Language { get; set; } = "";

    /// <summary>Where the main search bar (double-Ctrl / Alt+Space) appears on screen.</summary>
    public BarPosition SearchBarPosition { get; set; } = BarPosition.Top;

    /// <summary>Where the Quick Switch bar appears over the File Explorer window.</summary>
    public BarPosition QuickSwitchPosition { get; set; } = BarPosition.Top;

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
