using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace ShroomGerman;

internal static class FontFallbacks
{
    private static bool ready;
    private static readonly List<TMP_FontAsset> retained = new();

    internal static void Ensure()
    {
        if (ready || TMP_Settings.instance == null) return;
        ready = true;
        var bundled = new Dictionary<string, string> {
            ["Noto Sans"] = "NotoSans-Regular.ttf", ["Noto Sans Thai"] = "NotoSansThai-Regular.ttf",
            ["Noto Sans CJK SC"] = "NotoSansCJKsc-Regular.otf"
        };
        var global = TMP_Settings.fallbackFontAssets;
        if (global == null) TMP_Settings.instance.m_fallbackFontAssets = global = new();
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Font).TypeHandle);
        foreach (var pair in bundled)
        {
            try
            {
                var path = Path.Combine(Plugin.CatalogFolder, "fonts", pair.Value);
                if (!File.Exists(path)) throw new FileNotFoundException("Bundled font missing", path);
                // Reproduce Font(path) using its retained native initializer:
                // managed constructors are stripped in this IL2CPP player.
                // File-backed data is required by TextCore; Windows system
                // font handles alone cannot be used as a dynamic TMP source.
                var font = new Font(Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(Il2CppInterop.Runtime.Il2CppClassPointerStore<Font>.NativeClassPtr));
                Font.Internal_CreateFontFromPath(font, path);
                var asset = TMP_FontAsset.CreateFontAsset(font, 48, 5, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (asset == null) throw new InvalidOperationException("TextCore could not load the font file.");
                var probes = pair.Key == "Noto Sans Thai" ? "ก" : pair.Key == "Noto Sans CJK SC" ? "中한" : "Ωїế";
                foreach (var character in probes)
                    if (!asset.HasCharacter(character, false, true)) throw new InvalidOperationException("Could not rasterize required glyph U+" + ((int)character).ToString("X4"));
                asset.name = "FanTranslations " + pair.Key;
                global.Add(asset); retained.Add(asset);
            }
            catch (Exception e) { Plugin.Output.LogWarning("Font fallback " + pair.Key + ": " + e.Message); }
        }
        Plugin.Output.LogInfo($"Font fallbacks loaded: {retained.Count}/{bundled.Count}; original game fonts remain primary.");
    }
}
