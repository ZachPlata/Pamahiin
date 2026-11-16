using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Fuse Box acts as a middleman between players and the house light system.
/// Light switches will not emit light unless the fuse box is enabled.
/// When the fuse box is turned off or tripped, switches retain their ON/OFF status
/// and temporarily cut power to their light sources until power is restored.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class FuseBox : NetworkBehaviour, IInteractable
{
    [Header("Fuse Box Settings")]
    [Tooltip("Maximum number of lights allowed on before the fuse box becomes overloaded. " +
             "Set to 0 to disable the overload mechanic.")]
    [SerializeField] private int maxLightsBeforeOverload = 5;

    [Header("Audio")]
    [Tooltip("Optional looping hum clip while the fuse box is ON. Falls back to Resources/Audio/fusebox_hum or a generated 60Hz hum.")]
    [SerializeField] private AudioClip humLoopClip;
    [SerializeField] private float humVolume = 0.45f;
    [SerializeField] private float humMinDistance = 0.45f;
    [SerializeField] private float humMaxDistance = 3.2f;

    private NetworkVariable<bool> isEnabled = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private AudioSource humAudioSource;
    private AudioClip fallbackHumClip;

    /// <summary>Fired whenever the fuse box power state changes on server or clients.</summary>
    public static event System.Action<bool> OnPowerStateChanged;

    /// <summary>Whether the fuse box is currently supplying power to the house.</summary>
    public bool IsEnabled => isEnabled.Value;

    public static FuseBox Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("FuseBox: Multiple FuseBox instances detected! Using the latest one.");
        }
        Instance = this;
        EnsureHumAudioSource();
    }

    public override void OnNetworkSpawn()
    {
        EnsureHumAudioSource();
        isEnabled.OnValueChanged += HandleEnabledChanged;

        if (IsServer)
        {
            ContractDifficulty diff = ProgressionAndLoadoutManager.Instance != null
                ? ProgressionAndLoadoutManager.Instance.SelectedDifficulty
                : ContractDifficulty.Amateur;
            isEnabled.Value = ProgressionAndLoadoutManager.GetDifficultyFuseBoxStartsOn(diff);
        }

        OnFuseStateChanged(isEnabled.Value);
    }

    public override void OnNetworkDespawn()
    {
        isEnabled.OnValueChanged -= HandleEnabledChanged;
        UpdateHumAudioState(false);
        if (Instance == this) Instance = null;
    }

    private void OnDisable()
    {
        UpdateHumAudioState(false);
    }

    private void Update()
    {
        if (!IsSpawned || !isEnabled.Value || humAudioSource == null) return;

        UpdateHumAudioSourceVolumeAndZ();
        if (!humAudioSource.isPlaying && humAudioSource.clip != null)
        {
            humAudioSource.Play();
        }
    }

    private void HandleEnabledChanged(bool oldVal, bool newVal)
    {
        if (oldVal != newVal)
        {
            GameAudioManager.PlayFuseBox(transform.position, newVal);
        }
        OnFuseStateChanged(newVal);
    }

    // --- IInteractable ---

    public void Interact()
    {
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
        ToggleFuseBoxRpc();
    }

    public string GetInteractText()
    {
        return isEnabled.Value ? "Turn Off Fuse Box" : "Turn On Fuse Box";
    }

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPosition) { }
    public void OnDragEnd(ulong clientId) { }

    // --- Core Logic ---

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleFuseBoxRpc(RpcParams rpcParams = default)
    {
        if (PlayerController.TryGetByClientId(rpcParams.Receive.SenderClientId, out var sender) && !sender.IsAlive)
        {
            return;
        }
        isEnabled.Value = !isEnabled.Value;
        Debug.Log($"FuseBox toggled to: {isEnabled.Value}");
    }

    /// <summary>
    /// Called by the ghost during a hunt to trip the fuse box.
    /// </summary>
    public void GhostTrip()
    {
        if (!IsServer) return;
        if (!isEnabled.Value) return; // Already tripped

        isEnabled.Value = false;
        Debug.Log("FuseBox: Ghost tripped the fuse box!");

        if (ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(transform.position, 3, 15f);
        }
    }

    /// <summary>
    /// Returns true if the fuse box is currently overloaded (too many lights on).
    /// </summary>
    public bool IsOverloaded()
    {
        if (maxLightsBeforeOverload <= 0) return false;

        int activeLightCount = 0;
        var switches = Object.FindObjectsByType<HouseLightSwitch>(FindObjectsInactive.Exclude);
        foreach (var sw in switches)
        {
            if (sw.IsOn) activeLightCount++;
        }

        return activeLightCount >= maxLightsBeforeOverload;
    }

    /// <summary>
    /// Returns the number of currently active light switches.
    /// </summary>
    public int GetActiveLightCount()
    {
        int count = 0;
        var switches = Object.FindObjectsByType<HouseLightSwitch>(FindObjectsInactive.Exclude);
        foreach (var sw in switches)
        {
            if (sw.IsOn) count++;
        }
        return count;
    }

    private void OnFuseStateChanged(bool newState)
    {
        UpdateHumAudioState(newState);
        OnPowerStateChanged?.Invoke(newState);

        var switches = Object.FindObjectsByType<HouseLightSwitch>(FindObjectsInactive.Exclude);
        foreach (var sw in switches)
        {
            if (sw != null) sw.RefreshLightState();
        }
    }

    private void EnsureHumAudioSource()
    {
        if (humAudioSource != null) return;

        Transform child = transform.Find("FuseBoxHumAudio");
        if (child != null)
        {
            humAudioSource = child.GetComponent<AudioSource>();
        }

        if (humAudioSource == null)
        {
            var audioGo = new GameObject("FuseBoxHumAudio");
            audioGo.transform.SetParent(transform, false);
            humAudioSource = audioGo.AddComponent<AudioSource>();
        }

        humAudioSource.playOnAwake = false;
        humAudioSource.loop = true;
        GameAudioManager.ConfigureProximityAudioSource(humAudioSource, humMinDistance, humMaxDistance, 1.0f);
    }

    private AudioClip ResolveHumClip()
    {
        if (humLoopClip != null) return humLoopClip;

        AudioClip loaded = GameAudioManager.GetClip("fusebox_hum");
        if (loaded != null)
        {
            humLoopClip = loaded;
            return humLoopClip;
        }

        if (fallbackHumClip == null)
        {
            const int sampleRate = 22050;
            const int sampleCount = sampleRate * 2;
            float[] samples = new float[sampleCount];
            float twoPi = Mathf.PI * 2f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                float mod = 0.92f + 0.08f * Mathf.Sin(twoPi * 1f * t);
                samples[i] = (0.52f * Mathf.Sin(twoPi * 60f * t) +
                              0.30f * Mathf.Sin(twoPi * 120f * t) +
                              0.12f * Mathf.Sin(twoPi * 180f * t) +
                              0.06f * Mathf.Sin(twoPi * 240f * t)) * mod * 0.32f;
            }
            fallbackHumClip = AudioClip.Create("fusebox_hum_fallback", sampleCount, 1, sampleRate, false);
            fallbackHumClip.SetData(samples, 0);
        }

        return fallbackHumClip;
    }

    private void UpdateHumAudioState(bool poweredOn)
    {
        if (humAudioSource == null)
        {
            if (!poweredOn) return;
            EnsureHumAudioSource();
            if (humAudioSource == null) return;
        }

        if (poweredOn)
        {
            AudioClip clip = ResolveHumClip();
            if (clip != null)
            {
                humAudioSource.clip = clip;
                humAudioSource.loop = true;
                UpdateHumAudioSourceVolumeAndZ();
                if (!humAudioSource.isPlaying)
                {
                    humAudioSource.Play();
                }
            }
        }
        else if (humAudioSource.isPlaying)
        {
            humAudioSource.Stop();
        }
    }

    private void UpdateHumAudioSourceVolumeAndZ()
    {
        if (humAudioSource == null) return;

        Vector3 pos = transform.position;
        humAudioSource.transform.position = new Vector3(pos.x, pos.y, GameAudioManager.GetActiveListenerZ());

        float floorMult = GameAudioManager.GetFloorAttenuation(pos, 0.12f);
        float clarityMult = WeatherManager.GetAudioClarityMultiplier(pos);
        humAudioSource.volume = GameAudioManager.GetEffectiveSfxVolume(humVolume * floorMult * clarityMult);
    }
}
