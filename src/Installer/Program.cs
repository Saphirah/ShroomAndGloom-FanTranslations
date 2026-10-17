using System.Text.Json;

namespace ShroomGerman.Setup;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            object report;
            var exit = 0;
            try
            {
                report = args[0] switch {
                    "--detect" => new { Paths = GameLocator.Find() } as object,
                    "--install" when args.Length >= 2 => Install(args[1]),
                    "--uninstall" when args.Length >= 2 => new { Message = ModInstaller.Uninstall(args[1]) },
                    "--verify" when args.Length >= 2 => ModInstaller.Verify(args[1]),
                    _ => throw new ArgumentException("Unbekannte Installer-Option.")
                };
            }
            catch (Exception e) { report = new { Error = e.Message }; exit = 1; }
            var reportIndex = Array.IndexOf(args, "--report");
            if (reportIndex >= 0 && args.Length > reportIndex + 1)
                File.WriteAllText(args[reportIndex + 1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return exit;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
        return 0;
    }
    private static object Install(string root) { ModInstaller.Install(root); return new { Installed = true, GamePath = root }; }
}

internal sealed class SetupForm : Form
{
    private readonly ComboBox location = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Fill, Text = "Suche Steam-Installation …" };
    private readonly Button install = new() { Text = "Installieren", AutoSize = true, Enabled = false };
    private readonly Button remove = new() { Text = "Deinstallieren", AutoSize = true, Enabled = false };
    private readonly Button browse = new() { Text = "Ordner wählen …", AutoSize = true };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Visible = false };
    private bool busy;

    public SetupForm()
    {
        Text = "Shroom and Gloom · Deutsch";
        ClientSize = new Size(660, 340); MinimumSize = new Size(600, 380);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(24, 28, 25); ForeColor = Color.FromArgb(234, 235, 219);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(new Label { Text = "Shroom and Gloom · Deutsch", AutoSize = true, Font = new Font("Segoe UI", 20, FontStyle.Bold) });
        layout.Controls.Add(new Label { Text = "Deutsche Übersetzungsmod · Version 0.1.0\nDas Spiel wird nicht gestartet. Spielstände bleiben erhalten.", AutoSize = true });
        var picker = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); picker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        picker.Controls.Add(location); picker.Controls.Add(browse);
        layout.Controls.Add(picker); layout.Controls.Add(status); layout.Controls.Add(progress);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(install); buttons.Controls.Add(remove); layout.Controls.Add(buttons); Controls.Add(layout);
        foreach (var button in new[] { install, remove, browse })
        { button.FlatStyle = FlatStyle.Flat; button.BackColor = Color.FromArgb(57, 75, 53); button.ForeColor = ForeColor; button.Padding = new Padding(8, 2, 8, 2); }
        install.Click += async (_, _) => await Run(false);
        remove.Click += async (_, _) => await Run(true);
        location.SelectedIndexChanged += (_, _) => UpdateButtons();
        browse.Click += (_, _) => {
            using var dialog = new FolderBrowserDialog { Description = "Spielordner mit Shroom and Gloom.exe wählen", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (!GameLocator.IsGame(dialog.SelectedPath)) { status.Text = "In diesem Ordner wurde das Spiel nicht gefunden."; return; }
            if (!location.Items.Contains(dialog.SelectedPath)) location.Items.Add(dialog.SelectedPath);
            location.SelectedItem = dialog.SelectedPath;
        };
        Shown += async (_, _) => {
            try {
                var paths = await Task.Run(GameLocator.Find);
                location.Items.AddRange(paths.Cast<object>().ToArray());
                if (paths.Count > 0) location.SelectedIndex = 0;
                else status.Text = "Spiel nicht gefunden. Bitte den Spielordner auswählen.";
            } catch (Exception e) { status.Text = "Automatische Suche fehlgeschlagen: " + e.Message; }
        };
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
    }
    private void UpdateButtons()
    {
        install.Enabled = !busy && location.SelectedItem != null;
        browse.Enabled = location.Enabled = !busy;
        remove.Enabled = install.Enabled && File.Exists(Path.Combine((string)location.SelectedItem!, ModInstaller.RecordName));
        if (!busy && location.SelectedItem != null) status.Text = "Spiel gefunden. Bereit zur Installation.";
    }
    private async Task Run(bool uninstall)
    {
        var path = (string)location.SelectedItem!;
        busy = true; UpdateButtons(); progress.Visible = true;
        status.Text = uninstall ? "Mod wird entfernt …" : "Mod wird installiert …";
        try
        {
            string message;
            if (uninstall) message = await Task.Run(() => ModInstaller.Uninstall(path));
            else {
                var updates = new Progress<int>(value => progress.Value = value);
                await Task.Run(() => ModInstaller.Install(path, updates));
                message = "Installiert. Beim nächsten regulären Spielstart ist Deutsch aktiv.\nDie erste Vorbereitung des Modloaders kann etwa eine Minute dauern.";
            }
            busy = false; UpdateButtons(); status.Text = message;
        }
        catch (UnauthorizedAccessException) { busy = false; UpdateButtons(); status.Text = "Kein Schreibzugriff. Bitte den Installer als Administrator starten."; }
        catch (Exception e) { busy = false; UpdateButtons(); status.Text = e.Message; }
        finally { progress.Visible = false; }
    }
}
