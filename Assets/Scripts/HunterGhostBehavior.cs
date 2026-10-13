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

    // Network synchronization & visuals
    private NetworkVariable<Vector2> netPosition = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> netRotation = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Light2D ghostLight;
    private bool isExecutingKill = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        // Return to Kinematic so NavMeshAgent can move the transform natively without physics corner-snags
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        if (ghostHandler == null) ghostHandler = GetComponentInParent<GhostHandler>();

        // Setup NavMeshAgent for 2D
        agent = GetComponent<NavMeshAgent>();
        if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();

        agent.updatePosition = true; // Let NavMeshAgent perfectly move the transform
        agent.updateRotation = false; // We handle rotation manually for vision cone accuracy
        agent.updateUpAxis = false;   // Essential for 2D NavMesh (XY plane)
        agent.acceleration = agentAcceleration;
        agent.stoppingDistance = agentStoppingDistance;
        agent.autoBraking = false;

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

    public void OnHuntStarted()
    {
        if (!IsServer) return;
        Debug.Log("HunterGhost: OnHuntStarted called. NavMesh Activated.");

        // Ensure the agent is snapped to the nearest valid NavMesh position
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
            else
            {
                agent.Warp(transform.position);
            }
            if (!agent.isOnNavMesh) Debug.LogWarning("HunterGhost: FAILED to snap to NavMesh! Ensure the room marker is on a baked area.");
        }

        nodesVisitedCount = 0;
        chaseTarget = null;
        targetedHidingDoor = null;
        isTrackingEquipment = false;
        chaseLostTimer = 0f;
        recentNodes.Clear();
        targetNode = FindNearestNode(transform.position);
    }

    private void Update()
    {
        if (ghostLight != null)
        {
            var sr = GetComponentInChildren<SpriteRenderer>();
            ghostLight.enabled = sr != null && sr.enabled;
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
            if (ghostHandler != null)
            {
                // Sync NavMeshAgent speed (Angered state moves much faster!)
                float currentBaseSpeed = (ghostHandler.CurrentState == GhostHandlerState.Chasing) ? chaseSpeed : moveSpeed;
                if (isAngeredState)
                {
                    currentBaseSpeed *= 2.2f;
                }
                agent.speed = isRepelled ? (moveSpeed * 0.5f) : currentBaseSpeed;

                if (ghostHandler.CurrentState == GhostHandlerState.Chasing)
                {
                    UpdateChasing();
                }
                else if (ghostHandler.CurrentState == GhostHandlerState.Searching || ghostHandler.CurrentState == GhostHandlerState.Returning)
                {
                    UpdateNodePathfinding();
                }

                // Scan for players & check door obstructions
                if (ghostHandler.CurrentState != GhostHandlerState.Dormant)
                {
                    CheckLineOfSight();
                    CheckDoorObstructions();
                }
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
                RaycastHit2D killCheck = Physics2D.Raycast(transform.position, dirToPlayer.normalized, distance, combinedMask);
                bool canKill = (killCheck.collider == null);
                if (!canKill)
                {
                    var door = GetDoorFromCollider(killCheck.collider);
                    if (door != null && door.IsOpen)
                    {
                        canKill = true;
                    }
                }

                if (canKill && !isExecutingKill)
                {
                    if (chaseTarget.KillPlayer())
                    {
                        NotifyPlayerCaughtClientRpc(chaseTarget.OwnerClientId);
                        StartCoroutine(KillSequenceRoutine());
                        return;
                    }
                }
            }
        }
        else
        {
            // ChaseTarget is null: Navigate to last known player position
            SetAgentDestination(lastKnownPlayerPos);

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

    private void CheckLineOfSight()
    {
        LayerMask combinedMask = GetCombinedObstacleMask();

        // 1. Scan for ACTIVE GHOST EQUIPMENT in hand
        activeEquipmentScanTimer -= Time.deltaTime;
        if (activeEquipmentScanTimer <= 0f)
        {
            activeEquipmentScanTimer = 0.5f;
            foreach (var player in PlayerController.AllPlayers)
            {
                if (player != null && player.IsAlive && IsPlayerEquipmentActive(player))
                {
                    float dist = Vector2.Distance(transform.position, player.transform.position);
                    if (dist <= lineOfSightDistance * 1.5f)
                    {
                        // Check if player is hiding inside or near a closed cabinet/closet
                        var allDoors = Object.FindObjectsByType<NetworkDoor>(FindObjectsInactive.Exclude);
                        foreach (var d in allDoors)
                        {
                            if (d != null && !d.IsFrontDoor && d.IsHidingSpot && !d.IsOpen)
                            {
                                if (Vector2.Distance(player.transform.position, d.transform.position) <= 1.5f)
                                {
                                    targetedHidingDoor = d;
                                    break;
                                }
                            }
                        }

                        if (chaseTarget == null) Debug.Log($"HunterGhost: Detected active equipment on player '{player.name}'! Chasing!");
                        chaseTarget = player;
                        lastKnownPlayerPos = player.transform.position;
                        chaseLostTimer = chaseMemoryDuration;
                        isTrackingEquipment = true;
                        nodesVisitedCount = 0;
                        ghostHandler.RequestStateChange(GhostHandlerState.Chasing);
                        return;
                    }
                }
            }
        }

        // 2. Visual Line of Sight scan
        foreach (var player in PlayerController.AllPlayers)
        {
            if (player == null || !player.IsAlive) continue;

            float distance = Vector2.Distance(transform.position, player.transform.position);
            float currentLOS = (ghostHandler.CurrentState == GhostHandlerState.Chasing)
                ? lineOfSightDistance * 1.5f
                : lineOfSightDistance;

            if (distance <= currentLOS && CheckHasVisualLOS(player, distance, combinedMask))
            {
                if (chaseTarget == null)
                {
                    if (ghostHandler.CurrentState == GhostHandlerState.Chasing)
                        Debug.Log("HunterGhost: Player spotted! Re-acquiring target!");
                    else
                        Debug.Log("HunterGhost: I SEE THE PLAYER! Chasing!");
                }

                chaseTarget = player;
                lastKnownPlayerPos = player.transform.position;
                chaseLostTimer = chaseMemoryDuration;
                nodesVisitedCount = 0;
                ghostHandler.RequestStateChange(GhostHandlerState.Chasing);
                return;
            }
        }
    }

    private bool CheckHasVisualLOS(PlayerController player, float distance, LayerMask combinedMask)
    {
        if (player == null || !player.IsAlive) return false;
        if (distance > lineOfSightDistance * 1.5f) return false;
        if (distance < 0.05f) return true;

        Vector2 dir = (player.transform.position - transform.position).normalized;
        RaycastHit2D hit = Physics2D.Raycast(transform.position, dir, distance, combinedMask);
        if (hit.collider == null) return true;

        var door = GetDoorFromCollider(hit.collider);
        if (door != null && door.IsOpen)
        {
            return true;
        }

        // Check if the obstruction is a closed cabinet/closet door that conceals the player
        if (door != null && !door.IsFrontDoor && door.IsHidingSpot && !door.IsOpen)
        {
            if (Vector2.Distance(player.transform.position, door.transform.position) <= 1.5f)
            {
                targetedHidingDoor = door;
                return true;
            }
        }

        return false;
    }

    private bool IsPlayerEquipmentActive(PlayerController player)
    {
        if (player == null || !player.IsAlive) return false;
        var inventory = player.GetComponent<PlayerInventory>();
        return inventory != null && inventory.CurrentItem != null && inventory.CurrentItem.IsPoweredOn;
    }

    private void SetAgentDestination(Vector3 targetPos)
    {
        if (agent == null || !agent.isOnNavMesh) return;
        if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
        else
        {
            agent.SetDestination(targetPos);
        }
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
                        if (distPlayerToDoor <= 1.5f)
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

        if (agent != null)
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

        if (agent != null)
        {
            // Flee away from repel source
            Vector3 fleeDir = (transform.position - repelSource).normalized;
            if (fleeDir.sqrMagnitude < 0.01f) fleeDir = Random.insideUnitCircle.normalized;
            Vector3 fleeTarget = transform.position + fleeDir * 6f;
            agent.SetDestination(fleeTarget);
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

        Vector3 originalPos = transform.position;
        Vector3 originalScale = transform.localScale;
        if (originalScale.sqrMagnitude < 0.01f)
        {
            originalScale = new Vector3(0.266f, 0.266f, 1f);
        }
        transform.localScale = originalScale;

        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            sr.enabled = true;
            sr.color = Color.white;
            sr.sortingLayerName = "Item";
            sr.sortingOrder = 50; // Render cleanly above diagram, floor, and players
        }

        AudioClip clipToPlay = dyingClip != null ? dyingClip : CreateProceduralShriekClip();
        if (clipToPlay != null)
        {
            AudioSource.PlayClipAtPoint(clipToPlay, transform.position, 1f);
        }

        float duration = 2.8f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;

            // Violently shake
            float shakeMagnitude = Mathf.Lerp(0.22f, 0.02f, progress);
            transform.position = originalPos + (Vector3)(Random.insideUnitCircle * shakeMagnitude);

            // Shrink and fade
            transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, progress);
            if (sr != null)
            {
                Color c = Color.white;
                c.a = Mathf.Lerp(1f, 0f, progress);
                sr.color = c;
            }

            yield return null;
        }

        if (sr != null)
        {
            sr.enabled = false;
        }

        // Notify ghostHandler that the ghost is exorcised so it halts hunts/events,
        // but DO NOT despawn or destroy the Ghost Handler hierarchy so Ghost Type data is preserved.
        if (ghostHandler != null)
        {
            ghostHandler.OnGhostExorcised();
        }

        gameObject.SetActive(false);
    }

    private static AudioClip CreateProceduralShriekClip()
    {
        int sampleRate = 44100;
        float duration = 2.8f;
        int sampleCount = Mathf.FloorToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float progress = t / duration;

            // Pitch swoops from high scream (~880 Hz) downward to low guttural growl (~140 Hz)
            float freq = Mathf.Lerp(880f, 140f, Mathf.Pow(progress, 0.7f));
            
            // Vibrato / erratic warble
            float vibrato = Mathf.Sin(2f * Mathf.PI * 18f * t) * 60f;
            float phase = 2f * Mathf.PI * (freq + vibrato) * t;

            // Base tone with eerie harmonics
            float wave = Mathf.Sin(phase) * 0.5f 
                       + Mathf.Sin(phase * 1.5f) * 0.25f 
                       + Mathf.Sin(phase * 2.3f) * 0.15f;

            // Add harsh noisy rasp
            float noise = (Random.value * 2f - 1f) * 0.35f;
            float raw = (wave + noise);

            // Envelope: quick attack, sustained loud shriek, violent fadeout
            float envelope;
            if (progress < 0.08f) envelope = progress / 0.08f;
            else if (progress > 0.7f) envelope = Mathf.Lerp(1f, 0f, (progress - 0.7f) / 0.3f);
            else envelope = 1f;

            samples[i] = Mathf.Clamp(raw * envelope * 0.8f, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("ProceduralGhostShriek", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private System.Collections.IEnumerator KillSequenceRoutine()
    {
        isExecutingKill = true;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

        // Reveal ghost fully over the player so the player actually sees their killer
        ShowKillPresenceClientRpc();

        yield return new WaitForSeconds(1.8f);

        isExecutingKill = false;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
        if (ghostHandler != null)
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
