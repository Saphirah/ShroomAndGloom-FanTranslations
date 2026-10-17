using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ShroomGerman.Setup;

internal sealed class CatalogEntry
{
    [JsonPropertyName("table")] public string Table { get; set; } = "";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

internal sealed class LanguagePack
{
    [JsonPropertyName("locale")] public string Locale { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("nativeName")] public string NativeName { get; set; } = "";
    [JsonPropertyName("quality")] public string Quality { get; set; } = "";
    [JsonPropertyName("entries")] public List<CatalogEntry> Entries { get; set; } = new();
    [JsonPropertyName("literals")] public Dictionary<string, string> Literals { get; set; } = new();
    private Dictionary<string, List<CatalogEntry>>? tables;
    internal Dictionary<string, List<CatalogEntry>> Tables => tables ??= Entries.GroupBy(row => row.Table).ToDictionary(group => group.Key, group => group.ToList());
    internal const string Prefix = "BepInEx/plugins/ShroomGerman/languages/";
    internal static bool IsPath(string path) => path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
        && Regex.IsMatch(path[Prefix.Length..], "^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})?\\.json$");

    internal static LanguagePack Parse(string path, byte[] bytes)
    {
        var pack = JsonSerializer.Deserialize<LanguagePack>(bytes) ?? throw new InvalidDataException("Empty language pack.");
        if (!Regex.IsMatch(pack.Locale, "^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})?$")
            || !Path.GetFileName(path).Equals(pack.Locale + ".json", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(pack.NativeName) || pack.Entries == null || pack.Literals == null)
            throw new InvalidDataException("Invalid language pack identity.");
        var tables = new HashSet<string> { "Card Names", "UI", "Enemy", "World", "Card" };
        var seen = new HashSet<(string, string)>(); var present = new HashSet<string>();
        foreach (var row in pack.Entries)
        {
            if (!tables.Contains(row.Table) || !long.TryParse(row.Id, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                || row.Text == null || !seen.Add((row.Table, row.Id)))
                throw new InvalidDataException("Invalid translated entry.");
            present.Add(row.Table);
        }
        if (!present.SetEquals(tables) || pack.Literals.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null))
            throw new InvalidDataException("Incomplete language pack.");
        return pack;
    }
}
