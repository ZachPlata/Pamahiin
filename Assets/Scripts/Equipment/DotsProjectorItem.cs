using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class DotsProjectorItem : EquipmentItem
{
    private const string DotsSortingLayerName = "DOTSLayer";
    private const string SilhouetteChildName = "DotsSilhouetteVisual";
    private const string SilhouetteSheetResourcePath = "Sprites/CHARACTERS/ghost_dots_walk_sheet";
    private const string SilhouetteControllerResourcePath = "Animations/DotsSilhouetteAnim";
    private const float WallClearancePadding = 0.05f;
    private const float MinimumWalkSegmentLength = 0.08f;
    private const float FallbackFps = 10f;

    [Header("DOTS Settings")]
    public float projectionRadius = 0.75f;
    public float coneAngle = 60f;
    [SerializeField] private Light2D dotsLight;

    [Header("DOTS Silhouette Visual")]
    [SerializeField] private SpriteRenderer silhouetteRenderer;
    [SerializeField] private Animator silhouetteAnimator;
    [SerializeField] private float silhouetteWorldScale = 0.28f;
    [SerializeField] private float silhouetteWalkDuration = 1.8f;
    [SerializeField] [Range(0.1f, 1f)] private float silhouettePeakAlpha = 0.84f;

    private NetworkVariable<bool> isPoweredOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override bool IsPoweredOn => isPoweredOn.Value;

    public Light2D DotsLight => dotsLight;
    public SpriteRenderer SilhouetteRenderer => silhouetteRenderer;
    public Animator SilhouetteAnimator => silhouetteAnimator;
    public float SilhouetteWorldScale => silhouetteWorldScale;
    public float SilhouetteWalkDuration => silhouetteWalkDuration;
    public float SilhouettePeakAlpha => silhouettePeakAlpha;
    public bool IsProjectingSilhouette => isProjectingSilhouette;

    private Sprite[] silhouetteFrames;
    private bool isProjectingSilhouette;
    private Vector2 walkStartWorldPos;
    private Vector2 walkEndWorldPos;
    private float walkElapsed;
    private float walkDuration = 1.8f;
    private float walkWorldAngleDeg;
    private float shimmerPhaseOffset;

    private static Material cachedUnlitSpriteMaterial;
    private static readonly RaycastHit2D[] WallHitBuffer = new RaycastHit2D[8];

    protected override void Awake()
    {
        base.Awake();
        itemName = "D.O.T.S. Projector";
        if (dotsLight != null)
        {
            dotsLight.enabled = false; // Guarantee disabled at start
        }

        EnsureSilhouetteVisualInitialized();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isPoweredOn.OnValueChanged += OnPowerStateChanged;
        UpdateVisuals();
    }

    public override void OnNetworkDespawn()
    {
        isPoweredOn.OnValueChanged -= OnPowerStateChanged;
        StopSilhouetteLocal();
        base.OnNetworkDespawn();
    }

    protected override void OnDisable()
    {
        StopSilhouetteLocal();
        base.OnDisable();
    }

    private void OnPowerStateChanged(bool oldVal, bool newVal)
    {
        if (oldVal != newVal) GameAudioManager.PlayFlashlightClick(transform.position);
        UpdateVisuals();
    }

    // Align the horizontal (+X) sprite and Spot Light 2D beam with the player's forward (+Y) axis.
    protected override Quaternion HeldRotationOffset => Quaternion.Euler(0f, 0f, 90f);

    protected override void Update()
    {
        base.Update();

        if (isPoweredOn.Value && dotsLight != null && dotsLight.enabled)
        {
            // 16-bit stepped laser grid shimmer
            float stepped = Mathf.Floor((0.88f + 0.18f * Mathf.Sin(Time.time * 12f)) * 8f) / 8f;
            dotsLight.intensity = stepped;
        }
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (!isProjectingSilhouette) return;

        if (!CanProjectVisual())
        {
            StopSilhouetteLocal();
            return;
        }

        EnsureSilhouetteVisualInitialized();
        if (silhouetteRenderer == null)
        {
            StopSilhouetteLocal();
            return;
        }

        walkElapsed += Time.deltaTime;
        if (walkElapsed >= walkDuration)
        {
            StopSilhouetteLocal();
            return;
        }

        UpdateActiveSilhouetteFrame();
    }

    public override void UsePrimary()
    {
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
        TogglePowerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TogglePowerRpc(RpcParams rpcParams = default)
    {
        if (!IsPlayerAliveAndPresent(rpcParams.Receive.SenderClientId)) return;
        isPoweredOn.Value = !isPoweredOn.Value;
    }

    protected override void UpdateVisuals()
    {
        base.UpdateVisuals();

        bool canOperate = CanProjectVisual();

        if (dotsLight != null)
        {
            dotsLight.enabled = canOperate;
        }

        if (!canOperate)
        {
            StopSilhouetteLocal();
        }
    }

    public bool CanProjectVisual()
    {
        return isActiveAndEnabled && IsEligibleState() && isPoweredOn.Value;
    }

    private bool IsEligibleState()
    {
        return (isInHand.Value || IsOnGround || IsPlaced) && !IsHeldByDeadPlayer();
    }

    public Vector2 GetBeamOrigin()
    {
        return dotsLight != null ? (Vector2)dotsLight.transform.position : (Vector2)transform.position;
    }

    public Vector2 GetBeamForward()
    {
        if (dotsLight != null)
        {
            Vector2 lightUp = dotsLight.transform.up;
            if (lightUp.sqrMagnitude > 0.0001f)
            {
                return lightUp.normalized;
            }
        }

        Vector2 rootRight = transform.right;
        return rootRight.sqrMagnitude > 0.0001f ? rootRight.normalized : Vector2.right;
    }

    public float GetEffectiveBeamRadius()
    {
        if (dotsLight != null && dotsLight.pointLightOuterRadius > 0.05f)
        {
            return Mathf.Min(projectionRadius, dotsLight.pointLightOuterRadius);
        }

        return projectionRadius;
    }

    public float GetEffectiveConeHalfAngleDeg()
    {
        float effectiveAngle = coneAngle;
        if (dotsLight != null && dotsLight.pointLightOuterAngle > 1f)
        {
            effectiveAngle = Mathf.Min(effectiveAngle, dotsLight.pointLightOuterAngle);
        }

        return Mathf.Clamp(effectiveAngle * 0.5f, 5f, 89f);
    }

    public bool IsInOrAimingIntoRoom(Collider2D roomMarker)
    {
        Vector2 origin = GetBeamOrigin();
        if (roomMarker == null)
        {
            return GhostRoomMarker.IsInsideGhostRoom(origin) || GhostRoomMarker.IsInsideGhostRoom(transform.position);
        }

        if (roomMarker.OverlapPoint(origin) || roomMarker.OverlapPoint(transform.position))
        {
            return true;
        }

        Vector2 probe = origin + GetBeamForward() * (GetEffectiveBeamRadius() * 0.55f);
        if (!roomMarker.OverlapPoint(probe))
        {
            return false;
        }

        int wallMask = LayerMask.GetMask("Walls");
        return wallMask == 0 || !HasWallBetween(origin, probe, wallMask);
    }

    public bool IsEligibleForProjection(bool requireRoomMatch, Collider2D roomMarker, Vector2? ghostWorldPos = null, float maxRangeFromGhost = 0f)
    {
        if (!CanProjectVisual()) return false;
        if (requireRoomMatch && !IsInOrAimingIntoRoom(roomMarker)) return false;

        if (ghostWorldPos.HasValue && maxRangeFromGhost > 0f)
        {
            if (Vector2.Distance(ghostWorldPos.Value, GetBeamOrigin()) > maxRangeFromGhost)
            {
                return false;
            }
        }

        return true;
    }

    public bool TriggerDotsProjectionOnServer(Collider2D roomMarker, bool ignoreRoomBounds, out Vector2 projectionCenter, float durationOverride = -1f)
    {
        projectionCenter = GetBeamOrigin();

        if (!CanProjectVisual()) return false;
        if (!ignoreRoomBounds && !IsInOrAimingIntoRoom(roomMarker)) return false;

        Vector2 origin = GetBeamOrigin();
        Vector2 forward = GetBeamForward();
        float maxRange = GetEffectiveBeamRadius();
        float halfAngleDeg = GetEffectiveConeHalfAngleDeg();
        int wallMask = LayerMask.GetMask("Walls");

        if (!TrySampleWalkSegment(origin, forward, maxRange, halfAngleDeg, roomMarker, ignoreRoomBounds, wallMask, out Vector2 startPos, out Vector2 endPos))
        {
            return false;
        }

        projectionCenter = (startPos + endPos) * 0.5f;
        float duration = durationOverride > 0f ? durationOverride : Mathf.Max(0.4f, silhouetteWalkDuration);
        float angleDeg = ComputeWalkRotationAngle(endPos - startPos);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            PlayDotsSilhouetteClientRpc(startPos, endPos, duration, angleDeg);
        }
        else
        {
            StartSilhouetteLocal(startPos, endPos, duration, angleDeg);
        }

        return true;
    }

    public void CancelDotsProjectionOnServer()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            CancelDotsSilhouetteClientRpc();
        }
        else
        {
            StopSilhouetteLocal();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayDotsSilhouetteClientRpc(Vector2 startPos, Vector2 endPos, float duration, float angleDeg)
    {
        if (!CanProjectVisual()) return;
        StartSilhouetteLocal(startPos, endPos, duration, angleDeg);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void CancelDotsSilhouetteClientRpc()
    {
        StopSilhouetteLocal();
    }

    private void StartSilhouetteLocal(Vector2 startPos, Vector2 endPos, float duration, float angleDeg)
    {
        EnsureSilhouetteVisualInitialized();
        if (silhouetteRenderer == null) return;

        walkStartWorldPos = startPos;
        walkEndWorldPos = endPos;
        walkDuration = Mathf.Max(0.25f, duration);
        walkElapsed = 0f;
        walkWorldAngleDeg = angleDeg;
        shimmerPhaseOffset = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        isProjectingSilhouette = true;

        silhouetteRenderer.gameObject.SetActive(true);

        if (silhouetteAnimator != null && silhouetteAnimator.runtimeAnimatorController != null)
        {
            silhouetteAnimator.Rebind();
            silhouetteAnimator.Update(0f);
            silhouetteAnimator.Play(0, 0, 0f);
        }

        UpdateActiveSilhouetteFrame();
    }

    public void StopSilhouetteLocal()
    {
        isProjectingSilhouette = false;
        walkElapsed = 0f;

        if (silhouetteRenderer != null && silhouetteRenderer.gameObject.activeSelf)
        {
            silhouetteRenderer.gameObject.SetActive(false);
        }
    }

    private void UpdateActiveSilhouetteFrame()
    {
        if (silhouetteRenderer == null) return;

        float normalizedTime = Mathf.Clamp01(walkElapsed / Mathf.Max(0.01f, walkDuration));
        Vector2 worldPos = Vector2.Lerp(walkStartWorldPos, walkEndWorldPos, normalizedTime);

        Transform visTransform = silhouetteRenderer.transform;
        visTransform.position = new Vector3(worldPos.x, worldPos.y, transform.position.z);
        visTransform.rotation = Quaternion.Euler(0f, 0f, walkWorldAngleDeg);
        visTransform.localScale = ComputeCompensatedLocalScale(transform.lossyScale, silhouetteWorldScale);

        if ((silhouetteAnimator == null || silhouetteAnimator.runtimeAnimatorController == null || !silhouetteAnimator.enabled)
            && silhouetteFrames != null && silhouetteFrames.Length > 0)
        {
            int frameIdx = Mathf.FloorToInt(walkElapsed * FallbackFps) % silhouetteFrames.Length;
            silhouetteRenderer.sprite = silhouetteFrames[frameIdx];
        }

        float fadeEnvelope = ComputeTimelineFadeEnvelope(normalizedTime);
        float coneAlpha = ComputeConeContainmentAlpha(
            worldPos,
            GetBeamOrigin(),
            GetBeamForward(),
            GetEffectiveBeamRadius(),
            GetEffectiveConeHalfAngleDeg());

        // 16-bit laser scan shimmer synchronized with projector beam aesthetic
        float shimmer = Mathf.Floor((0.88f + 0.12f * Mathf.Sin((Time.time + shimmerPhaseOffset) * 18f)) * 12f) / 12f;
        float alpha = Mathf.Clamp01(silhouettePeakAlpha * fadeEnvelope * coneAlpha * shimmer);

        Color c = Color.white;
        c.a = alpha;
        silhouetteRenderer.color = c;
    }

    public void EnsureSilhouetteVisualInitialized()
    {
        if (silhouetteRenderer == null)
        {
            Transform existingChild = transform.Find(SilhouetteChildName);
            if (existingChild != null)
            {
                silhouetteRenderer = existingChild.GetComponent<SpriteRenderer>();
                if (silhouetteAnimator == null)
                {
                    silhouetteAnimator = existingChild.GetComponent<Animator>();
                }
            }
        }

        if (silhouetteRenderer == null)
        {
            GameObject visualGo = new GameObject(SilhouetteChildName);
            visualGo.transform.SetParent(transform, false);
            visualGo.SetActive(false);
            silhouetteRenderer = visualGo.AddComponent<SpriteRenderer>();
            silhouetteAnimator = visualGo.AddComponent<Animator>();
        }
        else if (silhouetteAnimator == null)
        {
            silhouetteAnimator = silhouetteRenderer.GetComponent<Animator>();
            if (silhouetteAnimator == null)
            {
                silhouetteAnimator = silhouetteRenderer.gameObject.AddComponent<Animator>();
            }
        }

        silhouetteRenderer.sortingLayerName = DotsSortingLayerName;
        silhouetteRenderer.sortingOrder = 5;
        ApplyUnlitMaterialIfNeeded(silhouetteRenderer);

        LoadSilhouetteFrames();
        if (silhouetteRenderer.sprite == null && silhouetteFrames != null && silhouetteFrames.Length > 0)
        {
            silhouetteRenderer.sprite = silhouetteFrames[0];
        }

        if (silhouetteAnimator != null)
        {
            if (silhouetteAnimator.runtimeAnimatorController == null)
            {
                silhouetteAnimator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(SilhouetteControllerResourcePath);
            }

            silhouetteAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        if (!isProjectingSilhouette && silhouetteRenderer.gameObject.activeSelf)
        {
            silhouetteRenderer.gameObject.SetActive(false);
        }
    }

    private void LoadSilhouetteFrames()
    {
        if (silhouetteFrames != null && silhouetteFrames.Length > 0) return;

        Sprite[] loaded = Resources.LoadAll<Sprite>(SilhouetteSheetResourcePath);
        if (loaded == null || loaded.Length == 0) return;

        Array.Sort(loaded, (a, b) =>
        {
            int idxA = ParseTrailingIndex(a != null ? a.name : string.Empty);
            int idxB = ParseTrailingIndex(b != null ? b.name : string.Empty);
            if (idxA != idxB) return idxA.CompareTo(idxB);
            return string.CompareOrdinal(a != null ? a.name : string.Empty, b != null ? b.name : string.Empty);
        });

        silhouetteFrames = loaded;
    }

    private static int ParseTrailingIndex(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName)) return int.MaxValue;
        int underscore = spriteName.LastIndexOf('_');
        if (underscore >= 0 && int.TryParse(spriteName.Substring(underscore + 1), out int idx))
        {
            return idx;
        }
        return int.MaxValue;
    }

    private static void ApplyUnlitMaterialIfNeeded(SpriteRenderer sr)
    {
        if (sr == null) return;
        Material current = sr.sharedMaterial;
        if (current != null && current.shader != null && !current.shader.name.Contains("Lit-"))
        {
            return;
        }

        if (cachedUnlitSpriteMaterial == null)
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (unlitShader == null)
            {
                unlitShader = Shader.Find("Sprites/Default");
            }

            if (unlitShader != null)
            {
                cachedUnlitSpriteMaterial = new Material(unlitShader)
                {
                    name = "DotsSilhouetteUnlitRuntime",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        if (cachedUnlitSpriteMaterial != null)
        {
            sr.sharedMaterial = cachedUnlitSpriteMaterial;
        }
    }

    public static Vector3 ComputeCompensatedLocalScale(Vector3 parentLossyScale, float targetWorldScale)
    {
        float sx = Mathf.Abs(parentLossyScale.x) > 0.0001f ? Mathf.Abs(parentLossyScale.x) : 1f;
        float sy = Mathf.Abs(parentLossyScale.y) > 0.0001f ? Mathf.Abs(parentLossyScale.y) : 1f;
        float sz = Mathf.Abs(parentLossyScale.z) > 0.0001f ? Mathf.Abs(parentLossyScale.z) : 1f;
        return new Vector3(targetWorldScale / sx, targetWorldScale / sy, targetWorldScale / sz);
    }

    public static float ComputeWalkRotationAngle(Vector2 walkDirection)
    {
        if (walkDirection.sqrMagnitude <= 0.0001f) return 0f;
        return Mathf.Atan2(walkDirection.y, walkDirection.x) * Mathf.Rad2Deg - 90f;
    }

    public static float ComputeTimelineFadeEnvelope(float normalizedTime)
    {
        float t = Mathf.Clamp01(normalizedTime);
        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.22f, t));
        float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, t));
        return fadeIn * fadeOut;
    }

    public static float ComputeConeContainmentAlpha(
        Vector2 worldPos,
        Vector2 beamOrigin,
        Vector2 beamForward,
        float maxRange,
        float coneHalfAngleDeg)
    {
        Vector2 fwd = beamForward.sqrMagnitude > 0.0001f ? beamForward.normalized : Vector2.right;
        Vector2 offset = worldPos - beamOrigin;
        float dist = offset.magnitude;
        if (dist <= 0.001f) return 1f;
        if (dist >= maxRange) return 0f;

        Vector2 dir = offset / dist;
        float dot = Vector2.Dot(fwd, dir);
        if (dot <= 0f) return 0f;

        float angleDeg = Vector2.Angle(fwd, dir);
        if (angleDeg >= coneHalfAngleDeg) return 0f;

        float radialAlpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(maxRange * 0.80f, maxRange, dist));
        float angularAlpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coneHalfAngleDeg * 0.72f, coneHalfAngleDeg, angleDeg));
        return radialAlpha * angularAlpha;
    }

    private static bool TrySampleWalkSegment(
        Vector2 beamOrigin,
        Vector2 beamForward,
        float maxRange,
        float coneHalfAngleDeg,
        Collider2D roomMarker,
        bool ignoreRoomBounds,
        int wallLayerMask,
        out Vector2 startPos,
        out Vector2 endPos)
    {
        int baseSign = UnityEngine.Random.value < 0.5f ? -1 : 1;
        float baseDepth = UnityEngine.Random.Range(0.35f, 0.75f);
        float baseTilt = UnityEngine.Random.Range(-14f, 14f);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            float depthFraction = attempt == 0 ? baseDepth : Mathf.Clamp01(0.25f + 0.15f * attempt);
            float tiltDeg = attempt == 0 ? baseTilt : 0f;
            int sign = attempt % 2 == 0 ? baseSign : -baseSign;

            if (TryComputeConstrainedWalkSegment(
                beamOrigin, beamForward, maxRange, coneHalfAngleDeg,
                roomMarker, ignoreRoomBounds, wallLayerMask,
                depthFraction, tiltDeg, sign,
                out startPos, out endPos))
            {
                return true;
            }
        }

        startPos = beamOrigin;
        endPos = beamOrigin;
        return false;
    }

    public static bool TryComputeConstrainedWalkSegment(
        Vector2 beamOrigin,
        Vector2 beamForward,
        float maxRange,
        float coneHalfAngleDeg,
        Collider2D roomMarker,
        bool ignoreRoomBounds,
        int wallLayerMask,
        float depthFraction,
        float tiltDegrees,
        int walkSign,
        out Vector2 startPos,
        out Vector2 endPos)
    {
        startPos = beamOrigin;
        endPos = beamOrigin;

        Vector2 fwd = beamForward.sqrMagnitude > 0.0001f ? beamForward.normalized : Vector2.right;
        float clampedRange = Mathf.Max(0.05f, maxRange);

        float forwardWallDist = RaycastWallDistance(beamOrigin, fwd, clampedRange, wallLayerMask);
        float clearRange = Mathf.Min(clampedRange, forwardWallDist - WallClearancePadding);
        if (clearRange < MinimumWalkSegmentLength)
        {
            return false;
        }

        float depth = Mathf.Clamp(clearRange * Mathf.Lerp(0.42f, 0.72f, Mathf.Clamp01(depthFraction)), 0.06f, clearRange);
        Vector2 centerPos = beamOrigin + fwd * depth;

        if (!ignoreRoomBounds && roomMarker != null && !roomMarker.OverlapPoint(centerPos))
        {
            if (!TryProjectCenterIntoRoom(beamOrigin, fwd, clearRange, roomMarker, out centerPos))
            {
                return false;
            }

            depth = Mathf.Max(0.06f, Vector2.Distance(beamOrigin, centerPos));
        }

        if (wallLayerMask != 0 && HasWallBetween(beamOrigin, centerPos, wallLayerMask))
        {
            float segmentDist = RaycastWallDistance(beamOrigin, fwd, depth, wallLayerMask);
            float safeDist = segmentDist - WallClearancePadding;
            if (safeDist < MinimumWalkSegmentLength)
            {
                return false;
            }

            depth = safeDist;
            centerPos = beamOrigin + fwd * depth;
            if (!ignoreRoomBounds && roomMarker != null && !roomMarker.OverlapPoint(centerPos))
            {
                return false;
            }
        }

        Vector2 perp = new Vector2(-fwd.y, fwd.x) * (walkSign >= 0 ? 1f : -1f);
        Vector2 walkDir = RotateVector2(perp, tiltDegrees).normalized;

        float coneHalfWidthAtDepth = depth * Mathf.Tan(Mathf.Clamp(coneHalfAngleDeg, 5f, 89f) * Mathf.Deg2Rad);
        float targetHalfLength = Mathf.Clamp(coneHalfWidthAtDepth * 0.85f, 0.06f, 0.55f);

        startPos = ClipHalfSegment(centerPos, -walkDir, targetHalfLength, beamOrigin, roomMarker, ignoreRoomBounds, wallLayerMask);
        endPos = ClipHalfSegment(centerPos, walkDir, targetHalfLength, beamOrigin, roomMarker, ignoreRoomBounds, wallLayerMask);

        return Vector2.Distance(startPos, endPos) >= MinimumWalkSegmentLength;
    }

    private static Vector2 ClipHalfSegment(
        Vector2 centerPos,
        Vector2 stepDir,
        float maxHalfLen,
        Vector2 beamOrigin,
        Collider2D roomMarker,
        bool ignoreRoomBounds,
        int wallLayerMask)
    {
        float wallDist = RaycastWallDistance(centerPos, stepDir, maxHalfLen + WallClearancePadding, wallLayerMask);
        float safeLen = Mathf.Min(maxHalfLen, Mathf.Max(0f, wallDist - WallClearancePadding));
        Vector2 endpoint = centerPos + stepDir * safeLen;

        if (!ignoreRoomBounds && roomMarker != null && !roomMarker.OverlapPoint(endpoint))
        {
            endpoint = BinaryClampInsideRoom(centerPos, endpoint, roomMarker);
        }

        if (wallLayerMask != 0 && safeLen > 0.001f && HasWallBetween(beamOrigin, endpoint, wallLayerMask))
        {
            endpoint = BinaryClampLineOfSight(centerPos, endpoint, beamOrigin, wallLayerMask);
        }

        return endpoint;
    }

    private static bool TryProjectCenterIntoRoom(
        Vector2 beamOrigin,
        Vector2 fwd,
        float clearRange,
        Collider2D roomMarker,
        out Vector2 centerPos)
    {
        const int samples = 10;
        for (int i = 1; i <= samples; i++)
        {
            float frac = i / (float)samples;
            Vector2 probe = beamOrigin + fwd * (clearRange * frac);
            if (roomMarker.OverlapPoint(probe))
            {
                centerPos = probe;
                return true;
            }
        }

        centerPos = beamOrigin;
        return false;
    }

    private static Vector2 BinaryClampInsideRoom(Vector2 insidePoint, Vector2 outsidePoint, Collider2D roomMarker)
    {
        float lo = 0f;
        float hi = 1f;
        for (int i = 0; i < 8; i++)
        {
            float mid = (lo + hi) * 0.5f;
            Vector2 probe = Vector2.Lerp(insidePoint, outsidePoint, mid);
            if (roomMarker.OverlapPoint(probe)) lo = mid;
            else hi = mid;
        }

        return Vector2.Lerp(insidePoint, outsidePoint, lo * 0.92f);
    }

    private static Vector2 BinaryClampLineOfSight(Vector2 safePoint, Vector2 occludedPoint, Vector2 rayOrigin, int wallLayerMask)
    {
        float lo = 0f;
        float hi = 1f;
        for (int i = 0; i < 8; i++)
        {
            float mid = (lo + hi) * 0.5f;
            Vector2 probe = Vector2.Lerp(safePoint, occludedPoint, mid);
            if (!HasWallBetween(rayOrigin, probe, wallLayerMask)) lo = mid;
            else hi = mid;
        }

        return Vector2.Lerp(safePoint, occludedPoint, lo * 0.92f);
    }

    private static float RaycastWallDistance(Vector2 origin, Vector2 direction, float maxDistance, int wallLayerMask)
    {
        if (wallLayerMask == 0 || maxDistance <= 0f) return maxDistance;

        ContactFilter2D filter = ContactFilter2D.noFilter;
        filter.useTriggers = false;
        filter.SetLayerMask(wallLayerMask);

        int count = Physics2D.Raycast(origin, direction, filter, WallHitBuffer, maxDistance);
        if (count <= 0) return maxDistance;

        float minDist = maxDistance;
        for (int i = 0; i < count; i++)
        {
            if (WallHitBuffer[i].distance < minDist)
            {
                minDist = WallHitBuffer[i].distance;
            }
        }

        return minDist;
    }

    private static bool HasWallBetween(Vector2 a, Vector2 b, int wallLayerMask)
    {
        Vector2 delta = b - a;
        float len = delta.magnitude;
        if (len <= 0.001f || wallLayerMask == 0) return false;
        return RaycastWallDistance(a, delta / len, len, wallLayerMask) < len - 0.0005f;
    }

    private static Vector2 RotateVector2(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(rad);
        float cos = Mathf.Cos(rad);
        return new Vector2(cos * v.x - sin * v.y, sin * v.x + cos * v.y);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, projectionRadius);
    }
}
