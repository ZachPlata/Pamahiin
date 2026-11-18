using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

public enum EvidenceType
{
    EMF,
    Crucifix, // Handled as an interaction type that is allowed during hunts
    Freezing,
    DOTS,
    GhostWriting,
    SpiritBox,
    GhostOrbs
}

public enum GhostHandlerState
{
    Dormant,
    Searching,
    Chasing,
    Investigating,
    Returning
}

public class GhostHandler : NetworkBehaviour
{
    [Header("Behaviors")]
    public EventGhostBehavior eventGhost;
    public HunterGhostBehavior hunterGhost;

    [Header("Settings")]
    public Collider2D ghostroomMarker;
    public float huntDurationMin = 20f;
    public float huntDurationMax = 30f;

    [Header("Evidence & Features")]
    public System.Collections.Generic.List<EvidenceType> currentEvidence = new System.Collections.Generic.List<EvidenceType>();
    public GameObject ghostOrbPrefab;

    private GhostOrbSystem activeGhostOrb;
    private bool hasActivated = false;

    /// <summary>The single native Ghost Orb spawned for this ghost (null if none).</summary>
    public GhostOrbSystem ActiveGhostOrb => activeGhostOrb;

    public string ghostName = "Unknown";

    public bool HasEvidence(EvidenceType type)
    {
        return currentEvidence.Contains(type);
    }

    private void InitializeGhostIdentity()
    {
        if (currentEvidence.Count > 0) return; // Already manually set

        // Always add Crucifix as a base interaction
        currentEvidence.Add(EvidenceType.Crucifix);

        GhostCatalog.GhostDefinition chosen = GhostCatalog.GetRandomGhost();
        ghostName = chosen.Id;
        for (int i = 0; i < chosen.Evidences.Length; i++)
        {
            currentEvidence.Add(chosen.Evidences[i]);
        }

        if (GameMatchManager.Instance != null && !string.IsNullOrEmpty(ghostName) && !ghostName.Equals("Unknown", System.StringComparison.OrdinalIgnoreCase))
        {
            GameMatchManager.Instance.SetTrueGhostName(ghostName);
        }
    }

    private bool isExorcised = false;
    public bool IsExorcised => isExorcised || syncedExorcised.Value;

    private NetworkVariable<GhostHandlerState> currentState = new NetworkVariable<GhostHandlerState>(
        GhostHandlerState.Dormant, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isExorcismFailedHunt = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> syncedGhostActivated = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> syncedExorcised = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> syncedEvidenceMask = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<FixedString128Bytes> syncedGhostName = new NetworkVariable<FixedString128Bytes>(
        new FixedString128Bytes("Unknown"), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<FixedString128Bytes> syncedRoomMarkerName = new NetworkVariable<FixedString128Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<Vector2> syncedRoomCenter = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<float> syncedSetupGraceRemaining = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<float> syncedHuntGraceRemaining = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool hasSubscribedSyncCallbacks = false;

    public GhostHandlerState CurrentState => currentState.Value;
    public bool IsExorcismFailedHunt => isExorcismFailedHunt.Value;
    public bool IsInSetupGracePeriod => IsServer
        ? (setupGraceDuration > 0f && (!setupGraceStarted || Time.time < setupGracePeriodEndTime))
        : syncedSetupGraceRemaining.Value > 0f;
    public float SetupGracePeriodRemaining => IsServer
        ? (setupGraceStarted ? Mathf.Max(0f, setupGracePeriodEndTime - Time.time) : setupGraceDuration)
        : syncedSetupGraceRemaining.Value;
    public bool IsInHuntGracePeriod => syncedHuntGraceRemaining.Value > 0f;
    public float HuntGracePeriodRemaining => syncedHuntGraceRemaining.Value;

    private float huntTimer = 0f;
    private float currentHuntDuration = 0f;
    private float setupGraceDuration = 0f;
    private float setupGracePeriodEndTime = 0f;
    private bool setupGraceStarted = false;
    private float huntGraceRemaining = 0f;

    [Header("Automatic Hunt Loop & Thresholds")]
    [SerializeField] private float huntPollInterval = 6f;
    [SerializeField] private float postHuntCooldown = 25f;
    [SerializeField] private float movementCheckRadius = 8f;
    [SerializeField] private float kumakatokDoorWindow = 15f;
    [SerializeField] private float tagamalingLunarCycleSeconds = 50f;
    private float nextHuntAttemptTime = 0f;
    private ulong kaperosaTargetClientId = ulong.MaxValue;
    private PlayerController kaperosaObsessionPlayer;
    private bool hasFailedRitualOccurred = false;
    private static float lastInteriorDoorInteractionTime = -999f;
    private static RoofFader[] cachedEnclosedRoofs;

    public static void NotifyInteriorDoorInteracted(Vector3 doorPos)
    {
        lastInteriorDoorInteractionTime = Time.time;
    }

    public void NotifyRitualFailed()
    {
        hasFailedRitualOccurred = true;
    }
    
    // Visibility State
    private float ghostEventTimer = 0f;
    private float flickerTimer = 0f;
    private bool isHunterFlickerVisible = false;
    private bool devVisibilityForced = false;

    // Hunt environment effects
    private float huntLightFlickerTimer = 0f;
    private float huntFuseTripTimer = 0f;

    public override void OnNetworkDespawn()
    {
        UnsubscribeSyncCallbacks();
        DespawnActiveOrb();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        UnsubscribeSyncCallbacks();
        DespawnActiveOrb();
        base.OnDestroy();
    }

    private void DespawnActiveOrb()
    {
        if (activeGhostOrb == null) return;

        var orbNet = activeGhostOrb.GetComponent<NetworkObject>();
        if (orbNet != null && orbNet.IsSpawned)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                orbNet.Despawn(true);
        }
        else if (orbNet == null || !orbNet.IsSpawned)
        {
            Destroy(activeGhostOrb.gameObject);
        }
        activeGhostOrb = null;
    }

    private static FixedString128Bytes ToFixedString128(string value)
    {
        if (string.IsNullOrEmpty(value)) return default;

        string trimmed = value;
        while (trimmed.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(trimmed) > 120)
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        }

        return new FixedString128Bytes(trimmed);
    }

