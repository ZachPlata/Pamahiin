using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Standalone placed summoning circle entity with a circular PolygonCollider2D,
/// "Summon Ghost" hover prompt, and 9-frame sequential 5-candle ignition + waving flame loop.
/// </summary>
[RequireComponent(typeof(SpriteRenderer), typeof(PolygonCollider2D))]
public class SummoningCircle : MonoBehaviour, IInteractable
{
    public enum CircleState
    {
        Idle = 0,
        Igniting = 1,
        BurningLoop = 2
    }

    private const string DEFAULT_SHEET_RESOURCE = "Sprites/Ritual/summoning_circle_sheet";

    [Header("Sprite Sheet & Override Slots")]
    [Tooltip("Optional custom horizontal sprite sheet overriding Resources/Sprites/Ritual/summoning_circle_sheet.")]
    [SerializeField] private Texture2D customSpriteSheet = null;
    [Tooltip("Optional explicit sprite array overriding customSpriteSheet / Resources sheet.")]
    [SerializeField] private Sprite[] customFrames = null;
    [SerializeField] private int sheetFrameCount = 9;
    [SerializeField] private int ignitionEndFrame = 5;
    [SerializeField] private int loopStartFrame = 5;

    [Header("Animation Timings")]
    [SerializeField] private float ignitionFrameRate = 3.5f;
    [SerializeField] private float loopFrameRate = 8f;

    [Header("World Sizing & Sorting")]
    [SerializeField] private float circleWorldDiameter = 0.26f;
    [SerializeField] private string sortingLayerName = "Item";
    [SerializeField] private int sortingOrder = 10;

    [Header("Circular Hover & Interaction Collider")]
    [SerializeField, Range(24, 32)] private int colliderVertexCount = 30;
    [SerializeField] private float colliderLocalRadius = 0.41f;

    [Header("Audio")]
    [Tooltip("Optional custom SFX played as each candle ignites.")]
    [SerializeField] private AudioClip customCandleIgniteClip = null;

    private static readonly List<SummoningCircle> ActiveCircles = new List<SummoningCircle>();
    private static AudioClip cachedIgniteTickClip;

    private SpriteRenderer spriteRenderer;
    private PolygonCollider2D circleCollider;
    private Light2D candleLight;
    private Sprite[] resolvedFrames = Array.Empty<Sprite>();
    private Texture2D resolvedSheetSource;
    private CircleState state = CircleState.Idle;
    private Coroutine animationCoroutine;
    private bool initialized = false;

    public CircleState CurrentState => state;
    public bool IsIgniting => state == CircleState.Igniting;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnEnable()
    {
        if (!ActiveCircles.Contains(this))
        {
            ActiveCircles.Add(this);
        }
        EnsureInitialized();
    }

