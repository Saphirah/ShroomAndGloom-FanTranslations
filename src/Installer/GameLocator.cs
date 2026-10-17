using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace ShroomGerman.Setup;

internal static class GameLocator
{
    public static bool IsGame(string root) => File.Exists(Path.Combine(root, "Shroom and Gloom.exe"))
        && File.Exists(Path.Combine(root, "GameAssembly.dll"))
        && Directory.Exists(Path.Combine(root, "Shroom and Gloom_Data"));

    public static List<string> Find()
    {
        var steam = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path) { if (!string.IsNullOrWhiteSpace(path)) steam.Add(path); }
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (var keyName in new[] { @"Software\Valve\Steam", @"Software\WOW6432Node\Valve\Steam" })
        {
            using var key = hive.OpenSubKey(keyName);
            Add(key?.GetValue("SteamPath") as string); Add(key?.GetValue("InstallPath") as string);
        }
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            foreach (var relative in new[] { "Steam", "SteamLibrary", @"Games\Steam", @"Games\SteamLibrary",
                         @"Program Files (x86)\Steam", @"Program Files\Steam", "" })
                Add(Path.Combine(drive.RootDirectory.FullName, relative));
        }
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        foreach (var root in steam.ToList())
        {
            var vdf = Path.Combine(root, @"steamapps\libraryfolders.vdf");
            try
            {
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"((?:\\\\.|[^\"\\\\])*)\""))
                    Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var root in steam)
        {
            var apps = Path.Combine(root, "steamapps");
            var candidates = new List<string> { Path.Combine(apps, "common", "Shroom and Gloom") };
            var manifest = Path.Combine(apps, "appmanifest_3271280.acf");
            try
            {
                if (File.Exists(manifest))
                {
                    var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s*\"([^\"]+)\"");
                    if (m.Success && Path.GetFileName(m.Groups[1].Value) == m.Groups[1].Value)
                        candidates.Add(Path.Combine(apps, "common", m.Groups[1].Value));
                }
                foreach (var path in candidates.Where(IsGame)) games.Add(Path.GetFullPath(path));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return games.OrderBy(x => x).ToList();
    }
}
