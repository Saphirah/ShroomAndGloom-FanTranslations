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
