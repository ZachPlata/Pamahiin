using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Post-processes active scene sprites to enforce 16-bit Point filtering,
/// upgrades flat Lobby geometry to 16-bit pixel art textures, and attaches
/// 16-bit sprite animations to Ghosts and Equipment.
/// </summary>
public static class PixelScenePostProcessor
{
    private static readonly Dictionary<string, Sprite> cachedSprites = new Dictionary<string, Sprite>();

    public static Sprite LoadPixelSprite(string resourceName, float pixelsPerUnit = 64f)
    {
        string key = $"{resourceName}_{pixelsPerUnit}";
        if (cachedSprites.TryGetValue(key, out var existing) && existing != null)
        {
            return existing;
        }

        Texture2D tex = Resources.Load<Texture2D>($"Sprites/{resourceName}");
        if (tex == null) return null;

        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;

        Sprite sp = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        sp.name = resourceName;
        cachedSprites[key] = sp;
        return sp;
    }

    public static void ApplyPixelArtToScene()
    {
        // 1. Force Point filter on every active SpriteRenderer texture and hide invisible trigger zone placeholders
        var allRenderers = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include);
        foreach (var sr in allRenderers)
        {
            if (sr == null) continue;

            if (sr.sprite != null && sr.sprite.texture != null)
            {
                sr.sprite.texture.filterMode = FilterMode.Point;
            }

            if (sr.GetComponent<SafeZone>() != null ||
                sr.GetComponent<HideZone>() != null ||
                sr.GetComponent<GhostRoomMarker>() != null ||
                sr.GetComponent<EquipmentSpawnPoint>() != null ||
                sr.GetComponent<PlayerSpawnPoint>() != null)
            {
                sr.enabled = false;
                continue;
            }

            // Hide oversized white "Square" trigger placeholders on light switches so they don't wash out under IR night vision
            if (sr.GetComponent<HouseLightSwitch>() != null && sr.sprite != null && sr.sprite.name == "Square")
            {
                if (sr.bounds.size.x > 0.25f || sr.bounds.size.y > 0.25f)
                {
                    sr.enabled = false;
                }
            }
        }

        // 2. Attach GhostPixelAnimator to any Ghost in the scene
        foreach (var eg in Object.FindObjectsByType<EventGhostBehavior>(FindObjectsInactive.Include))
        {
            if (eg != null && eg.GetComponent<GhostPixelAnimator>() == null)
            {
                eg.gameObject.AddComponent<GhostPixelAnimator>();
            }
        }

        foreach (var hg in Object.FindObjectsByType<HunterGhostBehavior>(FindObjectsInactive.Include))
        {
            if (hg != null && hg.GetComponent<GhostPixelAnimator>() == null)
            {
                hg.gameObject.AddComponent<GhostPixelAnimator>();
            }
        }

        // 3. Upgrade Lobby flat squares to 16-bit pixel art sprites while keeping exact world bounds
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == ProgressionAndLoadoutManager.LOBBY_SCENE_NAME)
        {
            UpgradeLobbySprites(allRenderers);
        }
    }

    private static void UpgradeLobbySprites(SpriteRenderer[] renderers)
    {
        foreach (var sr in renderers)
        {
            if (sr == null || sr.gameObject == null) continue;
            if (sr.drawMode != SpriteDrawMode.Simple) continue;
            string n = sr.gameObject.name;

            Sprite replacement = null;
            if (n == "SafeHouse_Floor")
                replacement = LoadPixelSprite("lobby_floor", 64f);
            else if (n == "SafeHouse_Rug")
                replacement = LoadPixelSprite("lobby_rug", 64f);
            else if (n.StartsWith("Wall_"))
                replacement = LoadPixelSprite("lobby_wall", 64f);
            else if (n == "BriefingTable")
                replacement = LoadPixelSprite("lobby_table", 48f);
            else if (n == "EquipmentRack_Prop")
                replacement = LoadPixelSprite("lobby_locker", 24f);

            if (replacement != null && sr.sprite != replacement)
            {
                Vector3 worldSize = sr.bounds.size;
                sr.sprite = replacement;
                sr.color = Color.white;
                Vector2 spriteUnits = replacement.bounds.size;
                if (spriteUnits.x > 0.001f && spriteUnits.y > 0.001f && worldSize.x > 0.01f && worldSize.y > 0.01f)
                {
                    sr.transform.localScale = new Vector3(
                        worldSize.x / spriteUnits.x,
                        worldSize.y / spriteUnits.y,
                        1f);
                }
            }
        }
    }
}

