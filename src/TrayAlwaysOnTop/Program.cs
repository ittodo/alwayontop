using Velopack;

namespace TrayAlwaysOnTop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack must process install/update hooks before any UI or singleton logic.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(true)
            .OnBeforeUninstallFastCallback(version =>
            {
                StartupManager.TrySetEnabled(false, out var ignoredError);
            })
            .Run();

        if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (args.Contains("--hook-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            if (!WinKeyGestureStateSmokeTest())
            {
                Environment.ExitCode = 3;
                return;
            }

            ApplicationConfiguration.Initialize();
            var settings = new AppSettings();
            using var overlayService = new GlobalModifierOverlayService(
                () => settings.Copy(),
                () => true);
            if (!overlayService.TrySetEnabled(true, out _))
            {
                Environment.ExitCode = 2;
                return;
            }

            overlayService.TrySetEnabled(false, out _);
            return;
        }

        using var singleInstance = new Mutex(true, "Local\\TrayAlwaysOnTop.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Tray Always On Top이 이미 실행 중입니다. 알림 영역을 확인해 주세요.",
                "Tray Always On Top",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        Application.Run(new TrayApplicationContext());
    }

    private static bool WinKeyGestureStateSmokeTest()
    {
        const uint leftWin = 0x5B;
        const int threshold = 420;
        var expectedNativeInputSize = nint.Size == 8 ? 40 : 28;
        if (System.Runtime.InteropServices.Marshal.SizeOf<NativeInput>() != expectedNativeInputSize)
        {
            return false;
        }

        var shortTap = new WinKeyGestureTracker();
        shortTap.Press(leftWin, 1000, combinedWithOtherModifier: false);
        if (shortTap.Release(leftWin, 1100, threshold) != WinKeyReleaseAction.InjectTap)
        {
            return false;
        }

        var longPress = new WinKeyGestureTracker();
        longPress.Press(leftWin, 1000, combinedWithOtherModifier: false);
        if (longPress.Release(leftWin, 1500, threshold) != WinKeyReleaseAction.Suppress)
        {
            return false;
        }

        var shortcut = new WinKeyGestureTracker();
        shortcut.Press(leftWin, 1000, combinedWithOtherModifier: false);
        if (shortcut.DeliverForShortcut().Count != 1
            || shortcut.Release(leftWin, 1100, threshold) != WinKeyReleaseAction.PassThrough)
        {
            return false;
        }

        var combined = new WinKeyGestureTracker();
        combined.Press(leftWin, 1000, combinedWithOtherModifier: true);
        if (combined.Release(leftWin, 1100, threshold) != WinKeyReleaseAction.Suppress)
        {
            return false;
        }

        var modifierAddedLater = new WinKeyGestureTracker();
        modifierAddedLater.Press(leftWin, 1000, combinedWithOtherModifier: false);
        modifierAddedLater.MarkOtherModifierActivity();
        if (modifierAddedLater.Release(leftWin, 1100, threshold) != WinKeyReleaseAction.Suppress)
        {
            return false;
        }

        var overlayShown = new WinKeyGestureTracker();
        overlayShown.Press(leftWin, 1000, combinedWithOtherModifier: false);
        overlayShown.MarkLongPress();
        return overlayShown.Release(leftWin, 1100, threshold) == WinKeyReleaseAction.Suppress;
    }
}
