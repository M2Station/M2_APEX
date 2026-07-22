using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

using Listly.Models;
using Listly.Native;

using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace Listly.Views;

public partial class QuickSwitchBar : Window
{
    /// <summary>The matches currently shown in the list.</summary>
    public ObservableCollection<SearchResult> Results { get; } = new();

    /// <summary>Raised when a list row is clicked; the argument is the row index.</summary>
    public event Action<int>? ItemInvoked;

    public QuickSwitchBar()
    {
        InitializeComponent();
        ResultsList.ItemsSource = Results;
        ResultsList.PreviewMouseLeftButtonUp += OnItemClicked;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Never activate (Explorer keeps focus so the selection stays highlighted) and
        // stay out of Alt+Tab.
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
    }

    public void ShowFor(IntPtr explorerHwnd, BarPosition position)
    {
        PositionOver(explorerHwnd, position);
        if (!IsVisible)
            Show();
        Topmost = true;
    }

    public void HideBar()
    {
        if (IsVisible)
            Hide();
        Results.Clear();
    }

    /// <summary>Replaces the list with a new set of matches and highlights the selected row.</summary>
    public void SetResults(string query, IReadOnlyList<SearchResult> results, int selectedIndex)
    {
        QueryText.Text = query;

        Results.Clear();
        foreach (var result in results)
            Results.Add(result);

        if (results.Count == 0)
        {
            CountText.Text = "No matching item";
            ResultsList.SelectedIndex = -1;
            return;
        }

        CountText.Text = $"{selectedIndex + 1} / {results.Count}";
        ResultsList.SelectedIndex = selectedIndex;
        ScrollSelected();
    }

    /// <summary>Moves the selection highlight without rebuilding the list.</summary>
    public void SetSelected(int index)
    {
        if (index < 0 || index >= Results.Count)
            return;

        ResultsList.SelectedIndex = index;
        CountText.Text = $"{index + 1} / {Results.Count}";
        ScrollSelected();
    }

    private void ScrollSelected()
    {
        if (ResultsList.SelectedItem is not null)
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void OnItemClicked(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ResultsList, (DependencyObject)e.OriginalSource) is not ListBoxItem container)
            return;

        int index = ResultsList.ItemContainerGenerator.IndexFromContainer(container);
        if (index >= 0)
            ItemInvoked?.Invoke(index);
    }

    private void PositionOver(IntPtr explorerHwnd, BarPosition position)
    {
        if (!NativeMethods.GetWindowRect(explorerHwnd, out var rect))
            return;

        double scale = NativeMethods.GetDpiForWindow(explorerHwnd) / 96.0;
        if (scale <= 0)
            scale = 1.0;

        double widthPx = rect.Right - rect.Left;
        double centerXpx = rect.Left + widthPx / 2.0;
        double topDip = rect.Top / scale;
        double heightDip = (rect.Bottom - rect.Top) / scale;

        // Horizontal is always centred; the vertical anchor follows the chosen position.
        // The bar grows downward, so each option anchors its top edge.
        Left = centerXpx / scale - Width / 2.0;
        Top = position switch
        {
            BarPosition.Center => topDip + heightDip * 0.30,
            BarPosition.Bottom => topDip + heightDip * 0.55,
            _ => topDip + 76,
        };
    }
}
