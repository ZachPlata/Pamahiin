using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked corpse spawned at the victim's death location.
/// Plays a 12-frame top-down choking/collapse animation via Animator on spawn, then settles
/// into an articulated 6-part 2D ragdoll (<see cref="CorpseRagdollController"/>).
/// Living teammates (IsAlive) can grab and drag the body using Left Mouse Button; dead spectators cannot.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(PolygonCollider2D))]
public class PlayerCorpse : NetworkBehaviour, IInteractable
{
    public static readonly List<PlayerCorpse> AllCorpses = new List<PlayerCorpse>();

    public static readonly Vector3 CorpseLocalScale = new Vector3(0.19f, 0.19f, 1f);

    /// <summary>
    /// Snug anatomical trigger contour (~0.44 x 0.66 local -> ~0.084 x 0.125 world at 0.19 scale)
    /// for ItemHoverIndicator silhouette highlighting and cursor pickup detection.
    /// </summary>
    public static readonly Vector2[] DefaultCorpsePolygonContour =
    {
        new Vector2(0.00f, 0.33f),
        new Vector2(0.12f, 0.27f),
        new Vector2(0.22f, 0.13f),
        new Vector2(0.18f, -0.09f),
        new Vector2(0.13f, -0.33f),
        new Vector2(-0.13f, -0.33f),
        new Vector2(-0.18f, -0.09f),
        new Vector2(-0.22f, 0.13f),
        new Vector2(-0.12f, 0.27f)
    };

    private const string ChokeAnimControllerResourcePath = "Animations/CorpseAnim";
    private const string ChokeSheetResourcePath = "Sprites/CHARACTERS/player_death_choke_sheet";
    private const string ChokeStateName = "CorpseChoke";
    private const float ChokeFrameRate = 12f;
    private const int ChokeFrameCount = 12;
    private const float ChokeDurationFallback = 1.0f;
    private const float DragWallCheckRadius = 0.032f;
    private const float DragMaxDistancePerStep = 0.25f;

