using System.ComponentModel;
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
    Bottom,
    TopLeft,
    BottomRight
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
    public BarPosition QuickSwitchPosition { get; set; } = BarPosition.BottomRight;

    /// <summary>
    /// Custom launcher entries shown in M2_Commander: as buttons below the file grid, inside the
    /// F1 / right-click action menu, and editable via F11. Each entry runs an external program with
    /// the selected file/folder path substituted for the <c>{path}</c> token in
    /// <see cref="CommanderCommand.Arguments"/>. Seeded with M2_LOG / M2_ST4 / VS Code examples;
    /// an explicit empty list in the JSON is respected (not re-seeded).
    /// </summary>
    public List<CommanderCommand> CommanderCommands { get; set; } = CommanderCommand.DefaultSeed();

    /// <summary>
    /// When true, activating M2_Commander switches the keyboard layout to English (en-US) so its
    /// type-to-filter accepts Latin letters right away. Toggle in the F11 settings screen.
    /// </summary>
    public bool CommanderForceEnglishInput { get; set; } = true;

    /// <summary>Number of panes M2_Commander opens with (2-4).</summary>
    public int CommanderPaneCount { get; set; } = 2;

    /// <summary>Each pane's last folder, in order; restored on the next open.</summary>
    public List<string> CommanderPanePaths { get; set; } = new();

    /// <summary>Remembered M2_Commander window bounds (null = a screen-relative default is used).</summary>
    public double? CommanderWindowLeft { get; set; }
    public double? CommanderWindowTop { get; set; }
    public double? CommanderWindowWidth { get; set; }
    public double? CommanderWindowHeight { get; set; }

    /// <summary>User-defined quick links shown on the M2_Commander drives ("This PC") view.</summary>
    public List<CommanderLink> CommanderLinks { get; set; } = CommanderLink.DefaultSeed();

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
                {
                    loaded.UpgradeLegacyCommands();
                    return loaded;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable config: fall back to defaults.
        }

        return new AppSettings();
    }

    /// <summary>
    /// One-time in-memory upgrade of seeded commands still on a superseded default. Currently a
    /// "Beyond Compare" entry left on the single-path <c>{path}</c> default is switched to compare
    /// the two panes (<c>{left}</c> vs <c>{right}</c>). User-customised arguments are left untouched.
    /// </summary>
    private void UpgradeLegacyCommands()
    {
        if (CommanderCommands is null)
            return;

        foreach (var cmd in CommanderCommands)
        {
            if (string.Equals(cmd.Label?.Trim(), "Beyond Compare", StringComparison.OrdinalIgnoreCase)
                && string.Equals(cmd.Arguments?.Trim(), "\"{path}\"", StringComparison.Ordinal))
            {
                cmd.Arguments = "\"{left}\" \"{right}\"";
            }
        }
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

    /// <summary>Deletes the persisted settings file so the next load returns factory defaults.</summary>
    public static void DeleteSavedFile()
    {
        try
        {
            if (File.Exists(ConfigPath))
                File.Delete(ConfigPath);
        }
        catch
        {
            // Best effort.
        }
    }
}

/// <summary>
/// A user-defined launcher entry for M2_Commander (F11 settings). Runs <see cref="Path"/> with
/// <see cref="Arguments"/>, where the token <c>{path}</c> is replaced by the selected file/folder
/// (or the current folder when nothing is selected). Implements change notification so the F11
/// editor's text boxes and the "Browse…" button stay in sync.
/// </summary>
public sealed class CommanderCommand : INotifyPropertyChanged
{
    private string _label = string.Empty;
    private string _path = string.Empty;
    private string _arguments = "\"{path}\"";

    /// <summary>Text shown on the button / menu item.</summary>
    public string Label
    {
        get => _label;
        set { if (_label != value) { _label = value; OnChanged(nameof(Label)); } }
    }

    /// <summary>Full path to the executable to launch (blank = entry is hidden).</summary>
    public string Path
    {
        get => _path;
        set { if (_path != value) { _path = value; OnChanged(nameof(Path)); } }
    }

    /// <summary>Command-line arguments; <c>{path}</c> is replaced with the target file/folder.</summary>
    public string Arguments
    {
        get => _arguments;
        set { if (_arguments != value) { _arguments = value; OnChanged(nameof(Arguments)); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// The example launcher entries created on first run, built from the tool catalog
    /// (<c>Assets/commander-tools.json</c>) with each tool's executable auto-detected where possible.
    /// </summary>
    public static List<CommanderCommand> DefaultSeed() =>
        CommanderTool.Catalog.Select(t => new CommanderCommand
        {
            Label = t.Label,
            Path = t.DetectPath() ?? string.Empty,
            Arguments = string.IsNullOrEmpty(t.Arguments) ? "\"{path}\"" : t.Arguments
        }).ToList();
}

/// <summary>
/// A user-defined quick link shown on M2_Commander's drives ("This PC") view. <see cref="Target"/>
/// may be a folder / UNC path (browsed in-pane or opened in Explorer) or a URL (opened in the browser).
/// </summary>
public sealed class CommanderLink : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _target = string.Empty;

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnChanged(nameof(Name)); } }
    }

    /// <summary>A folder / UNC path or a URL. URLs (http://, https://, …) open in the browser.</summary>
    public string Target
    {
        get => _target;
        set { if (_target != value) { _target = value; OnChanged(nameof(Target)); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>The default quick links created on first run.</summary>
    public static List<CommanderLink> DefaultSeed() => new()
    {
        new CommanderLink { Name = "M2_STATION", Target = @"\\192.168.100.168" },
    };
}
