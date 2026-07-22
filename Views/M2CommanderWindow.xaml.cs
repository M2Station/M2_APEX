using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Listly.Models;
using Listly.Native;
using Listly.Services;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace Listly.Views;

/// <summary>
/// M2_Commander — a Norton Commander / muCommander style dual-pane file manager,
/// opened with Ctrl+` from the search surfaces. Keyboard-driven: Tab switches panes,
/// Enter opens, Backspace goes up, Alt+←/→ navigate history, and F12 lists all shortcuts.
/// F1 (or right-click) opens the action menu — copy/move/delete/rename plus new folder/file —
/// and F11 edits the user's custom launcher commands shown below the file grid.
/// </summary>
public partial class M2CommanderWindow : Window
{
    private const int MinPanes = 2;
    private const int MaxPanes = 4;

    private readonly List<Pane> _panes = new();
    private Pane _active = null!;
    private Action<string>? _promptAction;

    // UI zoom factor derived from the screen resolution (see ApplyWindowChrome).
    private double _uiScale = 1.0;

    private readonly AppSettings _settings;
    private readonly FileIndexService _fileIndex;

    // Working copy edited by the F11 overlay; committed to _settings only on Save.
    private readonly ObservableCollection<CommanderCommand> _editCommands = new();

    // Source of the last COPY, re-used by PASTE (pane-to-pane; no OS clipboard).
    private string? _clipSource;

    // Active type-to-filter text for the current pane (empty = no filter).
    private string _filterText = string.Empty;

    // Pseudo-location shown above drive roots: a "This PC" listing of every drive.
    private const string DrivesView = "::drives::";

    private static readonly (string Keys, string DescKey)[] HelpRows =
    {
        ("F1", "commander.k.actions"),
        ("Tab", "commander.k.switch"),
        ("Alt+← / Alt+→", "commander.k.pane"),
        ("Enter", "commander.k.open"),
        ("Backspace / Alt+↑", "commander.k.up"),
        ("Alt+[", "commander.k.back"),
        ("Alt+]", "commander.k.forward"),
        ("Ctrl+C", "commander.k.copy"),
        ("Ctrl+V", "commander.k.paste"),
        ("F2", "commander.k.rename"),
        ("Del", "commander.k.delete"),
        ("Shift+Del", "commander.k.fastDelete"),
        ("A–Z", "commander.k.filter"),
        ("Esc", "commander.k.clearFilter"),
        ("Ctrl+U", "commander.k.swap"),
        ("Ctrl+R", "commander.k.refresh"),
        ("F11", "commander.k.commands"),
        ("Ctrl+`", "commander.k.openApp"),
        ("F12", "commander.k.help"),
        ("F10", "commander.k.quit"),
    };

    public M2CommanderWindow(AppSettings settings, FileIndexService fileIndex)
    {
        _settings = settings;
        _fileIndex = fileIndex;
        InitializeComponent();

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewTextInput += OnPreviewTextInput;
        PreviewMouseDown += OnWindowMouseDown;

        ApplyWindowChrome();
        RefreshCommandBar();
        InitPanes();
    }

    /// <summary>Shows the window focused on <paramref name="path"/> (a file selects it; a folder opens it).</summary>
    public void ShowAt(string? path)
    {
        var (dir, selectName) = Resolve(path);
        _active.Back.Clear();
        _active.Forward.Clear();
        NavigateTo(_active, dir, selectName, pushHistory: false);

        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
        FocusSelected(_active);
        SwitchToEnglishInput();
    }

    // --- Panes / responsive window ------------------------------------------

    /// <summary>
    /// Sizes the window to ~80% of the work area (or the remembered bounds) and applies a gentle
    /// resolution-based zoom to the whole UI so fonts and layout scale up on large / high-res screens.
    /// </summary>
    private void ApplyWindowChrome()
    {
        var work = SystemParameters.WorkArea;

        _uiScale = Math.Clamp(work.Height / 1080.0, 1.0, 1.25);
        RootScale.ScaleX = RootScale.ScaleY = _uiScale;
        MinWidth = 720 * _uiScale;
        MinHeight = 420 * _uiScale;

        WindowStartupLocation = WindowStartupLocation.Manual;

        if (_settings.CommanderWindowWidth is double w && w > 200 &&
            _settings.CommanderWindowHeight is double h && h > 200)
        {
            Width = w;
            Height = h;
            Left = _settings.CommanderWindowLeft ?? work.Left + (work.Width - w) / 2;
            Top = _settings.CommanderWindowTop ?? work.Top + (work.Height - h) / 2;
        }
        else
        {
            Width = Math.Round(work.Width * 0.8);
            Height = Math.Round(work.Height * 0.8);
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + (work.Height - Height) / 2;
        }

        // Keep the window on-screen if the saved bounds came from a different display.
        Width = Math.Min(Width, work.Width);
        Height = Math.Min(Height, work.Height);
        Left = Math.Max(work.Left, Math.Min(Left, work.Right - Width));
        Top = Math.Max(work.Top, Math.Min(Top, work.Bottom - Height));
    }

