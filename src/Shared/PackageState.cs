using System.Security.Cryptography;
using System.Text.Json;

namespace ShroomGerman.Setup;

internal sealed class InstallRecord
{
    public string Version { get; set; } = "";
    public string Commit { get; set; } = "";
    // Package snapshot is independent of file ownership (shared loader files
    // can already exist and must not become ours merely through an update).
    public Dictionary<string, string> PackageFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class PackageInfo
{
    public int InstallerProtocol { get; set; }
    public string Version { get; set; } = "";
    public string Commit { get; set; } = "";
    public bool SourceDirty { get; set; }
    public Dictionary<string, string> LanguageAssets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string GameAssemblySha256 { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new();
}

internal static class PackageState
{
    internal const string RecordName = ".shroomgerman-install.json";
    internal static string Hash(string path) { using var s = File.OpenRead(path); return HashStream(s); }
    internal static string HashStream(Stream stream) { using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant(); }

    internal static string SafePath(string root, string relative)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized) || normalized.Split(Path.DirectorySeparatorChar).Any(p => p is ".." or "." or ""
            || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.EndsWith('.') || p.EndsWith(' ')))
            throw new InvalidDataException("Ungültiger Dateipfad im Modpaket.");
        var target = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        if (!target.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Dateipfad außerhalb des Spielordners.");
        for (var current = target; current.Length > fullRoot.Length; current = Path.GetDirectoryName(current)!)
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Verknüpfung im Zielpfad gefunden: " + current);
        return target;
    }

    internal static bool IsModFile(string relative) => new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" }
        .Contains(relative, StringComparer.OrdinalIgnoreCase) || new[] { "BepInEx/core/", "BepInEx/plugins/ShroomGerman/", "dotnet/", "BepInEx/unity-libs/" }
        .Any(prefix => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    internal static InstallRecord ReadRecord(string root)
    {
        var path = Path.Combine(root, RecordName);
        if (!File.Exists(path)) return new();
        var record = JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(path)) ?? throw new InvalidDataException("Installationsprotokoll ist beschädigt.");
        record.Files = new Dictionary<string, string>(record.Files, StringComparer.OrdinalIgnoreCase);
        record.PackageFiles = new Dictionary<string, string>(record.PackageFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var relative in record.Files.Keys) {
            if (!IsModFile(relative)) throw new InvalidDataException("Unbekannte Datei im Installationsprotokoll: " + relative);
            SafePath(root, relative);
        }
        return record;
    }

}
