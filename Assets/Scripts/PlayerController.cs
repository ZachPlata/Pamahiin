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

    [Header("Interaction Settings")]
    [SerializeField] private float interactRange = 1.5f;
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

    /// <summary>
    /// Player is hidden from ghost vision raycasts when crouched inside a designated HideZone.
    /// </summary>
    public bool IsHiddenFromGhost => isCrouching.Value && IsInHideZone;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Vector2 moveInput;
    private Vector2 mousePosition;
    private Camera localCamera;

    private Vector3 originalScale;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalScale = transform.localScale;
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
            // Simple sanity drain over time
            sanity.Value = Mathf.Max(0f, sanity.Value - Time.deltaTime * 0.5f);
        }

        if (!IsOwner || !isAlive.Value) return;

        // 1. Gather WASD input
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        moveInput = new Vector2(moveX, moveY).normalized;

        // 2. Crouch input (Ctrl or C)
        bool crouchHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
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

        // 4. Interact Input
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryInteract();
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner || !isAlive.Value) return;

        // 1. Determine current speed
        float currentSpeed = walkSpeed;
        if (isCrouching.Value)
        {
            currentSpeed = crouchSpeed;
        }
        else if (Input.GetKey(KeyCode.LeftShift))
        {
            currentSpeed = sprintSpeed;
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

    private void OnCrouchStateChanged(bool crouched)
    {
        // Visual posture feedback: scale slightly down when crouching
        transform.localScale = crouched ? originalScale * 0.85f : originalScale;
    }

    private void OnAliveStateChanged(bool alive)
    {
        if (!alive)
        {
            // Death state visuals and collision
            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            }

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

    private void TryInteract()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, interactRange, interactLayer);

        IInteractable closestInteractable = null;
        float closestDistance = float.MaxValue;

        foreach (var hit in hits)
        {
            IInteractable interactable = hit.GetComponent<IInteractable>();
            if (interactable != null)
            {
                float distance = Vector2.Distance(transform.position, hit.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestInteractable = interactable;
                }
            }
        }

        if (closestInteractable != null)
        {
            closestInteractable.Interact();
        }
    }

    public void RestoreSanity(float amount)
    {
        if (!IsServer) return;
        sanity.Value = Mathf.Min(100f, sanity.Value + amount);
    }
}