using System.Text.Json;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace ShroomGerman;

internal static class TextPresentation
{
    private static Dictionary<string, string> literals = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> pauseLabels = new(StringComparer.OrdinalIgnoreCase) {
        "FORTSETZEN", "PAUSE BEENDEN", "EINSTELLUNGEN", "MITWIRKENDE", "HAUPTMENÜ", "BEENDEN"
    };
    private static readonly Dictionary<IntPtr, OriginalLayout> layouts = new();
    private static bool reported;
    private sealed record OriginalLayout(float Size, float Min, float Max, bool Auto, bool Wrap, TextOverflowModes Overflow, bool Container);

    internal static void Load(string folder)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(folder, "ui-literals.de.json")))!;
        literals = new(data, StringComparer.OrdinalIgnoreCase);
    }

    internal static string Translate(string input)
    {
        if (string.IsNullOrEmpty(input) || !Plugin.IsGerman(LocalizationSettings.SelectedLocale)) return input;
        string Replace(string text)
        {
            var core = text.Trim();
            if (!literals.TryGetValue(core, out var de)) return text;
            // Preserve actual whitespace byte-for-byte; only the visible label changes.
            var leading = text.Length - text.TrimStart().Length;
            var trailing = text.Length - text.TrimEnd().Length;
            return text[..leading] + de + (trailing == 0 ? "" : text[^trailing..]);
        }
        if (!input.Contains('<')) return Replace(input);
        return Regex.Replace(input, @"(?<=>)[^<>]+(?=<)|^[^<>]+(?=<)|(?<=>)[^<>]+$", m => Replace(m.Value));
    }

    internal static void Apply(TMP_Text text)
    {
        try
        {
            if (text == null) return;
            var german = Plugin.IsGerman(LocalizationSettings.SelectedLocale);
            var value = text.text;
            if (german)
            {
                var translated = Translate(value);
                if (translated != value) text.text = translated;
                if (!pauseLabels.Contains(translated.Trim())) return;
                var inMenu = text.GetComponentInParent<UnityEngine.UI.Button>() != null
                    || text.GetComponentInParent<ButtonClick>() != null || text.GetComponentInParent<GooButton>() != null;
                for (var node = text.transform; node != null && !inMenu; node = node.parent)
                    inMenu = node.name.Contains("pause", StringComparison.OrdinalIgnoreCase);
                if (!inMenu) return;
                if (!layouts.ContainsKey(text.Pointer))
                    layouts[text.Pointer] = new(text.fontSize, text.fontSizeMin, text.fontSizeMax, text.enableAutoSizing,
                        text.enableWordWrapping, text.overflowMode, text.TryCast<TextMeshProUGUI>()?.autoSizeTextContainer ?? false);
                var original = layouts[text.Pointer];
                var maximum = original.Auto && original.Max > 0 ? original.Max : original.Size;
                var minimum = Mathf.Min(maximum, Mathf.Max(8f, maximum * 0.35f));
                if (text.enableWordWrapping) text.enableWordWrapping = false;
                if (text.fontSizeMax != maximum) text.fontSizeMax = maximum;
                if (text.fontSizeMin != minimum) text.fontSizeMin = minimum;
                if (!text.enableAutoSizing) text.enableAutoSizing = true;
                if (text.overflowMode != TextOverflowModes.Overflow) text.overflowMode = TextOverflowModes.Overflow;
                var ui = text.TryCast<TextMeshProUGUI>();
                if (ui != null && ui.autoSizeTextContainer) ui.autoSizeTextContainer = false;
            }
            else if (layouts.Remove(text.Pointer, out var original))
            {
                text.enableAutoSizing = original.Auto; text.fontSize = original.Size;
                text.fontSizeMin = original.Min; text.fontSizeMax = original.Max;
                text.enableWordWrapping = original.Wrap; text.overflowMode = original.Overflow;
                var ui = text.TryCast<TextMeshProUGUI>();
                if (ui != null) ui.autoSizeTextContainer = original.Container;
            }
        }
        catch (Exception e)
        {
            if (!reported) { reported = true; Plugin.Output.LogWarning("Textdarstellung: " + e.Message); }
        }
    }

    internal static void Forget(TMP_Text text) => layouts.Remove(text.Pointer);
}

[HarmonyPatch(typeof(LocalizedFallbackText), nameof(LocalizedFallbackText.GetText))]
internal static class FallbackLabels
{
    static void Postfix(ref string __result) => __result = TextPresentation.Translate(__result);
}

[HarmonyPatch(typeof(TMP_Text), "set_text")]
internal static class AssignedLabels
{
    static void Prefix(ref string __0) => __0 = TextPresentation.Translate(__0);
}

[HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string), typeof(bool) })]
internal static class SetLabels
{
    static void Prefix(ref string __0) => __0 = TextPresentation.Translate(__0);
}

[HarmonyPatch(typeof(TextMeshProUGUI), nameof(TextMeshProUGUI.OnEnable))]
internal static class EnabledLabels
{
    static void Postfix(TextMeshProUGUI __instance) => TextPresentation.Apply(__instance);
}

// Covers serialized labels that bypass text setters, before the UI mesh is rebuilt.
[HarmonyPatch(typeof(TextMeshProUGUI), "OnPreRenderCanvas")]
internal static class RenderedLabels
{
    static void Prefix(TextMeshProUGUI __instance) => TextPresentation.Apply(__instance);
}

[HarmonyPatch(typeof(TextMeshProUGUI), "OnDestroy")]
internal static class DestroyedLabels
{
    static void Prefix(TextMeshProUGUI __instance) => TextPresentation.Forget(__instance);
}
