using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Spawns clones of the ghost hunting equipment selected and purchased by the player in the Lobby
/// inside the van when the investigation map loads.
/// </summary>
public class TruckGearSpawner : NetworkBehaviour
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
        // If TruckGearSpawner was created at runtime without a NetworkObject, execute on server/host in Start
        if (!hasSpawnedLoadout && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer))
        {
            SpawnSelectedLoadoutInVan();
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && !hasSpawnedLoadout)
        {
            SpawnSelectedLoadoutInVan();
        }
    }

    public void SpawnSelectedLoadoutInVan()
    {
        if (hasSpawnedLoadout) return;
        hasSpawnedLoadout = true;

        ProgressionAndLoadoutManager.EnsureInstance();
        var loadoutMgr = ProgressionAndLoadoutManager.Instance;
        if (loadoutMgr == null) return;

        // 1. Collect all pre-placed van equipment items in the scene by their EquipmentItem subclass type
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

        // 2. Collect registered NetworkPrefabs by their EquipmentItem subclass type
        Dictionary<Type, GameObject> registeredPrefabsByType = new Dictionary<Type, GameObject>();
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

        // 3. For each equipment type in the catalog, despawn unchosen static items and spawn clones for chosen quantities
        foreach (var entry in loadoutMgr.Catalog)
        {
            int desiredCount = loadoutMgr.GetLoadoutCount(entry.id);
            sceneItemsByType.TryGetValue(entry.componentType, out List<EquipmentItem> sceneInstances);

            if (desiredCount <= 0)
            {
                // Player did not bring this equipment type -> remove all static instances from the van
                if (sceneInstances != null)
                {
                    foreach (var sceneItem in sceneInstances)
                    {
                        DespawnOrDestroySceneItem(sceneItem);
                    }
                }
                continue;
            }

            // Determine spawn transform reference from the van's shelf layout
            Vector3 baseWorldPos = new Vector3(1.16f, 2.95f, 0f);
            Quaternion baseWorldRot = Quaternion.Euler(0f, 0f, 90f);
            Vector3 baseLocalScale = Vector3.one * 0.008f;
            Transform vanParent = null;
            string baseItemName = entry.id;

            if (sceneInstances != null && sceneInstances.Count > 0 && sceneInstances[0] != null)
            {
                EquipmentItem refItem = sceneInstances[0];
                baseWorldPos = refItem.transform.position;
                baseWorldRot = refItem.transform.rotation;
                baseLocalScale = refItem.transform.localScale;
                vanParent = refItem.transform.parent;
                baseItemName = refItem.gameObject.name.Replace("(Clone)", "").Trim();
            }

            // Create a clean inactive template clone from the scene item before modifying/despawning it
            EquipmentItem runtimeTemplate = null;
            if (sceneInstances != null && sceneInstances.Count > 0 && sceneInstances[0] != null)
            {
                runtimeTemplate = Instantiate(sceneInstances[0], baseWorldPos, baseWorldRot, vanParent);
                runtimeTemplate.gameObject.name = baseItemName;
                runtimeTemplate.gameObject.SetActive(false);
                ClearInScenePlacedFlag(runtimeTemplate.GetComponent<NetworkObject>());
            }

            bool spawnedAllViaPrefab = false;
            if (registeredPrefabsByType.TryGetValue(entry.componentType, out GameObject registeredPrefab) && registeredPrefab != null)
            {
                // Despawn the static scene instances and instantiate pure prefab clones
                if (sceneInstances != null)
                {
                    foreach (var sceneItem in sceneInstances)
                    {
                        DespawnOrDestroySceneItem(sceneItem);
                    }
                }

                for (int i = 0; i < desiredCount; i++)
                {
                    Vector3 spawnPos = baseWorldPos + new Vector3(i * 0.022f, 0f, 0f);
                    // Prefer cloning runtimeTemplate if registeredPrefab has different visual scale/sprites (e.g. legacy placeholder FlashLightItem.prefab)
                    GameObject sourceToClone = (runtimeTemplate != null && entry.componentType == typeof(FlashlightItem))
                        ? runtimeTemplate.gameObject
                        : registeredPrefab;

                    GameObject cloneObj = Instantiate(sourceToClone, spawnPos, baseWorldRot, vanParent);
                    cloneObj.transform.localScale = baseLocalScale;
                    cloneObj.name = $"{baseItemName}(Clone)";
                    cloneObj.SetActive(true);

                    NetworkObject netObj = cloneObj.GetComponent<NetworkObject>();
                    if (netObj != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                    {
                        try
                        {
                            ClearInScenePlacedFlag(netObj);
                            netObj.Spawn(true);
                        }
                        catch
                        {
                            var eq = cloneObj.GetComponent<EquipmentItem>();
                            if (eq != null) eq.InitializeLocalCloneFallback();
                        }
                    }
                }
                spawnedAllViaPrefab = true;
            }

            if (!spawnedAllViaPrefab)
            {
                // Fallback if Unity Editor hasn't re-imported DefaultNetworkPrefabs.asset yet:
                // Keep up to desiredCount scene instances as active clones, despawn any excess, and instantiate extra clones
                int existingCount = sceneInstances != null ? sceneInstances.Count : 0;

                for (int i = 0; i < existingCount; i++)
                {
                    var sceneItem = sceneInstances[i];
                    if (sceneItem == null) continue;

                    if (i < desiredCount)
                    {
                        sceneItem.gameObject.name = $"{baseItemName}(Clone)";
                        sceneItem.transform.position = baseWorldPos + new Vector3(i * 0.022f, 0f, 0f);
                        sceneItem.gameObject.SetActive(true);
                    }
                    else
                    {
                        DespawnOrDestroySceneItem(sceneItem);
                    }
                }

                if (runtimeTemplate != null)
                {
                    for (int i = existingCount; i < desiredCount; i++)
                    {
                        Vector3 spawnPos = baseWorldPos + new Vector3(i * 0.022f, 0f, 0f);
                        EquipmentItem extraClone = Instantiate(runtimeTemplate, spawnPos, baseWorldRot, vanParent);
                        extraClone.transform.localScale = baseLocalScale;
                        extraClone.gameObject.name = $"{baseItemName}(Clone)";
                        extraClone.gameObject.SetActive(true);
                        extraClone.InitializeLocalCloneFallback();
                    }
                }
            }

            if (runtimeTemplate != null)
            {
                Destroy(runtimeTemplate.gameObject);
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
