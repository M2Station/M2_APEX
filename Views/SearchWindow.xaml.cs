using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

using Listly.Models;
using Listly.Native;
using Listly.Services;
using Listly.ViewModels;

namespace Listly.Views;

public partial class SearchWindow : Window
{
    private readonly SearchViewModel _viewModel;
    private readonly LaunchService _launch;
    private readonly AppSettings _settings;
    private IntPtr _invokerHwnd;

    /// <summary>Raised on Ctrl+` ; argument is the selected item's path (or null for none/web/command).</summary>
    public event Action<string?>? OpenCommanderRequested;

    public SearchWindow(SearchViewModel viewModel, LaunchService launch, AppSettings settings)
    {
        _viewModel = viewModel;
        _launch = launch;
        _settings = settings;

        InitializeComponent();
        DataContext = _viewModel;

        Deactivated += (_, _) => HideSearch();
        PreviewKeyDown += OnPreviewKeyDown;
        ResultsList.PreviewMouseLeftButtonUp += OnResultClicked;
        ResultsList.SelectionChanged += (_, _) => ScrollSelectedIntoView();
        SizeChanged += (_, _) => { if (IsVisible) PositionWindow(); };
    }

    /// <summary>Bottom-bar "Ctrl+`" link: shows a short animated demo of opening M2_Commander.</summary>
    private void OnCommanderHintClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        new GifPreviewWindow(CommanderGifUri(_settings.Theme)) { Icon = Icon }.Show();
    }

    /// <summary>Pack URI of the Ctrl+` demo GIF for the active theme (themes without their own use Low Key).</summary>
    private static Uri CommanderGifUri(string? theme)
    {
        string name = theme switch
        {
            "daylight" => "daylight",
            "army" => "army",
            "army_dark" => "army_dark",
            _ => "low_key",
        };
        return new Uri($"pack://application:,,,/Assets/gif/ctrl-backtick-{name}.gif", UriKind.Absolute);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Hide from Alt+Tab.
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);
    }

    public void ToggleSearch()
    {
        if (IsVisible)
            HideSearch();
        else
            ShowSearch();
    }

    public void ShowSearch()
    {
        _invokerHwnd = NativeMethods.GetForegroundWindow();
        _viewModel.Clear();
        _viewModel.ShowInitial();

        PositionWindow();

        Show();
        Topmost = true;

        var hwnd = new WindowInteropHelper(this).Handle;
        ForceForeground(hwnd);
        Activate();

        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
        QueryBox.SelectAll();
    }

    /// <summary>
    /// Places the search bar for the configured position. Top/Center/Bottom are horizontally
    /// centred at a vertical fraction; Top-left and Bottom-right snap to a screen corner
    /// (Bottom-right re-anchors as the list grows, via SizeChanged).
    /// </summary>
    private void PositionWindow()
    {
        var area = NativeMethods.GetWorkAreaDip(_invokerHwnd);
        const double margin = 22;
        double h = ActualHeight > 1 ? ActualHeight : 420;
        double centered = area.Left + (area.Width - Width) / 2;

        (double left, double top) = _settings.SearchBarPosition switch
        {
            BarPosition.TopLeft => (area.Left + margin, area.Top + margin),
            BarPosition.BottomRight => (area.Right - Width - margin, area.Bottom - h - margin),
            BarPosition.Center => (centered, area.Top + area.Height * 0.35),
            BarPosition.Bottom => (centered, area.Top + area.Height * 0.55),
            _ => (centered, area.Top + area.Height * 0.16),
        };

        Left = left;
        Top = top;
    }

    /// <summary>
    /// Reliably pulls this window to the foreground from a background process. A background
    /// process cannot normally steal focus, so we briefly attach to the currently focused
    /// thread's input queue. This bypasses the Windows foreground lock so typing lands in the
    /// search box instead of the app that was focused when the hotkey fired (e.g. Explorer).
    /// </summary>
    private static void ForceForeground(IntPtr hwnd)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        uint foreThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint thisThread = NativeMethods.GetCurrentThreadId();

        if (foreThread != 0 && foreThread != thisThread)
        {
            NativeMethods.AttachThreadInput(foreThread, thisThread, true);
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.AttachThreadInput(foreThread, thisThread, false);
        }
        else
        {
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    public void HideSearch()
    {
        if (!IsVisible)
            return;

        Hide();
        _viewModel.Clear();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                HideSearch();
                e.Handled = true;
                break;

            case Key.Down:
                _viewModel.MoveSelection(1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;

            case Key.Up:
                _viewModel.MoveSelection(-1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;

            case Key.PageDown:
                _viewModel.MoveSelection(5);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;

            case Key.PageUp:
                _viewModel.MoveSelection(-5);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;

            case Key.Enter:
                ActivateSelected(Keyboard.Modifiers);
                e.Handled = true;
                break;

            case Key.OemTilde when Keyboard.Modifiers == ModifierKeys.Control:
                RequestOpenCommander();
                e.Handled = true;
                break;

            case Key.C when Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && QueryBox.SelectionLength == 0:
                if (_viewModel.SelectedResult is { } toCopy)
                    _launch.CopyPath(toCopy);
                e.Handled = true;
                break;
        }
    }

    private void OnResultClicked(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ResultsList, (DependencyObject)e.OriginalSource) is not ListBoxItem container)
            return;

        int index = ResultsList.ItemContainerGenerator.IndexFromContainer(container);
        if (index < 0)
            return;

        _viewModel.SelectedIndex = index;
        ActivateSelected(Keyboard.Modifiers);
    }

    private void ActivateSelected(ModifierKeys modifiers)
    {
        if (_viewModel.SelectedResult is not { } result)
            return;

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            _launch.OpenContainingFolder(result);
        }
        else
        {
            bool asAdmin = modifiers.HasFlag(ModifierKeys.Shift);
            _launch.Launch(result, asAdmin);
        }

        HideSearch();
    }

    private void RequestOpenCommander()
    {
        string? path = null;

        // If you typed a search and highlighted a file/folder, open M2_Commander there.
        if (!string.IsNullOrWhiteSpace(_viewModel.Query)
            && _viewModel.SelectedResult is { Kind: not (ResultKind.WebSearch or ResultKind.Command) } result)
        {
            path = result.Path;
        }

        // Otherwise fall back to the folder that was in front when search opened
        // (e.g. the Explorer window you were looking at).
        path ??= ExplorerAccess.GetFolderPath(_invokerHwnd);

        OpenCommanderRequested?.Invoke(path);
        HideSearch();
    }

    private void ScrollSelectedIntoView()
    {
        if (ResultsList.SelectedItem is not null)
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }
}
