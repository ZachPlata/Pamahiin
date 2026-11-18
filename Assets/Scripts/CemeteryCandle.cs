using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Animated flickering gravestone/altar candle for CemeteryMap.
/// Slices Sprites/CemeteryMap/candle_sheet into 4 frames @ 64 PPU, gently flickers
/// its child CandleLight_* Point Light2D, and allows players to relight blown-out candles via [E].
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CemeteryCandle : MonoBehaviour, IInteractable
{
    public static readonly List<CemeteryCandle> AllCandles = new List<CemeteryCandle>();

    private static Sprite[] s_CandleFrames;

    [Header("Candle Configuration")]
    [SerializeField] private int candleId = 0;
    [SerializeField] private bool isLit = true;
    [SerializeField] private Light2D candleLight;
    [SerializeField] private float baseIntensity = 0.34f;
    [SerializeField] private float baseOuterRadius = 0.38f;

    private SpriteRenderer spriteRenderer;
    private Collider2D interactCollider;
    private float animTimer;
    private int currentFrame;
    private float fps = 7f;
    private float flickerPhase;
    private float flickerSpeed;

    public int CandleId => candleId;
    public bool IsLit => isLit;

    public void Configure(int id, Light2D lightRef, bool startLit = true)
    {
        candleId = id;
        candleLight = lightRef;
        isLit = startLit;
        EnsureInitialized();
        SetLitState(startLit);
    }

    private void Awake()
    {
        EnsureInitialized();
        SetLitState(isLit);
    }

    private void Start()
    {
        if (CemeteryCandleManager.Instance != null && CemeteryCandleManager.Instance.IsSpawned)
        {
            SetLitState(CemeteryCandleManager.Instance.IsCandleLit(candleId));
        }
    }

    private void OnEnable()
    {
        if (!AllCandles.Contains(this))
        {
            AllCandles.Add(this);
        }
    }

    private void OnDisable()
    {
        AllCandles.Remove(this);
    }

    private void EnsureInitialized()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
        if (transform.localScale.x >= 0.5f || transform.localScale.y >= 0.5f)
        {
            transform.localScale = new Vector3(0.24f, 0.24f, 1f);
        }
        if (interactCollider == null)
        {
            interactCollider = GetComponent<Collider2D>();
            if (interactCollider != null)
            {
                interactCollider.isTrigger = true;
            }
        }
        if (candleLight == null)
        {
            candleLight = GetComponentInChildren<Light2D>(true);
        }

        EnsureCandleFramesLoaded();

        if (fps <= 0.1f || flickerSpeed <= 0.1f)
        {
            Random.State prevState = Random.state;
            int seed = (candleId * 7919)
                ^ (Mathf.RoundToInt(transform.position.x * 1000f) * 31)
                ^ (Mathf.RoundToInt(transform.position.y * 1000f) * 131);
            Random.InitState(seed);
            currentFrame = Random.Range(0, Mathf.Max(1, s_CandleFrames != null ? s_CandleFrames.Length : 4));
            animTimer = Random.Range(0f, 0.16f);
            fps = Random.Range(5.5f, 8.5f);
            flickerPhase = Random.Range(0f, Mathf.PI * 2f);
            flickerSpeed = Random.Range(6.5f, 10.5f);
            Random.state = prevState;
        }

        if (candleLight != null)
        {
            candleLight.lightType = Light2D.LightType.Point;
            candleLight.color = new Color(1.0f, 0.72f, 0.34f, 1f);
            candleLight.pointLightOuterRadius = baseOuterRadius;
            candleLight.shadowsEnabled = false;
        }
    }

    private static void EnsureCandleFramesLoaded()
    {
        if (s_CandleFrames != null && s_CandleFrames.Length == 4 && s_CandleFrames[0] != null)
        {
            return;
        }

        Texture2D tex = Resources.Load<Texture2D>("Sprites/CemeteryMap/candle_sheet");
        if (tex == null) return;

        tex.filterMode = FilterMode.Point;
        const int frameCount = 4;
        int fw = Mathf.Max(1, tex.width / frameCount);
        int fh = tex.height;

        Sprite[] sliced = new Sprite[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            sliced[i] = Sprite.Create(
                tex,
                new Rect(i * fw, 0, fw, fh),
                new Vector2(0.5f, 0.5f),
                64f,
                0,
                SpriteMeshType.FullRect);
            sliced[i].name = $"candle_sheet_f{i}";
        }
        s_CandleFrames = sliced;
    }

    public void SetLitState(bool lit)
    {
        isLit = lit;
        EnsureInitialized();

        if (candleLight != null)
        {
            candleLight.enabled = lit;
            if (lit)
            {
                candleLight.intensity = baseIntensity;
            }
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = lit ? Color.white : new Color(0.45f, 0.42f, 0.42f, 0.9f);
            if (s_CandleFrames != null && s_CandleFrames.Length > 0)
            {
                int idx = lit ? (currentFrame % s_CandleFrames.Length) : 0;
                if (s_CandleFrames[idx] != null)
                {
                    spriteRenderer.sprite = s_CandleFrames[idx];
                }
            }
        }

        // Only expose interaction trigger when blown out so lit candles never clutter nearby switches/props
        if (interactCollider != null)
        {
            interactCollider.enabled = !lit;
        }
    }

    private void Update()
    {
        if (!isLit) return;

        if (s_CandleFrames != null && s_CandleFrames.Length > 0 && spriteRenderer != null)
        {
            animTimer += Time.deltaTime;
            float step = 1f / Mathf.Max(1f, fps);
            if (animTimer >= step)
            {
                animTimer -= step;
                currentFrame = (currentFrame + 1) % s_CandleFrames.Length;
                if (s_CandleFrames[currentFrame] != null)
                {
                    spriteRenderer.sprite = s_CandleFrames[currentFrame];
                }
            }
        }

        if (candleLight != null && candleLight.enabled)
        {
            float flicker = Mathf.Sin(Time.time * flickerSpeed + flickerPhase) * 0.04f
                          + Mathf.Sin(Time.time * (flickerSpeed * 1.73f) + flickerPhase) * 0.02f;
            candleLight.intensity = Mathf.Max(0.18f, baseIntensity + flicker);
        }
    }

    // --- IInteractable ---
    public void Interact()
    {
        if (isLit) return;
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;

        if (CemeteryCandleManager.Instance != null)
        {
            CemeteryCandleManager.Instance.RequestSetCandleLit(candleId, true);
        }
        else
        {
            SetLitState(true);
        }
    }

    public string GetInteractText()
    {
        return !isLit ? "Relight Candle" : string.Empty;
    }

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPos) { }
    public void OnDragEnd(ulong clientId) { }
}
