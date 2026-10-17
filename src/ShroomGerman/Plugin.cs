using System.Globalization;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using ShroomGerman.Setup;
using GameLocale = UnityEngine.Localization.Locale;

namespace ShroomGerman;

// Keep the original GUID and assembly path so existing installations upgrade safely.
[BepInPlugin("de.shroomandgloom.german", "Shroom and Gloom Fan Translations", "0.2.0")]
public sealed class Plugin : BasePlugin
{
    internal static ManualLogSource Output = null!;
    internal static Dictionary<string, LanguagePack> Languages = new(StringComparer.OrdinalIgnoreCase);
    internal static Dictionary<string, List<CatalogEntry>> Tables = new();
    internal static Task<bool>? LanguageUpdate;
    internal static string CatalogFolder = "";
    internal static string PreferredLocale = "auto";
    internal static string ActiveCode = "";
    internal static ConfigEntry<string> LastLocale = null!;
    internal static bool Ready;
    internal static int Generation;
    internal static LanguagePack CurrentPack => Languages.TryGetValue(ActiveCode, out var pack) ? pack : Languages["en"];

    internal static void LoadPacks()
    {
        var packs = new Dictionary<string, LanguagePack>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(CatalogFolder, "languages"), "*.json"))
        {
            var pack = LanguagePack.Parse(file, File.ReadAllBytes(file));
            if (!packs.TryAdd(pack.Locale, pack)) throw new InvalidDataException("Duplicate language: " + pack.Locale);
        }
        if (!packs.ContainsKey("en")) throw new InvalidDataException("English fallback pack is missing.");
        Languages = packs; Generation++;
        Tables = CurrentPack.Tables;
    }

    public override void Load()
    {
        Output = Log;
        try
        {
            CatalogFolder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!;
            var compatibility = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(CatalogFolder, "compatibility.json")))!;
            using (var file = File.OpenRead(Path.Combine(Paths.GameRootPath, "GameAssembly.dll")))
            using (var sha = SHA256.Create())
                if (Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant() != compatibility["gameAssemblySha256"])
                    throw new InvalidDataException("Unsupported game build. Please update Fan Translations.");
            LoadPacks();
            var oldConfig = File.Exists(Config.ConfigFilePath) ? File.ReadAllText(Config.ConfigFilePath) : "";
            var legacyGerman = System.Text.RegularExpressions.Regex.IsMatch(oldConfig, @"DeutschBeimStart\s*=\s*true", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            PreferredLocale = Config.Bind("Language", "StartupLocale", "auto",
                "auto preserves the game selection. Set a locale code (for example de or fr) to choose a startup language.").Value;
            LastLocale = Config.Bind("Language", "LastLocale", legacyGerman ? "de" : "",
                "Last language selected in the game's settings.");
            new Harmony("de.shroomandgloom.german").PatchAll(typeof(Plugin).Assembly);
            AddComponent<TranslationPump>();
            Log.LogInfo($"Fan Translations ready: {Languages.Count} language packs.");
            if (Config.Bind("Aktualisierung", "SprachpaketBeimStart", true,
                "Check published language packs in the background on startup. Offline uses installed translations.").Value)
            {
                var root = Paths.GameRootPath;
                LanguageUpdate = Task.Run(async () => {
                    try { return await LanguageUpdates.RunAsync(root, message => Log.LogInfo(message)); }
                    catch (Exception e) { Log.LogInfo("Language update skipped: " + e.Message); return false; }
                });
            }
        }
        catch (Exception e) { Log.LogError("Fan Translations could not load: " + e); }
    }

    internal static bool IsSupported(GameLocale? locale) => locale != null && Languages.ContainsKey(locale.Identifier.Code);
    internal static void EnsureLocales()
    {
        var provider = LocalizationSettings.AvailableLocales;
        if (provider == null) return;
        foreach (var pack in Languages.Values)
        {
            var locale = provider.GetLocale(pack.Locale);
            if (locale == null) { locale = GameLocale.CreateLocale(pack.Locale); provider.AddLocale(locale); }
            locale.LocaleName = pack.NativeName;
        }
    }
    internal static bool Activate(GameLocale? locale)
    {
        if (!IsSupported(locale)) return false;
        var pack = Languages[locale!.Identifier.Code];
        var changed = ActiveCode != pack.Locale;
        ActiveCode = pack.Locale;
        Tables = pack.Tables;
        if (changed) TextPresentation.Load(CatalogFolder);
        return FanTables.Apply(locale, Tables);
    }
}

