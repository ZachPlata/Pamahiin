using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float sprintSpeed = 5.5f;

    [Header("Interaction Settings")]
    [SerializeField] private float interactRange = 1.5f;
    [SerializeField] private LayerMask interactLayer;
    
    private Rigidbody2D rb;
    private Vector2 moveInput;
    private Vector2 mousePosition;
    
    private Camera localCamera;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            localCamera = Camera.main;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        // 1. Gather WASD input
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        moveInput = new Vector2(moveX, moveY).normalized;

        // 2. Gather Mouse Position safely
        if (localCamera != null)
        {
            Vector3 mouseScreen = Input.mousePosition;
            // Force the Z depth to match the camera's distance so the world point calculates perfectly
            mouseScreen.z = Mathf.Abs(localCamera.transform.position.z);
            mousePosition = localCamera.ScreenToWorldPoint(mouseScreen);
        }
        else
        {
            // Fallback just in case the camera wasn't found immediately
            localCamera = Camera.main;
        }

        // 3. Interact Input
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryInteract();
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;

        // 1. Apply Movement
        float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
        rb.MovePosition(rb.position + moveInput * currentSpeed * Time.fixedDeltaTime);

        // 2. Apply Rotation (Look at Mouse)
        Vector2 lookDirection = mousePosition - rb.position;
        float angle = Mathf.Atan2(lookDirection.y, lookDirection.x) * Mathf.Rad2Deg - 90f;
        
        // MoveRotation ensures physics and networking sync smoothly
        rb.MoveRotation(angle);
    }

    private void TryInteract()
    {
        // Draw an invisible circle around the player to check for objects
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, interactRange, interactLayer);
        
        IInteractable closestInteractable = null;
        float closestDistance = float.MaxValue;

        foreach (var hit in hits)
        {
            // Check if the object we hit has a script using our IInteractable interface
            IInteractable interactable = hit.GetComponent<IInteractable>();
            if (interactable != null)
            {
                // Calculate how far away this specific object is
                float distance = Vector2.Distance(transform.position, hit.transform.position);
                
                // If it's closer than the last one we checked, make it our new target
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestInteractable = interactable;
                }
            }
        }

        // Only interact with the single closest object we found
        if (closestInteractable != null)
        {
            closestInteractable.Interact();
        }
    }
}