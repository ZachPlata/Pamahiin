using Unity.Netcode;
using UnityEngine;

public class CrucifixItem : EquipmentItem
{
    [Header("Crucifix Settings")]
    public float blockRadius = 4f;
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

    protected override void Update()
    {
        base.Update();

        // Check if an Angered ghost is nearby to repel it (applies when placed on the ground or held)
        if (!isBurned.Value)
        {
            var hunterGhost = Object.FindAnyObjectByType<HunterGhostBehavior>();
            if (hunterGhost != null && hunterGhost.IsAngeredState && !hunterGhost.IsRepelled)
            {
                float dist = Vector2.Distance(transform.position, hunterGhost.transform.position);
                if (dist <= blockRadius)
                {
                    TryRepelGhost(hunterGhost);
                }
            }
        }
    }

    public override void UsePrimary()
    {
        base.UsePrimary();

        if (isBurned.Value)
        {
            Debug.Log("[Crucifix] This crucifix has already burned out!");
            return;
        }

        // Active use by player
        var hunterGhost = Object.FindAnyObjectByType<HunterGhostBehavior>();
        if (hunterGhost != null && hunterGhost.IsAngeredState && !hunterGhost.IsRepelled)
        {
            float dist = Vector2.Distance(transform.position, hunterGhost.transform.position);
            if (dist <= blockRadius + 4f)
            {
                TryRepelGhost(hunterGhost);
                return;
            }
        }

        Debug.Log("[Crucifix] Held up crucifix ward.");
    }

    private void TryRepelGhost(HunterGhostBehavior ghost)
    {
        if (isBurned.Value) return;

        Debug.Log("[Crucifix] Repelling Angered Ghost for 10 seconds!");
        if (IsServer || !IsSpawned)
        {
            isBurned.Value = true;
            ghost.ApplyCrucifixRepel(10f, transform.position);
        }
        else
        {
            RepelGhostServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RepelGhostServerRpc()
    {
        if (isBurned.Value) return;
        isBurned.Value = true;

        var hunterGhost = Object.FindAnyObjectByType<HunterGhostBehavior>();
        if (hunterGhost != null)
        {
            hunterGhost.ApplyCrucifixRepel(10f, transform.position);
        }
    }

    /// <summary>
    /// Attempts to block a hunt. Returns true if successful (crucifix wasn't already burned).
    /// </summary>
    public bool TryBlockHunt()
    {
        if (!IsServer || isBurned.Value || !IsPlaced) return false;

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
