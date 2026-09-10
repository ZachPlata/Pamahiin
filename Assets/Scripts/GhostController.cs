using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public enum GhostState
{
    Dormant,
    Wander,
    Interact,
    Evidence,
    HuntManifest,
    HuntSearch,
    HuntChase
}

[RequireComponent(typeof(Rigidbody2D))]
public class GhostController : NetworkBehaviour
{
    [Header("Ghost Identity & Evidence")]
    public string ghostName = "White Lady";
    [SerializeField] private bool evidenceEmf5 = true;
    [SerializeField] private bool evidenceFreezingTemps = true;
    [SerializeField] private bool evidenceGhostWriting = false;
    [SerializeField] private bool evidenceDotsProjector = false;
    [SerializeField] private bool evidenceSpiritBox = false;
    [SerializeField] private bool evidenceGhostOrbs = false;

    [Header("Ghost Orb Prefab")]
    [SerializeField] private GameObject ghostOrbPrefab;

    public bool EvidenceSpiritBox => evidenceSpiritBox;

    [Header("Movement Speeds")]
    [SerializeField] private float wanderSpeed = 1.6f;
    [SerializeField] private float searchSpeed = 2.4f;
    [SerializeField] private float chaseSpeed = 3.8f;

    [Header("Vision & Detection")]
    [SerializeField] private float visionConeAngle = 120f;
    [SerializeField] private float visionDistance = 8f;
    [SerializeField] private float proximityRadius = 2f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Roaming Settings")]
    [SerializeField] private Vector2 favoriteRoomCenter = Vector2.zero;
    [SerializeField] private float roamRadius = 7f;

    [Header("Hunt Settings")]
    [SerializeField] private float huntGracePeriod = 3f;
    [SerializeField] private float huntDuration = 30f;
    [SerializeField] private float huntCooldown = 45f;

    [Header("Visuals & Audio")]
    [SerializeField] private SpriteRenderer ghostSprite;
    [SerializeField] private SpriteRenderer dotsSilhouetteSprite;
    [SerializeField] private Light2D ghostAuraLight;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip manifestAudio;
    [SerializeField] private AudioClip chaseAudio;

    // Network synced state
    private NetworkVariable<GhostState> currentState = new NetworkVariable<GhostState>(
        GhostState.Dormant, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isVisuallyManifested = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        
    private NetworkVariable<bool> isDotsSilhouetteVisible = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        
    private NetworkVariable<bool> isDevOutlineVisible = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public GhostState CurrentState => currentState.Value;
    public bool IsHunting => currentState.Value == GhostState.HuntManifest ||
                             currentState.Value == GhostState.HuntSearch ||
                             currentState.Value == GhostState.HuntChase;

    private Rigidbody2D rb;
    private Vector2 currentDestination;
    private PlayerController chaseTargetPlayer;
    private Vector2 lastSeenPlayerPosition;

    private float stateTimer = 0f;
    private float huntTimer = 0f;
    private float nextHuntAllowedTime = 0f;
    private float lostTargetTimer = 0f;
    private float huntItemFlingTimer = 0f;
    private Vector2 lastStuckPos;
    private float stuckTimer = 0f;
    private float spriteFlickerTimer = 0f;
    private bool spriteFlickerState = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (ghostSprite == null) ghostSprite = GetComponentInChildren<SpriteRenderer>();
        if (ghostAuraLight == null) ghostAuraLight = GetComponentInChildren<Light2D>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        currentState.OnValueChanged += (oldVal, newVal) => OnStateChanged(newVal);
        isVisuallyManifested.OnValueChanged += (oldVal, newVal) => UpdateVisuals(newVal);
        isDotsSilhouetteVisible.OnValueChanged += (oldVal, newVal) => UpdateDotsVisuals(newVal);
        isDevOutlineVisible.OnValueChanged += (oldVal, newVal) => UpdateVisuals(isVisuallyManifested.Value);

        UpdateVisuals(isVisuallyManifested.Value);
    }

