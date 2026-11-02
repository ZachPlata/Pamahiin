using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side manager that handles match progression, specifically tracking journal deductions,
/// applying lobby contract difficulty & van equipment loadout, and triggering the end-game reveal sequence.
/// </summary>
public class GameMatchManager : NetworkBehaviour
{
    public static GameMatchManager Instance { get; private set; }

    public Action<string> OnMatchEnded; // Passes the true ghost name

    private Dictionary<ulong, string> playerDeductions = new Dictionary<ulong, string>();
    private string cachedTrueGhostName = "";
    private bool isGhostDefeated = false;

    public bool IsGhostDefeated => isGhostDefeated;

    public void MarkGhostDefeated()
    {
        isGhostDefeated = true;
    }

    public void SetTrueGhostName(string ghostName)
    {
        if (!string.IsNullOrEmpty(ghostName) && !ghostName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            cachedTrueGhostName = ghostName;
            Debug.Log($"[GameMatchManager] True Ghost Name cached: {cachedTrueGhostName}");
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ProgressionAndLoadoutManager.EnsureInstance();
    }

    private void Start()
    {
        // Reset previous match deduction guess at start of a new investigation
        JournalController.LastLockedInGuess = "None";

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer)
        {
            EnsureVanLoadoutAndDifficultyApplied();
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            EnsureVanLoadoutAndDifficultyApplied();
        }
    }

    private void EnsureVanLoadoutAndDifficultyApplied()
    {
        // 1. Spawn selected equipment clones in the van
        var spawner = FindAnyObjectByType<TruckGearSpawner>();
        if (spawner == null)
        {
            spawner = new GameObject("TruckGearSpawner").AddComponent<TruckGearSpawner>();
        }
        spawner.SpawnSelectedLoadoutInVan();

        // 2. Apply selected Contract Difficulty modifiers to GhostHandler
        var ghost = FindAnyObjectByType<GhostHandler>();
        if (ghost != null && ProgressionAndLoadoutManager.Instance != null)
        {
            switch (ProgressionAndLoadoutManager.Instance.SelectedDifficulty)
            {
                case ContractDifficulty.Amateur:
                    ghost.huntDurationMin = 18f;
                    ghost.huntDurationMax = 26f;
                    break;
                case ContractDifficulty.Intermediate:
                    ghost.huntDurationMin = 24f;
                    ghost.huntDurationMax = 34f;
                    break;
                case ContractDifficulty.Professional:
                    ghost.huntDurationMin = 30f;
                    ghost.huntDurationMax = 42f;
                    break;
                case ContractDifficulty.Nightmare:
                    ghost.huntDurationMin = 38f;
                    ghost.huntDurationMax = 52f;
                    break;
            }
        }
    }

    public void LockInDeduction(string ghostType)
    {
        RecordDeduction(ghostType);
    }

    public void RecordDeduction(string ghostType)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            SubmitDeductionServerRpc(ghostType);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitDeductionServerRpc(string ghostType, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!playerDeductions.ContainsKey(clientId))
        {
            playerDeductions.Add(clientId, ghostType);
        }
        else
        {
            playerDeductions[clientId] = ghostType;
        }

        Debug.Log($"Server recorded deduction '{ghostType}' from client {clientId}. Match continues until van End Game button is pressed.");
    }

    public void ForceEndMatch()
    {
        string actualGhostName = cachedTrueGhostName;
        var ghost = UnityEngine.Object.FindAnyObjectByType<GhostHandler>();
        if (ghost != null)
        {
            if (ghost.IsExorcised) isGhostDefeated = true;
            if (!string.IsNullOrEmpty(ghost.ghostName) && !ghost.ghostName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                actualGhostName = ghost.ghostName;
                cachedTrueGhostName = ghost.ghostName;
            }
        }

        if (ExorcismManager.Instance != null && ExorcismManager.Instance.IsExorcismCompleted)
        {
            isGhostDefeated = true;
        }

        if (string.IsNullOrEmpty(actualGhostName))
        {
            actualGhostName = "Unknown Entity";
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            // Offline fallback
            var revealController = UnityEngine.Object.FindAnyObjectByType<EndGameRevealController>();
            if (revealController != null) revealController.TriggerReveal(actualGhostName);
            return;
        }

        if (!IsServer) return;

        EndMatchClientRpc(actualGhostName, isGhostDefeated);
    }

    [ClientRpc]
    private void EndMatchClientRpc(string trueName, bool defeated)
    {
        isGhostDefeated = defeated;

        // Lock player controls
        if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            var pc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = false;
        }

        // Trigger the UI Reveal
        OnMatchEnded?.Invoke(trueName);
    }

    public string GetLocalDeduction()
    {
        return null;
    }
}
