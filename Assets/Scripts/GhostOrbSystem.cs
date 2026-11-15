using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Controls the movement and visibility of a Ghost Orb (Spirit Orb).
/// Ghost Orbs are strictly invisible unless a Video Camera is present in the scene
/// and actively viewing them (either held & powered on by the local player, or viewed via CCTV when a Video Camera exists).
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
    private GhostHandler cachedGhostHandler;

    private Renderer[] allRenderers;
    private ParticleSystem[] particleSystems;
    private bool isCurrentlyVisible = true;

    private bool hasAppliedInitialVisibility = false;

    private void Awake()
    {
        CacheRenderers();
        SetOrbVisible(false);
    }

    private void CacheRenderers()
    {
        allRenderers = GetComponentsInChildren<Renderer>(true);
        particleSystems = GetComponentsInChildren<ParticleSystem>(true);
    }

    private void SetOrbVisible(bool visible)
    {
        if (hasAppliedInitialVisibility && isCurrentlyVisible == visible)
        {
            return;
        }
        hasAppliedInitialVisibility = true;

        if (allRenderers == null) CacheRenderers();

        if (allRenderers != null)
        {
            foreach (var r in allRenderers)
            {
                if (r != null) r.enabled = visible;
            }
        }

        if (particleSystems != null && isCurrentlyVisible != visible)
        {
            foreach (var ps in particleSystems)
            {
                if (ps == null) continue;
                if (visible)
                {
                    if (!ps.isPlaying) ps.Play();
                }
                else
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        isCurrentlyVisible = visible;
    }

    private GhostHandler GetGhostHandler()
    {
        if (cachedGhostHandler == null)
        {
            cachedGhostHandler = FindAnyObjectByType<GhostHandler>();
        }
        return cachedGhostHandler;
    }

    /// <summary>
    /// Binds this orb to the active Ghost Room collider without moving its networked transform.
    /// </summary>
    public void BindRoomCollider(Collider2D collider)
    {
        if (collider == null)
        {
            roomCollider = null;
            initialized = false;
            return;
        }

        roomCollider = collider;
        roomBounds = collider.bounds;
        initialized = true;
    }

    public void Initialize(Collider2D collider)
    {
        if (collider == null)
        {
            roomCollider = null;
            roomBounds = new Bounds(transform.position, Vector3.one);
            initialized = false;
            SetOrbVisible(false);
            return;
        }

        BindRoomCollider(collider);

        Vector2 startPos = roomBounds.center;
        if (!roomCollider.OverlapPoint(startPos))
        {
            startPos = roomCollider.ClosestPoint(startPos);
        }
        transform.position = startPos;
        PickNewTarget();
    }

    /// <summary>
    /// Server-side: moves this existing orb to a position inside its room (used by ghost events
    /// so no duplicate orb is spawned).
    /// </summary>
    public void NudgeTo(Vector2 position)
    {
        if (IsSpawned && !IsServer) return;

        if (roomCollider != null && !roomCollider.OverlapPoint(position))
            position = roomCollider.ClosestPoint(position);
        else if (roomCollider == null && initialized && !roomBounds.Contains(position))
            position = roomBounds.ClosestPoint(position);

        transform.position = position;
        timer = 0f;
        if (initialized) PickNewTarget();
    }

    public void Initialize(Bounds bounds)
    {
        roomBounds = bounds;
        initialized = true;
        transform.position = bounds.center;
        PickNewTarget();
    }

    public override void OnNetworkSpawn()
    {
        CacheRenderers();
        int orbLayer = LayerMask.NameToLayer("GhostOrbs");
        if (orbLayer >= 0)
        {
            gameObject.layer = orbLayer;
        }

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = Color.white;
        }

        var handler = GetGhostHandler();
        if (handler != null && handler.ghostroomMarker != null)
        {
            BindRoomCollider(handler.ghostroomMarker);
        }

        SetOrbVisible(false);
    }

    private bool HasValidGhostRoom()
    {
        var handler = GetGhostHandler();
        if (handler != null)
        {
            if (roomCollider != handler.ghostroomMarker)
            {
                BindRoomCollider(handler.ghostroomMarker);
            }

            return handler.ghostroomMarker != null
                && !handler.IsExorcised
                && handler.HasEvidence(EvidenceType.GhostOrbs)
                && roomCollider != null
                && roomCollider.enabled
                && roomCollider.gameObject.activeInHierarchy;
        }

        return roomCollider != null
            && roomCollider.enabled
            && roomCollider.gameObject.activeInHierarchy;
    }

    private void Update()
    {
        bool hasValidRoom = HasValidGhostRoom();

        // Server-side movement
        if ((IsServer || !IsSpawned) && initialized && hasValidRoom)
        {
            timer += Time.deltaTime;
            if (timer >= changeDirectionInterval)
            {
                PickNewTarget();
                timer = 0f;
            }

            transform.position = Vector2.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

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

        // Client-side visibility: NEVER show without an active non-exorcised ghost room or without a VideoCameraItem in the scene
        bool shouldBeVisible = false;
        var allCameras = VideoCameraItem.ActiveCameras;

        if (hasValidRoom && allCameras != null && allCameras.Count > 0)
        {
            // 1. Check if the local player is holding a powered-on Video Camera in hand
            for (int i = 0; i < allCameras.Count; i++)
            {
                var cam = allCameras[i];
                if (cam != null && cam.IsPoweredOn && cam.IsInHand && cam.IsHeldByLocalPlayer)
                {
                    shouldBeVisible = true;
                    break;
                }
            }

            // 2. Or if the Truck Dashboard is open with Night Vision ON AND at least one Video Camera is available in the scene
            if (!shouldBeVisible)
            {
                var dashboard = TruckDashboardController.Instance;
                if (dashboard != null && dashboard.IsDashboardOpen && dashboard.IsCCTVNightVisionOn)
                {
                    for (int i = 0; i < allCameras.Count; i++)
                    {
                        var cam = allCameras[i];
                        if (cam != null && (cam.IsPlaced || cam.IsOnGround || cam.IsInHand || cam.IsPoweredOn))
                        {
                            shouldBeVisible = true;
                            break;
                        }
                    }
                }
            }
        }

        SetOrbVisible(shouldBeVisible);
    }

    private void PickNewTarget()
    {
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
