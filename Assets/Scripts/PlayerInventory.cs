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
    }

    public void UsePrimary()
    {
        if (CurrentItem != null) CurrentItem.UsePrimary();
    }

    public void UseSecondary()
    {
        if (CurrentItem != null) CurrentItem.UseSecondary();
    }

    public void DropCurrentItem(Vector3 dropPosition)
    {
        if (CurrentItem != null)
        {
            CurrentItem.DropItemRpc(dropPosition);
            slots[currentSlotIndex] = null;
        }
    }

    public void DropAllItems(Vector3 dropPosition)
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (slots[i] != null)
            {
                slots[i].DropItemRpc(dropPosition + (Vector3)(Random.insideUnitCircle * 0.5f));
                slots[i] = null;
            }
        }
    }

    public void PlaceCurrentItem(Vector3 placePosition, Quaternion placeRotation)
    {
        if (CurrentItem != null)
        {
            CurrentItem.PlaceItemRpc(placePosition, placeRotation);
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