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
    private readonly WinKeyGestureTracker _winKeyTracker = new();
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
                if ((keyboardInput.Flags & NativeMethods.LlkhfInjected) != 0)
                {
                    return NativeMethods.CallNextHookEx(_hookHandle, code, message, data);
                }

                var modifier = GetModifier(keyboardInput.VirtualKeyCode);
                var messageId = message.ToInt32();
                var isKeyDown = messageId is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
                var isKeyUp = messageId is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;

                if (modifier == HotKeyModifiers.Win)
                {
                    if (isKeyDown)
                    {
                        return HandleWinKeyDown(keyboardInput.VirtualKeyCode);
                    }

                    if (isKeyUp)
                    {
                        return HandleWinKeyUp(keyboardInput.VirtualKeyCode, code, message, data);
                    }
                }

                if (modifier != HotKeyModifiers.None)
                {
                    if (isKeyDown && _pressedModifierKeys.Add(keyboardInput.VirtualKeyCode))
                    {
                        _winKeyTracker.MarkOtherModifierActivity();
                        HandleModifiersChanged();
                    }
                    else if (isKeyUp && _pressedModifierKeys.Remove(keyboardInput.VirtualKeyCode))
                    {
                        _winKeyTracker.MarkOtherModifierActivity();
                        HandleModifiersChanged();
                    }
                }
                else if (isKeyDown)
                {
                    _showTimer.Stop();
                    _overlay.HideOverlay();
                    var winKeys = _winKeyTracker.DeliverForShortcut();
                    if (winKeys.Count > 0
                        && InjectShortcutKeyDown(winKeys, keyboardInput.VirtualKeyCode))
                    {
                        return new nint(1);
                    }
                }
            }
            catch
            {
                // A keyboard hook must never interfere with the user's input path.
                _pressedModifierKeys.Clear();
                _winKeyTracker.ResetAndGetDeliveredKeys();
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, code, message, data);
    }

    private nint HandleWinKeyDown(uint virtualKeyCode)
    {
        var otherModifiers = GetPressedModifiers() & ~HotKeyModifiers.Win;
        _winKeyTracker.Press(
            virtualKeyCode,
            Environment.TickCount64,
            otherModifiers != HotKeyModifiers.None);
        if (_pressedModifierKeys.Add(virtualKeyCode))
        {
            HandleModifiersChanged();
        }

        return new nint(1);
    }

    private nint HandleWinKeyUp(uint virtualKeyCode, int code, nint message, nint data)
    {
        var action = _winKeyTracker.Release(
            virtualKeyCode,
            Environment.TickCount64,
            _showTimer.Interval);
        if (_pressedModifierKeys.Remove(virtualKeyCode))
        {
            HandleModifiersChanged();
        }

        switch (action)
        {
            case WinKeyReleaseAction.PassThrough:
                return NativeMethods.CallNextHookEx(_hookHandle, code, message, data);

            case WinKeyReleaseAction.InjectTap:
                InjectWinTap(virtualKeyCode);
                return new nint(1);

            default:
                return new nint(1);
        }
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
            if (modifiers.HasFlag(HotKeyModifiers.Win))
            {
                _winKeyTracker.MarkLongPress();
            }

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
        if (_hookHandle != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
        }

        foreach (var virtualKeyCode in _winKeyTracker.ResetAndGetDeliveredKeys())
        {
            InjectWinKey(virtualKeyCode, keyUp: true);
        }

        _pressedModifierKeys.Clear();
    }

    private static bool InjectShortcutKeyDown(IReadOnlyList<uint> winKeys, uint targetKey)
    {
        var inputs = new NativeInput[winKeys.Count + 1];
        for (var index = 0; index < winKeys.Count; index++)
        {
            inputs[index] = CreateKeyboardInput(winKeys[index], keyUp: false);
        }

        inputs[^1] = CreateKeyboardInput(targetKey, keyUp: false);
        return NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeInput>()) == inputs.Length;
    }

    private static bool InjectWinTap(uint virtualKeyCode)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(virtualKeyCode, keyUp: false),
            CreateKeyboardInput(virtualKeyCode, keyUp: true)
        };
        return NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeInput>()) == inputs.Length;
    }

    private static bool InjectWinKey(uint virtualKeyCode, bool keyUp)
    {
        var inputs = new[] { CreateKeyboardInput(virtualKeyCode, keyUp) };
        return NativeMethods.SendInput(1, inputs, Marshal.SizeOf<NativeInput>()) == 1;
    }

    private static NativeInput CreateKeyboardInput(uint virtualKeyCode, bool keyUp) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Data = new NativeInputUnion
        {
            Keyboard = new NativeKeyboardInput
            {
                VirtualKeyCode = (ushort)virtualKeyCode,
                Flags = (IsExtendedKey(virtualKeyCode) ? NativeMethods.KeyEventExtendedKey : 0)
                    | (keyUp ? NativeMethods.KeyEventKeyUp : 0)
            }
        }
    };

    private static bool IsExtendedKey(uint virtualKeyCode) => virtualKeyCode is
        0x21 or // Page Up
        0x22 or // Page Down
        0x23 or // End
        0x24 or // Home
        0x25 or // Left
        0x26 or // Up
        0x27 or // Right
        0x28 or // Down
        0x2D or // Insert
        0x2E or // Delete
        VkLeftWin or
        VkRightWin or
        VkRightControl or
        VkRightMenu;

    private static HotKeyModifiers GetModifier(uint virtualKeyCode) => virtualKeyCode switch
    {
        VkShift or VkLeftShift or VkRightShift => HotKeyModifiers.Shift,
        VkControl or VkLeftControl or VkRightControl => HotKeyModifiers.Control,
        VkMenu or VkLeftMenu or VkRightMenu => HotKeyModifiers.Alt,
        VkLeftWin or VkRightWin => HotKeyModifiers.Win,
        _ => HotKeyModifiers.None
    };
}
