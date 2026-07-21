using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

using Listly.Native;
using Listly.Services;
using Listly.ViewModels;

namespace Listly.Views;

public partial class SearchWindow : Window
{
    private readonly SearchViewModel _viewModel;
    private readonly LaunchService _launch;

    public SearchWindow(SearchViewModel viewModel, LaunchService launch)
    {
        _viewModel = viewModel;
        _launch = launch;

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
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.16;

        Show();
        Activate();
        Topmost = true;

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetForegroundWindow(hwnd);

        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
        QueryBox.SelectAll();
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

    private void ScrollSelectedIntoView()
    {
        if (ResultsList.SelectedItem is not null)
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }
}
