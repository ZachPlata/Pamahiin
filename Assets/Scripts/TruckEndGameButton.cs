using System.Collections;
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider2D))]
public class TruckEndGameButton : NetworkBehaviour, IInteractable
{
    private const float StageTransitionDebounceSeconds = 0.6f;

    private readonly NetworkVariable<bool> syncedRampOpened = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool hasTriggered = false;
    private bool localRampOpened = false;
    private float rampOpenedUnscaledTime = -10f;
    private bool hasSearchedRampDoor = false;
    private NetworkDoor cachedRampDoor;

    private void Awake()
    {
        int interactLayer = LayerMask.NameToLayer("Interactable");
        if (interactLayer >= 0)
        {
            gameObject.layer = interactLayer;
        }

        var col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        // Prevent Netcode from disabling this in-scene NetworkObject if parented under a non-NetworkObject (Van)
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && transform.parent != null && transform.parent.GetComponentInParent<NetworkObject>() == null)
        {
            netObj.AutoObjectParentSync = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        syncedRampOpened.OnValueChanged += OnSyncedRampOpenedChanged;
        if (syncedRampOpened.Value)
        {
            localRampOpened = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        syncedRampOpened.OnValueChanged -= OnSyncedRampOpenedChanged;
        base.OnNetworkDespawn();
    }

    private void OnSyncedRampOpenedChanged(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            localRampOpened = true;
            rampOpenedUnscaledTime = Time.unscaledTime;
        }
    }

    private NetworkDoor GetRampDoor()
    {
        if (cachedRampDoor != null) return cachedRampDoor;
        if (hasSearchedRampDoor) return null;

        hasSearchedRampDoor = true;
        NetworkDoor[] allDoors = Object.FindObjectsByType<NetworkDoor>(FindObjectsInactive.Include);
        NetworkDoor bestDoor = null;
        bool bestValid = false;
        float bestDist = float.MaxValue;

        for (int i = 0; i < allDoors.Length; i++)
        {
            NetworkDoor door = allDoors[i];
            if (door == null) continue;

            string lower = door.gameObject.name.ToLowerInvariant();
            bool isRamp = door.gameObject.name == "JeepneyRampDoor"
                || lower.Contains("jeepneyramp")
                || lower.Contains("rampdoor");
            if (!isRamp) continue;

            bool valid = door.HasValidDoorVisual;
            float dist = Vector2.Distance(transform.position, door.transform.position);
            if (bestDoor == null || (valid && !bestValid) || (valid == bestValid && dist < bestDist))
            {
                bestDoor = door;
                bestValid = valid;
                bestDist = dist;
            }
        }

        cachedRampDoor = bestDoor;
        return cachedRampDoor;
    }

    private bool IsRampStageCompleted()
    {
        NetworkDoor ramp = GetRampDoor();
        if (ramp == null) return true; // Scenes without a Jeepney ramp (e.g., GameMapScene) skip Stage 1

        if (IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            return syncedRampOpened.Value || ramp.IsOpen;
        }

        return localRampOpened || ramp.IsOpen;
    }

    private static bool IsFrontDoorGateBlocked()
    {
        return NetworkDoor.HasAnyFrontDoor() && !NetworkDoor.HasAnyFrontDoorOpened();
    }

