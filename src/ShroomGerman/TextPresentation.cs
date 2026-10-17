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
    private static readonly HashSet<string> menuLabels = new(StringComparer.OrdinalIgnoreCase) {
        "FORTSETZEN", "PAUSE BEENDEN", "EINSTELLUNGEN", "MITWIRKENDE", "HAUPTMENÜ", "BEENDEN",
        "NEUE PARTIE", "SPIELEN", "STARTEN", "NEU STARTEN", "ZURÜCK"
    };
    private static readonly Dictionary<IntPtr, OriginalLayout> layouts = new();
    private static HashSet<string> cardClasses = new(StringComparer.OrdinalIgnoreCase);
    private static bool reported;
    private sealed record OriginalLayout(float Size, float Min, float Max, bool Auto, bool Wrap, TextOverflowModes Overflow, bool Container, Vector4 Margin);

    // Native IL2CPP callbacks can bypass patched lifecycle methods. Discover active
    // labels independently, including labels enabled before the locale was ready.
    internal static void Refresh()
    {
        foreach (var text in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
        {
            Apply(text);
            if (layouts.ContainsKey(text.Pointer) && text.havePropertiesChanged)
                text.ForceMeshUpdate();
        }
    }

    internal static void Load(string folder)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(folder, "ui-literals.de.json")))!;
        literals = new(data, StringComparer.OrdinalIgnoreCase);
        cardClasses = new(Plugin.Tables["Card"].Where(row => row.Key?.StartsWith("CLASS_", StringComparison.Ordinal) == true)
            .Select(row => row.De.Trim()), StringComparer.OrdinalIgnoreCase);
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
                if (string.IsNullOrEmpty(value)) return;
                var translated = Translate(value);
                if (translated != value) text.text = translated;
                var label = Regex.Replace(translated, "<[^>]+>", "").Trim();
                var isClass = false;
                if (cardClasses.Contains(label)) {
                    var gameplay = text.GetComponentInParent<V_GameplayCardInstance>();
                    var foresight = text.GetComponentInParent<V_ForesightCardInstance>();
                    isClass = gameplay?._typeText?.Pointer == text.Pointer || foresight?._typeText?.Pointer == text.Pointer;
                }
                // Some menu labels are siblings of the clickable graphic, rather
                // than children of a Button. Match these exact short labels directly.
                var inMenu = menuLabels.Contains(label);
                var deck = text.GetComponentInParent<DeckDisplay>();
                var isDeckTitle = deck?._titleText?.Pointer == text.Pointer;
                if (!inMenu && !isClass && !isDeckTitle) return;
                var nominal = text.enableAutoSizing && text.fontSizeMax > 0 ? text.fontSizeMax : text.fontSize;
                if (nominal <= 0) return;
                if (!layouts.ContainsKey(text.Pointer))
                    layouts[text.Pointer] = new(text.fontSize, text.fontSizeMin, text.fontSizeMax, text.enableAutoSizing,
                        text.enableWordWrapping, text.overflowMode, GetAutoContainer(text), text.margin);
                var original = layouts[text.Pointer];
                var maximum = original.Auto && original.Max > 0 ? original.Max : original.Size;
                // World-space TMP labels can use sizes below 8. An absolute floor
                // would lock Min to Max and effectively disable shrinking there.
                var minimum = Mathf.Min(maximum, Mathf.Max(0.01f, maximum * 0.25f));
                if (text.enableWordWrapping) text.enableWordWrapping = false;
                if (text.fontSizeMax != maximum) text.fontSizeMax = maximum;
                if (text.fontSizeMin != minimum) text.fontSizeMin = minimum;
                if (!text.enableAutoSizing) text.enableAutoSizing = true;
                if (text.overflowMode != TextOverflowModes.Overflow) text.overflowMode = TextOverflowModes.Overflow;
                if (GetAutoContainer(text)) SetAutoContainer(text, false);
                var margin = FitMargins(text, original.Margin, inMenu);
                if (text.margin != margin) text.margin = margin;
            }
            else if (layouts.Remove(text.Pointer, out var original))
            {
                text.enableAutoSizing = original.Auto; text.fontSize = original.Size;
                text.fontSizeMin = original.Min; text.fontSizeMax = original.Max;
                text.enableWordWrapping = original.Wrap; text.overflowMode = original.Overflow;
                SetAutoContainer(text, original.Container);
                text.margin = original.Margin;
            }
        }
        catch (Exception e)
        {
            if (!reported) { reported = true; Plugin.Output.LogWarning("Textdarstellung: " + e.Message); }
        }
    }

    internal static void Forget(TMP_Text text) => layouts.Remove(text.Pointer);

    private static Vector4 FitMargins(TMP_Text text, Vector4 original, bool menu)
    {
        var rect = text.rectTransform;
        var bounds = rect.rect;
        if (bounds.width <= 0) return original;
        var left = bounds.xMin;
        var right = bounds.xMax;
        if (menu)
        {
            // Account for a text rect wider than its button/mask: TMP otherwise
            // considers the text to fit while the parent clips both ends.
            for (var node = text.transform.parent; node != null; node = node.parent)
            {
                if (node.GetComponent<UnityEngine.UI.Button>() == null
                    && node.GetComponent<UnityEngine.UI.Mask>() == null
                    && node.GetComponent<UnityEngine.UI.RectMask2D>() == null) continue;
                var area = node.TryCast<RectTransform>();
                if (area == null || area.rect.width <= 0) continue;
                var box = area.rect;
                var min = float.PositiveInfinity;
                var max = float.NegativeInfinity;
                foreach (var point in new[] { new Vector3(box.xMin, box.yMin), new Vector3(box.xMin, box.yMax),
                    new Vector3(box.xMax, box.yMin), new Vector3(box.xMax, box.yMax) })
                {
                    var x = rect.InverseTransformPoint(area.TransformPoint(point)).x;
                    min = Mathf.Min(min, x); max = Mathf.Max(max, x);
                }
                left = Mathf.Max(left, min); right = Mathf.Min(right, max);
            }
        }
        if (right <= left) return original;
        var padding = (right - left) * 0.05f;
        return new Vector4(Mathf.Max(original.x, left - bounds.xMin + padding), original.y,
            Mathf.Max(original.z, bounds.xMax - right + padding), original.w);
    }

    private static bool GetAutoContainer(TMP_Text text) => text.TryCast<TextMeshProUGUI>()?.autoSizeTextContainer
        ?? text.TryCast<TextMeshPro>()?.autoSizeTextContainer ?? false;

    private static void SetAutoContainer(TMP_Text text, bool value)
    {
        var ui = text.TryCast<TextMeshProUGUI>();
        if (ui != null) ui.autoSizeTextContainer = value;
        else {
            var world = text.TryCast<TextMeshPro>();
            if (world != null) world.autoSizeTextContainer = value;
        }
    }
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

[HarmonyPatch(typeof(TextMeshPro), nameof(TextMeshPro.OnEnable))]
internal static class EnabledWorldLabels
{
    static void Postfix(TextMeshPro __instance) => TextPresentation.Apply(__instance);
}

[HarmonyPatch(typeof(TextMeshPro), "OnPreRenderObject")]
internal static class RenderedWorldLabels
{
    static void Prefix(TextMeshPro __instance) => TextPresentation.Apply(__instance);
}

[HarmonyPatch(typeof(TextMeshPro), "OnDestroy")]
internal static class DestroyedWorldLabels
{
    static void Prefix(TextMeshPro __instance) => TextPresentation.Forget(__instance);
}