/// <summary>
/// 16-bit frame-by-frame animator for EventGhost and HunterGhost.
/// Handles Idle breathing, Walking stride + footsteps, and Hunting crimson flicker/jitter.
/// </summary>
public class GhostPixelAnimator : MonoBehaviour
{
    private SpriteRenderer sr;
    private HunterGhostBehavior hunter;
    private EventGhostBehavior eventGhost;

    private Sprite[] idleFrames;
    private Sprite[] walkFrames;
    private Sprite[] huntFrames;

    private Vector3 lastPos;
    private float frameTimer;
    private int currentFrame;
    private float footstepTimer;
    private bool isDyingOverride = false;

    public void SetDyingOverride(bool active)
    {
        isDyingOverride = active;
    }

    private void Awake()
    {
        sr = GetComponentInChildren<SpriteRenderer>();
        hunter = GetComponent<HunterGhostBehavior>();
        eventGhost = GetComponent<EventGhostBehavior>();
        lastPos = transform.position;

        float ppu = (sr != null && sr.sprite != null) ? sr.sprite.pixelsPerUnit : 100f;

        idleFrames = new Sprite[]
        {
            PixelScenePostProcessor.LoadPixelSprite("ghost_idle_0", ppu),
            PixelScenePostProcessor.LoadPixelSprite("ghost_idle_1", ppu)
        };
        walkFrames = new Sprite[]
        {
            PixelScenePostProcessor.LoadPixelSprite("ghost_walk_0", ppu),
            PixelScenePostProcessor.LoadPixelSprite("ghost_idle_0", ppu),
            PixelScenePostProcessor.LoadPixelSprite("ghost_walk_1", ppu),
            PixelScenePostProcessor.LoadPixelSprite("ghost_idle_1", ppu)
        };
        huntFrames = new Sprite[]
        {
            PixelScenePostProcessor.LoadPixelSprite("ghost_hunt_0", ppu),
            PixelScenePostProcessor.LoadPixelSprite("ghost_walk_0", ppu),
            PixelScenePostProcessor.LoadPixelSprite("ghost_hunt_1", ppu),
            PixelScenePostProcessor.LoadPixelSprite("ghost_walk_1", ppu)
        };
    }

    private void Update()
    {
        if (isDyingOverride) return;

        if (sr == null || !sr.enabled)
        {
            lastPos = transform.position;
            return;
        }

        float speed = (transform.position - lastPos).magnitude / Mathf.Max(0.0001f, Time.deltaTime);
        lastPos = transform.position;

        bool isHunting = hunter != null && hunter.ghostHandler != null &&
                         hunter.ghostHandler.CurrentState != GhostHandlerState.Dormant;
        bool isMoving = speed > 0.05f;

        float fps = isHunting ? 9f : (isMoving ? 6f : 2.5f);
        frameTimer += Time.deltaTime * fps;
        if (frameTimer >= 1f)
        {
            frameTimer -= 1f;
            currentFrame++;
        }

        Sprite[] activeSet = isHunting ? huntFrames : (isMoving ? walkFrames : idleFrames);
        if (activeSet != null && activeSet.Length > 0)
        {
            Sprite next = activeSet[currentFrame % activeSet.Length];
            if (next != null) sr.sprite = next;
        }

        // Play eerie 16-bit ghost footsteps while moving and visible/hunting (living observers only)
        bool localAlive = PlayerController.LocalInstance == null || PlayerController.LocalInstance.IsAlive;
        if (localAlive && (isMoving || isHunting) && isHunting)
        {
            footstepTimer -= Time.deltaTime;
            float interval = hunter != null && hunter.IsAngeredState ? 0.34f : 0.48f;
            if (footstepTimer <= 0f)
            {
                footstepTimer = interval;
                GameAudioManager.PlayGhostFootstep(transform.position);
            }
        }
    }
}

