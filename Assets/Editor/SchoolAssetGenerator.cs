using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates pure 90-degree top-down 16-bit SNES horror pixel art textures
/// for San Nicolas High School (SchoolMap) under Assets/Resources/Sprites/SchoolMap/.
/// </summary>
public static class SchoolAssetGenerator
{
    public const string SPRITE_ROOT = "Assets/Resources/Sprites/SchoolMap";

    // Dark 16-bit SNES horror palette with mandatory #100C0E pixel outlines
    private static readonly Color32 PAL_OUTLINE = new Color32(16, 12, 14, 255);
    private static readonly Color32 PAL_MAHOGANY_DK = new Color32(34, 21, 14, 255);
    private static readonly Color32 PAL_MAHOGANY_MD = new Color32(64, 41, 26, 255);
    private static readonly Color32 PAL_MAHOGANY_LT = new Color32(96, 64, 40, 255);
    private static readonly Color32 PAL_OAK_MD = new Color32(118, 86, 54, 255);
    private static readonly Color32 PAL_OAK_LT = new Color32(146, 110, 72, 255);
    private static readonly Color32 PAL_SLATE_DK = new Color32(22, 25, 31, 255);
    private static readonly Color32 PAL_SLATE_MD = new Color32(45, 50, 59, 255);
    private static readonly Color32 PAL_SLATE_LT = new Color32(76, 84, 96, 255);
    private static readonly Color32 PAL_BRASS = new Color32(178, 140, 62, 255);
    private static readonly Color32 PAL_TEAL_DK = new Color32(30, 68, 74, 255);
    private static readonly Color32 PAL_TEAL_LT = new Color32(52, 112, 118, 255);
    private static readonly Color32 PAL_BURGUNDY_DK = new Color32(82, 24, 32, 255);
    private static readonly Color32 PAL_BURGUNDY_LT = new Color32(126, 40, 52, 255);
    private static readonly Color32 PAL_CERAMIC_DK = new Color32(124, 134, 138, 255);
    private static readonly Color32 PAL_CERAMIC_LT = new Color32(172, 182, 186, 255);
    private static readonly Color32 PAL_CHALK_GREEN = new Color32(28, 52, 42, 255);
    private static readonly Color32 PAL_CRT_BEIGE = new Color32(122, 116, 104, 255);
    private static readonly Color32 PAL_CRT_CYAN = new Color32(74, 198, 182, 255);
    private static readonly Color32 PAL_CRT_GLOW = new Color32(148, 244, 224, 255);
    private static readonly Color32 PAL_FOLIAGE_DK = new Color32(16, 32, 22, 255);
    private static readonly Color32 PAL_FOLIAGE_MD = new Color32(26, 52, 32, 255);
    private static readonly Color32 PAL_FOLIAGE_LT = new Color32(42, 76, 46, 255);

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

