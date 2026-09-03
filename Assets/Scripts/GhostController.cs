using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public enum GhostState
{
    Wander,
    Interact,
    Evidence,
    HuntManifest,
    HuntSearch,
    HuntChase
}

[RequireComponent(typeof(Rigidbody2D))]
public class GhostController : NetworkBehaviour
{
    [Header("Ghost Identity & Evidence")]
    [SerializeField] private string ghostName = "White Lady";
    [SerializeField] private bool evidenceEmf5 = true;
    [SerializeField] private bool evidenceFreezingTemps = true;
    [SerializeField] private bool evidenceGhostWriting = false;

    [Header("Movement Speeds")]
    [SerializeField] private float wanderSpeed = 1.6f;
    [SerializeField] private float searchSpeed = 2.4f;
    [SerializeField] private float chaseSpeed = 3.8f;

    [Header("Vision & Detection")]
    [SerializeField] private float visionConeAngle = 120f;
    [SerializeField] private float visionDistance = 8f;
    [SerializeField] private float proximityRadius = 2f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Roaming Settings")]
    [SerializeField] private Vector2 favoriteRoomCenter = Vector2.zero;
    [SerializeField] private float roamRadius = 7f;

    [Header("Hunt Settings")]
    [SerializeField] private float huntGracePeriod = 3f;
    [SerializeField] private float huntDuration = 30f;
    [SerializeField] private float huntCooldown = 45f;

    [Header("Visuals & Audio")]
    [SerializeField] private SpriteRenderer ghostSprite;
    [SerializeField] private Light2D ghostAuraLight;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip manifestAudio;
    [SerializeField] private AudioClip chaseAudio;

