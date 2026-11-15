using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates pure 90-degree top-down 16-bit pixel art sprite sheets (aligned with
/// Lobby &amp; Equipment dark mahogany/gunmetal/brass horror aesthetic) and procedural
/// 16-bit WAV audio clips for BarangayMap, the 16-bit Jeepney, streetlamps, and weather.
/// </summary>
public static class BarangayAssetGenerator
{
    private const string SPRITE_DIR = "Assets/Resources/Sprites/BarangayMap";
    private const string AUDIO_DIR = "Assets/Resources/Audio";
    private const string RETRO_SFX_DIR = "Assets/Audio/Sound Effects/Retro16Bit";

    // Benchmark Lobby / Equipment horror palette tokens
    private static readonly Color32 PAL_OUTLINE = new Color32(16, 12, 14, 255);
    private static readonly Color32 PAL_MAHOGANY_DK = new Color32(36, 22, 14, 255);
    private static readonly Color32 PAL_MAHOGANY_MD = new Color32(68, 44, 28, 255);
    private static readonly Color32 PAL_MAHOGANY_LT = new Color32(102, 68, 42, 255);
    private static readonly Color32 PAL_SLATE_DK = new Color32(22, 24, 30, 255);
    private static readonly Color32 PAL_SLATE_MD = new Color32(46, 50, 58, 255);
    private static readonly Color32 PAL_SLATE_LT = new Color32(78, 84, 94, 255);
    private static readonly Color32 PAL_BRASS = new Color32(178, 140, 62, 255);
    private static readonly Color32 PAL_RUST = new Color32(78, 42, 28, 255);

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

        public Color32 Get(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return new Color32(0, 0, 0, 0);
            return Pixels[y * Width + x];
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

        /// <summary>Adds a crisp 1px dark outline around solid foreground shapes.</summary>
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
            SavePngAtAssetPath(fullPath);
        }

        public void SavePngAtAssetPath(string fullAssetPath)
        {
            string fullPath = fullAssetPath.Replace('\\', '/');
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.SetPixels32(Pixels);
            tex.Apply();
            File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
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

    [MenuItem("Pamahiin/Generate Barangay 16-Bit Assets")]
    public static void GenerateAllAssets()
    {
        GenerateExteriorSprites();
        GenerateHouseSprites();
        GenerateFurnitureSprites();
        GeneratePropSpriteSheets();
        GenerateJeepneySprites();
        GenerateWeatherSpriteSheets();
        GenerateSummoningCircleSheet();
        GenerateGhostDeathSheet();
        GenerateAudioClips();
        AssetDatabase.Refresh();
        Debug.Log("[BarangayAssetGenerator] Generated all 16-bit top-down sprites, ritual/death sprite sheets, Jeepney assets, and WAV audio clips.");
    }

    [MenuItem("Pamahiin/Generate Ritual & Ghost Death Assets")]
    public static void GenerateRitualAndGhostDeathAssets()
    {
        if (!Directory.Exists(AUDIO_DIR)) Directory.CreateDirectory(AUDIO_DIR);
        if (!Directory.Exists(RETRO_SFX_DIR)) Directory.CreateDirectory(RETRO_SFX_DIR);
        GenerateSummoningCircleSheet();
        GenerateGhostDeathSheet();
        GenerateGhostDyingShriekWav();
        AssetDatabase.Refresh();
        Debug.Log("[BarangayAssetGenerator] Generated summoning_circle_sheet.png, ghost_death_sheet.png, and ghost_dying_shriek.wav.");
    }

    // -----------------------------------------------------------------------
    // 1. Exterior (Road, Streetlamps, Trash, Tricycle, Packed Philippine Houses)
    // -----------------------------------------------------------------------
    private static void GenerateExteriorSprites()
    {
        // Vertical 2-lane wet midnight barangay asphalt road + weathered sidewalks (128x128)
        PixelCanvas road = new PixelCanvas(128, 128);
        Color32 baseAsphalt = new Color32(22, 24, 30, 255);
        Color32 sidewalk = new Color32(56, 54, 52, 255);
        Color32 curbEdge = new Color32(34, 34, 36, 255);
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                float h = Hash01(x, y, 101);
                if (x < 16 || x >= 112)
                {
                    float g = 0.88f + h * 0.22f;
                    Color32 sc = Shade(sidewalk, g);
                    if (x == 15 || x == 112) sc = curbEdge;
                    else if (x == 14 || x == 113) sc = Shade(sidewalk, 1.15f);
                    if (y % 32 == 0) sc = new Color32(26, 26, 28, 255);
                    else if (y % 32 == 1 && h > 0.65f) sc = new Color32(34, 46, 30, 255); // Moss crack
                    road.Set(x, y, sc);
                }
                else
                {
                    float g = 0.86f + h * 0.26f;
                    Color32 ac = Shade(baseAsphalt, g);
                    if ((x >= 34 && x <= 41) || (x >= 87 && x <= 94)) ac = Shade(ac, 0.80f); // Damp tire wear
                    if (h > 0.985f) ac = new Color32(14, 15, 19, 255);
                    if ((x == 16 || x == 111) && (y % 7 < 4)) ac = Shade(ac, 0.72f); // Gutter shadow
                    road.Set(x, y, ac);
                }
            }
        }
        Color32 dash = new Color32(142, 118, 62, 185);
        for (int sy = 4; sy < 128; sy += 32)
        {
            road.FillRect(62, sy, 4, 16, dash, true);
            road.FillRect(62, sy + 2, 4, 2, new Color32(24, 26, 30, 120), true); // Worn chip
        }
        // Dark rusted storm drain grates
        for (int i = 0; i < 2; i++)
        {
            int gx = i == 0 ? 16 : 105;
            int gy = i == 0 ? 18 : 82;
            road.FillRect(gx, gy, 7, 16, new Color32(12, 13, 16, 255));
            road.StrokeRect(gx, gy, 7, 16, 1, new Color32(36, 38, 44, 255));
            for (int ry = gy + 2; ry < gy + 15; ry += 3)
                road.FillRect(gx + 1, ry, 5, 1, new Color32(46, 50, 56, 255));
        }
        road.SavePng("Exterior/asphalt_road.png");

        // Top-down cast-iron barangay streetlamp post (24x56, base anchored down-side, lantern head overhanging)
        PixelCanvas lamp = new PixelCanvas(24, 56);
        // Subtle ground shadow under pole
        lamp.FillEllipse(12, 16, 6, 4, new Color32(10, 10, 14, 160), true);
        // Iron octagonal pedestal base
        lamp.FillRect(8, 10, 8, 8, PAL_SLATE_DK);
        lamp.FillRect(9, 11, 6, 6, PAL_SLATE_MD);
        lamp.FillRect(10, 12, 4, 4, PAL_SLATE_LT);
        // Vertical pole & curved overhang bracket toward road center
        lamp.FillRect(10, 16, 4, 22, PAL_SLATE_DK);
        lamp.FillRect(11, 17, 2, 20, PAL_SLATE_MD);
        lamp.FillRect(10, 26, 4, 2, PAL_RUST); // Rust collar band
        // Lantern head cage + warm frosted glass & filament core
        lamp.FillRect(7, 36, 10, 12, PAL_SLATE_DK);
        lamp.FillRect(8, 37, 8, 10, new Color32(182, 132, 62, 255));
        lamp.FillRect(9, 38, 6, 8, new Color32(236, 198, 112, 255));
        lamp.FillRect(11, 40, 2, 4, new Color32(255, 242, 196, 255));
        lamp.StrokeRect(7, 36, 10, 12, 1, PAL_OUTLINE);
        // Cross rib bars on lantern top cap
        lamp.FillRect(8, 41, 8, 1, PAL_SLATE_DK);
        lamp.FillRect(11, 37, 1, 10, PAL_SLATE_DK);
        lamp.AddPixelOutline(PAL_OUTLINE);
        lamp.SavePng("Exterior/lamp_post.png");

        // Roadside Trash scatter (64x64)
        PixelCanvas trash = new PixelCanvas(64, 64);
        trash.FillEllipse(20, 24, 9, 6, new Color32(22, 24, 30, 255));
        trash.FillEllipse(18, 26, 6, 4, new Color32(38, 42, 50, 255));
        trash.FillRect(24, 18, 4, 3, new Color32(138, 128, 102, 255));
        trash.FillRect(32, 26, 14, 10, new Color32(94, 68, 44, 255));
        trash.StrokeRect(32, 26, 14, 10, 1, new Color32(54, 38, 24, 255));
        trash.FillRect(35, 30, 8, 1, new Color32(46, 32, 20, 255));
        trash.FillRect(12, 38, 7, 5, new Color32(128, 38, 36, 255));
        trash.FillRect(44, 38, 6, 4, new Color32(40, 76, 114, 255));
        trash.FillRect(26, 42, 3, 5, new Color32(114, 120, 126, 255));
        trash.FillRect(37, 14, 4, 2, new Color32(48, 76, 38, 255));
        trash.AddPixelOutline(PAL_OUTLINE);
        trash.SavePng("Exterior/trash_scatter.png");

        // Top-Down Philippine Tricycle oriented vertically (48x64)
        PixelCanvas trike = new PixelCanvas(48, 64);
        trike.FillRect(6, 8, 36, 48, new Color32(0, 0, 0, 130), true);
        trike.FillRect(8, 14, 20, 34, new Color32(132, 94, 28, 255));
        trike.StrokeRect(8, 14, 20, 34, 2, new Color32(68, 46, 14, 255));
        trike.FillRect(22, 18, 4, 26, new Color32(30, 62, 98, 255));
        for (int ry = 18; ry < 44; ry += 5) trike.FillRect(8, ry, 20, 1, new Color32(98, 68, 20, 255));
        trike.FillRect(5, 28, 4, 9, PAL_OUTLINE);
        trike.FillRect(33, 8, 5, 11, PAL_OUTLINE);
        trike.FillRect(33, 43, 5, 11, PAL_OUTLINE);
        trike.FillRect(32, 14, 7, 32, new Color32(98, 26, 28, 255));
        trike.FillRect(33, 20, 5, 14, PAL_SLATE_DK);
        trike.FillRect(28, 41, 14, 3, new Color32(126, 132, 140, 255));
        trike.FillRect(33, 51, 4, 3, new Color32(214, 192, 112, 255));
        trike.AddPixelOutline(PAL_OUTLINE);
        trike.SavePng("Exterior/tricycle_topdown.png");

        // Packed Neighbor House Roofs (deep moody storm-slate & rust)
        SaveNeighborRoof("Exterior/neighbor_house_firewall_1.png", 112, 68, hasFirewall: true, variant: 0);
        SaveNeighborRoof("Exterior/neighbor_house_firewall_2.png", 80, 104, hasFirewall: true, variant: 1);
        SaveNeighborRoof("Exterior/neighbor_house_nofirewall_1.png", 104, 68, hasFirewall: false, variant: 0);
        SaveNeighborRoof("Exterior/neighbor_house_nofirewall_2.png", 80, 104, hasFirewall: false, variant: 1);
        SaveNeighborRoof("Exterior/neighbor_roof_ragged_1.png", 80, 96, hasFirewall: false, variant: 2);
        SaveNeighborRoof("Exterior/neighbor_roof_ragged_2.png", 96, 68, hasFirewall: true, variant: 3);
    }

    private static void SaveNeighborRoof(string path, int w, int h, bool hasFirewall, int variant)
    {
        PixelCanvas c = new PixelCanvas(w, h);
        Color32 baseYero = (variant % 2 == 0)
            ? new Color32(44, 50, 58, 255)
            : new Color32(40, 54, 48, 255);
        Color32 rustTone = new Color32(72, 40, 26, 255);
        Color32 blueTarp = new Color32(22, 48, 86, 255);
        Color32 hollowBlock = new Color32(68, 66, 64, 255);

        int inset = hasFirewall ? 5 : 1;
        for (int y = inset; y < h - inset; y++)
        {
            for (int x = inset; x < w - inset; x++)
            {
                if (!hasFirewall && variant >= 2)
                {
                    if ((x < 4 || x > w - 5 || y < 4 || y > h - 5) && Hash01(x / 3, y / 3, 500 + variant) > 0.58f)
                        continue;
                }

                bool rib = ((variant % 2 == 0 ? x : y) % 4) < 2;
                Color32 px = Shade(baseYero, rib ? 1.08f : 0.82f);

                float patchNoise = Hash01(x / 6, y / 6, 300 + variant * 17);
                if (patchNoise > 0.68f) px = Shade(rustTone, rib ? 1.06f : 0.80f);
                else if (variant != 1 && x > w / 2 && y < h / 2 && patchNoise < 0.24f) px = Shade(blueTarp, rib ? 1.08f : 0.86f);

                if ((y % 28 == 0) || (x % 40 == 0)) px = Shade(px, 0.66f);
                c.Set(x, y, px);
            }
        }

        int tx = w / 3;
        int ty = h / 2;
        c.FillCircle(tx, ty, 5, PAL_OUTLINE);
        c.FillCircle(tx, ty, 2, baseYero);
        c.FillRect(w - inset - 16, inset + 10, 7, 4, hollowBlock);
        c.StrokeRect(w - inset - 16, inset + 10, 7, 4, 1, PAL_OUTLINE);
        c.FillRect(inset + 14, h - inset - 16, 6, 4, hollowBlock);
        c.StrokeRect(inset + 14, h - inset - 16, 6, 4, 1, PAL_OUTLINE);

        if (hasFirewall)
        {
            c.StrokeRect(0, 0, w, h, 5, new Color32(66, 64, 62, 255));
            c.StrokeRect(1, 1, w - 2, h - 2, 1, PAL_SLATE_DK);
            for (int y = 0; y < h; y += 8)
            {
                c.FillRect(0, y, 5, 1, new Color32(42, 40, 40, 255));
                c.FillRect(w - 5, y, 5, 1, new Color32(42, 40, 40, 255));
            }
        }
        else
        {
            c.StrokeRect(inset, inset, w - inset * 2, h - inset * 2, 1, PAL_OUTLINE);
        }

        c.SavePng(path);
    }

    // -----------------------------------------------------------------------
    // 2. Main House & Basement Floors, Walls, Doors, Cabinets, Stairs & Roof
    // -----------------------------------------------------------------------
    private static void GenerateHouseSprites()
    {
        // Deep roasted mahogany planks matching lobby_floor.png (40,32,24 / 24,16,8)
        PixelCanvas wood = new PixelCanvas(64, 64);
        Color32 woodBase = new Color32(52, 36, 24, 255);
        for (int y = 0; y < 64; y++)
        {
            int plankRow = y / 8;
            float rowTint = 0.88f + Hash01(0, plankRow, 12) * 0.22f;
            for (int x = 0; x < 64; x++)
            {
                bool seam = (y % 8 == 0) || ((x + plankRow * 19) % 32 == 0);
                bool highlight = (y % 8 == 1);
                float grain = 0.90f + Hash01(x / 3, y, 22) * 0.18f;
                Color32 px = seam
                    ? new Color32(20, 14, 10, 255)
                    : highlight ? Shade(woodBase, rowTint * 1.14f * grain) : Shade(woodBase, rowTint * grain);
                wood.Set(x, y, px);
            }
        }
        wood.SavePng("House/floor_wood_tile.png");

        // Bathroom & Kitchen cold stained ceramic tiles
        PixelCanvas tile = new PixelCanvas(64, 64);
        Color32 tileA = new Color32(72, 78, 78, 255);
        Color32 tileB = new Color32(62, 68, 68, 255);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                bool grout = (x % 16 == 0) || (y % 16 == 0);
                bool checker = ((x / 16) + (y / 16)) % 2 == 0;
                float speckle = 0.92f + Hash01(x, y, 33) * 0.14f;
                Color32 px = grout ? new Color32(30, 34, 34, 255) : Shade(checker ? tileA : tileB, speckle);
                if (!grout && Hash01(x / 5, y / 5, 37) > 0.84f) px = Shade(px, 0.86f); // Grime blotch
                tile.Set(x, y, px);
            }
        }
        tile.SavePng("House/floor_bathroom_tile.png");

        // Garage concrete
        PixelCanvas garage = new PixelCanvas(64, 64);
        Color32 gBase = new Color32(46, 48, 50, 255);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float n = 0.86f + Hash01(x, y, 44) * 0.24f;
                Color32 px = Shade(gBase, n);
                if (x == 32 || y == 32) px = Shade(px, 0.68f);
                garage.Set(x, y, px);
            }
        }
        garage.FillEllipse(30, 34, 9, 7, new Color32(22, 20, 20, 185), true);
        garage.SavePng("House/floor_garage_concrete.png");

        // Basement damp stained concrete
        PixelCanvas bsm = new PixelCanvas(64, 64);
        Color32 bBase = new Color32(38, 42, 46, 255);
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float n = 0.84f + Hash01(x, y, 55) * 0.26f;
                Color32 px = Shade(bBase, n);
                if (Hash01(x / 4, y / 4, 59) > 0.74f) px = Shade(px, 0.74f);
                bsm.Set(x, y, px);
            }
        }
        bsm.SavePng("House/floor_basement_concrete.png");

        // Porch stone/timber veranda
        PixelCanvas porch = new PixelCanvas(64, 64);
        Color32 pBase = new Color32(64, 56, 48, 255);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                porch.Set(x, y, (x % 32 == 0 || y % 32 == 0)
                    ? new Color32(26, 22, 18, 255)
                    : Shade(pBase, 0.88f + Hash01(x, y, 61) * 0.18f));
        porch.SavePng("House/floor_porch_tile.png");

        // Top-down Concrete Wall Cap (16x16 @ 256 PPU -> fills wt = 0.055 cleanly with lifted plum-slate contrast)
        PixelCanvas wall = new PixelCanvas(16, 16);
        wall.FillDitheredRect(0, 0, 16, 16, new Color32(48, 42, 56, 255), 71, 0.16f);
        wall.StrokeRect(0, 0, 16, 16, 2, new Color32(16, 13, 20, 255));
        wall.StrokeRect(2, 2, 12, 12, 1, new Color32(76, 68, 88, 255)); // Bevel highlight
        wall.FillDitheredRect(3, 3, 10, 10, new Color32(40, 35, 48, 255), 73, 0.14f);
        wall.SavePng("House/wall_concrete_top.png");

        // Main House Roof Overlay (toggled by RoofFader)
        PixelCanvas mainRoof = new PixelCanvas(128, 128);
        Color32 mBase = new Color32(38, 44, 52, 255);
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                bool rib = (x % 4 < 2);
                Color32 px = Shade(mBase, rib ? 1.08f : 0.82f);
                if (y == 64 || x == 64) px = Shade(px, 0.72f);
                if (Hash01(x / 8, y / 8, 77) > 0.78f) px = Shade(PAL_RUST, rib ? 1.05f : 0.82f);
                mainRoof.Set(x, y, px);
            }
        }
        mainRoof.StrokeRect(0, 0, 128, 128, 3, PAL_OUTLINE);
        mainRoof.SavePng("House/roof_main_gi.png");

        // Top-Down Oak/Mahogany Door (hinge at x=0..3, doorknob at x=25..28)
        PixelCanvas dClosed = new PixelCanvas(32, 12);
        dClosed.FillDitheredRect(1, 2, 30, 8, new Color32(84, 52, 30, 255), 81, 0.12f);
        dClosed.FillRect(1, 8, 30, 1, new Color32(108, 70, 42, 255)); // Top beveled grain
        dClosed.FillRect(1, 3, 30, 1, PAL_MAHOGANY_DK);
        dClosed.StrokeRect(0, 1, 32, 10, 1, PAL_OUTLINE);
        dClosed.FillRect(1, 2, 3, 8, PAL_SLATE_DK); // Hinge bracket at left pivot edge
        dClosed.FillRect(25, 0, 3, 2, PAL_BRASS); // Brass knob front/back
        dClosed.FillRect(25, 10, 3, 2, PAL_BRASS);
        dClosed.SavePng("House/door_wood_closed.png");

        PixelCanvas dOpen = new PixelCanvas(32, 12);
        dOpen.FillRect(1, 2, 5, 8, PAL_MAHOGANY_MD);
        dOpen.FillRect(26, 2, 5, 8, PAL_MAHOGANY_MD);
        dOpen.SavePng("House/door_wood_open.png");

        // Top-Down Mahogany Closet Cabinet Closed & Open
        PixelCanvas cClosed = new PixelCanvas(32, 16);
        cClosed.FillDitheredRect(1, 2, 30, 12, new Color32(72, 44, 26, 255), 85, 0.12f);
        cClosed.StrokeRect(0, 1, 32, 14, 1, PAL_OUTLINE);
        cClosed.FillRect(15, 2, 2, 12, PAL_OUTLINE);
        for (int lx = 4; lx < 14; lx += 3) cClosed.FillRect(lx, 4, 1, 8, PAL_MAHOGANY_DK);
        for (int rx = 19; rx < 28; rx += 3) cClosed.FillRect(rx, 4, 1, 8, PAL_MAHOGANY_DK);
        cClosed.FillRect(13, 1, 2, 2, PAL_BRASS);
        cClosed.FillRect(17, 1, 2, 2, PAL_BRASS);
        cClosed.SavePng("House/cabinet_closed.png");

        PixelCanvas cOpen = new PixelCanvas(32, 16);
        cOpen.FillRect(2, 3, 28, 10, PAL_OUTLINE);
        cOpen.FillRect(1, 1, 4, 14, PAL_MAHOGANY_MD);
        cOpen.FillRect(27, 1, 4, 14, PAL_MAHOGANY_MD);
        cOpen.SavePng("House/cabinet_open.png");

        // Top-Down Exterior Window Frame (32x10)
        PixelCanvas win = new PixelCanvas(32, 10);
        win.FillRect(0, 0, 32, 10, PAL_OUTLINE);
        win.StrokeRect(1, 1, 30, 8, 1, PAL_SLATE_MD);
        win.FillRect(2, 2, 28, 6, new Color32(42, 68, 86, 255));
        win.FillRect(15, 1, 2, 8, PAL_SLATE_DK);
        win.FillRect(4, 3, 8, 2, new Color32(106, 152, 178, 190), true);
        win.FillRect(19, 3, 8, 2, new Color32(106, 152, 178, 190), true);
        win.SavePng("House/window_frame_top.png");

        // Horizontal Top-Down Stairwells (96x32)
        SaveStairwell("House/stairwell_down.png", goingDown: true);
        SaveStairwell("House/stairwell_up.png", goingDown: false);
    }

    private static void SaveStairwell(string path, bool goingDown)
    {
        PixelCanvas s = new PixelCanvas(96, 32, PAL_OUTLINE);
        s.StrokeRect(0, 0, 96, 32, 2, PAL_SLATE_MD);
        int steps = 10;
        for (int i = 0; i < steps; i++)
        {
            int sx = 3 + i * 9;
            float t = i / (float)(steps - 1);
            float shade = goingDown ? Mathf.Lerp(1.05f, 0.30f, t) : Mathf.Lerp(0.32f, 1.05f, t);
            Color32 stepCol = Shade(PAL_MAHOGANY_MD, shade);
            s.FillRect(sx, 4, 8, 24, stepCol);
            s.FillRect(sx, 4, 1, 24, PAL_OUTLINE);
        }
        s.FillRect(2, 2, 92, 2, new Color32(118, 96, 52, 255));
        s.FillRect(2, 28, 92, 2, new Color32(118, 96, 52, 255));
        Color32 arrow = goingDown ? new Color32(178, 146, 56, 200) : new Color32(92, 158, 128, 200);
        int tipX = goingDown ? 80 : 16;
        int dir = goingDown ? -1 : 1;
        s.FillRect(tipX, 15, 2, 2, arrow, true);
        s.FillRect(tipX + dir * 2, 13, 2, 6, arrow, true);
        s.FillRect(tipX + dir * 4, 11, 2, 10, arrow, true);
        s.FillRect(tipX + dir * 6, 15, 10, 2, arrow, true);
        s.SavePng(path);
    }

    // -----------------------------------------------------------------------
    // 3. Pure 90-Degree Top-Down Room Furniture (Dark Mahogany/Slate/Brass)
    // -----------------------------------------------------------------------
    private static void GenerateFurnitureSprites()
    {
        // Bed (48x36)
        PixelCanvas bed = new PixelCanvas(48, 36);
        bed.FillDitheredRect(1, 1, 46, 34, PAL_MAHOGANY_MD, 201, 0.1f);
        bed.FillRect(3, 3, 42, 30, new Color32(118, 114, 108, 255));
        bed.FillRect(4, 6, 10, 10, new Color32(146, 142, 136, 255));
        bed.StrokeRect(4, 6, 10, 10, 1, new Color32(82, 78, 74, 255));
        bed.FillRect(4, 20, 10, 10, new Color32(146, 142, 136, 255));
        bed.StrokeRect(4, 20, 10, 10, 1, new Color32(82, 78, 74, 255));
        bed.FillDitheredRect(17, 3, 28, 30, new Color32(38, 48, 64, 255), 202, 0.1f);
        bed.StrokeRect(17, 3, 28, 30, 1, PAL_OUTLINE);
        bed.AddPixelOutline(PAL_OUTLINE);
        bed.SavePng("House/Furniture/furn_bed.png");

        // Nursery Crib (32x24)
        PixelCanvas crib = new PixelCanvas(32, 24);
        crib.FillRect(2, 2, 28, 20, PAL_MAHOGANY_LT);
        crib.FillRect(4, 4, 24, 16, new Color32(76, 92, 108, 255));
        crib.FillRect(5, 6, 7, 12, new Color32(144, 142, 138, 255));
        crib.StrokeRect(1, 1, 30, 22, 2, PAL_MAHOGANY_DK);
        crib.AddPixelOutline(PAL_OUTLINE);
        crib.SavePng("House/Furniture/furn_crib.png");

        // Sofa (48x24)
        PixelCanvas sofa = new PixelCanvas(48, 24);
        sofa.FillRect(2, 2, 44, 20, new Color32(68, 38, 34, 255));
        sofa.FillRect(4, 4, 40, 5, new Color32(48, 26, 24, 255));
        for (int cx = 5; cx <= 33; cx += 13)
        {
            sofa.FillDitheredRect(cx, 10, 11, 10, new Color32(84, 48, 42, 255), 205 + cx, 0.1f);
            sofa.StrokeRect(cx, 10, 11, 10, 1, PAL_OUTLINE);
        }
        sofa.FillRect(2, 4, 4, 17, new Color32(54, 30, 26, 255));
        sofa.FillRect(42, 4, 4, 17, new Color32(54, 30, 26, 255));
        sofa.AddPixelOutline(PAL_OUTLINE);
        sofa.SavePng("House/Furniture/furn_sofa.png");

        // Kitchen Counter + Sink / Stove (48x20)
        PixelCanvas counter = new PixelCanvas(48, 20);
        counter.FillDitheredRect(1, 1, 46, 18, new Color32(84, 80, 74, 255), 210, 0.1f);
        counter.StrokeRect(1, 1, 46, 18, 1, PAL_OUTLINE);
        counter.FillRect(5, 4, 14, 12, new Color32(106, 112, 118, 255));
        counter.FillRect(7, 6, 10, 8, PAL_SLATE_MD);
        counter.FillRect(11, 3, 2, 3, PAL_BRASS);
        counter.FillRect(28, 3, 16, 14, PAL_SLATE_DK);
        counter.FillCircle(32, 7, 2, PAL_OUTLINE);
        counter.FillCircle(40, 7, 2, PAL_OUTLINE);
        counter.FillCircle(32, 13, 2, PAL_OUTLINE);
        counter.FillCircle(40, 13, 2, PAL_OUTLINE);
        counter.AddPixelOutline(PAL_OUTLINE);
        counter.SavePng("House/Furniture/furn_counter_stove.png");

        // Dining Table + Chairs (40x32)
        PixelCanvas dining = new PixelCanvas(40, 32);
        for (int cx = 10; cx <= 26; cx += 12)
        {
            dining.FillRect(cx, 1, 8, 5, PAL_MAHOGANY_DK);
            dining.FillRect(cx, 26, 8, 5, PAL_MAHOGANY_DK);
        }
        dining.FillDitheredRect(4, 7, 32, 18, PAL_MAHOGANY_MD, 215, 0.1f);
        dining.StrokeRect(4, 7, 32, 18, 2, PAL_MAHOGANY_DK);
        dining.FillRect(16, 13, 8, 6, PAL_BRASS); // Center brass tray/runner accent
        dining.AddPixelOutline(PAL_OUTLINE);
        dining.SavePng("House/Furniture/furn_dining_table.png");

        // Bathroom Tub + Toilet + Sink (32x32)
        PixelCanvas bath = new PixelCanvas(32, 32);
        bath.FillRect(2, 2, 28, 14, new Color32(132, 136, 138, 255));
        bath.StrokeRect(2, 2, 28, 14, 1, PAL_SLATE_MD);
        bath.FillRect(5, 5, 22, 8, new Color32(68, 86, 94, 255));
        bath.FillRect(2, 19, 10, 11, new Color32(136, 140, 140, 255));
        bath.FillRect(4, 20, 6, 4, new Color32(106, 110, 112, 255));
        bath.FillRect(18, 20, 12, 10, new Color32(114, 118, 120, 255));
        bath.FillEllipse(24, 25, 4, 3, new Color32(64, 74, 78, 255));
        bath.AddPixelOutline(PAL_OUTLINE);
        bath.SavePng("House/Furniture/furn_bathroom_set.png");

        // Utility Washer/Dryer (40x24)
        PixelCanvas utility = new PixelCanvas(40, 24);
        utility.FillRect(2, 2, 16, 18, new Color32(114, 118, 124, 255));
        utility.StrokeRect(2, 2, 16, 18, 1, PAL_SLATE_MD);
        utility.FillCircle(10, 11, 5, PAL_SLATE_MD);
        utility.FillRect(21, 2, 17, 18, new Color32(108, 112, 118, 255));
        utility.StrokeRect(21, 2, 17, 18, 1, PAL_SLATE_MD);
        utility.FillRect(24, 5, 11, 3, PAL_SLATE_DK);
        utility.AddPixelOutline(PAL_OUTLINE);
        utility.SavePng("House/Furniture/furn_washer_dryer.png");

        // Garage Parked Car (64x36)
        PixelCanvas car = new PixelCanvas(64, 36);
        car.FillRect(3, 4, 58, 28, new Color32(42, 52, 66, 255));
        car.StrokeRect(3, 4, 58, 28, 2, PAL_SLATE_DK);
        car.FillRect(16, 7, 24, 22, new Color32(30, 38, 50, 255));
        car.FillRect(39, 8, 7, 20, new Color32(18, 24, 32, 255));
        car.FillRect(11, 8, 5, 20, new Color32(18, 24, 32, 255));
        car.FillRect(58, 6, 3, 5, new Color32(184, 172, 108, 255));
        car.FillRect(58, 25, 3, 5, new Color32(184, 172, 108, 255));
        car.AddPixelOutline(PAL_OUTLINE);
        car.SavePng("House/Furniture/furn_garage_car.png");

        // Workbench (48x24)
        PixelCanvas bench = new PixelCanvas(48, 24);
        bench.FillDitheredRect(2, 2, 44, 20, PAL_MAHOGANY_MD, 225, 0.1f);
        bench.StrokeRect(2, 2, 44, 20, 2, PAL_MAHOGANY_DK);
        bench.FillRect(8, 6, 12, 8, new Color32(98, 72, 46, 255));
        bench.FillRect(28, 5, 8, 10, PAL_SLATE_MD);
        bench.FillRect(30, 7, 4, 6, PAL_RUST);
        bench.AddPixelOutline(PAL_OUTLINE);
        bench.SavePng("House/Furniture/furn_workbench.png");

        // FuseBox (24x24)
        PixelCanvas fuse = new PixelCanvas(24, 24);
        fuse.FillRect(3, 3, 18, 18, PAL_SLATE_MD);
        fuse.StrokeRect(3, 3, 18, 18, 2, PAL_SLATE_DK);
        fuse.FillRect(7, 7, 10, 10, PAL_SLATE_DK);
        fuse.FillRect(9, 10, 6, 4, PAL_BRASS);
        fuse.AddPixelOutline(PAL_OUTLINE);
        fuse.SavePng("House/Furniture/furn_fusebox.png");

        // Kitchen Island (48x28)
        PixelCanvas island = new PixelCanvas(48, 28);
        island.FillDitheredRect(2, 2, 44, 24, new Color32(82, 78, 72, 255), 230, 0.08f);
        island.StrokeRect(2, 2, 44, 24, 2, PAL_MAHOGANY_DK);
        island.FillRect(6, 6, 14, 12, PAL_MAHOGANY_LT);
        island.FillRect(28, 7, 12, 10, PAL_SLATE_LT);
        island.AddPixelOutline(PAL_OUTLINE);
        island.SavePng("House/Furniture/furn_kitchen_island.png");

        // Refrigerator (24x24)
        PixelCanvas fridge = new PixelCanvas(24, 24);
        fridge.FillRect(2, 2, 20, 20, new Color32(118, 124, 130, 255));
        fridge.StrokeRect(2, 2, 20, 20, 2, PAL_SLATE_MD);
        fridge.FillRect(4, 18, 16, 2, PAL_SLATE_LT);
        fridge.AddPixelOutline(PAL_OUTLINE);
        fridge.SavePng("House/Furniture/furn_fridge.png");

        // Living Room Woven Rug (48x48)
        PixelCanvas rugLiving = new PixelCanvas(48, 48);
        rugLiving.FillDitheredRect(2, 2, 44, 44, new Color32(72, 32, 30, 255), 235, 0.1f);
        rugLiving.StrokeRect(4, 4, 40, 40, 2, new Color32(118, 88, 48, 255));
        rugLiving.StrokeRect(10, 10, 28, 28, 2, new Color32(48, 22, 20, 255));
        rugLiving.FillRect(18, 18, 12, 12, new Color32(98, 68, 42, 255));
        rugLiving.AddPixelOutline(PAL_OUTLINE);
        rugLiving.SavePng("House/Furniture/furn_rug_living.png");

        // Nursery Rug (36x36)
        PixelCanvas rugNursery = new PixelCanvas(36, 36);
        rugNursery.FillCircle(18, 18, 16, new Color32(54, 72, 92, 255));
        rugNursery.FillCircle(18, 18, 12, new Color32(72, 92, 112, 255));
        rugNursery.FillRect(16, 12, 4, 12, PAL_BRASS);
        rugNursery.FillRect(12, 16, 12, 4, PAL_BRASS);
        rugNursery.AddPixelOutline(PAL_OUTLINE);
        rugNursery.SavePng("House/Furniture/furn_rug_nursery.png");

        // Boys Bedroom Striped Rug (36x28)
        PixelCanvas rugBoys = new PixelCanvas(36, 28);
        rugBoys.FillRect(2, 2, 32, 24, new Color32(38, 52, 72, 255));
        for (int ry = 6; ry < 24; ry += 6) rugBoys.FillRect(4, ry, 28, 2, new Color32(76, 94, 114, 255));
        rugBoys.StrokeRect(2, 2, 32, 24, 1, PAL_SLATE_DK);
        rugBoys.AddPixelOutline(PAL_OUTLINE);
        rugBoys.SavePng("House/Furniture/furn_rug_boys.png");

        // TV Stand (40x16)
        PixelCanvas tv = new PixelCanvas(40, 16);
        tv.FillRect(2, 2, 36, 12, PAL_MAHOGANY_MD);
        tv.StrokeRect(2, 2, 36, 12, 1, PAL_MAHOGANY_DK);
        tv.FillRect(8, 5, 24, 5, PAL_OUTLINE);
        tv.FillRect(10, 6, 20, 2, new Color32(42, 62, 78, 255));
        tv.AddPixelOutline(PAL_OUTLINE);
        tv.SavePng("House/Furniture/furn_tv_stand.png");

        // Study Desk (32x20)
        PixelCanvas desk = new PixelCanvas(32, 20);
        desk.FillRect(2, 2, 28, 16, PAL_MAHOGANY_MD);
        desk.StrokeRect(2, 2, 28, 16, 1, PAL_MAHOGANY_DK);
        desk.FillRect(8, 6, 14, 8, new Color32(138, 134, 124, 255));
        desk.FillRect(23, 5, 4, 4, new Color32(38, 76, 114, 255));
        desk.AddPixelOutline(PAL_OUTLINE);
        desk.SavePng("House/Furniture/furn_desk.png");

        // Bedroom Dresser (36x18)
        PixelCanvas dresser = new PixelCanvas(36, 18);
        dresser.FillRect(2, 2, 32, 14, PAL_MAHOGANY_MD);
        dresser.StrokeRect(2, 2, 32, 14, 1, PAL_MAHOGANY_DK);
        dresser.FillRect(6, 5, 10, 8, PAL_SLATE_LT);
        dresser.FillRect(20, 5, 10, 8, PAL_BRASS);
        dresser.AddPixelOutline(PAL_OUTLINE);
        dresser.SavePng("House/Furniture/furn_dresser.png");

        // Basement Pillar (20x20)
        PixelCanvas pillar = new PixelCanvas(20, 20);
        pillar.FillRect(2, 2, 16, 16, PAL_SLATE_MD);
        pillar.StrokeRect(2, 2, 16, 16, 2, PAL_SLATE_DK);
        pillar.FillRect(5, 5, 10, 10, PAL_SLATE_LT);
        pillar.AddPixelOutline(PAL_OUTLINE);
        pillar.SavePng("House/Furniture/furn_pillar.png");
    }

    // -----------------------------------------------------------------------
    // 4. Multi-Frame Throwable/Breakable Prop Sprite Sheets (6 frames @ 24x24)
    // -----------------------------------------------------------------------
    private static void GeneratePropSpriteSheets()
    {
        BuildPropSheet("Props/prop_plate_sheet.png", new Color32(168, 164, 156, 255), PropKind.RoundPlate);
        BuildPropSheet("Props/prop_cup_sheet.png", new Color32(136, 114, 78, 255), PropKind.Cup);
        BuildPropSheet("Props/prop_glass_bottle_sheet.png", new Color32(52, 108, 72, 255), PropKind.Bottle);
        BuildPropSheet("Props/prop_picture_frame_sheet.png", PAL_MAHOGANY_LT, PropKind.RectFrame);
        BuildPropSheet("Props/prop_paint_can_sheet.png", PAL_SLATE_LT, PropKind.PaintCan);
        BuildPropSheet("Props/prop_chair_sheet.png", PAL_MAHOGANY_MD, PropKind.Chair);
        BuildPropSheet("Props/prop_tv_remote_sheet.png", PAL_SLATE_DK, PropKind.Remote);
        BuildPropSheet("Props/prop_shoe_sheet.png", PAL_MAHOGANY_DK, PropKind.Shoe);
    }

    private enum PropKind { RoundPlate, Cup, Bottle, RectFrame, PaintCan, Chair, Remote, Shoe }

    private static void BuildPropSheet(string path, Color32 mainCol, PropKind kind)
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
                if (kind == PropKind.PaintCan)
                    sheet.FillEllipse(ox + 13, cy, 7, 5, new Color32(138, 42, 38, 210), true);
            }
            else
            {
                DrawPropShape(sheet, ox + 12, cy, kind, mainCol, knocked: false);
            }
        }

        sheet.AddPixelOutline(PAL_OUTLINE, 180);
        sheet.SavePng(path);
    }

    private static void DrawPropShape(PixelCanvas c, int cx, int cy, PropKind kind, Color32 col, bool knocked)
    {
        Color32 edge = Shade(col, 0.55f);
        switch (kind)
        {
            case PropKind.RoundPlate:
                if (!knocked) { c.FillCircle(cx, cy, 6, col); c.FillCircle(cx, cy, 3, Shade(col, 0.85f)); }
                else c.FillEllipse(cx, cy, 6, 3, col);
                break;
            case PropKind.Cup:
                if (!knocked) { c.FillCircle(cx, cy, 4, col); c.FillCircle(cx, cy, 2, PAL_MAHOGANY_DK); c.FillRect(cx + 3, cy - 1, 3, 2, edge); }
                else { c.FillRect(cx - 4, cy - 2, 7, 5, col); c.FillEllipse(cx + 4, cy, 4, 2, new Color32(46, 30, 18, 175), true); }
                break;
            case PropKind.Bottle:
                if (!knocked) { c.FillCircle(cx, cy, 4, col); c.FillCircle(cx, cy, 1, new Color32(142, 184, 152, 255)); }
                else { c.FillRect(cx - 6, cy - 2, 9, 5, col); c.FillRect(cx + 3, cy - 1, 4, 3, Shade(col, 1.15f)); }
                break;
            case PropKind.RectFrame:
                int rw = knocked ? 10 : 12, rh = knocked ? 12 : 9;
                c.FillRect(cx - rw / 2, cy - rh / 2, rw, rh, col);
                c.FillRect(cx - rw / 2 + 2, cy - rh / 2 + 2, rw - 4, rh - 4, new Color32(124, 118, 108, 255));
                break;
            case PropKind.PaintCan:
                if (!knocked) { c.FillCircle(cx, cy, 5, col); c.FillCircle(cx, cy, 3, new Color32(138, 42, 38, 255)); }
                else { c.FillRect(cx - 5, cy - 4, 10, 8, col); c.FillEllipse(cx + 5, cy, 4, 3, new Color32(138, 42, 38, 220), true); }
                break;
            case PropKind.Chair:
                c.FillRect(cx - 5, cy - 5, 10, 10, col);
                c.FillRect(cx - 5, cy + (knocked ? -5 : 3), 10, 3, edge);
                break;
            case PropKind.Remote:
                if (!knocked) c.FillRect(cx - 2, cy - 5, 5, 11, col);
                else c.FillRect(cx - 5, cy - 2, 11, 5, col);
                c.Set(cx, cy + (knocked ? 0 : 3), new Color32(174, 42, 42, 255));
                break;
            case PropKind.Shoe:
                int sw = knocked ? 9 : 5, sh = knocked ? 5 : 9;
                c.FillRect(cx - sw / 2, cy - sh / 2, sw, sh, col);
                c.FillRect(cx - sw / 2 + 1, cy - sh / 2 + 1, sw - 2, 2, new Color32(136, 132, 124, 255));
                break;
        }
    }

    // -----------------------------------------------------------------------
    // 5. 16-Bit Top-Down Philippine Jeepney (North-Facing +Y, Rear Exit -Y)
    // -----------------------------------------------------------------------
    private static void GenerateJeepneySprites()
    {
        // Elongated Body + Open Passenger Interior (104x256 @ 64 PPU -> 0.56 x 1.38 world units)
        PixelCanvas body = new PixelCanvas(104, 256);
        // Subtle ground AO shadow around perimeter
        body.FillRect(6, 3, 92, 249, new Color32(0, 0, 0, 105), true);

        // Tires (4 visible wheel edges: rear y=30..56, front y=188..214)
        body.FillRect(5, 30, 7, 26, PAL_OUTLINE);
        body.FillRect(92, 30, 7, 26, PAL_OUTLINE);
        body.FillRect(5, 188, 7, 26, PAL_OUTLINE);
        body.FillRect(92, 188, 7, 26, PAL_OUTLINE);

        // Main outer steel chassis (deep crimson + weathered slate & brass accent)
        Color32 crimson = new Color32(94, 24, 28, 255);
        Color32 crimsonDk = new Color32(58, 16, 18, 255);
        Color32 steel = new Color32(62, 68, 76, 255);
        Color32 chrome = new Color32(148, 156, 166, 255);
        body.FillDitheredRect(9, 8, 86, 238, crimson, 301, 0.1f);
        body.StrokeRect(9, 8, 86, 238, 2, crimsonDk);
        // Decorative jeepney side stripes (gold + brass)
        body.FillRect(11, 10, 2, 224, PAL_BRASS);
        body.FillRect(91, 10, 2, 224, PAL_BRASS);

        // Elongated Passenger Bay floor (y=12..178, 166 px tall, open rear entry at bottom y=0..12 x=28..76)
        body.FillDitheredRect(15, 12, 74, 166, new Color32(34, 36, 40, 255), 302, 0.14f);
        for (int ry = 16; ry < 176; ry += 5)
            body.FillRect(29, ry, 46, 1, new Color32(22, 24, 28, 255)); // Center aisle tread floor ribs
        // Rear entry threshold lip (x=28..76, y=4..12)
        body.FillRect(28, 4, 48, 8, new Color32(42, 46, 52, 255));
        body.StrokeRect(28, 4, 48, 8, 1, PAL_OUTLINE);

        // Left Equipment Bench / Gear Shelf (x=15..27, y=18..168) & Right Passenger Bench (x=77..89, y=18..168)
        body.FillDitheredRect(15, 18, 12, 150, new Color32(58, 30, 26, 255), 303, 0.1f);
        body.StrokeRect(15, 18, 12, 150, 1, PAL_MAHOGANY_DK);
        body.FillDitheredRect(77, 18, 12, 150, new Color32(72, 32, 30, 255), 304, 0.1f);
        body.StrokeRect(77, 18, 12, 150, 1, PAL_MAHOGANY_DK);
        // Gear rack slot dividers on left bench + bench seam ticks on right bench
        for (int sy = 28; sy < 164; sy += 15)
        {
            body.FillRect(16, sy, 9, 1, PAL_OUTLINE);
        }
        for (int sy = 34; sy < 162; sy += 24)
        {
            body.FillRect(78, sy, 10, 1, PAL_OUTLINE);
        }
        // Chrome side wall grab rails
        body.FillRect(25, 20, 2, 146, chrome);
        body.FillRect(77, 20, 2, 146, chrome);

        // Subtle painted anchors for FrontDoorKey shelf (right-front) & Departure Buzzer mount (right-rear)
        body.FillRect(78, 156, 10, 10, PAL_SLATE_DK);
        body.StrokeRect(78, 156, 10, 10, 1, PAL_BRASS);
        body.FillRect(80, 32, 8, 10, PAL_SLATE_MD);
        body.StrokeRect(80, 32, 8, 10, 1, PAL_OUTLINE);

        // Reinforced Cab Partition Barrier Wall (y=174..183, full width x=12..92) separating bay from driver cab
        body.FillRect(12, 174, 80, 10, PAL_SLATE_DK);
        body.StrokeRect(12, 174, 80, 10, 1, PAL_OUTLINE);
        body.FillRect(14, 175, 76, 3, crimsonDk); // Lower passenger-facing steel panel trim
        body.FillRect(14, 181, 76, 2, chrome); // Top stainless partition cap rail
        body.FillRect(15, 175, 3, 2, PAL_BRASS); // Corner mounting plates
        body.FillRect(86, 175, 3, 2, PAL_BRASS);
        // Barred center cab divider slit window
        body.FillRect(40, 178, 24, 3, new Color32(18, 28, 36, 255));
        for (int gx = 43; gx < 64; gx += 4)
        {
            body.FillRect(gx, 178, 1, 3, chrome);
        }

        // Driver/shotgun bay floor & seats behind the cab bulkhead (y=184..204)
        body.FillDitheredRect(15, 184, 74, 20, PAL_SLATE_DK, 305, 0.08f);
        body.FillRect(18, 185, 22, 14, new Color32(64, 34, 28, 255)); // Driver seat
        body.FillRect(64, 185, 22, 14, new Color32(64, 34, 28, 255)); // Passenger seat
        body.FillCircle(29, 201, 5, chrome); // Steering wheel
        body.FillCircle(29, 201, 3, PAL_OUTLINE);

        // Slanted Windshield (y=204..213)
        body.FillRect(13, 204, 78, 9, new Color32(26, 44, 58, 255));
        body.FillRect(51, 204, 2, 9, chrome); // Split center mullion
        body.DrawLine(18, 206, 32, 211, new Color32(84, 128, 154, 180), true);
        body.DrawLine(60, 206, 74, 211, new Color32(84, 128, 154, 180), true);

        // Iconic Jeepney Stainless/Crimson Front Engine Hood (y=213..246)
        body.FillDitheredRect(14, 213, 76, 33, steel, 306, 0.09f);
        body.FillDitheredRect(24, 215, 56, 29, crimson, 307, 0.09f);
        body.StrokeRect(24, 215, 56, 29, 1, chrome);
        // Hood louvers
        for (int vx = 28; vx <= 72; vx += 6)
            body.FillRect(vx, 220, 3, 14, PAL_SLATE_DK);
        // Iconic brass hood ornament crest + front grill & chrome bumper
        body.FillRect(49, 238, 6, 8, PAL_BRASS);
        body.FillRect(12, 246, 80, 5, chrome);
        body.StrokeRect(12, 246, 80, 5, 1, PAL_OUTLINE);

        // Twin Round Headlights (top +Y front bumper corners) + Red Tail Lights (bottom -Y corners)
        body.FillCircle(20, 248, 4, PAL_BRASS);
        body.FillCircle(20, 248, 2, new Color32(255, 242, 188, 255));
        body.FillCircle(84, 248, 4, PAL_BRASS);
        body.FillCircle(84, 248, 2, new Color32(255, 242, 188, 255));
        body.FillRect(12, 7, 7, 3, new Color32(168, 34, 34, 255));
        body.FillRect(85, 7, 7, 3, new Color32(168, 34, 34, 255));

        body.AddPixelOutline(PAL_OUTLINE);
        body.SavePng("Jeepney/jeepney_body_interior.png");

        // Companion Bulkhead-Mounted CRT CCTV Dashboard Sprite (40x20 @ 64 PPU)
        PixelCanvas tv = new PixelCanvas(40, 20);
        tv.FillRect(2, 0, 36, 2, new Color32(0, 0, 0, 130), true); // Under-casing drop shadow
        tv.FillRect(7, 17, 5, 3, chrome); // Left wall mounting bracket
        tv.FillRect(28, 17, 5, 3, chrome); // Right wall mounting bracket
        DrawDashboardConsole(tv, 1, 2, 38, 15, chrome);
        tv.AddPixelOutline(PAL_OUTLINE);
        tv.SavePng("Jeepney/jeepney_dashboard_tv.png");

        // Elongated Jeepney Corrugated Roof Overlay (104x256, fades out when players step inside)
        PixelCanvas roof = new PixelCanvas(104, 256);
        roof.FillDitheredRect(11, 14, 82, 198, new Color32(48, 54, 62, 255), 310, 0.1f);
        roof.StrokeRect(11, 14, 82, 198, 2, crimsonDk);
        // Extended corrugated roof ribs
        for (int ry = 18; ry < 210; ry += 4)
            roof.FillRect(14, ry, 76, 1, PAL_SLATE_DK);
        // Elongated roof cargo rack + strapped canvas tarp + spare tire
        roof.StrokeRect(18, 28, 68, 120, 2, chrome);
        for (int bx = 26; bx <= 78; bx += 13) roof.FillRect(bx, 28, 2, 120, chrome);
        roof.FillDitheredRect(24, 40, 56, 54, new Color32(46, 62, 48, 255), 311, 0.12f); // Olive cargo tarp
        roof.StrokeRect(24, 40, 56, 54, 1, PAL_OUTLINE);
        roof.FillCircle(52, 120, 11, PAL_OUTLINE); // Spare tire
        roof.FillCircle(52, 120, 5, PAL_SLATE_LT);
        // Destination visor signboard at front edge of roof
        roof.FillRect(22, 204, 60, 8, crimson);
        roof.FillRect(26, 206, 52, 4, new Color32(214, 192, 132, 255));
        roof.AddPixelOutline(PAL_OUTLINE);
        roof.SavePng("Jeepney/jeepney_roof.png");

        // Rear Boarding Ramp: Closed upright barrier (48x12)
        PixelCanvas rClosed = new PixelCanvas(48, 12);
        rClosed.FillDitheredRect(1, 2, 46, 8, PAL_SLATE_MD, 320, 0.1f);
        rClosed.StrokeRect(0, 1, 48, 10, 1, PAL_OUTLINE);
        for (int hx = 4; hx < 42; hx += 8)
            rClosed.FillRect(hx, 3, 4, 6, new Color32(158, 124, 42, 255)); // Worn hazard stripes
        rClosed.FillRect(20, 0, 8, 3, PAL_BRASS); // Center latch handle
        rClosed.SavePng("Jeepney/jeepney_ramp_closed.png");

        // Rear Boarding Ramp: Open lowered diamond-plate ramp extending -Y (48x28)
        PixelCanvas rOpen = new PixelCanvas(48, 28);
        rOpen.FillRect(2, 0, 44, 4, new Color32(0, 0, 0, 120), true); // Road drop shadow
        rOpen.FillDitheredRect(2, 3, 44, 24, PAL_SLATE_MD, 322, 0.12f);
        rOpen.StrokeRect(1, 2, 46, 25, 1, PAL_OUTLINE);
        for (int ry = 6; ry < 25; ry += 4)
            rOpen.FillRect(4, ry, 40, 1, PAL_SLATE_LT); // Anti-slip tread bars
        rOpen.FillRect(2, 24, 44, 3, PAL_SLATE_DK); // Top hinge threshold bar
        rOpen.FillRect(6, 23, 6, 4, PAL_RUST);
        rOpen.FillRect(36, 23, 6, 4, PAL_RUST);
        rOpen.SavePng("Jeepney/jeepney_ramp_open.png");
    }

    private static void DrawDashboardConsole(PixelCanvas canvas, int x, int y, int w, int h, Color32 chrome)
    {
        // Mounting shelf / steel housing against front bulkhead
        canvas.FillRect(x, y, w, h, PAL_SLATE_DK);
        canvas.StrokeRect(x, y, w, h, 1, PAL_OUTLINE);
        canvas.FillRect(x + 2, y + h - 3, w - 4, 2, chrome);

        // Twin/wide retro CRT CCTV monitor unit facing -Y into passenger bay
        int screenX = x + 4;
        int screenY = y + 2;
        int screenW = Mathf.Max(8, w - 12);
        int screenH = Mathf.Max(4, h - 6);
        canvas.FillRect(screenX - 1, screenY - 1, screenW + 2, screenH + 2, PAL_OUTLINE);
        canvas.FillRect(screenX, screenY, screenW, screenH, new Color32(36, 98, 86, 255));
        for (int sy = screenY + 1; sy < screenY + screenH; sy += 2)
        {
            canvas.FillRect(screenX + 1, sy, screenW - 2, 1, new Color32(92, 205, 168, 255));
        }

        // Control dials & status LEDs on right side of console
        int ledX = screenX + screenW + 2;
        canvas.FillRect(ledX, y + 2, 2, 2, new Color32(82, 224, 120, 255)); // Green power LED
        canvas.FillRect(ledX, y + 6, 2, 2, new Color32(236, 178, 56, 255)); // Amber activity LED
        canvas.FillRect(ledX + 3, y + 2, 2, 2, new Color32(204, 48, 48, 255)); // Red REC LED
        canvas.FillRect(ledX + 3, y + 6, 2, 2, chrome); // Rotary tuning knob
    }

    // -----------------------------------------------------------------------
    // 6. World-Entity Weather Sprite Sheets (128f PPU crisp smaller entities)
    // -----------------------------------------------------------------------
    private static void GenerateWeatherSpriteSheets()
    {
        // Fireflies (8 frames @ 16x16 -> 128x16, sliced at 128f PPU = 2x smaller cleanly glowing motes)
        PixelCanvas fireflies = new PixelCanvas(128, 16);
        byte[] pulseAlphas = { 55, 110, 185, 245, 255, 195, 125, 70 };
        int[] glowRadii = { 2, 2, 3, 4, 4, 3, 2, 2 };
        for (int f = 0; f < 8; f++)
        {
            int ox = f * 16;
            int cx = ox + 8;
            int cy = 8 + (f % 2 == 0 ? 0 : 1);
            byte coreAlpha = pulseAlphas[f];
            int r = glowRadii[f];

            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= r + 0.1f)
                    {
                        float falloff = 1f - dist / (r + 0.4f);
                        byte ga = (byte)Mathf.Clamp(Mathf.RoundToInt(coreAlpha * 0.38f * falloff * falloff), 0, 130);
                        fireflies.Blend(cx + dx, cy + dy, new Color32(174, 232, 88, ga));
                    }
                }
            }

            int wingDy = (f % 4 < 2) ? 1 : -1;
            fireflies.Blend(cx - 1, cy + wingDy, new Color32(48, 60, 52, 160));
            fireflies.Blend(cx + 1, cy + wingDy, new Color32(48, 60, 52, 160));
            fireflies.Blend(cx, cy, new Color32(244, 255, 176, coreAlpha));
        }
        fireflies.SavePng("Weather/fireflies_sheet.png");

        // Fog wisps (8 frames @ 64x48 -> 512x48)
        PixelCanvas fog = new PixelCanvas(512, 48);
        for (int f = 0; f < 8; f++)
        {
            int ox = f * 64;
            float phase = f * (Mathf.PI * 2f / 8f);
            for (int y = 0; y < 48; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float nx = (x - 32f) / 28f;
                    float ny = (y - 24f) / 18f;
                    float radial = 1f - (nx * nx + ny * ny);
                    if (radial <= 0f) continue;

                    float billow = 0.55f + 0.45f * Mathf.Sin(x * 0.196f + phase) * Mathf.Cos(y * 0.21f - phase * 0.5f);
                    float density = radial * billow;
                    if (density > 0.16f)
                    {
                        byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt((density - 0.16f) * 185f), 0, 140);
                        if (density < 0.42f && ((x + y + f) & 1) == 0 && alpha > 14) alpha = (byte)(alpha * 3 / 4);
                        fog.Set(ox + x, y, new Color32(158, 172, 186, alpha));
                    }
                }
            }
        }
        fog.SavePng("Weather/fog_clouds_sheet.png");

        // Grouped RainCluster sheet (7 frames @ 32x28 -> 224x28, sliced at 128f PPU)
        // Each cluster renders 4 independent crisp ~12x20-scale falling drops & splash crowns.
        PixelCanvas rain = new PixelCanvas(224, 28);
        Color32 streakTail = new Color32(132, 168, 218, 145);
        Color32 streakTip = new Color32(204, 228, 255, 220);
        int[] dropX = { 6, 13, 21, 27 };
        int[] dropGroundY = { 4, 8, 3, 7 };
        int[] dropPhase = { 0, 2, 4, 5 };

        for (int f = 0; f < 7; f++)
        {
            int ox = f * 32;
            for (int d = 0; d < 4; d++)
            {
                int subF = (f + dropPhase[d]) % 7;
                int cx = ox + dropX[d];
                int gy = dropGroundY[d];
                if (subF <= 3)
                {
                    int topY = gy + 16 - subF * 4;
                    int botY = topY - 5;
                    rain.DrawLine(cx + 1, topY, cx, botY, streakTail, true);
                    rain.Blend(cx, botY, streakTip);
                }
                else if (subF == 4)
                {
                    rain.DrawLine(cx + 1, gy + 4, cx, gy, streakTail, true);
                    rain.Blend(cx - 1, gy + 1, streakTip);
                    rain.Blend(cx + 1, gy + 1, streakTip);
                }
                else if (subF == 5)
                {
                    Color32 ringCol = new Color32(174, 208, 244, 185);
                    rain.Blend(cx - 2, gy, ringCol);
                    rain.Blend(cx + 2, gy, ringCol);
                    rain.Blend(cx, gy - 1, ringCol);
                    rain.Blend(cx, gy + 1, ringCol);
                    rain.Blend(cx - 1, gy + 3, streakTip);
                    rain.Blend(cx + 1, gy + 3, streakTip);
                }
                else
                {
                    Color32 fadeCol = new Color32(148, 186, 228, 95);
                    rain.Blend(cx - 3, gy, fadeCol);
                    rain.Blend(cx + 3, gy, fadeCol);
                    rain.Blend(cx, gy - 1, fadeCol);
                    rain.Blend(cx, gy + 1, fadeCol);
                }
            }
        }
        rain.SavePng("Weather/rain_streaks_sheet.png");

        // Ground puddle ripples (7 frames @ 32x32 -> 224x32)
        PixelCanvas puddle = new PixelCanvas(224, 32);
        for (int f = 0; f < 7; f++)
        {
            int ox = f * 32;
            puddle.FillEllipse(ox + 16, 16, 13, 9, new Color32(22, 34, 52, 130), true);
            puddle.FillEllipse(ox + 15, 17, 10, 6, new Color32(34, 54, 78, 120), true);
            int r = 1 + f * 2;
            int ry = Mathf.Max(1, r * 2 / 3);
            byte alpha = (byte)Mathf.Clamp(215 - f * 26, 40, 215);
            Color32 ring = new Color32(168, 208, 242, alpha);
            for (int a = 0; a < 28; a++)
            {
                float ang = a * Mathf.PI * 2f / 28f;
                puddle.Blend(ox + 16 + Mathf.RoundToInt(Mathf.Cos(ang) * r),
                             16 + Mathf.RoundToInt(Mathf.Sin(ang) * ry), ring);
            }
            if (f >= 3)
            {
                int r2 = 1 + (f - 3) * 2;
                int ry2 = Mathf.Max(1, r2 * 2 / 3);
                Color32 ring2 = new Color32(184, 218, 246, (byte)(alpha * 2 / 3));
                for (int a = 0; a < 20; a++)
                {
                    float ang = a * Mathf.PI * 2f / 20f;
                    puddle.Blend(ox + 16 + Mathf.RoundToInt(Mathf.Cos(ang) * r2),
                                 16 + Mathf.RoundToInt(Mathf.Sin(ang) * ry2), ring2);
                }
            }
        }
        puddle.SavePng("Weather/puddle_ripples_sheet.png");
    }

    // -----------------------------------------------------------------------
    // 7. Procedural 16-Bit WAV Audio Clips (Resources/Audio + Retro16Bit)
    // -----------------------------------------------------------------------
    private static void GenerateAudioClips()
    {
        if (!Directory.Exists(AUDIO_DIR)) Directory.CreateDirectory(AUDIO_DIR);
        if (!Directory.Exists(RETRO_SFX_DIR)) Directory.CreateDirectory(RETRO_SFX_DIR);
        const int sr = 22050;

        // Crickets (preserve existing if already fine, or clean sample buffer)
        WriteWavIfMissing("weather_night_crickets.wav", sr, 3.0f, count =>
        {
            float[] buf = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float pulse = Mathf.Sin(t * 2f * Mathf.PI * 3.2f) > 0.35f ? 1f : 0f;
                float env = Mathf.Sin(Mathf.Repeat(t * 24f, 1f) * Mathf.PI);
                float carrier = Mathf.Sin(2f * Mathf.PI * 3950f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 4220f * t);
                buf[i] = carrier * pulse * env * 0.14f;
            }
            return buf;
        });

        // Warm filtered steady rain + scattered droplet patters (overwrite)
        WriteWavOverwrite("weather_rain_loop.wav", sr, 3.0f, count =>
        {
            float[] buf = new float[count];
            float lp = 0f, bp = 0f;
            float[] splashTimes = { 0.18f, 0.44f, 0.79f, 1.08f, 1.37f, 1.69f, 1.98f, 2.34f, 2.71f };
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float white = Hash01(i, 11, 801) * 2f - 1f;
                lp = lp * 0.78f + white * 0.22f;
                bp = bp * 0.55f + (white - lp) * 0.28f;
                float body = lp * 0.34f + bp * 0.18f;

                float dropSum = 0f;
                for (int d = 0; d < splashTimes.Length; d++)
                {
                    float dt = t - splashTimes[d];
                    if (dt >= 0f && dt < 0.045f)
                    {
                        float splashEnv = Mathf.Exp(-dt * 85f);
                        dropSum += (Hash01(i + d * 19, 3, 811) * 2f - 1f) * splashEnv * 0.22f;
                    }
                }
                buf[i] = Mathf.Clamp(body + dropSum, -0.85f, 0.85f);
            }
            return buf;
        });

        // Thunder clap (preserve or create)
        WriteWavIfMissing("weather_thunder_clap.wav", sr, 2.2f, count =>
        {
            float[] buf = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float crack = Mathf.Exp(-t * 9f) * (Hash01(i, 1, 901) * 2f - 1f);
                float rumble = Mathf.Exp(-t * 1.4f) * (Mathf.Sin(2f * Mathf.PI * 52f * t) * 0.5f + (Hash01(i / 4, 2, 902) * 2f - 1f) * 0.45f);
                buf[i] = Mathf.Clamp(crack * 0.85f + rumble * 0.6f, -0.95f, 0.95f);
            }
            return buf;
        });

        // Atmospheric wind + fog without 140Hz synth drone, plus faint damp puddle drips (overwrite)
        WriteWavOverwrite("weather_wind_fog.wav", sr, 3.5f, count =>
        {
            float[] buf = new float[count];
            float lpLow = 0f, lpAir = 0f;
            float[] dripTimes = { 0.42f, 1.38f, 2.55f };
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float swell = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t / 3.5f - 0.6f));
                float gust = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * t * 0.85f);
                float white = Hash01(i, 5, 910) * 2f - 1f;
                lpLow = lpLow * 0.965f + white * 0.035f;
                lpAir = lpAir * 0.86f + white * 0.14f;
                float airyGust = (lpLow * 1.45f + (lpAir - lpLow) * 0.42f) * swell * gust;

                float drip = 0f;
                for (int d = 0; d < dripTimes.Length; d++)
                {
                    float dt = t - dripTimes[d];
                    if (dt >= 0f && dt < 0.05f)
                    {
                        float fDrop = 920f + dt * 11500f; // Water droplet pitch-up plink
                        float env = Mathf.Exp(-dt * 95f) * Mathf.Clamp01(dt * 450f);
                        drip += Mathf.Sin(2f * Mathf.PI * fDrop * dt) * env * 0.14f;
                        drip += (Hash01(i + d * 7, 17, 912) * 2f - 1f) * Mathf.Exp(-dt * 120f) * 0.04f;
                    }
                }
                buf[i] = Mathf.Clamp(airyGust + drip, -0.85f, 0.85f);
            }
            return buf;
        });

        // Mechanical doorknob jiggle / latch rattle (overwrite & mirror to Retro16Bit)
        WriteWavOverwrite("door_locked.wav", sr, 0.45f, count =>
        {
            float[] buf = new float[count];
            float[] clicks = { 0.02f, 0.14f, 0.27f };
            float lp = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float noise = Hash01(i, 21, 960) * 2f - 1f;
                lp = lp * 0.70f + noise * 0.30f;
                float sig = 0f;
                for (int c = 0; c < clicks.Length; c++)
                {
                    float dt = t - clicks[c];
                    if (dt >= 0f && dt < 0.08f)
                    {
                        float eMetal = Mathf.Exp(-dt * 95f);
                        float eWood = Mathf.Exp(-dt * 42f);
                        float metallic = (noise - lp) * eMetal * 0.55f;
                        float woodRattle = (Mathf.Sin(2f * Mathf.PI * (145f + c * 18f) * dt) * 0.45f + lp * 0.55f) * eWood * 0.55f;
                        sig += metallic + woodRattle;
                    }
                }
                buf[i] = Mathf.Clamp(sig, -0.90f, 0.90f);
            }
            return buf;
        }, RETRO_SFX_DIR);

        // Heavy damp oak door opening (latch click + creaking wood resonance, overwrite & mirror)
        WriteWavOverwrite("door_open.wav", sr, 0.65f, count =>
        {
            float[] buf = new float[count];
            float lp = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float noise = Hash01(i, 23, 962) * 2f - 1f;
                lp = lp * 0.84f + noise * 0.16f;
                float latchClick = t < 0.055f ? (noise - lp * 0.6f) * Mathf.Exp(-t * 110f) * 0.55f : 0f;
                float creakEnv = t >= 0.06f ? Mathf.Sin(Mathf.Clamp01((t - 0.06f) / 0.54f) * Mathf.PI) : 0f;
                float creakFreq = 112f + 26f * Mathf.Sin(t * 14f) - t * 24f;
                float stickSlip = Mathf.Sin(2f * Mathf.PI * creakFreq * t)
                    + 0.45f * Mathf.Sin(2f * Mathf.PI * creakFreq * 2.02f * t);
                float timber = lp * 0.38f + stickSlip * 0.24f;
                buf[i] = Mathf.Clamp(latchClick + timber * creakEnv * 0.65f, -0.90f, 0.90f);
            }
            return buf;
        }, RETRO_SFX_DIR);

        // Heavy damp oak door closing (latch snap + low oak thud, overwrite & mirror)
        WriteWavOverwrite("door_close.wav", sr, 0.48f, count =>
        {
            float[] buf = new float[count];
            float lp = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float noise = Hash01(i, 25, 964) * 2f - 1f;
                lp = lp * 0.80f + noise * 0.20f;
                float thump = t >= 0.04f
                    ? (Mathf.Sin(2f * Mathf.PI * 76f * (t - 0.04f)) * 0.7f + lp * 0.5f) * Mathf.Exp(-(t - 0.04f) * 22f)
                    : 0f;
                float latchSnap = t >= 0.07f
                    ? (noise - lp * 0.5f) * Mathf.Exp(-(t - 0.07f) * 105f) * 0.62f
                    : 0f;
                float swingWhoosh = t < 0.08f ? lp * (t / 0.08f) * 0.35f : 0f;
                buf[i] = Mathf.Clamp(swingWhoosh + thump * 0.78f + latchSnap, -0.92f, 0.92f);
            }
            return buf;
        }, RETRO_SFX_DIR);

        // Jeepney arrival: diesel deceleration + drum brake squeal/hiss + stop clunk (2.2s, overwrite)
        WriteWavOverwrite("truck_arrive_stop.wav", sr, 2.2f, count =>
        {
            float[] buf = new float[count];
            float lp = 0f;
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float noise = Hash01(i, 31, 970) * 2f - 1f;
                lp = lp * 0.88f + noise * 0.12f;

                // Diesel engine chug decelerating into idle then cut off at 1.95s
                float engEnv = t < 1.90f ? Mathf.Clamp01(1f - Mathf.Max(0f, t - 1.55f) / 0.35f) : 0f;
                float engHz = Mathf.Lerp(58f, 24f, Mathf.Clamp01(t / 1.55f));
                phase += 2f * Mathf.PI * engHz / sr;
                float diesel = (Mathf.Sin(phase) * 0.55f + Mathf.Sin(phase * 2f) * 0.32f + lp * 0.45f) * engEnv * 0.52f;

                // Wet road tire hiss & brake squeal (0.65s..1.65s)
                float brakeWin = (t > 0.60f && t < 1.70f)
                    ? Mathf.Sin(Mathf.Clamp01((t - 0.60f) / 1.10f) * Mathf.PI)
                    : 0f;
                float squeal = (Mathf.Sin(2f * Mathf.PI * 1040f * t) * 0.08f + (noise - lp) * 0.22f) * brakeWin;

                // Heavy brake stop suspension clunk at 1.72s
                float clunk = t >= 1.72f
                    ? (Mathf.Sin(2f * Mathf.PI * 64f * (t - 1.72f)) * 0.6f + lp * 0.45f) * Mathf.Exp(-(t - 1.72f) * 20f) * 0.68f
                    : 0f;

                buf[i] = Mathf.Clamp(diesel + squeal + clunk, -0.92f, 0.92f);
            }
            return buf;
        });

        // Jeepney departure: ignition crank + diesel rev + fading drive-away rumble (2.3s, overwrite)
        WriteWavOverwrite("truck_start_depart.wav", sr, 2.3f, count =>
        {
            float[] buf = new float[count];
            float lp = 0f;
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float noise = Hash01(i, 33, 972) * 2f - 1f;
                lp = lp * 0.88f + noise * 0.12f;

                // Starter crank pulses (0.05..0.50s)
                float crank = 0f;
                if (t > 0.04f && t < 0.52f)
                {
                    float pulse = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 7.5f * t));
                    crank = (Mathf.Sin(2f * Mathf.PI * 135f * t) * 0.35f + (noise - lp) * 0.4f) * pulse * 0.42f;
                }

                // Engine catch + rev up (0.45s..2.3s) + distance fade
                float runEnv = t >= 0.42f ? Mathf.Clamp01((t - 0.42f) / 0.15f) * Mathf.Clamp01(1f - Mathf.Max(0f, t - 1.15f) / 1.15f) : 0f;
                float engHz = t < 1.25f
                    ? Mathf.Lerp(32f, 76f, Mathf.Clamp01((t - 0.42f) / 0.83f))
                    : Mathf.Lerp(76f, 52f, Mathf.Clamp01((t - 1.25f) / 1.05f));
                phase += 2f * Mathf.PI * engHz / sr;
                float rev = (Mathf.Sin(phase) * 0.55f + Mathf.Sin(phase * 2f) * 0.35f + lp * 0.50f) * runEnv * 0.65f;

                buf[i] = Mathf.Clamp(crank + rev, -0.92f, 0.92f);
            }
            return buf;
        });

        GeneratePropAudioClips();
        GenerateGhostDyingShriekWav();

        WriteWavIfMissing("stair_footsteps.wav", sr, 0.45f, count =>
        {
            float[] buf = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float local = Mathf.Repeat(t, 0.15f);
                float env = Mathf.Exp(-local * 26f);
                buf[i] = (Mathf.Sin(2f * Mathf.PI * 120f * local) * 0.65f + (Hash01(i, 14, 950) * 2f - 1f) * 0.35f) * env * 0.65f;
            }
            return buf;
        });
    }

    [MenuItem("Pamahiin/Regenerate Prop Audio")]
    public static void RegeneratePropAudio()
    {
        if (!Directory.Exists(AUDIO_DIR)) Directory.CreateDirectory(AUDIO_DIR);
        GeneratePropAudioClips();
        AssetDatabase.Refresh();
        Debug.Log("[BarangayAssetGenerator] Regenerated prop_levitate, prop_throw_whoosh, prop_shatter_glass, prop_knock_wood.");
    }

    private static float[] BandpassNoise(int count, int sampleRate, int seed, float minFreq, float peakFreq, float endFreq, float q, Func<float, float> envelopeFunc)
    {
        float[] buf = new float[count];
        float low = 0f;
        float band = 0f;
        float normQ = 1f / Mathf.Max(0.5f, q);
        for (int i = 0; i < count; i++)
        {
            float norm = (float)i / Mathf.Max(1, count - 1);
            float fc = norm < 0.45f
                ? Mathf.Lerp(minFreq, peakFreq, norm / 0.45f)
                : Mathf.Lerp(peakFreq, endFreq, (norm - 0.45f) / 0.55f);
            float f = Mathf.Clamp(2f * Mathf.Sin(Mathf.PI * fc / (sampleRate * 2f)), 0.001f, 0.85f);
            float input = Hash01(i, 19, seed) * 2f - 1f;

            // Run 2 sub-steps for clean SVF stability across high frequencies
            for (int s = 0; s < 2; s++)
            {
                low += f * band;
                float high = input - low - normQ * band;
                band += f * high;
            }
            buf[i] = band * envelopeFunc(norm);
        }
        return buf;
    }

    private static void NormalizePeak(float[] buf, float targetPeak)
    {
        float peak = 0f;
        for (int i = 0; i < buf.Length; i++)
        {
            float a = Mathf.Abs(buf[i]);
            if (a > peak) peak = a;
        }
        if (peak > 1e-5f)
        {
            float gain = targetPeak / peak;
            for (int i = 0; i < buf.Length; i++) buf[i] *= gain;
        }
    }

    public static void GeneratePropAudioClips()
    {
        const int srProp = 44100;

        // Subtle eerie vibration / air tremor instead of a rising cartoon tone
        WriteWavOverwrite("prop_levitate.wav", srProp, 0.50f, count =>
        {
            float[] buf = BandpassNoise(count, srProp, 915, 290f, 520f, 340f, 2.4f, norm =>
            {
                float t = norm * 0.50f;
                float fade = Mathf.Sin(norm * Mathf.PI);
                float tremor = 0.58f + 0.42f * Mathf.Sin(2f * Mathf.PI * 16f * t);
                return fade * tremor;
            });
            NormalizePeak(buf, 0.30f);
            return buf;
        });

        // Smooth swept bandpass air whoosh with zero low bass thump or sample-and-hold buzz
        WriteWavOverwrite("prop_throw_whoosh.wav", srProp, 0.22f, count =>
        {
            float[] airLow = BandpassNoise(count, srProp, 920, 300f, 960f, 280f, 2.0f, norm =>
            {
                return Mathf.Pow(Mathf.Sin(norm * Mathf.PI), 0.75f) * (1f - 0.3f * norm);
            });
            float[] airHigh = BandpassNoise(count, srProp, 921, 900f, 2100f, 700f, 1.8f, norm =>
            {
                return Mathf.Pow(Mathf.Sin(norm * Mathf.PI), 1.1f) * 0.45f;
            });
            float[] buf = new float[count];
            for (int i = 0; i < count; i++) buf[i] = airLow[i] + airHigh[i];
            NormalizePeak(buf, 0.52f);
            return buf;
        });

        // Crisp multi-shard glass shatter (*DISHHH*) — no low-Q metallic gong bell partials
        WriteWavOverwrite("prop_shatter_glass.wav", srProp, 0.65f, count =>
        {
            float[] buf = new float[count];
            float lp2k = 0f;
            float lp6k = 0f;
            float alpha2k = 2f * Mathf.PI * 2200f / (srProp + 2f * Mathf.PI * 2200f);
            float alpha6k = 2f * Mathf.PI * 6200f / (srProp + 2f * Mathf.PI * 6200f);
            float modSmooth = 1f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / srProp;
                float wn = Hash01(i, 9, 930) * 2f - 1f;
                lp2k += alpha2k * (wn - lp2k);
                lp6k += alpha6k * (wn - lp6k);

                // Sharp high-passed initial crack + cascading brittle hiss spray
                float hpCrack = wn - lp2k;
                float crispBand = lp6k - lp2k * 0.85f;
                float microGrain = 0.45f + 0.55f * Hash01(i / 48, 3, 931);
                modSmooth += 0.08f * (microGrain - modSmooth);

                float initialAttack = Mathf.Exp(-t * 170f) * 1.15f + Mathf.Exp(-t * 62f) * 0.55f;
                float sprayTail = Mathf.Exp(-t * 9.8f) * modSmooth * 0.58f;
                buf[i] = hpCrack * initialAttack + crispBand * sprayTail;
            }

            // Scatter 38 short high-frequency glass shard tinks across impact + settle tail
            const int shardCount = 38;
            for (int k = 0; k < shardCount; k++)
            {
                float startSec = k < 12
                    ? Hash01(k, 1, 932) * 0.035f
                    : (k < 28 ? 0.025f + Hash01(k, 2, 932) * 0.19f : 0.17f + Hash01(k, 3, 932) * 0.27f);
                float freq = Mathf.Lerp(3850f, 10800f, Hash01(k, 4, 933));
                float decay = Mathf.Lerp(58f, 145f, Hash01(k, 5, 933));
                float amp = (k < 12 ? 0.24f : (k < 28 ? 0.15f : 0.09f)) * (0.65f + 0.35f * Hash01(k, 6, 934));
                float phase = Hash01(k, 7, 934) * Mathf.PI * 2f;

                int startIdx = Mathf.FloorToInt(startSec * srProp);
                int maxLen = Mathf.Min(count, startIdx + Mathf.CeilToInt(5.5f / decay * srProp));
                for (int i = startIdx; i < maxLen; i++)
                {
                    float dt = (float)(i - startIdx) / srProp;
                    buf[i] += Mathf.Sin(2f * Mathf.PI * freq * dt + phase) * Mathf.Exp(-dt * decay) * amp;
                }
            }

            for (int i = 0; i < count; i++) buf[i] = (float)Math.Tanh(buf[i] * 1.35f);
            NormalizePeak(buf, 0.86f);
            return buf;
        });

        // Tight dry wooden/prop impact tap + secondary settle clack (no 165Hz potato-launcher boom)
        WriteWavOverwrite("prop_knock_wood.wav", srProp, 0.22f, count =>
        {
            float[] midSnap = BandpassNoise(count, srProp, 940, 620f, 880f, 520f, 2.4f, _ => 1f);
            float[] hiClick = BandpassNoise(count, srProp, 941, 1650f, 2200f, 1400f, 2.2f, _ => 1f);
            float[] buf = new float[count];
            float tap2Start = 0.082f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / srProp;
                // First crisp tap (~8-15ms decay)
                float fSweep = Mathf.Lerp(290f, 135f, Mathf.Clamp01(t / 0.014f));
                float microThud = Mathf.Sin(2f * Mathf.PI * fSweep * t) * Mathf.Exp(-t * 145f) * 0.28f;
                float env1 = Mathf.Exp(-t * 105f);
                float hit1 = microThud + (midSnap[i] * 0.72f + hiClick[i] * 0.42f) * env1;

                // Smaller secondary settle clack on floor
                float hit2 = 0f;
                if (t >= tap2Start)
                {
                    float dt = t - tap2Start;
                    float env2 = Mathf.Exp(-dt * 135f) * 0.44f;
                    hit2 = (midSnap[i] * 0.65f + hiClick[i] * 0.55f) * env2;
                }

                buf[i] = hit1 + hit2;
            }
            NormalizePeak(buf, 0.56f);
            return buf;
        });
    }

    // -----------------------------------------------------------------------
    // 7. Ritual Summoning Circle Sheet, Ghost Exorcism Death Sheet & Shriek WAV
    // -----------------------------------------------------------------------
    public static void GenerateSummoningCircleSheet()
    {
        const int frameSize = 128;
        const int frameCount = 16; // 2048x128: F0=idle, F1=runes awaken, F2-11=5 candles ignite (flare+burn+surge), F12-15=loop
        PixelCanvas sheet = new PixelCanvas(frameSize * frameCount, frameSize);

        Color32 chalkEdge = new Color32(18, 12, 16, 175);
        Color32 chalkBright = new Color32(246, 239, 222, 252);
        Color32 chalkMid = new Color32(212, 200, 178, 232);
        Color32 chalkDust = new Color32(170, 158, 138, 138);

        Color32 runeRedDim = new Color32(168, 24, 24, 210);
        Color32 runeRedBright = new Color32(245, 58, 34, 245);
        Color32 runeGoldCore = new Color32(255, 196, 86, 255);

        Color32 saucerDark = new Color32(30, 24, 28, 230);
        Color32 waxPool = new Color32(124, 18, 20, 235);
        Color32 waxBody = new Color32(168, 28, 28, 255);
        Color32 waxHighlight = new Color32(218, 58, 48, 255);
        Color32 waxShadow = new Color32(94, 14, 18, 255);
        Color32 waxCreamTop = new Color32(236, 198, 186, 255);
        Color32 wickDark = new Color32(24, 18, 20, 255);

        Color32 flameOuter = new Color32(198, 18, 18, 235);
        Color32 flameBody = new Color32(255, 48, 24, 255);
        Color32 flameTip = new Color32(255, 128, 48, 255);
        Color32 flameCore = new Color32(255, 232, 142, 255);

        for (int f = 0; f < frameCount; f++)
        {
            int ox = f * frameSize;
            int cx = ox + frameSize / 2;
            int cy = frameSize / 2;

            bool runesAwake = f >= 1;
            int litCount = f < 2 ? 0 : (f >= 10 ? 5 : ((f - 2) / 2) + 1);
            bool isClimaxFrame = f == 11;
            bool isLoopFrame = f >= 12;

            float igniteHeat = f == 0 ? 0f
                : (f == 1 ? 0.35f
                : (isClimaxFrame ? 1.0f
                : (isLoopFrame ? (0.78f + 0.18f * Mathf.Sin(((f - 12) / 4f) * Mathf.PI * 2f))
                : Mathf.Clamp01(0.32f + litCount * 0.13f))));

            // 1. Dark ritual ash / bloodstain floor smudge grounding the circle
            for (int y = cy - 58; y <= cy + 58; y++)
            {
                for (int x = cx - 58; x <= cx + 58; x++)
                {
                    int lx = x - ox;
                    float dx = lx - 64f;
                    float dy = y - 64f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < 56f)
                    {
                        float h = Hash01(lx, y, 710);
                        float fade = Mathf.Clamp01(1f - d / 56f);
                        if (h > 0.62f)
                        {
                            byte a = (byte)Mathf.RoundToInt(fade * 58f);
                            sheet.Blend(x, y, new Color32(18, 12, 16, a));
                        }
                        if (runesAwake && d < 32f)
                        {
                            float innerAura = Mathf.Pow(1f - d / 32f, 1.5f) * igniteHeat;
                            if (innerAura > 0.08f)
                            {
                                sheet.Blend(x, y, new Color32(150, 16, 16, (byte)Mathf.RoundToInt(innerAura * 72f)));
                            }
                        }
                    }
                }
            }

            // 2. Crisp double outer chalk rings (r=56 & r=46.5), inner binding ring (r=21), + 20 rune glyphs
            for (int y = 2; y < frameSize - 2; y++)
            {
                for (int x = ox + 2; x < ox + frameSize - 2; x++)
                {
                    int lx = x - ox;
                    float dx = lx - 64f;
                    float dy = y - 64f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float grain = Hash01(lx, y, 720);

                    // Outer chalk circle (2px thick + subtle dark contrast outline)
                    float dOuter = Mathf.Abs(dist - 55.5f);
                    if (dOuter <= 1.25f && grain > 0.10f)
                    {
                        sheet.Blend(x, y, grain > 0.52f ? chalkBright : chalkMid);
                    }
                    else if (dOuter <= 2.35f)
                    {
                        if (grain > 0.72f) sheet.Blend(x, y, chalkDust);
                        else sheet.Blend(x, y, chalkEdge);
                    }

                    // Inner ring separating rune band from pentagram (r = 46.2)
                    float dBandInner = Mathf.Abs(dist - 46.2f);
                    if (dBandInner <= 1.05f && grain > 0.14f)
                    {
                        Color32 baseC = grain > 0.56f ? chalkBright : chalkMid;
                        sheet.Blend(x, y, baseC);
                        if (runesAwake && grain > 0.42f)
                        {
                            Color32 rc = Color32.Lerp(runeRedDim, runeRedBright, igniteHeat);
                            rc.a = (byte)Mathf.RoundToInt(175f * igniteHeat);
                            sheet.Blend(x, y, rc);
                        }
                    }
                    else if (dBandInner <= 1.9f && grain <= 0.5f)
                    {
                        sheet.Blend(x, y, chalkEdge);
                    }

                    // 20 evenly spaced occult glyphs inside the rune band (r = 48.2 .. 53.6)
                    if (dist >= 48.0f && dist <= 53.8f)
                    {
                        float runeSector = Mathf.Repeat((ang + Mathf.PI) / (Mathf.PI * 2f) * 20f, 1f);
                        int runeIndex = Mathf.FloorToInt(Mathf.Repeat((ang + Mathf.PI) / (Mathf.PI * 2f) * 20f, 20f));
                        float rRad = (dist - 48.0f) / 5.8f; // 0..1 radial inside band
                        bool drawRunePx = false;

                        switch (runeIndex % 4)
                        {
                            case 0: // Cross + bar sigil
                                drawRunePx = Mathf.Abs(runeSector - 0.5f) < 0.09f || Mathf.Abs(rRad - 0.5f) < 0.14f;
                                break;
                            case 1: // Chevron / X sigil
                                drawRunePx = Mathf.Abs(Mathf.Abs(runeSector - 0.5f) * 1.4f - Mathf.Abs(rRad - 0.5f)) < 0.14f && Mathf.Abs(runeSector - 0.5f) < 0.30f;
                                break;
                            case 2: // Trident / Twin vertical prongs + base
                                drawRunePx = (rRad < 0.22f && Mathf.Abs(runeSector - 0.5f) < 0.28f) ||
                                             Mathf.Abs(runeSector - 0.32f) < 0.08f ||
                                             Mathf.Abs(runeSector - 0.68f) < 0.08f;
                                break;
                            default: // Occult diamond / eye glyph
                                float diamond = Mathf.Abs(runeSector - 0.5f) * 1.8f + Mathf.Abs(rRad - 0.5f);
                                drawRunePx = Mathf.Abs(diamond - 0.36f) < 0.13f;
                                break;
                        }

                        if (drawRunePx && grain > 0.12f)
                        {
                            sheet.Blend(x, y, chalkBright);
                            if (runesAwake)
                            {
                                float glyphDelay = (runeIndex % 5) * 0.08f;
                                float glowT = Mathf.Clamp01((igniteHeat - glyphDelay * 0.4f) * 1.25f);
                                if (glowT > 0.05f)
                                {
                                    Color32 glow = Color32.Lerp(runeRedDim, isClimaxFrame ? runeGoldCore : runeRedBright, glowT);
                                    sheet.Blend(x, y, glow);
                                }
                            }
                        }
                    }

                    // Inner sacred binding ring (r = 21.0) & central occult eye (r = 8.2)
                    float dMidRing = Mathf.Abs(dist - 21.0f);
                    if (dMidRing <= 0.95f && grain > 0.16f)
                    {
                        sheet.Blend(x, y, grain > 0.55f ? chalkBright : chalkMid);
                        if (runesAwake)
                        {
                            Color32 rc = Color32.Lerp(runeRedDim, runeRedBright, igniteHeat);
                            rc.a = (byte)Mathf.RoundToInt(200f * igniteHeat);
                            sheet.Blend(x, y, rc);
                        }
                    }

                    float dCenterEye = Mathf.Abs(dist - 8.0f);
                    if (dCenterEye <= 0.9f && grain > 0.16f)
                    {
                        sheet.Blend(x, y, chalkBright);
                        if (runesAwake)
                        {
                            Color32 rc = Color32.Lerp(runeRedBright, runeGoldCore, isClimaxFrame ? 0.85f : igniteHeat * 0.45f);
                            rc.a = (byte)Mathf.RoundToInt(235f * igniteHeat);
                            sheet.Blend(x, y, rc);
                        }
                    }
                }
            }

            // 3. 5 candle vertices placed clockwise starting at 12 o'clock (90° in Y-up Texture2D coordinates)
            Vector2Int[] candlePts = new Vector2Int[5];
            Vector2Int[] starPts = new Vector2Int[5];
            for (int k = 0; k < 5; k++)
            {
                float deg = 90f - k * 72f;
                float rad = deg * Mathf.Deg2Rad;
                starPts[k] = new Vector2Int(
                    cx + Mathf.RoundToInt(Mathf.Cos(rad) * 44.5f),
                    cy + Mathf.RoundToInt(Mathf.Sin(rad) * 44.5f));
                candlePts[k] = new Vector2Int(
                    cx + Mathf.RoundToInt(Mathf.Cos(rad) * 43.0f),
                    cy + Mathf.RoundToInt(Mathf.Sin(rad) * 43.0f));
            }

            // Central inverted ritual sigil bars inside the r=21 circle
            for (int k = 0; k < 3; k++)
            {
                float a0 = (-90f + k * 120f) * Mathf.Deg2Rad;
                float a1 = (-90f + ((k + 1) % 3) * 120f) * Mathf.Deg2Rad;
                Vector2Int t0 = new Vector2Int(cx + Mathf.RoundToInt(Mathf.Cos(a0) * 19f), cy + Mathf.RoundToInt(Mathf.Sin(a0) * 19f));
                Vector2Int t1 = new Vector2Int(cx + Mathf.RoundToInt(Mathf.Cos(a1) * 19f), cy + Mathf.RoundToInt(Mathf.Sin(a1) * 19f));
                DrawThickChalkLine(sheet, t0.x, t0.y, t1.x, t1.y, ox, chalkBright, chalkMid, chalkDust, chalkEdge, 745 + k,
                    runesAwake ? Color32.Lerp(runeRedDim, runeRedBright, igniteHeat) : (Color32?)null, igniteHeat);
            }

            // Glowing center pupil dot when active
            if (runesAwake)
            {
                Color32 pupilCol = Color32.Lerp(runeRedDim, isClimaxFrame ? runeGoldCore : runeRedBright, igniteHeat);
                sheet.FillCircle(cx, cy, isClimaxFrame ? 3 : 2, pupilCol, true);
            }

            // 5-pointed pentagram connecting k -> (k + 2) % 5, with each edge igniting crimson as candles light!
            for (int k = 0; k < 5; k++)
            {
                Vector2Int p0 = starPts[k];
                Vector2Int p1 = starPts[(k + 2) % 5];
                bool edgeIgnited = litCount > k;
                float edgeGlow = edgeIgnited ? (isClimaxFrame ? 1.0f : Mathf.Clamp01(0.62f + igniteHeat * 0.38f)) : (runesAwake ? 0.25f : 0f);
                Color32? edgeCol = edgeGlow > 0.02f
                    ? Color32.Lerp(runeRedDim, isClimaxFrame ? runeGoldCore : runeRedBright, edgeGlow)
                    : (Color32?)null;
                DrawThickChalkLine(sheet, p0.x, p0.y, p1.x, p1.y, ox, chalkBright, chalkMid, chalkDust, chalkEdge, 730 + k, edgeCol, edgeGlow);
            }

            // 4. Crimson floor light halos around each lit candle
            float loopPhase = isLoopFrame
                ? ((f - 12) / 4f) * (Mathf.PI * 2f)
                : (f * 1.15f);

            for (int k = 0; k < litCount; k++)
            {
                Vector2Int cp = candlePts[k];
                bool justFlared = !isLoopFrame && ((k == 0 && f == 2) || (k == 1 && f == 4) || (k == 2 && f == 6) || (k == 3 && f == 8) || (k == 4 && f == 10));
                float candlePhase = loopPhase + k * 1.37f;
                float haloRadius = (justFlared || isClimaxFrame) ? 16.5f : 13.0f;
                float haloStrength = (justFlared || isClimaxFrame) ? 135f : (92f + 16f * Mathf.Sin(candlePhase));

                int rCeil = Mathf.CeilToInt(haloRadius);
                for (int dy = -rCeil; dy <= rCeil; dy++)
                {
                    for (int dx = -rCeil; dx <= rCeil; dx++)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d <= haloRadius)
                        {
                            float falloff = Mathf.Pow(1f - d / haloRadius, 1.55f);
                            Color32 hc = (justFlared || isClimaxFrame)
                                ? new Color32(235, 62, 24, (byte)Mathf.Clamp(Mathf.RoundToInt(haloStrength * falloff), 0, 210))
                                : new Color32(195, 26, 20, (byte)Mathf.Clamp(Mathf.RoundToInt(haloStrength * falloff), 0, 180));
                            sheet.Blend(cp.x + dx, cp.y + dy, hc);
                        }
                    }
                }
            }

            // 5. Draw all 5 red-wax pillar candles + animated flames / ignition flares + sparks
            for (int k = 0; k < 5; k++)
            {
                Vector2Int cp = candlePts[k];
                // Stone/wax saucer shadow & coaster
                sheet.FillCircle(cp.x, cp.y - 2, 6, new Color32(10, 6, 8, 165), true);
                sheet.FillCircle(cp.x, cp.y - 1, 5, saucerDark, true);
                sheet.FillEllipse(cp.x, cp.y - 1, 4, 3, waxPool, true);

                // Pillar candle silhouette with dark outline + 3D shading + melted cream top
                sheet.FillRect(cp.x - 4, cp.y - 4, 8, 9, PAL_OUTLINE);
                sheet.FillRect(cp.x - 3, cp.y - 3, 6, 7, waxBody);
                sheet.FillRect(cp.x - 3, cp.y - 3, 2, 6, waxShadow);
                sheet.FillRect(cp.x + 1, cp.y - 2, 2, 5, waxHighlight);
                sheet.FillRect(cp.x - 3, cp.y + 3, 6, 2, waxCreamTop);
                sheet.Set(cp.x - 2, cp.y + 2, waxHighlight);
                sheet.Set(cp.x + 2, cp.y + 1, waxHighlight);

                // Wick
                sheet.Set(cp.x, cp.y + 5, wickDark);
                sheet.Set(cp.x, cp.y + 6, wickDark);

                // Pre-ignition ember spark over Candle #1 on Frame 1 (Awakening Runes)
                if (f == 1 && k == 0)
                {
                    sheet.Blend(cp.x, cp.y + 7, new Color32(255, 110, 40, 210));
                    sheet.Blend(cp.x - 1, cp.y + 9, new Color32(180, 150, 140, 120));
                    sheet.Blend(cp.x + 1, cp.y + 10, new Color32(255, 84, 32, 165));
                }

                if (k < litCount)
                {
                    bool justFlared = !isLoopFrame && ((k == 0 && f == 2) || (k == 1 && f == 4) || (k == 2 && f == 6) || (k == 3 && f == 8) || (k == 4 && f == 10));
                    float candlePhase = loopPhase + k * 1.37f;
                    float swaySign = (k % 2 == 0) ? 1f : -1f;
                    int midDx = justFlared ? 0 : Mathf.RoundToInt(swaySign * Mathf.Sin(candlePhase - 0.55f) * 1.15f);
                    int tipDx = justFlared ? Mathf.RoundToInt(swaySign * 1f) : Mathf.RoundToInt(swaySign * Mathf.Sin(candlePhase) * 2.1f + 0.6f * Mathf.Cos(candlePhase * 2f + k));

                    int baseH = justFlared ? 12 : (isClimaxFrame ? 11 : 8);
                    int flameH = baseH + (justFlared ? 0 : Mathf.RoundToInt(1.5f * Mathf.Sin(candlePhase + k * 0.9f) + 0.7f * Mathf.Cos(candlePhase * 2f - k)));
                    flameH = Mathf.Clamp(flameH, 7, 13);

                    int fyBase = cp.y + 6;
                    sheet.Blend(cp.x - 1, fyBase, flameOuter);
                    sheet.Blend(cp.x + 1, fyBase, flameOuter);
                    sheet.Blend(cp.x - 2, fyBase + 1, flameOuter);
                    sheet.Blend(cp.x + 2, fyBase + 1, flameOuter);

                    for (int h = 0; h < flameH; h++)
                    {
                        float t = (float)h / Mathf.Max(1, flameH - 1);
                        int fx = cp.x + Mathf.RoundToInt(Mathf.Lerp(0f, tipDx, t));
                        if (h == 1 || h == 2 || h == 3) fx = cp.x + midDx;

                        if (h <= 1)
                        {
                            sheet.Set(fx, fyBase + h, flameCore);
                            sheet.Blend(fx - 1, fyBase + h, flameBody);
                            sheet.Blend(fx + 1, fyBase + h, flameBody);
                        }
                        else if (h < flameH - 3)
                        {
                            sheet.Set(fx, fyBase + h, h <= 3 ? flameCore : flameBody);
                            sheet.Blend(fx - 1, fyBase + h, flameOuter);
                            sheet.Blend(fx + 1, fyBase + h, flameOuter);
                            if (justFlared || isClimaxFrame)
                            {
                                sheet.Blend(fx - 2, fyBase + h, new Color32(255, 60, 20, 145));
                                sheet.Blend(fx + 2, fyBase + h, new Color32(255, 60, 20, 145));
                            }
                        }
                        else if (h < flameH - 1)
                        {
                            sheet.Set(fx, fyBase + h, flameBody);
                            sheet.Blend(fx - 1, fyBase + h, flameOuter);
                            sheet.Blend(fx + 1, fyBase + h, flameOuter);
                        }
                        else
                        {
                            sheet.Set(fx, fyBase + h, flameTip);
                        }
                    }

                    // Floating ritual embers above lit candles
                    int sparkY = fyBase + flameH + 1 + ((f + k) % 3);
                    int sparkX = cp.x + tipDx + ((k % 2 == 0) ? 1 : -1);
                    sheet.Blend(sparkX, sparkY, (justFlared || isClimaxFrame) ? flameCore : flameTip);
                }
            }
        }

        sheet.SavePngAtAssetPath("Assets/Resources/Sprites/Ritual/summoning_circle_sheet.png");
    }

    private static void DrawThickChalkLine(
        PixelCanvas canvas,
        int x0,
        int y0,
        int x1,
        int y1,
        int ox,
        Color32 bright,
        Color32 mid,
        Color32 dust,
        Color32 edgeShadow,
        int seed,
        Color32? emberCol,
        float emberStrength)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int step = 0;
        while (true)
        {
            float n = Hash01(x0 - ox, y0 + step, seed);
            canvas.Blend(x0 - 1, y0 - 1, edgeShadow);
            canvas.Blend(x0 + 1, y0 - 1, edgeShadow);

            if (n > 0.08f)
            {
                Color32 baseC = n > 0.52f ? bright : mid;
                canvas.Blend(x0, y0, baseC);
                canvas.Blend(x0 + 1, y0, baseC);
                canvas.Blend(x0, y0 + 1, n > 0.35f ? mid : dust);
                if (emberCol.HasValue && emberStrength > 0.05f)
                {
                    Color32 ec = emberCol.Value;
                    ec.a = (byte)Mathf.Clamp(Mathf.RoundToInt(215f * emberStrength), 0, 235);
                    canvas.Blend(x0, y0, ec);
                    if (emberStrength > 0.55f)
                    {
                        canvas.Blend(x0 + 1, y0, ec);
                        canvas.Blend(x0, y0 + 1, ec);
                    }
                }
            }

            if (n > 0.72f)
            {
                canvas.Blend(x0 - 1, y0 + 1, dust);
            }

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
            step++;
        }
    }

    private static void DrawChalkLine(PixelCanvas canvas, int x0, int y0, int x1, int y1, int ox, Color32 bright, Color32 mid, Color32 dust, int seed)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int step = 0;
        while (true)
        {
            float n = Hash01(x0 - ox, y0 + step, seed);
            if (n > 0.14f)
            {
                canvas.Blend(x0, y0, n > 0.58f ? bright : mid);
            }
            if (n > 0.72f)
            {
                canvas.Blend(x0 + 1, y0, dust);
            }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
            step++;
        }
    }

    public static void GenerateGhostDeathSheet()
    {
        const int frameSize = 64;
        const int frameCount = 12;
        PixelCanvas sheet = new PixelCanvas(frameSize * frameCount, frameSize);

        Color32 bodyMid = new Color32(138, 138, 142, 255);
        Color32 bodyLight = new Color32(174, 174, 178, 255);
        Color32 bodyDark = new Color32(88, 88, 94, 255);
        Color32 headVoid = new Color32(36, 22, 10, 255);
        Color32 bloodBright = new Color32(192, 16, 14, 255);
        Color32 bloodDark = new Color32(114, 12, 12, 255);
        Color32 handTip = new Color32(238, 134, 22, 255);

        Color32 rayWhite = new Color32(255, 252, 224, 245);
        Color32 rayGold = new Color32(255, 224, 126, 215);
        Color32 rayCyan = new Color32(168, 244, 255, 195);
        Color32 fireHot = new Color32(255, 244, 182, 255);
        Color32 fireOrange = new Color32(255, 126, 34, 255);
        Color32 fireRed = new Color32(204, 42, 18, 255);
        Color32 ashDark = new Color32(38, 38, 44, 245);
        Color32 ashMid = new Color32(68, 68, 74, 235);

        for (int f = 0; f < frameCount; f++)
        {
            PixelCanvas frame = new PixelCanvas(frameSize, frameSize);

            if (f <= 2)
            {
                // Frames 0-2: Shrivel inward from top-down U-shaped ghost silhouette
                float t = f / 2f;
                float scaleX = Mathf.Lerp(1.0f, 0.78f, t);
                float scaleY = Mathf.Lerp(1.0f, 0.80f, t);
                int armGap = Mathf.RoundToInt(Mathf.Lerp(10f, 7f, t));
                int armTopY = Mathf.RoundToInt(Mathf.Lerp(47f, 41f, t));
                DrawGhostPose(frame, 32, 26, armGap, armTopY, scaleX, scaleY, f * 17, bodyMid, bodyLight, bodyDark, headVoid, bloodBright, bloodDark, handTip, 0f, false);
                frame.AddPixelOutline(PAL_OUTLINE);
            }
            else if (f <= 6)
            {
                // Frames 3-6: Arms throw wide & arch skyward while purification light rays burst outward
                float t = (f - 3) / 3f;
                int armGap = Mathf.RoundToInt(Mathf.Lerp(12f, 18f, t));
                int armTopY = Mathf.RoundToInt(Mathf.Lerp(45f, 52f, Mathf.Sin(t * Mathf.PI * 0.7f)));
                float glowMix = Mathf.Lerp(0.35f, 0.92f, t);

                // Radial purification burst behind/around the ghost
                int burstR = Mathf.RoundToInt(Mathf.Lerp(14f, 30f, t));
                for (int y = 2; y < 62; y++)
                {
                    for (int x = 2; x < 62; x++)
                    {
                        float dx = x - 32f;
                        float dy = y - 28f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d < burstR)
                        {
                            float ang = Mathf.Atan2(dy, dx);
                            float rayWave = 0.5f + 0.5f * Mathf.Sin(ang * 8f + f * 0.8f);
                            float radial = Mathf.Clamp01(1f - d / burstR);
                            float alpha = radial * (0.35f + 0.65f * rayWave);
                            if (alpha > 0.22f)
                            {
                                Color32 rc = d < 9f ? rayWhite : (rayWave > 0.6f ? rayGold : rayCyan);
                                rc.a = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 240f), 0, 245);
                                frame.Blend(x, y, rc);
                            }
                        }
                    }
                }

                DrawGhostPose(frame, 32, 26, armGap, armTopY, 0.96f, 0.92f, f * 29, bodyMid, bodyLight, bodyDark, headVoid, bloodBright, bloodDark, rayWhite, glowMix, true);
                frame.AddPixelOutline(new Color32(64, 48, 24, 255));

                // Foreground rays erupting from chest/head
                for (int r = 0; r < 8; r++)
                {
                    float rad = (r * 45f + f * 12f) * Mathf.Deg2Rad;
                    int len = Mathf.RoundToInt(Mathf.Lerp(10f, 27f, t));
                    int rx = 32 + Mathf.RoundToInt(Mathf.Cos(rad) * len);
                    int ry = 28 + Mathf.RoundToInt(Mathf.Sin(rad) * len);
                    frame.DrawLine(32, 28, rx, ry, r % 2 == 0 ? rayWhite : rayGold, true);
                }
            }
            else if (f <= 9)
            {
                // Frames 7-9: Incineration - fire consumes arms inward into rising sparks
                float t = (f - 7) / 2f;
                int armGap = Mathf.RoundToInt(Mathf.Lerp(16f, 9f, t));
                int armTopY = Mathf.RoundToInt(Mathf.Lerp(48f, 36f, t));

                DrawGhostPose(frame, 32, 26, armGap, armTopY, Mathf.Lerp(0.92f, 0.58f, t), Mathf.Lerp(0.88f, 0.60f, t), f * 41, bodyDark, bodyMid, ashDark, ashDark, fireRed, fireOrange, fireHot, 0.35f, false);

                // Erode outer silhouette into flames and sparks
                for (int y = 4; y < 60; y++)
                {
                    for (int x = 4; x < 60; x++)
                    {
                        Color32 cur = frame.Get(x, y);
                        float d = Mathf.Sqrt((x - 32f) * (x - 32f) + (y - 28f) * (y - 28f));
                        float n = Hash01(x, y, 800 + f);
                        if (cur.a > 0)
                        {
                            float burnEdge = d / 22f + t * 0.55f;
                            if (burnEdge > 0.85f && n > 0.35f)
                            {
                                frame.Set(x, y, n > 0.72f ? fireHot : (n > 0.52f ? fireOrange : fireRed));
                            }
                            else if (burnEdge > 1.15f && n > 0.25f)
                            {
                                frame.Set(x, y, new Color32(0, 0, 0, 0));
                            }
                        }
                        else if (d < 24f && n > 0.91f - t * 0.04f)
                        {
                            // Floating ember sparks
                            frame.Set(x, y + Mathf.RoundToInt(t * 4f), n > 0.95f ? fireHot : fireOrange);
                        }
                    }
                }
            }
            else
            {
                // Frames 10-11: Burn to ashes - smoldering charcoal pile + fading cinders
                bool finalFrame = (f == 11);
                int rx = finalFrame ? 7 : 10;
                int ry = finalFrame ? 4 : 6;
                for (int y = 20; y <= 38; y++)
                {
                    for (int x = 18; x <= 46; x++)
                    {
                        float nx = (x - 32f) / rx;
                        float ny = (y - 27f) / ry;
                        float dist = nx * nx + ny * ny;
                        float n = Hash01(x, y, 850 + f);
                        if (dist <= 1.0f && (!finalFrame || n > 0.42f))
                        {
                            Color32 ac = n > 0.88f
                                ? (finalFrame ? fireRed : fireOrange)
                                : (n > 0.52f ? ashMid : ashDark);
                            if (finalFrame) ac.a = (byte)Mathf.RoundToInt(ac.a * 0.65f);
                            frame.Set(x, y, ac);
                        }
                        else if (dist <= 1.65f && n > (finalFrame ? 0.90f : 0.82f))
                        {
                            Color32 cinder = n > 0.95f ? fireOrange : ashDark;
                            if (finalFrame) cinder.a = 120;
                            frame.Blend(x, y, cinder);
                        }
                    }
                }
                if (!finalFrame)
                {
                    frame.AddPixelOutline(PAL_OUTLINE);
                }
            }

            // Copy frame onto horizontal strip
            int ox = f * frameSize;
            for (int y = 0; y < frameSize; y++)
            {
                for (int x = 0; x < frameSize; x++)
                {
                    sheet.Set(ox + x, y, frame.Get(x, y));
                }
            }
        }

        sheet.SavePngAtAssetPath("Assets/Resources/Sprites/ghost_death_sheet.png");
    }

    private static void DrawGhostPose(
        PixelCanvas canvas,
        int cx,
        int cy,
        int armGap,
        int armTopY,
        float scaleX,
        float scaleY,
        int seed,
        Color32 bodyMid,
        Color32 bodyLight,
        Color32 bodyDark,
        Color32 headVoid,
        Color32 bloodBright,
        Color32 bloodDark,
        Color32 handTip,
        float glowMix,
        bool openArc)
    {
        Color32 glowWhite = new Color32(255, 250, 218, 255);
        Color32 TintGlow(Color32 c) => glowMix <= 0.001f ? c : Color32.Lerp(c, glowWhite, glowMix);

        int torsoRx = Mathf.Max(6, Mathf.RoundToInt(14f * scaleX));
        int torsoRy = Mathf.Max(6, Mathf.RoundToInt(11f * scaleY));

        // 1. Rounded U-shaped base body
        for (int y = cy - torsoRy; y <= cy + torsoRy; y++)
        {
            for (int x = cx - torsoRx; x <= cx + torsoRx; x++)
            {
                float nx = (x - cx) / (float)torsoRx;
                float ny = (y - cy) / (float)torsoRy;
                float r2 = nx * nx + ny * ny;
                if (r2 <= 1.0f)
                {
                    float n = Hash01(x, y, seed);
                    Color32 col = ny < -0.45f ? bodyDark : (Mathf.Abs(nx) < 0.45f ? bodyLight : bodyMid);
                    if (n > 0.84f) col = bloodDark;
                    if (n > 0.92f) col = bloodBright;
                    canvas.Set(x, y, TintGlow(col));
                }
            }
        }

        // 2. Two top-down arms reaching forward / arcing outward
        int armWidth = Mathf.Max(3, Mathf.RoundToInt(4.5f * scaleX));
        for (int side = -1; side <= 1; side += 2)
        {
            int startX = cx + side * Mathf.RoundToInt(9f * scaleX);
            int endX = cx + side * armGap;
            for (int y = cy + 2; y <= armTopY; y++)
            {
                float t = Mathf.Clamp01((float)(y - (cy + 2)) / Mathf.Max(1, armTopY - (cy + 2)));
                float curve = openArc ? Mathf.Sin(t * Mathf.PI * 0.5f) : t;
                int ax = Mathf.RoundToInt(Mathf.Lerp(startX, endX, curve));
                for (int dx = -armWidth / 2; dx <= armWidth / 2; dx++)
                {
                    float n = Hash01(ax + dx, y, seed + side * 7);
                    bool isTip = y >= armTopY - 2;
                    Color32 col = isTip ? handTip : (Mathf.Abs(dx) <= 1 ? bodyLight : bodyMid);
                    if (!isTip && n > 0.86f) col = n > 0.93f ? bloodBright : bloodDark;
                    canvas.Set(ax + dx, y, TintGlow(col));
                }
            }
        }

        // 3. Dark hollow center head/hood
        int headRx = Mathf.Max(4, Mathf.RoundToInt(6.5f * scaleX));
        int headRy = Mathf.Max(4, Mathf.RoundToInt(6.5f * scaleY));
        int headCy = cy + 1;
        for (int y = headCy - headRy; y <= headCy + headRy; y++)
        {
            for (int x = cx - headRx; x <= cx + headRx; x++)
            {
                float nx = (x - cx) / (float)headRx;
                float ny = (y - headCy) / (float)headRy;
                if (nx * nx + ny * ny <= 1.0f)
                {
                    float n = Hash01(x, y, seed + 55);
                    Color32 hc = n > 0.90f ? bloodDark : headVoid;
                    canvas.Set(x, y, glowMix > 0.65f ? Color32.Lerp(hc, glowWhite, glowMix * 0.7f) : hc);
                }
            }
        }
    }

    public static void GenerateGhostDyingShriekWav()
    {
        if (!Directory.Exists(AUDIO_DIR)) Directory.CreateDirectory(AUDIO_DIR);
        if (!Directory.Exists(RETRO_SFX_DIR)) Directory.CreateDirectory(RETRO_SFX_DIR);

        const int sampleRate = 44100;
        const float duration = 2.8f;
        WriteWavOverwrite(
            "ghost_dying_shriek.wav",
            sampleRate,
            duration,
            _ => HunterGhostBehavior.SynthesizeDyingShriekSamples(sampleRate, duration),
            RETRO_SFX_DIR);
    }

    private static void WriteWavIfMissing(string fileName, int sampleRate, float duration, Func<int, float[]> bufferFunc)
    {
        string path = Path.Combine(AUDIO_DIR, fileName).Replace('\\', '/');
        if (File.Exists(path)) return;
        WriteWavBuffer(path, sampleRate, bufferFunc(Mathf.FloorToInt(sampleRate * duration)));
    }

    private static void WriteWavOverwrite(string fileName, int sampleRate, float duration, Func<int, float[]> bufferFunc, string mirrorDir = null)
    {
        float[] samples = bufferFunc(Mathf.FloorToInt(sampleRate * duration));
        string mainPath = Path.Combine(AUDIO_DIR, fileName).Replace('\\', '/');
        WriteWavBuffer(mainPath, sampleRate, samples);

        if (!string.IsNullOrEmpty(mirrorDir))
        {
            if (!Directory.Exists(mirrorDir)) Directory.CreateDirectory(mirrorDir);
            string mirrorPath = Path.Combine(mirrorDir, fileName).Replace('\\', '/');
            WriteWavBuffer(mirrorPath, sampleRate, samples);
        }
    }

    private static void WriteWavBuffer(string path, int sampleRate, float[] sampleBuffer)
    {
        int samples = sampleBuffer.Length;
        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            int dataBytes = samples * 2;
            bw.Write(new[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + dataBytes);
            bw.Write(new[] { 'W', 'A', 'V', 'E' });
            bw.Write(new[] { 'f', 'm', 't', ' ' });
            bw.Write(16);
            bw.Write((short)1); // PCM
            bw.Write((short)1); // Mono
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(new[] { 'd', 'a', 't', 'a' });
            bw.Write(dataBytes);

            for (int i = 0; i < samples; i++)
            {
                short pcm = (short)Mathf.Clamp(Mathf.RoundToInt(sampleBuffer[i] * 32767f), -32767, 32767);
                bw.Write(pcm);
            }
        }
    }
}
