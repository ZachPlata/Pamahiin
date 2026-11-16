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
        if (audioSource != null)
        {
            GameAudioManager.ConfigureProximityAudioSource(audioSource, 0.45f, 2.6f, 1.0f);
        }
        if (consumeAudioClip == null) consumeAudioClip = GameAudioManager.GetClip("sanity_pills");
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

    public override void TriggerEquipmentTutorial()
    {
        TutorialTooltipManager.TriggerHint(TutorialHintId.SanityPills);
    }

    /// <summary>
    /// Left click when held in hand consumes the pills.
    /// </summary>
    public override void UsePrimary()
    {
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
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
    public override void UsePrimaryRpc(RpcParams rpcParams = default)
    {
        if (!IsPlayerAliveAndPresent(rpcParams.Receive.SenderClientId)) return;
        base.UsePrimaryRpc(rpcParams);
        if (ownerClientId.Value != ulong.MaxValue)
        {
            ConsumePillsServerRpc(ownerClientId.Value);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ConsumePillsServerRpc(ulong consumingClientId, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (!IsPlayerAliveAndPresent(consumingClientId)) return;

        Debug.Log($"[SanityPills] Server received consume request from client {consumingClientId}");

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(consumingClientId, out var client) &&
            client.PlayerObject != null)
        {
            var player = client.PlayerObject.GetComponent<PlayerController>();
            if (player != null && player.IsAlive)
            {
                float effectiveRestore = ProgressionAndLoadoutManager.Instance != null
                    ? ProgressionAndLoadoutManager.GetDifficultySanityPillRestore(ProgressionAndLoadoutManager.Instance.SelectedDifficulty)
                    : restoreAmount;
                player.RestoreSanity(effectiveRestore);
                Debug.Log($"[SanityPills] Restored {effectiveRestore} sanity for client {consumingClientId}. New sanity: {player.Sanity}");
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
            float floorMult = GameAudioManager.GetFloorAttenuation(transform.position, 0f);
            if (floorMult <= 0.01f) return;

            if (audioSource != null)
            {
                GameAudioManager.ConfigureProximityAudioSource(audioSource, 0.45f, 2.6f, 1.0f);
                audioSource.PlayOneShot(consumeAudioClip, GameAudioManager.GetEffectiveSfxVolume(0.85f * floorMult));
            }
            else
            {
                GameAudioManager.PlayClipAt(consumeAudioClip, transform.position, 0.85f, 1.0f, 1f, 0.45f, 2.6f, 0f);
            }
        }
    }
}
