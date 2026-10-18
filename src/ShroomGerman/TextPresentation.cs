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
    private static HashSet<string> statLabels = new(StringComparer.OrdinalIgnoreCase);
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
        var data = new Dictionary<string, string>(Plugin.CurrentPack.Literals, StringComparer.OrdinalIgnoreCase);
        // Existing direct labels may already contain their old German value.
        foreach (var previous in literals)
            if (data.TryGetValue(previous.Key, out var updated) && updated != previous.Value)
                data.TryAdd(previous.Value, updated);
        literals = new(data, StringComparer.OrdinalIgnoreCase);
        menuLabels.Clear();
        foreach (var row in Plugin.Tables.GetValueOrDefault("UI", new()))
            if (row.Key != null && (row.Key.StartsWith("PAUSE_MENU_") && row.Key != "PAUSE_MENU_TITLE"
                || row.Key is "TITLE_SCREEN_CONTINUE" or "TITLE_SCREEN_NEW_RUN")) menuLabels.Add(row.Text.Trim());
        foreach (var key in new[] { "CONTINUE", "SETTINGS", "CREDITS", "MAIN MENU", "QUIT", "PLAY", "START", "RESTART", "BACK" })
            if (data.TryGetValue(key, out var value)) menuLabels.Add(value.Trim());
        cardClasses = new(Plugin.Tables["Card"].Where(row => row.Key?.StartsWith("CLASS_", StringComparison.Ordinal) == true)
            .Select(row => row.Text.Trim()), StringComparer.OrdinalIgnoreCase);
        statLabels = new(Plugin.Tables["UI"].Where(row => row.Key?.StartsWith("ROUND_UP_STAT_", StringComparison.Ordinal) == true)
            .Select(row => row.Text.Trim()), StringComparer.OrdinalIgnoreCase);
    }

    internal static string Translate(string input)
    {
        if (string.IsNullOrEmpty(input) || !Plugin.IsSupported(LocalizationSettings.SelectedLocale)) return input;
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
            var german = Plugin.IsSupported(LocalizationSettings.SelectedLocale);
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
                var stat = statLabels.Contains(label) ? text.GetComponentInParent<LabelValueStatEntryUI>() : null;
                if (stat?.LabelText?.Pointer != text.Pointer) stat = null;
                if (!inMenu && !isClass && !isDeckTitle && stat == null) return;
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
                var margin = stat != null ? FitStatMargins(text, original.Margin, stat) : FitMargins(text, original.Margin, inMenu);
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

    internal static void FitStat(LabelValueStatEntryUI stat)
    {
        if (stat != null && stat.LabelText != null) Apply(stat.LabelText);
    }

    private static Vector4 FitStatMargins(TMP_Text label, Vector4 original, LabelValueStatEntryUI stat)
    {
        var row = stat.transform.TryCast<RectTransform>();
        var value = stat.ValueText;
        if (row == null || row.rect.width <= 0 || value == null) return original;
        var area = row.rect;
        var parent = row.parent?.TryCast<RectTransform>();
        if (parent != null && parent.rect.width > 0)
        {
            // An auto-sized row can itself exceed its layout column. Respect
            // the immediate grid cell (or enclosing column) as the outer limit.
            var grid = parent.GetComponent<UnityEngine.UI.GridLayoutGroup>();
            var parentWidth = grid != null && grid.cellSize.x > 0 ? grid.cellSize.x : parent.rect.width;
            var parentOrigin = row.InverseTransformPoint(parent.TransformPoint(Vector3.zero));
            var parentEnd = row.InverseTransformPoint(parent.TransformPoint(new Vector3(parentWidth, 0)));
            var widthInRow = Mathf.Abs(parentEnd.x - parentOrigin.x);
            if (widthInRow > 0) area.xMax = area.xMin + Mathf.Min(area.width, widthInRow);
        }
        var gap = area.width * .025f;
        var valueRect = value.rectTransform;
        var origin = row.InverseTransformPoint(valueRect.TransformPoint(Vector3.zero));
        var unit = row.InverseTransformPoint(valueRect.TransformPoint(Vector3.right));
        var scale = Mathf.Abs(unit.x - origin.x);
        if (scale <= .0001f) return original;
        // Measure at the original maximum, so changing digit counts does not
        // repeatedly shrink the reserved width after TMP has auto-sized a value.
        if (!layouts.ContainsKey(value.Pointer))
            layouts[value.Pointer] = new(value.fontSize, value.fontSizeMin, value.fontSizeMax, value.enableAutoSizing,
                value.enableWordWrapping, value.overflowMode, GetAutoContainer(value), value.margin);
        var initial = layouts[value.Pointer];
        var maximum = initial.Auto && initial.Max > 0 ? initial.Max : initial.Size;
        value.fontSizeMax = maximum;
        value.fontSizeMin = Mathf.Max(.01f, maximum * .25f);
        value.enableAutoSizing = true;
        value.enableWordWrapping = false;
        SetAutoContainer(value, false);
        var width = Mathf.Clamp(value.GetPreferredValues(value.text).x * scale + gap, area.width * .12f, area.width * .35f);
        valueRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width / scale);
        var position = row.InverseTransformPoint(valueRect.position);
        position.x = area.xMax - gap - width * (1f - valueRect.pivot.x);
        valueRect.position = row.TransformPoint(position);
        // Label and value are separate TMP objects. Constrain the label to the
        // remaining cell width, even if its serialized rect spans both columns.
        var rect = label.rectTransform;
        var bounds = rect.rect;
        var left = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        foreach (var point in new[] { new Vector3(area.xMin + gap, area.yMin), new Vector3(area.xMin + gap, area.yMax),
            new Vector3(area.xMax - width - gap * 2, area.yMin), new Vector3(area.xMax - width - gap * 2, area.yMax) })
        {
            var x = rect.InverseTransformPoint(row.TransformPoint(point)).x;
            left = Mathf.Min(left, x); right = Mathf.Max(right, x);
        }
        if (right <= left) return original;
        return new Vector4(Mathf.Max(original.x, left - bounds.xMin), original.y,
            Mathf.Max(original.z, bounds.xMax - right), original.w);
    }

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

[HarmonyPatch(typeof(LabelValueStatEntryUI), nameof(LabelValueStatEntryUI.AdjustSpacing))]
internal static class FittedStatRows
{
    static void Postfix(LabelValueStatEntryUI __instance) => TextPresentation.FitStat(__instance);
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
