using System.Diagnostics;
using System.Text.Json;

namespace TrayAlwaysOnTop;

internal static class VisualStudioIntegrationInstaller
{
    public static bool IsVisualStudioInstalled => GetInstalledInstances().Count > 0;

    public static bool IsExtensionInstalled(VisualStudioInstance instance)
    {
        if (!Version.TryParse(instance.Version, out var version))
        {
            return false;
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "VisualStudio",
            $"{version.Major}.0_{instance.InstanceId}",
            "Extensions");
        if (!Directory.Exists(root))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(root, "extension.vsixmanifest", SearchOption.AllDirectories)
                .Any(path => File.ReadAllText(path).Contains("TrayAlwaysOnTop.VisualStudio", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static IReadOnlyList<VisualStudioInstance> GetInstalledInstances()
    {
        var vswhere = FindVsWhere();
        if (vswhere is null)
        {
            return [];
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = vswhere,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "-products", "*", "-prerelease", "-version", "[17.0,)", "-format", "json", "-utf8" })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var json = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode != 0)
            {
                return [];
            }

            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateArray()
                .Select(ParseInstance)
                .Where(instance => instance is not null)
                .Cast<VisualStudioInstance>()
                .OrderBy(instance => instance.Version)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    public static async Task<(bool Success, string Message)> InstallAsync(
        IReadOnlyList<VisualStudioInstance>? selectedInstances = null)
    {
        var instances = selectedInstances ?? GetInstalledInstances();
        if (instances.Count == 0)
        {
            return (false, "Visual Studio 2022 또는 2026을 찾지 못했습니다.");
        }

        var running = Process.GetProcessesByName("devenv");
        try
        {
            if (running.Length > 0)
            {
                return (false, "Visual Studio가 실행 중입니다. 모든 Visual Studio 창을 닫은 뒤 다시 설치해 주세요.");
            }
        }
        finally
        {
            foreach (var process in running) process.Dispose();
        }

        var package = FindExtensionPackage();
        if (package is null)
        {
            return (false, "Visual Studio 연동 확장 패키지를 찾지 못했습니다. 앱을 최신 버전으로 업데이트해 주세요.");
        }

        var installed = new List<string>();
        var failed = new List<string>();
        foreach (var instance in instances)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = instance.VsixInstallerPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("/quiet");
                startInfo.ArgumentList.Add($"/instanceIds:{instance.InstanceId}");
                startInfo.ArgumentList.Add(package);
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    failed.Add(instance.DisplayName);
                    continue;
                }

                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    installed.Add(instance.DisplayName);
                }
                else
                {
                    failed.Add($"{instance.DisplayName} (오류 {process.ExitCode})");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failed.Add($"{instance.DisplayName} ({exception.Message})");
            }
        }

        var message = installed.Count > 0
            ? $"설치 완료: {string.Join(", ", installed)}\nVisual Studio를 시작하면 현재 컨텍스트 단축키가 연결됩니다."
            : "Visual Studio 연동 확장을 설치하지 못했습니다.";
        if (failed.Count > 0)
        {
            message += $"\n설치 실패: {string.Join(", ", failed)}";
        }

        return (failed.Count == 0 && installed.Count == instances.Count, message);
    }

    private static VisualStudioInstance? ParseInstance(JsonElement item)
    {
        if (!item.TryGetProperty("instanceId", out var instanceIdElement)
            || !item.TryGetProperty("installationPath", out var pathElement)
            || !item.TryGetProperty("installationVersion", out var versionElement))
        {
            return null;
        }

        var installationPath = pathElement.GetString();
        var instanceId = instanceIdElement.GetString();
        var version = versionElement.GetString();
        if (string.IsNullOrWhiteSpace(installationPath)
            || string.IsNullOrWhiteSpace(instanceId)
            || string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var installer = Path.Combine(installationPath, "Common7", "IDE", "VSIXInstaller.exe");
        if (!File.Exists(installer))
        {
            return null;
        }

        var displayName = item.TryGetProperty("displayName", out var displayNameElement)
            ? displayNameElement.GetString()
            : null;
        return new VisualStudioInstance(
            instanceId,
            displayName ?? $"Visual Studio {version}",
            version,
            installationPath,
            installer);
    }

    private static string? FindVsWhere()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var path = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        return File.Exists(path) ? path : null;
    }

    private static string? FindExtensionPackage()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "Integrations", "TrayAlwaysOnTop-VisualStudio.vsix");
        if (File.Exists(packaged))
        {
            return packaged;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var development = Path.Combine(directory.FullName, "artifacts", "TrayAlwaysOnTop-VisualStudio.vsix");
            if (File.Exists(development))
            {
                return development;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
