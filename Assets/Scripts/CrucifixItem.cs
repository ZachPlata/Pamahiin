using Unity.Netcode;
using UnityEngine;

public class CrucifixItem : EquipmentItem
{
    [Header("Crucifix Settings")]
    public float blockRadius = 3f;
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite burnedSprite;

    private NetworkVariable<bool> isBurned = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsBurned => isBurned.Value;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Crucifix";
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isBurned.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
    }

    /// <summary>
    /// Attempts to block a hunt. Returns true if successful (crucifix wasn't already burned).
    /// </summary>
    public bool TryBlockHunt()
    {
        if (!IsServer || isBurned.Value) return false;

        // Burn the crucifix
        isBurned.Value = true;
        return true;
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();
        
        if (spriteRenderer != null)
        {
            if (isBurned.Value && burnedSprite != null)
            {
                spriteRenderer.sprite = burnedSprite;
            }
            else if (!isBurned.Value && normalSprite != null)
            {
                spriteRenderer.sprite = normalSprite;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, blockRadius);
    }
}
