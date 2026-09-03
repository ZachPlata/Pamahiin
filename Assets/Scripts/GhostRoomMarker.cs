using UnityEngine;

/// <summary>
/// Attach this to a sprite in the scene to designate it as a potential ghost spawn room.
/// The sprite will be hidden at runtime.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class GhostRoomMarker : MonoBehaviour
{
    private void Awake()
    {
        // Disable the sprite renderer so it doesn't appear in runtime
        var spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false;
        }
    }
}
