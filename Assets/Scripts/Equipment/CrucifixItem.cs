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
        isBurned.OnValueChanged += (oldVal, newVal) =>
        {
            if (!oldVal && newVal) GameAudioManager.PlayCrucifixBurn(transform.position);
            UpdateVisuals();
        };
    }

    protected override void Update()
    {
        base.Update();

        if (isBurned.Value && spriteRenderer != null)
        {
            // 16-bit smoldering ember pulse animation on burned crucifix
            float ember = Mathf.Floor((0.75f + 0.25f * Mathf.Sin(Time.time * 10f)) * 4f) / 4f;
            spriteRenderer.color = new Color(1f, ember * 0.65f, ember * 0.45f, 1f);
        }

        // Check if an Angered ghost is nearby to repel it (never triggered while carried by dead spectator or while ghost is paused in grace period)
        if (!isBurned.Value && !IsHeldByDeadPlayer())
        {
            var hunterGhost = HunterGhostBehavior.Instance;
            if (hunterGhost != null && hunterGhost.IsAngeredState && !hunterGhost.IsRepelled && !hunterGhost.IsInHuntGracePeriod)
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
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
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
    private void RepelGhostServerRpc(RpcParams rpcParams = default)
    {
        if (!IsPlayerAliveAndPresent(rpcParams.Receive.SenderClientId)) return;
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
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer) return false;
        if (isBurned.Value || !IsPlaced) return false;

        // Burn the crucifix (1 hunt blocked per crucifix)
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
