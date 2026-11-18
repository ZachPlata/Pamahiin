using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Safe, scripted ghost controller for WarehouseTutorialScene (Bays 3 & 4).
/// Uses Pamahiin's ghost_idle_0 and ghost_hunt_0 sprites, provides visible presence in Bay 3,
/// and runs a non-lethal simulated hunt in Bay 4 that resets the player with a hint if caught.
/// </summary>
public class TutorialGhostController : MonoBehaviour
{
    [Header("Visuals & Sprites")]
    [SerializeField] private SpriteRenderer ghostRenderer;
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite huntSprite;

    [Header("Hunt Settings")]
    [SerializeField] private Transform[] patrolWaypoints;
    [SerializeField] private float huntSpeed = 0.11f;
    [SerializeField] private float catchRadius = 0.07f;
    [SerializeField] private float simulatedHuntDuration = 10f;
    [SerializeField] private float huntGracePeriod = 4f;
    [SerializeField] private float resetGracePeriod = 3f;

    public bool CrucifixBlocked { get; private set; }
    public bool IsHunting { get; private set; }
    public bool IsHuntCompleted { get; private set; }
    public bool IsBanished { get; private set; }
    public bool DotsTriggered { get; private set; }

    public event Action OnCrucifixBlocked;
    public event Action<int> OnHuntWarningTick;
    public event Action OnHuntStarted;
    public event Action OnPlayerCaughtSafely;
    public event Action OnHuntCompleted;

    private Coroutine activeHuntRoutine;
    private Vector3 initialPosition;
    private Transform containmentSpot;

    private void Awake()
    {
        initialPosition = transform.position;
        if (ghostRenderer == null)
        {
            ghostRenderer = GetComponentInChildren<SpriteRenderer>();
        }
        ResolveFallbackSprites();
        ApplySprite(idleSprite);
        SetGhostVisible(true, 0.65f);
    }

    private void Update()
    {
        if (IsBanished || IsHunting || ghostRenderer == null || !ghostRenderer.enabled) return;

        // Subtle spectral hover bob when idle in Bay 3 containment
        Vector3 anchor = containmentSpot != null ? containmentSpot.position : initialPosition;
        float bob = Mathf.Sin(Time.time * 2.2f) * 0.015f;
        transform.position = new Vector3(anchor.x, anchor.y + bob, anchor.z);
    }

    private void ResolveFallbackSprites()
    {
        if (idleSprite == null)
        {
            idleSprite = Resources.Load<Sprite>("Sprites/ghost_idle_0");
        }
        if (huntSprite == null)
        {
            huntSprite = Resources.Load<Sprite>("Sprites/ghost_hunt_0");
        }
        if (huntSprite == null) huntSprite = idleSprite;
    }

    private void ApplySprite(Sprite sprite)
    {
        if (ghostRenderer != null && sprite != null)
        {
            ghostRenderer.sprite = sprite;
        }
    }

    public void SetContainmentAnchor(Transform anchor)
    {
        containmentSpot = anchor;
        if (anchor != null && !IsHunting && !IsBanished)
        {
            transform.position = anchor.position;
        }
    }

    public void SetGhostVisible(bool visible, float alpha = 0.75f)
    {
        if (ghostRenderer == null) return;
        ghostRenderer.enabled = visible;
        Color c = ghostRenderer.color;
        c.a = alpha;
        ghostRenderer.color = c;
    }

    public void TriggerDotsSilhouette()
    {
        DotsTriggered = true;
        if (!IsBanished)
        {
            ApplySprite(idleSprite);
            SetGhostVisible(true, 0.8f);
        }
    }

    public bool TryBlockWithCrucifix(CrucifixItem crucifix)
    {
        if (CrucifixBlocked) return true;
        if (crucifix == null || crucifix.IsBurned) return false;

        if (crucifix.TryBlockHunt())
        {
            CrucifixBlocked = true;
            OnCrucifixBlocked?.Invoke();
            return true;
        }
        return false;
    }

    public void TriggerGhostWriting(GhostWritingBookItem book)
    {
        if (book != null && !book.IsWritten)
        {
            book.WriteInBook();
        }
    }

    public void StartNonLethalHunt(Transform playerTransform, Transform resetPoint, HideZone hideZone)
    {
        if (IsHunting || IsHuntCompleted || IsBanished) return;
        if (activeHuntRoutine != null) StopCoroutine(activeHuntRoutine);
        activeHuntRoutine = StartCoroutine(NonLethalHuntRoutine(playerTransform, resetPoint, hideZone));
    }

    public void PlayBanishEffect()
    {
        StopHunt();
        IsBanished = true;
        StartCoroutine(BanishFadeRoutine());
    }

