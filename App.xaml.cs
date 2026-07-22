using System.Threading;
using System.Windows;

using Listly.Models;
using Listly.Services;
using Listly.ViewModels;
using Listly.Views;

using Forms = System.Windows.Forms;

namespace Listly;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private AppSettings _settings = null!;
    private UsageTracker _usage = null!;
    private FileIndexService _fileIndex = null!;
    private AppIndexService _appIndex = null!;
    private CommandProvider _commands = null!;
    private SearchEngine _engine = null!;
    private LaunchService _launch = null!;
    private HotkeyService _hotkey = null!;
    private SearchViewModel _viewModel = null!;
    private SearchWindow _window = null!;
    private QuickSwitchBar _quickSwitchBar = null!;
    private QuickSwitchService _quickSwitch = null!;
    private SettingsWindow? _settingsWindow;
    private Forms.NotifyIcon _tray = null!;
    private System.Windows.Media.ImageSource? _windowIcon;
    private string? _pendingUpdateUrl;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash(args.Exception);
            args.SetObserved();
        };

        _singleInstance = new Mutex(initiallyOwned: true, "M2_APEX.SingleInstance", out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        Resources["M2LogoImage"] = Assets.M2Logo.CreateImage();
        _windowIcon = Assets.M2Logo.RenderBitmap(64, Assets.M2Logo.Foreground, Assets.M2Logo.BadgeBackground);

        _settings = AppSettings.Load();
        ThemeManager.Apply(_settings.Theme);
        Loc.Current = string.IsNullOrEmpty(_settings.Language) ? Loc.SystemLanguage() : _settings.Language;
        _usage = new UsageTracker();
        _fileIndex = new FileIndexService(_settings);
        _appIndex = new AppIndexService();
        _commands = new CommandProvider();
        _engine = new SearchEngine(_fileIndex, _appIndex, _commands, _usage, _settings);
        _launch = new LaunchService(_usage);
        _viewModel = new SearchViewModel(_engine);
        _window = new SearchWindow(_viewModel, _launch, _settings);
        _window.Icon = _windowIcon;

        _quickSwitchBar = new QuickSwitchBar();
        _quickSwitch = new QuickSwitchService(_settings, _quickSwitchBar);
        // While the main search bar is open, keep Quick Switch dormant so focus stays on it.
        _quickSwitch.Suppressed = () => _window.IsVisible;

        _fileIndex.StatusChanged += status =>
            Dispatcher.BeginInvoke(() => _viewModel.Status = status);

        _hotkey = new HotkeyService(_settings);
        _hotkey.Triggered += () => Dispatcher.BeginInvoke(() => _window.ToggleSearch());
        _hotkey.KeyFilter = _quickSwitch.OnKeyDown;
        _hotkey.Install();

        SetupTray();
        StartBackgroundIndexing();
        StartUpdateCheck();
    }

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = AppIcon.CreateTrayIcon(),
            Visible = true,
            Text = Loc.T("tray.tooltip")
        };

        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(() => _window.ShowSearch());
        _tray.BalloonTipClicked += (_, _) => OnUpdateBalloonClicked();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("tray.openSearch"), null, (_, _) => Dispatcher.BeginInvoke(() => _window.ShowSearch()));
        menu.Items.Add(Loc.T("tray.rebuildIndex"), null, (_, _) => _ = _fileIndex.BuildAsync());
        menu.Items.Add(Loc.T("tray.settings"), null, (_, _) => Dispatcher.BeginInvoke(() => OpenSettings()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("tray.exit"), null, (_, _) => Dispatcher.BeginInvoke(() => Shutdown()));
        _tray.ContextMenuStrip = menu;

        Loc.LanguageChanged += RefreshTrayText;

        _tray.ShowBalloonTip(4000, Loc.T("tray.runningTitle"),
            Loc.T("tray.runningBody"), Forms.ToolTipIcon.Info);
    }

    private void RefreshTrayText()
    {
        if (_tray is null)
            return;

        _tray.Text = Loc.T("tray.tooltip");

        var items = _tray.ContextMenuStrip?.Items;
        if (items is null || items.Count < 5)
            return;

        items[0].Text = Loc.T("tray.openSearch");
        items[1].Text = Loc.T("tray.rebuildIndex");
        items[2].Text = Loc.T("tray.settings");
        items[4].Text = Loc.T("tray.exit");
    }

    private void StartBackgroundIndexing()
    {
        _ = _appIndex.BuildAsync();
        _fileIndex.LoadCache();
        if (_fileIndex.Count == 0)
            _ = _fileIndex.BuildAsync();
    }

    private void StartUpdateCheck()
    {
        // One quiet check on launch; only surfaces a tray balloon if a newer release exists.
        _ = Task.Run(async () =>
        {
            var info = await UpdateService.CheckForUpdateAsync();
            if (info is not { HasUpdate: true })
                return;

            _ = Dispatcher.BeginInvoke(() =>
            {
                _pendingUpdateUrl = info.DownloadUrl ?? info.ReleaseUrl;
                _tray?.ShowBalloonTip(6000, Loc.T("update.balloonTitle"),
                    Loc.T("update.balloonBody", info.LatestVersion, info.CurrentVersion),
                    Forms.ToolTipIcon.Info);
            });
        });
    }

    private void OnUpdateBalloonClicked()
    {
        if (!string.IsNullOrEmpty(_pendingUpdateUrl))
            UpdateService.OpenInBrowser(_pendingUpdateUrl);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _fileIndex) { Icon = _windowIcon };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void LogCrash(Exception? ex) => CrashLog.Log(ex);
}
