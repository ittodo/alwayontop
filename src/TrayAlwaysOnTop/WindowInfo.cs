namespace TrayAlwaysOnTop;

internal sealed record WindowInfo(nint Handle, string Title, string ProcessName)
{
    public string DisplayName => string.IsNullOrWhiteSpace(ProcessName)
        ? Title
        : $"{Title}  —  {ProcessName}";
}
