using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Generates pixel-art sprite sheets for the vertical driving loading backdrop,
/// builds Assets/Scenes/LoadingScene.unity, and registers it in EditorBuildSettings.
/// </summary>
public static class LoadingSceneBuilder
{
    public const string LOADING_SCENE_PATH = "Assets/Scenes/LoadingScene.unity";
    private const string SPRITE_OUTPUT_DIR = "Assets/Resources/Sprites/Loading";

    private const string JEEPNEY_ROOF_PATH = "Assets/Resources/Sprites/BarangayMap/Jeepney/jeepney_roof.png";
    private const string ASPHALT_ROAD_PATH = "Assets/Resources/Sprites/BarangayMap/Exterior/asphalt_road.png";

    private const string ROAD_SHEET_PATH = SPRITE_OUTPUT_DIR + "/road_moving_sheet.png";
    private const string TREES_SHEET_PATH = SPRITE_OUTPUT_DIR + "/trees_waving_sheet.png";
    private const string WIND_SHEET_PATH = SPRITE_OUTPUT_DIR + "/wind_lines_sheet.png";
    private const string GROUND_TILE_PATH = SPRITE_OUTPUT_DIR + "/roadside_ground_tile.png";
    private const string HEADLIGHT_CONE_PATH = SPRITE_OUTPUT_DIR + "/jeepney_headlight_cone.png";
    private const string TAILLIGHT_GLOW_PATH = SPRITE_OUTPUT_DIR + "/jeepney_taillight_glow.png";

    private const int ROAD_FRAMES = 16;
    private const int TREE_COLS = 4;
    private const int TREE_ROWS = 2;
    private const int WIND_FRAMES = 8;
    private const float PIXELS_PER_UNIT = 64f;

    [MenuItem("Pamahiin/Build Loading Scene (Jeepney Road)")]
    public static void BuildLoadingSceneMenu()
    {
        BuildAllAssetsAndScene(promptSaveOpenScenes: true);
    }

