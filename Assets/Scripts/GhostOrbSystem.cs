using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Controls the movement of a single Ghost Orb.
/// Ghost Orbs are instantiated by the GhostController in the Favorite Room if the ghost type supports it.
/// They are placed on a specific "GhostOrbs" layer which is only seen by the Video Camera.
/// </summary>
public class GhostOrbSystem : NetworkBehaviour
{
    [Header("Orb Movement Settings")]
    [SerializeField] private float moveSpeed = 0.5f;
    [SerializeField] private float changeDirectionInterval = 2f;
    
    private Vector2 anchorPosition;
    private float roamRadius = 2f;
    private Vector2 targetPosition;
    private float timer = 0f;

    public void Initialize(Vector2 anchor, float radius)
    {
        anchorPosition = anchor;
        roamRadius = radius;
        PickNewTarget();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            // The renderer is handled locally, but we need to ensure the layer is correct
            gameObject.layer = LayerMask.NameToLayer("GhostOrbs");
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        timer += Time.deltaTime;
        if (timer >= changeDirectionInterval)
        {
            PickNewTarget();
            timer = 0f;
        }

        transform.position = Vector2.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
    }

    private void PickNewTarget()
    {
        Vector2 randomDir = Random.insideUnitCircle;
        targetPosition = anchorPosition + (randomDir * roamRadius);
    }
}
