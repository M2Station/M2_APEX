using System.Diagnostics;
using System.Runtime.InteropServices;

using Listly.Models;
using Listly.Native;

namespace Listly.Services;

/// <summary>Snapshot of modifier keys at the moment a key was pressed.</summary>
public readonly record struct ModifierState(bool Ctrl, bool Alt, bool Shift, bool Win);

/// <summary>
/// Installs a low-level keyboard hook that raises <see cref="Triggered"/> when the
/// user double-taps Ctrl (Listary's signature gesture) or presses Alt+Space.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int VkSpace = 0x20;
    private const uint LlkhfAltDown = 0x20;
    private const uint LlkhfInjected = 0x10;

    // Process-name prefixes that indicate a KVM / mouse-sharing tool is running on this PC.
    private static readonly string[] KvmProcessHints =
        { "synergy", "deskflow", "barrier", "sharemouse", "mousewithoutborders", "inputdirector" };

    private readonly AppSettings _settings;
    private NativeMethods.LowLevelKeyboardProc? _proc;
    private IntPtr _hookHandle = IntPtr.Zero;
    private System.Threading.Timer? _kvmTimer;
    private volatile bool _kvmPresent;

    private bool _ctrlHeld;
    private bool _otherKeyDuringCtrl;
    private int _lastCtrlTapTime;

    public HotkeyService(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Raised when a configured trigger gesture is detected.</summary>
    public event Action? Triggered;

    /// <summary>
    /// Optional fast filter invoked for every key-down. Return <c>true</c> to swallow the key.
    /// Must be lightweight (no blocking work) since it runs inside the keyboard hook.
    /// </summary>
    public Func<int, ModifierState, bool>? KeyFilter { get; set; }

    /// <summary>Installs the hook. Must be called from a thread with a message loop (the UI thread).</summary>
    public void Install()
    {
        if (_hookHandle != IntPtr.Zero)
            return;

        _proc = HookCallback;
        var moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, moduleHandle, 0);

        // Poll off the hook thread for a running KVM / mouse-sharing tool, so the hook can cheaply
        // decide whether to trust the "local pointer is hidden" signal.
        _kvmTimer = new System.Threading.Timer(_ => RefreshKvmPresence(), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = (int)wParam;
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

            if (HandleKey(message, data))
                return (IntPtr)1; // swallow the key (used for Alt+Space)
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private bool HandleKey(int message, NativeMethods.KBDLLHOOKSTRUCT data)
    {
        // Skip input that isn't meant for this PC: synthetic keystrokes, or — while a KVM is
        // running — keys typed after the shared pointer moved to another computer.
        if (_settings.IgnoreForeignInput && IsForeignInput(data))
            return false;

        bool isDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
        bool isUp = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
        int vk = (int)data.vkCode;

        // Alt+Space
        if (_settings.EnableAltSpace && isDown && vk == VkSpace && (data.flags & LlkhfAltDown) != 0)
        {
            RaiseTriggered();
            return true;
        }

        // Quick Switch and other key interceptors.
        if (isDown && KeyFilter is not null)
        {
            var mods = new ModifierState(
                IsKeyDown(NativeMethods.VK_CONTROL),
                IsKeyDown(NativeMethods.VK_MENU),
                IsKeyDown(NativeMethods.VK_SHIFT),
                IsKeyDown(NativeMethods.VK_LWIN) || IsKeyDown(NativeMethods.VK_RWIN));

            if (KeyFilter(vk, mods))
                return true;
        }

        bool isCtrl = vk is NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL or NativeMethods.VK_CONTROL;

        if (isDown)
        {
            if (isCtrl)
            {
                if (!_ctrlHeld)
                {
                    _ctrlHeld = true;
                    _otherKeyDuringCtrl = false;
                }
            }
            else if (_ctrlHeld)
            {
                _otherKeyDuringCtrl = true;
            }
        }
        else if (isUp && isCtrl)
        {
            _ctrlHeld = false;

            if (_settings.EnableDoubleCtrl && !_otherKeyDuringCtrl)
            {
                int now = Environment.TickCount;
                int delta = now - _lastCtrlTapTime;

                if (_lastCtrlTapTime != 0 && delta > 0 && delta <= _settings.DoubleCtrlThresholdMs)
                {
                    LogDoubleCtrl(delta, triggered: true);
                    _lastCtrlTapTime = 0;
                    RaiseTriggered();
                }
                else
                {
                    if (_lastCtrlTapTime != 0 && delta > 0)
                        LogDoubleCtrl(delta, triggered: false);
                    _lastCtrlTapTime = now;
                }
            }
            else
            {
                _lastCtrlTapTime = 0;
            }
        }

        return false;
    }

    /// <summary>
    /// Records the gap between the two Ctrl taps to the performance log so the double-Ctrl threshold can
    /// be tuned from real data. Off-loaded to the thread pool: the low-level keyboard hook thread must
    /// never block on disk I/O (it runs under a system timeout).
    /// </summary>
    private void LogDoubleCtrl(int deltaMs, bool triggered)
    {
        if (!PerfLog.Enabled)
            return;

        int threshold = _settings.DoubleCtrlThresholdMs;
        string outcome = triggered ? "triggered" : "too slow (> threshold)";
        _ = Task.Run(() => PerfLog.Mark($"double-Ctrl gap {deltaMs} ms (threshold {threshold} ms) \u2192 {outcome}"));
    }

    private bool IsForeignInput(NativeMethods.KBDLLHOOKSTRUCT data)
    {
        bool injected = (data.flags & LlkhfInjected) != 0;
        bool pointerHidden = IsPointerHidden();

        // On a KVM / mouse-sharing CLIENT (e.g. Synergy on the secondary PC) the shared keyboard is
        // delivered as INJECTED input while this PC is the active screen — its pointer is visible. That
        // is the legitimate local input here, so accept it; otherwise double-Ctrl / Alt+Space would never
        // fire on the secondary machine (every tap looks synthetic).
        if (injected && _kvmPresent && !pointerHidden)
            return false;

        // Other synthetic keystrokes — a macro, remote desktop, or a KVM injecting another host's input —
        // are foreign.
        if (injected)
            return true;

        // A KVM is running and the local pointer is hidden — this is how those tools show the shared
        // pointer has crossed to another PC — so physical keys typed here are meant for that computer.
        return _kvmPresent && pointerHidden;
    }

    private static bool IsPointerHidden()
    {
        var info = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
        if (!NativeMethods.GetCursorInfo(ref info))
            return false;

        // flags == 0: hidden via ShowCursor(false). hCursor == 0: a blank/null cursor. Touch
        // suppression (CURSOR_SUPPRESSED) keeps a real hCursor, so it is not mistaken for hidden.
        return info.flags == 0 || info.hCursor == IntPtr.Zero;
    }

    private void RefreshKvmPresence()
    {
        if (!_settings.IgnoreForeignInput)
        {
            _kvmPresent = false;
            return;
        }

        try
        {
            bool present = false;
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    string name = process.ProcessName;
                    foreach (var hint in KvmProcessHints)
                    {
                        if (name.StartsWith(hint, StringComparison.OrdinalIgnoreCase))
                        {
                            present = true;
                            break;
                        }
                    }
                }
                catch
                {
                    // Process exited between enumeration and read; ignore.
                }
                finally
                {
                    process.Dispose();
                }

                if (present)
                    break;
            }

            _kvmPresent = present;
        }
        catch
        {
            // Keep the previous value if the process scan fails.
        }
    }

    private void RaiseTriggered() => Triggered?.Invoke();

    private static bool IsKeyDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        _kvmTimer?.Dispose();
        _kvmTimer = null;

        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _proc = null;
    }
}
