using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

using Microsoft.Win32;

using Forms = System.Windows.Forms;

namespace Listly.Services;

/// <summary>
/// Opt-in snapshot of the Windows environment at launch, plus power (sleep/resume) events. Helps
/// explain why the first activation after a reboot or a wake-from-sleep can feel slow. Disabled by
/// default; the one-time snapshot and the rare power events are all off-loaded, so there is no cost on
/// any interactive path.
/// </summary>
public static class SystemLog
{
    private static readonly DebugChannel Channel = new("system.log", "M2_APEX system log");

    public static bool Enabled
    {
        get => Channel.Enabled;
        set => Channel.Enabled = value;
    }

    public static string Folder => Channel.Folder;
    public static string FilePath => Channel.FilePath;

    /// <summary>Records a one-time snapshot of the machine + OS + power + boot state at startup.</summary>
    public static void Snapshot()
    {
        if (!Enabled)
            return;

        try
        {
            string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
            TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var proc = Process.GetCurrentProcess();

            string power;
            try { power = Forms.SystemInformation.PowerStatus.PowerLineStatus.ToString(); }
            catch { power = "?"; }

            var lines = new[]
            {
                $"app version   : v{version}",
                $"OS            : {RuntimeInformation.OSDescription}",
                $"architecture  : OS {RuntimeInformation.OSArchitecture} / process {RuntimeInformation.ProcessArchitecture}",
                $".NET          : {RuntimeInformation.FrameworkDescription}",
                $"CPU cores     : {Environment.ProcessorCount}",
                $"system uptime : {uptime:hh\\:mm\\:ss} (time since Windows booted \u2014 small = launched right after boot)",
                $"process start : {proc.StartTime:yyyy-MM-dd HH:mm:ss}",
                $"power line    : {power}",
                $"working set   : {proc.WorkingSet64 / (1024 * 1024)} MB",
            };

            Channel.AppendAsync(string.Join("\n", lines));
        }
        catch
        {
            // Best effort.
        }
    }

    /// <summary>Starts listening for sleep/resume events (call once at startup).</summary>
    public static void HookPowerEvents() => SystemEvents.PowerModeChanged += OnPowerModeChanged;

    /// <summary>Stops listening for sleep/resume events (call at shutdown).</summary>
    public static void UnhookPowerEvents() => SystemEvents.PowerModeChanged -= OnPowerModeChanged;

    private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (!Enabled)
            return;

        // Resume is the interesting one: the first activation after wake can feel slow.
        Channel.AppendAsync($"[{DateTime.Now:HH:mm:ss}] power event: {e.Mode}");
    }
}
