using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Listly.Native;
using Listly.Services;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace Listly.Views;

/// <summary>
/// M2_Commander — a Norton Commander / muCommander style dual-pane file manager,
/// opened with Ctrl+E from the search surfaces. Keyboard-driven: Tab switches panes,
/// Enter opens, Backspace goes up, and F3/F4/F5/F6/F7/F8/F10 map to the classic actions.
/// </summary>
public partial class M2CommanderWindow : Window
{
    private readonly Pane _left;
    private readonly Pane _right;
    private Pane _active;
    private Action<string>? _promptAction;

    public M2CommanderWindow()
    {
        InitializeComponent();

        _left = new Pane { List = LeftList, Header = LeftPath, PaneBorder = LeftPane, HeaderBar = LeftHeaderBar };
        _right = new Pane { List = RightList, Header = RightPath, PaneBorder = RightPane, HeaderBar = RightHeaderBar };
        _active = _left;

        LeftList.ItemsSource = _left.Entries;
        RightList.ItemsSource = _right.Entries;

        LeftList.PreviewMouseDown += (_, _) => SetActive(_left);
        RightList.PreviewMouseDown += (_, _) => SetActive(_right);
        LeftList.MouseDoubleClick += OnListDoubleClick;
        RightList.MouseDoubleClick += OnListDoubleClick;
        LeftList.SelectionChanged += (_, _) => { if (_active == _left) UpdateStatus(); };
        RightList.SelectionChanged += (_, _) => { if (_active == _right) UpdateStatus(); };

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnWindowMouseDown;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        NavigateTo(_left, home, null, pushHistory: false);
        NavigateTo(_right, home, null, pushHistory: false);
        UpdateActiveVisual();
    }

    /// <summary>Shows the window focused on <paramref name="path"/> (a file selects it; a folder opens it).</summary>
    public void ShowAt(string? path)
    {
        var (dir, selectName) = Resolve(path);
        NavigateTo(_active, dir, selectName, pushHistory: true);

        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        _active.List.Focus();
    }

    // --- Navigation ---------------------------------------------------------

    private void NavigateTo(Pane pane, string dir, string? selectName, bool pushHistory)
    {
        string full;
        try { full = Path.GetFullPath(dir); }
        catch { return; }

        if (!TryBuildEntries(full, out var entries, out var error))
        {
            StatusText.Text = error;
            return;
        }

        if (pushHistory && !string.IsNullOrEmpty(pane.Dir) && !PathEquals(pane.Dir, full))
        {
            pane.Back.Push(pane.Dir);
            pane.Forward.Clear();
        }

        CommitEntries(pane, full, entries, selectName);
    }

    private void GoUp(Pane pane)
    {
        var parent = Directory.GetParent(pane.Dir.TrimEnd(Path.DirectorySeparatorChar));
        if (parent == null)
            return;

        var from = Path.GetFileName(pane.Dir.TrimEnd(Path.DirectorySeparatorChar));
        NavigateTo(pane, parent.FullName, from, pushHistory: true);
    }

    private void GoBack(Pane pane)
    {
        if (pane.Back.Count == 0)
            return;

        var target = pane.Back.Pop();
        if (TryBuildEntries(target, out var entries, out var error))
        {
            pane.Forward.Push(pane.Dir);
            CommitEntries(pane, target, entries, null);
        }
        else
        {
            StatusText.Text = error;
        }
    }

    private void GoForward(Pane pane)
    {
        if (pane.Forward.Count == 0)
            return;

        var target = pane.Forward.Pop();
        if (TryBuildEntries(target, out var entries, out var error))
        {
            pane.Back.Push(pane.Dir);
            CommitEntries(pane, target, entries, null);
        }
        else
        {
            StatusText.Text = error;
        }
    }

    private void OpenEntry(Pane pane, CommanderEntry entry)
    {
        if (entry.IsParent)
        {
            GoUp(pane);
            return;
        }

        if (entry.IsFolder)
        {
            NavigateTo(pane, entry.Path, null, pushHistory: true);
            return;
        }

        OpenFile(entry.Path);
    }

