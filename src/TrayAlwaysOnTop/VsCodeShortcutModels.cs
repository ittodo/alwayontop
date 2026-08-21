namespace TrayAlwaysOnTop;

internal sealed record VsCodeShortcutMessage(
    int ProtocolVersion,
    string App,
    bool WindowActive,
    string Context,
    string? LanguageId,
    IReadOnlyList<VsCodeShortcutItem> Shortcuts);

internal sealed record VsCodeShortcutItem(
    string Key,
    string Command,
    string Title,
    string? When);

internal sealed record ContextualShortcut(
    HotKeyModifiers Modifiers,
    Keys Key,
    string Shortcut,
    string Description,
    ShortcutVisualKind Kind,
    string Source,
    string? RemainingChord = null);
