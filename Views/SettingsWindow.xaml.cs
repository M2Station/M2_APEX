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
    private static string[] PositionLabels => new[] { Loc.T("pos.top"), Loc.T("pos.center"), Loc.T("pos.bottom"), Loc.T("pos.topLeft"), Loc.T("pos.bottomRight") };
    private bool _loadingTheme;
    private bool _loadingLanguage;

    public SettingsWindow(AppSettings settings, FileIndexService fileIndex)
    {
        _settings = settings;
        _fileIndex = fileIndex;

        InitializeComponent();
        LoadFromSettings();

        _fileIndex.StatusChanged += OnIndexStatus;
        Closed += (_, _) => _fileIndex.StatusChanged -= OnIndexStatus;

        // Revert any unsaved live theme / language preview back to the persisted choice.
        Closed += (_, _) => ThemeManager.Apply(_settings.Theme);
        Closed += (_, _) => Loc.Current = string.IsNullOrEmpty(_settings.Language) ? Loc.SystemLanguage() : _settings.Language;

        IndexStatus.Text = Loc.T("index.count", _fileIndex.Count.ToString("N0"));
        CreditLogo.Data = Assets.M2Logo.Geometry;
        HeaderLogo.Data = Assets.M2Logo.Geometry;
        HeaderVersion.Text = $"v{UpdateService.CurrentVersion}";
        CrashLogPathText.Text = CrashLog.Folder;
        UpdateTitle();
    }

    private void LoadFromSettings()
    {
        _loadingTheme = true;
        ThemeBox.ItemsSource = _themes.Select(t => t.Name).ToList();
        ThemeBox.SelectedIndex = Math.Max(0, IndexOfTheme(ThemeManager.Normalize(_settings.Theme)));
        _loadingTheme = false;

        _loadingLanguage = true;
        LanguageBox.ItemsSource = Loc.Languages.Select(l => l.Name).ToList();
        LanguageBox.SelectedIndex = Math.Max(0, IndexOfLanguage(Loc.Current));
        _loadingLanguage = false;

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
        VersionText.Text = Loc.T("settings.version", UpdateService.CurrentVersion);
        ShowStartupStatus();
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

        if (LanguageBox.SelectedIndex >= 0)
            _settings.Language = Loc.Languages[LanguageBox.SelectedIndex].Id;

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

    private void OnCheckStartupClick(object sender, RoutedEventArgs e) => ShowStartupStatus();

    private void OnUnregisterStartupClick(object sender, RoutedEventArgs e)
    {
        StartupService.Unregister();
        StartupBox.IsChecked = false;
        ShowStartupStatus();
    }

    /// <summary>Shows whether M2_APEX is registered to launch at startup and which exe is registered,
    /// so a Setup-vs-Portable copy mismatch is obvious.</summary>
    private void ShowStartupStatus()
    {
        var registered = StartupService.GetRegisteredPath();
        if (string.IsNullOrEmpty(registered))
        {
            StartupStatusText.Text = Loc.T("settings.startupNotRegistered");
            return;
        }

        string current = Environment.ProcessPath ?? string.Empty;
        string note;
        if (!File.Exists(registered))
            note = Loc.T("settings.startupMissing");
        else if (!string.IsNullOrEmpty(current)
                 && string.Equals(Path.GetFullPath(registered), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase))
            note = Loc.T("settings.startupThis");
        else
            note = Loc.T("settings.startupOther", current);

        StartupStatusText.Text = Loc.T("settings.startupRegistered", registered) + "\n" + note;
    }

    private void OnFactoryResetClick(object sender, RoutedEventArgs e)
    {
        var choice = System.Windows.MessageBox.Show(this,
            Loc.T("settings.factoryResetConfirm"),
            Loc.T("settings.factoryResetTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
            return;

        // Wipe every persisted piece of state so the next launch starts at factory defaults:
        // settings (including M2 Commander), the on-disk index cache, and usage history.
        AppSettings.DeleteSavedFile();
        _fileIndex.ClearCache();
        UsageTracker.DeleteStore();
        StartupService.SetEnabled(false);

        RestartApp();
    }

    /// <summary>Relaunches M2_APEX after this instance exits (releasing the single-instance mutex).</summary>
    private static void RestartApp()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                Process.Start(new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -WindowStyle Hidden -Command \"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 400; Start-Process -FilePath '{exe}'\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
            }
        }
        catch
        {
            // Best effort: even if relaunch can't be scheduled, the reset already applied on disk.
        }

        System.Windows.Application.Current.Shutdown();
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

    private void OnOpenCrashLogClick(object sender, RoutedEventArgs e) => CrashLog.OpenFolder();

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatus.Text = Loc.T("update.checking");
        try
        {
            var info = await UpdateService.CheckForUpdateAsync();
            if (info is null)
            {
                UpdateStatus.Text = Loc.T("update.failed");
            }
            else if (info.HasUpdate)
            {
                UpdateStatus.Text = Loc.T("update.available", info.LatestVersion);
                var choice = System.Windows.MessageBox.Show(this,
                    Loc.T("update.promptBody", info.LatestVersion, info.CurrentVersion),
                    Loc.T("update.promptTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (choice == MessageBoxResult.Yes)
                    UpdateService.OpenInBrowser(info.DownloadUrl ?? info.ReleaseUrl);
            }
            else
            {
                UpdateStatus.Text = Loc.T("update.upToDate", info.CurrentVersion);
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

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingLanguage || LanguageBox.SelectedIndex < 0)
            return;

        // Live preview across every open window; persisted only on Save.
        Loc.Current = Loc.Languages[LanguageBox.SelectedIndex].Id;
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Title = $"{Loc.T("settings.title")}  v{UpdateService.CurrentVersion}";

    private int IndexOfLanguage(string id)
    {
        for (int i = 0; i < Loc.Languages.Count; i++)
            if (string.Equals(Loc.Languages[i].Id, id, StringComparison.OrdinalIgnoreCase))
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
