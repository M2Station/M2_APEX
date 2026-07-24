using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly UsageTracker _usage;
    private readonly ObservableCollection<QuickPickRow> _editSearchLinks = new();
    private readonly IReadOnlyList<ThemeManager.ThemeInfo> _themes = ThemeManager.Themes;
    private static string[] PositionLabels => new[] { Loc.T("pos.top"), Loc.T("pos.center"), Loc.T("pos.bottom"), Loc.T("pos.topLeft"), Loc.T("pos.bottomRight") };
    private bool _loadingTheme;
    private bool _loadingLanguage;

    public SettingsWindow(AppSettings settings, FileIndexService fileIndex, UsageTracker usage)
    {
        _settings = settings;
        _fileIndex = fileIndex;
        _usage = usage;

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
        DebugLogPathText.Text = CrashLog.Folder;
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

        _settings.SearchLinks ??= new();
        _editSearchLinks.Clear();
        foreach (var link in _settings.SearchLinks)
        {
            // Auto-fill a blank program for a known M2 app from its install folders (fast probe) so an
            // app installed after the pick was seeded shows "Check Update" instead of "Install".
            string target = link.Target ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target))
                target = SupportApp.ByLabel(link.Name)?.DetectPath() ?? string.Empty;

            _editSearchLinks.Add(new QuickPickRow
            {
                Name = link.Name ?? string.Empty,
                Target = target,
                Arguments = link.Arguments ?? string.Empty
            });
        }
        SearchLinkList.ItemsSource = _editSearchLinks;

        HiddenBox.IsChecked = _settings.IndexHiddenFiles;
        DrivesBox.Text = string.Join(Environment.NewLine, _settings.IndexedDrives);
        ExcludedBox.Text = string.Join(Environment.NewLine, _settings.ExcludedFolders);
        StartupBox.IsChecked = StartupService.IsEnabled();
        PerformanceLogBox.IsChecked = _settings.EnablePerformanceLog;
        SystemLogBox.IsChecked = _settings.EnableSystemLog;
        SearchLogBox.IsChecked = _settings.EnableSearchLog;
        IndexLogBox.IsChecked = _settings.EnableIndexLog;
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
        _settings.EnablePerformanceLog = PerformanceLogBox.IsChecked == true;
        _settings.EnableSystemLog = SystemLogBox.IsChecked == true;
        _settings.EnableSearchLog = SearchLogBox.IsChecked == true;
        _settings.EnableIndexLog = IndexLogBox.IsChecked == true;
        PerfLog.Enabled = _settings.EnablePerformanceLog;
        SystemLog.Enabled = _settings.EnableSystemLog;
        SearchLog.Enabled = _settings.EnableSearchLog;
        IndexLog.Enabled = _settings.EnableIndexLog;
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

        _settings.SearchLinks = _editSearchLinks
            .Where(IsPersistableRow)
            .Select(l => new SearchLink
            {
                Name = (l.Name ?? string.Empty).Trim(),
                Target = (l.Target ?? string.Empty).Trim(),
                Arguments = (l.Arguments ?? string.Empty).Trim()
            })
            .ToList();

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

    private void OnSearchLinkAddClick(object sender, RoutedEventArgs e) =>
        _editSearchLinks.Add(new QuickPickRow());

    private void OnSearchLinkRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: QuickPickRow row })
            _editSearchLinks.Remove(row);
    }

    private void OnSearchLinkUpClick(object sender, RoutedEventArgs e) => MoveSearchLink(sender, -1);

    private void OnSearchLinkDownClick(object sender, RoutedEventArgs e) => MoveSearchLink(sender, +1);

    private void MoveSearchLink(object sender, int delta)
    {
        if (sender is not System.Windows.Controls.Button { Tag: QuickPickRow row })
            return;

        int i = _editSearchLinks.IndexOf(row);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _editSearchLinks.Count)
            return;

        _editSearchLinks.Move(i, j);
    }

    /// <summary>Keep custom rows only when they have a target; always keep known-app rows (even with a
    /// blank program) so the Install button persists. Blank targets stay hidden from the search list.</summary>
    private static bool IsPersistableRow(QuickPickRow row) =>
        !string.IsNullOrWhiteSpace(row.Target) ||
        (!string.IsNullOrWhiteSpace(row.Name) && SupportApp.ByLabel(row.Name) is not null);

    /// <summary>
    /// Quick-picks "Auto detect" (mirrors M2 Commander's F11 auto-detect): ensures a row exists for every
    /// first-party M2 app and fills any blank program from the tool's install folders / the file index.
    /// Existing user-set targets are never overwritten.
    /// </summary>
    private void OnQuickPickAutoDetectClick(object sender, RoutedEventArgs e)
    {
        foreach (var tool in SupportApp.Catalog.Where(t => t.IsApp))
        {
            if (!_editSearchLinks.Any(r => string.Equals((r.Name ?? string.Empty).Trim(), tool.Label, StringComparison.OrdinalIgnoreCase)))
                _editSearchLinks.Add(new QuickPickRow
                {
                    Name = tool.Label,
                    Arguments = string.IsNullOrEmpty(tool.Arguments) ? string.Empty : tool.Arguments
                });
        }

        int filled = 0, targets = 0;
        foreach (var row in _editSearchLinks)
        {
            if (!string.IsNullOrWhiteSpace(row.Target))
                continue;

            var tool = SupportApp.ByLabel(row.Name);
            if (tool is null)
                continue;

            targets++;
            var found = ProgramDetector.Detect(tool, _fileIndex);
            if (found is null)
                continue;

            row.Target = found;
            if (string.IsNullOrWhiteSpace(row.Arguments))
                row.Arguments = string.IsNullOrEmpty(tool.Arguments) ? "\"{path}\"" : tool.Arguments;
            filled++;
        }

        QuickAutoDetectResult.Text = Loc.T("settings.quickAutoResult", filled, targets);
    }

    /// <summary>Install button (program not detected): opens the app's GitHub releases page to download it.</summary>
    private void OnQuickPickInstallClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: QuickPickRow row }
            && TryParseRepo(row.Repo, out var owner, out var repo))
            UpdateService.OpenReleasesPage(owner, repo);
    }

    /// <summary>Check Update button: compares the installed program's version with the latest release.</summary>
    private async void OnQuickPickCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button
            || button.Tag is not QuickPickRow row
            || !TryParseRepo(row.Repo, out var owner, out var repo))
            return;

        button.IsEnabled = false;
        try
        {
            string current = UpdateService.GetInstalledVersion(row.Target);
            var info = await UpdateService.CheckForUpdateAsync(owner, repo, current);
            string title = string.IsNullOrWhiteSpace(row.Name) ? repo : row.Name;

            if (info is null)
            {
                System.Windows.MessageBox.Show(this, Loc.T("update.failed"), title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (info.HasUpdate)
            {
                var choice = System.Windows.MessageBox.Show(this,
                    Loc.T("update.promptBody", info.LatestVersion, info.CurrentVersion),
                    title, MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (choice == MessageBoxResult.Yes)
                    UpdateService.OpenInBrowser(info.DownloadUrl ?? info.ReleaseUrl, owner, repo);
            }
            else
            {
                System.Windows.MessageBox.Show(this, Loc.T("update.upToDate", info.CurrentVersion), title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>Splits a catalog "owner/name" repo string; false when malformed.</summary>
    private static bool TryParseRepo(string? repo, out string owner, out string name)
    {
        owner = name = string.Empty;
        if (string.IsNullOrWhiteSpace(repo))
            return false;

        var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        owner = parts[0];
        name = parts[1];
        return true;
    }

    private void OnCleanHistoryClick(object sender, RoutedEventArgs e)
    {
        _usage.Clear();
        CleanHistoryStatus.Text = Loc.T("settings.searchHistoryCleared");
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

    private void OnOpenDebugLogClick(object sender, RoutedEventArgs e) => CrashLog.OpenFolder();

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

/// <summary>
/// A single editable quick-pick row in Settings. Wraps the persisted <see cref="SearchLink"/> fields and,
/// when its <see cref="Name"/> matches a first-party M2 app in the tool catalog, exposes an Install
/// (program not detected) or Check Update (program detected) action driven by whether a target is set.
/// </summary>
public sealed class QuickPickRow : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _target = string.Empty;
    private string _arguments = string.Empty;
    private string _repo = string.Empty;

    /// <summary>Display name; also matches a catalog app (drives the Install / Check Update action).</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
                return;
            _name = value;
            _repo = SupportApp.ByLabel(value)?.Repo ?? string.Empty;
            OnChanged(nameof(Name));
            OnChanged(nameof(ShowInstall));
            OnChanged(nameof(ShowCheckUpdate));
        }
    }

    /// <summary>Application path, folder / UNC path, or URL. Blank = the app is treated as not installed.</summary>
    public string Target
    {
        get => _target;
        set
        {
            if (_target == value)
                return;
            _target = value;
            OnChanged(nameof(Target));
            OnChanged(nameof(ShowInstall));
            OnChanged(nameof(ShowCheckUpdate));
        }
    }

    /// <summary>Optional launch arguments; <c>{path}</c> is replaced with the focused folder.</summary>
    public string Arguments
    {
        get => _arguments;
        set { if (_arguments != value) { _arguments = value; OnChanged(nameof(Arguments)); } }
    }

    /// <summary>GitHub owner/repo resolved from the catalog by <see cref="Name"/>; empty for custom rows.</summary>
    public string Repo => _repo;

    /// <summary>Known M2 app whose program has not been detected yet -> show Install.</summary>
    public bool ShowInstall => !string.IsNullOrEmpty(_repo) && string.IsNullOrWhiteSpace(_target);

    /// <summary>Known M2 app whose program is set -> show Check Update.</summary>
    public bool ShowCheckUpdate => !string.IsNullOrEmpty(_repo) && !string.IsNullOrWhiteSpace(_target);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
