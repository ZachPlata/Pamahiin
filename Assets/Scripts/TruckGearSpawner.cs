using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns clones of the ghost hunting equipment vertically inside the EquipmentSpawn area
/// (on the Lobby EquipmentArmoryRack in LobbyScene, or on the Van shelf in GameMapScene).
/// Multiple copies (2x+) of the same equipment type stack cleanly at their vertical slot.
/// </summary>
public class TruckGearSpawner : MonoBehaviour
{
    public static TruckGearSpawner Instance { get; private set; }

    [Serializable]
    public class GearSpawnSetup
    {
        public EquipmentItem gearPrefab;
        public int spawnCount = 1;
        public Transform[] spawnPoints;
    }

    [Header("Gear Settings")]
    [SerializeField] private List<GearSpawnSetup> gearToSpawn = new List<GearSpawnSetup>();

    private bool hasSpawnedLoadout = false;
    public bool HasSpawnedLoadout => hasSpawnedLoadout;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // Only auto-spawn immediately in standalone/offline mode.
        // In active Netcode sessions, NetworkController triggers spawning after OnLoadEventCompleted so all clients finish loading and syncing first.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            return;
        }

        if (!hasSpawnedLoadout)
        {
            SpawnSelectedLoadoutInVan();
        }
    }

    /// <summary>
    /// Ensures a TruckGearSpawner exists in the active scene and spawns equipment from the Server after clients finish loading.
    /// </summary>
    public static TruckGearSpawner EnsureSpawnerAndSpawnOnServer()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            return null;
        }

        var spawner = Instance ?? FindAnyObjectByType<TruckGearSpawner>(FindObjectsInactive.Include);
        if (spawner == null)
        {
            var spawnPt = EquipmentSpawnPoint.FindOrResolve();
            GameObject hostGo = spawnPt != null ? spawnPt.gameObject : new GameObject("TruckGearSpawner");
            spawner = hostGo.GetComponent<TruckGearSpawner>() ?? hostGo.AddComponent<TruckGearSpawner>();
        }

        spawner.SpawnSelectedLoadoutInVan();
        return spawner;
    }

    /// <summary>
    /// Collects the NetworkObjectIds of all currently spawned EquipmentItems in the active scene.
    /// </summary>
    public static List<ulong> GetActiveSpawnedEquipmentNetworkIds()
    {
        var ids = new List<ulong>();
        foreach (var eq in EquipmentItem.AllEquipment)
        {
            if (eq != null && eq.NetworkObject != null && eq.NetworkObject.IsSpawned)
            {
                ids.Add(eq.NetworkObject.NetworkObjectId);
            }
        }
        return ids;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Cleans up all spawned equipment items and clears player inventory slots before transitioning scenes.
    /// </summary>
    public static void CleanupAllEquipmentBeforeSceneChange()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
            return;

        foreach (var player in PlayerController.AllPlayers)
        {
            if (player != null)
            {
                var inv = player.GetComponent<PlayerInventory>();
                if (inv != null) inv.ClearAllSlots();
            }
        }

        EquipmentItem[] allItems = FindObjectsByType<EquipmentItem>(FindObjectsInactive.Include);
        foreach (var item in allItems)
        {
            if (item != null)
            {
                DespawnOrDestroySceneItem(item);
            }
        }

        CleanupAllGhostOrbs();
    }

    /// <summary>
    /// Despawns/destroys any Ghost Orb so match-specific orbs never leak into the next scene.
    /// </summary>
    public static void CleanupAllGhostOrbs()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
            return;

        GhostOrbSystem[] orbs = FindObjectsByType<GhostOrbSystem>(FindObjectsInactive.Include);
        foreach (var orb in orbs)
        {
            if (orb == null) continue;
            NetworkObject netObj = orb.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                try
                {
                    netObj.Despawn(true);
                    continue;
                }
                catch { }
            }
            orb.gameObject.SetActive(false);
            Destroy(orb.gameObject);
        }
    }

    public void SpawnSelectedLoadoutInVan()
    {
        if (hasSpawnedLoadout) return;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            return; // Only Server/Host or offline standalone spawns the gear
        }

        hasSpawnedLoadout = true;

        ProgressionAndLoadoutManager.EnsureInstance();
        var loadoutMgr = ProgressionAndLoadoutManager.Instance;
        if (loadoutMgr == null) return;

        EquipmentSpawnPoint equipSpawn = EquipmentSpawnPoint.FindOrResolve();
        bool isLobbyArmory = (equipSpawn != null && equipSpawn.ShouldSpawnAllCatalogItems) ||
                             SceneManager.GetActiveScene().name == ProgressionAndLoadoutManager.LOBBY_SCENE_NAME;

        // 1. Collect all pre-placed equipment items in the scene by their EquipmentItem subclass type
        EquipmentItem[] allSceneEquipment = FindObjectsByType<EquipmentItem>(FindObjectsInactive.Include);
        Dictionary<Type, List<EquipmentItem>> sceneItemsByType = new Dictionary<Type, List<EquipmentItem>>();

        foreach (var item in allSceneEquipment)
        {
            if (item == null) continue;
            Type t = item.GetType();
            if (!sceneItemsByType.TryGetValue(t, out var list))
            {
                list = new List<EquipmentItem>();
                sceneItemsByType[t] = list;
            }
            list.Add(item);
        }

        // 2. Collect equipment prefabs from serialized list, NetworkManager Prefabs, and Editor AssetDatabase fallback
        Dictionary<Type, GameObject> registeredPrefabsByType = new Dictionary<Type, GameObject>();

        if (gearToSpawn != null)
        {
            foreach (var setup in gearToSpawn)
            {
                if (setup?.gearPrefab != null)
                {
                    registeredPrefabsByType[setup.gearPrefab.GetType()] = setup.gearPrefab.gameObject;
                }
            }
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null && NetworkManager.Singleton.NetworkConfig.Prefabs != null)
        {
            foreach (var netPrefab in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
            {
                if (netPrefab?.Prefab == null) continue;
                var eq = netPrefab.Prefab.GetComponent<EquipmentItem>();
                if (eq != null)
                {
                    registeredPrefabsByType[eq.GetType()] = netPrefab.Prefab;
                }
            }
        }

#if UNITY_EDITOR
        string[] editorPrefabPaths = new string[]
        {
            "Assets/Prefabs/FlashLightItem.prefab",
            "Assets/Prefabs/EMFReaderItem.prefab",
            "Assets/Prefabs/ThermometerItem.prefab",
            "Assets/Prefabs/SpiritBoxItem.prefab",
            "Assets/Prefabs/GhostWritingBookItem.prefab",
            "Assets/Prefabs/DotsProjector.prefab",
            "Assets/Prefabs/VideoCameraItem.prefab",
            "Assets/Prefabs/Crucifix.prefab",
            "Assets/Prefabs/SanityPillsItem.prefab",
            "Assets/Prefabs/RitualChalkItem.prefab"
        };
        foreach (string path in editorPrefabPaths)
        {
            GameObject p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p != null)
            {
                var eq = p.GetComponent<EquipmentItem>();
                if (eq != null && !registeredPrefabsByType.ContainsKey(eq.GetType()))
                {
                    registeredPrefabsByType[eq.GetType()] = p;
                }
            }
        }