    private NetworkVariable<ulong> victimClientId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<Vector2> syncedPosition = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<float> syncedRotation = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<ulong> draggerClientId = new NetworkVariable<ulong>(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private SpriteRenderer spriteRenderer;
    private PolygonCollider2D interactTrigger;
    private Animator chokeAnimator;
    private CorpseRagdollController ragdollController;
    private Sprite[] fallbackChokeSprites;
    private Coroutine animCoroutine;

    private bool isLocallyDragging;
    private Vector2 localDragTarget;
    private Vector2 lastSentDragTarget;
    private float nextDragRpcTime;
    private int wallLayerMask;

    public ulong VictimClientId => victimClientId.Value;
    public PolygonCollider2D InteractTrigger => interactTrigger;
    public CorpseRagdollController RagdollController => ragdollController;

    public static void ResetCachedAssetsForTests()
    {
        // Preserved for test compatibility; sliced Sprite sub-assets are loaded via Resources.
    }

    private void Awake()
    {
        transform.localScale = CorpseLocalScale;

        spriteRenderer = GetComponent<SpriteRenderer>();
        interactTrigger = GetComponent<PolygonCollider2D>();
        chokeAnimator = GetComponent<Animator>();
        if (chokeAnimator == null)
        {
            chokeAnimator = gameObject.AddComponent<Animator>();
        }
        chokeAnimator.updateMode = AnimatorUpdateMode.Normal;
        chokeAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (chokeAnimator.runtimeAnimatorController == null)
        {
            RuntimeAnimatorController ctrl = Resources.Load<RuntimeAnimatorController>(ChokeAnimControllerResourcePath);
            if (ctrl != null)
            {
                chokeAnimator.runtimeAnimatorController = ctrl;
            }
        }

        wallLayerMask = LayerMask.GetMask("Walls");

        int interactLayer = LayerMask.NameToLayer("Interactable");
        if (interactLayer >= 0)
        {
            gameObject.layer = interactLayer;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.sortingLayerName = "DOTSLayer";
            spriteRenderer.sortingOrder = -1;
        }

        if (interactTrigger != null)
        {
            interactTrigger.isTrigger = true;
            interactTrigger.offset = Vector2.zero;
            interactTrigger.SetPath(0, DefaultCorpsePolygonContour);
        }

        ragdollController = GetComponent<CorpseRagdollController>();
        if (ragdollController == null)
        {
            ragdollController = gameObject.AddComponent<CorpseRagdollController>();
        }
        ragdollController.EnsureInitialized(interactTrigger);
        ragdollController.ActivateRagdoll(false);
    }

    public override void OnNetworkSpawn()
    {
        if (!AllCorpses.Contains(this))
        {
            AllCorpses.Add(this);
        }

        syncedPosition.OnValueChanged += OnSyncedTransformChanged;
        syncedRotation.OnValueChanged += OnSyncedRotationChanged;

        if (!IsServer)
        {
            ApplySyncedTransform(syncedPosition.Value, syncedRotation.Value);
        }

        StartChokeAnimation();
    }

    public override void OnNetworkDespawn()
    {
        AllCorpses.Remove(this);
        syncedPosition.OnValueChanged -= OnSyncedTransformChanged;
        syncedRotation.OnValueChanged -= OnSyncedRotationChanged;

        if (animCoroutine != null)
        {
            StopCoroutine(animCoroutine);
            animCoroutine = null;
        }
    }

    public override void OnDestroy()
    {
        AllCorpses.Remove(this);
        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsSpawned) return;

        if (IsServer && draggerClientId.Value != ulong.MaxValue)
        {
            // Release corpse automatically if the dragging player dies or disconnects
            if (!IsPlayerAlive(draggerClientId.Value))
            {
                draggerClientId.Value = ulong.MaxValue;
            }
        }

        if (!IsServer && !isLocallyDragging)
        {
            Vector3 currentPos = transform.position;
            Vector3 targetPos = new Vector3(syncedPosition.Value.x, syncedPosition.Value.y, 0f);
            Vector2 moveDelta = Vector2.zero;
            if ((currentPos - targetPos).sqrMagnitude > 0.0001f)
            {
                Vector3 nextPos = Vector3.Lerp(currentPos, targetPos, Time.deltaTime * 22f);
                moveDelta = new Vector2(nextPos.x - currentPos.x, nextPos.y - currentPos.y);
                transform.position = nextPos;
            }

            float targetZ = syncedRotation.Value;
            float currentZ = transform.eulerAngles.z;
            if (Mathf.Abs(Mathf.DeltaAngle(currentZ, targetZ)) > 0.25f)
            {
                float nextZ = Mathf.LerpAngle(currentZ, targetZ, Time.deltaTime * 14f);
                transform.rotation = Quaternion.Euler(0f, 0f, nextZ);
            }

            if (ragdollController != null)
            {
                bool remoteDragging = draggerClientId.Value != ulong.MaxValue;
                if (ragdollController.IsDragging != remoteDragging)
                {
                    ragdollController.SetDragging(remoteDragging);
                }
                if (moveDelta.sqrMagnitude > 0.000001f)
                {
                    ragdollController.OnDragMotion(moveDelta, Time.deltaTime);
                }
            }
        }
    }

    private void OnSyncedTransformChanged(Vector2 oldVal, Vector2 newVal)
    {
        if (IsServer || isLocallyDragging) return;
        transform.position = new Vector3(newVal.x, newVal.y, 0f);
    }

    private void OnSyncedRotationChanged(float oldVal, float newVal)
    {
        if (IsServer) return;
        transform.rotation = Quaternion.Euler(0f, 0f, newVal);
    }

    private void ApplySyncedTransform(Vector2 pos, float rotZ)
    {
        transform.position = new Vector3(pos.x, pos.y, 0f);
        transform.rotation = Quaternion.Euler(0f, 0f, rotZ);
    }

    private void StartChokeAnimation()
    {
        if (animCoroutine != null)
        {
            StopCoroutine(animCoroutine);
            animCoroutine = null;
        }
        animCoroutine = StartCoroutine(PlayChokeAnimationRoutine());
    }

