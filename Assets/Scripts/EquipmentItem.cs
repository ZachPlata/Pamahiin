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
    public Collider2D InteractCollider => interactCollider;
    protected SpriteRenderer spriteRenderer;

    // Network synchronization
    protected NetworkVariable<ulong> ownerClientId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    protected NetworkVariable<bool> isInHand = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    protected NetworkVariable<bool> isPlaced = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public string ItemName => itemName;
    public bool IsInHand => isInHand.Value;
    public ulong CurrentHolderClientId => ownerClientId.Value;
    public bool IsOnGround => ownerClientId.Value == ulong.MaxValue && !isPlaced.Value;
    public bool IsPlaced => ownerClientId.Value == ulong.MaxValue && isPlaced.Value;
    public virtual bool IsPoweredOn => false;

    protected virtual void Awake()
    {
        var colliders = GetComponents<Collider2D>();
        foreach (var col in colliders)
        {
            if (!col.isTrigger)
            {
                interactCollider = col;
                break;
            }
        }
        if (interactCollider == null && colliders.Length > 0)
        {
            interactCollider = colliders[0];
        }

        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        ownerClientId.OnValueChanged += (oldVal, newVal) => UpdateEquipState(newVal);
        isInHand.OnValueChanged += (oldVal, newVal) => OnInHandChanged(newVal);
        isPlaced.OnValueChanged += (oldVal, newVal) => OnPlacedChanged(newVal);

        UpdateEquipState(ownerClientId.Value);
        OnInHandChanged(isInHand.Value);
        OnPlacedChanged(isPlaced.Value);
    }

    protected virtual void Update()
    {
        // Follow the owner's hand/position smoothly
        if (ownerClientId.Value != ulong.MaxValue && NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            var playerObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(ownerClientId.Value);
            if (playerObj != null)
            {
                Transform handPoint = playerObj.transform.Find("HandPoint");
                if (handPoint != null)
                {
                    transform.position = handPoint.position;
                    transform.rotation = handPoint.rotation;
                }
                else
                {
                    transform.position = playerObj.transform.position;
                    transform.rotation = playerObj.transform.rotation;
                }
            }
        }
    }

    public virtual void Interact()
    {
        if (IsOnGround || IsPlaced)
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
        if (IsOnGround || IsPlaced)
        {
            var localPlayer = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            if (localPlayer != null)
            {
                var localInventory = localPlayer.GetComponent<PlayerInventory>();
                if (localInventory != null && !localInventory.HasEmptySlot())
                {
                    return "Slot Occupied";
                }
            }
            return $"Pick Up {itemName}";
        }
        return "";
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void PickupItemRpc(ulong clientId)
    {
        Debug.Log($"Player {clientId} picked up {itemName}");
        ownerClientId.Value = clientId;
        isInHand.Value = true;
        isPlaced.Value = false;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public virtual void DropItemRpc(Vector3 dropPosition)
    {
        Debug.Log($"Player dropped {itemName}");
        ownerClientId.Value = ulong.MaxValue;
        isPlaced.Value = false;
        isInHand.Value = true; // Dropped items on floor should be visible
        
        // Update position on server before broadcasting state
        transform.position = dropPosition;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public virtual void PlaceItemRpc(Vector3 placePosition, Quaternion placeRotation)
    {
        Debug.Log($"Player placed {itemName}");
        ownerClientId.Value = ulong.MaxValue;
        isPlaced.Value = true;
        isInHand.Value = true; // Placed items should be visible
        
        transform.position = placePosition;
        transform.rotation = placeRotation;
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
        Debug.Log($"Player used primary action on {itemName}");
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
        Debug.Log($"Player used secondary action on {itemName}");
        UseSecondaryRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public virtual void UseSecondaryRpc()
    {
    }

    protected virtual void UpdateEquipState(ulong newOwnerId)
    {
        bool onGroundOrPlaced = (newOwnerId == ulong.MaxValue);
        if (interactCollider != null)
        {
            interactCollider.enabled = onGroundOrPlaced;
        }

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            if (!onGroundOrPlaced || isPlaced.Value)
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            else
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
            }
        }

        // If local client picked it up, add to local inventory
        if (!onGroundOrPlaced && NetworkManager.Singleton != null && newOwnerId == NetworkManager.Singleton.LocalClientId)
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

    protected virtual void OnPlacedChanged(bool placed)
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            if (placed || ownerClientId.Value != ulong.MaxValue)
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            else
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
            }
        }
        UpdateVisuals();
    }

    protected virtual void UpdateVisuals()
    {
        bool isVisible = isInHand.Value || IsOnGround || IsPlaced;
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = isVisible;
        }
    }

    // --- IInteractable Dragging Methods ---
    public bool CanDrag() => IsOnGround; // Can only drag if it's dropped (not placed)
    
    public void OnDragBegin(ulong clientId)
    {
        if (IsServer) ApplyDragBegin();
        else DragBeginRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DragBeginRpc()
    {
        ApplyDragBegin();
    }

    private void ApplyDragBegin()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            // Set to Kinematic while dragging so it passes through walls and doesn't bounce
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    public void OnDragUpdate(Vector2 targetPos)
    {
        if (IsServer)
        {
            ApplyDrag(targetPos);
        }
        else
        {
            DragUpdateRpc(targetPos);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DragUpdateRpc(Vector2 targetPos)
    {
        ApplyDrag(targetPos);
    }

    private void ApplyDrag(Vector2 targetPos)
    {
        // Smoothly follow the mouse position. Since it's kinematic, it will phase through walls.
        transform.position = Vector2.Lerp(transform.position, targetPos, Time.deltaTime * 15f);
    }
    
    public void OnDragEnd(ulong clientId)
    {
        if (IsServer)
        {
            ApplyDragEnd();
        }
        else
        {
            DragEndRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DragEndRpc()
    {
        ApplyDragEnd();
    }

    private void ApplyDragEnd()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            // Return to dynamic physics when dropped
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }
}
