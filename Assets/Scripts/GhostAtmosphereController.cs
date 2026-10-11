using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;

/// <summary>
/// Drives all ghost-proximity atmospheric effects for the LOCAL player:
///  - Pulsing vignette that speeds up and intensifies the closer the ghost is during a hunt
///  - Camera orthographic zoom-in (0.10x) while ghost is actively chasing the local player,
///    combined with the pulse effect
///  - Heartbeat sound that gets progressively louder and faster the closer the ghost is,
///    stops the instant the ghost touches (kills) the player
///  - Soft ambient vignette pulse on ghost events (dormant state)
///
/// Attach to the same GameObject as your main Camera, or any persistent Manager.
/// Wire up all Inspector references before entering Play Mode.
/// </summary>
public class GhostAtmosphereController : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────────
    public static GhostAtmosphereController Instance { get; private set; }

    // ── Inspector References ───────────────────────────────────────────────────
    [Header("Scene References")]
    [Tooltip("The URP Volume that contains Vignette (and optionally ChromaticAberration).")]
    [SerializeField] private Volume postProcessVolume;

    [Tooltip("The main camera whose orthographicSize we zoom.")]
    [SerializeField] private Camera mainCamera;

    [Header("Heartbeat Audio")]
    [Tooltip("AudioSource used exclusively for the heartbeat clip.")]
    [SerializeField] private AudioSource heartbeatSource;

    [Tooltip("Heartbeat audio clip (short single-beat, played repeatedly by script).")]
    [SerializeField] private AudioClip heartbeatClip;

    // ── Proximity Settings ─────────────────────────────────────────────────────
    [Header("Proximity Thresholds")]
    [Tooltip("Ghost must be closer than this distance for any effect to start (during hunt).")]
    [SerializeField] private float effectStartDistance = 14f;

    [Tooltip("Distance at which effects are at maximum intensity.")]
    [SerializeField] private float effectMaxDistance = 2f;

    // ── Vignette Pulse ─────────────────────────────────────────────────────────
    [Header("Vignette Pulse")]
    [SerializeField] private float vignetteBaseMin = 0.10f;
    [SerializeField] private float vignetteBaseMax = 0.35f;
    [SerializeField] private float vignetteIntensePeakMin = 0.28f;
    [SerializeField] private float vignetteIntensePeakMax = 0.78f;
    [SerializeField] private float pulseSpeedMin = 0.6f;    // cycles/sec when far
    [SerializeField] private float pulseSpeedMax = 3.5f;    // cycles/sec when very close

    // ── Camera Zoom ────────────────────────────────────────────────────────────
    [Header("Camera Zoom (Chase only)")]
    [Tooltip("How many orthographic units to zoom in when ghost is chasing at max proximity.")]
    [SerializeField] private float chaseZoomAmount = 0.10f;
    [SerializeField] private float zoomLerpSpeed = 3f;

    // ── Heartbeat ──────────────────────────────────────────────────────────────
    [Header("Heartbeat")]
    [SerializeField] private float heartbeatVolumeMin = 0.05f;
    [SerializeField] private float heartbeatVolumeMax = 1.0f;
    [SerializeField] private float heartbeatIntervalFar = 1.4f;    // seconds between beats when far
    [SerializeField] private float heartbeatIntervalClose = 0.28f; // seconds between beats when very close

    // ── Ghost-Event Ambient Effect (Dormant state) ─────────────────────────────
    [Header("Ghost Event Ambient Vignette")]
    [Tooltip("Peak vignette intensity shown briefly when a ghost event fires while dormant.")]
    [SerializeField] private float eventVignetteIntensity = 0.45f;
    [SerializeField] private float eventVignetteDuration = 2.5f;

    // ── Runtime State ──────────────────────────────────────────────────────────
    private Vignette vignette;
    private ChromaticAberration chromaticAberration;

    private float baseOrthoSize;

    private float pulseTimer = 0f;
    private float heartbeatTimer = 0f;

    private bool eventVignetteActive = false;
    private float eventVignetteTimer = 0f;

    private GhostHandler ghostHandler;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera != null)
            baseOrthoSize = mainCamera.orthographicSize;

        if (postProcessVolume != null)
        {
            postProcessVolume.profile.TryGet(out vignette);
            postProcessVolume.profile.TryGet(out chromaticAberration);
        }

        if (heartbeatSource == null)
        {
            heartbeatSource = GetComponent<AudioSource>();
            if (heartbeatSource == null)
            {
                heartbeatSource = gameObject.AddComponent<AudioSource>();
            }
        }

        if (heartbeatSource != null)
        {
            heartbeatSource.loop = false;
            heartbeatSource.playOnAwake = false;
            heartbeatSource.spatialBlend = 0f; // 2D flat sound so player hears it directly
            heartbeatSource.volume = 0f;
        }
    }

    private void Update()
    {
        // Re-discover GhostHandler dynamically (handles late spawning)
        if (ghostHandler == null)
            ghostHandler = FindAnyObjectByType<GhostHandler>();

        PlayerController localPlayer = GetLocalPlayer();
        if (localPlayer == null || !localPlayer.IsAlive)
        {
            SmoothlyClearEffects();
            return;
        }

        GhostHandlerState state = ghostHandler != null
            ? ghostHandler.CurrentState
            : GhostHandlerState.Dormant;

        Vector3 ghostPos = GetActiveGhostPosition(state);
        float distToGhost = (ghostPos != Vector3.zero)
            ? Vector2.Distance(localPlayer.transform.position, ghostPos)
            : float.MaxValue;

        // 0 = ghost far / outside range, 1 = ghost right on top of player
        float proximity = Mathf.InverseLerp(effectStartDistance, effectMaxDistance, distToGhost);

        bool huntActive  = state != GhostHandlerState.Dormant;
        bool chaseActive = state == GhostHandlerState.Chasing;

        UpdateVignette(huntActive, proximity);
        UpdateCameraZoom(chaseActive, proximity);
        UpdateHeartbeat(huntActive, proximity);
        UpdateChromaticAberration(huntActive, chaseActive, proximity);

        // Tick the event-vignette timer
        if (eventVignetteActive)
        {
            eventVignetteTimer -= Time.deltaTime;
            if (eventVignetteTimer <= 0f)
                eventVignetteActive = false;
        }
    }

    // ── Effect Updates ─────────────────────────────────────────────────────────

    private void UpdateVignette(bool huntActive, float proximity)
    {
        if (vignette == null) return;

        float targetIntensity;

        if (huntActive)
        {
            // Hunt pulse: speed and peak both scale with proximity
            float pulseSpeed = Mathf.Lerp(pulseSpeedMin, pulseSpeedMax, proximity);
            pulseTimer += Time.deltaTime * pulseSpeed * Mathf.PI * 2f;

            float pulseSine = (Mathf.Sin(pulseTimer) + 1f) * 0.5f; // range 0-1

            float peakLow  = Mathf.Lerp(vignetteBaseMin,       vignetteIntensePeakMin, proximity);
            float peakHigh = Mathf.Lerp(vignetteBaseMax,       vignetteIntensePeakMax, proximity);

            targetIntensity = Mathf.Lerp(peakLow, peakHigh, pulseSine);
        }
        else if (eventVignetteActive)
        {
            // Soft slow pulse for dormant ghost-event scare
            pulseTimer += Time.deltaTime * 1.4f * Mathf.PI * 2f;
            float pulseSine = (Mathf.Sin(pulseTimer) + 1f) * 0.5f;
            targetIntensity = Mathf.Lerp(vignetteBaseMin, eventVignetteIntensity, pulseSine);
        }
        else
        {
            targetIntensity = vignetteBaseMin;
            pulseTimer = 0f;
        }

        vignette.intensity.value = Mathf.Lerp(
            vignette.intensity.value, targetIntensity, Time.deltaTime * 6f);
    }

    private void UpdateCameraZoom(bool chaseActive, float proximity)
    {
        if (mainCamera == null) return;

        float targetSize = chaseActive
            ? baseOrthoSize - (chaseZoomAmount * proximity)
            : baseOrthoSize;

        mainCamera.orthographicSize = Mathf.Lerp(
            mainCamera.orthographicSize, targetSize, Time.deltaTime * zoomLerpSpeed);
    }

    private void UpdateHeartbeat(bool huntActive, float proximity)
    {
        if (heartbeatSource == null || heartbeatClip == null) return;

        if (!huntActive || proximity <= 0f)
        {
            heartbeatSource.volume = Mathf.Lerp(heartbeatSource.volume, 0f, Time.deltaTime * 3f);
            heartbeatTimer = 0f;
            return;
        }

        float targetVolume = Mathf.Lerp(heartbeatVolumeMin, heartbeatVolumeMax, proximity);
        heartbeatSource.volume = Mathf.Lerp(heartbeatSource.volume, targetVolume, Time.deltaTime * 4f);

        float interval = Mathf.Lerp(heartbeatIntervalFar, heartbeatIntervalClose, proximity);
        heartbeatTimer -= Time.deltaTime;
        if (heartbeatTimer <= 0f)
        {
            heartbeatSource.PlayOneShot(heartbeatClip, heartbeatSource.volume);
            heartbeatTimer = interval;
        }
    }

    private void UpdateChromaticAberration(bool huntActive, bool chaseActive, float proximity)
    {
        if (chromaticAberration == null) return;

        float target = 0f;
        if (chaseActive)
            target = Mathf.Lerp(0f, 0.65f, proximity);
        else if (huntActive)
            target = Mathf.Lerp(0f, 0.22f, proximity);

        chromaticAberration.intensity.value = Mathf.Lerp(
            chromaticAberration.intensity.value, target, Time.deltaTime * 5f);
    }

    private void SmoothlyClearEffects()
    {
        if (vignette != null)
            vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, 0f, Time.deltaTime * 4f);

        if (mainCamera != null)
            mainCamera.orthographicSize = Mathf.Lerp(
                mainCamera.orthographicSize, baseOrthoSize, Time.deltaTime * zoomLerpSpeed);

        if (heartbeatSource != null)
            heartbeatSource.volume = Mathf.Lerp(heartbeatSource.volume, 0f, Time.deltaTime * 5f);

        if (chromaticAberration != null)
            chromaticAberration.intensity.value = Mathf.Lerp(
                chromaticAberration.intensity.value, 0f, Time.deltaTime * 4f);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private PlayerController GetLocalPlayer()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            var obj = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
            return obj != null ? obj.GetComponent<PlayerController>() : null;
        }

        // Offline fallback
        foreach (var p in PlayerController.AllPlayers)
            if (p != null && p.IsAlive) return p;

        return null;
    }

    private Vector3 GetActiveGhostPosition(GhostHandlerState state)
    {
        if (ghostHandler == null) return Vector3.zero;

        if (state == GhostHandlerState.Dormant)
        {
            // Ghost events briefly show the event ghost
            return ghostHandler.eventGhost != null
                ? ghostHandler.eventGhost.transform.position
                : Vector3.zero;
        }

        // Hunt phase — use the hunter ghost
        return ghostHandler.hunterGhost != null
            ? ghostHandler.hunterGhost.transform.position
            : Vector3.zero;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Call this from GhostHandler.TriggerGhostEvent() to show a brief ambient
    /// vignette scare when a ghost event fires in the dormant state.
    /// </summary>
    public void TriggerGhostEventEffect()
    {
        eventVignetteActive = true;
        eventVignetteTimer  = eventVignetteDuration;
    }

    /// <summary>
    /// Immediately silence the heartbeat and clear effects — called the instant
    /// the ghost kills the local player.
    /// </summary>
    public void OnPlayerCaught()
    {
        if (heartbeatSource != null)
        {
            heartbeatSource.Stop();
            heartbeatSource.volume = 0f;
        }
        heartbeatTimer = 0f;
        SmoothlyClearEffects();
    }
}