    public void ToggleDevOutline()
    {
        if (IsServer)
        {
            isDevOutlineVisible.Value = !isDevOutlineVisible.Value;
        }
    }

    public void ActivateGhost()
    {
        if (!IsServer || currentState.Value != GhostState.Dormant) return;

        // Randomize Ghost Type and Evidence
        int rand = Random.Range(0, 3);
        evidenceEmf5 = false;
        evidenceFreezingTemps = false;
        evidenceGhostWriting = false;
        evidenceDotsProjector = false;
        evidenceSpiritBox = false;
        evidenceGhostOrbs = false;

        if (rand == 0)
        {
            ghostName = "Tikbalang";
            evidenceEmf5 = true;
            evidenceSpiritBox = true;
            evidenceGhostOrbs = true;
        }
        else if (rand == 1)
        {
            ghostName = "Kapre";
            evidenceEmf5 = true;
            evidenceDotsProjector = true;
            evidenceFreezingTemps = true;
        }
        else
        {
            ghostName = "Manananggal";
            evidenceGhostOrbs = true;
            evidenceGhostWriting = true;
            evidenceFreezingTemps = true;
        }

        GhostRoomMarker[] availableRooms = Object.FindObjectsByType<GhostRoomMarker>(FindObjectsInactive.Exclude);
        if (availableRooms != null && availableRooms.Length > 0)
        {
            // Pick a random room and teleport the ghost there
            GhostRoomMarker chosenRoom = availableRooms[Random.Range(0, availableRooms.Length)];
            transform.position = chosenRoom.transform.position;
            if (rb != null) rb.position = transform.position;
        }

        favoriteRoomCenter = transform.position;
        currentDestination = favoriteRoomCenter;
        nextHuntAllowedTime = Time.time + huntCooldown;

        if (evidenceGhostOrbs && ghostOrbPrefab != null)
        {
            var orb = Instantiate(ghostOrbPrefab, favoriteRoomCenter, Quaternion.identity);
            var netObj = orb.GetComponent<NetworkObject>();
            if (netObj != null) netObj.Spawn();
            
            var orbSys = orb.GetComponent<GhostOrbSystem>();
            if (orbSys != null) orbSys.Initialize(favoriteRoomCenter, roamRadius);
        }

        if (ParanormalManager.Instance != null)
        {
            // ParanormalManager.Instance.SetGhostInfo(transform, favoriteRoomCenter, roamRadius, evidenceFreezingTemps, evidenceEmf5);
        }

        SetState(GhostState.Wander);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.ClearGhostInfo();
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        stateTimer += Time.deltaTime;

        // Ignore Player collisions dynamically
        Collider2D myCol = GetComponent<Collider2D>();
        if (myCol != null)
        {
            foreach (var p in PlayerController.AllPlayers)
            {
                if (p != null)
                {
                    Collider2D pCol = p.GetComponent<Collider2D>();
                    if (pCol != null) Physics2D.IgnoreCollision(myCol, pCol, true);
                }
            }
        }

        // Hunt Sprite Flickering
        if (IsHunting && ghostSprite != null)
        {
            spriteFlickerTimer += Time.deltaTime;
            if (spriteFlickerTimer >= 0.15f)
            {
                spriteFlickerTimer = 0f;
                spriteFlickerState = !spriteFlickerState;
                ghostSprite.enabled = spriteFlickerState;
            }
        }

        if (evidenceDotsProjector)
        {
            bool inDots = false;
            var projectors = Object.FindObjectsByType<DotsProjectorItem>(FindObjectsInactive.Exclude);
            foreach (var proj in projectors)
            {
                if (proj.IsPoweredOn)
                {
                    Vector2 dirToGhost = (Vector2)transform.position - (Vector2)proj.transform.position;
                    if (dirToGhost.magnitude <= proj.projectionRadius)
                    {
                        // Check if the ghost is inside the cone angle relative to the projector's forward direction (up)
                        float angle = Vector2.Angle(proj.transform.up, dirToGhost);
                        if (angle <= proj.coneAngle / 2f)
                        {
                            inDots = true;
                            break;
                        }
                    }
                }
            }
            if (isDotsSilhouetteVisible.Value != inDots)
            {
                isDotsSilhouetteVisible.Value = inDots;
            }
        }

        switch (currentState.Value)
        {
            case GhostState.Dormant:
                // Do nothing until activated
                break;
            case GhostState.Wander:
                UpdateWanderState();
                break;
            case GhostState.Interact:
                UpdateInteractState();
                break;
            case GhostState.Evidence:
                UpdateEvidenceState();
                break;
            case GhostState.HuntManifest:
                UpdateHuntManifestState();
                break;
            case GhostState.HuntSearch:
                UpdateHuntSearchState();
                break;
            case GhostState.HuntChase:
                UpdateHuntChaseState();
                break;
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // Move towards currentDestination
        float speed = wanderSpeed;
        if (currentState.Value == GhostState.HuntChase) speed = chaseSpeed;
        else if (currentState.Value == GhostState.HuntSearch) speed = searchSpeed;

        Vector2 direction = (currentDestination - rb.position).normalized;
        float distance = Vector2.Distance(rb.position, currentDestination);

        if (distance > 0.3f)
        {
            // Simple 2D obstacle avoidance raycast
            RaycastHit2D hit = Physics2D.CircleCast(rb.position, 0.4f, direction, 0.8f, obstacleLayer);
            if (hit.collider != null)
            {
                // Adjust direction slightly around normal
                direction = Vector2.Perpendicular(hit.normal).normalized;
                
                // If obstacle is very close, we might be running face first into a wall
                if (hit.distance < 0.2f && !IsHunting)
                {
                    currentDestination = rb.position + Random.insideUnitCircle * 5f;
                }
            }

            rb.MovePosition(rb.position + direction * speed * Time.fixedDeltaTime);
            
            // Unstuck logic
            if (Vector2.Distance(rb.position, lastStuckPos) < 0.05f)
            {
                stuckTimer += Time.fixedDeltaTime;
                if (stuckTimer >= 0.5f)
                {
                    stuckTimer = 0f;
                    currentDestination = rb.position + Random.insideUnitCircle * 5f;
                }
            }
            else
            {
                stuckTimer = 0f;
                lastStuckPos = rb.position;
            }

            // Rotate smoothly towards movement direction
            if (direction.sqrMagnitude > 0.01f)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
                rb.MoveRotation(Mathf.LerpAngle(rb.rotation, angle, Time.fixedDeltaTime * 6f));
            }
        }
    }

