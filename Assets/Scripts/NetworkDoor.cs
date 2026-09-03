using Unity.Netcode;
using UnityEngine;

public class NetworkDoor : NetworkBehaviour, IInteractable
{
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Door Settings")]
    [SerializeField] private float openAngle = 90f; // How far the door swings
    [SerializeField] private float swingSpeed = 5f; // How fast it opens

    private Quaternion closedRotation;
    private Quaternion targetOpenRotation;

    private void Awake()
    {
        // Store the starting rotation as the "closed" state
        closedRotation = transform.rotation;
        
        // Calculate the target rotation (adding the openAngle to the Z axis)
        targetOpenRotation = closedRotation * Quaternion.Euler(0, 0, openAngle);
    }

    private void Update()
    {
        // Smoothly rotate the hinge towards the target angle every frame
        Quaternion targetRotation = isOpen.Value ? targetOpenRotation : closedRotation;
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * swingSpeed);
    }

    public void Interact()
    {
        if (IsServer)
        {
            isOpen.Value = !isOpen.Value;
        }
        else
        {
            ToggleDoorRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleDoorRpc()
    {
        isOpen.Value = !isOpen.Value;
    }

    public string GetInteractText()
    {
        return isOpen.Value ? "Close Door" : "Open Door";
    }
}