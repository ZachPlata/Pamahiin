using Unity.Netcode;
using UnityEngine;

public class SanityPillsItem : EquipmentItem
{
    [Header("Sanity Pills Settings")]
    [SerializeField] private float restoreAmount = 40f;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Sanity Pills";
    }

    public override void UsePrimaryRpc()
    {
        base.UsePrimaryRpc();

        if (!IsServer) return;

        if (ownerClientId.Value != ulong.MaxValue)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(ownerClientId.Value, out var client) && client.PlayerObject != null)
            {
                var player = client.PlayerObject.GetComponent<PlayerController>();
                if (player != null)
                {
                    player.RestoreSanity(restoreAmount);

                    // Remove from inventory
                    var inventory = client.PlayerObject.GetComponent<PlayerInventory>();
                    if (inventory != null)
                    {
                        inventory.RemoveItem(this);
                    }

                    // Despawn and destroy the object over the network
                    GetComponent<NetworkObject>().Despawn(true);
                }
            }
        }
    }
}
