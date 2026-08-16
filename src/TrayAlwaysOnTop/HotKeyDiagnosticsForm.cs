namespace TrayAlwaysOnTop;

internal sealed class HotKeyDiagnosticsForm : Form
{
    private readonly AppSettings _settings;
    private readonly bool _appHotKeyRegistered;
    private readonly Label _status = new()
    {
        Text = "현재 등록된 전역 단축키를 확인하는 중...",
        AutoSize = true,
        Dock = DockStyle.Fill
    };
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
        View = View.Details
    };

    public HotKeyDiagnosticsForm(AppSettings settings, bool appHotKeyRegistered)
    {
        _settings = settings.Copy();
        _appHotKeyRegistered = appHotKeyRegistered;
        Text = "사용 중인 전역 단축키";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(560, 400);
        StartPosition = FormStartPosition.CenterScreen;

        _list.Columns.Add("단축키", 230);
        _list.Columns.Add("등록 위치", 420);

        var explanation = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            MaximumSize = new Size(670, 0),
            Text = "현재 RegisterHotKey 방식으로 점유된 조합만 표시합니다. 등록한 프로그램 이름은 Windows에서 공개하지 않으므로 구분할 수 없으며, 키보드 후킹 방식의 단축키는 감지되지 않습니다.",
            ForeColor = SystemColors.GrayText
        };

        var closeButton = new Button
        {
            Text = "닫기",
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Right
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_status);
        layout.Controls.Add(_list);
        layout.Controls.Add(explanation);
        layout.Controls.Add(closeButton);

        Controls.Add(layout);
        AcceptButton = closeButton;
        CancelButton = closeButton;
        Shown += async (_, _) => await ScanAsync();
    }

    private async Task ScanAsync()
    {
        UseWaitCursor = true;
        try
        {
            var results = await Task.Run(() => new HotKeyAvailabilityScanner().Scan(_settings, _appHotKeyRegistered));
            if (IsDisposed)
            {
                return;
            }

            _list.BeginUpdate();
            foreach (var result in results)
            {
                var item = new ListViewItem(result.Shortcut);
                item.SubItems.Add(result.Source);
                if (result.IsThisApp)
                {
                    item.Font = new Font(_list.Font, FontStyle.Bold);
                    item.ForeColor = Color.FromArgb(0, 100, 180);
                }

                _list.Items.Add(item);
            }
            _list.EndUpdate();
            _status.Text = $"현재 사용 중이거나 Windows에 예약된 조합: {results.Count}개";
        }
        catch (Exception exception)
        {
            _status.Text = "전역 단축키를 확인하지 못했습니다.";
            MessageBox.Show(exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }
}
