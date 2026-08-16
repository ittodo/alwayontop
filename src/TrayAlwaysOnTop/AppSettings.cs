namespace TrayAlwaysOnTop;

internal sealed class AppSettings
{
    public HotKeyModifiers Modifiers { get; set; } = HotKeyModifiers.Control | HotKeyModifiers.Win;

    public Keys Key { get; set; } = Keys.T;

    public bool ShowBorder { get; set; } = true;

    public bool ShowPinToggle { get; set; } = true;

    public bool ShowNotifications { get; set; } = true;

    public bool StartWithWindows { get; set; } = true;

    public AppSettings Copy() => new()
    {
        Modifiers = Modifiers,
        Key = Key,
        ShowBorder = ShowBorder,
        ShowPinToggle = ShowPinToggle,
        ShowNotifications = ShowNotifications,
        StartWithWindows = StartWithWindows
    };
}
