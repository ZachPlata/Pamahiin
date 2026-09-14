using Unity.Netcode;
using UnityEngine;

public class SpiritBoxItem : EquipmentItem
{
    [Header("Spirit Box Settings")]
    [SerializeField] private Sprite activatedSprite;
    [SerializeField] private Sprite deactivatedSprite;
    [SerializeField] private float detectionRadius = 4f;
    [SerializeField] private float queryInterval = 3f;

    private NetworkVariable<bool> isPoweredOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override bool IsPoweredOn => isPoweredOn.Value;
    private float nextQueryTime = 0f;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Spirit Box";
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isPoweredOn.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
    }

    public override void UsePrimary()
    {
        TogglePowerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TogglePowerRpc()
    {
        isPoweredOn.Value = !isPoweredOn.Value;
        if (isPoweredOn.Value)
        {
            nextQueryTime = Time.time + queryInterval;
        }
    }

    protected override void Update()
    {
        base.Update();

        if (IsServer && isPoweredOn.Value)
        {
            if (Time.time >= nextQueryTime)
            {
                nextQueryTime = Time.time + queryInterval;
                QueryGhost();
            }
        }
    }

    private void QueryGhost()
    {
        if (ParanormalManager.Instance != null)
        {
            // Simple check: is a ghost near us with spirit box evidence?
            var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
            if (ghostHandler != null && ghostHandler.HasEvidence(EvidenceType.SpiritBox))
            {
                // Note: The actual distance check should ideally be from the ghost's physical location,
                // but the event ghost or hunter ghost might be moving around. We'll use the active event ghost.
                if (ghostHandler.ghostroomMarker != null)
                {
                    // Check if the Spirit Box is physically inside the ghost room
                    if (ghostHandler.ghostroomMarker.OverlapPoint(transform.position))
                    {
                        // Trigger response!
                        TriggerResponseRpc();
                    }
                }
            }
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void TriggerResponseRpc()
    {
        // For now, we print to console. A sound effect or UI icon could be added here.
        Debug.Log("Spirit Box: Ghost responded!");
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();
        if (spriteRenderer != null)
        {
            if (isPoweredOn.Value && activatedSprite != null)
            {
                spriteRenderer.sprite = activatedSprite;
            }
            else if (!isPoweredOn.Value && deactivatedSprite != null)
            {
                spriteRenderer.sprite = deactivatedSprite;
            }
        }
    }
}
