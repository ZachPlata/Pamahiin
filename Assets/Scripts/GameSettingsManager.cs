using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Singleton that owns every user-configurable setting.
/// Settings are persisted via PlayerPrefs and applied immediately.
/// Survives scene loads (DontDestroyOnLoad).
/// 
/// AudioMixer groups expected:
///   "Master"  "Music"  "SFX"  "Voice"
/// If you have no mixer, the fallback adjusts AudioListener.volume for master.
/// </summary>
public class GameSettingsManager : MonoBehaviour
{
    private static GameSettingsManager _instance;
    public static GameSettingsManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<GameSettingsManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("GameSettingsManager");
                    _instance = go.AddComponent<GameSettingsManager>();
                }
            }
            return _instance;
        }
        private set => _instance = value;
    }

    // ── PlayerPrefs keys ────────────────────────────────────────────────────
    private const string KEY_MASTER  = "vol_master";
    private const string KEY_MUSIC   = "vol_music";
    private const string KEY_SFX     = "vol_sfx";
    private const string KEY_VOICE   = "vol_voice";
    private const string KEY_FULLSCR = "fullscreen";
    private const string KEY_QUALITY = "quality";
    private const string KEY_FPS     = "show_fps";
    private const string KEY_TUTORIAL_ENABLE = "tutorial_enabled";

    // ── Inspector ───────────────────────────────────────────────────────────
    [Header("Audio Mixer (optional but recommended)")]
    [Tooltip("Assign the project's main AudioMixer. Leave empty to use AudioListener fallback.")]
    [SerializeField] private AudioMixer audioMixer;

    // ── Runtime State ────────────────────────────────────────────────────────
    private readonly HashSet<string> seenHintsThisMatch = new HashSet<string>();

    // ── Exposed Properties ──────────────────────────────────────────────────
    public float MasterVolume  { get; private set; }
    public float MusicVolume   { get; private set; }
    public float SFXVolume     { get; private set; }
    public float VoiceVolume   { get; private set; }
    public bool  IsFullscreen  { get; private set; }
    public int   QualityLevel  { get; private set; }
    public bool  ShowFPS       { get; private set; }
    public bool  TutorialHintsEnabled { get; private set; } = true;
    public bool  TutorialHintsRepeatAlways => true;

    // ── Unity Lifecycle ─────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadAll();
    }

    // ── Load & Apply ────────────────────────────────────────────────────────
    private void LoadAll()
    {
        SetMasterVolume (PlayerPrefs.GetFloat(KEY_MASTER,  1f));
        SetMusicVolume  (PlayerPrefs.GetFloat(KEY_MUSIC,   0.8f));
        SetSFXVolume    (PlayerPrefs.GetFloat(KEY_SFX,     1f));
        SetVoiceVolume  (PlayerPrefs.GetFloat(KEY_VOICE,   1f));
        SetFullscreen   (PlayerPrefs.GetInt  (KEY_FULLSCR, Screen.fullScreen ? 1 : 0) == 1);
        SetQuality      (PlayerPrefs.GetInt  (KEY_QUALITY, QualitySettings.GetQualityLevel()));
        SetShowFPS      (PlayerPrefs.GetInt  (KEY_FPS,     0) == 1);
        SetTutorialHintsEnabled(PlayerPrefs.GetInt(KEY_TUTORIAL_ENABLE, 1) == 1);
    }

    // ── Public Setters ──────────────────────────────────────────────────────

    public void SetMasterVolume(float value)
    {
        MasterVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(KEY_MASTER, MasterVolume);
        ApplyMixerVolume("Master", MasterVolume);
        // Fallback when no mixer
        if (audioMixer == null) AudioListener.volume = MasterVolume;
    }

    public void SetMusicVolume(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(KEY_MUSIC, MusicVolume);
        ApplyMixerVolume("Music", MusicVolume);
    }

    public void SetSFXVolume(float value)
    {
        SFXVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(KEY_SFX, SFXVolume);
        ApplyMixerVolume("SFX", SFXVolume);
    }

    public void SetVoiceVolume(float value)
    {
        VoiceVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(KEY_VOICE, VoiceVolume);
        ApplyMixerVolume("Voice", VoiceVolume);
    }

    public void SetFullscreen(bool value)
    {
        IsFullscreen = value;
        PlayerPrefs.SetInt(KEY_FULLSCR, value ? 1 : 0);
        Screen.fullScreen = value;
    }

    public void SetQuality(int level)
    {
        QualityLevel = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
        PlayerPrefs.SetInt(KEY_QUALITY, QualityLevel);
        QualitySettings.SetQualityLevel(QualityLevel, true);
    }

    public void SetShowFPS(bool value)
    {
        ShowFPS = value;
        PlayerPrefs.SetInt(KEY_FPS, value ? 1 : 0);
    }

    public void SetTutorialHintsEnabled(bool value)
    {
        TutorialHintsEnabled = value;
        PlayerPrefs.SetInt(KEY_TUTORIAL_ENABLE, value ? 1 : 0);
    }

    public void SetTutorialHintsRepeatAlways(bool value)
    {
        // Deprecated: tutorial hints are now automatically per-match
    }

    public void ResetMatchTutorialHints()
    {
        seenHintsThisMatch.Clear();
        Debug.Log("[GameSettingsManager] Tutorial hints reset for new match.");
    }

    public bool HasSeenTutorial(string hintKey)
    {
        if (string.IsNullOrEmpty(hintKey)) return false;
        return seenHintsThisMatch.Contains(hintKey);
    }

    public void MarkTutorialSeen(string hintKey)
    {
        if (!string.IsNullOrEmpty(hintKey))
        {
            seenHintsThisMatch.Add(hintKey);
        }
    }

    public void ResetTutorialHistory()
    {
        ResetMatchTutorialHints();
    }

    public void SaveAll() => PlayerPrefs.Save();

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a linear 0-1 value to decibels and sets the exposed mixer parameter.
    /// Expects exposed parameters named exactly "MasterVol", "MusicVol", "SFXVol", "VoiceVol".
    /// </summary>
    private void ApplyMixerVolume(string groupName, float linear)
    {
        if (audioMixer == null) return;
        // -80 dB silence → 0 dB full
        float db = linear > 0.0001f ? Mathf.Log10(linear) * 20f : -80f;
        audioMixer.SetFloat($"{groupName}Vol", db);
    }
}
