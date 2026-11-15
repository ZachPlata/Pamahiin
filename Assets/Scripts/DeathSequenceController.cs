using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Strictly local-client controller that plays the full Phasmophobia-style first-person victim death cutscene:
///   1. Ghost "psst!" behind victim
///   2. 20-frame 16-bit Ghost Hands creeping in from corners &amp; crushing camera to black
///   3. Hard cut to Burial Pit off-bounds stage with damp grave ambience &amp; motionless ghost
///   4. Accelerating ghost lunge + 12-frame Jumpscare shriek flash
///   5. 0s hard cut to black &amp; silence -> restore camera to death location -> transition to GhostSpectator
/// </summary>
public class DeathSequenceController : MonoBehaviour
{
    public static DeathSequenceController Instance { get; private set; }

    private const int HandFrameCount = 20;
    private const float HandFrameRate = 14f;
    private const int JumpscareFrameCount = 12;
    private const float JumpscareFrameRate = 15f;

    private Canvas overlayCanvas;
    private Image blackoutImage;
    private Image sheetOverlayImage;
    private AspectRatioFitter sheetAspectFitter;
    private Animator sheetAnimator;
    private AudioSource sfxSource;
    private AudioSource ambienceSource;

    private RuntimeAnimatorController overlayAnimatorController;
    private Sprite[] ghostHandSprites;
    private Sprite[] jumpscareSprites;
    private AudioClip psstClip;
    private AudioClip handGrabClip;
    private AudioClip burialAmbienceClip;
    private AudioClip jumpscareClip;

    private Coroutine activeCoroutine;
    private int sequenceGeneration;
    private bool isPlaying;

    public bool IsPlaying => isPlaying;
    public Image SheetOverlayImage => sheetOverlayImage;
    public AspectRatioFitter SheetAspectFitter => sheetAspectFitter;
    public Animator SheetAnimator => sheetAnimator;

