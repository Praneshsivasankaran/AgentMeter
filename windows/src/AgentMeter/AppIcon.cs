namespace AgentMeter;

internal static class AppIcon
{
    public static Icon Load(int size = 32)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("AgentMeter.Assets.AgentMeter.ico")
            ?? throw new InvalidOperationException("Application icon resource is missing.");
        using var original = new Icon(stream, new Size(size, size));
        return (Icon)original.Clone();
    }
}