    private IEnumerator BanishFadeRoutine()
    {
        ApplySprite(huntSprite);
        SetGhostVisible(true, 0.9f);
        float t = 0f;
        Vector3 startScale = transform.localScale;
        while (t < 0.8f)
        {
            t += Time.deltaTime;
            float p = t / 0.8f;
            SetGhostVisible(true, Mathf.Lerp(0.9f, 0f, p));
            transform.localScale = startScale * (1f + p * 0.4f);
            yield return null;
        }
        SetGhostVisible(false, 0f);
        transform.localScale = startScale;
    }

    public void StopHunt()
    {
        if (activeHuntRoutine != null)
        {
            StopCoroutine(activeHuntRoutine);
            activeHuntRoutine = null;
        }
        IsHunting = false;
        ApplySprite(idleSprite);
        SetGhostVisible(!IsBanished, 0.65f);
        transform.position = initialPosition;
    }

    private IEnumerator RunGracePeriodRoutine(float duration)
    {
        float remaining = duration;
        int lastShownSecond = -1;
        while (remaining > 0f)
        {
            int sec = Mathf.CeilToInt(remaining);
            if (sec != lastShownSecond)
            {
                lastShownSecond = sec;
                OnHuntWarningTick?.Invoke(sec);
            }

            // Flicker warning visual while stationary at initialPosition
            float flickerAlpha = (Mathf.Sin(Time.time * 18f) > 0f) ? 0.75f : 0.25f;
            SetGhostVisible(true, flickerAlpha);

            remaining -= Time.deltaTime;
            yield return null;
        }
        SetGhostVisible(true, 0.92f);
    }

    private IEnumerator NonLethalHuntRoutine(Transform playerTransform, Transform resetPoint, HideZone hideZone)
    {
        IsHunting = true;
        transform.position = initialPosition;
        ApplySprite(huntSprite);

        yield return RunGracePeriodRoutine(huntGracePeriod);

        OnHuntStarted?.Invoke();

        float elapsed = 0f;
        int waypointIndex = 0;

        while (elapsed < simulatedHuntDuration)
        {
            elapsed += Time.deltaTime;

            bool playerHidden = IsPlayerSafelyHidden(playerTransform, hideZone);
            Vector3 targetPos = GetHuntTargetPosition(playerTransform, playerHidden, ref waypointIndex);

            transform.position = Vector3.MoveTowards(transform.position, targetPos, huntSpeed * Time.deltaTime);

            if (ghostRenderer != null && Mathf.Abs(targetPos.x - transform.position.x) > 0.005f)
            {
                ghostRenderer.flipX = targetPos.x < transform.position.x;
            }

            if (!playerHidden && playerTransform != null &&
                Vector2.Distance(transform.position, playerTransform.position) <= catchRadius)
            {
                if (resetPoint != null)
                {
                    playerTransform.position = resetPoint.position;
                }
                transform.position = initialPosition;
                elapsed = 0f;
                OnPlayerCaughtSafely?.Invoke();
                yield return RunGracePeriodRoutine(resetGracePeriod);
                continue;
            }

            yield return null;
        }

        IsHunting = false;
        IsHuntCompleted = true;
        ApplySprite(idleSprite);
        SetGhostVisible(true, 0.55f);
        transform.position = initialPosition;
        activeHuntRoutine = null;
        OnHuntCompleted?.Invoke();
    }

    private bool IsPlayerSafelyHidden(Transform playerTransform, HideZone hideZone)
    {
        if (playerTransform == null) return false;
        var player = playerTransform.GetComponent<PlayerController>();
        if (player == null) return false;

        bool inZone = player.IsInHideZone;
        if (!inZone)
        {
            var playerCol = playerTransform.GetComponent<Collider2D>();
            var allHideZones = FindObjectsByType<HideZone>();
            foreach (var hz in allHideZones)
            {
                if (hz == null) continue;
                var col = hz.GetComponent<Collider2D>();
                if (col == null) continue;
                if (col.OverlapPoint(playerTransform.position) ||
                    (playerCol != null && col.bounds.Intersects(playerCol.bounds)))
                {
                    inZone = true;
                    break;
                }
            }
        }

        if (!inZone) return false;

        var flashlights = FindObjectsByType<FlashlightItem>();
        foreach (var fl in flashlights)
        {
            if (fl != null && !fl.IsOnGround && !fl.IsPlaced && fl.IsLightOn)
            {
                return false;
            }
        }

        return true;
    }

    private Vector3 GetHuntTargetPosition(Transform playerTransform, bool playerHidden, ref int waypointIndex)
    {
        if (!playerHidden && playerTransform != null)
        {
            return playerTransform.position;
        }

        if (patrolWaypoints != null && patrolWaypoints.Length > 0)
        {
            Transform wp = patrolWaypoints[waypointIndex % patrolWaypoints.Length];
            if (wp != null)
            {
                if (Vector2.Distance(transform.position, wp.position) < 0.03f)
                {
                    waypointIndex = (waypointIndex + 1) % patrolWaypoints.Length;
                }
                return wp.position;
            }
        }

        return initialPosition;
    }
}
