using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SafeZone : MonoBehaviour
{
    public static readonly HashSet<SafeZone> AllActiveZones = new HashSet<SafeZone>();

    private static readonly Collider2D[] overlapBuffer = new Collider2D[16];
    private Collider2D[] zoneColliders;

    public bool HasEnabledCollider
    {
        get
        {
            EnsureCollidersCached();
            if (zoneColliders == null) return false;
            for (int i = 0; i < zoneColliders.Length; i++)
            {
                if (zoneColliders[i] != null && zoneColliders[i].enabled)
                {
                    return true;
                }
            }
            return false;
        }
    }

    private void Awake()
    {
        RefreshColliders();

        // Prevent scene placeholder trigger squares from rendering or glowing under IR night vision
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.enabled = false;
        }
    }

    private void Start()
    {
        RefreshColliders();
    }

    private void OnEnable()
    {
        AllActiveZones.Add(this);
        RefreshColliders();

        // When this SafeZone becomes active (e.g. room light switched ON),
        // immediately register any player currently standing inside it.
        CheckOverlappingPlayers();
    }

    public void RefreshColliders()
    {
        zoneColliders = GetComponents<Collider2D>();
        if (zoneColliders == null) return;

        for (int i = 0; i < zoneColliders.Length; i++)
        {
            if (zoneColliders[i] != null)
            {
                zoneColliders[i].isTrigger = true;
            }
        }
    }

    private void EnsureCollidersCached()
    {
        if (zoneColliders == null || zoneColliders.Length == 0)
        {
            zoneColliders = GetComponents<Collider2D>();
        }
    }

    public bool OverlapsPosition(Vector2 worldPos)
    {
        if (this == null || !isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            return false;
        }

        EnsureCollidersCached();
        if (zoneColliders == null) return false;

        for (int i = 0; i < zoneColliders.Length; i++)
        {
            var c = zoneColliders[i];
            if (c != null && c.enabled && c.OverlapPoint(worldPos))
            {
                return true;
            }
        }
        return false;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision == null) return;
        var player = collision.GetComponent<PlayerController>();
        if (player != null)
        {
            player.AddSafeZone(this);
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision == null) return;
        var player = collision.GetComponent<PlayerController>();
        if (player != null)
        {
            player.AddSafeZone(this);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision == null) return;
        var player = collision.GetComponent<PlayerController>();
        if (player != null)
        {
            player.RemoveSafeZone(this);
        }
    }

    private void OnDisable()
    {
        AllActiveZones.Remove(this);

        // When this SafeZone is disabled (e.g. light switch turned OFF or fuse tripped),
        // remove this safe zone from any player standing in it.
        NotifyOverlappingPlayers(enter: false);
    }

    private void OnDestroy()
    {
        AllActiveZones.Remove(this);
    }

    private void CheckOverlappingPlayers()
    {
        NotifyOverlappingPlayers(enter: true);
    }

    private void NotifyOverlappingPlayers(bool enter)
    {
        EnsureCollidersCached();
        if (zoneColliders == null) return;

        ContactFilter2D filter = ContactFilter2D.noFilter;
        for (int c = 0; c < zoneColliders.Length; c++)
        {
            var col = zoneColliders[c];
            if (col == null) continue;

            int count = Physics2D.OverlapCollider(col, filter, overlapBuffer);
            for (int i = 0; i < count; i++)
            {
                if (overlapBuffer[i] == null) continue;
                var player = overlapBuffer[i].GetComponent<PlayerController>();
                if (player != null)
                {
                    if (enter) player.AddSafeZone(this);
                    else player.RemoveSafeZone(this);
                }
            }
        }
    }
}
