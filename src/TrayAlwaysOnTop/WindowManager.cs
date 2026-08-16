using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TrayAlwaysOnTop;

internal sealed class WindowManager : IDisposable
{
    private readonly uint _currentProcessId = (uint)Environment.ProcessId;
    private readonly HashSet<nint> _managedPins = [];
    private readonly Dictionary<nint, BorderOverlay> _overlays = [];
    private bool _showBorder;
    private bool _showPinToggle;

    public nint LastExternalWindow { get; private set; }

    public event EventHandler<ToggleResult>? OverlayToggleCompleted;

    public WindowManager(bool showBorder, bool showPinToggle)
    {
        _showBorder = showBorder;
        _showPinToggle = showPinToggle;
    }

    public void CaptureForegroundWindow()
    {
        var handle = Normalize(NativeMethods.GetForegroundWindow());
        if (IsCandidate(handle))
        {
            LastExternalWindow = handle;
        }
    }

    public void SetOverlayOptions(bool showBorder, bool showPinToggle)
    {
        _showBorder = showBorder;
        _showPinToggle = showPinToggle;
        if (!showBorder && !showPinToggle)
        {
            foreach (var overlay in _overlays.Values)
            {
                overlay.Dispose();
            }

            _overlays.Clear();
            return;
        }

        foreach (var handle in _managedPins)
        {
            EnsureOverlay(handle);
            _overlays[handle].SetOptions(showBorder, showPinToggle);
        }
    }

    public IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        var windows = new List<WindowInfo>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            handle = Normalize(handle);
            if (!IsCandidate(handle) || windows.Any(window => window.Handle == handle))
            {
                return true;
            }

            var title = GetWindowTitle(handle);
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            var processName = GetProcessName(processId);

            windows.Add(new WindowInfo(handle, title, processName));
            return true;
        }, nint.Zero);

        return windows
            .OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public WindowInfo? GetLastExternalWindowInfo()
    {
        var handle = LastExternalWindow;
        if (!IsCandidate(handle))
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        return new WindowInfo(handle, GetWindowTitle(handle), GetProcessName(processId));
    }

    public bool IsTopmost(nint handle)
    {
        handle = Normalize(handle);
        return handle != nint.Zero
            && NativeMethods.IsWindow(handle)
            && (NativeMethods.GetWindowLongPtrW(handle, NativeMethods.GwlExStyle).ToInt64() & NativeMethods.WsExTopmost) != 0;
    }

    public ToggleResult Toggle(nint handle)
    {
        handle = Normalize(handle);
        if (!IsCandidate(handle))
        {
            return ToggleResult.Failed("고정할 창을 찾지 못했습니다. 대상 창을 먼저 선택해 주세요.");
        }

        var makeTopmost = !IsTopmost(handle);
        var success = NativeMethods.SetWindowPos(
            handle,
            makeTopmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNoTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);

        if (!success)
        {
            var exception = new Win32Exception(Marshal.GetLastWin32Error());
            return ToggleResult.Failed($"창 상태를 변경하지 못했습니다. {exception.Message}");
        }

        if (makeTopmost)
        {
            _managedPins.Add(handle);
            EnsureOverlay(handle);
        }
        else
        {
            _managedPins.Remove(handle);
            RemoveOverlay(handle);
        }

        return ToggleResult.Succeeded(handle, GetWindowTitle(handle), makeTopmost);
    }

    public void Synchronize()
    {
        foreach (var handle in _managedPins.ToArray())
        {
            if (!NativeMethods.IsWindow(handle) || !IsTopmost(handle))
            {
                _managedPins.Remove(handle);
                RemoveOverlay(handle);
                continue;
            }

            if (_showBorder || _showPinToggle)
            {
                EnsureOverlay(handle);
                _overlays[handle].Synchronize();
            }
        }
    }

    public void Dispose()
    {
        foreach (var overlay in _overlays.Values)
        {
            overlay.Dispose();
        }

        _overlays.Clear();

        foreach (var handle in _managedPins)
        {
            if (NativeMethods.IsWindow(handle))
            {
                NativeMethods.SetWindowPos(
                    handle,
                    NativeMethods.HwndNoTopmost,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
            }
        }

        _managedPins.Clear();
    }

    private void EnsureOverlay(nint handle)
    {
        if ((!_showBorder && !_showPinToggle) || _overlays.ContainsKey(handle))
        {
            return;
        }

        _overlays[handle] = new BorderOverlay(handle, _showBorder, _showPinToggle, () =>
        {
            var result = Toggle(handle);
            OverlayToggleCompleted?.Invoke(this, result);
        });
    }

    private void RemoveOverlay(nint handle)
    {
        if (_overlays.Remove(handle, out var overlay))
        {
            overlay.Dispose();
        }
    }

    private bool IsCandidate(nint handle)
    {
        if (handle == nint.Zero
            || handle == NativeMethods.GetShellWindow()
            || !NativeMethods.IsWindow(handle)
            || !NativeMethods.IsWindowVisible(handle)
            || string.IsNullOrWhiteSpace(GetWindowTitle(handle)))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == _currentProcessId)
        {
            return false;
        }

        var className = new StringBuilder(128);
        NativeMethods.GetClassNameW(handle, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        var cloaked = 0;
        var result = NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaCloaked, out cloaked, sizeof(int));
        return result != 0 || cloaked == 0;
    }

    private static nint Normalize(nint handle)
    {
        if (handle == nint.Zero)
        {
            return nint.Zero;
        }

        var root = NativeMethods.GetAncestor(handle, NativeMethods.GaRoot);
        return root == nint.Zero ? handle : root;
    }

    private static string GetWindowTitle(nint handle)
    {
        var length = NativeMethods.GetWindowTextLengthW(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder(length + 1);
        NativeMethods.GetWindowTextW(handle, text, text.Capacity);
        return text.ToString();
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (Win32Exception)
        {
            return string.Empty;
        }
    }
}

internal sealed record ToggleResult(bool Success, nint Handle, string Title, bool IsTopmost, string? Error)
{
    public static ToggleResult Succeeded(nint handle, string title, bool isTopmost) =>
        new(true, handle, title, isTopmost, null);

    public static ToggleResult Failed(string error) =>
        new(false, nint.Zero, string.Empty, false, error);
}
