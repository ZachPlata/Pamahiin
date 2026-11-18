using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates a clean, minimalist dark mahogany &amp; brass architectural schematic PNG
/// for BarangayMap (6 Tanglewood Drive: Main Floor &amp; Basement) directly from the
/// exact room and doorway coordinates used by BarangayMapSceneBuilder.
/// </summary>
public static class BarangayBlueprintGenerator
{
    public const string BlueprintAssetPath = "Assets/UI/Journal/blueprint_barangay.png";

    private const int CanvasWidth = 840;
    private const int CanvasHeight = 980;
    private const float PxPerUnit = 142f;
    private const float MainOriginX = 146f;
    private const float MainOriginY = 648f;
    private const float BasementOriginX = 146f;
    private const float BasementOriginY = 876f;

    // Dark Mahogany & Brass Schematic Palette (aligned with Journal Cover & Lobby Banner)
    private static readonly Color32 ColBg = new Color32(28, 22, 18, 255);
    private static readonly Color32 ColGridMinor = new Color32(41, 32, 26, 255);
    private static readonly Color32 ColGridMajor = new Color32(54, 42, 33, 255);
    private static readonly Color32 ColFloor = new Color32(39, 31, 25, 255);
    private static readonly Color32 ColStairWell = new Color32(32, 25, 20, 255);
    private static readonly Color32 ColVoidPocket = new Color32(26, 20, 17, 255);
    private static readonly Color32 ColWall = new Color32(238, 224, 194, 255);
    private static readonly Color32 ColBrass = new Color32(215, 175, 92, 255);
    private static readonly Color32 ColBrassDim = new Color32(142, 110, 58, 255);
    private static readonly Color32 ColText = new Color32(244, 232, 206, 255);
    private static readonly Color32 ColTextMuted = new Color32(186, 162, 126, 255);
    private static readonly Color32 ColTextShadow = new Color32(16, 12, 10, 225);
    private static readonly Color32 ColFuse = new Color32(118, 232, 145, 255);
    private static readonly Color32 ColFuseFill = new Color32(28, 52, 34, 255);

    private sealed class BlueprintCanvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        public BlueprintCanvas(int w, int h, Color32 bg)
        {
            Width = w;
            Height = h;
            Pixels = new Color32[w * h];
            for (int i = 0; i < Pixels.Length; i++) Pixels[i] = bg;
        }

