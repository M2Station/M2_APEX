using System.IO;

namespace Listly.Services;

public readonly struct AppItem
{
    public readonly string Name;
    public readonly string Path;

    public AppItem(string name, string path)
    {
        Name = name;
        Path = path;
    }
}

/// <summary>
/// Discovers installed applications by scanning the common and per-user Start Menu
/// folders for shortcuts (.lnk / .url / .appref-ms).
/// </summary>
public sealed class AppIndexService
{
    private static readonly string[] Extensions = { ".lnk", ".url", ".appref-ms" };
    private volatile AppItem[] _apps = Array.Empty<AppItem>();

    public AppItem[] Apps => _apps;

    public Task BuildAsync() => Task.Run(Build);

    public void Build()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        };

        var byName = new Dictionary<string, AppItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", options);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (!Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    continue;

                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // Prefer the first occurrence; system entries tend to come first.
                if (!byName.ContainsKey(name))
                    byName[name] = new AppItem(name, file);
            }
        }

        _apps = byName.Values.ToArray();
    }
}
