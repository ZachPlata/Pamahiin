using Unity.Netcode;
using UnityEngine;

public class FrontDoor : NetworkBehaviour
{
    public GameObject ghostToActivate; 

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsServer) return;

        // Check if a player touched the door
        if (collision.CompareTag("Player"))
        {
            PlayerInventory inventory = collision.GetComponent<PlayerInventory>();
            
            if (inventory != null && inventory.hasFrontDoorKey)
            {
                OpenDoorRpc();
            }
        }
    }

    [Rpc(SendTo.Everyone)]
    private void OpenDoorRpc()
    {
        GetComponent<SpriteRenderer>().enabled = false;
        GetComponent<BoxCollider2D>().enabled = false;

        if (ghostToActivate != null)
        {
            ghostToActivate.SetActive(true);
        }
    }
}