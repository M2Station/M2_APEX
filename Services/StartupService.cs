using System.Diagnostics;
using System.Security.Principal;

using Microsoft.Win32;

namespace Listly.Services;

/// <summary>
/// Manages "launch at Windows startup". A non-elevated build registers a per-user <c>Run</c> key entry.
/// An elevated build (option B — so the global hotkey works over administrator windows) instead registers
/// a Task Scheduler logon task with highest privileges, because Windows will not auto-launch an
/// elevation-requiring app from the <c>Run</c> key. Both are best effort and never throw.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "M2_APEX";
    private const string TaskName = "M2_APEX";

    /// <summary>True when startup is registered by either mechanism (scheduled task or Run key).</summary>
    public static bool IsEnabled() => TaskExists() || RunKeyExists();

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            // An elevated app must auto-start via a highest-privileges logon task; the Run key cannot
            // launch it elevated. Fall back to the Run key only if the task could not be created.
            if (ProcessLauncher.IsElevated && CreateTask())
                SetRunKey(false);
            else
                SetRunKey(true);
        }
        else
        {
            DeleteTask();
            SetRunKey(false);
        }
    }

    /// <summary>
    /// Upgrades a legacy Run-key autostart to a scheduled task when running elevated, so existing users
    /// who enabled "launch at startup" before this build keep it working. Call once at startup.
    /// </summary>
    public static void SyncElevatedStartup()
    {
        try
        {
            if (ProcessLauncher.IsElevated && RunKeyExists() && !TaskExists() && CreateTask())
                SetRunKey(false);
        }
        catch
        {
            // Best effort; a failed migration just leaves the (non-functional) Run key in place.
        }
    }

    /// <summary>The executable path registered to launch at startup (quotes / args stripped), or null.</summary>
    public static string? GetRegisteredPath()
    {
        // When the scheduled task drives startup it points at this build, so report the current exe.
        if (ProcessLauncher.IsElevated && TaskExists())
            return Environment.ProcessPath;

        var cmd = GetRegisteredCommand()?.Trim();
        if (string.IsNullOrEmpty(cmd))
            return null;

        if (cmd[0] == '"')
        {
            int end = cmd.IndexOf('"', 1);
            return end > 1 ? cmd.Substring(1, end - 1) : cmd.Trim('"');
        }

        int space = cmd.IndexOf(' ');
        return space > 0 ? cmd.Substring(0, space) : cmd;
    }

    /// <summary>The raw command registered under the Run key (usually a quoted exe path), or null.</summary>
    public static string? GetRegisteredCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Removes the startup registration (same as disabling it).</summary>
    public static void Unregister() => SetEnabled(false);

    // --- Run key (per-user, non-elevated) ---------------------------------------------------------

    private static bool RunKeyExists()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetRunKey(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
                return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best effort; ignore registry failures.
        }
    }

    // --- Scheduled task (elevated, highest privileges, at logon) -----------------------------------

    private static bool TaskExists() => RunSchtasks("/Query", "/TN", TaskName);

    private static bool CreateTask()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;

        string user;
        try { user = WindowsIdentity.GetCurrent().Name; }
        catch { return false; }

        // ONLOGON + /RL HIGHEST launches the app elevated at this user's logon without a UAC prompt.
        return RunSchtasks(
            "/Create", "/TN", TaskName,
            "/TR", $"\"{exe}\"",
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/RU", user,
            "/F");
    }

    private static bool DeleteTask()
    {
        if (!TaskExists())
            return true;

        return RunSchtasks("/Delete", "/TN", TaskName, "/F");
    }

    private static bool RunSchtasks(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc is null)
                return false;

            // Drain output (small) before waiting so the pipe can never block the child.
            _ = proc.StandardOutput.ReadToEnd();
            _ = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10000);

            return proc.HasExited && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