    /// <summary>Builds the saved number of panes (2-4) at their remembered folders.</summary>
    private void InitPanes()
    {
        int count = Math.Clamp(_settings.CommanderPaneCount, MinPanes, MaxPanes);
        var paths = _settings.CommanderPanePaths ?? new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        for (int i = 0; i < count; i++)
            _panes.Add(CreatePane());

        RebuildPaneLayout();
        _active = _panes[0];

        for (int i = 0; i < _panes.Count; i++)
        {
            string dir = i < paths.Count && IsUsablePath(paths[i]) ? paths[i] : home;
            NavigateTo(_panes[i], dir, null, pushHistory: false);
        }

        UpdateActiveVisual();
        UpdatePaneChrome();
    }

    /// <summary>Creates one pane (its visual + wiring) without placing it in the layout yet.</summary>
    private Pane CreatePane()
    {
        var view = new CommanderPane();
        var pane = new Pane { View = view };

        view.List.ItemsSource = pane.Entries;
        view.List.PreviewMouseDown += (_, _) => SetActive(pane);
        view.List.PreviewMouseRightButtonUp += OnListRightButtonUp;
        view.List.MouseDoubleClick += OnListDoubleClick;
        view.List.SelectionChanged += (_, _) => { if (_active == pane) UpdateStatus(); };
        view.CloseButton.Click += (_, _) => ClosePane(pane);

        return pane;
    }

