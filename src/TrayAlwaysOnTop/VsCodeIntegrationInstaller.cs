using System.Diagnostics;

namespace TrayAlwaysOnTop;

internal static class VsCodeIntegrationInstaller
{
    public static bool IsVsCodeInstalled => FindCodeCli() is not null;

    public static async Task<(bool Success, string Message)> InstallAsync()
    {
        var cli = FindCodeCli();
        if (cli is null)
        {
            return (false, "VS Code를 찾지 못했습니다.");
        }

        var package = FindExtensionPackage();
        if (package is null)
        {
            return (false, "VS Code 연동 확장 패키지를 찾지 못했습니다. 앱을 최신 버전으로 업데이트해 주세요.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cli.Value.Executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.Environment["ELECTRON_RUN_AS_NODE"] = "1";
            startInfo.Environment["VSCODE_DEV"] = string.Empty;
            startInfo.ArgumentList.Add(cli.Value.CliScript);
            startInfo.ArgumentList.Add("--install-extension");
            startInfo.ArgumentList.Add(package);
            startInfo.ArgumentList.Add("--force");
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (false, "VS Code 확장 설치 프로그램을 시작하지 못했습니다.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            if (process.ExitCode != 0)
            {
                return (false, string.IsNullOrWhiteSpace(error) ? output : error);
            }

            return (true, "VS Code 연동 확장을 설치했습니다. 열려 있는 VS Code 창에서 ‘창 다시 로드’를 실행하거나 VS Code를 다시 시작해 주세요.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return (false, $"VS Code 연동 확장을 설치하지 못했습니다.\n{exception.Message}");
        }
    }

    private static (string Executable, string CliScript)? FindCodeCli()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Microsoft VS Code",
                "Code.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft VS Code",
                "Code.exe")
        };
        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
        {
            return null;
        }

        var root = Path.GetDirectoryName(executable)!;
        var cliScript = Directory
            .EnumerateDirectories(root)
            .Select(directory => Path.Combine(directory, "resources", "app", "out", "cli.js"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return cliScript is null ? null : (executable, cliScript);
    }

    private static string? FindExtensionPackage()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "Integrations", "TrayAlwaysOnTop-VSCode.vsix");
        if (File.Exists(packaged))
        {
            return packaged;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var development = Path.Combine(directory.FullName, "artifacts", "TrayAlwaysOnTop-VSCode.vsix");
            if (File.Exists(development))
            {
                return development;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
