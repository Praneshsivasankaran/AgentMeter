namespace AgentMeter;

internal static class AppIcon
{
    public static Icon Load(int size = 32)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("AgentMeter.Assets.Llumi.ico")
            ?? throw new InvalidOperationException("Application icon resource is missing.");
        using var original = new Icon(stream, new Size(size, size));
        return (Icon)original.Clone();
    }
    // Independent small system glyph: no rounded-square application tile in the tray.
    public static Icon LoadTray(int size)
    {
        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.ScaleTransform(size / 24f, size / 24f);
        using var outline = new Pen(Color.FromArgb(160, 0, 0, 0), 4.2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        using var pen = new Pen(Color.White, 2.4f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        graphics.DrawArc(outline, 3, 5, 18, 18, 180, 180);
        graphics.DrawArc(pen, 3, 5, 18, 18, 180, 180);
        graphics.DrawLine(outline, 12, 16, 18, 8);
        graphics.DrawLine(pen, 12, 16, 18, 8);
        graphics.FillEllipse(Brushes.White, 10, 14, 4, 4);
        var handle = bitmap.GetHicon();
        try { using var icon = Icon.FromHandle(handle); return (Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

}
