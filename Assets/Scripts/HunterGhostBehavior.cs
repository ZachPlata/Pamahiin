using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;

[RequireComponent(typeof(Rigidbody2D))]
public class HunterGhostBehavior : NetworkBehaviour
{
    [Header("Dependencies")]
    public GhostHandler ghostHandler; // The Group Leader

    [Header("Hunt Settings")]
    public float moveSpeed = 2.5f;
    public float chaseSpeed = 3.8f;
    public int maxNodesBeforeReturn = 8; // Kept for inspector compatibility

    [Header("LOS Settings")]
    public float lineOfSightDistance = 8f;
    public float lineOfSightAngle = 90f;
    public LayerMask obstacleLayer; // Kept for inspector compatibility

    private Rigidbody2D rb;
    private NavMeshAgent agent;
    private GhostNode targetNode;
    private int nodesVisitedCount = 0;

    private PlayerController chaseTarget;
    private Vector3 lastKnownPlayerPos;

    private float activeEquipmentScanTimer = 0f;

    private System.Collections.Generic.Queue<GhostNode> recentNodes = new System.Collections.Generic.Queue<GhostNode>();
    private int historySize = 3;

    // Breadcrumb cache for chasing (Kept for backwards compatibility with Dev Console)
    public static bool ShowBreadcrumbs = false;
    private LineRenderer breadcrumbLine;

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
        
        // Setup dummy LineRenderer for backwards compatibility with Dev Console Button
        breadcrumbLine = gameObject.AddComponent<LineRenderer>();
        breadcrumbLine.enabled = false;
    }

    public void OnHuntStarted()
    {
        if (!IsServer) return;
        Debug.Log("HunterGhost: OnHuntStarted called. NavMesh Activated.");

        // Ensure the agent is snapped to the NavMesh
        if (!agent.isOnNavMesh)
        {
            agent.Warp(transform.position);
            if (!agent.isOnNavMesh) Debug.LogWarning("HunterGhost: FAILED to snap to NavMesh! Ensure the room marker is on a baked area.");
        }

        nodesVisitedCount = 0;
        chaseTarget = null;
        recentNodes.Clear();
        targetNode = FindNearestNode(transform.position);
    }

    private void Update()
    {
        if (IsServer)
        {
            if (ghostHandler != null)
            {
                // Sync NavMeshAgent speed
                agent.speed = (ghostHandler.CurrentState == GhostHandlerState.Chasing) ? chaseSpeed : moveSpeed;

                if (ghostHandler.CurrentState == GhostHandlerState.Chasing)
                {
                    UpdateChasing();
                }
                else if (ghostHandler.CurrentState == GhostHandlerState.Searching || ghostHandler.CurrentState == GhostHandlerState.Returning)
                {
                    UpdateNodePathfinding();
                }

                // Scan for players
                if (ghostHandler.CurrentState != GhostHandlerState.Dormant)
                {
                    CheckLineOfSight();
                }
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // Manually rotate the ghost to face its velocity (NavMeshAgent velocity)
        if (agent != null && agent.velocity.sqrMagnitude > 0.01f)
        {
            float targetAngle = Mathf.Atan2(agent.velocity.y, agent.velocity.x) * Mathf.Rad2Deg - 90f;
            rb.MoveRotation(Mathf.LerpAngle(rb.rotation, targetAngle, Time.fixedDeltaTime * 12f));
        }
    }

    private void UpdateChasing()
    {
        if (chaseTarget != null && chaseTarget.IsAlive && !chaseTarget.IsHiddenFromGhost)
        {
            lastKnownPlayerPos = chaseTarget.transform.position;
            agent.SetDestination(chaseTarget.transform.position);
            
            Vector2 dirToPlayer = chaseTarget.transform.position - (Vector3)transform.position;
            float distance = dirToPlayer.magnitude;
            
            bool loseSight = distance > lineOfSightDistance * 1.5f;
            if (!loseSight && distance > 0.5f)
            {
                RaycastHit2D hit = Physics2D.Raycast(transform.position, dirToPlayer.normalized, distance, obstacleLayer);
                if (hit.collider != null) loseSight = true;
            }
            
            if (loseSight)
            {
                if (chaseTarget != null) Debug.Log("HunterGhost: Lost sight of player. Moving to last known position.");
                chaseTarget = null;
            }
            
            if (chaseTarget != null && distance < 0.125f) // Using your 0.125 tight kill range
            {
                if (chaseTarget.KillPlayer())
                {
                    ghostHandler.EndHunt();
                }
            }
        }
        else
        {
            agent.SetDestination(lastKnownPlayerPos);
            if (Vector2.Distance(transform.position, lastKnownPlayerPos) < 0.5f)
            {
                Debug.Log("HunterGhost: Reached last known position. Searching for nodes again.");
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

        agent.SetDestination(targetNode.transform.position);

        if (Vector2.Distance(transform.position, targetNode.transform.position) < 0.3f || (agent.hasPath && agent.remainingDistance < 0.3f))
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
        // Scan for active equipment first
        activeEquipmentScanTimer -= Time.deltaTime;
        if (activeEquipmentScanTimer <= 0f)
        {
            activeEquipmentScanTimer = 1f;
            foreach (var player in PlayerController.AllPlayers)
            {
                if (player.IsAlive)
                {
                    var inventory = player.GetComponent<PlayerInventory>();
                    if (inventory != null && inventory.CurrentItem != null && inventory.CurrentItem.IsPoweredOn)
                    {
                        if (chaseTarget == null) Debug.Log("HunterGhost: I SEE THE PLAYER! Chasing!");
                        chaseTarget = player;
                        ghostHandler.RequestStateChange(GhostHandlerState.Chasing);
                        return;
                    }
                }
            }
        }

        foreach (var player in PlayerController.AllPlayers)
        {
            if (player == null || !player.IsAlive) continue;
            if (player.IsHiddenFromGhost) continue;

            Vector2 dirToPlayer = player.transform.position - (Vector3)transform.position;
            float distance = dirToPlayer.magnitude;

            float currentLOS = ghostHandler.CurrentState == GhostHandlerState.Chasing 
                ? lineOfSightDistance * 1.5f 
                : lineOfSightDistance;

            if (distance <= currentLOS)
            {
                bool hasLOS = distance < 0.5f;
                if (!hasLOS)
                {
                    RaycastHit2D hit = Physics2D.Raycast(transform.position, dirToPlayer.normalized, distance, obstacleLayer);
                    if (hit.collider == null) hasLOS = true;
                }

                if (hasLOS)
                {
                    if (chaseTarget == null)
                    {
                        if (ghostHandler.CurrentState == GhostHandlerState.Chasing)
                            Debug.Log("HunterGhost: Player peeked! Re-acquiring target instead of following breadcrumbs!");
                        else
                            Debug.Log("HunterGhost: I SEE THE PLAYER! Chasing!");
                    }
                    
                    chaseTarget = player;
                    nodesVisitedCount = 0;
                    ghostHandler.RequestStateChange(GhostHandlerState.Chasing);
                    return;
                }
            }
        }
    }

    private GhostNode FindNearestNode(Vector3 position)
    {
        GhostNode[] allNodes = FindObjectsByType<GhostNode>(FindObjectsSortMode.None);
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

        System.Collections.Generic.List<GhostNode> validNeighbors = new System.Collections.Generic.List<GhostNode>();
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

        System.Collections.Generic.List<GhostNode> validNeighbors = new System.Collections.Generic.List<GhostNode>();
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
}
