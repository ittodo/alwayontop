namespace TrayAlwaysOnTop;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore = new();
    private readonly HotKeyService _hotKeyService = new();
    private readonly UpdateService _updateService = new();
    private readonly VsCodeIntegrationService _vsCodeIntegrationService = new();
    private readonly VisualStudioIntegrationService _visualStudioIntegrationService = new();
    private readonly WindowsTerminalShortcutService _windowsTerminalShortcutService = new();
    private readonly GlobalModifierOverlayService _modifierOverlayService;
    private readonly WindowManager _windowManager;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _currentWindowItem = new();
    private readonly ToolStripMenuItem _windowListItem = new("열린 창 선택");
    private readonly ToolStripMenuItem _hotKeyDiagnosticsItem = new("전역 단축키 목록...");
    private readonly ToolStripMenuItem _vsCodeIntegrationItem = new("VS Code 연동...");
    private readonly ToolStripMenuItem _visualStudioIntegrationItem = new("Visual Studio 연동...");
    private readonly ToolStripMenuItem _updateItem = new("업데이트 확인...");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer _startupUpdateTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _integrationPromptTimer = new() { Interval = 9000 };
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
        _hotKeyDiagnosticsItem.Click += (_, _) => ShowHotKeyDiagnostics();
        _vsCodeIntegrationItem.Click += async (_, _) => await ShowVsCodeIntegrationAsync();
        _visualStudioIntegrationItem.Click += async (_, _) => await ShowVisualStudioIntegrationAsync();
        _updateItem.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);
        var exitItem = new ToolStripMenuItem("종료");
        exitItem.Click += (_, _) => ExitThread();

        _menu.Items.Add(new ToolStripMenuItem("Tray Always On Top") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_currentWindowItem);
        _menu.Items.Add(_windowListItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_hotKeyDiagnosticsItem);
        _menu.Items.Add(_vsCodeIntegrationItem);
        _menu.Items.Add(_visualStudioIntegrationItem);
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

        _modifierOverlayService = new GlobalModifierOverlayService(
            () => _settings.Copy(),
            () => _hotKeyService.IsRegistered,
            GetForegroundContextualShortcuts);
        if (!_modifierOverlayService.TrySetEnabled(_settings.ShowGlobalShortcutOverlay, out var overlayError))
        {
            ShowBalloon(
                "단축키 안내 시작 실패",
                overlayError ?? "화면 중앙 단축키 안내를 시작하지 못했습니다.",
                ToolTipIcon.Warning,
                force: true);
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

        _integrationPromptTimer.Tick += (_, _) =>
        {
            _integrationPromptTimer.Stop();
            ShowVsCodeIntegrationPrompt();
            ShowVisualStudioIntegrationPrompt();
        };
        _integrationPromptTimer.Start();

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
        _integrationPromptTimer.Stop();
        _integrationPromptTimer.Dispose();
        _timer.Stop();
        _timer.Dispose();
        _modifierOverlayService.Dispose();
        _vsCodeIntegrationService.Dispose();
        _visualStudioIntegrationService.Dispose();
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
        var overlayApplied = _modifierOverlayService.TrySetEnabled(
            _settings.ShowGlobalShortcutOverlay,
            out var overlayError);
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

        if (!overlayApplied)
        {
            MessageBox.Show(
                overlayError ?? "화면 중앙 단축키 안내 설정을 적용하지 못했습니다.",
                "단축키 안내 설정 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        ShowBalloon("설정 저장됨", $"전역 단축키: {FormatHotKey(_settings)}", ToolTipIcon.Info);
    }

    private void ShowHotKeyDiagnostics()
    {
        var lastWindow = _windowManager.GetLastExternalWindowInfo();
        IReadOnlyList<ContextualShortcut> shortcuts;
        string status;
        if (_settings.ShowWindowsTerminalShortcuts
            && string.Equals(lastWindow?.ProcessName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase))
        {
            shortcuts = _windowsTerminalShortcutService.GetShortcuts();
            status = _windowsTerminalShortcutService.StatusText;
        }
        else if (_settings.ShowVisualStudioShortcuts
                 && string.Equals(lastWindow?.ProcessName, "devenv", StringComparison.OrdinalIgnoreCase))
        {
            shortcuts = _visualStudioIntegrationService.GetLastActiveShortcuts();
            status = _visualStudioIntegrationService.StatusText;
        }
        else
        {
            var contextual = new List<ContextualShortcut>();
            if (_settings.ShowVsCodeShortcuts)
            {
                contextual.AddRange(_vsCodeIntegrationService.GetLastActiveShortcuts());
            }
            if (_settings.ShowVisualStudioShortcuts)
            {
                contextual.AddRange(_visualStudioIntegrationService.GetLastActiveShortcuts());
            }
            shortcuts = contextual;
            status = $"{_vsCodeIntegrationService.StatusText} · {_visualStudioIntegrationService.StatusText}";
        }

        using var form = new HotKeyDiagnosticsForm(
            _settings,
            _hotKeyService.IsRegistered,
            shortcuts,
            status);
        form.ShowDialog();
    }

    private IReadOnlyList<ContextualShortcut> GetForegroundContextualShortcuts()
    {
        var shortcuts = new List<ContextualShortcut>();
        if (_settings.ShowVsCodeShortcuts)
        {
            shortcuts.AddRange(_vsCodeIntegrationService.GetForegroundShortcuts());
        }

        if (_settings.ShowVisualStudioShortcuts)
        {
            shortcuts.AddRange(_visualStudioIntegrationService.GetForegroundShortcuts());
        }

        if (_settings.ShowWindowsTerminalShortcuts)
        {
            shortcuts.AddRange(_windowsTerminalShortcutService.GetForegroundShortcuts());
        }

        return shortcuts;
    }

    private async Task ShowVsCodeIntegrationAsync()
    {
        if (_vsCodeIntegrationService.IsConnected)
        {
            var reinstall = MessageBox.Show(
                "VS Code 연동이 정상적으로 연결되어 있습니다. 확장을 다시 설치할까요?",
                "VS Code 연동",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (reinstall != DialogResult.Yes)
            {
                return;
            }
        }
        else
        {
            var install = MessageBox.Show(
                "현재 VS Code 연동 확장이 연결되어 있지 않습니다. 지금 설치할까요?",
                "VS Code 연동",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (install != DialogResult.Yes)
            {
                return;
            }
        }

        _vsCodeIntegrationItem.Enabled = false;
        try
        {
            var result = await VsCodeIntegrationInstaller.InstallAsync();
            MessageBox.Show(
                result.Message,
                "VS Code 연동",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            _vsCodeIntegrationItem.Enabled = true;
        }
    }

    private async Task ShowVisualStudioIntegrationAsync()
    {
        var instances = VisualStudioIntegrationInstaller.GetInstalledInstances();
        if (instances.Count == 0)
        {
            MessageBox.Show(
                "Visual Studio 2022 또는 2026을 찾지 못했습니다.",
                "Visual Studio 연동",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var connected = _visualStudioIntegrationService.GetConnectedVersions();
        var instanceText = string.Join(
            "\n",
            instances.Select(instance =>
            {
                var major = Version.TryParse(instance.Version, out var version) ? version.Major : 0;
                var productYear = major == 17 ? "2022" : major == 18 ? "2026" : instance.Version;
                var state = connected.Contains(productYear, StringComparer.OrdinalIgnoreCase)
                    ? "연결됨"
                    : VisualStudioIntegrationInstaller.IsExtensionInstalled(instance) ? "설치됨" : "미설치";
                return $"• {instance.DisplayName} ({productYear}) — {state}";
            }));
        var install = MessageBox.Show(
            $"다음 Visual Studio에 현재 컨텍스트 단축키 확장을 설치합니다.\n\n{instanceText}\n\n계속할까요?",
            "Visual Studio 연동",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (install != DialogResult.Yes)
        {
            return;
        }

        _visualStudioIntegrationItem.Enabled = false;
        try
        {
            var result = await VisualStudioIntegrationInstaller.InstallAsync(instances);
            MessageBox.Show(
                result.Message,
                "Visual Studio 연동",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            _visualStudioIntegrationItem.Enabled = true;
        }
    }

    private void ShowVsCodeIntegrationPrompt()
    {
        if (_settings.VsCodeIntegrationPromptShown
            || !VsCodeIntegrationInstaller.IsVsCodeInstalled
            || _vsCodeIntegrationService.IsConnected)
        {
            return;
        }

        _settings.VsCodeIntegrationPromptShown = true;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (IOException)
        {
            // The prompt remains non-blocking even when its one-time state cannot be saved.
        }
        catch (UnauthorizedAccessException)
        {
            // The prompt remains non-blocking even when its one-time state cannot be saved.
        }

        ShowBalloon(
            "VS Code 단축키 연동 가능",
            "트레이 메뉴의 ‘VS Code 연동...’에서 현재 컨텍스트 단축키 기능을 설치할 수 있습니다.",
            ToolTipIcon.Info,
            force: true);
    }

    private void ShowVisualStudioIntegrationPrompt()
    {
        if (_settings.VisualStudioIntegrationPromptShown
            || !VisualStudioIntegrationInstaller.IsVisualStudioInstalled
            || _visualStudioIntegrationService.IsConnected)
        {
            return;
        }

        _settings.VisualStudioIntegrationPromptShown = true;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        ShowBalloon(
            "Visual Studio 단축키 연동 가능",
            "트레이 메뉴의 ‘Visual Studio 연동...’에서 2022·2026 현재 컨텍스트 단축키 기능을 설치할 수 있습니다.",
            ToolTipIcon.Info,
            force: true);
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

    private static string FormatHotKey(AppSettings settings) =>
        HotKeyFormatter.Format(settings.Modifiers, settings.Key);

    private static string TrimText(string text, int maximumLength) =>
        text.Length <= maximumLength ? text : text[..(maximumLength - 1)] + "…";
}
