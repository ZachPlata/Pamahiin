using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UIElements;

public static class PamahiinUIThemeSetup
{
    private const string FontPath = "Assets/Fonts/PressStart2P-Regular.ttf";
    private const string ThemeUssPath = "Assets/Resources/UI/PamahiinTheme.uss";
    private const string RuntimeThemeTssPath = "Assets/Resources/UI/PamahiinRuntimeTheme.tss";
    private const string RuntimePanelSettingsPath = "Assets/Resources/UI/PamahiinPanelSettings.asset";
    private const string ScenePanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
    private const string ExorcismDialPath = "Assets/Resources/UI/Sprites/ExorcismDial.png";
    private const string TmpFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/PressStart2P-Regular SDF.asset";
    private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    [MenuItem("Pamahiin/UI/Apply Theme Assets & Font Setup")]
    public static void ApplyAllAndSave()
    {
        GenerateExorcismDialSprite();
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        var themeSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(RuntimeThemeTssPath);
        if (themeSheet == null)
        {
            throw new InvalidOperationException($"Failed to load ThemeStyleSheet at '{RuntimeThemeTssPath}'.");
        }

        EnsureRuntimePanelSettings(themeSheet);
        UpdateScenePanelSettings(themeSheet);
        EnsureTmpFontAsset();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[PamahiinUIThemeSetup] Successfully generated/updated all 16-bit UI theme assets.");
    }

    private static void GenerateExorcismDialSprite()
    {
        string dir = Path.GetDirectoryName(ExorcismDialPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        const int size = 96;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];

        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 outline = new Color32(10, 7, 12, 255);
        Color32 brassHi = new Color32(232, 198, 118, 255);
        Color32 brassMid = new Color32(178, 140, 72, 255);
        Color32 brassLo = new Color32(94, 68, 34, 255);
        Color32 grooveBg = new Color32(14, 10, 14, 244);
        Color32 crimsonHi = new Color32(182, 42, 54, 255);
        Color32 crimsonLo = new Color32(92, 18, 26, 255);
        Color32 voidCenter = new Color32(20, 10, 14, 232);
        Color32 runeGlyph = new Color32(108, 28, 36, 185);
        Color32 tickBright = new Color32(244, 214, 138, 255);
        Color32 tickDim = new Color32(138, 106, 56, 255);

        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);
                float absDx = Mathf.Abs(dx);
                float absDy = Mathf.Abs(dy);

                Color32 c = clear;

                if (dist <= 47.4f)
                {
                    // Outer dark silhouette
                    if (dist >= 45.6f)
                    {
                        c = outline;
                    }
                    // Outer beveled brass bezel
                    else if (dist >= 42.2f)
                    {
                        bool topLeftLight = (dy - dx) > 0.5f; // y-up in Texture2D: +y is top, -x is left
                        if (dist >= 44.3f)
                        {
                            c = (-dx + dy) > 0f ? brassHi : brassLo;
                        }
                        else if (dist < 43.1f)
                        {
                            c = outline;
                        }
                        else
                        {
                            c = (-dx + dy) > 8f ? brassHi : (topLeftLight ? brassMid : brassLo);
                        }
                    }
                    // Ritual needle/target groove
                    else if (dist >= 32.5f)
                    {
                        c = grooveBg;

                        // Cardinal pixel ticks (0, 90, 180, 270)
                        bool cardinal = (absDx <= 1.2f || absDy <= 1.2f) && dist >= 34.5f;
                        // Intercardinal ticks (45 deg)
                        bool intercardinal = Mathf.Abs(absDx - absDy) <= 1.1f && dist >= 36.5f;
                        // Subtle 30-degree notches
                        float deg = Mathf.Repeat(angle * Mathf.Rad2Deg, 30f);
                        bool minorTick = (deg < 2.2f || deg > 27.8f) && dist >= 39.5f;

                        if (cardinal)
                            c = tickBright;
                        else if (intercardinal)
                            c = brassMid;
                        else if (minorTick)
                            c = tickDim;
                    }
                    // Inner crimson-and-brass sanctum ring
                    else if (dist >= 28.4f)
                    {
                        if (dist >= 31.3f || dist < 29.3f)
                            c = outline;
                        else if (dist >= 30.3f)
                            c = (-dx + dy) > 0f ? crimsonHi : crimsonLo;
                        else
                            c = (-dx + dy) > 0f ? brassMid : brassLo;
                    }
                    // Inner dark void with subtle pixel octagram cross lines
                    else
                    {
                        c = voidCenter;
                        bool crossAxis = (absDx <= 0.75f || absDy <= 0.75f) && dist >= 10f && dist <= 26f;
                        bool diagAxis = Mathf.Abs(absDx - absDy) <= 0.85f && dist >= 14f && dist <= 24f;
                        bool innerOctRing = dist >= 22.8f && dist <= 24.0f;
                        if (crossAxis || diagAxis || innerOctRing)
                        {
                            c = runeGlyph;
                        }
                    }
                }

