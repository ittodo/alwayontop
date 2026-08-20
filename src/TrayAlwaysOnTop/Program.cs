using Velopack;
using System.IO.Pipes;
using System.Text;

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

        if (args.Contains("--vscode-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = VsCodeIntegrationSmokeTest() ? 0 : 4;
            return;
        }

        if (args.Contains("--vscode-live-test", StringComparer.OrdinalIgnoreCase))
        {
            using var integration = new VsCodeIntegrationService();
            Environment.ExitCode = SpinWait.SpinUntil(
                () => integration.IsConnected && integration.ReceivedShortcutCount >= 20,
                20000)
                ? 0
                : 5;
            return;
        }

        if (args.Contains("--vscode-install-test", StringComparer.OrdinalIgnoreCase))
        {
            var installation = VsCodeIntegrationInstaller.InstallAsync().GetAwaiter().GetResult();
            Environment.ExitCode = installation.Success ? 0 : 6;
            return;
        }

        if (args.Contains("--vscode-overlay-preview", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            using var integration = new VsCodeIntegrationService();
            SpinWait.SpinUntil(() => integration.ReceivedShortcutCount >= 20, 10000);
            using var overlay = new ModifierShortcutOverlayForm();
            overlay.ShowFor(
                HotKeyModifiers.Control,
                new AppSettings(),
                appHotKeyRegistered: true,
                integration.GetLastActiveShortcuts());
            using var closeTimer = new System.Windows.Forms.Timer { Interval = 8000 };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                overlay.Close();
                Application.ExitThread();
            };
            closeTimer.Start();
            Application.Run();
            return;
        }

        if (args.Contains("--terminal-live-test", StringComparer.OrdinalIgnoreCase))
        {
            var terminalShortcuts = new WindowsTerminalShortcutService().GetShortcuts();
            Environment.ExitCode = terminalShortcuts.Count >= 4
                && terminalShortcuts.Any(shortcut => shortcut.Modifiers == HotKeyModifiers.Control
                    && shortcut.Key == Keys.C
                    && shortcut.Description == "복사")
                && terminalShortcuts.Any(shortcut => shortcut.Modifiers == (HotKeyModifiers.Control | HotKeyModifiers.Shift)
                    && shortcut.Key == Keys.F
                    && shortcut.Description == "검색")
                && terminalShortcuts.Any(shortcut => shortcut.Modifiers == (HotKeyModifiers.Alt | HotKeyModifiers.Shift)
                    && shortcut.Key == Keys.D
                    && shortcut.Description == "현재 창 자동 분할")
                    ? 0
                    : 7;
            return;
        }

        if (args.Contains("--window-filter-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = WindowFilterSmokeTest() ? 0 : 8;
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

    private static bool VsCodeIntegrationSmokeTest()
    {
        if (!VsCodeKeyGestureParser.TryParse(
                "ctrl+k ctrl+s",
                out var modifiers,
                out var key,
                out var remaining)
            || modifiers != HotKeyModifiers.Control
            || key != Keys.K
            || remaining != "ctrl+s")
        {
            return false;
        }

        using var service = new VsCodeIntegrationService();
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                "TrayAlwaysOnTop.VSCode",
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            client.Connect(3000);
            var message = "{\"protocolVersion\":1,\"app\":\"vscode\",\"windowActive\":true,\"context\":\"test\",\"languageId\":\"csharp\",\"shortcuts\":[{\"key\":\"ctrl+k ctrl+s\",\"command\":\"test\",\"title\":\"테스트\",\"when\":\"editorTextFocus\"}]}\n";
            var bytes = Encoding.UTF8.GetBytes(message);
            client.Write(bytes);
            client.Flush();
            return SpinWait.SpinUntil(() => service.IsConnected, 3000);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool WindowFilterSmokeTest()
    {
        return WindowManager.IsProtectedShellWindow("Shell_TrayWnd", "explorer")
            && WindowManager.IsProtectedShellWindow("Shell_SecondaryTrayWnd", "explorer")
            && WindowManager.IsProtectedShellWindow("NotifyIconOverflowWindow", "explorer")
            && WindowManager.IsProtectedShellWindow("TopLevelWindowForOverflowXamlIsland", "explorer")
            && WindowManager.IsProtectedShellWindow("Windows.UI.Core.CoreWindow", "ShellExperienceHost")
            && WindowManager.IsProtectedShellWindow("Windows.UI.Core.CoreWindow", "StartMenuExperienceHost")
            && !WindowManager.IsProtectedShellWindow("CabinetWClass", "explorer")
            && !WindowManager.IsProtectedShellWindow("Chrome_WidgetWin_1", "chrome");
    }
}
