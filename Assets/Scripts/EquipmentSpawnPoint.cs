using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach to the EquipmentSpawn GameObject (in the Van or on the Lobby EquipmentArmoryRack).
/// Defines a vertical column where ghost hunting equipment items spawn from top to bottom.
/// </summary>
public class EquipmentSpawnPoint : MonoBehaviour
{
    public static EquipmentSpawnPoint Instance { get; private set; }

    [Header("Equipment Spawn Settings")]
    [Tooltip("Hide the placeholder SpriteRenderer box when entering Play Mode.")]
    [SerializeField] private bool hideSpriteInPlayMode = true;

    [Tooltip("If true (or when in LobbyScene), spawns all 10 ghost hunting equipment types so the armory rack is fully stocked and interactable.")]
    [SerializeField] private bool spawnAllItemsInLobby = true;

    private Bounds cachedWorldBounds;
    private bool hasCachedBounds = false;

    public bool ShouldSpawnAllCatalogItems =>
        spawnAllItemsInLobby && SceneManager.GetActiveScene().name == ProgressionAndLoadoutManager.LOBBY_SCENE_NAME;

    private void Awake()
    {
        Instance = this;
        CacheBoundsAndSetup();
    }

    private void Start()
    {
        // Guarantee TruckGearSpawner spawns equipment into this EquipmentSpawn area
        if (TruckGearSpawner.Instance == null)
        {
            var existing = FindAnyObjectByType<TruckGearSpawner>();
            if (existing == null)
            {
                existing = gameObject.AddComponent<TruckGearSpawner>();
            }
        }

        if (TruckGearSpawner.Instance != null)
        {
            TruckGearSpawner.Instance.SpawnSelectedLoadoutInVan();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void CacheBoundsAndSetup()
    {
        cachedWorldBounds = ComputeTransformWorldBounds();
        hasCachedBounds = true;

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && hideSpriteInPlayMode && Application.isPlaying)
        {
            sr.enabled = false;
        }

        var col = GetComponent<Collider2D>();
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
            b = new Bounds(new Vector3(transform.position.x, transform.position.y, 0f), new Vector3(0.08f, 0.72f, 0.1f));
        }

        return b;
    }

    public Bounds GetWorldBounds()
    {
        if (!hasCachedBounds || cachedWorldBounds.size.sqrMagnitude < 0.0001f)
        {
            cachedWorldBounds = ComputeTransformWorldBounds();
            hasCachedBounds = true;
        }
        return cachedWorldBounds;
    }

    /// <summary>
    /// Computes the world position for the given vertical slot index (from top to bottom inside EquipmentSpawn).
    /// </summary>
    public Vector3 GetVerticalSlotPosition(int slotIndex, int totalSlots)
    {
        Bounds b = GetWorldBounds();
        totalSlots = Mathf.Max(1, totalSlots);
        slotIndex = Mathf.Clamp(slotIndex, 0, totalSlots - 1);

        if (totalSlots == 1)
        {
            return new Vector3(b.center.x, b.center.y, 0f);
        }

        float t = (slotIndex + 0.5f) / totalSlots;
        float y = Mathf.Lerp(b.max.y, b.min.y, t);
        return new Vector3(b.center.x, y, 0f);
    }

    public static EquipmentSpawnPoint FindOrResolve()
    {
        if (Instance != null)
        {
            Instance.CacheBoundsAndSetup();
            return Instance;
        }

        var found = FindAnyObjectByType<EquipmentSpawnPoint>(FindObjectsInactive.Include);
        if (found != null)
        {
            Instance = found;
            found.CacheBoundsAndSetup();
            return found;
        }

        GameObject named = GameObject.Find("EquipmentSpawn") ?? GameObject.Find("EquipmentSpawnPoint");
        if (named != null)
        {
            var added = named.GetComponent<EquipmentSpawnPoint>();
            if (added == null) added = named.AddComponent<EquipmentSpawnPoint>();
            added.CacheBoundsAndSetup();
            Instance = added;
            return added;
        }

        return null;
    }

    private void OnDrawGizmos()
    {
        Bounds b = ComputeTransformWorldBounds();
        Gizmos.color = new Color(0.1f, 0.75f, 1f, 0.25f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(b.center, b.size);

        int previewSlots = 10;
        for (int i = 0; i < previewSlots; i++)
        {
            float t = (i + 0.5f) / previewSlots;
            Vector3 pt = new Vector3(b.center.x, Mathf.Lerp(b.max.y, b.min.y, t), 0f);
            Gizmos.DrawSphere(pt, 0.012f);
        }
    }
}
