using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using System;

/// <summary>
/// Server-side manager that handles match progression, specifically tracking journal deductions
/// and triggering the end-game reveal sequence.
/// </summary>
public class GameMatchManager : NetworkBehaviour
{
    public static GameMatchManager Instance { get; private set; }

    public Action<string> OnMatchEnded; // Passes the true ghost name

    private Dictionary<ulong, string> playerDeductions = new Dictionary<ulong, string>();

    // We dynamically fetch the ghost name from GhostController instead.

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void LockInDeduction(string ghostType)
    {
        if (NetworkManager.Singleton.IsClient)
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

        Debug.Log($"Server recorded deduction '{ghostType}' from client {clientId}");

        // In a real game, we might check if all alive players have locked in.
        // For testing/MVP, let's assume if the Host submits, we end the game, or if everyone submits.
        CheckForMatchEnd();
    }

    private void CheckForMatchEnd()
    {
        // Simple condition: everyone connected has submitted a deduction
        if (playerDeductions.Count >= NetworkManager.Singleton.ConnectedClientsIds.Count)
        {
            string actualGhostName = "Unknown Entity";
            var ghost = UnityEngine.Object.FindAnyObjectByType<GhostController>();
            if (ghost != null)
            {
                actualGhostName = ghost.ghostName;
            }

            EndMatchClientRpc(actualGhostName);
        }
    }

    [ClientRpc]
    private void EndMatchClientRpc(string trueName)
    {
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
        // Used by UI to compare guess vs reality
        // Since dictionary is server-only, UI should have cached its own guess, 
        // but we'll expose a hook if needed.
        return null; // Implemented locally in JournalController ideally
    }
}
