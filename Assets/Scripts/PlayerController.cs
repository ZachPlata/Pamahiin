using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public class PlayerController : NetworkBehaviour
{
    public static readonly List<PlayerController> AllPlayers = new List<PlayerController>();
    public static PlayerController LocalInstance { get; private set; }

    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float sprintSpeed = 5.5f;
    [SerializeField] private float crouchSpeed = 1.6f;

    public float WalkSpeed { get => walkSpeed; set => walkSpeed = value; }
    public float SprintSpeed { get => sprintSpeed; set => sprintSpeed = value; }

    [Header("Stamina Settings")]
    [SerializeField] private float maxStamina = 1.25f;
    [SerializeField] private float staminaRegenRate = 0.5f;
    private float currentStamina;

    public float CurrentStamina { get => currentStamina; set => currentStamina = value; }
    public float MaxStamina { get => maxStamina; set => maxStamina = value; }

    [Header("Interaction Settings")]
    [SerializeField] private float maxReachRadius = 2.5f;
    [SerializeField] private LayerMask interactLayer;

    [Header("Rotation Settings")]
    [Tooltip("Deadzone distance from the player center where mouse angle will not update (prevents jitter when hovering over the character pivot).")]
    [SerializeField] private float rotationDeadzone = 0.03f;

    // Network states
    private NetworkVariable<bool> isAlive = new NetworkVariable<bool>(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isCrouching = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<bool> netIsWalking = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<bool> netIsHolding = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    [Header("Sanity Settings")]
    [Tooltip("Rate at which sanity drains per second while in the dark and outside safe zones (sped up by 0.5x to 0.75/s).")]
    [SerializeField] private float darkSanityDrainRate = 0.75f;

    private NetworkVariable<float> sanity = new NetworkVariable<float>(
        100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> isGodMode = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsAlive => isAlive.Value;
    public bool IsCrouching => isCrouching.Value;
    public float Sanity => sanity.Value;
    public float MaxReachRadius => maxReachRadius;
    public bool IsInHideZone { get; private set; }
    public HideZone CurrentHideZone { get; private set; }
    private readonly System.Collections.Generic.HashSet<SafeZone> activeSafeZones = new System.Collections.Generic.HashSet<SafeZone>();

    public bool IsInSafeZone
    {
        get
        {
            // 1. Clean up any inactive, disabled, or destroyed safe zones (e.g. lights turned off)
            activeSafeZones.RemoveWhere(sz => sz == null || !sz.gameObject.activeInHierarchy || !sz.enabled);
            if (activeSafeZones.Count > 0) return true;

            // 2. Physics overlap fallback (handles spawning inside trigger or standing in room when light turns on)
            Collider2D[] overlapping = Physics2D.OverlapCircleAll(transform.position, 0.25f);
            foreach (var col in overlapping)
            {
                if (col != null && col.isTrigger)
                {
                    var sz = col.GetComponent<SafeZone>();
                    if (sz != null && sz.isActiveAndEnabled)
                    {
                        activeSafeZones.Add(sz);
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Player is hidden from ghost vision raycasts when crouched inside a designated HideZone.
    /// </summary>
    public bool IsHiddenFromGhost => isCrouching.Value && IsInHideZone;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private PlayerInventory inventory;
    private Animator animator;
    private Vector2 moveInput;
    private Vector2 mousePosition;
    private Camera localCamera;

    private Vector3 originalScale;
    private IInteractable currentDraggedInteractable = null;

    private IInteractable currentlyHoveredInteractable = null;
    private EquipmentItem lastHoveredEquipment = null;
    private int cachedStackCount = 1;
    private float nextStackCountRefreshTime = 0f;
    private Light2D playerPovLight;

    private static readonly System.Collections.Generic.List<Collider2D> s_OverlapBuffer = new System.Collections.Generic.List<Collider2D>(16);
    private static readonly System.Collections.Generic.List<Collider2D> s_StackOverlapBuffer = new System.Collections.Generic.List<Collider2D>(16);
    private static readonly System.Collections.Generic.HashSet<EquipmentItem> s_CountedEquipment = new System.Collections.Generic.HashSet<EquipmentItem>();
    private static readonly ContactFilter2D s_NoFilter = ContactFilter2D.noFilter;

    [Header("Ambient Light Settings")]
    [SerializeField] private float playerAmbientRadius = 0.58f;
    [SerializeField] private float playerAmbientInnerRadius = 0.08f;
    [SerializeField] private float playerAmbientIntensity = 0.34f;
    [SerializeField] private float playerAmbientFalloff = 0.72f;
    [SerializeField] private Color playerAmbientColor = new Color(0.84f, 0.89f, 0.96f, 1f);

    public Light2D PlayerAmbientLight => playerPovLight;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.freezeRotation = true;
            rb.angularVelocity = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
        spriteRenderer = GetComponent<SpriteRenderer>();
        inventory = GetComponent<PlayerInventory>();
        animator = GetComponent<Animator>();
        originalScale = transform.localScale;
        currentStamina = maxStamina;
    }

    public override void OnNetworkSpawn()
    {
        AllPlayers.Add(this);

        if (TryGetComponent<Light2D>(out var rootLight))
        {
            rootLight.enabled = false;
        }

        if (IsOwner)
        {
            LocalInstance = this;
            localCamera = Camera.main;
            EnsurePlayerPOVLight();
            StartCoroutine(DelayedTeleportToSpawn());
            if (ItemHoverIndicator.Instance == null)
            {
                new GameObject("ItemHoverIndicator").AddComponent<ItemHoverIndicator>();
            }
            GameSettingsManager.Instance?.ResetMatchTutorialHints();
            StartCoroutine(DelayedMatchStartTutorialRoutine());
            StartCoroutine(DelayedJournalHintRoutine());
        }
        else if (playerPovLight != null)
        {
            playerPovLight.enabled = false;
        }

        isAlive.OnValueChanged += (oldVal, newVal) =>
        {
            OnAliveStateChanged(newVal);
            if (!IsOwner) UpdateRemoteAnimator();
        };
        isCrouching.OnValueChanged += (oldVal, newVal) => OnCrouchStateChanged(newVal);
        netIsWalking.OnValueChanged += (oldVal, newVal) => { if (!IsOwner) UpdateRemoteAnimator(); };
        netIsHolding.OnValueChanged += (oldVal, newVal) => { if (!IsOwner) UpdateRemoteAnimator(); };

        if (!IsOwner)
        {
            UpdateRemoteAnimator();
        }
    }

    private void EnsurePlayerPOVLight()
    {
        if (TryGetComponent<Light2D>(out var rootLight))
        {
            rootLight.enabled = false;
        }

        if (playerPovLight == null)
        {
            Transform existing = transform.Find("PlayerPOVLight");
            if (existing != null)
            {
                playerPovLight = existing.GetComponent<Light2D>();
            }
        }

        if (playerPovLight == null)
        {
            var povGo = new GameObject("PlayerPOVLight");
            povGo.transform.SetParent(transform, false);
            povGo.transform.localPosition = Vector3.zero;
            povGo.transform.localRotation = Quaternion.identity;
            playerPovLight = povGo.AddComponent<Light2D>();
        }

        UpdatePOVLightScale();
        ApplyPlayerAmbientLightSettings();
        playerPovLight.enabled = IsOwner && isAlive.Value;
    }

    private void ApplyPlayerAmbientLightSettings()
    {
        if (playerPovLight == null) return;

        playerPovLight.lightType = Light2D.LightType.Point;
        playerPovLight.color = playerAmbientColor;
        playerPovLight.intensity = playerAmbientIntensity;
        playerPovLight.falloffIntensity = playerAmbientFalloff;
        playerPovLight.pointLightInnerAngle = 360f;
        playerPovLight.pointLightOuterAngle = 360f;
        playerPovLight.pointLightInnerRadius = playerAmbientInnerRadius;
        playerPovLight.pointLightOuterRadius = playerAmbientRadius;
        playerPovLight.shadowsEnabled = true;
        playerPovLight.shadowIntensity = 1.0f;
        playerPovLight.shadowVolumeIntensity = 0f;
        playerPovLight.targetSortingLayers = System.Array.ConvertAll(SortingLayer.layers, layer => layer.id);
    }

    private void UpdatePOVLightScale()
    {
        if (playerPovLight == null) return;

        // Cancel current parent scale (including negative Y scale on Player.prefab and crouch 0.85x)
        // so the 360-degree circular light stays uniform in world space.
        Vector3 currentScale = transform.localScale;
        float sx = Mathf.Abs(currentScale.x) > 1e-5f ? 1f / currentScale.x : 1f;
        float sy = Mathf.Abs(currentScale.y) > 1e-5f ? 1f / currentScale.y : 1f;
        playerPovLight.transform.localScale = new Vector3(sx, sy, 1f);
    }

    private System.Collections.IEnumerator DelayedMatchStartTutorialRoutine()
    {
        yield return new WaitForSeconds(5f);
        TutorialTooltipManager.TriggerHint(TutorialHintId.Movement);
        TutorialTooltipManager.TriggerHint(TutorialHintId.ItemPickup);
    }

    private System.Collections.IEnumerator DelayedJournalHintRoutine()
    {
        yield return new WaitForSeconds(35f);
        TutorialTooltipManager.TriggerHint(TutorialHintId.Journal);
    }

    public override void OnNetworkDespawn()
    {
        EndCurrentDrag();
        AllPlayers.Remove(this);
        if (LocalInstance == this)
        {
            LocalInstance = null;
            ItemHoverIndicator.Instance?.Hide();
        }
    }

    private void EndCurrentDrag()
    {
        if (currentDraggedInteractable == null) return;

        if ((currentDraggedInteractable as Object) != null)
        {
            ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
            currentDraggedInteractable.OnDragEnd(localClientId);
        }
        currentDraggedInteractable = null;
    }

    /// <summary>
    /// Checks whether player movement and rotation should be blocked by open full-screen GUIs:
    /// Dev Console, Pause Menu, Truck Dashboard, Spirit Box Dialogue, or an active Journal text input.
    /// (Note: Journal keeps movement functional while reading, unless typing into its Party Code input).
    /// </summary>
    public static bool IsMovementBlocked =>
        DeveloperConsoleManager.IsConsoleOpen ||
        PauseMenuController.IsOpen ||
        TruckDashboardController.IsOpen ||
        SpiritBoxItem.IsLocalDialogueActive ||
        JournalController.IsTextInputFocused ||
        LoadingScreenManager.IsLoadingVisible ||
        EndGameRevealController.IsOpen;

    /// <summary>
    /// Checks whether world interactions (clicks, item usage, dragging, E interact, drop, place, slot switching)
    /// should be blocked by active GUIs (Journal, Pause Menu, Truck Dashboard, Dev Console, Spirit Box).
    /// </summary>
    public static bool IsInteractionBlocked =>
        IsMovementBlocked ||
        JournalController.IsOpen;

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
                sanity.Value = Mathf.Max(0f, sanity.Value - Time.deltaTime * darkSanityDrainRate);
            }
        }

        if (!IsOwner)
        {
            UpdateRemoteAnimator();
            return;
        }

        if (!isAlive.Value)
        {
            EndCurrentDrag();
            SetLocalAnimationState(false, false);
            return;
        }

        // Block movement and world interactions while in locking GUIs (Pause Menu, Truck Dashboard, Dev Console, Spirit Box)
        if (IsMovementBlocked)
        {
            moveInput = Vector2.zero;
            EndCurrentDrag();
            currentlyHoveredInteractable = null;
            ItemHoverIndicator.Instance?.Hide();
            SetLocalAnimationState(false, inventory != null && inventory.CurrentItem != null);
            return;
        }

        // 1. Gather WASD input (functional during normal gameplay and while Journal is open)
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        moveInput = new Vector2(moveX, moveY).normalized;

        // 2. Crouch input (C)
        bool crouchHeld = Input.GetKey(KeyCode.C);
        if (crouchHeld != isCrouching.Value)
        {
            isCrouching.Value = crouchHeld;
        }

        // If interaction is blocked (e.g. inside Journal), disable world interactions and clicks while keeping movement active
        if (IsInteractionBlocked)
        {
            EndCurrentDrag();
            currentlyHoveredInteractable = null;
            ItemHoverIndicator.Instance?.Hide();

            // Maintain walking animation state for WASD movement in Journal
            SetLocalAnimationState(moveInput.sqrMagnitude > 0.01f, inventory != null && inventory.CurrentItem != null);
            return;
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

        // Check for hovered interactable and update visual hover indicator every frame
        currentlyHoveredInteractable = CalculateHoveredInteractable();
        UpdateItemHoverIndicator();

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
                    ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
                    currentDraggedInteractable = hovered;
                    currentDraggedInteractable.OnDragBegin(localClientId);
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

        if ((Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0)) && currentDraggedInteractable != null)
        {
            EndCurrentDrag();
        }

        if (currentDraggedInteractable != null)
        {
            if ((currentDraggedInteractable as Object) == null || !currentDraggedInteractable.CanDrag())
            {
                EndCurrentDrag();
            }
            else
            {
                Vector2 dragTarget = mousePosition;
                Vector2 toTarget = dragTarget - (Vector2)transform.position;

                if (toTarget.magnitude > maxReachRadius)
                {
                    dragTarget = (Vector2)transform.position + toTarget.normalized * maxReachRadius;
                }

                currentDraggedInteractable.OnDragUpdate(dragTarget);
            }
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

        // 9. Update Animator and replicate animation state
        SetLocalAnimationState(moveInput.sqrMagnitude > 0.01f, inventory != null && inventory.CurrentItem != null);
    }

    private void SetLocalAnimationState(bool walking, bool holding)
    {
        if (animator != null)
        {
            animator.SetBool("isWalking", walking);
            animator.SetBool("isHolding", holding);
        }

        if (IsSpawned && IsOwner)
        {
            if (netIsWalking.Value != walking) netIsWalking.Value = walking;
            if (netIsHolding.Value != holding) netIsHolding.Value = holding;
        }
    }

    private void UpdateRemoteAnimator()
    {
        if (animator == null) return;

        if (!isAlive.Value)
        {
            animator.SetBool("isWalking", false);
            animator.SetBool("isHolding", false);
            return;
        }

        animator.SetBool("isWalking", netIsWalking.Value);
        animator.SetBool("isHolding", netIsHolding.Value || HasRemoteHeldEquipment());
    }

    private bool HasRemoteHeldEquipment()
    {
        if (!IsSpawned) return false;
        ulong clientId = OwnerClientId;
        var items = EquipmentItem.AllEquipment;
        for (int i = 0; i < items.Count; i++)
        {
            var eq = items[i];
            if (eq != null && eq.CurrentHolderClientId == clientId && eq.IsInHand)
            {
                return true;
            }
        }
        return false;
    }

    private void OpenJournal() { /* Stub for Journal UI */ }
    private void ToggleLocalVoice() { /* Stub for Proximity Chat */ }
    private void ToggleRadioVoice() { /* Stub for Radio Chat */ }

    private void FixedUpdate()
    {
        if (!IsOwner || !isAlive.Value) return;
        if (IsMovementBlocked) return;

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

        // While Journal is open, rotate along movement direction so mouse UI interaction doesn't rotate character
        if (JournalController.IsOpen)
        {
            if (moveInput.sqrMagnitude > 0.01f)
            {
                float moveAngle = Mathf.Atan2(moveInput.y, moveInput.x) * Mathf.Rad2Deg - 90f;
                rb.MoveRotation(moveAngle);
            }
            else
            {
                rb.angularVelocity = 0f;
            }
            return;
        }

        // Update mouse position synchronously with physics step
        if (localCamera != null)
        {
            Vector3 mouseScreen = Input.mousePosition;
            mouseScreen.z = Mathf.Abs(localCamera.transform.position.z);
            mousePosition = localCamera.ScreenToWorldPoint(mouseScreen);
        }

        // 2. Apply Rotation (Look at Mouse)
        Vector2 lookDirection = mousePosition - rb.position;
        if (lookDirection.sqrMagnitude > rotationDeadzone * rotationDeadzone)
        {
            float angle = Mathf.Atan2(lookDirection.y, lookDirection.x) * Mathf.Rad2Deg - 90f;
            rb.MoveRotation(angle);
        }
        else
        {
            rb.angularVelocity = 0f;
        }
    }

    public void SetInHideZone(bool inZone, HideZone zone)
    {
        IsInHideZone = inZone;
        CurrentHideZone = inZone ? zone : null;
    }

    public void AddSafeZone(SafeZone zone)
    {
        if (zone != null)
        {
            activeSafeZones.Add(zone);
        }
    }

    public void RemoveSafeZone(SafeZone zone)
    {
        if (zone != null)
        {
            activeSafeZones.Remove(zone);
        }
    }

    public void SetInSafeZone(bool isSafe)
    {
        if (!isSafe)
        {
            activeSafeZones.Clear();
        }
    }

    private void OnCrouchStateChanged(bool crouched)
    {
        // Visual posture feedback: scale slightly down when crouching
        transform.localScale = crouched ? originalScale * 0.85f : originalScale;
        UpdatePOVLightScale();
    }

    private void OnAliveStateChanged(bool alive)
    {
        if (!alive)
        {
            if (playerPovLight != null)
            {
                playerPovLight.enabled = false;
            }
            GameAudioManager.PlayGhostKill(transform.position);
            if (IsOwner)
            {
                EndCurrentDrag();
                ItemHoverIndicator.Instance?.Hide();
                if (inventory != null)
                {
                    inventory.DropAllItems(transform.position);
                }
            }

            // Death state visuals and collision
            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            }

            // Change layer to Ignore Raycast so ghosts and raycasts ignore dead spectators
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycastLayer >= 0)
            {
                gameObject.layer = ignoreRaycastLayer;
            }

            // Disable physics interaction
            var col = GetComponent<Collider2D>();
            if (col != null) col.enabled = false;
            rb.linearVelocity = Vector2.zero;
        }
        else
        {
            if (playerPovLight != null)
            {
                playerPovLight.enabled = IsOwner;
            }
            if (spriteRenderer != null)
            {
                spriteRenderer.color = Color.white;
            }

            gameObject.layer = 0; // Default layer

            var col = GetComponent<Collider2D>();
            if (col != null) col.enabled = true;
        }
    }

    /// <summary>
    /// Server method to revive a dead player.
    /// </summary>
    public bool RevivePlayer()
    {
        if (!IsServer) return false;
        if (isAlive.Value) return false;
        isAlive.Value = true;
        sanity.Value = 100f;
        return true;
    }

    /// <summary>
    /// Server method to kill a player caught by the ghost.
    /// </summary>
    public bool KillPlayer()
    {
        if (!IsServer) return false;
        if (isGodMode.Value) return false; // God mode prevents death
        if (!isAlive.Value) return false; // Already dead
        isAlive.Value = false;
        return true;
    }

    private IInteractable GetHoveredInteractable()
    {
        return currentlyHoveredInteractable;
    }

    private IInteractable CalculateHoveredInteractable()
    {
        if (Vector2.Distance(transform.position, mousePosition) > maxReachRadius) return null;

        // Check both interactLayer and all colliders at cursor without per-frame array allocation
        s_OverlapBuffer.Clear();
        int hitCount = Physics2D.OverlapCircle(mousePosition, 0.06f, s_NoFilter, s_OverlapBuffer);

        IInteractable bestInteractable = null;
        float bestDistance = float.MaxValue;
        bool bestIsEquipmentOrKey = false;

        for (int i = 0; i < hitCount; i++)
        {
            var hit = s_OverlapBuffer[i];
            if (hit == null) continue;

            var equipment = hit.GetComponent<EquipmentItem>();
            if (equipment != null)
            {
                if (!equipment.IsOnGround && !equipment.IsPlaced)
                {
                    continue; // Ignore items currently held in a player's hand/inventory
                }
                if (equipment.InteractCollider != null && equipment.InteractCollider != hit)
                {
                    continue; // Ignore any extra colliders on the item (like huge AoE triggers)
                }
            }

            var interactable = hit.GetComponent<IInteractable>();
            if (interactable == null)
            {
                interactable = hit.GetComponentInParent<IInteractable>();
            }

            if (interactable != null)
            {
                if (interactable is NetworkDoor door && !door.AllowPlayerInteract)
                {
                    continue;
                }

                bool isEquipOrKey = (interactable is EquipmentItem) || (interactable is FrontDoorKeyItem) || (interactable is SummoningCircle);
                float dist = Vector2.Distance(mousePosition, hit.ClosestPoint(mousePosition));

                // Prioritize small pickup items and the summoning circle over large background triggers, then closest point to mouse
                if (isEquipOrKey && !bestIsEquipmentOrKey)
                {
                    bestIsEquipmentOrKey = true;
                    bestDistance = dist;
                    bestInteractable = interactable;
                }
                else if (isEquipOrKey == bestIsEquipmentOrKey && dist < bestDistance)
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

    private void UpdateItemHoverIndicator()
    {
        if (ItemHoverIndicator.Instance == null)
        {
            new GameObject("ItemHoverIndicator").AddComponent<ItemHoverIndicator>();
        }

        if (IsInteractionBlocked || !isAlive.Value)
        {
            ItemHoverIndicator.Instance.Hide();
            return;
        }

        if (currentlyHoveredInteractable != null)
        {
            if (currentlyHoveredInteractable is EquipmentItem equipment)
            {
                if (!(equipment is RitualChalkItem && equipment.IsPlaced) && (equipment.IsOnGround || equipment.IsPlaced))
                {
                    float distToMouse = Vector2.Distance(transform.position, mousePosition);
                    float distToItem = Vector2.Distance(transform.position, equipment.transform.position);

                    if (distToMouse <= maxReachRadius && distToItem <= maxReachRadius + 0.8f)
                    {
                        if (lastHoveredEquipment != equipment || Time.unscaledTime >= nextStackCountRefreshTime)
                        {
                            lastHoveredEquipment = equipment;
                            cachedStackCount = CountStackedEquipment(equipment);
                            nextStackCountRefreshTime = Time.unscaledTime + 0.15f;
                        }
                        string displayLabel = $"{equipment.ItemName} x{cachedStackCount}";
                        ItemHoverIndicator.Instance.Show(equipment.gameObject, displayLabel);
                        return;
                    }
                }
            }
            else if (currentlyHoveredInteractable is SummoningCircle summoningCircle)
            {
                if (TryShowWorldInteractableHover(summoningCircle, summoningCircle.GetInteractText())) return;
            }
            else if (currentlyHoveredInteractable is FrontDoorKeyItem keyItem)
            {
                float distToMouse = Vector2.Distance(transform.position, mousePosition);
                float distToItem = Vector2.Distance(transform.position, keyItem.transform.position);

                if (distToMouse <= maxReachRadius && distToItem <= maxReachRadius + 0.8f)
                {
                    ItemHoverIndicator.Instance.Show(keyItem.gameObject, "Front Door Key x1");
                    return;
                }
            }
            else if (currentlyHoveredInteractable is TruckEndGameButton endBtn)
            {
                if (TryShowWorldInteractableHover(endBtn, endBtn.GetInteractText())) return;
            }
            else if (currentlyHoveredInteractable is HouseLightSwitch lightSwitch)
            {
                if (TryShowWorldInteractableHover(lightSwitch, lightSwitch.GetInteractText())) return;
            }
            else if (currentlyHoveredInteractable is FuseBox fuseBox)
            {
                if (TryShowWorldInteractableHover(fuseBox, fuseBox.GetInteractText())) return;
            }
            else if (currentlyHoveredInteractable is TruckDashboardController dashboard)
            {
                float distToMouse = Vector2.Distance(transform.position, mousePosition);
                float distToItem = Vector2.Distance(transform.position, dashboard.transform.position);

                if (distToMouse <= maxReachRadius && distToItem <= maxReachRadius + 0.8f)
                {
                    ItemHoverIndicator.Instance.Show(dashboard.gameObject, dashboard.GetInteractText());
                    return;
                }
            }
        }

        ItemHoverIndicator.Instance.Hide();
        lastHoveredEquipment = null;
    }

    private bool TryShowWorldInteractableHover(Component target, string interactText)
    {
        if (target == null) return false;
        float distToMouse = Vector2.Distance(transform.position, mousePosition);
        float distToItem = Vector2.Distance(transform.position, target.transform.position);

        if (distToMouse <= maxReachRadius && distToItem <= maxReachRadius + 0.8f)
        {
            ItemHoverIndicator.Instance.Show(target.gameObject, interactText);
            return true;
        }
        return false;
    }

    private static int CountStackedEquipment(EquipmentItem target)
    {
        if (target == null) return 1;

        int count = 0;
        Vector2 targetPos = target.transform.position;
        System.Type targetType = target.GetType();

        // Check nearby colliders first for fast stack counting without per-frame allocations
        s_StackOverlapBuffer.Clear();
        int nearbyCount = Physics2D.OverlapCircle(targetPos, 0.06f, s_NoFilter, s_StackOverlapBuffer);
        s_CountedEquipment.Clear();

        for (int i = 0; i < nearbyCount; i++)
        {
            var col = s_StackOverlapBuffer[i];
            if (col == null) continue;
            var eq = col.GetComponent<EquipmentItem>();
            if (eq != null && !s_CountedEquipment.Contains(eq) && (eq.IsOnGround || eq.IsPlaced))
            {
                if (eq.GetType() == targetType || eq.ItemName == target.ItemName)
                {
                    s_CountedEquipment.Add(eq);
                    count++;
                }
            }
        }

        return Mathf.Max(1, count);
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedLocal;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedLocal;
        if (IsOwner)
        {
            EndCurrentDrag();
            ItemHoverIndicator.Instance?.Hide();
        }
    }

    private void OnSceneLoadedLocal(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (IsOwner && IsSpawned)
        {
            StartCoroutine(DelayedTeleportToSpawn());
        }
    }

    public void TeleportToSpawnPoint()
    {
        Vector3 spawnPos = PlayerSpawnPoint.GetSpawnPosition(OwnerClientId);
        TeleportTo(spawnPos);
    }

    public void TeleportTo(Vector3 position)
    {
        transform.position = position;

        if (rb != null)
        {
            rb.position = position;
            rb.linearVelocity = Vector2.zero;
        }

        bool isNetListening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (TryGetComponent<ClientNetworkTransform>(out var clientNetTransform))
        {
            if (!isNetListening || (clientNetTransform.IsSpawned && clientNetTransform.CanCommitToTransform))
            {
                clientNetTransform.Teleport(position, transform.rotation, transform.localScale);
            }
        }
        else if (TryGetComponent<Unity.Netcode.Components.NetworkTransform>(out var netTransform))
        {
            if (!isNetListening || (netTransform.IsSpawned && netTransform.CanCommitToTransform))
            {
                netTransform.Teleport(position, transform.rotation, transform.localScale);
            }
        }
    }

    /// <summary>
    /// Teleports a player cleanly from either the owning client or the server, respecting ClientNetworkTransform authority.
    /// </summary>
    public void ServerTeleportTo(Vector3 position)
    {
        bool isNetListening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!isNetListening || !IsSpawned || IsOwner)
        {
            TeleportTo(position);
            return;
        }

        if (IsServer)
        {
            TeleportToOwnerRpc(position);
        }
    }

    [Rpc(SendTo.Owner)]
    private void TeleportToOwnerRpc(Vector3 position)
    {
        TeleportTo(position);
    }

    private System.Collections.IEnumerator DelayedTeleportToSpawn()
    {
        // Yield before teleporting so SynchronizeSceneNetworkObjects and sibling NetworkBehaviour spawns finish first.
        yield return null;

        if (TryGetComponent<ClientNetworkTransform>(out var clientNetTransform))
        {
            int waitFrames = 0;
            while (NetworkManager.Singleton != null &&
                   NetworkManager.Singleton.IsListening &&
                   (!clientNetTransform.IsSpawned || !clientNetTransform.CanCommitToTransform) &&
                   waitFrames < 30)
            {
                waitFrames++;
                yield return null;
            }
        }

        PixelScenePostProcessor.ApplyPixelArtToScene();
        TeleportToSpawnPoint();
        yield return null;
        PixelScenePostProcessor.ApplyPixelArtToScene();
        TeleportToSpawnPoint();
    }
}