    // Network synced state
    private NetworkVariable<GhostState> currentState = new NetworkVariable<GhostState>(
        GhostState.Wander, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isVisuallyManifested = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public GhostState CurrentState => currentState.Value;
    public bool IsHunting => currentState.Value == GhostState.HuntManifest ||
                             currentState.Value == GhostState.HuntSearch ||
                             currentState.Value == GhostState.HuntChase;

    private Rigidbody2D rb;
    private Vector2 currentDestination;
    private PlayerController chaseTargetPlayer;
    private Vector2 lastSeenPlayerPosition;

    private float stateTimer = 0f;
    private float huntTimer = 0f;
    private float nextHuntAllowedTime = 0f;
    private float lostTargetTimer = 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (ghostSprite == null) ghostSprite = GetComponentInChildren<SpriteRenderer>();
        if (ghostAuraLight == null) ghostAuraLight = GetComponentInChildren<Light2D>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        currentState.OnValueChanged += (oldVal, newVal) => OnStateChanged(newVal);
        isVisuallyManifested.OnValueChanged += (oldVal, newVal) => UpdateVisuals(newVal);

        if (IsServer)
        {
            favoriteRoomCenter = transform.position;
            currentDestination = favoriteRoomCenter;
            nextHuntAllowedTime = Time.time + huntCooldown;

            if (ParanormalManager.Instance != null)
            {
                ParanormalManager.Instance.SetGhostInfo(transform, favoriteRoomCenter, roamRadius, evidenceFreezingTemps);
            }
        }

        UpdateVisuals(isVisuallyManifested.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.ClearGhostInfo();
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        stateTimer += Time.deltaTime;

        switch (currentState.Value)
        {
            case GhostState.Wander:
                UpdateWanderState();
                break;
            case GhostState.Interact:
                UpdateInteractState();
                break;
            case GhostState.Evidence:
                UpdateEvidenceState();
                break;
            case GhostState.HuntManifest:
                UpdateHuntManifestState();
                break;
            case GhostState.HuntSearch:
                UpdateHuntSearchState();
                break;
            case GhostState.HuntChase:
                UpdateHuntChaseState();
                break;
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // Move towards currentDestination
        float speed = wanderSpeed;
        if (currentState.Value == GhostState.HuntChase) speed = chaseSpeed;
        else if (currentState.Value == GhostState.HuntSearch) speed = searchSpeed;

        Vector2 direction = (currentDestination - rb.position).normalized;
        float distance = Vector2.Distance(rb.position, currentDestination);

        if (distance > 0.3f)
        {
            // Simple 2D obstacle avoidance raycast
            RaycastHit2D hit = Physics2D.CircleCast(rb.position, 0.4f, direction, 0.8f, obstacleLayer);
            if (hit.collider != null)
            {
                // Adjust direction slightly around normal
                direction = Vector2.Perpendicular(hit.normal).normalized;
            }

            rb.MovePosition(rb.position + direction * speed * Time.fixedDeltaTime);

            // Rotate smoothly towards movement direction
            if (direction.sqrMagnitude > 0.01f)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
                rb.MoveRotation(Mathf.LerpAngle(rb.rotation, angle, Time.fixedDeltaTime * 6f));
            }
        }
    }

    #region State Machine Logic (Server)

    private void UpdateWanderState()
    {
        float dist = Vector2.Distance(rb.position, currentDestination);
        if (dist <= 0.5f || stateTimer > 12f)
        {
            stateTimer = 0f;

            // Random chance for interaction or evidence drop
            float roll = Random.value;
            if (roll < 0.35f)
            {
                SetState(GhostState.Interact);
                return;
            }
            else if (roll < 0.55f)
            {
                SetState(GhostState.Evidence);
                return;
            }

            // Pick new wander destination near favorite room
            Vector2 randomOffset = Random.insideUnitCircle * roamRadius;
            currentDestination = favoriteRoomCenter + randomOffset;
        }

        // Automatic hunt trigger if cooldown has elapsed
        if (Time.time >= nextHuntAllowedTime && Random.value < 0.002f)
        {
            StartHunt();
        }
    }

    private void UpdateInteractState()
    {
        PerformParanormalInteraction();
        SetState(GhostState.Wander);
    }

    private void UpdateEvidenceState()
    {
        // Emit evidence (e.g. EMF 5 spike if ghost possesses that trait)
        if (evidenceEmf5 && ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(transform.position, 5, 20f);
        }

        SetState(GhostState.Wander);
    }

    private void UpdateHuntManifestState()
    {
        // Grace period countdown
        if (stateTimer >= huntGracePeriod)
        {
            SetState(GhostState.HuntSearch);
        }
    }

    private void UpdateHuntSearchState()
    {
        huntTimer += Time.deltaTime;
        if (huntTimer >= huntDuration)
        {
            EndHunt();
            return;
        }

        // Check if any player enters vision cone or proximity circle
        PlayerController detectedPlayer = ScanForPlayers();
        if (detectedPlayer != null)
        {
            chaseTargetPlayer = detectedPlayer;
            lastSeenPlayerPosition = detectedPlayer.transform.position;
            currentDestination = lastSeenPlayerPosition;
            SetState(GhostState.HuntChase);
            return;
        }

        // Roam to search destinations
        if (Vector2.Distance(rb.position, currentDestination) <= 0.6f || stateTimer > 6f)
        {
            stateTimer = 0f;
            currentDestination = rb.position + Random.insideUnitCircle * 8f;
        }
    }

    private void UpdateHuntChaseState()
    {
        huntTimer += Time.deltaTime;
        if (huntTimer >= huntDuration)
        {
            EndHunt();
            return;
        }

        if (chaseTargetPlayer == null || !chaseTargetPlayer.IsAlive)
        {
            SetState(GhostState.HuntSearch);
            return;
        }

        // Check if target is hidden in a HideZone or broke line of sight
        bool canSee = CanSeePlayer(chaseTargetPlayer);

        if (canSee && !chaseTargetPlayer.IsHiddenFromGhost)
        {
            lastSeenPlayerPosition = chaseTargetPlayer.transform.position;
            currentDestination = lastSeenPlayerPosition;
            lostTargetTimer = 0f;
        }
        else
        {
            // Player broke LOS or crouched behind cover
            lostTargetTimer += Time.deltaTime;
            currentDestination = lastSeenPlayerPosition;

            // Give up chase after 2.5s of losing LOS and revert to search
            if (lostTargetTimer >= 2.5f || Vector2.Distance(rb.position, lastSeenPlayerPosition) <= 0.6f)
            {
                chaseTargetPlayer = null;
                SetState(GhostState.HuntSearch);
                return;
            }
        }

        // Kill check
        float distToPlayer = Vector2.Distance(rb.position, chaseTargetPlayer.transform.position);
        if (distToPlayer <= 0.9f)
        {
            chaseTargetPlayer.KillPlayer();
            EndHunt();
        }
    }

    private void PerformParanormalInteraction()
    {
        // 1. Find nearby doors or interactables
        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, 3.5f);
        foreach (var col in colliders)
        {
            var door = col.GetComponent<NetworkDoor>();
            if (door != null)
            {
                door.GhostInteract();
                return;
            }

            var propRb = col.GetComponent<Rigidbody2D>();
            if (propRb != null && col.gameObject != gameObject && !col.CompareTag("Player"))
            {
                // Throw physical prop
                Vector2 throwDir = Random.insideUnitCircle.normalized;
                propRb.AddForce(throwDir * 250f);
                if (ParanormalManager.Instance != null)
                {
                    ParanormalManager.Instance.RegisterEvent(propRb.position, 3, 20f);
                }
                return;
            }
        }

        // Fallback: register ghost presence event
        if (ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(transform.position, 2, 15f);
        }
    }

