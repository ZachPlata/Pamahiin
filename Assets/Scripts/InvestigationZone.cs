using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines a huntable investigation boundary for maps with outdoor play areas (e.g. CemeteryMap).
/// When at least one active InvestigationZone exists in the scene, players inside any
/// InvestigationZone or enclosed RoofFader building are valid hunt targets.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class InvestigationZone : MonoBehaviour
{
    public static readonly List<InvestigationZone> AllZones = new List<InvestigationZone>();

    private Collider2D[] zoneColliders;

    private void Awake()
    {
        zoneColliders = GetComponents<Collider2D>();
        if (zoneColliders != null)
        {
            for (int i = 0; i < zoneColliders.Length; i++)
            {
                if (zoneColliders[i] != null)
                {
                    zoneColliders[i].isTrigger = true;
                }
            }
        }

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.enabled = false;
        }
    }

    private void OnEnable()
    {
        if (!AllZones.Contains(this))
        {
            AllZones.Add(this);
        }
    }

    private void OnDisable()
    {
        AllZones.Remove(this);
    }

    public bool ContainsPosition(Vector2 worldPos)
    {
        if (zoneColliders == null || zoneColliders.Length == 0)
        {
            zoneColliders = GetComponents<Collider2D>();
        }
        if (zoneColliders == null || zoneColliders.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < zoneColliders.Length; i++)
        {
            Collider2D col = zoneColliders[i];
            if (col == null || !col.enabled) continue;
            if (col.OverlapPoint(worldPos)) return true;

            Bounds b = col.bounds;
            if (worldPos.x >= b.min.x && worldPos.x <= b.max.x &&
                worldPos.y >= b.min.y && worldPos.y <= b.max.y)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsPositionInAnyZone(Vector2 worldPos)
    {
        for (int i = AllZones.Count - 1; i >= 0; i--)
        {
            InvestigationZone zone = AllZones[i];
            if (zone == null)
            {
                AllZones.RemoveAt(i);
                continue;
            }
            if (zone.isActiveAndEnabled && zone.ContainsPosition(worldPos))
            {
                return true;
            }
        }
        return false;
    }
}
