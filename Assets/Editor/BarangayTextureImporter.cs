using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Enforces crisp 16-bit Point-filtered Sprite import settings for all generated
/// BarangayMap sprites, prop sheets, weather overlays, and sliced horror sheets.
/// </summary>
public class BarangayTextureImporter : AssetPostprocessor
{
    private const string TARGET_ROOT = "Assets/Resources/Sprites/BarangayMap/";
    private const string CEMETERY_ROOT = "Assets/Resources/Sprites/CemeteryMap/";
    private const string RITUAL_ROOT = "Assets/Resources/Sprites/Ritual/";
    private const string GHOST_DEATH_PATH = "Assets/Resources/Sprites/ghost_death_sheet.png";
    public const string PLAYER_DEATH_PATH = "Assets/Resources/Sprites/CHARACTERS/player_death_choke_sheet.png";
    public const string PLAYER_RAGDOLL_PATH = "Assets/Resources/Sprites/CHARACTERS/player_corpse_ragdoll_sheet.png";
    public const string DEATH_HAND_PATH = "Assets/Resources/Sprites/UI/death_ghost_hand_sheet.png";
    public const string BURIAL_JUMPSCARE_PATH = "Assets/Resources/Sprites/UI/burial_jumpscare_sheet.png";
    public const string DOTS_SILHOUETTE_WALK_PATH = "Assets/Resources/Sprites/CHARACTERS/ghost_dots_walk_sheet.png";

    private struct SheetSliceSpec
    {
        public int Cols;
        public int Rows;
        public int PixelsPerUnit;
        public string NamePrefix;
        public string[] CustomNames;
        public Vector2[] CustomPivots;
    }

    private static readonly string[] RagdollPartNames =
    {
        "Corpse_Torso",
        "Corpse_Head",
        "Corpse_LeftArm",
        "Corpse_RightArm",
        "Corpse_LeftLeg",
        "Corpse_RightLeg"
    };

