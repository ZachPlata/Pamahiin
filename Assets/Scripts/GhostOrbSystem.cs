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

    private Renderer[] allRenderers;
    private ParticleSystem[] particleSystems;
    private bool isCurrentlyVisible = true;

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

        SetOrbVisible(false);
    }

    private void Update()
    {
        // Server-side movement
        if ((IsServer || !IsSpawned) && initialized)
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

        // Client-side visibility: NEVER show if no VideoCameraItem exists in the scene
        bool shouldBeVisible = false;
        var allCameras = FindObjectsByType<VideoCameraItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        if (allCameras != null && allCameras.Length > 0)
        {
            // 1. Check if the local player is holding a powered-on Video Camera in hand
            foreach (var cam in allCameras)
            {
                if (cam != null && cam.IsPoweredOn && cam.IsInHand && cam.IsHeldByLocalPlayer)
                {
                    shouldBeVisible = true;
                    break;
                }
            }

            // 2. Or if the Truck Dashboard is open AND at least one Video Camera is placed/active in the scene
            if (!shouldBeVisible)
            {
                var dashboard = FindAnyObjectByType<TruckDashboardController>();
                if (dashboard != null && dashboard.IsDashboardOpen)
                {
                    foreach (var cam in allCameras)
                    {
                        if (cam != null && (cam.IsPlaced || cam.IsOnGround || cam.IsPoweredOn))
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
