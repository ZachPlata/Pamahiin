using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(Collider2D))]
public class FrontDoorKeyItem : NetworkBehaviour, IInteractable
{
    public void Interact()
    {
        PickupKeyRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void PickupKeyRpc()
    {
        // Unlock all front doors
        var doors = Object.FindObjectsByType<NetworkDoor>(FindObjectsInactive.Exclude);
        foreach (var door in doors)
        {
            if (door.IsFrontDoor)
            {
                door.UnlockWithKey();
            }
        }

        // Destroy the key globally
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
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
