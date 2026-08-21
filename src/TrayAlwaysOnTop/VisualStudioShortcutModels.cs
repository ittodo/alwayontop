namespace TrayAlwaysOnTop;

internal sealed record VisualStudioShortcutMessage(
    int ProtocolVersion,
    string App,
    string VisualStudioVersion,
    int ProcessId,
    bool WindowActive,
    string Context,
    IReadOnlyList<VisualStudioShortcutItem> Shortcuts);

internal sealed record VisualStudioShortcutItem(
    string Key,
    string? RemainingChord,
    string Command,
    string Title,
    string Scope);

internal sealed record VisualStudioInstance(
    string InstanceId,
    string DisplayName,
    string Version,
    string InstallationPath,
    string VsixInstallerPath);
