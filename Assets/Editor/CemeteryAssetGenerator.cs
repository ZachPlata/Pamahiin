using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates pure 90-degree top-down 16-bit pixel art sprites and prop sheets for
/// Kampo Santo ng San Nicolas (CemeteryMap) under Assets/Resources/Sprites/CemeteryMap/.
/// </summary>
public static class CemeteryAssetGenerator
{
    public const string SPRITE_DIR = "Assets/Resources/Sprites/CemeteryMap";

    private static readonly Color32 PAL_OUTLINE = new Color32(16, 12, 14, 255);
    private static readonly Color32 PAL_SLATE_DK = new Color32(26, 28, 34, 255);
    private static readonly Color32 PAL_SLATE_MD = new Color32(52, 56, 64, 255);
    private static readonly Color32 PAL_SLATE_LT = new Color32(84, 88, 96, 255);
    private static readonly Color32 PAL_STONE_DK = new Color32(44, 44, 48, 255);
    private static readonly Color32 PAL_STONE_MD = new Color32(74, 74, 78, 255);
    private static readonly Color32 PAL_STONE_LT = new Color32(108, 108, 112, 255);
    private static readonly Color32 PAL_MOSS_DK = new Color32(22, 34, 24, 255);
    private static readonly Color32 PAL_MOSS_MD = new Color32(36, 54, 34, 255);
    private static readonly Color32 PAL_MOSS_LT = new Color32(54, 78, 44, 255);
    private static readonly Color32 PAL_RUST_DK = new Color32(58, 28, 20, 255);
    private static readonly Color32 PAL_RUST_MD = new Color32(92, 46, 30, 255);
    private static readonly Color32 PAL_WOOD_DK = new Color32(38, 24, 16, 255);
    private static readonly Color32 PAL_WOOD_MD = new Color32(72, 46, 28, 255);
    private static readonly Color32 PAL_WOOD_LT = new Color32(106, 70, 44, 255);
    private static readonly Color32 PAL_BRASS = new Color32(178, 140, 62, 255);

    private sealed class PixelCanvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        public PixelCanvas(int width, int height, Color32? fill = null)
        {
            Width = width;
            Height = height;
            Pixels = new Color32[width * height];
            Color32 c = fill ?? new Color32(0, 0, 0, 0);
            for (int i = 0; i < Pixels.Length; i++) Pixels[i] = c;
        }

        public void Set(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;
            Pixels[y * Width + x] = c;
        }

