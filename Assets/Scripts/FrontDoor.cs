using Unity.Netcode;
using UnityEngine;

public class FrontDoor : NetworkBehaviour
{
    [Header("Door Settings")]
    public float openAngle = 90f; // Degrees to swing open
    public float swingSpeed = 5f;
    public GameObject ghostToActivate;

    // Track if the door has been permanently unlocked
    public NetworkVariable<bool> isUnlocked = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // Track if the door is currently swung open or closed
    public NetworkVariable<bool> isDoorOpen = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Quaternion closedRotation;
    private Quaternion openRotation;
    
    private bool isLocalPlayerTouching = false;
    private PlayerInventory localPlayerInventory;

    void Awake()
    {
        closedRotation = transform.rotation;
        openRotation = closedRotation * Quaternion.Euler(0, 0, openAngle); 
    }

    void Update()
    {
        // Smoothly rotate the door
        Quaternion targetRotation = isDoorOpen.Value ? openRotation : closedRotation;
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * swingSpeed);

        // Interaction Logic
        if (isLocalPlayerTouching && Input.GetKeyDown(KeyCode.E))
        {
            if (!isUnlocked.Value) // If it's locked...
            {
                if (localPlayerInventory != null && localPlayerInventory.hasFrontDoorKey)
                {
                    InteractDoorRpc(true); // Unlock and open it!
                }
                else
                {
                    Debug.Log("The door is locked. You need the Front Door Key.");
                }
            }
            else // If it's already unlocked...
            {
                InteractDoorRpc(false); // Just toggle open/close
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            NetworkObject netObj = collision.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsLocalPlayer)
            {
                isLocalPlayerTouching = true;
                localPlayerInventory = collision.GetComponent<PlayerInventory>();
            }
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            NetworkObject netObj = collision.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsLocalPlayer)
            {
                isLocalPlayerTouching = false;
                localPlayerInventory = null;
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void InteractDoorRpc(bool isUnlocking)
    {
        if (isUnlocking && !isUnlocked.Value)
        {
            // First time using the key
            isUnlocked.Value = true;
            isDoorOpen.Value = true; // Swing it open

            if (ghostToActivate != null)
            {
                ghostToActivate.SetActive(true); // Trigger the ghost scare
            }
        }
        else if (isUnlocked.Value)
        {
            // Just toggle the door open or closed
            isDoorOpen.Value = !isDoorOpen.Value;
        }
    }
}