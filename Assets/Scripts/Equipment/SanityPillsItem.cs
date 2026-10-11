using Unity.Netcode;
using UnityEngine;

public class SanityPillsItem : EquipmentItem
{
    [Header("Sanity Pills Settings")]
    [SerializeField] private float restoreAmount = 40f;
    [SerializeField] private AudioClip consumeAudioClip;
    private AudioSource audioSource;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Sanity Pills";

        // Ensure layer is Interactable so PlayerController hit-testing can detect it
        int interactLayer = LayerMask.NameToLayer("Interactable");
        if (interactLayer != -1 && gameObject.layer != interactLayer)
        {
            gameObject.layer = interactLayer;
        }

        // Ensure collider is sized appropriately so it can easily be hovered and picked up
        EnsureProperCollider();

        audioSource = GetComponent<AudioSource>();
    }

    private void EnsureProperCollider()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
        {
            var circle = gameObject.AddComponent<CircleCollider2D>();
            circle.radius = 5.5f;
            circle.isTrigger = true;
            interactCollider = circle;
        }
        else
        {
            // If the collider bounds are microscopic (e.g. tiny 0.1 unit unscaled polygon),
            // replace it with a properly sized CircleCollider2D matching the bottle sprite.
            if (col is PolygonCollider2D poly && poly.points.Length > 0 && Mathf.Abs(poly.points[0].x) < 1f)
            {
                Destroy(col);
                var circle = gameObject.AddComponent<CircleCollider2D>();
                circle.radius = 5.5f;
                circle.isTrigger = true;
                interactCollider = circle;
            }
            else
            {
                interactCollider = col;
            }
        }
    }

    // Do not drag sanity pills with mouse; clicking LMB or pressing E directly picks them up!
    public override bool CanDrag() => false;

    /// <summary>
    /// Left click when held in hand consumes the pills.
    /// </summary>
    public override void UsePrimary()
    {
        // Must be held by local player
        if (NetworkManager.Singleton == null || ownerClientId.Value != NetworkManager.Singleton.LocalClientId)
        {
            return;
        }

        Debug.Log($"[SanityPills] Consuming {itemName} locally, requesting server consumption...");

        // Play audio locally
        PlayConsumeAudio();

        // Immediately remove from local inventory for responsive UI feedback
        var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayer != null)
        {
            var inv = localPlayer.GetComponent<PlayerInventory>();
            if (inv != null)
            {
                inv.RemoveItem(this);
            }
        }

        // Notify server to restore sanity and despawn the network object
        ConsumePillsServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public override void UsePrimaryRpc()
    {
        base.UsePrimaryRpc();
        if (ownerClientId.Value != ulong.MaxValue)
        {
            ConsumePillsServerRpc(ownerClientId.Value);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ConsumePillsServerRpc(ulong consumingClientId)
    {
        if (!IsServer) return;

        Debug.Log($"[SanityPills] Server received consume request from client {consumingClientId}");

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(consumingClientId, out var client) &&
            client.PlayerObject != null)
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();
            if (player != null && player.IsAlive)
            {
                player.RestoreSanity(restoreAmount);
                Debug.Log($"[SanityPills] Restored {restoreAmount} sanity for client {consumingClientId}. New sanity: {player.Sanity}");
            }

            var inventory = client.PlayerObject.GetComponent<PlayerInventory>();
            if (inventory != null)
            {
                inventory.RemoveItem(this);
            }
        }

        // Broadcast audio / effect to other clients
        NotifyConsumedClientRpc();

        // Despawn and destroy network object
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyConsumedClientRpc()
    {
        PlayConsumeAudio();
    }

    private void PlayConsumeAudio()
    {
        if (consumeAudioClip != null)
        {
            if (audioSource != null)
            {
                audioSource.PlayOneShot(consumeAudioClip);
            }
            else
            {
                AudioSource.PlayClipAtPoint(consumeAudioClip, transform.position);
            }
        }
    }
}
