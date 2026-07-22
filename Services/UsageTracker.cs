using System.IO;
using System.Text.Json;

namespace Listly.Services;

/// <summary>
/// Records how often and how recently items are launched so the search engine can
/// prioritise results by the user's habits (like Listary's "smart order").
/// </summary>
public sealed class UsageTracker
{
    private sealed class Entry
    {
        public int Count { get; set; }
        public long LastUsedUtcTicks { get; set; }
    }

    private readonly Dictionary<string, Entry> _entries;
    private readonly object _gate = new();

    private static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M2_APEX");

    private static string StorePath => Path.Combine(ConfigDir, "usage.json");

    /// <summary>Deletes the usage-history store so ranking starts fresh (factory reset).</summary>
    public static void DeleteStore()
    {
        try
        {
            if (File.Exists(StorePath))
                File.Delete(StorePath);
        }
        catch
        {
            // Best effort.
        }
    }

    public UsageTracker()
    {
        _entries = Load();
    }

    private static Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var json = File.ReadAllText(StorePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json);
                if (data is not null)
                    return new Dictionary<string, Entry>(data, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Ignore and start fresh.
        }

        return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    }

    public void RecordLaunch(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.Count++;
            entry.LastUsedUtcTicks = DateTime.UtcNow.Ticks;
        }

        Save();
    }

    /// <summary>Returns a ranking bonus based on launch frequency and recency.</summary>
    public double GetBonus(string key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return 0;

            double frequency = Math.Log(entry.Count + 1) * 2.5;

            var age = DateTime.UtcNow - new DateTime(entry.LastUsedUtcTicks, DateTimeKind.Utc);
            double recency = age.TotalDays switch
            {
                < 1 => 3.0,
                < 7 => 2.0,
                < 30 => 1.0,
                _ => 0.2
            };

            return frequency + recency;
        }
    }

    /// <summary>Items ordered by score, used to populate the list when the query is empty.</summary>
    public IEnumerable<string> TopItems(int count)
    {
        lock (_gate)
        {
            return _entries
                .OrderByDescending(kv => GetBonusUnlocked(kv.Value))
                .Take(count)
                .Select(kv => kv.Key)
                .ToList();
        }
    }

    private static double GetBonusUnlocked(Entry entry)
    {
        double frequency = Math.Log(entry.Count + 1) * 2.5;
        var age = DateTime.UtcNow - new DateTime(entry.LastUsedUtcTicks, DateTimeKind.Utc);
        double recency = age.TotalDays < 7 ? 2.0 : age.TotalDays < 30 ? 1.0 : 0.2;
        return frequency + recency;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            Dictionary<string, Entry> snapshot;
            lock (_gate)
            {
                snapshot = new Dictionary<string, Entry>(_entries);
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            // Best effort.
        }
    }
}
