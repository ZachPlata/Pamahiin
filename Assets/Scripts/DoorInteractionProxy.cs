using UnityEngine;

/// <summary>
/// Attached to the runtime door visual object to forward IInteractable calls to NetworkDoor.
/// </summary>
public class DoorInteractionProxy : MonoBehaviour, IInteractable
{
    [SerializeField] private NetworkDoor targetDoor;

    public void Setup(NetworkDoor door)
    {
        targetDoor = door;
    }

    public void Interact()
    {
        if (targetDoor != null)
        {
            targetDoor.Interact();
        }
    }

    public string GetInteractText()
    {
        return targetDoor != null ? targetDoor.GetInteractText() : "";
    }
}
