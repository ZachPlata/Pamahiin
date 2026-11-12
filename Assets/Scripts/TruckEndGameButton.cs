using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider2D))]
public class TruckEndGameButton : NetworkBehaviour, IInteractable
{
    private void Awake()
    {
        int interactLayer = LayerMask.NameToLayer("Interactable");
        if (interactLayer >= 0)
        {
            gameObject.layer = interactLayer;
        }

        var col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        // Prevent Netcode from disabling this in-scene NetworkObject if parented under a non-NetworkObject (Van)
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && transform.parent != null && transform.parent.GetComponentInParent<NetworkObject>() == null)
        {
            netObj.AutoObjectParentSync = false;
        }
    }

    public void Interact()
    {
        Debug.Log("[TruckEndGameButton] End Mission button interacted!");
        GameAudioManager.PlayUIClick();

        var matchMgr = GameMatchManager.Instance != null
            ? GameMatchManager.Instance
            : Object.FindAnyObjectByType<GameMatchManager>();

        if (matchMgr != null)
        {
            matchMgr.RequestEndMatch();
            return;
        }

        // Fallback if GameMatchManager is missing from the scene
        if (IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)
        {
            RequestEndMatchServerRpc();
        }
        else
        {
            TriggerEndMatchFallback();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestEndMatchServerRpc()
    {
        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.ForceEndMatch();
        }
        else
        {
            TriggerEndMatchFallback();
        }
    }

    private void TriggerEndMatchFallback()
    {
        string ghostName = "Unknown Entity";
        var ghost = Object.FindAnyObjectByType<GhostHandler>();
        if (ghost != null && !string.IsNullOrEmpty(ghost.ghostName) && !ghost.ghostName.Equals("Unknown", System.StringComparison.OrdinalIgnoreCase))
        {
            ghostName = ghost.ghostName;
        }

        var reveal = Object.FindAnyObjectByType<EndGameRevealController>();
        if (reveal != null)
        {
            reveal.TriggerReveal(ghostName);
        }
    }

    public string GetInteractText() => "End Mission and Leave";

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPos) { }
    public void OnDragEnd(ulong clientId) { }
}