    public void StartHunt()
    {
        if (!IsServer || IsHunting) return;

        huntTimer = 0f;
        stateTimer = 0f;
        isVisuallyManifested.Value = true;

        // Lock all exit doors
        SetDoorsLocked(true);

        SetState(GhostState.HuntManifest);
    }

    public void EndHunt()
    {
        if (!IsServer) return;

        isVisuallyManifested.Value = false;
        nextHuntAllowedTime = Time.time + huntCooldown;

        // Unlock all doors
        SetDoorsLocked(false);

        SetState(GhostState.Wander);
    }

    private void SetDoorsLocked(bool locked)
    {
        var doors = Object.FindObjectsByType<NetworkDoor>(FindObjectsSortMode.None);
        foreach (var door in doors)
        {
            if (door.IsExitDoor || locked)
            {
                door.SetLocked(locked);
            }
        }
    }

    private void SetState(GhostState newState)
    {
        currentState.Value = newState;
        stateTimer = 0f;
    }

    #endregion

    #region Vision & Detection

    private PlayerController ScanForPlayers()
    {
        foreach (var player in PlayerController.AllPlayers)
        {
            if (player == null || !player.IsAlive) continue;
            if (player.IsHiddenFromGhost) continue;

            float distance = Vector2.Distance(rb.position, player.transform.position);

            // 1. Proximity check (360 degrees)
            if (distance <= proximityRadius)
            {
                if (HasLineOfSight(player.transform.position))
                {
                    return player;
                }
            }

            // 2. Vision Cone check
            if (distance <= visionDistance)
            {
                Vector2 dirToPlayer = ((Vector2)player.transform.position - rb.position).normalized;
                float angle = Vector2.Angle(transform.up, dirToPlayer);

                if (angle <= (visionConeAngle * 0.5f))
                {
                    if (HasLineOfSight(player.transform.position))
                    {
                        return player;
                    }
                }
            }
        }

        return null;
    }

    private bool CanSeePlayer(PlayerController player)
    {
        if (player == null || !player.IsAlive || player.IsHiddenFromGhost) return false;

        float distance = Vector2.Distance(rb.position, player.transform.position);
        if (distance > visionDistance * 1.3f) return false;

        return HasLineOfSight(player.transform.position);
    }

    private bool HasLineOfSight(Vector2 targetPos)
    {
        Vector2 dir = targetPos - rb.position;
        float dist = dir.magnitude;

        RaycastHit2D hit = Physics2D.Raycast(rb.position, dir.normalized, dist, obstacleLayer);
        return hit.collider == null;
    }

    #endregion

    private void OnStateChanged(GhostState newState)
    {
        if (newState == GhostState.HuntManifest && manifestAudio != null && audioSource != null)
        {
            audioSource.PlayOneShot(manifestAudio);
        }
        else if (newState == GhostState.HuntChase && chaseAudio != null && audioSource != null)
        {
            if (!audioSource.isPlaying) audioSource.PlayOneShot(chaseAudio);
        }
    }

    private void UpdateVisuals(bool manifested)
    {
        if (ghostSprite != null)
        {
            // During wander, ghost is fully invisible (or very subtle alpha)
            // During hunts, ghost manifests visually
            ghostSprite.enabled = manifested;
        }

        if (ghostAuraLight != null)
        {
            ghostAuraLight.enabled = manifested;
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Visualize favorite room roam radius
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(favoriteRoomCenter, roamRadius);

        // Visualize vision cone
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, proximityRadius);
        Gizmos.DrawRay(transform.position, Quaternion.Euler(0, 0, visionConeAngle * 0.5f) * transform.up * visionDistance);
        Gizmos.DrawRay(transform.position, Quaternion.Euler(0, 0, -visionConeAngle * 0.5f) * transform.up * visionDistance);
    }
}
