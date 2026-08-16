using System.Drawing.Drawing2D;

namespace TrayAlwaysOnTop;

internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var background = new SolidBrush(Color.FromArgb(0, 120, 212));
            graphics.FillEllipse(background, 1, 1, 30, 30);

            using var pen = new Pen(Color.White, 3)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(pen, 10, 11, 22, 11);
            graphics.DrawLine(pen, 12, 11, 12, 17);
            graphics.DrawLine(pen, 20, 11, 20, 17);
            graphics.DrawLine(pen, 10, 18, 22, 18);
            graphics.DrawLine(pen, 16, 18, 16, 26);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
