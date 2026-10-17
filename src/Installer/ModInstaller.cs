using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ShroomGerman.Setup;

internal sealed class InstallRecord
{
    public string Version { get; set; } = "0.1.0";
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class PackageInfo
{
    public string GameAssemblySha256 { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new();
}

internal static class ModInstaller
{
    internal const string RecordName = ".shroomgerman-install.json";
    internal static Stream Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidDataException("Paketdatei fehlt: " + name);
    internal static string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }
    internal static PackageInfo Info { get; } = JsonSerializer.Deserialize<PackageInfo>(Resource("package-info.json"))!;

    internal static string SafePath(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized) || normalized.Split(Path.DirectorySeparatorChar).Any(p => p is ".." or "." or ""))
            throw new InvalidDataException("Ungültiger Dateipfad im Modpaket.");
        var target = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        if (!target.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Dateipfad außerhalb des Spielordners.");
        for (var current = target; current.Length > fullRoot.Length; current = Path.GetDirectoryName(current)!)
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Verknüpfung im Zielpfad gefunden: " + current);
        return target;
    }

    private static bool IsModFile(string relative) => new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" }
        .Contains(relative, StringComparer.OrdinalIgnoreCase) || new[] { "BepInEx/core/", "BepInEx/plugins/ShroomGerman/", "dotnet/", "BepInEx/unity-libs/" }
        .Any(prefix => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    internal static void CheckGame(string root)
    {
        if (!GameLocator.IsGame(root)) throw new IOException("Bitte den Ordner mit „Shroom and Gloom.exe“ auswählen.");
        if (Process.GetProcessesByName("Shroom and Gloom").Length != 0)
            throw new IOException("Bitte Shroom and Gloom zuerst schließen. Der Installer startet das Spiel nicht.");
    }

    internal static InstallRecord ReadRecord(string root)
    {
        var path = Path.Combine(root, RecordName);
        if (!File.Exists(path)) return new();
        var record = JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(path)) ?? throw new InvalidDataException("Installationsprotokoll ist beschädigt.");
        record.Files = new Dictionary<string, string>(record.Files, StringComparer.OrdinalIgnoreCase);
        foreach (var relative in record.Files.Keys) {
            if (!IsModFile(relative)) throw new InvalidDataException("Unbekannte Datei im Installationsprotokoll: " + relative);
            SafePath(root, relative);
        }
        return record;
    }

    public static void Install(string root, IProgress<int>? progress = null)
    {
        root = Path.GetFullPath(root); CheckGame(root);
        if (!Hash(Path.Combine(root, "GameAssembly.dll")).Equals(Info.GameAssemblySha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Diese Spielversion wird von Mod 0.1.0 noch nicht unterstützt. Unterstützt wird Steam-Build 25442225.");
        var record = ReadRecord(root);
        using var zip = new ZipArchive(Resource("payload.zip"));
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var dest = SafePath(root, e.FullName);
            if (!IsModFile(e.FullName) || !paths.Add(dest) || !Info.Files.TryGetValue(e.FullName, out var expected))
                throw new InvalidDataException("Unerwartete oder doppelte Paketdatei.");
            using var source = e.Open();
            if (!Convert.ToHexString(SHA256.HashData(source)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Prüfsumme der Paketdatei stimmt nicht: " + e.FullName);
            if (!File.Exists(dest)) continue;
            var current = Hash(dest);
            if (current == expected) continue;
            if (!record.Files.TryGetValue(e.FullName, out var owned) || current != owned)
                throw new IOException("Vorhandene Moddatei wird geschützt: " + e.FullName + "\nBitte bestehende Modloader nicht überschreiben.");
        }
        if (entries.Count != Info.Files.Count) throw new InvalidDataException("Modpaket ist unvollständig.");
        // Keep rollback copies in memory: the payload is small and originals are only mod files.
        var rollback = new Dictionary<string, byte[]?>();
        var oldRecord = File.Exists(Path.Combine(root, RecordName)) ? File.ReadAllBytes(Path.Combine(root, RecordName)) : null;
        try
        {
            var done = 0;
            foreach (var e in entries)
            {
                var dest = SafePath(root, e.FullName);
                if (!File.Exists(dest) || Hash(dest) != Info.Files[e.FullName])
                {
                    rollback[dest] = File.Exists(dest) ? File.ReadAllBytes(dest) : null;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using (var source = e.Open()) using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None)) source.CopyTo(output);
                    record.Files[e.FullName] = Info.Files[e.FullName];
                }
                // Identical pre-existing third-party loader files are used, not claimed as ours.
                progress?.Report(++done * 100 / entries.Count);
            }
            record.Version = "0.1.0";
            File.WriteAllText(Path.Combine(root, RecordName), JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            foreach (var (path, bytes) in rollback)
                if (bytes == null) File.Delete(path); else File.WriteAllBytes(path, bytes);
            if (oldRecord == null) File.Delete(Path.Combine(root, RecordName));
            else File.WriteAllBytes(Path.Combine(root, RecordName), oldRecord);
            throw;
        }
    }

    public static string Uninstall(string root)
    {
        CheckGame(root);
        if (!File.Exists(Path.Combine(root, RecordName))) throw new IOException("Keine protokollierte Installation dieser Mod gefunden.");
        var record = ReadRecord(root);
        var retained = new List<string>();
        // If another plugin was added, keep the shared loader and remove our plugin only.
        var plugins = Path.Combine(root, "BepInEx/plugins");
        var foreignPlugin = Directory.Exists(plugins) && Directory.EnumerateFiles(plugins, "*.dll", SearchOption.AllDirectories)
            .Any(p => !Path.GetRelativePath(plugins, p).Equals(Path.Combine("ShroomGerman", "ShroomGerman.dll"), StringComparison.OrdinalIgnoreCase));
        foreach (var (relative, expected) in record.Files.ToList())
        {
            if (foreignPlugin && !relative.StartsWith("BepInEx/plugins/ShroomGerman/", StringComparison.OrdinalIgnoreCase))
            { retained.Add(relative); continue; }
            var dest = SafePath(root, relative);
            if (!File.Exists(dest)) { record.Files.Remove(relative); continue; }
            if (Hash(dest) != expected) { retained.Add(relative); continue; }
            File.Delete(dest); record.Files.Remove(relative);
        }
        if (record.Files.Count == 0) File.Delete(Path.Combine(root, RecordName));
        else File.WriteAllText(Path.Combine(root, RecordName), JsonSerializer.Serialize(record));
        return retained.Count == 0 ? "Die deutsche Mod wurde entfernt. Spielstände bleiben erhalten."
            : "Deinstallation abgeschlossen. Veränderte Dateien oder ein von anderen Mods benötigter Loader wurden beibehalten.";
    }

    public static object Verify(string root) => new {
        GamePath = root, GameFound = GameLocator.IsGame(root),
        Files = Info.Files.ToDictionary(x => x.Key, x => File.Exists(SafePath(root, x.Key)) && Hash(SafePath(root, x.Key)) == x.Value)
    };
}
