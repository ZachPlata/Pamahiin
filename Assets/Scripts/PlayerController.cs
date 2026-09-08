using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    public static readonly List<PlayerController> AllPlayers = new List<PlayerController>();

    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float sprintSpeed = 5.5f;
    [SerializeField] private float crouchSpeed = 1.6f;

    [Header("Stamina Settings")]
    [SerializeField] private float maxStamina = 1.25f;
    [SerializeField] private float staminaRegenRate = 0.5f;
    private float currentStamina;

    [Header("Interaction Settings")]
    [SerializeField] private float maxReachRadius = 2.5f;
    [SerializeField] private LayerMask interactLayer;

    // Network states
    private NetworkVariable<bool> isAlive = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isCrouching = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<float> sanity = new NetworkVariable<float>(
        100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsAlive => isAlive.Value;
    public bool IsCrouching => isCrouching.Value;
    public float Sanity => sanity.Value;
    public bool IsInHideZone { get; private set; }
    public HideZone CurrentHideZone { get; private set; }
    public bool IsInSafeZone { get; private set; }

    /// <summary>
    /// Player is hidden from ghost vision raycasts when crouched inside a designated HideZone.
    /// </summary>
    public bool IsHiddenFromGhost => isCrouching.Value && IsInHideZone;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private PlayerInventory inventory;
    private Vector2 moveInput;
    private Vector2 mousePosition;
    private Camera localCamera;

    private Vector3 originalScale;
    private IInteractable currentDraggedInteractable = null;

    private float hoverCheckTimer = 0f;
    private IInteractable currentlyHoveredInteractable = null;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        inventory = GetComponent<PlayerInventory>();
        originalScale = transform.localScale;
        currentStamina = maxStamina;
    }

    public override void OnNetworkSpawn()
    {
        AllPlayers.Add(this);

        if (IsOwner)
        {
            localCamera = Camera.main;
        }

        isAlive.OnValueChanged += (oldVal, newVal) => OnAliveStateChanged(newVal);
        isCrouching.OnValueChanged += (oldVal, newVal) => OnCrouchStateChanged(newVal);
    }

    public override void OnNetworkDespawn()
    {
        AllPlayers.Remove(this);
    }

    private void Update()
    {
        if (IsServer && isAlive.Value)
        {
            bool inDark = true;
            if (inventory != null && inventory.CurrentItem is FlashlightItem flashlight)
            {
                if (flashlight.IsLightOn) inDark = false;
            }

            if (inDark && !IsInSafeZone)
            {
                // Light-based sanity drain (only drain in the dark and outside safe zones)
                sanity.Value = Mathf.Max(0f, sanity.Value - Time.deltaTime * 0.5f);
            }
        }

        if (!IsOwner || !isAlive.Value) return;

        // 1. Gather WASD input
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        moveInput = new Vector2(moveX, moveY).normalized;

        // 2. Crouch input (C)
        bool crouchHeld = Input.GetKey(KeyCode.C);
        if (crouchHeld != isCrouching.Value)
        {
            isCrouching.Value = crouchHeld;
        }

        // 3. Mouse Position
        if (localCamera != null)
        {
            Vector3 mouseScreen = Input.mousePosition;
            mouseScreen.z = Mathf.Abs(localCamera.transform.position.z);
            mousePosition = localCamera.ScreenToWorldPoint(mouseScreen);
        }
        else
        {
            localCamera = Camera.main;
        }

        // Check for hovered interactable every 0.1 seconds
        hoverCheckTimer -= Time.deltaTime;
        if (hoverCheckTimer <= 0f)
        {
            currentlyHoveredInteractable = CalculateHoveredInteractable();
            hoverCheckTimer = 0.1f;
        }

        // 4. Targeted Interact Input (E)
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryInteract();
        }

        // 5. Contextual Left Click (Drag vs Use Primary vs Interact)
        if (Input.GetMouseButtonDown(0))
        {
            var hovered = GetHoveredInteractable();
            if (hovered != null)
            {
                if (hovered.CanDrag())
                {
                    currentDraggedInteractable = hovered;
                    currentDraggedInteractable.OnDragBegin(NetworkManager.Singleton.LocalClientId);
                }
                else
                {
                    hovered.Interact();
                }
            }
            else
            {
                if (inventory != null) inventory.UsePrimary();
            }
        }

        if (Input.GetMouseButtonUp(0) && currentDraggedInteractable != null)
        {
            currentDraggedInteractable.OnDragEnd(NetworkManager.Singleton.LocalClientId);
            currentDraggedInteractable = null;
        }

        if (currentDraggedInteractable != null)
        {
            Vector2 dragTarget = mousePosition;
            Vector2 toTarget = dragTarget - (Vector2)transform.position;
            
            if (toTarget.magnitude > maxReachRadius)
            {
                dragTarget = (Vector2)transform.position + toTarget.normalized * maxReachRadius;
                toTarget = dragTarget - (Vector2)transform.position;
            }

            // Raycast to prevent dragging through walls
            int wallMask = LayerMask.GetMask("Walls");
            RaycastHit2D hit = Physics2D.Raycast(transform.position, toTarget.normalized, toTarget.magnitude, wallMask);
            if (hit.collider != null)
            {
                // Stop slightly before the wall
                dragTarget = hit.point - (toTarget.normalized * 0.2f);
            }

            currentDraggedInteractable.OnDragUpdate(dragTarget);
        }

        // 6. Place Item (Right Click)
        if (Input.GetMouseButtonDown(1))
        {
            if (Vector2.Distance(transform.position, mousePosition) <= maxReachRadius)
            {
                if (inventory != null) inventory.PlaceCurrentItem(mousePosition, transform.rotation);
            }
        }

        // 7. Drop Item (G)
        if (Input.GetKeyDown(KeyCode.G))
        {
            if (inventory != null) inventory.DropCurrentItem(transform.position);
        }

        // 8. Hooks for UI / Audio
        if (Input.GetKeyDown(KeyCode.J)) OpenJournal();
        if (Input.GetKeyDown(KeyCode.V)) ToggleLocalVoice();
        if (Input.GetKeyDown(KeyCode.B)) ToggleRadioVoice();
    }

    private void OpenJournal() { /* Stub for Journal UI */ }
    private void ToggleLocalVoice() { /* Stub for Proximity Chat */ }
    private void ToggleRadioVoice() { /* Stub for Radio Chat */ }

    private void FixedUpdate()
    {
        if (!IsOwner || !isAlive.Value) return;

        // 1. Determine current speed
        float currentSpeed = walkSpeed;
        if (isCrouching.Value)
        {
            currentSpeed = crouchSpeed;
        }
        else if (Input.GetKey(KeyCode.LeftShift) && currentStamina > 0f)
        {
            currentSpeed = sprintSpeed;
            currentStamina -= Time.fixedDeltaTime;
        }
        else
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenRate * Time.fixedDeltaTime);
        }

        rb.MovePosition(rb.position + moveInput * currentSpeed * Time.fixedDeltaTime);

        // 2. Apply Rotation (Look at Mouse)
        Vector2 lookDirection = mousePosition - rb.position;
        if (lookDirection.sqrMagnitude > 0.001f)
        {
            float angle = Mathf.Atan2(lookDirection.y, lookDirection.x) * Mathf.Rad2Deg - 90f;
            rb.MoveRotation(angle);
        }
    }

    public void SetInHideZone(bool inZone, HideZone zone)
    {
        IsInHideZone = inZone;
        CurrentHideZone = inZone ? zone : null;
    }

    public void SetInSafeZone(bool isSafe)
    {
        IsInSafeZone = isSafe;
    }

    private void OnCrouchStateChanged(bool crouched)
    {
        // Visual posture feedback: scale slightly down when crouching
        transform.localScale = crouched ? originalScale * 0.85f : originalScale;
    }

    private void OnAliveStateChanged(bool alive)
    {
        if (!alive)
        {
            if (IsOwner && inventory != null)
            {
                inventory.DropAllItems(transform.position);
            }

            // Death state visuals and collision
            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            }

            // Change layer to Spectator so ghosts and physics doors ignore them
            gameObject.layer = LayerMask.NameToLayer("Spectator");

            // Disable physics interaction
            var col = GetComponent<Collider2D>();
            if (col != null) col.enabled = false;
            rb.linearVelocity = Vector2.zero;
        }
    }

    /// <summary>
    /// Server method to kill a player caught by the ghost.
    /// </summary>
    public void KillPlayer()
    {
        if (!IsServer) return;
        isAlive.Value = false;
    }

    private IInteractable GetHoveredInteractable()
    {
        return currentlyHoveredInteractable;
    }

    private IInteractable CalculateHoveredInteractable()
    {
        if (Vector2.Distance(transform.position, mousePosition) > maxReachRadius) return null;

        // Use a very small precise radius for accurate hit detection
        Collider2D[] hits = Physics2D.OverlapCircleAll(mousePosition, 0.05f, interactLayer);
        
        IInteractable bestInteractable = null;
        float bestDistance = float.MaxValue;

        foreach (var hit in hits)
        {
            var interactable = hit.GetComponent<IInteractable>();
            if (interactable != null)
            {
                // Priority: The object whose center is closest to the mouse cursor.
                // This ensures small items on top of large interactables (like monitors) get priority.
                float dist = Vector2.Distance(mousePosition, hit.transform.position);
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    bestInteractable = interactable;
                }
            }
        }
        return bestInteractable;
    }

    private void TryInteract()
    {
        var hovered = GetHoveredInteractable();
        if (hovered != null)
        {
            hovered.Interact();
        }
    }

    public void RestoreSanity(float amount)
    {
        if (!IsServer) return;
        sanity.Value = Mathf.Min(100f, sanity.Value + amount);
    }
}