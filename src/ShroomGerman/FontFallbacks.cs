using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using System.Runtime.InteropServices;

namespace ShroomGerman;

internal static class FontFallbacks
{
    private static bool ready;
    private static readonly List<TMP_FontAsset> retained = new();
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "AddFontResourceExW")]
    private static extern int AddFontResourceEx(string path, uint flags, IntPtr reserved);
    internal static void Ensure()
    {
        if (ready || TMP_Settings.instance == null) return;
        ready = true;
        try
        {
        var bundled = new Dictionary<string, string> {
            ["Noto Sans"] = "NotoSans-Regular.ttf", ["Noto Sans Thai"] = "NotoSansThai-Regular.ttf",
            ["Noto Sans CJK SC"] = "NotoSansCJKsc-Regular.otf"
        };
        var registered = new HashSet<string>();
        foreach (var pair in bundled)
        {
            var path = Path.Combine(Plugin.CatalogFolder, "fonts", pair.Value);
            // Private to this process, with no administrator access or permanent
            // font installation. Windows releases the resources when the game exits.
            if (File.Exists(path) && AddFontResourceEx(path, 0x10, IntPtr.Zero) > 0) registered.Add(pair.Key);
        }
        var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
        installed.UnionWith(registered);
        var global = TMP_Settings.fallbackFontAssets;
        if (global == null) TMP_Settings.instance.m_fallbackFontAssets = global = new();
        foreach (var name in new[] { "Noto Sans", "Noto Sans Thai", "Noto Sans CJK SC", "Arial", "Tahoma", "Malgun Gothic", "Microsoft YaHei", "Microsoft JhengHei", "Yu Gothic", "MS Gothic" })
        {
            if (!installed.Contains(name)) continue;
            var font = Font.CreateDynamicFontFromOSFont(name, 48);
            if (font == null) continue;
            var asset = TMP_FontAsset.CreateFontAsset(font, 48, 5, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (asset == null) continue;
            asset.name = "FanTranslations " + name;
            global.Add(asset); retained.Add(asset);
        }
        Plugin.Output.LogInfo($"Font fallbacks ready: {retained.Count} installed system fonts; existing game fonts remain primary.");
        }
        catch (Exception e) { Plugin.Output.LogWarning("Font fallbacks could not initialize; existing game fonts remain active: " + e.Message); }
    }
}