    private void OnDisable()
    {
        ActiveCircles.Remove(this);
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }
    }

    /// <summary>
    /// Configures world scale, sorting layer/order, circular PolygonCollider2D, and initial unlit frame.
    /// </summary>
    public void Configure(float worldDiameter, string targetSortingLayer, int targetSortingOrder)
    {
        circleWorldDiameter = Mathf.Clamp(worldDiameter, 0.15f, 0.38f);
        if (!string.IsNullOrEmpty(targetSortingLayer)) sortingLayerName = targetSortingLayer;
        sortingOrder = targetSortingOrder;
        initialized = false;
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        int interactableLayer = LayerMask.NameToLayer("Interactable");
        if (interactableLayer >= 0)
        {
            gameObject.layer = interactableLayer;
        }

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

        resolvedFrames = ResolveFrames();
        if (resolvedFrames.Length > 0 && resolvedFrames[0] != null)
        {
            spriteRenderer.sprite = resolvedFrames[0];
        }
        spriteRenderer.enabled = true;
        spriteRenderer.color = Color.white;
        spriteRenderer.sortingLayerName = sortingLayerName;
        spriteRenderer.sortingOrder = sortingOrder;

        float localSpriteWidth = (resolvedFrames.Length > 0 && resolvedFrames[0] != null && resolvedFrames[0].pixelsPerUnit > 0f)
            ? (resolvedFrames[0].rect.width / resolvedFrames[0].pixelsPerUnit)
            : 0.96f;
        float worldScale = (localSpriteWidth > 0.01f) ? (circleWorldDiameter / localSpriteWidth) : 0.27f;
        transform.localScale = new Vector3(worldScale, worldScale, 1f);
        transform.rotation = Quaternion.identity;

        ConfigureCircularPolygonCollider();
        EnsureCandleLight();
        ApplyIdleVisuals();
    }

    private Sprite[] ResolveFrames()
    {
        if (customFrames != null && customFrames.Length > 0)
        {
            return customFrames;
        }

        Texture2D sheet = customSpriteSheet != null
            ? customSpriteSheet
            : Resources.Load<Texture2D>(DEFAULT_SHEET_RESOURCE);

        if (sheet == null) return Array.Empty<Sprite>();
        if (resolvedFrames != null && resolvedFrames.Length > 0 && resolvedSheetSource == sheet)
        {
            return resolvedFrames;
        }

        sheet.filterMode = FilterMode.Point;
        int count = Mathf.Max(1, sheetFrameCount);
        int fw = Mathf.Max(1, sheet.width / count);
        int fh = sheet.height;

        Sprite[] frames = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = Sprite.Create(
                sheet,
                new Rect(i * fw, 0, fw, fh),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            frames[i].name = $"summoning_circle_f{i}";
        }
        resolvedSheetSource = sheet;
        return frames;
    }

    private void ConfigureCircularPolygonCollider()
    {
        circleCollider = GetComponent<PolygonCollider2D>();
        if (circleCollider == null) circleCollider = gameObject.AddComponent<PolygonCollider2D>();

        // Disable any stray BoxCollider2D so ItemHoverIndicator always traces the circle
        foreach (var box in GetComponents<BoxCollider2D>())
        {
            if (box != null) box.enabled = false;
        }

        circleCollider.isTrigger = true;
        circleCollider.enabled = true;

        int ptsCount = Mathf.Clamp(colliderVertexCount, 24, 32);
        Vector2[] circleVertices = new Vector2[ptsCount];
        float radius = Mathf.Max(0.1f, colliderLocalRadius);
        for (int i = 0; i < ptsCount; i++)
        {
            float angle = (i / (float)ptsCount) * Mathf.PI * 2f;
            circleVertices[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        circleCollider.pathCount = 1;
        circleCollider.SetPath(0, circleVertices);
    }

    private void EnsureCandleLight()
    {
        if (candleLight == null)
        {
            candleLight = GetComponentInChildren<Light2D>();
        }

        if (candleLight == null)
        {
            var lightGo = new GameObject("CircleCandleGlow");
            lightGo.transform.SetParent(transform, false);
            candleLight = lightGo.AddComponent<Light2D>();
            candleLight.lightType = Light2D.LightType.Point;
            candleLight.color = new Color(1f, 0.23f, 0.16f, 1f);
            candleLight.pointLightInnerRadius = 0.08f;
            candleLight.pointLightOuterRadius = 1.45f;
            candleLight.intensity = 0f;
            candleLight.enabled = false;
        }
    }

    private void ApplyIdleVisuals()
    {
        state = CircleState.Idle;
        if (spriteRenderer != null && resolvedFrames.Length > 0 && resolvedFrames[0] != null)
        {
            spriteRenderer.sprite = resolvedFrames[0];
        }
        if (candleLight != null)
        {
            candleLight.intensity = 0f;
            candleLight.enabled = false;
        }
    }

    /// <summary>
    /// Resets the circle back to Frame 0 (all 5 red candles unlit).
    /// </summary>
    public void ResetToUnlit()
    {
        EnsureInitialized();
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }
        ApplyIdleVisuals();
    }

    /// <summary>
    /// Lights all 5 red candles sequentially around the circle (Frames 0 -> ignitionEndFrame),
    /// invokes onAllCandlesLit, and continues looping the waving flame frames (loopStartFrame..end).
    /// </summary>
    public void PlayIgnitionSequence(Action onAllCandlesLit)
    {
        EnsureInitialized();
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
        }
        animationCoroutine = StartCoroutine(IgnitionAndLoopRoutine(onAllCandlesLit));
    }

    private IEnumerator IgnitionAndLoopRoutine(Action onAllCandlesLit)
    {
        state = CircleState.Igniting;

        if (resolvedFrames.Length == 0)
        {
            onAllCandlesLit?.Invoke();
            state = CircleState.BurningLoop;
            animationCoroutine = null;
            yield break;
        }

        int maxIgniteFrame = Mathf.Clamp(ignitionEndFrame, 0, resolvedFrames.Length - 1);
        float stepDuration = 1f / Mathf.Max(0.5f, ignitionFrameRate);

        if (candleLight != null)
        {
            candleLight.enabled = true;
            candleLight.color = new Color(1f, 0.23f, 0.16f, 1f);
            candleLight.pointLightOuterRadius = 1.45f;
            candleLight.intensity = 0.15f;
        }

        for (int f = 0; f <= maxIgniteFrame; f++)
        {
            if (spriteRenderer != null && resolvedFrames[f] != null)
            {
                spriteRenderer.sprite = resolvedFrames[f];
            }

            if (f > 0)
            {
                PlayCandleIgniteTick(f);
            }

            if (candleLight != null)
            {
                float norm = maxIgniteFrame > 0 ? (float)f / maxIgniteFrame : 1f;
                candleLight.intensity = Mathf.Lerp(0.18f, 1.75f, norm);
                candleLight.pointLightOuterRadius = Mathf.Lerp(0.75f, 1.55f, norm);
            }

            yield return new WaitForSeconds(stepDuration);
        }

        state = CircleState.BurningLoop;
        onAllCandlesLit?.Invoke();

        int loopMin = Mathf.Clamp(loopStartFrame, 0, resolvedFrames.Length - 1);
        int loopCount = Mathf.Max(1, resolvedFrames.Length - loopMin);
        float loopStep = 1f / Mathf.Max(1f, loopFrameRate);
        int loopIdx = 0;

        while (true)
        {
            int frameIndex = loopMin + (loopIdx % loopCount);
            if (spriteRenderer != null && resolvedFrames[frameIndex] != null)
            {
                spriteRenderer.sprite = resolvedFrames[frameIndex];
            }

            if (candleLight != null)
            {
                float pulse = Mathf.Sin(Time.time * 11f + loopIdx * 0.9f) * 0.14f;
                candleLight.intensity = 1.65f + pulse;
            }

            loopIdx++;
            yield return new WaitForSeconds(loopStep);
        }
    }

    private void PlayCandleIgniteTick(int candleNumber)
    {
        AudioClip clip = customCandleIgniteClip != null ? customCandleIgniteClip : GetOrCreateIgniteTickClip();
        if (clip != null)
        {
            float pitch = 0.92f + candleNumber * 0.05f;
            GameAudioManager.PlayClipAt(clip, transform.position, 0.42f, 0.9f, pitch, 0.35f, 3.0f, 0.05f);
        }
    }

    private static AudioClip GetOrCreateIgniteTickClip()
    {
        if (cachedIgniteTickClip != null) return cachedIgniteTickClip;

        const int sr = 22050;
        const float dur = 0.14f;
        int len = Mathf.FloorToInt(sr * dur);
        float[] samples = new float[len];
        float lp = 0f;
        uint state = 19731u;

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            float n = ((state & 0xFFFF) / 32767.5f) - 1f;
            lp = lp * 0.72f + n * 0.28f;
            float env = Mathf.Exp(-t * 28f) * Mathf.Clamp01(t / 0.008f);
            float warm = Mathf.Sin(2f * Mathf.PI * 195f * t) * Mathf.Exp(-t * 36f) * 0.25f;
            samples[i] = Mathf.Clamp((n - lp * 0.4f) * env * 0.55f + warm, -0.8f, 0.8f);
        }

        cachedIgniteTickClip = AudioClip.Create("CandleIgniteTick", len, 1, sr, false);
        cachedIgniteTickClip.SetData(samples, 0);
        return cachedIgniteTickClip;
    }

    public static SummoningCircle FindClosest(Vector3 worldPos, float maxRadius = 0.75f)
    {
        SummoningCircle best = null;
        float bestSqr = maxRadius * maxRadius;
        Vector2 p = new Vector2(worldPos.x, worldPos.y);

        for (int i = 0; i < ActiveCircles.Count; i++)
        {
            var c = ActiveCircles[i];
            if (c == null || !c.gameObject.activeInHierarchy) continue;
            float sqr = ((Vector2)c.transform.position - p).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                best = c;
            }
        }
        return best;
    }

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPos) { }
    public void OnDragEnd(ulong clientId) { }

    public string GetInteractText()
    {
        var exorcismManager = ExorcismManager.Instance != null
            ? ExorcismManager.Instance
            : FindAnyObjectByType<ExorcismManager>();

        if (exorcismManager != null)
        {
            if (exorcismManager.IsExorcismCompleted)
            {
                return "Exorcism Completed";
            }
            if (exorcismManager.IsExorcismActive || state == CircleState.Igniting)
            {
                return "Exorcism in Progress";
            }
        }
        else if (state == CircleState.Igniting)
        {
            return "Exorcism in Progress";
        }

        return "Summon Ghost";
    }

    public void Interact()
    {
        var exorcismManager = ExorcismManager.Instance != null
            ? ExorcismManager.Instance
            : FindAnyObjectByType<ExorcismManager>();

        if (exorcismManager == null)
        {
            Debug.LogWarning("[SummoningCircle] ExorcismManager not found in scene!");
            return;
        }

        if (exorcismManager.IsExorcismActive || exorcismManager.IsExorcismCompleted || state == CircleState.Igniting)
        {
            return;
        }

        exorcismManager.InitiateRitual(transform.position);
    }
}
