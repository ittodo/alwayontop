namespace TrayAlwaysOnTop;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore = new();
    private readonly HotKeyService _hotKeyService = new();
    private readonly UpdateService _updateService = new();
    private readonly WindowManager _windowManager;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _currentWindowItem = new();
    private readonly ToolStripMenuItem _windowListItem = new("열린 창 선택");
    private readonly ToolStripMenuItem _updateItem = new("업데이트 확인...");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer _startupUpdateTimer = new() { Interval = 5000 };
    private readonly CancellationTokenSource _shutdown = new();
    private AppSettings _settings;
    private bool _updateCheckRunning;
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
        _updateItem.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);
        var exitItem = new ToolStripMenuItem("종료");
        exitItem.Click += (_, _) => ExitThread();

        _menu.Items.Add(new ToolStripMenuItem("Tray Always On Top") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_currentWindowItem);
        _menu.Items.Add(_windowListItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_updateItem);
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

        _startupUpdateTimer.Tick += async (_, _) =>
        {
            _startupUpdateTimer.Stop();
            await CheckForUpdatesAsync(manual: false);
        };
        _startupUpdateTimer.Start();

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
        _shutdown.Cancel();
        _startupUpdateTimer.Stop();
        _startupUpdateTimer.Dispose();
        _timer.Stop();
        _timer.Dispose();
        _hotKeyService.Dispose();
        _windowManager.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
        _menu.Dispose();
        _shutdown.Dispose();
        base.ExitThreadCore();
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateCheckRunning || _disposed)
        {
            return;
        }

        _updateCheckRunning = true;
        _updateItem.Enabled = false;
        _updateItem.Text = "업데이트 확인 중...";

        try
        {
            var result = await _updateService.CheckAndDownloadAsync(
                progress: null,
                cancellationToken: _shutdown.Token);

            switch (result.Status)
            {
                case UpdateCheckStatus.NotInstalled:
                    _updateItem.Text = "업데이트 확인...";
                    if (manual)
                    {
                        MessageBox.Show(
                            "자동 업데이트는 Velopack으로 설치한 버전에서 사용할 수 있습니다.",
                            "업데이트",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    break;

                case UpdateCheckStatus.UpToDate:
                    _updateItem.Text = "업데이트 확인...";
                    if (manual)
                    {
                        MessageBox.Show(
                            $"현재 최신 버전입니다. ({result.Version ?? "버전 확인 불가"})",
                            "업데이트",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    break;

                case UpdateCheckStatus.ReadyToRestart:
                    _updateItem.Text = $"업데이트 준비됨 ({result.Version})";
                    if (manual)
                    {
                        var restart = MessageBox.Show(
                            $"버전 {result.Version} 업데이트가 준비되었습니다. 지금 다시 시작할까요?",
                            "업데이트 준비됨",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information);
                        if (restart == DialogResult.Yes)
                        {
                            _windowManager.ReleaseAllPins();
                            _updateService.ApplyPendingUpdateAndRestart();
                        }
                    }
                    else
                    {
                        ShowBalloon(
                            "업데이트 준비됨",
                            $"버전 {result.Version}을 내려받았습니다. 다음 실행 때 자동 적용됩니다.",
                            ToolTipIcon.Info);
                    }
                    break;
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // The app is shutting down.
        }
        catch (Exception exception)
        {
            _updateItem.Text = "업데이트 확인...";
            if (manual)
            {
                MessageBox.Show(
                    $"업데이트를 확인하지 못했습니다.\n{exception.Message}",
                    "업데이트 확인 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _updateCheckRunning = false;
            if (!_disposed)
            {
                _updateItem.Enabled = true;
            }
        }
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