    private void OpenJeepneyRampServer()
    {
        localRampOpened = true;
        rampOpenedUnscaledTime = Time.unscaledTime;

        if (IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
        {
            syncedRampOpened.Value = true;
        }

        NetworkDoor ramp = GetRampDoor();
        if (ramp != null)
        {
            ramp.SetDoorOpen(true);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void OpenJeepneyRampRpc(RpcParams rpcParams = default)
    {
        if (PlayerController.TryGetByClientId(rpcParams.Receive.SenderClientId, out var sender) && !sender.IsAlive)
        {
            return;
        }
        OpenJeepneyRampServer();
    }

    public void Interact()
    {
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
        if (hasTriggered) return;

        // Stage 1: Open the Jeepney rear ramp door first
        if (!IsRampStageCompleted())
        {
            Debug.Log("[TruckEndGameButton] Stage 1 interacted: Opening Jeepney ramp.");
            GameAudioManager.PlayUIClick();
            localRampOpened = true;
            rampOpenedUnscaledTime = Time.unscaledTime;

            if (IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)
            {
                OpenJeepneyRampRpc();
            }
            else
            {
                OpenJeepneyRampServer();
            }
            return;
        }

        // Prevent accidental double-tap across the Stage 1 -> Stage 2 boundary
        if (Time.unscaledTime - rampOpenedUnscaledTime < StageTransitionDebounceSeconds)
        {
            return;
        }

        // Stage 2 gate: Mission departure requires the house front door to have been opened at least once
        if (!ProgressionAndLoadoutManager.IsInWarehouseTutorialScene && IsFrontDoorGateBlocked())
        {
            Debug.Log("[TruckEndGameButton] Departure blocked: open the house front door first.");
            GameAudioManager.PlayDoorLocked(transform.position);
            return;
        }

        hasTriggered = true;

        Debug.Log("[TruckEndGameButton] Stage 2 interacted: Ending mission and departing!");
        GameAudioManager.PlayUIClick();

        if (ProgressionAndLoadoutManager.IsInWarehouseTutorialScene && WarehouseTutorialManager.Instance != null)
        {
            WarehouseTutorialManager.Instance.CompleteTutorialContractStep();
            return;
        }

        var matchMgr = GameMatchManager.Instance != null
            ? GameMatchManager.Instance
            : Object.FindAnyObjectByType<GameMatchManager>();

        if (matchMgr != null)
        {
            matchMgr.RequestEndMatch();
            return;
        }

        // Fallback if GameMatchManager is missing from the scene
        if (IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)
        {
            RequestEndMatchServerRpc();
        }
        else
        {
            TriggerEndMatchFallback();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestEndMatchServerRpc(RpcParams rpcParams = default)
    {
        if (PlayerController.TryGetByClientId(rpcParams.Receive.SenderClientId, out var sender) && !sender.IsAlive)
        {
            return;
        }
        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.ForceEndMatch();
        }
        else
        {
            TriggerEndMatchFallback();
        }
    }

    private void TriggerEndMatchFallback()
    {
        if (PlayerController.LocalInstance != null)
        {
            PlayerController.LocalInstance.enabled = false;
        }

        var rampGo = GameObject.Find("JeepneyRampDoor");
        var ramp = rampGo != null ? rampGo.GetComponent<NetworkDoor>() : null;
        if (ramp != null)
        {
            ramp.SetDoorOpen(false);
        }

        StartCoroutine(WaitForTruckAudioFallbackRoutine(ramp));
    }

    private IEnumerator WaitForTruckAudioFallbackRoutine(NetworkDoor ramp)
    {
        Vector3 sfxPos = ramp != null ? ramp.transform.position : transform.position;
        float departDuration = GameAudioManager.PlayTruckStartDepart(sfxPos);
        if (departDuration <= 0f) departDuration = 1.5f;

        float elapsed = 0f;
        float maxWait = departDuration + 1.5f;
        while (elapsed < departDuration || (ramp != null && ramp.CurrentProgress > 0.02f && elapsed < maxWait))
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        string ghostName = "Unknown Entity";
        var ghost = Object.FindAnyObjectByType<GhostHandler>();
        if (ghost != null && !string.IsNullOrEmpty(ghost.ghostName) && !ghost.ghostName.Equals("Unknown", System.StringComparison.OrdinalIgnoreCase))
        {
            ghostName = ghost.ghostName;
        }

        EndGameRevealController.PreparePendingReveal(ghostName, ExorcismManager.Instance != null && ExorcismManager.Instance.IsExorcismCompleted);

        var loadingMgr = LoadingScreenManager.EnsureInstance();
        if (loadingMgr != null)
        {
            loadingMgr.ShowIntermission(
                "DEPARTING INVESTIGATION SITE",
                "Unloading investigation area & returning to Safe House Lobby...",
                "CASE DEBRIEFING");
            loadingMgr.SetProgress(0.35f, "Returning to Safe House Lobby...");
            yield return new WaitForSecondsRealtime(1.0f);
        }

        TruckGearSpawner.CleanupAllGhostOrbs();
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.ReturnToLobbyFromMatch();
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(ProgressionAndLoadoutManager.LOBBY_SCENE_NAME);
        }
    }

    public string GetInteractText()
    {
        if (!IsRampStageCompleted()) return "Open Jeepney Ramp";
        if (!ProgressionAndLoadoutManager.IsInWarehouseTutorialScene && IsFrontDoorGateBlocked()) return "Open Front Door First";
        return ProgressionAndLoadoutManager.IsInWarehouseTutorialScene ? "Complete Tutorial Contract" : "End Mission and Leave";
    }

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPos) { }
    public void OnDragEnd(ulong clientId) { }
}
