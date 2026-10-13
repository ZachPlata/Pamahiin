using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Unity.Netcode;

/// <summary>
/// Attach this script to a Trigger Collider that covers the interior of the building.
/// Assign all Roof SpriteRenderers or TilemapRenderers to the list.
/// When the local player enters the trigger, the roof fades out. When they leave, it fades back in.
/// Also fades the roof when a placed VideoCameraItem is inside AND the local client
/// has the Truck Dashboard open (so the CCTV feed can see through the roof).
/// This fade is purely client-side — other players won't see it.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class RoofFader : MonoBehaviour
{
    [Header("Renderers to Fade")]
    [Tooltip("Drag the SpriteRenderers of the roof here.")]
    [SerializeField] private List<SpriteRenderer> spriteRenderers = new List<SpriteRenderer>();

    [Tooltip("Drag any TilemapRenderers of the roof here (if using Tilemaps for roofs).")]
    [SerializeField] private List<TilemapRenderer> tilemapRenderers = new List<TilemapRenderer>();

    [Header("Fade Settings")]
    [SerializeField] private float fadeDuration = 0.5f;

    private float targetAlpha = 1f;
    private float currentAlpha = 1f;
    private int localPlayersInside = 0;
    private Collider2D triggerCollider;
    private TruckDashboardController dashboard;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }

        // Initialize alphas to 1
        SetAlpha(1f);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsLocalPlayer(other))
        {
            localPlayersInside++;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (IsLocalPlayer(other))
        {
            localPlayersInside--;
            if (localPlayersInside < 0) localPlayersInside = 0;
        }
    }

    private bool IsLocalPlayer(Collider2D other)
    {
        // Check if the colliding object has a PlayerController (safest way, no tags needed)
        PlayerController playerController = other.GetComponent<PlayerController>();
        if (playerController != null)
        {
            NetworkObject netObj = other.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsOwner)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if a 2D world position is inside this roof trigger's bounding box.
    /// </summary>
    public bool IsPositionInside(Vector2 position)
    {
        if (triggerCollider == null) triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider == null) return false;
        Bounds bounds = triggerCollider.bounds;
        return position.x >= bounds.min.x && position.x <= bounds.max.x &&
               position.y >= bounds.min.y && position.y <= bounds.max.y;
    }

    /// <summary>
    /// Instantly hides or restores the roof renderers for a specific camera render pass (e.g. CCTV render texture).
    /// </summary>
    public void SetCameraRenderHide(bool hide)
    {
        foreach (var sr in spriteRenderers)
        {
            if (sr != null) sr.enabled = !hide;
        }

        foreach (var tmr in tilemapRenderers)
        {
            if (tmr != null) tmr.enabled = !hide;
        }
    }

    /// <summary>
    /// Returns true if the local client has the dashboard open AND the currently viewed camera
    /// (or any deployed video camera) is physically inside this RoofFader's bounds.
    /// Uses a 2D AABB check (X/Y only, ignoring Z).
    /// </summary>
    private bool ShouldFadeForCCTV()
    {
        // Cache the dashboard reference
        if (dashboard == null)
        {
            dashboard = TruckDashboardController.Instance != null 
                ? TruckDashboardController.Instance 
                : FindAnyObjectByType<TruckDashboardController>();
        }

        if (dashboard == null || !dashboard.IsDashboardOpen) return false;
        if (triggerCollider == null) return false;

        // 1. Check the currently viewed camera feed on the Truck Dashboard
        Camera activeCam = dashboard.ActiveCamera;
        if (activeCam != null && IsPositionInside(activeCam.transform.position))
        {
            return true;
        }

        // 2. Fallback: check if any placed or deployed VideoCameraItem is inside this roof
        var allCameras = FindObjectsByType<VideoCameraItem>(FindObjectsInactive.Exclude);
        foreach (var cam in allCameras)
        {
            if (cam == null) continue;

            if (cam.IsPlaced || cam.IsOnGround)
            {
                if (IsPositionInside(cam.transform.position))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void Update()
    {
        targetAlpha = (localPlayersInside > 0 || ShouldFadeForCCTV()) ? 0f : 1f;

        if (Mathf.Abs(currentAlpha - targetAlpha) > 0.001f)
        {
            float speed = 1f / fadeDuration;
            currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, speed * Time.deltaTime);
            SetAlpha(currentAlpha);
        }
    }

    private void SetAlpha(float alpha)
    {
        foreach (var sr in spriteRenderers)
        {
            if (sr != null)
            {
                Color c = sr.color;
                c.a = alpha;
                sr.color = c;
            }
        }

        foreach (var tmr in tilemapRenderers)
        {
            if (tmr != null)
            {
                Color c = tmr.GetComponent<Tilemap>().color;
                c.a = alpha;
                tmr.GetComponent<Tilemap>().color = c;
            }
        }
    }
}