        public void Blend(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || c.a == 0) return;
            int idx = y * Width + x;
            if (c.a == 255)
            {
                Pixels[idx] = c;
                return;
            }
            Color32 dst = Pixels[idx];
            float a = c.a / 255f;
            float inv = 1f - a;
            Pixels[idx] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * a + dst.r * inv), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * a + dst.g * inv), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * a + dst.b * inv), 0, 255),
                (byte)Mathf.Clamp(Mathf.Max(c.a, dst.a), 0, 255));
        }

        public void FillRect(int x, int y, int w, int h, Color32 c, bool blend = false)
        {
            for (int yy = y; yy < y + h; yy++)
            {
                for (int xx = x; xx < x + w; xx++)
                {
                    if (blend) Blend(xx, yy, c);
                    else Set(xx, yy, c);
                }
            }
        }

        public void FillDitheredRect(int x, int y, int w, int h, Color32 baseCol, int seed, float variance = 0.14f)
        {
            for (int yy = y; yy < y + h; yy++)
            {
                for (int xx = x; xx < x + w; xx++)
                {
                    float n = 1f - variance * 0.5f + Hash01(xx, yy, seed) * variance;
                    Set(xx, yy, Shade(baseCol, n));
                }
            }
        }

        public void StrokeRect(int x, int y, int w, int h, int thickness, Color32 c)
        {
            FillRect(x, y, w, thickness, c);
            FillRect(x, y + h - thickness, w, thickness, c);
            FillRect(x, y, thickness, h, c);
            FillRect(x + w - thickness, y, thickness, h, c);
        }

        public void FillCircle(int cx, int cy, int r, Color32 c, bool blend = false)
        {
            int r2 = r * r;
            for (int yy = cy - r; yy <= cy + r; yy++)
            {
                for (int xx = cx - r; xx <= cx + r; xx++)
                {
                    int dx = xx - cx;
                    int dy = yy - cy;
                    if (dx * dx + dy * dy <= r2)
                    {
                        if (blend) Blend(xx, yy, c);
                        else Set(xx, yy, c);
                    }
                }
            }
        }

        public void FillEllipse(int cx, int cy, int rx, int ry, Color32 c, bool blend = false)
        {
            float rx2 = Mathf.Max(1, rx * rx);
            float ry2 = Mathf.Max(1, ry * ry);
            for (int yy = cy - ry; yy <= cy + ry; yy++)
            {
                for (int xx = cx - rx; xx <= cx + rx; xx++)
                {
                    float nx = (xx - cx) * (xx - cx) / rx2;
                    float ny = (yy - cy) * (yy - cy) / ry2;
                    if (nx + ny <= 1f)
                    {
                        if (blend) Blend(xx, yy, c);
                        else Set(xx, yy, c);
                    }
                }
            }
        }

        public void DrawLine(int x0, int y0, int x1, int y1, Color32 c, bool blend = false)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                if (blend) Blend(x0, y0, c);
                else Set(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public void AddPixelOutline(Color32 outlineCol, int alphaThreshold = 180)
        {
            Color32[] copy = (Color32[])Pixels.Clone();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (copy[y * Width + x].a >= alphaThreshold) continue;
                    bool touchesOpaque =
                        (x > 0 && copy[y * Width + (x - 1)].a >= alphaThreshold) ||
                        (x < Width - 1 && copy[y * Width + (x + 1)].a >= alphaThreshold) ||
                        (y > 0 && copy[(y - 1) * Width + x].a >= alphaThreshold) ||
                        (y < Height - 1 && copy[(y + 1) * Width + x].a >= alphaThreshold);
                    if (touchesOpaque) Set(x, y, outlineCol);
                }
            }
        }

        public void SavePng(string relativePath)
        {
            string fullPath = Path.Combine(SPRITE_DIR, relativePath).Replace('\\', '/');
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.SetPixels32(Pixels);
            tex.Apply();
            File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }

    private static float Hash01(int x, int y, int seed)
    {
        uint n = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        n = (n ^ (n >> 13)) * 1274126177U;
        return ((n ^ (n >> 16)) & 0xFFFF) / 65535f;
    }

    private static Color32 Shade(Color32 c, float factor)
    {
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * factor), 0, 255),
            c.a);
    }

    [MenuItem("Pamahiin/Generate Cemetery Assets")]
    public static void GenerateAllAssets()
    {
        GenerateGateAndCandles();
        GenerateTileableGroundsAndRoofs();
        GenerateCemeteryStructures();
        GenerateCemeteryPropSheets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        Debug.Log("[CemeteryAssetGenerator] Generated all 16-bit CemeteryMap sprites and prop sheets.");
    }

    private static void GenerateGateAndCandles()
    {
        // 1. Single-sprite rusty wrought-iron gate (64x16 @ 128 PPU -> 0.50 x 0.125 world)
        PixelCanvas gate = new PixelCanvas(64, 16);
        gate.FillRect(0, 6, 64, 4, PAL_SLATE_DK);
        gate.FillRect(1, 7, 62, 2, PAL_SLATE_MD);
        // Hinge post on left (south hinge when rotated +90) and latch post on right
        gate.FillRect(0, 3, 5, 10, PAL_SLATE_DK);
        gate.FillRect(1, 4, 3, 8, PAL_SLATE_LT);
        gate.FillRect(59, 4, 5, 8, PAL_SLATE_DK);
        gate.FillRect(60, 5, 3, 6, PAL_RUST_MD);
        // Vertical spear bars + rust patches
        for (int bx = 7; bx <= 56; bx += 5)
        {
            Color32 barCol = (bx % 10 == 2) ? PAL_RUST_MD : PAL_SLATE_LT;
            gate.FillRect(bx, 2, 2, 12, PAL_SLATE_DK);
            gate.FillRect(bx, 4, 2, 8, barCol);
            gate.Set(bx, 14, PAL_RUST_MD);
            gate.Set(bx + 1, 1, PAL_RUST_DK);
        }
        // Center ornamental iron ring & lock plate
        gate.FillRect(27, 4, 10, 8, PAL_SLATE_DK);
        gate.StrokeRect(28, 5, 8, 6, 1, PAL_RUST_MD);
        gate.FillRect(30, 6, 4, 4, PAL_BRASS);
        gate.AddPixelOutline(PAL_OUTLINE);
        gate.SavePng("gate_iron.png");

        // 2. Animated flickering candle cluster sheet (64x16 -> 4 horizontal 16x16 frames)
        PixelCanvas candles = new PixelCanvas(64, 16);
        Color32 waxBase = new Color32(214, 198, 164, 255);
        Color32 waxShadow = new Color32(142, 126, 102, 255);
        Color32 waxDrip = new Color32(236, 224, 196, 255);
        Color32 flameOuter = new Color32(244, 146, 44, 255);
        Color32 flameCore = new Color32(255, 236, 140, 255);
        Color32 glowHalo = new Color32(255, 186, 82, 42);

        for (int f = 0; f < 4; f++)
        {
            int ox = f * 16;
            // Compact cluster of 3 top-down wax tapers
            candles.FillCircle(ox + 8, 7, 2, waxShadow);
            candles.FillCircle(ox + 8, 8, 2, waxBase);
            candles.Set(ox + 7, 7, waxDrip);

            candles.FillCircle(ox + 5, 6, 1, waxShadow);
            candles.Set(ox + 5, 7, waxBase);

            candles.FillCircle(ox + 11, 6, 1, waxShadow);
            candles.Set(ox + 11, 7, waxBase);
        }

        // Outline only the solid wax bases so flames stay crisp and light
        candles.AddPixelOutline(PAL_OUTLINE, 200);

        int[] swayX = { 0, 1, 0, -1 };
        int[] flameH = { 3, 2, 3, 2 };
        for (int f = 0; f < 4; f++)
        {
            int ox = f * 16;
            candles.FillCircle(ox + 8, 8, 4, glowHalo, true);

            int fx = ox + 8 + swayX[f];
            candles.FillRect(fx, 8, 1, flameH[f], flameOuter, true);
            candles.Set(fx, 9, flameCore);

            candles.Set(ox + 5 + (f % 2), 7, flameOuter);
            candles.Set(ox + 5, 8, flameCore);

            candles.Set(ox + 11 - (f % 2), 7, flameOuter);
            candles.Set(ox + 11, 8, flameCore);
        }
        candles.SavePng("candle_sheet.png");
    }

    private static void GenerateTileableGroundsAndRoofs()
    {
        // 1. Dark midnight cemetery grass (64x64)
        PixelCanvas grass = new PixelCanvas(64, 64);
        Color32 grassBase = new Color32(22, 34, 24, 255);
        Color32 grassPatch = new Color32(28, 42, 26, 255);
        Color32 soilPatch = new Color32(30, 26, 22, 255);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float n = Hash01(x, y, 701);
                float cluster = Hash01(x / 4, y / 4, 702);
                Color32 col = cluster > 0.72f ? soilPatch : (cluster < 0.35f ? grassPatch : grassBase);
                col = Shade(col, 0.88f + n * 0.24f);
                if (n > 0.90f) col = new Color32(38, 60, 34, 255); // Crisp moss/grass blade speck
                else if (n < 0.06f) col = new Color32(16, 22, 16, 255);
                grass.Set(x, y, col);
            }
        }
        grass.SavePng("cemetery_grass.png");

        // 2. Damp compacted dirt nitso alley (64x64)
        PixelCanvas dirt = new PixelCanvas(64, 64);
        Color32 dirtBase = new Color32(42, 34, 28, 255);
        Color32 gravel = new Color32(58, 52, 46, 255);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float n = Hash01(x, y, 711);
                float patch = Hash01(x / 3, y / 3, 712);
                Color32 col = patch > 0.70f ? new Color32(34, 28, 22, 255) : dirtBase;
                col = Shade(col, 0.86f + n * 0.26f);
                if (n > 0.94f) col = gravel;
                else if (patch < 0.14f && n > 0.75f) col = PAL_MOSS_DK;
                dirt.Set(x, y, col);
            }
        }
        dirt.SavePng("cemetery_dirt_alley.png");

        // 3. Cracked flagstone cemetery avenue (64x64)
        PixelCanvas stone = new PixelCanvas(64, 64);
        Color32 mortar = new Color32(26, 26, 28, 255);
        stone.FillRect(0, 0, 64, 64, mortar);
        for (int row = 0; row < 4; row++)
        {
            int sy = row * 16;
            int offset = (row % 2) * 8;
            for (int colIdx = 0; colIdx < 4; colIdx++)
            {
                int sx = (colIdx * 16 + offset) % 64;
                for (int dy = 1; dy < 15; dy++)
                {
                    for (int dx = 1; dx < 15; dx++)
                    {
                        int px = (sx + dx) % 64;
                        int py = (sy + dy) % 64;
                        float n = Hash01(px, py, 721 + row * 11 + colIdx);
                        Color32 slab = Shade(PAL_STONE_MD, 0.82f + n * 0.22f);
                        if (dy == 1 || dx == 1) slab = Shade(slab, 1.12f);
                        if (Hash01(px / 2, py / 2, 725) > 0.86f && (dx < 3 || dy < 3)) slab = PAL_MOSS_MD;
                        stone.Set(px, py, slab);
                    }
                }
            }
        }
        stone.SavePng("cemetery_path_stone.png");

        // 4. Aged marble/slate mausoleum floor tile (64x64)
        PixelCanvas mTile = new PixelCanvas(64, 64);
        Color32 tileA = new Color32(62, 62, 66, 255);
        Color32 tileB = new Color32(44, 44, 48, 255);
        Color32 grout = new Color32(22, 22, 26, 255);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                if (x % 16 == 0 || y % 16 == 0)
                {
                    mTile.Set(x, y, grout);
                    continue;
                }
                bool alt = ((x / 16) + (y / 16)) % 2 == 0;
                float n = Hash01(x, y, 731);
                Color32 c = Shade(alt ? tileA : tileB, 0.90f + n * 0.18f);
                if (Hash01(x / 5, y / 5, 732) > 0.88f) c = Shade(c, 0.80f);
                mTile.Set(x, y, c);
            }
        }
        mTile.SavePng("cemetery_mausoleum_tile.png");

        // 5. Weathered concrete slab mausoleum roof (128x128)
        PixelCanvas roof = new PixelCanvas(128, 128);
        Color32 concreteBase = new Color32(54, 54, 58, 255);
        Color32 concreteRim = new Color32(72, 72, 76, 255);
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                float n = Hash01(x, y, 741);
                bool border = x < 6 || x >= 122 || y < 6 || y >= 122;
                Color32 col = border ? concreteRim : concreteBase;
                col = Shade(col, 0.86f + n * 0.22f);
                if (!border && Hash01(x / 8, y / 8, 742) > 0.78f) col = Shade(col, 0.76f); // Rain stain
                if (!border && Hash01(x / 6, y / 6, 743) < 0.14f) col = PAL_MOSS_DK;
                if (x == 0 || x == 127 || y == 0 || y == 127 || x == 6 || x == 121 || y == 6 || y == 121)
                    col = PAL_SLATE_DK;
                roof.Set(x, y, col);
            }
        }
        roof.SavePng("cemetery_roof_concrete.png");
    }

    private static void GenerateCemeteryStructures()
    {
        // 1. Concrete multi-tiered Nitso block (64x32)
        PixelCanvas nitso = new PixelCanvas(64, 32);
        nitso.FillRect(1, 1, 62, 30, PAL_OUTLINE);
        nitso.FillDitheredRect(3, 3, 58, 26, PAL_STONE_MD, 801, 0.14f);
        nitso.StrokeRect(3, 3, 58, 26, 2, PAL_STONE_LT);
        // Parapet roof cap + 4 recessed niche plaques & candle stains
        nitso.FillDitheredRect(5, 11, 54, 16, PAL_STONE_DK, 802, 0.12f);
        for (int i = 0; i < 4; i++)
        {
            int nx = 6 + i * 14;
            nitso.FillRect(nx, 4, 10, 6, PAL_SLATE_DK);
            nitso.FillRect(nx + 1, 5, 8, 4, new Color32(158, 152, 140, 255)); // Marble lapida plaque
            nitso.FillRect(nx + 4, 6, 2, 2, PAL_SLATE_MD);
            // Wax / soot smudge on shelf edge
            nitso.FillRect(nx + 2, 2, 6, 2, new Color32(36, 32, 28, 255));
        }
        nitso.FillRect(10, 14, 12, 6, PAL_MOSS_DK);
        nitso.FillRect(42, 18, 10, 5, PAL_MOSS_MD);
        nitso.SavePng("cemetery_nitso_block.png");

        // 2. Gravestone 1 (24x24) - cross headstone with plot curb
        PixelCanvas g1 = new PixelCanvas(24, 24);
        g1.FillRect(4, 2, 16, 18, new Color32(28, 36, 26, 255));
        g1.StrokeRect(4, 2, 16, 18, 1, PAL_STONE_DK);
        g1.FillRect(7, 13, 10, 6, PAL_STONE_MD);
        g1.StrokeRect(7, 13, 10, 6, 1, PAL_STONE_LT);
        g1.FillRect(11, 14, 2, 5, PAL_STONE_LT);
        g1.FillRect(9, 16, 6, 2, PAL_STONE_LT);
        g1.FillRect(8, 5, 8, 6, PAL_MOSS_MD);
        g1.AddPixelOutline(PAL_OUTLINE);
        g1.SavePng("cemetery_gravestone_1.png");

        // 3. Gravestone 2 (24x28) - rounded/obelisk headstone
        PixelCanvas g2 = new PixelCanvas(24, 28);
        g2.FillRect(4, 2, 16, 22, new Color32(28, 36, 26, 255));
        g2.StrokeRect(4, 2, 16, 22, 1, PAL_STONE_DK);
        g2.FillRect(7, 15, 10, 8, PAL_STONE_MD);
        g2.FillRect(8, 16, 8, 6, PAL_STONE_LT);
        g2.FillRect(10, 18, 4, 2, PAL_STONE_DK);
        g2.FillRect(6, 5, 12, 8, PAL_STONE_DK);
        g2.FillRect(8, 7, 8, 4, PAL_MOSS_DK);
        g2.AddPixelOutline(PAL_OUTLINE);
        g2.SavePng("cemetery_gravestone_2.png");

        // 4. Flat Lapida Slab (32x24)
        PixelCanvas lapida = new PixelCanvas(32, 24);
        lapida.FillDitheredRect(3, 3, 26, 18, PAL_STONE_MD, 811, 0.12f);
        lapida.StrokeRect(3, 3, 26, 18, 2, PAL_STONE_LT);
        lapida.FillRect(7, 7, 18, 10, new Color32(144, 140, 132, 255));
        lapida.StrokeRect(7, 7, 18, 10, 1, PAL_STONE_DK);
        lapida.FillRect(10, 11, 12, 1, PAL_SLATE_MD);
        lapida.FillRect(12, 13, 8, 1, PAL_SLATE_MD);
        lapida.FillRect(5, 5, 4, 3, PAL_MOSS_MD);
        lapida.AddPixelOutline(PAL_OUTLINE);
        lapida.SavePng("cemetery_lapida_flat.png");

        // 5. Raised Sarcophagus Tomb (32x40)
        PixelCanvas tomb = new PixelCanvas(32, 40);
        tomb.FillDitheredRect(3, 3, 26, 34, PAL_STONE_DK, 812, 0.10f);
        tomb.FillDitheredRect(5, 5, 22, 30, PAL_STONE_MD, 813, 0.12f);
        tomb.StrokeRect(5, 5, 22, 30, 2, PAL_STONE_LT);
        tomb.FillRect(9, 10, 14, 20, new Color32(126, 124, 118, 255));
        tomb.FillRect(15, 13, 2, 14, PAL_STONE_DK);
        tomb.FillRect(11, 20, 10, 2, PAL_STONE_DK);
        tomb.FillRect(6, 7, 5, 5, PAL_MOSS_MD);
        tomb.AddPixelOutline(PAL_OUTLINE);
        tomb.SavePng("cemetery_tomb_raised.png");

        // 6. Mausoleum Stone Altar (40x24)
        PixelCanvas altar = new PixelCanvas(40, 24);
        altar.FillDitheredRect(2, 3, 36, 18, PAL_STONE_MD, 814, 0.10f);
        altar.StrokeRect(2, 3, 36, 18, 2, PAL_STONE_LT);
        // Crimson & brass liturgical runner cloth
        altar.FillRect(13, 4, 14, 16, new Color32(106, 26, 28, 255));
        altar.StrokeRect(13, 4, 14, 16, 1, PAL_BRASS);
        altar.FillRect(18, 9, 4, 6, PAL_BRASS);
        altar.AddPixelOutline(PAL_OUTLINE);
        altar.SavePng("cemetery_altar.png");

        // 7. Central Cross Monument (40x40)
        PixelCanvas cross = new PixelCanvas(40, 40);
        cross.FillCircle(20, 20, 17, PAL_STONE_DK);
        cross.FillCircle(20, 20, 14, PAL_STONE_MD);
        cross.FillCircle(20, 20, 10, PAL_STONE_LT);
        cross.FillRect(13, 13, 14, 14, PAL_STONE_DK);
        // Stepped stone cross shadow + top cap
        cross.FillRect(17, 7, 6, 26, PAL_STONE_LT);
        cross.FillRect(11, 18, 18, 6, PAL_STONE_LT);
        cross.FillRect(18, 9, 4, 22, new Color32(168, 164, 154, 255));
        cross.FillRect(13, 19, 14, 4, new Color32(168, 164, 154, 255));
        cross.FillCircle(20, 21, 2, PAL_BRASS);
        cross.AddPixelOutline(PAL_OUTLINE);
        cross.SavePng("cemetery_cross_monument.png");

        // 8. Gnarled Balete / Acacia Tree (64x64)
        PixelCanvas tree = new PixelCanvas(64, 64);
        // Dark canopy shadow & sprawling aerial roots
        tree.FillCircle(32, 32, 26, new Color32(12, 16, 14, 155), true);
        for (int a = 0; a < 6; a++)
        {
            float ang = a * Mathf.PI / 3f;
            int rx = 32 + Mathf.RoundToInt(Mathf.Cos(ang) * 16f);
            int ry = 32 + Mathf.RoundToInt(Mathf.Sin(ang) * 16f);
            tree.DrawLine(32, 32, rx, ry, PAL_WOOD_DK);
            tree.DrawLine(32, 31, rx, ry - 1, PAL_WOOD_MD);
        }
        // Layered dark mossy foliage clusters
        (int cx, int cy, int r, Color32 col)[] puffs =
        {
            (22, 38, 13, PAL_MOSS_DK),
            (42, 36, 13, PAL_MOSS_DK),
            (24, 22, 12, PAL_MOSS_DK),
            (41, 22, 12, PAL_MOSS_DK),
            (32, 32, 16, PAL_MOSS_MD),
            (28, 36, 10, PAL_MOSS_LT),
            (38, 28, 9, PAL_MOSS_LT),
            (32, 24, 8, PAL_MOSS_MD)
        };
        foreach (var p in puffs)
        {
            tree.FillCircle(p.cx, p.cy, p.r, p.col);
        }
        // Dappled leaf texture
        for (int y = 6; y < 58; y++)
        {
            for (int x = 6; x < 58; x++)
            {
                if (tree.Pixels[y * 64 + x].a < 200) continue;
                float n = Hash01(x, y, 821);
                if (n > 0.85f) tree.Set(x, y, PAL_MOSS_LT);
                else if (n < 0.16f) tree.Set(x, y, PAL_MOSS_DK);
            }
        }
        tree.AddPixelOutline(PAL_OUTLINE);
        tree.SavePng("cemetery_balete_tree.png");

        // 9. Gate Flower & Candle Vendor Stall (56x32)
        PixelCanvas stall = new PixelCanvas(56, 32);
        stall.FillDitheredRect(3, 4, 50, 24, PAL_WOOD_MD, 831, 0.12f);
        stall.StrokeRect(3, 4, 50, 24, 2, PAL_WOOD_DK);
        // Striped weathered tarp awning over north half
        for (int sx = 5; sx < 51; sx += 6)
        {
            Color32 stripe = ((sx / 6) % 2 == 0) ? new Color32(42, 68, 96, 255) : new Color32(126, 122, 114, 255);
            stall.FillRect(sx, 14, 6, 13, stripe);
        }
        stall.StrokeRect(5, 14, 46, 13, 1, PAL_OUTLINE);
        // Flower buckets & candle bundles on counter
        stall.FillCircle(12, 9, 3, new Color32(168, 52, 62, 255));
        stall.FillCircle(20, 9, 3, new Color32(214, 178, 64, 255));
        stall.FillCircle(28, 9, 3, new Color32(196, 192, 186, 255));
        stall.FillRect(36, 7, 10, 5, new Color32(204, 188, 146, 255));
        stall.AddPixelOutline(PAL_OUTLINE);
        stall.SavePng("cemetery_flower_stall.png");

        // 10. Cast-iron Poso Water Pump (24x24)
        PixelCanvas poso = new PixelCanvas(24, 24);
        poso.FillRect(4, 4, 16, 16, PAL_STONE_MD);
        poso.StrokeRect(4, 4, 16, 16, 1, PAL_STONE_LT);
        poso.FillCircle(12, 12, 5, PAL_SLATE_DK);
        poso.FillCircle(12, 12, 3, new Color32(42, 96, 72, 255));
        poso.FillRect(11, 14, 2, 7, PAL_SLATE_LT); // Lever handle
        poso.FillRect(10, 6, 4, 3, PAL_RUST_MD);  // Spout
        poso.AddPixelOutline(PAL_OUTLINE);
        poso.SavePng("cemetery_poso.png");

        // 11. Stone Memorial Bench (40x16)
        PixelCanvas bench = new PixelCanvas(40, 16);
        bench.FillRect(4, 3, 6, 10, PAL_STONE_DK);
        bench.FillRect(30, 3, 6, 10, PAL_STONE_DK);
        bench.FillDitheredRect(2, 4, 36, 8, PAL_STONE_MD, 841, 0.12f);
        bench.StrokeRect(2, 4, 36, 8, 1, PAL_STONE_LT);
        bench.AddPixelOutline(PAL_OUTLINE);
        bench.SavePng("cemetery_bench.png");

        // 12. Caretaker's Kariton Handcart (40x28)
        PixelCanvas cart = new PixelCanvas(40, 28);
        // Wheels
        cart.FillRect(12, 2, 10, 3, PAL_OUTLINE);
        cart.FillRect(12, 23, 10, 3, PAL_OUTLINE);
        // Push handles extending east
        cart.FillRect(28, 7, 9, 2, PAL_WOOD_DK);
        cart.FillRect(28, 19, 9, 2, PAL_WOOD_DK);
        // Wooden slatted bed
        cart.FillDitheredRect(4, 5, 25, 18, PAL_WOOD_MD, 845, 0.14f);
        cart.StrokeRect(4, 5, 25, 18, 2, PAL_WOOD_DK);
        for (int cy = 9; cy <= 19; cy += 5)
            cart.FillRect(6, cy, 21, 1, PAL_WOOD_LT);
        // Shovel & broom in cart
        cart.DrawLine(7, 8, 24, 18, PAL_SLATE_LT);
        cart.FillRect(22, 16, 4, 4, PAL_RUST_MD);
        cart.AddPixelOutline(PAL_OUTLINE);
        cart.SavePng("cemetery_handcart.png");

        // 13. Dried Leaves & Withered Petals Scatter (48x48)
        PixelCanvas leaves = new PixelCanvas(48, 48);
        Color32[] leafCols =
        {
            new Color32(78, 56, 32, 220),
            new Color32(54, 42, 26, 210),
            new Color32(94, 44, 48, 210),
            new Color32(44, 58, 32, 210)
        };
        for (int i = 0; i < 24; i++)
        {
            int lx = 6 + Mathf.FloorToInt(Hash01(i, 1, 851) * 36);
            int ly = 6 + Mathf.FloorToInt(Hash01(i, 2, 852) * 36);
            Color32 lc = leafCols[i % leafCols.Length];
            leaves.FillRect(lx, ly, 3, 2, lc, true);
            leaves.Set(lx + 2, ly + 1, Shade(lc, 1.15f));
        }
        leaves.SavePng("cemetery_trash_leaves.png");
    }

    private enum CemeteryPropKind { CeramicVase, CandleJar, WateringCan }

    private static void GenerateCemeteryPropSheets()
    {
        BuildPropSheet("Props/prop_vase_sheet.png", new Color32(158, 146, 128, 255), CemeteryPropKind.CeramicVase);
        BuildPropSheet("Props/prop_candle_jar_sheet.png", new Color32(116, 164, 184, 255), CemeteryPropKind.CandleJar);
        BuildPropSheet("Props/prop_watering_can_sheet.png", new Color32(96, 62, 48, 255), CemeteryPropKind.WateringCan);
    }

    private static void BuildPropSheet(string relPath, Color32 mainCol, CemeteryPropKind kind)
    {
        const int fw = 24, fh = 24, frames = 6;
        PixelCanvas sheet = new PixelCanvas(fw * frames, fh);

        for (int f = 0; f < frames; f++)
        {
            int ox = f * fw;
            int cy = 12;

            if (f == 1 || f == 2)
            {
                int lift = f == 1 ? 2 : 4;
                sheet.FillCircle(ox + 12, cy - 3, 5, new Color32(0, 0, 0, 120), true);
                sheet.FillCircle(ox + 12, cy + lift, 6, new Color32(120, 195, 220, 85), true);
                DrawPropShape(sheet, ox + 12, cy + lift, kind, mainCol, knocked: false);
            }
            else if (f == 3)
            {
                DrawPropShape(sheet, ox + 13, cy - 1, kind, Shade(mainCol, 0.85f), knocked: true);
            }
            else if (f == 4)
            {
                Color32 burst = new Color32(204, 198, 184, 210);
                Color32 darkShard = Shade(mainCol, 0.65f);
                for (int a = 0; a < 8; a++)
                {
                    float ang = a * Mathf.PI * 0.25f;
                    int px = ox + 12 + Mathf.RoundToInt(Mathf.Cos(ang) * 7f);
                    int py = cy + Mathf.RoundToInt(Mathf.Sin(ang) * 7f);
                    sheet.FillRect(px - 1, py - 1, 3, 3, (a % 2 == 0) ? mainCol : burst, true);
                }
                sheet.FillCircle(ox + 12, cy, 3, darkShard);
            }
            else if (f == 5)
            {
                sheet.FillRect(ox + 6, cy - 4, 4, 3, mainCol);
                sheet.FillRect(ox + 14, cy + 2, 5, 3, Shade(mainCol, 0.80f));
                sheet.FillRect(ox + 8, cy + 4, 3, 3, Shade(mainCol, 1.12f));
                sheet.FillRect(ox + 12, cy - 6, 4, 2, Shade(mainCol, 0.60f));
                if (kind == CemeteryPropKind.CeramicVase)
                {
                    sheet.FillRect(ox + 9, cy - 1, 3, 2, new Color32(168, 52, 62, 220));
                }
            }
            else
            {
                DrawPropShape(sheet, ox + 12, cy, kind, mainCol, knocked: false);
            }
        }

        sheet.AddPixelOutline(PAL_OUTLINE, 180);
        sheet.SavePng(relPath);
    }

    private static void DrawPropShape(PixelCanvas c, int cx, int cy, CemeteryPropKind kind, Color32 col, bool knocked)
    {
        switch (kind)
        {
            case CemeteryPropKind.CeramicVase:
                if (!knocked)
                {
                    c.FillCircle(cx, cy, 5, col);
                    c.FillCircle(cx, cy, 2, PAL_MOSS_DK);
                    c.FillCircle(cx - 1, cy + 1, 2, new Color32(176, 56, 68, 255));
                    c.FillCircle(cx + 2, cy - 1, 1, new Color32(218, 184, 72, 255));
                }
                else
                {
                    c.FillRect(cx - 5, cy - 3, 8, 6, col);
                    c.FillRect(cx + 3, cy - 1, 3, 3, new Color32(176, 56, 68, 255));
                }
                break;

            case CemeteryPropKind.CandleJar:
                if (!knocked)
                {
                    c.FillCircle(cx, cy, 4, col);
                    c.FillCircle(cx, cy, 2, new Color32(228, 186, 98, 255));
                    c.Set(cx, cy, new Color32(255, 232, 142, 255));
                }
                else
                {
                    c.FillRect(cx - 5, cy - 2, 8, 5, col);
                    c.FillRect(cx - 3, cy - 1, 3, 3, new Color32(204, 190, 154, 255));
                }
                break;

            case CemeteryPropKind.WateringCan:
                if (!knocked)
                {
                    c.FillCircle(cx, cy, 5, col);
                    c.FillCircle(cx, cy, 2, new Color32(48, 72, 84, 255));
                    c.FillRect(cx + 4, cy - 1, 5, 2, PAL_SLATE_LT); // Spout
                    c.FillRect(cx - 6, cy - 2, 2, 4, PAL_SLATE_DK); // Handle
                }
                else
                {
                    c.FillRect(cx - 4, cy - 4, 8, 7, col);
                    c.FillRect(cx + 4, cy, 4, 2, PAL_SLATE_LT);
                }
                break;
        }
    }
}
