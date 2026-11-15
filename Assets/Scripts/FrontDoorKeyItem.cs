using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(Collider2D))]
public class FrontDoorKeyItem : NetworkBehaviour, IInteractable
{
    public void Interact()
    {
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
        PickupKeyRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void PickupKeyRpc(RpcParams rpcParams = default)
    {
        if (PlayerController.TryGetByClientId(rpcParams.Receive.SenderClientId, out var sender) && !sender.IsAlive)
        {
            return;
        }
        Debug.Log("Player picked up Front Door Key");
        // Unlock all front doors
        var doors = Object.FindObjectsByType<NetworkDoor>(FindObjectsInactive.Exclude);
        foreach (var door in doors)
        {
            if (door.IsFrontDoor)
            {
                door.UnlockWithKey();
            }
        }

        // Deactivate scene-placed key safely without destroying its NetworkObject
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(false);
            gameObject.SetActive(false);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        gameObject.SetActive(false);
    }

    public string GetInteractText()
    {
        return "Grab Front Door Key";
    }

    // --- IInteractable Dragging Methods ---
    public bool CanDrag() => false;
    
    public void OnDragBegin(ulong clientId) { }
    
    public void OnDragUpdate(Vector2 targetPos) { }
    
    public void OnDragEnd(ulong clientId) { }
}
