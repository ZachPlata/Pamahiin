using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sprite-sheet animated ghost-disruptable prop (plates, cups, bottles, frames, paint cans, chairs).
/// Avoids physics forces; uses server-authoritative levitation, short raycast wall collision, and
/// shatter/knockdown sprite frames + spatial SFX.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class GhostPropInteractable : NetworkBehaviour, IInteractable
{
    public static readonly List<GhostPropInteractable> AllProps = new List<GhostPropInteractable>();

    private static readonly Dictionary<string, Sprite[]> sheetFrameCache = new Dictionary<string, Sprite[]>();

    [Header("Prop Identity & Sheet")]
    [SerializeField] private string propName = "Ceramic Plate";
    [SerializeField] private string spriteSheetResource = "Props/prop_plate_sheet";
    [SerializeField] private bool isBreakable = true;
    [SerializeField] private string impactSfx = "prop_shatter_glass";

    [Header("Disruption Settings")]
    [SerializeField] private float throwDistance = 0.42f;
    [SerializeField] private float levitateDuration = 0.65f;
    [SerializeField] private float throwGlideDuration = 0.22f;
    [SerializeField] private float disruptCooldown = 28f;

    // Frame convention (6 horizontal 24x24 frames):
    // 0 = Intact, 1 = Levitate A, 2 = Levitate B, 3 = Knocked/Tilted, 4 = Impact, 5 = Shattered
    private NetworkVariable<int> netFrameIndex = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector2> netPropPos = new NetworkVariable<Vector2>(
        Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> netDisrupted = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> netBusy = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private SpriteRenderer spriteRenderer;
    private Collider2D propCollider;
    private Vector3 initialPosition;
    private Sprite[] frames;
    private Coroutine activeRoutine;
    private float nextDisruptAllowedTime;

    public bool IsBusy => netBusy.Value;
    public bool IsDisrupted => netDisrupted.Value;
    public bool CanGhostDisrupt => !netBusy.Value && !(isBreakable && netDisrupted.Value) && Time.time >= nextDisruptAllowedTime;

    public void Configure(string displayName, string sheetResource, bool breakable, string shatterOrKnockSfx)
    {
        propName = displayName;
        spriteSheetResource = sheetResource;
        isBreakable = breakable;
        impactSfx = shatterOrKnockSfx;
        EnsureSheetLoaded();
        ApplyFrame(0);
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        propCollider = GetComponent<Collider2D>();
        if (propCollider != null) propCollider.isTrigger = true;

        int interactLayer = LayerMask.NameToLayer("Interactable");
        if (interactLayer >= 0) gameObject.layer = interactLayer;

        initialPosition = transform.position;
        EnsureSheetLoaded();
        ApplyFrame(0);
    }

    private void OnEnable()
    {
        if (!AllProps.Contains(this)) AllProps.Add(this);
    }

    private void OnDisable()
    {
        AllProps.Remove(this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        initialPosition = transform.position;

        if (IsServer)
        {
            netPropPos.Value = transform.position;
            netFrameIndex.Value = 0;
            netDisrupted.Value = false;
            netBusy.Value = false;
        }

        netFrameIndex.OnValueChanged += (_, newFrame) => ApplyFrame(newFrame);
        netPropPos.OnValueChanged += (_, newPos) =>
        {
            if (!IsServer) transform.position = new Vector3(newPos.x, newPos.y, transform.position.z);
        };

        ApplyFrame(netFrameIndex.Value);
        if (!IsServer && netPropPos.Value != Vector2.zero)
        {
            transform.position = new Vector3(netPropPos.Value.x, netPropPos.Value.y, transform.position.z);
        }
    }

    private void EnsureSheetLoaded()
    {
        if (frames != null && frames.Length == 6 && frames[0] != null) return;
        if (string.IsNullOrEmpty(spriteSheetResource)) return;

        if (sheetFrameCache.TryGetValue(spriteSheetResource, out Sprite[] cached) && cached != null && cached.Length == 6)
        {
            frames = cached;
            return;
        }

        Texture2D tex = Resources.Load<Texture2D>($"Sprites/BarangayMap/{spriteSheetResource}");
        if (tex == null)
        {
            tex = Resources.Load<Texture2D>($"Sprites/{spriteSheetResource}");
        }
        if (tex == null)
        {
            tex = Resources.Load<Texture2D>($"Sprites/SchoolMap/{spriteSheetResource}");
        }
        if (tex == null) return;

        tex.filterMode = FilterMode.Point;
        const int frameCount = 6;
        int fw = Mathf.Max(1, tex.width / frameCount);
        int fh = tex.height;

        Sprite[] sliced = new Sprite[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            sliced[i] = Sprite.Create(
                tex,
                new Rect(i * fw, 0, fw, fh),
                new Vector2(0.5f, 0.5f),
                64f,
                0,
                SpriteMeshType.FullRect);
            sliced[i].name = $"{spriteSheetResource}_f{i}";
        }

        sheetFrameCache[spriteSheetResource] = sliced;
        frames = sliced;
    }

    private void ApplyFrame(int index)
    {
        EnsureSheetLoaded();
        if (spriteRenderer == null || frames == null || frames.Length == 0) return;
        int clamped = Mathf.Clamp(index, 0, frames.Length - 1);
        if (frames[clamped] != null)
        {
            spriteRenderer.sprite = frames[clamped];
        }
    }

    public bool TriggerGhostDisruption(bool forceThrow = false)
    {
        if (!IsServer || !CanGhostDisrupt) return false;

        if (activeRoutine != null) StopCoroutine(activeRoutine);
        nextDisruptAllowedTime = Time.time + disruptCooldown;
        bool doThrow = forceThrow || (!netDisrupted.Value && Random.value < 0.48f);
        activeRoutine = StartCoroutine(doThrow ? LevitateAndThrowRoutine() : KnockDownRoutine());
        return true;
    }

    private IEnumerator LevitateAndThrowRoutine()
    {
        netBusy.Value = true;
        Vector2 originPos = transform.position;
        PlaySfxClientRpc("prop_levitate", originPos, 0.55f);

        // Phase 1: Levitate wobble frames 1 & 2
        float elapsed = 0f;
        while (elapsed < levitateDuration)
        {
            elapsed += Time.deltaTime;
            int liftFrame = (Mathf.FloorToInt(elapsed * 8f) % 2 == 0) ? 1 : 2;
            netFrameIndex.Value = liftFrame;

            Vector2 hover = originPos + new Vector2(
                Mathf.Sin(elapsed * 22f) * 0.008f,
                0.015f + Mathf.Sin(elapsed * 14f) * 0.006f);
            transform.position = new Vector3(hover.x, hover.y, transform.position.z);
            netPropPos.Value = hover;
            yield return null;
        }

        // Phase 2: Compute raycast destination against Walls layer
        Vector2 throwDir = Random.insideUnitCircle.normalized;
        if (throwDir.sqrMagnitude < 0.01f) throwDir = Vector2.right;

        int wallMask = LayerMask.GetMask("Walls");
        RaycastHit2D wallHit = wallMask != 0
            ? Physics2D.Raycast(originPos, throwDir, throwDistance, wallMask)
            : default;

        Vector2 targetPos = wallHit.collider != null
            ? wallHit.point - throwDir * 0.035f
            : originPos + throwDir * throwDistance;

        PlaySfxClientRpc("prop_throw_whoosh", originPos, 0.7f);

        float glideTime = 0f;
        while (glideTime < throwGlideDuration)
        {
            glideTime += Time.deltaTime;
            float t = Mathf.Clamp01(glideTime / throwGlideDuration);
            Vector2 p = Vector2.Lerp(originPos, targetPos, t);
            transform.position = new Vector3(p.x, p.y, transform.position.z);
            netPropPos.Value = p;
            yield return null;
        }

        transform.position = new Vector3(targetPos.x, targetPos.y, transform.position.z);
        netPropPos.Value = targetPos;

        // Phase 3: Impact burst frame 4 + shatter/knock SFX -> final resting frame
        netFrameIndex.Value = 4;
        string sfx = isBreakable ? impactSfx : "prop_knock_wood";
        PlaySfxClientRpc(sfx, targetPos, 0.85f);

        if (ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(targetPos, 3, 12f);
        }

        yield return new WaitForSeconds(0.14f);

        netFrameIndex.Value = isBreakable ? 5 : 3;
        netDisrupted.Value = true;
        netBusy.Value = false;
        activeRoutine = null;
    }

    private IEnumerator KnockDownRoutine()
    {
        netBusy.Value = true;
        Vector2 pos = transform.position;
        netFrameIndex.Value = 1;
        yield return new WaitForSeconds(0.14f);

        netFrameIndex.Value = 3;
        netDisrupted.Value = true;
        PlaySfxClientRpc("prop_knock_wood", pos, 0.75f);

        if (ParanormalManager.Instance != null)
        {
            ParanormalManager.Instance.RegisterEvent(pos, 2, 10f);
        }

        netBusy.Value = false;
        activeRoutine = null;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlaySfxClientRpc(string clipName, Vector2 pos, float vol)
    {
        GameAudioManager.PlayAt(clipName, pos, vol, 1.0f, Random.Range(0.92f, 1.08f), 0.4f, 3.0f, 0.06f);
    }

    // --- IInteractable (Players can upright non-breakable knocked props) ---
    public void Interact()
    {
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
        if (!netBusy.Value && netDisrupted.Value && !isBreakable)
        {
            UprightPropServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void UprightPropServerRpc(RpcParams rpcParams = default)
    {
        if (PlayerController.TryGetByClientId(rpcParams.Receive.SenderClientId, out var sender) && !sender.IsAlive)
        {
            return;
        }
        if (netBusy.Value || isBreakable) return;
        netFrameIndex.Value = 0;
        netDisrupted.Value = false;
        transform.position = initialPosition;
        netPropPos.Value = initialPosition;
    }

    public string GetInteractText()
    {
        if (netDisrupted.Value && !isBreakable && !netBusy.Value)
        {
            return $"Upright {propName}";
        }
        return "";
    }

    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) { }
    public void OnDragUpdate(Vector2 targetPosition) { }
    public void OnDragEnd(ulong clientId) { }
}
