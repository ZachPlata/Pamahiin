using Unity.Netcode;
using UnityEngine;

public class GhostWritingBookItem : EquipmentItem
{
    [Header("Ghost Writing Settings")]
    [SerializeField] private Sprite closedSprite;
    [SerializeField] private Sprite openedSprite;
    [SerializeField] private Sprite writtenClosedSprite;

    private NetworkVariable<bool> isWritten = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsWritten => isWritten.Value;
    
    // The book is considered "opened" only when it's placed on the ground and hasn't been written in yet.
    public bool IsOpened => IsOnGround && !isWritten.Value;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Ghost Writing Book";
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isWritten.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
    }

    protected override void UpdateEquipState(ulong newOwnerId)
    {
        base.UpdateEquipState(newOwnerId);
        UpdateVisuals(); // Update visual state when dropped or picked up
    }

    public void WriteInBook()
    {
        if (!IsServer) return;
        
        if (IsOpened)
        {
            isWritten.Value = true;
        }
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();

        if (spriteRenderer != null)
        {
            if (isWritten.Value && writtenClosedSprite != null)
            {
                spriteRenderer.sprite = writtenClosedSprite;
            }
            else if (IsOpened && openedSprite != null)
            {
                spriteRenderer.sprite = openedSprite;
            }
            else if (closedSprite != null)
            {
                spriteRenderer.sprite = closedSprite;
            }
        }
    }
}