    /// <summary>Lays the current panes out as equal-width columns separated by drag splitters.</summary>
    private void RebuildPaneLayout()
    {
        PaneHost.Children.Clear();
        PaneHost.ColumnDefinitions.Clear();

        for (int i = 0; i < _panes.Count; i++)
        {
            if (i > 0)
            {
                PaneHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                var splitter = new GridSplitter
                {
                    Width = 8,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                    Background = System.Windows.Media.Brushes.Transparent
                };
                Grid.SetColumn(splitter, PaneHost.ColumnDefinitions.Count - 1);
                PaneHost.Children.Add(splitter);
            }

            PaneHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_panes[i].View, PaneHost.ColumnDefinitions.Count - 1);
            PaneHost.Children.Add(_panes[i].View);
        }
    }

    private void OnAddPaneClick(object sender, RoutedEventArgs e)
    {
        if (_panes.Count >= MaxPanes)
            return;

        var pane = CreatePane();
        _panes.Add(pane);
        RebuildPaneLayout();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        NavigateTo(pane, IsUsablePath(_active.Dir) ? _active.Dir : home, null, pushHistory: false);

        SetActive(pane);
        UpdatePaneChrome();
        FocusSelected(pane);
    }

    private void ClosePane(Pane pane)
    {
        if (_panes.Count <= MinPanes)
            return;

        int idx = _panes.IndexOf(pane);
        _panes.Remove(pane);

        if (_active == pane)
            _active = _panes[Math.Min(idx, _panes.Count - 1)];

        RebuildPaneLayout();
        UpdateActiveVisual();
        UpdatePaneChrome();
        UpdateStatus();
        FocusSelected(_active);
    }

    /// <summary>Shows the close (×) button once past the minimum, and hides + at the maximum.</summary>
    private void UpdatePaneChrome()
    {
        bool canClose = _panes.Count > MinPanes;
        foreach (var pane in _panes)
            pane.CloseButton.Visibility = canClose ? Visibility.Visible : Visibility.Collapsed;

        AddPaneButton.Visibility = _panes.Count < MaxPanes ? Visibility.Visible : Visibility.Collapsed;
    }

    private Pane PaneForList(object list) => _panes.FirstOrDefault(p => ReferenceEquals(p.List, list)) ?? _active;

    private Pane NextPane(int dir)
    {
        int i = (_panes.IndexOf(_active) + dir + _panes.Count) % _panes.Count;
        return _panes[i];
    }

    private void FocusAdjacentPane(int dir)
    {
        int i = Math.Clamp(_panes.IndexOf(_active) + dir, 0, _panes.Count - 1);
        SetActive(_panes[i]);
        FocusSelected(_active);
    }

    private static bool IsUsablePath(string? path) =>
        path == DrivesView || (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path));

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveCommanderState();
        base.OnClosing(e);
    }

    private void SaveCommanderState()
    {
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!b.IsEmpty)
        {
            _settings.CommanderWindowLeft = b.Left;
            _settings.CommanderWindowTop = b.Top;
            _settings.CommanderWindowWidth = b.Width;
            _settings.CommanderWindowHeight = b.Height;
        }

        _settings.CommanderPaneCount = _panes.Count;
        _settings.CommanderPanePaths = _panes.Select(p => p.Dir).ToList();
        _settings.Save();
    }

    // --- Navigation ---------------------------------------------------------

    private void NavigateTo(Pane pane, string dir, string? selectName, bool pushHistory)
    {
        string full;
        if (dir == DrivesView)
        {
            full = DrivesView;
        }
        else
        {
            try { full = Path.GetFullPath(dir); }
            catch { return; }
        }

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
        if (pane.Dir == DrivesView)
            return;

        var parent = ParentOf(pane.Dir);

        // At a drive root (e.g. D:\) ParentOf returns the "This PC" drives list; select the drive we
        // came from. Otherwise select the folder we just left inside its parent.
        var from = parent == DrivesView
            ? Path.GetPathRoot(pane.Dir)
            : Path.GetFileName(pane.Dir.TrimEnd(Path.DirectorySeparatorChar));

        NavigateTo(pane, parent, from, pushHistory: true);
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

        // Just mark the source; the actual copy happens on PASTE (Ctrl+V) into the active pane.
        _clipSource = sel.Path;
        StatusText.Text = Loc.T("commander.copiedMark", sel.Name);
    }

    /// <summary>
    /// Copies the full path of the selected file or folder to the Windows clipboard.
    /// Clipboard is ambiguous here (WPF vs WinForms), so it is fully qualified.
    /// </summary>
    private void CopyPathSelected()
    {
        var sel = ActiveSelected();
        if (sel is null || sel.IsParent)
            return;

        try
        {
            System.Windows.Clipboard.SetText(sel.Path);
            StatusText.Text = Loc.T("commander.pathCopied", sel.Path);
        }
        catch
        {
            // Clipboard can transiently fail when another app holds it; ignore.
        }
    }

    private void PasteClipboard()
    {
        if (_clipSource is null || !(File.Exists(_clipSource) || Directory.Exists(_clipSource)))
            return;

        ShellOp(NativeMethods.FO_COPY, _clipSource, _active.Dir, 0);
    }

    private void MoveSelected()
    {
        var sel = ActiveSelected();
        if (sel is null || sel.IsParent || _panes.Count != 2)
            return;

        ShellOp(NativeMethods.FO_MOVE, sel.Path, Other(_active).Dir, 0);
    }

    private void DeleteSelected()
    {
        var sel = ActiveSelected();
        if (sel is null || sel.IsParent)
            return;

        // "Fast delete": permanent removal (no FOF_ALLOWUNDO, so it bypasses the Recycle Bin).
        // FOF_NOCONFIRMATION is deliberately NOT set, so the shell still shows its standard
        // "permanently delete?" prompt before the irreversible operation.
        ShellOp(NativeMethods.FO_DELETE, sel.Path, null, 0);
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
        FocusSelected(_active);
    }

    /// <summary>
    /// "Fast delete folder": a permanent, folder-only wipe modelled on the classic sordum.net
    /// "Fast Delete" shell tool — DEL /F/Q/S force-removes every file in the tree, then RMDIR /Q/S
    /// removes the emptied directory. Both bypass the Recycle Bin and the shell's per-file progress
    /// UI, so clearing a huge tree is far quicker than a normal delete. It is irreversible, so it
    /// only runs on a real folder (never a file or the ".." row) after a YES/NO confirmation.
    /// </summary>
    private void FastDeleteFolderSelected()
    {
        var sel = ActiveSelected();
        if (sel is null || sel.IsParent || !sel.IsFolder)
            return;

        string path = sel.Path;

        // Guard against wiping a whole drive: DEL /S + RMDIR /S have no shell safety net.
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(path) || (root is not null && PathEquals(path, root)))
        {
            Warn(Loc.T("commander.fastDelBlocked"));
            return;
        }

        var confirm = MessageBox.Show(this, Loc.T("commander.fastDelConfirm", sel.Name),
            "M2_Commander", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        StatusText.Text = Loc.T("commander.fastDelRunning", sel.Name);

        Task.Run(() => FastDeleteFolder(path)).ContinueWith(t =>
            Dispatcher.Invoke(() =>
            {
                if (t.Exception is not null)
                    Warn(t.Exception.GetBaseException().Message);
                RefreshBoth();
                FocusSelected(_active);
            }), TaskScheduler.Default);
    }

    /// <summary>
    /// Runs the DEL /F/Q/S + RMDIR /Q/S "fast delete" against <paramref name="target"/>.
    /// A Windows folder path can never contain a double quote (a reserved character), so wrapping
    /// each path in quotes makes the composed command line injection-safe; the one exception is a
    /// literal '%' (cmd would expand it as an environment variable), which takes the .NET fallback.
    /// </summary>
    private static void FastDeleteFolder(string target)
    {
        if (!target.Contains('%'))
        {
            string q = "\"" + target + "\"";
            var psi = new ProcessStartInfo("cmd.exe")
            {
                // /s /c keeps the outer quotes literal; DEL clears files (incl. read-only) fast and
                // silently, RMDIR then removes the emptied tree. WorkingDirectory stays out of the
                // target so its own handle never blocks the removal.
                Arguments = $"/s /c \"del /f/q/s {q} >nul 2>nul & rmdir /q/s {q}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath()
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
        }

        // Final sweep: finish anything cmd left behind (and cover the '%' fallback path) by
        // clearing read-only attributes and deleting the remainder with the .NET APIs.
        if (Directory.Exists(target))
        {
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); }
                catch { /* best effort */ }
            }
            Directory.Delete(target, recursive: true);
        }
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

    private void NewTextFile()
    {
        ShowPrompt(Loc.T("commander.newFile"), "new.txt", name =>
        {
            try
            {
                if (!Path.HasExtension(name))
                    name += ".txt";

                var full = Path.Combine(_active.Dir, name);
                if (!File.Exists(full))
                    File.Create(full).Dispose();

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
        if (_panes.Count != 2)
            return;

        var l = _panes[0].Dir;
        var r = _panes[1].Dir;
        if (string.IsNullOrEmpty(l) || string.IsNullOrEmpty(r))
            return;

        NavigateTo(_panes[0], r, null, pushHistory: true);
        NavigateTo(_panes[1], l, null, pushHistory: true);
    }

    private void RefreshBoth()
    {
        foreach (var pane in _panes)
            RefreshPane(pane);
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

        if (dir == DrivesView)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                string sizeText = string.Empty;
                string detail = drive.DriveType.ToString();
                try
                {
                    if (drive.IsReady)
                    {
                        sizeText = FormatSize(drive.TotalSize);
                        if (!string.IsNullOrWhiteSpace(drive.VolumeLabel))
                            detail = drive.VolumeLabel;
                    }
                }
                catch
                {
                    // Ignore drives we cannot query (e.g. an empty card reader).
                }

                entries.Add(new CommanderEntry
                {
                    Name = drive.Name,
                    Path = drive.Name,
                    IsFolder = true,
                    Glyph = IconGlyph.Drive,
                    SizeText = sizeText,
                    DateText = detail
                });
            }

            return true;
        }

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

            var parentPath = ParentOf(dir);
            entries.Add(new CommanderEntry
            {
                Name = "..",
                Path = parentPath,
                IsFolder = true,
                IsParent = true,
                Glyph = IconGlyph.Folder,
                SizeText = "<UP>",
                DateText = string.Empty
            });

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
        // A fresh listing drops any active type-to-filter so it does not hide the new folder.
        pane.List.Items.Filter = null;
        if (_active == pane)
            _filterText = string.Empty;

        pane.Dir = dir;
        pane.Entries.Clear();
        foreach (var entry in entries)
            pane.Entries.Add(entry);

        pane.Header.Text = DisplayPath(dir);

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
        {
            UpdateStatus();
            if (IsLoaded)
                FocusSelected(pane);
        }
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

        // The filter belongs to the pane being left; clear it before switching.
        _active.List.Items.Filter = null;
        _filterText = string.Empty;

        _active = pane;
        UpdateActiveVisual();
        UpdateStatus();
    }

    private Pane Other(Pane pane) =>
        _panes.Count == 2 ? (ReferenceEquals(_panes[0], pane) ? _panes[1] : _panes[0]) : pane;

    private CommanderEntry? ActiveSelected() => _active.List.SelectedItem as CommanderEntry;

    /// <summary>
    /// Gives keyboard focus to the selected row's container so the next arrow key moves from it.
    /// Setting SelectedIndex programmatically leaves keyboard focus unset, which makes the first
    /// Up/Down after navigating jump to row 0.
    /// </summary>
    private static void FocusSelected(Pane pane)
    {
        pane.List.UpdateLayout();
        if (pane.List.SelectedIndex >= 0
            && pane.List.ItemContainerGenerator.ContainerFromIndex(pane.List.SelectedIndex) is ListBoxItem container)
        {
            container.Focus();
        }
        else
        {
            pane.List.Focus();
        }
    }

    private void UpdateActiveVisual()
    {
        foreach (var pane in _panes)
            ApplyPaneActive(pane, ReferenceEquals(pane, _active));
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
        string filter = _filterText.Length > 0 ? $"    🔍 {_filterText}" : string.Empty;
        StatusText.Text = $"{DisplayPath(pane.Dir)}    ({count})" + selInfo + filter;
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
        FocusSelected(_active);
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

    private void OnHelpClick(object sender, RoutedEventArgs e) => ShowHelp();
    private void OnHelpCloseClick(object sender, RoutedEventArgs e) => CloseHelp();
    private void OnQuitClick(object sender, RoutedEventArgs e) => Close();

    private void ShowHelp()
    {
        HelpList.ItemsSource = HelpRows.Select(r => new HelpRow(r.Keys, Loc.T(r.DescKey))).ToList();
        HelpOverlay.Visibility = Visibility.Visible;
        HelpCloseButton.Focus();
    }

    private void CloseHelp()
    {
        HelpOverlay.Visibility = Visibility.Collapsed;
        FocusSelected(_active);
    }

    // --- Action menu (F1 / right-click) ------------------------------------

    private void OnActionsClick(object sender, RoutedEventArgs e) =>
        ShowActionMenu(ActionsButton, System.Windows.Controls.Primitives.PlacementMode.Top);

    private void OnCommandsClick(object sender, RoutedEventArgs e) => OpenCommandEditor();

    private void OnListRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        SetActive(PaneForList(list));

        // Select the row under the cursor so the action targets it; a right-click on empty space
        // clears the selection, leaving only paste / new-folder / new-file enabled.
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } item)
            item.IsSelected = true;
        else
            list.SelectedIndex = -1;

        ShowActionMenu(list, System.Windows.Controls.Primitives.PlacementMode.MousePoint);
        e.Handled = true;
    }

    private void ShowActionMenu(UIElement target, System.Windows.Controls.Primitives.PlacementMode placement)
    {
        var menu = BuildActionMenu();
        menu.PlacementTarget = target;
        menu.Placement = placement;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Builds the F1 / right-click action menu, enabling entries for the current context: a real
    /// file or folder is required for copy/move/delete/rename; paste needs a prior copy; new folder
    /// and new file always apply to the active pane. Any custom commands follow after a separator.
    /// </summary>
    private ContextMenu BuildActionMenu()
    {
        var sel = ActiveSelected();
        bool hasItem = sel is { IsParent: false };
        bool isFolder = sel is { IsParent: false, IsFolder: true };
        bool canPaste = _clipSource is not null && (File.Exists(_clipSource) || Directory.Exists(_clipSource));
        bool canMove = hasItem && _panes.Count == 2;

        var menu = new ContextMenu();
        menu.Items.Add(ActionItem(Loc.T("commander.menu.copy"), "Ctrl+C", hasItem, CopySelected));
        menu.Items.Add(ActionItem(Loc.T("commander.menu.paste"), "Ctrl+V", canPaste, PasteClipboard));
        menu.Items.Add(ActionItem(Loc.T("commander.menu.move"), string.Empty, canMove, MoveSelected));
        menu.Items.Add(ActionItem(Loc.T("commander.menu.copyPath"), string.Empty, hasItem, CopyPathSelected));
        menu.Items.Add(ActionItem(Loc.T("commander.menu.delete"), "Del", hasItem, DeleteSelected));
        menu.Items.Add(ActionItem(Loc.T("commander.menu.fastDelete"), "Shift+Del", isFolder, FastDeleteFolderSelected));
        menu.Items.Add(ActionItem(Loc.T("commander.menu.rename"), "F2", hasItem, PromptRename));
        menu.Items.Add(ActionItem(Loc.T("commander.menu.newFolder"), string.Empty, true, PromptMkdir));
        menu.Items.Add(ActionItem(Loc.T("commander.menu.newFile"), string.Empty, true, NewTextFile));

        if (_settings.CommanderCommands.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var cmd in _settings.CommanderCommands)
            {
                var captured = cmd;
                menu.Items.Add(ActionItem(cmd.Label, string.Empty, true, () => RunCommand(captured)));
            }
        }

        return menu;
    }

    private static MenuItem ActionItem(string header, string gesture, bool enabled, Action action)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = gesture };
        item.Click += (_, _) => action();
        return item;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        return node as T;
    }

    // --- Custom launcher commands (buttons under the grid + F11 editor) -----

    private void RefreshCommandBar()
    {
        CommandBar.ItemsSource = null;
        CommandBar.ItemsSource = _settings.CommanderCommands
            .Where(c => !string.IsNullOrWhiteSpace(c.Label))
            .ToList();
    }

    private void OnCommandButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: CommanderCommand cmd })
            RunCommand(cmd);
    }

    private void RunCommand(CommanderCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Path))
        {
            Warn(Loc.T("commander.cmd.noPath", cmd.Label));
            OpenCommandEditor();
            return;
        }

        var sel = ActiveSelected();
        string target = sel is { IsParent: false } ? sel.Path : _active.Dir;
        string args = (cmd.Arguments ?? string.Empty).Replace("{path}", target);

        try
        {
            Process.Start(new ProcessStartInfo(cmd.Path, args) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }
    }

    private void OpenCommandEditor()
    {
        _editCommands.Clear();
        foreach (var cmd in _settings.CommanderCommands)
            _editCommands.Add(new CommanderCommand { Label = cmd.Label, Path = cmd.Path, Arguments = cmd.Arguments });

        ForceEnglishCheck.IsChecked = _settings.CommanderForceEnglishInput;
        AutoDetectResult.Text = string.Empty;
        CommandEditorList.ItemsSource = _editCommands;
        CommandEditorOverlay.Visibility = Visibility.Visible;
    }

    private void OnCommandAddClick(object sender, RoutedEventArgs e) =>
        _editCommands.Add(new CommanderCommand { Arguments = "\"{path}\"" });

    /// <summary>
    /// F11 "Auto detect": ensures rows exist for the well-known tools (M2_ST4 / M2_LOG / VS Code)
    /// and fills any blank Program path by looking the label up in the app's file index (VS Code
    /// uses its known install locations). Existing user-set paths are never overwritten.
    /// </summary>
    private void OnCommandAutoDetectClick(object sender, RoutedEventArgs e)
    {
        foreach (var label in new[] { "M2_ST4", "M2_LOG", "VS Code" })
        {
            if (!_editCommands.Any(c => string.Equals(c.Label?.Trim(), label, StringComparison.OrdinalIgnoreCase)))
                _editCommands.Add(new CommanderCommand { Label = label, Arguments = "\"{path}\"" });
        }

        int filled = 0, targets = 0;
        foreach (var cmd in _editCommands)
        {
            if (!string.IsNullOrWhiteSpace(cmd.Path))
                continue;

            targets++;
            var found = DetectProgram(cmd.Label);
            if (found is null)
                continue;

            cmd.Path = found;
            if (string.IsNullOrWhiteSpace(cmd.Arguments))
                cmd.Arguments = "\"{path}\"";
            filled++;
        }

        AutoDetectResult.Text = Loc.T("commander.cmd.autoResult", filled, targets);
    }

    /// <summary>Resolves a launcher label to an executable path (VS Code first, then the index).</summary>
    private string? DetectProgram(string? label)
    {
        var name = label?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;

        if (name.Replace(" ", string.Empty).Equals("vscode", StringComparison.OrdinalIgnoreCase)
            || name.Equals("code", StringComparison.OrdinalIgnoreCase))
            return CommanderCommand.FindVsCode() ?? FindInIndex("Code");

        return FindInIndex(name);
    }

    /// <summary>
    /// Best executable in the file index whose base name matches <paramref name="name"/> (spaces,
    /// underscores and hyphens ignored). Prefers .exe over .bat/.cmd, then the shortest path.
    /// </summary>
    private string? FindInIndex(string name)
    {
        var items = _fileIndex.Items;
        if (items.Length == 0)
            return null;

        string[] exts = { ".exe", ".bat", ".cmd" };
        string wanted = NormalizeName(name);

        string? best = null;
        int bestRank = int.MaxValue;
        foreach (var item in items)
        {
            if (item.IsDirectory)
                continue;

            int extRank = Array.IndexOf(exts, Path.GetExtension(item.Name).ToLowerInvariant());
            if (extRank < 0)
                continue;

            if (!NormalizeName(Path.GetFileNameWithoutExtension(item.Name)).Equals(wanted, StringComparison.Ordinal))
                continue;

            int rank = extRank * 100_000 + item.Path.Length;
            if (rank < bestRank)
            {
                bestRank = rank;
                best = item.Path;
            }
        }

        return best;
    }

    private static string NormalizeName(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private void OnCommandRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: CommanderCommand cmd })
            _editCommands.Remove(cmd);
    }

    private void OnCommandBrowseClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: CommanderCommand cmd })
            return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("commander.cmd.program"),
            Filter = "Programs (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*"
        };

        try
        {
            var dir = Path.GetDirectoryName(cmd.Path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                dialog.InitialDirectory = dir;
        }
        catch { /* ignore malformed path */ }

        if (dialog.ShowDialog(this) == true)
        {
            cmd.Path = dialog.FileName;
            if (string.IsNullOrWhiteSpace(cmd.Label))
                cmd.Label = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void OnCommandSaveClick(object sender, RoutedEventArgs e)
    {
        var cleaned = new List<CommanderCommand>();
        foreach (var c in _editCommands)
        {
            var label = (c.Label ?? string.Empty).Trim();
            var path = (c.Path ?? string.Empty).Trim();
            if (label.Length == 0 && path.Length == 0)
                continue;
            if (label.Length == 0)
                label = Path.GetFileNameWithoutExtension(path);
            if (label.Length == 0)
                continue;

            cleaned.Add(new CommanderCommand
            {
                Label = label,
                Path = path,
                Arguments = string.IsNullOrEmpty(c.Arguments) ? "\"{path}\"" : c.Arguments
            });
        }

        _settings.CommanderCommands = cleaned;
        _settings.CommanderForceEnglishInput = ForceEnglishCheck.IsChecked == true;
        _settings.Save();
        RefreshCommandBar();
        CloseCommandEditor();
    }

    private void OnCommandCancelClick(object sender, RoutedEventArgs e) => CloseCommandEditor();

    private void CloseCommandEditor()
    {
        CommandEditorOverlay.Visibility = Visibility.Collapsed;
        FocusSelected(_active);
    }

    // --- Input --------------------------------------------------------------

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var pane = PaneForList(sender);
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
        if (HelpOverlay.Visibility == Visibility.Visible)
        {
            var help = e.Key == Key.System ? e.SystemKey : e.Key;
            if (help is Key.Escape or Key.F12)
            {
                CloseHelp();
                e.Handled = true;
            }
            return;
        }

        // The F11 editor owns the keyboard while open (so text boxes work); only Esc closes it.
        if (CommandEditorOverlay.Visibility == Visibility.Visible)
        {
            if ((e.Key == Key.System ? e.SystemKey : e.Key) == Key.Escape)
            {
                CloseCommandEditor();
                e.Handled = true;
            }
            return;
        }

        if (PromptOverlay.Visibility == Visibility.Visible)
            return;

        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Alt + navigation. Alt+←/→ focus the pane on the left / right; Alt+↑ goes to the parent
        // folder; Alt+[ / Alt+] are Back / Forward history. Handled before the main switch with a
        // bitwise Alt test so right-Alt / AltGr (reported as Ctrl+Alt) also works.
        if ((mods & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            switch (key)
            {
                case Key.Left:
                    FocusAdjacentPane(-1);
                    e.Handled = true;
                    return;
                case Key.Right:
                    FocusAdjacentPane(+1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    GoUp(_active);
                    e.Handled = true;
                    return;
                case Key.OemOpenBrackets:   // Alt+[  = Back
                    GoBack(_active);
                    e.Handled = true;
                    return;
                case Key.OemCloseBrackets:  // Alt+]  = Forward
                    GoForward(_active);
                    e.Handled = true;
                    return;
            }
        }

        switch (key)
        {
            case Key.Tab:
                SetActive(NextPane(1));
                FocusSelected(_active);
                e.Handled = true;
                break;
            case Key.Enter:
                InvokeSelected();
                e.Handled = true;
                break;
            case Key.Back:
                if (_filterText.Length > 0)
                {
                    _filterText = _filterText[..^1];
                    ApplyFilter();
                }
                else
                {
                    GoUp(_active);
                }
                e.Handled = true;
                break;
            case Key.F1:
                ShowActionMenu(_active.List, System.Windows.Controls.Primitives.PlacementMode.Center);
                e.Handled = true;
                break;
            case Key.F2:
                PromptRename();
                e.Handled = true;
                break;
            case Key.Delete when (mods & ModifierKeys.Shift) == ModifierKeys.Shift:
                FastDeleteFolderSelected();
                e.Handled = true;
                break;
            case Key.Delete:
                DeleteSelected();
                e.Handled = true;
                break;
            case Key.C when mods == ModifierKeys.Control:
                CopySelected();
                e.Handled = true;
                break;
            case Key.V when mods == ModifierKeys.Control:
                PasteClipboard();
                e.Handled = true;
                break;
            case Key.F11:
                OpenCommandEditor();
                e.Handled = true;
                break;
            case Key.F12:
                ShowHelp();
                e.Handled = true;
                break;
            case Key.F10:
                Close();
                e.Handled = true;
                break;
            case Key.Escape:
                // Esc clears an active type-to-filter but no longer closes the window
                // (use F10 or the title-bar close button to quit M2_Commander).
                if (_filterText.Length > 0)
                {
                    ClearFilter();
                    e.Handled = true;
                }
                break;
            case Key.U when mods == ModifierKeys.Control:
                SwapPanes();
                e.Handled = true;
                break;
            case Key.R when mods == ModifierKeys.Control:
                RefreshBoth();
                e.Handled = true;
                break;
        }
    }

    // --- Type-to-filter / input language ------------------------------------

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (HelpOverlay.Visibility == Visibility.Visible
            || CommandEditorOverlay.Visibility == Visibility.Visible
            || PromptOverlay.Visibility == Visibility.Visible)
            return;

        // Ctrl/Alt combos are shortcuts, not filter text.
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
            return;

        string text = e.Text;
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0]))
            return;

        _filterText += text;
        ApplyFilter();
        e.Handled = true;
    }

    private void ApplyFilter()
    {
        var list = _active.List;
        if (_filterText.Length == 0)
        {
            list.Items.Filter = null;
        }
        else
        {
            string query = _filterText.ToLowerInvariant();
            list.Items.Filter = o =>
                o is CommanderEntry entry
                && (entry.IsParent || FuzzyMatcher.Score(query, entry.Name) > FuzzyMatcher.NoMatch);
        }

        SelectBestFilterMatch();
        UpdateStatus();
        if (IsLoaded)
            FocusSelected(_active);
    }

    private void ClearFilter()
    {
        if (_filterText.Length == 0)
            return;

        // Unfiltering keeps the currently selected item selected (WPF preserves SelectedItem).
        _filterText = string.Empty;
        _active.List.Items.Filter = null;
        UpdateStatus();
        FocusSelected(_active);
    }

    /// <summary>Selects the highest-scoring visible entry for the current filter (skipping "..").</summary>
    private void SelectBestFilterMatch()
    {
        var list = _active.List;
        if (list.Items.Count == 0)
            return;

        if (_filterText.Length == 0)
        {
            list.SelectedIndex = 0;
            list.ScrollIntoView(list.SelectedItem);
            return;
        }

        string query = _filterText.ToLowerInvariant();
        int bestIndex = -1;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.Items[i] is not CommanderEntry entry || entry.IsParent)
                continue;

            double score = FuzzyMatcher.Score(query, entry.Name);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        list.SelectedIndex = bestIndex >= 0 ? bestIndex : 0;
        if (list.SelectedItem != null)
            list.ScrollIntoView(list.SelectedItem);
    }

    /// <summary>Switches this window's keyboard layout to English (en-US) when the setting is on.</summary>
    private void SwitchToEnglishInput()
    {
        if (!_settings.CommanderForceEnglishInput)
            return;

        try
        {
            var hkl = NativeMethods.LoadKeyboardLayout("00000409", NativeMethods.KLF_ACTIVATE);
            if (hkl == IntPtr.Zero)
                return;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                NativeMethods.PostMessage(hwnd, (uint)NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
        }
        catch
        {
            // Best effort; input-language switching is non-critical.
        }
    }

    // --- Helpers ------------------------------------------------------------

    private void Warn(string message) =>
        MessageBox.Show(this, message, "M2_Commander", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static string DisplayPath(string dir) => dir == DrivesView ? Loc.T("commander.thisPc") : dir;

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

    /// <summary>
    /// Returns the parent folder of <paramref name="dir"/>, or <see cref="DrivesView"/> when it is a
    /// drive/UNC root. We special-case the root instead of trimming and calling Directory.GetParent:
    /// trimming a bare drive root like "D:\" yields "D:", which Windows treats as a *drive-relative*
    /// path ("the current directory on D:"). Directory.GetParent then resolves it against the process
    /// working directory and returns a folder *inside* the drive, so Alt+\u2191 at a drive root looped
    /// back down into the working folder instead of reaching the "This PC" drive list.
    /// </summary>
    private static string ParentOf(string dir)
    {
        if (string.IsNullOrEmpty(dir) || dir == DrivesView)
            return DrivesView;

        var root = Path.GetPathRoot(dir);
        if (!string.IsNullOrEmpty(root) && PathEquals(dir, root))
            return DrivesView;

        return Directory.GetParent(dir.TrimEnd(Path.DirectorySeparatorChar))?.FullName ?? DrivesView;
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

    private sealed record HelpRow(string Keys, string Desc);

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
        public required CommanderPane View { get; init; }
        public ListBox List => View.List;
        public TextBlock Header => View.PathText;
        public Border PaneBorder => View.Root;
        public Border HeaderBar => View.HeaderBar;
        public System.Windows.Controls.Button CloseButton => View.CloseButton;
        public ObservableCollection<CommanderEntry> Entries { get; } = new();
        public string Dir { get; set; } = string.Empty;
        public Stack<string> Back { get; } = new();
        public Stack<string> Forward { get; } = new();
    }
}