    public static DeathSequenceController EnsureInstance()
    {
        if (Instance != null) return Instance;

        var existing = FindAnyObjectByType<DeathSequenceController>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject go = new GameObject("DeathSequenceController");
        Instance = go.AddComponent<DeathSequenceController>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildOverlayAndAudio();
        LoadResources();
        HideAllVisualsImmediate();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void BuildOverlayAndAudio()
    {
        GameObject canvasGo = new GameObject("DeathCutsceneCanvas");
        canvasGo.transform.SetParent(transform, false);
        overlayCanvas = canvasGo.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 250;
        canvasGo.AddComponent<CanvasScaler>();

        // Full-screen animated sheet Image (Ghost Hands & Jumpscare) using 16:9 EnvelopeParent AspectRatioFitter
        GameObject sheetGo = new GameObject("SheetOverlay");
        sheetGo.transform.SetParent(canvasGo.transform, false);
        sheetOverlayImage = sheetGo.AddComponent<Image>();
        sheetOverlayImage.raycastTarget = false;
        sheetOverlayImage.preserveAspect = false;
        sheetAspectFitter = sheetGo.AddComponent<AspectRatioFitter>();
        sheetAspectFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        sheetAspectFitter.aspectRatio = 16f / 9f;
        sheetAnimator = sheetGo.AddComponent<Animator>();
        sheetAnimator.updateMode = AnimatorUpdateMode.Normal;
        sheetAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        RectTransform sheetRt = sheetGo.GetComponent<RectTransform>();
        sheetRt.anchorMin = Vector2.zero;
        sheetRt.anchorMax = Vector2.one;
        sheetRt.offsetMin = Vector2.zero;
        sheetRt.offsetMax = Vector2.zero;

        // Pitch-black blackout curtain (above or beneath depending on stage, rendered last so 0s black cut covers everything)
        GameObject blackGo = new GameObject("BlackoutCurtain");
        blackGo.transform.SetParent(canvasGo.transform, false);
        blackoutImage = blackGo.AddComponent<Image>();
        blackoutImage.color = new Color(0f, 0f, 0f, 0f);
        blackoutImage.raycastTarget = false;
        RectTransform blackRt = blackGo.GetComponent<RectTransform>();
        blackRt.anchorMin = Vector2.zero;
        blackRt.anchorMax = Vector2.one;
        blackRt.offsetMin = Vector2.zero;
        blackRt.offsetMax = Vector2.zero;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;

        ambienceSource = gameObject.AddComponent<AudioSource>();
        ambienceSource.playOnAwake = false;
        ambienceSource.loop = true;
        ambienceSource.spatialBlend = 0f;
    }

    private void LoadResources()
    {
        overlayAnimatorController = Resources.Load<RuntimeAnimatorController>("Animations/DeathOverlayAnim");
        if (sheetAnimator != null && overlayAnimatorController != null)
        {
            sheetAnimator.runtimeAnimatorController = overlayAnimatorController;
        }

        ghostHandSprites = LoadOrderedSprites("Sprites/UI/death_ghost_hand_sheet");
        jumpscareSprites = LoadOrderedSprites("Sprites/UI/burial_jumpscare_sheet");

        psstClip = Resources.Load<AudioClip>("Audio/ghost_psst");
        handGrabClip = Resources.Load<AudioClip>("Audio/ghost_hand_grab");
        burialAmbienceClip = Resources.Load<AudioClip>("Audio/burial_ambience");
        jumpscareClip = Resources.Load<AudioClip>("Audio/ghost_jumpscare");
    }

    private static Sprite[] LoadOrderedSprites(string resourcePath)
    {
        Sprite[] loaded = Resources.LoadAll<Sprite>(resourcePath);
        if (loaded == null || loaded.Length == 0) return System.Array.Empty<Sprite>();
        System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a != null ? a.name : string.Empty, b != null ? b.name : string.Empty));
        return loaded;
    }

    public void BeginDeathSequence(PlayerController victim)
    {
        if (victim == null || !victim.IsOwner) return;

        LoadResources();
        CancelAndReset();

        sequenceGeneration++;
        isPlaying = true;
        activeCoroutine = StartCoroutine(ExecuteDeathSequenceRoutine(victim, sequenceGeneration));
    }

    public void CancelAndReset()
    {
        sequenceGeneration++;
        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
            activeCoroutine = null;
        }
        isPlaying = false;

        if (sfxSource != null) sfxSource.Stop();
        if (ambienceSource != null) ambienceSource.Stop();

        WeatherManager.SetLocalInBurialMap(false);
        if (CameraFollow.Instance != null)
        {
            CameraFollow.Instance.SetTargetOverride(null);
        }
        if (BurialMapStage.Instance != null)
        {
            BurialMapStage.Instance.SetStageActive(false);
        }

        HideAllVisualsImmediate();
    }

    private void HideAllVisualsImmediate()
    {
        if (sheetAnimator != null && sheetAnimator.runtimeAnimatorController != null && sheetAnimator.isActiveAndEnabled)
        {
            sheetAnimator.Play("Idle", 0, 0f);
        }
        if (overlayCanvas != null) overlayCanvas.enabled = false;
        if (sheetOverlayImage != null)
        {
            sheetOverlayImage.enabled = false;
            sheetOverlayImage.color = Color.white;
            sheetOverlayImage.sprite = null;
        }
        if (blackoutImage != null)
        {
            blackoutImage.color = new Color(0f, 0f, 0f, 0f);
            blackoutImage.enabled = false;
        }
    }

    private IEnumerator PlayOverlayClipRoutine(
        string stateName,
        Sprite[] fallbackSprites,
        float frameRate,
        int frameCount,
        bool darkenNearEnd,
        PlayerController victim,
        int gen)
    {
        float safeFps = Mathf.Max(1f, frameRate);
        float totalDuration = frameCount / safeFps;

        if (sheetOverlayImage == null)
        {
            yield return new WaitForSeconds(totalDuration);
            yield break;
        }

        sheetOverlayImage.enabled = true;
        sheetOverlayImage.color = Color.white;
        if (fallbackSprites != null && fallbackSprites.Length > 0 && fallbackSprites[0] != null)
        {
            sheetOverlayImage.sprite = fallbackSprites[0];
        }

        bool useAnimator = sheetAnimator != null && sheetAnimator.runtimeAnimatorController != null;
        if (useAnimator)
        {
            sheetAnimator.enabled = true;
            sheetAnimator.Play(stateName, 0, 0f);
            sheetAnimator.Update(0f);

            float elapsed = 0f;
            float maxWait = totalDuration + 0.15f;
            while (elapsed < maxWait)
            {
                if (gen != sequenceGeneration || victim == null || victim.IsAlive) yield break;

                elapsed += Time.deltaTime;
                AnimatorStateInfo info = sheetAnimator.GetCurrentAnimatorStateInfo(0);
                float progress = info.IsName(stateName)
                    ? Mathf.Clamp01(info.normalizedTime)
                    : Mathf.Clamp01(elapsed / totalDuration);

                if (darkenNearEnd && blackoutImage != null && progress >= 0.80f)
                {
                    float darkT = (progress - 0.80f) / 0.20f;
                    blackoutImage.color = new Color(0f, 0f, 0f, Mathf.Clamp01(darkT * 0.65f));
                }

                if ((info.IsName(stateName) && info.normalizedTime >= 1f) || elapsed >= totalDuration)
                {
                    break;
                }
                yield return null;
            }
            yield break;
        }

        if (fallbackSprites != null && fallbackSprites.Length > 0)
        {
            float frameDur = 1f / safeFps;
            for (int f = 0; f < fallbackSprites.Length; f++)
            {
                if (gen != sequenceGeneration || victim == null || victim.IsAlive) yield break;
                if (fallbackSprites[f] != null)
                {
                    sheetOverlayImage.sprite = fallbackSprites[f];
                }
                if (darkenNearEnd && blackoutImage != null && f >= fallbackSprites.Length - 3)
                {
                    float darkT = (f - (fallbackSprites.Length - 4)) / 3f;
                    blackoutImage.color = new Color(0f, 0f, 0f, Mathf.Clamp01(darkT * 0.65f));
                }
                yield return new WaitForSeconds(frameDur);
            }
            yield break;
        }

        yield return new WaitForSeconds(totalDuration);
    }

    private IEnumerator ExecuteDeathSequenceRoutine(PlayerController victim, int gen)
    {
        // Silence hunt pulse, chromatic aberration & heartbeat immediately
        if (GhostAtmosphereController.Instance != null)
        {
            GhostAtmosphereController.Instance.OnPlayerCaught();
        }

        overlayCanvas.enabled = true;
        blackoutImage.enabled = true;
        blackoutImage.color = new Color(0f, 0f, 0f, 0f);

        // ── Phase 1: Ghost "psst!" behind the victim (0.00s -> 0.40s) ───────
        if (sfxSource != null && psstClip != null)
        {
            sfxSource.PlayOneShot(psstClip, 0.90f);
        }
        yield return new WaitForSeconds(0.40f);
        if (gen != sequenceGeneration || victim == null || victim.IsAlive) yield break;

        // ── Phase 2: 20-frame 16-bit Ghost Hands crush the camera (~1.43s) ──
        if (sfxSource != null && handGrabClip != null)
        {
            sfxSource.PlayOneShot(handGrabClip, 0.95f);
        }

        yield return StartCoroutine(PlayOverlayClipRoutine(
            "DeathGhostHands",
            ghostHandSprites,
            HandFrameRate,
            HandFrameCount,
            darkenNearEnd: true,
            victim: victim,
            gen: gen));

        if (gen != sequenceGeneration || victim == null || victim.IsAlive) yield break;

        // ── Phase 3: Pitch-black transition & switch Camera to Burial Map ───
        sheetOverlayImage.enabled = false;
        blackoutImage.color = Color.black;

        BurialMapStage stage = BurialMapStage.EnsureAndGet();
        stage.SetStageActive(true);
        stage.SetLungeProgress(0f);

        WeatherManager.SetLocalInBurialMap(true);
        if (CameraFollow.Instance != null)
        {
            CameraFollow.Instance.SetTargetOverride(stage.CameraAnchor);
        }

        if (ambienceSource != null && burialAmbienceClip != null)
        {
            ambienceSource.clip = burialAmbienceClip;
            ambienceSource.volume = 0.72f;
            ambienceSource.Play();
        }

        yield return new WaitForSeconds(0.35f);
        if (gen != sequenceGeneration || victim == null || victim.IsAlive) yield break;

        // Quick eerie reveal of grave pit
        float revealT = 0f;
        const float revealDur = 0.25f;
        while (revealT < revealDur)
        {
            if (gen != sequenceGeneration || victim == null || victim.IsAlive) yield break;
            revealT += Time.deltaTime;
            blackoutImage.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(revealT / revealDur));
            yield return null;
        }
        blackoutImage.color = new Color(0f, 0f, 0f, 0f);

        // ── Phase 4: Tense stillness (0.85s) -> accelerating lunge (0.55s) ─
        yield return new WaitForSeconds(0.85f);
        if (gen != sequenceGeneration || victim == null || victim.IsAlive) yield break;

        float lungeElapsed = 0f;
        const float lungeDur = 0.55f;
        while (lungeElapsed < lungeDur)
        {
            if (gen != sequenceGeneration || victim == null || victim.IsAlive) yield break;
            lungeElapsed += Time.deltaTime;
            stage.SetLungeProgress(lungeElapsed / lungeDur);
            yield return null;
        }
        stage.SetLungeProgress(1f);

        // ── Jumpscare Flash + Shriek (~0.80s) ───────────────────────────────
        if (sfxSource != null && jumpscareClip != null)
        {
            sfxSource.PlayOneShot(jumpscareClip, 1.0f);
        }

        yield return StartCoroutine(PlayOverlayClipRoutine(
            "BurialJumpscare",
            jumpscareSprites,
            JumpscareFrameRate,
            JumpscareFrameCount,
            darkenNearEnd: false,
            victim: victim,
            gen: gen));

        if (gen != sequenceGeneration || victim == null || victim.IsAlive) yield break;

        // ── Phase 5: Instant 0s Hard Cut to Black & Total Silence ───────────
        sheetOverlayImage.enabled = false;
        blackoutImage.color = Color.black;
        if (sfxSource != null) sfxSource.Stop();
        if (ambienceSource != null) ambienceSource.Stop();

        yield return new WaitForSeconds(0.35f);
        if (gen != sequenceGeneration || victim == null || victim.IsAlive) yield break;

        // Return camera & weather state to the neighborhood at the victim's death location
        stage.SetStageActive(false);
        WeatherManager.SetLocalInBurialMap(false);
        if (CameraFollow.Instance != null)
        {
            CameraFollow.Instance.SetTargetOverride(null);
        }

        // Notify server to transition from DyingSequence -> GhostSpectator BEFORE fade clears
        victim.FinishDeathSequenceServerRpc();

        float fadeOutElapsed = 0f;
        const float fadeOutDur = 0.45f;
        while (fadeOutElapsed < fadeOutDur)
        {
            if (gen != sequenceGeneration || victim == null || victim.IsAlive) yield break;
            fadeOutElapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(fadeOutElapsed / fadeOutDur);
            blackoutImage.color = new Color(0f, 0f, 0f, alpha);
            yield return null;
        }

        HideAllVisualsImmediate();
        isPlaying = false;
        activeCoroutine = null;
    }
}
