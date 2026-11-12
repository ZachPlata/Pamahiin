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

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (!hasSpawnedLoadout && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer))
        {
            SpawnSelectedLoadoutInVan();
        }
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

        EquipmentItem[] allItems = FindObjectsByType<EquipmentItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var item in allItems)
        {
            if (item != null)
            {
                DespawnOrDestroySceneItem(item);
            }
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
        EquipmentItem[] allSceneEquipment = FindObjectsByType<EquipmentItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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

            if (chosenEntries.Count == 0)
            {
                string[] defaultStarterIds = { "Flashlight", "EMFReader", "Thermometer", "SpiritBox", "GhostWritingBook", "VideoCamera" };
                foreach (string starterId in defaultStarterIds)
                {
                    var entry = loadoutMgr.Catalog.Find(e => e.id == starterId);
                    if (entry != null)
                    {
                        chosenEntries.Add(entry);
                        effectiveCounts[entry.id] = 1;
                    }
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
                    ConfigureShelfItem(cloneObj, i);
                    cloneObj.SetActive(true);

                    var eq = cloneObj.GetComponent<EquipmentItem>();
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

    private static void ConfigureShelfItem(GameObject itemObj, int stackIndex)
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
                float maxWorldDim = Mathf.Max(sr.bounds.size.x, sr.bounds.size.y);
                if (maxWorldDim > 0.055f)
                {
                    float scaleFactor = 0.042f / maxWorldDim;
                    itemObj.transform.localScale *= scaleFactor;
                }
            }
        }
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
