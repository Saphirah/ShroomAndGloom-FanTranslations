using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ShroomGerman.Setup;

internal static class ModInstaller
{
    internal const string RecordName = PackageState.RecordName;
    internal static string Hash(string path) => PackageState.Hash(path);
    internal static string SafePath(string root, string relative) => PackageState.SafePath(root, relative);
    private static bool IsModFile(string relative) => PackageState.IsModFile(relative);
    internal static InstallRecord ReadRecord(string root) => PackageState.ReadRecord(root);

    internal static void CheckGame(string root)
    {
        if (!GameLocator.IsGame(root)) throw new IOException("Bitte den Ordner mit „Shroom and Gloom.exe“ auswählen.");
        if (Process.GetProcessesByName("Shroom and Gloom").Length != 0)
            throw new IOException("Bitte Shroom and Gloom zuerst schließen.");
    }

    public static async Task<string> InstallAsync(string root, IProgress<PackageProgress>? progress = null)
    {
        CheckGame(root);
        using var package = await RemotePackage.DownloadAsync(progress, gameRoot: root);
        progress?.Report(new("Wird installiert …", 75));
        await Task.Run(() => {
            using var payload = File.OpenRead(package.PayloadPath);
            Install(root, package.Info, payload, new InlineProgress<int>(value =>
                progress?.Report(new("Wird installiert …", 75 + value / 4))), package.FilesToInstall);
        });
        return package.Info.Version;
    }

    internal static void Install(string root, PackageInfo info, Stream payload, IProgress<int>? progress = null,
        IReadOnlyDictionary<string, string>? selectedFiles = null)
    {
        root = Path.GetFullPath(root); CheckGame(root);
        if (!Hash(Path.Combine(root, "GameAssembly.dll")).Equals(info.GameAssemblySha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Das aktuelle Modpaket unterstützt diese Spielversion nicht.");
        var record = ReadRecord(root);
        var expectedFiles = selectedFiles ?? info.Files;
        if (expectedFiles.Any(file => !info.Files.TryGetValue(file.Key, out var hash) || hash != file.Value))
            throw new InvalidDataException("Update gehört nicht zum aktuellen Modpaket.");
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        if (entries.Count > 4096 || entries.Any(e => e.Length > 128L * 1024 * 1024)
            || entries.Sum(e => e.Length) > 512L * 1024 * 1024)
            throw new InvalidDataException("Das Modpaket ist zu groß.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var dest = SafePath(root, e.FullName);
            if (!IsModFile(e.FullName) || !paths.Add(dest) || !expectedFiles.TryGetValue(e.FullName, out var expected))
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
        if (entries.Count != expectedFiles.Count) throw new InvalidDataException("Modpaket ist unvollständig.");
        // Do not advance the commit if files omitted from a text/current update
        // disappeared or changed after the download decision.
        foreach (var file in info.Files.Where(file => !expectedFiles.ContainsKey(file.Key)))
        {
            var path = SafePath(root, file.Key);
            if (!File.Exists(path) || !Hash(path).Equals(file.Value, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Die installierten Moddateien haben sich geändert. Bitte Installation erneut starten.");
        }
        CheckGame(root); // Recheck after download and validation.
        // Keep rollback copies in memory: the payload is small and originals are only mod files.
        var rollback = new Dictionary<string, byte[]?>();
        var oldRecord = File.Exists(Path.Combine(root, RecordName)) ? File.ReadAllBytes(Path.Combine(root, RecordName)) : null;
        try
        {
            var done = 0;
            foreach (var e in entries)
            {
                var dest = SafePath(root, e.FullName);
                if (!File.Exists(dest) || Hash(dest) != info.Files[e.FullName])
                {
                    rollback[dest] = File.Exists(dest) ? File.ReadAllBytes(dest) : null;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using (var source = e.Open()) using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None)) source.CopyTo(output);
                    record.Files[e.FullName] = info.Files[e.FullName];
                }
                // Identical pre-existing third-party loader files are used, not claimed as ours.
                progress?.Report(++done * 100 / entries.Count);
            }
            record.Version = info.Version;
            record.Commit = info.Commit;
            record.PackageFiles = new(info.Files, StringComparer.OrdinalIgnoreCase);
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
        return retained.Count == 0 ? "Die Übersetzungsmod wurde entfernt. Spielstände bleiben erhalten."
            : "Deinstallation abgeschlossen. Veränderte Dateien oder ein von anderen Mods benötigter Loader wurden beibehalten.";
    }

    public static object Verify(string root)
    {
        var record = ReadRecord(root);
        return new {
            GamePath = root, GameFound = GameLocator.IsGame(root), Version = record.Version, Commit = record.Commit,
            Files = record.Files.ToDictionary(x => x.Key, x => File.Exists(SafePath(root, x.Key)) && Hash(SafePath(root, x.Key)) == x.Value)
        };
    }
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
