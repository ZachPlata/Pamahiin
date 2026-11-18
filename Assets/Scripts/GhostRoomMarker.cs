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
        if (ghostHandler == null || ghostHandler.IsExorcised || ghostHandler.ghostroomMarker == null)
        {
            return false;
        }

        if (ghostHandler.ghostroomMarker.OverlapPoint(position)) return true;
        Bounds b = ghostHandler.ghostroomMarker.bounds;
        return PointInBounds(position, b);
    }

    private static bool PointInBounds(Vector2 position, Bounds b)
    {
        return position.x >= b.min.x && position.x <= b.max.x && position.y >= b.min.y && position.y <= b.max.y;
    }

    private void DrawRoomOutlineGizmo(bool selected)
    {
        if (RoomCollider == null) return;

        Bounds b = RoomCollider.bounds;
        Vector3 size = new Vector3(Mathf.Max(0.05f, b.size.x), Mathf.Max(0.05f, b.size.y), 0.01f);
        Vector3 center = new Vector3(b.center.x, b.center.y, 0f);

        if (selected)
        {
            Gizmos.color = new Color(0.35f, 1f, 0.55f, 0.10f);
            Gizmos.DrawCube(center, size);
            Gizmos.color = new Color(0.35f, 1f, 0.55f, 1f);
        }
        else
        {
            Gizmos.color = new Color(0.25f, 0.90f, 0.95f, 0.75f);
        }

        Gizmos.DrawWireCube(center, size);
    }

    private void OnDrawGizmos()
    {
        DrawRoomOutlineGizmo(selected: false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawRoomOutlineGizmo(selected: true);
    }
}
