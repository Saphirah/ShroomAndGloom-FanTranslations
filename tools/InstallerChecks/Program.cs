using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ShroomGerman.Setup;
using ShroomGerman;

// Isolated fixtures only: never start, stop or modify the installed game.
var passed = 0;
async Task Check(string name, Func<Task> action) { await action(); Console.WriteLine("PASS " + name); passed++; }
async Task Reject(string name, Action<Fixture> configure)
{
    await Check(name, async () => {
        var fixture = new Fixture(); configure(fixture);
        using var client = new HttpClient(fixture);
        try { using var package = await RemotePackage.DownloadAsync(client: client); }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException) { return; }
        throw new Exception("Invalid release accepted: " + name);
    });
}
await Check("first install downloads full package and records source commit", async () => {
    var fixture = new Fixture(); using var client = new HttpClient(fixture);
    var package = await RemotePackage.DownloadAsync(client: client);
    var folder = Path.GetDirectoryName(package.PayloadPath)!;
    if (package.Mode != UpdateMode.Full || package.Info.Commit != fixture.Info.Commit || !fixture.Downloads.Contains(RemotePackage.AssetName)) throw new Exception("Wrong package");
    package.Dispose();
    if (Directory.Exists(folder)) throw new Exception("Temporary files retained");
});
await Reject("modified download", f => f.BadDigest = true);
await Reject("missing package metadata", f => f.Missing = true);
await Reject("draft release", f => f.Draft = true);
await Reject("prerelease", f => f.Prerelease = true);
await Reject("future installer protocol", f => f.Protocol = 3);
await Reject("release and package version mismatch", f => f.Tag = "v9.0.0");
await Reject("foreign download URL", f => f.Url = "https://example.com/mod.zip");
await Reject("truncated download", f => f.Truncate = true);
await Reject("GitHub rate limit", f => f.Status = HttpStatusCode.Forbidden);
await Reject("offline download", f => f.Offline = true);
await Reject("uncommitted preview is never installed", f => f.Dirty = true);
await Reject("missing commit is rejected", f => f.Commit = "");
await Reject("package and manifest mismatch", f => f.ManifestMismatch = true);
async Task PlanCheck(string name, Fixture next, Action<Sandbox>? alter, UpdateMode mode)
{
    await Check(name, async () => {
        using var sandbox = new Sandbox(); sandbox.WriteInstalled(new Fixture()); alter?.Invoke(sandbox);
        using var client = new HttpClient(next);
        using var package = await RemotePackage.DownloadAsync(client: client, gameRoot: sandbox.Root);
        if (package.Mode != mode) throw new Exception("Wrong update mode: " + package.Mode);
        if (mode != UpdateMode.Full && next.Downloads.Contains(RemotePackage.AssetName)) throw new Exception("Unnecessary full download");
        if (mode == UpdateMode.Text && !next.Downloads.Contains(RemotePackage.TextAssetName)) throw new Exception("Missing text download");
        if (mode == UpdateMode.Current && next.Downloads.Count != 1) throw new Exception("Current install should fetch only manifest");
        using var zip = ZipFile.OpenRead(package.PayloadPath);
        if (zip.Entries.Count != package.FilesToInstall.Count) throw new Exception("Wrong payload selection");
    });
}
await PlanCheck("text-only commit downloads only text package", new Fixture { Text = "revised", Commit = new string('c', 40) }, null, UpdateMode.Text);
await PlanCheck("code change downloads full package", new Fixture { Runtime = "new-code", Commit = new string('c', 40) }, null, UpdateMode.Full);
await PlanCheck("already current downloads no payload", new Fixture(), null, UpdateMode.Current);
await PlanCheck("different commit with identical artifacts downloads no payload", new Fixture { Commit = new string('c', 40) }, null, UpdateMode.Current);
await PlanCheck("missing loader despite recorded commit triggers full download", new Fixture(), s => File.Delete(ModInstaller.SafePath(s.Root, Fixture.CodePath)), UpdateMode.Full);
await PlanCheck("changed installed code triggers full download", new Fixture(), s => File.WriteAllText(ModInstaller.SafePath(s.Root, Fixture.CodePath), "modified"), UpdateMode.Full);
await PlanCheck("missing text triggers small download", new Fixture(), s => File.Delete(ModInstaller.SafePath(s.Root, Fixture.TextPath)), UpdateMode.Text);
await PlanCheck("legacy install without commit uses full package once", new Fixture(), s => {
    var record = ModInstaller.ReadRecord(s.Root); record.Commit = "";
    File.WriteAllText(Path.Combine(s.Root, ModInstaller.RecordName), JsonSerializer.Serialize(record));
}, UpdateMode.Full);
await Check("path traversal and alternate data streams", () => {
    foreach (var path in new[] { "../GameAssembly.dll", "BepInEx/../../GameAssembly.dll", "BepInEx/plugins/a.dll:stream", "BepInEx/plugins/file. " })
    {
        try { ModInstaller.SafePath(Path.GetTempPath(), path); }
        catch (InvalidDataException) { continue; }
        throw new Exception("Unsafe path accepted: " + path);
    }
    return Task.CompletedTask;
});
if (Process.GetProcessesByName("Shroom and Gloom").Length == 0)
    await Check("isolated install then text update persists commit and uninstall ownership", async () => {
        using var sandbox = new Sandbox();
        var first = new Fixture(); using var client = new HttpClient(first);
        using (var package = await RemotePackage.DownloadAsync(client: client))
        using (var input = File.OpenRead(package.PayloadPath)) ModInstaller.Install(sandbox.Root, package.Info, input);
        var next = new Fixture { Text = "revised", Commit = new string('c', 40) }; using var client2 = new HttpClient(next);
        // A rejected text payload must not advance the installed commit or touch text.
        using (var package = await RemotePackage.DownloadAsync(client: client2, gameRoot: sandbox.Root))
        {
            using var invalid = new MemoryStream(new byte[] { 1, 2, 3 });
            try { ModInstaller.Install(sandbox.Root, package.Info, invalid, selectedFiles: package.FilesToInstall); throw new Exception("Invalid payload accepted"); }
            catch (InvalidDataException) { }
            if (ModInstaller.ReadRecord(sandbox.Root).Commit != first.Commit
                || File.ReadAllText(ModInstaller.SafePath(sandbox.Root, Fixture.TextPath)) != "text") throw new Exception("Failed update changed installation");
        }
        using (var package = await RemotePackage.DownloadAsync(client: client2, gameRoot: sandbox.Root))
        using (var input = File.OpenRead(package.PayloadPath)) ModInstaller.Install(sandbox.Root, package.Info, input, selectedFiles: package.FilesToInstall);
        var record = ModInstaller.ReadRecord(sandbox.Root);
        if (record.Commit != next.Commit || record.PackageFiles[Fixture.CodePath] != first.Info.Files[Fixture.CodePath]
            || File.ReadAllText(ModInstaller.SafePath(sandbox.Root, Fixture.TextPath)) != "revised") throw new Exception("Update record mismatch");
        var same = new Fixture { Text = "revised", Commit = new string('d', 40) }; using var client3 = new HttpClient(same);
        using (var package = await RemotePackage.DownloadAsync(client: client3, gameRoot: sandbox.Root))
        using (var input = File.OpenRead(package.PayloadPath)) {
            if (package.Mode != UpdateMode.Current) throw new Exception("Unnecessary download for identical commit artifacts");
            ModInstaller.Install(sandbox.Root, package.Info, input, selectedFiles: package.FilesToInstall);
        }
        if (ModInstaller.ReadRecord(sandbox.Root).Commit != same.Commit) throw new Exception("Current commit not saved");
        ModInstaller.Uninstall(sandbox.Root);
        if (File.Exists(ModInstaller.SafePath(sandbox.Root, Fixture.CodePath))) throw new Exception("Owned files retained");
    });
