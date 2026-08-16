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
    private readonly CheckBox _startWithWindows = new() { Text = "Windows 시작 시 자동 실행", AutoSize = true };

    public AppSettings Result { get; private set; }

    public SettingsForm(AppSettings settings)
    {
        Result = settings.Copy();

        Text = "Tray Always On Top 설정";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(430, 305);
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

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 8
        };
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
            StartWithWindows = _startWithWindows.Checked
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
