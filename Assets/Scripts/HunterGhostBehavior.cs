using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody2D))]
public class HunterGhostBehavior : NetworkBehaviour
{
    [Header("Dependencies")]
    public GhostHandler ghostHandler; // The Group Leader

    [Header("Node Settings")]
    public float moveSpeed = 3f;
    public float chaseSpeed = 4.5f;
    public int maxNodesBeforeReturn = 5;

    [Header("LOS Settings")]
    public float lineOfSightDistance = 8f;
    public float lineOfSightAngle = 90f;
    public LayerMask obstacleLayer;

    private Rigidbody2D rb;
    private GhostNode targetNode;
    private int nodesVisitedCount = 0;
    
    private PlayerController chaseTarget;
    private Vector3 lastKnownPlayerPos;

    private System.Collections.Generic.Queue<GhostNode> recentNodes = new System.Collections.Generic.Queue<GhostNode>();
    private int historySize = 3;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void OnHuntStarted()
    {
        if (!IsServer) return;
        Debug.Log("HunterGhost: OnHuntStarted called.");
        nodesVisitedCount = 0;
        chaseTarget = null;
        recentNodes.Clear();
        targetNode = FindNearestNode(rb.position);
        Debug.Log($"HunterGhost: Found nearest node: {(targetNode != null ? targetNode.gameObject.name : "NULL")}");
    }

    private void Update()
    {
        if (!IsServer) return;
        
        // Check LOS only if actively searching or returning
        if (ghostHandler != null && (ghostHandler.CurrentState == GhostHandlerState.Searching || ghostHandler.CurrentState == GhostHandlerState.Returning))
        {
            CheckLineOfSight();
        }

        if (ghostHandler != null && ghostHandler.CurrentState == GhostHandlerState.Chasing)
        {
            UpdateChasing();
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        if (ghostHandler != null && ghostHandler.CurrentState == GhostHandlerState.Chasing)
        {
            if (chaseTarget != null)
            {
                MoveTowards(chaseTarget.transform.position, chaseSpeed);
            }
            else
            {
                MoveTowards(lastKnownPlayerPos, chaseSpeed);
            }
        }
        else if (ghostHandler != null && ghostHandler.CurrentState != GhostHandlerState.Dormant)
        {
            // Node pathfinding runs when searching or returning
            UpdateNodePathfinding();
        }
    }

    private void CheckLineOfSight()
    {
        foreach (var player in PlayerController.AllPlayers)
        {
            if (player == null || !player.IsAlive) continue;
            if (player.IsHiddenFromGhost) continue;

            Vector2 dirToPlayer = player.transform.position - (Vector3)rb.position;
            float distance = dirToPlayer.magnitude;

            if (distance <= lineOfSightDistance)
            {
                float angle = Vector2.Angle(transform.up, dirToPlayer);
                if (angle <= lineOfSightAngle / 2f)
                {
                    RaycastHit2D hit = Physics2D.Raycast(rb.position, dirToPlayer.normalized, distance, obstacleLayer);
                    if (hit.collider == null)
                    {
                        chaseTarget = player;
                        nodesVisitedCount = 0;
                        ghostHandler.RequestStateChange(GhostHandlerState.Chasing);
                        return;
                    }
                }
            }
        }
    }

    private void UpdateChasing()
    {
        if (chaseTarget != null && chaseTarget.IsAlive && !chaseTarget.IsHiddenFromGhost)
        {
            lastKnownPlayerPos = chaseTarget.transform.position;
            
            Vector2 dirToPlayer = chaseTarget.transform.position - (Vector3)rb.position;
            float distance = dirToPlayer.magnitude;
            RaycastHit2D hit = Physics2D.Raycast(rb.position, dirToPlayer.normalized, distance, obstacleLayer);
            
            if (hit.collider != null || distance > lineOfSightDistance * 1.5f)
            {
                chaseTarget = null;
            }
            
            if (distance < 0.5f)
            {
                chaseTarget.KillPlayer();
                ghostHandler.EndHunt();
            }
        }
        else
        {
            if (Vector2.Distance(rb.position, lastKnownPlayerPos) < 0.5f)
            {
                recentNodes.Clear();
                targetNode = FindNearestNode(rb.position);
                ghostHandler.RequestStateChange(GhostHandlerState.Searching);
            }
        }
    }

    private void UpdateNodePathfinding()
    {
        if (targetNode == null)
        {
            targetNode = FindNearestNode(rb.position);
            if (targetNode == null) 
            {
                Debug.LogWarning("HunterGhost: Cannot pathfind, no nodes found in scene.");
                return;
            }
        }

        MoveTowards(targetNode.transform.position, moveSpeed);

        if (Vector2.Distance(rb.position, targetNode.transform.position) < 0.3f)
        {
            nodesVisitedCount++;
            
            if (!recentNodes.Contains(targetNode))
            {
                recentNodes.Enqueue(targetNode);
                if (recentNodes.Count > historySize) recentNodes.Dequeue();
            }
            
            if (ghostHandler != null && ghostHandler.ghostroomMarker != null && ghostHandler.ghostroomMarker.bounds.Contains(rb.position))
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

        // If all neighbors are in history (e.g. a dead end), allow turning back by clearing history
        if (validNeighbors.Count == 0)
        {
            foreach (var neighbor in current.neighbors)
            {
                if (neighbor != null) validNeighbors.Add(neighbor);
            }
            recentNodes.Clear();
        }

        if (validNeighbors.Count == 0) return current;

        // Apply directional bias
        GhostNode bestNode = validNeighbors[0];
        float bestScore = -float.MaxValue;

        Vector2 currentDir = transform.up; // The ghost's forward direction

        foreach (var node in validNeighbors)
        {
            Vector2 dirToNode = (node.transform.position - transform.position).normalized;
            // Dot product: 1 is straight ahead, 0 is perpendicular, -1 is directly behind
            float score = Vector2.Dot(currentDir, dirToNode);
            
            // Add a small random factor so it's not strictly deterministic
            score += Random.Range(-0.2f, 0.4f);
            
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
}
