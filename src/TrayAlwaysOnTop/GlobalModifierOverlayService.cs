using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TrayAlwaysOnTop;

internal sealed class GlobalModifierOverlayService : IDisposable
{
    private const uint VkShift = 0x10;
    private const uint VkControl = 0x11;
    private const uint VkMenu = 0x12;
    private const uint VkLeftWin = 0x5B;
    private const uint VkRightWin = 0x5C;
    private const uint VkLeftShift = 0xA0;
    private const uint VkRightShift = 0xA1;
    private const uint VkLeftControl = 0xA2;
    private const uint VkRightControl = 0xA3;
    private const uint VkLeftMenu = 0xA4;
    private const uint VkRightMenu = 0xA5;

    private readonly Func<AppSettings> _getSettings;
    private readonly Func<bool> _isAppHotKeyRegistered;
    private readonly ModifierShortcutOverlayForm _overlay = new();
    private readonly System.Windows.Forms.Timer _showTimer = new() { Interval = 420 };
    private readonly HashSet<uint> _pressedModifierKeys = [];
    private readonly NativeMethods.LowLevelKeyboardProc _hookCallback;
    private nint _hookHandle;
    private bool _disposed;

    public GlobalModifierOverlayService(
        Func<AppSettings> getSettings,
        Func<bool> isAppHotKeyRegistered)
    {
        _getSettings = getSettings;
        _isAppHotKeyRegistered = isAppHotKeyRegistered;
        _hookCallback = HookCallback;
        _showTimer.Tick += (_, _) => ShowPendingOverlay();
    }

    public bool TrySetEnabled(bool enabled, out string? error)
    {
        if (_disposed)
        {
            error = "전역 단축키 안내 서비스가 이미 종료되었습니다.";
            return false;
        }

        if (!enabled)
        {
            Disable();
            error = null;
            return true;
        }

        if (_hookHandle != nint.Zero)
        {
            error = null;
            return true;
        }

        var moduleHandle = NativeMethods.GetModuleHandleW(null);
        _hookHandle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WhKeyboardLl,
            _hookCallback,
            moduleHandle,
            0);
        if (_hookHandle == nint.Zero)
        {
            error = $"전역 키보드 감지를 시작하지 못했습니다. ({new Win32Exception().Message})";
            return false;
        }

        error = null;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disable();
        _showTimer.Dispose();
        _overlay.Dispose();
    }

    private nint HookCallback(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            try
            {
                var keyboardInput = Marshal.PtrToStructure<LowLevelKeyboardInput>(data);
                var modifier = GetModifier(keyboardInput.VirtualKeyCode);
                var messageId = message.ToInt32();
                var isKeyDown = messageId is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
                var isKeyUp = messageId is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;

                if (modifier != HotKeyModifiers.None)
                {
                    if (isKeyDown && _pressedModifierKeys.Add(keyboardInput.VirtualKeyCode))
                    {
                        HandleModifiersChanged();
                    }
                    else if (isKeyUp && _pressedModifierKeys.Remove(keyboardInput.VirtualKeyCode))
                    {
                        HandleModifiersChanged();
                    }
                }
                else if (isKeyDown)
                {
                    _showTimer.Stop();
                    _overlay.HideOverlay();
                }
            }
            catch
            {
                // A keyboard hook must never interfere with the user's input path.
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, code, message, data);
    }

    private void HandleModifiersChanged()
    {
        var modifiers = GetPressedModifiers();
        if (modifiers == HotKeyModifiers.None)
        {
            _showTimer.Stop();
            _overlay.HideOverlay();
            return;
        }

        if (_overlay.Visible)
        {
            _overlay.ShowFor(modifiers, _getSettings(), _isAppHotKeyRegistered());
            return;
        }

        _showTimer.Stop();
        _showTimer.Start();
    }

    private void ShowPendingOverlay()
    {
        _showTimer.Stop();
        var modifiers = GetPressedModifiers();
        if (modifiers != HotKeyModifiers.None)
        {
            _overlay.ShowFor(modifiers, _getSettings(), _isAppHotKeyRegistered());
        }
    }

    private HotKeyModifiers GetPressedModifiers()
    {
        var modifiers = HotKeyModifiers.None;
        foreach (var key in _pressedModifierKeys)
        {
            modifiers |= GetModifier(key);
        }

        return modifiers;
    }

    private void Disable()
    {
        _showTimer.Stop();
        _overlay.HideOverlay();
        _pressedModifierKeys.Clear();
        if (_hookHandle == nint.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = nint.Zero;
    }

    private static HotKeyModifiers GetModifier(uint virtualKeyCode) => virtualKeyCode switch
    {
        VkShift or VkLeftShift or VkRightShift => HotKeyModifiers.Shift,
        VkControl or VkLeftControl or VkRightControl => HotKeyModifiers.Control,
        VkMenu or VkLeftMenu or VkRightMenu => HotKeyModifiers.Alt,
        VkLeftWin or VkRightWin => HotKeyModifiers.Win,
        _ => HotKeyModifiers.None
    };
}
