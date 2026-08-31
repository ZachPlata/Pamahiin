using Unity.Netcode;
using UnityEngine;

public class KeyPickup : NetworkBehaviour
{
    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("Player") && Input.GetKey(KeyCode.E))
        {
            PlayerInventory inventory = collision.GetComponent<PlayerInventory>();
            
            if (inventory != null && inventory.IsOwner)
            {
                inventory.hasFrontDoorKey = true;
                
                RequestDespawnRpc();
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestDespawnRpc()
    {
        GetComponent<NetworkObject>().Despawn();
    }
}