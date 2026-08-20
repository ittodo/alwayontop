using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Text.Json;

namespace TrayAlwaysOnTop;

internal sealed class WindowsTerminalShortcutService
{
    private static readonly (string Keys, string Action)[] FallbackDefaultBindings =
    [
        ("alt+f4", "Terminal.CloseWindow"),
        ("alt+enter", "Terminal.ToggleFullscreen"),
        ("f11", "Terminal.ToggleFullscreen"),
        ("ctrl+shift+space", "Terminal.OpenNewTabDropdown"),
        ("ctrl+,", "Terminal.OpenSettingsUI"),
        ("ctrl+shift+,", "Terminal.OpenSettingsFile"),
        ("ctrl+shift+f", "Terminal.FindText"),
        ("ctrl+shift+p", "Terminal.ToggleCommandPalette"),
        ("ctrl+shift+t", "Terminal.OpenNewTab"),
        ("ctrl+shift+n", "Terminal.OpenNewWindow"),
        ("ctrl+shift+d", "Terminal.DuplicateTab"),
        ("ctrl+tab", "Terminal.NextTab"),
        ("ctrl+shift+tab", "Terminal.PrevTab"),
        ("ctrl+shift+w", "Terminal.ClosePane"),
        ("alt+shift+d", "Terminal.DuplicatePaneAuto"),
        ("alt+shift+-", "Terminal.DuplicatePaneDown"),
        ("alt+shift+plus", "Terminal.DuplicatePaneRight"),
        ("ctrl+shift+c", "Terminal.CopyToClipboard"),
        ("ctrl+shift+v", "Terminal.PasteFromClipboard"),
        ("ctrl+shift+a", "Terminal.SelectAll")
    ];

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
            ["Terminal.DuplicatePaneDown"] = "현재 창 아래로 분할",
            ["Terminal.DuplicatePaneRight"] = "현재 창 오른쪽으로 분할",
            ["Terminal.CloseWindow"] = "Terminal 창 닫기",
            ["Terminal.OpenNewTabDropdown"] = "새 탭 메뉴 열기",
            ["Terminal.OpenSettingsFile"] = "설정 파일 열기",
            ["Terminal.OpenDefaultSettingsFile"] = "기본 설정 파일 열기",
            ["Terminal.ToggleCommandPalette"] = "명령 팔레트 열기",
            ["Terminal.OpenNewTab"] = "새 탭 열기",
            ["Terminal.OpenNewWindow"] = "새 창 열기",
            ["Terminal.DuplicateTab"] = "현재 탭 복제",
            ["Terminal.NewTab"] = "새 탭 열기",
            ["Terminal.NewWindow"] = "새 창 열기",
            ["Terminal.ClosePane"] = "현재 패널·탭 닫기",
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
            ["Terminal.ScrollToTop"] = "맨 위로 스크롤",
            ["Terminal.ScrollToBottom"] = "맨 아래로 스크롤",
            ["Terminal.SelectAll"] = "모두 선택",
            ["Terminal.ToggleMarkMode"] = "표시 모드 전환",
            ["Terminal.ShowContextMenu"] = "상황에 맞는 메뉴 열기",
            ["Terminal.ClearBuffer"] = "화면과 기록 지우기",
            ["Terminal.IncreaseFontSize"] = "글꼴 크게",
            ["Terminal.DecreaseFontSize"] = "글꼴 작게",
            ["Terminal.ResetFontSize"] = "글꼴 크기 초기화",
            ["Terminal.Suggestions"] = "명령 제안 열기",
            ["Terminal.MoveFocusDown"] = "아래 패널로 포커스 이동",
            ["Terminal.MoveFocusLeft"] = "왼쪽 패널로 포커스 이동",
            ["Terminal.MoveFocusRight"] = "오른쪽 패널로 포커스 이동",
            ["Terminal.MoveFocusUp"] = "위 패널로 포커스 이동",
            ["Terminal.MoveFocusPrevious"] = "이전 패널로 포커스 이동",
            ["Terminal.ResizePaneDown"] = "패널 아래로 크기 조절",
            ["Terminal.ResizePaneLeft"] = "패널 왼쪽으로 크기 조절",
            ["Terminal.ResizePaneRight"] = "패널 오른쪽으로 크기 조절",
            ["Terminal.ResizePaneUp"] = "패널 위로 크기 조절",
            ["copy"] = "복사",
            ["paste"] = "붙여넣기",
            ["find"] = "검색",
            ["newTab"] = "새 탭 열기",
            ["newWindow"] = "새 창 열기",
            ["duplicateTab"] = "현재 탭 복제",
            ["closePane"] = "현재 패널·탭 닫기",
            ["splitPane"] = "현재 패널 분할",
            ["commandPalette"] = "명령 팔레트 열기",
            ["toggleFullscreen"] = "전체 화면 전환",
            ["selectAll"] = "모두 선택"
        };

    private readonly string? _settingsPath;
    private readonly string? _defaultsPath;
    private (DateTime LastWriteUtc, long Length) _lastSettingsState = (DateTime.MinValue, -1);
    private (DateTime LastWriteUtc, long Length) _lastDefaultsState = (DateTime.MinValue, -1);
    private IReadOnlyList<ContextualShortcut> _cached = [];

    public WindowsTerminalShortcutService()
    {
        _settingsPath = FindSettingsPath();
        _defaultsPath = FindDefaultsPath();
    }

    public string StatusText => _settingsPath is null && _defaultsPath is null
        ? "Windows Terminal 설정 파일을 찾지 못함"
        : $"Windows Terminal 기본값 + 사용자 설정 · {_cached.Count}개";

    public IReadOnlyList<ContextualShortcut> GetForegroundShortcuts() =>
        IsWindowsTerminalForeground() ? LoadShortcuts() : [];

    public IReadOnlyList<ContextualShortcut> GetShortcuts() => LoadShortcuts();

    private IReadOnlyList<ContextualShortcut> LoadShortcuts()
    {
        if (_settingsPath is null && _defaultsPath is null)
        {
            return [];
        }

        try
        {
            var settingsState = GetFileState(_settingsPath);
            var defaultsState = GetFileState(_defaultsPath);

            if (settingsState == _lastSettingsState && defaultsState == _lastDefaultsState)
            {
                return _cached;
            }

            var shortcuts = new Dictionary<(HotKeyModifiers Modifiers, Keys Key), ContextualShortcut>();
            if (_defaultsPath is not null && File.Exists(_defaultsPath))
            {
                ApplyBindingsFile(_defaultsPath, shortcuts);
            }
            else
            {
                foreach (var (keys, action) in FallbackDefaultBindings)
                {
                    ApplyShortcut(keys, action, shortcuts);
                }
            }

            if (_settingsPath is not null && File.Exists(_settingsPath))
            {
                ApplyBindingsFile(_settingsPath, shortcuts);
            }

            _cached = shortcuts.Values
                .OrderBy(shortcut => shortcut.Shortcut, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            _lastSettingsState = settingsState;
            _lastDefaultsState = defaultsState;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return _cached;
        }

        return _cached;
    }

    private static void ApplyBindingsFile(
        string path,
        IDictionary<(HotKeyModifiers Modifiers, Keys Key), ContextualShortcut> shortcuts)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path), JsonOptions);
        AddBindings(document.RootElement, "keybindings", shortcuts);
        AddBindings(document.RootElement, "actions", shortcuts);
    }

    private static (DateTime LastWriteUtc, long Length) GetFileState(string? path)
    {
        if (path is null)
        {
            return (DateTime.MinValue, -1);
        }

        var file = new FileInfo(path);
        return file.Exists ? (file.LastWriteTimeUtc, file.Length) : (DateTime.MinValue, -1);
    }

    private static void AddBindings(
        JsonElement root,
        string propertyName,
        IDictionary<(HotKeyModifiers Modifiers, Keys Key), ContextualShortcut> shortcuts)
    {
        if (!root.TryGetProperty(propertyName, out var bindings)
            || bindings.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var binding in bindings.EnumerateArray())
        {
            if (!TryGetAction(binding, out var action)
                || !binding.TryGetProperty("keys", out var keys))
            {
                continue;
            }

            if (keys.ValueKind == JsonValueKind.String)
            {
                ApplyShortcut(keys.GetString(), action, shortcuts);
            }
            else if (keys.ValueKind == JsonValueKind.Array)
            {
                foreach (var key in keys.EnumerateArray())
                {
                    if (key.ValueKind == JsonValueKind.String)
                    {
                        ApplyShortcut(key.GetString(), action, shortcuts);
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

    private static void ApplyShortcut(
        string? gesture,
        string action,
        IDictionary<(HotKeyModifiers Modifiers, Keys Key), ContextualShortcut> shortcuts)
    {
        if (gesture is null
            || !VsCodeKeyGestureParser.TryParse(gesture, out var modifiers, out var key, out var remaining))
        {
            return;
        }

        var shortcutKey = (modifiers, key);
        if (string.Equals(action, "unbound", StringComparison.OrdinalIgnoreCase))
        {
            shortcuts.Remove(shortcutKey);
            return;
        }

        var description = GetActionName(action);
        if (!string.IsNullOrWhiteSpace(remaining))
        {
            description += $" · 다음 키: {remaining}";
        }

        shortcuts[shortcutKey] = new ContextualShortcut(
            modifiers,
            key,
            HotKeyFormatter.Format(modifiers, key),
            description,
            ShortcutVisualKind.WindowsTerminal,
            "Terminal");
    }

    private static string GetActionName(string action)
    {
        if (ActionNames.TryGetValue(action, out var name))
        {
            return name;
        }

        const string newTabProfilePrefix = "Terminal.OpenNewTabProfile";
        if (action.StartsWith(newTabProfilePrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(action[newTabProfilePrefix.Length..], out var profileIndex))
        {
            return $"프로필 {profileIndex + 1}로 새 탭 열기";
        }

        const string switchToTabPrefix = "Terminal.SwitchToTab";
        if (action.StartsWith(switchToTabPrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(action[switchToTabPrefix.Length..], out var tabIndex))
        {
            return $"{tabIndex + 1}번 탭으로 이동";
        }

        if (string.Equals(action, "Terminal.SwitchToLastTab", StringComparison.OrdinalIgnoreCase))
        {
            return "마지막 탭으로 이동";
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

    private static string? FindDefaultsPath()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("WindowsTerminal"))
            {
                using (process)
                {
                    var executablePath = process.MainModule?.FileName;
                    if (executablePath is null)
                    {
                        continue;
                    }

                    var candidate = Path.Combine(Path.GetDirectoryName(executablePath)!, "defaults.json");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // Fall back to the registered package locations below.
        }

        const string packagesKeyPath =
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        try
        {
            using var packagesKey = Registry.CurrentUser.OpenSubKey(packagesKeyPath);
            if (packagesKey is null)
            {
                return null;
            }

            var packageNames = packagesKey.GetSubKeyNames()
                .Where(name => name.StartsWith("Microsoft.WindowsTerminal_", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Microsoft.WindowsTerminalPreview_", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase);
            foreach (var packageName in packageNames)
            {
                using var packageKey = packagesKey.OpenSubKey(packageName);
                var packageRoot = packageKey?.GetValue("PackageRootFolder") as string;
                if (string.IsNullOrWhiteSpace(packageRoot))
                {
                    continue;
                }

                var candidate = Path.Combine(packageRoot, "defaults.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
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
