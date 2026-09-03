using Unity.Netcode;
using UnityEngine;

/// <summary>
/// EMF Reader equipment.
/// Detects paranormal events (doors moved, thrown objects, ghost sightings, and EMF 5 evidence).
/// Features 5-level LED status, directional sonar guidance, and audio beeping.
/// </summary>
public class EMFReaderItem : EquipmentItem
{
    [Header("EMF Settings")]
    [SerializeField] private float scanRadius = 6.0f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip beepSound;

    [Header("Visual Indicators")]
    [SerializeField] private SpriteRenderer[] ledRenderers = new SpriteRenderer[5];
    [SerializeField] private Transform directionalPointer; // Sonar blip / needle pointing to source

    private static readonly Color ColorOff = new Color(0.1f, 0.1f, 0.1f, 0.5f);
    private static readonly Color[] LedColors = new Color[5]
    {
        Color.green,               // EMF 1: Power/Ambient
        Color.green,               // EMF 2: Door / Switch interaction
        Color.yellow,              // EMF 3: Thrown object
        new Color(1f, 0.5f, 0f),   // EMF 4: Ghost presence
        Color.red                  // EMF 5: Definitive Evidence
    };

    private NetworkVariable<bool> isPoweredOn = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsPoweredOn => isPoweredOn.Value;
    public int CurrentEmfLevel { get; private set; } = 1;

    private float nextBeepTime = 0f;

    protected override void Awake()
    {
        base.Awake();
        itemName = "EMF Reader";
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
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
            CurrentEmfLevel = 0;
            UpdateLeds(0);
            if (directionalPointer != null) directionalPointer.gameObject.SetActive(false);
            return;
        }

        // Only scan when held in hand or active on ground
        if (isInHand.Value || IsOnGround)
        {
            ScanForParanormalActivity();
            HandleAudioFeedback();
        }
    }

    private void ScanForParanormalActivity()
    {
        if (ParanormalManager.Instance == null)
        {
            CurrentEmfLevel = 1;
            UpdateLeds(1);
            return;
        }

        int detectedLevel = ParanormalManager.Instance.GetHighestEmfAt(
            transform.position,
            scanRadius,
            out Vector2 sourceDirection,
            out float distance);

        CurrentEmfLevel = detectedLevel;
        UpdateLeds(CurrentEmfLevel);

        // Update directional sonar pointer
        if (directionalPointer != null)
        {
            if (CurrentEmfLevel > 1 && sourceDirection != Vector2.zero)
            {
                directionalPointer.gameObject.SetActive(true);
                float angle = Mathf.Atan2(sourceDirection.y, sourceDirection.x) * Mathf.Rad2Deg - 90f;
                directionalPointer.rotation = Quaternion.Euler(0, 0, angle);
            }
            else
            {
                directionalPointer.gameObject.SetActive(false);
            }
        }
    }

    private void UpdateLeds(int level)
    {
        if (ledRenderers == null || ledRenderers.Length == 0) return;

        for (int i = 0; i < ledRenderers.Length; i++)
        {
            if (ledRenderers[i] == null) continue;

            if (i < level)
            {
                ledRenderers[i].color = LedColors[i];
            }
            else
            {
                ledRenderers[i].color = ColorOff;
            }
        }
    }

    private void HandleAudioFeedback()
    {
        if (audioSource == null || beepSound == null) return;
        if (CurrentEmfLevel <= 1) return;

        // Higher EMF produces faster beeps
        float interval = CurrentEmfLevel switch
        {
            2 => 1.2f,
            3 => 0.6f,
            4 => 0.3f,
            5 => 0.12f,
            _ => 2.0f
        };

        if (Time.time >= nextBeepTime)
        {
            nextBeepTime = Time.time + interval;
            audioSource.pitch = 0.8f + (CurrentEmfLevel * 0.15f);
            audioSource.PlayOneShot(beepSound, 0.4f);
        }
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();
        bool isVisible = isInHand.Value || IsOnGround;

        if (!isVisible || !isPoweredOn.Value)
        {
            UpdateLeds(0);
            if (directionalPointer != null) directionalPointer.gameObject.SetActive(false);
        }
        else
        {
            UpdateLeds(CurrentEmfLevel);
        }
    }
}
