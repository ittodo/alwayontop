namespace TrayAlwaysOnTop;

internal sealed class HotKeyDiagnosticsForm : Form
{
    private readonly AppSettings _settings;
    private readonly bool _appHotKeyRegistered;
    private readonly Label _unavailableStatus = new()
    {
        Text = "현재 등록할 수 없는 전역 단축키를 확인하는 중...",
        AutoSize = true,
        Dock = DockStyle.Fill
    };
    private readonly ListView _unavailableList = CreateListView();

    public HotKeyDiagnosticsForm(AppSettings settings, bool appHotKeyRegistered)
    {
        _settings = settings.Copy();
        _appHotKeyRegistered = appHotKeyRegistered;
        Text = "전역 단축키 목록";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 570);
        MinimumSize = new Size(620, 440);
        StartPosition = FormStartPosition.CenterScreen;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateWindowsShortcutsPage());
        tabs.TabPages.Add(CreateUnavailablePage());
        tabs.TabPages.Add(CreateThisAppPage());

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
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(tabs);
        layout.Controls.Add(closeButton);

        Controls.Add(layout);
        AcceptButton = closeButton;
        CancelButton = closeButton;
        Shown += async (_, _) => await ScanAsync();
    }

    private TabPage CreateWindowsShortcutsPage()
    {
        var list = CreateListView();
        list.Columns.Add("단축키", 230);
        list.Columns.Add("Windows 기본 기능", 460);
        foreach (var shortcut in WindowsShortcutCatalog.Shortcuts)
        {
            var item = new ListViewItem(shortcut.Shortcut);
            item.SubItems.Add(shortcut.Description);
            list.Items.Add(item);
        }

        return CreateTabPage(
            "Windows 기본",
            $"Windows에서 기본으로 제공하는 대표 단축키 {WindowsShortcutCatalog.Shortcuts.Count}개입니다. Windows 버전, 설정 및 앱에 따라 일부 동작이 달라질 수 있습니다.",
            list);
    }

    private TabPage CreateUnavailablePage()
    {
        _unavailableList.Columns.Add("단축키", 230);
        _unavailableList.Columns.Add("상태", 460);
        return CreateTabPage(
            "등록 불가",
            "실제 등록을 시도해 오류 1409가 발생한 조합입니다. Windows는 점유한 프로그램 이름을 공개하지 않으므로 소유자는 구분할 수 없습니다.",
            _unavailableList,
            _unavailableStatus);
    }

    private TabPage CreateThisAppPage()
    {
        var list = CreateListView();
        list.Columns.Add("단축키", 230);
        list.Columns.Add("상태", 460);

        var item = new ListViewItem(HotKeyFormatter.Format(_settings.Modifiers, _settings.Key));
        item.SubItems.Add(_appHotKeyRegistered ? "Tray Always On Top에 등록됨" : "등록되지 않음");
        item.Font = new Font(list.Font, FontStyle.Bold);
        item.ForeColor = _appHotKeyRegistered
            ? Color.FromArgb(0, 100, 180)
            : Color.Firebrick;
        list.Items.Add(item);

        return CreateTabPage(
            "내가 등록한 키",
            _appHotKeyRegistered
                ? "현재 이 앱이 실제로 등록하여 동작하는 전역 단축키입니다."
                : "설정된 단축키가 있지만 현재 등록되지 않아 동작하지 않습니다.",
            list);
    }

    private static TabPage CreateTabPage(string title, string explanation, Control content, Control? status = null)
    {
        var page = new TabPage(title) { Padding = new Padding(10) };
        var description = new Label
        {
            Text = explanation,
            AutoSize = true,
            Dock = DockStyle.Fill,
            MaximumSize = new Size(710, 0),
            ForeColor = SystemColors.GrayText
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = status is null ? 2 : 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (status is not null)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(description);
        if (status is not null)
        {
            layout.Controls.Add(status);
        }
        layout.Controls.Add(content);
        page.Controls.Add(layout);
        return page;
    }

    private static ListView CreateListView() => new()
    {
        Dock = DockStyle.Fill,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
        View = View.Details
    };

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

            _unavailableList.BeginUpdate();
            _unavailableList.Items.Clear();
            foreach (var result in results)
            {
                var item = new ListViewItem(result.Shortcut);
                item.SubItems.Add($"등록 불가 — {result.Source}");
                _unavailableList.Items.Add(item);
            }
            _unavailableList.EndUpdate();
            _unavailableStatus.Text = results.Count == 0
                ? "검사 범위에서 등록할 수 없는 조합을 찾지 못했습니다."
                : $"현재 등록할 수 없는 조합: {results.Count}개";
        }
        catch (Exception exception)
        {
            _unavailableStatus.Text = "전역 단축키를 확인하지 못했습니다.";
            MessageBox.Show(exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }
}
