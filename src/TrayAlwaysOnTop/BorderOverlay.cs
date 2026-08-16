using System.Drawing.Drawing2D;

namespace TrayAlwaysOnTop;

internal sealed class BorderOverlay : IDisposable
{
    private const int Thickness = 4;
    private const int PinSize = 24;
    private readonly nint _target;
    private readonly OverlayStrip[] _strips;
    private readonly PinToggleOverlay _pinToggle;
    private bool _showBorder;
    private bool _showPinToggle;

    public BorderOverlay(nint target, bool showBorder, bool showPinToggle, Action toggleRequested)
    {
        _target = target;
        _strips = [new(), new(), new(), new()];
        _pinToggle = new PinToggleOverlay(toggleRequested);
        _showBorder = showBorder;
        _showPinToggle = showPinToggle;
        Synchronize();
    }

    public void SetOptions(bool showBorder, bool showPinToggle)
    {
        _showBorder = showBorder;
        _showPinToggle = showPinToggle;
        Synchronize();
    }

    public void Synchronize()
    {
        if (!NativeMethods.IsWindow(_target) || NativeMethods.IsIconic(_target))
        {
            SetVisible(false);
            return;
        }

        var result = NativeMethods.DwmGetWindowAttribute(
            _target,
            NativeMethods.DwmwaExtendedFrameBounds,
            out NativeRect bounds,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeRect>());

        if (result != 0 && !NativeMethods.GetWindowRect(_target, out bounds))
        {
            SetVisible(false);
            return;
        }

        var rectangle = bounds.ToRectangle();
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            SetVisible(false);
            return;
        }

        _strips[0].SetBounds(rectangle.Left, rectangle.Top, rectangle.Width, Thickness);
        _strips[1].SetBounds(rectangle.Left, rectangle.Bottom - Thickness, rectangle.Width, Thickness);
        _strips[2].SetBounds(rectangle.Left, rectangle.Top + Thickness, Thickness, Math.Max(1, rectangle.Height - (Thickness * 2)));
        _strips[3].SetBounds(rectangle.Right - Thickness, rectangle.Top + Thickness, Thickness, Math.Max(1, rectangle.Height - (Thickness * 2)));

        var pinBounds = GetPinBounds(rectangle);
        _pinToggle.SetBounds(pinBounds.X, pinBounds.Y, pinBounds.Width, pinBounds.Height);
        SetVisible(true);
    }

    private Rectangle GetPinBounds(Rectangle windowBounds)
    {
        var captionResult = NativeMethods.DwmGetWindowAttribute(
            _target,
            NativeMethods.DwmwaCaptionButtonBounds,
            out NativeRect captionButtons,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeRect>());

        if (captionResult == 0
            && captionButtons.Right > captionButtons.Left
            && captionButtons.Bottom > captionButtons.Top)
        {
            var captionHeight = captionButtons.Bottom - captionButtons.Top;
            var x = windowBounds.Left + captionButtons.Left - PinSize - 8;
            var y = windowBounds.Top + captionButtons.Top + Math.Max(0, (captionHeight - PinSize) / 2);
            return new Rectangle(
                Math.Clamp(x, windowBounds.Left + 6, windowBounds.Right - PinSize - 6),
                Math.Clamp(y, windowBounds.Top + 2, windowBounds.Bottom - PinSize - 2),
                PinSize,
                PinSize);
        }

        // Custom title bars often do not expose caption button bounds. Keep a
        // conservative gap from the right edge and center on a standard caption.
        var fallbackX = windowBounds.Width >= 210
            ? windowBounds.Right - 166
            : windowBounds.Left + Math.Max(0, (windowBounds.Width - PinSize) / 2);
        return new Rectangle(fallbackX, windowBounds.Top + 4, PinSize, PinSize);
    }

    public void Dispose()
    {
        foreach (var strip in _strips)
        {
            strip.Dispose();
        }

        _pinToggle.Dispose();
    }

    private void SetVisible(bool visible)
    {
        foreach (var strip in _strips)
        {
            if (visible && _showBorder)
            {
                if (!strip.Visible)
                {
                    strip.Show();
                }
            }
            else
            {
                strip.Hide();
            }
        }

        if (visible && _showPinToggle)
        {
            if (!_pinToggle.Visible)
            {
                _pinToggle.Show();
            }
        }
        else
        {
            _pinToggle.Hide();
        }
    }

    private sealed class OverlayStrip : Form
    {
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;

        public OverlayStrip()
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(0, 151, 215);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
                return parameters;
            }
        }
    }

    private sealed class PinToggleOverlay : Form
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private readonly Action _toggleRequested;
        private readonly ToolTip _toolTip = new();
        private bool _hovered;

        public PinToggleOverlay(Action toggleRequested)
        {
            _toggleRequested = toggleRequested;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(0, 120, 212);
            Cursor = Cursors.Hand;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;

            Click += (_, _) => _toggleRequested();
            MouseEnter += (_, _) =>
            {
                _hovered = true;
                Invalidate();
            };
            MouseLeave += (_, _) =>
            {
                _hovered = false;
                Invalidate();
            };
            _toolTip.SetToolTip(this, "클릭하여 항상 위 고정 해제");
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
                return parameters;
            }
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            var graphics = eventArgs.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(_hovered ? Color.FromArgb(0, 90, 170) : Color.FromArgb(0, 120, 212));

            using var borderPen = new Pen(Color.White, 1.5f);
            graphics.DrawRectangle(borderPen, 1, 1, Width - 3, Height - 3);

            using var pinPen = new Pen(Color.White, 2.4f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(pinPen, 7, 7, 17, 7);
            graphics.DrawLine(pinPen, 9, 7, 9, 12);
            graphics.DrawLine(pinPen, 15, 7, 15, 12);
            graphics.DrawLine(pinPen, 7, 13, 17, 13);
            graphics.DrawLine(pinPen, 12, 13, 12, 20);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _toolTip.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
