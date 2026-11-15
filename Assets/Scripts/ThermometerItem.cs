using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Digital Thermometer equipment.
/// Samples localized room temperatures via forward ray/cone.
/// Detects ghost favorite room (low temps) and sub-zero Freezing Temperatures evidence.
/// </summary>
public class ThermometerItem : EquipmentItem
{
    [Header("Thermometer Settings")]
    [SerializeField] private float sampleDistance = 2.5f;
    [SerializeField] private float refreshInterval = 0.75f;
    [SerializeField] private TMP_Text digitalDisplay;
    [SerializeField] private ParticleSystem freezingBreathEffect;

    public float SampleDistance => sampleDistance;

    private NetworkVariable<bool> isPoweredOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool localFallbackPower = false;

    private bool UseNetworkPower => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    public override bool IsPoweredOn => UseNetworkPower ? isPoweredOn.Value : localFallbackPower;
    public float CurrentTemperature { get; private set; } = 20.0f;
    public bool IsFreezing => CurrentTemperature < 0.0f;

    private float nextSampleTime = 0f;
    private float displayedTemperature = 20.0f;
    private int lastDisplayedTempTenths = int.MinValue;
    private bool lastDisplayedFreezing = false;
    private bool wasOffDisplay = false;
    
    public float DisplayedTemperature => displayedTemperature;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Thermometer";
        localFallbackPower = false;
        if (digitalDisplay == null) digitalDisplay = GetComponentInChildren<TMP_Text>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isPoweredOn.OnValueChanged += (oldVal, newVal) =>
        {
            if (oldVal != newVal) GameAudioManager.PlayFlashlightClick(transform.position);
            UpdateVisuals();
        };
        localFallbackPower = false;
        UpdateVisuals();
    }

    private void Start()
    {
        if (!UseNetworkPower) localFallbackPower = false;
        UpdateVisuals();
    }

    public override void UsePrimary()
    {
        if (UseNetworkPower)
        {
            TogglePowerRpc();
        }
        else
        {
            localFallbackPower = !localFallbackPower;
            GameAudioManager.PlayFlashlightClick(transform.position);
            UpdateVisuals();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TogglePowerRpc()
    {
        isPoweredOn.Value = !isPoweredOn.Value;
    }

    protected override void Update()
    {
        base.Update();

        if (!IsPoweredOn)
        {
            if (!wasOffDisplay)
            {
                wasOffDisplay = true;
                lastDisplayedTempTenths = int.MinValue;
                if (digitalDisplay != null) digitalDisplay.text = "--.-°C";
            }
            return;
        }

        if (isInHand.Value || IsOnGround)
        {
            if (Time.time >= nextSampleTime)
            {
                nextSampleTime = Time.time + refreshInterval;
                SampleTemperature();
            }

            // Smooth interpolation of display
            displayedTemperature = Mathf.MoveTowards(displayedTemperature, CurrentTemperature, Time.deltaTime * 2.5f);
            if (digitalDisplay != null)
            {
                int tempTenths = Mathf.RoundToInt(displayedTemperature * 10f);
                if (wasOffDisplay || tempTenths != lastDisplayedTempTenths)
                {
                    lastDisplayedTempTenths = tempTenths;
                    digitalDisplay.text = $"{tempTenths * 0.1f:F1}°C";
                }

                bool isFreezing = displayedTemperature < 0f;
                if (wasOffDisplay || isFreezing != lastDisplayedFreezing)
                {
                    lastDisplayedFreezing = isFreezing;
                    digitalDisplay.color = isFreezing ? Color.cyan : Color.white;
                }
            }
            wasOffDisplay = false;

            // Cold breath effect check when held by local player
            if (isInHand.Value && ownerClientId.Value == NetworkManager.Singleton?.LocalClientId)
            {
                HandleColdBreath(displayedTemperature < 0f);
            }
        }
    }

    private void SampleTemperature()
    {
        if (ParanormalManager.Instance == null)
        {
            CurrentTemperature = 20.0f;
            return;
        }

        // Sample temperature exactly where the thermometer is located
        CurrentTemperature = ParanormalManager.Instance.GetTemperatureAt(transform.position);
    }

    private void HandleColdBreath(bool isFreezing)
    {
        if (freezingBreathEffect == null) return;

        if (isFreezing && !freezingBreathEffect.isPlaying)
        {
            freezingBreathEffect.Play();
        }
        else if (!isFreezing && freezingBreathEffect.isPlaying)
        {
            freezingBreathEffect.Stop();
        }
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();
        bool isVisible = isInHand.Value || IsOnGround;

        if (digitalDisplay != null)
        {
            digitalDisplay.gameObject.SetActive(isVisible && IsPoweredOn);
        }
    }
}
