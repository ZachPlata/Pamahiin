using UnityEngine;

/// <summary>
/// Attach this to a sprite in the scene to designate it as a potential ghost spawn room.
/// The sprite will be hidden at runtime.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class GhostRoomMarker : MonoBehaviour
{
    private Collider2D roomCollider;

    private void Awake()
    {
        roomCollider = GetComponent<Collider2D>();
        // Disable the sprite renderer so it doesn't appear in runtime
        var spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false;
        }
    }

    public Collider2D RoomCollider
    {
        get
        {
            if (roomCollider == null) roomCollider = GetComponent<Collider2D>();
            return roomCollider;
        }
    }

    public bool ContainsPoint(Vector2 point)
    {
        if (RoomCollider == null) return false;
        if (RoomCollider.OverlapPoint(point)) return true;

        Bounds b = RoomCollider.bounds;
        return point.x >= b.min.x && point.x <= b.max.x && point.y >= b.min.y && point.y <= b.max.y;
    }

    /// <summary>
    /// Validates whether a given world position is inside the active Ghost Room.
    /// </summary>
    public static bool IsInsideGhostRoom(Vector2 position)
    {
        var ghostHandler = Object.FindAnyObjectByType<GhostHandler>();
        if (ghostHandler != null && ghostHandler.ghostroomMarker != null)
        {
            if (ghostHandler.ghostroomMarker.OverlapPoint(position)) return true;
            Bounds b = ghostHandler.ghostroomMarker.bounds;
            return position.x >= b.min.x && position.x <= b.max.x && position.y >= b.min.y && position.y <= b.max.y;
        }

        var markers = Object.FindObjectsByType<GhostRoomMarker>(FindObjectsInactive.Exclude);
        foreach (var marker in markers)
        {
            if (marker != null && marker.ContainsPoint(position))
            {
                return true;
            }
        }
        return false;
    }
}
