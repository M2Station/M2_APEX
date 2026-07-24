using System.IO;

namespace Listly.Services;

/// <summary>
/// A single opt-in debug log file under the shared DEBUG_LOG folder. Handles the enable flag, a
/// per-session header, and thread-safe appends. Best effort — never throws. Use <see cref="AppendAsync"/>
/// from UI / hook / hot paths so the caller never blocks on disk I/O; every call is a cheap no-op while
/// the channel is disabled.
/// </summary>
public sealed class DebugChannel
{
    private readonly string _fileName;
    private readonly string _title;
    private readonly object _gate = new();
    private bool _enabled;
    private bool _sessionStarted;

    public DebugChannel(string fileName, string title)
    {
        _fileName = fileName;
        _title = title;
    }

    /// <summary>When false, every write is a no-op. Re-enabling starts a fresh session header.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            lock (_gate)
            {
                if (value && !_enabled)
                    _sessionStarted = false;
                _enabled = value;
            }
        }
    }

    /// <summary>Folder that holds the debug logs (shared with the crash log).</summary>
    public string Folder => CrashLog.Folder;

    /// <summary>Full path to this channel's log file.</summary>
    public string FilePath => Path.Combine(Folder, _fileName);

    /// <summary>Appends a line on the calling thread. Use for low-frequency, order-sensitive writes.</summary>
    public void Append(string line)
    {
        if (!_enabled)
            return;

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Folder);

                if (!_sessionStarted)
                {
                    File.AppendAllText(FilePath,
                        $"\n===== {_title} \u00B7 {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n");
                    _sessionStarted = true;
                }

                File.AppendAllText(FilePath, line + "\n");
            }
        }
        catch
        {
            // Best effort; logging must never disrupt the app.
        }
    }

    /// <summary>Fire-and-forget append that never blocks the caller (safe on UI / hook / hot paths).</summary>
    public void AppendAsync(string line)
    {
        if (!_enabled)
            return;

        string captured = line;
        _ = Task.Run(() => Append(captured));
    }
}
