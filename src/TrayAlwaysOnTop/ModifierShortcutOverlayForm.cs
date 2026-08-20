using System.Drawing.Drawing2D;

namespace TrayAlwaysOnTop;

internal sealed record ModifierShortcutHint(
    Keys Key,
    string Description,
    ShortcutVisualKind Kind);

internal sealed class ModifierShortcutOverlayForm : Form
{
    private const int HeaderHeight = 72;
    private const int RowHeight = 38;
    private const int ColumnWidth = 300;
    private const int MaximumVisibleRows = 8;
    private IReadOnlyList<ModifierShortcutHint> _hints = [];
    private HotKeyModifiers _modifiers;
    private int _columns = 1;
    private int _rows;
    private float _scrollOffset;
    private int _scrollDelayTicks;
    private readonly System.Windows.Forms.Timer _scrollTimer = new() { Interval = 33 };

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
        _scrollTimer.Tick += (_, _) => AdvanceScroll();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExToolWindow = 0x00000080;
            const int wsExTransparent = 0x00000020;
            const int wsExNoActivate = 0x08000000;
            var parameters = base.CreateParams;
            parameters.ExStyle |= wsExToolWindow | wsExTransparent | wsExNoActivate;
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
                $"VS Code · {shortcut.Description}",
                shortcut.Kind)));

        if (appHotKeyRegistered && NormalizeModifiers(settings.Modifiers) == _modifiers)
        {
            hints.Add(new ModifierShortcutHint(
                settings.Key,
                "현재 창 항상 위 고정/해제",
                ShortcutVisualKind.ThisApp));
        }

        _hints = hints;
        _columns = Math.Clamp((int)Math.Ceiling(Math.Max(1, hints.Count) / 8d), 1, 3);
        _rows = Math.Max(1, (int)Math.Ceiling(Math.Max(1, hints.Count) / (double)_columns));
        var desiredSize = new Size(
            _columns * ColumnWidth + 40,
            HeaderHeight + Math.Min(_rows, MaximumVisibleRows) * RowHeight + 22);
        var activeScreen = Screen.FromHandle(NativeMethods.GetForegroundWindow());
        var workingArea = activeScreen.WorkingArea;
        Size = new Size(
            Math.Min(desiredSize.Width, workingArea.Width - 24),
            Math.Min(desiredSize.Height, workingArea.Height - 24));
        Location = new Point(
            workingArea.Left + (workingArea.Width - Width) / 2,
            workingArea.Top + (workingArea.Height - Height) / 2);
        UpdateRoundedRegion();
        _scrollOffset = 0;
        _scrollDelayTicks = 60;
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
        _scrollTimer.Start();
    }

    public void HideOverlay()
    {
        if (Visible)
        {
            Hide();
        }

        _scrollTimer.Stop();
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
            $"{FormatModifiers(_modifiers)} 조합 단축키",
            titleFont,
            titleBrush,
            new PointF(20, 14));
        graphics.DrawString(
            "보조키를 누른 상태에서 대상 키를 누르세요",
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

        var rowsPerColumn = (int)Math.Ceiling(_hints.Count / (double)_columns);
        var contentState = graphics.Save();
        graphics.SetClip(new Rectangle(0, HeaderHeight, Width, Height - HeaderHeight));
        for (var index = 0; index < _hints.Count; index++)
        {
            var column = index / rowsPerColumn;
            var row = index % rowsPerColumn;
            DrawHint(graphics, _hints[index], column, row, _scrollOffset, rowFont, keyFont);
        }
        graphics.Restore(contentState);
    }

    private static void DrawHint(
        Graphics graphics,
        ModifierShortcutHint hint,
        int column,
        int row,
        float scrollOffset,
        Font rowFont,
        Font keyFont)
    {
        var x = 20 + column * ColumnWidth;
        var y = HeaderHeight + row * RowHeight + 4 - (int)scrollOffset;
        var keyBounds = new Rectangle(x, y, 62, 29);
        var accent = hint.Kind switch
        {
            ShortcutVisualKind.ThisApp => Color.FromArgb(39, 190, 124),
            ShortcutVisualKind.VsCode => Color.FromArgb(167, 112, 239),
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

        var descriptionBounds = new Rectangle(x + 72, y, ColumnWidth - 88, 29);
        TextRenderer.DrawText(
            graphics,
            hint.Description,
            rowFont,
            descriptionBounds,
            Color.FromArgb(232, 235, 240),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scrollTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void AdvanceScroll()
    {
        var viewportHeight = Math.Max(0, Height - HeaderHeight - 22);
        var maximumOffset = Math.Max(0, _rows * RowHeight - viewportHeight);
        if (maximumOffset <= 0)
        {
            return;
        }

        if (_scrollDelayTicks > 0)
        {
            _scrollDelayTicks--;
            return;
        }

        _scrollOffset += 0.66f;
        if (_scrollOffset >= maximumOffset + RowHeight)
        {
            _scrollOffset = 0;
            _scrollDelayTicks = 60;
        }

        Invalidate(new Rectangle(0, HeaderHeight, Width, Height - HeaderHeight));
    }

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
}
