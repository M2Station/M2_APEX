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
        _viewModel.Clear();
        _viewModel.ShowInitial();

        var area = SystemParameters.WorkArea;
        double topFraction = _settings.SearchBarPosition switch
        {
            BarPosition.Center => 0.35,
            BarPosition.Bottom => 0.55,
            _ => 0.16,
        };
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * topFraction;

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
        var result = _viewModel.SelectedResult;
        string? path = result is { Kind: not (ResultKind.WebSearch or ResultKind.Command) } ? result.Path : null;
        OpenCommanderRequested?.Invoke(path);
        HideSearch();
    }

    private void ScrollSelectedIntoView()
    {
        if (ResultsList.SelectedItem is not null)
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }
}
