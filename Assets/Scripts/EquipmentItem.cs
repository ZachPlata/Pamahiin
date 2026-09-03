using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Abstract base class for all holdable and deployable ghost hunting tools.
/// Handles network synchronization for ownership, active hand state, and ground placement.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public abstract class EquipmentItem : NetworkBehaviour, IInteractable
{
    [Header("Equipment Info")]
    [SerializeField] protected string itemName = "Equipment";

    protected Collider2D interactCollider;
    protected SpriteRenderer spriteRenderer;

    // Network synchronization
    protected NetworkVariable<ulong> ownerClientId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    protected NetworkVariable<bool> isInHand = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public string ItemName => itemName;
    public bool IsInHand => isInHand.Value;
    public ulong CurrentHolderClientId => ownerClientId.Value;
    public bool IsOnGround => ownerClientId.Value == ulong.MaxValue;

    protected virtual void Awake()
    {
        interactCollider = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        ownerClientId.OnValueChanged += (oldVal, newVal) => UpdateEquipState(newVal);
        isInHand.OnValueChanged += (oldVal, newVal) => OnInHandChanged(newVal);

        UpdateEquipState(ownerClientId.Value);
        OnInHandChanged(isInHand.Value);
    }

    protected virtual void Update()
    {
        // Follow the owner's hand/position smoothly
        if (ownerClientId.Value != ulong.MaxValue)
        {
            if (NetworkManager.Singleton != null && 
                NetworkManager.Singleton.ConnectedClients != null &&
                NetworkManager.Singleton.ConnectedClients.TryGetValue(ownerClientId.Value, out var client) &&
                client.PlayerObject != null)
            {
                transform.position = client.PlayerObject.transform.position;
                transform.rotation = client.PlayerObject.transform.rotation;
            }
        }
    }

    public virtual void Interact()
    {
        if (IsOnGround)
        {
            var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (localPlayer != null)
            {
                var localInventory = localPlayer.GetComponent<PlayerInventory>();
                if (localInventory != null && localInventory.HasEmptySlot())
                {
                    PickupItemRpc(NetworkManager.Singleton.LocalClientId);
                }
            }
        }
    }

    public virtual string GetInteractText()
    {
        if (IsOnGround)
        {
            var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (localPlayer != null)
            {
                var localInventory = localPlayer.GetComponent<PlayerInventory>();
                if (localInventory != null && !localInventory.HasEmptySlot())
                {
                    return "Inventory Full";
                }
            }
            return $"Pick Up {itemName}";
        }
        return "";
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PickupItemRpc(ulong clientId)
    {
        ownerClientId.Value = clientId;
        isInHand.Value = true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public virtual void DropItemRpc()
    {
        ownerClientId.Value = ulong.MaxValue;
        isInHand.Value = true; // Dropped items on floor should be visible
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetInHandRpc(bool inHand)
    {
        isInHand.Value = inHand;
    }

    /// <summary>
    /// Triggered when the owner left-clicks while holding this item.
    /// </summary>
    public virtual void UsePrimary()
    {
        UsePrimaryRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public virtual void UsePrimaryRpc()
    {
    }

    /// <summary>
    /// Triggered when the owner right-clicks while holding this item.
    /// </summary>
    public virtual void UseSecondary()
    {
        UseSecondaryRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public virtual void UseSecondaryRpc()
    {
    }

    protected virtual void UpdateEquipState(ulong newOwnerId)
    {
        bool onGround = (newOwnerId == ulong.MaxValue);
        if (interactCollider != null)
        {
            interactCollider.enabled = onGround;
        }

        // If local client picked it up, add to local inventory
        if (!onGround && NetworkManager.Singleton != null && newOwnerId == NetworkManager.Singleton.LocalClientId)
        {
            var localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (localPlayer != null)
            {
                var localInventory = localPlayer.GetComponent<PlayerInventory>();
                if (localInventory != null)
                {
                    localInventory.AddItem(this);
                }
            }
        }

        UpdateVisuals();
    }

    protected virtual void OnInHandChanged(bool inHand)
    {
        UpdateVisuals();
    }

    protected virtual void UpdateVisuals()
    {
        bool isVisible = isInHand.Value || IsOnGround;
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = isVisible;
        }
    }
}
