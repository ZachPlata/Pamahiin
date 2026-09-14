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
    public float dotsDashSpeed = 10f;
    
    private float nextEventTime = 0f;
    public float eventIntervalMin = 8f;
    public float eventIntervalMax = 15f;

    // DOTS Dash state
    private bool isDashing = false;
    private Vector2 dashTarget;
    private DotsProjectorItem activeDotsProjector;
    private float currentDashTime = 0f;

    // Low Sanity Manifestation State
    private bool isManifesting = false;
    private float manifestTimer = 0f;
    private PlayerController targetManifestPlayer = null;

    private Rigidbody2D rb;
    private SpriteRenderer ghostSprite;
    private Vector3 initialSpawnLocation;
    private Vector3 currentDestination;
    private float waitTimer = 0f;
    private bool isInitialized = false;

    // Network synced state for visibility
    private NetworkVariable<bool> isVisiblyManifested = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
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
            if (visible)
            {
                Color c = ghostSprite.color;
                // If it's a DOTS dash, we use 50% opacity. If it's a regular manifestation, we use 100% opacity.
                c.a = isDashing ? 0.5f : 1f;
                ghostSprite.color = c;
            }
            else
            {
                Color c = ghostSprite.color;
                c.a = 1f; 
                ghostSprite.color = c;
            }
        }
    }

    public void InitializeOrigin(Vector3 origin)
    {
        initialSpawnLocation = origin;
        currentDestination = origin;
        rb.position = origin;
        isInitialized = true;
        nextEventTime = Time.time + Random.Range(eventIntervalMin, eventIntervalMax);
    }

    private void Update()
    {
        if (!IsServer || !isInitialized) return;

        if (ghostHandler != null && ghostHandler.CurrentState == GhostHandlerState.Dormant)
        {
            if (!isDashing && !isManifesting && Time.time >= nextEventTime)
            {
                nextEventTime = Time.time + Random.Range(eventIntervalMin, eventIntervalMax);
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

        if (isDashing)
        {
            currentDashTime += Time.fixedDeltaTime;
            
            bool cancelDash = (activeDotsProjector != null && !activeDotsProjector.IsPoweredOn);
            bool reachedTarget = Vector2.Distance(rb.position, dashTarget) < 0.2f;
            bool stuck = currentDashTime > 1.5f;

            if (cancelDash || reachedTarget || stuck)
            {
                isDashing = false;
                activeDotsProjector = null;
                isVisiblyManifested.Value = false;
                UpdateVisuals(false);
                return;
            }

            MoveTowards(dashTarget, dotsDashSpeed);
            return;
        }

        MoveTowards(currentDestination, wanderSpeed);

        if (Vector2.Distance(rb.position, currentDestination) < 0.2f)
        {
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
        
        DotsProjectorItem targetDots = null;
        SpiritBoxItem targetBox = null;
        VideoCameraItem targetCamera = null;
        PlayerController targetPlayer = null;

        if (type == GhostEventType.DotsManifestation)
        {
            var projectors = Object.FindObjectsByType<DotsProjectorItem>(FindObjectsInactive.Exclude);
            if (projectors.Length > 0) targetDots = projectors[Random.Range(0, projectors.Length)];
        }
        else if (type == GhostEventType.SpiritBoxTalk)
        {
            var boxes = Object.FindObjectsByType<SpiritBoxItem>(FindObjectsInactive.Exclude);
            if (boxes.Length > 0) targetBox = boxes[Random.Range(0, boxes.Length)];
        }
        else if (type == GhostEventType.GhostOrbsManifestation)
        {
            var cameras = Object.FindObjectsByType<VideoCameraItem>(FindObjectsInactive.Exclude);
            if (cameras.Length > 0) targetCamera = cameras[Random.Range(0, cameras.Length)];
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
            if (col.GetComponent<GhostWritingBookItem>() != null) hasBook = true;
            var rbObj = col.GetComponent<Rigidbody2D>();
            if (rbObj != null && rbObj != rb && rbObj.bodyType == RigidbodyType2D.Dynamic)
            {
                if (col.GetComponent<EquipmentItem>() == null) hasDynamicRb = true;
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

        var dotsProjectors = Object.FindObjectsByType<DotsProjectorItem>(FindObjectsInactive.Exclude);
        bool hasActiveDots = false;
        DotsProjectorItem targetDots = null;
        foreach (var dots in dotsProjectors)
        {
            if (dots.IsPoweredOn && Vector2.Distance(rb.position, dots.transform.position) <= dots.projectionRadius * 1.5f)
            {
                hasActiveDots = true;
                targetDots = dots;
                break;
            }
        }

        var cameras = Object.FindObjectsByType<VideoCameraItem>(FindObjectsInactive.Exclude);
        bool hasActiveCamera = false;
        VideoCameraItem targetCamera = null;
        foreach (var cam in cameras)
        {
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
        if (hasDynamicRb) validEvents.Add(GhostEventType.ThrowObject);
        
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
                    if (book != null && book.IsOpened)
                    {
                        book.WriteInBook();
                        if (ParanormalManager.Instance != null) ParanormalManager.Instance.RegisterEvent(rb.position, 2, 10f);
                        break;
                    }
                }
                break;
                
            case GhostEventType.DotsManifestation:
                if (dots != null)
                {
                    // Teleport the ghost to the edge of the DOTS projector so it always dashes THROUGH the beam
                    Vector2 randomStartDir = Random.insideUnitCircle.normalized;
                    rb.position = (Vector2)dots.transform.position + randomStartDir * dots.projectionRadius;
                    
                    Vector2 dirToDots = ((Vector2)dots.transform.position - rb.position);
                    isDashing = true;
                    currentDashTime = 0f;
                    activeDotsProjector = dots;
                    isVisiblyManifested.Value = true; // Show at 50% opacity
                    UpdateVisuals(true);
                    
                    // Dash across the projector to the opposite side
                    dashTarget = rb.position + dirToDots.normalized * (dots.projectionRadius * 2.5f);
                    
                    if (ParanormalManager.Instance != null) ParanormalManager.Instance.RegisterEvent(rb.position, 2, 5f);
                }
                break;
                
            case GhostEventType.SpiritBoxTalk:
                if (spiritBox != null)
                {
                    spiritBox.TogglePowerRpc(); // Ghost flickers it or responds directly
                    if (ParanormalManager.Instance != null) ParanormalManager.Instance.RegisterEvent(rb.position, 2, 8f);
                }
                break;
                
            case GhostEventType.GhostOrbsManifestation:
                if (camera != null && ghostHandler != null && ghostHandler.ghostOrbPrefab != null)
                {
                    // Temporarily spawn an extra orb directly in front of the camera as an active event
                    Vector2 spawnPos = camera.transform.position + camera.transform.up * 1f;
                    GameObject orb = Instantiate(ghostHandler.ghostOrbPrefab, spawnPos, Quaternion.identity);
                    var orbNetwork = orb.GetComponent<NetworkObject>();
                    if (orbNetwork != null) orbNetwork.Spawn();
                    
                    var orbSystem = orb.GetComponent<GhostOrbSystem>();
                    if (orbSystem != null) 
                    {
                        // Give it a tiny bounds so it stays right in front of the camera
                        Bounds camBounds = new Bounds(spawnPos, new Vector3(1f, 1f, 1f));
                        orbSystem.Initialize(camBounds);
                    }
                    
                    // Destroy this temporary event orb after 5 seconds
                    Destroy(orb, 5f);
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
        Collider2D[] cols = Physics2D.OverlapCircleAll(rb.position, interactionScanRadius, interactionLayer);
        foreach (var col in cols)
        {
            var door = col.GetComponent<NetworkDoor>();
            if (door != null && Random.value < 0.3f)
            {
                door.GhostInteract();
                return;
            }

            var rbObj = col.GetComponent<Rigidbody2D>();
            if (rbObj != null && rbObj != rb && rbObj.bodyType == RigidbodyType2D.Dynamic && Random.value < 0.3f)
            {
                if (col.GetComponent<EquipmentItem>() == null)
                {
                    Vector2 throwForce = Random.insideUnitCircle * Random.Range(3f, 8f);
                    rbObj.AddForce(throwForce, ForceMode2D.Impulse);
                    if (ParanormalManager.Instance != null) ParanormalManager.Instance.RegisterEvent(rb.position, 3, 15f);
                    return;
                }
            }
        }
    }
}
