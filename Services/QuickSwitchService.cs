using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Threading;

using Listly.Models;
using Listly.Native;
using Listly.Views;

namespace Listly.Services;

/// <summary>
/// Implements Listary's "Quick Switch": while a Windows Explorer file list has focus,
/// typing a keyword pops a small bar and fuzzy-jumps the selection to the matching item.
/// </summary>
public sealed class QuickSwitchService
{
    private const int VkBack = 0x08;
    private const int VkReturn = 0x0D;
    private const int VkEscape = 0x1B;
    private const int VkUp = 0x26;
    private const int VkDown = 0x28;
    private const int VkE = 0x45;

    private readonly AppSettings _settings;
    private readonly QuickSwitchBar _bar;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _watchdog;

    private volatile bool _open;
    private IntPtr _sessionHwnd;
    private ExplorerFolder? _folder;
    private AutomationElement? _list;
    private string _query = string.Empty;
    private List<ExplorerItem> _matches = new();
    private int _matchIndex;
    private CancellationTokenSource? _selectCts;

    public QuickSwitchService(AppSettings settings, QuickSwitchBar bar)
    {
        _settings = settings;
        _bar = bar;
        _dispatcher = System.Windows.Application.Current.Dispatcher;
        _watchdog = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _watchdog.Tick += (_, _) => CheckForeground();
        _bar.ItemInvoked += OnBarItemInvoked;
    }

    /// <summary>
    /// Optional predicate. When it returns <c>true</c> — e.g. the main search window is open —
    /// Quick Switch stays dormant and passes keys through, so focus remains on that window
    /// until the user dismisses it (Esc). Prevents the double-Ctrl search bar and Quick Switch
    /// from fighting over File Explorer keystrokes.
    /// </summary>
    public Func<bool>? Suppressed { get; set; }

    /// <summary>Invoked (on the UI thread) when Ctrl+E is pressed while the Quick Switch bar is open.</summary>
    public Action<string?>? OpenCommanderRequested { get; set; }

    /// <summary>Fast key filter installed into <see cref="HotkeyService"/>. Returns true to swallow.</summary>
    public bool OnKeyDown(int vk, ModifierState mods)
    {
        if (!_settings.EnableQuickSwitch)
            return false;

        // Stand down entirely while the main search window owns the screen.
        if (Suppressed?.Invoke() == true)
        {
            if (_open)
                Post(Close);
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();

        if (_open)
        {
            // Only keep intercepting while the ORIGINATING Explorer file list is still
            // focused. The instant focus moves anywhere else, close and let the key pass
            // through. This guarantees typing can never be blocked in other windows.
            bool stillActive = _sessionHwnd != IntPtr.Zero
                && foreground == _sessionHwnd
                && ExplorerAccess.IsExplorerListFocused(foreground);

            if (!stillActive)
            {
                Post(Close);
                return false;
            }

            // Ctrl+E: hand the highlighted item (or the current folder) to M2_Commander.
            if (mods.Ctrl && !mods.Alt && !mods.Win && vk == VkE)
            {
                Post(() =>
                {
                    string? target = _matches.Count > 0 ? _matches[_matchIndex].Path : _folder?.Path;
                    OpenCommanderRequested?.Invoke(target);
                    Close();
                });
                return true;
            }

            // Let Explorer shortcuts through (e.g. Ctrl+C copies the highlighted file).
            if (mods.Ctrl || mods.Alt || mods.Win)
            {
                Post(Close);
                return false;
            }

            switch (vk)
            {
                case VkEscape:
                    Post(Close);
                    return true;
                case VkReturn:
                    Post(OpenSelected);
                    return true;
                case VkBack:
                    Post(Backspace);
                    return true;
                case VkUp:
                    Post(() => Move(-1));
                    return true;
                case VkDown:
                    Post(() => Move(1));
                    return true;
                default:
                    if (TryMapChar(vk, mods.Shift, out char typed))
                    {
                        Post(() => Append(typed));
                        return true;
                    }

                    // Any other key (Tab, Del, arrows, F-keys) dismisses and passes through.
                    Post(Close);
                    return false;
            }
        }

        // Not open: only start a session when a printable key is typed while an
        // Explorer file list has focus. Never swallow when modifiers are held.
        if (mods.Ctrl || mods.Alt || mods.Win)
            return false;

        if (TryMapChar(vk, mods.Shift, out char first) && ExplorerAccess.IsExplorerListFocused(foreground))
        {
            _sessionHwnd = foreground;
            _open = true; // arm synchronously so following keys route here
            Post(() => Start(foreground, first));
            return true;
        }

        return false;
    }

    private void Start(IntPtr hwnd, char first)
    {
        try
        {
            var folder = ExplorerAccess.GetFolder(hwnd);
            if (folder is null || folder.Items.Count == 0)
            {
                _open = false;
                _sessionHwnd = IntPtr.Zero;
                return;
            }

            _folder = folder;
            _query = first.ToString();
            _list = null;
            _matches = new List<ExplorerItem>();
            _matchIndex = 0;

            _bar.ShowFor(hwnd, _settings.QuickSwitchPosition);
            Rematch();
            CaptureListAsync(hwnd);

            _watchdog.Start();
        }
        catch
        {
            // Never let a failure leave the session armed (which would swallow keys).
            _open = false;
            _sessionHwnd = IntPtr.Zero;
            _folder = null;
            try { _bar.HideBar(); }
            catch { /* ignore */ }
        }
    }

    private void CaptureListAsync(IntPtr hwnd)
    {
        Task.Run(() =>
        {
            var list = ExplorerAccess.GetItemsList(hwnd);
            _dispatcher.BeginInvoke(() =>
            {
                if (!_open)
                    return;
                _list = list;
                SelectCurrent();
            });
        });
    }

    private void Append(char c)
    {
        if (!_open)
            return;
        _query += c;
        Rematch();
    }

    private void Backspace()
    {
        if (!_open)
            return;

        if (_query.Length <= 1)
        {
            Close();
            return;
        }

        _query = _query[..^1];
        Rematch();
    }

    private void Move(int delta)
    {
        if (!_open || _matches.Count == 0)
            return;

        _matchIndex = (_matchIndex + delta + _matches.Count) % _matches.Count;
        _bar.SetSelected(_matchIndex);
        SelectCurrent();
    }

    private void Rematch()
    {
        if (_folder is null)
            return;

        var queryLower = _query.ToLowerInvariant();
        var scored = new List<(double Score, ExplorerItem Item)>();

        foreach (var item in _folder.Items)
        {
            double score = FuzzyMatcher.Score(queryLower, item.Name);
            if (double.IsNegativeInfinity(score))
                continue;

            scored.Add((score + (item.IsFolder ? 0.3 : 0), item));
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        _matches = scored.Take(100).Select(x => x.Item).ToList();
        _matchIndex = 0;

        PublishResults(queryLower);
        SelectCurrent();
    }

    private void PublishResults(string queryLower)
    {
        var results = new List<SearchResult>(_matches.Count);
        foreach (var item in _matches)
        {
            results.Add(new SearchResult
            {
                Title = item.Name,
                Path = item.Path,
                Kind = item.IsFolder ? ResultKind.Folder : ResultKind.File,
                Glyph = IconGlyph.ForFile(item.Path, item.IsFolder),
                MatchedIndices = FuzzyMatcher.GetMatchedIndices(queryLower, item.Name)
            });
        }

        _bar.SetResults(_query, results, _matches.Count == 0 ? 0 : _matchIndex);
    }

    private void SelectCurrent()
    {
        if (_list is null || _matches.Count == 0)
            return;

        var name = _matches[_matchIndex].Name;
        var list = _list;

        _selectCts?.Cancel();
        var cts = _selectCts = new CancellationTokenSource();
        var token = cts.Token;

        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
                return;
            ExplorerAccess.SelectByName(list, name);
        });
    }

