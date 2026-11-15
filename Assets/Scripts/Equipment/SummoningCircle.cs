using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Standalone placed summoning circle entity with a circular PolygonCollider2D,
/// "Summon Ghost" hover prompt, and multi-stage suspenseful candle ignition + waving flame loop.
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
    [SerializeField] private int sheetFrameCount = 16;
    [SerializeField] private int ignitionEndFrame = 11;
    [SerializeField] private int loopStartFrame = 12;

    [Header("Animation Timings")]
    [Tooltip("Total duration in seconds of the suspenseful ritual ignition sequence before the ghost manifests.")]
    [SerializeField, Range(5f, 10f)] private float totalIgnitionDuration = 7.5f;
    [SerializeField] private float loopFrameRate = 8f;

    [Header("World Sizing & Sorting")]
    [SerializeField] private float circleWorldDiameter = 0.30f;
    [SerializeField] private string sortingLayerName = "Item";
    [SerializeField] private int sortingOrder = 10;

    [Header("Circular Hover & Interaction Collider")]
    [SerializeField, Range(24, 32)] private int colliderVertexCount = 30;
    [SerializeField] private float colliderLocalRadius = 0.56f;

    [Header("Audio")]
    [Tooltip("Optional custom SFX played as each candle ignites.")]
    [SerializeField] private AudioClip customCandleIgniteClip = null;

    private static readonly List<SummoningCircle> ActiveCircles = new List<SummoningCircle>();
    private static AudioClip cachedIgniteTickClip;
    private static AudioClip cachedAwakenDroneClip;
    private static AudioClip cachedClimaxSurgeClip;

    private SpriteRenderer spriteRenderer;
    private PolygonCollider2D circleCollider;
    private Light2D candleLight;
    private Sprite fallbackSingleSprite;
    private Sprite[] resolvedFrames = Array.Empty<Sprite>();
    private Texture2D resolvedSheetSource;
    private CircleState state = CircleState.Idle;
    private Coroutine animationCoroutine;
    private bool initialized = false;

    public CircleState CurrentState => state;
    public bool IsIgniting => state == CircleState.Igniting;
    public float ExpectedIgnitionDuration => Mathf.Clamp(totalIgnitionDuration, 5f, 10f);

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
    public void Configure(float worldDiameter, string targetSortingLayer, int targetSortingOrder, Sprite emergencyFallbackSprite = null)
    {
        circleWorldDiameter = Mathf.Clamp(worldDiameter, 0.15f, 0.38f);
        if (!string.IsNullOrEmpty(targetSortingLayer)) sortingLayerName = targetSortingLayer;
        sortingOrder = targetSortingOrder;
        if (emergencyFallbackSprite != null) fallbackSingleSprite = emergencyFallbackSprite;
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
        else if (fallbackSingleSprite != null)
        {
            spriteRenderer.sprite = fallbackSingleSprite;
        }
        spriteRenderer.enabled = true;
        spriteRenderer.color = Color.white;
        spriteRenderer.sortingLayerName = sortingLayerName;
        spriteRenderer.sortingOrder = sortingOrder;

        Sprite activeRefSprite = (resolvedFrames.Length > 0 && resolvedFrames[0] != null) ? resolvedFrames[0] : fallbackSingleSprite;
        float localSpriteWidth = (activeRefSprite != null && activeRefSprite.pixelsPerUnit > 0f)
            ? (activeRefSprite.rect.width / activeRefSprite.pixelsPerUnit)
            : 1.28f;
        float worldScale = (localSpriteWidth > 0.01f) ? (circleWorldDiameter / localSpriteWidth) : 0.23f;
        transform.localScale = new Vector3(worldScale, worldScale, 1f);
        transform.rotation = Quaternion.identity;

        ConfigureCircularPolygonCollider(localSpriteWidth);
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
        int fh = Mathf.Max(1, sheet.height);
        int inferredCount = (sheet.width >= fh && sheet.width % fh == 0) ? (sheet.width / fh) : Mathf.Max(1, sheetFrameCount);
        int count = Mathf.Max(1, inferredCount);
        int fw = Mathf.Max(1, sheet.width / count);

        // Keep frame range consistent if a custom 9-frame or 16-frame strip is supplied
        if (count == 9)
        {
            ignitionEndFrame = 5;
            loopStartFrame = 5;
        }
        else if (count >= 16)
        {
            ignitionEndFrame = 11;
            loopStartFrame = 12;
        }

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

    private void ConfigureCircularPolygonCollider(float localSpriteWidth)
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
        float derivedRadius = localSpriteWidth > 0.01f ? (localSpriteWidth * 0.44f) : colliderLocalRadius;
        float radius = Mathf.Max(0.1f, derivedRadius);
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
            candleLight.pointLightOuterRadius = 1.65f;
            candleLight.falloffIntensity = 0.65f;
            candleLight.shadowsEnabled = false;
            candleLight.targetSortingLayers = Array.ConvertAll(SortingLayer.layers, layer => layer.id);
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

        float targetDuration = ExpectedIgnitionDuration;

        if (resolvedFrames.Length == 0)
        {
            // Keep suspense beat even if emergency fallback single sprite is active
            yield return new WaitForSeconds(targetDuration);
            onAllCandlesLit?.Invoke();
            state = CircleState.BurningLoop;
            animationCoroutine = null;
            yield break;
        }

        int maxIgniteFrame = Mathf.Clamp(ignitionEndFrame, 0, resolvedFrames.Length - 1);
        float[] frameWeights = BuildIgnitionFrameWeights(maxIgniteFrame);
        float weightSum = 0f;
        for (int i = 0; i < frameWeights.Length; i++) weightSum += frameWeights[i];
        float timeScale = weightSum > 0.01f ? (targetDuration / weightSum) : 1f;

        if (candleLight != null)
        {
            candleLight.enabled = true;
            candleLight.color = new Color(1f, 0.23f, 0.16f, 1f);
            candleLight.pointLightOuterRadius = 1.65f;
            candleLight.intensity = 0.15f;
        }

        for (int f = 0; f <= maxIgniteFrame; f++)
        {
            if (spriteRenderer != null && resolvedFrames[f] != null)
            {
                spriteRenderer.sprite = resolvedFrames[f];
            }

            PlayIgnitionStageAudio(f, maxIgniteFrame);

            float stepDuration = frameWeights[f] * timeScale;
            float normStart = maxIgniteFrame > 0 ? Mathf.Max(0f, (f - 1) / (float)maxIgniteFrame) : 1f;
            float normEnd = maxIgniteFrame > 0 ? (f / (float)maxIgniteFrame) : 1f;
            bool isFlare = (maxIgniteFrame >= 11 && (f == 2 || f == 4 || f == 6 || f == 8 || f == 10 || f == 11));

            float elapsed = 0f;
            while (elapsed < stepDuration)
            {
                elapsed += Time.deltaTime;
                float u = stepDuration > 0.001f ? Mathf.Clamp01(elapsed / stepDuration) : 1f;
                if (candleLight != null)
                {
                    float norm = Mathf.Lerp(normStart, normEnd, u);
                    float flicker = Mathf.Sin(Time.time * 16f + f * 1.7f) * 0.10f;
                    float flareBoost = isFlare ? Mathf.Sin(u * Mathf.PI) * 0.55f : 0f;
                    candleLight.intensity = Mathf.Lerp(0.18f, 1.95f, norm) + flareBoost + flicker;
                    candleLight.pointLightOuterRadius = Mathf.Lerp(0.85f, 1.80f, norm) + (isFlare ? 0.20f : 0f);
                }
                yield return null;
            }
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
                float pulse = Mathf.Sin(Time.time * 11f + loopIdx * 0.9f) * 0.16f;
                candleLight.intensity = 1.75f + pulse;
            }

            loopIdx++;
            yield return new WaitForSeconds(loopStep);
        }
    }

    private static float[] BuildIgnitionFrameWeights(int maxIgniteFrame)
    {
        float[] weights = new float[maxIgniteFrame + 1];
        if (maxIgniteFrame >= 11)
        {
            // Paced across ~7.5s for deliberate horror suspense:
            // F0 tremble -> F1 crimson rune wake -> F2/3..F10/11 candles 1..5 -> full climax surge
            float[] preset12 = { 0.35f, 0.85f, 0.38f, 0.84f, 0.38f, 0.84f, 0.38f, 0.84f, 0.38f, 0.84f, 0.42f, 0.98f };
            for (int i = 0; i <= maxIgniteFrame; i++)
            {
                weights[i] = i < preset12.Length ? preset12[i] : 0.75f;
            }
        }
        else
        {
            for (int i = 0; i <= maxIgniteFrame; i++)
            {
                weights[i] = i == 0 ? 0.5f : 1.0f;
            }
        }
        return weights;
    }

    private void PlayIgnitionStageAudio(int frame, int maxIgniteFrame)
    {
        if (maxIgniteFrame >= 11)
        {
            if (frame == 1)
            {
                AudioClip awaken = GetOrCreateAwakenDroneClip();
                if (awaken != null)
                {
                    GameAudioManager.PlayClipAt(awaken, transform.position, 0.48f, 0.9f, 1.0f, 0.4f, 3.6f, 0.08f);
                }
            }
            else if (frame == 2 || frame == 4 || frame == 6 || frame == 8 || frame == 10)
            {
                int candleNumber = (frame / 2);
                PlayCandleIgniteTick(candleNumber);
            }
            else if (frame == 11)
            {
                AudioClip surge = GetOrCreateClimaxSurgeClip();
                if (surge != null)
                {
                    GameAudioManager.PlayClipAt(surge, transform.position, 0.56f, 0.9f, 1.0f, 0.5f, 4.2f, 0.10f);
                }
            }
        }
        else if (frame > 0)
        {
            PlayCandleIgniteTick(frame);
        }
    }

    private void PlayCandleIgniteTick(int candleNumber)
    {
        AudioClip clip = customCandleIgniteClip != null ? customCandleIgniteClip : GetOrCreateIgniteTickClip();
        if (clip != null)
        {
            float pitch = 0.88f + candleNumber * 0.055f;
            GameAudioManager.PlayClipAt(clip, transform.position, 0.46f, 0.9f, pitch, 0.35f, 3.2f, 0.05f);
        }
    }

    private static AudioClip GetOrCreateAwakenDroneClip()
    {
        if (cachedAwakenDroneClip != null) return cachedAwakenDroneClip;

        const int sr = 22050;
        const float dur = 0.95f;
        int len = Mathf.FloorToInt(sr * dur);
        float[] samples = new float[len];

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            float env = Mathf.Clamp01(t / 0.08f) * Mathf.Exp(-t * 2.4f);
            float lowHum = Mathf.Sin(2f * Mathf.PI * 82.4f * t) * 0.52f + Mathf.Sin(2f * Mathf.PI * 123.5f * t) * 0.24f;
            float subOct = Mathf.Sin(2f * Mathf.PI * 55.0f * t) * 0.34f;
            samples[i] = Mathf.Clamp((lowHum + subOct) * env * 0.72f, -0.8f, 0.8f);
        }

        cachedAwakenDroneClip = AudioClip.Create("CircleAwakenDrone", len, 1, sr, false);
        cachedAwakenDroneClip.SetData(samples, 0);
        return cachedAwakenDroneClip;
    }

    private static AudioClip GetOrCreateClimaxSurgeClip()
    {
        if (cachedClimaxSurgeClip != null) return cachedClimaxSurgeClip;

        const int sr = 22050;
        const float dur = 0.85f;
        int len = Mathf.FloorToInt(sr * dur);
        float[] samples = new float[len];
        float lp = 0f;
        uint state = 48271u;

        for (int i = 0; i < len; i++)
        {
            float t = (float)i / sr;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            float n = ((state & 0xFFFF) / 32767.5f) - 1f;
            lp = lp * 0.82f + n * 0.18f;

            float env = Mathf.Clamp01(t / 0.04f) * Mathf.Exp(-t * 3.1f);
            float fSweep = Mathf.Lerp(110f, 245f, Mathf.Clamp01(t / 0.55f));
            float chord = Mathf.Sin(2f * Mathf.PI * fSweep * t) * 0.45f + Mathf.Sin(2f * Mathf.PI * (fSweep * 1.498f) * t) * 0.28f;
            float fireWhoosh = lp * 0.55f;
            samples[i] = Mathf.Clamp((chord + fireWhoosh) * env * 0.78f, -0.85f, 0.85f);
        }

        cachedClimaxSurgeClip = AudioClip.Create("CircleClimaxSurge", len, 1, sr, false);
        cachedClimaxSurgeClip.SetData(samples, 0);
        return cachedClimaxSurgeClip;
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
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
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
