using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Listly.Services;

/// <summary>
/// Opt-in performance logging to <c>%AppData%\M2_APEX\DEBUG_LOG\performance.log</c>. Records how long
/// startup phases and each search activation take, so a slow first-launch after a reboot can be
/// diagnosed from real timings instead of guesswork. Disabled by default; every call is a cheap no-op
/// until <see cref="Enabled"/> is turned on (via the Settings → Debug tab). Best effort: never throws.
/// </summary>
public static class PerfLog
{
    private static readonly object _gate = new();

    // Wall-clock moment this process started, used as the zero point for every "+elapsed" stamp so the
    // first entry already reflects the cold-start cost (process launch → runtime ready → OnStartup).
    private static readonly DateTime ProcessStart = GetProcessStart();

    private static bool _enabled;
    private static bool _sessionStarted;

    /// <summary>When true, timings are appended to <see cref="FilePath"/>; when false, all calls are no-ops.</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            lock (_gate)
            {
                // Re-enabling starts a fresh session header on the next write.
                if (value && !_enabled)
                    _sessionStarted = false;
                _enabled = value;
            }
        }
    }

    /// <summary>Folder that holds the debug logs (shared with the crash log).</summary>
    public static string Folder => CrashLog.Folder;

    /// <summary>Full path to the performance log file.</summary>
    public static string FilePath => Path.Combine(Folder, "performance.log");

    /// <summary>A high-resolution timestamp for measuring a span; pair with <see cref="LogElapsed"/>.</summary>
    public static long StartTimestamp() => Stopwatch.GetTimestamp();

    /// <summary>Records a one-off milestone stamped with the elapsed time since process start.</summary>
    public static void Mark(string stage)
    {
        if (!_enabled)
            return;

        Write($"[+{SinceStart()}] {stage}");
    }

    /// <summary>Records how long an operation took, given the <paramref name="startTimestamp"/> from
    /// <see cref="StartTimestamp"/>.</summary>
    public static void LogElapsed(string label, long startTimestamp)
    {
        if (!_enabled)
            return;

        double ms = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        Write($"[+{SinceStart()}] {label}: {ms:F1} ms");
    }

    /// <summary>
    /// Empties the performance log. Runs under the same lock as <see cref="Write"/> so it never races a
    /// concurrent write, and re-arms the session header so the next write starts a fresh session.
    /// </summary>
    public static void Clear()
    {
        lock (_gate)
        {
            CrashLog.TryClearFile(FilePath);
            _sessionStarted = false;
        }
    }

    private static string SinceStart()
    {
        double seconds = Math.Max(0, (DateTime.Now - ProcessStart).TotalSeconds);
        return $"{seconds:00.000}s";
    }

    private static void Write(string line)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Folder);

                if (!_sessionStarted)
                {
                    string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
                    File.AppendAllText(FilePath,
                        $"\n===== M2_APEX performance log \u00B7 {DateTime.Now:yyyy-MM-dd HH:mm:ss} \u00B7 v{version} =====\n");
                    _sessionStarted = true;
                }

                File.AppendAllText(FilePath, line + "\n");
            }
        }
        catch
        {
            // Best effort; timing must never disrupt the app.
        }
    }

    private static DateTime GetProcessStart()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime;
        }
        catch
        {
            return DateTime.Now;
        }
    }
}
