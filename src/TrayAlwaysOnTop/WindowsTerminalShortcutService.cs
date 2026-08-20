using System.Diagnostics;
using System.Text.Json;

namespace TrayAlwaysOnTop;

internal sealed class WindowsTerminalShortcutService
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly IReadOnlyDictionary<string, string> ActionNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Terminal.CopyToClipboard"] = "복사",
            ["Terminal.PasteFromClipboard"] = "붙여넣기",
            ["Terminal.FindText"] = "검색",
            ["Terminal.DuplicatePaneAuto"] = "현재 창 자동 분할",
            ["Terminal.NewTab"] = "새 탭 열기",
            ["Terminal.NewWindow"] = "새 창 열기",
            ["Terminal.ClosePane"] = "현재 창 닫기",
            ["Terminal.CloseTab"] = "현재 탭 닫기",
            ["Terminal.NextTab"] = "다음 탭으로 이동",
            ["Terminal.PrevTab"] = "이전 탭으로 이동",
            ["Terminal.OpenSettings"] = "설정 열기",
            ["Terminal.OpenSettingsUI"] = "설정 화면 열기",
            ["Terminal.CommandPalette"] = "명령 팔레트 열기",
            ["Terminal.ToggleFullscreen"] = "전체 화면 전환",
            ["Terminal.ToggleFocusMode"] = "포커스 모드 전환",
            ["Terminal.ScrollUp"] = "위로 스크롤",
            ["Terminal.ScrollDown"] = "아래로 스크롤",
            ["Terminal.ScrollUpPage"] = "한 페이지 위로 스크롤",
            ["Terminal.ScrollDownPage"] = "한 페이지 아래로 스크롤",
            ["Terminal.SelectAll"] = "모두 선택"
        };

    private readonly string? _settingsPath;
    private DateTime _lastWriteUtc;
    private long _lastLength = -1;
    private IReadOnlyList<ContextualShortcut> _cached = [];

    public WindowsTerminalShortcutService()
    {
        _settingsPath = FindSettingsPath();
    }

    public string StatusText => _settingsPath is null
        ? "Windows Terminal 설정 파일을 찾지 못함"
        : $"Windows Terminal settings.json · {_cached.Count}개";

    public IReadOnlyList<ContextualShortcut> GetForegroundShortcuts() =>
        IsWindowsTerminalForeground() ? LoadShortcuts() : [];

    public IReadOnlyList<ContextualShortcut> GetShortcuts() => LoadShortcuts();

    private IReadOnlyList<ContextualShortcut> LoadShortcuts()
    {
        if (_settingsPath is null)
        {
            return [];
        }

        try
        {
            var file = new FileInfo(_settingsPath);
            if (!file.Exists)
            {
                return [];
            }

            if (file.LastWriteTimeUtc == _lastWriteUtc && file.Length == _lastLength)
            {
                return _cached;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_settingsPath), JsonOptions);
            var shortcuts = new List<ContextualShortcut>();
            AddBindings(document.RootElement, "keybindings", shortcuts);
            AddBindings(document.RootElement, "actions", shortcuts);
            _cached = shortcuts
                .DistinctBy(shortcut => (shortcut.Modifiers, shortcut.Key, shortcut.Description))
                .ToArray();
            _lastWriteUtc = file.LastWriteTimeUtc;
            _lastLength = file.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return _cached;
        }

        return _cached;
    }

    private static void AddBindings(
        JsonElement root,
        string propertyName,
        ICollection<ContextualShortcut> shortcuts)
    {
        if (!root.TryGetProperty(propertyName, out var bindings)
            || bindings.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var binding in bindings.EnumerateArray())
        {
            if (!TryGetAction(binding, out var action)
                || string.Equals(action, "unbound", StringComparison.OrdinalIgnoreCase)
                || !binding.TryGetProperty("keys", out var keys))
            {
                continue;
            }

            if (keys.ValueKind == JsonValueKind.String)
            {
                AddShortcut(keys.GetString(), action, shortcuts);
            }
            else if (keys.ValueKind == JsonValueKind.Array)
            {
                foreach (var key in keys.EnumerateArray())
                {
                    if (key.ValueKind == JsonValueKind.String)
                    {
                        AddShortcut(key.GetString(), action, shortcuts);
                    }
                }
            }
        }
    }

    private static bool TryGetAction(JsonElement binding, out string action)
    {
        action = string.Empty;
        if (binding.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
        {
            action = id.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(action);
        }

        if (!binding.TryGetProperty("command", out var command))
        {
            return false;
        }

        if (command.ValueKind == JsonValueKind.String)
        {
            action = command.GetString() ?? string.Empty;
        }
        else if (command.ValueKind == JsonValueKind.Object
            && command.TryGetProperty("action", out var nestedAction)
            && nestedAction.ValueKind == JsonValueKind.String)
        {
            action = nestedAction.GetString() ?? string.Empty;
        }

        return !string.IsNullOrWhiteSpace(action);
    }

    private static void AddShortcut(
        string? gesture,
        string action,
        ICollection<ContextualShortcut> shortcuts)
    {
        if (gesture is null
            || !VsCodeKeyGestureParser.TryParse(gesture, out var modifiers, out var key, out var remaining))
        {
            return;
        }

        var description = GetActionName(action);
        if (!string.IsNullOrWhiteSpace(remaining))
        {
            description += $" · 다음 키: {remaining}";
        }

        shortcuts.Add(new ContextualShortcut(
            modifiers,
            key,
            HotKeyFormatter.Format(modifiers, key),
            description,
            ShortcutVisualKind.WindowsTerminal,
            "Terminal"));
    }

    private static string GetActionName(string action)
    {
        if (ActionNames.TryGetValue(action, out var name))
        {
            return name;
        }

        var shortName = action.Contains('.') ? action[(action.LastIndexOf('.') + 1)..] : action;
        return string.Concat(shortName.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));
    }

    private static string? FindSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(
                localAppData,
                "Packages",
                "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
                "LocalState",
                "settings.json"),
            Path.Combine(localAppData, "Microsoft", "Windows Terminal", "settings.json"),
            Path.Combine(
                localAppData,
                "Packages",
                "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe",
                "LocalState",
                "settings.json")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool IsWindowsTerminalForeground()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == nint.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        try
        {
            return string.Equals(
                Process.GetProcessById((int)processId).ProcessName,
                "WindowsTerminal",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
