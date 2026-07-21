using System.IO;

namespace Listly.Services;

/// <summary>A single indexed file or folder. A struct to keep the index compact.</summary>
public readonly struct IndexItem
{
    public readonly string Name;
    public readonly string Path;
    public readonly bool IsDirectory;

    public IndexItem(string name, string path, bool isDirectory)
    {
        Name = name;
        Path = path;
        IsDirectory = isDirectory;
    }
}

/// <summary>
/// Builds and maintains an in-memory index of files and folders across the
/// configured drives. Results are cached to disk so subsequent launches are instant.
/// </summary>
public sealed class FileIndexService
{
    private readonly Models.AppSettings _settings;
    private volatile IndexItem[] _items = Array.Empty<IndexItem>();

    public FileIndexService(Models.AppSettings settings)
    {
        _settings = settings;
    }

    public IndexItem[] Items => _items;

    public int Count => _items.Length;

    public bool IsIndexing { get; private set; }

    public event Action<string>? StatusChanged;

    private static string CacheDir =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M2_APEX");

    private static string CachePath => System.IO.Path.Combine(CacheDir, "index.cache");

    /// <summary>Loads a previously saved index from disk, if present.</summary>
    public void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return;

            var lines = File.ReadAllLines(CachePath);
            var list = new List<IndexItem>(lines.Length);
            foreach (var line in lines)
            {
                if (line.Length < 3)
                    continue;

                bool isDir = line[0] == '1';
                string path = line.Substring(2);
                list.Add(new IndexItem(System.IO.Path.GetFileName(path), path, isDir));
            }

            _items = list.ToArray();
            StatusChanged?.Invoke($"Loaded {_items.Length:N0} indexed items from cache");
        }
        catch
        {
            // Ignore cache read failures.
        }
    }

    /// <summary>Rebuilds the index on a background thread.</summary>
    public Task BuildAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Build(cancellationToken), cancellationToken);
    }

    private void Build(CancellationToken cancellationToken)
    {
        if (IsIndexing)
            return;

        IsIndexing = true;
        StatusChanged?.Invoke("Indexing…");

        try
        {
            var drives = ResolveDrives();
            var excluded = new HashSet<string>(_settings.ExcludedFolders, StringComparer.OrdinalIgnoreCase);
            var result = new List<IndexItem>(capacity: 1 << 16);

            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = _settings.IndexHiddenFiles
                    ? FileAttributes.System
                    : FileAttributes.System | FileAttributes.Hidden
            };

            var queue = new Queue<string>();
            foreach (var drive in drives)
                queue.Enqueue(drive);

            int lastReported = 0;

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dir = queue.Dequeue();

                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir, "*", options))
                    {
                        var name = System.IO.Path.GetFileName(sub);
                        if (excluded.Contains(name))
                            continue;

                        result.Add(new IndexItem(name, sub, true));
                        queue.Enqueue(sub);
                    }
                }
                catch
                {
                    // Skip folders we cannot enumerate.
                }

                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*", options))
                        result.Add(new IndexItem(System.IO.Path.GetFileName(file), file, false));
                }
                catch
                {
                    // Skip files we cannot enumerate.
                }

                if (result.Count - lastReported >= 20000)
                {
                    lastReported = result.Count;
                    StatusChanged?.Invoke($"Indexing… {result.Count:N0} items");
                }
            }

            _items = result.ToArray();
            SaveCache(_items);
            StatusChanged?.Invoke($"Indexed {_items.Length:N0} items");
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke("Indexing cancelled");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Indexing failed: {ex.Message}");
        }
        finally
        {
            IsIndexing = false;
        }
    }

    private IEnumerable<string> ResolveDrives()
    {
        if (_settings.IndexedDrives.Count > 0)
            return _settings.IndexedDrives.Where(Directory.Exists);

        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName);
    }

    private static void SaveCache(IndexItem[] items)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            using var writer = new StreamWriter(CachePath, append: false);
            foreach (var item in items)
            {
                writer.Write(item.IsDirectory ? '1' : '0');
                writer.Write('\t');
                writer.WriteLine(item.Path);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