    private void OpenSelected()
    {
        if (_open && _matches.Count > 0 && _folder is not null)
        {
            var item = _matches[_matchIndex];
            var hwnd = _folder.Hwnd;

            if (!ExplorerAccess.InvokeItem(hwnd, item.Path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                }
                catch
                {
                    // Ignore open failures.
                }
            }
        }

        Close();
    }

    private void Close()
    {
        _open = false;
        _sessionHwnd = IntPtr.Zero;
        _watchdog.Stop();
        _selectCts?.Cancel();
        _folder = null;
        _list = null;
        _matches = new List<ExplorerItem>();
        _query = string.Empty;
        _bar.HideBar();
    }

    private void CheckForeground()
    {
        if (!_open)
        {
            _watchdog.Stop();
            return;
        }

        if (_folder is not null && NativeMethods.GetForegroundWindow() != _folder.Hwnd)
            Close();
    }

    private void OnBarItemInvoked(int index)
    {
        if (!_open || index < 0 || index >= _matches.Count)
            return;

        _matchIndex = index;
        OpenSelected();
    }

    private void Post(Action action) => _dispatcher.BeginInvoke(action);

    private static bool TryMapChar(int vk, bool shift, out char c)
    {
        // Letters A-Z (match is case-insensitive, so always lower).
        if (vk is >= 0x41 and <= 0x5A)
        {
            c = (char)('a' + (vk - 0x41));
            return true;
        }

        // Top-row digits (skip when Shift is held, which yields symbols).
        if (vk is >= 0x30 and <= 0x39 && !shift)
        {
            c = (char)('0' + (vk - 0x30));
            return true;
        }

        // Numpad digits.
        if (vk is >= 0x60 and <= 0x69)
        {
            c = (char)('0' + (vk - 0x60));
            return true;
        }

        switch (vk)
        {
            case 0x20: // Space
                c = ' ';
                return true;
            case 0xBD: // OEM_MINUS
                c = shift ? '_' : '-';
                return true;
            case 0xBE: // OEM_PERIOD
                c = '.';
                return true;
            default:
                c = '\0';
                return false;
        }
    }
}