[HarmonyPatch(typeof(LocalizationHelpers), nameof(LocalizationHelpers.IsLocaleSupported))]
internal static class SupportedLanguage
{
    static bool Prefix(GameLocale __0, ref bool __result) { if (!Plugin.IsSupported(__0)) return true; __result = true; return false; }
}
[HarmonyPatch(typeof(LocalizationHelpers), nameof(LocalizationHelpers.IsLocaleSelectable))]
internal static class SelectableLanguage
{
    static bool Prefix(GameLocale __0, ref bool __result) { if (!Plugin.IsSupported(__0)) return true; __result = true; return false; }
}
[HarmonyPatch(typeof(LocalizationHelpers), nameof(LocalizationHelpers.GetSelectableLocales))]
internal static class LanguageList
{
    static void Postfix(ref Il2CppSystem.Collections.Generic.List<GameLocale> __result)
    {
        if (__result == null) return;
        Plugin.EnsureLocales();
        foreach (var pack in Plugin.Languages.Values)
        {
            var locale = LocalizationSettings.AvailableLocales.GetLocale(pack.Locale);
            var exists = false;
            for (var i = 0; i < __result.Count; i++) if (__result[i].Identifier.Code == pack.Locale) { exists = true; break; }
            if (!exists && locale != null) __result.Add(locale);
        }
    }
}
[HarmonyPatch(typeof(LocalizationHelpers), nameof(LocalizationHelpers.DetermineStartingLocale))]
internal static class StartingLanguage
{
    static void Postfix(ref GameLocale __result)
    {
        Plugin.EnsureLocales();
        if (Plugin.PreferredLocale.Equals("auto", StringComparison.OrdinalIgnoreCase)) return;
        var locale = LocalizationSettings.AvailableLocales.GetLocale(Plugin.PreferredLocale);
        if (Plugin.IsSupported(locale)) __result = locale;
    }
}
[HarmonyPatch(typeof(LocalizationSettings), "set_SelectedLocale")]
internal static class BeforeLanguageChange
{
    static void Prefix(GameLocale __0) {
        if (!Plugin.Ready) return; // Locale initialization must finish before consulting its database cache.
        try {
            Plugin.Activate(__0);
            if (Plugin.Ready && Plugin.IsSupported(__0)) Plugin.LastLocale.Value = __0.Identifier.Code;
        } catch (Exception e) { Plugin.Output.LogWarning("Language preparation: " + e.Message); }
    }
}

public sealed class TranslationPump : MonoBehaviour
{
    public TranslationPump(IntPtr pointer) : base(pointer) { }
    private float nextCheck, nextLayoutCheck;
    private string lastLocale = "";
    private int lastGeneration;
    private bool selected;
    public void LateUpdate()
    {
        if (Time.unscaledTime < nextLayoutCheck) return;
        nextLayoutCheck = Time.unscaledTime + .25f;
        TextPresentation.Refresh();
    }
    public void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + .5f;
        try
        {
            if (Plugin.LanguageUpdate is { IsCompleted: true } update)
            {
                Plugin.LanguageUpdate = null;
                if (update.GetAwaiter().GetResult()) { Plugin.LoadPacks(); TextPresentation.Load(Plugin.CatalogFolder); }
            }
            if (!LocalizationSettings.InitializationOperation.IsDone) return;
            Plugin.EnsureLocales(); FontFallbacks.Ensure();
            if (!selected)
            {
                selected = true;
                var desired = Plugin.PreferredLocale.Equals("auto", StringComparison.OrdinalIgnoreCase) ? Plugin.LastLocale.Value : Plugin.PreferredLocale;
                if (!string.IsNullOrEmpty(desired))
                {
                    var preferred = LocalizationSettings.AvailableLocales.GetLocale(desired);
                    if (Plugin.IsSupported(preferred)) LocalizationSettings.SelectedLocale = preferred;
                }
                Plugin.Ready = true;
            }
            var locale = LocalizationSettings.SelectedLocale;
            if (!Plugin.IsSupported(locale)) return;
            if (!Plugin.Activate(locale)) return;
            if (lastLocale != locale.Identifier.Code || lastGeneration != Plugin.Generation)
            {
                lastLocale = locale.Identifier.Code; lastGeneration = Plugin.Generation;
                TextPresentation.Load(Plugin.CatalogFolder);
                LocalizationHelpers.OnLanguageChange?.Invoke();
                Plugin.Output.LogInfo($"Language active: {lastLocale}; {Plugin.Tables.Count} tables.");
            }
        }
        catch (Exception e) { Plugin.Output.LogWarning("Language integration will retry: " + e.Message); nextCheck = Time.unscaledTime + 5f; }
    }
}
