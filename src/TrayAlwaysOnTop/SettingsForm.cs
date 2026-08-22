namespace TrayAlwaysOnTop;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox _control = new() { Text = "Ctrl", AutoSize = true };
    private readonly CheckBox _win = new() { Text = "Win", AutoSize = true };
    private readonly CheckBox _alt = new() { Text = "Alt", AutoSize = true };
    private readonly CheckBox _shift = new() { Text = "Shift", AutoSize = true };
    private readonly ComboBox _key = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox _showBorder = new() { Text = "고정된 창에 파란색 테두리 표시", AutoSize = true };
    private readonly CheckBox _showPinToggle = new() { Text = "고정된 창에 클릭 가능한 핀 토글 표시", AutoSize = true };
    private readonly CheckBox _showNotifications = new() { Text = "고정/해제 알림 표시", AutoSize = true };
    private readonly CheckBox _showGlobalShortcutOverlay = new() { Text = "보조키를 누르면 화면 중앙에 단축키 안내 표시", AutoSize = true };
    private readonly CheckBox _showVsCodeShortcuts = new() { Text = "VS Code의 현재 컨텍스트 단축키 함께 표시", AutoSize = true };
    private readonly CheckBox _showVisualStudioShortcuts = new() { Text = "Visual Studio의 현재 컨텍스트 단축키 함께 표시", AutoSize = true };
    private readonly CheckBox _showWindowsTerminalShortcuts = new() { Text = "Windows Terminal의 설정된 단축키 함께 표시", AutoSize = true };
    private readonly CheckBox _suppressInFullscreen = new() { Text = "전체화면 앱에서는 단축키 안내 표시 안 함", AutoSize = true };
    private readonly ListBox _excludedProcesses = new() { Dock = DockStyle.Fill, Height = 92 };
    private readonly CheckBox _startWithWindows = new() { Text = "Windows 시작 시 자동 실행", AutoSize = true };

    public AppSettings Result { get; private set; }

    public SettingsForm(AppSettings settings)
    {
        Result = settings.Copy();

        Text = "Tray Always On Top 설정";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 610);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;

        foreach (var key in GetSelectableKeys())
        {
            _key.Items.Add(key);
        }

        _control.Checked = settings.Modifiers.HasFlag(HotKeyModifiers.Control);
        _win.Checked = settings.Modifiers.HasFlag(HotKeyModifiers.Win);
        _alt.Checked = settings.Modifiers.HasFlag(HotKeyModifiers.Alt);
        _shift.Checked = settings.Modifiers.HasFlag(HotKeyModifiers.Shift);
        _key.SelectedItem = settings.Key;
        if (_key.SelectedIndex < 0)
        {
            _key.SelectedItem = Keys.T;
        }

        _showBorder.Checked = settings.ShowBorder;
        _showPinToggle.Checked = settings.ShowPinToggle;
        _showNotifications.Checked = settings.ShowNotifications;
        _showGlobalShortcutOverlay.Checked = settings.ShowGlobalShortcutOverlay;
        _showVsCodeShortcuts.Checked = settings.ShowVsCodeShortcuts;
        _showVisualStudioShortcuts.Checked = settings.ShowVisualStudioShortcuts;
        _showWindowsTerminalShortcuts.Checked = settings.ShowWindowsTerminalShortcuts;
        _suppressInFullscreen.Checked = settings.SuppressShortcutOverlayInFullscreenApps;
        foreach (var processName in ForegroundShortcutOverlayPolicy.NormalizeProcessNames(settings.ShortcutOverlayExcludedProcesses))
        {
            _excludedProcesses.Items.Add(processName);
        }
        _startWithWindows.Checked = settings.StartWithWindows;

        var modifiers = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        modifiers.Controls.AddRange([_control, _win, _alt, _shift, _key]);

        var saveButton = new Button { Text = "저장", DialogResult = DialogResult.None, AutoSize = true };
        saveButton.Click += SaveButton_Click;
        var cancelButton = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.AddRange([cancelButton, saveButton]);

        var removeExcludedButton = new Button { Text = "선택한 앱 삭제", AutoSize = true };
        removeExcludedButton.Click += (_, _) =>
        {
            if (_excludedProcesses.SelectedIndex >= 0)
            {
                _excludedProcesses.Items.RemoveAt(_excludedProcesses.SelectedIndex);
            }
        };
        var excludedApps = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0, 8, 0, 8)
        };
        excludedApps.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        excludedApps.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        excludedApps.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        excludedApps.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        excludedApps.Controls.Add(new Label
        {
            Text = "단축키 안내 제외 앱",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        });
        excludedApps.Controls.Add(_excludedProcesses);
        excludedApps.Controls.Add(removeExcludedButton);
        excludedApps.Controls.Add(new Label
        {
            Text = "앱 추가는 트레이 메뉴의 ‘현재 앱 단축키 안내 제외’를 사용하세요.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(460, 0),
            Margin = new Padding(0, 6, 0, 0)
        });

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 14
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = "전역 단축키",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        });
        layout.Controls.Add(modifiers);
        layout.Controls.Add(_showBorder);
        layout.Controls.Add(_showPinToggle);
        layout.Controls.Add(_showNotifications);
        layout.Controls.Add(_showGlobalShortcutOverlay);
        layout.Controls.Add(_showVsCodeShortcuts);
        layout.Controls.Add(_showVisualStudioShortcuts);
        layout.Controls.Add(_showWindowsTerminalShortcuts);
        layout.Controls.Add(_suppressInFullscreen);
        layout.Controls.Add(excludedApps);
        layout.Controls.Add(_startWithWindows);
        layout.Controls.Add(new Label
        {
            Text = "단축키를 누르거나 트레이 메뉴에서 창을 선택하면 항상 위 상태가 전환됩니다.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(380, 0),
            Margin = new Padding(0, 12, 0, 0)
        });
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void SaveButton_Click(object? sender, EventArgs eventArgs)
    {
        var modifiers = HotKeyModifiers.None;
        if (_control.Checked) modifiers |= HotKeyModifiers.Control;
        if (_win.Checked) modifiers |= HotKeyModifiers.Win;
        if (_alt.Checked) modifiers |= HotKeyModifiers.Alt;
        if (_shift.Checked) modifiers |= HotKeyModifiers.Shift;

        if (modifiers == HotKeyModifiers.None)
        {
            MessageBox.Show("Ctrl, Win, Alt, Shift 중 하나 이상을 선택해 주세요.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result = new AppSettings
        {
            Modifiers = modifiers,
            Key = (Keys)(_key.SelectedItem ?? Keys.T),
            ShowBorder = _showBorder.Checked,
            ShowPinToggle = _showPinToggle.Checked,
            ShowNotifications = _showNotifications.Checked,
            ShowGlobalShortcutOverlay = _showGlobalShortcutOverlay.Checked,
            ShowVsCodeShortcuts = _showVsCodeShortcuts.Checked,
            ShowVisualStudioShortcuts = _showVisualStudioShortcuts.Checked,
            ShowWindowsTerminalShortcuts = _showWindowsTerminalShortcuts.Checked,
            SuppressShortcutOverlayInFullscreenApps = _suppressInFullscreen.Checked,
            ShortcutOverlayExcludedProcesses = ForegroundShortcutOverlayPolicy.NormalizeProcessNames(
                _excludedProcesses.Items.Cast<string>()).ToList(),
            StartWithWindows = _startWithWindows.Checked,
            VsCodeIntegrationPromptShown = Result.VsCodeIntegrationPromptShown,
            VisualStudioIntegrationPromptShown = Result.VisualStudioIntegrationPromptShown
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    private static IEnumerable<Keys> GetSelectableKeys()
    {
        for (var key = Keys.A; key <= Keys.Z; key++)
        {
            yield return key;
        }

        for (var key = Keys.F1; key <= Keys.F24; key++)
        {
            yield return key;
        }

        yield return Keys.Insert;
        yield return Keys.Home;
        yield return Keys.End;
        yield return Keys.PageUp;
        yield return Keys.PageDown;
    }
}
