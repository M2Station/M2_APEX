using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Listly.Models;
using Listly.Services;

namespace Listly.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly FileIndexService _fileIndex;
    private readonly IReadOnlyList<ThemeManager.ThemeInfo> _themes = ThemeManager.Themes;
    private static readonly string[] PositionLabels = { "Top", "Center", "Bottom" };
    private bool _loadingTheme;

    public SettingsWindow(AppSettings settings, FileIndexService fileIndex)
    {
        _settings = settings;
        _fileIndex = fileIndex;

        InitializeComponent();
        LoadFromSettings();

        _fileIndex.StatusChanged += OnIndexStatus;
        Closed += (_, _) => _fileIndex.StatusChanged -= OnIndexStatus;

        // Revert any unsaved live theme preview back to the persisted choice.
        Closed += (_, _) => ThemeManager.Apply(_settings.Theme);

        IndexStatus.Text = $"{_fileIndex.Count:N0} items indexed";
        CreditLogo.Data = Assets.M2Logo.Geometry;
    }

    private void LoadFromSettings()
    {
        _loadingTheme = true;
        ThemeBox.ItemsSource = _themes.Select(t => t.Name).ToList();
        ThemeBox.SelectedIndex = Math.Max(0, IndexOfTheme(ThemeManager.Normalize(_settings.Theme)));
        _loadingTheme = false;

        DoubleCtrlBox.IsChecked = _settings.EnableDoubleCtrl;
        AltSpaceBox.IsChecked = _settings.EnableAltSpace;
        QuickSwitchBox.IsChecked = _settings.EnableQuickSwitch;
        ForeignInputBox.IsChecked = _settings.IgnoreForeignInput;
        ThresholdBox.Text = _settings.DoubleCtrlThresholdMs.ToString();
        SearchPosBox.ItemsSource = PositionLabels;
        SearchPosBox.SelectedIndex = (int)_settings.SearchBarPosition;
        QuickPosBox.ItemsSource = PositionLabels;
        QuickPosBox.SelectedIndex = (int)_settings.QuickSwitchPosition;
        FilesFirstBox.IsChecked = _settings.ShowFilesFirst;
        MaxResultsBox.Text = _settings.MaxResults.ToString();
        WebUrlBox.Text = _settings.WebSearchUrl;
        PriorityBox.Text = string.Join(Environment.NewLine, _settings.PriorityLocations);
        HiddenBox.IsChecked = _settings.IndexHiddenFiles;
        DrivesBox.Text = string.Join(Environment.NewLine, _settings.IndexedDrives);
        ExcludedBox.Text = string.Join(Environment.NewLine, _settings.ExcludedFolders);
        StartupBox.IsChecked = StartupService.IsEnabled();
        VersionText.Text = $"M2_APEX  v{UpdateService.CurrentVersion}";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _settings.EnableDoubleCtrl = DoubleCtrlBox.IsChecked == true;
        _settings.EnableAltSpace = AltSpaceBox.IsChecked == true;
        _settings.EnableQuickSwitch = QuickSwitchBox.IsChecked == true;
        _settings.IgnoreForeignInput = ForeignInputBox.IsChecked == true;
        _settings.ShowFilesFirst = FilesFirstBox.IsChecked == true;
        _settings.IndexHiddenFiles = HiddenBox.IsChecked == true;
        _settings.SearchBarPosition = (BarPosition)Math.Max(0, SearchPosBox.SelectedIndex);
        _settings.QuickSwitchPosition = (BarPosition)Math.Max(0, QuickPosBox.SelectedIndex);

        if (int.TryParse(ThresholdBox.Text, out int threshold))
            _settings.DoubleCtrlThresholdMs = Math.Clamp(threshold, 150, 1000);

        if (int.TryParse(MaxResultsBox.Text, out int max))
            _settings.MaxResults = Math.Clamp(max, 4, 40);

        if (!string.IsNullOrWhiteSpace(WebUrlBox.Text))
            _settings.WebSearchUrl = WebUrlBox.Text.Trim();

        _settings.PriorityLocations = SplitLines(PriorityBox.Text);
        _settings.IndexedDrives = SplitLines(DrivesBox.Text);
        _settings.ExcludedFolders = SplitLines(ExcludedBox.Text);

        if (ThemeBox.SelectedIndex >= 0)
            _settings.Theme = _themes[ThemeBox.SelectedIndex].Id;

        _settings.Save();
        StartupService.SetEnabled(StartupBox.IsChecked == true);

        DialogResultSafeClose();
    }

    private void OnRebuildClick(object sender, RoutedEventArgs e)
    {
        // Apply indexing-related settings before rebuilding.
        _settings.IndexHiddenFiles = HiddenBox.IsChecked == true;
        _settings.IndexedDrives = SplitLines(DrivesBox.Text);
        _settings.ExcludedFolders = SplitLines(ExcludedBox.Text);
        _ = _fileIndex.BuildAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnCreditClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/oahsiao") { UseShellExecute = true });
        }
        catch
        {
            // Best effort.
        }
    }

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Checking\u2026";
        try
        {
            var info = await UpdateService.CheckForUpdateAsync();
            if (info is null)
            {
                UpdateStatus.Text = "Couldn't check for updates. Try again later.";
            }
            else if (info.HasUpdate)
            {
                UpdateStatus.Text = $"New version {info.LatestVersion} available.";
                var choice = System.Windows.MessageBox.Show(this,
                    $"A new version {info.LatestVersion} is available.\nYou have {info.CurrentVersion}.\n\nOpen the download page now?",
                    "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (choice == MessageBoxResult.Yes)
                    UpdateService.OpenInBrowser(info.DownloadUrl ?? info.ReleaseUrl);
            }
            else
            {
                UpdateStatus.Text = $"You're on the latest version ({info.CurrentVersion}).";
            }
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTheme || ThemeBox.SelectedIndex < 0)
            return;

        // Live preview across every open window; persisted only on Save.
        ThemeManager.Apply(_themes[ThemeBox.SelectedIndex].Id);
    }

    private int IndexOfTheme(string id)
    {
        for (int i = 0; i < _themes.Count; i++)
            if (_themes[i].Id == id)
                return i;
        return -1;
    }

    private void OnIndexStatus(string status)
    {
        Dispatcher.BeginInvoke(() => IndexStatus.Text = status);
    }

    private static List<string> SplitLines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private void DialogResultSafeClose()
    {
        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // Ignore if already closing.
        }
    }
}
