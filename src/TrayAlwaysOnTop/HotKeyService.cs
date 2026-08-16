using System.ComponentModel;

namespace TrayAlwaysOnTop;

internal sealed class HotKeyService : NativeWindow, IDisposable
{
    private const int HotKeyId = 0x5147;
    private bool _isRegistered;

    public bool IsRegistered => _isRegistered;

    public event EventHandler? Pressed;

    public HotKeyService()
    {
        CreateHandle(new CreateParams
        {
            Caption = "TrayAlwaysOnTop.HotKeyWindow",
            Parent = new nint(-3)
        });
    }

    public bool TryRegister(HotKeyModifiers modifiers, Keys key, out string? error)
    {
        Unregister();

        var nativeModifiers = (uint)(modifiers | HotKeyModifiers.NoRepeat);
        if (!NativeMethods.RegisterHotKey(Handle, HotKeyId, nativeModifiers, (uint)key))
        {
            var exception = new Win32Exception();
            error = $"이 단축키는 다른 프로그램에서 사용 중이거나 등록할 수 없습니다. ({exception.Message})";
            return false;
        }

        _isRegistered = true;
        error = null;
        return true;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotKey && message.WParam.ToInt32() == HotKeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    private void Unregister()
    {
        if (!_isRegistered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(Handle, HotKeyId);
        _isRegistered = false;
    }
}
