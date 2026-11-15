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
    protected NetworkVariable<Vector3> syncedScale = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> draggingClientId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector2> syncedDragPosition = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public string ItemName => itemName;
    public bool IsInHand => isInHand.Value;
    public ulong CurrentHolderClientId => ownerClientId.Value;
    public bool IsOnGround => ownerClientId.Value == ulong.MaxValue && !isPlaced.Value;
    public bool IsPlaced => ownerClientId.Value == ulong.MaxValue && isPlaced.Value;
    public virtual bool IsPoweredOn => false;

    public static readonly System.Collections.Generic.List<EquipmentItem> AllEquipment =
        new System.Collections.Generic.List<EquipmentItem>();

    private const float DragLerpSpeed = 15f;
    private const float DragRpcSendInterval = 1f / 30f;
    private const float DragRpcMinDeltaSqr = 0.000001f;

    private ulong cachedHandOwnerId = ulong.MaxValue;
    private Transform cachedHandTarget;
    private int serverEquipSeq;
    private int lastClientEquipSeq = -1;
    private bool isLocallyDragging;
    private Vector2 localDragTarget;
    private Vector2 lastSentDragTarget;
    private float nextDragRpcTime;
    private Vector2 serverDragTarget;

    protected virtual void OnEnable()
    {
        if (!AllEquipment.Contains(this)) AllEquipment.Add(this);
    }

    protected virtual void OnDisable()
    {
        AllEquipment.Remove(this);
        isLocallyDragging = false;
    }

    public virtual bool IsHeldByLocalPlayer
    {
        get
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient?.PlayerObject != null)
            {
                var inv = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerInventory>();
                if (inv != null && inv.CurrentItem == this) return true;
            }
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                return ownerClientId.Value == NetworkManager.Singleton.LocalClientId && isInHand.Value;
            }
            return isInHand.Value;
        }
    }

    protected virtual void Awake()
    {
        int interactLayer = LayerMask.NameToLayer("Interactable");
        if (interactLayer >= 0)
        {
            gameObject.layer = interactLayer;
        }

        spriteRenderer = GetComponent<SpriteRenderer>();

        // Prefer PolygonCollider2D for clean silhouette interaction & hover outline
        PolygonCollider2D polyCol = GetComponent<PolygonCollider2D>();
        var boxCols = GetComponents<BoxCollider2D>();

        if (polyCol == null)
        {
            foreach (var box in boxCols)
            {
                if (box != null)
                {
                    box.enabled = false;
                    Destroy(box);
                }
            }
            polyCol = gameObject.AddComponent<PolygonCollider2D>();
        }
        else
        {
            foreach (var box in boxCols)
            {
                if (box != null) box.enabled = false;
            }
        }

        polyCol.isTrigger = true;
        interactCollider = polyCol;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    public override void OnNetworkSpawn()
    {
        ownerClientId.OnValueChanged += (oldVal, newVal) =>
        {
            if (oldVal == ulong.MaxValue && newVal != ulong.MaxValue)
                GameAudioManager.PlayItemPickup(transform.position);
            else if (oldVal != ulong.MaxValue && newVal == ulong.MaxValue && !isPlaced.Value)
                GameAudioManager.PlayItemDrop(transform.position);
            UpdateEquipState(newVal);
        };
        isInHand.OnValueChanged += (oldVal, newVal) => OnInHandChanged(newVal);
        isPlaced.OnValueChanged += (oldVal, newVal) =>
        {
            if (!oldVal && newVal)
                GameAudioManager.PlayItemPlace(transform.position);
            OnPlacedChanged(newVal);
        };

        syncedScale.OnValueChanged += OnSyncedScaleChanged;
        draggingClientId.OnValueChanged += OnDraggingClientIdChanged;

        ApplySyncedScale();
        UpdateEquipState(ownerClientId.Value);
        OnInHandChanged(isInHand.Value);
        OnPlacedChanged(isPlaced.Value);
    }

    private void OnDraggingClientIdChanged(ulong previousValue, ulong newValue)
    {
        ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
        if (newValue == ulong.MaxValue || newValue != localClientId)
        {
            isLocallyDragging = false;
        }
    }

    private void OnSyncedScaleChanged(Vector3 previousValue, Vector3 newValue)
    {
        ApplySyncedScale();
    }

    /// <summary>
    /// Applies and replicates a specific world scale (e.g. from TruckGearSpawner) across all clients.
    /// </summary>
    public void SetSyncedScale(Vector3 scale)
    {
        if (scale.sqrMagnitude <= 1e-8f) return;

        transform.localScale = scale;
        OnSyncedScaleApplied(scale);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
        {
            syncedScale.Value = scale;
        }
    }

    protected void ApplySyncedScale()
    {
        if (!IsSpawned || syncedScale.Value.sqrMagnitude <= 1e-8f) return;

        transform.localScale = syncedScale.Value;
        OnSyncedScaleApplied(syncedScale.Value);
    }

    protected virtual void OnSyncedScaleApplied(Vector3 appliedScale) { }

    /// <summary>
    /// Ensures runtime-cloned equipment items initialize their state listeners even if spawned locally.
    /// </summary>
    public void InitializeLocalCloneFallback()
    {
        if (!IsSpawned)
        {
            OnNetworkSpawn();
        }
    }

    public override void OnNetworkDespawn()
    {
        syncedScale.OnValueChanged -= OnSyncedScaleChanged;
        draggingClientId.OnValueChanged -= OnDraggingClientIdChanged;
        isLocallyDragging = false;
        base.OnNetworkDespawn();

        // If this item was held in local inventory, clean up the slot
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient?.PlayerObject != null)
        {
            var localInventory = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerInventory>();
            if (localInventory != null)
            {
                localInventory.RemoveItem(this);
            }
        }
    }

    /// <summary>
    /// Resolves the current holder's root PlayerController transform (for both local and remote clients).
    /// </summary>
    protected Transform ResolveHolderPlayerTransform()
    {
        return ResolvePlayerTransform(ownerClientId.Value);
    }

    private Transform ResolvePlayerTransform(ulong clientId)
    {
        if (clientId == ulong.MaxValue) return null;

        var allPlayers = PlayerController.AllPlayers;
        for (int i = 0; i < allPlayers.Count; i++)
        {
            var p = allPlayers[i];
            if (p != null && p.IsSpawned && p.OwnerClientId == clientId)
            {
                return p.transform;
            }
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
        {
            var playerObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
            if (playerObj != null) return playerObj.transform;
        }

        return null;
    }

    private Transform ResolveOwnerHandTarget(ulong clientId)
    {
        if (clientId == ulong.MaxValue)
        {
            cachedHandOwnerId = ulong.MaxValue;
            cachedHandTarget = null;
            return null;
        }

        if (cachedHandOwnerId == clientId && cachedHandTarget != null)
        {
            return cachedHandTarget;
        }

        Transform playerTr = ResolvePlayerTransform(clientId);
        if (playerTr != null)
        {
            Transform handPoint = playerTr.Find("HandPoint");
            cachedHandOwnerId = clientId;
            cachedHandTarget = handPoint != null ? handPoint : playerTr;
            return cachedHandTarget;
        }

        return null;
    }

    /// <summary>
    /// Local rotation offset applied when the item is held in the player's hand or placed.
    /// Override for sprites whose artwork points along a different local axis than +Y.
    /// </summary>
    protected virtual Quaternion HeldRotationOffset => Quaternion.identity;

    /// <summary>
    /// Computes the final placement rotation from a base facing rotation.
    /// </summary>
    public Quaternion GetPlacementRotation(Quaternion baseRotation) => baseRotation * HeldRotationOffset;

    private void SnapToOwnerHand(ulong clientId)
    {
        Transform target = ResolveOwnerHandTarget(clientId);
        if (target != null)
        {
            transform.position = target.position;
            transform.rotation = target.rotation * HeldRotationOffset;
        }
    }

    protected virtual void Update()
    {
        // Follow the owner's hand/position smoothly across server and all remote clients
        if (ownerClientId.Value != ulong.MaxValue)
        {
            SnapToOwnerHand(ownerClientId.Value);
            return;
        }

        UpdateGroundDragMotion();
    }

    private void UpdateGroundDragMotion()
    {
        // Offline / local fallback
        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (isLocallyDragging)
            {
                MoveTowardDragTarget(localDragTarget);
            }
            return;
        }

        if (IsServer)
        {
            if (draggingClientId.Value == ulong.MaxValue) return;

            if (!IsOnGround || !IsPlayerAliveAndPresent(draggingClientId.Value))
            {
                EndServerDrag(null);
                return;
            }

            MoveTowardDragTarget(serverDragTarget);
            Vector2 currentPos = new Vector2(transform.position.x, transform.position.y);
            if ((syncedDragPosition.Value - currentPos).sqrMagnitude > 1e-8f)
            {
                syncedDragPosition.Value = currentPos;
            }
            return;
        }

        if (isLocallyDragging)
        {
            MoveTowardDragTarget(localDragTarget);
        }
        else if (draggingClientId.Value != ulong.MaxValue)
        {
            MoveTowardDragTarget(syncedDragPosition.Value);
        }
    }

    private void MoveTowardDragTarget(Vector2 targetPos)
    {
        Vector3 target3D = new Vector3(targetPos.x, targetPos.y, 0f);
        Vector3 nextPos = Vector3.Lerp(transform.position, target3D, Time.deltaTime * DragLerpSpeed);
        nextPos.z = 0f;
        transform.position = nextPos;
    }

    protected virtual void LateUpdate()
    {
        if (ownerClientId.Value != ulong.MaxValue)
        {
            SnapToOwnerHand(ownerClientId.Value);
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
        if (ownerClientId.Value != ulong.MaxValue) return;

        Debug.Log($"Player {clientId} picked up {itemName}");
        ClearDragStateOnServer();
        ownerClientId.Value = clientId;
        isPlaced.Value = false;
        isInHand.Value = true;
        cachedHandOwnerId = ulong.MaxValue;
        cachedHandTarget = null;
        SnapToOwnerHand(clientId);
        SyncPickupStateClientRpc(clientId, true, ++serverEquipSeq);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public virtual void DropItemRpc(Vector3 dropPosition)
    {
        Debug.Log($"Player dropped {itemName}");
        ClearDragStateOnServer();
        ownerClientId.Value = ulong.MaxValue;
        isPlaced.Value = false;
        isInHand.Value = true; // Dropped items on floor should be visible
        cachedHandOwnerId = ulong.MaxValue;
        cachedHandTarget = null;

        Vector3 finalPos = new Vector3(dropPosition.x, dropPosition.y, 0f);
        transform.position = finalPos;
        syncedDragPosition.Value = new Vector2(finalPos.x, finalPos.y);
        SyncDroppedTransformClientRpc(finalPos, transform.rotation, ++serverEquipSeq);
    }

    /// <summary>
    /// Validates whether the item can be placed at the target position.
    /// Override in derived classes to enforce placement rules (e.g. Ritual Chalk in Ghost Room).
    /// </summary>
    public virtual bool CanPlace(Vector3 targetPosition) => true;

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public virtual void PlaceItemRpc(Vector3 placePosition, Quaternion placeRotation)
    {
        if (!CanPlace(placePosition))
        {
            Debug.Log($"[EquipmentItem] Placement rejected for {itemName} at {placePosition}: validation failed.");
            return;
        }

        Debug.Log($"Player placed {itemName}");
        ClearDragStateOnServer();
        ownerClientId.Value = ulong.MaxValue;
        isPlaced.Value = true;
        isInHand.Value = true; // Placed items should be visible
        cachedHandOwnerId = ulong.MaxValue;
        cachedHandTarget = null;

        Vector3 finalPos = new Vector3(placePosition.x, placePosition.y, 0f);
        transform.position = finalPos;
        transform.rotation = placeRotation;
        syncedDragPosition.Value = new Vector2(finalPos.x, finalPos.y);
        SyncPlacedTransformClientRpc(finalPos, placeRotation, ++serverEquipSeq);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncPickupStateClientRpc(ulong clientId, bool inHand, int seq)
    {
        if (seq <= lastClientEquipSeq) return;
        lastClientEquipSeq = seq;
        isLocallyDragging = false;
        cachedHandOwnerId = ulong.MaxValue;
        cachedHandTarget = null;
        if (interactCollider != null) interactCollider.enabled = false;
        SnapToOwnerHand(clientId);
        ApplySyncedScale();
        UpdateVisuals();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncDroppedTransformClientRpc(Vector3 pos, Quaternion rot, int seq)
    {
        if (seq <= lastClientEquipSeq) return;
        lastClientEquipSeq = seq;
        isLocallyDragging = false;
        cachedHandOwnerId = ulong.MaxValue;
        cachedHandTarget = null;
        if (interactCollider != null) interactCollider.enabled = true;
        transform.position = pos;
        transform.rotation = rot;
        ApplySyncedScale();
        UpdateVisuals();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncPlacedTransformClientRpc(Vector3 pos, Quaternion rot, int seq)
    {
        if (seq <= lastClientEquipSeq) return;
        lastClientEquipSeq = seq;
        isLocallyDragging = false;
        cachedHandOwnerId = ulong.MaxValue;
        cachedHandTarget = null;
        if (interactCollider != null) interactCollider.enabled = true;
        transform.position = pos;
        transform.rotation = rot;
        ApplySyncedScale();
        UpdateVisuals();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetInHandRpc(bool inHand)
    {
        isInHand.Value = inHand;
        SyncInHandStateClientRpc(inHand, ++serverEquipSeq);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncInHandStateClientRpc(bool inHand, int seq)
    {
        if (seq <= lastClientEquipSeq) return;
        lastClientEquipSeq = seq;
        if (inHand && ownerClientId.Value != ulong.MaxValue)
        {
            SnapToOwnerHand(ownerClientId.Value);
        }
        UpdateVisuals();
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
        cachedHandOwnerId = ulong.MaxValue;
        cachedHandTarget = null;

        bool onGroundOrPlaced = (newOwnerId == ulong.MaxValue);
        if (interactCollider != null)
        {
            interactCollider.enabled = onGroundOrPlaced;
        }

        if (!onGroundOrPlaced)
        {
            SnapToOwnerHand(newOwnerId);
        }

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
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
                    TriggerEquipmentTutorial();
                }
            }
        }

        UpdateVisuals();
    }

    protected virtual void OnInHandChanged(bool inHand)
    {
        UpdateVisuals();
        if (inHand && IsHeldByLocalPlayer)
        {
            TriggerEquipmentTutorial();
        }
    }

    public virtual void TriggerEquipmentTutorial()
    {
        if (this is FlashlightItem) TutorialTooltipManager.TriggerHint(TutorialHintId.Flashlight);
        else if (this is EMFReaderItem) TutorialTooltipManager.TriggerHint(TutorialHintId.EMFReader);
        else if (this is SpiritBoxItem) TutorialTooltipManager.TriggerHint(TutorialHintId.SpiritBox);
        else if (this is ThermometerItem) TutorialTooltipManager.TriggerHint(TutorialHintId.Thermometer);
        else if (this is GhostWritingBookItem) TutorialTooltipManager.TriggerHint(TutorialHintId.WritingBook);
        else if (this is VideoCameraItem) TutorialTooltipManager.TriggerHint(TutorialHintId.VideoCamera);
        else if (this is RitualChalkItem) TutorialTooltipManager.TriggerHint(TutorialHintId.RitualChalk);
        else if (this is CrucifixItem) TutorialTooltipManager.TriggerHint(TutorialHintId.Crucifix);
        else if (this is SanityPillsItem) TutorialTooltipManager.TriggerHint(TutorialHintId.SanityPills);
        else
        {
            TutorialTooltipManager.TriggerCustomHint(
                $"EQUIPMENT: {itemName.ToUpper()}",
                "[L-CLICK] / [R-CLICK]",
                "Left-Click to turn on or activate. Right-Click to place it down on the floor.",
                $"equip_{itemName.Replace(" ", "")}"
            );
        }
    }

    protected virtual void OnPlacedChanged(bool placed)
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
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
    public virtual bool CanDrag()
    {
        if (!IsOnGround) return false;
        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return true;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        return draggingClientId.Value == ulong.MaxValue || draggingClientId.Value == localClientId;
    }

    public void OnDragBegin(ulong clientId)
    {
        if (!CanDrag()) return;

        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            isLocallyDragging = true;
            localDragTarget = transform.position;
            ApplyDragBegin();
            return;
        }

        if (IsServer)
        {
            TryBeginServerDrag(NetworkManager.Singleton.LocalClientId, fromRpc: false);
            return;
        }

        isLocallyDragging = true;
        localDragTarget = transform.position;
        lastSentDragTarget = localDragTarget;
        nextDragRpcTime = 0f;
        ApplyDragBegin();
        DragBeginRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DragBeginRpc(RpcParams rpcParams = default)
    {
        TryBeginServerDrag(rpcParams.Receive.SenderClientId, fromRpc: true);
    }

    private void TryBeginServerDrag(ulong senderClientId, bool fromRpc)
    {
        bool canBegin = IsOnGround &&
                        (draggingClientId.Value == ulong.MaxValue || draggingClientId.Value == senderClientId) &&
                        IsPlayerAliveAndPresent(senderClientId);

        if (!canBegin)
        {
            if (fromRpc)
            {
                Vector3 authPos = new Vector3(transform.position.x, transform.position.y, 0f);
                SyncDragRejectedClientRpc(senderClientId, authPos);
            }
            return;
        }

        Vector2 currentPos = new Vector2(transform.position.x, transform.position.y);
        draggingClientId.Value = senderClientId;
        serverDragTarget = currentPos;
        syncedDragPosition.Value = currentPos;

        if (NetworkManager.Singleton != null && senderClientId == NetworkManager.Singleton.LocalClientId)
        {
            isLocallyDragging = true;
            localDragTarget = currentPos;
        }

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
        if (!IsOnGround) return;

        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            if (isLocallyDragging) localDragTarget = targetPos;
            return;
        }

        if (IsServer)
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            if (draggingClientId.Value == localClientId)
            {
                Vector2 clamped = ClampDragTargetToSender(localClientId, targetPos);
                serverDragTarget = clamped;
                localDragTarget = clamped;
            }
            return;
        }

        if (!isLocallyDragging) return;

        localDragTarget = targetPos;
        if (Time.unscaledTime >= nextDragRpcTime &&
            (targetPos - lastSentDragTarget).sqrMagnitude >= DragRpcMinDeltaSqr)
        {
            lastSentDragTarget = targetPos;
            nextDragRpcTime = Time.unscaledTime + DragRpcSendInterval;
            DragUpdateRpc(targetPos);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DragUpdateRpc(Vector2 targetPos, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!IsOnGround || draggingClientId.Value != senderClientId) return;

        if (!IsPlayerAliveAndPresent(senderClientId))
        {
            EndServerDrag(null);
            return;
        }

        serverDragTarget = ClampDragTargetToSender(senderClientId, targetPos);
    }

    public void OnDragEnd(ulong clientId)
    {
        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            isLocallyDragging = false;
            transform.position = new Vector3(transform.position.x, transform.position.y, 0f);
            ApplyDragEnd();
            return;
        }

        if (IsServer)
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            isLocallyDragging = false;
            if (draggingClientId.Value == localClientId)
            {
                EndServerDrag(localDragTarget);
            }
            return;
        }

        bool wasDraggingLocally = isLocallyDragging;
        isLocallyDragging = false;
        ApplyDragEnd();
        if (wasDraggingLocally)
        {
            DragEndRpc(localDragTarget);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DragEndRpc(Vector2 finalTargetPos, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (draggingClientId.Value != senderClientId) return;

        Vector2? clampedFinal = IsOnGround ? ClampDragTargetToSender(senderClientId, finalTargetPos) : (Vector2?)null;
        EndServerDrag(clampedFinal);
    }

    private void EndServerDrag(Vector2? finalTargetPos)
    {
        if (finalTargetPos.HasValue && IsOnGround)
        {
            serverDragTarget = finalTargetPos.Value;
            MoveTowardDragTarget(serverDragTarget);
        }

        ClearDragStateOnServer();

        Vector3 finalPos = new Vector3(transform.position.x, transform.position.y, 0f);
        transform.position = finalPos;
        syncedDragPosition.Value = new Vector2(finalPos.x, finalPos.y);
        ApplyDragEnd();
        SyncDragEndClientRpc(finalPos, ++serverEquipSeq);
    }

    private void ClearDragStateOnServer()
    {
        if (IsServer && IsSpawned)
        {
            draggingClientId.Value = ulong.MaxValue;
        }
        isLocallyDragging = false;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncDragEndClientRpc(Vector3 finalPos, int seq)
    {
        if (seq <= lastClientEquipSeq) return;
        lastClientEquipSeq = seq;
        isLocallyDragging = false;
        finalPos.z = 0f;
        transform.position = finalPos;
        ApplyDragEnd();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncDragRejectedClientRpc(ulong rejectedClientId, Vector3 authoritativePos)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClientId != rejectedClientId) return;
        isLocallyDragging = false;
        authoritativePos.z = 0f;
        transform.position = authoritativePos;
        ApplyDragEnd();
    }

    private bool IsPlayerAliveAndPresent(ulong clientId)
    {
        Transform playerTr = ResolvePlayerTransform(clientId);
        if (playerTr == null) return false;

        var playerCtrl = playerTr.GetComponent<PlayerController>();
        return playerCtrl == null || playerCtrl.IsAlive;
    }

    private Vector2 ClampDragTargetToSender(ulong senderClientId, Vector2 targetPos)
    {
        Transform playerTr = ResolvePlayerTransform(senderClientId);
        if (playerTr == null) return targetPos;

        var playerCtrl = playerTr.GetComponent<PlayerController>();
        float maxReach = playerCtrl != null ? playerCtrl.MaxReachRadius : 2.5f;
        float allowedReach = maxReach + 0.08f;

        Vector2 playerPos = playerTr.position;
        Vector2 offset = targetPos - playerPos;
        float dist = offset.magnitude;
        if (dist > allowedReach && dist > 1e-6f)
        {
            return playerPos + (offset / dist) * allowedReach;
        }

        return targetPos;
    }

    private void ApplyDragEnd()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }
}
