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

    private NetworkVariable<bool> isPoweredOn = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsPoweredOn => isPoweredOn.Value;
    public float CurrentTemperature { get; private set; } = 20.0f;
    public bool IsFreezing => CurrentTemperature < 0.0f;

    private float nextSampleTime = 0f;
    private float displayedTemperature = 20.0f;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Thermometer";
        if (digitalDisplay == null) digitalDisplay = GetComponentInChildren<TMP_Text>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isPoweredOn.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
        UpdateVisuals();
    }

    public override void UsePrimary()
    {
        TogglePowerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TogglePowerRpc()
    {
        isPoweredOn.Value = !isPoweredOn.Value;
    }

    protected override void Update()
    {
        base.Update();

        if (!isPoweredOn.Value)
        {
            if (digitalDisplay != null) digitalDisplay.text = "--.-°C";
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
            displayedTemperature = Mathf.MoveTowards(displayedTemperature, CurrentTemperature, Time.deltaTime * 5f);
            if (digitalDisplay != null)
            {
                digitalDisplay.text = $"{displayedTemperature:F1}°C";
                digitalDisplay.color = displayedTemperature < 0f ? Color.cyan : Color.white;
            }

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

        // Project forward from the player's aim direction
        Vector2 samplePoint = (Vector2)transform.position + ((Vector2)transform.up * sampleDistance);
        CurrentTemperature = ParanormalManager.Instance.GetTemperatureAt(samplePoint);
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
            digitalDisplay.gameObject.SetActive(isVisible && isPoweredOn.Value);
        }
    }
}
