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
    private bool initialized = false;
    private Vector2 targetPosition;
    private float timer = 0f;

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
        }

        // Client-side visual toggle
        if (sr != null)
        {
            bool shouldBeVisible = false;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                // Only show if the LOCAL player is holding a powered-on Video Camera in hand
                var allCameras = FindObjectsByType<VideoCameraItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
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
        // Pick a random point strictly inside the room bounds (with a small inset margin)
        float margin = 0.2f;
        float x = Random.Range(roomBounds.min.x + margin, roomBounds.max.x - margin);
        float y = Random.Range(roomBounds.min.y + margin, roomBounds.max.y - margin);
        targetPosition = new Vector2(x, y);
    }
}