    private IEnumerator PlayChokeAnimationRoutine()
    {
        if (ragdollController != null)
        {
            ragdollController.ActivateRagdoll(false);
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
            spriteRenderer.sortingLayerName = "DOTSLayer";
            spriteRenderer.sortingOrder = -1;
        }

        if (chokeAnimator != null && chokeAnimator.runtimeAnimatorController != null)
        {
            chokeAnimator.enabled = true;
            chokeAnimator.Play(ChokeStateName, 0, 0f);
            chokeAnimator.Update(0f);

            float elapsed = 0f;
            float maxWait = ChokeDurationFallback + 0.15f;
            while (elapsed < maxWait)
            {
                elapsed += Time.deltaTime;
                AnimatorStateInfo info = chokeAnimator.GetCurrentAnimatorStateInfo(0);
                if ((info.IsName(ChokeStateName) && info.normalizedTime >= 1f) || elapsed >= ChokeDurationFallback)
                {
                    break;
                }
                yield return null;
            }
        }
        else
        {
            EnsureFallbackChokeSpritesLoaded();
            if (spriteRenderer != null && fallbackChokeSprites != null && fallbackChokeSprites.Length > 0)
            {
                float frameDur = 1f / ChokeFrameRate;
                for (int i = 0; i < fallbackChokeSprites.Length; i++)
                {
                    if (fallbackChokeSprites[i] != null)
                    {
                        spriteRenderer.sprite = fallbackChokeSprites[i];
                    }
                    if (i < fallbackChokeSprites.Length - 1)
                    {
                        yield return new WaitForSeconds(frameDur);
                    }
                }
            }
            else
            {
                yield return new WaitForSeconds(ChokeDurationFallback);
            }
        }

        TransitionChokeToRagdoll();
        animCoroutine = null;
    }

