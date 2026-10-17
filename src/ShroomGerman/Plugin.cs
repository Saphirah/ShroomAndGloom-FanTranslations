using System.Globalization;
using System.Text.Json;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using GameLocale = UnityEngine.Localization.Locale;

namespace ShroomGerman;

[BepInPlugin("de.shroomandgloom.german", "Shroom and Gloom – Deutsch", "0.1.2")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Output = null!;
    internal static Dictionary<string, List<Translation>> Tables = new();
    internal static bool GermanOnStartup;

    public override void Load()
    {
        Output = Log;
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "translations.de.json");
            var compatibility = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "compatibility.json")))!;
            using (var gameAssembly = File.OpenRead(Path.Combine(Paths.GameRootPath, "GameAssembly.dll")))
            using (var sha = SHA256.Create())
            {
                var hash = Convert.ToHexString(sha.ComputeHash(gameAssembly)).ToLowerInvariant();
                if (hash != compatibility["gameAssemblySha256"])
                    throw new InvalidDataException("Spiel wurde aktualisiert. Diese Modversion unterstützt Steam-Build 25442225; bitte die Mod aktualisieren.");
            }
            var rows = JsonSerializer.Deserialize<List<Translation>>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Leere Sprachdatei");
            foreach (var row in rows)
            {
                if (!long.TryParse(row.Id, NumberStyles.None, CultureInfo.InvariantCulture, out _) || row.De is null)
                    throw new InvalidDataException("Ungültiger Übersetzungseintrag");
            }
            Tables = rows.GroupBy(x => x.Table).ToDictionary(g => g.Key, g => g.ToList());
            TextPresentation.Load(Path.GetDirectoryName(path)!);
            GermanOnStartup = Config.Bind("Sprache", "DeutschBeimStart", true,
                "Deutsch beim Spielstart auswählen. Bei false gilt die Sprachauswahl des Spiels.").Value;
            new Harmony("de.shroomandgloom.german").PatchAll(typeof(Plugin).Assembly);
            AddComponent<TranslationPump>();
            Log.LogInfo($"Bereit: {rows.Count} geprüfte Einträge in {Tables.Count} Tabellen. Warte auf das Sprachsystem.");
        }
        catch (Exception e) { Log.LogError($"Deutsche Übersetzung konnte nicht geladen werden: {e}"); }
    }

    internal static bool IsGerman(GameLocale? locale) => locale != null && locale.Identifier.Code == "de";
}

public sealed class Translation
{
    public string Table { get; set; } = "";
    public string Id { get; set; } = "";
    public string? Key { get; set; }
    public string De { get; set; } = "";
}

// Keep the game's language menu and its own selection/persistence flow.
[HarmonyPatch(typeof(LocalizationHelpers), nameof(LocalizationHelpers.IsLocaleSupported))]
internal static class SupportedLanguage
{
    static bool Prefix(GameLocale __0, ref bool __result)
    {
        if (!Plugin.IsGerman(__0)) return true;
        __result = true; return false;
    }
}

[HarmonyPatch(typeof(LocalizationHelpers), nameof(LocalizationHelpers.IsLocaleSelectable))]
internal static class SelectableLanguage
{
    static bool Prefix(GameLocale __0, ref bool __result)
    {
        if (!Plugin.IsGerman(__0)) return true;
        __result = true; return false;
    }
}

[HarmonyPatch(typeof(LocalizationHelpers), nameof(LocalizationHelpers.GetSelectableLocales))]
internal static class LanguageList
{
    static void Postfix(ref Il2CppSystem.Collections.Generic.List<GameLocale> __result)
    {
        if (__result == null) return;
        var german = LocalizationSettings.AvailableLocales.GetLocale("de");
        if (german == null) return;
        for (var i = 0; i < __result.Count; i++)
            if (Plugin.IsGerman(__result[i])) return;
        __result.Add(german);
    }
}

[HarmonyPatch(typeof(LocalizationHelpers), nameof(LocalizationHelpers.DetermineStartingLocale))]
internal static class StartingLanguage
{
    static void Postfix(ref GameLocale __result)
    {
        if (!Plugin.GermanOnStartup) return;
        var german = LocalizationSettings.AvailableLocales.GetLocale("de");
        if (german != null) __result = german;
    }
}

public sealed class TranslationPump : MonoBehaviour
{
    public TranslationPump(IntPtr pointer) : base(pointer) { }
    private bool applied;
    private bool selected;
    private bool refreshed;
    private bool wasGerman;
    private float nextCheck;
    private readonly Dictionary<string, StringTable> patched = new();

    public void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.5f;
        try
        {
            if (!LocalizationSettings.InitializationOperation.IsDone) return;
            var locale = LocalizationSettings.AvailableLocales.GetLocale("de");
            if (locale == null) return;
            // Re-check on language changes and periodically: Addressables may reload table instances.
            if (!applied || Plugin.IsGerman(LocalizationSettings.SelectedLocale))
            {
                var complete = true;
                var count = 0;
                foreach (var (collection, rows) in Plugin.Tables)
                {
                    var operation = LocalizationSettings.StringDatabase.GetTableAsync(collection, locale);
                    if (!operation.IsDone || operation.Result == null) { complete = false; continue; }
                    var table = operation.Result;
                    if (patched.TryGetValue(collection, out var previous) && previous.Pointer == table.Pointer) continue;
                    foreach (var row in rows)
                    {
                        var id = long.Parse(row.Id, CultureInfo.InvariantCulture);
                        var entry = table.GetEntry(id);
                        if (entry == null) entry = table.AddEntry(id, row.De);
                        else entry.Value = row.De;
                        count++;
                    }
                    patched[collection] = table;
                }
                if (count > 0)
                {
                    refreshed = false;
                    Plugin.Output.LogInfo($"Deutsche Tabellen aktualisiert: {count} Einträge; {patched.Count}/{Plugin.Tables.Count} Tabellen.");
                }
                applied = complete && patched.Count == Plugin.Tables.Count;
            }
            if (!applied) return;
            if (!selected)
            {
                selected = true;
                if (Plugin.GermanOnStartup) LocalizationSettings.SelectedLocale = locale;
            }
            var german = Plugin.IsGerman(LocalizationSettings.SelectedLocale);
            if (german && (!refreshed || !wasGerman))
            {
                LocalizationHelpers.OnLanguageChange?.Invoke();
                refreshed = true;
                Plugin.Output.LogInfo("Deutsch aktiv; alle fünf Sprachbereiche stehen bereit.");
            }
            wasGerman = german;
            nextCheck = Time.unscaledTime + (german ? 3f : 0.5f);
        }
        catch (Exception e)
        {
            Plugin.Output.LogError($"Sprachintegration angehalten; Spiel bleibt unverändert spielbar: {e}");
            enabled = false;
        }
    }
}
