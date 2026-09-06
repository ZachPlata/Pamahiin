using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Unity.Netcode;

/// <summary>
/// Attach this script to a Trigger Collider that covers the interior of the building.
/// Assign all Roof SpriteRenderers or TilemapRenderers to the list.
/// When the local player enters the trigger, the roof fades out. When they leave, it fades back in.
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
    private int localPlayersInside = 0; // In case of overlapping triggers, count entrances and exits.

    private void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        // Initialize alphas to 1
        SetAlpha(1f);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsLocalPlayer(other))
        {
            localPlayersInside++;
            UpdateTargetAlpha();
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (IsLocalPlayer(other))
        {
            localPlayersInside--;
            if (localPlayersInside < 0) localPlayersInside = 0;
            UpdateTargetAlpha();
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

    private void UpdateTargetAlpha()
    {
        targetAlpha = localPlayersInside > 0 ? 0f : 1f;
    }

    private void Update()
    {
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
