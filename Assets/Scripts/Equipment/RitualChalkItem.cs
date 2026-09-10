using Unity.Netcode;
using UnityEngine;

public class RitualChalkItem : EquipmentItem
{
    [Header("Ritual Settings")]
    [Tooltip("The radius required to start the ritual (must be in ghost room)")]
    public float ritualRadius = 3f;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Ritual Chalk";
    }

    public override void UsePrimary()
    {
        if (IsOwner)
        {
            // Try to start the exorcism
            // In a full implementation, we'd check if we are in the Ghost's favorite room.
            // For now, if we are within range of the ghost or just anywhere, it triggers it!
            var ghost = Object.FindAnyObjectByType<GhostHandler>();
            if (ghost != null && Vector2.Distance(transform.position, ghost.transform.position) < 15f)
            {
                Debug.Log("Ritual Chalk Used! Starting Exorcism...");
                
                // Trigger the minigame
                if (ExorcismManager.Instance != null)
                {
                    ExorcismManager.Instance.StartExorcism();
                }

                // Consume the item
                DestroyItemServerRpc();
            }
            else
            {
                Debug.Log("You must be closer to the ghost's domain to use the ritual chalk.");
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DestroyItemServerRpc()
    {
        NetworkObject.Despawn(true);
    }
}
