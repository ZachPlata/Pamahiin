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
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        wiredButtons.Clear();
        nextUiScanTime = 0f;
        PixelScenePostProcessor.ApplyPixelArtToScene();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextUiScanTime)
        {
            nextUiScanTime = Time.unscaledTime + 1.0f;
            WireAllUIButtons();
        }
    }

    private void WireAllUIButtons()
    {
        var docs = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
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

    private static float GetEffectiveSfxVolume(float baseVolume = 1f)
    {
        float sfx = GameSettingsManager.Instance != null ? GameSettingsManager.Instance.SFXVolume : 0.8f;
        float master = GameSettingsManager.Instance != null ? GameSettingsManager.Instance.MasterVolume : 0.8f;
        return Mathf.Clamp01(baseVolume * sfx * master);
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

    public static void PlayAt(string clipName, Vector3 position, float volume = 0.85f, float spatialBlend = 0.75f, float pitch = 1f)
    {
        var clip = GetClip(clipName);
        if (clip == null) return;

        GameObject tempGo = new GameObject($"SFX_{clipName}");
        tempGo.transform.position = position;
        var src = tempGo.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = GetEffectiveSfxVolume(volume);
        src.spatialBlend = spatialBlend;
        src.minDistance = 1.5f;
        src.maxDistance = 12f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.pitch = pitch;
        src.Play();
        Destroy(tempGo, (clip.length / Mathf.Max(0.1f, Mathf.Abs(pitch))) + 0.1f);
    }

    public static void PlayFlashlightClick(Vector3 pos) => PlayAt("flashlight_click", pos, 0.8f, 0.6f, Random.Range(0.95f, 1.05f));
    public static void PlayLightSwitch(Vector3 pos) => PlayAt("light_switch", pos, 0.85f, 0.7f, Random.Range(0.95f, 1.05f));
    public static void PlayItemPickup(Vector3 pos) => PlayAt("item_pickup", pos, 0.8f, 0.5f, Random.Range(0.95f, 1.06f));
    public static void PlayItemDrop(Vector3 pos) => PlayAt("item_drop", pos, 0.85f, 0.65f, Random.Range(0.92f, 1.05f));
    public static void PlayItemPlace(Vector3 pos) => PlayAt("item_place", pos, 0.8f, 0.6f, Random.Range(0.95f, 1.05f));
    public static void PlayDoorOpen(Vector3 pos) => PlayAt("door_open", pos, 0.85f, 0.8f, Random.Range(0.92f, 1.05f));
    public static void PlayDoorClose(Vector3 pos) => PlayAt("door_close", pos, 0.9f, 0.8f, Random.Range(0.94f, 1.04f));
    public static void PlayDoorLocked(Vector3 pos) => PlayAt("door_locked", pos, 0.9f, 0.8f, Random.Range(0.95f, 1.05f));
    public static void PlayFuseBox(Vector3 pos, bool on) => PlayAt(on ? "fusebox_on" : "fusebox_trip", pos, 0.95f, 0.8f);
    public static void PlayCrucifixBurn(Vector3 pos) => PlayAt("crucifix_burn", pos, 1.0f, 0.5f);
    public static void PlayChalkDraw(Vector3 pos) => PlayAt("chalk_draw", pos, 0.9f, 0.6f);
    public static void PlayGhostEvent(Vector3 pos) => PlayAt("ghost_event", pos, 0.95f, 0.5f);
    public static void PlayGhostHuntStart(Vector3 pos) => PlayAt("ghost_hunt_start", pos, 1.0f, 0.35f);
    public static void PlayGhostFootstep(Vector3 pos) => PlayAt("ghost_footstep", pos, 0.75f, 0.85f, Random.Range(0.88f, 1.08f));
    public static void PlayGhostKill(Vector3 pos) => PlayAt("ghost_kill", pos, 1.0f, 0.1f);
    public static void PlayThermometerBeep(Vector3 pos) => PlayAt("thermometer_beep", pos, 0.65f, 0.5f);

    public static AudioClip GetSpiritBoxVoiceClip(int questionNumber)
    {
        int clamped = Mathf.Clamp(questionNumber, 1, 5);
        return GetClip($"spiritbox_voice_{clamped}");
    }
}

