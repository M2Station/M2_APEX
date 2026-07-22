using Microsoft.Win32;

namespace Listly.Services;

/// <summary>Manages the "launch at Windows startup" registry entry (per-user).</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "M2_APEX";

    public static bool IsEnabled()
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

    public static void SetEnabled(bool enabled)
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

    /// <summary>The executable path registered to launch at startup (quotes / args stripped), or null.</summary>
    public static string? GetRegisteredPath()
    {
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

    /// <summary>Removes the startup registration (same as disabling it).</summary>
    public static void Unregister() => SetEnabled(false);
}
