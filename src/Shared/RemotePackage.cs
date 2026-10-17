using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShroomGerman.Setup;

internal sealed record PackageProgress(string Status, int Percent);

internal sealed class RemotePackage : IDisposable
{
    internal const string AssetName = "ShroomAndGloom-FanTranslations.zip";
    internal const string TextAssetName = "ShroomAndGloom-German-Text.zip";
    internal const string ManifestName = "package-info.json";
    internal const string LatestUrl = "https://api.github.com/repos/Saphirah/ShroomAndGloom-FanTranslations/releases/latest";
    private const long MaxBytes = 128L * 1024 * 1024;
    private readonly string folder;
    internal PackageInfo Info { get; private set; } = null!;
    internal IReadOnlyDictionary<string, string> FilesToInstall { get; private set; } = null!;
    internal UpdateMode Mode { get; private set; }
    internal string PayloadPath => Path.Combine(folder, "payload.zip");

    private RemotePackage()
    {
        folder = Path.Combine(Path.GetTempPath(), "ShroomGerman-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    internal static async Task<RemotePackage> DownloadAsync(IProgress<PackageProgress>? progress = null, HttpClient? client = null, string? gameRoot = null,
        bool textsOnly = false)
    {
        using var ownedClient = client == null ? new HttpClient { Timeout = TimeSpan.FromMinutes(5) } : null;
        client ??= ownedClient!;
        var package = new RemotePackage();
        try
        {
            progress?.Report(new("Aktuelle Übersetzung wird gesucht …", 0));
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
            request.Headers.UserAgent.ParseAdd("ShroomAndGloom-FanTranslations/2");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new IOException("Noch kein veröffentlichtes Modpaket verfügbar.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new IOException("GitHub ist vorübergehend nicht erreichbar. Bitte später erneut versuchen.");
            response.EnsureSuccessStatusCode();
            await using var metadata = new MemoryStream();
            await CopyLimited(response, metadata, 1024 * 1024);
            using var release = JsonDocument.Parse(metadata.ToArray());
            var root = release.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
                throw new InvalidDataException("Das Modpaket ist noch nicht freigegeben.");
            var manifestPath = Path.Combine(package.folder, ManifestName);
            await DownloadAsset(client, root, ManifestName, manifestPath, 4 * 1024 * 1024);
            package.Info = JsonSerializer.Deserialize<PackageInfo>(await File.ReadAllTextAsync(manifestPath))
                ?? throw new InvalidDataException("Ungültige Paketbeschreibung.");
            ValidateInfo(package.Info, root.GetProperty("tag_name").GetString());
            var record = gameRoot == null ? new InstallRecord() : PackageState.ReadRecord(gameRoot);
            package.Mode = UpdatePlan.Select(gameRoot, record, package.Info);
            if (textsOnly && package.Mode == UpdateMode.Full)
                throw new IOException("Neue Modversion verfügbar. Bitte den Installer verwenden; die vorhandene Übersetzung bleibt aktiv.");
            package.FilesToInstall = package.Mode switch {
                UpdateMode.Current => new Dictionary<string, string>(),
                UpdateMode.Text => package.Info.Files.Where(file => UpdatePlan.IsText(file.Key)
                    && (package.Info.LanguageAssets.Count == 0 || gameRoot == null || !File.Exists(PackageState.SafePath(gameRoot, file.Key))
                        || PackageState.Hash(PackageState.SafePath(gameRoot, file.Key)) != file.Value))
                    .ToDictionary(file => file.Key, file => file.Value),
                _ => package.Info.Files
            };
            if (package.Mode == UpdateMode.Current)
            {
                progress?.Report(new("Übersetzung ist bereits aktuell.", 70));
                using var empty = new ZipArchive(File.Create(package.PayloadPath), ZipArchiveMode.Create);
                return package;
            }
            if (package.Mode == UpdateMode.Text && package.Info.LanguageAssets.Count > 0)
            {
                using var combined = new ZipArchive(File.Create(package.PayloadPath), ZipArchiveMode.Create);
                var done = 0;
                foreach (var file in package.FilesToInstall)
                {
                    if (!LanguagePack.IsPath(file.Key) || !package.Info.LanguageAssets.TryGetValue(file.Key, out var name))
                        throw new InvalidDataException("Missing language asset mapping.");
                    progress?.Report(new("Language update: " + Path.GetFileNameWithoutExtension(file.Key), 5 + done * 60 / package.FilesToInstall.Count));
                    var languageArchive = Path.Combine(package.folder, "download.zip");
                    await DownloadAsset(client, root, name, languageArchive, 8 * 1024 * 1024);
                    using var outer = ZipFile.OpenRead(languageArchive);
                    VerifyManifest(outer, manifestPath);
                    var inner = outer.GetEntry("payload.zip") ?? throw new InvalidDataException("Missing language payload.");
                    if (inner.Length > 8 * 1024 * 1024) throw new InvalidDataException("Language payload too large.");
                    using var input = inner.Open(); using var memory = new MemoryStream(); input.CopyTo(memory); memory.Position = 0;
                    using var content = new ZipArchive(memory);
                    if (content.Entries.Count != 1 || content.Entries[0].FullName != file.Key || content.Entries[0].Length > 8 * 1024 * 1024)
                        throw new InvalidDataException("Unexpected language payload.");
                    using var source = content.Entries[0].Open(); using var output = combined.CreateEntry(file.Key).Open(); source.CopyTo(output);
                    done++;
                }
                return package;
            }
            var assetName = package.Mode == UpdateMode.Text ? TextAssetName : AssetName;
            var status = package.Mode == UpdateMode.Text ? "Nur Übersetzung wird heruntergeladen …" : "Modpaket wird heruntergeladen …";
            progress?.Report(new(status, 5));
            var archivePath = Path.Combine(package.folder, "download.zip");
            await DownloadAsset(client, root, assetName, archivePath, textsOnly ? 8 * 1024 * 1024 : MaxBytes,
                percent => progress?.Report(new(status, 5 + percent * 60 / 100)));
            progress?.Report(new("Modpaket wird geprüft …", 70));
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count != 2 || archive.Entries.Select(e => e.FullName).Distinct().Count() != 2)
                throw new InvalidDataException("Unerwarteter Inhalt im Modpaket.");
            var manifest = archive.GetEntry(ManifestName) ?? throw new InvalidDataException("Paketbeschreibung fehlt.");
            var payload = archive.GetEntry("payload.zip") ?? throw new InvalidDataException("Moddateien fehlen.");
            if (manifest.Length > 4 * 1024 * 1024 || payload.Length > MaxBytes)
                throw new InvalidDataException("Das entpackte Modpaket ist zu groß.");
            using (var input = manifest.Open())
                if (!PackageState.HashStream(input)
                    .Equals(PackageState.Hash(manifestPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Modpaket und Paketbeschreibung gehören nicht zum selben Release.");
            using (var input = payload.Open())
            using (var output = File.Create(package.PayloadPath)) input.CopyTo(output);
            return package;
        }
        catch (HttpRequestException e)
        {
            package.Dispose();
            throw new IOException("Download von GitHub fehlgeschlagen. Bitte Internetverbindung prüfen und erneut versuchen.", e);
        }
        catch (TaskCanceledException e)
        {
            package.Dispose();
            throw new IOException("GitHub-Download hat zu lange gedauert. Bitte erneut versuchen.", e);
        }
        catch { package.Dispose(); throw; }
    }

    private static void ValidateInfo(PackageInfo info, string? tag)
    {
        if (info.InstallerProtocol is not (1 or 2))
            throw new IOException("Dieses Modpaket benötigt einen neueren Installer. Bitte den Installer von GitHub herunterladen.");
        if (info.SourceDirty || !Regex.IsMatch(info.Commit, "^[0-9a-f]{40}$"))
            throw new InvalidDataException("Das Modpaket stammt nicht aus einem freigegebenen, vollständig gespeicherten Commit.");
        if (!Version.TryParse(info.Version, out _) || tag != "v" + info.Version
            || !Regex.IsMatch(info.GameAssemblySha256, "^[0-9a-fA-F]{64}$")
            || info.Files == null || info.Files.Count is 0 or > 4096
            || info.Files.Values.Any(hash => hash == null || !Regex.IsMatch(hash, "^[0-9a-fA-F]{64}$")))
            throw new InvalidDataException("Ungültige Version oder Dateiliste des Modpakets.");
        if (info.LanguageAssets == null || info.LanguageAssets.Any(pair => !LanguagePack.IsPath(pair.Key)
            || !info.Files.ContainsKey(pair.Key) || !Regex.IsMatch(pair.Value, "^language-[a-zA-Z0-9-]+\\.zip$")))
            throw new InvalidDataException("Invalid language asset mapping.");
        if (info.InstallerProtocol == 2 && !info.Files.Keys.Where(LanguagePack.IsPath).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(info.LanguageAssets.Keys))
            throw new InvalidDataException("Incomplete language asset mapping.");
    }

    private static void VerifyManifest(ZipArchive archive, string manifestPath)
    {
        if (archive.Entries.Count != 2 || archive.Entries.Select(entry => entry.FullName).Distinct().Count() != 2)
            throw new InvalidDataException("Unexpected language archive.");
        var manifest = archive.GetEntry(ManifestName) ?? throw new InvalidDataException("Missing language manifest.");
        if (manifest.Length > 4 * 1024 * 1024) throw new InvalidDataException("Manifest too large.");
        using var input = manifest.Open();
        if (PackageState.HashStream(input) != PackageState.Hash(manifestPath)) throw new InvalidDataException("Language pack is from a different release.");
    }

    private static async Task DownloadAsset(HttpClient client, JsonElement release, string name, string path, long limit, Action<int>? progress = null)
    {
        var assets = release.GetProperty("assets").EnumerateArray().Where(asset => asset.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) throw new IOException("Im aktuellen Release fehlt die Paketdatei: " + name);
        var asset = assets[0];
        var size = asset.GetProperty("size").GetInt64();
        var digest = asset.GetProperty("digest").GetString() ?? "";
        if (size <= 0 || size > limit || !Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$"))
            throw new InvalidDataException("Ungültige Größe oder fehlende GitHub-Prüfsumme des Modpakets.");
        var url = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        if (url.Scheme != "https" || url.Host != "github.com" || !url.IsDefaultPort
            || !url.AbsolutePath.StartsWith("/Saphirah/ShroomAndGloom-FanTranslations/releases/download/", StringComparison.Ordinal))
            throw new InvalidDataException("Unbekannte Downloadquelle des Modpakets.");
        using var download = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        download.EnsureSuccessStatusCode();
        await using (var output = File.Create(path))
            await CopyLimited(download, output, size, bytes => progress?.Invoke((int)(bytes * 100 / size)));
        if (new FileInfo(path).Length != size || !PackageState.Hash(path).Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Die Prüfsumme des Downloads stimmt nicht. Bitte erneut versuchen.");
    }

    private static async Task CopyLimited(HttpResponseMessage response, Stream output, long limit, Action<long>? progress = null)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Download ist größer als erwartet.");
        await using var input = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer)) != 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException("Download ist größer als erwartet.");
            await output.WriteAsync(buffer.AsMemory(0, count));
            progress?.Invoke(total);
        }
    }

    public void Dispose()
    {
        // Only fixed filenames are ever extracted; remove no unrelated temp files.
        foreach (var name in new[] { "payload.zip", "download.zip", ManifestName })
            try { File.Delete(Path.Combine(folder, name)); } catch (IOException) { }
        try { Directory.Delete(folder); } catch (IOException) { }
    }
}
