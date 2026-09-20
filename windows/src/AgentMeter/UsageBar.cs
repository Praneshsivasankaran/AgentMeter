using System.Drawing.Drawing2D;

namespace AgentMeter;

internal sealed class UsageBar : Control
{
    private double? remaining;
    private bool stale;
    private Color accent = Palette.Codex;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Accent { get => accent; set { if (accent != value) { accent = value; Invalidate(); } } }

    public UsageBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent; TabStop = false;
        AccessibleRole = AccessibleRole.ProgressBar;
        AccessibleName = "Remaining allowance unavailable";
    }

    public void UpdateValue(double? value, bool isStale)
    {
        value = value is >= 0 and <= 100 && double.IsFinite(value.Value) ? value : null;
        if (remaining == value && stale == isStale) return;
        remaining = value; stale = isStale;
        AccessibleName = value is { } v ? $"{v:0.#} percent remaining{(isStale ? ", stale" : "")}" : "Remaining allowance unavailable";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 1 || Height < 1) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var trackShape = DrawingHelpers.RoundedRectangle(new(0, 0, Width, Height), Height / 2f);
        using var track = new SolidBrush(Palette.Track);
        e.Graphics.FillPath(track, trackShape);
        if (remaining is not > 0) return;
        var valueWidth = (float)(Width * remaining.Value / 100);
        using var shape = DrawingHelpers.RoundedRectangle(new(0, 0, valueWidth, Height), Height / 2f);
        var color = stale ? Color.FromArgb(155, accent) : accent;
        using var fill = new SolidBrush(color);
        e.Graphics.FillPath(fill, shape);
    }
}
