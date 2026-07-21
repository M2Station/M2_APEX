using System.IO;
using System.Windows;
using System.Windows.Controls;

using Listly.Models;
using Listly.Services;

namespace Listly.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly FileIndexService _fileIndex;
    private readonly IReadOnlyList<ThemeManager.ThemeInfo> _themes = ThemeManager.Themes;
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
        ThresholdBox.Text = _settings.DoubleCtrlThresholdMs.ToString();
        FilesFirstBox.IsChecked = _settings.ShowFilesFirst;
        MaxResultsBox.Text = _settings.MaxResults.ToString();
        WebUrlBox.Text = _settings.WebSearchUrl;
        HiddenBox.IsChecked = _settings.IndexHiddenFiles;
        DrivesBox.Text = string.Join(Environment.NewLine, _settings.IndexedDrives);
        ExcludedBox.Text = string.Join(Environment.NewLine, _settings.ExcludedFolders);
        StartupBox.IsChecked = StartupService.IsEnabled();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _settings.EnableDoubleCtrl = DoubleCtrlBox.IsChecked == true;
        _settings.EnableAltSpace = AltSpaceBox.IsChecked == true;
        _settings.EnableQuickSwitch = QuickSwitchBox.IsChecked == true;
        _settings.ShowFilesFirst = FilesFirstBox.IsChecked == true;
        _settings.IndexHiddenFiles = HiddenBox.IsChecked == true;

        if (int.TryParse(ThresholdBox.Text, out int threshold))
            _settings.DoubleCtrlThresholdMs = Math.Clamp(threshold, 150, 1000);

        if (int.TryParse(MaxResultsBox.Text, out int max))
            _settings.MaxResults = Math.Clamp(max, 4, 40);

        if (!string.IsNullOrWhiteSpace(WebUrlBox.Text))
            _settings.WebSearchUrl = WebUrlBox.Text.Trim();

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