                pixels[y * size + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        File.WriteAllBytes(ExorcismDialPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(ExorcismDialPath, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(ExorcismDialPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spritePixelsPerUnit = 32f;
            settings.alphaIsTransparency = true;
            importer.SetTextureSettings(settings);

            importer.SaveAndReimport();
        }
    }

    private static void EnsureRuntimePanelSettings(ThemeStyleSheet themeSheet)
    {
        var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(RuntimePanelSettingsPath);
        if (ps == null)
        {
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            ConfigurePanelSettings(ps, themeSheet);
            AssetDatabase.CreateAsset(ps, RuntimePanelSettingsPath);
        }
        else
        {
            ConfigurePanelSettings(ps, themeSheet);
            EditorUtility.SetDirty(ps);
        }
    }

    private static void UpdateScenePanelSettings(ThemeStyleSheet themeSheet)
    {
        var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(ScenePanelSettingsPath);
        if (ps == null)
        {
            throw new InvalidOperationException($"Expected scene PanelSettings at '{ScenePanelSettingsPath}'.");
        }

        ConfigurePanelSettings(ps, themeSheet);
        EditorUtility.SetDirty(ps);
    }

    private static void ConfigurePanelSettings(PanelSettings ps, ThemeStyleSheet themeSheet)
    {
        ps.themeStyleSheet = themeSheet;
        ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        ps.referenceResolution = new Vector2Int(1200, 800);
        ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        ps.match = 0f;
        ps.referenceDpi = 96f;
        ps.fallbackDpi = 96f;
    }

    private static void EnsureTmpFontAsset()
    {
        var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (sourceFont == null)
        {
            throw new InvalidOperationException($"Source pixel font not found at '{FontPath}'.");
        }

        string dir = Path.GetDirectoryName(TmpFontPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TmpFontPath) != null)
        {
            AssetDatabase.DeleteAsset(TmpFontPath);
        }

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            32,
            4,
            GlyphRenderMode.SDFAA,
            512,
            512,
            AtlasPopulationMode.Dynamic,
            true);

        if (fontAsset == null)
        {
            throw new InvalidOperationException("TMP_FontAsset.CreateFontAsset returned null.");
        }

        fontAsset.name = "PressStart2P-Regular SDF";
        AssetDatabase.CreateAsset(fontAsset, TmpFontPath);

        if (fontAsset.atlasTextures != null)
        {
            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                Texture2D atlas = fontAsset.atlasTextures[i];
                if (atlas != null)
                {
                    atlas.name = $"{fontAsset.name} Atlas {i}";
                    AssetDatabase.AddObjectToAsset(atlas, fontAsset);
                }
            }
        }

        if (fontAsset.material != null)
        {
            fontAsset.material.name = $"{fontAsset.name} Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        // Pre-seed printable ASCII and allowed UI glyphs
        const string initialChars =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "\u25B6\u25C0\u25B2\u25BC\u2014\u00B0\u2022\u2190\u2192\u2026\u2605";
        fontAsset.TryAddCharacters(initialChars, out _);

        EditorUtility.SetDirty(fontAsset);

        // Assign as default TMP Font Asset in TMP Settings
        var tmpSettingsObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TmpSettingsPath);
        if (tmpSettingsObj != null)
        {
            var so = new SerializedObject(tmpSettingsObj);
            var defaultProp = so.FindProperty("m_defaultFontAsset");
            if (defaultProp != null)
            {
                defaultProp.objectReferenceValue = fontAsset;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(tmpSettingsObj);
            }
        }
    }
}