    private static readonly Vector2[] RagdollPartPivots =
    {
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 20f / 64f),
        new Vector2(40f / 64f, 44f / 64f),
        new Vector2(24f / 64f, 44f / 64f),
        new Vector2(0.5f, 46f / 64f),
        new Vector2(0.5f, 46f / 64f)
    };

    private static readonly string[] DotsWalkFrameNames =
    {
        "ghost_dots_walk_sheet_0",
        "ghost_dots_walk_sheet_1",
        "ghost_dots_walk_sheet_2",
        "ghost_dots_walk_sheet_3",
        "ghost_dots_walk_sheet_4",
        "ghost_dots_walk_sheet_5",
        "ghost_dots_walk_sheet_6",
        "ghost_dots_walk_sheet_7"
    };

    private static bool TryGetSlicedSheetSpec(string normalizedPath, out SheetSliceSpec spec)
    {
        switch (normalizedPath)
        {
            case DEATH_HAND_PATH:
                spec = new SheetSliceSpec
                {
                    Cols = 5,
                    Rows = 4,
                    PixelsPerUnit = 100,
                    NamePrefix = "DeathGhostHand"
                };
                return true;
            case BURIAL_JUMPSCARE_PATH:
                spec = new SheetSliceSpec
                {
                    Cols = 4,
                    Rows = 3,
                    PixelsPerUnit = 100,
                    NamePrefix = "BurialJumpscare"
                };
                return true;
            case PLAYER_DEATH_PATH:
                spec = new SheetSliceSpec
                {
                    Cols = 4,
                    Rows = 3,
                    PixelsPerUnit = 100,
                    NamePrefix = "PlayerDeathChoke"
                };
                return true;
            case PLAYER_RAGDOLL_PATH:
                spec = new SheetSliceSpec
                {
                    Cols = 3,
                    Rows = 2,
                    PixelsPerUnit = 100,
                    NamePrefix = "Corpse",
                    CustomNames = RagdollPartNames,
                    CustomPivots = RagdollPartPivots
                };
                return true;
            case DOTS_SILHOUETTE_WALK_PATH:
                spec = new SheetSliceSpec
                {
                    Cols = 8,
                    Rows = 1,
                    PixelsPerUnit = 100,
                    NamePrefix = "ghost_dots_walk_sheet",
                    CustomNames = DotsWalkFrameNames
                };
                return true;
            default:
                spec = default;
                return false;
        }
    }

    private void OnPreprocessTexture()
    {
        string normalizedPath = assetPath.Replace('\\', '/');
        bool isBarangay = normalizedPath.StartsWith(TARGET_ROOT);
        bool isCemetery = normalizedPath.StartsWith(CEMETERY_ROOT);
        bool isSlicedHorrorSheet = TryGetSlicedSheetSpec(normalizedPath, out _);
        bool isRitualOrDeath = normalizedPath.StartsWith(RITUAL_ROOT)
            || normalizedPath == GHOST_DEATH_PATH
            || isSlicedHorrorSheet;
        if (!isBarangay && !isCemetery && !isRitualOrDeath) return;

        TextureImporter ti = assetImporter as TextureImporter;
        if (ti == null) return;

        bool isTileable = (isBarangay && (normalizedPath.Contains("/Exterior/asphalt_road")
            || normalizedPath.Contains("/House/floor_")
            || normalizedPath.Contains("/House/wall_")
            || normalizedPath.Contains("/House/roof_")
            || normalizedPath.Contains("/Burial/burial_dirt_floor")
            || normalizedPath.Contains("/Burial/burial_dirt_wall")))
            || (isCemetery && (normalizedPath.EndsWith("/cemetery_grass.png")
            || normalizedPath.EndsWith("/cemetery_dirt_alley.png")
            || normalizedPath.EndsWith("/cemetery_path_stone.png")
            || normalizedPath.EndsWith("/cemetery_mausoleum_tile.png")
            || normalizedPath.EndsWith("/cemetery_roof_concrete.png")));

        float ppu = isRitualOrDeath ? 100f : 64f;
        if (isBarangay)
        {
            if (normalizedPath.Contains("/House/wall_concrete_top"))
                ppu = 256f;
            else if (normalizedPath.Contains("/Weather/rain_streaks_sheet") || normalizedPath.Contains("/Weather/fireflies_sheet"))
                ppu = 128f;
        }
        else if (isCemetery)
        {
            if (normalizedPath.EndsWith("/gate_iron.png"))
                ppu = 128f;
            else
                ppu = 64f;
        }

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = isSlicedHorrorSheet ? SpriteImportMode.Multiple : SpriteImportMode.Single;
        ti.filterMode = FilterMode.Point;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        if (isRitualOrDeath)
        {
            ti.maxTextureSize = 4096;
        }
        ti.spritePixelsPerUnit = ppu;
        ti.wrapMode = isTileable ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;

        if (isSlicedHorrorSheet)
        {
            TextureImporterPlatformSettings defaultPlatform = ti.GetDefaultPlatformTextureSettings();
            defaultPlatform.textureCompression = TextureImporterCompression.Uncompressed;
            defaultPlatform.format = TextureImporterFormat.RGBA32;
            ti.SetPlatformTextureSettings(defaultPlatform);
        }

        if (isRitualOrDeath
            || normalizedPath.Contains("/Props/")
            || normalizedPath.Contains("/Weather/")
            || (isCemetery && normalizedPath.EndsWith("/candle_sheet.png")))
        {
            ti.isReadable = true;
        }

        TextureImporterSettings settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spritePixelsPerUnit = ppu;
        ti.SetTextureSettings(settings);
    }

    public static void SliceAllHorrorSpriteSheets()
    {
        string[] paths =
        {
            DEATH_HAND_PATH,
            BURIAL_JUMPSCARE_PATH,
            PLAYER_DEATH_PATH,
            PLAYER_RAGDOLL_PATH,
            DOTS_SILHOUETTE_WALK_PATH
        };

        foreach (string path in paths)
        {
            SliceHorrorSpriteSheet(path);
        }
    }

    public static void SliceHorrorSpriteSheet(string assetPath)
    {
        string normalizedPath = assetPath.Replace('\\', '/');
        if (!TryGetSlicedSheetSpec(normalizedPath, out SheetSliceSpec spec))
        {
            return;
        }

        TextureImporter ti = AssetImporter.GetAtPath(normalizedPath) as TextureImporter;
        if (ti == null)
        {
            Debug.LogWarning($"[BarangayTextureImporter] TextureImporter not found at {normalizedPath}");
            return;
        }

        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalizedPath);
        if (tex == null)
        {
            AssetDatabase.ImportAsset(normalizedPath, ImportAssetOptions.ForceUpdate);
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalizedPath);
        }
        if (tex == null)
        {
            Debug.LogWarning($"[BarangayTextureImporter] Could not load Texture2D at {normalizedPath}");
            return;
        }

        int texWidth = tex.width;
        int texHeight = tex.height;
        ti.GetSourceTextureWidthAndHeight(out int srcW, out int srcH);
        if (srcW > 0 && srcH > 0)
        {
            texWidth = srcW;
            texHeight = srcH;
        }

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Multiple;
        ti.filterMode = FilterMode.Point;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 4096;
        ti.spritePixelsPerUnit = spec.PixelsPerUnit;
        ti.isReadable = true;

        TextureImporterPlatformSettings defaultPlatform = ti.GetDefaultPlatformTextureSettings();
        defaultPlatform.textureCompression = TextureImporterCompression.Uncompressed;
        defaultPlatform.format = TextureImporterFormat.RGBA32;
        ti.SetPlatformTextureSettings(defaultPlatform);

        TextureImporterSettings importerSettings = new TextureImporterSettings();
        ti.ReadTextureSettings(importerSettings);
        importerSettings.spriteMeshType = SpriteMeshType.FullRect;
        importerSettings.spritePixelsPerUnit = spec.PixelsPerUnit;
        ti.SetTextureSettings(importerSettings);

        SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
        factories.Init();
        ISpriteEditorDataProvider dataProvider = factories.GetSpriteEditorDataProviderFromObject(ti);
        if (dataProvider == null)
        {
            Debug.LogError($"[BarangayTextureImporter] ISpriteEditorDataProvider null for {normalizedPath}");
            return;
        }

        dataProvider.InitSpriteEditorDataProvider();

        SpriteRect[] existing = dataProvider.GetSpriteRects();
        Dictionary<string, GUID> existingIdsByName = new Dictionary<string, GUID>();
        if (existing != null)
        {
            foreach (SpriteRect rect in existing)
            {
                if (!string.IsNullOrEmpty(rect.name) && rect.spriteID != default)
                {
                    existingIdsByName[rect.name] = rect.spriteID;
                }
            }
        }

        int cellW = texWidth / spec.Cols;
        int cellH = texHeight / spec.Rows;
        int total = spec.Cols * spec.Rows;
        SpriteRect[] rects = new SpriteRect[total];

        int idx = 0;
        for (int rowFromTop = 0; rowFromTop < spec.Rows; rowFromTop++)
        {
            int unityY = texHeight - ((rowFromTop + 1) * cellH);
            for (int col = 0; col < spec.Cols; col++)
            {
                string spriteName = spec.CustomNames != null && idx < spec.CustomNames.Length
                    ? spec.CustomNames[idx]
                    : $"{spec.NamePrefix}_{idx:D2}";
                Vector2 pivot = spec.CustomPivots != null && idx < spec.CustomPivots.Length
                    ? spec.CustomPivots[idx]
                    : new Vector2(0.5f, 0.5f);

                GUID spriteId = existingIdsByName.TryGetValue(spriteName, out GUID preserved)
                    ? preserved
                    : GUID.Generate();

                rects[idx] = new SpriteRect
                {
                    name = spriteName,
                    spriteID = spriteId,
                    rect = new Rect(col * cellW, unityY, cellW, cellH),
                    alignment = SpriteAlignment.Custom,
                    pivot = pivot
                };
                idx++;
            }
        }

        dataProvider.SetSpriteRects(rects);
        dataProvider.Apply();

        EditorUtility.SetDirty(ti);
        ti.SaveAndReimport();
    }
}
