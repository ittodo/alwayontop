using System.Drawing.Drawing2D;

namespace TrayAlwaysOnTop;

internal enum ShortcutVisualKind
{
    WindowsDefault,
    Unavailable,
    ThisApp
}

internal sealed record KeyboardShortcutVisual(
    HotKeyModifiers Modifiers,
    Keys Key,
    string Shortcut,
    string Description,
    ShortcutVisualKind Kind);

internal sealed class KeyboardShortcutMapControl : Control
{
    private static readonly IReadOnlyList<KeySpec> KeyboardLayout = BuildKeyboardLayout();
    private IReadOnlyList<KeyboardShortcutVisual> _shortcuts = [];
    private KeyboardShortcutVisual? _selectedShortcut;

    public KeyboardShortcutMapControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;
        MinimumSize = new Size(480, 210);
        AccessibleName = "단축키 키보드 오버레이";
    }

    public void SetShortcuts(IReadOnlyList<KeyboardShortcutVisual> shortcuts)
    {
        _shortcuts = shortcuts;
        _selectedShortcut = shortcuts.FirstOrDefault();
        Invalidate();
    }

    public void SelectShortcut(KeyboardShortcutVisual? shortcut)
    {
        _selectedShortcut = shortcut;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var scale = DeviceDpi / 96f;
        var padding = 10f * scale;
        var headerHeight = 34f * scale;
        DrawSelectionHeader(graphics, new RectangleF(padding, padding, ClientSize.Width - padding * 2, headerHeight));

        var availableWidth = Math.Max(1f, ClientSize.Width - padding * 2);
        var availableHeight = Math.Max(1f, ClientSize.Height - headerHeight - padding * 2);
        var unit = Math.Min(availableWidth / 19f, availableHeight / 6f);
        var keyboardWidth = unit * 19f;
        var keyboardHeight = unit * 6f;
        var originX = (ClientSize.Width - keyboardWidth) / 2f;
        var originY = headerHeight + padding + Math.Max(0f, (availableHeight - keyboardHeight) / 2f);
        var gap = Math.Max(1.5f, 2.2f * scale);

        using var keyFont = new Font(Font.FontFamily, Math.Clamp(unit * 0.20f, 7f * scale, 10f * scale), FontStyle.Regular, GraphicsUnit.Pixel);
        using var badgeFont = new Font(Font.FontFamily, Math.Clamp(unit * 0.18f, 7f * scale, 9f * scale), FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelBrush = new SolidBrush(Color.FromArgb(38, 43, 48));
        using var borderPen = new Pen(Color.FromArgb(168, 174, 181), Math.Max(1f, scale));

        foreach (var key in KeyboardLayout)
        {
            var rectangle = new RectangleF(
                originX + key.X * unit + gap / 2,
                originY + key.Y * unit + gap / 2,
                key.Width * unit - gap,
                key.Height * unit - gap);
            var relatedShortcuts = _shortcuts.Where(shortcut => KeyMatches(shortcut.Key, key.Key)).ToArray();
            var isSelectedTarget = _selectedShortcut is not null && KeyMatches(_selectedShortcut.Key, key.Key);
            var isSelectedModifier = _selectedShortcut is not null
                && key.Modifier != HotKeyModifiers.None
                && _selectedShortcut.Modifiers.HasFlag(key.Modifier);
            var isSelected = isSelectedTarget || isSelectedModifier;
            var visualKind = isSelected
                ? _selectedShortcut!.Kind
                : relatedShortcuts.FirstOrDefault()?.Kind;
            var fillColor = visualKind is null
                ? Color.FromArgb(245, 246, 248)
                : GetOverlayColor(visualKind.Value, isSelected);

            using var path = CreateRoundedRectangle(rectangle, Math.Max(3f, 5f * scale));
            using var fillBrush = new SolidBrush(fillColor);
            graphics.FillPath(fillBrush, path);
            if (isSelected)
            {
                using var selectedPen = new Pen(GetAccentColor(visualKind!.Value), Math.Max(2f, 2.2f * scale));
                graphics.DrawPath(selectedPen, path);
            }
            else
            {
                graphics.DrawPath(borderPen, path);
            }

            TextRenderer.DrawText(
                graphics,
                key.Label,
                keyFont,
                Rectangle.Round(rectangle),
                labelBrush.Color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (relatedShortcuts.Length > 1)
            {
                DrawCountBadge(graphics, rectangle, relatedShortcuts.Length, relatedShortcuts[0].Kind, badgeFont, scale);
            }
        }
    }

    private void DrawSelectionHeader(Graphics graphics, RectangleF bounds)
    {
        var selected = _selectedShortcut;
        var text = selected is null
            ? "목록에서 단축키를 선택하면 키보드 위에 표시됩니다."
            : $"{selected.Shortcut}  —  {selected.Description}";
        var accent = selected is null ? Color.FromArgb(105, 110, 118) : GetAccentColor(selected.Kind);
        var dotSize = Math.Max(8f, 10f * DeviceDpi / 96f);
        using var dotBrush = new SolidBrush(accent);
        graphics.FillEllipse(dotBrush, bounds.X, bounds.Y + (bounds.Height - dotSize) / 2f, dotSize, dotSize);
        using var headerFont = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(
            graphics,
            text,
            headerFont,
            Rectangle.Round(new RectangleF(bounds.X + dotSize + 8f, bounds.Y, bounds.Width - dotSize - 8f, bounds.Height)),
            Color.FromArgb(35, 39, 44),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    private static void DrawCountBadge(
        Graphics graphics,
        RectangleF keyBounds,
        int count,
        ShortcutVisualKind kind,
        Font font,
        float scale)
    {
        var size = Math.Max(14f, 17f * scale);
        var bounds = new RectangleF(keyBounds.Right - size - 2f, keyBounds.Top + 2f, size, size);
        using var brush = new SolidBrush(GetAccentColor(kind));
        graphics.FillEllipse(brush, bounds);
        TextRenderer.DrawText(
            graphics,
            count.ToString(),
            font,
            Rectangle.Round(bounds),
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private static Color GetAccentColor(ShortcutVisualKind kind) => kind switch
    {
        ShortcutVisualKind.WindowsDefault => Color.FromArgb(25, 103, 210),
        ShortcutVisualKind.Unavailable => Color.FromArgb(190, 55, 55),
        ShortcutVisualKind.ThisApp => Color.FromArgb(16, 135, 87),
        _ => Color.Gray
    };

    private static Color GetOverlayColor(ShortcutVisualKind kind, bool selected)
    {
        var accent = GetAccentColor(kind);
        var alpha = selected ? 88 : 34;
        return Blend(Color.White, accent, alpha / 255f);
    }

    private static Color Blend(Color background, Color foreground, float amount) => Color.FromArgb(
        (int)(background.R + (foreground.R - background.R) * amount),
        (int)(background.G + (foreground.G - background.G) * amount),
        (int)(background.B + (foreground.B - background.B) * amount));

    private static bool KeyMatches(Keys left, Keys right) =>
        (left & Keys.KeyCode) == (right & Keys.KeyCode);

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
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

    private static IReadOnlyList<KeySpec> BuildKeyboardLayout()
    {
        var keys = new List<KeySpec>();
        void Add(float x, float y, string label, Keys key, float width = 1f, HotKeyModifiers modifier = HotKeyModifiers.None) =>
            keys.Add(new KeySpec(x, y, width, 1f, label, key, modifier));

        Add(0, 0, "Esc", Keys.Escape);
        for (var number = 1; number <= 4; number++) Add(number + 1, 0, $"F{number}", Keys.F1 + number - 1);
        for (var number = 5; number <= 8; number++) Add(number + 1.5f, 0, $"F{number}", Keys.F1 + number - 1);
        for (var number = 9; number <= 12; number++) Add(number + 2f, 0, $"F{number}", Keys.F1 + number - 1);
        Add(16, 0, "PrtSc", Keys.Snapshot);
        Add(17, 0, "ScrLk", Keys.Scroll);
        Add(18, 0, "Pause", Keys.Pause);

        Add(0, 1, "`", Keys.Oemtilde);
        for (var number = 1; number <= 9; number++) Add(number, 1, number.ToString(), Keys.D0 + number);
        Add(10, 1, "0", Keys.D0);
        Add(11, 1, "-", Keys.OemMinus);
        Add(12, 1, "=", Keys.Oemplus);
        Add(13, 1, "Back", Keys.Back, 2f);
        Add(16, 1, "Ins", Keys.Insert);
        Add(17, 1, "Home", Keys.Home);
        Add(18, 1, "PgUp", Keys.Prior);

        Add(0, 2, "Tab", Keys.Tab, 1.5f);
        const string topLetters = "QWERTYUIOP";
        for (var index = 0; index < topLetters.Length; index++) Add(1.5f + index, 2, topLetters[index].ToString(), (Keys)topLetters[index]);
        Add(11.5f, 2, "[", Keys.OemOpenBrackets);
        Add(12.5f, 2, "]", Keys.OemCloseBrackets);
        Add(13.5f, 2, "\\", Keys.OemPipe, 1.5f);
        Add(16, 2, "Del", Keys.Delete);
        Add(17, 2, "End", Keys.End);
        Add(18, 2, "PgDn", Keys.Next);

        Add(0, 3, "Caps", Keys.CapsLock, 1.75f);
        const string homeLetters = "ASDFGHJKL";
        for (var index = 0; index < homeLetters.Length; index++) Add(1.75f + index, 3, homeLetters[index].ToString(), (Keys)homeLetters[index]);
        Add(10.75f, 3, ";", Keys.OemSemicolon);
        Add(11.75f, 3, "'", Keys.OemQuotes);
        Add(12.75f, 3, "Enter", Keys.Return, 2.25f);

        Add(0, 4, "Shift", Keys.LShiftKey, 2.25f, HotKeyModifiers.Shift);
        const string bottomLetters = "ZXCVBNM";
        for (var index = 0; index < bottomLetters.Length; index++) Add(2.25f + index, 4, bottomLetters[index].ToString(), (Keys)bottomLetters[index]);
        Add(9.25f, 4, ",", Keys.Oemcomma);
        Add(10.25f, 4, ".", Keys.OemPeriod);
        Add(11.25f, 4, "/", Keys.OemQuestion);
        Add(12.25f, 4, "Shift", Keys.RShiftKey, 2.75f, HotKeyModifiers.Shift);
        Add(17, 4, "▲", Keys.Up);

        Add(0, 5, "Ctrl", Keys.LControlKey, 1.25f, HotKeyModifiers.Control);
        Add(1.25f, 5, "Win", Keys.LWin, 1.25f, HotKeyModifiers.Win);
        Add(2.5f, 5, "Alt", Keys.LMenu, 1.25f, HotKeyModifiers.Alt);
        Add(3.75f, 5, "Space", Keys.Space, 6.25f);
        Add(10, 5, "Alt", Keys.RMenu, 1.25f, HotKeyModifiers.Alt);
        Add(11.25f, 5, "Win", Keys.RWin, 1.25f, HotKeyModifiers.Win);
        Add(12.5f, 5, "Menu", Keys.Apps, 1.25f);
        Add(13.75f, 5, "Ctrl", Keys.RControlKey, 1.25f, HotKeyModifiers.Control);
        Add(16, 5, "◀", Keys.Left);
        Add(17, 5, "▼", Keys.Down);
        Add(18, 5, "▶", Keys.Right);
        return keys;
    }

    private sealed record KeySpec(
        float X,
        float Y,
        float Width,
        float Height,
        string Label,
        Keys Key,
        HotKeyModifiers Modifier);
}