    private void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }
    }

    private void InvokeSelected()
    {
        var sel = ActiveSelected();
        if (sel != null)
            OpenEntry(_active, sel);
    }

    // --- File operations ----------------------------------------------------

    private void EditSelected()
    {
        var sel = ActiveSelected();
        if (sel is null || sel.IsFolder)
            return;

        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{sel.Path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }
    }

    private void CopySelected()
    {
        var sel = ActiveSelected();
        if (sel is null || sel.IsParent)
            return;

        ShellOp(NativeMethods.FO_COPY, sel.Path, Other(_active).Dir, 0);
    }

    private void MoveSelected()
    {
        var sel = ActiveSelected();
        if (sel is null || sel.IsParent)
            return;

        ShellOp(NativeMethods.FO_MOVE, sel.Path, Other(_active).Dir, 0);
    }

    private void DeleteSelected()
    {
        var sel = ActiveSelected();
        if (sel is null || sel.IsParent)
            return;

        ShellOp(NativeMethods.FO_DELETE, sel.Path, null, NativeMethods.FOF_ALLOWUNDO);
    }

    private void ShellOp(uint func, string from, string? to, ushort extraFlags)
    {
        try
        {
            var op = new NativeMethods.SHFILEOPSTRUCT
            {
                hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle,
                wFunc = func,
                pFrom = from + "\0\0",
                pTo = to is null ? null : to + "\0\0",
                fFlags = extraFlags
            };
            NativeMethods.SHFileOperation(ref op);
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }

        RefreshBoth();
        _active.List.Focus();
    }

    private void PromptMkdir()
    {
        ShowPrompt(Loc.T("commander.newFolder"), string.Empty, name =>
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(_active.Dir, name));
                RefreshPane(_active);
                SelectByName(_active, name);
            }
            catch (Exception ex)
            {
                Warn(ex.Message);
            }
        });
    }

    private void PromptRename()
    {
        var sel = ActiveSelected();
        if (sel is null || sel.IsParent)
            return;

        ShowPrompt(Loc.T("commander.renameTo"), sel.Name, newName =>
        {
            try
            {
                var dest = Path.Combine(_active.Dir, newName);
                if (sel.IsFolder)
                    Directory.Move(sel.Path, dest);
                else
                    File.Move(sel.Path, dest);

                RefreshPane(_active);
                SelectByName(_active, newName);
            }
            catch (Exception ex)
            {
                Warn(ex.Message);
            }
        });
    }

    private void SwapPanes()
    {
        var l = _left.Dir;
        var r = _right.Dir;
        if (string.IsNullOrEmpty(l) || string.IsNullOrEmpty(r))
            return;

        NavigateTo(_left, r, null, pushHistory: true);
        NavigateTo(_right, l, null, pushHistory: true);
    }

    private void RefreshBoth()
    {
        RefreshPane(_left);
        RefreshPane(_right);
    }

    private void RefreshPane(Pane pane)
    {
        if (string.IsNullOrEmpty(pane.Dir))
            return;

        int idx = pane.List.SelectedIndex;
        if (TryBuildEntries(pane.Dir, out var entries, out _))
        {
            CommitEntries(pane, pane.Dir, entries, null);
            if (idx >= 0 && idx < pane.Entries.Count)
                pane.List.SelectedIndex = idx;
        }
    }

    // --- Data building ------------------------------------------------------

    private static bool TryBuildEntries(string dir, out List<CommanderEntry> entries, out string error)
    {
        entries = new List<CommanderEntry>();
        error = string.Empty;

        try
        {
            var info = new DirectoryInfo(dir);
            var dirs = new List<CommanderEntry>();
            var files = new List<CommanderEntry>();

            foreach (var child in info.EnumerateFileSystemInfos())
            {
                try
                {
                    var attr = child.Attributes;
                    if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        continue;

                    bool isDir = (attr & FileAttributes.Directory) != 0;
                    string date = child.LastWriteTime.ToString("yyyy-MM-dd HH:mm");

                    if (isDir)
                    {
                        dirs.Add(new CommanderEntry
                        {
                            Name = child.Name,
                            Path = child.FullName,
                            IsFolder = true,
                            Glyph = IconGlyph.Folder,
                            SizeText = "<DIR>",
                            DateText = date
                        });
                    }
                    else
                    {
                        files.Add(new CommanderEntry
                        {
                            Name = child.Name,
                            Path = child.FullName,
                            IsFolder = false,
                            Glyph = IconGlyph.ForFile(child.FullName, false),
                            SizeText = FormatSize(((FileInfo)child).Length),
                            DateText = date
                        });
                    }
                }
                catch
                {
                    // Skip entries we cannot read.
                }
            }

            dirs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            if (Directory.GetParent(dir.TrimEnd(Path.DirectorySeparatorChar)) is { } parent)
            {
                entries.Add(new CommanderEntry
                {
                    Name = "..",
                    Path = parent.FullName,
                    IsFolder = true,
                    IsParent = true,
                    Glyph = IconGlyph.Folder,
                    SizeText = "<UP>",
                    DateText = string.Empty
                });
            }

            entries.AddRange(dirs);
            entries.AddRange(files);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void CommitEntries(Pane pane, string dir, List<CommanderEntry> entries, string? selectName)
    {
        pane.Dir = dir;
        pane.Entries.Clear();
        foreach (var entry in entries)
            pane.Entries.Add(entry);

        pane.Header.Text = dir;

        int index = 0;
        if (selectName != null)
        {
            for (int i = 0; i < pane.Entries.Count; i++)
            {
                if (!pane.Entries[i].IsParent &&
                    string.Equals(pane.Entries[i].Name, selectName, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
        }

        if (pane.Entries.Count > 0)
        {
            pane.List.SelectedIndex = index;
            pane.List.ScrollIntoView(pane.List.SelectedItem);
        }

        if (_active == pane)
            UpdateStatus();
    }

    private static void SelectByName(Pane pane, string name)
    {
        for (int i = 0; i < pane.Entries.Count; i++)
        {
            if (string.Equals(pane.Entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                pane.List.SelectedIndex = i;
                pane.List.ScrollIntoView(pane.List.SelectedItem);
                break;
            }
        }
    }

    // --- Active pane / status ----------------------------------------------

    private void SetActive(Pane pane)
    {
        if (_active == pane)
        {
            UpdateStatus();
            return;
        }

        _active = pane;
        UpdateActiveVisual();
        UpdateStatus();
    }

    private Pane Other(Pane pane) => pane == _left ? _right : _left;

    private CommanderEntry? ActiveSelected() => _active.List.SelectedItem as CommanderEntry;

    private void UpdateActiveVisual()
    {
        ApplyPaneActive(_left, _active == _left);
        ApplyPaneActive(_right, _active == _right);
    }

    private void ApplyPaneActive(Pane pane, bool active)
    {
        pane.PaneBorder.BorderBrush = (System.Windows.Media.Brush)FindResource(active ? "ThemeAccentBrush" : "ThemeBorderBrush");
        pane.PaneBorder.BorderThickness = new Thickness(active ? 2 : 1);
        pane.HeaderBar.Background = (System.Windows.Media.Brush)FindResource(active ? "ThemeSelectedBrush" : "ThemeButtonBrush");
    }

    private void UpdateStatus()
    {
        var pane = _active;
        int count = pane.Entries.Count(e => !e.IsParent);
        var sel = ActiveSelected();
        string selInfo = sel is null || sel.IsParent ? string.Empty : $"    ▸ {sel.Name}   {sel.SizeText}";
        StatusText.Text = $"{pane.Dir}    ({count})" + selInfo;
    }

    // --- Prompt overlay -----------------------------------------------------

    private void ShowPrompt(string title, string initial, Action<string> onOk)
    {
        PromptTitle.Text = title;
        PromptBox.Text = initial;
        _promptAction = onOk;
        PromptOverlay.Visibility = Visibility.Visible;
        PromptBox.Focus();
        PromptBox.SelectAll();
    }

    private void ClosePrompt()
    {
        PromptOverlay.Visibility = Visibility.Collapsed;
        _promptAction = null;
        _active.List.Focus();
    }

    private void OnPromptOkClick(object sender, RoutedEventArgs e)
    {
        var action = _promptAction;
        var text = PromptBox.Text.Trim();
        ClosePrompt();
        if (action != null && text.Length > 0)
            action(text);
    }

    private void OnPromptCancelClick(object sender, RoutedEventArgs e) => ClosePrompt();

    private void OnPromptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnPromptOkClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClosePrompt();
            e.Handled = true;
        }
    }

    // --- Function-bar buttons ----------------------------------------------

    private void OnViewClick(object sender, RoutedEventArgs e) => InvokeSelected();
    private void OnEditClick(object sender, RoutedEventArgs e) => EditSelected();
    private void OnCopyClick(object sender, RoutedEventArgs e) => CopySelected();
    private void OnMoveClick(object sender, RoutedEventArgs e) => MoveSelected();
    private void OnMkdirClick(object sender, RoutedEventArgs e) => PromptMkdir();
    private void OnDeleteClick(object sender, RoutedEventArgs e) => DeleteSelected();
    private void OnQuitClick(object sender, RoutedEventArgs e) => Close();

    // --- Input --------------------------------------------------------------

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var pane = sender == LeftList ? _left : _right;
        SetActive(pane);
        var sel = ActiveSelected();
        if (sel != null)
            OpenEntry(pane, sel);
    }

    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1)
        {
            GoBack(_active);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            GoForward(_active);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (PromptOverlay.Visibility == Visibility.Visible)
            return;

        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Tab:
                SetActive(Other(_active));
                _active.List.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                InvokeSelected();
                e.Handled = true;
                break;
            case Key.Back:
                GoUp(_active);
                e.Handled = true;
                break;
            case Key.F3:
                InvokeSelected();
                e.Handled = true;
                break;
            case Key.F4:
                EditSelected();
                e.Handled = true;
                break;
            case Key.F5:
                CopySelected();
                e.Handled = true;
                break;
            case Key.F6:
                MoveSelected();
                e.Handled = true;
                break;
            case Key.F7:
                PromptMkdir();
                e.Handled = true;
                break;
            case Key.F8:
                DeleteSelected();
                e.Handled = true;
                break;
            case Key.F2:
                PromptRename();
                e.Handled = true;
                break;
            case Key.F10:
                Close();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.U when mods == ModifierKeys.Control:
                SwapPanes();
                e.Handled = true;
                break;
            case Key.R when mods == ModifierKeys.Control:
                RefreshBoth();
                e.Handled = true;
                break;
            case Key.Left when mods == ModifierKeys.Alt:
                GoBack(_active);
                e.Handled = true;
                break;
            case Key.Right when mods == ModifierKeys.Alt:
                GoForward(_active);
                e.Handled = true;
                break;
        }
    }

    // --- Helpers ------------------------------------------------------------

    private void Warn(string message) =>
        MessageBox.Show(this, message, "M2_Commander", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static (string dir, string? selectName) Resolve(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (Directory.Exists(path))
                    return (path, null);

                if (File.Exists(path))
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        return (dir, Path.GetFileName(path));
                }
            }
        }
        catch
        {
            // Fall through to the default folder.
        }

        return (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), null);
    }

    private static bool PathEquals(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    /// <summary>One row in a pane's listing.</summary>
    public sealed class CommanderEntry
    {
        public required string Name { get; init; }
        public required string Path { get; init; }
        public bool IsFolder { get; init; }
        public bool IsParent { get; init; }
        public string Glyph { get; init; } = IconGlyph.GenericFile;
        public string SizeText { get; init; } = string.Empty;
        public string DateText { get; init; } = string.Empty;
    }

    private sealed class Pane
    {
        public required ListBox List { get; init; }
        public required TextBlock Header { get; init; }
        public required Border PaneBorder { get; init; }
        public required Border HeaderBar { get; init; }
        public ObservableCollection<CommanderEntry> Entries { get; } = new();
        public string Dir { get; set; } = string.Empty;
        public Stack<string> Back { get; } = new();
        public Stack<string> Forward { get; } = new();
    }
}
