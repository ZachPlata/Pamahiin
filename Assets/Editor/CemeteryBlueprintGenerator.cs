using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates the dark mahogany &amp; brass architectural schematic PNG for
/// Kampo Santo ng San Nicolas (Assets/UI/Journal/blueprint_cemetery.png).
/// </summary>
public static class CemeteryBlueprintGenerator
{
    public const string BlueprintAssetPath = "Assets/UI/Journal/blueprint_cemetery.png";

    private const int CanvasWidth = 840;
    private const int CanvasHeight = 980;
    private const float PxPerUnit = 80f;
    private const float OriginX = 104f;
    private const float OriginY = 472f;

    private static readonly Color32 ColBg = new Color32(28, 22, 18, 255);
    private static readonly Color32 ColGridMinor = new Color32(41, 32, 26, 255);
    private static readonly Color32 ColGridMajor = new Color32(54, 42, 33, 255);
    private static readonly Color32 ColGround = new Color32(36, 30, 24, 255);
    private static readonly Color32 ColPath = new Color32(54, 45, 36, 255);
    private static readonly Color32 ColBuilding = new Color32(46, 36, 29, 255);
    private static readonly Color32 ColBlock = new Color32(72, 58, 44, 255);
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
        ['/'] = new[] { "....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#...." },
        ['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
        ['|'] = new[] { "..#..", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['&'] = new[] { ".##..", "#..#.", ".##..", ".###.", "#...#", "#..#.", ".##.#" },
        ['('] = new[] { "...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#." },
        [')'] = new[] { ".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#..." },
        ['['] = new[] { ".###.", ".#...", ".#...", ".#...", ".#...", ".#...", ".###." },
        [']'] = new[] { ".###.", "...#.", "...#.", "...#.", "...#.", "...#.", ".###." },
        ['▶'] = new[] { ".#...", ".##..", ".###.", ".####", ".###.", ".##..", ".#..." }
    };

    [MenuItem("Pamahiin/Generate Cemetery Blueprint")]
    public static void GenerateBlueprintPng()
    {
        var c = new BlueprintCanvas(CanvasWidth, CanvasHeight, ColBg);
        DrawBackgroundGridAndFrame(c);
        DrawCemeteryLayout(c);
        DrawLegendFooter(c);

        string dir = Path.GetDirectoryName(BlueprintAssetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        Texture2D tex = new Texture2D(CanvasWidth, CanvasHeight, TextureFormat.RGBA32, false);
        tex.SetPixels32(c.Pixels);
        tex.Apply();
        File.WriteAllBytes(BlueprintAssetPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

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

        Debug.Log($"[CemeteryBlueprintGenerator] Generated crisp blueprint asset at {BlueprintAssetPath}");
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

        c.DrawRectOutline(10, 10, CanvasWidth - 11, CanvasHeight - 11, 2, ColBrassDim);
        c.DrawRectOutline(16, 16, CanvasWidth - 17, CanvasHeight - 17, 1, ColBrass);
    }

    private static void DrawCemeteryLayout(BlueprintCanvas c)
    {
        DrawCenteredText(c, "KAMPO SANTO NG SAN NICOLAS", CanvasWidth * 0.5f, 926, 3, ColBrass, shadow: true);
        DrawCenteredText(c, "PUBLIC CEMETERY COMPOUND", CanvasWidth * 0.5f, 892, 2, ColTextMuted, shadow: true);
        c.FillRect(68, 868, CanvasWidth - 69, 869, ColBrassDim);

        // 1. Compound Base Grounds & Main Stone Avenues
        FillWorldRect(c, new Rect(0.00f, -3.10f, 8.50f, 6.50f), ColGround);
        FillWorldRect(c, new Rect(0.02f, -0.16f, 8.33f, 0.56f), ColPath); // E-W Avenue
        FillWorldRect(c, new Rect(3.80f, -2.95f, 0.56f, 6.20f), ColPath); // N-S Avenue
        FillWorldRect(c, new Rect(3.53f, -0.43f, 1.10f, 1.10f), ColPath); // Central Cross Plaza

        // Central Cross Monument outline
        DrawWorldRectBox(c, new Rect(3.92f, -0.04f, 0.32f, 0.32f), ColBrassDim, ColBrass);

        // 2. Perimeter Wall (4px) with West FrontGate gap y=[-0.13..+0.37]
        const float extT = 4f;
        DrawWorldWall(c, 0.00f, 3.40f, 8.50f, 3.40f, extT);
        DrawWorldWall(c, 0.00f, -3.10f, 8.50f, -3.10f, extT);
        DrawWorldWall(c, 8.50f, -3.10f, 8.50f, 3.40f, extT);
        DrawWorldVerticalWallWithDoor(c, 0.00f, -3.10f, 3.40f, 0.12f, 0.25f, extT, highlightDoor: true);

        // Front Gate Callout on West Side
        float gateX = OriginX;
        float gateY = OriginY + 0.12f * PxPerUnit;
        c.DrawThickLine(gateX - 28f, gateY, gateX - 8f, gateY, 2.5f, ColBrass);
        DrawCenteredText(c, "▶", gateX - 6f, gateY, 2, ColBrass, shadow: true);
        DrawCenteredText(c, "FRONT", gateX - 54f, gateY + 12f, 2, ColBrass, shadow: true);
        DrawCenteredText(c, "GATE", gateX - 54f, gateY - 6f, 2, ColBrass, shadow: true);

        // 3. Caretaker's Bantay Shed (Rect(0.22, -1.75, 1.45, 1.10), north door at 0.95)
        Rect shed = new Rect(0.22f, -1.75f, 1.45f, 1.10f);
        FillWorldRect(c, shed, ColBuilding);
        DrawWorldWall(c, shed.xMin, shed.yMin, shed.xMax, shed.yMin, 3f);
        DrawWorldWall(c, shed.xMin, shed.yMin, shed.xMin, shed.yMax, 3f);
        DrawWorldWall(c, shed.xMax, shed.yMin, shed.xMax, shed.yMax, 3f);
        DrawWorldHorizontalWallWithDoors(c, shed.xMin, shed.xMax, shed.yMax, new[] { 0.95f }, 0.23f, 3f);

        // Gate Vendor Stall
        DrawWorldRectBox(c, new Rect(0.35f, 0.78f, 0.62f, 0.34f), ColBlock, ColBrassDim);

        // 4. Balete & Acacia Groves + Sample Grave Plot Markers
        DrawWorldRectBox(c, new Rect(2.68f, 2.18f, 0.36f, 0.36f), ColBlock, ColBrassDim); // North Balete
        DrawWorldRectBox(c, new Rect(2.68f, -2.32f, 0.34f, 0.34f), ColBlock, ColBrassDim); // South Acacia

        // 5. NE Multi-Tiered Nitso Maze Blocks
        Rect[] nitsoBlocks =
        {
            new Rect(4.75f, 0.84f, 1.35f, 0.28f),
            new Rect(6.68f, 0.84f, 1.37f, 0.28f),
            new Rect(4.75f, 1.66f, 0.90f, 0.28f),
            new Rect(6.20f, 1.66f, 0.85f, 0.28f),
            new Rect(7.58f, 1.66f, 0.52f, 0.28f),
            new Rect(4.75f, 2.48f, 1.35f, 0.28f),
            new Rect(6.68f, 2.48f, 1.37f, 0.28f)
        };
        for (int i = 0; i < nitsoBlocks.Length; i++)
        {
            DrawWorldRectBox(c, nitsoBlocks[i], ColBlock, ColWall);
        }

        // 6. SE Enclosed Family Mausoleums (x3)
        DrawEnclosedMausoleum(c, new Rect(4.55f, -1.48f, 1.65f, 1.20f), 5.38f, "M. DELOS\nSANTOS");
        DrawEnclosedMausoleum(c, new Rect(6.55f, -1.48f, 1.75f, 1.20f), 7.42f, "M.\nVILLANUEVA");
        DrawEnclosedMausoleum(c, new Rect(5.20f, -2.96f, 2.35f, 1.18f), 6.38f, "ABANDONED\nCRYPT");

        // 7. Sector & Landmark Labels
        DrawWorldLabel(c, "FLOWER\nSTALL", 0.68f, 1.32f, 2, ColTextMuted);
        DrawWorldLabel(c, "NORTH GRASS\nBURIALS", 1.25f, 2.25f, 2, ColText);
        DrawWorldLabel(c, "BALETE\nGROVE", 2.86f, 1.55f, 2, ColBrass);
        DrawWorldLabel(c, "NITSO MAZE", 6.40f, 3.04f, 2, ColText);
        DrawWorldLabel(c, "CROSS\nPLAZA", 4.08f, 0.42f, 2, ColBrass);
        DrawWorldLabel(c, "BANTAY\nSHED", 1.06f, -1.04f, 2, ColText);
        DrawFuseBoxMarker(c, 0.38f, -1.18f, 1.02f, -1.52f);
        DrawWorldLabel(c, "CARETAKER\nYARD", 1.05f, -2.42f, 2, ColTextMuted);
        DrawWorldLabel(c, "SW GRASS\nPLOTS", 2.85f, -1.35f, 2, ColText);
    }

    private static void DrawEnclosedMausoleum(BlueprintCanvas c, Rect r, float northDoorX, string label)
    {
        FillWorldRect(c, r, ColBuilding);
        DrawWorldWall(c, r.xMin, r.yMin, r.xMax, r.yMin, 3f);
        DrawWorldWall(c, r.xMin, r.yMin, r.xMin, r.yMax, 3f);
        DrawWorldWall(c, r.xMax, r.yMin, r.xMax, r.yMax, 3f);
        DrawWorldHorizontalWallWithDoors(c, r.xMin, r.xMax, r.yMax, new[] { northDoorX }, 0.23f, 3f);
        DrawWorldLabel(c, label, r.center.x, r.center.y - 0.04f, 2, ColText);
    }

    private static void DrawLegendFooter(BlueprintCanvas c)
    {
        c.FillRect(68, 128, CanvasWidth - 69, 129, ColBrassDim);
        DrawCenteredText(c, "▶ FRONT GATE  |  [F] FUSE BOX (BANTAY SHED)  |  4 SAFE BUILDINGS",
            CanvasWidth * 0.5f, 82, 2, ColTextMuted, shadow: false);
    }

    private static void DrawFuseBoxMarker(
        BlueprintCanvas c, float boxWx, float boxWy, float labelWx, float labelWy)
    {
        float bx = OriginX + boxWx * PxPerUnit;
        float by = OriginY + boxWy * PxPerUnit;
        float lx = OriginX + labelWx * PxPerUnit;
        float ly = OriginY + labelWy * PxPerUnit;

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

    private static void FillWorldRect(BlueprintCanvas c, Rect r, Color32 col)
    {
        int x0 = Mathf.RoundToInt(OriginX + r.xMin * PxPerUnit);
        int x1 = Mathf.RoundToInt(OriginX + r.xMax * PxPerUnit);
        int y0 = Mathf.RoundToInt(OriginY + r.yMin * PxPerUnit);
        int y1 = Mathf.RoundToInt(OriginY + r.yMax * PxPerUnit);
        c.FillRect(x0, y0, x1, y1, col);
    }

    private static void DrawWorldRectBox(BlueprintCanvas c, Rect r, Color32 fill, Color32 border)
    {
        int x0 = Mathf.RoundToInt(OriginX + r.xMin * PxPerUnit);
        int x1 = Mathf.RoundToInt(OriginX + r.xMax * PxPerUnit);
        int y0 = Mathf.RoundToInt(OriginY + r.yMin * PxPerUnit);
        int y1 = Mathf.RoundToInt(OriginY + r.yMax * PxPerUnit);
        c.FillRect(x0, y0, x1, y1, fill);
        c.DrawRectOutline(x0, y0, x1, y1, 2, border);
    }

    private static void DrawWorldWall(BlueprintCanvas c, float wx0, float wy0, float wx1, float wy1, float thickness)
    {
        c.DrawThickLine(
            OriginX + wx0 * PxPerUnit,
            OriginY + wy0 * PxPerUnit,
            OriginX + wx1 * PxPerUnit,
            OriginY + wy1 * PxPerUnit,
            thickness,
            ColWall);
    }

    private static void DrawWorldVerticalWallWithDoor(
        BlueprintCanvas c, float wx, float wyMin, float wyMax, float doorY, float halfGap, float thickness, bool highlightDoor = false)
    {
        float dBot = Mathf.Max(wyMin, doorY - halfGap);
        float dTop = Mathf.Min(wyMax, doorY + halfGap);
        if (dBot > wyMin + 0.01f) DrawWorldWall(c, wx, wyMin, wx, dBot, thickness);
        if (wyMax > dTop + 0.01f) DrawWorldWall(c, wx, dTop, wx, wyMax, thickness);

        if (highlightDoor)
        {
            c.DrawThickLine(
                OriginX + wx * PxPerUnit,
                OriginY + dBot * PxPerUnit,
                OriginX + wx * PxPerUnit,
                OriginY + dTop * PxPerUnit,
                2.5f,
                ColBrass);
        }
    }

    private static void DrawWorldHorizontalWallWithDoors(
        BlueprintCanvas c, float wxMin, float wxMax, float wy, float[] doorCenters, float halfGap, float thickness)
    {
        float cursor = wxMin;
        for (int i = 0; i < doorCenters.Length; i++)
        {
            float dLeft = Mathf.Max(cursor, doorCenters[i] - halfGap);
            float dRight = Mathf.Min(wxMax, doorCenters[i] + halfGap);
            if (dLeft > cursor + 0.01f) DrawWorldWall(c, cursor, wy, dLeft, wy, thickness);
            cursor = dRight;
        }
        if (wxMax > cursor + 0.01f) DrawWorldWall(c, cursor, wy, wxMax, wy, thickness);
    }

    private static void DrawWorldLabel(BlueprintCanvas c, string text, float wx, float wy, int scale, Color32 col)
    {
        DrawCenteredText(c, text, OriginX + wx * PxPerUnit, OriginY + wy * PxPerUnit, scale, col, shadow: true);
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
