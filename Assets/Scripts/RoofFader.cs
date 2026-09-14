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
    /// Returns true if the local client has the dashboard open AND a placed, powered-on
    /// VideoCameraItem is physically inside this RoofFader's bounds.
    /// Uses a 2D AABB check (X/Y only, ignoring Z) — Bounds.Contains is 3D
    /// and fails in 2D games because the collider's Z extent is nearly zero.
    /// </summary>
    private bool ShouldFadeForCCTV()
    {
        // Cache the dashboard reference
        if (dashboard == null)
        {
            dashboard = FindAnyObjectByType<TruckDashboardController>();
        }

        // Only fade for CCTV when the local client has the dashboard open
        if (dashboard == null)
        {
            Debug.Log("[RoofFader] No TruckDashboardController found.");
            return false;
        }
        if (!dashboard.IsDashboardOpen) return false;
        if (triggerCollider == null) return false;

        Bounds bounds = triggerCollider.bounds;

        var allCameras = FindObjectsByType<VideoCameraItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var cam in allCameras)
        {
            if (cam == null) continue;

            // Check if the camera is on the ground or placed, and powered on
            if ((cam.IsPlaced || cam.IsOnGround) && cam.IsPoweredOn)
            {
                // 2D AABB check — ignore Z axis entirely
                Vector2 camPos = cam.transform.position;
                bool insideX = camPos.x >= bounds.min.x && camPos.x <= bounds.max.x;
                bool insideY = camPos.y >= bounds.min.y && camPos.y <= bounds.max.y;

                if (insideX && insideY)
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