    private void TransitionChokeToRagdoll()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false;
        }
        if (chokeAnimator != null)
        {
            chokeAnimator.enabled = false;
        }
        if (ragdollController != null)
        {
            ragdollController.ActivateRagdoll(true);
        }
    }

    private void EnsureFallbackChokeSpritesLoaded()
    {
        if (fallbackChokeSprites != null && fallbackChokeSprites.Length > 0) return;

        Sprite[] loaded = Resources.LoadAll<Sprite>(ChokeSheetResourcePath);
        if (loaded == null || loaded.Length == 0)
        {
            fallbackChokeSprites = System.Array.Empty<Sprite>();
            return;
        }
        System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a != null ? a.name : string.Empty, b != null ? b.name : string.Empty));
        fallbackChokeSprites = loaded;
    }

    // ── Spawning / Cleanup Helpers (Server-Authoritative) ───────────────────

    public static PlayerCorpse SpawnForVictimServer(ulong victimId, Vector3 worldPos, float zRot, GameObject explicitPrefab = null)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return null;

        // Avoid duplicate corpses for the same victim
        foreach (var existing in AllCorpses)
        {
            if (existing != null && existing.IsSpawned && existing.VictimClientId == victimId)
            {
                existing.ServerResetPosition(worldPos, zRot);
                return existing;
            }
        }

        GameObject prefab = ResolveCorpsePrefab(explicitPrefab);
        if (prefab == null)
        {
            Debug.LogWarning("[PlayerCorpse] Could not resolve PlayerCorpse.prefab (check DefaultNetworkPrefabs.asset).");
            return null;
        }

        Vector3 spawnPos = new Vector3(worldPos.x, worldPos.y, 0f);
        GameObject instance = Instantiate(prefab, spawnPos, Quaternion.Euler(0f, 0f, zRot));
        var netObj = instance.GetComponent<NetworkObject>();
        var corpse = instance.GetComponent<PlayerCorpse>();
        if (netObj == null || corpse == null)
        {
            Destroy(instance);
            return null;
        }

        corpse.victimClientId.Value = victimId;
        corpse.syncedPosition.Value = new Vector2(spawnPos.x, spawnPos.y);
        corpse.syncedRotation.Value = zRot;
        corpse.draggerClientId.Value = ulong.MaxValue;
        netObj.Spawn(true);
        return corpse;
    }

    public static void DespawnForVictimServer(ulong victimId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        for (int i = AllCorpses.Count - 1; i >= 0; i--)
        {
            var corpse = AllCorpses[i];
            if (corpse != null && corpse.VictimClientId == victimId)
            {
                if (corpse.NetworkObject != null && corpse.NetworkObject.IsSpawned)
                {
                    corpse.NetworkObject.Despawn(true);
                }
                else
                {
                    Destroy(corpse.gameObject);
                }
            }
        }
    }

    public static void DespawnAllServer()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        for (int i = AllCorpses.Count - 1; i >= 0; i--)
        {
            var corpse = AllCorpses[i];
            if (corpse == null) continue;
            if (corpse.NetworkObject != null && corpse.NetworkObject.IsSpawned)
            {
                corpse.NetworkObject.Despawn(true);
            }
            else
            {
                Destroy(corpse.gameObject);
            }
        }
        AllCorpses.Clear();
    }

    private void ServerResetPosition(Vector3 worldPos, float zRot)
    {
        Vector2 p2 = new Vector2(worldPos.x, worldPos.y);
        syncedPosition.Value = p2;
        syncedRotation.Value = zRot;
        draggerClientId.Value = ulong.MaxValue;
        ApplySyncedTransform(p2, zRot);
        if (ragdollController != null)
        {
            ragdollController.SetDragging(false);
        }
        StartChokeAnimation();
    }

    private static GameObject ResolveCorpsePrefab(GameObject explicitPrefab)
    {
        if (explicitPrefab != null) return explicitPrefab;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
        {
            foreach (var np in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
            {
                if (np != null && np.Prefab != null && np.Prefab.GetComponent<PlayerCorpse>() != null)
                {
                    return np.Prefab;
                }
            }
        }

        return Resources.Load<GameObject>("Prefabs/PlayerCorpse");
    }

    // ── IInteractable (Living Players Only) ─────────────────────────────────

    public void Interact()
    {
        // Corpses are dragged via LMB hold, not picked into inventory
    }

    public string GetInteractText()
    {
        if (PlayerController.LocalInstance == null || !PlayerController.LocalInstance.IsAlive)
        {
            return string.Empty;
        }
        return "Hold [LMB] Drag Body";
    }

    public bool CanDrag()
    {
        if (PlayerController.LocalInstance == null || !PlayerController.LocalInstance.IsAlive)
        {
            return false;
        }

        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            return true;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        return draggerClientId.Value == ulong.MaxValue || draggerClientId.Value == localId;
    }

    public void OnDragBegin(ulong clientId)
    {
        if (!CanDrag()) return;

        isLocallyDragging = true;
        localDragTarget = transform.position;
        lastSentDragTarget = localDragTarget;
        nextDragRpcTime = 0f;

        if (ragdollController != null)
        {
            if (!ragdollController.IsRagdollActive)
            {
                if (animCoroutine != null)
                {
                    StopCoroutine(animCoroutine);
                    animCoroutine = null;
                }
                TransitionChokeToRagdoll();
            }
            ragdollController.SetDragging(true);
        }

        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        if (IsServer)
        {
            TryBeginDragServer(NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            DragBeginServerRpc();
        }
    }

    public void OnDragUpdate(Vector2 targetPos)
    {
        if (!isLocallyDragging || !CanDrag()) return;

        Vector2 currentPos = transform.position;
        Vector2 clamped = ClampDragTargetAgainstWalls(currentPos, targetPos, wallLayerMask);
        Vector2 delta = clamped - currentPos;
        localDragTarget = clamped;
        transform.position = new Vector3(clamped.x, clamped.y, 0f);

        if (delta.sqrMagnitude > 0.000004f)
        {
            float targetAngle = ComputeDragTargetAngle(delta, transform.eulerAngles.z);
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(transform.eulerAngles.z, targetAngle, 0.35f));
        }

        if (ragdollController != null)
        {
            ragdollController.OnDragMotion(delta, Time.deltaTime);
        }

        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        if (IsServer)
        {
            TryUpdateDragServer(NetworkManager.Singleton.LocalClientId, clamped);
            return;
        }

        if (Time.unscaledTime >= nextDragRpcTime && (clamped - lastSentDragTarget).sqrMagnitude >= 0.0001f)
        {
            nextDragRpcTime = Time.unscaledTime + (1f / 30f);
            lastSentDragTarget = clamped;
            DragUpdateServerRpc(clamped);
        }
    }

    public void OnDragEnd(ulong clientId)
    {
        if (!isLocallyDragging) return;
        isLocallyDragging = false;

        if (ragdollController != null)
        {
            ragdollController.SetDragging(false);
        }

        if (!IsSpawned || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

        if (IsServer)
        {
            TryEndDragServer(NetworkManager.Singleton.LocalClientId, localDragTarget);
        }
        else
        {
            DragEndServerRpc(localDragTarget);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DragBeginServerRpc(RpcParams rpcParams = default)
    {
        TryBeginDragServer(rpcParams.Receive.SenderClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DragUpdateServerRpc(Vector2 targetPos, RpcParams rpcParams = default)
    {
        TryUpdateDragServer(rpcParams.Receive.SenderClientId, targetPos);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DragEndServerRpc(Vector2 finalTargetPos, RpcParams rpcParams = default)
    {
        TryEndDragServer(rpcParams.Receive.SenderClientId, finalTargetPos);
    }

    private void TryBeginDragServer(ulong senderId)
    {
        if (!IsPlayerAlive(senderId)) return;
        if (draggerClientId.Value != ulong.MaxValue && draggerClientId.Value != senderId) return;
        draggerClientId.Value = senderId;
        if (ragdollController != null)
        {
            ragdollController.SetDragging(true);
        }
    }

    private void TryUpdateDragServer(ulong senderId, Vector2 targetPos)
    {
        if (draggerClientId.Value != senderId || !IsPlayerAlive(senderId)) return;

        Vector2 current = syncedPosition.Value;
        Vector2 delta = targetPos - current;
        if (delta.magnitude > DragMaxDistancePerStep)
        {
            targetPos = current + delta.normalized * DragMaxDistancePerStep;
        }

        Vector2 safe = ClampDragTargetAgainstWalls(current, targetPos, wallLayerMask);
        Vector2 safeDelta = safe - current;
        syncedPosition.Value = safe;
        transform.position = new Vector3(safe.x, safe.y, 0f);

        if (safeDelta.sqrMagnitude > 0.000004f)
        {
            float targetAngle = ComputeDragTargetAngle(safeDelta, syncedRotation.Value);
            float blendedAngle = Mathf.LerpAngle(syncedRotation.Value, targetAngle, 0.35f);
            syncedRotation.Value = blendedAngle;
            transform.rotation = Quaternion.Euler(0f, 0f, blendedAngle);
        }

        if (ragdollController != null)
        {
            ragdollController.OnDragMotion(safeDelta, Time.deltaTime);
        }
    }

    private void TryEndDragServer(ulong senderId, Vector2 finalTargetPos)
    {
        if (draggerClientId.Value != senderId) return;
        if (IsPlayerAlive(senderId))
        {
            Vector2 safe = ClampDragTargetAgainstWalls(syncedPosition.Value, finalTargetPos, wallLayerMask);
            syncedPosition.Value = safe;
            transform.position = new Vector3(safe.x, safe.y, 0f);
        }
        draggerClientId.Value = ulong.MaxValue;
        if (ragdollController != null)
        {
            ragdollController.SetDragging(false);
        }
    }

    private static float ComputeDragTargetAngle(Vector2 delta, float currentAngle)
    {
        if (delta.sqrMagnitude <= 0.000004f) return currentAngle;
        return Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f;
    }

    public static Vector2 ClampDragTargetAgainstWalls(Vector2 currentPos, Vector2 targetPos, int wallMask)
    {
        Vector2 delta = targetPos - currentPos;
        float dist = delta.magnitude;
        if (dist <= 0.0005f || wallMask == 0) return targetPos;

        RaycastHit2D hit = Physics2D.CircleCast(currentPos, DragWallCheckRadius, delta.normalized, dist, wallMask);
        if (hit.collider != null)
        {
            float safeDist = Mathf.Max(0f, hit.distance - 0.006f);
            return currentPos + delta.normalized * safeDist;
        }
        return targetPos;
    }

    private static bool IsPlayerAlive(ulong clientId)
    {
        if (PlayerController.TryGetByClientId(clientId, out var pc))
        {
            return pc.IsAlive;
        }
        return false;
    }
}
