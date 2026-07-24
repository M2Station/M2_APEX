using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Listly.Services;

using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Listly.Views;

/// <summary>
/// A minimal modal single-choice picker, themed with the app's <c>DynamicResource</c> brushes. Shown when
/// an auto-detect turns up more than one candidate (e.g. Windows PowerShell vs PowerShell 7) and the user
/// has to pick which one to use. Built in code so no extra XAML is needed for this rare, transient dialog.
/// </summary>
internal static class ChoiceDialog
{
    /// <summary>
    /// Shows the options modally over <paramref name="owner"/> and returns the chosen index, or -1 when
    /// the user cancels.
    /// </summary>
    public static int Show(Window? owner, string title, string prompt, IReadOnlyList<string> options)
    {
        int result = -1;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(0, 0, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = FindBrush("ThemeTextBrush"),
        });

        var window = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow,
            MinWidth = 380,
            Background = FindBrush("ThemeSolidBackgroundBrush") ?? System.Windows.SystemColors.WindowBrush,
        };

        if (owner is not null && owner.IsLoaded)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = options[i],
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(14, 9, 14, 9),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = FindBrush("ThemeButtonBrush"),
                Foreground = FindBrush("ThemeButtonTextBrush"),
                BorderBrush = FindBrush("ThemeBorderBrush"),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            button.Click += (_, _) => { result = index; window.Close(); };
            panel.Children.Add(button);
        }

        var cancel = new Button
        {
            Content = Loc.T("commander.cancel"),
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(16, 7, 16, 7),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = FindBrush("ThemeButtonBrush"),
            Foreground = FindBrush("ThemeButtonTextBrush"),
            BorderBrush = FindBrush("ThemeBorderBrush"),
            IsCancel = true,
        };
        cancel.Click += (_, _) => { result = -1; window.Close(); };
        panel.Children.Add(cancel);

        window.ShowDialog();
        return result;
    }

    /// <summary>
    /// Resolves a PowerShell host for auto-detect: returns the only one found, lets the user pick when
    /// several are installed, or null when none are found or the user cancels.
    /// </summary>
    public static string? PickPowerShell(Window? owner, FileIndexService? index)
    {
        var installs = PowerShellLocator.FindAll(index);
        if (installs.Count == 0)
            return null;
        if (installs.Count == 1)
            return installs[0].Path;

        var labels = installs.Select(o => $"{o.Label}\n{o.Path}").ToList();
        int pick = Show(owner, Loc.T("commander.chooser.psTitle"), Loc.T("commander.chooser.psPrompt"), labels);
        return pick >= 0 ? installs[pick].Path : null;
    }

    private static Brush? FindBrush(string key) => Application.Current?.TryFindResource(key) as Brush;
}
