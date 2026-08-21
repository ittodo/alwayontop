using System.Drawing.Drawing2D;

namespace TrayAlwaysOnTop;

internal sealed record ModifierShortcutHint(
    Keys Key,
    string Description,
    ShortcutVisualKind Kind);

internal sealed record ShortcutOverlayLayout(
    int Columns,
    int RowsPerPage,
    int ItemsPerPage,
    int PageCount,
    Size WindowSize);

internal sealed class ModifierShortcutOverlayForm : Form
{
    private const int HeaderHeight = 78;
    private const int RowHeight = 54;
    private const int ColumnWidth = 380;
    private const int PreferredRowsPerColumn = 7;
    private const int MaximumRowsPerPage = 10;
    private const int FooterHeight = 52;
    private const int WindowMargin = 24;
    private IReadOnlyList<ModifierShortcutHint> _hints = [];
    private HotKeyModifiers _modifiers;
    private int _columns = 1;
    private int _itemsPerPage = 1;
    private int _pageCount = 1;
    private int _currentPage;
    private Rectangle _previousPageBounds;
    private Rectangle _nextPageBounds;
    private IReadOnlyList<ContextualShortcut> _contextualShortcuts = [];
    private string? _titleOverride;

    public ModifierShortcutOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(29, 32, 38);
        Opacity = 0.96;
        DoubleBuffered = true;
        AccessibleName = "전역 보조키 단축키 안내";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExToolWindow = 0x00000080;
            const int wsExNoActivate = 0x08000000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= wsExToolWindow | wsExNoActivate;
            return parameters;
        }
    }

    public void ShowFor(
        HotKeyModifiers modifiers,
        AppSettings settings,
        bool appHotKeyRegistered,
        IReadOnlyList<ContextualShortcut> contextualShortcuts)
    {
        _modifiers = NormalizeModifiers(modifiers);
        _contextualShortcuts = contextualShortcuts;
        _titleOverride = null;
        var hints = WindowsShortcutCatalog.Shortcuts
            .Where(shortcut => NormalizeModifiers(shortcut.Modifiers) == _modifiers)
            .Select(shortcut => new ModifierShortcutHint(
                shortcut.Key,
                shortcut.Description,
                ShortcutVisualKind.WindowsDefault))
            .ToList();

        hints.InsertRange(0, contextualShortcuts
            .Where(shortcut => NormalizeModifiers(shortcut.Modifiers) == _modifiers)
            .Select(shortcut => new ModifierShortcutHint(
                shortcut.Key,
                $"{shortcut.Source} · {shortcut.Description}",
                shortcut.Kind)));

        if (appHotKeyRegistered && NormalizeModifiers(settings.Modifiers) == _modifiers)
        {
            hints.Add(new ModifierShortcutHint(
                settings.Key,
                "현재 창 항상 위 고정/해제",
                ShortcutVisualKind.ThisApp));
        }

        ApplyHints(hints);
    }

    public bool TryShowChordFor(Keys pressedKey)
    {
        var matches = _contextualShortcuts
            .Where(shortcut => NormalizeModifiers(shortcut.Modifiers) == _modifiers
                && (shortcut.Key & Keys.KeyCode) == (pressedKey & Keys.KeyCode)
                && !string.IsNullOrWhiteSpace(shortcut.RemainingChord))
            .Select(shortcut =>
            {
                var parsed = VsCodeKeyGestureParser.TryParse(
                    shortcut.RemainingChord!,
                    out var modifiers,
                    out var key,
                    out _);
                return (Shortcut: shortcut, Parsed: parsed, Modifiers: modifiers, Key: key);
            })
            .Where(item => item.Parsed)
            .ToArray();
        if (matches.Length == 0)
        {
            return false;
        }

        var firstStroke = HotKeyFormatter.Format(_modifiers, pressedKey);
        _modifiers = matches[0].Modifiers;
        _titleOverride = $"{firstStroke} 다음 키";
        var hints = matches
            .Where(item => item.Modifiers == _modifiers)
            .Select(item => new ModifierShortcutHint(
                item.Key,
                $"{item.Shortcut.Source} · {RemoveNextKeySuffix(item.Shortcut.Description)}",
                item.Shortcut.Kind))
            .DistinctBy(hint => (hint.Key, hint.Description))
            .ToList();
        ApplyHints(hints);
        return true;
    }

    private void ApplyHints(IReadOnlyList<ModifierShortcutHint> hints)
    {
        _hints = hints;
        var activeScreen = Screen.FromHandle(NativeMethods.GetForegroundWindow());
        var workingArea = activeScreen.WorkingArea;
        var layout = CalculateLayout(hints.Count, workingArea.Size);
        _columns = layout.Columns;
        _itemsPerPage = layout.ItemsPerPage;
        _pageCount = layout.PageCount;
        _currentPage = 0;
        Size = layout.WindowSize;
        Location = new Point(
            workingArea.Left + (workingArea.Width - Width) / 2,
            workingArea.Top + (workingArea.Height - Height) / 2);
        UpdateNavigationBounds();
        UpdateRoundedRegion();
        Invalidate();

        if (!Visible)
        {
            Show();
        }

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HwndTopmost,
            Left,
            Top,
            Width,
            Height,
            NativeMethods.SwpNoActivate);
    }

    public void HideOverlay()
    {
        if (Visible)
        {
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(BackColor);

        using var titleFont = new Font(Font.FontFamily, 14f, FontStyle.Bold);
        using var rowFont = new Font(Font.FontFamily, 10f, FontStyle.Regular);
        using var keyFont = new Font(Font.FontFamily, 9f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Color.White);
        using var secondaryBrush = new SolidBrush(Color.FromArgb(185, 192, 202));
        graphics.DrawString(
            _titleOverride ?? $"{FormatModifiers(_modifiers)} 조합 단축키",
            titleFont,
            titleBrush,
            new PointF(20, 14));
        graphics.DrawString(
            $"보조키를 누른 상태에서 대상 키를 누르세요 · {_hints.Count}개 항목",
            rowFont,
            secondaryBrush,
            new PointF(20, 43));

        if (_hints.Count == 0)
        {
            graphics.DrawString(
                "알려진 Windows 또는 앱 단축키가 없습니다.",
                rowFont,
                secondaryBrush,
                new PointF(20, HeaderHeight + 10));
            return;
        }

        var pageHints = _hints
            .Skip(_currentPage * _itemsPerPage)
            .Take(_itemsPerPage)
            .ToArray();
        var rowsPerColumn = Math.Max(1, (int)Math.Ceiling(pageHints.Length / (double)_columns));
        var contentBottom = _pageCount > 1 ? Height - FooterHeight : Height;
        var contentState = graphics.Save();
        graphics.SetClip(new Rectangle(0, HeaderHeight, Width, contentBottom - HeaderHeight));
        for (var index = 0; index < pageHints.Length; index++)
        {
            var column = index / rowsPerColumn;
            var row = index % rowsPerColumn;
            DrawHint(graphics, pageHints[index], column, row, rowFont, keyFont);
        }
        graphics.Restore(contentState);

        if (_pageCount > 1)
        {
            DrawPageNavigation(graphics, rowFont);
        }
    }

    private static void DrawHint(
        Graphics graphics,
        ModifierShortcutHint hint,
        int column,
        int row,
        Font rowFont,
        Font keyFont)
    {
        var x = 20 + column * ColumnWidth;
        var y = HeaderHeight + row * RowHeight + 4;
        var keyBounds = new Rectangle(x, y + 8, 62, 30);
        var accent = hint.Kind switch
        {
            ShortcutVisualKind.ThisApp => Color.FromArgb(39, 190, 124),
            ShortcutVisualKind.VsCode => Color.FromArgb(167, 112, 239),
            ShortcutVisualKind.VisualStudio => Color.FromArgb(45, 162, 228),
            ShortcutVisualKind.WindowsTerminal => Color.FromArgb(235, 158, 52),
            _ => Color.FromArgb(65, 143, 240)
        };
        using var path = CreateRoundedRectangle(keyBounds, 6f);
        using var keyBrush = new SolidBrush(Color.FromArgb(58, 64, 75));
        using var borderPen = new Pen(accent, 1.5f);
        graphics.FillPath(keyBrush, path);
        graphics.DrawPath(borderPen, path);
        TextRenderer.DrawText(
            graphics,
            HotKeyFormatter.Format(HotKeyModifiers.None, hint.Key),
            keyFont,
            keyBounds,
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var descriptionBounds = new Rectangle(x + 72, y, ColumnWidth - 88, 46);
        TextRenderer.DrawText(
            graphics,
            hint.Description,
            rowFont,
            descriptionBounds,
            Color.FromArgb(232, 235, 240),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        if (eventArgs.Button != MouseButtons.Left || _pageCount <= 1)
        {
            return;
        }

        if (_previousPageBounds.Contains(eventArgs.Location))
        {
            NavigatePage(-1);
        }
        else if (_nextPageBounds.Contains(eventArgs.Location))
        {
            NavigatePage(1);
        }
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        Cursor = IsInteractiveNavigationPoint(eventArgs.Location) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        Cursor = Cursors.Default;
    }

    protected override void OnMouseWheel(MouseEventArgs eventArgs)
    {
        base.OnMouseWheel(eventArgs);
        if (_pageCount <= 1 || eventArgs.Delta == 0)
        {
            return;
        }

        NavigatePage(eventArgs.Delta < 0 ? 1 : -1);
    }

    protected override void WndProc(ref Message message)
    {
        const int wmNcHitTest = 0x0084;
        const int wmMouseActivate = 0x0021;
        const int htClient = 1;
        const int htTransparent = -1;
        const int maNoActivate = 3;

        if (message.Msg == wmMouseActivate)
        {
            message.Result = new nint(maNoActivate);
            return;
        }

        base.WndProc(ref message);
        if (message.Msg == wmNcHitTest)
        {
            var packedPoint = message.LParam.ToInt64();
            var screenPoint = new Point(
                unchecked((short)(packedPoint & 0xffff)),
                unchecked((short)((packedPoint >> 16) & 0xffff)));
            var clientPoint = PointToClient(screenPoint);
            message.Result = IsInteractiveNavigationPoint(clientPoint)
                    ? new nint(htClient)
                    : new nint(htTransparent);
        }
    }

    private void NavigatePage(int direction)
    {
        var nextPage = CalculatePage(_currentPage, direction, _pageCount);
        if (nextPage == _currentPage)
        {
            return;
        }

        _currentPage = nextPage;
        Invalidate();
    }

    private void DrawPageNavigation(Graphics graphics, Font font)
    {
        var inactive = Color.FromArgb(92, 99, 110);
        var active = Color.FromArgb(235, 238, 243);
        TextRenderer.DrawText(
            graphics,
            "◀",
            font,
            _previousPageBounds,
            _currentPage > 0 ? active : inactive,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(
            graphics,
            $"{_currentPage + 1} / {_pageCount}",
            font,
            new Rectangle(Width / 2 - 42, Height - FooterHeight + 8, 84, 34),
            active,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(
            graphics,
            "▶",
            font,
            _nextPageBounds,
            _currentPage < _pageCount - 1 ? active : inactive,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private void UpdateNavigationBounds()
    {
        var y = Height - FooterHeight + 8;
        _previousPageBounds = new Rectangle(Width / 2 - 86, y, 36, 34);
        _nextPageBounds = new Rectangle(Width / 2 + 50, y, 36, 34);
    }

    private bool IsInteractiveNavigationPoint(Point point) =>
        (_currentPage > 0 && _previousPageBounds.Contains(point))
        || (_currentPage < _pageCount - 1 && _nextPageBounds.Contains(point));

    internal static ShortcutOverlayLayout CalculateLayout(int hintCount, Size workingArea)
    {
        var count = Math.Max(1, hintCount);
        var maximumColumns = Math.Clamp(
            (workingArea.Width - WindowMargin - 40) / ColumnWidth,
            1,
            4);
        var maximumRowsByScreen = Math.Max(
            1,
            (workingArea.Height - WindowMargin - HeaderHeight - FooterHeight - 16) / RowHeight);
        var rowsPerPage = Math.Min(MaximumRowsPerPage, maximumRowsByScreen);
        var columns = Math.Clamp(
            (int)Math.Ceiling(count / (double)PreferredRowsPerColumn),
            1,
            maximumColumns);
        if (count > columns * rowsPerPage)
        {
            columns = Math.Min(maximumColumns, (int)Math.Ceiling(count / (double)rowsPerPage));
        }

        var itemsPerPage = Math.Max(1, columns * rowsPerPage);
        var pageCount = Math.Max(1, (int)Math.Ceiling(hintCount / (double)itemsPerPage));
        var firstPageCount = Math.Min(count, itemsPerPage);
        var visibleRows = Math.Max(1, (int)Math.Ceiling(firstPageCount / (double)columns));
        var footerHeight = pageCount > 1 ? FooterHeight : 22;
        var windowSize = new Size(
            Math.Min(columns * ColumnWidth + 40, workingArea.Width - WindowMargin),
            Math.Min(HeaderHeight + visibleRows * RowHeight + footerHeight, workingArea.Height - WindowMargin));
        return new ShortcutOverlayLayout(columns, rowsPerPage, itemsPerPage, pageCount, windowSize);
    }

    internal static int CalculatePage(int currentPage, int direction, int pageCount) =>
        Math.Clamp(currentPage + Math.Sign(direction), 0, Math.Max(0, pageCount - 1));

    private void UpdateRoundedRegion()
    {
        using var path = CreateRoundedRectangle(new Rectangle(0, 0, Width, Height), 16f);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static HotKeyModifiers NormalizeModifiers(HotKeyModifiers modifiers) =>
        modifiers & (HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift | HotKeyModifiers.Win);

    private static string FormatModifiers(HotKeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotKeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotKeyModifiers.Win)) parts.Add("Win");
        if (modifiers.HasFlag(HotKeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotKeyModifiers.Shift)) parts.Add("Shift");
        return string.Join(" + ", parts);
    }

    private static string RemoveNextKeySuffix(string description)
    {
        var separator = description.IndexOf(" · 다음 키:", StringComparison.Ordinal);
        return separator >= 0 ? description[..separator] : description;
    }
}
