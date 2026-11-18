using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a floor region in a multi-floor map so audio, ghost prop disruption,
/// hunter reachability, and outdoor weather know which floor an entity occupies.
/// Floor indices: 0 = Exterior, 1 = F1, 2 = F2, 3 = F3.
/// </summary>
public class FloorZone : MonoBehaviour
{
    public const int FLOOR_EXTERIOR = 0;
    public const int FLOOR_FIRST = 1;
    public const int FLOOR_SECOND = 2;
    public const int FLOOR_THIRD = 3;

    [Header("Floor Configuration")]
    [Tooltip("Floor index: 0 = Exterior, 1 = Floor 1, 2 = Floor 2, 3 = Floor 3.")]
    [SerializeField] private int floorIndex = FLOOR_FIRST;

    [Tooltip("True for enclosed interior floors (F1-F3); false for explicit exterior zones.")]
    [SerializeField] private bool isInterior = true;

    [Tooltip("Human-readable label for diagnostics and floor UI.")]
    [SerializeField] private string floorLabel = "Floor 1";

    private static readonly List<FloorZone> registeredZones = new List<FloorZone>();
    private Collider2D[] cachedColliders;

    public int FloorIndex => floorIndex;
    public bool IsInterior => isInterior && floorIndex > FLOOR_EXTERIOR;
    public string FloorLabel => floorLabel;
    public static IReadOnlyList<FloorZone> All => registeredZones;

    private void Awake()
    {
        RefreshColliders();
    }

    private void OnEnable()
    {
        if (!registeredZones.Contains(this))
        {
            registeredZones.Add(this);
        }
        RefreshColliders();
    }

    private void OnDisable()
    {
        registeredZones.Remove(this);
    }

    private void OnDestroy()
    {
        registeredZones.Remove(this);
    }

    /// <summary>
    /// Configures floor metadata from scene builders or unit tests.
    /// </summary>
    public void Configure(int index, bool interior = true, string label = null)
    {
        floorIndex = Mathf.Max(FLOOR_EXTERIOR, index);
        isInterior = interior && floorIndex > FLOOR_EXTERIOR;
        floorLabel = string.IsNullOrEmpty(label)
            ? (floorIndex == FLOOR_EXTERIOR ? "Exterior" : $"Floor {floorIndex}")
            : label;
        RefreshColliders();

        if (isActiveAndEnabled && !registeredZones.Contains(this))
        {
            registeredZones.Add(this);
        }
    }

    /// <summary>
    /// Refreshes the cached Collider2D components used for floor containment queries.
    /// </summary>
    public void RefreshColliders()
    {
        cachedColliders = GetComponents<Collider2D>();
        if (cachedColliders == null || cachedColliders.Length == 0)
        {
            cachedColliders = GetComponentsInChildren<Collider2D>(true);
        }

        if (cachedColliders != null)
        {
            for (int i = 0; i < cachedColliders.Length; i++)
            {
                if (cachedColliders[i] != null)
                {
                    cachedColliders[i].isTrigger = true;
                }
            }
        }
    }

    /// <summary>
    /// Returns true if the given 2D world position falls inside any collider attached to this zone.
    /// </summary>
    public bool ContainsPosition(Vector2 worldPos)
    {
        if (cachedColliders == null || cachedColliders.Length == 0 || !Application.isPlaying)
        {
            cachedColliders = GetComponents<Collider2D>();
            if (cachedColliders == null || cachedColliders.Length == 0)
            {
                cachedColliders = GetComponentsInChildren<Collider2D>(true);
            }
        }

        if (cachedColliders == null || cachedColliders.Length == 0) return false;

        for (int i = 0; i < cachedColliders.Length; i++)
        {
            Collider2D col = cachedColliders[i];
            if (col == null) continue;

            if (col is BoxCollider2D box)
            {
                Vector3 localPos = box.transform.InverseTransformPoint(new Vector3(worldPos.x, worldPos.y, box.transform.position.z));
                Vector2 half = box.size * 0.5f;
                if (Mathf.Abs(localPos.x - box.offset.x) <= half.x &&
                    Mathf.Abs(localPos.y - box.offset.y) <= half.y)
                {
                    return true;
                }
            }
            else if (col.isActiveAndEnabled && col.OverlapPoint(worldPos))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if the active scene contains at least one enabled FloorZone.
    /// </summary>
    public static bool HasAnyZones()
    {
        PruneOrPopulateZones();
        return registeredZones.Count > 0;
    }

    /// <summary>
    /// Returns the floor index at worldPos (0 = Exterior, 1 = F1, 2 = F2, 3 = F3).
    /// Returns 0 when outside all interior zones or when no FloorZones exist.
    /// </summary>
    public static int GetFloorIndex(Vector3 pos)
    {
        return TryGetZoneAtPosition(pos, out FloorZone zone) ? zone.floorIndex : FLOOR_EXTERIOR;
    }

    /// <summary>
    /// Resolves floor index via FloorZones if any exist in the scene.
    /// Returns false in legacy maps (like BarangayMap) that have no FloorZone components.
    /// </summary>
    public static bool TryGetFloorIndex(Vector3 pos, out int index)
    {
        if (!HasAnyZones())
        {
            index = FLOOR_EXTERIOR;
            return false;
        }

        index = GetFloorIndex(pos);
        return true;
    }

    /// <summary>
    /// Returns true if worldPos is inside an interior FloorZone (floorIndex > 0).
    /// </summary>
    public static bool IsPositionInsideInteriorFloor(Vector3 pos)
    {
        return TryGetZoneAtPosition(pos, out FloorZone zone) && zone.IsInterior;
    }

    /// <summary>
    /// Finds the highest-priority FloorZone containing pos (preferring interior zones over exterior zones).
    /// </summary>
    public static bool TryGetZoneAtPosition(Vector3 pos, out FloorZone matchedZone)
    {
        PruneOrPopulateZones();
        matchedZone = null;
        Vector2 pos2D = new Vector2(pos.x, pos.y);

        for (int i = 0; i < registeredZones.Count; i++)
        {
            FloorZone zone = registeredZones[i];
            if (zone == null || !zone.isActiveAndEnabled) continue;
            if (!zone.ContainsPosition(pos2D)) continue;

            if (zone.IsInterior)
            {
                matchedZone = zone;
                return true;
            }

            if (matchedZone == null)
            {
                matchedZone = zone;
            }
        }

        return matchedZone != null;
    }

    private static void PruneOrPopulateZones()
    {
        for (int i = registeredZones.Count - 1; i >= 0; i--)
        {
            if (registeredZones[i] == null || !registeredZones[i].isActiveAndEnabled)
            {
                registeredZones.RemoveAt(i);
            }
        }

        if (registeredZones.Count == 0 || !Application.isPlaying)
        {
            FloorZone[] found = FindObjectsByType<FloorZone>(FindObjectsInactive.Exclude);
            for (int i = 0; i < found.Length; i++)
            {
                FloorZone zone = found[i];
                if (zone != null && zone.isActiveAndEnabled && !registeredZones.Contains(zone))
                {
                    registeredZones.Add(zone);
                }
            }
        }
    }
}
