using Unity.Netcode;
using UnityEngine;

public class GhostWritingBookItem : EquipmentItem
{
    [Header("Ghost Writing Settings")]
    [SerializeField] private Sprite closedSprite;
    [SerializeField] private Sprite openedSprite;
    [Tooltip("Sprite displayed when the book has ghost writing in it (supports both open or closed written states).")]
    [SerializeField] private Sprite writtenClosedSprite;
    [SerializeField] private Sprite writtenOpenSprite;
    [SerializeField] private Sprite inventorySprite;
    [SerializeField] private AudioClip writingSound;

    private NetworkVariable<bool> isWritten = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsWritten => isWritten.Value;
    
    // The book is considered "opened" only when it's placed on the ground and hasn't been written in yet.
    public bool IsOpened => IsPlaced && !isWritten.Value;
    public bool IsInInventory => ownerClientId.Value != ulong.MaxValue;
    public Sprite InventorySprite => inventorySprite;

    private Vector3 baseLocalScale;
    private bool hasBaseScale = false;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Ghost Writing Book";

        if (!hasBaseScale)
        {
            baseLocalScale = transform.localScale;
            hasBaseScale = true;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isWritten.OnValueChanged += (oldVal, newVal) => 
        {
            UpdateVisuals();
            if (newVal && !oldVal && writingSound != null)
            {
                AudioSource.PlayClipAtPoint(writingSound, transform.position, 1f);
            }
        };
    }

    protected override void UpdateEquipState(ulong newOwnerId)
    {
        base.UpdateEquipState(newOwnerId);
        UpdateVisuals(); // Update visual state when dropped or picked up
    }

    /// <summary>
    /// Validates whether the book is currently placed inside the active Ghost Room bounds.
    /// </summary>
    public bool IsInsideGhostRoom()
    {
        return GhostRoomMarker.IsInsideGhostRoom(transform.position);
    }

    public void WriteInBook()
    {
        if (!IsServer) return;
        
        if (IsOpened && IsInsideGhostRoom())
        {
            isWritten.Value = true;
        }
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();

        if (spriteRenderer != null)
        {
            Sprite targetSprite = null;

            if (IsInInventory && inventorySprite != null)
            {
                targetSprite = inventorySprite;
            }
            else if (isWritten.Value)
            {
                // When written in, check available written sprites
                if (IsPlaced && writtenOpenSprite != null)
                {
                    targetSprite = writtenOpenSprite;
                }
                else if (writtenClosedSprite != null)
                {
                    targetSprite = writtenClosedSprite;
                }
                else if (writtenOpenSprite != null)
                {
                    targetSprite = writtenOpenSprite;
                }
                else if (openedSprite != null)
                {
                    targetSprite = openedSprite;
                }
            }
            else if (IsOpened && openedSprite != null)
            {
                targetSprite = openedSprite;
            }
            else if (closedSprite != null)
            {
                targetSprite = closedSprite;
            }

            if (targetSprite != null)
            {
                spriteRenderer.sprite = targetSprite;
                AdjustScaleForSprite(targetSprite);
            }
        }
    }

    private void AdjustScaleForSprite(Sprite sprite)
    {
        if (sprite == null) return;

        if (!hasBaseScale)
        {
            baseLocalScale = transform.localScale;
            hasBaseScale = true;
        }

        // Reference width in world units calculated from openedSprite or closedSprite (~8.06 to 10.24 units unscaled)
        float referenceWidth = 10.24f;
        if (openedSprite != null)
        {
            referenceWidth = openedSprite.rect.width / openedSprite.pixelsPerUnit;
        }
        else if (closedSprite != null)
        {
            referenceWidth = closedSprite.rect.width / closedSprite.pixelsPerUnit;
        }

        float currentWidth = sprite.rect.width / sprite.pixelsPerUnit;
        if (currentWidth > 0.001f)
        {
            float ratio = referenceWidth / currentWidth;
            // If the sprite resolution/PPU is significantly different (e.g. 48px vs 1024px, ratio ~21x),
            // dynamically compensate the local scale so it stays visible and correctly sized in the scene.
            if (ratio > 1.25f || ratio < 0.8f)
            {
                transform.localScale = baseLocalScale * ratio;
            }
            else
            {
                transform.localScale = baseLocalScale;
            }
        }
    }
}