else Console.WriteLine("SKIP install/write checks: real game running; download and planning fixtures still checked.");
string Catalog(string label) => JsonSerializer.Serialize(new[] { "Card Names", "UI", "Enemy", "World", "Card" }
    .Select(table => new { table, id = "1", key = "EXAMPLE", de = label }));
await Check("startup updater applies text and preserves exact whitespace and runtime", async () => {
    using var sandbox = new Sandbox(); var old = new Fixture { Text = Catalog("alt") }; sandbox.WriteInstalled(old);
    var next = new Fixture { Text = Catalog("  neu \n"), Commit = new string('c', 40) }; using var client = new HttpClient(next);
    if (!await LanguageUpdates.RunAsync(sandbox.Root, client: client)) throw new Exception("Text not applied");
    if (File.ReadAllText(ModInstaller.SafePath(sandbox.Root, Fixture.TextPath)) != next.Text
        || File.ReadAllText(ModInstaller.SafePath(sandbox.Root, Fixture.CodePath)) != old.Runtime
        || ModInstaller.ReadRecord(sandbox.Root).Commit != next.Commit
        || next.Downloads.Contains(RemotePackage.AssetName)) throw new Exception("Startup update mismatch");
});
await Check("startup updater skips current text and advances identical artifact commit", async () => {
    using var sandbox = new Sandbox(); var old = new Fixture { Text = Catalog("alt") }; sandbox.WriteInstalled(old);
    var next = new Fixture { Text = old.Text, Commit = new string('d', 40) }; using var client = new HttpClient(next);
    if (await LanguageUpdates.RunAsync(sandbox.Root, client: client) || next.Downloads.Count != 1
        || ModInstaller.ReadRecord(sandbox.Root).Commit != next.Commit) throw new Exception("Unnecessary text download");
});
async Task RejectStartup(string name, Fixture next, Action<Sandbox>? alter = null)
{
    await Check(name, async () => {
        using var sandbox = new Sandbox(); sandbox.WriteInstalled(new Fixture { Text = Catalog("alt") }); alter?.Invoke(sandbox);
        var textPath = ModInstaller.SafePath(sandbox.Root, Fixture.TextPath);
        var old = File.ReadAllBytes(textPath); var oldRecord = File.ReadAllBytes(Path.Combine(sandbox.Root, ModInstaller.RecordName));
        using var client = new HttpClient(next);
        try { await LanguageUpdates.RunAsync(sandbox.Root, client: client); throw new Exception("Unsafe update accepted"); }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException) { }
        if (!File.ReadAllBytes(textPath).SequenceEqual(old)
            || !File.ReadAllBytes(Path.Combine(sandbox.Root, ModInstaller.RecordName)).SequenceEqual(oldRecord)
            || next.Downloads.Contains(RemotePackage.AssetName)) throw new Exception("Existing installation changed");
    });
}
await RejectStartup("startup updater refuses code changes without full download", new Fixture { Runtime = "new code", Text = Catalog("neu") });
await RejectStartup("startup updater preserves text when offline", new Fixture { Offline = true });
await RejectStartup("startup updater rejects invalid JSON before writing", new Fixture { Text = "not JSON" });
await RejectStartup("startup updater preserves local edits", new Fixture { Text = Catalog("neu") }, sandbox =>
    File.WriteAllText(ModInstaller.SafePath(sandbox.Root, Fixture.TextPath), Catalog("local edit")));
