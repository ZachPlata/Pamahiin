using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody2D))]
public class EventGhostBehavior : NetworkBehaviour
{
    public enum GhostEventType
    {
        ThrowObject,
        WriteInBook,
        DotsManifestation,
        GhostOrbsManifestation,
        SpiritBoxTalk,
        LowSanityManifestation
    }

    [Header("Dependencies")]
    public GhostHandler ghostHandler; 
    
    [Header("Settings")]
    public float wanderRadius = 3f;
    public float wanderSpeed = 1.5f;

    [Header("Evidence Mechanics")]
    public float interactionScanRadius = 5f;
    public LayerMask interactionLayer;
    [HideInInspector]
    public float dotsDashSpeed = 1.5f;
    
    private float nextEventTime = 0f;
    public float eventIntervalMin = 8f;
    public float eventIntervalMax = 15f;

    [Header("Prop Disruption Frequency")]
    [SerializeField] private float propScanRadius = 2.6f;
    [SerializeField] private float propDisruptCooldownMin = 28f;
    [SerializeField] private float propDisruptCooldownMax = 48f;
    private float nextPropDisruptTime = 0f;

    private DotsProjectorItem activeDotsProjector;

    // Low Sanity Manifestation State
    private bool isManifesting = false;
    private float manifestTimer = 0f;
    private PlayerController targetManifestPlayer = null;

    private Rigidbody2D rb;
    private SpriteRenderer ghostSprite;
    private Vector3 initialSpawnLocation;
    private Vector3 currentDestination;
    private float waitTimer = 0f;
    private float wanderStuckTimer = 0f;
    private bool isInitialized = false;

    // Temporary orb only used if no native Ghost Orb exists yet (never duplicates the native one)
    private GhostOrbSystem temporaryEventOrb;

    /// <summary>
    /// Immediately cleans up any temporary orb spawned by a ghost event (e.g., on exorcism).
    /// </summary>
    public void CleanupTemporaryOrb()
    {
        if (temporaryEventOrb == null) return;
        DespawnOrDestroyOrb(temporaryEventOrb);
        temporaryEventOrb = null;
    }

