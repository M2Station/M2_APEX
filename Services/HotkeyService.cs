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

    private readonly AppSettings _settings;
    private NativeMethods.LowLevelKeyboardProc? _proc;
    private IntPtr _hookHandle = IntPtr.Zero;

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
                    _lastCtrlTapTime = 0;
                    RaiseTriggered();
                }
                else
                {
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

    private void RaiseTriggered() => Triggered?.Invoke();

    private static bool IsKeyDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _proc = null;
    }
}
