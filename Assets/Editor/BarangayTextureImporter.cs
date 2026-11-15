using UnityEditor;
using UnityEngine;

/// <summary>
/// Enforces crisp 16-bit Point-filtered Sprite import settings for all generated
/// BarangayMap sprites, prop sheets, and weather overlays.
/// </summary>
public class BarangayTextureImporter : AssetPostprocessor
{
    private const string TARGET_ROOT = "Assets/Resources/Sprites/BarangayMap/";
    private const string RITUAL_ROOT = "Assets/Resources/Sprites/Ritual/";
    private const string GHOST_DEATH_PATH = "Assets/Resources/Sprites/ghost_death_sheet.png";

    private void OnPreprocessTexture()
    {
        string normalizedPath = assetPath.Replace('\\', '/');
        bool isBarangay = normalizedPath.StartsWith(TARGET_ROOT);
        bool isRitualOrDeath = normalizedPath.StartsWith(RITUAL_ROOT) || normalizedPath == GHOST_DEATH_PATH;
        if (!isBarangay && !isRitualOrDeath) return;

        TextureImporter ti = assetImporter as TextureImporter;
        if (ti == null) return;

        bool isTileable = isBarangay && (normalizedPath.Contains("/Exterior/asphalt_road")
            || normalizedPath.Contains("/House/floor_")
            || normalizedPath.Contains("/House/wall_")
            || normalizedPath.Contains("/House/roof_"));

        float ppu = isRitualOrDeath ? 100f : 64f;
        if (isBarangay)
        {
            if (normalizedPath.Contains("/House/wall_concrete_top"))
                ppu = 256f;
            else if (normalizedPath.Contains("/Weather/rain_streaks_sheet") || normalizedPath.Contains("/Weather/fireflies_sheet"))
                ppu = 128f;
        }

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
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

        if (isRitualOrDeath || normalizedPath.Contains("/Props/") || normalizedPath.Contains("/Weather/"))
        {
            ti.isReadable = true;
        }

        TextureImporterSettings settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spritePixelsPerUnit = ppu;
        ti.SetTextureSettings(settings);
    }
}
