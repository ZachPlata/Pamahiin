using UnityEngine;

/// <summary>
/// Placed on low cover objects (desks, counters, beds).
/// When a player is inside this zone AND crouching, they are hidden from the Ghost's vision cone.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class HideZone : MonoBehaviour
{
    [Header("Hide Zone Settings")]
    [SerializeField] private string coverName = "Desk";

    public string CoverName => coverName;

    private void Awake()
    {
        // Ensure collider is a trigger
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var player = other.GetComponent<PlayerController>();
        if (player != null)
        {
            player.SetInHideZone(true, this);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var player = other.GetComponent<PlayerController>();
        if (player != null)
        {
            player.SetInHideZone(false, this);
        }
    }
}
