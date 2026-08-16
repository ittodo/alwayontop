using System.Runtime.InteropServices;

namespace TrayAlwaysOnTop;

internal sealed class HotKeyAvailabilityScanner
{
    private const int ProbeId = 0x6251;
    private const int ErrorHotKeyAlreadyRegistered = 1409;

    private static readonly HotKeyModifiers[] ModifierSets = BuildModifierSets();
    private static readonly Keys[] KeysToTest = BuildKeysToTest();

    public IReadOnlyList<DetectedHotKey> Scan(AppSettings settings, bool appHotKeyRegistered)
    {
        var results = new List<DetectedHotKey>();
        using var probeWindow = new ProbeWindow();

        foreach (var modifiers in ModifierSets)
        {
            foreach (var key in KeysToTest)
            {
                if (appHotKeyRegistered && modifiers == settings.Modifiers && key == settings.Key)
                {
                    continue;
                }

                var registered = NativeMethods.RegisterHotKey(
                    probeWindow.Handle,
                    ProbeId,
                    (uint)(modifiers | HotKeyModifiers.NoRepeat),
                    (uint)key);

                if (registered)
                {
                    NativeMethods.UnregisterHotKey(probeWindow.Handle, ProbeId);
                }
                else if (Marshal.GetLastWin32Error() == ErrorHotKeyAlreadyRegistered)
                {
                    results.Add(new DetectedHotKey(
                        HotKeyFormatter.Format(modifiers, key),
                        "Windows 또는 다른 프로그램"));
                }
            }
        }

        return results;
    }

    private static HotKeyModifiers[] BuildModifierSets()
    {
        var baseSets = new[]
        {
            HotKeyModifiers.Control,
            HotKeyModifiers.Alt,
            HotKeyModifiers.Win,
            HotKeyModifiers.Control | HotKeyModifiers.Alt,
            HotKeyModifiers.Control | HotKeyModifiers.Win,
            HotKeyModifiers.Alt | HotKeyModifiers.Win,
            HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Win
        };

        return baseSets
            .SelectMany(modifiers => new[] { modifiers, modifiers | HotKeyModifiers.Shift })
            .ToArray();
    }

    private static Keys[] BuildKeysToTest()
    {
        var keys = new List<Keys>();
        for (var key = Keys.A; key <= Keys.Z; key++) keys.Add(key);
        for (var key = Keys.D0; key <= Keys.D9; key++) keys.Add(key);
        for (var key = Keys.F1; key <= Keys.F24; key++) keys.Add(key);

        keys.AddRange(
        [
            Keys.Space,
            Keys.Tab,
            Keys.Escape,
            Keys.Return,
            Keys.Insert,
            Keys.Delete,
            Keys.Home,
            Keys.End,
            Keys.Prior,
            Keys.Next,
            Keys.Left,
            Keys.Right,
            Keys.Up,
            Keys.Down,
            Keys.Snapshot
        ]);
        return keys.ToArray();
    }

    private sealed class ProbeWindow : NativeWindow, IDisposable
    {
        public ProbeWindow()
        {
            CreateHandle(new CreateParams
            {
                Caption = "TrayAlwaysOnTop.HotKeyProbe",
                Parent = new nint(-3)
            });
        }

        public void Dispose()
        {
            NativeMethods.UnregisterHotKey(Handle, ProbeId);
            DestroyHandle();
        }
    }
}

internal sealed record DetectedHotKey(string Shortcut, string Source);
