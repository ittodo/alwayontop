namespace TrayAlwaysOnTop;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore = new();
    private readonly HotKeyService _hotKeyService = new();
    private readonly WindowManager _windowManager;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _currentWindowItem = new();
    private readonly ToolStripMenuItem _windowListItem = new("열린 창 선택");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 150 };
    private AppSettings _settings;
    private bool _disposed;

    public TrayApplicationContext()
    {
        _settings = _settingsStore.Load();
        _windowManager = new WindowManager(_settings.ShowBorder, _settings.ShowPinToggle);
        _windowManager.OverlayToggleCompleted += (_, result) => HandleToggleResult(result);
        StartupManager.TrySetEnabled(_settings.StartWithWindows, out var startupError);

        _currentWindowItem.Click += (_, _) => ToggleLastWindow();
        _windowListItem.DropDownOpening += (_, _) => PopulateWindowList();

        var settingsItem = new ToolStripMenuItem("설정...");
        settingsItem.Click += (_, _) => ShowSettings();
        var exitItem = new ToolStripMenuItem("종료");
        exitItem.Click += (_, _) => ExitThread();

        _menu.Items.Add(new ToolStripMenuItem("Tray Always On Top") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_currentWindowItem);
        _menu.Items.Add(_windowListItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(exitItem);
        _menu.Opening += (_, _) =>
        {
            _windowManager.CaptureForegroundWindow();
            UpdateCurrentWindowItem();
        };

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Tray Always On Top",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleLastWindow();

        _hotKeyService.Pressed += (_, _) =>
        {
            _windowManager.CaptureForegroundWindow();
            ToggleLastWindow();
        };

        if (!_hotKeyService.TryRegister(_settings.Modifiers, _settings.Key, out var hotKeyError))
        {
            ShowBalloon("단축키 등록 실패", hotKeyError ?? "단축키를 등록할 수 없습니다.", ToolTipIcon.Warning, force: true);
        }

        if (startupError is not null)
        {
            ShowBalloon("자동 실행 설정 실패", startupError, ToolTipIcon.Warning, force: true);
        }

        _timer.Tick += (_, _) =>
        {
            _windowManager.CaptureForegroundWindow();
            _windowManager.Synchronize();
        };
        _timer.Start();

        ShowBalloon(
            "Tray Always On Top 실행 중",
            $"{FormatHotKey(_settings)}를 눌러 현재 창을 고정하거나 해제하세요.",
            ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        if (_disposed)
        {
            base.ExitThreadCore();
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        _hotKeyService.Dispose();
        _windowManager.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
        _menu.Dispose();
        base.ExitThreadCore();
    }

    private void ToggleLastWindow()
    {
        var result = _windowManager.Toggle(_windowManager.LastExternalWindow);
        HandleToggleResult(result);
    }

    private void ToggleWindow(nint handle)
    {
        var result = _windowManager.Toggle(handle);
        HandleToggleResult(result);
    }

    private void HandleToggleResult(ToggleResult result)
    {
        if (!result.Success)
        {
            ShowBalloon("Always on Top", result.Error ?? "창 상태를 변경하지 못했습니다.", ToolTipIcon.Warning, force: true);
            return;
        }

        var action = result.IsTopmost ? "항상 위로 고정했습니다." : "항상 위 고정을 해제했습니다.";
        ShowBalloon(TrimText(result.Title, 80), action, ToolTipIcon.Info);
        UpdateCurrentWindowItem();
    }

    private void UpdateCurrentWindowItem()
    {
        var window = _windowManager.GetLastExternalWindowInfo();
        if (window is null)
        {
            _currentWindowItem.Text = $"현재 창 항상 위 전환 ({FormatHotKey(_settings)})";
            _currentWindowItem.Enabled = false;
            _currentWindowItem.Checked = false;
            return;
        }

        _currentWindowItem.Text = $"현재 창: {TrimText(window.Title, 42)} ({FormatHotKey(_settings)})";
        _currentWindowItem.Enabled = true;
        _currentWindowItem.Checked = _windowManager.IsTopmost(window.Handle);
    }

    private void PopulateWindowList()
    {
        _windowListItem.DropDownItems.Clear();
        var windows = _windowManager.GetOpenWindows();
        if (windows.Count == 0)
        {
            _windowListItem.DropDownItems.Add(new ToolStripMenuItem("선택 가능한 창이 없습니다") { Enabled = false });
            return;
        }

        foreach (var window in windows)
        {
            var item = new ToolStripMenuItem(TrimText(window.DisplayName, 70))
            {
                Checked = _windowManager.IsTopmost(window.Handle),
                ToolTipText = window.DisplayName,
                Tag = window.Handle
            };
            item.Click += (_, _) => ToggleWindow((nint)item.Tag!);
            _windowListItem.DropDownItems.Add(item);
        }
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var previous = _settings.Copy();
        if (!_hotKeyService.TryRegister(form.Result.Modifiers, form.Result.Key, out var error))
        {
            _hotKeyService.TryRegister(previous.Modifiers, previous.Key, out _);
            MessageBox.Show(error, "단축키 등록 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings = form.Result;
        _windowManager.SetOverlayOptions(_settings.ShowBorder, _settings.ShowPinToggle);
        var startupApplied = StartupManager.TrySetEnabled(_settings.StartWithWindows, out var startupError);
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (IOException exception)
        {
            MessageBox.Show($"설정을 저장하지 못했습니다.\n{exception.Message}", "설정 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (UnauthorizedAccessException exception)
        {
            MessageBox.Show($"설정을 저장하지 못했습니다.\n{exception.Message}", "설정 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (!startupApplied)
        {
            MessageBox.Show(
                $"Windows 자동 실행 설정을 적용하지 못했습니다.\n{startupError}",
                "자동 실행 설정 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        ShowBalloon("설정 저장됨", $"전역 단축키: {FormatHotKey(_settings)}", ToolTipIcon.Info);
    }

    private void ShowBalloon(string title, string message, ToolTipIcon icon, bool force = false)
    {
        if (!force && !_settings.ShowNotifications)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(2500);
    }

    private static string FormatHotKey(AppSettings settings)
    {
        var parts = new List<string>();
        if (settings.Modifiers.HasFlag(HotKeyModifiers.Control)) parts.Add("Ctrl");
        if (settings.Modifiers.HasFlag(HotKeyModifiers.Win)) parts.Add("Win");
        if (settings.Modifiers.HasFlag(HotKeyModifiers.Alt)) parts.Add("Alt");
        if (settings.Modifiers.HasFlag(HotKeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(settings.Key.ToString());
        return string.Join(" + ", parts);
    }

    private static string TrimText(string text, int maximumLength) =>
        text.Length <= maximumLength ? text : text[..(maximumLength - 1)] + "…";
}