    private static int EncodeEvidenceMask(System.Collections.Generic.List<EvidenceType> evidence)
    {
        if (evidence == null) return 0;
        int mask = 0;
        for (int i = 0; i < evidence.Count; i++)
        {
            mask |= 1 << (int)evidence[i];
        }
        return mask;
    }

    private static void DecodeEvidenceMask(int mask, System.Collections.Generic.List<EvidenceType> target)
    {
        target.Clear();
        foreach (EvidenceType ev in System.Enum.GetValues(typeof(EvidenceType)))
        {
            if ((mask & (1 << (int)ev)) != 0)
            {
                target.Add(ev);
            }
        }
    }

    private static Collider2D ResolveRoomMarkerCollider(string roomName, Vector2 center)
    {
        if (string.IsNullOrEmpty(roomName)) return null;

        GhostRoomMarker[] markers = FindObjectsByType<GhostRoomMarker>(FindObjectsInactive.Include);
        Collider2D bestMatch = null;
        float bestDistSq = float.MaxValue;

        for (int i = 0; i < markers.Length; i++)
        {
            var marker = markers[i];
            if (marker == null || marker.RoomCollider == null) continue;
            if (marker.gameObject.name != roomName) continue;

            float distSq = ((Vector2)marker.RoomCollider.bounds.center - center).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestMatch = marker.RoomCollider;
            }
        }

        if (bestMatch != null) return bestMatch;

