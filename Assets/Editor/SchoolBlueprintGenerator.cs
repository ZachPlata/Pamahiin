using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates matching dark mahogany & brass Journal architectural schematics for all 3 floors
/// of San Nicolas High School directly from SchoolMapLayout coordinates.
/// </summary>
public static class SchoolBlueprintGenerator
{
    public const string BlueprintF1Path = "Assets/UI/Journal/blueprint_school_f1.png";
    public const string BlueprintF2Path = "Assets/UI/Journal/blueprint_school_f2.png";
    public const string BlueprintF3Path = "Assets/UI/Journal/blueprint_school_f3.png";

    private const int CanvasWidth = 880;
    private const int CanvasHeight = 760;
    private const float PxPerUnit = 58f;
    private const float OriginX = 440f;
    private const float OriginY = 365f;

    private static readonly Color32 ColBg = new Color32(28, 22, 18, 255);
    private static readonly Color32 ColGridMinor = new Color32(41, 32, 26, 255);
    private static readonly Color32 ColGridMajor = new Color32(54, 42, 33, 255);
    private static readonly Color32 ColFloor = new Color32(42, 33, 27, 255);
    private static readonly Color32 ColCorridor = new Color32(34, 27, 22, 255);
    private static readonly Color32 ColCourtyard = new Color32(24, 30, 26, 255);
    private static readonly Color32 ColWall = new Color32(238, 224, 194, 255);
    private static readonly Color32 ColBrass = new Color32(215, 175, 92, 255);
    private static readonly Color32 ColBrassDim = new Color32(142, 110, 58, 255);
    private static readonly Color32 ColEntryDoor = new Color32(235, 168, 78, 255);
    private static readonly Color32 ColText = new Color32(244, 232, 206, 255);
    private static readonly Color32 ColTextMuted = new Color32(186, 162, 126, 255);
    private static readonly Color32 ColTextShadow = new Color32(16, 12, 10, 225);

    private sealed class Canvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        public Canvas(int w, int h, Color32 bg)
        {
            Width = w;
            Height = h;
            Pixels = new Color32[w * h];
            for (int i = 0; i < Pixels.Length; i++) Pixels[i] = bg;
        }

