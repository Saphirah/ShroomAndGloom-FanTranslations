using System.Globalization;
using ShroomGerman.Setup;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using GameLocale = UnityEngine.Localization.Locale;

namespace ShroomGerman;

internal static class FanTables
{
    private static readonly Dictionary<string, StringTable> templates = new();
    private static readonly Dictionary<string, (StringTable Table, int Generation)> tables = new();
    private static readonly HashSet<string> stages = new();
    private static void Trace(string stage) { if (stages.Add(stage)) Plugin.Output.LogDebug("Table preparation: " + stage); }

    internal static bool Apply(GameLocale locale, Dictionary<string, List<CatalogEntry>> translated)
    {
        var database = LocalizationSettings.StringDatabase;
        var english = LocalizationSettings.AvailableLocales.GetLocale("en");
        if (english == null) return false;
        var templatesReady = true;
        foreach (var collection in translated.Keys)
        {
            if (templates.ContainsKey(collection)) continue;
            Trace("request English " + collection);
            var operation = database.GetTableAsync(collection, english);
            if (!operation.IsDone || operation.Result == null) { templatesReady = false; continue; }
            // Keep the original English asset alive when its database entry is
            // replaced by our overlay below.
            Trace("retain English " + collection);
            Addressables.ResourceManager.Acquire(operation);
            templates[collection] = operation.Result;
        }
        if (!templatesReady) return false;
        var current = true;
        foreach (var collection in translated.Keys)
        {
            if (!tables.TryGetValue(locale.Identifier.Code + "/" + collection, out var previous)
                || previous.Table == null || previous.Generation != Plugin.Generation || !database.IsTableLoaded(collection, locale))
            { current = false; break; }
            var operation = database.GetTableAsync(collection, locale);
            if (!operation.IsDone || operation.Result?.Pointer != previous.Table.Pointer) { current = false; break; }
        }
        if (current) return true;
        // Use Unity's native cache reset. Marshalling its value-tuple dictionary
        // keys through IL2CPP can corrupt the managed runtime. Reset also removes
        // failed requests for locales without shipped Addressables tables.
        Trace("reset database cache");
        database.ResetState();
        foreach (var pair in translated)
        {
            var key = locale.Identifier.Code + "/" + pair.Key;
            Trace("clone " + key);
            var known = tables.TryGetValue(key, out var previous);
            if (known && previous.Table == null) known = false;
            var table = (known ? previous.Table : UnityEngine.Object.Instantiate((UnityEngine.Object)templates[pair.Key]).Cast<StringTable>())
                ?? throw new InvalidOperationException("Could not create language table.");
            table.name = pair.Key + "_fan_" + locale.Identifier.Code;
            table.LocaleIdentifier = locale.Identifier;
            table.SharedData = templates[pair.Key].SharedData;
            foreach (var row in pair.Value)
            {
                var id = long.Parse(row.Id, CultureInfo.InvariantCulture);
                var entry = table.GetEntry(id);
                if (entry == null) table.AddEntry(id, row.Text); else entry.Value = row.Text;
            }
            Trace("register " + key);
            var ready = Addressables.ResourceManager.CreateCompletedOperation(table, (string)null!);
            database.RegisterCompletedTableOperation(ready);
            tables[key] = (table, Plugin.Generation);
        }
        return true;
    }

}
