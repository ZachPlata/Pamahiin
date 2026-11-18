using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight 16-bit frame animator for static school horror props such as swaying
/// courtyard tree canopies and power-reactive ComLab CRT monitors.
/// </summary>
public class SchoolPropAnimator : MonoBehaviour
{
    [SerializeField] private string sheetResource = "";
    [SerializeField] private string offResource = "";
    [SerializeField] private int frameCount = 2;
    [SerializeField] private float fps = 3f;
    [SerializeField] private bool requiresFuseBoxPower = false;
    [SerializeField] private bool randomizePhase = true;
    [SerializeField] private float pixelsPerUnit = 64f;

    private static readonly Dictionary<string, Sprite[]> sheetCache = new Dictionary<string, Sprite[]>();
    private static readonly Dictionary<string, Sprite> singleCache = new Dictionary<string, Sprite>();

    private SpriteRenderer spriteRenderer;
    private Sprite[] animFrames;
    private Sprite offSprite;
    private float timer;
    private int currentFrame;
    private bool subscribedFuseBox;

    /// <summary>Configures animator settings from SchoolMapSceneBuilder or tests.</summary>
    public void Configure(
        string sheetRes,
        int frames,
        float frameRate,
        bool powerRequired = false,
        string offRes = "",
        float ppu = 64f)
    {
        sheetResource = sheetRes;
        frameCount = Mathf.Max(1, frames);
        fps = Mathf.Max(0.25f, frameRate);
        requiresFuseBoxPower = powerRequired;
        offResource = offRes;
        pixelsPerUnit = ppu;
        animFrames = null;
        offSprite = null;
        EnsureSpritesLoaded();
        ApplyVisualState();
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (randomizePhase)
        {
            Random.State prev = Random.state;
            int seed = Mathf.RoundToInt(transform.position.x * 131f) ^ Mathf.RoundToInt(transform.position.y * 977f);
            Random.InitState(seed);
            timer = Random.Range(0f, 1f / Mathf.Max(0.25f, fps));
            currentFrame = Random.Range(0, Mathf.Max(1, frameCount));
            Random.state = prev;
        }
        EnsureSpritesLoaded();
    }

    private void OnEnable()
    {
        if (requiresFuseBoxPower && !subscribedFuseBox)
        {
            FuseBox.OnPowerStateChanged += HandlePowerChanged;
            subscribedFuseBox = true;
        }
        ApplyVisualState();
    }

    private void OnDisable()
    {
        if (subscribedFuseBox)
        {
            FuseBox.OnPowerStateChanged -= HandlePowerChanged;
            subscribedFuseBox = false;
        }
    }

    private void HandlePowerChanged(bool _)
    {
        ApplyVisualState();
    }

    private void Update()
    {
        if (spriteRenderer == null) return;
        EnsureSpritesLoaded();

        if (requiresFuseBoxPower && !IsPowerAvailable())
        {
            if (offSprite != null && spriteRenderer.sprite != offSprite)
            {
                spriteRenderer.sprite = offSprite;
            }
            return;
        }

        if (animFrames == null || animFrames.Length == 0) return;

        timer += Time.deltaTime;
        float step = 1f / Mathf.Max(0.25f, fps);
        if (timer >= step)
        {
            timer -= step;
            currentFrame = (currentFrame + 1) % animFrames.Length;
            if (animFrames[currentFrame] != null)
            {
                spriteRenderer.sprite = animFrames[currentFrame];
            }
        }
    }

    private bool IsPowerAvailable()
    {
        return FuseBox.Instance != null && FuseBox.Instance.IsEnabled;
    }

    private void ApplyVisualState()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) return;

        if (requiresFuseBoxPower && !IsPowerAvailable())
        {
            if (offSprite != null) spriteRenderer.sprite = offSprite;
            return;
        }

        if (animFrames != null && animFrames.Length > 0)
        {
            spriteRenderer.sprite = animFrames[currentFrame % animFrames.Length];
        }
    }

    private void EnsureSpritesLoaded()
    {
        if (animFrames == null && !string.IsNullOrEmpty(sheetResource))
        {
            string key = $"{sheetResource}_{frameCount}_{pixelsPerUnit}";
            if (!sheetCache.TryGetValue(key, out animFrames) || animFrames == null || animFrames.Length == 0 || animFrames[0] == null)
            {
                animFrames = SliceHorizontalSheet(sheetResource, frameCount, pixelsPerUnit);
                sheetCache[key] = animFrames;
            }
        }

        if (offSprite == null && !string.IsNullOrEmpty(offResource))
        {
            string key = $"{offResource}_{pixelsPerUnit}";
            if (!singleCache.TryGetValue(key, out offSprite) || offSprite == null)
            {
                offSprite = PixelScenePostProcessor.LoadPixelSprite(offResource, pixelsPerUnit);
                singleCache[key] = offSprite;
            }
        }
    }

    private static Sprite[] SliceHorizontalSheet(string resourceName, int count, float ppu)
    {
        Texture2D tex = Resources.Load<Texture2D>($"Sprites/{resourceName}");
        if (tex == null) return System.Array.Empty<Sprite>();

        tex.filterMode = FilterMode.Point;
        int fw = Mathf.Max(1, tex.width / count);
        int fh = tex.height;
        Sprite[] sprites = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            sprites[i] = Sprite.Create(
                tex,
                new Rect(i * fw, 0, fw, fh),
                new Vector2(0.5f, 0.5f),
                ppu,
                0,
                SpriteMeshType.FullRect);
            sprites[i].name = $"{resourceName}_f{i}";
        }
        return sprites;
    }
}