#endif

        // 3. Determine which catalog items to spawn
        List<EquipmentCatalogEntry> chosenEntries = new List<EquipmentCatalogEntry>();
        Dictionary<string, int> effectiveCounts = new Dictionary<string, int>();

        if (isLobbyArmory)
        {
            // In the Lobby Armory Rack, spawn ALL 10 ghost hunting equipment types so the armory is fully stocked and interactable!
            foreach (var entry in loadoutMgr.Catalog)
            {
                chosenEntries.Add(entry);
                effectiveCounts[entry.id] = Mathf.Max(1, loadoutMgr.GetLoadoutCount(entry.id));
            }
        }
        else
        {
            foreach (var entry in loadoutMgr.Catalog)
            {
                int desiredCount = loadoutMgr.GetLoadoutCount(entry.id);
                if (desiredCount <= 0)
                {
                    if (sceneItemsByType.TryGetValue(entry.componentType, out List<EquipmentItem> unusedSceneItems) && unusedSceneItems != null)
                    {
                        foreach (var sceneItem in unusedSceneItems)
                        {
                            DespawnOrDestroySceneItem(sceneItem);
                        }
                    }
                }
                else
                {
                    chosenEntries.Add(entry);
                    effectiveCounts[entry.id] = desiredCount;
                }
            }
        }

        int totalSlots = chosenEntries.Count;
        if (totalSlots == 0) return;

        // 4. Spawn each equipment type vertically inside EquipmentSpawn; stack 2x+ quantities on the same slot
        for (int slotIdx = 0; slotIdx < totalSlots; slotIdx++)
        {
            var entry = chosenEntries[slotIdx];
            int desiredCount = effectiveCounts.TryGetValue(entry.id, out int cnt) ? cnt : Mathf.Max(1, loadoutMgr.GetLoadoutCount(entry.id));
            sceneItemsByType.TryGetValue(entry.componentType, out List<EquipmentItem> sceneInstances);

            Vector3 slotWorldPos;
            if (equipSpawn != null)
            {
                slotWorldPos = equipSpawn.GetVerticalSlotPosition(slotIdx, totalSlots);
            }
            else if (isLobbyArmory)
            {
                float yTop = 0.56f;
                float yBottom = -0.32f;
                float t = totalSlots == 1 ? 0.5f : (slotIdx + 0.5f) / totalSlots;
                slotWorldPos = new Vector3(-0.92f, Mathf.Lerp(yTop, yBottom, t), 0f);
            }
            else
            {
                float yTop = -4.42f;
                float yBottom = -4.87f;
                float t = totalSlots == 1 ? 0.5f : (slotIdx + 0.5f) / totalSlots;
                slotWorldPos = new Vector3(-1.1933f, Mathf.Lerp(yTop, yBottom, t), 0f);
            }

            Quaternion baseWorldRot = Quaternion.identity;
            Vector3 baseLocalScale = Vector3.one * 0.006f;
            string baseItemName = entry.displayName;

            if (registeredPrefabsByType.TryGetValue(entry.componentType, out GameObject regPrefab) && regPrefab != null)
            {
                baseLocalScale = regPrefab.transform.localScale;
                baseWorldRot = regPrefab.transform.rotation;
                baseItemName = regPrefab.name;
            }

            if (registeredPrefabsByType.TryGetValue(entry.componentType, out GameObject registeredPrefab) && registeredPrefab != null)
            {
                if (sceneInstances != null)
                {
                    foreach (var sceneItem in sceneInstances)
                    {
                        DespawnOrDestroySceneItem(sceneItem);
                    }
                }

                for (int i = 0; i < desiredCount; i++)
                {
                    Vector3 spawnPos = new Vector3(slotWorldPos.x, slotWorldPos.y, 0f);

                    GameObject cloneObj = Instantiate(registeredPrefab, spawnPos, baseWorldRot, null);
                    cloneObj.transform.position = spawnPos;
                    cloneObj.transform.rotation = baseWorldRot;
                    cloneObj.transform.localScale = baseLocalScale;
                    cloneObj.name = $"{baseItemName}(Clone)";
                    ConfigureShelfItem(cloneObj, i, equipSpawn, totalSlots, slotWorldPos, isLobbyArmory);
                    cloneObj.SetActive(true);
                    spawnPos = cloneObj.transform.position;

                    cloneObj.transform.position = spawnPos;
                    var eq = cloneObj.GetComponent<EquipmentItem>();
                    if (eq != null)
                    {
                        eq.SetSyncedScale(cloneObj.transform.localScale);
                    }

                    NetworkObject netObj = cloneObj.GetComponent<NetworkObject>();
                    if (netObj != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer)
                    {
                        try
                        {
                            ClearInScenePlacedFlag(netObj);
                            netObj.Spawn(true);
                            cloneObj.transform.position = spawnPos;
                            if (eq != null)
                            {
                                eq.SetSyncedScale(cloneObj.transform.localScale);
                                eq.DropItemRpc(spawnPos);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[TruckGearSpawner] Local spawn fallback for {cloneObj.name}: {ex.Message}");
                            cloneObj.transform.position = spawnPos;
                            if (eq != null) eq.InitializeLocalCloneFallback();
                        }
                    }
                    else
                    {
                        cloneObj.transform.position = spawnPos;
                        if (eq != null) eq.InitializeLocalCloneFallback();
                    }
                }
            }
        }
    }

    private static readonly Vector2 MaxRackSlotWorldSize = new Vector2(0.0585f, 0.0347f);

    private static void ConfigureShelfItem(
        GameObject itemObj,
        int stackIndex,
        EquipmentSpawnPoint equipSpawn,
        int totalSlots,
        Vector3 slotWorldPos,
        bool isLobbyArmory)
    {
        if (itemObj == null) return;

        int interactLayer = LayerMask.NameToLayer("Interactable");
        if (interactLayer >= 0)
        {
            itemObj.layer = interactLayer;
        }

        var rb = itemObj.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        var cols = itemObj.GetComponents<Collider2D>();
        foreach (var col in cols)
        {
            if (col != null) col.isTrigger = true;
        }

        var sr = itemObj.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.enabled = true;
            sr.sortingLayerName = "Item";
            sr.sortingOrder = 15 + stackIndex;

            if (sr.sprite != null)
            {
                Vector2 slotMax = equipSpawn != null
                    ? equipSpawn.GetSlotMaxSize(totalSlots)
                    : MaxRackSlotWorldSize;
                if (!isLobbyArmory)
                {
                    slotMax = Vector2.Min(slotMax, MaxRackSlotWorldSize);
                }

                // Clamp down only if visible tight artwork overflows the padded slot ceiling
                if (TryGetVisibleSpriteWorldMetrics(sr.sprite, itemObj.transform.rotation, itemObj.transform.localScale, out Vector2 visSize, out _))
                {
                    if (visSize.x > slotMax.x || visSize.y > slotMax.y)
                    {
                        float fitFactor = Mathf.Min(
                            slotMax.x / Mathf.Max(0.0001f, visSize.x),
                            slotMax.y / Mathf.Max(0.0001f, visSize.y));
                        itemObj.transform.localScale *= Mathf.Clamp(fitFactor, 0.05f, 1f);
                    }
                }

                // Sync specialized scaling hooks BEFORE reading final centered pivot offset
                var book = itemObj.GetComponent<GhostWritingBookItem>();
                if (book != null)
                {
                    book.SetBaseScale(itemObj.transform.localScale);
                }

                var chalk = itemObj.GetComponent<RitualChalkItem>();
                if (chalk != null)
                {
                    float spriteW = (sr.sprite.pixelsPerUnit > 0f) ? (sr.sprite.rect.width / sr.sprite.pixelsPerUnit) : 4.64f;
                    float shelfLength = Mathf.Clamp(Mathf.Abs(itemObj.transform.localScale.x) * spriteW, 0.012f, 0.035f);
                    chalk.SetShelfTargetLength(shelfLength);
                }

                // Center visible artwork silhouette inside the slot (handles off-center canvas pivots)
                if (TryGetVisibleSpriteWorldMetrics(sr.sprite, itemObj.transform.rotation, itemObj.transform.localScale, out _, out Vector2 visibleCenterOffset))
                {
                    itemObj.transform.position = new Vector3(
                        slotWorldPos.x - visibleCenterOffset.x,
                        slotWorldPos.y - visibleCenterOffset.y,
                        0f);
                }
            }
        }
    }

    private static bool TryGetVisibleSpriteWorldMetrics(
        Sprite sprite,
        Quaternion worldRotation,
        Vector3 localScale,
        out Vector2 worldSize,
        out Vector2 visibleCenterOffset)
    {
        worldSize = Vector2.zero;
        visibleCenterOffset = Vector2.zero;
        if (sprite == null) return false;

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        Vector2[] verts = sprite.vertices;
        if (verts != null && verts.Length > 0)
        {
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 localPt = new Vector3(verts[i].x * localScale.x, verts[i].y * localScale.y, 0f);
                Vector3 rotPt = worldRotation * localPt;
                if (rotPt.x < min.x) min.x = rotPt.x;
                if (rotPt.y < min.y) min.y = rotPt.y;
                if (rotPt.x > max.x) max.x = rotPt.x;
                if (rotPt.y > max.y) max.y = rotPt.y;
            }
        }
        else
        {
            Bounds sb = sprite.bounds;
            Vector3 c = sb.center;
            Vector3 e = sb.extents;
            Vector3[] corners =
            {
                new Vector3((c.x - e.x) * localScale.x, (c.y - e.y) * localScale.y, 0f),
                new Vector3((c.x + e.x) * localScale.x, (c.y - e.y) * localScale.y, 0f),
                new Vector3((c.x - e.x) * localScale.x, (c.y + e.y) * localScale.y, 0f),
                new Vector3((c.x + e.x) * localScale.x, (c.y + e.y) * localScale.y, 0f)
            };
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 rotPt = worldRotation * corners[i];
                if (rotPt.x < min.x) min.x = rotPt.x;
                if (rotPt.y < min.y) min.y = rotPt.y;
                if (rotPt.x > max.x) max.x = rotPt.x;
                if (rotPt.y > max.y) max.y = rotPt.y;
            }
        }

        if (max.x <= min.x || max.y <= min.y) return false;

        worldSize = new Vector2(max.x - min.x, max.y - min.y);
        visibleCenterOffset = new Vector2((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f);
        return true;
    }

    private static void DespawnOrDestroySceneItem(EquipmentItem item)
    {
        if (item == null) return;
        NetworkObject netObj = item.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            try
            {
                netObj.Despawn(true);
                return;
            }
            catch { }
        }
        item.gameObject.SetActive(false);
        Destroy(item.gameObject);
    }

    private static void ClearInScenePlacedFlag(NetworkObject netObj)
    {
        if (netObj == null) return;
        try
        {
            FieldInfo field = typeof(NetworkObject).GetField("m_InScenePlaced", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                if (field.FieldType == typeof(bool))
                    field.SetValue(netObj, false);
                else if (field.FieldType == typeof(int))
                    field.SetValue(netObj, 0);
            }
        }
        catch { }
    }
}