    private static void DespawnOrDestroyOrb(GhostOrbSystem orb)
    {
        if (orb == null) return;

        var netObj = orb.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                netObj.Despawn(true);
        }
        else
        {
            Destroy(orb.gameObject);
        }
    }

    private System.Collections.IEnumerator DespawnTemporaryOrbRoutine(GhostOrbSystem orb, float delay)
    {
        yield return new WaitForSeconds(delay);
        DespawnOrDestroyOrb(orb);
        if (temporaryEventOrb == orb)
        {
            temporaryEventOrb = null;
        }
    }

    // Network synced state for visibility
    private NetworkVariable<bool> isVisiblyManifested = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
        }

        var col = GetComponent<Collider2D>();
        if (col == null)
        {
            var circle = gameObject.AddComponent<CircleCollider2D>();
            circle.radius = 0.2f;
        }

        ghostSprite = GetComponentInChildren<SpriteRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isVisiblyManifested.OnValueChanged += (oldVal, newVal) => UpdateVisuals(newVal);
        UpdateVisuals(isVisiblyManifested.Value);
    }

    private void UpdateVisuals(bool visible)
    {
        if (ghostSprite != null)
        {
            ghostSprite.enabled = visible;
            Color c = ghostSprite.color;
            c.a = 1f;
            ghostSprite.color = c;
        }
    }

    public void InitializeOrigin(Vector3 origin)
    {
        initialSpawnLocation = origin;
        currentDestination = origin;
        rb.position = origin;
        isInitialized = true;
        nextEventTime = Time.time + Random.Range(eventIntervalMin, eventIntervalMax);
        nextPropDisruptTime = Time.time + Random.Range(propDisruptCooldownMin * 0.5f, propDisruptCooldownMax * 0.65f);
    }

    private void Update()
    {
        if (!IsServer || !isInitialized) return;

        if (ghostHandler != null && ghostHandler.CurrentState == GhostHandlerState.Dormant)
        {
            if (!isManifesting && Time.time >= nextEventTime)
            {
                float diffMult = ProgressionAndLoadoutManager.Instance != null
                    ? ProgressionAndLoadoutManager.GetDifficultyEventIntervalMultiplier(ProgressionAndLoadoutManager.Instance.SelectedDifficulty)
                    : 1f;
                float interval = Random.Range(eventIntervalMin, eventIntervalMax) * WeatherManager.GetEventIntervalMultiplier() * diffMult;
                nextEventTime = Time.time + interval;
                ChooseAndExecuteEvent();
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer || !isInitialized) return;

        // Only move if we are the active phase according to GhostHandler
        if (ghostHandler != null && ghostHandler.CurrentState != GhostHandlerState.Dormant)
            return;

        if (isManifesting)
        {
            manifestTimer -= Time.fixedDeltaTime;
            
            // Check if player looked at ghost
            bool lookedAtGhost = false;
            if (targetManifestPlayer != null)
            {
                Vector2 dirToGhost = (rb.position - (Vector2)targetManifestPlayer.transform.position).normalized;
                // Using transform.up as forward for top-down 2D
                float dot = Vector2.Dot(targetManifestPlayer.transform.up, dirToGhost);
                if (dot > 0.5f) // Roughly 90 degree FOV cone
                {
                    lookedAtGhost = true;
                }
            }

            if (manifestTimer <= 0f || lookedAtGhost)
            {
                isManifesting = false;
                targetManifestPlayer = null;
                isVisiblyManifested.Value = false;
                UpdateVisuals(false);
            }
            return; // Stand still while manifesting
        }

        MoveTowards(currentDestination, wanderSpeed);

        wanderStuckTimer += Time.fixedDeltaTime;
        if (Vector2.Distance(rb.position, currentDestination) < 0.2f || wanderStuckTimer > 5.0f)
        {
            wanderStuckTimer = 0f;
            waitTimer -= Time.fixedDeltaTime;
            if (waitTimer <= 0f)
            {
                if (ghostHandler != null && ghostHandler.ghostroomMarker != null)
                {
                    Bounds bounds = ghostHandler.ghostroomMarker.bounds;
                    currentDestination = new Vector3(
                        Random.Range(bounds.min.x, bounds.max.x),
                        Random.Range(bounds.min.y, bounds.max.y),
                        initialSpawnLocation.z
                    );
                }
                else
                {
                    Vector2 randomOffset = Random.insideUnitCircle * wanderRadius;
                    currentDestination = initialSpawnLocation + (Vector3)randomOffset;
                }
                waitTimer = Random.Range(2f, 5f);
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // Don't physically block players when wandering invisibly
        if (collision.gameObject.GetComponent<PlayerController>() != null)
        {
            var myCol = GetComponent<Collider2D>();
            if (myCol != null && collision.collider != null)
            {
                Physics2D.IgnoreCollision(myCol, collision.collider, true);
            }
        }
    }

    private void MoveTowards(Vector3 targetPos, float speed)
    {
        Vector2 dir = (targetPos - (Vector3)rb.position).normalized;
        if (dir.sqrMagnitude > 0.01f)
        {
            rb.MovePosition(rb.position + dir * speed * Time.fixedDeltaTime);
            
            float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            rb.MoveRotation(Mathf.LerpAngle(rb.rotation, targetAngle, Time.fixedDeltaTime * 6f));
        }
    }

    public void ForceEvent(GhostEventType type)
    {
        if (!IsServer) return;
        ExecuteForcedEvent(type);
    }

    public void ExecuteForcedEvent(GhostEventType type)
    {
        DotsProjectorItem targetDots = null;
        SpiritBoxItem targetBox = null;
        VideoCameraItem targetCamera = null;
        PlayerController targetPlayer = null;

        if (type == GhostEventType.DotsManifestation)
        {
            if (ghostHandler != null && (ghostHandler.IsExorcised || ghostHandler.CurrentState != GhostHandlerState.Dormant))
                return;

            targetDots = SelectDotsProjectorForEvent(true, null, 0f, out bool ignoreRoomBounds);
            if (targetDots != null)
            {
                ExecuteDotsManifestation(targetDots, ignoreRoomBounds);
            }
            return;
        }
        else if (type == GhostEventType.SpiritBoxTalk)
        {
            var boxes = Object.FindObjectsByType<SpiritBoxItem>(FindObjectsInactive.Exclude);
            if (boxes.Length > 0) targetBox = boxes[Random.Range(0, boxes.Length)];
        }
        else if (type == GhostEventType.GhostOrbsManifestation)
        {
            var cameras = Object.FindObjectsByType<VideoCameraItem>(FindObjectsInactive.Exclude);
            var roomCameras = new List<VideoCameraItem>();
            foreach (var c in cameras)
            {
                if (ghostHandler != null && ghostHandler.ghostroomMarker != null && ghostHandler.ghostroomMarker.OverlapPoint(c.transform.position))
                    roomCameras.Add(c);
                else if (GhostRoomMarker.IsInsideGhostRoom(c.transform.position))
                    roomCameras.Add(c);
            }
            if (roomCameras.Count > 0) targetCamera = roomCameras[Random.Range(0, roomCameras.Count)];
        }
        else if (type == GhostEventType.LowSanityManifestation)
        {
            var players = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude);
            if (players.Length > 0) targetPlayer = players[Random.Range(0, players.Length)];
        }

        ExecuteEvent(type, targetDots, targetBox, targetPlayer, targetCamera);
    }

    private void ChooseAndExecuteEvent()
    {
        List<GhostEventType> validEvents = new List<GhostEventType>();

        // Find surrounding environment data
        Collider2D[] cols = Physics2D.OverlapCircleAll(rb.position, interactionScanRadius, interactionLayer);
        bool hasBook = false;
        bool hasDynamicRb = false;
        
        foreach (var col in cols)
        {
            var book = col.GetComponent<GhostWritingBookItem>();
            if (book != null && book.IsOpened && book.IsInsideGhostRoom()) hasBook = true;
            var rbObj = col.GetComponent<Rigidbody2D>();
            if (rbObj != null && rbObj != rb && rbObj.bodyType == RigidbodyType2D.Dynamic)
            {
                if (col.GetComponent<EquipmentItem>() == null) hasDynamicRb = true;
            }
        }

        // Check nearby sprite-animated ghost throwable/breakable props on the same floor
        bool propCooldownElapsed = Time.time >= nextPropDisruptTime;
        bool hasGhostProp = false;
        if (propCooldownElapsed)
        {
            foreach (var prop in GhostPropInteractable.AllProps)
            {
                if (prop != null && prop.CanGhostDisrupt &&
                    GameAudioManager.IsBasementPosition(prop.transform.position) == GameAudioManager.IsBasementPosition(rb.position) &&
                    Vector2.Distance(rb.position, prop.transform.position) <= propScanRadius)
                {
                    hasGhostProp = true;
                    break;
                }
            }
        }

        var spiritBoxes = Object.FindObjectsByType<SpiritBoxItem>(FindObjectsInactive.Exclude);
        bool hasActiveSpiritBox = false;
        SpiritBoxItem targetBox = null;
        foreach (var box in spiritBoxes)
        {
            if (box.IsPoweredOn && Vector2.Distance(rb.position, box.transform.position) <= interactionScanRadius)
            {
                hasActiveSpiritBox = true;
                targetBox = box;
                break;
            }
        }

        bool hasActiveDots = false;
        DotsProjectorItem targetDots = null;
        if (ghostHandler != null
            && !ghostHandler.IsExorcised
            && ghostHandler.CurrentState == GhostHandlerState.Dormant
            && ghostHandler.HasEvidence(EvidenceType.DOTS)
            && ghostHandler.IsEvidenceAllowed(EvidenceType.DOTS))
        {
            Vector2 ghostPos = rb != null ? rb.position : (Vector2)transform.position;
            targetDots = SelectDotsProjectorForEvent(false, ghostPos, interactionScanRadius, out _);
            hasActiveDots = targetDots != null;
        }

        var cameras = Object.FindObjectsByType<VideoCameraItem>(FindObjectsInactive.Exclude);
        bool hasActiveCamera = false;
        VideoCameraItem targetCamera = null;
        foreach (var cam in cameras)
        {
            // Only consider video cameras located inside the active ghost room
            if (ghostHandler != null && ghostHandler.ghostroomMarker != null)
            {
                if (!ghostHandler.ghostroomMarker.OverlapPoint(cam.transform.position))
                    continue;
            }
            else if (!GhostRoomMarker.IsInsideGhostRoom(cam.transform.position))
            {
                continue;
            }

            if (cam.IsPoweredOn && Vector2.Distance(rb.position, cam.transform.position) <= interactionScanRadius)
            {
                hasActiveCamera = true;
                targetCamera = cam;
                break;
            }
        }

        PlayerController lowSanityPlayer = null;
        foreach (var pc in Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
        {
            if (pc.Sanity < 50f)
            {
                lowSanityPlayer = pc;
                break;
            }
        }

        // Build valid pool based on evidence & environment
        if ((hasGhostProp || hasDynamicRb) && propCooldownElapsed) validEvents.Add(GhostEventType.ThrowObject);
        
        if (hasBook && ghostHandler != null && ghostHandler.HasEvidence(EvidenceType.GhostWriting)) 
            validEvents.Add(GhostEventType.WriteInBook);
        
        if (hasActiveDots && ghostHandler != null && ghostHandler.HasEvidence(EvidenceType.DOTS)) 
            validEvents.Add(GhostEventType.DotsManifestation);
            
        if (hasActiveCamera && ghostHandler != null && ghostHandler.HasEvidence(EvidenceType.GhostOrbs)) 
            validEvents.Add(GhostEventType.GhostOrbsManifestation);
            
        if (hasActiveSpiritBox && ghostHandler != null && ghostHandler.HasEvidence(EvidenceType.SpiritBox)) 
            validEvents.Add(GhostEventType.SpiritBoxTalk);
            
        if (lowSanityPlayer != null) 
            validEvents.Add(GhostEventType.LowSanityManifestation);

        // If no events possible, default to door/throw interaction attempt
        if (validEvents.Count == 0)
        {
            DoGenericInteraction();
            return;
        }

        // Pick and execute
        GhostEventType chosenEvent = validEvents[Random.Range(0, validEvents.Count)];
        ExecuteEvent(chosenEvent, targetDots, targetBox, lowSanityPlayer, targetCamera);
    }

    private void ExecuteEvent(GhostEventType type, DotsProjectorItem dots, SpiritBoxItem spiritBox, PlayerController player, VideoCameraItem camera)
    {
        switch (type)
        {
            case GhostEventType.ThrowObject:
                DoGenericInteraction(); // Throws or doors
                break;
                
            case GhostEventType.WriteInBook:
                Collider2D[] cols = Physics2D.OverlapCircleAll(rb.position, interactionScanRadius, interactionLayer);
                foreach (var col in cols)
                {
                    var book = col.GetComponent<GhostWritingBookItem>();
                    if (book != null && book.IsOpened && book.IsInsideGhostRoom())
                    {
                        book.WriteInBook();
                        if (ParanormalManager.Instance != null) ParanormalManager.Instance.RegisterEvent(rb.position, 2, 10f);
                        break;
                    }
                }
                break;
                
            case GhostEventType.DotsManifestation:
                ExecuteDotsManifestation(dots, false);
                break;
                
            case GhostEventType.SpiritBoxTalk:
                if (spiritBox != null)
                {
                    spiritBox.TogglePowerRpc(); // Ghost flickers it or responds directly
                    if (ParanormalManager.Instance != null) ParanormalManager.Instance.RegisterEvent(rb.position, 2, 8f);
                }
                break;
                
            case GhostEventType.GhostOrbsManifestation:
                if (camera != null && ghostHandler != null && !ghostHandler.IsExorcised && ghostHandler.ghostOrbPrefab != null && ghostHandler.ghostroomMarker != null)
                {
                    // Temporarily spawn an extra orb inside the active ghost room in front of camera
                    Vector2 candidatePos = (Vector2)camera.transform.position + (Vector2)camera.transform.up * 0.8f;
                    Vector2 spawnPos = candidatePos;
                    if (!ghostHandler.ghostroomMarker.OverlapPoint(spawnPos))
                    {
                        spawnPos = ghostHandler.ghostroomMarker.ClosestPoint(candidatePos);
                    }

                    if (ghostHandler.ghostroomMarker.OverlapPoint(spawnPos))
                    {
                        // Reuse the existing native orb instead of spawning a duplicate Ghost Orbs(Clone)
                        GhostOrbSystem existingOrb = ghostHandler.ActiveGhostOrb;
                        if (existingOrb == null) existingOrb = FindAnyObjectByType<GhostOrbSystem>();

                        if (existingOrb != null)
                        {
                            existingOrb.BindRoomCollider(ghostHandler.ghostroomMarker);
                            existingOrb.NudgeTo(spawnPos);
                        }
                        else if (temporaryEventOrb == null)
                        {
                            GameObject orb = Instantiate(ghostHandler.ghostOrbPrefab, spawnPos, Quaternion.identity);
                            var orbNetwork = orb.GetComponent<NetworkObject>();
                            if (orbNetwork != null) orbNetwork.Spawn();

                            var orbSystem = orb.GetComponent<GhostOrbSystem>();
                            if (orbSystem != null)
                            {
                                orbSystem.Initialize(ghostHandler.ghostroomMarker);
                                temporaryEventOrb = orbSystem;
                                StartCoroutine(DespawnTemporaryOrbRoutine(orbSystem, 5f));
                            }
                        }
                    }
                }
                break;
                
            case GhostEventType.LowSanityManifestation:
                if (player != null)
                {
                    // Spawn behind the player (outside their FOV cone)
                    // player.transform.up is forward in 2D. -player.transform.up is behind.
                    Vector2 randomBehindOffset = (-player.transform.up + (Vector3)Random.insideUnitCircle * 0.5f).normalized * 2f;
                    rb.position = (Vector2)player.transform.position + randomBehindOffset;
                    
                    targetManifestPlayer = player;
                    isManifesting = true;
                    manifestTimer = 3f; // Stay visible for 3 seconds
                    isVisiblyManifested.Value = true;
                    UpdateVisuals(true);
                    
                    if (ParanormalManager.Instance != null) ParanormalManager.Instance.RegisterEvent(rb.position, 4, 15f);
                }
                break;
        }
    }

    private void DoGenericInteraction()
    {
        // Limit prop throws/knockdowns to an infrequent cooldown on the ghost's own floor.
        bool canDisruptProp = Time.time >= nextPropDisruptTime;
        var nearbyProps = new System.Collections.Generic.List<GhostPropInteractable>();
        if (canDisruptProp)
        {
            foreach (var prop in GhostPropInteractable.AllProps)
            {
                if (prop != null && prop.CanGhostDisrupt &&
                    GameAudioManager.IsBasementPosition(prop.transform.position) == GameAudioManager.IsBasementPosition(rb.position) &&
                    Vector2.Distance(rb.position, prop.transform.position) <= propScanRadius)
                {
                    nearbyProps.Add(prop);
                }
            }
        }

        if (canDisruptProp && nearbyProps.Count > 0 && Random.value < 0.42f)
        {
            var chosen = nearbyProps[Random.Range(0, nearbyProps.Count)];
            if (chosen.TriggerGhostDisruption(false))
            {
                nextPropDisruptTime = Time.time + Random.Range(propDisruptCooldownMin, propDisruptCooldownMax) * WeatherManager.GetEventIntervalMultiplier();
                return;
            }
        }

        Collider2D[] cols = Physics2D.OverlapCircleAll(rb.position, interactionScanRadius, interactionLayer);
        foreach (var col in cols)
        {
            var door = col.GetComponent<NetworkDoor>();
            if (door != null && Random.value < 0.45f)
            {
                door.GhostInteract();
                return;
            }
        }

        if (canDisruptProp && nearbyProps.Count > 0 && Random.value < 0.45f)
        {
            var chosen = nearbyProps[Random.Range(0, nearbyProps.Count)];
            if (chosen.TriggerGhostDisruption(false))
            {
                nextPropDisruptTime = Time.time + Random.Range(propDisruptCooldownMin, propDisruptCooldownMax) * WeatherManager.GetEventIntervalMultiplier();
            }
        }
    }

    public DotsProjectorItem SelectDotsProjectorForEvent(
        bool allowSandboxFallback,
        Vector2? ghostWorldPos,
        float minScanRadius,
        out bool usedSandboxFallback)
    {
        usedSandboxFallback = false;
        var projectors = Object.FindObjectsByType<DotsProjectorItem>(FindObjectsInactive.Exclude);
        Collider2D roomMarker = ghostHandler != null ? ghostHandler.ghostroomMarker : null;

        var roomMatches = new List<DotsProjectorItem>();
        foreach (var dots in projectors)
        {
            if (dots == null) continue;
            float maxDist = ghostWorldPos.HasValue
                ? Mathf.Max(minScanRadius, dots.GetEffectiveBeamRadius() * 2.5f)
                : 0f;

            if (dots.IsEligibleForProjection(true, roomMarker, ghostWorldPos, maxDist))
            {
                roomMatches.Add(dots);
            }
        }

        if (roomMatches.Count > 0)
        {
            return roomMatches[Random.Range(0, roomMatches.Count)];
        }

        if (!allowSandboxFallback)
        {
            return null;
        }

        var sandboxMatches = new List<DotsProjectorItem>();
        foreach (var dots in projectors)
        {
            if (dots == null) continue;
            if (dots.IsEligibleForProjection(false, null))
            {
                sandboxMatches.Add(dots);
            }
        }

        if (sandboxMatches.Count > 0)
        {
            usedSandboxFallback = true;
            return sandboxMatches[Random.Range(0, sandboxMatches.Count)];
        }

        return null;
    }

    public bool ExecuteDotsManifestation(DotsProjectorItem dots, bool ignoreRoomBounds = false)
    {
        if (dots == null) return false;
        if (ghostHandler != null && (ghostHandler.IsExorcised || ghostHandler.CurrentState != GhostHandlerState.Dormant))
            return false;

        Collider2D roomCol = ghostHandler != null ? ghostHandler.ghostroomMarker : null;
        if (!dots.TriggerDotsProjectionOnServer(roomCol, ignoreRoomBounds, out Vector2 projectionCenter))
        {
            return false;
        }

        activeDotsProjector = dots;
        if (ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(projectionCenter, 2, 5f);
        }
        return true;
    }

    public void CancelActiveDotsProjectionOnServer()
    {
        if (activeDotsProjector != null)
        {
            activeDotsProjector.CancelDotsProjectionOnServer();
            activeDotsProjector = null;
        }
    }
}
