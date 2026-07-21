using System.Diagnostics;

using Listly.Models;
using Listly.Native;

namespace Listly.Services;

/// <summary>
/// Supplies built-in system commands (lock, sleep, shutdown, recycle bin, etc.)
/// that appear as search results, similar to Listary's Commands feature.
/// </summary>
public sealed class CommandProvider
{
    private readonly List<SearchResult> _commands;

    public CommandProvider()
    {
        _commands = new List<SearchResult>
        {
            Make("Lock", "Lock this PC", "\uE72E", () => NativeMethods.LockWorkStation()),
            Make("Sleep", "Put the PC to sleep", "\uE708",
                () => Shell("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0")),
            Make("Shut down", "Shut down the PC", "\uE7E8", () => Shell("shutdown", "/s /t 0")),
            Make("Restart", "Restart the PC", "\uE777", () => Shell("shutdown", "/r /t 0")),
            Make("Sign out", "Sign out of Windows", "\uE77B", () => Shell("shutdown", "/l")),
            Make("Recycle Bin", "Open the Recycle Bin", "\uE74D",
                () => Shell("explorer.exe", "shell:RecycleBinFolder")),
            Make("Empty Recycle Bin", "Permanently delete recycled items", "\uE74D",
                () => NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null,
                    NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI)),
            Make("Settings", "Open Windows Settings", "\uE713", () => Shell("cmd.exe", "/c start ms-settings:")),
            Make("Control Panel", "Open Control Panel", "\uE770", () => Shell("control.exe", "")),
            Make("Task Manager", "Open Task Manager", "\uE9D9", () => Shell("taskmgr.exe", "")),
        };
    }

    public IReadOnlyList<SearchResult> Commands => _commands;

    private static SearchResult Make(string title, string subtitle, string glyph, Action action) => new()
    {
        Title = title,
        Subtitle = subtitle,
        Path = "command:" + title,
        Kind = ResultKind.Command,
        Glyph = glyph,
        Activate = action
    };

    private static void Shell(string file, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
        }
        catch
        {
            // Ignore command failures.
        }
    }
}
