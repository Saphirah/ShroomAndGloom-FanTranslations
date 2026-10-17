using System.Drawing.Drawing2D;

namespace ShroomGerman.Setup;

internal sealed class SetupForm : Form
{
    private static readonly Color Ink = Color.FromArgb(235, 240, 229);
    private static readonly Color Muted = Color.FromArgb(146, 158, 147);
    private readonly RoundedButton location = new() { Text = "Spiel wird gesucht …", TextAlign = ContentAlignment.MiddleLeft, TabIndex = 0 };
    private readonly RoundedButton browse = new() { Text = "Ordner wählen", TabIndex = 1 };
    private readonly RoundedButton install = new() { Text = "Installieren", Primary = true, Enabled = false, TabIndex = 2 };
    private readonly RoundedButton remove = new() { Text = "Deinstallieren", Enabled = false, TabIndex = 3 };
    private readonly RoundedButton close = new() { Text = "×", Quiet = true, AccessibleName = "Schließen", TabIndex = 5 };
    private readonly RoundedButton minimize = new() { Text = "−", Quiet = true, AccessibleName = "Minimieren", TabIndex = 4 };
    private readonly ToolTip hint = new();
    private readonly List<string> paths = new();
    private string? selectedPath;
    private string status = "Suche Steam-Installation …";
    private bool error;
    private bool busy;
    private int progress;

    public SetupForm()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        Text = "Shroom and Gloom · Deutsch";
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        ClientSize = new Size(720, 390);
        MinimumSize = new Size(720, 390);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(19, 25, 22);
        ForeColor = Ink;
        DoubleBuffered = true;
        Controls.AddRange(new Control[] { location, browse, install, remove, close, minimize });
        close.Font = new Font("Segoe UI", 19);
        minimize.Font = new Font("Segoe UI", 16);
        close.Click += (_, _) => Close();
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        install.Click += async (_, _) => await Run(false);
        remove.Click += async (_, _) => await Run(true);
        browse.Click += (_, _) => Browse();
        location.Click += (_, _) => {
            if (paths.Count < 2) return;
            var menu = new ContextMenuStrip { BackColor = BackColor, ForeColor = ForeColor };
            foreach (var path in paths) {
                var item = menu.Items.Add(path);
                item.Click += (_, _) => SelectPath(path);
            }
            menu.Closed += (_, _) => menu.Dispose();
            menu.Show(location, new Point(0, location.Height));
        };
        Shown += async (_, _) => {
            try {
                paths.AddRange(await Task.Run(GameLocator.Find));
                if (paths.Count > 0) SelectPath(paths[0]);
                else SetStatus("Spielordner auswählen.");
            } catch (Exception e) { SetStatus("Suche fehlgeschlagen: " + e.Message, true); }
        };
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        UpdateButtons();
    }

    private int S(float value) => (int)Math.Round(value * DeviceDpi / 96f);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (close == null) return; // Form construction can trigger layout before field initialization.
        var pad = S(32);
        close.SetBounds(ClientSize.Width - S(52), S(12), S(36), S(32));
        minimize.SetBounds(ClientSize.Width - S(92), S(12), S(36), S(32));
        browse.SetBounds(ClientSize.Width - pad - S(134), S(184), S(134), S(48));
        location.SetBounds(pad, S(184), browse.Left - pad - S(12), S(48));
        install.SetBounds(ClientSize.Width - pad - S(156), ClientSize.Height - pad - S(46), S(156), S(46));
        remove.SetBounds(install.Left - S(12) - S(152), install.Top, S(152), S(46));
        using var shape = RoundedButton.Shape(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), S(20));
        var old = Region;
        Region = new Region(shape);
        old?.Dispose();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new LinearGradientBrush(ClientRectangle, Color.FromArgb(28, 39, 31), BackColor, 55f);
        g.FillRectangle(fill, ClientRectangle);
        using var title = new Font("Segoe UI", 25, FontStyle.Bold);
        using var caption = new Font("Segoe UI", 10);
        using var label = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        TextRenderer.DrawText(g, "Shroom and Gloom", title, new Rectangle(S(29), S(58), Width - S(60), S(46)), Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "Deutsch · v0.1.1", caption, new Rectangle(S(32), S(109), Width - S(64), S(24)), Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "SPIELORDNER", label, new Rectangle(S(32), S(155), Width - S(64), S(22)), Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, status, caption, new Rectangle(S(32), S(247), Width - S(64), Math.Max(S(36), install.Top - S(257))),
            error ? Color.FromArgb(244, 167, 148) : Muted, TextFormatFlags.Left | TextFormatFlags.WordBreak);
        using var line = new Pen(Color.FromArgb(44, 57, 47), S(1));
        g.DrawLine(line, S(32), install.Top - S(20), Width - S(32), install.Top - S(20));
        if (busy) {
            using var track = new SolidBrush(Color.FromArgb(44, 57, 47));
            using var accent = new SolidBrush(RoundedButton.Accent);
            g.FillRectangle(track, S(32), install.Top - S(20), Width - S(64), S(2));
            g.FillRectangle(accent, S(32), install.Top - S(20), (Width - S(64)) * progress / 100, S(2));
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x84 && m.Result == (IntPtr)1) { // Drag the unused header like a native title bar.
            var point = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            if (point.Y < S(145)) m.Result = (IntPtr)2;
        }
    }

    private void SelectPath(string path)
    {
        selectedPath = path;
        location.Text = path;
        hint.SetToolTip(location, path);
        UpdateButtons();
        SetStatus(File.Exists(Path.Combine(path, ModInstaller.RecordName)) ? "Mod installiert." : "Bereit zur Installation.");
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Shroom and Gloom · Spielordner", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!GameLocator.IsGame(dialog.SelectedPath)) { SetStatus("Spiel in diesem Ordner nicht gefunden.", true); return; }
        if (!paths.Contains(dialog.SelectedPath)) paths.Add(dialog.SelectedPath);
        SelectPath(dialog.SelectedPath);
    }

    private void UpdateButtons()
    {
        install.Enabled = !busy && selectedPath != null;
        browse.Enabled = !busy;
        location.Enabled = !busy && paths.Count > 1;
        remove.Enabled = install.Enabled && File.Exists(Path.Combine(selectedPath!, ModInstaller.RecordName));
        close.Enabled = !busy;
    }

    private void SetStatus(string text, bool failed = false) { status = text; error = failed; Invalidate(); }

    private async Task Run(bool uninstall)
    {
        if (selectedPath == null || busy) return;
        var path = selectedPath;
        busy = true; progress = 0; UpdateButtons();
        SetStatus(uninstall ? "Wird entfernt …" : "Wird installiert …");
        try {
            if (uninstall) SetStatus(await Task.Run(() => ModInstaller.Uninstall(path)));
            else {
                var updates = new Progress<int>(value => { progress = value; Invalidate(); });
                await Task.Run(() => ModInstaller.Install(path, updates));
                SetStatus("Installiert. Deutsch ist beim nächsten Spielstart aktiv.");
            }
        }
        catch (UnauthorizedAccessException) { SetStatus("Kein Schreibzugriff. Installer als Administrator öffnen.", true); }
        catch (Exception e) { SetStatus(e.Message, true); }
        finally { busy = false; UpdateButtons(); Invalidate(); }
    }

    protected override void Dispose(bool disposing) { if (disposing) hint.Dispose(); base.Dispose(disposing); }
}

