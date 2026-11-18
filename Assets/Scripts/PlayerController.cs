using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public enum PlayerLifeState : byte
{
    Alive = 0,
    DyingSequence = 1,
    GhostSpectator = 2
}

[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public class PlayerController : NetworkBehaviour
{
    public static readonly List<PlayerController> AllPlayers = new List<PlayerController>();
    public static PlayerController LocalInstance { get; private set; }
    public static event System.Action OnAnyPlayerSanityStateChanged;

    public static void NotifySanityStateChanged() => OnAnyPlayerSanityStateChanged?.Invoke();

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
    private NetworkVariable<PlayerLifeState> lifeState = new NetworkVariable<PlayerLifeState>(
        PlayerLifeState.Alive, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<Vector2> deathWorldPosition = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isCrouching = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<bool> netIsWalking = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<bool> netIsSprinting = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkVariable<bool> netIsHolding = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private static int nextJoinOrderCounter = 0;
    private bool localIsWalking = false;
    private bool localIsSprinting = false;

    [Header("Corpse Prefab (Auto-Resolved From NetworkPrefabs If Unset)")]
    [SerializeField] private GameObject playerCorpsePrefab;

    [Header("Sanity Settings")]
    [Tooltip("Rate at which sanity drains per second while in the dark and outside safe zones (sped up by 0.5x to 0.75/s).")]
    [SerializeField] private float darkSanityDrainRate = 0.75f;

    private NetworkVariable<float> sanity = new NetworkVariable<float>(
        100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> isGodMode = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public PlayerLifeState LifeState => lifeState.Value;
    public bool IsAlive => lifeState.Value == PlayerLifeState.Alive;
    public bool IsDyingSequence => lifeState.Value == PlayerLifeState.DyingSequence;
    public bool IsGhostSpectator => lifeState.Value == PlayerLifeState.GhostSpectator;
    public Vector2 DeathWorldPosition => deathWorldPosition.Value;
    public GameObject CorpsePrefab => playerCorpsePrefab;
    public bool IsCrouching => isCrouching.Value;
    public int JoinOrderIndex { get; private set; } = -1;
    public bool IsMoving => IsAlive && (IsSpawned ? netIsWalking.Value : localIsWalking);
    public bool IsSprinting => IsAlive && (IsSpawned ? netIsSprinting.Value : localIsSprinting);
    public float Sanity => sanity.Value;
    public float MaxReachRadius => maxReachRadius;
    public bool IsInHideZone { get; private set; }
    public HideZone CurrentHideZone { get; private set; }
    private readonly System.Collections.Generic.HashSet<SafeZone> activeSafeZones = new System.Collections.Generic.HashSet<SafeZone>();

    public bool IsInSafeZone
    {
        get
        {
            // 1. Evict null, inactive, disabled, or non-overlapping zones from the trigger cache
            activeSafeZones.RemoveWhere(sz =>
                sz == null ||
                !sz.gameObject.activeInHierarchy ||
                !sz.enabled ||
                (sz.HasEnabledCollider && !sz.OverlapsPosition(transform.position)));

            // 2. Stateless exact point-in-collider check against active SafeZones (no 0.25f wall/exterior bleed)
            Vector2 worldPos = transform.position;
            bool hasNullZone = false;
            foreach (var sz in SafeZone.AllActiveZones)
            {
                if (sz == null)
                {
                    hasNullZone = true;
                    continue;
                }
                if (sz.OverlapsPosition(worldPos))
                {
                    if (hasNullZone) SafeZone.AllActiveZones.RemoveWhere(s => s == null);
                    return true;
                }
            }

            if (hasNullZone)
            {
                SafeZone.AllActiveZones.RemoveWhere(s => s == null);
            }

            return activeSafeZones.Count > 0;
        }
    }

    /// <summary>
    /// Player is hidden from ghost vision raycasts when crouched inside a designated HideZone.
    /// </summary>
    public bool IsHiddenFromGhost => isCrouching.Value && IsInHideZone;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    private Collider2D playerCollider;
    private PlayerInventory inventory;
    private Animator animator;
    private Coroutine deathSequenceTimeoutCoroutine;
    private Vector2 moveInput;
    private Vector2 mousePosition;
    private Camera localCamera;

    private Vector3 originalScale;
    private IInteractable currentDraggedInteractable = null;

    private IInteractable currentlyHoveredInteractable = null;
    private IInteractable currentlyHoveredEnvironment = null;
    private IInteractable currentlyHoveredEquipment = null;
    private IInteractable pendingLeftClickTarget = null;
    private float pendingLeftClickStartTime = 0f;
    private Vector2 pendingLeftClickStartScreenPos = Vector2.zero;
    public const float DragHoldThresholdSeconds = 0.18f;
    public const float DragMoveThresholdPixels = 6f;
    public const float EquipmentHoverCursorRadius = 0.004f;
    public const float EnvironmentHoverCursorRadius = 0.012f;
    public const float EquipmentSpriteBoundsPadding = 0.003f;
    private EquipmentItem lastHoveredEquipment = null;
    private int cachedStackCount = 1;
    private float nextStackCountRefreshTime = 0f;
    private Light2D playerPovLight;

    private LineRenderer reachGuideRenderer;
    private Material reachGuideMaterial;
    private float reachGuideBuiltRadius = -1f;
    private float reachGuideAlpha = 0f;
    private float reachGuideHoldTimer = 0f;
    private const int ReachGuideSegments = 48;
    public const float ReachGuideHoldDuration = 1.1f;
    public const float ReachGuideFadeSpeed = 0.65f;
    public const float ReachGuideGrayAlpha = 0.25f;
    public const float ReachGuideLineWidth = 0.002f;

    private static readonly System.Collections.Generic.List<Collider2D> s_OverlapBuffer = new System.Collections.Generic.List<Collider2D>(16);
    private static readonly System.Collections.Generic.List<Collider2D> s_StackOverlapBuffer = new System.Collections.Generic.List<Collider2D>(16);
    private static readonly System.Collections.Generic.HashSet<EquipmentItem> s_CountedEquipment = new System.Collections.Generic.HashSet<EquipmentItem>();
    private static readonly ContactFilter2D s_NoFilter = ContactFilter2D.noFilter;
    private int wallLayerMask;

    [Header("Ambient Light Settings")]
    [SerializeField] private float playerAmbientRadius = 0.58f;
    [SerializeField] private float playerAmbientInnerRadius = 0.08f;
    [SerializeField] private float playerAmbientIntensity = 0.34f;
    [SerializeField] private float playerAmbientFalloff = 0.72f;
    [SerializeField] private Color playerAmbientColor = new Color(0.84f, 0.89f, 0.96f, 1f);

    public Light2D PlayerAmbientLight => playerPovLight;
    public Collider2D PlayerCollider => playerCollider;

    public static bool TryGetByClientId(ulong clientId, out PlayerController player)
    {
        for (int i = 0; i < AllPlayers.Count; i++)
        {
            var p = AllPlayers[i];
            if (p != null && p.IsSpawned && p.OwnerClientId == clientId)
            {
                player = p;
                return true;
            }
        }
        player = null;
        return false;
    }

    public static bool IsEquipmentAllowedForGhostSpectator(EquipmentItem eq)
    {
        return eq != null && eq.IsOnGround && !(eq is SanityPillsItem) && !(eq is RitualChalkItem);
    }

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
        playerCollider = GetComponent<Collider2D>();
        inventory = GetComponent<PlayerInventory>();
        animator = GetComponent<Animator>();
        originalScale = transform.localScale;
        currentStamina = maxStamina;
        wallLayerMask = LayerMask.GetMask("Walls");
    }

    public override void OnNetworkSpawn()
    {
        if (!AllPlayers.Contains(this))
        {
            AllPlayers.Add(this);
        }
        if (JoinOrderIndex < 0)
        {
            JoinOrderIndex = nextJoinOrderCounter++;
        }

        if (TryGetComponent<Light2D>(out var rootLight))
        {
            rootLight.enabled = false;
        }

        if (IsOwner)
        {
            LocalInstance = this;
            localCamera = Camera.main;
            EnsurePlayerPOVLight();
            EnsureReachGuideRenderer();
            StartCoroutine(DelayedTeleportToSpawn());
            if (ItemHoverIndicator.Instance == null)
            {
                new GameObject("ItemHoverIndicator").AddComponent<ItemHoverIndicator>();
            }
            GameSettingsManager.Instance?.ResetMatchTutorialHints();
            StartCoroutine(DelayedMatchStartTutorialRoutine());
            StartCoroutine(DelayedJournalHintRoutine());
        }
        else
        {
            if (playerPovLight != null)
            {
                playerPovLight.enabled = false;
            }
            if (reachGuideRenderer != null)
            {
                reachGuideRenderer.enabled = false;
            }
        }

        sanity.OnValueChanged += OnSanityValueChanged;
        lifeState.OnValueChanged += (oldState, newState) =>
        {
            OnLifeStateChanged(oldState, newState);
            if (!IsOwner) UpdateRemoteAnimator();
        };
        isCrouching.OnValueChanged += (oldVal, newVal) => OnCrouchStateChanged(newVal);
        netIsWalking.OnValueChanged += (oldVal, newVal) => { if (!IsOwner) UpdateRemoteAnimator(); };
        netIsHolding.OnValueChanged += (oldVal, newVal) => { if (!IsOwner) UpdateRemoteAnimator(); };

        if (lifeState.Value != PlayerLifeState.Alive)
        {
            ApplyLifeStateLocally(lifeState.Value);
        }
        RefreshAllPlayerVisibilityAndCollisions();

        if (!IsOwner)
        {
            UpdateRemoteAnimator();
        }

        NotifySanityStateChanged();
    }

    private void OnSanityValueChanged(float previousValue, float newValue)
    {
        NotifySanityStateChanged();
    }

    private float GetEffectiveDarkSanityDrainRate()
    {
        float multiplier = 1f;
        if (ProgressionAndLoadoutManager.Instance != null)
        {
            multiplier = ProgressionAndLoadoutManager.GetDifficultySanityDrainMultiplier(
                ProgressionAndLoadoutManager.Instance.SelectedDifficulty);
        }
        return darkSanityDrainRate * multiplier;
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
        playerPovLight.enabled = IsOwner && IsAlive;
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

    private void ApplySpectatorAmbientLightSettings()
    {
        if (playerPovLight == null) return;

        playerPovLight.lightType = Light2D.LightType.Point;
        playerPovLight.color = new Color(0.55f, 0.76f, 1.0f, 1f);
        playerPovLight.intensity = 0.85f;
        playerPovLight.falloffIntensity = 0.55f;
        playerPovLight.pointLightInnerAngle = 360f;
        playerPovLight.pointLightOuterAngle = 360f;
        playerPovLight.pointLightInnerRadius = 0.45f;
        playerPovLight.pointLightOuterRadius = 1.65f;
        playerPovLight.shadowsEnabled = false;
        playerPovLight.shadowIntensity = 0f;
        playerPovLight.shadowVolumeIntensity = 0f;
        playerPovLight.targetSortingLayers = System.Array.ConvertAll(SortingLayer.layers, layer => layer.id);
    }

    private void UpdatePOVLightScale()
    {
        if (playerPovLight != null)
        {
            // Cancel current parent scale (including negative Y scale on Player.prefab and crouch 0.85x)
            // so the 360-degree circular light stays uniform in world space.
            Vector3 currentScale = transform.localScale;
            float sx = Mathf.Abs(currentScale.x) > 1e-5f ? 1f / currentScale.x : 1f;
            float sy = Mathf.Abs(currentScale.y) > 1e-5f ? 1f / currentScale.y : 1f;
            playerPovLight.transform.localScale = new Vector3(sx, sy, 1f);
        }

        UpdateReachGuideScale();
    }

    private void EnsureReachGuideRenderer()
    {
        if (reachGuideRenderer == null)
        {
            Transform existing = transform.Find("ReachGuideCircle");
            if (existing != null)
            {
                reachGuideRenderer = existing.GetComponent<LineRenderer>();
            }

            if (reachGuideRenderer == null)
            {
                var ringGo = new GameObject("ReachGuideCircle");
                ringGo.transform.SetParent(transform, false);
                ringGo.transform.localPosition = Vector3.zero;
                ringGo.transform.localRotation = Quaternion.identity;
                reachGuideRenderer = ringGo.AddComponent<LineRenderer>();
            }

            reachGuideRenderer.loop = true;
            reachGuideRenderer.useWorldSpace = false;
            reachGuideRenderer.positionCount = ReachGuideSegments;
            reachGuideRenderer.numCornerVertices = 2;
            reachGuideRenderer.numCapVertices = 2;
            reachGuideRenderer.startWidth = ReachGuideLineWidth;
            reachGuideRenderer.endWidth = ReachGuideLineWidth;
            reachGuideRenderer.sortingLayerName = "Roof";
            reachGuideRenderer.sortingOrder = 490;

            if (reachGuideMaterial == null)
            {
                Shader unlitShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit");
                if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
                if (unlitShader != null)
                {
                    reachGuideMaterial = new Material(unlitShader);
                }
            }

            if (reachGuideMaterial != null)
            {
                reachGuideRenderer.sharedMaterial = reachGuideMaterial;
            }

            if (reachGuideAlpha <= 0.005f)
            {
                reachGuideRenderer.enabled = false;
            }
        }

        UpdateReachGuideScale();
        if (reachGuideRenderer != null)
        {
            reachGuideRenderer.startWidth = ReachGuideLineWidth;
            reachGuideRenderer.endWidth = ReachGuideLineWidth;
        }
        if (!Mathf.Approximately(reachGuideBuiltRadius, maxReachRadius))
        {
            RebuildReachGuidePositions(maxReachRadius);
        }
    }

    private void UpdateReachGuideScale()
    {
        if (reachGuideRenderer == null) return;

        // Cancel Player.prefab root scale (and crouch 0.85x multiplier) so the ring renders at 1:1 world units.
        Vector3 currentScale = transform.localScale;
        float sx = Mathf.Abs(currentScale.x) > 1e-5f ? 1f / currentScale.x : 1f;
        float sy = Mathf.Abs(currentScale.y) > 1e-5f ? 1f / currentScale.y : 1f;
        float sz = Mathf.Abs(currentScale.z) > 1e-5f ? 1f / currentScale.z : 1f;
        reachGuideRenderer.transform.localScale = new Vector3(sx, sy, sz);
        reachGuideRenderer.transform.localRotation = Quaternion.identity;
    }

    private void RebuildReachGuidePositions(float radius)
    {
        if (reachGuideRenderer == null) return;

        float safeRadius = Mathf.Max(0f, radius);
        reachGuideRenderer.positionCount = ReachGuideSegments;
        float angleStep = (Mathf.PI * 2f) / ReachGuideSegments;
        for (int i = 0; i < ReachGuideSegments; i++)
        {
            float angle = i * angleStep;
            reachGuideRenderer.SetPosition(i, new Vector3(
                Mathf.Cos(angle) * safeRadius,
                Mathf.Sin(angle) * safeRadius,
                0f));
        }
        reachGuideBuiltRadius = safeRadius;
    }

    private void TriggerReachGuide()
    {
        bool settingEnabled = GameSettingsManager.Instance == null || GameSettingsManager.Instance.ReachIndicatorEnabled;
        if (!IsAlive || IsDyingSequence || IsInteractionBlocked || !settingEnabled)
        {
            HideReachGuideImmediate();
            return;
        }

        EnsureReachGuideRenderer();
        reachGuideAlpha = ReachGuideGrayAlpha;
        reachGuideHoldTimer = ReachGuideHoldDuration;
        ApplyReachGuideVisuals();
    }

    private void HideReachGuideImmediate()
    {
        reachGuideAlpha = 0f;
        reachGuideHoldTimer = 0f;
        if (reachGuideRenderer != null)
        {
            reachGuideRenderer.enabled = false;
        }
    }

    private void UpdateReachGuideCircle() => StepReachGuideCircle(Time.deltaTime);

    private void StepReachGuideCircle(float deltaTime)
    {
        EnsureReachGuideRenderer();

        bool settingEnabled = GameSettingsManager.Instance == null || GameSettingsManager.Instance.ReachIndicatorEnabled;
        if (!IsAlive || IsDyingSequence || IsInteractionBlocked || !settingEnabled)
        {
            HideReachGuideImmediate();
            return;
        }

        float dt = Mathf.Max(0f, deltaTime);
        if (reachGuideHoldTimer > 0f)
        {
            reachGuideHoldTimer = Mathf.Max(0f, reachGuideHoldTimer - dt);
        }
        else
        {
            reachGuideAlpha = Mathf.MoveTowards(reachGuideAlpha, 0f, dt * ReachGuideFadeSpeed);
        }

        ApplyReachGuideVisuals();
    }

    private void ApplyReachGuideVisuals()
    {
        if (reachGuideRenderer == null) return;

        if (reachGuideAlpha <= 0.005f)
        {
            reachGuideRenderer.enabled = false;
            return;
        }

        Color grayColor = new Color(0.75f, 0.75f, 0.75f, reachGuideAlpha);
        reachGuideRenderer.enabled = true;
        reachGuideRenderer.startWidth = ReachGuideLineWidth;
        reachGuideRenderer.endWidth = ReachGuideLineWidth;
        reachGuideRenderer.startColor = grayColor;
        reachGuideRenderer.endColor = grayColor;
        if (reachGuideMaterial != null)
        {
            reachGuideMaterial.color = grayColor;
        }
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
        sanity.OnValueChanged -= OnSanityValueChanged;
        pendingLeftClickTarget = null;
        EndCurrentDrag();
        AllPlayers.Remove(this);
        if (LocalInstance == this)
        {
            LocalInstance = null;
            ItemHoverIndicator.Instance?.Hide();
            ItemHoverIndicator.Instance?.HideSpeechBubble();
            DeathSequenceController.Instance?.CancelAndReset();
            GhostSpectatorVisuals.Instance?.SetSpectatorVisualsActive(false);
            NetworkDoor.SetLocalSpectatorDoorBypass(false, playerCollider);
        }
        RefreshAllPlayerVisibilityAndCollisions();
        NotifySanityStateChanged();
    }

    public override void OnDestroy()
    {
        if (reachGuideMaterial != null)
        {
            if (Application.isPlaying) Destroy(reachGuideMaterial);
            else DestroyImmediate(reachGuideMaterial);
            reachGuideMaterial = null;
        }
        base.OnDestroy();
    }

    private void EndCurrentDrag()
    {
        pendingLeftClickTarget = null;
        if (currentDraggedInteractable == null) return;

        if ((currentDraggedInteractable as Object) != null)
        {
            ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
            currentDraggedInteractable.OnDragEnd(localClientId);
        }
        currentDraggedInteractable = null;
    }

    /// <summary>
    /// Checks whether player movement and rotation should be blocked by open modal GUIs:
    /// Dev Console, Pause Menu, Truck Dashboard, Spirit Box Dialogue, Journal,
    /// Loading Screen, End Game Reveal, Exorcism UI panels, or the player's death sequence.
    /// </summary>
    public static bool IsMovementBlocked =>
        DeveloperConsoleManager.IsConsoleOpen ||
        PauseMenuController.IsOpen ||
        TruckDashboardController.IsOpen ||
        SpiritBoxItem.IsLocalDialogueActive ||
        (SpiritBoxDialogueUI.Instance != null && SpiritBoxDialogueUI.Instance.IsOpen) ||
        JournalController.IsOpen ||
        LoadingScreenManager.IsLoadingVisible ||
        EndGameRevealController.IsOpen ||
        ExorcismManager.IsUIOpen ||
        (LocalInstance != null && LocalInstance.IsDyingSequence);

    /// <summary>
    /// Checks whether world interactions (clicks, item usage, dragging, E interact, drop, place, slot switching)
    /// should be blocked by active modal GUIs.
    /// </summary>
    public static bool IsInteractionBlocked => IsMovementBlocked;

    private void Update()
    {
        if (IsServer && IsAlive)
        {
            bool inDark = !PlayerInventory.HasActiveHeldFlashlight(this);

            if (inDark && !IsInSafeZone)
            {
                // Light-based sanity drain (only drain in the dark and outside safe zones)
                float nextSanity = Mathf.Max(0f, sanity.Value - Time.deltaTime * GetEffectiveDarkSanityDrainRate());
                if (!Mathf.Approximately(nextSanity, sanity.Value))
                {
                    sanity.Value = nextSanity;
                }
            }
        }

        if (!IsOwner)
        {
            UpdateRemoteAnimator();
            return;
        }

        if (IsDyingSequence)
        {
            pendingLeftClickTarget = null;
            EndCurrentDrag();
            currentlyHoveredEnvironment = null;
            currentlyHoveredEquipment = null;
            currentlyHoveredInteractable = null;
            ItemHoverIndicator.Instance?.Hide();
            ItemHoverIndicator.Instance?.HideSpeechBubble();
            HideReachGuideImmediate();
            SetLocalAnimationState(false, false);
            return;
        }

        // Block movement and world interactions while in locking GUIs (Journal, Pause Menu, Truck Dashboard, Dev Console, Spirit Box, Exorcism UI)
        if (IsMovementBlocked)
        {
            moveInput = Vector2.zero;
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            pendingLeftClickTarget = null;
            EndCurrentDrag();
            currentlyHoveredEnvironment = null;
            currentlyHoveredEquipment = null;
            currentlyHoveredInteractable = null;
            ItemHoverIndicator.Instance?.Hide();
            ItemHoverIndicator.Instance?.HideSpeechBubble();
            HideReachGuideImmediate();
            SetLocalAnimationState(false, IsAlive && inventory != null && inventory.CurrentItem != null);
            return;
        }

        // 1. Gather WASD input (functional during normal gameplay and GhostSpectator)
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        moveInput = new Vector2(moveX, moveY).normalized;

        // 2. Crouch input (C) — Living players only
        bool crouchHeld = IsAlive && Input.GetKey(KeyCode.C);
        if (crouchHeld != isCrouching.Value)
        {
            isCrouching.Value = crouchHeld;
        }

        // If interaction is blocked, disable world interactions and clicks
        if (IsInteractionBlocked)
        {
            pendingLeftClickTarget = null;
            EndCurrentDrag();
            currentlyHoveredEnvironment = null;
            currentlyHoveredEquipment = null;
            currentlyHoveredInteractable = null;
            ItemHoverIndicator.Instance?.Hide();
            ItemHoverIndicator.Instance?.HideSpeechBubble();
            HideReachGuideImmediate();
            SetLocalAnimationState(false, IsAlive && inventory != null && inventory.CurrentItem != null);
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

        bool pointerOverInteractiveHud = PlayerHUDManager.WasHotbarClickedThisFrame || PlayerHUDManager.IsPointerOverInteractiveHUD();

        // Check for hovered interactables (equipment vs environment channels) and update visual hover indicator every frame
        if (pointerOverInteractiveHud)
        {
            currentlyHoveredEquipment = null;
            currentlyHoveredEnvironment = null;
            currentlyHoveredInteractable = null;
        }
        else
        {
            currentlyHoveredEquipment = CalculateHoveredEquipment();
            currentlyHoveredEnvironment = CalculateHoveredEnvironment();
            currentlyHoveredInteractable = currentlyHoveredEquipment ?? currentlyHoveredEnvironment;
        }
        UpdateItemHoverIndicator();

        // 4. Targeted Environment Interact Input (E)
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryInteract();
        }

        // 4b. Dedicated Flashlight Toggle Input (F)
        if (IsAlive && Input.GetKeyDown(KeyCode.F) && inventory != null)
        {
            inventory.ToggleFlashlight();
        }

        // 5. Contextual Left Click (Tap Use/Pickup vs Hold Drag — Equipment Only)
        if (Input.GetMouseButtonDown(0) && !pointerOverInteractiveHud)
        {
            HandleLeftClickDown();
        }

        if (pendingLeftClickTarget != null && currentDraggedInteractable == null)
        {
            if ((pendingLeftClickTarget as Object) == null)
            {
                pendingLeftClickTarget = null;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                var tapTarget = pendingLeftClickTarget;
                pendingLeftClickTarget = null;
                bool allowedTap = IsAlive || (IsGhostSpectator && tapTarget is EquipmentItem eqTap && IsEquipmentAllowedForGhostSpectator(eqTap));
                if (allowedTap && (tapTarget is EquipmentItem || tapTarget is FrontDoorKeyItem))
                {
                    tapTarget.Interact();
                }
                else if (IsAlive && inventory != null)
                {
                    inventory.UsePrimary();
                }
            }
            else if (Input.GetMouseButton(0))
            {
                bool heldLongEnough = (Time.unscaledTime - pendingLeftClickStartTime) >= DragHoldThresholdSeconds;
                bool movedFarEnough = Vector2.Distance((Vector2)Input.mousePosition, pendingLeftClickStartScreenPos) > DragMoveThresholdPixels;
                if (heldLongEnough || movedFarEnough)
                {
                    var dragCandidate = pendingLeftClickTarget;
                    pendingLeftClickTarget = null;
                    bool allowedDrag = IsAlive || (IsGhostSpectator && dragCandidate is EquipmentItem eqDragCandidate && IsEquipmentAllowedForGhostSpectator(eqDragCandidate));
                    if (allowedDrag && dragCandidate.CanDrag())
                    {
                        ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0UL;
                        currentDraggedInteractable = dragCandidate;
                        currentDraggedInteractable.OnDragBegin(localClientId);
                    }
                }
            }
            else
            {
                pendingLeftClickTarget = null;
            }
        }

        if ((Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0)) && currentDraggedInteractable != null)
        {
            EndCurrentDrag();
        }

        if (currentDraggedInteractable != null)
        {
            bool stillAllowed = IsAlive || (IsGhostSpectator && currentDraggedInteractable is EquipmentItem eqDrag && IsEquipmentAllowedForGhostSpectator(eqDrag));
            if ((currentDraggedInteractable as Object) == null || !stillAllowed || !currentDraggedInteractable.CanDrag())
            {
                EndCurrentDrag();
            }
            else
            {
                Vector2 origin = transform.position;
                Vector2 dragTarget = mousePosition;
                Vector2 toTarget = dragTarget - origin;

                if (toTarget.magnitude > maxReachRadius)
                {
                    dragTarget = origin + toTarget.normalized * maxReachRadius;
                }

                if (!IsGhostSpectator && wallLayerMask != 0)
                {
                    RaycastHit2D wallHit = Physics2D.Linecast(origin, dragTarget, wallLayerMask);
                    if (wallHit.collider != null)
                    {
                        Vector2 dir = dragTarget - origin;
                        float len = dir.magnitude;
                        if (len > 1e-5f)
                        {
                            float clampedDist = Mathf.Max(0f, wallHit.distance - 0.02f);
                            dragTarget = origin + (dir / len) * clampedDist;
                        }
                    }
                }

                currentDraggedInteractable.OnDragUpdate(dragTarget);
            }
        }

        // 6. Place Item (Right Click) — Living players only
        if (IsAlive && Input.GetMouseButtonDown(1) && !pointerOverInteractiveHud)
        {
            TryPlaceHeldItemAtMouse();
        }

        // 7. Drop Item (G) — Allowed for Alive and GhostSpectator (ground equipment)
        if (Input.GetKeyDown(KeyCode.G))
        {
            if (inventory != null) inventory.DropCurrentItem(transform.position);
        }

        // 8. Hooks for UI
        if (Input.GetKeyDown(KeyCode.J)) OpenJournal();

        // 9. Update Animator and replicate animation state (dead spectators stay silent & still to living players)
        if (IsAlive)
        {
            SetLocalAnimationState(moveInput.sqrMagnitude > 0.01f, inventory != null && inventory.CurrentItem != null);
        }
        else
        {
            SetLocalAnimationState(false, false);
        }

        UpdateReachGuideCircle();
    }

    private void SetLocalAnimationState(bool walking, bool holding)
    {
        localIsWalking = walking;
        if (!walking)
        {
            localIsSprinting = false;
            if (IsSpawned && IsOwner && netIsSprinting.Value)
            {
                netIsSprinting.Value = false;
            }
        }

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

        if (!IsAlive)
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

    private void FixedUpdate()
    {
        if (!IsOwner || IsDyingSequence) return;
        if (IsMovementBlocked)
        {
            moveInput = Vector2.zero;
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }
            return;
        }

        // 1. Determine current speed
        float currentSpeed = walkSpeed;
        bool sprintingNow = false;
        if (IsGhostSpectator)
        {
            // Dead spectators float smoothly without exhausting stamina
            currentSpeed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
        }
        else if (isCrouching.Value)
        {
            currentSpeed = crouchSpeed;
        }
        else if (Input.GetKey(KeyCode.LeftShift) && currentStamina > 0f)
        {
            currentSpeed = sprintSpeed;
            currentStamina -= Time.fixedDeltaTime;
            sprintingNow = moveInput.sqrMagnitude > 0.01f;
        }
        else
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenRate * Time.fixedDeltaTime);
        }

        localIsSprinting = sprintingNow;
        if (IsSpawned && IsOwner && netIsSprinting.Value != sprintingNow)
        {
            netIsSprinting.Value = sprintingNow;
        }

        rb.MovePosition(rb.position + moveInput * currentSpeed * Time.fixedDeltaTime);

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

    private void OnLifeStateChanged(PlayerLifeState oldState, PlayerLifeState newState)
    {
        if (newState == PlayerLifeState.DyingSequence && oldState == PlayerLifeState.Alive)
        {
            GameAudioManager.PlayGhostKill(transform.position);
        }

        ApplyLifeStateLocally(newState);

        // If transitioning from Alive -> DyingSequence cleanly, make sure owner's inventory state is cleared locally too
        if (newState != PlayerLifeState.Alive && IsOwner)
        {
            EndCurrentDrag();
            ItemHoverIndicator.Instance?.Hide();
            ItemHoverIndicator.Instance?.HideSpeechBubble();
            if (isCrouching.Value) isCrouching.Value = false;
            if (newState == PlayerLifeState.DyingSequence && inventory != null)
            {
                inventory.ClearAllSlots();
            }
        }

        NotifySanityStateChanged();
    }

    private void ApplyLifeStateLocally(PlayerLifeState state)
    {
        int spectatorLayer = LayerMask.NameToLayer("GhostSpectator");
        if (spectatorLayer < 0) spectatorLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (spectatorLayer < 0) spectatorLayer = 2;

        if (state == PlayerLifeState.Alive)
        {
            gameObject.layer = 0; // Default
            if (playerCollider != null) playerCollider.enabled = true;
            transform.localScale = originalScale;
            UpdatePOVLightScale();

            if (IsOwner)
            {
                DeathSequenceController.Instance?.CancelAndReset();
                GhostSpectatorVisuals.Instance?.SetSpectatorVisualsActive(false);
                NetworkDoor.SetLocalSpectatorDoorBypass(false, playerCollider);
                ApplyPlayerAmbientLightSettings();
                if (playerPovLight != null) playerPovLight.enabled = true;
            }
            else if (playerPovLight != null)
            {
                playerPovLight.enabled = false;
            }
        }
        else if (state == PlayerLifeState.DyingSequence)
        {
            gameObject.layer = spectatorLayer;
            if (playerCollider != null) playerCollider.enabled = false;
            if (rb != null) rb.linearVelocity = Vector2.zero;

            if (playerPovLight != null) playerPovLight.enabled = false;

            if (IsOwner)
            {
                GhostSpectatorVisuals.Instance?.SetSpectatorVisualsActive(false);
                NetworkDoor.SetLocalSpectatorDoorBypass(false, playerCollider);
                DeathSequenceController.EnsureInstance().BeginDeathSequence(this);
            }
        }
        else // PlayerLifeState.GhostSpectator
        {
            gameObject.layer = spectatorLayer;
            if (playerCollider != null) playerCollider.enabled = true;

            if (IsOwner)
            {
                // Snap camera/player softly to death location if not already there
                ApplySpectatorAmbientLightSettings();
                if (playerPovLight != null) playerPovLight.enabled = true;
                GhostSpectatorVisuals.EnsureInstance().SetSpectatorVisualsActive(true);
                NetworkDoor.SetLocalSpectatorDoorBypass(true, playerCollider);
            }
            else if (playerPovLight != null)
            {
                playerPovLight.enabled = false;
            }
        }

        RefreshAllPlayerVisibilityAndCollisions();

        // Update visuals on any equipment held by this player (hidden invisibly from living observers)
        for (int i = 0; i < EquipmentItem.AllEquipment.Count; i++)
        {
            var eq = EquipmentItem.AllEquipment[i];
            if (eq != null)
            {
                eq.RefreshVisualsForHolderState();
            }
        }
    }

    public static void RefreshAllPlayerVisibilityAndCollisions()
    {
        bool viewerIsGhostSpectator = LocalInstance != null && LocalInstance.IsGhostSpectator;
        Color cyanGhostTint = new Color(0.55f, 0.77f, 1.0f, 0.35f);

        for (int i = 0; i < AllPlayers.Count; i++)
        {
            var p = AllPlayers[i];
            if (p == null) continue;

            if (p.spriteRenderer != null)
            {
                bool show = p.IsAlive || (viewerIsGhostSpectator && p.IsGhostSpectator);
                p.spriteRenderer.enabled = show;
                p.spriteRenderer.color = p.IsGhostSpectator ? cyanGhostTint : Color.white;
            }

            if (p.playerCollider == null) continue;

            for (int j = i + 1; j < AllPlayers.Count; j++)
            {
                var other = AllPlayers[j];
                if (other == null || other.playerCollider == null) continue;

                // Disable physical player-vs-player collisions for all player pairs
                // so overlapping networked players never cause Physics2D/NetworkTransform lag spikes.
                Physics2D.IgnoreCollision(p.playerCollider, other.playerCollider, true);
            }
        }
    }

    /// <summary>
    /// Server method to revive a dead player.
    /// </summary>
    public bool RevivePlayer()
    {
        if (!IsServer) return false;
        if (IsAlive) return false;

        if (deathSequenceTimeoutCoroutine != null)
        {
            StopCoroutine(deathSequenceTimeoutCoroutine);
            deathSequenceTimeoutCoroutine = null;
        }

        PlayerCorpse.DespawnForVictimServer(OwnerClientId);
        lifeState.Value = PlayerLifeState.Alive;
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
        if (!IsAlive) return false; // Already dying or dead

        if (inventory == null) inventory = GetComponent<PlayerInventory>();

        Vector3 deathPos3D = rb != null
            ? new Vector3(rb.position.x, rb.position.y, 0f)
            : new Vector3(transform.position.x, transform.position.y, 0f);
        deathWorldPosition.Value = new Vector2(deathPos3D.x, deathPos3D.y);

        // Server-authoritative item drop within 0.06..0.10 units before transitioning life state
        inventory?.ServerDropAllItemsOnDeath(deathPos3D);

        lifeState.Value = PlayerLifeState.DyingSequence;

        PlayerCorpse.SpawnForVictimServer(OwnerClientId, deathPos3D, transform.eulerAngles.z, playerCorpsePrefab);

        if (deathSequenceTimeoutCoroutine != null)
        {
            StopCoroutine(deathSequenceTimeoutCoroutine);
        }
        deathSequenceTimeoutCoroutine = StartCoroutine(ServerDeathSequenceTimeoutRoutine());
        return true;
    }

    [Rpc(SendTo.Server)]
    public void FinishDeathSequenceServerRpc()
    {
        if (lifeState.Value != PlayerLifeState.DyingSequence) return;

        if (deathSequenceTimeoutCoroutine != null)
        {
            StopCoroutine(deathSequenceTimeoutCoroutine);
            deathSequenceTimeoutCoroutine = null;
        }

        lifeState.Value = PlayerLifeState.GhostSpectator;
        GameMatchManager.Instance?.CheckAllPlayersDead();
    }

    private System.Collections.IEnumerator ServerDeathSequenceTimeoutRoutine()
    {
        yield return new WaitForSeconds(8.5f);
        deathSequenceTimeoutCoroutine = null;
        if (lifeState.Value == PlayerLifeState.DyingSequence)
        {
            lifeState.Value = PlayerLifeState.GhostSpectator;
            GameMatchManager.Instance?.CheckAllPlayersDead();
        }
    }

    private bool IsBlockedByWall(Vector2 targetPoint)
    {
        if (IsGhostSpectator || wallLayerMask == 0) return false;

        Vector2 origin = transform.position;
        float dist = Vector2.Distance(origin, targetPoint);
        if (dist <= 1e-5f) return false;

        RaycastHit2D hit = Physics2D.Linecast(origin, targetPoint, wallLayerMask);
        return hit.collider != null && hit.distance < dist - 0.01f;
    }

    public static bool IsEquipmentChannelInteractable(IInteractable interactable)
    {
        return interactable is EquipmentItem || interactable is FrontDoorKeyItem || interactable is PlayerCorpse;
    }

    private IInteractable GetHoveredInteractable()
    {
        return currentlyHoveredEquipment ?? currentlyHoveredEnvironment ?? currentlyHoveredInteractable;
    }

    private IInteractable CalculateHoveredInteractable()
    {
        return CalculateHoveredEquipment() ?? CalculateHoveredEnvironment();
    }

    public void ShowCannotReachBubble()
    {
        if (!IsAlive || IsDyingSequence) return;
        ItemHoverIndicator.TriggerSpeechBubble(transform, ItemHoverIndicator.DefaultCannotReachMessage);
        TriggerReachGuide();
    }

    private void HandleLeftClickDown()
    {
        pendingLeftClickTarget = null;
        var hoveredEq = currentlyHoveredEquipment;
        if (hoveredEq != null)
        {
            bool allowedHover = IsAlive || (IsGhostSpectator && hoveredEq is EquipmentItem eqHover && IsEquipmentAllowedForGhostSpectator(eqHover));
            if (allowedHover && hoveredEq.CanDrag())
            {
                pendingLeftClickTarget = hoveredEq;
                pendingLeftClickStartTime = Time.unscaledTime;
                pendingLeftClickStartScreenPos = Input.mousePosition;
            }
            else if (allowedHover && (hoveredEq is EquipmentItem || hoveredEq is FrontDoorKeyItem))
            {
                hoveredEq.Interact();
            }
            else if (IsAlive && inventory != null)
            {
                inventory.UsePrimary();
            }
        }
        else if (IsAlive && HasUnreachableEquipmentAtCursor())
        {
            ShowCannotReachBubble();
        }
        else if (IsAlive && inventory != null)
        {
            inventory.UsePrimary();
        }
    }

    private bool TryPlaceHeldItemAtMouse()
    {
        if (!IsAlive || inventory == null || inventory.CurrentItem == null) return false;
        if (Vector2.Distance(transform.position, mousePosition) <= maxReachRadius && !IsBlockedByWall(mousePosition))
        {
            inventory.PlaceCurrentItem(mousePosition, transform.rotation);
            return true;
        }
        ShowCannotReachBubble();
        return false;
    }

    private static SpriteRenderer ResolveEquipmentSpriteRenderer(IInteractable interactable)
    {
        if (interactable is EquipmentItem eq)
        {
            return eq.GetComponent<SpriteRenderer>() ?? eq.GetComponentInChildren<SpriteRenderer>();
        }
        return null;
    }

    private bool IsCursorDirectlyOverCollider(Collider2D hit, float cursorRadius, SpriteRenderer sr = null)
    {
        if (hit == null) return false;

        Vector2 closestOnCol = hit.ClosestPoint(mousePosition);
        if (Vector2.Distance(mousePosition, closestOnCol) > cursorRadius)
        {
            return false;
        }

        if (sr != null && sr.enabled && sr.sprite != null)
        {
            Bounds spriteBounds = sr.bounds;
            spriteBounds.Expand(new Vector3(EquipmentSpriteBoundsPadding * 2f, EquipmentSpriteBoundsPadding * 2f, 1f));
            if (!spriteBounds.Contains(new Vector3(mousePosition.x, mousePosition.y, spriteBounds.center.z)))
            {
                return false;
            }
        }

        return true;
    }

    private IInteractable FindEnvironmentInteractable(Vector2 searchOrigin, float searchRadius, float cursorRadius, bool requireReachable)
    {
        if (IsDyingSequence || IsGhostSpectator) return null;

        s_OverlapBuffer.Clear();
        int hitCount = Physics2D.OverlapCircle(searchOrigin, searchRadius, s_NoFilter, s_OverlapBuffer);

        IInteractable bestInteractable = null;
        float bestDistance = float.MaxValue;
        bool bestIsPriorityWorld = false;

        for (int i = 0; i < hitCount; i++)
        {
            var hit = s_OverlapBuffer[i];
            if (hit == null) continue;

            if (hit.GetComponent<EquipmentItem>() != null ||
                hit.GetComponent<FrontDoorKeyItem>() != null ||
                hit.GetComponent<PlayerCorpse>() != null)
            {
                continue;
            }

            var interactable = hit.GetComponent<IInteractable>();
            if (interactable == null)
            {
                interactable = hit.GetComponentInParent<IInteractable>();
            }

            if (interactable == null || IsEquipmentChannelInteractable(interactable))
            {
                continue;
            }

            if (interactable is NetworkDoor door && !door.AllowPlayerInteract)
            {
                continue;
            }

            if (interactable is DoorInteractionProxy doorProxy && doorProxy.TargetDoor != null && !doorProxy.TargetDoor.AllowPlayerInteract)
            {
                continue;
            }

            if (!IsCursorDirectlyOverCollider(hit, cursorRadius))
            {
                continue;
            }

            Vector2 closestToPlayer = hit.ClosestPoint(transform.position);
            bool inReach = Vector2.Distance(transform.position, closestToPlayer) <= maxReachRadius;
            bool blockedByWall = IsBlockedByWall(closestToPlayer);
            bool reachable = inReach && !blockedByWall;

            if (requireReachable != reachable)
            {
                continue;
            }

            bool isPriorityWorld = interactable is SummoningCircle;
            float dist = Vector2.Distance(mousePosition, hit.ClosestPoint(mousePosition));

            if (isPriorityWorld && !bestIsPriorityWorld)
            {
                bestIsPriorityWorld = true;
                bestDistance = dist;
                bestInteractable = interactable;
            }
            else if (isPriorityWorld == bestIsPriorityWorld && dist < bestDistance)
            {
                bestDistance = dist;
                bestInteractable = interactable;
            }
        }
        return bestInteractable;
    }

    private IInteractable FindEquipmentInteractableAtCursor(bool requireReachable)
    {
        if (IsDyingSequence) return null;

        s_OverlapBuffer.Clear();
        int hitCount = Physics2D.OverlapCircle(mousePosition, EquipmentHoverCursorRadius, s_NoFilter, s_OverlapBuffer);

        IInteractable bestInteractable = null;
        float bestDistance = float.MaxValue;
        bool bestIsEquipOrKey = false;
        bool spectating = IsGhostSpectator;

        for (int i = 0; i < hitCount; i++)
        {
            var hit = s_OverlapBuffer[i];
            if (hit == null) continue;

            var interactable = hit.GetComponent<IInteractable>();
            if (interactable == null)
            {
                interactable = hit.GetComponentInParent<IInteractable>();
            }

            if (interactable == null || !IsEquipmentChannelInteractable(interactable))
            {
                continue;
            }

            if (interactable is EquipmentItem equipment)
            {
                if (spectating)
                {
                    if (!IsEquipmentAllowedForGhostSpectator(equipment)) continue;
                }
                else if ((!equipment.IsOnGround && !equipment.IsPlaced) || (equipment is RitualChalkItem && equipment.IsPlaced))
                {
                    continue;
                }

                if (equipment.InteractCollider != null && equipment.InteractCollider != hit)
                {
                    continue;
                }
            }
            else if (spectating)
            {
                continue;
            }

            SpriteRenderer sr = ResolveEquipmentSpriteRenderer(interactable);
            if (!IsCursorDirectlyOverCollider(hit, EquipmentHoverCursorRadius, sr))
            {
                continue;
            }

            Vector2 closestToPlayer = hit.ClosestPoint(transform.position);
            bool inReach = Vector2.Distance(transform.position, closestToPlayer) <= maxReachRadius;
            bool blockedByWall = IsBlockedByWall(closestToPlayer);
            bool reachable = inReach && !blockedByWall;

            if (requireReachable != reachable)
            {
                continue;
            }

            bool isEquipOrKey = (interactable is EquipmentItem) || (interactable is FrontDoorKeyItem);
            float dist = Vector2.Distance(mousePosition, hit.ClosestPoint(mousePosition));

            if (isEquipOrKey && !bestIsEquipOrKey)
            {
                bestIsEquipOrKey = true;
                bestDistance = dist;
                bestInteractable = interactable;
            }
            else if (isEquipOrKey == bestIsEquipOrKey && dist < bestDistance)
            {
                bestDistance = dist;
                bestInteractable = interactable;
            }
        }
        return bestInteractable;
    }

    private bool HasUnreachableEquipmentAtCursor()
    {
        return FindEquipmentInteractableAtCursor(requireReachable: false) != null;
    }

    private bool HasUnreachableDoorAtCursor()
    {
        IInteractable unreachableEnv = FindEnvironmentInteractable(mousePosition, EnvironmentHoverCursorRadius, EnvironmentHoverCursorRadius, requireReachable: false);
        return unreachableEnv is NetworkDoor || unreachableEnv is DoorInteractionProxy;
    }

    private IInteractable CalculateHoveredEnvironment()
    {
        return FindEnvironmentInteractable(mousePosition, EnvironmentHoverCursorRadius, EnvironmentHoverCursorRadius, requireReachable: true);
    }

    private IInteractable CalculateHoveredEquipment()
    {
        return FindEquipmentInteractableAtCursor(requireReachable: true);
    }

    private void TryInteract()
    {
        if (!IsAlive) return;

        if (currentlyHoveredEnvironment != null)
        {
            currentlyHoveredEnvironment.Interact();
        }
        else if (HasUnreachableDoorAtCursor())
        {
            ShowCannotReachBubble();
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

        if (IsInteractionBlocked || IsDyingSequence)
        {
            ItemHoverIndicator.Instance.Hide();
            return;
        }

        var hovered = currentlyHoveredEquipment ?? currentlyHoveredEnvironment ?? currentlyHoveredInteractable;
        if (hovered != null)
        {
            if (hovered is EquipmentItem equipment)
            {
                bool allowed = IsGhostSpectator
                    ? IsEquipmentAllowedForGhostSpectator(equipment)
                    : !(equipment is RitualChalkItem && equipment.IsPlaced) && (equipment.IsOnGround || equipment.IsPlaced);

                if (allowed)
                {
                    float distToMouse = Vector2.Distance(transform.position, mousePosition);
                    if (distToMouse <= maxReachRadius + EquipmentHoverCursorRadius)
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
            else if (IsAlive && hovered is PlayerCorpse corpse)
            {
                if (TryShowWorldInteractableHover(corpse, corpse.GetInteractText())) return;
            }
            else if (IsAlive && hovered is SummoningCircle summoningCircle)
            {
                if (TryShowWorldInteractableHover(summoningCircle, summoningCircle.GetInteractText())) return;
            }
            else if (IsAlive && hovered is LobbyBriefingTableInteractable briefingTable)
            {
                if (TryShowWorldInteractableHover(briefingTable, briefingTable.GetInteractText())) return;
            }
            else if (hovered is FrontDoorKeyItem keyItem)
            {
                float distToMouse = Vector2.Distance(transform.position, mousePosition);
                if (distToMouse <= maxReachRadius + EquipmentHoverCursorRadius)
                {
                    ItemHoverIndicator.Instance.Show(keyItem.gameObject, "Front Door Key x1");
                    return;
                }
            }
            else if (hovered is TruckEndGameButton endBtn)
            {
                if (TryShowWorldInteractableHover(endBtn, endBtn.GetInteractText())) return;
            }
            else if (hovered is HouseLightSwitch lightSwitch)
            {
                if (TryShowWorldInteractableHover(lightSwitch, lightSwitch.GetInteractText())) return;
            }
            else if (hovered is FuseBox fuseBox)
            {
                if (TryShowWorldInteractableHover(fuseBox, fuseBox.GetInteractText())) return;
            }
            else if (hovered is TruckDashboardController dashboard)
            {
                float distToMouse = Vector2.Distance(transform.position, mousePosition);
                if (distToMouse <= maxReachRadius + EnvironmentHoverCursorRadius)
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
        if (distToMouse <= maxReachRadius + EnvironmentHoverCursorRadius)
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
        if (!AllPlayers.Contains(this))
        {
            AllPlayers.Add(this);
        }
        RefreshAllPlayerVisibilityAndCollisions();
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedLocal;
        AllPlayers.Remove(this);
        if (IsOwner)
        {
            pendingLeftClickTarget = null;
            EndCurrentDrag();
            ItemHoverIndicator.Instance?.Hide();
            ItemHoverIndicator.Instance?.HideSpeechBubble();
        }
    }

    private void OnSceneLoadedLocal(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (IsOwner)
        {
            WeatherManager.SetLocalInBurialMap(false);
            DeathSequenceController.Instance?.CancelAndReset();
            GhostSpectatorVisuals.Instance?.SetSpectatorVisualsActive(false);
            CameraFollow.Instance?.SetTargetOverride(null);
            if (IsSpawned)
            {
                StartCoroutine(DelayedTeleportToSpawn());
            }
        }
        RefreshAllPlayerVisibilityAndCollisions();
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