    #region State Machine Logic (Server)

    private void UpdateWanderState()
    {
        float dist = Vector2.Distance(rb.position, currentDestination);
        if (dist <= 0.5f || stateTimer > 12f)
        {
            stateTimer = 0f;

            // Random chance for interaction or evidence drop
            float roll = Random.value;
            if (roll < 0.35f)
            {
                SetState(GhostState.Interact);
                return;
            }
            else if (roll < 0.55f)
            {
                SetState(GhostState.Evidence);
                return;
            }

            // Pick new wander destination near favorite room
            Vector2 randomOffset = Random.insideUnitCircle * roamRadius;
            currentDestination = favoriteRoomCenter + randomOffset;
        }

        // Automatic hunts are disabled in Phase 3.
        // Hunts will only trigger via ForceStartHunt() during the Exorcism phase.
    }

    private void UpdateInteractState()
    {
        PerformParanormalInteraction();
        SetState(GhostState.Wander);
    }

    private void UpdateEvidenceState()
    {
        // Emit evidence (e.g. Freezing temps or Ghost Orbs if applicable)
        // EMF 5 is handled automatically via a 25% chance during standard interactions

        SetState(GhostState.Wander);
    }

    private void UpdateHuntManifestState()
    {
        // Grace period countdown
        if (stateTimer >= huntGracePeriod)
        {
            SetState(GhostState.HuntSearch);
        }
    }

