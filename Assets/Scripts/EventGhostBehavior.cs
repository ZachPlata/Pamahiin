using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody2D))]
public class EventGhostBehavior : NetworkBehaviour
{
    [Header("Dependencies")]
    public GhostHandler ghostHandler; // The Group Leader
    
    [Header("Settings")]
    public float wanderRadius = 3f;
    public float wanderSpeed = 1.5f;

    [Header("Evidence Mechanics")]
    public float interactionScanRadius = 3f;
    public LayerMask interactionLayer;
    public SpriteRenderer dotsSilhouette;
    public float dotsDashSpeed = 10f;
    
    private float nextInteractionScanTime = 0f;
    private float interactionScanInterval = 3f;
    private bool isDashing = false;
    private Vector2 dashTarget;

    private Rigidbody2D rb;
    private Vector3 initialSpawnLocation;
    private Vector3 currentDestination;
    private float waitTimer = 0f;
    private bool isInitialized = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void InitializeOrigin(Vector3 origin)
    {
        initialSpawnLocation = origin;
        currentDestination = origin;
        rb.position = origin;
        isInitialized = true;
    }

    private void Update()
    {
        if (!IsServer || !isInitialized) return;

        if (ghostHandler != null && ghostHandler.CurrentState == GhostHandlerState.Dormant)
        {
            if (Time.time >= nextInteractionScanTime)
            {
                nextInteractionScanTime = Time.time + interactionScanInterval;
                ScanForInteractions();
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer || !isInitialized) return;

        // Only move if we are the active phase according to GhostHandler
        if (ghostHandler != null && ghostHandler.CurrentState != GhostHandlerState.Dormant)
            return;

        if (isDashing)
        {
            MoveTowards(dashTarget, dotsDashSpeed);
            if (Vector2.Distance(rb.position, dashTarget) < 0.2f)
            {
                isDashing = false;
                if (dotsSilhouette != null) dotsSilhouette.enabled = false;
            }
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

    private void ScanForInteractions()
    {
        Collider2D[] cols = Physics2D.OverlapCircleAll(rb.position, interactionScanRadius, interactionLayer);
        foreach (var col in cols)
        {
            // 1. Ghost Writing
            var book = col.GetComponent<GhostWritingBookItem>();
            if (book != null && book.IsOpened && ghostHandler != null && ghostHandler.HasEvidence(EvidenceType.GhostWriting))
            {
                book.WriteInBook();
                if (ParanormalManager.Instance != null)
                    ParanormalManager.Instance.RegisterEvent(rb.position, 2, 10f);
            }

            // 2. Door Interactions
            var door = col.GetComponent<NetworkDoor>();
            if (door != null && Random.value < 0.2f) // 20% chance to interact if near a door
            {
                door.GhostInteract();
            }

            // 3. Object Throws
            var rbObj = col.GetComponent<Rigidbody2D>();
            if (rbObj != null && rbObj != rb && rbObj.bodyType == RigidbodyType2D.Dynamic && Random.value < 0.1f) // 10% chance to throw physical objects
            {
                Vector2 throwForce = Random.insideUnitCircle * Random.Range(3f, 8f);
                rbObj.AddForce(throwForce, ForceMode2D.Impulse);
                
                if (ParanormalManager.Instance != null)
                    ParanormalManager.Instance.RegisterEvent(rb.position, 3, 15f); // Throw event (EMF 3)
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!IsServer || isDashing) return;
        
        var dots = collision.GetComponent<DotsProjectorItem>();
        if (dots != null && dots.IsPoweredOn && ghostHandler != null && ghostHandler.HasEvidence(EvidenceType.DOTS))
        {
            isDashing = true;
            if (dotsSilhouette != null) dotsSilhouette.enabled = true;
            
            // Dash slightly past the projector
            Vector2 dirToDots = ((Vector2)dots.transform.position - rb.position).normalized;
            dashTarget = rb.position + dirToDots * (dots.projectionRadius * 1.5f);
            
            if (ParanormalManager.Instance != null)
                ParanormalManager.Instance.RegisterEvent(rb.position, 2, 5f);
        }
    }
}