        public void BlendPixel(int x, int y, Color32 c)
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
                255);
        }

        public void FillRect(int x0, int y0, int x1, int y1, Color32 c)
        {
            int minX = Mathf.Min(x0, x1);
            int maxX = Mathf.Max(x0, x1);
            int minY = Mathf.Min(y0, y1);
            int maxY = Mathf.Max(y0, y1);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    BlendPixel(x, y, c);
                }
            }
        }

        public void DrawThickLine(float x0, float y0, float x1, float y1, float thickness, Color32 c)
        {
            int minX = Mathf.FloorToInt(Mathf.Min(x0, x1) - thickness);
            int maxX = Mathf.CeilToInt(Mathf.Max(x0, x1) + thickness);
            int minY = Mathf.FloorToInt(Mathf.Min(y0, y1) - thickness);
            int maxY = Mathf.CeilToInt(Mathf.Max(y0, y1) + thickness);

            float vx = x1 - x0;
            float vy = y1 - y0;
            float lenSq = vx * vx + vy * vy;
            float half = thickness * 0.5f;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float t = lenSq > 0.0001f ? Mathf.Clamp01(((x - x0) * vx + (y - y0) * vy) / lenSq) : 0f;
                    float px = x0 + t * vx;
                    float py = y0 + t * vy;
                    float dist = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                    if (dist <= half + 0.5f)
                    {
                        float cov = Mathf.Clamp01((half + 0.5f) - dist);
                        Color32 col = c;
                        col.a = (byte)Mathf.RoundToInt(c.a * cov);
                        BlendPixel(x, y, col);
                    }
                }
            }
        }

        public void DrawRectOutline(int x0, int y0, int x1, int y1, int thickness, Color32 c)
        {
            FillRect(x0, y1 - thickness + 1, x1, y1, c);
            FillRect(x0, y0, x1, y0 + thickness - 1, c);
            FillRect(x0, y0, x0 + thickness - 1, y1, c);
            FillRect(x1 - thickness + 1, y0, x1, y1, c);
        }
    }

    // Crisp 5x7 pixel font for architectural labels & directional markers
    private static readonly Dictionary<char, string[]> Font5x7 = new Dictionary<char, string[]>
    {
        ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
        ['C'] = new[] { ".####", "#....", "#....", "#....", "#....", "#....", ".####" },
        ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
        ['G'] = new[] { ".####", "#....", "#....", "#..##", "#...#", "#...#", ".####" },
        ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
        ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "#..#.", "#..#.", ".##.." },
        ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
        ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
        ['M'] = new[] { "#...#", "##.##", "#.#.#", "#...#", "#...#", "#...#", "#...#" },
        ['N'] = new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
        ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
        ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
        ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
        ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
        ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
        ['W'] = new[] { "#...#", "#...#", "#...#", "#...#", "#.#.#", "##.##", "#...#" },
        ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
        ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
        ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
        ['0'] = new[] { ".###.", "#..##", "#.#.#", "#.#.#", "##..#", "#...#", ".###." },
        ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
        ['2'] = new[] { ".###.", "#...#", "....#", "..##.", ".#...", "#....", "#####" },
        ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
        ['4'] = new[] { "#...#", "#...#", "#...#", "#####", "....#", "....#", "....#" },
        ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
        ['6'] = new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." },
        ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
        ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
        ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." },
        [' '] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." },
        ['.'] = new[] { ".....", ".....", ".....", ".....", ".....", ".##..", ".##.." },
        ['\''] = new[] { "..#..", "..#..", ".#...", ".....", ".....", ".....", "....." },
        ['/'] = new[] { "....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#...." },
        ['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
        ['|'] = new[] { "..#..", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['('] = new[] { "...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#." },
        [')'] = new[] { ".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#..." },
        ['▼'] = new[] { ".....", "#####", "#####", ".###.", ".###.", "..#..", "....." },
        ['▲'] = new[] { ".....", "..#..", ".###.", ".###.", "#####", "#####", "....." },
        ['▶'] = new[] { ".#...", ".##..", ".###.", ".####", ".###.", ".##..", ".#..." },
        ['◀'] = new[] { "...#.", "..##.", ".###.", "####.", ".###.", "..##.", "...#." },
        ['['] = new[] { ".###.", ".#...", ".#...", ".#...", ".#...", ".#...", ".###." },
        [']'] = new[] { ".###.", "...#.", "...#.", "...#.", "...#.", "...#.", ".###." },
    };

    [MenuItem("Pamahiin/Generate Journal Blueprint PNG")]
    public static void GenerateBlueprintPng()
    {
        var canvas = new BlueprintCanvas(CanvasWidth, CanvasHeight, ColBg);

        DrawBackgroundGridAndFrame(canvas);
        DrawMainFloor(canvas);
        DrawBasement(canvas);
        DrawLegendFooter(canvas);

        string dir = Path.GetDirectoryName(BlueprintAssetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        Texture2D tex = new Texture2D(CanvasWidth, CanvasHeight, TextureFormat.RGBA32, false);
        tex.SetPixels32(canvas.Pixels);
        tex.Apply();
        File.WriteAllBytes(BlueprintAssetPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(BlueprintAssetPath, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(BlueprintAssetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        AssetDatabase.ImportAsset("Assets/UI/Journal/JournalUI.uss", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/UI/Journal/JournalUI.uxml", ImportAssetOptions.ForceUpdate);

        Debug.Log($"[BarangayBlueprintGenerator] Generated crisp blueprint asset at {BlueprintAssetPath}");
    }

    private static void DrawBackgroundGridAndFrame(BlueprintCanvas c)
    {
        const int cell = 24;
        for (int x = 16; x < CanvasWidth - 16; x += cell)
        {
            bool major = ((x - 16) / cell) % 4 == 0;
            c.FillRect(x, 16, x, CanvasHeight - 16, major ? ColGridMajor : ColGridMinor);
        }
        for (int y = 16; y < CanvasHeight - 16; y += cell)
        {
            bool major = ((y - 16) / cell) % 4 == 0;
            c.FillRect(16, y, CanvasWidth - 16, y, major ? ColGridMajor : ColGridMinor);
        }

        // Double-line brass schematic border
        c.DrawRectOutline(10, 10, CanvasWidth - 11, CanvasHeight - 11, 2, ColBrassDim);
        c.DrawRectOutline(16, 16, CanvasWidth - 17, CanvasHeight - 17, 1, ColBrass);
    }

    private static void DrawMainFloor(BlueprintCanvas c)
    {
        // Section Header
        DrawCenteredText(c, "MAIN FLOOR", CanvasWidth * 0.5f, 944, 3, ColBrass, shadow: true);
        c.FillRect(76, 920, CanvasWidth - 77, 921, ColBrassDim);

        // 1. Interior Floor Silhouettes
        FillWorldRect(c, MainOriginX, MainOriginY, new Rect(0.00f, 0.40f, 4.48f, 1.15f), ColFloor);      // North wing
        FillWorldRect(c, MainOriginX, MainOriginY, new Rect(1.54f, 1.55f, 1.22f, 0.10f), ColFloor);      // Nursery bump
        FillWorldRect(c, MainOriginX, MainOriginY, new Rect(0.00f, 0.22f, 0.28f, 0.18f), ColVoidPocket); // Boys closet recess (unmarked)
        FillWorldRect(c, MainOriginX, MainOriginY, new Rect(0.28f, -0.08f, 4.20f, 0.48f), ColFloor);     // Foyer + Living upper
        FillWorldRect(c, MainOriginX, MainOriginY, new Rect(0.00f, -1.85f, 4.48f, 1.77f), ColFloor);     // South wing
        FillWorldRect(c, MainOriginX, MainOriginY, new Rect(1.54f, 0.40f, 0.34f, 0.30f), ColVoidPocket); // Hallway closet pocket (unmarked)
        FillWorldRect(c, MainOriginX, MainOriginY, new Rect(1.62f, -0.34f, 0.34f, 0.26f), ColVoidPocket);// Foyer closet pocket (unmarked)
        FillWorldRect(c, MainOriginX, MainOriginY, new Rect(3.12f, -0.78f, 1.36f, 0.36f), ColStairWell); // Stairwell

        // 2. Stairwell Treads (descending eastward at x=[3.70..4.34, -0.74..-0.46])
        DrawHorizontalStairTreads(c, MainOriginX, MainOriginY, new Rect(3.70f, -0.74f, 0.64f, 0.28f), 8, pointsRight: true);

        // 3. Outer Perimeter Walls (4px)
        const float extT = 4f;
        DrawWorldWall(c, MainOriginX, MainOriginY, 0.00f, 1.55f, 1.54f, 1.55f, extT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 1.54f, 1.55f, 1.54f, 1.65f, extT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 1.54f, 1.65f, 2.76f, 1.65f, extT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 2.76f, 1.65f, 2.76f, 1.55f, extT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 2.76f, 1.55f, 4.48f, 1.55f, extT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 4.48f, -1.85f, 4.48f, 1.55f, extT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 0.00f, -1.85f, 4.48f, -1.85f, extT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 0.00f, -1.85f, 0.00f, -0.08f, extT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 0.00f, -0.08f, 0.28f, -0.08f, extT);
        DrawWorldVerticalWallWithDoor(c, MainOriginX, MainOriginY, 0.28f, -0.08f, 0.22f, 0.06f, 0.11f, extT, highlightDoor: true); // Front Door
        DrawWorldWall(c, MainOriginX, MainOriginY, 0.00f, 0.22f, 0.28f, 0.22f, extT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 0.28f, 0.22f, 0.28f, 0.40f, 3f);
        DrawWorldWall(c, MainOriginX, MainOriginY, 0.00f, 0.22f, 0.00f, 1.55f, extT);

        // 4. Interior Partitions & Doorway Gaps (3px)
        const float intT = 3f;
        const float doorHalf = 0.13f;

        DrawWorldWall(c, MainOriginX, MainOriginY, 0.92f, 0.40f, 0.92f, 1.55f, intT); // Boys / Bath
        DrawWorldWall(c, MainOriginX, MainOriginY, 1.54f, 0.40f, 1.54f, 1.55f, intT); // Bath / Nursery
        DrawWorldWall(c, MainOriginX, MainOriginY, 1.88f, 0.40f, 1.88f, 0.70f, intT); // Hall closet pocket R
        DrawWorldWall(c, MainOriginX, MainOriginY, 1.54f, 0.70f, 1.88f, 0.70f, intT); // Hall closet pocket Top
        DrawWorldWall(c, MainOriginX, MainOriginY, 2.76f, 0.40f, 2.76f, 1.55f, intT); // Nursery / Master

        DrawWorldVerticalWallWithDoor(c, MainOriginX, MainOriginY, 3.82f, 0.96f, 1.55f, 1.18f, doorHalf, intT); // Master Bath
        DrawWorldVerticalWallWithDoor(c, MainOriginX, MainOriginY, 3.82f, 0.40f, 0.96f, 0.64f, doorHalf, intT); // Master Closet
        DrawWorldWall(c, MainOriginX, MainOriginY, 3.82f, 0.96f, 4.48f, 0.96f, intT); // M.Bath / M.Closet

        // North Corridor Wall (y = 0.40) with room doorways (solid across cabinet recesses so hiding spots stay hidden)
        DrawWorldHorizontalWallWithDoors(c, MainOriginX, MainOriginY, 0.00f, 4.48f, 0.40f,
            new[] { 0.64f, 1.18f, 2.16f, 3.04f }, doorHalf, intT);

        // Foyer South Wall + Foyer Closet structural pocket
        DrawWorldWall(c, MainOriginX, MainOriginY, 0.28f, -0.08f, 2.42f, -0.08f, intT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 1.62f, -0.34f, 1.96f, -0.34f, intT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 1.96f, -0.34f, 1.96f, -0.08f, intT);

        // Garage East + Utility Walls
        DrawWorldVerticalWallWithDoor(c, MainOriginX, MainOriginY, 1.62f, -1.85f, -0.08f, -0.56f, doorHalf, intT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 1.62f, -0.78f, 2.42f, -0.78f, intT);
        DrawWorldVerticalWallWithDoor(c, MainOriginX, MainOriginY, 2.42f, -0.78f, -0.08f, -0.56f, doorHalf, intT);

        // Main Floor Stairwell Enclosure
        DrawWorldWall(c, MainOriginX, MainOriginY, 3.12f, -0.42f, 4.48f, -0.42f, intT);
        DrawWorldWall(c, MainOriginX, MainOriginY, 3.12f, -0.78f, 4.48f, -0.78f, intT);
        DrawWorldVerticalWallWithDoor(c, MainOriginX, MainOriginY, 3.12f, -0.78f, -0.42f, -0.60f, 0.11f, intT);

        // 5. Front Porch Entry Callout
        float porchX = MainOriginX + 0.28f * PxPerUnit;
        float porchY = MainOriginY + 0.06f * PxPerUnit;
        c.DrawThickLine(porchX - 38f, porchY, porchX - 10f, porchY, 2.5f, ColBrass);
        DrawCenteredText(c, "▶", porchX - 8f, porchY, 2, ColBrass, shadow: true);
        DrawCenteredText(c, "FRONT", porchX - 78f, porchY + 10f, 2, ColBrass, shadow: true);
        DrawCenteredText(c, "DOOR", porchX - 78f, porchY - 8f, 2, ColBrass, shadow: true);

        // 6. Concise Room Labels (scale 2 = 10x14 px in 840x980)
        DrawWorldLabel(c, MainOriginX, MainOriginY, "BOYS'\nROOM", 0.46f, 0.98f, 2, ColText);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "BATH", 1.23f, 0.98f, 2, ColText);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "NURSERY", 2.18f, 1.16f, 2, ColText);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "MASTER\nBEDROOM", 3.29f, 0.98f, 2, ColText);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "M.\nBATH", 4.15f, 1.26f, 2, ColText);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "CLOSET", 4.15f, 0.68f, 2, ColText);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "FOYER / HALL", 1.35f, 0.16f, 2, ColText);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "LIVING ROOM", 3.62f, 0.00f, 2, ColText);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "GARAGE", 0.81f, -0.78f, 2, ColText);
        DrawFuseBoxMarker(c, MainOriginX, MainOriginY, 1.50f, -1.18f, 0.82f, -1.32f);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "UTILITY", 2.02f, -0.56f, 2, ColText);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "KITCHEN", 2.43f, -1.32f, 2, ColText);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "DINING\nROOM", 3.86f, -1.32f, 2, ColText);
        DrawWorldLabel(c, MainOriginX, MainOriginY, "STAIRS\n▼", 3.41f, -0.60f, 2, ColBrass);
    }

    private static void DrawBasement(BlueprintCanvas c)
    {
        // Section Divider + Header
        c.FillRect(76, 354, CanvasWidth - 77, 355, ColBrassDim);
        DrawCenteredText(c, "BASEMENT", CanvasWidth * 0.5f, 330, 3, ColBrass, shadow: true);

        // 1. Interior Floor Silhouettes
        FillWorldRect(c, BasementOriginX, BasementOriginY, new Rect(1.00f, -5.80f, 2.80f, 1.70f), ColFloor);
        FillWorldRect(c, BasementOriginX, BasementOriginY, new Rect(0.64f, -5.55f, 0.36f, 0.80f), ColFloor);
        FillWorldRect(c, BasementOriginX, BasementOriginY, new Rect(1.42f, -4.46f, 0.86f, 0.36f), ColStairWell);

        // 2. Stairwell Treads (descending westward in NW corner [1.52..2.24, -4.42..-4.14])
        DrawHorizontalStairTreads(c, BasementOriginX, BasementOriginY, new Rect(1.52f, -4.42f, 0.72f, 0.28f), 8, pointsRight: false);

        // 3. Basement Outer Walls (4px) + Stairwell Partition (3px)
        const float extT = 4f;
        const float intT = 3f;

        DrawWorldWall(c, BasementOriginX, BasementOriginY, 1.00f, -4.10f, 3.80f, -4.10f, extT); // North
        DrawWorldWall(c, BasementOriginX, BasementOriginY, 1.00f, -5.80f, 3.80f, -5.80f, extT); // South
        DrawWorldWall(c, BasementOriginX, BasementOriginY, 3.80f, -5.80f, 3.80f, -4.10f, extT); // East
        DrawWorldWall(c, BasementOriginX, BasementOriginY, 1.00f, -4.75f, 1.00f, -4.10f, extT); // West Upper
        DrawWorldWall(c, BasementOriginX, BasementOriginY, 0.64f, -4.75f, 1.00f, -4.75f, extT); // Alcove Top
        DrawWorldWall(c, BasementOriginX, BasementOriginY, 0.64f, -5.55f, 0.64f, -4.75f, extT); // Alcove West
        DrawWorldWall(c, BasementOriginX, BasementOriginY, 0.64f, -5.55f, 1.00f, -5.55f, extT); // Alcove Bot
        DrawWorldWall(c, BasementOriginX, BasementOriginY, 1.00f, -5.80f, 1.00f, -5.55f, extT); // West Lower

        // NW Stairwell Partition (wall on left/west end, open on right/east entry)
        DrawWorldWall(c, BasementOriginX, BasementOriginY, 1.42f, -4.46f, 2.28f, -4.46f, intT);
        DrawWorldWall(c, BasementOriginX, BasementOriginY, 1.42f, -4.46f, 1.42f, -4.10f, intT);

        // 4. Basement Labels
        DrawWorldLabel(c, BasementOriginX, BasementOriginY, "STAIRS ▲", 1.88f, -4.64f, 2, ColBrass);
        DrawWorldLabel(c, BasementOriginX, BasementOriginY, "BASEMENT", 2.40f, -5.22f, 2, ColText);
    }

    private static void DrawLegendFooter(BlueprintCanvas c)
    {
        DrawCenteredText(c, "6 TANGLEWOOD DRIVE  |  ▶ ENTRY  |  [F] FUSE BOX  |  ▼ / ▲ STAIRS", CanvasWidth * 0.5f, 32, 2, ColTextMuted, shadow: false);
    }

    private static void DrawFuseBoxMarker(
        BlueprintCanvas c, float originX, float originY,
        float boxWx, float boxWy, float labelWx, float labelWy)
    {
        float bx = originX + boxWx * PxPerUnit;
        float by = originY + boxWy * PxPerUnit;
        float lx = originX + labelWx * PxPerUnit;
        float ly = originY + labelWy * PxPerUnit;

        int bx0 = Mathf.RoundToInt(bx - 9f);
        int by0 = Mathf.RoundToInt(by - 11f);
        int bx1 = Mathf.RoundToInt(bx + 9f);
        int by1 = Mathf.RoundToInt(by + 11f);

        float dx = bx - lx;
        float dy = by - ly;
        if (Mathf.Abs(dx) > 1f || Mathf.Abs(dy) > 1f)
        {
            float tLabel = Mathf.Min(
                Mathf.Abs(dx) > 0.01f ? 52f / Mathf.Abs(dx) : 1f,
                Mathf.Abs(dy) > 0.01f ? 11f / Mathf.Abs(dy) : 1f);
            float tBadge = Mathf.Min(
                Mathf.Abs(dx) > 0.01f ? 12f / Mathf.Abs(dx) : 1f,
                Mathf.Abs(dy) > 0.01f ? 14f / Mathf.Abs(dy) : 1f);
            if (tLabel + tBadge < 0.95f)
            {
                c.DrawThickLine(
                    lx + dx * tLabel,
                    ly + dy * tLabel,
                    bx - dx * tBadge,
                    by - dy * tBadge,
                    2f,
                    ColFuse);
            }
        }

        c.FillRect(bx0 - 1, by0 - 1, bx1 + 1, by1 + 1, ColTextShadow);
        c.FillRect(bx0, by0, bx1, by1, ColFuseFill);
        c.DrawRectOutline(bx0, by0, bx1, by1, 2, ColFuse);
        DrawCenteredText(c, "F", bx, by, 2, ColFuse, shadow: false);

        DrawCenteredText(c, "FUSE BOX", lx, ly, 2, ColFuse, shadow: true);
    }

    private static void FillWorldRect(BlueprintCanvas c, float originX, float originY, Rect r, Color32 col)
    {
        int x0 = Mathf.RoundToInt(originX + r.xMin * PxPerUnit);
        int x1 = Mathf.RoundToInt(originX + r.xMax * PxPerUnit);
        int y0 = Mathf.RoundToInt(originY + r.yMin * PxPerUnit);
        int y1 = Mathf.RoundToInt(originY + r.yMax * PxPerUnit);
        c.FillRect(x0, y0, x1, y1, col);
    }

    private static void DrawWorldWall(BlueprintCanvas c, float originX, float originY, float wx0, float wy0, float wx1, float wy1, float thickness)
    {
        c.DrawThickLine(
            originX + wx0 * PxPerUnit,
            originY + wy0 * PxPerUnit,
            originX + wx1 * PxPerUnit,
            originY + wy1 * PxPerUnit,
            thickness,
            ColWall);
    }

    private static void DrawWorldVerticalWallWithDoor(
        BlueprintCanvas c, float originX, float originY,
        float wx, float wyMin, float wyMax, float doorY, float halfGap, float thickness,
        bool highlightDoor = false)
    {
        float dBot = Mathf.Max(wyMin, doorY - halfGap);
        float dTop = Mathf.Min(wyMax, doorY + halfGap);
        if (dBot > wyMin + 0.01f) DrawWorldWall(c, originX, originY, wx, wyMin, wx, dBot, thickness);
        if (wyMax > dTop + 0.01f) DrawWorldWall(c, originX, originY, wx, dTop, wx, wyMax, thickness);

        if (highlightDoor)
        {
            c.DrawThickLine(
                originX + wx * PxPerUnit,
                originY + dBot * PxPerUnit,
                originX + wx * PxPerUnit,
                originY + dTop * PxPerUnit,
                2.5f,
                ColBrass);
        }
    }

    private static void DrawWorldHorizontalWallWithDoors(
        BlueprintCanvas c, float originX, float originY,
        float wxMin, float wxMax, float wy, float[] doorCenters, float halfGap, float thickness)
    {
        float cursor = wxMin;
        for (int i = 0; i < doorCenters.Length; i++)
        {
            float dLeft = Mathf.Max(cursor, doorCenters[i] - halfGap);
            float dRight = Mathf.Min(wxMax, doorCenters[i] + halfGap);
            if (dLeft > cursor + 0.01f) DrawWorldWall(c, originX, originY, cursor, wy, dLeft, wy, thickness);
            cursor = dRight;
        }
        if (wxMax > cursor + 0.01f) DrawWorldWall(c, originX, originY, cursor, wy, wxMax, wy, thickness);
    }

    private static void DrawHorizontalStairTreads(
        BlueprintCanvas c, float originX, float originY, Rect stairRect, int steps, bool pointsRight)
    {
        float px0 = originX + stairRect.xMin * PxPerUnit;
        float px1 = originX + stairRect.xMax * PxPerUnit;
        float py0 = originY + stairRect.yMin * PxPerUnit;
        float py1 = originY + stairRect.yMax * PxPerUnit;
        float midY = (py0 + py1) * 0.5f;

        c.DrawRectOutline(
            Mathf.RoundToInt(px0), Mathf.RoundToInt(py0),
            Mathf.RoundToInt(px1), Mathf.RoundToInt(py1),
            2, ColBrassDim);

        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps;
            float sx = Mathf.Lerp(px0, px1, t);
            c.DrawThickLine(sx, py0 + 2f, sx, py1 - 2f, 1.5f, ColBrassDim);
        }

        // Directional center arrow
        c.DrawThickLine(px0 + 8f, midY, px1 - 8f, midY, 2f, ColBrass);
        DrawCenteredText(c, pointsRight ? "▶" : "◀", pointsRight ? px1 - 9f : px0 + 9f, midY, 2, ColBrass, shadow: true);
    }

    private static void DrawWorldLabel(BlueprintCanvas c, float originX, float originY, string text, float wx, float wy, int scale, Color32 col)
    {
        DrawCenteredText(c, text, originX + wx * PxPerUnit, originY + wy * PxPerUnit, scale, col, shadow: true);
    }

    private static void DrawCenteredText(BlueprintCanvas c, string text, float cx, float cy, int scale, Color32 col, bool shadow)
    {
        if (string.IsNullOrEmpty(text)) return;
        string[] lines = text.Split('\n');
        int charW = 5 * scale;
        int charH = 7 * scale;
        int charStep = charW + scale;
        int lineStep = charH + scale * 2;
        float totalH = lines.Length * charH + (lines.Length - 1) * (scale * 2);

        for (int li = 0; li < lines.Length; li++)
        {
            string line = lines[li].ToUpperInvariant();
            float lineW = line.Length > 0 ? line.Length * charStep - scale : 0f;
            float startX = cx - lineW * 0.5f;
            float lineTopY = cy + totalH * 0.5f - li * lineStep;

            for (int ci = 0; ci < line.Length; ci++)
            {
                char ch = line[ci];
                if (!Font5x7.TryGetValue(ch, out string[] rows))
                {
                    if (!Font5x7.TryGetValue(' ', out rows)) continue;
                }

                int gx = Mathf.RoundToInt(startX + ci * charStep);
                int gyTop = Mathf.RoundToInt(lineTopY);

                for (int r = 0; r < 7; r++)
                {
                    string row = rows[r];
                    for (int colIdx = 0; colIdx < 5; colIdx++)
                    {
                        if (row[colIdx] != '#') continue;
                        int px = gx + colIdx * scale;
                        // Texture2D y=0 is bottom, so row 0 is top of glyph
                        int py = gyTop - (r + 1) * scale;
                        if (shadow)
                        {
                            c.FillRect(px + 1, py - 1, px + scale, py + scale - 2, ColTextShadow);
                        }
                        c.FillRect(px, py, px + scale - 1, py + scale - 1, col);
                    }
                }
            }
        }
    }
}
