using System.Diagnostics;
using System.IO;
using System.Windows;

using Listly.Models;

using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace Listly.Services;

/// <summary>Executes the action associated with a search result.</summary>
public sealed class LaunchService
{
    private readonly UsageTracker _usage;

    public LaunchService(UsageTracker usage)
    {
        _usage = usage;
    }

    public void Launch(SearchResult result, bool asAdmin = false)
    {
        switch (result.Kind)
        {
            case ResultKind.Command:
                _usage.RecordLaunch(result.Path);
                result.Activate?.Invoke();
                break;

            case ResultKind.WebSearch:
                OpenUrl(result.Argument ?? result.Path);
                break;

            default:
                _usage.RecordLaunch(result.Path);
                Open(result.Path, asAdmin);
                break;
        }
    }

    /// <summary>
    /// Runs a user-defined quick pick: opens <see cref="SearchLink.Target"/> (app / folder / UNC / URL),
    /// optionally with <see cref="SearchLink.Arguments"/>. The token <c>{path}</c> in either field is
    /// replaced with <paramref name="contextPath"/> — the folder that was focused when search opened.
    /// </summary>
    public void LaunchQuickLink(SearchLink link, string? contextPath)
    {
        string ctx = contextPath ?? string.Empty;
        string target = (link.Target ?? string.Empty).Replace("{path}", ctx).Trim();
        if (target.Length == 0)
            return;

        string args = (link.Arguments ?? string.Empty).Replace("{path}", ctx).Trim();

        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (args.Length > 0)
                psi.Arguments = args;

            ProcessLauncher.Start(psi);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    public void OpenContainingFolder(SearchResult result)
    {
        if (result.Kind is ResultKind.WebSearch or ResultKind.Command)
            return;

        try
        {
            if (File.Exists(result.Path))
            {
                ProcessLauncher.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.Path}\"")
                {
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(result.Path))
            {
                ProcessLauncher.Start(new ProcessStartInfo("explorer.exe", $"\"{result.Path}\"")
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    public void CopyPath(SearchResult result)
    {
        try
        {
            Clipboard.SetText(result.Path);
        }
        catch
        {
            // Clipboard can transiently fail; ignore.
        }
    }

    private static void Open(string path, bool asAdmin)
    {
        try
        {
            var psi = new ProcessStartInfo(path) { UseShellExecute = true };
            if (asAdmin)
                psi.Verb = "runas";

            ProcessLauncher.Start(psi);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            ProcessLauncher.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private static void ShowError(Exception ex)
    {
        MessageBox.Show(ex.Message, "M2_APEX", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
