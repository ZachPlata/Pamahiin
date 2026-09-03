using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    public const int MaxSlots = 3;
    public EquipmentItem[] slots = new EquipmentItem[MaxSlots];
    public int currentSlotIndex = 0;

    public EquipmentItem CurrentItem => (currentSlotIndex >= 0 && currentSlotIndex < MaxSlots) ? slots[currentSlotIndex] : null;

    private void Update()
    {
        if (!IsOwner) return;

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

        EquipmentItem currentItem = CurrentItem;

        // Primary Use (Left Click)
        if (Input.GetMouseButtonDown(0) && currentItem != null)
        {
            currentItem.UsePrimary();
        }

        // Secondary Use (Right Click)
        if (Input.GetMouseButtonDown(1) && currentItem != null)
        {
            currentItem.UseSecondary();
        }

        // Drop current item (G)
        if (Input.GetKeyDown(KeyCode.G) && currentItem != null)
        {
            currentItem.DropItemRpc();
            slots[currentSlotIndex] = null;
        }
    }

    public void SwitchSlot(int newSlot)
    {
        if (newSlot < 0 || newSlot >= MaxSlots) return;
        if (currentSlotIndex == newSlot) return;

        // Put away previously held item
        if (slots[currentSlotIndex] != null)
        {
            slots[currentSlotIndex].SetInHandRpc(false);
        }

        currentSlotIndex = newSlot;

        // Bring out new item
        if (slots[currentSlotIndex] != null)
        {
            slots[currentSlotIndex].SetInHandRpc(true);
        }
    }

    public bool HasEmptySlot()
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (slots[i] == null) return true;
        }
        return false;
    }

    public bool AddItem(EquipmentItem item)
    {
        if (item == null) return false;

        for (int i = 0; i < MaxSlots; i++)
        {
            if (slots[i] == null)
            {
                slots[i] = item;
                bool isInHand = (i == currentSlotIndex);
                item.SetInHandRpc(isInHand);
                return true;
            }
        }

        return false;
    }

    public void RemoveItem(EquipmentItem item)
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (slots[i] == item)
            {
                slots[i] = null;
                return;
            }
        }
    }
}