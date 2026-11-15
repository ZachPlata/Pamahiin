using TMPro;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Runtime helper for resolving Pamahiin's shared 16-bit UI Toolkit PanelSettings,
/// ThemeStyleSheet, and pixel-art fonts across dynamically spawned UI and IMGUI/TMP helpers.
/// </summary>
public static class PamahiinUIThemeUtility
{
    private const string RuntimePanelSettingsResource = "UI/PamahiinPanelSettings";
    private const string RuntimeThemeResource = "UI/PamahiinRuntimeTheme";
    private const string TmpPixelFontResource = "Fonts & Materials/PressStart2P-Regular SDF";

    private static PanelSettings cachedFallbackSettings;
    private static Font cachedPixelFont;

    public static PanelSettings ResolveRuntimePanelSettings(PanelSettings existing = null)
    {
        if (existing != null && existing.themeStyleSheet != null && existing != cachedFallbackSettings)
        {
            return existing;
        }

        var runtimeResource = Resources.Load<PanelSettings>(RuntimePanelSettingsResource);
        if (runtimeResource != null)
        {
            EnsureThemeSheet(runtimeResource);
            return runtimeResource;
        }

        var docs = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
        foreach (var doc in docs)
        {
            if (doc != null && doc.panelSettings != null && doc.panelSettings != cachedFallbackSettings && doc.panelSettings.themeStyleSheet != null)
            {
                return doc.panelSettings;
            }
        }

        var loadedSettings = Resources.FindObjectsOfTypeAll<PanelSettings>();
        foreach (var ps in loadedSettings)
        {
            if (ps != null && ps != cachedFallbackSettings && ps.themeStyleSheet != null)
            {
                return ps;
            }
        }

        if (cachedFallbackSettings == null)
        {
            cachedFallbackSettings = ScriptableObject.CreateInstance<PanelSettings>();
            cachedFallbackSettings.name = "PamahiinFallbackPanelSettings";
            cachedFallbackSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            cachedFallbackSettings.referenceResolution = new Vector2Int(1200, 800);
            cachedFallbackSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            cachedFallbackSettings.match = 0f;
            EnsureThemeSheet(cachedFallbackSettings);
        }

        return cachedFallbackSettings;
    }

    public static ThemeStyleSheet ResolveRuntimeThemeSheet()
    {
        return Resources.Load<ThemeStyleSheet>(RuntimeThemeResource);
    }

    public static TMP_FontAsset ResolvePixelTmpFont()
    {
        var fontAsset = Resources.Load<TMP_FontAsset>(TmpPixelFontResource);
        if (fontAsset != null)
        {
            return fontAsset;
        }

        return TMP_Settings.defaultFontAsset;
    }

    public static Font ResolvePixelFont()
    {
        if (cachedPixelFont != null)
        {
            return cachedPixelFont;
        }

        var tmpFont = ResolvePixelTmpFont();
        if (tmpFont != null && tmpFont.sourceFontFile != null)
        {
            cachedPixelFont = tmpFont.sourceFontFile;
            return cachedPixelFont;
        }

        var loadedFonts = Resources.FindObjectsOfTypeAll<Font>();
        foreach (var font in loadedFonts)
        {
            if (font != null && font.name.Contains("PressStart2P"))
            {
                cachedPixelFont = font;
                return cachedPixelFont;
            }
        }

        return null;
    }

    private static void EnsureThemeSheet(PanelSettings settings)
    {
        if (settings != null && settings.themeStyleSheet == null)
        {
            var theme = ResolveRuntimeThemeSheet();
            if (theme != null)
            {
                settings.themeStyleSheet = theme;
            }
        }
    }
}
