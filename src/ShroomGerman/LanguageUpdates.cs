using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ShroomGerman.Setup;

namespace ShroomGerman;

// Managed file/network operations only. Unity objects are refreshed later by the
// TranslationPump on the game thread. Never downloads or replaces executable code.
internal static class LanguageUpdates
{
    internal static async Task<bool> RunAsync(string gameRoot, Action<string>? log = null, HttpClient? client = null)
    {
        var pluginFolder = PackageState.SafePath(gameRoot, "BepInEx/plugins/ShroomGerman");
        using var guard = new FileStream(Path.Combine(pluginFolder, ".language-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var ownedClient = client == null ? new HttpClient { Timeout = TimeSpan.FromSeconds(15) } : null;
        using var package = await RemotePackage.DownloadAsync(client: client ?? ownedClient!, gameRoot: gameRoot, textsOnly: true);
        var record = PackageState.ReadRecord(gameRoot);
        var contents = new Dictionary<string, byte[]>();
        using (var zip = ZipFile.OpenRead(package.PayloadPath))
        {
            if (zip.Entries.Count != package.FilesToInstall.Count) throw new InvalidDataException("Sprachpaket ist unvollständig.");
            foreach (var entry in zip.Entries)
            {
                if (!UpdatePlan.IsText(entry.FullName) || !package.FilesToInstall.TryGetValue(entry.FullName, out var expected)
                    || contents.ContainsKey(entry.FullName) || entry.Length > 8 * 1024 * 1024)
                    throw new InvalidDataException("Unerwartete Datei im Sprachpaket.");
                using var input = entry.Open(); using var output = new MemoryStream();
                var buffer = new byte[81920]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (output.Length + count > 8 * 1024 * 1024) throw new InvalidDataException("Sprachdatei ist zu groß.");
                    output.Write(buffer, 0, count);
                }
                var bytes = output.ToArray();
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Prüfsumme der Sprachdatei stimmt nicht.");
                Validate(entry.FullName, bytes);
                contents.Add(entry.FullName, bytes);
            }
        }
        // Recheck after downloading; never overwrite local edits or a changed runtime.
        if (UpdatePlan.Select(gameRoot, record, package.Info) == UpdateMode.Full)
            throw new IOException("Moddateien haben sich geändert. Sprachupdate übersprungen.");
        var rollback = new Dictionary<string, byte[]?>();
        foreach (var file in contents)
        {
            var path = PackageState.SafePath(gameRoot, file.Key);
            if (!File.Exists(path))
            {
                if (!LanguagePack.IsPath(file.Key)) throw new IOException("Sprachdatei fehlt. Bitte den Installer verwenden.");
                rollback[path] = null; continue;
            }
            var current = PackageState.Hash(path);
            if (current != package.Info.Files[file.Key]
                && (!record.Files.TryGetValue(file.Key, out var owned) || current != owned))
                throw new IOException("Lokal geänderte Übersetzung bleibt erhalten: " + Path.GetFileName(path));
            rollback[path] = File.ReadAllBytes(path);
        }
        var recordPath = Path.Combine(gameRoot, PackageState.RecordName);
        var oldRecord = File.ReadAllBytes(recordPath);
        try
        {
            foreach (var file in contents)
            {
                var path = PackageState.SafePath(gameRoot, file.Key);
                if (File.Exists(path) && PackageState.Hash(path) == package.Info.Files[file.Key]) continue;
                WriteAtomic(path, file.Value);
                // Text files replaced by us become owned; shared loader ownership
                // is never expanded by the automatic language update.
                record.Files[file.Key] = package.Info.Files[file.Key];
            }
            record.Commit = package.Info.Commit; record.Version = package.Info.Version;
            record.PackageFiles = new(package.Info.Files, StringComparer.OrdinalIgnoreCase);
            WriteAtomic(recordPath, JsonSerializer.SerializeToUtf8Bytes(record, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            foreach (var file in rollback)
                if (file.Value == null) File.Delete(file.Key); else WriteAtomic(file.Key, file.Value);
            WriteAtomic(recordPath, oldRecord);
            throw;
        }
        log?.Invoke(contents.Count == 0 ? "Sprachpaket ist aktuell." : $"Sprachpaket {package.Info.Version} aktualisiert (Commit {package.Info.Commit[..8]}).");
        return contents.Count != 0;
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static void Validate(string path, byte[] bytes)
    {
        if (LanguagePack.IsPath(path)) { LanguagePack.Parse(path, bytes); return; }
        using var document = JsonDocument.Parse(bytes);
        if (path.EndsWith("ui-literals.de.json", StringComparison.Ordinal))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Ungültige UI-Übersetzung.");
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in document.RootElement.EnumerateObject())
                if (!keys.Add(value.Name) || string.IsNullOrWhiteSpace(value.Name) || value.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Ungültige UI-Übersetzung.");
            return;
        }
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Ungültiger Sprachkatalog.");
        var knownTables = new HashSet<string> { "Card Names", "UI", "Enemy", "World", "Card" };
        var tables = new HashSet<string>(); var ids = new HashSet<(string?, string?)>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var table = row.GetProperty("table").GetString(); var id = row.GetProperty("id").GetString();
            if (table == null || !knownTables.Contains(table) || !long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                || !ids.Add((table, id)) || row.GetProperty("de").ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Ungültiger Übersetzungseintrag.");
            tables.Add(table);
        }
        if (!tables.SetEquals(knownTables)) throw new InvalidDataException("Im Sprachpaket fehlen Tabellen.");
    }
}