    private void HandleHuntEnvironment()
    {
        huntItemFlingTimer += Time.deltaTime;
        if (huntItemFlingTimer >= 1f)
        {
            huntItemFlingTimer = 0f;
            Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, 6f);
            
            if (Random.value < 0.25f) // 25% chance every 1 second
            {
                foreach (var col in colliders)
                {
                    var propRb = col.GetComponent<Rigidbody2D>();
                    if (propRb != null && col.gameObject != gameObject && !col.CompareTag("Player") && !col.CompareTag("Ghost"))
                    {
                        Vector2 throwDir = Random.insideUnitCircle.normalized;
                        float throwForce = Random.Range(3f, 6f);
                        StartCoroutine(ThrowPropRoutine(propRb, throwDir, throwForce, 0.5f));
                        break; // Just fling one nearby item
                    }
                }
            }
            
            // Light flickering
            foreach (var col in colliders)
            {
                var lightSwitch = col.GetComponent<HouseLightSwitch>();
                if (lightSwitch != null && lightSwitch.IsOn)
                {
                    lightSwitch.FlickerEffect(1.2f);
                }
            }
        }
    }

    private void UpdateHuntSearchState()
    {
        huntTimer += Time.deltaTime;
        if (huntTimer >= huntDuration)
        {
            EndHunt();
            return;
        }

        HandleHuntEnvironment();

        // Check if any player enters vision cone or proximity circle
        PlayerController detectedPlayer = ScanForPlayers();
        if (detectedPlayer != null)
        {
            chaseTargetPlayer = detectedPlayer;
            lastSeenPlayerPosition = detectedPlayer.transform.position;
            currentDestination = lastSeenPlayerPosition;
            SetState(GhostState.HuntChase);
            return;
        }

        // Roam to search destinations
        if (Vector2.Distance(rb.position, currentDestination) <= 0.6f || stateTimer > 6f)
        {
            stateTimer = 0f;
            currentDestination = rb.position + Random.insideUnitCircle * 8f;
        }
    }

    private void UpdateHuntChaseState()
    {
        huntTimer += Time.deltaTime;
        if (huntTimer >= huntDuration)
        {
            EndHunt();
            return;
        }

        if (chaseTargetPlayer == null || !chaseTargetPlayer.IsAlive)
        {
            SetState(GhostState.HuntSearch);
            return;
        }

        HandleHuntEnvironment();

        // Check if target is hidden in a HideZone or broke line of sight
        bool canSee = CanSeePlayer(chaseTargetPlayer);

        if (canSee && !chaseTargetPlayer.IsHiddenFromGhost)
        {
            lastSeenPlayerPosition = chaseTargetPlayer.transform.position;
            currentDestination = lastSeenPlayerPosition;
            lostTargetTimer = 0f;
        }
        else
        {
            // Player broke LOS or crouched behind cover
            lostTargetTimer += Time.deltaTime;
            currentDestination = lastSeenPlayerPosition;

            // Give up chase after 2.5s of losing LOS and revert to search
            if (lostTargetTimer >= 2.5f || Vector2.Distance(rb.position, lastSeenPlayerPosition) <= 0.6f)
            {
                chaseTargetPlayer = null;
                SetState(GhostState.HuntSearch);
                return;
            }
        }

        // Kill check
        float distToPlayer = Vector2.Distance(rb.position, chaseTargetPlayer.transform.position);
        if (distToPlayer <= 0.35f)
        {
            chaseTargetPlayer.KillPlayer();
            EndHunt();
        }
    }

    private void PerformParanormalInteraction()
    {
        // 1. Find nearby doors or interactables
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, 3.5f);
        foreach (var col in colliders)
        {
            var door = col.GetComponent<NetworkDoor>();
            if (door != null)
            {
                door.GhostInteract();
                return;
            }

            var propRb = col.GetComponent<Rigidbody2D>();
            if (propRb != null && col.gameObject != gameObject && !col.CompareTag("Player"))
            {
                if (Random.value < 0.15f) // 15% chance to throw item during normal interactions
                {
                    // Throw physical prop with a much smaller force, and stop it after 0.1-0.3s
                    Vector2 throwDir = Random.insideUnitCircle.normalized;
                    float throwForce = Random.Range(0.5f, 1.5f); // 10% of previous force
                    float slideDuration = Random.Range(0.1f, 0.3f);
                    StartCoroutine(ThrowPropRoutine(propRb, throwDir, throwForce, slideDuration));

                    if (ParanormalManager.Instance != null)
                    {
                        ParanormalManager.Instance.RegisterEvent(propRb.position, 3, 20f);
                    }
                    return;
                }
            }
        }

        // Ghost writing interaction
        if (evidenceGhostWriting)
        {
            var books = Object.FindObjectsByType<GhostWritingBookItem>(FindObjectsInactive.Exclude);
            foreach (var book in books)
            {
                if (book.IsOpened && Vector2.Distance(transform.position, book.transform.position) <= 3.5f)
                {
                    book.WriteInBook();
                    if (ParanormalManager.Instance != null)
                    {
                        ParanormalManager.Instance.RegisterEvent(book.transform.position, 2, 15f);
                    }
                    return;
                }
            }
        }

        // Light switch interaction: randomly pick an active light switch in the house and turn it off
        var switches = Object.FindObjectsByType<HouseLightSwitch>(FindObjectsInactive.Exclude);
        List<HouseLightSwitch> activeSwitches = new List<HouseLightSwitch>();
        foreach (var sw in switches)
        {
            if (sw.IsOn) activeSwitches.Add(sw);
        }

        if (activeSwitches.Count > 0)
        {
            var chosenSwitch = activeSwitches[Random.Range(0, activeSwitches.Count)];
            chosenSwitch.GhostTurnOff();
            return;
        }

        // Fallback: register ghost presence event (EMF 4 for manifestations)
        if (ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(transform.position, 4, 15f);
        }
    }

    private IEnumerator ThrowPropRoutine(Rigidbody2D propRb, Vector2 throwDir, float force, float duration)
    {
        if (propRb == null) yield break;
        
        propRb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // Prevent tunneling when thrown
        propRb.AddForce(throwDir * force, ForceMode2D.Impulse);
        
        yield return new WaitForSeconds(duration);
        
        if (propRb != null)
        {
            propRb.linearVelocity = Vector2.zero;
            propRb.angularVelocity = 0f;
            propRb.collisionDetectionMode = CollisionDetectionMode2D.Discrete; // Reset for performance
        }
    }

    public void ForceStartHunt()
    {
        if (!IsServer) return;
        
        // Exorcism hunts cannot be blocked and ignore cooldowns!
        InternalStartHunt();
    }

    public void StartHunt()
    {
        if (!IsServer || IsHunting) return;

        // Check for nearby crucifixes that can block the hunt
        var crucifixes = Object.FindObjectsByType<CrucifixItem>(FindObjectsInactive.Exclude);
        foreach (var crucifix in crucifixes)
        {
            if (!crucifix.IsBurned && Vector2.Distance(transform.position, crucifix.transform.position) <= crucifix.blockRadius)
            {
                if (crucifix.TryBlockHunt())
                {
                    // Hunt successfully blocked by crucifix
                    nextHuntAllowedTime = Time.time + huntCooldown;
                    return;
                }
            }
        }

        InternalStartHunt();
    }

    private void InternalStartHunt()
    {
        huntTimer = 0f;
        stateTimer = 0f;
        isVisuallyManifested.Value = true;

        // Lock all exit doors
        SetDoorsLocked(true);

        // Register EMF 4 during Hunt Manifestation
        if (ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(transform.position, 4, huntDuration);
        }

        SetState(GhostState.HuntManifest);
    }

    public void EndHunt()
    {
        if (!IsServer) return;

        isVisuallyManifested.Value = false;
        nextHuntAllowedTime = Time.time + huntCooldown;

        // Unlock all doors
        SetDoorsLocked(false);

        SetState(GhostState.Wander);
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

    private void SetState(GhostState newState)
    {
        currentState.Value = newState;
        stateTimer = 0f;
    }

    #endregion

    #region Vision & Detection

    private PlayerController ScanForPlayers()
    {
        foreach (var player in PlayerController.AllPlayers)
        {
            if (player == null || !player.IsAlive) continue;
            if (player.IsHiddenFromGhost) continue;

            float distance = Vector2.Distance(rb.position, player.transform.position);

            // 1. Proximity check (360 degrees)
            if (distance <= proximityRadius)
            {
                if (HasLineOfSight(player.transform.position))
                {
                    return player;
                }
            }

            // 2. Vision Cone check
            if (distance <= visionDistance)
            {
                Vector2 dirToPlayer = ((Vector2)player.transform.position - rb.position).normalized;
                float angle = Vector2.Angle(transform.up, dirToPlayer);

                if (angle <= (visionConeAngle * 0.5f))
                {
                    if (HasLineOfSight(player.transform.position))
                    {
                        return player;
                    }
                }
            }
        }

        return null;
    }

    private bool CanSeePlayer(PlayerController player)
    {
        if (player == null || !player.IsAlive || player.IsHiddenFromGhost) return false;

        float distance = Vector2.Distance(rb.position, player.transform.position);
        if (distance > visionDistance * 1.3f) return false;

        return HasLineOfSight(player.transform.position);
    }

    private bool HasLineOfSight(Vector2 targetPos)
    {
        Vector2 dir = targetPos - rb.position;
        float dist = dir.magnitude;

        RaycastHit2D hit = Physics2D.Raycast(rb.position, dir.normalized, dist, obstacleLayer);
        return hit.collider == null;
    }

    #endregion

    private void OnStateChanged(GhostState newState)
    {
        if (newState == GhostState.HuntManifest && manifestAudio != null && audioSource != null)
        {
            audioSource.PlayOneShot(manifestAudio);
        }
        else if (newState == GhostState.HuntChase && chaseAudio != null && audioSource != null)
        {
            if (!audioSource.isPlaying) audioSource.PlayOneShot(chaseAudio);
        }
    }

    private void UpdateVisuals(bool manifested)
    {
        if (ghostSprite != null)
        {
            bool devOutline = isDevOutlineVisible.Value;
            ghostSprite.enabled = manifested || devOutline;
            
            if (manifested)
            {
                ghostSprite.color = Color.white;
            }
            else if (devOutline)
            {
                ghostSprite.color = new Color(1f, 0f, 0f, 0.4f); // Semi-transparent red outline
            }
        }

        if (ghostAuraLight != null)
        {
            ghostAuraLight.enabled = manifested;
        }
    }

    private void UpdateDotsVisuals(bool visible)
    {
        if (dotsSilhouetteSprite != null)
        {
            dotsSilhouetteSprite.enabled = visible;
            
            if (visible)
            {
                // Make the silhouette translucent
                Color c = dotsSilhouetteSprite.color;
                c.a = 0.5f;
                dotsSilhouetteSprite.color = c;
            }

            // Ensure it has a ShadowCaster2D to cast a shadow when revealed by DOTS
            var shadowCaster = dotsSilhouetteSprite.GetComponent<UnityEngine.Rendering.Universal.ShadowCaster2D>();
            if (shadowCaster == null)
            {
                shadowCaster = dotsSilhouetteSprite.gameObject.AddComponent<UnityEngine.Rendering.Universal.ShadowCaster2D>();
                shadowCaster.castsShadows = true;
                // You can configure other shadow caster properties here if necessary (e.g. selfShadows)
            }
            
            // Only cast shadows if the sprite is actually visible from the DOTS
            if (shadowCaster != null)
            {
                shadowCaster.castsShadows = visible;
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Visualize favorite room roam radius
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(favoriteRoomCenter, roamRadius);

        // Visualize vision cone
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, proximityRadius);
        Gizmos.DrawRay(transform.position, Quaternion.Euler(0, 0, visionConeAngle * 0.5f) * transform.up * visionDistance);
        Gizmos.DrawRay(transform.position, Quaternion.Euler(0, 0, -visionConeAngle * 0.5f) * transform.up * visionDistance);
    }
}