internal sealed class RoundedButton : Control
{
    internal static readonly Color Accent = Color.FromArgb(188, 230, 145);
    public bool Primary { get; set; }
    public bool Quiet { get; set; }
    public ContentAlignment TextAlign { get; set; } = ContentAlignment.MiddleCenter;
    private bool hover;

    public RoundedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        AccessibleRole = AccessibleRole.PushButton;
        TabStop = true;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 10, FontStyle.Regular);
    }

    internal static GraphicsPath Shape(RectangleF rect, float radius)
    {
        var p = new GraphicsPath(); var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        p.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        p.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        p.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new ButtonAccessibility(this);
    private sealed class ButtonAccessibility(RoundedButton button) : ControlAccessibleObject(button)
    {
        public override string DefaultAction => "Drücken";
        public override void DoDefaultAction() { if (button.Enabled) button.OnClick(EventArgs.Empty); }
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Parent == null) { base.OnPaintBackground(e); return; }
        // Match the form beneath the rounded corners; never let the native button
        // renderer introduce square borders or black transparent-background patches.
        using var fill = new LinearGradientBrush(new Rectangle(-Left, -Top, Parent.Width, Parent.Height),
            Color.FromArgb(28, 39, 31), Parent.BackColor, 55f);
        e.Graphics.FillRectangle(fill, ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var background = Primary ? (Enabled ? (hover ? Color.FromArgb(207, 242, 173) : Accent) : Color.FromArgb(51, 65, 46))
            : (hover && Enabled ? Color.FromArgb(49, 63, 52) : Color.FromArgb(29, 39, 32));
        using var shape = Shape(new RectangleF(.5f, .5f, Width - 1, Height - 1), 10 * DeviceDpi / 96f);
        if (!Quiet || hover) { using var brush = new SolidBrush(background); g.FillPath(brush, shape); }
        if (!Quiet && !Primary) { using var pen = new Pen(Color.FromArgb(59, 73, 61)); g.DrawPath(pen, shape); }
        if (Quiet) {
            var cx = Width / 2f; var cy = Height / 2f; var half = 4.5f * DeviceDpi / 96f;
            using var glyph = new Pen(Enabled ? Color.FromArgb(181, 195, 181) : Color.FromArgb(104, 120, 106), 1.4f * DeviceDpi / 96f);
            if (Text == "×") {
                g.DrawLine(glyph, cx - half, cy - half, cx + half, cy + half);
                g.DrawLine(glyph, cx - half, cy + half, cx + half, cy - half);
            } else g.DrawLine(glyph, cx - half, cy, cx + half, cy);
            return;
        }
        var textColor = Primary && Enabled ? Color.FromArgb(25, 38, 19) : Enabled || TextAlign == ContentAlignment.MiddleLeft
            ? Color.FromArgb(227, 234, 222) : Color.FromArgb(104, 120, 106);
        var bounds = Rectangle.Inflate(ClientRectangle, -(int)(14 * DeviceDpi / 96f), 0);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
            | (TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, Font, bounds, textColor, flags);
        if (Focused && ShowFocusCues) { using var pen = new Pen(Accent); g.DrawPath(pen, shape); }
    }
}
