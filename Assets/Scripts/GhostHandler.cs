using UnityEngine;
using Unity.Netcode;

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

        int rand = Random.Range(0, 3);
        if (rand == 0)
        {
            ghostName = "Tikbalang";
            currentEvidence.Add(EvidenceType.EMF);
            currentEvidence.Add(EvidenceType.SpiritBox);
            currentEvidence.Add(EvidenceType.GhostOrbs);
        }
        else if (rand == 1)
        {
            ghostName = "Kapre";
            currentEvidence.Add(EvidenceType.EMF);
            currentEvidence.Add(EvidenceType.Freezing);
            currentEvidence.Add(EvidenceType.DOTS);
        }
        else
        {
            ghostName = "Manananggal";
            currentEvidence.Add(EvidenceType.Freezing);
            currentEvidence.Add(EvidenceType.GhostWriting);
            currentEvidence.Add(EvidenceType.GhostOrbs);
        }
    }

    private NetworkVariable<GhostHandlerState> currentState = new NetworkVariable<GhostHandlerState>(
        GhostHandlerState.Dormant, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public GhostHandlerState CurrentState => currentState.Value;

    private float huntTimer = 0f;
    private float currentHuntDuration = 0f;
    
    // Visibility State
    private float ghostEventTimer = 0f;
    private float flickerTimer = 0f;
    private bool isHunterFlickerVisible = false;
    private bool devVisibilityForced = false;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            SetState(GhostHandlerState.Dormant);
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
        
        currentState.OnValueChanged += (oldState, newState) => UpdateVisibility(newState);
        UpdateVisibility(currentState.Value);
    }

    public void ActivateGhost()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer) return;

        Debug.Log("GhostHandler: ActivateGhost triggered!");

        InitializeGhostIdentity();

        // Auto-assign and randomly select a Ghost Room if one isn't manually set
        if (ghostroomMarker == null)
        {
            GhostRoomMarker[] allRooms = FindObjectsByType<GhostRoomMarker>(FindObjectsSortMode.None);
            Debug.Log($"GhostHandler: Found {allRooms.Length} GhostRoomMarkers in the scene.");
            if (allRooms.Length > 0)
            {
                GhostRoomMarker selectedRoom = allRooms[Random.Range(0, allRooms.Length)];
                ghostroomMarker = selectedRoom.GetComponent<Collider2D>();
                Debug.Log($"GhostHandler: Randomly selected room at {ghostroomMarker.bounds.center}");
            }
        }
        else
        {
            Debug.Log($"GhostHandler: Room was already manually assigned to {ghostroomMarker.gameObject.name}");
        }

        // Room markers are no longer disabled here, allowing ambient room temps to work.
        
        // Snap the parent and the ghosts to the randomly selected room so they spawn there!
        if (ghostroomMarker != null)
        {
            transform.position = ghostroomMarker.bounds.center;
            if (eventGhost != null)
                eventGhost.transform.position = ghostroomMarker.bounds.center;
            
            if (hunterGhost != null)
                hunterGhost.transform.position = ghostroomMarker.bounds.center;
        }

        if (eventGhost != null)
            eventGhost.InitializeOrigin(eventGhost.transform.position);

        // Tell ParanormalManager about our presence and native evidence
        if (ParanormalManager.Instance != null && ghostroomMarker != null)
        {
            float roomRadius = Mathf.Max(ghostroomMarker.bounds.extents.x, ghostroomMarker.bounds.extents.y);
            ParanormalManager.Instance.SetGhostInfo(
                eventGhost != null ? eventGhost.transform : transform,
                ghostroomMarker,
                HasEvidence(EvidenceType.Freezing),
                HasEvidence(EvidenceType.EMF),
                this
            );
        }

        // Spawn Ghost Orbs natively if applicable
        if (HasEvidence(EvidenceType.GhostOrbs) && ghostOrbPrefab != null && ghostroomMarker != null)
        {
            GameObject orb = Instantiate(ghostOrbPrefab, ghostroomMarker.bounds.center, Quaternion.identity);
            var orbNetwork = orb.GetComponent<NetworkObject>();
            if (orbNetwork != null) orbNetwork.Spawn();
            
            var orbSystem = orb.GetComponent<GhostOrbSystem>();
            if (orbSystem != null) orbSystem.Initialize(ghostroomMarker.bounds);
        }
    }

    private void UpdateVisibility(GhostHandlerState state)
    {
        if (eventGhost != null)
        {
            var eventRenderer = eventGhost.GetComponentInChildren<SpriteRenderer>();
            var eventCol = eventGhost.GetComponent<Collider2D>();
            bool eventVisible = (state == GhostHandlerState.Dormant) && (ghostEventTimer > 0f || devVisibilityForced);
            
            if (eventRenderer != null) eventRenderer.enabled = eventVisible;
            if (eventCol != null) eventCol.enabled = eventVisible; // Only collide if visible/active
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
        if (!IsServer || currentState.Value != GhostHandlerState.Dormant) return;
        
        // Show the event ghost for 3 seconds
        ghostEventTimer = 3f;
        UpdateVisibility(currentState.Value);
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

    public void StartHunt()
    {
        if (!IsServer || currentState.Value != GhostHandlerState.Dormant) return;

        // Check for nearby crucifixes that can block the hunt
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

        currentHuntDuration = Random.Range(huntDurationMin, huntDurationMax);
        huntTimer = 0f;
        
        if (hunterGhost != null)
        {
            hunterGhost.OnHuntStarted();
        }
        
        SetDoorsLocked(true);
        SetState(GhostHandlerState.Searching);
    }

    public void EndHunt()
    {
        if (!IsServer) return;
        
        // Reset ghost to the center of the ghost room when hunt ends so it doesn't stay in the hallway
        if (ghostroomMarker != null && hunterGhost != null)
        {
            var agent = hunterGhost.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh) agent.Warp(ghostroomMarker.bounds.center);
            else hunterGhost.transform.position = ghostroomMarker.bounds.center;
        }

        SetDoorsLocked(false);
        SetState(GhostHandlerState.Dormant);
    }

    private void SetDoorsLocked(bool locked)
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