if (args.Length == 1)
    await Check("built full/text archives share manifest and all file hashes", () => {
        var dist = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
        var manifestBytes = File.ReadAllBytes(Path.Combine(dist, RemotePackage.ManifestName));
        var info = JsonSerializer.Deserialize<PackageInfo>(manifestBytes)!;
        var assets = info.LanguageAssets.Count > 0 ? new[] { RemotePackage.AssetName }.Concat(info.LanguageAssets.Values) : new[] { RemotePackage.AssetName, RemotePackage.TextAssetName };
        foreach (var name in assets)
        {
            using var outer = ZipFile.OpenRead(Path.Combine(dist, name));
            using (var manifest = outer.GetEntry(RemotePackage.ManifestName)!.Open())
                if (!SHA256.HashData(manifest).SequenceEqual(SHA256.HashData(manifestBytes))) throw new Exception("Manifest mismatch");
            using var innerStream = outer.GetEntry("payload.zip")!.Open(); using var memory = new MemoryStream(); innerStream.CopyTo(memory); memory.Position = 0;
            using var zip = new ZipArchive(memory);
            var language = info.LanguageAssets.FirstOrDefault(pair => pair.Value == name).Key;
            var expected = language != null ? 1 : name == RemotePackage.TextAssetName ? info.Files.Where(f => UpdatePlan.IsText(f.Key)).Count() : info.Files.Count;
            if (zip.Entries.Count != expected) throw new Exception("Incomplete package");
            foreach (var entry in zip.Entries)
            {
                using var input = entry.Open();
                if (Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant() != info.Files[entry.FullName]) throw new Exception("Payload hash mismatch");
                if ((language != null || name == RemotePackage.TextAssetName) && !UpdatePlan.IsText(entry.FullName)) throw new Exception("Code in text update");
                if (language != null && entry.FullName != language) throw new Exception("Wrong language in package");
                if (LanguagePack.IsPath(entry.FullName)) {
                    using var bytes = entry.Open(); using var data = new MemoryStream(); bytes.CopyTo(data); LanguagePack.Parse(entry.FullName, data.ToArray());
                }
            }
        }
        return Task.CompletedTask;
    });
