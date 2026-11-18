using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    public const int MaxSlots = 3;
    public EquipmentItem[] slots = new EquipmentItem[MaxSlots];
    public int currentSlotIndex = 0;

    public event Action<int> OnSlotChanged;
    public event Action OnInventoryUpdated;

    private NetworkVariable<int> syncedSlotIndex = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> slot0NetId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> slot1NetId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> slot2NetId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private int? pendingOwnerSlotTarget;

    private static bool IsNetworkListening =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    public static bool HasActiveHeldEquipment(PlayerController player) =>
        MatchesActiveHeldEquipment(player, flashlightOnly: false);

    public static bool HasActiveHeldFlashlight(PlayerController player) =>
        MatchesActiveHeldEquipment(player, flashlightOnly: true);

    private static bool MatchesActiveHeldEquipment(PlayerController player, bool flashlightOnly)
    {
        if (player == null || !player.IsAlive) return false;

        var inv = player.GetComponent<PlayerInventory>();
        if (inv != null)
        {
            if (inv.CurrentItem != null)
            {
                if (flashlightOnly)
                {
                    if (inv.CurrentItem is FlashlightItem fl && fl.IsLightOn) return true;
                }
                else if (inv.CurrentItem.IsPoweredOn)
                {
                    return true;
                }
            }

            if (inv.slots != null)
            {
                for (int i = 0; i < inv.slots.Length; i++)
                {
                    if (inv.slots[i] is FlashlightItem slotFl && slotFl.IsLightOn)
                    {
                        return true;
                    }
                }
            }
        }

        bool netListening = IsNetworkListening;
        if (netListening && !player.IsSpawned) return false;

        ulong clientId = player.OwnerClientId;
        var items = EquipmentItem.AllEquipment;
        for (int i = 0; i < items.Count; i++)
        {
            var eq = items[i];
            if (eq == null) continue;
            if (netListening && !eq.IsSpawned) continue;
            if (eq.IsOnGround || eq.IsPlaced ||
                eq.CurrentHolderClientId == ulong.MaxValue ||
                eq.CurrentHolderClientId != clientId)
            {
                continue;
            }

            if (eq is FlashlightItem fl)
            {
                if (fl.IsLightOn) return true;
                continue;
            }

            if (!eq.IsInHand) continue;

            if (!flashlightOnly && eq.IsPoweredOn)
            {
                return true;
            }
        }

        return false;
    }

    public EquipmentItem CurrentItem
    {
        get
        {
            if (currentSlotIndex >= 0 && currentSlotIndex < MaxSlots)
            {
                var item = slots[currentSlotIndex];
                if (item == null)
                {
                    slots[currentSlotIndex] = null;
                    return null;
                }
                return item;
            }
            return null;
        }
    }

    private PlayerController playerController;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        syncedSlotIndex.OnValueChanged += OnSyncedSlotIndexChanged;
        slot0NetId.OnValueChanged += OnSyncedSlotItemChanged;
        slot1NetId.OnValueChanged += OnSyncedSlotItemChanged;
        slot2NetId.OnValueChanged += OnSyncedSlotItemChanged;
        SyncSlotsFromNetworkVariables();
    }

    public override void OnNetworkDespawn()
    {
        syncedSlotIndex.OnValueChanged -= OnSyncedSlotIndexChanged;
        slot0NetId.OnValueChanged -= OnSyncedSlotItemChanged;
        slot1NetId.OnValueChanged -= OnSyncedSlotItemChanged;
        slot2NetId.OnValueChanged -= OnSyncedSlotItemChanged;
        pendingOwnerSlotTarget = null;
        base.OnNetworkDespawn();
    }

    private void OnSyncedSlotIndexChanged(int previousValue, int newValue)
    {
        SyncSlotsFromNetworkVariables();
    }

    private void OnSyncedSlotItemChanged(ulong previousValue, ulong newValue)
    {
        SyncSlotsFromNetworkVariables();
    }

    private ulong GetSlotNetId(int index)
    {
        switch (index)
        {
            case 0: return slot0NetId.Value;
            case 1: return slot1NetId.Value;
            case 2: return slot2NetId.Value;
            default: return ulong.MaxValue;
        }
    }

    private void SetSlotNetId(int index, ulong netId)
    {
        if (!IsServer || !IsSpawned) return;
        switch (index)
        {
            case 0:
                if (slot0NetId.Value != netId) slot0NetId.Value = netId;
                break;
            case 1:
                if (slot1NetId.Value != netId) slot1NetId.Value = netId;
                break;
            case 2:
                if (slot2NetId.Value != netId) slot2NetId.Value = netId;
                break;
        }
    }

    private static EquipmentItem ResolveEquipmentByNetId(ulong networkObjectId)
    {
        if (networkObjectId == ulong.MaxValue) return null;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var netObj) &&
            netObj != null)
        {
            return netObj.GetComponent<EquipmentItem>();
        }

        var all = EquipmentItem.AllEquipment;
        for (int i = 0; i < all.Count; i++)
        {
            var eq = all[i];
            if (eq != null && eq.IsSpawned && eq.NetworkObjectId == networkObjectId)
            {
                return eq;
            }
        }
        return null;
    }

    public void SyncSlotsFromNetworkVariables()
    {
        if (!IsSpawned) return;

        bool itemsChanged = false;
        for (int i = 0; i < MaxSlots; i++)
        {
            ulong netId = GetSlotNetId(i);
            EquipmentItem resolved = netId == ulong.MaxValue ? null : ResolveEquipmentByNetId(netId);
            if (!ReferenceEquals(slots[i], resolved))
            {
                slots[i] = resolved;
                itemsChanged = true;
            }
        }

        int serverSlot = Mathf.Clamp(syncedSlotIndex.Value, 0, MaxSlots - 1);
        bool slotChanged = false;

        if (IsOwner && !IsServer && pendingOwnerSlotTarget.HasValue)
        {
            if (serverSlot == pendingOwnerSlotTarget.Value)
            {
                pendingOwnerSlotTarget = null;
                if (currentSlotIndex != serverSlot)
                {
                    currentSlotIndex = serverSlot;
                    slotChanged = true;
                }
            }
        }
        else if (currentSlotIndex != serverSlot)
        {
            currentSlotIndex = serverSlot;
            slotChanged = true;
        }

        if (slotChanged) OnSlotChanged?.Invoke(currentSlotIndex);
        if (itemsChanged) OnInventoryUpdated?.Invoke();
    }

    private bool CanOperateInventoryAsAlive() => playerController == null || playerController.IsAlive;
    private bool CanOperateGroundGearSlots() => playerController == null || playerController.IsAlive || playerController.IsGhostSpectator;

    private void Update()
    {
        if (!IsOwner) return;
        if (PlayerController.IsInteractionBlocked || !CanOperateGroundGearSlots()) return;

        // Slot selection via numeric hotkeys (1, 2, 3)
        if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchSlot(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchSlot(2);

        // Scroll wheel slot cycling
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (scroll > 0f)
        {
            SwitchSlot((currentSlotIndex - 1 + MaxSlots) % MaxSlots);
        }
        else if (scroll < 0f)
        {
            SwitchSlot((currentSlotIndex + 1) % MaxSlots);
        }
    }

    public void UsePrimary()
    {
        if (!CanOperateInventoryAsAlive()) return;
        if (CurrentItem != null) CurrentItem.UsePrimary();
    }

    public bool ToggleFlashlight()
    {
        if (!CanOperateInventoryAsAlive()) return false;

        if (CurrentItem is FlashlightItem activeFlashlight)
        {
            activeFlashlight.UsePrimary();
            return true;
        }

        for (int i = 0; i < MaxSlots; i++)
        {
            if (slots[i] is FlashlightItem stowedFlashlight)
            {
                stowedFlashlight.UsePrimary();
                return true;
            }
        }

        return false;
    }

    public void UseSecondary()
    {
        if (!CanOperateInventoryAsAlive()) return;
        if (CurrentItem != null) CurrentItem.UseSecondary();
    }

    public void DropCurrentItem(Vector3 dropPosition)
    {
        if (!CanOperateGroundGearSlots()) return;
        if (CurrentItem != null)
        {
            var itemToDrop = CurrentItem;
            Vector3 underPlayerDrop = new Vector3(dropPosition.x, dropPosition.y, 0f);
            if (IsNetworkListening && itemToDrop.IsSpawned)
            {
                itemToDrop.DropItemRpc(underPlayerDrop);
            }
            else
            {
                itemToDrop.ServerDropAtPosition(underPlayerDrop);
            }
            slots[currentSlotIndex] = null;
            OnInventoryUpdated?.Invoke();
        }
    }

    public void DropAllItems(Vector3 dropPosition)
    {
        bool droppedAny = false;
        int wallMask = LayerMask.GetMask("Walls");
        for (int i = 0; i < MaxSlots; i++)
        {
            if (slots[i] != null)
            {
                Vector3 safePos = ComputeTightDropPosition(dropPosition, i, wallMask);
                slots[i].DropItemRpc(safePos);
                slots[i] = null;
                droppedAny = true;
            }
        }
        if (droppedAny) OnInventoryUpdated?.Invoke();
    }

    /// <summary>
    /// Server-authoritative inventory drop on player death: scatters held equipment strictly within
    /// 0.06..0.10 world units across a tight 120-degree fan, clamped against Walls so items never pierce geometry.
    /// Also resets the owner's local inventory slots.
    /// </summary>
    public void ServerDropAllItemsOnDeath(Vector3 deathPosition)
    {
        int wallMask = LayerMask.GetMask("Walls");
        int dropIdx = 0;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            if (!IsServer) return;

            for (int i = EquipmentItem.AllEquipment.Count - 1; i >= 0; i--)
            {
                var eq = EquipmentItem.AllEquipment[i];
                if (eq != null && !eq.IsOnGround && !eq.IsPlaced && eq.CurrentHolderClientId == OwnerClientId)
                {
                    Vector3 safePos = ComputeTightDropPosition(deathPosition, dropIdx++, wallMask);
                    eq.ServerDropAtPosition(safePos);
                }
            }
            ServerClearAllSlots();
            ClearInventoryClientRpc();
            if (IsOwner) ClearAllSlots();
            return;
        }

        // Offline / unspawned fallback
        DropAllItems(deathPosition);
    }

    [Rpc(SendTo.Owner)]
    public void ClearInventoryClientRpc()
    {
        ClearAllSlots();
    }

    /// <summary>
    /// Computes a deterministic, wall-clamped drop coordinate inside a tight 120-degree fan (0.065..0.095 world units)
    /// scaled accurately to the 0.0435 player radius and 0.3 camera orthoSize.
    /// </summary>
    public static Vector3 ComputeTightDropPosition(Vector3 origin, int slotIndex, int wallLayerMask)
    {
        float[] fanAnglesDeg = { -60f, 0f, 60f };
        float angleRad = fanAnglesDeg[Mathf.Abs(slotIndex) % fanAnglesDeg.Length] * Mathf.Deg2Rad - Mathf.PI * 0.5f;
        Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)).normalized;
        float desiredDist = 0.075f + (slotIndex % 2) * 0.015f; // 0.075..0.090 (within 0.06..0.10 reach)

        if (wallLayerMask != 0)
        {
            RaycastHit2D hit = Physics2D.Raycast(origin, dir, desiredDist + 0.02f, wallLayerMask);
            if (hit.collider != null)
            {
                desiredDist = Mathf.Max(0.015f, hit.distance - 0.015f);
            }
        }

        return new Vector3(origin.x + dir.x * desiredDist, origin.y + dir.y * desiredDist, 0f);
    }

    public void PlaceCurrentItem(Vector3 placePosition, Quaternion placeRotation)
    {
        if (!CanOperateInventoryAsAlive()) return;
        if (CurrentItem != null)
        {
            Vector3 finalPos = new Vector3(placePosition.x, placePosition.y, 0f);
            if (!CurrentItem.CanPlace(finalPos))
            {
                return;
            }

            Quaternion finalRot = CurrentItem.GetPlacementRotation(placeRotation);
            CurrentItem.transform.position = finalPos;
            CurrentItem.transform.rotation = finalRot;
            CurrentItem.PlaceItemRpc(finalPos, finalRot);
            slots[currentSlotIndex] = null;
            OnInventoryUpdated?.Invoke();
        }
    }

    public void SwitchSlot(int newSlot)
    {
        if (newSlot < 0 || newSlot >= MaxSlots) return;
        if (currentSlotIndex == newSlot) return;

        if (IsSpawned && IsNetworkListening)
        {
            if (IsServer)
            {
                ServerPerformSwitchSlot(newSlot);
            }
            else if (IsOwner)
            {
                currentSlotIndex = newSlot;
                pendingOwnerSlotTarget = newSlot;
                OnSlotChanged?.Invoke(currentSlotIndex);
                SwitchSlotServerRpc(newSlot);
            }
            return;
        }

        // Offline / unspawned fallback
        if (slots[currentSlotIndex] != null)
        {
            slots[currentSlotIndex].ServerSetInHand(false);
        }

        currentSlotIndex = newSlot;

        if (slots[currentSlotIndex] != null)
        {
            slots[currentSlotIndex].ServerSetInHand(true);
        }

        OnSlotChanged?.Invoke(currentSlotIndex);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SwitchSlotServerRpc(int newSlot, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        ServerPerformSwitchSlot(newSlot);
    }

    private void ServerPerformSwitchSlot(int newSlot)
    {
        if (newSlot < 0 || newSlot >= MaxSlots) return;
        if (!CanOperateGroundGearSlots()) return;
        if (currentSlotIndex == newSlot && (!IsSpawned || syncedSlotIndex.Value == newSlot)) return;

        int prevSlot = currentSlotIndex;
        if (prevSlot >= 0 && prevSlot < MaxSlots && slots[prevSlot] != null)
        {
            slots[prevSlot].ServerSetInHand(false);
        }

        currentSlotIndex = newSlot;
        if (IsServer && IsSpawned && syncedSlotIndex.Value != newSlot)
        {
            syncedSlotIndex.Value = newSlot;
        }

        for (int i = 0; i < MaxSlots; i++)
        {
            if (slots[i] == null) continue;
            bool shouldBeInHand = i == currentSlotIndex;
            if (i == currentSlotIndex || slots[i].IsInHand)
            {
                slots[i].ServerSetInHand(shouldBeInHand);
            }
        }

        OnSlotChanged?.Invoke(currentSlotIndex);
    }

    public bool HasEmptySlot()
    {
        return slots[currentSlotIndex] == null;
    }

    public bool CanAssignItem(EquipmentItem item)
    {
        if (item == null) return false;
        for (int i = 0; i < MaxSlots; i++)
        {
            if (slots[i] == item || (object)slots[i] == (object)item || slots[i] == null)
            {
                return true;
            }
        }
        return false;
    }

    public bool ServerAssignItemToSlot(EquipmentItem item)
    {
        if (item == null) return false;
        if (IsNetworkListening && !IsServer) return false;

        for (int i = 0; i < MaxSlots; i++)
        {
            if (slots[i] == item || (object)slots[i] == (object)item)
            {
                if (IsServer && IsSpawned && item.IsSpawned)
                {
                    SetSlotNetId(i, item.NetworkObjectId);
                    if (syncedSlotIndex.Value != currentSlotIndex)
                    {
                        syncedSlotIndex.Value = currentSlotIndex;
                    }
                    item.ServerSetInHand(i == currentSlotIndex);
                }
                return true;
            }
        }

        int targetSlot = -1;
        if (currentSlotIndex >= 0 && currentSlotIndex < MaxSlots && slots[currentSlotIndex] == null)
        {
            targetSlot = currentSlotIndex;
        }
        else
        {
            for (int i = 0; i < MaxSlots; i++)
            {
                if (slots[i] == null)
                {
                    targetSlot = i;
                    break;
                }
            }
        }

        if (targetSlot < 0) return false;

        slots[targetSlot] = item;
        bool shouldBeInHand = targetSlot == currentSlotIndex;
        if (IsServer && IsSpawned && item.IsSpawned)
        {
            SetSlotNetId(targetSlot, item.NetworkObjectId);
            if (syncedSlotIndex.Value != currentSlotIndex)
            {
                syncedSlotIndex.Value = currentSlotIndex;
            }
            item.ServerSetInHand(shouldBeInHand);
        }
        else
        {
            item.ServerSetInHand(shouldBeInHand);
        }

        OnInventoryUpdated?.Invoke();
        return true;
    }

    public bool AddItem(EquipmentItem item)
    {
        if (item == null || !CanOperateGroundGearSlots()) return false;

        if (!CanOperateInventoryAsAlive())
        {
            bool heldByThisOwner = item.CurrentHolderClientId != ulong.MaxValue && item.CurrentHolderClientId == OwnerClientId;
            if (item.IsPlaced || (!item.IsOnGround && !heldByThisOwner) || item is SanityPillsItem || item is RitualChalkItem)
            {
                return false;
            }
        }

        // Idempotent if already synced or assigned to a slot
        for (int i = 0; i < MaxSlots; i++)
        {
            if (slots[i] == item || (object)slots[i] == (object)item)
            {
                if (i == currentSlotIndex)
                {
                    OnInventoryUpdated?.Invoke();
                }
                return true;
            }
        }

        if (slots[currentSlotIndex] == null)
        {
            slots[currentSlotIndex] = item;
            if (!item.IsInHand)
            {
                if (IsSpawned && IsNetworkListening && item.IsSpawned)
                {
                    item.SetInHandRpc(true);
                }
                else
                {
                    item.ServerSetInHand(true);
                }
            }
            if (CanOperateInventoryAsAlive())
            {
                item.TriggerEquipmentTutorial();
            }
            OnInventoryUpdated?.Invoke();
            return true;
        }

        return false;
    }

    public void ServerRemoveItem(EquipmentItem item)
    {
        RemoveItem(item);
    }

    public void RemoveItem(EquipmentItem item)
    {
        if (item == null) return;

        bool canWriteNetVars = IsServer && IsSpawned && IsNetworkListening && !NetworkManager.Singleton.ShutdownInProgress;
        bool removed = false;

        for (int i = 0; i < MaxSlots; i++)
        {
            if (slots[i] == item || (object)slots[i] == (object)item)
            {
                slots[i] = null;
                if (canWriteNetVars) SetSlotNetId(i, ulong.MaxValue);
                removed = true;
            }
            else if (canWriteNetVars && item.IsSpawned && GetSlotNetId(i) == item.NetworkObjectId)
            {
                SetSlotNetId(i, ulong.MaxValue);
                removed = true;
            }
        }

        if (removed)
        {
            OnInventoryUpdated?.Invoke();
        }
    }

    public void ServerClearAllSlots()
    {
        bool canWriteNetVars = IsServer && IsSpawned && IsNetworkListening && !NetworkManager.Singleton.ShutdownInProgress;
        for (int i = 0; i < MaxSlots; i++)
        {
            slots[i] = null;
            if (canWriteNetVars) SetSlotNetId(i, ulong.MaxValue);
        }
        pendingOwnerSlotTarget = null;
        OnInventoryUpdated?.Invoke();
    }

    public void ClearAllSlots()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            slots[i] = null;
        }
        pendingOwnerSlotTarget = null;
        OnInventoryUpdated?.Invoke();
    }
}