namespace ShroomGerman.Setup;

internal enum UpdateMode { Full, Text, Current }

internal static class UpdatePlan
{
    internal static bool IsText(string path) => LanguagePack.IsPath(path)
        || path.Equals("BepInEx/plugins/ShroomGerman/translations.de.json", StringComparison.OrdinalIgnoreCase)
        || path.Equals("BepInEx/plugins/ShroomGerman/ui-literals.de.json", StringComparison.OrdinalIgnoreCase);

    internal static UpdateMode Select(string? root, InstallRecord record, PackageInfo next)
    {
        if (root == null || string.IsNullOrEmpty(record.Commit) || record.PackageFiles.Count == 0)
            return UpdateMode.Full;
        var previousRuntime = record.PackageFiles.Where(file => !IsText(file.Key)).ToDictionary(file => file.Key, file => file.Value, StringComparer.OrdinalIgnoreCase);
        var nextRuntime = next.Files.Where(file => !IsText(file.Key)).ToArray();
        // The snapshot detects added/removed runtime files; actual hashes detect
        // missing or modified installed files, even if the recorded commit matches.
        if (previousRuntime.Count != nextRuntime.Length || nextRuntime.Any(file =>
            !previousRuntime.TryGetValue(file.Key, out var hash) || hash != file.Value || !Matches(root, file)))
            return UpdateMode.Full;
        return next.Files.Where(file => IsText(file.Key)).All(file => Matches(root, file))
            ? UpdateMode.Current : UpdateMode.Text;
    }

    private static bool Matches(string root, KeyValuePair<string, string> file)
    {
        var path = PackageState.SafePath(root, file.Key);
        return File.Exists(path) && PackageState.Hash(path).Equals(file.Value, StringComparison.OrdinalIgnoreCase);
    }
}
