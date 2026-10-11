using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages both host-direct-kick and democratic vote-kick.
///
/// Host  → can kick any client instantly via KickPlayer().
/// Client → calls RequestVoteKick(targetId). Once ≥50% of connected
///          non-host clients vote for the same target, the host executes
///          the kick automatically.
///
/// Events are fired so the PauseMenuController can update the UI.
/// </summary>
public class VoteKickManager : NetworkBehaviour
{
    public static VoteKickManager Instance { get; private set; }

    // votes[targetClientId] = set of voter clientIds
    private Dictionary<ulong, HashSet<ulong>> votes = new Dictionary<ulong, HashSet<ulong>>();

    /// <summary>Fired on all clients when a vote is cast or resolved.</summary>
    public event System.Action OnVoteStateChanged;

    /// <summary>Fired on the kicked client just before disconnect.</summary>
    public event System.Action OnLocalPlayerKicked;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Host Direct Kick ────────────────────────────────────────────────────

    /// <summary>Server-only. Immediately disconnects the target.</summary>
    public void HostKick(ulong targetClientId)
    {
        if (!IsServer) return;
        if (targetClientId == NetworkManager.Singleton.LocalClientId) return; // can't kick yourself

        NotifyKickedClientRpc(targetClientId);
        // Small delay so the ClientRpc reaches the target first
        StartCoroutine(DelayedKick(targetClientId, 0.3f));
    }

    private System.Collections.IEnumerator DelayedKick(ulong clientId, float delay)
    {
        yield return new WaitForSeconds(delay);
        NetworkManager.Singleton.DisconnectClient(clientId);
        votes.Remove(clientId);
    }

    // ── Vote Kick ──────────────────────────────────────────────────────────

    /// <summary>Any client calls this to vote against a target.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestVoteKickServerRpc(ulong targetClientId, RpcParams rpcParams = default)
    {
        ulong voterId = rpcParams.Receive.SenderClientId;

        // Guard: cannot vote for host or yourself
        if (targetClientId == NetworkManager.Singleton.LocalClientId) return;
        if (voterId == targetClientId) return;

        if (!votes.ContainsKey(targetClientId))
            votes[targetClientId] = new HashSet<ulong>();

        votes[targetClientId].Add(voterId);

        BroadcastVoteStateClientRpc(targetClientId, votes[targetClientId].Count);

        // Majority = > 50% of non-host connected clients
        int eligibleVoters = NetworkManager.Singleton.ConnectedClientsIds.Count - 1; // exclude host
        if (eligibleVoters > 0 && votes[targetClientId].Count * 2 > eligibleVoters)
        {
            Debug.Log($"VoteKick: majority reached for client {targetClientId}. Kicking.");
            HostKick(targetClientId);
        }
    }

    /// <summary>Clears votes for a player (e.g. when they disconnect naturally).</summary>
    public void ClearVotesFor(ulong clientId)
    {
        votes.Remove(clientId);
    }

    // ── RPCs ───────────────────────────────────────────────────────────────

    [Rpc(SendTo.ClientsAndHost)]
    private void BroadcastVoteStateClientRpc(ulong targetClientId, int voteCount)
    {
        Debug.Log($"VoteKick: {voteCount} vote(s) against client {targetClientId}.");
        OnVoteStateChanged?.Invoke();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyKickedClientRpc(ulong kickedClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == kickedClientId)
        {
            OnLocalPlayerKicked?.Invoke();
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Returns how many votes have been cast against a client.</summary>
    public int GetVoteCount(ulong targetClientId)
    {
        return votes.TryGetValue(targetClientId, out var set) ? set.Count : 0;
    }

    /// <summary>Total clients eligible to be kicked (everyone except the host).</summary>
    public int GetEligiblePlayerCount()
    {
        if (NetworkManager.Singleton == null) return 0;
        return Mathf.Max(0, NetworkManager.Singleton.ConnectedClientsIds.Count - 1);
    }
}
