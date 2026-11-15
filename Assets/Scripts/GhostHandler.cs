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

    private bool hasSubscribedSyncCallbacks = false;

    public GhostHandlerState CurrentState => currentState.Value;
    public bool IsExorcismFailedHunt => isExorcismFailedHunt.Value;

    private float huntTimer = 0f;
    private float currentHuntDuration = 0f;
    
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

    public void StartHunt(bool isFromFailedExorcism = false)
    {
        if (IsExorcised) return;
        if (!IsServer || currentState.Value != GhostHandlerState.Dormant) return;
        eventGhost?.CancelActiveDotsProjectionOnServer();

        isExorcismFailedHunt.Value = isFromFailedExorcism;

        if (!isFromFailedExorcism)
        {
            // Check for nearby crucifixes that can block normal hunts
            var crucifixes = FindObjectsByType<CrucifixItem>(FindObjectsInactive.Exclude);
            foreach (var crucifix in crucifixes)
            {
                if (!crucifix.IsBurned && ghostroomMarker != null)
                {
                    // If crucifix is inside the ghost room bounds, or near the center
                    if (ghostroomMarker.OverlapPoint(crucifix.transform.position) ||
                        Vector2.Distance(ghostroomMarker.bounds.center, crucifix.transform.position) <= crucifix.blockRadius)
                    {
                        if (crucifix.TryBlockHunt())
                        {
                            // Hunt successfully blocked by crucifix
                            Debug.Log("Hunt blocked by Crucifix!");
                            return;
                        }
                    }
                }
            }

            // Teleport hunter to random valid position inside ghostroomMarker
            if (ghostroomMarker != null && hunterGhost != null)
            {
                Bounds bounds = ghostroomMarker.bounds;
                Vector2 randomPoint = new Vector2(
                    Random.Range(bounds.min.x, bounds.max.x),
                    Random.Range(bounds.min.y, bounds.max.y)
                );
                hunterGhost.transform.position = randomPoint;
            }
        }

        currentHuntDuration = (isFromFailedExorcism ? (huntDurationMax * 1.5f) : Random.Range(huntDurationMin, huntDurationMax))
            * WeatherManager.GetHuntDurationMultiplier();
        huntTimer = 0f;
        huntLightFlickerTimer = 0f;
        huntFuseTripTimer = 0f;
        
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

    public void EndHunt()
    {
        if (!IsServer) return;
        if (currentState.Value == GhostHandlerState.Dormant && !isExorcismFailedHunt.Value) return;

        if (isExorcismFailedHunt.Value)
        {
            isExorcismFailedHunt.Value = false;
            TriggerRedEnvironmentClientRpc(false);
        }

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

        // Visual logic should run on BOTH Server and Client!
        if (currentState.Value == GhostHandlerState.Dormant)
        {
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
                huntTimer += Time.deltaTime;
                if (huntTimer >= currentHuntDuration)
                {
                    EndHunt();
                    return;
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
