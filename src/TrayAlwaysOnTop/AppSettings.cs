namespace TrayAlwaysOnTop;

internal sealed class AppSettings
{
    public HotKeyModifiers Modifiers { get; set; } = HotKeyModifiers.Control | HotKeyModifiers.Win;

    public Keys Key { get; set; } = Keys.T;

    public bool ShowBorder { get; set; } = true;

    public bool ShowPinToggle { get; set; } = true;

    public bool ShowNotifications { get; set; } = true;

    public bool ShowGlobalShortcutOverlay { get; set; } = true;

    public bool ShowVsCodeShortcuts { get; set; } = true;

    public bool ShowVisualStudioShortcuts { get; set; } = true;

    public bool ShowWindowsTerminalShortcuts { get; set; } = true;

    public bool StartWithWindows { get; set; } = true;

    public bool VsCodeIntegrationPromptShown { get; set; }

    public bool VisualStudioIntegrationPromptShown { get; set; }

    public AppSettings Copy() => new()
    {
        Modifiers = Modifiers,
        Key = Key,
        ShowBorder = ShowBorder,
        ShowPinToggle = ShowPinToggle,
        ShowNotifications = ShowNotifications,
        ShowGlobalShortcutOverlay = ShowGlobalShortcutOverlay,
        ShowVsCodeShortcuts = ShowVsCodeShortcuts,
        ShowVisualStudioShortcuts = ShowVisualStudioShortcuts,
        ShowWindowsTerminalShortcuts = ShowWindowsTerminalShortcuts,
        StartWithWindows = StartWithWindows,
        VsCodeIntegrationPromptShown = VsCodeIntegrationPromptShown,
        VisualStudioIntegrationPromptShown = VisualStudioIntegrationPromptShown
    };
}
