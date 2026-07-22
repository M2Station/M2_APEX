using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Listly.Services;

/// <summary>
/// Centralized crash / diagnostics logging to <c>%AppData%\M2_APEX\crash.log</c>, plus a helper
/// that reveals the folder in File Explorer so logs are easy to grab for later debugging.
/// </summary>
public static class CrashLog
{
    /// <summary>Folder that holds the crash log (shared with the app's other data files).</summary>
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M2_APEX");

    /// <summary>Full path to the crash log file.</summary>
    public static string FilePath => Path.Combine(Folder, "crash.log");

    /// <summary>
    /// Appends an exception to the crash log with a timestamped header (app version + OS).
    /// Best effort: never throws.
    /// </summary>
    public static void Log(Exception? ex, string? source = null)
    {
        if (ex is null)
            return;

        try
        {
            Directory.CreateDirectory(Folder);

            string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
            string origin = string.IsNullOrEmpty(source) ? string.Empty : $" \u00B7 {source}";
            string header = $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss}  v{version}  {Environment.OSVersion}{origin} =====";

            File.AppendAllText(FilePath, $"{header}\n{ex}\n\n");
        }
        catch
        {
            // Nothing more we can do if even logging fails.
        }
    }

    /// <summary>Opens the crash-log folder in File Explorer, selecting the log file if it exists.</summary>
    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);

            var startInfo = File.Exists(FilePath)
                ? new ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"")
                : new ProcessStartInfo(Folder) { UseShellExecute = true };

            Process.Start(startInfo);
        }
        catch
        {
            // Best effort.
        }
    }
}
