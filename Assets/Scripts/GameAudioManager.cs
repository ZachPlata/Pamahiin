using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Centralized 16-bit Sound Effects & Audio Manager.
/// Automatically loads retro 16-bit WAV/MP3 clips from Resources/Audio and wires UI,
/// equipment, door, fusebox, and ghost sound effects across all scenes.
/// </summary>
public class GameAudioManager : MonoBehaviour
{
    public static GameAudioManager Instance { get; private set; }

    private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
    private readonly HashSet<Button> wiredButtons = new HashSet<Button>();
    private AudioSource uiAudioSource;
    private AudioListener cachedAudioListener;
    private float nextUiScanTime = 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        EnsureInstance();
    }

    public static GameAudioManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        var existing = FindAnyObjectByType<GameAudioManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        var go = new GameObject("GameAudioManager");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<GameAudioManager>();
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
        DontDestroyOnLoad(gameObject);

        uiAudioSource = gameObject.AddComponent<AudioSource>();
        uiAudioSource.playOnAwake = false;
        uiAudioSource.spatialBlend = 0f;

        SceneManager.sceneLoaded += OnSceneLoaded;
        RefreshSceneBasementFlag();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        wiredButtons.Clear();
        cachedAudioListener = null;
        nextUiScanTime = 0f;
        RefreshSceneBasementFlag();
        PixelScenePostProcessor.ApplyPixelArtToScene();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextUiScanTime)
        {
            nextUiScanTime = Time.unscaledTime + 3.5f;
            WireAllUIButtons();
        }
    }

    private void WireAllUIButtons()
    {
        var docs = FindObjectsByType<UIDocument>();
        foreach (var doc in docs)
        {
            if (doc == null || doc.rootVisualElement == null) continue;
            var buttons = doc.rootVisualElement.Query<Button>().ToList();
            foreach (var btn in buttons)
            {
                if (btn != null && wiredButtons.Add(btn))
                {
                    btn.RegisterCallback<ClickEvent>(_ => PlayUIClick(), TrickleDown.TrickleDown);
                }
            }
        }
    }

    public const float BASEMENT_Y_THRESHOLD = -3.2f;
    public const float DEFAULT_MIN_DISTANCE = 0.45f;
    public const float DEFAULT_MAX_DISTANCE = 3.2f;

    public static bool HasSceneBasement { get; private set; }

    public static void RefreshSceneBasementFlag()
    {
        HasSceneBasement = FindAnyObjectByType<StairTeleporter>(FindObjectsInactive.Include) != null;
    }

    public static bool IsBasementPosition(Vector3 worldPos) => HasSceneBasement && worldPos.y < BASEMENT_Y_THRESHOLD;

    public static float GetEffectiveSfxVolume(float baseVolume = 1f)
    {
        float sfx = GameSettingsManager.Instance != null ? GameSettingsManager.Instance.SFXVolume : 0.8f;
        float master = GameSettingsManager.Instance != null ? GameSettingsManager.Instance.MasterVolume : 0.8f;
        return Mathf.Clamp01(baseVolume * sfx * master);
    }

    public static bool TryGetListenerWorldPosition(out Vector3 listenerPos)
    {
        if (WeatherManager.IsLocalInBurialMap && Camera.main != null)
        {
            var camPos = Camera.main.transform.position;
            listenerPos = new Vector3(camPos.x, camPos.y, 0f);
            return true;
        }

        if (PlayerController.LocalInstance != null)
        {
            var localPos = PlayerController.LocalInstance.transform.position;
            listenerPos = new Vector3(localPos.x, localPos.y, 0f);
            return true;
        }

        if (Camera.main != null)
        {
            var camPos = Camera.main.transform.position;
            listenerPos = new Vector3(camPos.x, camPos.y, 0f);
            return true;
        }

        if (Instance != null)
        {
            if (Instance.cachedAudioListener == null || !Instance.cachedAudioListener.isActiveAndEnabled)
            {
                Instance.cachedAudioListener = FindAnyObjectByType<AudioListener>();
            }
            if (Instance.cachedAudioListener != null)
            {
                listenerPos = Instance.cachedAudioListener.transform.position;
                return true;
            }
        }
        else
        {
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener != null)
            {
                listenerPos = listener.transform.position;
                return true;
            }
        }

        listenerPos = Vector3.zero;
        return false;
    }

    public static float GetActiveListenerZ()
    {
        if (Instance != null)
        {
            if (Instance.cachedAudioListener == null || !Instance.cachedAudioListener.isActiveAndEnabled)
            {
                Instance.cachedAudioListener = FindAnyObjectByType<AudioListener>();
            }
            return Instance.cachedAudioListener != null ? Instance.cachedAudioListener.transform.position.z : 0f;
        }
        var listener = FindAnyObjectByType<AudioListener>();
        return listener != null ? listener.transform.position.z : 0f;
    }

    public static float GetFloorAttenuation(Vector3 soundPos, float crossFloorMultiplier = 0.08f)
    {
        if (!TryGetListenerWorldPosition(out Vector3 listenerPos)) return 1f;
        return IsBasementPosition(soundPos) != IsBasementPosition(listenerPos) ? crossFloorMultiplier : 1f;
    }

    public static void ConfigureProximityAudioSource(AudioSource src, float minDistance = DEFAULT_MIN_DISTANCE, float maxDistance = DEFAULT_MAX_DISTANCE, float spatialBlend = 1.0f)
    {
        if (src == null) return;
        src.spatialBlend = Mathf.Clamp01(spatialBlend);
        src.minDistance = Mathf.Max(0.05f, minDistance);
        src.maxDistance = Mathf.Max(src.minDistance + 0.1f, maxDistance);
        src.rolloffMode = AudioRolloffMode.Linear;
        src.dopplerLevel = 0f;
    }

    public static AudioClip GetClip(string clipName)
    {
        EnsureInstance();
        if (Instance.clipCache.TryGetValue(clipName, out var cached) && cached != null)
        {
            return cached;
        }

        var loaded = Resources.Load<AudioClip>($"Audio/{clipName}");
        if (loaded != null)
        {
            Instance.clipCache[clipName] = loaded;
            return loaded;
        }
        return null;
    }

    public static void PlayUIClick()
    {
        EnsureInstance();
        var clip = GetClip("ui_click");
        if (clip != null && Instance.uiAudioSource != null)
        {
            Instance.uiAudioSource.PlayOneShot(clip, GetEffectiveSfxVolume(0.7f));
        }
    }

    public static void PlayAt(string clipName, Vector3 position, float volume = 0.85f, float spatialBlend = 1.0f, float pitch = 1f)
    {
        PlayAtReturnDuration(clipName, position, volume, spatialBlend, pitch, DEFAULT_MIN_DISTANCE, DEFAULT_MAX_DISTANCE);
    }

    public static void PlayAt(string clipName, Vector3 position, float volume, float spatialBlend, float pitch, float minDistance, float maxDistance, float crossFloorMultiplier = 0.08f)
    {
        PlayAtReturnDuration(clipName, position, volume, spatialBlend, pitch, minDistance, maxDistance, crossFloorMultiplier);
    }

    public static float PlayAtReturnDuration(string clipName, Vector3 position, float volume = 0.85f, float spatialBlend = 1.0f, float pitch = 1f)
    {
        return PlayAtReturnDuration(clipName, position, volume, spatialBlend, pitch, DEFAULT_MIN_DISTANCE, DEFAULT_MAX_DISTANCE);
    }

    public static float PlayAtReturnDuration(string clipName, Vector3 position, float volume, float spatialBlend, float pitch, float minDistance, float maxDistance, float crossFloorMultiplier = 0.08f)
    {
        var clip = GetClip(clipName);
        return PlayClipAtReturnDuration(clip, position, volume, spatialBlend, pitch, minDistance, maxDistance, crossFloorMultiplier, clipName);
    }

    public static void PlayClipAt(AudioClip clip, Vector3 position, float volume = 0.85f, float spatialBlend = 1.0f, float pitch = 1f, float minDistance = DEFAULT_MIN_DISTANCE, float maxDistance = DEFAULT_MAX_DISTANCE, float crossFloorMultiplier = 0.08f)
    {
        PlayClipAtReturnDuration(clip, position, volume, spatialBlend, pitch, minDistance, maxDistance, crossFloorMultiplier);
    }

    public static float PlayClipAtReturnDuration(AudioClip clip, Vector3 position, float volume = 0.85f, float spatialBlend = 1.0f, float pitch = 1f, float minDistance = DEFAULT_MIN_DISTANCE, float maxDistance = DEFAULT_MAX_DISTANCE, float crossFloorMultiplier = 0.08f, string label = null)
    {
        if (clip == null) return 0f;

        float duration = clip.length / Mathf.Max(0.1f, Mathf.Abs(pitch));
        float floorMult = crossFloorMultiplier < 1f ? GetFloorAttenuation(position, crossFloorMultiplier) : 1f;
        float scaledVolume = volume * floorMult * WeatherManager.GetAudioClarityMultiplier(position);
        if (scaledVolume <= 0.005f) return duration;

        if (spatialBlend >= 0.99f && TryGetListenerWorldPosition(out Vector3 listenerPos))
        {
            float distXY = Vector2.Distance(new Vector2(position.x, position.y), new Vector2(listenerPos.x, listenerPos.y));
            if (distXY > maxDistance + 0.15f)
            {
                return duration;
            }
        }

        string goName = !string.IsNullOrEmpty(label) ? $"SFX_{label}" : $"SFX_{clip.name}";
        GameObject tempGo = new GameObject(goName);
        tempGo.transform.position = new Vector3(position.x, position.y, GetActiveListenerZ());
        var src = tempGo.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = GetEffectiveSfxVolume(scaledVolume);
        ConfigureProximityAudioSource(src, minDistance, maxDistance, spatialBlend);
        src.pitch = pitch;
        src.Play();
        Destroy(tempGo, duration + 0.1f);
        return duration;
    }

    public static void PlayFlashlightClick(Vector3 pos) => PlayAt("flashlight_click", pos, 0.75f, 1.0f, Random.Range(0.95f, 1.05f), 0.4f, 2.4f, 0f);
    public static void PlayLightSwitch(Vector3 pos) => PlayAt("light_switch", pos, 0.8f, 1.0f, Random.Range(0.95f, 1.05f), 0.4f, 2.8f, 0.05f);
    public static void PlayItemPickup(Vector3 pos) => PlayAt("item_pickup", pos, 0.75f, 1.0f, Random.Range(0.95f, 1.06f), 0.4f, 2.4f, 0f);
    public static void PlayItemDrop(Vector3 pos) => PlayAt("item_drop", pos, 0.8f, 1.0f, Random.Range(0.92f, 1.05f), 0.4f, 2.6f, 0f);
    public static void PlayItemPlace(Vector3 pos) => PlayAt("item_place", pos, 0.75f, 1.0f, Random.Range(0.95f, 1.05f), 0.4f, 2.4f, 0f);
    public static void PlayDoorOpen(Vector3 pos) => PlayAt("door_open", pos, 0.8f, 1.0f, Random.Range(0.92f, 1.05f), 0.45f, 3.2f, 0.06f);
    public static void PlayDoorClose(Vector3 pos) => PlayAt("door_close", pos, 0.85f, 1.0f, Random.Range(0.94f, 1.04f), 0.45f, 3.2f, 0.06f);
    public static void PlayDoorLocked(Vector3 pos) => PlayAt("door_locked", pos, 0.85f, 1.0f, Random.Range(0.95f, 1.05f), 0.45f, 3.2f, 0.06f);
    public static void PlayFuseBox(Vector3 pos, bool on) => PlayAt(on ? "fusebox_on" : "fusebox_trip", pos, 0.9f, 1.0f, 1f, 0.5f, 3.8f, 0.12f);
    public static void PlayCrucifixBurn(Vector3 pos) => PlayAt("crucifix_burn", pos, 0.95f, 1.0f, 1f, 0.5f, 4.2f, 0.18f);
    public static void PlayChalkDraw(Vector3 pos) => PlayAt("chalk_draw", pos, 0.85f, 1.0f, 1f, 0.4f, 2.6f, 0f);
    public static void PlayGhostEvent(Vector3 pos) => PlayAt("ghost_event", pos, 0.9f, 1.0f, 1f, 0.55f, 4.5f, 0.22f);
    public static void PlayGhostHuntStart(Vector3 pos) => PlayAt("ghost_hunt_start", pos, 0.95f, 0.92f, 1f, 0.8f, 6.5f, 0.45f);
    public static void PlayGhostFootstep(Vector3 pos) => PlayAt("ghost_footstep", pos, 0.75f, 1.0f, Random.Range(0.88f, 1.08f), 0.45f, 3.4f, 0.10f);
    public static void PlayGhostKill(Vector3 pos) => PlayAt("ghost_kill", pos, 0.95f, 0.9f, 1f, 0.8f, 6.5f, 0.40f);
    public static void PlayThermometerBeep(Vector3 pos) => PlayAt("thermometer_beep", pos, 0.65f, 1.0f, 1f, 0.4f, 2.2f, 0f);
    public static float PlayTruckArriveStop(Vector3 pos) => PlayAtReturnDuration("truck_arrive_stop", pos, 0.9f, 1.0f, 1f, 0.8f, 5.5f, 0f);
    public static float PlayTruckStartDepart(Vector3 pos) => PlayAtReturnDuration("truck_start_depart", pos, 0.95f, 1.0f, 1f, 0.8f, 5.5f, 0f);

    public static AudioClip GetSpiritBoxVoiceClip(int questionNumber)
    {
        int clamped = Mathf.Clamp(questionNumber, 1, 5);
        return GetClip($"spiritbox_voice_{clamped}");
    }
}

