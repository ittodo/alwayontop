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
}