        public void Blend(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || c.a == 0) return;
            int idx = y * Width + x;
            if (c.a == 255) { Pixels[idx] = c; return; }
            Color32 d = Pixels[idx];
            float a = c.a / 255f, inv = 1f - a;
            Pixels[idx] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * a + d.r * inv), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * a + d.g * inv), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * a + d.b * inv), 0, 255),
                255);
        }

        public void FillRect(int x0, int y0, int x1, int y1, Color32 c)
        {
            int minX = Mathf.Min(x0, x1), maxX = Mathf.Max(x0, x1);
            int minY = Mathf.Min(y0, y1), maxY = Mathf.Max(y0, y1);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    Blend(x, y, c);
        }

        public void DrawOutline(int x0, int y0, int x1, int y1, int t, Color32 c)
        {
            FillRect(x0, y0, x1, y0 + t - 1, c);
            FillRect(x0, y1 - t + 1, x1, y1, c);
            FillRect(x0, y0, x0 + t - 1, y1, c);
            FillRect(x1 - t + 1, y0, x1, y1, c);
        }
    }

    private static readonly Dictionary<char, string[]> Font5x7 = new Dictionary<char, string[]>
    {
        ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
        ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
        ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
        ['G'] = new[] { ".###.", "#...#", "#....", "#..##", "#...#", "#...#", ".###." },
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
        ['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
        ['/'] = new[] { "....#", "...#.", "...#.", "..#..", ".#...", ".#...", "#...." },
        ['&'] = new[] { ".##..", "#..#.", "#.#..", ".##..", "#.#.#", "#..#.", ".##.#" },
        ['['] = new[] { ".###.", ".#...", ".#...", ".#...", ".#...", ".#...", ".###." },
        [']'] = new[] { ".###.", "...#.", "...#.", "...#.", "...#.", "...#.", ".###." },
        [':'] = new[] { ".....", ".##..", ".##..", ".....", ".##..", ".##..", "....." },
        ['|'] = new[] { "..#..", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['='] = new[] { ".....", ".....", "#####", ".....", "#####", ".....", "....." },
        ['('] = new[] { "...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#." },
        [')'] = new[] { ".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#..." },
        [','] = new[] { ".....", ".....", ".....", ".....", ".....", "..#..", ".#..." }
    };

    [MenuItem("Pamahiin/Generate SchoolMap Blueprint PNGs")]
    public static void GenerateAllBlueprints()
    {
        GenerateFloorBlueprint(1, BlueprintF1Path, "SAN NICOLAS HIGH SCHOOL - 1ST FLOOR");
        GenerateFloorBlueprint(2, BlueprintF2Path, "SAN NICOLAS HIGH SCHOOL - 2ND FLOOR");
        GenerateFloorBlueprint(3, BlueprintF3Path, "SAN NICOLAS HIGH SCHOOL - 3RD FLOOR");

        if (File.Exists("Assets/UI/Journal/JournalUI.uss"))
        {
            AssetDatabase.ImportAsset("Assets/UI/Journal/JournalUI.uss", ImportAssetOptions.ForceUpdate);
        }
    }

    private static void GenerateFloorBlueprint(int floorIndex, string outputPath, string title)
    {
        var c = new Canvas(CanvasWidth, CanvasHeight, ColBg);
        DrawGridAndBorder(c, title);

        var allRooms = SchoolMapLayout.BuildAllRooms();
        var allDoors = SchoolMapLayout.BuildAllDoors();

        // Exterior courtyards hint on Floor 1
        if (floorIndex == 1)
        {
            FillLocalRect(c, new Rect(-3.50f, 2.25f, 7.00f, 2.00f), ColCourtyard);
            DrawCenteredText(c, "NORTH COURTYARD", ToPxX(0f), ToPxY(3.45f), 2, ColTextMuted, shadow: false);
            DrawCenteredText(c, "[JEEPNEY]", ToPxX(0f), ToPxY(5.00f), 2, ColBrass, shadow: true);

            FillLocalRect(c, new Rect(-3.50f, -4.25f, 7.00f, 2.00f), ColCourtyard);
            DrawCenteredText(c, "SOUTH LAWN", ToPxX(0f), ToPxY(-3.30f), 2, ColTextMuted, shadow: false);
        }

        // Fill rooms (corridors darker than interior rooms)
        foreach (var room in allRooms)
        {
            if (room.Floor != floorIndex) continue;
            Color32 fill = room.Role == SchoolMapLayout.RoomRole.Corridor || room.Role == SchoolMapLayout.RoomRole.Stairwell
                ? ColCorridor
                : ColFloor;
            FillLocalRect(c, room.LocalRect, fill);

            // Room partition frame
            Rect r = room.LocalRect;
            int x0 = ToPxX(r.xMin);
            int y0 = ToPxY(r.yMin);
            int x1 = ToPxX(r.xMax);
            int y1 = ToPxY(r.yMax);
            c.DrawOutline(x0, y0, x1, y1, 2, room.Role == SchoolMapLayout.RoomRole.Corridor ? ColBrassDim : ColWall);
        }

        // Exterior bold outline around H-building wings
        DrawWingOuterOutline(c);

        // Draw Stair Treads inside 4 corners
        foreach (var sc in SchoolMapLayout.StairCorners)
        {
            float sx0 = ToPxX(sc.IsWestWing ? -6.35f : 5.65f);
            float sx1 = ToPxX(sc.IsWestWing ? -5.65f : 6.35f);
            if (floorIndex == 2)
            {
                DrawStairTreads(c, (int)sx0, ToPxY(sc.LocalCenterY + 0.14f), (int)sx1, ToPxY(sc.LocalCenterY + 0.50f));
                DrawStairTreads(c, (int)sx0, ToPxY(sc.LocalCenterY - 0.50f), (int)sx1, ToPxY(sc.LocalCenterY - 0.14f));
                c.FillRect((int)sx0, ToPxY(sc.LocalCenterY) - 1, (int)sx1, ToPxY(sc.LocalCenterY) + 1, ColBrass);
                DrawCenteredText(c, "DN/UP", ToPxX(sc.IsWestWing ? -5.15f : 5.15f), ToPxY(sc.LocalCenterY), 1, ColBrass, shadow: true);
            }
            else
            {
                DrawStairTreads(c, (int)sx0, ToPxY(sc.LocalCenterY - 0.22f), (int)sx1, ToPxY(sc.LocalCenterY + 0.22f));
                DrawCenteredText(c, floorIndex == 1 ? "UP F2" : "DN F2",
                    ToPxX(sc.IsWestWing ? -5.15f : 5.15f), ToPxY(sc.LocalCenterY), 1, ColBrass, shadow: true);
            }
        }

        // Draw doors cleanly over walls
        foreach (var d in allDoors)
        {
            if (d.Floor != floorIndex || d.IsHidingSpot) continue;
            int cx = ToPxX(d.LocalPos.x);
            int cy = ToPxY(d.LocalPos.y);
            Color32 doorCol = d.IsFrontDoor ? ColEntryDoor : ColBrass;
            if (d.VerticalWall)
                c.FillRect(cx - 2, cy - 6, cx + 2, cy + 6, doorCol);
            else
                c.FillRect(cx - 6, cy - 2, cx + 6, cy + 2, doorCol);
        }

        // Draw concise uppercase room labels
        foreach (var room in allRooms)
        {
            if (room.Floor != floorIndex) continue;
            if (room.Role == SchoolMapLayout.RoomRole.Corridor || room.Role == SchoolMapLayout.RoomRole.Stairwell) continue;
            string shortLabel = GetShortBlueprintLabel(room);
            if (string.IsNullOrEmpty(shortLabel)) continue;

            int rx = ToPxX(room.LocalRect.center.x);
            int ry = ToPxY(room.LocalRect.center.y);
            DrawCenteredText(c, shortLabel, rx, ry, 1, ColText, shadow: true);
        }

        if (floorIndex == 1)
        {
            DrawCenteredText(c, "[FB]", ToPxX(-3.20f), ToPxY(-1.80f), 1, ColEntryDoor, shadow: true);
            DrawCenteredText(c, "ENTRY", ToPxX(-4.00f), ToPxY(4.45f), 1, ColEntryDoor, shadow: true);
            DrawCenteredText(c, "ENTRY", ToPxX( 4.00f), ToPxY(4.45f), 1, ColEntryDoor, shadow: true);
        }

        DrawLegendFooter(c, floorIndex);

        string dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        Texture2D tex = new Texture2D(CanvasWidth, CanvasHeight, TextureFormat.RGBA32, false);
        tex.SetPixels32(c.Pixels);
        tex.Apply();
        File.WriteAllBytes(outputPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(outputPath) as TextureImporter;
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
    }

    private static string GetShortBlueprintLabel(SchoolMapLayout.RoomDef room)
    {
        return room.Id switch
        {
            var id when id.Contains("Restroom") => "WC",
            "F1_LockerAlcove" => "LOCKER",
            "F1_Workshop" => "WORKSHOP",
            "F1_DeansOffice_Inner" => "OFFICE",
            "F1_DeansOffice_Reception" => "DEAN",
            "F1_DeansOffice_Annex" => "RECORDS",
            "F1_Storage_NE" => "STOR",
            "F2_Theater_Backstage" => "STAGE",
            "F2_MultipurposeHall" => "THEATER HALL",
            "F2_LibraryDiscussion" => "STUDY",
            "F2_Library" => "LIBRARY",
            "F3_JanitorCloset_NW" => "CLOSET",
            "F3_ComLab_1" => "COMLAB 1",
            "F3_ComLab_2" => "COMLAB 2",
            "F3_ComLab_3" => "COMLAB 3",
            "F3_Utility_SW_Upper" => "UTIL",
            "F3_Utility_SW_Lower" => "ARCH",
            "F3_ControlSuite_SE" => "CONTROL",
            _ => room.Id.Contains("Classroom") ? "CLASS" : ""
        };
    }

    private static void DrawGridAndBorder(Canvas c, string header)
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
        c.DrawOutline(10, 10, CanvasWidth - 11, CanvasHeight - 11, 2, ColBrassDim);
        c.DrawOutline(16, 16, CanvasWidth - 17, CanvasHeight - 17, 1, ColBrass);

        DrawCenteredText(c, header, CanvasWidth * 0.5f, CanvasHeight - 44, 2, ColBrass, shadow: true);
        c.FillRect(60, CanvasHeight - 64, CanvasWidth - 61, CanvasHeight - 63, ColBrassDim);
    }

    private static void DrawWingOuterOutline(Canvas c)
    {
        foreach (var r in new[]
        {
            new Rect(-6.50f, -4.25f, 3.00f, 8.50f),
            new Rect(-3.50f, -2.25f, 7.00f, 4.50f),
            new Rect( 3.50f, -4.25f, 3.00f, 8.50f)
        })
        {
            c.DrawOutline(ToPxX(r.xMin), ToPxY(r.yMin), ToPxX(r.xMax), ToPxY(r.yMax), 3, ColWall);
        }
    }

    private static void DrawStairTreads(Canvas c, int x0, int y0, int x1, int y1)
    {
        c.DrawOutline(x0, y0, x1, y1, 1, ColBrass);
        for (int x = x0 + 5; x < x1 - 3; x += 6)
        {
            c.FillRect(x, y0 + 2, x, y1 - 2, ColBrassDim);
        }
    }

    private static void DrawLegendFooter(Canvas c, int floorIndex)
    {
        c.FillRect(60, 56, CanvasWidth - 61, 57, ColBrassDim);
        string legend = floorIndex == 1
            ? "KEY:  [FB] FUSE BOX (WORKSHOP)   |   GOLD = ENTRY/DOORS   |   4 CORNER STAIRWELLS TO F2"
            : floorIndex == 2
                ? "KEY:  NW/SW/NE/SE SPLIT LANDINGS (DN TO F1 / UP TO F3)   |   DARK STACKS = LIBRARY"
                : "KEY:  COMLABS 1-3 (NORTH)   |   CONTROL SUITE (SE)   |   4 CORNER STAIRS TO F2";
        DrawCenteredText(c, legend, CanvasWidth * 0.5f, 36, 1, ColTextMuted, shadow: true);
    }

    private static int ToPxX(float x) => Mathf.RoundToInt(OriginX + x * PxPerUnit);
    private static int ToPxY(float y) => Mathf.RoundToInt(OriginY + y * PxPerUnit);

    private static void FillLocalRect(Canvas c, Rect r, Color32 col)
    {
        c.FillRect(ToPxX(r.xMin), ToPxY(r.yMin), ToPxX(r.xMax), ToPxY(r.yMax), col);
    }

    private static void DrawCenteredText(Canvas c, string text, float cx, float cy, int scale, Color32 col, bool shadow)
    {
        if (string.IsNullOrEmpty(text)) return;
        text = text.ToUpperInvariant();
        int totalW = text.Length * 6 * scale - scale;
        int startX = Mathf.RoundToInt(cx - totalW * 0.5f);
        int startY = Mathf.RoundToInt(cy - 3.5f * scale);

        for (int i = 0; i < text.Length; i++)
        {
            if (!Font5x7.TryGetValue(text[i], out string[] glyph)) glyph = Font5x7[' '];
            int gx = startX + i * 6 * scale;
            for (int r = 0; r < 7; r++)
            {
                string rowStr = glyph[6 - r];
                for (int colIdx = 0; colIdx < 5; colIdx++)
                {
                    if (rowStr[colIdx] != '#') continue;
                    int px = gx + colIdx * scale;
                    int py = startY + r * scale;
                    if (shadow) c.FillRect(px + scale, py - scale, px + scale * 2 - 1, py - 1, ColTextShadow);
                    c.FillRect(px, py, px + scale - 1, py + scale - 1, col);
                }
            }
        }
    }
}
