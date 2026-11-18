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
    [SerializeField] private float agentStoppingDistance = 0.05f;

    [Header("LOS Settings")]
    public float lineOfSightDistance = 8f;
    public float lineOfSightAngle = 90f;
    public LayerMask obstacleLayer; // Kept for inspector compatibility

    [Header("Door & Cabinet Interaction")]
    [Tooltip("Distance at which the ghost reaches and forces open a target cabinet door.")]
    [SerializeField] private float doorOpenReach = 0.32f;
    [Tooltip("Distance at which the ghost pushes open a closed interior room door in its path.")]
    [SerializeField] private float interiorDoorOpenReach = 0.30f;
    [Tooltip("Maximum distance from a cabinet door for a player to count as hiding inside that cabinet.")]
    [SerializeField] private float cabinetPlayerInsideRadius = 0.28f;

    [Header("Phasmophobia Detection & Pursuit Calibration")]
    [SerializeField] private float electronicDetectionRadius = 9.0f;
    [SerializeField] private float doorNoiseHearRadius = 5.5f;
    [SerializeField] private float closeLoSOverrideDistance = 2.0f;
    [SerializeField] private float openedDoorHideCheckRadius = 0.25f;
    [SerializeField] private float hidingDoorStandOffDistance = 0.18f;
    [SerializeField] private float postKillDistanceCheckDelay = 3.0f;
    [SerializeField] private float electronicResidualGraceDuration = 1.5f;

    [Header("Combat & Kill")]
    [Tooltip("Range within which the ghost can kill the player in open chases (cabinet threshold fallback handled at runtime).")]
    [SerializeField] private float killDistance = 0.15f;

    public enum PursuitMode
    {
        None = 0,
        DetectionWaypoint = 1,
        LineOfSight = 2
    }

    private sealed class PlayerElectronicState
    {
        public bool wasActive;
        public float activeDuration;
        public float detectionGraceExpiry;
        public Vector3 lastActivePosition;
        public bool hasPinnedPing;
    }

    private Rigidbody2D rb;
    private NavMeshAgent agent;
    private GhostNode targetNode;
    private int nodesVisitedCount = 0;

    private PlayerController chaseTarget;
    private Vector3 lastKnownPlayerPos;
    private PursuitMode pursuitMode = PursuitMode.None;
    private Vector3 pinnedWaypointPos;
    private PlayerController pendingLoSSwitchPlayer;
    private float pendingLoSSwitchDelayTimer = -1f;
    private float postKillDistanceDelayTimer = 0f;
    private bool pendingPostKillDistanceResolve = false;

    // Targeted hiding spot tracking:
    // Only set when the ghost has UNBROKEN LOS and sees the player close the cabinet door,
    // OR when the player has active equipment powered on inside the cabinet.
    private NetworkDoor targetedHidingDoor = null;

    [Header("Chase Memory & Tracking")]
    [Tooltip("How long the ghost remembers and pursues the player's last known location after losing direct sight.")]
    [SerializeField] private float chaseMemoryDuration = 3.0f;
    public float ChaseMemoryDuration => chaseMemoryDuration;
    private readonly Dictionary<PlayerController, PlayerElectronicState> playerElectronicStates =
        new Dictionary<PlayerController, PlayerElectronicState>();
    private readonly List<PlayerController> electronicCandidates = new List<PlayerController>();
    private readonly List<PlayerController> losCandidates = new List<PlayerController>();
    private readonly HashSet<NetworkDoor> checkedDoorsBuffer = new HashSet<NetworkDoor>();
    private readonly Dictionary<NetworkDoor, float> doorNextAllowedInteractTime = new Dictionary<NetworkDoor, float>();

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
    public bool IsInHuntGracePeriod => ghostHandler != null && ghostHandler.IsInHuntGracePeriod;

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

        // Clamp stale scene/prefab serialized door & kill interaction distances to BarangayMap world scale
        if (doorOpenReach > 0.45f) doorOpenReach = 0.32f;
        if (interiorDoorOpenReach > 0.45f) interiorDoorOpenReach = 0.30f;
        if (cabinetPlayerInsideRadius > 0.40f) cabinetPlayerInsideRadius = 0.28f;
        if (openedDoorHideCheckRadius > 0.35f) openedDoorHideCheckRadius = 0.25f;
        if (hidingDoorStandOffDistance > 0.30f) hidingDoorStandOffDistance = 0.18f;
        if (killDistance > 0.15f) killDistance = 0.15f;
        if (agentStoppingDistance > 0.08f) agentStoppingDistance = 0.05f;
        if (agent != null) agent.stoppingDistance = agentStoppingDistance;

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
        pursuitMode = PursuitMode.None;
        pinnedWaypointPos = transform.position;
        pendingLoSSwitchPlayer = null;
        pendingLoSSwitchDelayTimer = -1f;
        postKillDistanceDelayTimer = 0f;
        pendingPostKillDistanceResolve = false;
        playerElectronicStates.Clear();
        doorNextAllowedInteractTime.Clear();
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
        pursuitMode = PursuitMode.None;
        pinnedWaypointPos = Vector3.zero;
        pendingLoSSwitchPlayer = null;
        pendingLoSSwitchDelayTimer = -1f;
        postKillDistanceDelayTimer = 0f;
        pendingPostKillDistanceResolve = false;
        playerElectronicStates.Clear();
        doorNextAllowedInteractTime.Clear();
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
                if (ghostHandler.IsInHuntGracePeriod)
                {
                    if (agent != null && agent.enabled && agent.isOnNavMesh)
                    {
                        agent.isStopped = true;
                        agent.velocity = Vector3.zero;
                    }
                    netPosition.Value = new Vector2(transform.position.x, transform.position.y);
                    return;
                }

                if (agent != null && agent.enabled && agent.isOnNavMesh && agent.isStopped)
                {
                    agent.isStopped = false;
                }

                if (agent != null && agent.isOnNavMesh && agent.isOnOffMeshLink)
                {
                    if (!isTraversingOffMeshLink)
                    {
                        StartCoroutine(TraverseOffMeshLinkRoutine());
                    }
                    return;
                }

                // Sync NavMeshAgent speed (Angered state moves much faster!)
                float currentBaseSpeed = (pursuitMode == PursuitMode.LineOfSight || ghostHandler.CurrentState == GhostHandlerState.Chasing)
                    ? chaseSpeed
                    : moveSpeed;
                if (isAngeredState)
                {
                    currentBaseSpeed *= 2.2f;
                }
                if (agent != null && agent.enabled)
                {
                    agent.speed = isRepelled ? (moveSpeed * 0.5f) : currentBaseSpeed;
                }

                if (postKillDistanceDelayTimer > 0f)
                {
                    postKillDistanceDelayTimer = Mathf.Max(0f, postKillDistanceDelayTimer - Time.deltaTime);
                }

                UpdateElectronicStates();
                EvaluateDetectionAndPursuit();

                if (pursuitMode == PursuitMode.None && stairFollowSource == null)
                {
                    UpdateNodePathfinding();
                }

                CheckCabinetDoorInteractions();
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

    private static int CompareJoinOrder(PlayerController a, PlayerController b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return 1;
        if (b == null) return -1;
        int idxA = a.JoinOrderIndex >= 0 ? a.JoinOrderIndex : PlayerController.AllPlayers.IndexOf(a);
        int idxB = b.JoinOrderIndex >= 0 ? b.JoinOrderIndex : PlayerController.AllPlayers.IndexOf(b);
        return idxA.CompareTo(idxB);
    }

    private PlayerController PickTargetMultiPlayerRule(List<PlayerController> candidates, bool allowDistanceCheck)
    {
        if (candidates == null || candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        candidates.Sort(CompareJoinOrder);
        if (!allowDistanceCheck)
        {
            return candidates[0];
        }

        PlayerController closest = candidates[0];
        float minDist = Vector2.Distance(transform.position, closest.transform.position);
        for (int i = 1; i < candidates.Count; i++)
        {
            float d = Vector2.Distance(transform.position, candidates[i].transform.position);
            if (d < minDist)
            {
                minDist = d;
                closest = candidates[i];
            }
        }
        return closest;
    }

    public void OnPlayerSpiritBoxPing(PlayerController player)
    {
        if (!IsServer || ghostHandler == null || ghostHandler.CurrentState == GhostHandlerState.Dormant) return;
        if (ghostHandler.IsInHuntGracePeriod || isExecutingKill) return;
        if (player == null || !player.IsAlive || !IsTargetInsideAndReachable(player)) return;

        float dist = Vector2.Distance(transform.position, player.transform.position);
        if (dist > electronicDetectionRadius) return;

        if (!playerElectronicStates.TryGetValue(player, out var state))
        {
            state = new PlayerElectronicState();
            playerElectronicStates[player] = state;
        }
        state.wasActive = true;
        state.lastActivePosition = player.transform.position;
        state.detectionGraceExpiry = Time.time + electronicResidualGraceDuration;
        state.hasPinnedPing = true;
    }

    public void OnPlayerDoorNoise(Vector3 doorPos)
    {
        if (!IsServer || ghostHandler == null || ghostHandler.CurrentState == GhostHandlerState.Dormant) return;
        if (ghostHandler.IsInHuntGracePeriod || isExecutingKill) return;
        if (Vector2.Distance(transform.position, doorPos) > doorNoiseHearRadius) return;
        if (!ComputeInsideAndReachable(doorPos)) return;

        // Lock rule: do not overwrite an existing DetectionWaypoint or active LineOfSight chase
        if (pursuitMode == PursuitMode.None)
        {
            EnterDetectionWaypointPursuit(doorPos, null, -1f);
        }
    }

    private void UpdateElectronicStates()
    {
        for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
        {
            var player = PlayerController.AllPlayers[i];
            if (player == null) continue;

            if (!player.IsAlive || !IsTargetInsideAndReachable(player))
            {
                if (playerElectronicStates.TryGetValue(player, out var inactiveState))
                {
                    inactiveState.wasActive = false;
                    inactiveState.activeDuration = 0f;
                    inactiveState.hasPinnedPing = false;
                }
                continue;
            }

            if (!playerElectronicStates.TryGetValue(player, out var state))
            {
                state = new PlayerElectronicState();
                playerElectronicStates[player] = state;
            }

            bool activeNow = IsPlayerEquipmentActive(player);
            float dist = Vector2.Distance(transform.position, player.transform.position);

            if (activeNow)
            {
                state.wasActive = true;
                state.activeDuration += Time.deltaTime;
                if (dist <= electronicDetectionRadius)
                {
                    state.lastActivePosition = player.transform.position;
                    state.detectionGraceExpiry = Time.time + electronicResidualGraceDuration;
                    state.hasPinnedPing = true;
                }
            }
            else
            {
                if (state.wasActive)
                {
                    if (dist <= electronicDetectionRadius || state.hasPinnedPing)
                    {
                        state.lastActivePosition = player.transform.position;
                        state.detectionGraceExpiry = Time.time + electronicResidualGraceDuration;
                        state.hasPinnedPing = true;
                    }
                    state.wasActive = false;
                    state.activeDuration = 0f;
                }
                else if (state.hasPinnedPing && Time.time > state.detectionGraceExpiry)
                {
                    state.hasPinnedPing = false;
                }
            }
        }
    }

    private NetworkDoor FindClosedHidingDoorNear(Vector3 pos, out Vector3 standOffWaypoint)
    {
        standOffWaypoint = pos;
        NetworkDoor bestDoor = null;
        float bestDist = cabinetPlayerInsideRadius;

        for (int i = 0; i < NetworkDoor.AllDoors.Count; i++)
        {
            var d = NetworkDoor.AllDoors[i];
            if (d == null || d.IsFrontDoor || !d.IsHidingSpot || d.IsOpen) continue;

            float dist = Vector2.Distance(pos, d.transform.position);
            if (dist <= bestDist)
            {
                bestDist = dist;
                bestDoor = d;
            }
        }

        if (bestDoor != null)
        {
            standOffWaypoint = ResolveHidingDoorStandOffPoint(bestDoor, pos);
        }
        return bestDoor;
    }

    private Vector3 ResolveHidingDoorStandOffPoint(NetworkDoor door, Vector3 playerPos)
    {
        if (door == null) return playerPos;
        Vector2 doorPos = door.transform.position;
        Vector2 awayFromPlayer = doorPos - (Vector2)playerPos;
        Vector2 towardGhost = (Vector2)transform.position - doorPos;

        Vector2[] candidateDirs =
        {
            awayFromPlayer.sqrMagnitude > 0.001f ? awayFromPlayer.normalized : Vector2.down,
            towardGhost.sqrMagnitude > 0.001f ? towardGhost.normalized : Vector2.down,
            (Vector2)door.transform.up,
            -(Vector2)door.transform.up,
            (Vector2)door.transform.right,
            -(Vector2)door.transform.right
        };

        float step = Mathf.Clamp(hidingDoorStandOffDistance, 0.14f, 0.24f);
        for (int i = 0; i < candidateDirs.Length; i++)
        {
            Vector3 probe = new Vector3(doorPos.x + candidateDirs[i].x * step, doorPos.y + candidateDirs[i].y * step, transform.position.z);
            if (NavMesh.SamplePosition(probe, out NavMeshHit hit, 0.35f, WalkableAreaMask))
            {
                if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                {
                    return hit.position;
                }

                if (reachPath == null) reachPath = new NavMeshPath();
                if (agent.CalculatePath(hit.position, reachPath) && reachPath.status == NavMeshPathStatus.PathComplete)
                {
                    return hit.position;
                }
            }
        }

        return door.transform.position;
    }

    private void EnterDetectionWaypointPursuit(Vector3 waypointPos, PlayerController losDelayPlayer, float losSwitchDelay)
    {
        NetworkDoor hidingDoor = FindClosedHidingDoorNear(waypointPos, out Vector3 resolvedPos);
        if (hidingDoor != null)
        {
            targetedHidingDoor = hidingDoor;
            waypointPos = resolvedPos;
        }
        else if (TryFindNearestNavMeshPoint(waypointPos, out NavMeshHit hit))
        {
            waypointPos = hit.position;
        }

        pursuitMode = PursuitMode.DetectionWaypoint;
        pinnedWaypointPos = waypointPos;
        lastKnownPlayerPos = waypointPos;
        chaseTarget = null;
        pendingLoSSwitchPlayer = losDelayPlayer;
        pendingLoSSwitchDelayTimer = losSwitchDelay;
        nodesVisitedCount = 0;

        SetAgentDestination(pinnedWaypointPos);
        if (ghostHandler != null && ghostHandler.CurrentState != GhostHandlerState.Dormant)
        {
            ghostHandler.RequestStateChange(GhostHandlerState.Investigating);
        }
    }

    private void EnterLineOfSightPursuit(PlayerController target, bool viaEquipment)
    {
        if (target == null) return;
        AcquireChaseTarget(target, viaEquipment);
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
            Debug.Log($"HunterGhost: Switching target to player '{target.name}'.");
        }

        pursuitMode = PursuitMode.LineOfSight;
        pendingLoSSwitchPlayer = null;
        pendingLoSSwitchDelayTimer = -1f;
        chaseTarget = target;
        lastKnownPlayerPos = target.transform.position;
        nodesVisitedCount = 0;

        NetworkDoor hidingDoor = FindClosedHidingDoorNear(target.transform.position, out Vector3 standOffPos);
        if (hidingDoor != null && (viaEquipment || targetedHidingDoor == hidingDoor))
        {
            targetedHidingDoor = hidingDoor;
            SetAgentDestination(standOffPos);
        }
        else
        {
            SetAgentDestination(target.transform.position);
        }

        ghostHandler?.RequestStateChange(GhostHandlerState.Chasing);
    }

    private void EvaluateDetectionAndPursuit()
    {
        if (isExecutingKill) return;

        if (stairFollowSource != null)
        {
            UpdateStairFollow();
            return;
        }

        LayerMask combinedMask = GetCombinedObstacleMask();

        // 1. Scan electronic / Spirit Box pings BEFORE Visual LoS
        electronicCandidates.Clear();
        for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
        {
            var player = PlayerController.AllPlayers[i];
            if (player == null || !player.IsAlive || !IsTargetInsideAndReachable(player)) continue;

            if (playerElectronicStates.TryGetValue(player, out var eState) &&
                eState.hasPinnedPing &&
                Time.time <= eState.detectionGraceExpiry)
            {
                electronicCandidates.Add(player);
            }
        }

        // 2. Scan active Visual LoS across all living indoor players
        losCandidates.Clear();
        float currentLOS = (pursuitMode == PursuitMode.LineOfSight || (ghostHandler != null && ghostHandler.CurrentState == GhostHandlerState.Chasing))
            ? lineOfSightDistance * 1.5f
            : lineOfSightDistance;

        for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
        {
            var player = PlayerController.AllPlayers[i];
            if (player == null || !player.IsAlive || player.IsHiddenFromGhost) continue;
            if (!IsTargetInsideAndReachable(player)) continue;

            float dist = Vector2.Distance(transform.position, player.transform.position);
            if (dist <= currentLOS && CheckHasVisualLOS(player, dist, combinedMask))
            {
                losCandidates.Add(player);
            }
        }

        bool allowDistanceCheck = postKillDistanceDelayTimer <= 0f;

        // 3. State Machine
        if (pursuitMode == PursuitMode.None)
        {
            if (electronicCandidates.Count > 0)
            {
                PlayerController chosen = PickTargetMultiPlayerRule(electronicCandidates, allowDistanceCheck);
                if (!allowDistanceCheck) pendingPostKillDistanceResolve = true;

                Vector3 pinPos = playerElectronicStates[chosen].lastActivePosition;
                if (losCandidates.Contains(chosen))
                {
                    int indoorCount = GhostHandler.CountLivingPlayersInInvestigationArea();
                    if (indoorCount <= 1)
                    {
                        EnterLineOfSightPursuit(chosen, true);
                    }
                    else
                    {
                        EnterDetectionWaypointPursuit(pinPos, chosen, Random.Range(0f, 20f));
                    }
                }
                else
                {
                    EnterDetectionWaypointPursuit(pinPos, null, -1f);
                }
                return;
            }

            if (losCandidates.Count > 0)
            {
                PlayerController chosen = PickTargetMultiPlayerRule(losCandidates, allowDistanceCheck);
                if (!allowDistanceCheck) pendingPostKillDistanceResolve = true;
                EnterLineOfSightPursuit(chosen, false);
                return;
            }
        }
        else if (pursuitMode == PursuitMode.DetectionWaypoint)
        {
            // Lock rule: NEVER overwrite pinnedWaypointPos from another electronic, voice, or door detection.
            // Only active Visual LoS can interrupt DetectionWaypoint (subject to the simultaneous-detection delay).
            if (pendingLoSSwitchPlayer != null && pendingLoSSwitchDelayTimer >= 0f)
            {
                if (pendingLoSSwitchPlayer.IsAlive && losCandidates.Contains(pendingLoSSwitchPlayer))
                {
                    pendingLoSSwitchDelayTimer -= Time.deltaTime;
                    if (pendingLoSSwitchDelayTimer <= 0f)
                    {
                        EnterLineOfSightPursuit(pendingLoSSwitchPlayer, true);
                        return;
                    }
                }
                else
                {
                    pendingLoSSwitchPlayer = null;
                    pendingLoSSwitchDelayTimer = -1f;
                }
            }

            if (losCandidates.Count > 0)
            {
                if (pendingLoSSwitchPlayer == null)
                {
                    PlayerController chosen = PickTargetMultiPlayerRule(losCandidates, allowDistanceCheck);
                    if (!allowDistanceCheck) pendingPostKillDistanceResolve = true;
                    EnterLineOfSightPursuit(chosen, false);
                    return;
                }

                // If another player is visible while delaying on pendingLoSSwitchPlayer, interrupt immediately to them
                for (int i = 0; i < losCandidates.Count; i++)
                {
                    if (losCandidates[i] != pendingLoSSwitchPlayer)
                    {
                        PlayerController chosen = PickTargetMultiPlayerRule(losCandidates, allowDistanceCheck);
                        if (!allowDistanceCheck) pendingPostKillDistanceResolve = true;
                        EnterLineOfSightPursuit(chosen, false);
                        return;
                    }
                }
            }

            if (targetedHidingDoor != null && targetedHidingDoor.IsOpen)
            {
                targetedHidingDoor = null;
            }

            Vector3 navDest = pinnedWaypointPos;
            if (targetedHidingDoor != null && !targetedHidingDoor.IsOpen)
            {
                navDest = ResolveHidingDoorStandOffPoint(targetedHidingDoor, pinnedWaypointPos);
                pinnedWaypointPos = navDest;
            }

            if (!SetAgentDestination(navDest))
            {
                // Unreachable waypoint -> clear and return to Searching
                ClearDetectionWaypointToRoam();
                return;
            }

            float arrivalThreshold = (targetedHidingDoor != null && !targetedHidingDoor.IsOpen) ? 0.18f : 0.5f;
            float distToPin = Vector2.Distance(transform.position, navDest);
            bool arrived = distToPin <= arrivalThreshold ||
                (agent != null && agent.enabled && agent.isOnNavMesh && !agent.pathPending && agent.hasPath && agent.remainingDistance <= arrivalThreshold);

            if (arrived)
            {
                if (targetedHidingDoor != null && !targetedHidingDoor.IsOpen &&
                    GetDistanceToDoor(transform.position, targetedHidingDoor) <= doorOpenReach)
                {
                    OpenDoorAndConnected(targetedHidingDoor);
                    targetedHidingDoor = null;
                }

                if (losCandidates.Count > 0)
                {
                    PlayerController chosen = PickTargetMultiPlayerRule(losCandidates, allowDistanceCheck);
                    if (!allowDistanceCheck) pendingPostKillDistanceResolve = true;
                    EnterLineOfSightPursuit(chosen, false);
                }
                else
                {
                    ClearDetectionWaypointToRoam();
                }
            }
        }
        else if (pursuitMode == PursuitMode.LineOfSight)
        {
            if (chaseTarget == null || !chaseTarget.IsAlive)
            {
                if (losCandidates.Count > 0)
                {
                    PlayerController chosen = PickTargetMultiPlayerRule(losCandidates, allowDistanceCheck);
                    if (!allowDistanceCheck) pendingPostKillDistanceResolve = true;
                    EnterLineOfSightPursuit(chosen, false);
                }
                else
                {
                    EnterDetectionWaypointPursuit(lastKnownPlayerPos, null, -1f);
                }
                return;
            }

            if (!IsTargetInsideAndReachable(chaseTarget))
            {
                if (TryBeginStairFollow(chaseTarget))
                {
                    return;
                }
                DropChaseTargetOutdoors();
                return;
            }

            // If a post-kill 3s distance delay just finished, run the multi-player Euclidean distance check now
            if (pendingPostKillDistanceResolve && allowDistanceCheck)
            {
                pendingPostKillDistanceResolve = false;
                if (losCandidates.Count > 1)
                {
                    PlayerController closest = PickTargetMultiPlayerRule(losCandidates, true);
                    if (closest != null && closest != chaseTarget)
                    {
                        EnterLineOfSightPursuit(closest, false);
                    }
                }
            }

            // Active LoS focus: only override if another visible player comes within closeLoSOverrideDistance (2.0f units)
            float currentTargetDist = Vector2.Distance(transform.position, chaseTarget.transform.position);
            PlayerController closeOverride = null;
            float bestOverrideDist = float.MaxValue;
            for (int i = 0; i < losCandidates.Count; i++)
            {
                var candidate = losCandidates[i];
                if (candidate == null || candidate == chaseTarget) continue;
                float d = Vector2.Distance(transform.position, candidate.transform.position);
                if (d <= closeLoSOverrideDistance && d < currentTargetDist && d < bestOverrideDist)
                {
                    bestOverrideDist = d;
                    closeOverride = candidate;
                }
            }
            if (closeOverride != null)
            {
                EnterLineOfSightPursuit(closeOverride, false);
                currentTargetDist = bestOverrideDist;
            }

            bool hasVisualLoS = CheckHasVisualLOS(chaseTarget, currentTargetDist, combinedMask);
            bool hasActiveEquipInRange = IsPlayerEquipmentActive(chaseTarget) && currentTargetDist <= electronicDetectionRadius;

            if (hasVisualLoS)
            {
                lastKnownPlayerPos = chaseTarget.transform.position;
                if (targetedHidingDoor != null && !targetedHidingDoor.IsOpen)
                {
                    SetAgentDestination(ResolveHidingDoorStandOffPoint(targetedHidingDoor, chaseTarget.transform.position));
                }
                else
                {
                    SetAgentDestination(chaseTarget.transform.position);
                }
            }
            else if (hasActiveEquipInRange)
            {
                lastKnownPlayerPos = chaseTarget.transform.position;
                NetworkDoor hidingDoor = FindClosedHidingDoorNear(chaseTarget.transform.position, out Vector3 standOffPos);
                if (hidingDoor != null)
                {
                    targetedHidingDoor = hidingDoor;
                    SetAgentDestination(standOffPos);
                }
                else
                {
                    SetAgentDestination(chaseTarget.transform.position);
                }
            }
            else
            {
                // Visual LoS broke and no active equipment: lock DetectionWaypoint at final lastKnownPlayerPos
                Vector3 finalPos = lastKnownPlayerPos;
                chaseTarget = null;
                EnterDetectionWaypointPursuit(finalPos, null, -1f);
                return;
            }

            // Kill check (0.15f in open chases; 0.24f threshold fallback when target is inside an opened cabinet)
            float effectiveKillDistance = (chaseTarget != null && chaseTarget.IsInHideZone)
                ? Mathf.Max(killDistance, 0.24f)
                : killDistance;
            if (!isRepelled && chaseTarget != null && currentTargetDist <= effectiveKillDistance && hasVisualLoS && !isExecutingKill)
            {
                if (chaseTarget.KillPlayer())
                {
                    NotifyPlayerCaughtClientRpc(chaseTarget.OwnerClientId);
                    postKillDistanceDelayTimer = postKillDistanceCheckDelay;
                    pendingPostKillDistanceResolve = true;
                    chaseTarget = null;
                    targetedHidingDoor = null;
                    pursuitMode = PursuitMode.None;
                    if (killSequenceCoroutine != null) StopCoroutine(killSequenceCoroutine);
                    killSequenceCoroutine = StartCoroutine(KillSequenceRoutine());
                }
            }
        }
    }

    private void ClearDetectionWaypointToRoam()
    {
        pursuitMode = PursuitMode.None;
        pinnedWaypointPos = Vector3.zero;
        pendingLoSSwitchPlayer = null;
        pendingLoSSwitchDelayTimer = -1f;
        targetedHidingDoor = null;
        chaseTarget = null;
        recentNodes.Clear();
        targetNode = FindNearestNode(transform.position);
        if (ghostHandler != null && ghostHandler.CurrentState != GhostHandlerState.Dormant)
        {
            ghostHandler.RequestStateChange(GhostHandlerState.Searching);
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

                // Closed hiding door only lets the ghost track the player if it ALREADY had active LoS on them entering
                if (!door.IsFrontDoor && door.IsHidingSpot &&
                    pursuitMode == PursuitMode.LineOfSight &&
                    chaseTarget == player)
                {
                    if (IsPlayerActuallyInsideCabinet(player, door))
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

    public bool IsPlayerEquipmentActive(PlayerController player)
    {
        return PlayerInventory.HasActiveHeldEquipment(player);
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
        bool needsRefresh = enclosedRoofs == null;
        if (!needsRefresh)
        {
            for (int i = 0; i < enclosedRoofs.Length; i++)
            {
                if (enclosedRoofs[i] == null)
                {
                    needsRefresh = true;
                    break;
                }
            }
        }

        if (needsRefresh)
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

    private bool IsPositionWithinIndoorBounds(Vector3 targetPos)
    {
        if (InvestigationZone.AllZones.Count > 0 && InvestigationZone.IsPositionInAnyZone(targetPos))
        {
            return true;
        }

        if (FloorZone.HasAnyZones())
        {
            return FloorZone.IsPositionInsideInteriorFloor(targetPos) || IsInsideEnclosedRoof(targetPos, out _);
        }

        if (GameAudioManager.HasSceneBasement && GameAudioManager.IsBasementPosition(targetPos))
        {
            return true;
        }

        bool inRoof = IsInsideEnclosedRoof(targetPos, out bool hasRoof);
        return !hasRoof || inRoof;
    }

    /// <summary>
    /// True when the player is inside the investigation area (house or outdoor InvestigationZone) and the ghost has a complete NavMesh path to them.
    /// Results are cached briefly to keep per-frame scans cheap.
    /// </summary>
    private bool IsTargetInsideAndReachable(PlayerController player)
    {
        if (player == null) return false;

        Vector3 targetPos = player.transform.position;
        if (!IsPositionWithinIndoorBounds(targetPos))
        {
            reachabilityCache[player] = new KeyValuePair<float, bool>(Time.time + 0.1f, false);
            return false;
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

        if (!IsPositionWithinIndoorBounds(targetPos))
        {
            return false;
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
        pursuitMode = PursuitMode.None;
        pinnedWaypointPos = Vector3.zero;
        pendingLoSSwitchPlayer = null;
        pendingLoSSwitchDelayTimer = -1f;
        chaseTarget = null;
        targetedHidingDoor = null;
        // Forget any last known position outside the house so the ghost returns to searching inside
        lastKnownPlayerPos = transform.position;
        if (ghostHandler != null && ghostHandler.CurrentState != GhostHandlerState.Dormant)
        {
            ghostHandler.RequestStateChange(GhostHandlerState.Searching);
        }
    }

    // ── Door & Cabinet Handling ────────────────────────────────────────────

    private static float GetDistanceToDoor(Vector3 fromPos, NetworkDoor door)
    {
        if (door == null) return float.MaxValue;
        float best = Vector2.Distance(fromPos, door.transform.position);
        var cols = door.GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null || !cols[i].enabled) continue;
            float d = Vector2.Distance(fromPos, cols[i].ClosestPoint(fromPos));
            if (d < best) best = d;
        }
        return best;
    }

    private bool IsPlayerActuallyInsideCabinet(PlayerController player, NetworkDoor cabinetDoor)
    {
        if (player == null || !player.IsAlive || cabinetDoor == null || !cabinetDoor.IsHidingSpot)
        {
            return false;
        }

        float distToDoor = Vector2.Distance(player.transform.position, cabinetDoor.transform.position);
        if (distToDoor > cabinetPlayerInsideRadius)
        {
            return false;
        }

        if (player.IsInHideZone && player.CurrentHideZone != null)
        {
            return Vector2.Distance(player.CurrentHideZone.transform.position, cabinetDoor.transform.position) <= 0.38f;
        }

        return distToDoor <= 0.22f;
    }

    private bool IsGhostMovingTowardDoor(NetworkDoor door)
    {
        if (door == null) return false;
        Vector2 toDoor = (Vector2)door.transform.position - (Vector2)transform.position;
        if (toDoor.sqrMagnitude < 0.015f) return true;

        Vector2 moveDir = Vector2.zero;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            if (agent.velocity.sqrMagnitude > 0.002f)
            {
                moveDir = agent.velocity;
            }
            else if (agent.hasPath)
            {
                moveDir = (Vector2)(agent.steeringTarget - transform.position);
            }
        }

        if (moveDir.sqrMagnitude < 0.0001f) return false;
        return Vector2.Dot(moveDir.normalized, toDoor.normalized) >= 0.25f;
    }

    private bool HasClearReachToDoor(NetworkDoor door, LayerMask combinedMask)
    {
        if (door == null) return false;
        Vector2 origin = transform.position;
        Vector2 target = door.transform.position;
        float dist = Vector2.Distance(origin, target);
        if (dist <= 0.05f) return true;

        Vector2 dir = (target - origin) / dist;
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, dist, combinedMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D col = hits[i].collider;
            if (col == null || col.isTrigger) continue;
            if (col.gameObject == gameObject || col.transform.IsChildOf(transform)) continue;
            if (ghostHandler != null && col.transform.IsChildOf(ghostHandler.transform)) continue;
            if (col.GetComponent<EquipmentItem>() != null || col.GetComponentInParent<EquipmentItem>() != null) continue;
            if (col.GetComponent<PlayerController>() != null || col.GetComponentInParent<PlayerController>() != null) continue;

            if (GetDoorFromCollider(col) == door)
            {
                return true;
            }

            // A solid wall or prop sits between the ghost and this door
            return false;
        }

        return true;
    }

    private bool IsDoorOnInteractionCooldown(NetworkDoor door)
    {
        return door != null &&
               doorNextAllowedInteractTime.TryGetValue(door, out float nextAllowed) &&
               Time.time < nextAllowed;
    }

    /// <summary>
    /// Forces a door open, and also opens any adjacent partner doors (such as double cabinet/closet doors).
    /// Only called when the ghost is SURE a player is inside (unbroken LOS or active equipment).
    /// </summary>
    public void OpenDoorAndConnected(NetworkDoor targetDoor)
    {
        if (targetDoor == null || targetDoor.IsFrontDoor || targetDoor.IsLocked) return;

        Debug.Log($"HunterGhost: Ghost forcing open cabinet door '{targetDoor.name}'!");
        targetDoor.GhostInteract(forceOpen: true, lockoutDuration: 1.2f);
        doorNextAllowedInteractTime[targetDoor] = Time.time + 2.5f;

        // If this door is part of a double cabinet (e.g. Cabinet 1 Door 1 & Door 2), open the partner door as well
        for (int i = 0; i < NetworkDoor.AllDoors.Count; i++)
        {
            var other = NetworkDoor.AllDoors[i];
            if (other != null && other != targetDoor && !other.IsFrontDoor && other.IsHidingSpot && !other.IsLocked)
            {
                // Double cabinet doors share the same frame (~0.22–0.28 units apart)
                if (Vector2.Distance(targetDoor.transform.position, other.transform.position) <= 0.28f)
                {
                    Debug.Log($"HunterGhost: Opening partner cabinet door '{other.name}'!");
                    other.GhostInteract(forceOpen: true, lockoutDuration: 1.2f);
                    doorNextAllowedInteractTime[other] = Time.time + 2.5f;
                }
            }
        }
    }

    /// <summary>
    /// Checks if the ghost is right at a closed cabinet door concealing a player during a hunt.
    /// If the ghost tracked the player into this cabinet or detects active electronics inside,
    /// it rips the cabinet door open once within direct reach.
    /// </summary>
    private void CheckCabinetDoorInteractions()
    {
        LayerMask combinedMask = GetCombinedObstacleMask();

        // 1. If we have a targeted hiding door, force it open only once the ghost actually reaches it
        if (targetedHidingDoor != null)
        {
            if (targetedHidingDoor.IsOpen || targetedHidingDoor.IsLocked)
            {
                targetedHidingDoor = null;
            }
            else if (!targetedHidingDoor.IsGhostForceLocked && !IsDoorOnInteractionCooldown(targetedHidingDoor))
            {
                float distToTargetDoor = GetDistanceToDoor(transform.position, targetedHidingDoor);
                if (distToTargetDoor <= doorOpenReach && HasClearReachToDoor(targetedHidingDoor, combinedMask))
                {
                    Debug.Log($"HunterGhost: Arrived at targeted cabinet door '{targetedHidingDoor.name}'! Forcing it open!");
                    OpenDoorAndConnected(targetedHidingDoor);
                    targetedHidingDoor = null;
                    return;
                }
            }
        }

        // 2. Scan only closed hiding doors within direct reach of the ghost
        Collider2D[] nearbyCols = Physics2D.OverlapCircleAll(transform.position, doorOpenReach, combinedMask);
        checkedDoorsBuffer.Clear();
        foreach (var col in nearbyCols)
        {
            var door = GetDoorFromCollider(col);
            if (door == null || door.IsFrontDoor || !door.IsHidingSpot || door.IsOpen || door.IsLocked || door.IsGhostForceLocked)
            {
                continue;
            }
            if (!checkedDoorsBuffer.Add(door) || IsDoorOnInteractionCooldown(door)) continue;
            if (GetDistanceToDoor(transform.position, door) > doorOpenReach) continue;
            if (!HasClearReachToDoor(door, combinedMask)) continue;

            bool playerHidingBehindDoor = false;
            foreach (var p in PlayerController.AllPlayers)
            {
                if (p == null || !p.IsAlive || !IsTargetInsideAndReachable(p)) continue;
                if (!IsPlayerActuallyInsideCabinet(p, door)) continue;

                bool hasResidualPing = playerElectronicStates.TryGetValue(p, out var eState) &&
                    eState.hasPinnedPing && Time.time <= eState.detectionGraceExpiry;

                // A) Player has active or recently-flashed equipment inside this cabinet:
                if (IsPlayerEquipmentActive(p) || hasResidualPing)
                {
                    playerHidingBehindDoor = true;
                    break;
                }

                // B) Ghost had active LineOfSight on this player entering:
                if (pursuitMode == PursuitMode.LineOfSight && chaseTarget == p)
                {
                    playerHidingBehindDoor = true;
                    break;
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

    private void CheckDoorObstructions()
    {
        LayerMask combinedMask = GetCombinedObstacleMask();
        float scanRadius = Mathf.Max(interiorDoorOpenReach, openedDoorHideCheckRadius);
        Collider2D[] nearbyDoors = Physics2D.OverlapCircleAll(transform.position, scanRadius, combinedMask);
        checkedDoorsBuffer.Clear();

        foreach (var col in nearbyDoors)
        {
            var door = GetDoorFromCollider(col);
            if (door == null || door.IsFrontDoor || door.IsHidingSpot || door.IsLocked || door.IsGhostForceLocked) continue;
            if (!checkedDoorsBuffer.Add(door)) continue;
            if (IsDoorOnInteractionCooldown(door)) continue;

            float distToDoor = GetDistanceToDoor(transform.position, door);
            if (!door.IsOpen)
            {
                if (distToDoor <= interiorDoorOpenReach &&
                    IsGhostMovingTowardDoor(door) &&
                    HasClearReachToDoor(door, combinedMask))
                {
                    Debug.Log($"HunterGhost: Ghost pushed open interior door '{door.name}'!");
                    door.GhostInteract(forceOpen: true, lockoutDuration: 1.5f);
                    doorNextAllowedInteractTime[door] = Time.time + 2.5f;
                }
            }
            else if (distToDoor <= openedDoorHideCheckRadius && HasClearReachToDoor(door, combinedMask))
            {
                // Hiding behind opened room door check (only when both ghost and player are directly at the open door)
                for (int i = 0; i < PlayerController.AllPlayers.Count; i++)
                {
                    var p = PlayerController.AllPlayers[i];
                    if (p == null || !p.IsAlive || !IsTargetInsideAndReachable(p)) continue;

                    if (Vector2.Distance(p.transform.position, door.transform.position) <= openedDoorHideCheckRadius &&
                        !CheckHasVisualLOS(p, Vector2.Distance(transform.position, p.transform.position), combinedMask))
                    {
                        Debug.Log($"HunterGhost: Forcefully closing opened room door '{door.name}' to expose hiding player!");
                        door.GhostInteract(forceOpen: false, lockoutDuration: 2.0f);
                        doorNextAllowedInteractTime[door] = Time.time + 3.5f;
                        if (pursuitMode == PursuitMode.None)
                        {
                            EnterDetectionWaypointPursuit(p.transform.position, null, -1f);
                        }
                        break;
                    }
                }
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
            bool pausedInGrace = ghostHandler != null && ghostHandler.IsInHuntGracePeriod;
            agent.isStopped = pausedInGrace;
            if (pausedInGrace)
            {
                agent.velocity = Vector3.zero;
            }
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
            if (GhostHandler.CountLivingPlayersInInvestigationArea() > 0)
            {
                pursuitMode = PursuitMode.None;
                chaseTarget = null;
                targetedHidingDoor = null;
                pendingLoSSwitchPlayer = null;
                pendingLoSSwitchDelayTimer = -1f;
                recentNodes.Clear();
                targetNode = FindNearestNode(transform.position);
                ghostHandler.RequestStateChange(GhostHandlerState.Searching);
            }
            else
            {
                ghostHandler.EndHunt();
            }
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
