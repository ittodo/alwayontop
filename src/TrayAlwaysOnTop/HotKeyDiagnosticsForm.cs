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
    private readonly KeyboardShortcutMapControl _unavailableKeyboard = new() { Dock = DockStyle.Fill };

    public HotKeyDiagnosticsForm(
        AppSettings settings,
        bool appHotKeyRegistered,
        IReadOnlyList<ContextualShortcut>? contextualShortcuts = null,
        string? contextualStatus = null)
    {
        _settings = settings.Copy();
        _appHotKeyRegistered = appHotKeyRegistered;
        Text = "전역 단축키 목록";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(980, 720);
        MinimumSize = new Size(760, 590);
        StartPosition = FormStartPosition.CenterScreen;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreateContextualShortcutsPage(contextualShortcuts ?? [], contextualStatus));
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

    private static TabPage CreateContextualShortcutsPage(
        IReadOnlyList<ContextualShortcut> shortcuts,
        string? status)
    {
        var keyboard = new KeyboardShortcutMapControl { Dock = DockStyle.Fill };
        var list = CreateListView();
        list.Columns.Add("단축키", 230);
        list.Columns.Add("현재 VS Code 기능", 620);
        var visuals = shortcuts
            .Select(shortcut => new KeyboardShortcutVisual(
                shortcut.Modifiers,
                shortcut.Key,
                shortcut.Shortcut,
                shortcut.Description,
                shortcut.Kind))
            .ToArray();
        PopulateList(list, visuals);
        BindSelection(list, keyboard);
        keyboard.SetShortcuts(visuals);
        SelectFirstItem(list);
        return CreateTabPage(
            "현재 앱",
            $"{status ?? "VS Code 연동 상태를 확인할 수 없습니다."} · 현재 활성 VS Code 컨텍스트에서 확실한 단축키 {shortcuts.Count}개",
            keyboard,
            list);
    }

    private TabPage CreateWindowsShortcutsPage()
    {
        var keyboard = new KeyboardShortcutMapControl { Dock = DockStyle.Fill };
        var list = CreateListView();
        list.Columns.Add("단축키", 230);
        list.Columns.Add("Windows 기본 기능", 620);
        var visuals = WindowsShortcutCatalog.Shortcuts
            .Select(shortcut => new KeyboardShortcutVisual(
                shortcut.Modifiers,
                shortcut.Key,
                shortcut.Shortcut,
                shortcut.Description,
                ShortcutVisualKind.WindowsDefault))
            .ToArray();
        PopulateList(list, visuals);
        BindSelection(list, keyboard);
        keyboard.SetShortcuts(visuals);
        SelectFirstItem(list);

        return CreateTabPage(
            "Windows 기본",
            $"Windows에서 기본으로 제공하는 대표 단축키 {WindowsShortcutCatalog.Shortcuts.Count}개입니다. 목록을 선택하면 조합이 키보드 위에 표시됩니다.",
            keyboard,
            list);
    }

    private TabPage CreateUnavailablePage()
    {
        _unavailableList.Columns.Add("단축키", 230);
        _unavailableList.Columns.Add("상태", 620);
        BindSelection(_unavailableList, _unavailableKeyboard);
        return CreateTabPage(
            "등록 불가",
            "실제 등록을 시도해 오류 1409가 발생한 조합입니다. Windows는 점유한 프로그램 이름을 공개하지 않으므로 소유자는 구분할 수 없습니다.",
            _unavailableKeyboard,
            _unavailableList,
            _unavailableStatus);
    }

    private TabPage CreateThisAppPage()
    {
        var keyboard = new KeyboardShortcutMapControl { Dock = DockStyle.Fill };
        var list = CreateListView();
        list.Columns.Add("단축키", 230);
        list.Columns.Add("상태", 620);

        var visual = new KeyboardShortcutVisual(
            _settings.Modifiers,
            _settings.Key,
            HotKeyFormatter.Format(_settings.Modifiers, _settings.Key),
            _appHotKeyRegistered ? "Tray Always On Top에 등록됨" : "등록되지 않아 동작하지 않음",
            ShortcutVisualKind.ThisApp);
        PopulateList(list, [visual]);
        var item = list.Items[0];
        item.Font = new Font(list.Font, FontStyle.Bold);
        item.ForeColor = _appHotKeyRegistered
            ? Color.FromArgb(0, 112, 72)
            : Color.Firebrick;
        BindSelection(list, keyboard);
        keyboard.SetShortcuts([visual]);
        SelectFirstItem(list);

        return CreateTabPage(
            "내가 등록한 키",
            _appHotKeyRegistered
                ? "현재 이 앱이 실제로 등록하여 동작하는 전역 단축키입니다."
                : "설정된 단축키가 있지만 현재 등록되지 않아 동작하지 않습니다.",
            keyboard,
            list);
    }

    private static TabPage CreateTabPage(
        string title,
        string explanation,
        Control keyboard,
        Control content,
        Control? status = null)
    {
        var page = new TabPage(title) { Padding = new Padding(10) };
        var description = new Label
        {
            Text = explanation,
            AutoSize = true,
            Dock = DockStyle.Fill,
            MaximumSize = new Size(900, 0),
            ForeColor = SystemColors.GrayText
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = status is null ? 3 : 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (status is not null)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        layout.Controls.Add(description);
        if (status is not null)
        {
            layout.Controls.Add(status);
        }
        layout.Controls.Add(keyboard);
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
        MultiSelect = false,
        View = View.Details
    };

    private static void PopulateList(ListView list, IReadOnlyList<KeyboardShortcutVisual> visuals)
    {
        foreach (var visual in visuals)
        {
            var item = new ListViewItem(visual.Shortcut) { Tag = visual };
            item.SubItems.Add(visual.Description);
            list.Items.Add(item);
        }
    }

    private static void BindSelection(ListView list, KeyboardShortcutMapControl keyboard)
    {
        list.SelectedIndexChanged += (_, _) =>
        {
            var selected = list.SelectedItems.Count == 0
                ? null
                : list.SelectedItems[0].Tag as KeyboardShortcutVisual;
            keyboard.SelectShortcut(selected);
        };
        keyboard.ShortcutSelected += selected =>
        {
            var matchingItem = list.Items
                .Cast<ListViewItem>()
                .FirstOrDefault(item => item.Tag as KeyboardShortcutVisual == selected);
            if (matchingItem is null)
            {
                return;
            }

            list.SelectedItems.Clear();
            matchingItem.Selected = true;
            matchingItem.Focused = true;
            matchingItem.EnsureVisible();
        };
    }

    private static void SelectFirstItem(ListView list)
    {
        if (list.Items.Count == 0)
        {
            return;
        }

        list.Items[0].Selected = true;
        list.Items[0].Focused = true;
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

            var visuals = results
                .Select(result => new KeyboardShortcutVisual(
                    result.Modifiers,
                    result.Key,
                    result.Shortcut,
                    $"등록 불가 — {result.Source}",
                    ShortcutVisualKind.Unavailable))
                .ToArray();
            _unavailableList.BeginUpdate();
            _unavailableList.Items.Clear();
            PopulateList(_unavailableList, visuals);
            _unavailableList.EndUpdate();
            _unavailableKeyboard.SetShortcuts(visuals);
            SelectFirstItem(_unavailableList);
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
