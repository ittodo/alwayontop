using System.Diagnostics;

namespace TrayAlwaysOnTop;

internal enum ShortcutOverlayExclusionReason
{
    None,
    Manual,
    Fullscreen
}

internal sealed record ShortcutOverlayPolicyResult(
    bool IsExcluded,
    ShortcutOverlayExclusionReason Reason,
    string ProcessName)
{
    public static ShortcutOverlayPolicyResult Allowed(string processName = "") =>
        new(false, ShortcutOverlayExclusionReason.None, processName);
}

internal sealed class ForegroundShortcutOverlayPolicy
{
    private const int FullscreenTolerancePixels = 2;

    public ShortcutOverlayPolicyResult Evaluate(nint foregroundWindow, AppSettings settings)
    {
        if (foregroundWindow == nint.Zero
            || !NativeMethods.IsWindow(foregroundWindow)
            || !NativeMethods.IsWindowVisible(foregroundWindow)
            || NativeMethods.IsIconic(foregroundWindow))
        {
            return ShortcutOverlayPolicyResult.Allowed();
        }

        NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var processId);
        var processName = GetProcessName(processId);
        if (IsManuallyExcluded(processName, settings.ShortcutOverlayExcludedProcesses))
        {
            return new ShortcutOverlayPolicyResult(true, ShortcutOverlayExclusionReason.Manual, processName);
        }

        if (!settings.SuppressShortcutOverlayInFullscreenApps
            || !TryGetWindowBounds(foregroundWindow, out var windowBounds))
        {
            return ShortcutOverlayPolicyResult.Allowed(processName);
        }

        var monitorBounds = Screen.FromHandle(foregroundWindow).Bounds;
        return IsFullscreen(windowBounds, monitorBounds)
            ? new ShortcutOverlayPolicyResult(true, ShortcutOverlayExclusionReason.Fullscreen, processName)
            : ShortcutOverlayPolicyResult.Allowed(processName);
    }

    internal static ShortcutOverlayPolicyResult Evaluate(
        string processName,
        Rectangle windowBounds,
        Rectangle monitorBounds,
        AppSettings settings)
    {
        if (IsManuallyExcluded(processName, settings.ShortcutOverlayExcludedProcesses))
        {
            return new ShortcutOverlayPolicyResult(true, ShortcutOverlayExclusionReason.Manual, processName);
        }

        return settings.SuppressShortcutOverlayInFullscreenApps && IsFullscreen(windowBounds, monitorBounds)
            ? new ShortcutOverlayPolicyResult(true, ShortcutOverlayExclusionReason.Fullscreen, processName)
            : ShortcutOverlayPolicyResult.Allowed(processName);
    }

    internal static string NormalizeProcessName(string? processName)
    {
        var value = (processName ?? string.Empty).Trim();
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.ToLowerInvariant() + ".exe";
    }

    internal static IReadOnlyList<string> NormalizeProcessNames(IEnumerable<string>? processNames) =>
        (processNames ?? [])
            .Select(NormalizeProcessName)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static bool IsManuallyExcluded(string? processName, IEnumerable<string>? exclusions)
    {
        var normalized = NormalizeProcessName(processName);
        return normalized.Length > 0
            && NormalizeProcessNames(exclusions).Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    internal static bool IsFullscreen(Rectangle windowBounds, Rectangle monitorBounds) =>
        Math.Abs(windowBounds.Left - monitorBounds.Left) <= FullscreenTolerancePixels
        && Math.Abs(windowBounds.Top - monitorBounds.Top) <= FullscreenTolerancePixels
        && Math.Abs(windowBounds.Right - monitorBounds.Right) <= FullscreenTolerancePixels
        && Math.Abs(windowBounds.Bottom - monitorBounds.Bottom) <= FullscreenTolerancePixels;

    private static bool TryGetWindowBounds(nint windowHandle, out Rectangle bounds)
    {
        if (NativeMethods.DwmGetWindowAttribute(
                windowHandle,
                NativeMethods.DwmwaExtendedFrameBounds,
                out NativeRect frame,
                System.Runtime.InteropServices.Marshal.SizeOf<NativeRect>()) == 0)
        {
            bounds = frame.ToRectangle();
            return true;
        }

        if (NativeMethods.GetWindowRect(windowHandle, out frame))
        {
            bounds = frame.ToRectangle();
            return true;
        }

        bounds = Rectangle.Empty;
        return false;
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch (ArgumentException) { return string.Empty; }
        catch (InvalidOperationException) { return string.Empty; }
        catch (System.ComponentModel.Win32Exception) { return string.Empty; }
    }
}
