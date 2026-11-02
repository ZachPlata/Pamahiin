using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Controls the movement of a single Ghost Orb.
/// Ghost Orbs are instantiated by the GhostHandler in the Favorite Room if the ghost type supports it.
/// They are placed on a specific "GhostOrbs" layer which is only seen by the Video Camera.
/// </summary>
public class GhostOrbSystem : NetworkBehaviour
{
    [Header("Orb Movement Settings")]
    [SerializeField] private float moveSpeed = 0.15f;
    [SerializeField] private float changeDirectionInterval = 3f;

    private Bounds roomBounds;
    private Collider2D roomCollider;
    private bool initialized = false;
    private Vector2 targetPosition;
    private float timer = 0f;

    /// <summary>
    /// Call this after spawning to give the orb the exact room collider to stay inside.
    /// </summary>
    public void Initialize(Collider2D collider)
    {
        roomCollider = collider;
        roomBounds = collider != null ? collider.bounds : new Bounds(transform.position, Vector3.one);
        initialized = true;

        Vector2 startPos = roomBounds.center;
        if (roomCollider != null && !roomCollider.OverlapPoint(startPos))
        {
            startPos = roomCollider.ClosestPoint(startPos);
        }
        transform.position = startPos;
        PickNewTarget();
    }

    /// <summary>
    /// Call this after spawning to give the orb the exact room bounds to stay inside.
    /// </summary>
    public void Initialize(Bounds bounds)
    {
        roomBounds = bounds;
        initialized = true;
        // Start at the center
        transform.position = bounds.center;
        PickNewTarget();
    }

    private SpriteRenderer sr;

    public override void OnNetworkSpawn()
    {
        sr = GetComponent<SpriteRenderer>();
        // Ensure every client sets the layer correctly
        int orbLayer = LayerMask.NameToLayer("GhostOrbs");
        if (orbLayer >= 0)
        {
            gameObject.layer = orbLayer;
        }
        else
        {
            Debug.LogWarning("GhostOrbSystem: Layer 'GhostOrb' is not defined in the project. " +
                "Please add it via Edit > Project Settings > Tags and Layers.");
        }
        // Make orb white and start hidden
        if (sr != null)
        {
            sr.color = Color.white;
            sr.enabled = false;
        }
    }

    private void Update()
    {
        // Server-side movement
        if (IsServer && initialized)
        {
            timer += Time.deltaTime;
            if (timer >= changeDirectionInterval)
            {
                PickNewTarget();
                timer = 0f;
            }

            // Constant speed movement (MoveTowards already gives constant speed)
            transform.position = Vector2.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

            // Enforce that the orb NEVER leaves the active ghost room
            if (roomCollider != null && !roomCollider.OverlapPoint(transform.position))
            {
                transform.position = roomCollider.ClosestPoint(transform.position);
                PickNewTarget();
            }
            else if (roomCollider == null && !roomBounds.Contains(transform.position))
            {
                transform.position = roomBounds.ClosestPoint(transform.position);
                PickNewTarget();
            }
        }

        // Client-side visual toggle
        if (sr != null)
        {
            bool shouldBeVisible = false;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                // Only show if the LOCAL player is holding a powered-on Video Camera in hand
                var allCameras = FindObjectsByType<VideoCameraItem>(FindObjectsInactive.Exclude);
                foreach (var cam in allCameras)
                {
                    if (cam.IsPoweredOn && cam.CurrentHolderClientId == NetworkManager.Singleton.LocalClientId && cam.IsInHand)
                    {
                        shouldBeVisible = true;
                        break;
                    }
                }
            }

            // Also show when the Truck Dashboard is open (so the CCTV feed can capture it)
            if (!shouldBeVisible)
            {
                var dashboard = FindAnyObjectByType<TruckDashboardController>();
                if (dashboard != null && dashboard.IsDashboardOpen)
                {
                    shouldBeVisible = true;
                }
            }

            sr.enabled = shouldBeVisible;
        }
    }

    private void PickNewTarget()
    {
        // Dynamic inset margin scaled to room dimensions so small rooms (e.g. bathroom) don't have inverted/impossible margins
        float marginX = Mathf.Min(0.12f, roomBounds.extents.x * 0.3f);
        float marginY = Mathf.Min(0.12f, roomBounds.extents.y * 0.3f);

        float minX = roomBounds.min.x + marginX;
        float maxX = roomBounds.max.x - marginX;
        float minY = roomBounds.min.y + marginY;
        float maxY = roomBounds.max.y - marginY;

        if (minX > maxX) { minX = roomBounds.center.x; maxX = roomBounds.center.x; }
        if (minY > maxY) { minY = roomBounds.center.y; maxY = roomBounds.center.y; }

        for (int attempts = 0; attempts < 15; attempts++)
        {
            float rx = Random.Range(minX, maxX);
            float ry = Random.Range(minY, maxY);
            Vector2 candidate = new Vector2(rx, ry);

            if (roomCollider == null || roomCollider.OverlapPoint(candidate))
            {
                targetPosition = candidate;
                return;
            }
        }

        // If no random point matched collider shape, use center or closest point
        if (roomCollider != null)
        {
            targetPosition = roomCollider.OverlapPoint(roomBounds.center) 
                ? (Vector2)roomBounds.center 
                : roomCollider.ClosestPoint(roomBounds.center);
        }
        else
        {
            targetPosition = roomBounds.center;
        }
    }
}