async Task LanguageCheck(string name, Dictionary<string, string> before, Dictionary<string, string> after, string[] downloads)
{
    await Check(name, async () => {
        using var sandbox = new Sandbox(); var old = new Fixture { LanguageTexts = before }; sandbox.WriteInstalled(old);
        var next = new Fixture { LanguageTexts = after, Commit = new string('e', 40) }; using var client = new HttpClient(next);
        if (!await LanguageUpdates.RunAsync(sandbox.Root, client: client)) throw new Exception("Language update missing");
        var assets = next.Downloads.Where(asset => asset != RemotePackage.ManifestName).OrderBy(asset => asset).ToArray();
        if (!assets.SequenceEqual(downloads.OrderBy(asset => asset))) throw new Exception("Unnecessary language download: " + string.Join(",", assets));
        foreach (var file in next.Files)
            if (!File.ReadAllBytes(ModInstaller.SafePath(sandbox.Root, file.Key)).SequenceEqual(file.Value)) throw new Exception("Language contents mismatch");
    });
}
await LanguageCheck("only changed French package is downloaded", new() { ["en"] = "same", ["de"] = "gleich", ["fr"] = "avant" },
    new() { ["en"] = "same", ["de"] = "gleich", ["fr"] = "après" }, new[] { "language-fr.zip" });
await LanguageCheck("two changed languages download exactly two packages", new() { ["en"] = "same", ["de"] = "alt", ["fr"] = "avant" },
    new() { ["en"] = "same", ["de"] = "neu", ["fr"] = "après" }, new[] { "language-de.zip", "language-fr.zip" });
await LanguageCheck("new language is added without a runtime download", new() { ["en"] = "same", ["de"] = "gleich" },
    new() { ["en"] = "same", ["de"] = "gleich", ["fr"] = "nouveau" }, new[] { "language-fr.zip" });
Console.WriteLine($"{passed} focused checks passed. No game interaction or publication.");

