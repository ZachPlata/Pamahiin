using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class HunterGhostBehavior : NetworkBehaviour
{
    [Header("Dependencies")]
    public GhostHandler ghostHandler; // The Group Leader

    [Header("Hunt Settings")]
    public float moveSpeed = 0.5f;
    public float chaseSpeed = 0.6f;
    public int maxNodesBeforeReturn = 8; // Kept for inspector compatibility
    [SerializeField] private float agentAcceleration = 6.0f;
    [SerializeField] private float agentStoppingDistance = 0.15f;

    [Header("LOS Settings")]
    public float lineOfSightDistance = 8f;
    public float lineOfSightAngle = 90f;
    public LayerMask obstacleLayer; // Kept for inspector compatibility

    [Header("Door & Cabinet Interaction")]
    [Tooltip("Distance at which the ghost reaches and forces open a target cabinet door.")]
    [SerializeField] private float doorOpenReach = 2.0f;

    [Header("Combat & Kill")]
    [Tooltip("Range within which the ghost can kill the player (sufficient to reach players inside cabinets/cover).")]
    [SerializeField] private float killDistance = 0.3f;

    private Rigidbody2D rb;
    private NavMeshAgent agent;
    private GhostNode targetNode;
    private int nodesVisitedCount = 0;

    private PlayerController chaseTarget;
    private Vector3 lastKnownPlayerPos;

    // Targeted hiding spot tracking:
    // Only set when the ghost has UNBROKEN LOS and sees the player close the cabinet door,
    // OR when the player has active equipment powered on inside the cabinet.
    private NetworkDoor targetedHidingDoor = null;

    [Header("Chase Memory & Tracking")]
    [Tooltip("How long the ghost remembers and pursues the player's last known location after losing direct sight.")]
    [SerializeField] private float chaseMemoryDuration = 3.0f;
    private float chaseLostTimer = 0f;
    private bool isTrackingEquipment = false;

    private float activeEquipmentScanTimer = 0f;

    private Queue<GhostNode> recentNodes = new Queue<GhostNode>();
    private int historySize = 3;

    // Breadcrumb cache for chasing (Kept for backwards compatibility with Dev Console)
    public static bool ShowBreadcrumbs = false;
    private LineRenderer breadcrumbLine;

    [Header("Exorcism & Angered State")]
    private bool isAngeredState = false;
    private bool isRepelled = false;
    private float repelTimer = 0f;
    public bool IsAngeredState => isAngeredState;
    public bool IsRepelled => isRepelled;

    [Header("Exorcism Death Visuals & Audio")]
    [Tooltip("Optional custom horizontal death sprite sheet overriding Resources/Sprites/ghost_death_sheet.")]
    [SerializeField] private Texture2D customDeathSpriteSheet = null;
    [Tooltip("Optional explicit array of death animation sprites overriding the sprite sheet.")]
    [SerializeField] private Sprite[] customDeathFrames = null;
    [SerializeField] private int deathFrameCount = 12;
    [SerializeField] private float deathAnimationDuration = 2.8f;
    [Tooltip("Optional custom AudioClip overriding Resources/Audio/ghost_dying_shriek.")]
    [SerializeField] private AudioClip customDyingShriekClip = null;

    private Sprite[] cachedDeathFrames;
    private Texture2D cachedDeathSheetSource;

    // Network synchronization & visuals
    private NetworkVariable<Vector2> netPosition = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> netRotation = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Light2D ghostLight;
    private SpriteRenderer cachedSpriteRenderer;
    private bool isExecutingKill = false;
    private Coroutine killSequenceCoroutine;
    private bool isTraversingOffMeshLink = false;
    private float targetReevalTimer = 0f;
    private readonly List<PlayerController> targetCandidates = new List<PlayerController>();

    public static HunterGhostBehavior Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        cachedSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        // Return to Kinematic so NavMeshAgent can move the transform natively without physics corner-snags
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        // Ensure ghost object is not accidentally marked as a Wall layer (which blocks its own LOS and raycasts)
        int wallLayer = LayerMask.NameToLayer("Walls");
        if (wallLayer >= 0 && gameObject.layer == wallLayer)
        {
            int defaultLayer = LayerMask.NameToLayer("Default");
            gameObject.layer = (defaultLayer >= 0) ? defaultLayer : 0;
        }

        if (ghostHandler == null) ghostHandler = GetComponentInParent<GhostHandler>();
        if (ghostHandler != null && wallLayer >= 0 && ghostHandler.gameObject.layer == wallLayer)
        {
            int defaultLayer = LayerMask.NameToLayer("Default");
            ghostHandler.gameObject.layer = (defaultLayer >= 0) ? defaultLayer : 0;
        }

        // Defer NavMeshAgent creation until OnHuntStarted() on server so adding/enabling an agent
        // never fires "Failed to create agent because it is not close enough to the NavMesh" while
        // dormant or before NavMeshSurface initializes in Hierarchy order.
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            ConfigureNavMeshAgent(agent);
            agent.enabled = false;
        }

        // Setup dummy LineRenderer for backwards compatibility with Dev Console Button
        breadcrumbLine = gameObject.AddComponent<LineRenderer>();
        breadcrumbLine.enabled = false;

        EnsureGhostLight();
    }

    private void EnsureGhostLight()
    {
        ghostLight = GetComponentInChildren<Light2D>();
        if (ghostLight == null)
        {
            var lightGo = new GameObject("GhostAuraLight");
            lightGo.transform.SetParent(transform, false);
            ghostLight = lightGo.AddComponent<Light2D>();
            ghostLight.lightType = Light2D.LightType.Point;
            ghostLight.color = new Color(0.95f, 0.25f, 0.25f, 1f); // Menacing dim crimson ghostly aura
            ghostLight.pointLightOuterRadius = 3.5f;
            ghostLight.pointLightInnerRadius = 0.5f;
            ghostLight.intensity = 1.0f;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer && agent != null)
        {
            agent.enabled = false; // Only server runs NavMeshAgent; clients lerp transform
        }
    }

    private void ConfigureNavMeshAgent(NavMeshAgent targetAgent)
    {
        if (targetAgent == null) return;
        targetAgent.updatePosition = true; // Let NavMeshAgent perfectly move the transform
        targetAgent.updateRotation = false; // We handle rotation manually for vision cone accuracy
        targetAgent.updateUpAxis = false;   // Essential for 2D NavMesh (XY plane)
        targetAgent.areaMask = 1 << 0;      // Walkable interior NavMesh area only
        targetAgent.acceleration = agentAcceleration;
        targetAgent.stoppingDistance = agentStoppingDistance;
        targetAgent.autoBraking = false;
        targetAgent.autoTraverseOffMeshLink = false;
    }

    private const int WalkableAreaMask = 1 << 0;

    private static bool TryFindNearestNavMeshPoint(Vector3 origin, out NavMeshHit hit)
    {
        if (NavMesh.SamplePosition(origin, out hit, 3.0f, WalkableAreaMask)) return true;
        if (NavMesh.SamplePosition(origin, out hit, 12.0f, WalkableAreaMask)) return true;
        return NavMesh.SamplePosition(origin, out hit, 30.0f, WalkableAreaMask);
    }

    private bool EnsureNavMeshAgentReady()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            return true;
        }

        if (!TryFindNearestNavMeshPoint(transform.position, out NavMeshHit hit))
        {
            return false;
        }

        // Snap transform onto the baked NavMesh BEFORE adding/enabling NavMeshAgent.
        transform.position = new Vector3(hit.position.x, hit.position.y, transform.position.z);

        if (agent == null)
        {
            agent = gameObject.AddComponent<NavMeshAgent>();
        }

        ConfigureNavMeshAgent(agent);
        if (!agent.enabled)
        {
            agent.enabled = true;
        }
        if (!agent.isOnNavMesh)
        {
            agent.Warp(hit.position);
        }

        return agent.enabled && agent.isOnNavMesh;
    }

    public void OnHuntStarted()
    {
        if (!IsServer) return;
        Debug.Log("HunterGhost: OnHuntStarted called. NavMesh Activated.");

        if (!EnsureNavMeshAgentReady())
        {
            Debug.LogWarning("HunterGhost: FAILED to snap to NavMesh! Ensure the room marker is on a baked area.");
            return;
        }

        agent.isStopped = false;

        nodesVisitedCount = 0;
        isExecutingKill = false;
        isTraversingOffMeshLink = false;
        chaseTarget = null;
        targetedHidingDoor = null;
        isTrackingEquipment = false;
        chaseLostTimer = 0f;
        targetReevalTimer = 0f;
        recentNodes.Clear();
        targetNode = FindNearestNode(transform.position);
    }

    /// <summary>
    /// Resets all pursuit, kill-sequence, off-mesh traversal, and pathing state when a hunt finishes.
    /// </summary>
    public void OnHuntEnded()
    {
        if (killSequenceCoroutine != null)
        {
            StopCoroutine(killSequenceCoroutine);
            killSequenceCoroutine = null;
        }

        isExecutingKill = false;
        isTraversingOffMeshLink = false;
        isAngeredState = false;
        isRepelled = false;
        repelTimer = 0f;
        chaseTarget = null;
        targetedHidingDoor = null;
        isTrackingEquipment = false;
        chaseLostTimer = 0f;
        targetReevalTimer = 0f;
        nodesVisitedCount = 0;
        recentNodes.Clear();

        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = Color.white;
        }

        if (agent != null && agent.isOnNavMesh)
        {
            if (agent.isOnOffMeshLink) agent.CompleteOffMeshLink();
            agent.ResetPath();
            agent.isStopped = false;
            agent.velocity = Vector3.zero;
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (ghostLight != null)
        {
            if (cachedSpriteRenderer == null)
            {
                cachedSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }
            bool shouldLightBeOn = cachedSpriteRenderer != null && cachedSpriteRenderer.enabled;
            if (ghostLight.enabled != shouldLightBeOn)
            {
                ghostLight.enabled = shouldLightBeOn;
            }
        }

        // Client-side transform interpolation so clients can see the ghost moving during hunt
        if (!IsServer && IsSpawned)
        {
            if (netPosition.Value != Vector2.zero)
            {
                transform.position = Vector3.Lerp(transform.position, new Vector3(netPosition.Value.x, netPosition.Value.y, transform.position.z), Time.deltaTime * 18f);
                transform.rotation = Quaternion.Euler(0f, 0f, netRotation.Value);
            }
        }

        if (isRepelled)
        {
            repelTimer -= Time.deltaTime;
            if (repelTimer <= 0f)
            {
                isRepelled = false;
                Debug.Log("HunterGhost: Crucifix repel effect has expired!");
            }
        }

        if (IsServer)
        {
            if (isExecutingKill) return;

            if (ghostHandler != null && ghostHandler.CurrentState != GhostHandlerState.Dormant)
            {
                if (agent != null && agent.isOnNavMesh && agent.isOnOffMeshLink)
                {
                    if (!isTraversingOffMeshLink)
                    {
                        StartCoroutine(TraverseOffMeshLinkRoutine());
                    }
                    return;
                }

                // Sync NavMeshAgent speed (Angered state moves much faster!)
                float currentBaseSpeed = (ghostHandler.CurrentState == GhostHandlerState.Chasing) ? chaseSpeed : moveSpeed;
                if (isAngeredState)
                {
                    currentBaseSpeed *= 2.2f;
                }
                if (agent != null && agent.enabled)
                {
                    agent.speed = isRepelled ? (moveSpeed * 0.5f) : currentBaseSpeed;
                }

                if (ghostHandler.CurrentState == GhostHandlerState.Chasing)
                {
                    UpdateChasing();
                }
                else if (ghostHandler.CurrentState == GhostHandlerState.Searching || ghostHandler.CurrentState == GhostHandlerState.Returning)
                {
                    UpdateNodePathfinding();
                }

                // Scan for players & check door obstructions
                CheckLineOfSight();
                CheckDoorObstructions();
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer || !IsSpawned) return;

        // Sync position and rotation over network for all clients
        netPosition.Value = transform.position;
        netRotation.Value = rb.rotation;

        // Manually rotate the ghost to face its velocity (NavMeshAgent velocity)
        if (agent != null && agent.velocity.sqrMagnitude > 0.01f)
        {
            float targetAngle = Mathf.Atan2(agent.velocity.y, agent.velocity.x) * Mathf.Rad2Deg - 90f;
            rb.MoveRotation(Mathf.LerpAngle(rb.rotation, targetAngle, Time.fixedDeltaTime * 12f));
        }
    }

    private void UpdateChasing()
    {
        LayerMask combinedMask = GetCombinedObstacleMask();

        // Always check if the ghost has reached a cabinet door concealing a player
        CheckCabinetDoorInteractions();

        // Following the player up/down stairs: walk to the stair, then warp to the other floor
        if (stairFollowSource != null)
        {
            UpdateStairFollow();
            return;
        }

        if (chaseTarget != null && chaseTarget.IsAlive && !IsTargetInsideAndReachable(chaseTarget))
        {
            if (TryBeginStairFollow(chaseTarget))
            {
                return;
            }
            Debug.Log("HunterGhost: Target left the house / is unreachable. Dropping chase.");
            DropChaseTargetOutdoors();
        }

        if (chaseTarget != null && chaseTarget.IsAlive)
        {
            Vector2 dirToPlayer = chaseTarget.transform.position - (Vector3)transform.position;
            float distance = dirToPlayer.magnitude;

            bool playerHasActiveEquip = IsPlayerEquipmentActive(chaseTarget);
            bool hasVisualLOS = CheckHasVisualLOS(chaseTarget, distance, combinedMask);

            if (hasVisualLOS || playerHasActiveEquip)
            {
                isTrackingEquipment = playerHasActiveEquip;
                lastKnownPlayerPos = chaseTarget.transform.position;
                chaseLostTimer = chaseMemoryDuration;
                SetAgentDestination(chaseTarget.transform.position);
            }
            else
            {
                // Visual LOS broken & no active equipment -> follow to last known position with grace timer
                chaseLostTimer -= Time.deltaTime;
                SetAgentDestination(lastKnownPlayerPos);

                if (chaseLostTimer <= 0f)
                {
                    Debug.Log("HunterGhost: Lost sight of player and memory expired. Returning to last known position.");
                    chaseTarget = null;
                    targetedHidingDoor = null;
                    isTrackingEquipment = false;
                }
            }

            // Kill check (reaches players in open rooms as well as players inside opened cabinets)
            if (isRepelled)
            {
                // Cannot kill during crucifix repel window!
                return;
            }

            if (chaseTarget != null && distance <= killDistance)
            {
                bool canKill = CheckHasVisualLOS(chaseTarget, distance, combinedMask);

                if (canKill && !isExecutingKill)
                {
                    if (chaseTarget.KillPlayer())
                    {
                        NotifyPlayerCaughtClientRpc(chaseTarget.OwnerClientId);
                        chaseTarget = null;
                        targetedHidingDoor = null;
                        isTrackingEquipment = false;
                        if (killSequenceCoroutine != null) StopCoroutine(killSequenceCoroutine);
                        killSequenceCoroutine = StartCoroutine(KillSequenceRoutine());
                        return;
                    }
                }
            }
        }
        else
        {
            // ChaseTarget is null: Navigate to last known player position (only if reachable inside the house)
            if (!SetAgentDestination(lastKnownPlayerPos))
            {
                lastKnownPlayerPos = transform.position;
            }

            float distToLastKnown = Vector2.Distance(transform.position, lastKnownPlayerPos);
            if (distToLastKnown < 0.5f)
            {
                Debug.Log("HunterGhost: Reached last known position. Returning to search.");
                recentNodes.Clear();
                targetNode = FindNearestNode(transform.position);
                ghostHandler.RequestStateChange(GhostHandlerState.Searching);
            }
        }
    }

    private void UpdateNodePathfinding()
    {
        if (targetNode == null)
        {
            targetNode = FindNearestNode(transform.position);
            if (targetNode == null) return;
        }

        SetAgentDestination(targetNode.transform.position);

        if (Vector2.Distance(transform.position, targetNode.transform.position) < 0.5f || (agent.hasPath && agent.remainingDistance < 0.5f))
        {
            nodesVisitedCount++;

            if (!recentNodes.Contains(targetNode))
            {
                recentNodes.Enqueue(targetNode);
                if (recentNodes.Count > historySize) recentNodes.Dequeue();
            }

            if (ghostHandler != null && ghostHandler.ghostroomMarker != null && ghostHandler.ghostroomMarker.bounds.Contains(transform.position))
            {
                nodesVisitedCount = 0;
                if (ghostHandler.CurrentState == GhostHandlerState.Returning)
                {
                    ghostHandler.RequestStateChange(GhostHandlerState.Searching);
                }
            }

            if (nodesVisitedCount > maxNodesBeforeReturn && ghostHandler != null && ghostHandler.ghostroomMarker != null)
            {
                if (ghostHandler.CurrentState == GhostHandlerState.Searching)
                {
                    ghostHandler.RequestStateChange(GhostHandlerState.Returning);
                }
                targetNode = GetBestNodeTowards(ghostHandler.ghostroomMarker.bounds.center, targetNode);
            }
            else
            {
                targetNode = GetRandomNeighbor(targetNode);
            }
        }
    }

    private PlayerController PickClosestCandidate(List<PlayerController> candidates, Vector3 origin)
    {
        if (candidates == null || candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        float minDist = float.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            float d = Vector2.Distance(origin, candidates[i].transform.position);
            if (d < minDist) minDist = d;
        }

        // Gather candidates tied within tieEpsilon so co-located players get random selection
        float tieEpsilon = 0.15f;
        int bestCount = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            float d = Vector2.Distance(origin, candidates[i].transform.position);
            if (d <= minDist + tieEpsilon)
            {
                candidates[bestCount++] = candidates[i];
            }
        }

        return candidates[Random.Range(0, bestCount)];
    }

    private void AcquireChaseTarget(PlayerController target, bool viaEquipment)
    {
        if (target == null) return;

        if (chaseTarget == null)
        {
            Debug.Log(viaEquipment
                ? $"HunterGhost: Detected active equipment on player '{target.name}'! Chasing!"
                : "HunterGhost: I SEE THE PLAYER! Chasing!");
        }
        else if (chaseTarget != target)
        {
            Debug.Log($"HunterGhost: Switching target to closer player '{target.name}'.");
        }

        if (viaEquipment)
        {
            // Check if player is hiding inside or near a closed cabinet/closet
            var allDoors = Object.FindObjectsByType<NetworkDoor>(FindObjectsInactive.Exclude);
            foreach (var d in allDoors)
            {
                if (d != null && !d.IsFrontDoor && d.IsHidingSpot && !d.IsOpen)
                {
                    if (Vector2.Distance(target.transform.position, d.transform.position) <= 1.5f)
                    {
                        targetedHidingDoor = d;
                        break;
                    }
                }
            }
        }

        chaseTarget = target;
        lastKnownPlayerPos = target.transform.position;
        chaseLostTimer = chaseMemoryDuration;
        isTrackingEquipment = viaEquipment;
        nodesVisitedCount = 0;
        SetAgentDestination(target.transform.position);
        ghostHandler.RequestStateChange(GhostHandlerState.Chasing);
    }

    private void CheckLineOfSight()
    {
        if (isExecutingKill) return;
        LayerMask combinedMask = GetCombinedObstacleMask();

        float currentTargetDist = (chaseTarget != null && chaseTarget.IsAlive)
            ? Vector2.Distance(transform.position, chaseTarget.transform.position)
            : float.MaxValue;

        targetReevalTimer -= Time.deltaTime;
        bool shouldReevalCurrentTarget = chaseTarget == null || !chaseTarget.IsAlive || targetReevalTimer <= 0f;

        // 1. Scan ALL alive players for ACTIVE GHOST EQUIPMENT in hand (proximity-weighted)
        activeEquipmentScanTimer -= Time.deltaTime;
        if (activeEquipmentScanTimer <= 0f)
        {
            activeEquipmentScanTimer = 0.45f;
            targetCandidates.Clear();
            float detectRadius = lineOfSightDistance * 1.5f;

            foreach (var player in PlayerController.AllPlayers)
            {
                if (player != null && player.IsAlive && IsPlayerEquipmentActive(player) && IsTargetInsideAndReachable(player))
                {
                    float dist = Vector2.Distance(transform.position, player.transform.position);
                    if (dist <= detectRadius)
                    {
                        targetCandidates.Add(player);
                    }
                }
            }

            if (targetCandidates.Count > 0)
            {
                PlayerController chosen = PickClosestCandidate(targetCandidates, transform.position);
                float chosenDist = Vector2.Distance(transform.position, chosen.transform.position);
                if (chaseTarget == null || !chaseTarget.IsAlive || (shouldReevalCurrentTarget && chosenDist < currentTargetDist - 0.15f))
                {
                    targetReevalTimer = 0.5f;
                    AcquireChaseTarget(chosen, true);
                    return;
                }
            }
        }

        // 2. Visual Line of Sight scan across ALL alive players (proximity-weighted)
        targetCandidates.Clear();
        float currentLOS = (ghostHandler.CurrentState == GhostHandlerState.Chasing)
            ? lineOfSightDistance * 1.5f
            : lineOfSightDistance;

        foreach (var player in PlayerController.AllPlayers)
        {
            if (player == null || !player.IsAlive || player.IsHiddenFromGhost) continue;
            if (!IsTargetInsideAndReachable(player)) continue; // Ignore players outside the house

            float distance = Vector2.Distance(transform.position, player.transform.position);
            if (distance <= currentLOS && CheckHasVisualLOS(player, distance, combinedMask))
            {
                targetCandidates.Add(player);
            }
        }

        if (targetCandidates.Count > 0)
        {
            PlayerController chosen = PickClosestCandidate(targetCandidates, transform.position);
            float chosenDist = Vector2.Distance(transform.position, chosen.transform.position);
            if (chaseTarget == null || !chaseTarget.IsAlive || (shouldReevalCurrentTarget && chosenDist < currentTargetDist - 0.15f))
            {
                targetReevalTimer = 0.5f;
                AcquireChaseTarget(chosen, false);
            }
        }
    }

    private bool CheckHasVisualLOS(PlayerController player, float distance, LayerMask combinedMask)
    {
        if (player == null || !player.IsAlive) return false;
        if (player.IsHiddenFromGhost) return false;
        if (distance > lineOfSightDistance * 1.5f) return false;
        if (distance < 0.05f) return true;

        Vector2 origin = transform.position;
        Vector2 target = player.transform.position;
        Vector2 dir = (target - origin).normalized;

        // Cast through all colliders along the sightline to filter out non-blocking objects
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, distance, combinedMask);

        // Sort by distance from ghost to evaluate obstructions sequentially
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;

            // 1. Ignore ghost's own colliders and parent hierarchy
            if (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform) ||
                (ghostHandler != null && hit.collider.transform.IsChildOf(ghostHandler.transform)))
            {
                continue;
            }

            // 2. Ignore player's colliders and items in player's hands
            if (hit.collider.gameObject == player.gameObject || hit.collider.transform.IsChildOf(player.transform))
            {
                continue;
            }

            // 3. Ignore trigger volumes (safe zones, room markers, light switch triggers, item pickup triggers, etc.)
            if (hit.collider.isTrigger)
            {
                continue;
            }

            // 4. Ignore equipment items on the floor (not opaque obstacles)
            if (hit.collider.GetComponent<EquipmentItem>() != null || hit.collider.GetComponentInParent<EquipmentItem>() != null)
            {
                continue;
            }

            // 5. Handle doors
            var door = GetDoorFromCollider(hit.collider);
            if (door != null)
            {
                if (door.IsOpen)
                {
                    // Open doors do not block line of sight
                    continue;
                }

                // Check if this closed door is a hiding spot concealing the player
                if (!door.IsFrontDoor && door.IsHidingSpot)
                {
                    if (Vector2.Distance(player.transform.position, door.transform.position) <= 1.5f)
                    {
                        targetedHidingDoor = door;
                        return true;
                    }
                }

                // Any other closed door blocks line of sight
                return false;
            }

            // 6. Solid obstacle (walls, physical room obstacles) blocks line of sight
            return false;
        }

        // Sightline is completely clear!
        return true;
    }

    private bool IsPlayerEquipmentActive(PlayerController player)
    {
        if (player == null || !player.IsAlive) return false;
        var inventory = player.GetComponent<PlayerInventory>();
        return inventory != null && inventory.CurrentItem != null && inventory.CurrentItem.IsPoweredOn;
    }

    /// <summary>
    /// Sets the agent destination only if a COMPLETE NavMesh path exists from the ghost.
    /// Returns false when the target is unreachable (e.g. outside the house past the Not Walkable
    /// FrontDoor seam), true when the destination was set or the agent isn't ready yet.
    /// </summary>
    private bool SetAgentDestination(Vector3 targetPos)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return true;
        targetPos.z = transform.position.z;
        if (!TryFindNearestNavMeshPoint(targetPos, out NavMeshHit hit)) return false;

        if (reachPath == null) reachPath = new NavMeshPath();
        if (!agent.CalculatePath(hit.position, reachPath) || reachPath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        agent.SetPath(reachPath);
        return true;
    }

    // ── Indoor containment (ghost never pursues players outside the house) ────────────────

    private NavMeshPath reachPath;
    private readonly Dictionary<PlayerController, KeyValuePair<float, bool>> reachabilityCache =
        new Dictionary<PlayerController, KeyValuePair<float, bool>>();
    private RoofFader[] enclosedRoofs;

    private bool IsInsideEnclosedRoof(Vector2 pos, out bool hasEnclosedRoof)
    {
        if (enclosedRoofs == null)
        {
            var all = Object.FindObjectsByType<RoofFader>(FindObjectsInactive.Include);
            var list = new List<RoofFader>();
            foreach (var r in all)
            {
                if (r != null && r.IsEnclosedBuilding) list.Add(r);
            }
            enclosedRoofs = list.ToArray();
        }

        hasEnclosedRoof = enclosedRoofs.Length > 0;
        foreach (var r in enclosedRoofs)
        {
            if (r != null && r.IsPositionInside(pos)) return true;
        }
        return false;
    }

    /// <summary>
    /// True when the player is inside the house and the ghost has a complete NavMesh path to them.
    /// Results are cached briefly to keep per-frame scans cheap.
    /// </summary>
    private bool IsTargetInsideAndReachable(PlayerController player)
    {
        if (player == null) return false;

        Vector3 targetPos = player.transform.position;
        if (!GameAudioManager.IsBasementPosition(targetPos))
        {
            bool inRoof = IsInsideEnclosedRoof(targetPos, out bool hasRoof);
            if (hasRoof && !inRoof)
            {
                reachabilityCache[player] = new KeyValuePair<float, bool>(Time.time + 0.1f, false);
                return false;
            }
        }

        if (reachabilityCache.TryGetValue(player, out var cached) && Time.time < cached.Key)
        {
            return cached.Value;
        }

        bool result = ComputeInsideAndReachable(targetPos);
        reachabilityCache[player] = new KeyValuePair<float, bool>(Time.time + 0.1f, result);
        return result;
    }

    private bool ComputeInsideAndReachable(Vector3 targetPos)
    {
        targetPos.z = transform.position.z;

        if (!GameAudioManager.IsBasementPosition(targetPos))
        {
            bool inRoof = IsInsideEnclosedRoof(targetPos, out bool hasRoof);
            if (hasRoof && !inRoof)
            {
                return false;
            }
        }

        // Fallback when the agent isn't ready: roof/basement containment alone suffices
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return true;
        }

        int mask = agent.areaMask != 0 ? (agent.areaMask & WalkableAreaMask) : WalkableAreaMask;
        if (mask == 0) mask = WalkableAreaMask;
        if (!NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 0.35f, mask)) return false;

        if (reachPath == null) reachPath = new NavMeshPath();
        return agent.CalculatePath(hit.position, reachPath) && reachPath.status == NavMeshPathStatus.PathComplete;
    }

    // ── Stair Following ──────────────────────────────────────────────────

    private StairTeleporter stairFollowSource;
    private float stairFollowTimer;
    private const float StairFollowTimeout = 12f;

    /// <summary>
    /// If the (formerly chased) player just arrived next to a stair teleporter's arrival point,
    /// the ghost heads to the matching stair on its own floor to follow them.
    /// </summary>
    private bool TryBeginStairFollow(PlayerController player)
    {
        if (player == null) return false;
        Vector2 playerPos = player.transform.position;
        float maxStairDist = lineOfSightDistance * 2.5f;

        foreach (var source in StairTeleporter.All)
        {
            if (source == null || source.Destination == null) continue;
            if (Vector2.Distance(playerPos, source.Destination.GetArrivalPosition()) > 3.5f) continue;
            if (Vector2.Distance(transform.position, source.transform.position) > maxStairDist) continue;

            stairFollowSource = source;
            stairFollowTimer = StairFollowTimeout;
            Debug.Log("HunterGhost: Player took the stairs! Following...");
            SetAgentDestination(source.transform.position);
            return true;
        }
        return false;
    }

    private void UpdateStairFollow()
    {
        if (chaseTarget == null || !chaseTarget.IsAlive || stairFollowSource == null)
        {
            stairFollowSource = null;
            return;
        }

        stairFollowTimer -= Time.deltaTime;
        Vector2 stairPos = stairFollowSource.transform.position;
        SetAgentDestination(stairPos);

        if (Vector2.Distance(transform.position, stairPos) <= 1.0f)
        {
            Vector3 dest = stairFollowSource.Destination.GetArrivalPosition();
            dest.z = transform.position.z;
            if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 1.0f, WalkableAreaMask) && agent != null && agent.enabled)
            {
                agent.Warp(hit.position);
            }
            else
            {
                transform.position = dest;
            }

            reachabilityCache.Clear();
            stairFollowSource = null;
            Debug.Log("HunterGhost: Followed the player through the stairs.");
            AcquireChaseTarget(chaseTarget, false);
        }
        else if (stairFollowTimer <= 0f)
        {
            stairFollowSource = null;
            DropChaseTargetOutdoors();
        }
    }

    private void DropChaseTargetOutdoors()
    {
        stairFollowSource = null;
        chaseTarget = null;
        targetedHidingDoor = null;
        isTrackingEquipment = false;
        chaseLostTimer = 0f;
        // Forget any last known position outside the house so the ghost returns to searching inside
        lastKnownPlayerPos = transform.position;
    }

    // ── Door & Cabinet Handling ────────────────────────────────────────────

    /// <summary>
    /// Forces a door open, and also opens any adjacent partner doors (such as double cabinet/closet doors).
    /// Only called when the ghost is SURE a player is inside (unbroken LOS or active equipment).
    /// </summary>
    public void OpenDoorAndConnected(NetworkDoor targetDoor)
    {
        if (targetDoor == null || targetDoor.IsFrontDoor) return;

        Debug.Log($"HunterGhost: Ghost forcing open cabinet door '{targetDoor.name}'!");
        targetDoor.GhostInteract(forceOpen: true);

        // If this door is part of a double cabinet (e.g. Cabinet 1 Door 1 & Door 2), open the partner door as well
        var allDoors = Object.FindObjectsByType<NetworkDoor>(FindObjectsInactive.Exclude);
        foreach (var other in allDoors)
        {
            if (other != null && other != targetDoor && !other.IsFrontDoor && !other.IsLocked)
            {
                // Double cabinet doors are located within ~1.2m of each other
                if (Vector2.Distance(targetDoor.transform.position, other.transform.position) <= 1.2f)
                {
                    Debug.Log($"HunterGhost: Opening partner cabinet door '{other.name}'!");
                    other.GhostInteract(forceOpen: true);
                }
            }
        }
    }

    /// <summary>
    /// Checks if the ghost is in reach of a closed cabinet door concealing a player during a chase.
    /// If the ghost tracked the player to this cabinet, sees them enter, or detects active electronics inside,
    /// it rips the cabinet door open.
    /// </summary>
    private void CheckCabinetDoorInteractions()
    {
        // 1. If we have a targeted hiding door, force it open as soon as the ghost is in reach
        if (targetedHidingDoor != null)
        {
            float distToTargetDoor = Vector2.Distance(transform.position, targetedHidingDoor.transform.position);
            if (distToTargetDoor <= doorOpenReach)
            {
                Debug.Log($"HunterGhost: Arrived at targeted cabinet door '{targetedHidingDoor.name}'! Forcing it open!");
                OpenDoorAndConnected(targetedHidingDoor);
                targetedHidingDoor = null;
                return;
            }
        }

        // 2. Scan all doors in reach of the ghost to see if a player is hiding behind one
        LayerMask combinedMask = GetCombinedObstacleMask();
        Collider2D[] nearbyCols = Physics2D.OverlapCircleAll(transform.position, doorOpenReach, combinedMask);
        foreach (var col in nearbyCols)
        {
            var door = GetDoorFromCollider(col);
            if (door != null && !door.IsFrontDoor && door.IsHidingSpot && !door.IsOpen)
            {
                bool playerHidingBehindDoor = false;

                // Check living players near this cabinet door
                foreach (var p in PlayerController.AllPlayers)
                {
                    if (p != null && p.IsAlive)
                    {
                        float distPlayerToDoor = Vector2.Distance(p.transform.position, door.transform.position);
                        if (distPlayerToDoor <= 1.5f && IsTargetInsideAndReachable(p))
                        {
                            // A) Player has active equipment powered on inside the cabinet:
                            if (IsPlayerEquipmentActive(p))
                            {
                                playerHidingBehindDoor = true;
                                chaseTarget = p;
                                lastKnownPlayerPos = p.transform.position;
                                break;
                            }

                            // B) Ghost was actively chasing this player or memory timer is active:
                            if (chaseTarget == p || chaseLostTimer > 0f)
                            {
                                playerHidingBehindDoor = true;
                                chaseTarget = p;
                                lastKnownPlayerPos = p.transform.position;
                                break;
                            }
                        }
                    }
                }

                // C) Check if last known position was inside / right at this cabinet door
                if (!playerHidingBehindDoor && Vector2.Distance(lastKnownPlayerPos, door.transform.position) <= 1.5f)
                {
                    if (chaseTarget != null || chaseLostTimer > 0f)
                    {
                        playerHidingBehindDoor = true;
                    }
                }

                if (playerHidingBehindDoor)
                {
                    Debug.Log($"HunterGhost: Ghost caught player hiding behind cabinet door '{door.name}'! Forcing it open!");
                    OpenDoorAndConnected(door);
                    targetedHidingDoor = null;
                    return;
                }
            }
        }
    }

    private void CheckDoorObstructions()
    {
        // If a hunting ghost encounters an unlocked non-hiding interior door blocking its path, push it open
        LayerMask combinedMask = GetCombinedObstacleMask();
        Collider2D[] nearbyDoors = Physics2D.OverlapCircleAll(transform.position, 1.5f, combinedMask);
        foreach (var col in nearbyDoors)
        {
            var door = GetDoorFromCollider(col);
            if (door != null && !door.IsFrontDoor && !door.IsHidingSpot && !door.IsOpen && !door.IsLocked)
            {
                Debug.Log($"HunterGhost: Ghost pushed open interior door '{door.name}'!");
                door.GhostInteract(forceOpen: true);
            }
        }
    }

    private NetworkDoor GetDoorFromCollider(Collider2D col)
    {
        if (col == null) return null;
        var proxy = col.GetComponent<DoorInteractionProxy>() ?? col.GetComponentInParent<DoorInteractionProxy>();
        if (proxy != null && proxy.TargetDoor != null) return proxy.TargetDoor;
        return col.GetComponentInParent<NetworkDoor>() ?? col.GetComponent<NetworkDoor>();
    }

    private LayerMask GetCombinedObstacleMask()
    {
        LayerMask mask = obstacleLayer;
        int interactableLayer = LayerMask.NameToLayer("Interactable");
        if (interactableLayer >= 0)
        {
            mask |= (1 << interactableLayer);
        }
        return mask;
    }

    // ── Pathfinding Helpers ────────────────────────────────────────────────

    private GhostNode FindNearestNode(Vector3 position)
    {
        GhostNode[] allNodes = FindObjectsByType<GhostNode>();
        GhostNode nearest = null;
        float minDist = float.MaxValue;
        foreach (var node in allNodes)
        {
            float dist = Vector2.Distance(position, node.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = node;
            }
        }
        return nearest;
    }

    private GhostNode GetRandomNeighbor(GhostNode current)
    {
        if (current.neighbors == null || current.neighbors.Count == 0) return current;

        List<GhostNode> validNeighbors = new List<GhostNode>();
        foreach (var neighbor in current.neighbors)
        {
            if (neighbor != null && !recentNodes.Contains(neighbor))
            {
                validNeighbors.Add(neighbor);
            }
        }

        if (validNeighbors.Count == 0)
        {
            foreach (var neighbor in current.neighbors)
            {
                if (neighbor != null) validNeighbors.Add(neighbor);
            }
            recentNodes.Clear();
        }

        if (validNeighbors.Count == 0) return current;

        GhostNode bestNode = validNeighbors[0];
        float bestScore = -float.MaxValue;
        Vector2 currentDir = transform.up;

        foreach (var node in validNeighbors)
        {
            Vector2 dirToNode = (node.transform.position - transform.position).normalized;
            float score = Vector2.Dot(currentDir, dirToNode) + Random.Range(-0.2f, 0.4f);

            if (score > bestScore)
            {
                bestScore = score;
                bestNode = node;
            }
        }

        return bestNode;
    }

    private GhostNode GetBestNodeTowards(Vector3 targetPos, GhostNode current)
    {
        if (current.neighbors == null || current.neighbors.Count == 0) return current;

        List<GhostNode> validNeighbors = new List<GhostNode>();
        foreach (var neighbor in current.neighbors)
        {
            if (neighbor != null && !recentNodes.Contains(neighbor))
            {
                validNeighbors.Add(neighbor);
            }
        }

        if (validNeighbors.Count == 0)
        {
            foreach (var neighbor in current.neighbors)
            {
                if (neighbor != null) validNeighbors.Add(neighbor);
            }
            recentNodes.Clear();
        }

        if (validNeighbors.Count == 0) return current;

        GhostNode bestNode = validNeighbors[0];
        float minDist = Vector2.Distance(bestNode.transform.position, targetPos);

        foreach (var neighbor in validNeighbors)
        {
            float dist = Vector2.Distance(neighbor.transform.position, targetPos);
            if (dist < minDist)
            {
                minDist = dist;
                bestNode = neighbor;
            }
        }

        return bestNode;
    }

    /// <summary>
    /// Sent to all clients; only the caught player's client will act on it.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyPlayerCaughtClientRpc(ulong caughtClientId)
    {
        // Only the local client that owns the caught player reacts
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.LocalClientId == caughtClientId)
        {
            if (GhostAtmosphereController.Instance != null)
                GhostAtmosphereController.Instance.OnPlayerCaught();
        }
    }

    // ── Exorcism Mechanics ──────────────────────────────────────────

    public void EnterAngeredState()
    {
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            EnterAngeredStateRpc();
        }
        else
        {
            EnterAngeredStateLocal();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void EnterAngeredStateRpc()
    {
        EnterAngeredStateLocal();
    }

    private void EnterAngeredStateLocal()
    {
        isAngeredState = true;
        isRepelled = false;
        repelTimer = 0f;
        enabled = true;

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = chaseSpeed * 2.2f;
        }

        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.enabled = true;
            sr.color = new Color(1f, 0.2f, 0.2f, 1f); // Fiery red
            sr.sortingLayerName = "Van";
            sr.sortingOrder = 15;
        }

        if (IsServer && ghostHandler != null)
        {
            ghostHandler.RequestStateChange(GhostHandlerState.Chasing);
        }
    }

    public void ApplyCrucifixRepel(float duration, Vector3 repelSource)
    {
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            ApplyCrucifixRepelRpc(duration, repelSource);
        }
        else
        {
            ApplyCrucifixRepelLocal(duration, repelSource);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ApplyCrucifixRepelRpc(float duration, Vector3 repelSource)
    {
        ApplyCrucifixRepelLocal(duration, repelSource);
    }

    private void ApplyCrucifixRepelLocal(float duration, Vector3 repelSource)
    {
        isRepelled = true;
        repelTimer = duration;

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            // Flee away from repel source
            Vector3 fleeDir = (transform.position - repelSource).normalized;
            if (fleeDir.sqrMagnitude < 0.01f) fleeDir = Random.insideUnitCircle.normalized;
            Vector3 fleeTarget = transform.position + fleeDir * 6f;
            SetAgentDestination(fleeTarget);
        }

        if (ExorcismManager.Instance != null)
        {
            ExorcismManager.Instance.ShowRepelBanner(duration);
        }
    }

    public void StartDyingSequence(AudioClip dyingClip, Vector3? manifestPosition = null)
    {
        gameObject.SetActive(true);
        StartCoroutine(DyingRoutine(dyingClip, manifestPosition));
    }

    private Sprite[] ResolveDeathFrames()
    {
        if (customDeathFrames != null && customDeathFrames.Length > 0)
        {
            return customDeathFrames;
        }

        Texture2D sheet = customDeathSpriteSheet != null
            ? customDeathSpriteSheet
            : Resources.Load<Texture2D>("Sprites/ghost_death_sheet");
        if (sheet == null) return System.Array.Empty<Sprite>();

        if (cachedDeathFrames != null && cachedDeathFrames.Length > 0 && cachedDeathSheetSource == sheet)
        {
            return cachedDeathFrames;
        }

        sheet.filterMode = FilterMode.Point;
        int frameCount = Mathf.Max(1, deathFrameCount);
        int fw = Mathf.Max(1, sheet.width / frameCount);
        int fh = sheet.height;

        cachedDeathFrames = new Sprite[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            cachedDeathFrames[i] = Sprite.Create(
                sheet,
                new Rect(i * fw, 0, fw, fh),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            cachedDeathFrames[i].name = $"ghost_death_f{i}";
        }
        cachedDeathSheetSource = sheet;
        return cachedDeathFrames;
    }

    private System.Collections.IEnumerator DyingRoutine(AudioClip dyingClip, Vector3? manifestPosition)
    {
        enabled = false;
        if (agent != null)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            agent.enabled = false; // Disable NavMeshAgent so it does not fight transform position
        }

        if (manifestPosition.HasValue && manifestPosition.Value != Vector3.zero)
        {
            transform.position = new Vector3(manifestPosition.Value.x, manifestPosition.Value.y, 0f);
        }

        Vector3 originalScale = transform.localScale;
        if (originalScale.sqrMagnitude < 0.01f)
        {
            originalScale = new Vector3(0.266f, 0.266f, 1f);
        }
        transform.localScale = originalScale;

        var pixelAnim = GetComponent<GhostPixelAnimator>();
        pixelAnim?.SetDyingOverride(true);

        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.enabled = true;
            sr.color = Color.white;
            sr.sortingLayerName = "Item";
            sr.sortingOrder = 50; // Render cleanly above diagram, floor, and players
        }

        Sprite[] deathFrames = ResolveDeathFrames();
        if (sr != null && deathFrames.Length > 0 && deathFrames[0] != null)
        {
            sr.sprite = deathFrames[0];
        }

        EnsureGhostLight();
        Color origLightColor = ghostLight != null ? ghostLight.color : new Color(0.95f, 0.25f, 0.25f, 1f);
        float origLightIntensity = ghostLight != null ? ghostLight.intensity : 1.0f;
        float origLightOuterRadius = ghostLight != null ? ghostLight.pointLightOuterRadius : 3.5f;
        if (ghostLight != null)
        {
            ghostLight.enabled = true;
        }

        AudioClip clipToPlay = dyingClip != null
            ? dyingClip
            : (customDyingShriekClip != null
                ? customDyingShriekClip
                : (GameAudioManager.GetClip("ghost_dying_shriek") ?? CreateProceduralShriekClip()));

        if (clipToPlay != null)
        {
            GameAudioManager.PlayClipAt(clipToPlay, transform.position, 1.0f, 0.85f, 1f, 0.9f, 7.0f, 0.45f);
        }

        float duration = Mathf.Max(0.25f, deathAnimationDuration);
        float elapsed = 0f;

        Color shrivelColor = new Color(0.82f, 0.90f, 1.0f, 1f);
        Color radianceColor = new Color(1f, 0.94f, 0.72f, 1f);
        Color emberColor = new Color(1f, 0.35f, 0.08f, 1f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            if (sr != null && deathFrames.Length > 0)
            {
                int frameIdx = Mathf.Clamp(Mathf.FloorToInt(progress * deathFrames.Length), 0, deathFrames.Length - 1);
                if (deathFrames[frameIdx] != null)
                {
                    sr.sprite = deathFrames[frameIdx];
                }

                float alpha = progress > 0.85f ? Mathf.Lerp(1f, 0f, (progress - 0.85f) / 0.15f) : 1f;
                sr.color = new Color(1f, 1f, 1f, alpha);
            }

            if (ghostLight != null)
            {
                if (progress < 0.25f)
                {
                    float t = progress / 0.25f;
                    ghostLight.color = Color.Lerp(origLightColor, shrivelColor, t);
                    ghostLight.intensity = Mathf.Lerp(0.65f, 1.25f, t) + 0.15f * Mathf.Sin(elapsed * 28f);
                    ghostLight.pointLightOuterRadius = Mathf.Lerp(2.0f, 2.6f, t);
                }
                else if (progress < 0.58f)
                {
                    float t = (progress - 0.25f) / 0.33f;
                    float bell = Mathf.Sin(t * Mathf.PI);
                    ghostLight.color = Color.Lerp(shrivelColor, radianceColor, Mathf.Clamp01(t * 2.2f));
                    ghostLight.intensity = Mathf.Lerp(1.4f, 3.8f, bell);
                    ghostLight.pointLightOuterRadius = Mathf.Lerp(2.6f, 4.8f, bell);
                }
                else
                {
                    float t = (progress - 0.58f) / 0.42f;
                    ghostLight.color = Color.Lerp(radianceColor, emberColor, Mathf.Clamp01(t * 1.6f));
                    ghostLight.intensity = Mathf.Lerp(2.0f, 0f, t);
                    ghostLight.pointLightOuterRadius = Mathf.Lerp(3.8f, 0.8f, t);
                }
            }

            yield return null;
        }

        if (ghostLight != null)
        {
            ghostLight.color = origLightColor;
            ghostLight.intensity = origLightIntensity;
            ghostLight.pointLightOuterRadius = origLightOuterRadius;
            ghostLight.enabled = false;
        }

        if (sr != null)
        {
            sr.color = Color.white;
            sr.enabled = false;
        }

        pixelAnim?.SetDyingOverride(false);

        // Notify ghostHandler that the ghost is exorcised so it halts hunts/events,
        // but DO NOT despawn or destroy the Ghost Handler hierarchy so Ghost Type data is preserved.
        if (ghostHandler != null)
        {
            ghostHandler.OnGhostExorcised();
        }

        gameObject.SetActive(false);
    }

    private struct SvfBandpass
    {
        private float low;
        private float band;

        public float Process(float input, float freqHz, float q, int sampleRate)
        {
            float f = Mathf.Clamp(2f * Mathf.Sin(Mathf.PI * freqHz / (sampleRate * 2f)), 0.001f, 0.85f);
            float damp = 1f / Mathf.Max(0.5f, q);
            for (int s = 0; s < 2; s++)
            {
                low += f * band;
                float high = input - low - damp * band;
                band += f * high;
            }
            return band;
        }
    }

    private static float DeterministicNoise(int index, int seed)
    {
        uint n = (uint)(index * 374761393 + seed * 668265263);
        n = (n ^ (n >> 13)) * 1274126177U;
        float u = ((n ^ (n >> 16)) & 0xFFFF) / 65535f;
        return u * 2f - 1f;
    }

    /// <summary>
    /// Deterministically synthesizes the dual-voice interlapping dying ghost shriek:
    /// Voice 1 (deep guttural A2–D3 chest voice, 110–147 Hz) + Voice 2 (natural human-octave scream, D4–C5).
    /// Shared by both runtime fallback and BarangayAssetGenerator WAV export.
    /// </summary>
    public static float[] SynthesizeDyingShriekSamples(int sampleRate = 44100, float duration = 2.8f)
    {
        int sampleCount = Mathf.Max(1, Mathf.FloorToInt(sampleRate * duration));
        float[] dry = new float[sampleCount];
        float[] wet = new float[sampleCount];

        float phaseLow = 0f;
        float phaseHigh = 0f;
        float twoPi = Mathf.PI * 2f;

        SvfBandpass lowF1 = default;
        SvfBandpass lowF2 = default;
        SvfBandpass lowAir = default;
        SvfBandpass highF1 = default;
        SvfBandpass highF2 = default;
        SvfBandpass highAir = default;

        float jitterLowSmooth = 0f;
        float jitterHighSmooth = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float norm = t / duration;

            float n1 = DeterministicNoise(i, 41);
            float n2 = DeterministicNoise(i, 97);
            jitterLowSmooth += 0.015f * (n1 - jitterLowSmooth);
            jitterHighSmooth += 0.025f * (n2 - jitterHighSmooth);

            // ── Voice 1: Deep guttural chest voice strictly anchored in A2–D3 (110.0–146.83 Hz)
            float baseLowHz = Mathf.Lerp(146.83f, 110.0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.15f) / 2.35f)));
            float tremorLow = Mathf.Sin(twoPi * 6.2f * t) * 3.5f + jitterLowSmooth * 2.2f;
            float freqLow = Mathf.Clamp(baseLowHz + tremorLow, 110.0f, 152.0f);
            phaseLow = (phaseLow + twoPi * freqLow / sampleRate) % twoPi;

            float glottalLow =
                Mathf.Sin(phaseLow) * 0.56f +
                Mathf.Sin(phaseLow * 2f) * 0.34f +
                Mathf.Sin(phaseLow * 3f) * 0.22f +
                Mathf.Sin(phaseLow * 4f) * 0.14f +
                Mathf.Sin(phaseLow * 5f) * 0.08f +
                Mathf.Sin(phaseLow * 6f) * 0.04f;

            float chestTone =
                lowF1.Process(glottalLow, 360f - 35f * norm, 3.8f, sampleRate) * 0.88f +
                lowF2.Process(glottalLow, 780f - 70f * norm, 4.8f, sampleRate) * 0.52f +
                glottalLow * 0.24f;
            float chestRasp = lowAir.Process(n1, 460f, 2.2f, sampleRate) * 0.24f;

            // Envelope: swells underneath 0.18s -> drives 0.30..2.10s -> fading A2 ember tail to 2.80s
            float envLowAttack = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.16f) / 0.24f));
            float envLowClash = 1f + 0.24f * Mathf.Exp(-Mathf.Pow((t - 1.45f) / 0.26f, 2f));
            float envLowDecay = t > 1.75f ? Mathf.Clamp01(1f - (t - 1.75f) / 1.05f) : 1f;
            envLowDecay = Mathf.Pow(envLowDecay, 1.35f);
            float envLow = envLowAttack * envLowClash * envLowDecay;

            // ── Voice 2: Natural human-octave screaming voice (D4–C5, ~294–523 Hz)
            float baseHighHz;
            if (t < 0.45f)
            {
                baseHighHz = Mathf.Lerp(340f, 512f, Mathf.SmoothStep(0f, 1f, t / 0.45f));
            }
            else if (t < 1.45f)
            {
                float u = (t - 0.45f) / 1.00f;
                baseHighHz = Mathf.Lerp(512f, 470f, u) + Mathf.Sin(twoPi * 1.8f * t) * 18f;
            }
            else
            {
                float u = Mathf.Clamp01((t - 1.45f) / 0.85f);
                baseHighHz = Mathf.Lerp(470f, 311.13f, Mathf.Pow(u, 0.85f));
            }
            float vibratoHigh = Mathf.Sin(twoPi * 7.5f * t) * 13.5f + jitterHighSmooth * 7.0f;
            float freqHigh = Mathf.Clamp(baseHighHz + vibratoHigh, 285f, 535f);
            phaseHigh = (phaseHigh + twoPi * freqHigh / sampleRate) % twoPi;

            float vocalHigh =
                Mathf.Sin(phaseHigh * 0.5f) * 0.12f +
                Mathf.Sin(phaseHigh) * 0.58f +
                Mathf.Sin(phaseHigh * 2f) * 0.34f +
                Mathf.Sin(phaseHigh * 3f) * 0.19f +
                Mathf.Sin(phaseHigh * 4f) * 0.10f;

            float formantF1 = Mathf.Lerp(720f, 620f, norm);
            float formantF2 = Mathf.Lerp(1380f, 1180f, norm);
            float highTone =
                highF1.Process(vocalHigh, formantF1, 4.4f, sampleRate) * 0.85f +
                highF2.Process(vocalHigh, formantF2, 5.2f, sampleRate) * 0.50f;
            float highRasp = highAir.Process(n2, 1650f, 2.0f, sampleRate) * 0.18f;

            // Envelope: strikes immediately (0.04s–1.15s), peaks again at dual-voice clash (1.25–1.65s), breaks by 2.22s
            float envHighAttack = Mathf.Clamp01(t / 0.04f);
            float envHighPrimary = t < 1.20f
                ? Mathf.Lerp(1.0f, 0.68f, Mathf.Clamp01((t - 0.55f) / 0.65f))
                : 0.68f;
            float envHighClash = 0.34f * Mathf.Exp(-Mathf.Pow((t - 1.42f) / 0.24f, 2f));
            float envHighTail = t > 1.60f ? Mathf.Clamp01(1f - (t - 1.60f) / 0.65f) : 1f;
            float envHigh = envHighAttack * (envHighPrimary + envHighClash) * Mathf.Pow(envHighTail, 1.5f);

            float mixed = (chestTone + chestRasp) * envLow * 0.78f + (highTone + highRasp) * envHigh * 0.72f;
            dry[i] = (float)System.Math.Tanh(mixed * 1.35f);
        }

        // Subtle multi-tap short room reverb (23ms, 41ms, 67ms)
        int tap1 = Mathf.RoundToInt(0.023f * sampleRate);
        int tap2 = Mathf.RoundToInt(0.041f * sampleRate);
        int tap3 = Mathf.RoundToInt(0.067f * sampleRate);
        float lpVerb = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float v = 0f;
            if (i >= tap1) v += dry[i - tap1] * 0.24f;
            if (i >= tap2) v += dry[i - tap2] * 0.17f;
            if (i >= tap3) v += dry[i - tap3] * 0.11f;
            lpVerb = lpVerb * 0.55f + v * 0.45f;

            float tailFade = i > sampleCount - Mathf.RoundToInt(0.14f * sampleRate)
                ? Mathf.Clamp01((float)(sampleCount - 1 - i) / (0.14f * sampleRate))
                : 1f;
            wet[i] = (dry[i] + lpVerb * 0.45f) * tailFade;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++)
        {
            float a = Mathf.Abs(wet[i]);
            if (a > peak) peak = a;
        }
        if (peak > 1e-5f)
        {
            float gain = 0.90f / peak;
            for (int i = 0; i < sampleCount; i++) wet[i] *= gain;
        }

        return wet;
    }

    private static AudioClip CreateProceduralShriekClip()
    {
        const int sampleRate = 44100;
        const float duration = 2.8f;
        float[] samples = SynthesizeDyingShriekSamples(sampleRate, duration);
        AudioClip clip = AudioClip.Create("ProceduralGhostShriek", samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private System.Collections.IEnumerator TraverseOffMeshLinkRoutine()
    {
        isTraversingOffMeshLink = true;
        if (agent != null && agent.isOnNavMesh && agent.isOnOffMeshLink)
        {
            OffMeshLinkData data = agent.currentOffMeshLinkData;
            Vector3 startPos = transform.position;
            Vector3 endPos = new Vector3(data.endPos.x, data.endPos.y, transform.position.z);

            float dist = Vector2.Distance(startPos, endPos);
            float duration = dist > 1.5f ? 0.25f : Mathf.Clamp(dist / Mathf.Max(0.25f, agent.speed), 0.1f, 0.35f);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (ghostHandler != null && ghostHandler.CurrentState == GhostHandlerState.Dormant)
                {
                    isTraversingOffMeshLink = false;
                    yield break;
                }
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.position = Vector3.Lerp(startPos, endPos, t);
                yield return null;
            }

            transform.position = endPos;
            if (agent != null && agent.isOnNavMesh && agent.isOnOffMeshLink)
            {
                agent.CompleteOffMeshLink();
            }
        }
        isTraversingOffMeshLink = false;
    }

    private System.Collections.IEnumerator KillSequenceRoutine()
    {
        isExecutingKill = true;
        chaseTarget = null;
        targetedHidingDoor = null;
        isTrackingEquipment = false;

        if (agent != null && agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

        // Reveal ghost fully over the player so the player actually sees their killer
        ShowKillPresenceClientRpc();

        yield return new WaitForSeconds(1.8f);

        killSequenceCoroutine = null;
        isExecutingKill = false;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
        if (ghostHandler != null && ghostHandler.CurrentState != GhostHandlerState.Dormant)
        {
            ghostHandler.EndHunt();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ShowKillPresenceClientRpc()
    {
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.enabled = true;
            Color c = sr.color;
            c.a = 1f;
            sr.color = c;
        }
        if (ghostLight != null)
        {
            ghostLight.enabled = true;
            ghostLight.intensity = 1.5f;
        }
    }
}