    public static void BuildAllAssetsAndScene(bool promptSaveOpenScenes = false)
    {
        if (promptSaveOpenScenes && !Application.isBatchMode)
        {
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        EnsureDirectories();
        GenerateAllLoadingTextures();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        ConfigureAllSpriteImports();
        BuildLoadingSceneAsset();
        EnsureSceneInBuildSettings(LOADING_SCENE_PATH);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("[LoadingSceneBuilder] Generated sprite sheets and built Assets/Scenes/LoadingScene.unity successfully.");
    }

    private static void EnsureDirectories()
    {
        Directory.CreateDirectory(SPRITE_OUTPUT_DIR);
        Directory.CreateDirectory("Assets/Scenes");
    }

    private static void GenerateAllLoadingTextures()
    {
        GenerateRoadMovingSheet();
        GenerateTreesWavingSheet();
        GenerateWindLinesSheet();
        GenerateRoadsideGroundTile();
        GenerateHeadlightCone();
        GenerateTaillightGlow();
    }

    private static void GenerateRoadMovingSheet()
    {
        byte[] sourceBytes = File.Exists(ASPHALT_ROAD_PATH) ? File.ReadAllBytes(ASPHALT_ROAD_PATH) : null;
        Texture2D src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        int cellW = 128;
        int cellH = 128;

        if (sourceBytes != null && src.LoadImage(sourceBytes))
        {
            cellW = src.width;
            cellH = src.height;
        }
        else
        {
            src.Reinitialize(cellW, cellH);
            Color32[] fallback = new Color32[cellW * cellH];
            for (int y = 0; y < cellH; y++)
            {
                for (int x = 0; x < cellW; x++)
                {
                    bool sidewalk = x < 16 || x >= cellW - 16;
                    bool dash = Mathf.Abs(x - cellW / 2) <= 2 && (y % 32) < 18;
                    Color32 c = sidewalk ? new Color32(48, 48, 52, 255)
                        : dash ? new Color32(138, 108, 48, 255)
                        : new Color32(20, 21, 25, 255);
                    fallback[y * cellW + x] = c;
                }
            }
            src.SetPixels32(fallback);
            src.Apply();
        }

        Color32[] srcPixels = src.GetPixels32();
        UnityEngine.Object.DestroyImmediate(src);

        int outW = cellW * ROAD_FRAMES;
        int outH = cellH;
        Texture2D sheet = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
        Color32[] dstPixels = new Color32[outW * outH];

        for (int f = 0; f < ROAD_FRAMES; f++)
        {
            // Moving pattern downward by (f * cellH / ROAD_FRAMES) in Unity's bottom-up pixel rows:
            // destination row y samples source row (y + shift) mod cellH so features descend down screen.
            int shift = (f * cellH) / ROAD_FRAMES;
            for (int y = 0; y < cellH; y++)
            {
                int srcY = (y + shift) % cellH;
                for (int x = 0; x < cellW; x++)
                {
                    Color32 px = srcPixels[srcY * cellW + x];
                    dstPixels[y * outW + (f * cellW + x)] = px;
                }
            }
        }

        sheet.SetPixels32(dstPixels);
        sheet.Apply();
        File.WriteAllBytes(ROAD_SHEET_PATH, sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
    }

    private static void GenerateTreesWavingSheet()
    {
        const int cell = 64;
        int outW = cell * TREE_COLS;
        int outH = cell * TREE_ROWS;
        Texture2D sheet = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[outW * outH];

        // Row 0 in Unity (bottom pixels, y = 0..63) -> Variant B: Palm tree cluster
        // Row 1 in Unity (top pixels, y = 64..127) -> Variant A: Leafy Narra/Mango canopy
        float[] swayOffsets = { -1.8f, 0f, 1.8f, 0f };
        float[] swayRotations = { -0.09f, 0f, 0.09f, 0f };

        for (int col = 0; col < TREE_COLS; col++)
        {
            PaintLeafyCanopyCell(pixels, outW, col * cell, cell, swayOffsets[col], col);
            PaintPalmTreeCell(pixels, outW, col * cell, 0, swayRotations[col], col);
        }

        sheet.SetPixels32(pixels);
        sheet.Apply();
        File.WriteAllBytes(TREES_SHEET_PATH, sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
    }

    private static void PaintLeafyCanopyCell(
        Color32[] buffer, int stride, int originX, int originY, float swayX, int frameIdx)
    {
        const int size = 64;
        float cx = 32f;
        float cy = 32f;

        Color32 shadow = new Color32(4, 7, 6, 110);
        Color32 outline = new Color32(8, 15, 10, 255);
        Color32 deepGreen = new Color32(14, 31, 19, 255);
        Color32 midGreen = new Color32(23, 50, 29, 255);
        Color32 highlightGreen = new Color32(38, 74, 45, 255);
        Color32 brightTip = new Color32(55, 96, 59, 255);

        Vector2[] lobes =
        {
            new Vector2(-10f, 8f),
            new Vector2(9f, 9f),
            new Vector2(-12f, -6f),
            new Vector2(11f, -5f),
            new Vector2(0f, 13f),
            new Vector2(-1f, -11f),
            new Vector2(0f, 1f)
        };
        float[] lobeRadii = { 12.5f, 12f, 12f, 12.5f, 11.5f, 11.5f, 14.5f };

        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                float x = px + 0.5f;
                float y = py + 0.5f;

                float minDist = 999f;
                float shadowDist = 999f;
                float bestLobeCenterDist = 999f;
                Vector2 bestLobeOffset = Vector2.zero;
                for (int l = 0; l < lobes.Length; l++)
                {
                    float swayWeight = (l == lobes.Length - 1) ? 0.5f : 1f;
                    float swayY = ((l + frameIdx) % 2 == 0) ? 0.9f : -0.9f;
                    float lx = cx + lobes[l].x + swayX * swayWeight;
                    float ly = cy + lobes[l].y + swayY;
                    float radialDist = Vector2.Distance(new Vector2(x, y), new Vector2(lx, ly));
                    float d = radialDist - lobeRadii[l];
                    if (d < minDist)
                    {
                        minDist = d;
                        bestLobeCenterDist = radialDist / lobeRadii[l];
                        bestLobeOffset = new Vector2(x - lx, y - ly) / lobeRadii[l];
                    }

                    float sd = Vector2.Distance(new Vector2(x - 3f, y + 3f), new Vector2(lx, ly)) - lobeRadii[l];
                    if (sd < shadowDist) shadowDist = sd;
                }

                Color32 outCol = new Color32(0, 0, 0, 0);
                if (minDist <= 1.25f)
                {
                    if (minDist > 0f)
                    {
                        outCol = outline;
                    }
                    else
                    {
                        int h = HashPixel(px + frameIdx * 2, py, 17);
                        // Radial mound highlight per leaf cluster + top-left moonlit bias + pixel dither
                        float ridgeFactor = (1f - bestLobeCenterDist) * 0.85f
                            + (-bestLobeOffset.x * 0.25f + bestLobeOffset.y * 0.35f)
                            + ((h % 11) - 5) * 0.045f;

                        if (ridgeFactor > 0.62f)
                        {
                            outCol = (h % 4 == 0) ? brightTip : highlightGreen;
                        }
                        else if (ridgeFactor > 0.28f)
                        {
                            outCol = highlightGreen;
                        }
                        else if (ridgeFactor > -0.08f)
                        {
                            outCol = midGreen;
                        }
                        else
                        {
                            outCol = deepGreen;
                        }
                    }
                }
                else if (shadowDist <= 0.5f)
                {
                    outCol = shadow;
                }

                buffer[(originY + py) * stride + (originX + px)] = outCol;
            }
        }
    }

    private static void PaintPalmTreeCell(
        Color32[] buffer, int stride, int originX, int originY, float swayAngle, int frameIdx)
    {
        const int size = 64;
        float cx = 32f + Mathf.Sin(frameIdx * Mathf.PI * 0.5f) * 1.4f;
        float cy = 32f;

        Color32 outline = new Color32(8, 15, 10, 255);
        Color32 darkLeaf = new Color32(15, 36, 20, 255);
        Color32 midLeaf = new Color32(27, 58, 32, 255);
        Color32 tipLeaf = new Color32(46, 86, 51, 255);
        Color32 crown = new Color32(42, 30, 19, 255);
        Color32 shadow = new Color32(4, 7, 6, 80);

        const int frondCount = 7;

        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                float x = px + 0.5f;
                float y = py + 0.5f;
                float dx = x - cx;
                float dy = y - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);

                bool insideFrond = false;
                bool isOutline = false;
                float frondNormalizedR = 0f;

                if (r < 4.4f)
                {
                    insideFrond = true;
                }
                else if (r <= 27f)
                {
                    for (int f = 0; f < frondCount; f++)
                    {
                        float baseAngle = f * (Mathf.PI * 2f / frondCount) + swayAngle + Mathf.Sin(frameIdx + f) * 0.04f;
                        float curveAngle = baseAngle + (r / 27f) * swayAngle * 1.5f;
                        float delta = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, curveAngle * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                        float halfWidthRad = Mathf.Lerp(0.32f, 0.06f, r / 27f);
                        float maxLen = (f % 2 == 0) ? 25.5f : 23f;

                        if (r <= maxLen && delta <= halfWidthRad)
                        {
                            insideFrond = true;
                            frondNormalizedR = r / maxLen;
                            break;
                        }
                        if (r <= maxLen + 1.3f && delta <= halfWidthRad + 0.065f)
                        {
                            isOutline = true;
                        }
                    }
                }

                Color32 outCol = new Color32(0, 0, 0, 0);
                if (insideFrond)
                {
                    if (r < 4.0f)
                    {
                        outCol = crown;
                    }
                    else
                    {
                        int h = HashPixel(px + frameIdx, py, 29);
                        if (frondNormalizedR > 0.65f && dy > -6f)
                        {
                            outCol = (h % 3 == 0) ? midLeaf : tipLeaf;
                        }
                        else if (frondNormalizedR > 0.3f)
                        {
                            outCol = (h % 4 == 0) ? tipLeaf : midLeaf;
                        }
                        else
                        {
                            outCol = darkLeaf;
                        }
                    }
                }
                else if (isOutline)
                {
                    outCol = outline;
                }
                else if (Vector2.Distance(new Vector2(x - 3f, y + 3f), new Vector2(cx, cy)) < 19f)
                {
                    outCol = shadow;
                }

                buffer[(originY + py) * stride + (originX + px)] = outCol;
            }
        }
    }

    private static void GenerateWindLinesSheet()
    {
        const int cellW = 64;
        const int cellH = 128;
        int outW = cellW * WIND_FRAMES;
        int outH = cellH;
        Texture2D sheet = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[outW * outH];

        // Define persistent vertical streaks (x, width, yStart, length, peakAlpha)
        (int x, int w, int y0, int len, byte alpha)[] streaks =
        {
            (8, 1, 14, 42, 110),
            (18, 2, 72, 48, 85),
            (27, 1, 36, 38, 125),
            (37, 1, 96, 44, 95),
            (46, 2, 18, 52, 80),
            (55, 1, 64, 34, 115)
        };

        for (int f = 0; f < WIND_FRAMES; f++)
        {
            int shiftDown = (f * cellH) / WIND_FRAMES;
            foreach (var st in streaks)
            {
                for (int dy = 0; dy < st.len; dy++)
                {
                    // Vertical progression from top to bottom cleanly wrapping across 128px height
                    int py = (st.y0 + dy - shiftDown + cellH * 4) % cellH;
                    float edgeFade = Mathf.Sin((dy / (float)st.len) * Mathf.PI);
                    byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(st.alpha * edgeFade), 0, 255);
                    if (a == 0) continue;

                    Color32 col = new Color32(204, 224, 240, a);
                    for (int wx = 0; wx < st.w; wx++)
                    {
                        int px = f * cellW + st.x + wx;
                        if (px >= 0 && px < outW && py >= 0 && py < outH)
                        {
                            pixels[py * outW + px] = col;
                        }
                    }
                }
            }
        }

        sheet.SetPixels32(pixels);
        sheet.Apply();
        File.WriteAllBytes(WIND_SHEET_PATH, sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
    }

    private static void GenerateRoadsideGroundTile()
    {
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[size * size];

        Color32 baseTurf = new Color32(12, 20, 14, 255);
        Color32 darkSoil = new Color32(9, 15, 11, 255);
        Color32 grassBlade = new Color32(19, 32, 21, 255);
        Color32 pebble = new Color32(22, 24, 22, 255);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int h = HashPixel(x, y, 41);
                Color32 c = baseTurf;
                if (h % 7 == 0) c = darkSoil;
                else if (h % 13 == 0) c = grassBlade;
                else if (h % 29 == 0) c = pebble;
                pixels[y * size + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(GROUND_TILE_PATH, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }

    private static void GenerateHeadlightCone()
    {
        const int w = 128;
        const int h = 192;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[w * h];

        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1); // 0 at lamp source (bottom), 1 at far tip (top)
            float halfConeWidth = Mathf.Lerp(5f, w * 0.46f, v);
            float forwardFade = Mathf.Pow(1f - v, 1.35f) * Mathf.Clamp01(v * 14f);

            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) - w * 0.5f);
                if (dx > halfConeWidth)
                {
                    pixels[y * w + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float radial = 1f - (dx / halfConeWidth);
                float alpha01 = Mathf.Pow(radial, 1.4f) * forwardFade * 0.52f;
                // Subtle stepped pixel-band quantization for 16-bit feel
                float quantized = Mathf.Floor(alpha01 * 18f) / 18f;
                byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(quantized * 255f), 0, 255);
                pixels[y * w + x] = new Color32(255, 224, 146, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(HEADLIGHT_CONE_PATH, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }

    private static void GenerateTaillightGlow()
    {
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[size * size];

        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                float falloff = Mathf.Clamp01(1f - dist);
                float alpha01 = Mathf.Pow(falloff, 1.8f) * 0.78f;
                byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha01 * 255f), 0, 255);
                pixels[y * size + x] = new Color32(255, 42, 36, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(TAILLIGHT_GLOW_PATH, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }

    private static int HashPixel(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) & 0x7FFFFFFF;
        }
    }

    private static void ConfigureAllSpriteImports()
    {
        ConfigureSlicedSheet(ROAD_SHEET_PATH, ROAD_FRAMES, 1, "road_moving", PIXELS_PER_UNIT);
        ConfigureSlicedSheet(TREES_SHEET_PATH, TREE_COLS, TREE_ROWS, "trees_waving", PIXELS_PER_UNIT);
        ConfigureSlicedSheet(WIND_SHEET_PATH, WIND_FRAMES, 1, "wind_lines", PIXELS_PER_UNIT);

        ConfigureSingleSprite(GROUND_TILE_PATH, PIXELS_PER_UNIT, FilterMode.Point, TextureWrapMode.Repeat);
        ConfigureSingleSprite(HEADLIGHT_CONE_PATH, PIXELS_PER_UNIT, FilterMode.Point, TextureWrapMode.Clamp);
        ConfigureSingleSprite(TAILLIGHT_GLOW_PATH, PIXELS_PER_UNIT, FilterMode.Point, TextureWrapMode.Clamp);
    }

    private static void ConfigureSingleSprite(string assetPath, float ppu, FilterMode filterMode, TextureWrapMode wrapMode)
    {
        TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (ti == null) return;

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.filterMode = filterMode;
        ti.wrapMode = wrapMode;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.spritePixelsPerUnit = ppu;

        TextureImporterSettings settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spritePixelsPerUnit = ppu;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        ti.SetTextureSettings(settings);

        EditorUtility.SetDirty(ti);
        ti.SaveAndReimport();
    }

    private static void ConfigureSlicedSheet(string assetPath, int cols, int rows, string prefix, float ppu)
    {
        TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (ti == null)
        {
            Debug.LogWarning($"[LoadingSceneBuilder] Missing TextureImporter at {assetPath}");
            return;
        }

        ti.GetSourceTextureWidthAndHeight(out int texW, out int texH);
        if (texW <= 0 || texH <= 0)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            texW = tex != null ? tex.width : 128 * cols;
            texH = tex != null ? tex.height : 128 * rows;
        }

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Multiple;
        ti.filterMode = FilterMode.Point;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 4096;
        ti.spritePixelsPerUnit = ppu;

        TextureImporterSettings importerSettings = new TextureImporterSettings();
        ti.ReadTextureSettings(importerSettings);
        importerSettings.spriteMeshType = SpriteMeshType.FullRect;
        importerSettings.spritePixelsPerUnit = ppu;
        ti.SetTextureSettings(importerSettings);

        SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
        factories.Init();
        ISpriteEditorDataProvider dataProvider = factories.GetSpriteEditorDataProviderFromObject(ti);
        if (dataProvider == null)
        {
            Debug.LogError($"[LoadingSceneBuilder] ISpriteEditorDataProvider null for {assetPath}");
            return;
        }

        dataProvider.InitSpriteEditorDataProvider();

        SpriteRect[] existing = dataProvider.GetSpriteRects();
        Dictionary<string, GUID> existingIds = new Dictionary<string, GUID>();
        if (existing != null)
        {
            foreach (SpriteRect rect in existing)
            {
                if (!string.IsNullOrEmpty(rect.name) && rect.spriteID != default)
                {
                    existingIds[rect.name] = rect.spriteID;
                }
            }
        }

        int cellW = texW / cols;
        int cellH = texH / rows;
        List<SpriteRect> rects = new List<SpriteRect>(cols * rows);
        int idx = 0;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                string spriteName = $"{prefix}_{idx}";
                SpriteRect rect = new SpriteRect
                {
                    name = spriteName,
                    rect = new Rect(c * cellW, texH - (r + 1) * cellH, cellW, cellH),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                    spriteID = existingIds.TryGetValue(spriteName, out GUID gid) ? gid : GUID.Generate()
                };
                rects.Add(rect);
                idx++;
            }
        }

        dataProvider.SetSpriteRects(rects.ToArray());
        dataProvider.Apply();

        EditorUtility.SetDirty(ti);
        ti.SaveAndReimport();
    }

    private static Sprite[] LoadAllSpritesSorted(string assetPath, string prefix)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        List<Sprite> sprites = new List<Sprite>();
        foreach (UnityEngine.Object obj in assets)
        {
            if (obj is Sprite sp)
            {
                sprites.Add(sp);
            }
        }

        sprites.Sort((a, b) =>
        {
            int ia = ExtractTrailingIndex(a.name, prefix);
            int ib = ExtractTrailingIndex(b.name, prefix);
            return ia.CompareTo(ib);
        });
        return sprites.ToArray();
    }

    private static int ExtractTrailingIndex(string name, string prefix)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        int underscore = name.LastIndexOf('_');
        if (underscore >= 0 && int.TryParse(name.Substring(underscore + 1), out int parsed))
        {
            return parsed;
        }
        return string.Compare(name, prefix, StringComparison.Ordinal);
    }

    private static void BuildLoadingSceneAsset()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Sprite jeepneyRoofSprite = AssetDatabase.LoadAssetAtPath<Sprite>(JEEPNEY_ROOF_PATH);
        Sprite groundTileSprite = AssetDatabase.LoadAssetAtPath<Sprite>(GROUND_TILE_PATH);
        Sprite headlightConeSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HEADLIGHT_CONE_PATH);
        Sprite taillightGlowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TAILLIGHT_GLOW_PATH);

        Sprite[] roadFrames = LoadAllSpritesSorted(ROAD_SHEET_PATH, "road_moving");
        Sprite[] allTreeFrames = LoadAllSpritesSorted(TREES_SHEET_PATH, "trees_waving");
        Sprite[] windFrames = LoadAllSpritesSorted(WIND_SHEET_PATH, "wind_lines");

        Sprite[] leafyFrames = SliceRange(allTreeFrames, 0, 4);
        Sprite[] palmFrames = SliceRange(allTreeFrames, 4, 4);

        GameObject root = new GameObject("LoadingRoadBackdrop");
        LoadingRoadBackdropController controller = root.AddComponent<LoadingRoadBackdropController>();

        // 1. Camera (orthographic, centered)
        GameObject camGo = new GameObject("LoadingSceneCamera");
        camGo.transform.SetParent(root.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 0f, -10f);
        camGo.tag = "MainCamera";

        Camera cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5.0f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.035f, 0.045f, 0.04f, 1f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 50f;
        cam.depth = 50f;
        AudioListener listener = camGo.AddComponent<AudioListener>();
        camGo.AddComponent<UniversalAdditionalCameraData>();

        // Global night Light2D (safe if any sprites are URP-lit)
        GameObject globalLightGo = new GameObject("NightGlobalLight");
        globalLightGo.transform.SetParent(root.transform, false);
        Light2D globalLight = globalLightGo.AddComponent<Light2D>();
        globalLight.lightType = Light2D.LightType.Global;
        globalLight.color = new Color(0.75f, 0.84f, 0.96f, 1f);
        globalLight.intensity = 0.82f;

        // 2. Full-bleed roadside ground backdrop (zero dead space on 4:3, 16:9, 21:9, etc.)
        GameObject groundGo = new GameObject("RoadsideGround");
        groundGo.transform.SetParent(root.transform, false);
        groundGo.transform.localPosition = new Vector3(0f, 0f, 2f);
        SpriteRenderer groundSr = groundGo.AddComponent<SpriteRenderer>();
        groundSr.sprite = groundTileSprite;
        groundSr.drawMode = SpriteDrawMode.Tiled;
        groundSr.size = new Vector2(36f, 24f);
        groundSr.sortingOrder = -20;

        // 3. Static Vertical Road (4 stacked animated road tiles from bottom to top -> 0 dead space)
        // Each tile is 128px @ 64PPU = 2.0u. Scaled 2.6x => 5.2u wide & 5.2u tall.
        // 16 frames moving 1/16 tile per frame at 20 fps => exact speed 6.5 units/sec matching trees!
        const float roadScale = 2.6f;
        const float roadTileWorldHeight = 2.0f * roadScale; // 5.2u
        const float roadFps = 20f;
        const float treeScrollSpeed = (roadTileWorldHeight / ROAD_FRAMES) * roadFps; // 6.5 u/s

        GameObject roadGroup = new GameObject("VerticalRoad");
        roadGroup.transform.SetParent(root.transform, false);
        float[] roadYs = { -7.8f, -2.6f, 2.6f, 7.8f };
        for (int i = 0; i < roadYs.Length; i++)
        {
            GameObject tileGo = new GameObject($"RoadTile_{i}");
            tileGo.transform.SetParent(roadGroup.transform, false);
            tileGo.transform.localPosition = new Vector3(0f, roadYs[i], 1f);
            tileGo.transform.localScale = new Vector3(roadScale, roadScale, 1f);

            SpriteRenderer tileSr = tileGo.AddComponent<SpriteRenderer>();
            tileSr.sprite = roadFrames.Length > 0 ? roadFrames[0] : null;
            tileSr.sortingOrder = -10;

            LoadingRoadSpriteAnimator anim = tileGo.AddComponent<LoadingRoadSpriteAnimator>();
            anim.Configure(roadFrames, roadFps, randomizeStart: false, offset: 0);
        }

        // 4. Static Jeepney centered in the middle of the road + warm headlights & taillights
        GameObject jeepneyGo = new GameObject("Jeepney");
        jeepneyGo.transform.SetParent(root.transform, false);
        jeepneyGo.transform.localPosition = Vector3.zero;
        jeepneyGo.transform.localScale = new Vector3(1.55f, 1.55f, 1f);

        SpriteRenderer jeepneySr = jeepneyGo.AddComponent<SpriteRenderer>();
        jeepneySr.sprite = jeepneyRoofSprite;
        jeepneySr.sortingOrder = 5;

        // Headlight beams extending upward (+Y)
        CreateLampSprite(
            jeepneyGo.transform,
            "HeadlightCone_Left",
            headlightConeSprite,
            new Vector3(-0.36f, 2.15f, 0f),
            new Vector3(0.95f, 1.45f, 1f),
            new Color(1f, 0.93f, 0.72f, 0.72f),
            sortingOrder: 6);

        CreateLampSprite(
            jeepneyGo.transform,
            "HeadlightCone_Right",
            headlightConeSprite,
            new Vector3(0.36f, 2.15f, 0f),
            new Vector3(0.95f, 1.45f, 1f),
            new Color(1f, 0.93f, 0.72f, 0.72f),
            sortingOrder: 6);

        // Soft Point Light2D illuminating asphalt & nearby foliage ahead
        GameObject headLight2DGo = new GameObject("HeadlightPointLight2D");
        headLight2DGo.transform.SetParent(jeepneyGo.transform, false);
        headLight2DGo.transform.localPosition = new Vector3(0f, 1.95f, 0f);
        Light2D headLight2D = headLight2DGo.AddComponent<Light2D>();
        headLight2D.lightType = Light2D.LightType.Point;
        headLight2D.color = new Color(1f, 0.87f, 0.58f, 1f);
        headLight2D.intensity = 1.15f;
        headLight2D.pointLightInnerRadius = 0.8f;
        headLight2D.pointLightOuterRadius = 4.6f;

        // Rear crimson taillights (-Y)
        CreateLampSprite(
            jeepneyGo.transform,
            "Taillight_Left",
            taillightGlowSprite,
            new Vector3(-0.42f, -1.16f, 0f),
            new Vector3(0.75f, 0.75f, 1f),
            new Color(1f, 0.25f, 0.22f, 0.92f),
            sortingOrder: 6);

        CreateLampSprite(
            jeepneyGo.transform,
            "Taillight_Right",
            taillightGlowSprite,
            new Vector3(0.42f, -1.16f, 0f),
            new Vector3(0.75f, 0.75f, 1f),
            new Color(1f, 0.25f, 0.22f, 0.92f),
            sortingOrder: 6);

        GameObject tailLight2DGo = new GameObject("TaillightPointLight2D");
        tailLight2DGo.transform.SetParent(jeepneyGo.transform, false);
        tailLight2DGo.transform.localPosition = new Vector3(0f, -1.35f, 0f);
        Light2D tailLight2D = tailLight2DGo.AddComponent<Light2D>();
        tailLight2D.lightType = Light2D.LightType.Point;
        tailLight2D.color = new Color(1f, 0.2f, 0.16f, 1f);
        tailLight2D.intensity = 0.75f;
        tailLight2D.pointLightInnerRadius = 0.2f;
        tailLight2D.pointLightOuterRadius = 1.8f;

        // 5. Waving & Scrolling Trees (ONLY objects translating from top to bottom!)
        GameObject treesGroup = new GameObject("RoadsideTrees");
        treesGroup.transform.SetParent(root.transform, false);

        List<Transform> treeList = new List<Transform>();
        const float wrapTop = 9.5f;
        const float wrapBottom = -9.5f;

        // Staggered tree lanes covering inner roadside edge out to ultrawide flanks
        (float x, float yOffset, float scale, bool palm, float rotDeg, float fps, int frameStart)[] treeSpecs =
        {
            // Left inner curb
            (-3.75f, -7.6f, 2.35f, false, 12f, 6.0f, 0),
            (-3.95f, -3.8f, 2.15f, true,  -8f, 7.2f, 1),
            (-3.65f,  0.1f, 2.45f, false, 40f, 5.6f, 2),
            (-3.90f,  4.0f, 2.25f, true,  18f, 6.8f, 3),
            (-3.75f,  7.9f, 2.30f, false,-22f, 6.2f, 1),

            // Left mid verge
            (-6.20f, -8.5f, 2.75f, true,  25f, 6.5f, 2),
            (-5.85f, -4.6f, 2.60f, false,-35f, 5.8f, 0),
            (-6.35f, -0.8f, 2.85f, false, 14f, 6.1f, 3),
            (-5.95f,  3.1f, 2.50f, true,  52f, 7.0f, 1),
            (-6.25f,  7.0f, 2.70f, false,-15f, 5.9f, 2),

            // Left outer ultrawide flank
            (-9.10f, -6.7f, 2.95f, false, 60f, 5.5f, 1),
            (-8.80f, -1.9f, 2.70f, true, -28f, 6.7f, 3),
            (-9.25f,  2.8f, 2.90f, false, 20f, 6.0f, 0),
            (-8.95f,  7.4f, 2.65f, true,  44f, 6.4f, 2),

            // Right inner curb
            ( 3.75f, -6.2f, 2.30f, true,  -16f, 6.9f, 1),
            ( 3.90f, -2.3f, 2.45f, false, 28f, 5.8f, 3),
            ( 3.70f,  1.6f, 2.20f, true,  48f, 7.1f, 0),
            ( 3.85f,  5.5f, 2.40f, false,-12f, 6.2f, 2),
            ( 3.75f,  9.0f, 2.25f, false, 70f, 5.7f, 1),

            // Right mid verge
            ( 6.15f, -7.8f, 2.70f, false,-24f, 5.9f, 3),
            ( 5.85f, -3.5f, 2.55f, true,  15f, 6.6f, 2),
            ( 6.30f,  0.6f, 2.85f, false, 36f, 6.0f, 0),
            ( 5.95f,  4.5f, 2.65f, true, -42f, 7.3f, 1),
            ( 6.20f,  8.3f, 2.75f, false, 10f, 5.6f, 2),

            // Right outer ultrawide flank
            ( 9.05f, -5.2f, 2.90f, true,  32f, 6.5f, 0),
            ( 8.80f, -0.4f, 2.75f, false,-18f, 5.7f, 2),
            ( 9.20f,  4.2f, 2.95f, false, 54f, 6.2f, 3),
            ( 8.90f,  8.8f, 2.65f, true, -25f, 6.8f, 1),
        };

        for (int i = 0; i < treeSpecs.Length; i++)
        {
            var spec = treeSpecs[i];
            Sprite[] frames = spec.palm && palmFrames.Length > 0 ? palmFrames : leafyFrames;
            GameObject treeGo = new GameObject($"Tree_{i:00}");
            treeGo.transform.SetParent(treesGroup.transform, false);
            treeGo.transform.localPosition = new Vector3(spec.x, spec.yOffset, 0f);
            treeGo.transform.localRotation = Quaternion.Euler(0f, 0f, spec.rotDeg);
            treeGo.transform.localScale = new Vector3(spec.scale, spec.scale, 1f);

            SpriteRenderer sr = treeGo.AddComponent<SpriteRenderer>();
            sr.sprite = frames.Length > 0 ? frames[spec.frameStart % frames.Length] : null;
            sr.sortingOrder = (Mathf.Abs(spec.x) < 4.5f) ? 2 : 0;

            LoadingRoadSpriteAnimator anim = treeGo.AddComponent<LoadingRoadSpriteAnimator>();
            anim.Configure(frames, spec.fps, randomizeStart: false, offset: spec.frameStart);

            treeList.Add(treeGo.transform);
        }

        // 6. Vertical Wind Lines animated along the road corridor
        GameObject windGroup = new GameObject("VerticalWindLines");
        windGroup.transform.SetParent(root.transform, false);

        (float x, float y, float scaleX, float scaleY, float alpha, float fps, int offset)[] windSpecs =
        {
            (-2.15f, -2.5f, 1.3f, 3.0f, 0.36f, 16f, 0),
            (-2.15f,  2.8f, 1.3f, 3.0f, 0.34f, 17f, 4),
            (-0.75f, -2.2f, 1.2f, 2.8f, 0.30f, 18f, 2),
            (-0.75f,  2.9f, 1.2f, 2.8f, 0.32f, 16f, 6),
            ( 0.75f, -2.6f, 1.2f, 2.8f, 0.32f, 17f, 5),
            ( 0.75f,  2.5f, 1.2f, 2.8f, 0.30f, 18f, 1),
            ( 2.15f, -2.8f, 1.3f, 3.0f, 0.35f, 16f, 3),
            ( 2.15f,  2.6f, 1.3f, 3.0f, 0.36f, 17f, 7),
            (-4.50f,  0.0f, 1.6f, 4.4f, 0.25f, 15f, 2),
            ( 4.50f,  0.0f, 1.6f, 4.4f, 0.25f, 15f, 5),
        };

        for (int i = 0; i < windSpecs.Length; i++)
        {
            var ws = windSpecs[i];
            GameObject windGo = new GameObject($"WindStrip_{i:00}");
            windGo.transform.SetParent(windGroup.transform, false);
            windGo.transform.localPosition = new Vector3(ws.x, ws.y, -0.5f);
            windGo.transform.localScale = new Vector3(ws.scaleX, ws.scaleY, 1f);

            SpriteRenderer windSr = windGo.AddComponent<SpriteRenderer>();
            windSr.sprite = windFrames.Length > 0 ? windFrames[ws.offset % windFrames.Length] : null;
            windSr.color = new Color(0.84f, 0.92f, 1f, ws.alpha);
            windSr.sortingOrder = 12;

            LoadingRoadSpriteAnimator windAnim = windGo.AddComponent<LoadingRoadSpriteAnimator>();
            windAnim.Configure(windFrames, ws.fps, randomizeStart: false, offset: ws.offset);
        }

        controller.Configure(
            cam,
            listener,
            jeepneyGo.transform,
            groundGo.transform,
            treeList.ToArray(),
            treeScrollSpeed,
            wrapTop,
            wrapBottom);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, LOADING_SCENE_PATH);
    }

    private static void CreateLampSprite(
        Transform parent,
        string name,
        Sprite sprite,
        Vector3 localPos,
        Vector3 localScale,
        Color color,
        int sortingOrder)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = sortingOrder;
    }

    private static Sprite[] SliceRange(Sprite[] source, int start, int count)
    {
        if (source == null || start >= source.Length) return Array.Empty<Sprite>();
        int actual = Mathf.Min(count, source.Length - start);
        Sprite[] slice = new Sprite[actual];
        Array.Copy(source, start, slice, 0, actual);
        return slice;
    }

    private static void EnsureSceneInBuildSettings(string scenePath)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        for (int i = 0; i < scenes.Count; i++)
        {
            if (string.Equals(scenes[i].path, scenePath, StringComparison.OrdinalIgnoreCase))
            {
                if (!scenes[i].enabled)
                {
                    scenes[i].enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                return;
            }
        }

        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
