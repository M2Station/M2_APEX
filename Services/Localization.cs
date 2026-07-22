using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Listly.Services;

/// <summary>
/// Lightweight localization backed by a single embedded JSON file (<c>Assets/Strings.json</c>),
/// grouped by language. Add a language by adding a top-level block; add a string by adding a key.
/// Missing strings fall back to English, then to the key itself, so the app always runs.
/// </summary>
public static class Loc
{
    // language id -> (key -> value)
    private static readonly Dictionary<string, Dictionary<string, string>> Data = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<(string Id, string Name)> LanguageList = new();
    private static string _current = "en";

    /// <summary>Raised after <see cref="Current"/> changes so code-set strings can refresh.</summary>
    public static event Action? LanguageChanged;

    /// <summary>Bindable proxy for XAML (see <c>Behaviors/LocExtension.cs</c>).</summary>
    public static StringTable Strings { get; } = new();

    static Loc() => Load();

    public static IReadOnlyList<(string Id, string Name)> Languages => LanguageList;

    /// <summary>Active language id (e.g. "en", "zh-TW").</summary>
    public static string Current
    {
        get => _current;
        set
        {
            string lang = Normalize(value);
            if (string.Equals(lang, _current, StringComparison.OrdinalIgnoreCase))
                return;

            _current = lang;
            Strings.RaiseChanged();
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>Resolves an id / culture name (e.g. "zh-TW", "zh", "") to an available language id.</summary>
    public static string Normalize(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            foreach (var (available, _) in LanguageList)
            {
                if (string.Equals(available, id, StringComparison.OrdinalIgnoreCase)
                    || available.StartsWith(id, StringComparison.OrdinalIgnoreCase)
                    || id.StartsWith(available, StringComparison.OrdinalIgnoreCase))
                    return available;
            }
        }

        return Data.ContainsKey("en") ? "en" : (LanguageList.Count > 0 ? LanguageList[0].Id : "en");
    }

    /// <summary>The best language id for the current OS UI culture (used when no preference is set).</summary>
    public static string SystemLanguage() => Normalize(CultureInfo.CurrentUICulture.Name);

    /// <summary>Localized string for <paramref name="key"/> in the current language.</summary>
    public static string T(string key)
    {
        if (Data.TryGetValue(_current, out var map) && map.TryGetValue(key, out var value))
            return value;
        if (Data.TryGetValue("en", out var en) && en.TryGetValue(key, out var fallback))
            return fallback;
        return key;
    }

    /// <summary>Localized, <see cref="string.Format(string, object[])"/>-formatted string.</summary>
    public static string T(string key, params object[] args)
    {
        string format = T(key);
        try { return string.Format(format, args); }
        catch (FormatException) { return format; }
    }

    private static void Load()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            string? resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("Strings.json", StringComparison.OrdinalIgnoreCase));
            if (resource is null)
                return;

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var doc = JsonDocument.Parse(stream);

            foreach (var language in doc.RootElement.EnumerateObject())
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                string name = language.Name;

                foreach (var entry in language.Value.EnumerateObject())
                {
                    if (entry.Name == "_name")
                    {
                        name = entry.Value.GetString() ?? language.Name;
                        continue;
                    }
                    map[entry.Name] = entry.Value.GetString() ?? string.Empty;
                }

                Data[language.Name] = map;
                LanguageList.Add((language.Name, name));
            }
        }
        catch
        {
            // On any failure, T() simply echoes keys — the app still runs.
        }
    }

    /// <summary>Indexer proxy that notifies WPF bindings when the language changes.</summary>
    public sealed class StringTable : INotifyPropertyChanged
    {
        public string this[string key] => T(key);

        public event PropertyChangedEventHandler? PropertyChanged;

        internal void RaiseChanged() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
