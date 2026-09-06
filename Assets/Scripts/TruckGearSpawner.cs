using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Spawns the required investigation gear inside the truck/van when the server loads the game scene.
/// This fulfills the "Pre-Loaded Gear" requirement (no loadout UI, physically placed gear).
/// </summary>
public class TruckGearSpawner : NetworkBehaviour
{
    [System.Serializable]
    public class GearSpawnSetup
    {
        public EquipmentItem gearPrefab;
        public int spawnCount = 1;
        public Transform[] spawnPoints;
    }

    [Header("Gear Settings")]
    [SerializeField] private List<GearSpawnSetup> gearToSpawn = new List<GearSpawnSetup>();

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            SpawnGear();
        }
    }

    private void SpawnGear()
    {
        foreach (var setup in gearToSpawn)
        {
            if (setup.gearPrefab == null) continue;

            int spawned = 0;
            for (int i = 0; i < setup.spawnCount; i++)
            {
                if (spawned >= setup.spawnPoints.Length)
                {
                    Debug.LogWarning($"Not enough spawn points defined for {setup.gearPrefab.name}");
                    break;
                }

                Transform spawnPoint = setup.spawnPoints[spawned];
                
                // Instantiate and spawn over the network
                EquipmentItem instance = Instantiate(setup.gearPrefab, spawnPoint.position, spawnPoint.rotation);
                NetworkObject netObj = instance.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    netObj.Spawn();
                }
                else
                {
                    Debug.LogError($"Gear prefab {setup.gearPrefab.name} is missing a NetworkObject component!");
                }
                
                spawned++;
            }
        }
    }
}
