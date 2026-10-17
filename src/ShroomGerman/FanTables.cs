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

    internal static bool Apply(GameLocale locale, Dictionary<string, List<CatalogEntry>> translated)
    {
        var database = LocalizationSettings.StringDatabase;
        var english = LocalizationSettings.AvailableLocales.GetLocale("en");
        if (english == null) return false;
        var templatesReady = true;
        foreach (var collection in translated.Keys)
        {
            if (templates.ContainsKey(collection)) continue;
            var operation = database.GetTableAsync(collection, english);
            if (!operation.IsDone || operation.Result == null) { templatesReady = false; continue; }
            // Keep the original English asset alive when its database entry is
            // replaced by our overlay below.
            Addressables.ResourceManager.Acquire(operation);
            templates[collection] = operation.Result;
        }
        if (!templatesReady) return false;
        foreach (var pair in translated)
        {
            var key = locale.Identifier.Code + "/" + pair.Key;
            var known = tables.TryGetValue(key, out var previous);
            if (known && previous.Table == null) known = false;
            if (known && previous.Generation == Plugin.Generation && database.IsTableLoaded(pair.Key, locale))
            {
                var operation = database.GetTableAsync(pair.Key, locale);
                if (operation.IsDone && operation.Result?.Pointer == previous.Table!.Pointer) continue;
            }
            var table = (known ? previous.Table : UnityEngine.Object.Instantiate((UnityEngine.Object)templates[pair.Key]).Cast<StringTable>())
                ?? throw new InvalidOperationException("Could not create language table.");
            table.name = pair.Key + "_fan_" + locale.Identifier.Code;
            table.LocaleIdentifier = locale.Identifier;
            table.SharedData = templates[pair.Key].SharedData;
            RemovePrevious(database, locale, pair.Key, table);
            foreach (var row in pair.Value)
            {
                var id = long.Parse(row.Id, CultureInfo.InvariantCulture);
                var entry = table.GetEntry(id);
                if (entry == null) table.AddEntry(id, row.Text); else entry.Value = row.Text;
            }
            var ready = Addressables.ResourceManager.CreateCompletedOperation(table, (string)null!);
            database.RegisterCompletedTableOperation(ready);
            tables[key] = (table, Plugin.Generation);
        }
        return true;
    }

    private static void RemovePrevious(LocalizedStringDatabase database, GameLocale locale, string collection, StringTable replacement)
    {
        // Unity's RegisterCompletedTableOperation intentionally refuses to replace
        // an existing name/guid entry. Release it first, including failed requests
        // (ReleaseTable alone leaves null-result operations cached).
        database.ReleaseTable(collection, locale);
        var identifiers = new List<UnityEngine.Localization.LocaleIdentifier> { locale.Identifier };
        if (LocalizationSettings.SelectedLocale?.Identifier.Code == locale.Identifier.Code)
            identifiers.Add(new UnityEngine.Localization.LocaleIdentifier("selected locale placeholder"));
        foreach (var identifier in identifiers)
        foreach (var name in new[] { collection, TableReference.StringFromGuid(replacement.SharedData.TableCollectionNameGuid) })
        {
            var key = new Il2CppSystem.ValueTuple<UnityEngine.Localization.LocaleIdentifier, string>(identifier, name);
            if (!database.TableOperations.TryGetValue(key, out var old)) continue;
            database.TableOperations.Remove(key);
            if (old.IsValid()) Addressables.ResourceManager.Release(old);
        }
    }
}
