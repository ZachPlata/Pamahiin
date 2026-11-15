using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Designates a vertical spawn zone (with 4 vertical spawn points) for players.
/// Automatically uses the SpriteRenderer or Collider2D bounds of the GameObject (e.g. PlayerSpawn in Van)
/// to distribute up to 4 players vertically so they never spawn outside the van or collide on spawn.
/// </summary>
public class PlayerSpawnPoint : MonoBehaviour
{
    private static readonly List<PlayerSpawnPoint> activeSpawnPoints = new List<PlayerSpawnPoint>();

    [Header("Vertical Spawn Settings")]
    [Tooltip("Number of vertical spawn slots inside this spawn area.")]
    [SerializeField] private int spawnSlotCount = 4;

    [Tooltip("Fallback vertical spacing between players if no SpriteRenderer/Collider2D bounds exist.")]
    [SerializeField] private float fallbackVerticalSpacing = 0.14f;

    [Tooltip("Optional explicit child spawn point transforms (4 points). If empty, 4 vertical points are computed from bounds.")]
    [SerializeField] private Transform[] spawnPoints;

    [Tooltip("Optional spawn priority (lower numbers chosen first).")]
    [SerializeField] private int priority = 0;

    [Tooltip("Hide the placeholder SpriteRenderer box when entering Play Mode.")]
    [SerializeField] private bool hideSpriteInPlayMode = true;

    private Bounds cachedWorldBounds;
    private bool hasCachedBounds = false;

    public int Priority => priority;

    private void Awake()
    {
        CacheBoundsAndHidePlaceholder();
        EnsureFourVerticalPoints();
    }

    private void CacheBoundsAndHidePlaceholder()
    {
        var sr = GetComponent<SpriteRenderer>();
        var col = GetComponent<Collider2D>();

        cachedWorldBounds = ComputeTransformWorldBounds();
        hasCachedBounds = true;

        if (sr != null && hideSpriteInPlayMode && Application.isPlaying)
        {
            sr.enabled = false;
        }

        // Ensure any collider on PlayerSpawn does not physically block or push players
        if (col != null && Application.isPlaying)
        {
            col.isTrigger = true;
            col.enabled = false;
        }
    }

    private Bounds ComputeTransformWorldBounds()
    {
        Vector3 c0 = transform.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 c1 = transform.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));
        Vector3 c2 = transform.TransformPoint(new Vector3( 0.5f,  0.5f, 0f));
        Vector3 c3 = transform.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));
        c0.z = c1.z = c2.z = c3.z = 0f;

        Bounds b = new Bounds(c0, Vector3.zero);
        b.Encapsulate(c1);
        b.Encapsulate(c2);
        b.Encapsulate(c3);

        if (b.size.sqrMagnitude < 0.0001f)
        {
            b = new Bounds(new Vector3(transform.position.x, transform.position.y, 0f), new Vector3(0.18f, fallbackVerticalSpacing * 4f, 0.1f));
        }
        return b;
    }

    private void EnsureFourVerticalPoints()
    {
        if (spawnPoints != null && spawnPoints.Length >= 4)
        {
            bool allValid = true;
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] == null) { allValid = false; break; }
            }
            if (allValid) return;
        }

        // Check if child points already exist
        int slots = Mathf.Max(4, spawnSlotCount);
        spawnPoints = new Transform[slots];
        Bounds b = GetWorldBounds();

        for (int i = 0; i < slots; i++)
        {
            string childName = $"SpawnPoint_{i + 1}";
            Transform child = transform.Find(childName);
            if (child == null)
            {
                GameObject ptObj = new GameObject(childName);
                child = ptObj.transform;
                child.SetParent(transform, true);
            }
            child.position = ComputeVerticalPointInBounds(b, i, slots);
            spawnPoints[i] = child;
        }
    }

    public Bounds GetWorldBounds()
    {
        if (hasCachedBounds) return cachedWorldBounds;

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.bounds.size.sqrMagnitude > 0.0001f)
        {
            return sr.bounds;
        }

        var col = GetComponent<Collider2D>();
        if (col != null && col.bounds.size.sqrMagnitude > 0.0001f)
        {
            return col.bounds;
        }

        return new Bounds(transform.position, new Vector3(0.18f, fallbackVerticalSpacing * 4f, 0.1f));
    }

    private Vector3 ComputeVerticalPointInBounds(Bounds b, int slotIndex, int totalSlots)
    {
        totalSlots = Mathf.Max(1, totalSlots);
        slotIndex = Mathf.Clamp(slotIndex, 0, totalSlots - 1);

        if (totalSlots == 1)
        {
            return new Vector3(b.center.x, b.center.y, 0f);
        }

        // Distribute vertically from top to bottom inside bounds with padding from top/bottom edges
        float t = (slotIndex + 0.5f) / totalSlots;
        float y = Mathf.Lerp(b.max.y, b.min.y, t);
        return new Vector3(b.center.x, y, 0f);
    }

    private void OnEnable()
    {
        if (!activeSpawnPoints.Contains(this))
        {
            activeSpawnPoints.Add(this);
            activeSpawnPoints.Sort((a, b) => a.priority.CompareTo(b.priority));
        }
    }

    private void OnDisable()
    {
        activeSpawnPoints.Remove(this);
    }

    /// <summary>
    /// Gets one of the 4 vertical spawn positions for the given client ID.
    /// </summary>
    public static Vector3 GetSpawnPosition(ulong clientId)
    {
        if (activeSpawnPoints.Count == 0)
        {
            var found = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsInactive.Include);
            foreach (var sp in found)
            {
                if (sp != null && sp.gameObject.activeInHierarchy && !activeSpawnPoints.Contains(sp))
                {
                    activeSpawnPoints.Add(sp);
                }
            }
            activeSpawnPoints.Sort((a, b) => a.priority.CompareTo(b.priority));
        }

        // Also check by name "PlayerSpawn" if component wasn't added yet
        if (activeSpawnPoints.Count == 0)
        {
            GameObject namedObj = GameObject.Find("PlayerSpawn") ?? GameObject.Find("PlayerSpawnPoint");
            if (namedObj != null)
            {
                var added = namedObj.GetComponent<PlayerSpawnPoint>();
                if (added == null) added = namedObj.AddComponent<PlayerSpawnPoint>();
                if (!activeSpawnPoints.Contains(added)) activeSpawnPoints.Add(added);
            }
        }

        if (activeSpawnPoints.Count == 0)
        {
            return Vector3.zero;
        }

        PlayerSpawnPoint primary = activeSpawnPoints[0];
        return primary.GetSlotPosition((int)clientId);
    }

    public Vector3 GetSlotPosition(int playerIndex)
    {
        int slots = Mathf.Max(4, spawnSlotCount);
        int idx = ((playerIndex % slots) + slots) % slots;

        if (spawnPoints != null && idx < spawnPoints.Length && spawnPoints[idx] != null)
        {
            return new Vector3(spawnPoints[idx].position.x, spawnPoints[idx].position.y, 0f);
        }

        return ComputeVerticalPointInBounds(GetWorldBounds(), idx, slots);
    }

    private void OnDrawGizmos()
    {
        Bounds b = GetWorldBounds();
        Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.25f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(b.center, b.size);

        int slots = Mathf.Max(4, spawnSlotCount);
        for (int i = 0; i < slots; i++)
        {
            Vector3 pt = ComputeVerticalPointInBounds(b, i, slots);
            Gizmos.color = new Color(0.1f, 1f, 0.4f, 0.9f);
            Gizmos.DrawSphere(pt, 0.025f);
        }
    }
}