sealed class Fixture : HttpMessageHandler
{
    internal const string CodePath = "BepInEx/plugins/ShroomGerman/ShroomGerman.dll";
    internal const string TextPath = "BepInEx/plugins/ShroomGerman/translations.de.json";
    public bool BadDigest, Missing, Draft, Prerelease, Truncate, Offline, Dirty, ManifestMismatch;
    public int Protocol = 1;
    public string Tag = "v0.1.5", Commit = new string('a', 40), Runtime = "code", Text = "text";
    public string? Url;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public Dictionary<string, string>? LanguageTexts;
    public List<string> Downloads = new();
    public Dictionary<string, byte[]> Files {
        get {
            if (LanguageTexts != null) {
                var files = LanguageTexts.ToDictionary(pair => LanguagePack.Prefix + pair.Key + ".json", pair => JsonSerializer.SerializeToUtf8Bytes(new LanguagePack {
                    Locale = pair.Key, Name = pair.Key, NativeName = pair.Key,
                    Entries = new[] { "Card Names", "UI", "Enemy", "World", "Card" }.Select(table => new CatalogEntry { Table = table, Id = "1", Text = pair.Value }).ToList(),
                    Literals = new() { ["PLAY"] = pair.Value }
                }));
                files[CodePath] = System.Text.Encoding.UTF8.GetBytes(Runtime); return files;
            }
            return new() {
        [CodePath] = System.Text.Encoding.UTF8.GetBytes(Runtime), [TextPath] = System.Text.Encoding.UTF8.GetBytes(Text),
        ["BepInEx/plugins/ShroomGerman/ui-literals.de.json"] = "{}"u8.ToArray()
            };
        }
    }
    public PackageInfo Info => new() { InstallerProtocol = LanguageTexts != null && Protocol == 1 ? 2 : Protocol, Version = "0.1.5", Commit = Commit, SourceDirty = Dirty,
        GameAssemblySha256 = Convert.ToHexString(SHA256.HashData("game"u8)).ToLowerInvariant(),
        Files = Files.ToDictionary(f => f.Key, f => Convert.ToHexString(SHA256.HashData(f.Value)).ToLowerInvariant()),
        LanguageAssets = Files.Keys.Where(LanguagePack.IsPath).ToDictionary(path => path, path => "language-" + Path.GetFileNameWithoutExtension(path) + ".zip") };
    static byte[] Zip(Dictionary<string, byte[]> files)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var f in files) {
                var entry = zip.CreateEntry(f.Key); entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var stream = entry.Open(); stream.Write(f.Value);
            }
        return output.ToArray();
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Offline) throw new HttpRequestException("Simulated offline state");
        var manifest = JsonSerializer.SerializeToUtf8Bytes(Info);
        var embedded = ManifestMismatch ? "{}"u8.ToArray() : manifest;
        var data = new Dictionary<string, byte[]> {
            [RemotePackage.ManifestName] = manifest,
            [RemotePackage.AssetName] = Zip(new() { [RemotePackage.ManifestName] = embedded, ["payload.zip"] = Zip(Files) }),
            [RemotePackage.TextAssetName] = Zip(new() { [RemotePackage.ManifestName] = embedded, ["payload.zip"] = Zip(Files.Where(f => UpdatePlan.IsText(f.Key)).ToDictionary(f => f.Key, f => f.Value)) })
        };
        foreach (var language in Info.LanguageAssets)
            data[language.Value] = Zip(new() { [RemotePackage.ManifestName] = embedded, ["payload.zip"] = Zip(new() { [language.Key] = Files[language.Key] }) });
        var response = new HttpResponseMessage(Status);
        if (request.RequestUri!.AbsoluteUri == RemotePackage.LatestUrl)
            response.Content = new StringContent(JsonSerializer.Serialize(new {
                draft = Draft, prerelease = Prerelease, tag_name = Tag,
                assets = Missing ? Array.Empty<object>() : data.Select(asset => (object)new { name = asset.Key, size = asset.Value.Length,
                    digest = "sha256:" + (BadDigest ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(asset.Value)).ToLowerInvariant()),
                    browser_download_url = Url ?? "https://github.com/Saphirah/ShroomAndGloom-FanTranslations/releases/download/v0.1.5/" + asset.Key }).ToArray()
            }));
        else {
            var name = Path.GetFileName(request.RequestUri.AbsolutePath); Downloads.Add(name);
            var bytes = data[name]; response.Content = new ByteArrayContent(Truncate ? bytes[..^1] : bytes);
        }
        return Task.FromResult(response);
    }
}
sealed class Sandbox : IDisposable
{
    public string Root { get; } = Path.GetFullPath(Path.Combine("artifacts", "update-check-" + Guid.NewGuid().ToString("N")));
    public Sandbox() {
        Directory.CreateDirectory(Path.Combine(Root, "Shroom and Gloom_Data"));
        File.WriteAllBytes(Path.Combine(Root, "GameAssembly.dll"), "game"u8.ToArray());
        File.WriteAllText(Path.Combine(Root, "Shroom and Gloom.exe"), "NOT AN EXECUTABLE - TEST ONLY");
    }
    public void WriteInstalled(Fixture fixture) {
        foreach (var file in fixture.Files) { var path = ModInstaller.SafePath(Root, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, file.Value); }
        File.WriteAllText(Path.Combine(Root, ModInstaller.RecordName), JsonSerializer.Serialize(new InstallRecord {
            Commit = fixture.Commit, Version = "0.1.5", PackageFiles = fixture.Info.Files, Files = fixture.Info.Files }));
    }
    public void Dispose() {
        var parent = Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar;
        if (!Root.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Root).StartsWith("update-check-")) throw new Exception("Unsafe fixture cleanup");
        Directory.Delete(Root, true);
    }
}