        public void FillRect(int x, int y, int w, int h, Color32 c)
        {
            for (int yy = y; yy < y + h; yy++)
            {
                for (int xx = x; xx < x + w; xx++)
                {
                    Set(xx, yy, c);
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

        public void StrokeRect(int x, int y, int w, int h, int t, Color32 c)
        {
            FillRect(x, y, w, t, c);
            FillRect(x, y + h - t, w, t, c);
            FillRect(x, y, t, h, c);
            FillRect(x + w - t, y, t, h, c);
        }

        public void FillCircle(int cx, int cy, int r, Color32 c)
        {
            int r2 = r * r;
            for (int yy = cy - r; yy <= cy + r; yy++)
            {
                for (int xx = cx - r; xx <= cx + r; xx++)
                {
                    int dx = xx - cx;
                    int dy = yy - cy;
                    if (dx * dx + dy * dy <= r2) Set(xx, yy, c);
                }
            }
        }

        public void DrawLine(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Set(x0, y0, c);
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
                    int idx = y * Width + x;
                    if (copy[idx].a >= alphaThreshold) continue;
                    bool touch =
                        (x > 0 && copy[idx - 1].a >= alphaThreshold) ||
                        (x < Width - 1 && copy[idx + 1].a >= alphaThreshold) ||
                        (y > 0 && copy[idx - Width].a >= alphaThreshold) ||
                        (y < Height - 1 && copy[idx + Width].a >= alphaThreshold);
                    if (touch) Pixels[idx] = outlineCol;
                }
            }
        }

        public void BlitFrame(PixelCanvas src, int destX, int destY)
        {
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    Color32 c = src.Get(x, y);
                    if (c.a > 0) Set(destX + x, destY + y, c);
                }
            }
        }
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7FFFFFFF) / 2147483647f;
        }
    }

    private static Color32 Shade(Color32 c, float factor)
    {
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * factor), 0, 255),
            c.a);
    }

    private static void WritePng(string relativePath, PixelCanvas canvas)
    {
        string fullPath = Path.Combine(SPRITE_ROOT, relativePath);
        string dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        Texture2D tex = new Texture2D(canvas.Width, canvas.Height, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.SetPixels32(canvas.Pixels);
        tex.Apply(false, false);

        byte[] png = tex.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(tex);
        File.WriteAllBytes(fullPath, png);
    }

    [MenuItem("Pamahiin/Generate SchoolMap Sprites")]
    public static void GenerateAllAssets()
    {
        GenerateFloorTiles();
        GenerateWallsAndDoors();
        GenerateFurniture();
        GeneratePropSheets();

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        Debug.Log("[SchoolAssetGenerator] Generated all 16-bit SNES horror SchoolMap sprites at " + SPRITE_ROOT);
    }

    private static void GenerateFloorTiles()
    {
        // 1. Floor 1 weathered blue-slate checkerboard
        var f1Checker = new PixelCanvas(32, 32);
        Color32 tileA = new Color32(28, 37, 46, 255);
        Color32 tileB = new Color32(38, 49, 60, 255);
        Color32 grout = new Color32(18, 22, 27, 255);
        for (int ty = 0; ty < 2; ty++)
        {
            for (int tx = 0; tx < 2; tx++)
            {
                Color32 baseC = ((tx + ty) % 2 == 0) ? tileA : tileB;
                f1Checker.FillDitheredRect(tx * 16, ty * 16, 16, 16, baseC, 101 + tx * 7 + ty * 13, 0.14f);
                f1Checker.StrokeRect(tx * 16, ty * 16, 16, 16, 1, grout);
            }
        }
        f1Checker.DrawLine(5, 22, 13, 27, grout);
        f1Checker.DrawLine(20, 6, 26, 11, grout);
        WritePng("Floors/tile_hall_f1_checker.png", f1Checker);

        // 2. Floor 2/3 cracked institutional concrete
        var concrete = new PixelCanvas(32, 32);
        concrete.FillDitheredRect(0, 0, 32, 32, new Color32(39, 43, 51, 255), 202, 0.16f);
        concrete.FillRect(0, 0, 32, 1, grout);
        concrete.FillRect(0, 0, 1, 32, grout);
        concrete.FillRect(16, 0, 1, 32, Shade(grout, 1.2f));
        concrete.FillRect(0, 16, 32, 1, Shade(grout, 1.2f));
        concrete.DrawLine(4, 10, 12, 15, PAL_OUTLINE);
        concrete.DrawLine(19, 24, 27, 19, grout);
        WritePng("Floors/tile_hall_f2_f3_concrete.png", concrete);

        // 3. Classroom scuffed linoleum grid
        var classGrid = new PixelCanvas(32, 32);
        for (int ty = 0; ty < 2; ty++)
        {
            for (int tx = 0; tx < 2; tx++)
            {
                Color32 baseC = ((tx + ty) % 2 == 0) ? new Color32(44, 50, 56, 255) : new Color32(49, 56, 62, 255);
                classGrid.FillDitheredRect(tx * 16, ty * 16, 16, 16, baseC, 303 + tx + ty * 5, 0.12f);
                classGrid.StrokeRect(tx * 16, ty * 16, 16, 16, 1, new Color32(28, 32, 36, 255));
            }
        }
        classGrid.DrawLine(7, 8, 12, 9, new Color32(60, 66, 70, 255));
        classGrid.DrawLine(21, 22, 26, 24, new Color32(34, 38, 42, 255));
        WritePng("Floors/tile_classroom_grid.png", classGrid);

        // 4. Restroom grimy dark 8x8 mosaic
        var restroom = new PixelCanvas(32, 32);
        Color32 rrGrout = new Color32(15, 20, 22, 255);
        for (int ty = 0; ty < 4; ty++)
        {
            for (int tx = 0; tx < 4; tx++)
            {
                Color32 baseC = ((tx + ty) % 2 == 0) ? new Color32(30, 42, 44, 255) : new Color32(38, 52, 54, 255);
                restroom.FillDitheredRect(tx * 8, ty * 8, 8, 8, baseC, 404 + tx * 3 + ty * 9, 0.18f);
                restroom.StrokeRect(tx * 8, ty * 8, 8, 8, 1, rrGrout);
            }
        }
        restroom.FillCircle(21, 10, 4, new Color32(20, 28, 25, 255));
        WritePng("Floors/tile_restroom_mosaic.png", restroom);

        // 5. Library aged mahogany planks
        var wood = new PixelCanvas(32, 32);
        int[] plankHeights = { 8, 8, 8, 8 };
        int yCursor = 0;
        for (int p = 0; p < plankHeights.Length; p++)
        {
            int h = plankHeights[p];
            Color32 plankBase = (p % 2 == 0) ? PAL_MAHOGANY_DK : PAL_MAHOGANY_MD;
            wood.FillDitheredRect(0, yCursor, 32, h, plankBase, 505 + p * 11, 0.14f);
            for (int x = 0; x < 32; x++)
            {
                if (Hash01(x, yCursor + 2, 511 + p) > 0.72f)
                {
                    wood.Set(x, yCursor + 3, Shade(plankBase, 0.78f));
                }
            }
            wood.FillRect(0, yCursor, 32, 1, PAL_OUTLINE);
            int seamX = (p * 11 + 6) % 28 + 2;
            wood.FillRect(seamX, yCursor, 1, h, PAL_OUTLINE);
            wood.Set(seamX + 1, yCursor + 2, PAL_BRASS);
            wood.Set(seamX + 1, yCursor + h - 2, PAL_BRASS);
            yCursor += h;
        }
        WritePng("Floors/tile_library_wood.png", wood);

        // 6. Workshop oil-stained concrete
        var workshop = new PixelCanvas(32, 32);
        workshop.FillDitheredRect(0, 0, 32, 32, new Color32(34, 37, 40, 255), 606, 0.18f);
        workshop.StrokeRect(0, 0, 32, 32, 1, PAL_OUTLINE);
        workshop.FillCircle(11, 18, 5, new Color32(22, 22, 24, 255));
        workshop.FillCircle(23, 8, 3, new Color32(26, 23, 21, 255));
        workshop.DrawLine(3, 28, 15, 22, new Color32(20, 22, 24, 255));
        WritePng("Floors/tile_workshop_concrete.png", workshop);

        // 7. ComLab raised access tiles
        var comlab = new PixelCanvas(32, 32);
        for (int ty = 0; ty < 2; ty++)
        {
            for (int tx = 0; tx < 2; tx++)
            {
                int ox = tx * 16;
                int oy = ty * 16;
                comlab.FillDitheredRect(ox, oy, 16, 16, new Color32(30, 36, 46, 255), 707 + tx + ty * 3, 0.10f);
                comlab.StrokeRect(ox, oy, 16, 16, 1, PAL_OUTLINE);
                comlab.StrokeRect(ox + 2, oy + 2, 12, 12, 1, new Color32(22, 27, 35, 255));
                comlab.Set(ox + 2, oy + 2, new Color32(58, 66, 80, 255));
                comlab.Set(ox + 13, oy + 13, new Color32(58, 66, 80, 255));
            }
        }
        WritePng("Floors/tile_comlab_raised.png", comlab);

        // 8. Courtyard stone pavers
        var courtyard = new PixelCanvas(32, 32);
        Color32 stoneA = new Color32(36, 40, 46, 255);
        Color32 stoneB = new Color32(43, 48, 55, 255);
        for (int ty = 0; ty < 2; ty++)
        {
            for (int tx = 0; tx < 2; tx++)
            {
                Color32 baseC = ((tx + ty) % 2 == 0) ? stoneA : stoneB;
                courtyard.FillDitheredRect(tx * 16, ty * 16, 16, 16, baseC, 808 + tx + ty * 7, 0.16f);
                courtyard.StrokeRect(tx * 16, ty * 16, 16, 16, 1, new Color32(20, 25, 24, 255));
            }
        }
        courtyard.FillRect(14, 15, 4, 2, new Color32(28, 48, 30, 255));
        WritePng("Floors/tile_courtyard_paving.png", courtyard);

        // 9. Exterior lawn overgrown grass
        var grass = new PixelCanvas(32, 32);
        grass.FillDitheredRect(0, 0, 32, 32, PAL_FOLIAGE_DK, 909, 0.25f);
        for (int i = 0; i < 42; i++)
        {
            int gx = Mathf.FloorToInt(Hash01(i, 1, 910) * 30) + 1;
            int gy = Mathf.FloorToInt(Hash01(i, 2, 911) * 30) + 1;
            Color32 blade = (i % 3 == 0) ? PAL_FOLIAGE_LT : PAL_FOLIAGE_MD;
            grass.Set(gx, gy, blade);
            grass.Set(gx, gy + 1, Shade(blade, 1.15f));
            if (i % 5 == 0) grass.Set(gx + 1, gy, new Color32(52, 46, 30, 255));
        }
        WritePng("Floors/tile_grass_lawn.png", grass);
    }

    private static void GenerateWallsAndDoors()
    {
        // 1. Wall plaster body (16x16 seamless wall block with dark outline edges)
        var plaster = new PixelCanvas(16, 16);
        plaster.FillDitheredRect(0, 0, 16, 16, PAL_SLATE_MD, 1001, 0.15f);
        plaster.FillRect(0, 0, 16, 2, PAL_OUTLINE);
        plaster.FillRect(0, 14, 16, 2, PAL_OUTLINE);
        plaster.FillRect(0, 2, 16, 2, PAL_SLATE_DK);
        plaster.FillRect(0, 12, 16, 2, PAL_SLATE_LT);
        WritePng("Walls/wall_school_plaster.png", plaster);

        // 2. Wall trim cap (16x16 dark crown with crisp border and bevel)
        var cap = new PixelCanvas(16, 16);
        cap.FillDitheredRect(0, 0, 16, 16, new Color32(34, 38, 46, 255), 1002, 0.12f);
        cap.StrokeRect(0, 0, 16, 16, 2, PAL_OUTLINE);
        cap.FillRect(2, 12, 12, 2, PAL_SLATE_LT);
        cap.FillRect(2, 2, 12, 2, PAL_SLATE_DK);
        WritePng("Walls/wall_school_trim_cap.png", cap);

        // 3. Wooden school classroom door (32x12 top-down leaf)
        var woodDoor = new PixelCanvas(32, 12);
        woodDoor.FillRect(0, 0, 32, 12, PAL_OUTLINE);
        woodDoor.FillDitheredRect(1, 2, 30, 8, PAL_MAHOGANY_MD, 1003, 0.14f);
        woodDoor.StrokeRect(2, 3, 28, 6, 1, PAL_MAHOGANY_LT);
        woodDoor.FillRect(2, 4, 4, 4, PAL_MAHOGANY_DK);
        woodDoor.FillRect(26, 4, 4, 4, PAL_MAHOGANY_DK);
        woodDoor.FillRect(25, 1, 4, 2, PAL_BRASS);
        woodDoor.FillRect(25, 9, 4, 2, PAL_BRASS);
        woodDoor.FillRect(2, 5, 2, 2, PAL_SLATE_LT);
        WritePng("Walls/door_school_wood.png", woodDoor);

        // 4. Reinforced metal exit door (32x12 top-down steel entrance)
        var metalDoor = new PixelCanvas(32, 12);
        metalDoor.FillRect(0, 0, 32, 12, PAL_OUTLINE);
        metalDoor.FillDitheredRect(1, 2, 30, 8, PAL_SLATE_MD, 1004, 0.10f);
        metalDoor.FillRect(3, 3, 26, 2, PAL_SLATE_LT);
        metalDoor.FillRect(3, 7, 26, 2, PAL_SLATE_DK);
        metalDoor.FillRect(8, 4, 16, 4, new Color32(28, 44, 52, 255)); // wired reinforcement glass inset
        metalDoor.FillRect(22, 1, 6, 1, PAL_BRASS); // brass push/pull handle bar
        metalDoor.FillRect(22, 10, 6, 1, PAL_BRASS);
        WritePng("Walls/door_metal_exit.png", metalDoor);

        // 5. Restroom stall partition door (24x8)
        var stallDoor = new PixelCanvas(24, 8);
        stallDoor.FillRect(0, 0, 24, 8, PAL_OUTLINE);
        stallDoor.FillDitheredRect(1, 1, 22, 6, new Color32(48, 64, 68, 255), 1005, 0.10f);
        stallDoor.FillRect(2, 2, 20, 1, new Color32(72, 92, 96, 255));
        stallDoor.FillRect(18, 2, 3, 4, PAL_BRASS);
        WritePng("Walls/door_stall.png", stallDoor);

        // 6. Locker bay door (20x8)
        var lockerDoor = new PixelCanvas(20, 8);
        lockerDoor.FillRect(0, 0, 20, 8, PAL_OUTLINE);
        lockerDoor.FillDitheredRect(1, 1, 18, 6, new Color32(64, 72, 82, 255), 1006, 0.12f);
        for (int lx = 4; lx <= 10; lx += 3)
        {
            lockerDoor.FillRect(lx, 2, 2, 4, PAL_SLATE_DK);
        }
        lockerDoor.FillRect(15, 2, 2, 4, PAL_BRASS);
        WritePng("Walls/locker_door.png", lockerDoor);
    }

    private static void GenerateFurniture()
    {
        // 1. Student desk (20x14)
        var sDesk = new PixelCanvas(20, 14);
        sDesk.FillRect(0, 0, 20, 14, PAL_OUTLINE);
        sDesk.FillDitheredRect(1, 1, 18, 12, PAL_OAK_MD, 2001, 0.12f);
        sDesk.StrokeRect(1, 1, 18, 12, 1, PAL_MAHOGANY_DK);
        sDesk.FillRect(3, 10, 14, 1, PAL_MAHOGANY_DK); // pencil groove
        sDesk.FillRect(5, 3, 6, 5, new Color32(198, 190, 168, 255)); // notebook page
        sDesk.FillRect(6, 4, 4, 1, PAL_SLATE_DK);
        sDesk.FillRect(6, 6, 3, 1, PAL_SLATE_DK);
        WritePng("Furniture/desk_student.png", sDesk);

        // 2. Teacher desk (36x18)
        var tDesk = new PixelCanvas(36, 18);
        tDesk.FillRect(0, 0, 36, 18, PAL_OUTLINE);
        tDesk.FillDitheredRect(1, 1, 34, 16, PAL_MAHOGANY_MD, 2002, 0.14f);
        tDesk.StrokeRect(2, 2, 32, 14, 1, PAL_MAHOGANY_LT);
        tDesk.FillRect(10, 4, 14, 10, new Color32(34, 48, 42, 255)); // desk blotter
        tDesk.FillRect(12, 6, 5, 7, new Color32(196, 184, 162, 255)); // gradebook
        tDesk.FillRect(18, 6, 5, 7, new Color32(186, 174, 152, 255));
        tDesk.FillRect(28, 11, 3, 3, new Color32(118, 44, 34, 255)); // mug / ink
        WritePng("Furniture/desk_teacher.png", tDesk);

        // 3. Dean mahogany executive desk (48x22)
        var dDesk = new PixelCanvas(48, 22);
        dDesk.FillRect(0, 0, 48, 22, PAL_OUTLINE);
        dDesk.FillDitheredRect(1, 1, 46, 20, PAL_MAHOGANY_DK, 2003, 0.12f);
        dDesk.StrokeRect(2, 2, 44, 18, 1, PAL_MAHOGANY_LT);
        dDesk.FillRect(1, 1, 3, 3, PAL_BRASS);
        dDesk.FillRect(44, 1, 3, 3, PAL_BRASS);
        dDesk.FillRect(1, 18, 3, 3, PAL_BRASS);
        dDesk.FillRect(44, 18, 3, 3, PAL_BRASS);
        dDesk.FillRect(13, 4, 22, 14, new Color32(28, 52, 38, 255)); // emerald leather inlay
        dDesk.FillRect(5, 6, 6, 8, new Color32(170, 140, 90, 255)); // folders
        dDesk.FillRect(37, 6, 6, 6, PAL_OUTLINE); // rotary telephone base
        dDesk.FillRect(38, 7, 4, 4, PAL_SLATE_MD);
        WritePng("Furniture/desk_dean_mahogany.png", dDesk);

        // 4. U-shaped counter (48x32)
        var uCounter = new PixelCanvas(48, 32);
        // Back wall rail + Left & Right side arms (bottom open for operator entry)
        uCounter.FillDitheredRect(1, 21, 46, 10, PAL_MAHOGANY_MD, 2004, 0.14f);
        uCounter.FillDitheredRect(1, 1, 11, 21, PAL_MAHOGANY_MD, 2005, 0.14f);
        uCounter.FillDitheredRect(36, 1, 11, 21, PAL_MAHOGANY_MD, 2006, 0.14f);
        uCounter.FillRect(2, 22, 44, 2, PAL_MAHOGANY_LT);
        uCounter.FillRect(2, 2, 9, 1, PAL_MAHOGANY_LT);
        uCounter.FillRect(37, 2, 9, 1, PAL_MAHOGANY_LT);
        uCounter.FillRect(21, 24, 6, 4, PAL_BRASS); // checkout registry / bell
        uCounter.AddPixelOutline(PAL_OUTLINE);
        WritePng("Furniture/desk_u_counter.png", uCounter);

        // 5. Teal student chair (12x12)
        var cTeal = new PixelCanvas(12, 12);
        cTeal.FillRect(2, 2, 8, 7, PAL_TEAL_DK);
        cTeal.FillRect(2, 8, 8, 3, PAL_TEAL_LT); // top-down backrest bar
        cTeal.FillRect(1, 3, 1, 6, PAL_SLATE_LT);
        cTeal.FillRect(10, 3, 1, 6, PAL_SLATE_LT);
        cTeal.AddPixelOutline(PAL_OUTLINE);
        WritePng("Furniture/chair_teal.png", cTeal);

        // 6. Red plush theater chair (14x14)
        var cTheater = new PixelCanvas(14, 14);
        cTheater.FillRect(3, 2, 8, 8, PAL_BURGUNDY_DK);
        cTheater.FillRect(3, 9, 8, 3, PAL_BURGUNDY_LT);
        cTheater.FillRect(1, 3, 2, 8, PAL_MAHOGANY_LT); // wooden armrests
        cTheater.FillRect(11, 3, 2, 8, PAL_MAHOGANY_LT);
        cTheater.Set(6, 10, PAL_BRASS);
        cTheater.AddPixelOutline(PAL_OUTLINE);
        WritePng("Furniture/chair_theater_red.png", cTheater);

        // 7. Industrial workshop bench (44x16)
        var bench = new PixelCanvas(44, 16);
        bench.FillRect(0, 0, 44, 16, PAL_OUTLINE);
        bench.FillDitheredRect(1, 1, 42, 14, PAL_OAK_MD, 2007, 0.16f);
        bench.FillRect(1, 5, 42, 1, PAL_MAHOGANY_DK);
        bench.FillRect(1, 10, 42, 1, PAL_MAHOGANY_DK);
        bench.FillRect(4, 2, 6, 5, PAL_SLATE_MD); // metal bench vise
        bench.FillRect(3, 3, 2, 3, PAL_SLATE_LT);
        bench.FillRect(28, 7, 10, 4, PAL_SLATE_DK); // saw / wrench silhouette
        bench.FillRect(30, 8, 6, 2, PAL_SLATE_LT);
        WritePng("Furniture/bench_workshop.png", bench);

        // 8. ComLab CRT Workstation Off & 2-Frame Flicker Sheet (24x18 per frame)
        var pcOff = BuildPcWorkstationFrame(flickerMode: 0);
        WritePng("Furniture/pc_workstation_off.png", pcOff);

        var pcSheet = new PixelCanvas(48, 18);
        pcSheet.BlitFrame(BuildPcWorkstationFrame(flickerMode: 1), 0, 0);
        pcSheet.BlitFrame(BuildPcWorkstationFrame(flickerMode: 2), 24, 0);
        WritePng("Furniture/pc_workstation_flicker_sheet.png", pcSheet);

        // 9. Bookshelf top-down (48x16)
        var shelf = new PixelCanvas(48, 16);
        shelf.FillRect(0, 0, 48, 16, PAL_OUTLINE);
        shelf.FillDitheredRect(1, 1, 46, 14, PAL_MAHOGANY_DK, 2008, 0.12f);
        shelf.StrokeRect(2, 2, 44, 12, 1, PAL_MAHOGANY_MD);
        Color32[] bookCols =
        {
            new Color32(118, 42, 38, 255),
            new Color32(42, 74, 96, 255),
            new Color32(48, 86, 54, 255),
            PAL_BRASS,
            PAL_MAHOGANY_LT
        };
        for (int bx = 3; bx < 44; bx += 3)
        {
            if (Hash01(bx, 2, 2009) > 0.22f)
            {
                Color32 bc = bookCols[(bx / 3) % bookCols.Length];
                shelf.FillRect(bx, 1, 2, 3, bc);
                shelf.FillRect(bx, 12, 2, 3, bc);
            }
        }
        WritePng("Furniture/bookshelf_topdown.png", shelf);

        // 10. Locker bank 4-bay (44x14)
        var lockers = new PixelCanvas(44, 14);
        lockers.FillRect(0, 0, 44, 14, PAL_OUTLINE);
        for (int b = 0; b < 4; b++)
        {
            int bx = 1 + b * 11;
            int bw = (b == 3) ? 9 : 10;
            lockers.FillDitheredRect(bx, 1, bw, 12, PAL_SLATE_MD, 2010 + b, 0.12f);
            lockers.FillRect(bx + 1, 10, bw - 2, 2, PAL_SLATE_LT);
            lockers.FillRect(bx + 2, 3, bw - 4, 1, PAL_OUTLINE);
            lockers.FillRect(bx + 2, 5, bw - 4, 1, PAL_OUTLINE);
            lockers.FillRect(bx + bw - 1, 1, 1, 12, PAL_OUTLINE);
        }
        WritePng("Furniture/locker_bank.png", lockers);

        // 11. Toilet stall top-down (22x26)
        var stall = new PixelCanvas(22, 26);
        stall.FillRect(0, 0, 3, 26, PAL_SLATE_MD);
        stall.FillRect(19, 0, 3, 26, PAL_SLATE_MD);
        stall.FillRect(0, 23, 22, 3, PAL_SLATE_DK);
        // Cistern + Porcelain toilet rim & dark water bowl
        stall.FillRect(6, 17, 10, 5, PAL_CERAMIC_LT);
        stall.FillCircle(11, 11, 5, PAL_CERAMIC_LT);
        stall.FillCircle(11, 11, 3, new Color32(28, 42, 46, 255));
        stall.Set(15, 20, PAL_BRASS);
        stall.AddPixelOutline(PAL_OUTLINE);
        WritePng("Furniture/toilet_stall_topdown.png", stall);

        // 12. Sink unit (18x14)
        var sink = new PixelCanvas(18, 14);
        sink.FillRect(0, 0, 18, 14, PAL_OUTLINE);
        sink.FillDitheredRect(1, 1, 16, 12, PAL_CERAMIC_DK, 2015, 0.10f);
        sink.FillRect(3, 3, 12, 7, PAL_CERAMIC_LT);
        sink.FillRect(4, 4, 10, 5, new Color32(34, 48, 52, 255));
        sink.FillRect(8, 6, 2, 2, PAL_OUTLINE);
        sink.FillRect(7, 10, 4, 3, PAL_SLATE_LT);
        WritePng("Furniture/sink_unit.png", sink);

        // 13. Chalkboard ledge (48x10)
        var board = new PixelCanvas(48, 10);
        board.FillRect(0, 0, 48, 10, PAL_OUTLINE);
        board.FillRect(1, 3, 46, 6, PAL_CHALK_GREEN);
        board.FillRect(1, 1, 46, 2, PAL_MAHOGANY_LT); // chalk tray
        board.FillRect(10, 1, 4, 1, new Color32(230, 230, 224, 255)); // chalk stick
        board.FillRect(26, 1, 6, 2, PAL_SLATE_DK); // eraser
        WritePng("Furniture/chalkboard_ledge.png", board);

        // 14. Animated tree canopy sheet (128x64 = 2 frames of 64x64)
        var treeSheet = new PixelCanvas(128, 64);
        treeSheet.BlitFrame(BuildTreeCanopyFrame(shiftX: 0), 0, 0);
        treeSheet.BlitFrame(BuildTreeCanopyFrame(shiftX: 1), 64, 0);
        WritePng("Furniture/tree_canopy_sheet.png", treeSheet);

        // 15. Withered skeletal courtyard tree (48x48)
        var withered = new PixelCanvas(48, 48);
        withered.FillCircle(24, 24, 5, PAL_MAHOGANY_DK);
        withered.DrawLine(24, 24, 10, 10, PAL_MAHOGANY_MD);
        withered.DrawLine(10, 10, 5, 16, PAL_MAHOGANY_MD);
        withered.DrawLine(10, 10, 12, 4, PAL_MAHOGANY_LT);
        withered.DrawLine(24, 24, 38, 11, PAL_MAHOGANY_MD);
        withered.DrawLine(38, 11, 43, 19, PAL_MAHOGANY_LT);
        withered.DrawLine(24, 24, 11, 37, PAL_MAHOGANY_MD);
        withered.DrawLine(11, 37, 6, 33, PAL_MAHOGANY_LT);
        withered.DrawLine(24, 24, 37, 38, PAL_MAHOGANY_MD);
        withered.DrawLine(37, 38, 33, 44, PAL_MAHOGANY_LT);
        withered.AddPixelOutline(PAL_OUTLINE);
        WritePng("Furniture/tree_withered.png", withered);
    }

    private static PixelCanvas BuildPcWorkstationFrame(int flickerMode)
    {
        var pc = new PixelCanvas(24, 18);
        pc.FillRect(0, 0, 24, 18, PAL_OUTLINE);
        pc.FillDitheredRect(1, 1, 22, 16, PAL_OAK_MD, 2020, 0.10f);
        // Beige CRT monitor casing at rear
        pc.FillRect(6, 9, 12, 7, PAL_CRT_BEIGE);
        pc.StrokeRect(6, 9, 12, 7, 1, PAL_OUTLINE);
        Color32 screenCol = flickerMode switch
        {
            1 => new Color32(34, 98, 92, 255),
            2 => PAL_CRT_GLOW,
            _ => new Color32(18, 22, 26, 255)
        };
        pc.FillRect(7, 9, 10, 3, screenCol);
        if (flickerMode > 0)
        {
            pc.Set(16, 10, flickerMode == 2 ? PAL_CRT_CYAN : new Color32(60, 220, 90, 255));
        }
        // Keyboard & mouse
        pc.FillRect(4, 3, 12, 4, new Color32(168, 162, 148, 255));
        pc.FillRect(5, 4, 10, 2, PAL_SLATE_MD);
        pc.FillRect(18, 3, 3, 4, new Color32(168, 162, 148, 255));
        return pc;
    }

    private static PixelCanvas BuildTreeCanopyFrame(int shiftX)
    {
        var tree = new PixelCanvas(64, 64);
        tree.FillCircle(32 + shiftX, 32, 24, PAL_FOLIAGE_DK);
        tree.FillCircle(23, 27, 14, PAL_FOLIAGE_MD);
        tree.FillCircle(40 + shiftX, 26, 13, PAL_FOLIAGE_MD);
        tree.FillCircle(32, 41, 14, PAL_FOLIAGE_MD);
        tree.FillCircle(25 + shiftX, 36, 9, PAL_FOLIAGE_LT);
        tree.FillCircle(38, 34, 8, PAL_FOLIAGE_LT);
        tree.AddPixelOutline(PAL_OUTLINE);
        return tree;
    }

    private static void GeneratePropSheets()
    {
        // All prop sheets follow 6-frame 24x24 layout (144x24 @ 64 PPU) compatible with GhostPropInteractable
        WritePng("Props/prop_chair_sheet.png", Build6FramePropSheet((f, frameIdx) =>
        {
            int ox = frameIdx == 0 ? 0 : (frameIdx <= 3 ? (frameIdx % 2 == 0 ? -1 : 1) : 2);
            int oy = frameIdx >= 4 ? -2 : (frameIdx == 2 ? 2 : 0);
            f.FillRect(8 + ox, 8 + oy, 8, 7, PAL_TEAL_DK);
            f.FillRect(8 + ox, frameIdx >= 4 ? 7 : 13 + oy, 8, 3, PAL_TEAL_LT);
            f.AddPixelOutline(PAL_OUTLINE);
        }));

        WritePng("Props/prop_books_sheet.png", Build6FramePropSheet((f, frameIdx) =>
        {
            if (frameIdx == 0)
            {
                f.FillRect(7, 8, 10, 8, new Color32(118, 42, 38, 255));
                f.FillRect(8, 9, 8, 6, new Color32(42, 74, 96, 255));
            }
            else if (frameIdx <= 3)
            {
                int dy = (frameIdx % 2 == 0) ? 1 : -1;
                f.FillRect(7, 8 + dy, 9, 8, new Color32(118, 42, 38, 255));
                f.FillRect(9, 10 - dy, 8, 7, new Color32(42, 74, 96, 255));
            }
            else
            {
                f.FillRect(3, 5, 7, 5, new Color32(118, 42, 38, 255));
                f.FillRect(13, 14, 7, 5, new Color32(42, 74, 96, 255));
                f.FillRect(14, 5, 6, 5, new Color32(210, 200, 175, 255));
            }
            f.AddPixelOutline(PAL_OUTLINE);
        }));

        WritePng("Props/prop_keyboard_sheet.png", Build6FramePropSheet((f, frameIdx) =>
        {
            if (frameIdx == 0)
            {
                f.FillRect(5, 9, 12, 5, new Color32(170, 164, 150, 255));
                f.FillRect(6, 10, 10, 3, PAL_SLATE_MD);
                f.FillRect(18, 9, 2, 4, new Color32(170, 164, 150, 255));
            }
            else if (frameIdx <= 3)
            {
                int dx = frameIdx % 2 == 0 ? 1 : -1;
                f.FillRect(5 + dx, 10, 12, 5, new Color32(170, 164, 150, 255));
                f.FillRect(6 + dx, 11, 10, 3, PAL_SLATE_MD);
            }
            else
            {
                f.FillRect(4, 13, 11, 4, new Color32(170, 164, 150, 255));
                f.FillRect(16, 4, 3, 4, new Color32(170, 164, 150, 255));
            }
            f.AddPixelOutline(PAL_OUTLINE);
        }));

        WritePng("Props/prop_mug_sheet.png", Build6FramePropSheet((f, frameIdx) =>
        {
            if (frameIdx < 4)
            {
                int dy = (frameIdx % 2 == 1) ? 1 : 0;
                f.FillCircle(12, 12 + dy, 4, PAL_CERAMIC_LT);
                f.FillCircle(12, 12 + dy, 2, PAL_MAHOGANY_DK);
            }
            else
            {
                f.FillCircle(7, 11, 4, new Color32(48, 28, 16, 255)); // spilled coffee puddle
                f.FillRect(13, 9, 5, 6, PAL_CERAMIC_LT);
            }
            f.AddPixelOutline(PAL_OUTLINE);
        }));

        WritePng("Props/prop_papers_sheet.png", Build6FramePropSheet((f, frameIdx) =>
        {
            Color32 paper = new Color32(212, 204, 182, 255);
            if (frameIdx == 0)
            {
                f.FillRect(8, 7, 8, 10, paper);
                f.FillRect(9, 14, 5, 1, PAL_SLATE_MD);
                f.FillRect(9, 11, 6, 1, new Color32(150, 42, 42, 255));
            }
            else
            {
                int spread = Mathf.Min(frameIdx * 2, 6);
                f.FillRect(Mathf.Max(2, 8 - spread), 5, 6, 7, paper);
                f.FillRect(Mathf.Min(16, 10 + spread), 13, 6, 7, paper);
                f.FillRect(10, Mathf.Max(2, 8 - spread / 2), 6, 7, Shade(paper, 0.90f));
            }
            f.AddPixelOutline(PAL_OUTLINE);
        }));

        WritePng("Props/prop_tools_sheet.png", Build6FramePropSheet((f, frameIdx) =>
        {
            if (frameIdx < 4)
            {
                int off = frameIdx == 0 ? 0 : (frameIdx % 2 == 0 ? -1 : 1);
                f.DrawLine(6 + off, 7, 17 + off, 16, PAL_SLATE_LT);
                f.FillRect(15 + off, 14, 4, 4, PAL_SLATE_LT);
                f.DrawLine(17 - off, 7, 7 - off, 16, PAL_MAHOGANY_LT);
            }
            else
            {
                f.DrawLine(3, 6, 11, 9, PAL_SLATE_LT);
                f.DrawLine(12, 16, 20, 12, PAL_MAHOGANY_LT);
                f.FillRect(19, 10, 3, 4, PAL_SLATE_MD);
            }
            f.AddPixelOutline(PAL_OUTLINE);
        }));
    }

    private static PixelCanvas Build6FramePropSheet(Action<PixelCanvas, int> drawFrame)
    {
        var sheet = new PixelCanvas(144, 24);
        for (int i = 0; i < 6; i++)
        {
            var frame = new PixelCanvas(24, 24);
            drawFrame(frame, i);
            sheet.BlitFrame(frame, i * 24, 0);
        }
        return sheet;
    }
}
