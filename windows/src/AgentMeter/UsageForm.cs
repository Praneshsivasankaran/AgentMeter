using System.ComponentModel;
using AgentMeter.Core;

namespace AgentMeter;

internal sealed class UsageForm : Form
{
    private readonly Panel header = new();
    private readonly Label title = new() { Text = "AgentMeter", AutoSize = false };
    private readonly Label footer = new() { AutoEllipsis = true };
    private readonly Button usageTab = Palette.Button("Usage", "Usage");
    private readonly Button settingsTab = Palette.Button("Settings", "Settings");
    private readonly GlyphButton refresh = new(Glyph.Refresh, "Refresh usage", "Refresh");
    private readonly GlyphButton menu = new(Glyph.Menu, "AgentMeter menu");
    private readonly Panel content = new() { AutoScroll = true };
    private readonly Panel settings = new() { Name = "settings", AutoScroll = true };
    private readonly CheckBox launch = new() { Text = "Launch at Startup", AutoSize = true };
    private readonly CheckBox compact = new() { Text = "Compact Monitor", AutoSize = true };
    private readonly CheckBox tray = new() { Text = "Tray Icon", AutoSize = true };
    private readonly ComboBox appearance = new() { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Appearance" };
    private readonly Label settingsMessage = new() { AutoSize = false };
    private readonly Label appearanceLabel = new() { Text = "Appearance", AutoSize = true };
    private readonly ContextMenuStrip actions = new();
    private readonly ToolTip hints = new();
    private readonly Dictionary<string, ProviderCard> cards = new();
    private readonly Font headingFont = new("Segoe UI", 13, FontStyle.Bold);
    private readonly Font bodyFont = new("Segoe UI", 10);
    private IReadOnlyList<ProviderState> lastStates = [];
    private bool loading, logFailed, rendering, syncing, settingsShown;
    private Preferences preferences = new();
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool AllowExit { get; set; }
    internal event Action? RefreshRequested;
    internal event Action? ExitRequested;
    internal event Action? PinRequested;
    internal event Action? StartupToggleRequested;
    internal event Action? MenuOpening;
    internal event Action<Preferences>? PreferencesChanged;

    public UsageForm(IEnumerable<string> names, Icon icon)
    {
        Text = "AgentMeter"; Icon = icon; Font = bodyFont;
        FormBorderStyle = FormBorderStyle.Sizable; ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        title.Font = headingFont; title.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.AddRange([title, usageTab, settingsTab, refresh, menu]);
        header.Paint += (_, e) => GlyphDrawing.DrawIdentity(e.Graphics, new Rectangle(S(18), S(15), S(18), S(22)));
        Controls.AddRange([header, content, settings, footer]);
        foreach (var name in names) { var card = new ProviderCard(hints); cards[name] = card; content.Controls.Add(card); }
        appearance.Items.AddRange(["System", "Light", "Dark"]);
        settings.Controls.AddRange([launch, compact, tray, appearanceLabel, appearance, settingsMessage]);
        launch.Location = new(S(20), S(24)); compact.Location = new(S(20), S(68)); tray.Location = new(S(20), S(112));
        appearanceLabel.Location = new(S(20), S(164)); appearance.SetBounds(S(20), S(194), S(200), S(30));
        settingsMessage.SetBounds(S(20), S(244), S(410), S(100));
        settingsMessage.Text = "AgentMeter stays available from the taskbar when the tray icon is hidden.\n\nIndependent of OpenAI and Anthropic. No AgentMeter account or telemetry.";
        usageTab.Click += (_, _) => ShowUsage(); settingsTab.Click += (_, _) => ShowSettings();
        refresh.Click += (_, _) => RefreshRequested?.Invoke();
        actions.Items.Add("Open AgentMeter", null, (_, _) => ShowUsage());
        actions.Items.Add("Refresh", null, (_, _) => RefreshRequested?.Invoke());
        actions.Items.Add("Settings", null, (_, _) => ShowSettings());
        actions.Items.Add("Quit", null, (_, _) => ExitRequested?.Invoke());
        menu.Click += (_, _) => { MenuOpening?.Invoke(); actions.Show(menu, new Point(0, menu.Height)); };
        launch.Click += (_, _) => { if (!syncing) StartupToggleRequested?.Invoke(); };
        compact.CheckedChanged += (_, _) => SavePreferences(); tray.CheckedChanged += (_, _) => SavePreferences();
        appearance.SelectedIndexChanged += (_, _) => SavePreferences();
        FormClosing += (_, e) => { if (!AllowExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HidePanel(); } };
        Resize += (_, _) => { if (!rendering) Render(lastStates, loading, logFailed); };
        DpiChanged += (_, _) => { FitInitialWindow(Screen.FromControl(this).WorkingArea); Render(lastStates, loading, logFailed); KeepOnScreen(); };
        _ = Handle;
        FitInitialWindow(Screen.FromControl(this).WorkingArea);
        SetPreferences(preferences);
    }
    private int S(int n) => (int)Math.Round(n * DeviceDpi / 96f);
    private void FitInitialWindow(Rectangle work)
    {
        // WinForms autoscaling is disabled because this view lays out in device pixels.
        // Set dimensions only after the handle establishes the monitor's actual DPI.
        var available = new Size(Math.Max(1, work.Width - S(24)), Math.Max(1, work.Height - S(24)));
        MinimumSize = new(Math.Min(S(380), available.Width), Math.Min(S(320), available.Height));
        var desired = SizeFromClientSize(new Size(S(640), S(440)));
        Size = new(Math.Min(desired.Width, available.Width), Math.Min(desired.Height, available.Height));
    }
    private void SavePreferences()
    {
        if (syncing || appearance.SelectedIndex < 0) return;
        preferences = new(compact.Checked, tray.Checked, (Appearance)appearance.SelectedIndex);
        PreferencesChanged?.Invoke(preferences);
    }
    internal void SetPreferences(Preferences value)
    {
        preferences = value; syncing = true;
        compact.Checked = value.CompactMonitor; tray.Checked = value.TrayIcon; appearance.SelectedIndex = (int)value.Appearance;
        syncing = false; ApplyTheme();
    }
    internal void PreferenceSaveFailed() => settingsMessage.Text = "Settings could not be saved. The previous preferences remain active.";
    internal void ApplyTheme()
    {
        void Theme(Control root)
        {
            root.BackColor = Palette.Background; root.ForeColor = Palette.Foreground;
            foreach (Control child in root.Controls) Theme(child);
        }
        Theme(this); footer.ForeColor = Palette.Muted; settingsMessage.ForeColor = Palette.Muted;
        foreach (var button in new[] { usageTab, settingsTab }) Palette.StyleButton(button);
        Render(lastStates, loading, logFailed); Invalidate(true);
    }
    internal void SetStartupState(bool enabled, bool available) { syncing = true; launch.Checked = enabled; launch.Enabled = available; syncing = false; }
    internal void ShowUsage() { settingsShown = false; Render(lastStates, loading, logFailed); }
    internal void ShowSettings() { settingsShown = true; MenuOpening?.Invoke(); Render(lastStates, loading, logFailed); }
    internal void ShowPanel(Point anchor)
    {
        if (!Visible) Location = PopupPlacement.NearTray(Screen.FromPoint(anchor).Bounds, Screen.FromPoint(anchor).WorkingArea, Size, S(16));
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show(); Activate(); KeepOnScreen();
    }
    internal void HidePanel() { actions.Close(); if (preferences.TrayIcon) Hide(); else WindowState = FormWindowState.Minimized; }
    internal void KeepOnScreen() => Location = PopupPlacement.Clamp(Location, Size, Screen.FromControl(this).WorkingArea);
    internal void Render(IReadOnlyList<ProviderState> states, bool loading, bool logFailed, Rectangle? availableWorkArea = null)
    {
        lastStates = states; this.loading = loading; this.logFailed = logFailed;
        if (rendering) return;
        rendering = true;
        try
        {
            if (availableWorkArea is { } work) FitInitialWindow(work);
            var width = ClientSize.Width; var height = ClientSize.Height;
            header.SetBounds(0, 0, width, S(103));
            title.SetBounds(S(46), S(7), width - S(90), S(38));
            usageTab.SetBounds(S(16), S(56), S(95), S(32)); settingsTab.SetBounds(S(120), S(56), S(95), S(32));
            menu.SetBounds(width - S(46), S(12), S(28), S(28)); refresh.SetBounds(width - S(136), S(57), S(120), S(30));
            content.SetBounds(S(12), S(105), width - S(24), Math.Max(S(60), height - S(150)));
            settings.Bounds = content.Bounds; settings.Visible = settingsShown; content.Visible = !settingsShown;
            launch.Location = new(S(20), S(16)); compact.Location = new(S(20), S(56)); tray.Location = new(S(20), S(96));
            appearanceLabel.Location = new(S(20), S(144)); appearance.SetBounds(S(20), S(171), S(200), S(30));
            settingsMessage.SetBounds(S(20), S(219), Math.Max(S(100), settings.Width - S(40)), S(64));
            settingsMessage.Width = Math.Max(S(100), settings.Width - S(40));
            refresh.Visible = !settingsShown; refresh.Text = loading ? "Refreshing…" : "Refresh";
            footer.SetBounds(S(24), height - S(39), width - S(48), S(30));
            var observed = states.Where(s => s.Snapshot is not null).Select(s => s.Snapshot!.ObservedAt).DefaultIfEmpty().Min();
            footer.Text = logFailed ? "Diagnostic log unavailable" : observed == default ? "Connect your tools to see remaining allowance." : $"Last updated {observed.ToLocalTime():HH:mm} · Refreshes automatically";
            if (settingsShown && !logFailed) footer.Text = "Preferences are saved automatically.";
            var scroll = content.AutoScrollPosition;
            content.AutoScrollPosition = Point.Empty;
            var y = 0;
            var columns = content.Width >= S(560) && states.Count > 1 ? 2 : 1;
            var cardWidth = (content.Width - SystemInformation.VerticalScrollBarWidth - S(16) * (columns - 1)) / columns;
            var index = 0; var rowHeight = 0;
            foreach (var (name, card) in cards) card.Visible = states.Any(s => s.Name == name);
            foreach (var state in states)
            {
                if (!cards.TryGetValue(state.Name, out var card)) { card = new ProviderCard(hints); cards[state.Name] = card; content.Controls.Add(card); }
                card.Width = cardWidth;
                card.Render(state, DateTimeOffset.UtcNow, DeviceDpi / 96f);
                card.SetBounds(index % columns * (cardWidth + S(16)), y, card.Width, S(card.LogicalHeight));
                rowHeight = Math.Max(rowHeight, card.Height);
                if (++index % columns == 0) { y += rowHeight + S(16); rowHeight = 0; }
            }
            if (rowHeight > 0) y += rowHeight + S(16);
            content.AutoScrollMinSize = new(0, y); content.AutoScrollPosition = new(0, -scroll.Y);
        }
        finally { rendering = false; }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { HidePanel(); return true; }
        if (keyData == (Keys.Control | Keys.R)) { RefreshRequested?.Invoke(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    // Kept as a callable control seam for the existing monitor ownership tests.
    internal void RequestMonitor() => PinRequested?.Invoke();
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { actions.Dispose(); hints.Dispose(); headingFont.Dispose(); bodyFont.Dispose(); }
    }
}
