using System.Windows;
using System.Windows.Media;

using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Listly.Services;

/// <summary>
/// Multi-theme colour registry ported from M2_SCOUT (<c>src/renderer/js/themes.js</c>).
/// Each theme is a map of CSS-style variables; the ones the WPF UI needs are turned
/// into themed <see cref="SolidColorBrush"/> resources and merged into
/// <see cref="Application.Current"/>.Resources. Windows reference them via
/// <c>DynamicResource</c>, so the whole UI repaints live when the active theme changes.
/// </summary>
public static class ThemeManager
{
    public sealed record ThemeInfo(string Id, string Name);

    /// <summary>Matches M2_APEX's original dark look ("Low Key (Dark)").</summary>
    public const string DefaultTheme = "low_key";

    // Light base — mirrors the original M2_SEEK light look.
    private static readonly Dictionary<string, string> Daylight = new()
    {
        ["--bg"] = "#f4f4f4",
        ["--panel"] = "#ffffff",
        ["--border"] = "#cfcfcf",
        ["--text"] = "#1c1c1c",
        ["--muted"] = "#555555",
        ["--input-bg"] = "#ffffff",
        ["--btn-bg"] = "#f0f0f0",
        ["--btn-bg-hover"] = "#e6e6e6",
        ["--btn-border"] = "#b9b9b9",
        ["--btn-text"] = "#1c1c1c",
        ["--accent"] = "#227F9E",
        ["--row-hover"] = "#eef5fb",
        ["--row-selected"] = "#cfe8ff",
    };

    // Dark base — derived from M2_GIT_DIFF "Low Key" (M2_APEX's current palette).
    private static readonly Dictionary<string, string> LowKey = new()
    {
        ["--bg"] = "#0a0e14",
        ["--panel"] = "#121a28",
        ["--border"] = "#1e2a3a",
        ["--text"] = "#cfe3f2",
        ["--muted"] = "#8aa0b6",
        ["--input-bg"] = "#0e1622",
        ["--btn-bg"] = "#16202e",
        ["--btn-bg-hover"] = "#1e2a3a",
        ["--btn-border"] = "#2a3a4d",
        ["--btn-text"] = "#cfe3f2",
        ["--accent"] = "#36d6ff",
        ["--row-hover"] = "#15212f",
        ["--row-selected"] = "#1d3b57",
    };

    // Army — military tactical black + olive drab + burnt orange (overrides the dark base).
    private static readonly Dictionary<string, string> Army = Merge(LowKey, new()
    {
        ["--bg"] = "#0f1108",
        ["--panel"] = "#2a2f1f",
        ["--border"] = "#404530",
        ["--text"] = "#f5f4ed",
        ["--muted"] = "#a8a892",
        ["--input-bg"] = "#131609",
        ["--btn-bg"] = "#232815",
        ["--btn-bg-hover"] = "#3a4024",
        ["--btn-border"] = "#404530",
        ["--btn-text"] = "#f5f4ed",
        ["--accent"] = "#e8832a",
        ["--row-hover"] = "#1f251a",
        ["--row-selected"] = "#3a4226",
    });

    // Army (Dark) — steel/iron-grey base with military-green accents.
    private static readonly Dictionary<string, string> ArmyDark = Merge(LowKey, new()
    {
        ["--bg"] = "#1b1e21",
        ["--panel"] = "#26292d",
        ["--border"] = "#3a4047",
        ["--text"] = "#dfe2e5",
        ["--muted"] = "#9aa3ab",
        ["--input-bg"] = "#15181b",
        ["--btn-bg"] = "#23272b",
        ["--btn-bg-hover"] = "#2e3338",
        ["--btn-border"] = "#3a4047",
        ["--btn-text"] = "#dfe2e5",
        ["--accent"] = "#8a9a3d",
        ["--row-hover"] = "#262b22",
        ["--row-selected"] = "#3a4626",
    });

    // VS Code Dark+ — matches VS Code's default dark colour scheme.
    private static readonly Dictionary<string, string> VsCodeDark = Merge(LowKey, new()
    {
        ["--bg"] = "#1e1e1e",
        ["--panel"] = "#252526",
        ["--border"] = "#3c3c3c",
        ["--text"] = "#d4d4d4",
        ["--muted"] = "#858585",
        ["--input-bg"] = "#3c3c3c",
        ["--btn-bg"] = "#0e639c",
        ["--btn-bg-hover"] = "#1177bb",
        ["--btn-border"] = "#0e639c",
        ["--btn-text"] = "#ffffff",
        ["--accent"] = "#007acc",
        ["--row-hover"] = "#2a2d2e",
        ["--row-selected"] = "#094771",
    });

    private static readonly List<(string Id, string Name, Dictionary<string, string> Vars)> Registry = new()
    {
        ("daylight", "Daylight (Light)", Daylight),
        ("low_key", "Low Key (Dark)", LowKey),
        ("vscode_dark", "VS Code (Dark)", VsCodeDark),
        ("army", "Army", Army),
        ("army_dark", "Army (Dark)", ArmyDark),
    };

    /// <summary>The selectable themes, in display order.</summary>
    public static IReadOnlyList<ThemeInfo> Themes =>
        Registry.Select(t => new ThemeInfo(t.Id, t.Name)).ToList();

    /// <summary>Returns <paramref name="id"/> if it is a known theme, otherwise the default.</summary>
    public static string Normalize(string? id) =>
        id is not null && Registry.Any(t => t.Id == id) ? id : DefaultTheme;

    private static ResourceDictionary? _applied;

    /// <summary>Applies a theme by name, repainting every window that uses the themed brushes.</summary>
    public static void Apply(string? id)
    {
        var themeId = Normalize(id);
        var vars = Registry.First(t => t.Id == themeId).Vars;
        var dictionary = Build(vars);

        var resources = Application.Current.Resources;
        if (_applied is not null)
            resources.MergedDictionaries.Remove(_applied);
        resources.MergedDictionaries.Add(dictionary);
        _applied = dictionary;
    }

    private static ResourceDictionary Build(IReadOnlyDictionary<string, string> vars)
    {
        var dictionary = new ResourceDictionary();

        void Add(string key, string var, byte? alpha = null)
        {
            var color = Parse(vars[var]);
            if (alpha is byte a)
                color = Color.FromArgb(a, color.R, color.G, color.B);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            dictionary[key] = brush;
        }

        Add("ThemeSolidBackgroundBrush", "--bg");
        Add("ThemePanelBrush", "--panel");
        Add("ThemeCardBrush", "--panel", 0xF5);
        Add("ThemeBorderBrush", "--btn-border");
        Add("ThemeSeparatorBrush", "--border");
        Add("ThemeTextBrush", "--text");
        Add("ThemeSubtleBrush", "--muted");
        Add("ThemeAccentBrush", "--accent");
        Add("ThemeSelectedBrush", "--row-selected");
        Add("ThemeHoverBrush", "--row-hover");
        Add("ThemeInputBrush", "--input-bg");
        Add("ThemeButtonBrush", "--btn-bg");
        Add("ThemeButtonHoverBrush", "--btn-bg-hover");
        Add("ThemeButtonTextBrush", "--btn-text");

        return dictionary;
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private static Dictionary<string, string> Merge(
        Dictionary<string, string> baseVars, Dictionary<string, string> overrides)
    {
        var merged = new Dictionary<string, string>(baseVars);
        foreach (var pair in overrides)
            merged[pair.Key] = pair.Value;
        return merged;
    }
}
