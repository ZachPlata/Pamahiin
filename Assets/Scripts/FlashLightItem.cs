using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class FlashlightItem : NetworkBehaviour, IInteractable
{
    [SerializeField] private Light2D spotlight;
    private Collider2D interactCollider;
    private SpriteRenderer spriteRenderer;

    // Network variables to sync who holds it, if the light is on, and if it's currently hidden in a pocket
    private NetworkVariable<ulong> ownerClientId = new NetworkVariable<ulong>(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> isLightOn = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> isInHand = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        interactCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        ownerClientId.OnValueChanged += (oldVal, newVal) => UpdateEquipState(newVal);
        isLightOn.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
        isInHand.OnValueChanged += (oldVal, newVal) => UpdateVisuals();
        
        UpdateEquipState(ownerClientId.Value);
        UpdateVisuals();
    }

    private void Update()
    {
        // Stick to the owner's hand
        if (ownerClientId.Value != ulong.MaxValue)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(ownerClientId.Value, out var client))
            {
                transform.position = client.PlayerObject.transform.position;
                transform.rotation = client.PlayerObject.transform.rotation;
            }
        }
    }

    public void Interact()
    {
        if (ownerClientId.Value == ulong.MaxValue)
        {
            // Get the local player's inventory
            var localInventory = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerInventory>();
            
            // Only send the pickup request if we actually have room
            if (localInventory != null && localInventory.HasEmptySlot())
            {
                PickupItemRpc(NetworkManager.Singleton.LocalClientId);
            }
        }
    }

    public string GetInteractText()
    {
        if (ownerClientId.Value == ulong.MaxValue)
        {
            var localInventory = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerInventory>();
            if (localInventory != null && !localInventory.HasEmptySlot())
            {
                return "Inventory Full";
            }
            return "Pick Up Flashlight";
        }
        return "";
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void PickupItemRpc(ulong clientId)
    {
        ownerClientId.Value = clientId;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void DropItemRpc()
    {
        ownerClientId.Value = ulong.MaxValue;
        isInHand.Value = true; // Dropped items should always be visible on the floor
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UseItemRpc()
    {
        isLightOn.Value = !isLightOn.Value;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetInHandRpc(bool inHand)
    {
        isInHand.Value = inHand;
    }

    private void UpdateEquipState(ulong newOwnerId)
    {
        bool isOnGround = (newOwnerId == ulong.MaxValue);
        interactCollider.enabled = isOnGround;

        // If WE just picked it up, send it to our local inventory manager
        if (!isOnGround && newOwnerId == NetworkManager.Singleton.LocalClientId)
        {
            var localInventory = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerInventory>();
            localInventory.AddItem(this.NetworkObject);
        }
    }

    private void UpdateVisuals()
    {
        // An item is visible if it is currently in our hand OR sitting on the ground
        bool isVisible = isInHand.Value || ownerClientId.Value == ulong.MaxValue;
        
        spriteRenderer.enabled = isVisible;
        spotlight.enabled = (isVisible && isLightOn.Value);
    }
}