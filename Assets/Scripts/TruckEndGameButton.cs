using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider2D))]
public class TruckEndGameButton : NetworkBehaviour, IInteractable
{
    public void Interact()
    {
        Debug.Log("Truck end game button clicked!");

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            Debug.Log("Offline mode: Forcing end match locally.");
            TriggerEndMatch();
            return;
        }

        if (!IsServer)
        {
            RequestEndMatchServerRpc();
        }
        else
        {
            TriggerEndMatch();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestEndMatchServerRpc()
    {
        TriggerEndMatch();
    }

    private void TriggerEndMatch()
    {
        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.ForceEndMatch();
        }
    }

    public string GetInteractText() => "End Mission and Leave";
    
    // Unused drag interface methods
    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPos) { }
    public void OnDragEnd(ulong clientId) { }
}
