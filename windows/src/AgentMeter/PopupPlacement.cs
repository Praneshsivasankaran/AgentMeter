namespace AgentMeter;

internal static class PopupPlacement
{
    public static Point NearTray(Rectangle screen, Rectangle work, Size size, int gap)
    {
        var x = work.Left > screen.Left ? work.Left + gap : work.Right - size.Width - gap;
        var y = work.Top > screen.Top ? work.Top + gap : work.Bottom - size.Height - gap;
        return Clamp(new Point(x, y), size, work);
    }

    public static Point Clamp(Point point, Size size, Rectangle work) => new(
        Math.Clamp(point.X, work.Left, Math.Max(work.Left, work.Right - size.Width)),
        Math.Clamp(point.Y, work.Top, Math.Max(work.Top, work.Bottom - size.Height)));
}