        // Fallback if ghostroomMarker was assigned to a Collider2D without GhostRoomMarker
        Collider2D[] allColliders = FindObjectsByType<Collider2D>(FindObjectsInactive.Include);
        for (int i = 0; i < allColliders.Length; i++)
        {
            var col = allColliders[i];
            if (col == null || col.gameObject.name != roomName) continue;

            float distSq = ((Vector2)col.bounds.center - center).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestMatch = col;
            }
        }

        return bestMatch;
    }

    private void PushGhostInfoToParanormalManager()
    {
        if (ParanormalManager.Instance == null || ghostroomMarker == null) return;

        ParanormalManager.Instance.SetGhostInfo(
            eventGhost != null ? eventGhost.transform : transform,
            ghostroomMarker,
            HasEvidence(EvidenceType.Freezing),
            HasEvidence(EvidenceType.EMF),
            this
        );
    }

    private void SyncStateToNetworkVariables()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !IsServer || !IsSpawned)
            return;

        syncedGhostName.Value = ToFixedString128(string.IsNullOrEmpty(ghostName) ? "Unknown" : ghostName);
        syncedEvidenceMask.Value = EncodeEvidenceMask(currentEvidence);
        syncedRoomMarkerName.Value = ghostroomMarker != null ? ToFixedString128(ghostroomMarker.gameObject.name) : default;
        syncedRoomCenter.Value = ghostroomMarker != null ? (Vector2)ghostroomMarker.bounds.center : Vector2.zero;
        syncedExorcised.Value = isExorcised;
        syncedGhostActivated.Value = hasActivated && ghostroomMarker != null;
    }

    private void OnSyncedBoolChanged(bool _, bool __) => ApplySyncedGhostState();
    private void OnSyncedIntChanged(int _, int __) => ApplySyncedGhostState();
    private void OnSyncedFixedStringChanged(FixedString128Bytes _, FixedString128Bytes __) => ApplySyncedGhostState();
    private void OnSyncedVector2Changed(Vector2 _, Vector2 __) => ApplySyncedGhostState();

    private void SubscribeSyncCallbacks()
    {
        if (hasSubscribedSyncCallbacks) return;
        hasSubscribedSyncCallbacks = true;

        syncedGhostActivated.OnValueChanged += OnSyncedBoolChanged;
        syncedExorcised.OnValueChanged += OnSyncedBoolChanged;
        syncedEvidenceMask.OnValueChanged += OnSyncedIntChanged;
        syncedGhostName.OnValueChanged += OnSyncedFixedStringChanged;
        syncedRoomMarkerName.OnValueChanged += OnSyncedFixedStringChanged;
        syncedRoomCenter.OnValueChanged += OnSyncedVector2Changed;
    }

    private void UnsubscribeSyncCallbacks()
    {
        if (!hasSubscribedSyncCallbacks) return;
        hasSubscribedSyncCallbacks = false;

        syncedGhostActivated.OnValueChanged -= OnSyncedBoolChanged;
        syncedExorcised.OnValueChanged -= OnSyncedBoolChanged;
        syncedEvidenceMask.OnValueChanged -= OnSyncedIntChanged;
        syncedGhostName.OnValueChanged -= OnSyncedFixedStringChanged;
        syncedRoomMarkerName.OnValueChanged -= OnSyncedFixedStringChanged;
        syncedRoomCenter.OnValueChanged -= OnSyncedVector2Changed;
    }

    private void ApplySyncedGhostState()
    {
        if (IsServer) return;

        isExorcised = syncedExorcised.Value;
        hasActivated = syncedGhostActivated.Value;

        string syncedName = syncedGhostName.Value.ToString();
        if (!string.IsNullOrEmpty(syncedName))
        {
            ghostName = syncedName;
            if (GameMatchManager.Instance != null && !ghostName.Equals("Unknown", System.StringComparison.OrdinalIgnoreCase))
            {
                GameMatchManager.Instance.SetTrueGhostName(ghostName);
            }
        }

        int mask = syncedEvidenceMask.Value;
        if (mask != 0)
        {
            DecodeEvidenceMask(mask, currentEvidence);
        }
        else if (syncedGhostActivated.Value)
        {
            currentEvidence.Clear();
        }

        if (syncedGhostActivated.Value)
        {
            string targetRoomName = syncedRoomMarkerName.Value.ToString();
            if (ghostroomMarker == null || (!string.IsNullOrEmpty(targetRoomName) && ghostroomMarker.gameObject.name != targetRoomName))
            {
                ghostroomMarker = ResolveRoomMarkerCollider(targetRoomName, syncedRoomCenter.Value);
            }

            if (ghostroomMarker != null)
            {
                PushGhostInfoToParanormalManager();

                GhostOrbSystem[] orbs = FindObjectsByType<GhostOrbSystem>(FindObjectsInactive.Include);
                for (int i = 0; i < orbs.Length; i++)
                {
                    if (orbs[i] != null)
                    {
                        orbs[i].BindRoomCollider(ghostroomMarker);
                    }
                }
            }
        }

        if (IsExorcised && eventGhost != null)
        {
            eventGhost.CleanupTemporaryOrb();
        }
    }

    public override void OnNetworkSpawn()
    {
        SubscribeSyncCallbacks();

        if (IsServer)
        {
            SetState(GhostHandlerState.Dormant);
            isExorcismFailedHunt.Value = false;
            ContractDifficulty diff = ProgressionAndLoadoutManager.Instance != null
                ? ProgressionAndLoadoutManager.Instance.SelectedDifficulty
                : ContractDifficulty.Amateur;
            ResetSetupGracePeriod(ProgressionAndLoadoutManager.GetDifficultySetupTimeSeconds(diff));
            SyncStateToNetworkVariables();
        }
        else
        {
            ApplySyncedGhostState();
        }
        
        // Force sorting order to 10 so they NEVER render behind the floor
        if (eventGhost != null)
        {
            var sr = eventGhost.GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = 10;
        }
        if (hunterGhost != null)
        {
            var sr = hunterGhost.GetComponentInChildren<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = 10;
        }
        
        currentState.OnValueChanged += (oldState, newState) => 
        {
            UpdateVisibility(newState);
            if (newState != GhostHandlerState.Dormant)
            {
                bool localDead = PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive;
                if (oldState == GhostHandlerState.Dormant && !localDead)
                {
                    GameAudioManager.PlayGhostHuntStart(transform.position);
                }
                if (!localDead)
                {
                    TutorialTooltipManager.TriggerHint(TutorialHintId.GhostHunt);
                }
            }
        };
        isExorcismFailedHunt.OnValueChanged += (oldVal, newVal) =>
        {
            if (ExorcismManager.Instance != null)
            {
                ExorcismManager.Instance.SetRedEnvironment(newVal);
            }
        };

        UpdateVisibility(currentState.Value);
        if (ExorcismManager.Instance != null)
        {
            ExorcismManager.Instance.SetRedEnvironment(isExorcismFailedHunt.Value);
        }
    }

    public void ActivateGhost()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer) return;

        if (hasActivated)
        {
            Debug.LogWarning("GhostHandler: ActivateGhost already ran; ignoring duplicate activation.");
            return;
        }
        hasActivated = true;

        Debug.Log("GhostHandler: ActivateGhost triggered!");

        // Auto-assign and randomly select a Ghost Room if one isn't manually set
        if (ghostroomMarker == null)
        {
            GhostRoomMarker[] allRooms = FindObjectsByType<GhostRoomMarker>(FindObjectsInactive.Exclude);
            var validRooms = new System.Collections.Generic.List<GhostRoomMarker>(allRooms.Length);
            for (int i = 0; i < allRooms.Length; i++)
            {
                if (allRooms[i] != null && allRooms[i].RoomCollider != null)
                {
                    validRooms.Add(allRooms[i]);
                }
            }

            Debug.Log($"GhostHandler: Found {allRooms.Length} active GhostRoomMarkers ({validRooms.Count} with Collider2D) in the scene.");
            if (validRooms.Count > 0)
            {
                GhostRoomMarker selectedRoom = validRooms[Random.Range(0, validRooms.Count)];
                ghostroomMarker = selectedRoom.RoomCollider;
                Debug.Log($"GhostHandler: Randomly selected room '{selectedRoom.gameObject.name}' at {ghostroomMarker.bounds.center}");
            }
        }
        else
        {
            Debug.Log($"GhostHandler: Room was already manually assigned to {ghostroomMarker.gameObject.name}");
        }

        if (ghostroomMarker == null)
        {
            Debug.LogError("GhostHandler: ActivateGhost aborted because no valid GhostRoomMarker with a Collider2D was found!");
            DespawnActiveOrb();
            TruckGearSpawner.CleanupAllGhostOrbs();
            hasActivated = false;
            SyncStateToNetworkVariables();
            return;
        }

        InitializeGhostIdentity();

        // Snap the parent and the ghosts to the selected room so they spawn there
        transform.position = ghostroomMarker.bounds.center;
        if (eventGhost != null)
            eventGhost.transform.position = ghostroomMarker.bounds.center;

        if (hunterGhost != null)
            hunterGhost.transform.position = ghostroomMarker.bounds.center;

        if (eventGhost != null)
            eventGhost.InitializeOrigin(eventGhost.transform.position);

        // Tell ParanormalManager about our presence and native evidence
        PushGhostInfoToParanormalManager();

        // Spawn Ghost Orbs natively if applicable, otherwise clean up any stray orb
        if (HasEvidence(EvidenceType.GhostOrbs) && ghostOrbPrefab != null)
        {
            if (activeGhostOrb == null)
            {
                // Reuse any live orb already in the scene instead of spawning a duplicate
                activeGhostOrb = FindAnyObjectByType<GhostOrbSystem>();
            }

            if (activeGhostOrb != null)
            {
                activeGhostOrb.Initialize(ghostroomMarker);
            }
            else
            {
                Vector3 spawnPos = ghostroomMarker.bounds.center;
                if (!ghostroomMarker.OverlapPoint(spawnPos))
                {
                    spawnPos = ghostroomMarker.ClosestPoint(spawnPos);
                }

                GameObject orb = Instantiate(ghostOrbPrefab, spawnPos, Quaternion.identity);
                var orbNetwork = orb.GetComponent<NetworkObject>();
                if (orbNetwork != null) orbNetwork.Spawn();

                var orbSystem = orb.GetComponent<GhostOrbSystem>();
                if (orbSystem != null)
                {
                    orbSystem.Initialize(ghostroomMarker);
                    activeGhostOrb = orbSystem;
                }
            }
        }
        else
        {
            DespawnActiveOrb();
            if (eventGhost != null) eventGhost.CleanupTemporaryOrb();
            TruckGearSpawner.CleanupAllGhostOrbs();
        }

        nextHuntAttemptTime = Time.time + huntPollInterval;
        SyncStateToNetworkVariables();
    }

    private void UpdateVisibility(GhostHandlerState state)
    {
        if (eventGhost != null)
        {
            var eventRenderer = eventGhost.GetComponentInChildren<SpriteRenderer>();
            var eventCol = eventGhost.GetComponent<Collider2D>();
            bool eventVisible = (state == GhostHandlerState.Dormant) && (ghostEventTimer > 0f || devVisibilityForced);
            
            if (eventRenderer != null) eventRenderer.enabled = eventVisible;
            if (eventCol != null) eventCol.enabled = true; // Always collide with walls/doors to prevent wandering through them
        }
            
        if (hunterGhost != null)
        {
            var hunterRenderer = hunterGhost.GetComponentInChildren<SpriteRenderer>();
            var hunterCol = hunterGhost.GetComponent<Collider2D>();
            
            if (state == GhostHandlerState.Dormant)
            {
                if (hunterRenderer != null) hunterRenderer.enabled = devVisibilityForced;
                if (hunterCol != null) hunterCol.enabled = false;
            }
            else
            {
                if (hunterRenderer != null) hunterRenderer.enabled = isHunterFlickerVisible || devVisibilityForced;
                if (hunterCol != null) hunterCol.enabled = true;
            }
        }
    }

    public void TriggerGhostEvent()
    {
        if (isExorcised) return;
        if (!IsServer || currentState.Value != GhostHandlerState.Dormant) return;
        
        // Show the event ghost for 3 seconds
        ghostEventTimer = 3f;
        UpdateVisibility(currentState.Value);
        GameAudioManager.PlayGhostEvent(eventGhost != null ? eventGhost.transform.position : transform.position);

        // Notify atmosphere controller on ALL clients via ClientRpc
        TriggerGhostEventEffectClientRpc();

        if (CemeteryCandleManager.Instance != null)
        {
            Vector3 candleCheckPos = eventGhost != null ? eventGhost.transform.position : transform.position;
            CemeteryCandleManager.Instance.BlowOutNearestCandle(candleCheckPos, 2.5f);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void TriggerGhostEventEffectClientRpc()
    {
        if (GhostAtmosphereController.Instance != null)
            GhostAtmosphereController.Instance.TriggerGhostEventEffect();
    }

    public void ToggleDevVisibility()
    {
        devVisibilityForced = !devVisibilityForced;
        UpdateVisibility(currentState.Value);
    }

    public void RequestStateChange(GhostHandlerState newState)
    {
        if (!IsServer) return;
        // Allows children to request state changes (e.g. Searching -> Chasing)
        if (currentState.Value != GhostHandlerState.Dormant)
        {
            SetState(newState);
        }
    }

    private void SetState(GhostHandlerState newState)
    {
        if (!IsServer) return;
        currentState.Value = newState;
    }

    public void OnGhostExorcised()
    {
        isExorcised = true;
        eventGhost?.CancelActiveDotsProjectionOnServer();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer && IsSpawned)
        {
            syncedExorcised.Value = true;
        }

        EndHunt();
        DespawnActiveOrb();
        if (eventGhost != null)
        {
            eventGhost.CleanupTemporaryOrb();
        }
        TruckGearSpawner.CleanupAllGhostOrbs();

        if (eventGhost != null)
        {
            var eventSr = eventGhost.GetComponentInChildren<SpriteRenderer>();
            if (eventSr != null) eventSr.enabled = false;
            eventGhost.gameObject.SetActive(false);
        }

        if (hunterGhost != null)
        {
            var hunterSr = hunterGhost.GetComponentInChildren<SpriteRenderer>();
            if (hunterSr != null) hunterSr.enabled = false;
            hunterGhost.gameObject.SetActive(false);
        }

        SetDoorsLocked(false);
        SetState(GhostHandlerState.Dormant);

        if (GameMatchManager.Instance != null)
        {
            GameMatchManager.Instance.MarkGhostDefeated();
            if (!string.IsNullOrEmpty(ghostName) && !ghostName.Equals("Unknown", System.StringComparison.OrdinalIgnoreCase))
            {
                GameMatchManager.Instance.SetTrueGhostName(ghostName);
            }
        }
    }

    public void ResetSetupGracePeriod(float seconds)
    {
        if (!IsServer) return;
        float clamped = Mathf.Max(0f, seconds);
        setupGraceDuration = clamped;
        bool shouldStartNow = clamped <= 0f || !NetworkDoor.HasAnyFrontDoor() || NetworkDoor.HasAnyFrontDoorOpened();
        setupGraceStarted = shouldStartNow;
        setupGracePeriodEndTime = shouldStartNow ? Time.time + clamped : 0f;
        syncedSetupGraceRemaining.Value = clamped;
    }

    public static bool IsPlayerInInvestigationArea(PlayerController p)
    {
        if (p == null || !p.IsAlive) return false;
        Vector3 pos = p.transform.position;
        if (GameAudioManager.IsBasementPosition(pos)) return true;

        if (InvestigationZone.AllZones.Count > 0 && InvestigationZone.IsPositionInAnyZone(pos))
        {
            return true;
        }

        if (cachedEnclosedRoofs == null || cachedEnclosedRoofs.Length == 0 || cachedEnclosedRoofs[0] == null)
        {
            var all = FindObjectsByType<RoofFader>(FindObjectsInactive.Include);
            var enclosed = new System.Collections.Generic.List<RoofFader>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].IsEnclosedBuilding) enclosed.Add(all[i]);
            }
            cachedEnclosedRoofs = enclosed.ToArray();
        }

        for (int i = 0; i < cachedEnclosedRoofs.Length; i++)
        {
            var roof = cachedEnclosedRoofs[i];
            if (roof != null && roof.IsPositionInside(pos)) return true;
        }
        return false;
    }

    public static int CountLivingPlayersInInvestigationArea()
    {
        int count = 0;
        for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
        {
            if (IsPlayerInInvestigationArea(PlayerController.AllPlayers[i]))
            {
                count++;
            }
        }
        return count;
    }

    private PlayerController ResolveKaperosaObsessionTarget()
    {
        if (kaperosaObsessionPlayer != null &&
            kaperosaObsessionPlayer.IsAlive &&
            PlayerController.AllPlayers.Contains(kaperosaObsessionPlayer))
        {
            return kaperosaObsessionPlayer;
        }

        if (kaperosaTargetClientId != ulong.MaxValue &&
            PlayerController.TryGetByClientId(kaperosaTargetClientId, out var existing) &&
            existing != null &&
            existing.IsAlive)
        {
            kaperosaObsessionPlayer = existing;
            return existing;
        }

        var living = new System.Collections.Generic.List<PlayerController>();
        for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
        {
            var p = PlayerController.AllPlayers[i];
            if (p != null && p.IsAlive)
            {
                living.Add(p);
            }
        }

        if (living.Count == 0)
        {
            kaperosaObsessionPlayer = null;
            kaperosaTargetClientId = ulong.MaxValue;
            return null;
        }

        kaperosaObsessionPlayer = living[Random.Range(0, living.Count)];
        kaperosaTargetClientId = kaperosaObsessionPlayer.OwnerClientId;
        return kaperosaObsessionPlayer;
    }

    public float GetEffectiveHuntSanity()
    {
        if (GhostCatalog.TryGetGhost(ghostName, out var def) &&
            def.HuntProfile.Condition == GhostHuntConditionType.SingleObsessionTarget)
        {
            var obsession = ResolveKaperosaObsessionTarget();
            return obsession != null ? obsession.Sanity : 100f;
        }

        float total = 0f;
        int livingCount = 0;
        for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
        {
            var p = PlayerController.AllPlayers[i];
            if (p != null && p.IsAlive)
            {
                total += p.Sanity;
                livingCount++;
            }
        }

        return livingCount > 0 ? (total / livingCount) : 100f;
    }

    private bool IsPositionInsideGhostRoom(Vector2 pos)
    {
        if (ghostroomMarker == null) return false;
        if (ghostroomMarker.OverlapPoint(pos)) return true;
        Bounds b = ghostroomMarker.bounds;
        return pos.x >= b.min.x && pos.x <= b.max.x && pos.y >= b.min.y && pos.y <= b.max.y;
    }

    private bool IsGhostRoomLit()
    {
        if (ghostroomMarker == null) return false;
        Bounds b = ghostroomMarker.bounds;
        Vector2 center = b.center;

        foreach (var sz in SafeZone.AllActiveZones)
        {
            if (sz == null || !sz.isActiveAndEnabled) continue;
            string lower = sz.gameObject.name.ToLowerInvariant();
            if (lower.Contains("van") || lower.Contains("jeepney")) continue;
            if (sz.OverlapsPosition(center)) return true;
        }

        var lights = FindObjectsByType<UnityEngine.Rendering.Universal.Light2D>(FindObjectsInactive.Exclude);
        for (int i = 0; i < lights.Length; i++)
        {
            var l = lights[i];
            if (l == null || !l.enabled || l.intensity <= 0.1f) continue;
            if (l.lightType == UnityEngine.Rendering.Universal.Light2D.LightType.Global) continue;

            string lname = l.gameObject.name.ToLowerInvariant();
            if (lname.Contains("playerpov") || lname.Contains("switchindicator") ||
                lname.Contains("ghostauralight") || lname.Contains("candle"))
            {
                continue;
            }
            if (l.GetComponentInParent<PlayerController>() != null || l.GetComponentInParent<EquipmentItem>() != null)
            {
                continue;
            }

            if (IsPositionInsideGhostRoom(l.transform.position))
            {
                return true;
            }
        }

        return false;
    }

    public float EvaluateCurrentHuntThreshold(out bool forceAbilityHunt)
    {
        forceAbilityHunt = false;
        if (!GhostCatalog.TryGetGhost(ghostName, out var def))
        {
            return 50f;
        }

        GhostHuntProfile profile = def.HuntProfile;
        Vector3 ghostPos = eventGhost != null ? eventGhost.transform.position : transform.position;

        switch (profile.Condition)
        {
            case GhostHuntConditionType.PlayerMovingNearby:
            {
                for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
                {
                    var p = PlayerController.AllPlayers[i];
                    if (p == null || !p.IsAlive) continue;
                    if ((p.IsMoving || p.IsSprinting) &&
                        (Vector2.Distance(ghostPos, p.transform.position) <= movementCheckRadius || IsPositionInsideGhostRoom(p.transform.position)))
                    {
                        return profile.AlternateThreshold;
                    }
                }
                return profile.BaseThreshold;
            }

            case GhostHuntConditionType.PlayerSprintingNearby:
            {
                for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
                {
                    var p = PlayerController.AllPlayers[i];
                    if (p == null || !p.IsAlive) continue;
                    if (p.IsSprinting &&
                        (Vector2.Distance(ghostPos, p.transform.position) <= movementCheckRadius || IsPositionInsideGhostRoom(p.transform.position)))
                    {
                        return profile.AlternateThreshold;
                    }
                }
                return profile.BaseThreshold;
            }

            case GhostHuntConditionType.PlayerInGhostRoom:
            {
                for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
                {
                    var p = PlayerController.AllPlayers[i];
                    if (p != null && p.IsAlive && IsPositionInsideGhostRoom(p.transform.position))
                    {
                        return profile.AlternateThreshold;
                    }
                }
                return profile.BaseThreshold;
            }

            case GhostHuntConditionType.EarlyHuntAbility:
            {
                if (!IsInSetupGracePeriod && profile.AbilityChance > 0f && Random.value < profile.AbilityChance)
                {
                    forceAbilityHunt = true;
                }
                return profile.BaseThreshold;
            }

            case GhostHuntConditionType.GroupedInGhostRoom:
            {
                int inRoomCount = 0;
                for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
                {
                    var p = PlayerController.AllPlayers[i];
                    if (p != null && p.IsAlive && IsPositionInsideGhostRoom(p.transform.position))
                    {
                        inRoomCount++;
                    }
                }
                return inRoomCount >= 2 ? profile.AlternateThreshold : profile.BaseThreshold;
            }

            case GhostHuntConditionType.RecentInteriorDoor:
                return (Time.time - lastInteriorDoorInteractionTime) <= kumakatokDoorWindow
                    ? profile.AlternateThreshold
                    : profile.BaseThreshold;

            case GhostHuntConditionType.SoloInHouse:
                return CountLivingPlayersInInvestigationArea() == 1
                    ? profile.AlternateThreshold
                    : profile.BaseThreshold;

            case GhostHuntConditionType.SummoningCirclePlaced:
            {
                if (SummoningCircle.FindClosest(Vector3.zero, 10000f) != null)
                {
                    return profile.AlternateThreshold;
                }
                var items = EquipmentItem.AllEquipment;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] is RitualChalkItem chalk && chalk.IsPlaced)
                    {
                        return profile.AlternateThreshold;
                    }
                }
                return profile.BaseThreshold;
            }

            case GhostHuntConditionType.GhostRoomLightsOn:
                return IsGhostRoomLit() ? profile.AlternateThreshold : profile.BaseThreshold;

            case GhostHuntConditionType.FailedRitual:
                return hasFailedRitualOccurred ? profile.AlternateThreshold : profile.BaseThreshold;

            case GhostHuntConditionType.LunarPhaseCycle:
            {
                float cycleSec = Mathf.Max(10f, tagamalingLunarCycleSeconds);
                int phase = Mathf.FloorToInt(Time.time / cycleSec) % 2;
                return phase == 1 ? profile.AlternateThreshold : profile.BaseThreshold;
            }

            case GhostHuntConditionType.SingleObsessionTarget:
            case GhostHuntConditionType.Always:
            default:
                return profile.BaseThreshold;
        }
    }

    private void TriggerFavoriteRoomState()
    {
        if (!IsServer || ghostroomMarker == null) return;

        if (eventGhost != null)
        {
            Vector3 curPos = eventGhost.transform.position;
            if (!IsPositionInsideGhostRoom(curPos))
            {
                Vector3 anchor = ghostroomMarker.bounds.center;
                if (!ghostroomMarker.OverlapPoint(anchor))
                {
                    anchor = ghostroomMarker.ClosestPoint(anchor);
                }
                anchor.z = curPos.z;
                eventGhost.InitializeOrigin(anchor);
            }
        }
    }

    public void StartHunt(bool isFromFailedExorcism = false)
    {
        if (ProgressionAndLoadoutManager.IsInWarehouseTutorialScene) return;
        if (IsExorcised) return;
        if (!IsServer || (!isFromFailedExorcism && currentState.Value != GhostHandlerState.Dormant)) return;
        if (!isFromFailedExorcism && IsInSetupGracePeriod)
        {
            Debug.Log($"[GhostHandler] Hunt blocked during setup grace period ({SetupGracePeriodRemaining:F1}s remaining).");
            return;
        }

        if (!isFromFailedExorcism && CountLivingPlayersInInvestigationArea() <= 0)
        {
            TriggerFavoriteRoomState();
            return;
        }

        eventGhost?.CancelActiveDotsProjectionOnServer();

        Vector3 spawnPos = transform.position;
        if (!isFromFailedExorcism && ghostroomMarker != null)
        {
            Bounds bounds = ghostroomMarker.bounds;
            spawnPos = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y),
                hunterGhost != null ? hunterGhost.transform.position.z : 0f
            );
        }

        if (!isFromFailedExorcism)
        {
            Vector3 ghostCurrentPos = eventGhost != null ? eventGhost.transform.position : transform.position;
            var crucifixes = FindObjectsByType<CrucifixItem>(FindObjectsInactive.Exclude);
            foreach (var crucifix in crucifixes)
            {
                if (crucifix == null || crucifix.IsBurned || !crucifix.IsPlaced) continue;

                Vector2 cPos = crucifix.transform.position;
                bool inGhostRoom = ghostroomMarker != null &&
                    (IsPositionInsideGhostRoom(cPos) || Vector2.Distance(ghostroomMarker.bounds.center, cPos) <= crucifix.blockRadius);
                bool nearGhostOrSpawn =
                    Vector2.Distance(ghostCurrentPos, cPos) <= crucifix.blockRadius ||
                    Vector2.Distance(spawnPos, cPos) <= crucifix.blockRadius;

                if (inGhostRoom || nearGhostOrSpawn)
                {
                    if (crucifix.TryBlockHunt())
                    {
                        nextHuntAttemptTime = Time.time + postHuntCooldown;
                        Debug.Log($"Hunt blocked by Crucifix! Enforcing {postHuntCooldown:F0}s cooldown.");
                        return;
                    }
                }
            }

            if (ghostroomMarker != null && hunterGhost != null)
            {
                hunterGhost.transform.position = spawnPos;
            }
        }

        isExorcismFailedHunt.Value = isFromFailedExorcism;

        currentHuntDuration = (isFromFailedExorcism ? (huntDurationMax * 1.5f) : Random.Range(huntDurationMin, huntDurationMax))
            * WeatherManager.GetHuntDurationMultiplier();
        huntTimer = 0f;
        huntLightFlickerTimer = 0f;
        huntFuseTripTimer = 0f;

        ContractDifficulty diff = ProgressionAndLoadoutManager.Instance != null
            ? ProgressionAndLoadoutManager.Instance.SelectedDifficulty
            : ContractDifficulty.Amateur;
        if (isFromFailedExorcism)
        {
            float failedPause = ProgressionAndLoadoutManager.GetFailedExorcismPauseSeconds(diff);
            Vector3 ghostCheckPos = hunterGhost != null ? hunterGhost.transform.position : transform.position;
            if (HasActiveCrucifixInVicinity(ghostCheckPos))
            {
                failedPause += 4f;
            }
            huntGraceRemaining = failedPause;
        }
        else
        {
            huntGraceRemaining = ProgressionAndLoadoutManager.GetDifficultyHuntGracePeriodSeconds(diff);
        }
        syncedHuntGraceRemaining.Value = huntGraceRemaining;
        
        if (hunterGhost != null)
        {
            hunterGhost.enabled = true;
            hunterGhost.OnHuntStarted();
            if (isFromFailedExorcism)
            {
                hunterGhost.EnterAngeredState();
            }
        }
        
        SetDoorsLocked(true);
        SetState(GhostHandlerState.Searching);

        if (isFromFailedExorcism)
        {
            TriggerRedEnvironmentClientRpc(true);
        }
    }

    private static bool HasActiveCrucifixInVicinity(Vector3 ghostPos)
    {
        var crucifixes = FindObjectsByType<CrucifixItem>(FindObjectsInactive.Exclude);
        for (int i = 0; i < crucifixes.Length; i++)
        {
            var crucifix = crucifixes[i];
            if (crucifix == null || crucifix.IsBurned || crucifix.IsHeldByDeadPlayer()) continue;
            if (Vector2.Distance(crucifix.transform.position, ghostPos) <= crucifix.blockRadius)
            {
                return true;
            }
        }
        return false;
    }

    public void EndHunt()
    {
        if (!IsServer) return;
        if (currentState.Value == GhostHandlerState.Dormant && !isExorcismFailedHunt.Value) return;

        if (isExorcismFailedHunt.Value)
        {
            isExorcismFailedHunt.Value = false;
            TriggerRedEnvironmentClientRpc(false);
        }

        huntGraceRemaining = 0f;
        syncedHuntGraceRemaining.Value = 0f;

        if (hunterGhost != null)
        {
            hunterGhost.OnHuntEnded();
        }

        // Reset ghost to the center of the ghost room when hunt ends so it doesn't stay in the hallway
        if (ghostroomMarker != null && hunterGhost != null)
        {
            Vector3 targetPos = ghostroomMarker.bounds.center;
            if (UnityEngine.AI.NavMesh.SamplePosition(targetPos, out UnityEngine.AI.NavMeshHit hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
            {
                targetPos = hit.position;
            }

            var agent = hunterGhost.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                if (agent.isOnNavMesh) agent.Warp(targetPos);
                agent.enabled = false;
            }
            hunterGhost.transform.position = targetPos;
        }

        nextHuntAttemptTime = Mathf.Max(nextHuntAttemptTime, Time.time + postHuntCooldown);
        SetDoorsLocked(false);
        SetState(GhostHandlerState.Dormant);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void TriggerRedEnvironmentClientRpc(bool active)
    {
        if (ExorcismManager.Instance != null)
        {
            ExorcismManager.Instance.SetRedEnvironment(active);
        }
    }

    public void SetDoorsLocked(bool locked)
    {
        var doors = Object.FindObjectsByType<NetworkDoor>(FindObjectsInactive.Exclude);
        foreach (var door in doors)
        {
            if (door.IsFrontDoor)
            {
                door.SetLocked(locked);
            }
        }
    }

    private void Update()
    {
        if (IsExorcised) return;

        if (IsServer)
        {
            if (!setupGraceStarted && setupGraceDuration > 0f && (!NetworkDoor.HasAnyFrontDoor() || NetworkDoor.HasAnyFrontDoorOpened()))
            {
                setupGraceStarted = true;
                setupGracePeriodEndTime = Time.time + setupGraceDuration;
            }

            float setupRem = SetupGracePeriodRemaining;
            if (Mathf.Abs(syncedSetupGraceRemaining.Value - setupRem) > 0.05f || (setupRem <= 0f && syncedSetupGraceRemaining.Value > 0f))
            {
                syncedSetupGraceRemaining.Value = setupRem;
            }
        }

        // Visual logic should run on BOTH Server and Client!
        if (currentState.Value == GhostHandlerState.Dormant)
        {
            if (IsServer && hasActivated && !IsInSetupGracePeriod && Time.time >= nextHuntAttemptTime)
            {
                nextHuntAttemptTime = Time.time + Mathf.Max(1f, huntPollInterval);
                if (CountLivingPlayersInInvestigationArea() <= 0)
                {
                    TriggerFavoriteRoomState();
                }
                else
                {
                    float threshold = EvaluateCurrentHuntThreshold(out bool forceAbilityHunt);
                    float effectiveSanity = GetEffectiveHuntSanity();
                    if (forceAbilityHunt || effectiveSanity <= threshold)
                    {
                        StartHunt(false);
                        if (currentState.Value != GhostHandlerState.Dormant) return;
                    }
                }
            }

            if (ghostEventTimer > 0f)
            {
                ghostEventTimer -= Time.deltaTime;
                if (ghostEventTimer <= 0f)
                {
                    // Event over, hide the sprite
                    UpdateVisibility(currentState.Value);
                }
            }
        }
        else
        {
            if (IsServer)
            {
                if (huntGraceRemaining > 0f)
                {
                    huntGraceRemaining = Mathf.Max(0f, huntGraceRemaining - Time.deltaTime);
                    syncedHuntGraceRemaining.Value = huntGraceRemaining;
                }
                else
                {
                    huntTimer += Time.deltaTime;
                    if (huntTimer >= currentHuntDuration)
                    {
                        EndHunt();
                        return;
                    }
                }

                // Flicker all active lights every 2 seconds during a hunt
                huntLightFlickerTimer += Time.deltaTime;
                if (huntLightFlickerTimer >= 2f)
                {
                    huntLightFlickerTimer = 0f;
                    var switches = Object.FindObjectsByType<HouseLightSwitch>(FindObjectsInactive.Exclude);
                    foreach (var sw in switches)
                    {
                        sw.HuntFlicker();
                    }
                }

                // 15% chance to trip the fuse box, checked every 3 seconds
                huntFuseTripTimer += Time.deltaTime;
                if (huntFuseTripTimer >= 3f)
                {
                    huntFuseTripTimer = 0f;
                    if (FuseBox.Instance != null && FuseBox.Instance.IsEnabled && Random.value < 0.15f)
                    {
                        FuseBox.Instance.GhostTrip();
                    }
                }
            }
            
            // Flickering logic during hunt (runs for everyone)
            flickerTimer -= Time.deltaTime;
            if (flickerTimer <= 0f)
            {
                isHunterFlickerVisible = !isHunterFlickerVisible;
                flickerTimer = Random.Range(0.1f, 0.4f);
                UpdateVisibility(currentState.Value);
            }
        }
    }

    /// <summary>
    /// Centralized evidence filter.
    /// Under normal conditions (Dormant), all available evidence types are allowed.
    /// During a hunt, only EMF and Crucifix are allowed.
    /// </summary>
    public bool IsEvidenceAllowed(EvidenceType type)
    {
        // Hunt active
        if (currentState.Value != GhostHandlerState.Dormant)
        {
            return type == EvidenceType.EMF || type == EvidenceType.Crucifix;
        }
        
        // Normal state: all allowed
        return true;
    }
}
