using Unity.Netcode;
using UnityEngine;

public class PlayerInventory : NetworkBehaviour
{
    public NetworkObject[] slots = new NetworkObject[3];
    public int currentSlotIndex = 0;

    private void Update()
    {
        if (!IsOwner) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchSlot(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchSlot(2);

        NetworkObject currentItem = slots[currentSlotIndex];

        if (Input.GetMouseButtonDown(0) && currentItem != null)
        {
            var flashlight = currentItem.GetComponent<FlashlightItem>();
            if (flashlight != null) flashlight.UseItemRpc();
        }

        if (Input.GetKeyDown(KeyCode.G) && currentItem != null)
        {
            var flashlight = currentItem.GetComponent<FlashlightItem>();
            if (flashlight != null) flashlight.DropItemRpc();
            
            slots[currentSlotIndex] = null; 
        }
    }

    private void SwitchSlot(int newSlot)
    {
        if (currentSlotIndex == newSlot) return;

        if (slots[currentSlotIndex] != null)
        {
            slots[currentSlotIndex].GetComponent<FlashlightItem>().SetInHandRpc(false);
        }

        currentSlotIndex = newSlot;

        if (slots[currentSlotIndex] != null)
        {
            slots[currentSlotIndex].GetComponent<FlashlightItem>().SetInHandRpc(true);
        }
    }

    public bool HasEmptySlot()
    {
        // Check if there is at least one null slot
        for (int i = 0; i < 3; i++)
        {
            if (slots[i] == null) return true;
        }
        return false;
    }

    public void AddItem(NetworkObject item)
    {
        for (int i = 0; i < 3; i++)
        {
            if (slots[i] == null)
            {
                slots[i] = item;
                bool isInHand = (i == currentSlotIndex);
                item.GetComponent<FlashlightItem>().SetInHandRpc(isInHand);
                return;
            }
        }
    }
}