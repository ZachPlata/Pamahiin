using Unity.Netcode;
using UnityEngine;

public class NetworkDoor : NetworkBehaviour, IInteractable
{
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isLocked = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Door Settings")]
    [SerializeField] private float openAngle = 90f; // How far the door swings
    [SerializeField] private float swingSpeed = 5f; // How fast it opens
    [SerializeField] private bool isExitDoor = false; // Hunt locks exit doors

    public bool IsOpen => isOpen.Value;
    public bool IsLocked => isLocked.Value;
    public bool IsExitDoor => isExitDoor;

    private Quaternion closedRotation;
    private Quaternion targetOpenRotation;

    private void Awake()
    {
        closedRotation = transform.rotation;
        targetOpenRotation = closedRotation * Quaternion.Euler(0, 0, openAngle);
    }

    private void Update()
    {
        // Smoothly rotate hinge towards target angle
        Quaternion targetRotation = isOpen.Value ? targetOpenRotation : closedRotation;
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * swingSpeed);
    }

    public void Interact()
    {
        if (isLocked.Value)
        {
            // Door is locked (e.g. during a Hunt)
            return;
        }

        if (IsServer)
        {
            ToggleDoor();
        }
        else
        {
            ToggleDoorRpc();
        }
    }

    private void ToggleDoor()
    {
        isOpen.Value = !isOpen.Value;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ToggleDoorRpc()
    {
        if (isLocked.Value) return;
        ToggleDoor();
    }

    /// <summary>
    /// Called by the Ghost AI on the server to slam, close, or open a door.
    /// Emits an EMF level 2 paranormal event.
    /// </summary>
    public void GhostInteract(bool? forceOpen = null)
    {
        if (!IsServer || isLocked.Value) return;

        isOpen.Value = forceOpen.HasValue ? forceOpen.Value : !isOpen.Value;

        if (ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(transform.position, 2, 25f);
        }
    }

    /// <summary>
    /// Locks or unlocks the door (used by Ghost Hunt Manifestation phase).
    /// </summary>
    public void SetLocked(bool locked)
    {
        if (!IsServer) return;
        isLocked.Value = locked;
        if (locked)
        {
            isOpen.Value = false; // Closed when locked
        }
    }

    public string GetInteractText()
    {
        if (isLocked.Value) return "Locked";
        return isOpen.Value ? "Close Door" : "Open Door";
    }
}