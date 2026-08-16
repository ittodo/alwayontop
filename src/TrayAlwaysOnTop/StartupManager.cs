using Microsoft.Win32;
using Velopack.Locators;

namespace TrayAlwaysOnTop;

internal static class StartupManager
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TrayAlwaysOnTop";

    public static bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            using var key = enabled
                ? Registry.CurrentUser.CreateSubKey(RegistryPath, true)
                : Registry.CurrentUser.OpenSubKey(RegistryPath, true);

            if (key is null)
            {
                error = "Windows 시작 프로그램 설정을 열 수 없습니다.";
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{GetStableExecutablePath()}\" --autostart", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }

            error = null;
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                          or IOException
                                          or System.Security.SecurityException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string GetStableExecutablePath()
    {
        var executablePath = Environment.ProcessPath ?? Application.ExecutablePath;

        // Installed Velopack apps have a stable launcher in RootAppDir. Using it
        // keeps this Run entry valid when the versioned app directory is replaced.
        if (VelopackLocator.IsCurrentSet)
        {
            var rootDirectory = VelopackLocator.Current.RootAppDir;
            if (!string.IsNullOrWhiteSpace(rootDirectory))
            {
                var stablePath = Path.Combine(rootDirectory, Path.GetFileName(executablePath));
                if (File.Exists(stablePath))
                {
                    return stablePath;
                }
            }
        }

        return executablePath;
    }
}
