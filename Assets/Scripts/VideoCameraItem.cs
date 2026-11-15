using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VideoCameraItem : EquipmentItem
{
    public const string CCTVLayerName = "CCTVOnly";
    public const int FallbackCCTVLayer = 10;

    public static readonly Color NightVisionBackgroundColor = new Color(0.2f, 0.2f, 0.2f);
    public static readonly Color NightVisionLightColor = new Color(0.88f, 0.94f, 0.92f);
    public const float NightVisionLightIntensity = 1.1f;
    public const float NightVisionLightInnerRadius = 0f;
    public const float NightVisionLightOuterRadius = 22f;
    public const float NightVisionLightFalloffIntensity = 0.5f;
    public const bool NightVisionShadowsEnabled = true;
    public const float NightVisionShadowIntensity = 0.6f;
    public const float NightVisionSaturation = -100f;
    public const float NightVisionPostExposure = 0.45f;
    public const float NightVisionContrast = 15f;

    [Header("Video Camera Settings")]
    [SerializeField] private Color nightVisionColor = new Color(0.2f, 0.2f, 0.2f);
    [SerializeField] private float nightVisionRadius = NightVisionLightOuterRadius;
    [SerializeField] private Camera feedCamera;
    [SerializeField] private float cctvOrthographicSize = 0.45f;

    public static int ResolveCCTVLayer()
    {
        int layer = LayerMask.NameToLayer(CCTVLayerName);
        return layer >= 0 ? layer : FallbackCCTVLayer;
    }

    public static VolumeProfile CreateNightVisionVolumeProfile()
    {
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        if (profile.Add<ColorAdjustments>(true) && profile.TryGet(out ColorAdjustments colorAdj))
        {
            colorAdj.saturation.Override(NightVisionSaturation);
            colorAdj.postExposure.Override(NightVisionPostExposure);
            colorAdj.contrast.Override(NightVisionContrast);
        }
        return profile;
    }

    public static void ApplyNightVisionLightSettings(Light2D targetLight, float outerRadius, int[] sortingLayerIds)
    {
        if (targetLight == null) return;
        targetLight.lightType = Light2D.LightType.Point;
        targetLight.color = NightVisionLightColor;
        targetLight.intensity = NightVisionLightIntensity;
        targetLight.falloffIntensity = NightVisionLightFalloffIntensity;
        targetLight.pointLightOuterRadius = outerRadius;
        targetLight.pointLightInnerRadius = NightVisionLightInnerRadius;
        targetLight.pointLightInnerAngle = 360f;
        targetLight.pointLightOuterAngle = 360f;
        targetLight.shadowsEnabled = NightVisionShadowsEnabled;
        targetLight.shadowIntensity = NightVisionShadowIntensity;
        if (sortingLayerIds != null)
        {
            targetLight.targetSortingLayers = sortingLayerIds;
        }
    }

    /// <summary>
    /// Ensures Camera.main renders GhostOrbs if and only if the local player is holding a powered-on VideoCameraItem.
    /// </summary>
    public static void SyncMainCameraGhostOrbsMask(VideoCameraItem excludeItem = null)
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        int ghostOrbLayer = LayerMask.NameToLayer("GhostOrbs");
        if (ghostOrbLayer == -1) return;

        if (HasLocalActiveNightVisionCamera(excludeItem))
        {
            mainCam.cullingMask |= (1 << ghostOrbLayer);
        }
        else
        {
            mainCam.cullingMask &= ~(1 << ghostOrbLayer);
        }
    }

    private static bool HasLocalActiveNightVisionCamera(VideoCameraItem excludeItem = null)
    {
        for (int i = 0; i < AllEquipment.Count; i++)
        {
            if (AllEquipment[i] is VideoCameraItem cam &&
                cam != excludeItem &&
                cam != null &&
                cam.gameObject.activeInHierarchy &&
                cam.IsLocallyHeldForNightVision() &&
                cam.IsPoweredOn)
            {
                return true;
            }
        }
        return false;
    }

    private bool IsLocallyHeldForNightVision()
    {
        return ownerClientId.Value != ulong.MaxValue &&
               NetworkManager.Singleton != null &&
               ownerClientId.Value == NetworkManager.Singleton.LocalClientId &&
               isInHand.Value;
    }
    
    private NetworkVariable<bool> isPoweredOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private static readonly System.Collections.Generic.List<VideoCameraItem> activeCameras = new System.Collections.Generic.List<VideoCameraItem>();
    public static System.Collections.Generic.IReadOnlyList<VideoCameraItem> ActiveCameras => activeCameras;

    public override bool IsPoweredOn => isPoweredOn.Value;
    public Camera CctvCamera => cctvCamera;
    public bool IsAvailableForCCTV => gameObject.activeInHierarchy && (ownerClientId.Value == ulong.MaxValue || isInHand.Value);

    private Camera localCamera;
    private Color originalCameraColor;
    private int originalCullingMask;
    private float originalOrthoSize;
    private bool originalPostProcessing;
    private UniversalAdditionalCameraData cameraData;
    private bool cameraSettingsStored = false;
    private bool appliedLocalCameraOverrides = false;

    private Volume nightVisionVolume;
    private Light2D nightVisionLight;
    
    private Camera cctvCamera;
    private Light2D playerAmbientLight;
    private float originalPlayerLightIntensity = 1f;
    private bool playerLightStored = false;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Video Camera";
        SetupNightVisionEffects();
    }

    private void SetupNightVisionEffects()
    {
        int[] allSortingLayerIds = System.Array.ConvertAll(SortingLayer.layers, layer => layer.id);

        // 1. Setup Volume for Black and White (player hand-held night vision only)
        GameObject volumeObj = new GameObject("NightVisionVolume");
        volumeObj.hideFlags = HideFlags.HideInHierarchy;
        volumeObj.transform.SetParent(null);
        volumeObj.transform.localScale = Vector3.one;
        nightVisionVolume = volumeObj.AddComponent<Volume>();
        nightVisionVolume.isGlobal = true;
        nightVisionVolume.priority = 100;
        nightVisionVolume.sharedProfile = CreateNightVisionVolumeProfile();
        nightVisionVolume.weight = 0f;

        // 2. Setup Point Light2D to see in the dark (player hand-held night vision only)
        // Kept unscaled from the weapon/item transform so it maintains true world-space radius.
        GameObject lightObj = new GameObject("NightVisionLight");
        lightObj.hideFlags = HideFlags.HideInHierarchy;
        lightObj.transform.SetParent(null);
        lightObj.transform.localScale = Vector3.one;
        nightVisionLight = lightObj.AddComponent<Light2D>();
        ApplyNightVisionLightSettings(nightVisionLight, nightVisionRadius, allSortingLayerIds);
        nightVisionLight.enabled = false;
        
        // 3. Bind and configure CCTV Camera for the Truck Dashboard feed (prefer prefab's FeedCamera child)
        cctvCamera = feedCamera != null ? feedCamera : transform.Find("FeedCamera")?.GetComponent<Camera>();
        if (cctvCamera == null)
        {
            cctvCamera = transform.Find("CCTV_POV")?.GetComponent<Camera>();
        }
        if (cctvCamera == null)
        {
            GameObject cctvObj = new GameObject("CCTV_POV");
            cctvObj.transform.SetParent(transform, false);
            cctvCamera = cctvObj.AddComponent<Camera>();
        }

        cctvCamera.gameObject.SetActive(true);
        cctvCamera.orthographic = true;
        cctvCamera.orthographicSize = cctvOrthographicSize;
        cctvCamera.nearClipPlane = 0.1f;
        cctvCamera.farClipPlane = 1000f;
        cctvCamera.backgroundColor = Color.black;
        cctvCamera.clearFlags = CameraClearFlags.SolidColor;
        cctvCamera.depth = -10; // Render before Main Camera and never interfere with screen output
        cctvCamera.enabled = false; // TruckDashboardController manages enabling/disabling

        // Isolate FeedCamera onto CCTVOnly for volumes/IR light and keep GhostOrbs off until CCTV NV turns ON
        int cctvLayer = ResolveCCTVLayer();
        cctvCamera.cullingMask |= (1 << cctvLayer);

        int ghostOrbLayer = LayerMask.NameToLayer("GhostOrbs");
        if (ghostOrbLayer != -1)
        {
            cctvCamera.cullingMask &= ~(1 << ghostOrbLayer);
        }

        // Enable post-processing on the CCTV camera using only the isolated CCTVOnly volume layer
        var cctvCamData = cctvCamera.GetComponent<UniversalAdditionalCameraData>();
        if (cctvCamData == null) cctvCamData = cctvCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        cctvCamData.renderPostProcessing = true;
        cctvCamData.volumeLayerMask = (1 << cctvLayer);
        cctvCamera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);

        SyncCCTVCameraWorldPose();
        SyncMainCameraGhostOrbsMask();
    }

    /// <summary>
    /// Returns the world position where the CCTV IR light should be anchored.
    /// When held, anchors at the holder's body center so shadows cast cleanly without wall clipping.
    /// </summary>
    public Vector3 GetCCTVNightVisionLightPosition()
    {
        if (ownerClientId.Value != ulong.MaxValue)
        {
            Transform holder = ResolveHolderPlayerTransform();
            if (holder != null)
            {
                return new Vector3(holder.position.x, holder.position.y, 0f);
            }
        }

        return new Vector3(transform.position.x, transform.position.y, 0f);
    }

    public void SyncCCTVCameraWorldPose()
    {
        if (cctvCamera == null) return;
        Transform camTr = cctvCamera.transform;
        Vector3 worldPos = transform.position;
        camTr.position = new Vector3(worldPos.x, worldPos.y, -10f);
        camTr.rotation = Quaternion.identity;
    }

    private TruckDashboardController FindDashboard()
    {
        return TruckDashboardController.Instance != null
            ? TruckDashboardController.Instance
            : FindAnyObjectByType<TruckDashboardController>(FindObjectsInactive.Include);
    }

    private void SyncDashboardRegistration()
    {
        if (cctvCamera == null) return;
        var dashboard = FindDashboard();
        if (dashboard == null) return;

        if (IsAvailableForCCTV)
        {
            SyncCCTVCameraWorldPose();
            dashboard.RegisterDynamicCamera(cctvCamera);
        }
        else
        {
            dashboard.UnregisterDynamicCamera(cctvCamera);
        }
    }

    private void UnregisterFromDashboard()
    {
        if (cctvCamera == null) return;
        var dashboard = FindDashboard();
        if (dashboard != null)
        {
            dashboard.UnregisterDynamicCamera(cctvCamera);
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (!activeCameras.Contains(this))
        {
            activeCameras.Add(this);
        }
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        SyncDashboardRegistration();
    }

    protected override void OnDisable()
    {
        activeCameras.Remove(this);
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        UnregisterFromDashboard();
        RestorePlayerAmbientLight();
        RestoreLocalCameraOverridesIfIdle();
        base.OnDisable();
        SyncMainCameraGhostOrbsMask(this);
    }

    private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam == cctvCamera)
        {
            SyncCCTVCameraWorldPose();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isPoweredOn.OnValueChanged += (oldVal, newVal) => UpdateNightVisionVisuals();
        SyncCCTVCameraWorldPose();
        UpdateNightVisionVisuals();
        SyncDashboardRegistration();
    }

    public override void OnNetworkDespawn()
    {
        UnregisterFromDashboard();
        base.OnNetworkDespawn();
    }

    protected override void OnSyncedScaleApplied(Vector3 appliedScale)
    {
        base.OnSyncedScaleApplied(appliedScale);
        SyncCCTVCameraWorldPose();
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

    protected override void UpdateEquipState(ulong newOwnerId)
    {
        base.UpdateEquipState(newOwnerId);
        UpdateNightVisionVisuals();
        SyncDashboardRegistration();
    }

    protected override void OnInHandChanged(bool inHand)
    {
        base.OnInHandChanged(inHand);
        UpdateNightVisionVisuals();
        SyncDashboardRegistration();
    }

    protected override void OnPlacedChanged(bool placed)
    {
        base.OnPlacedChanged(placed);
        SyncDashboardRegistration();
    }

    protected override void LateUpdate()
    {
        base.LateUpdate();
        SyncCCTVCameraWorldPose();
    }

    /// <summary>
    /// Keep the night vision light's world rotation at identity and position at the player's center
    /// so it doesn't rotate or swing with the player's arms, preventing shifting shadows.
    /// </summary>
    protected override void Update()
    {
        base.Update();
        if (nightVisionLight != null && nightVisionLight.enabled)
        {
            nightVisionLight.transform.rotation = Quaternion.identity;

            // Anchor the light to the holder's body center so hand movements don't swing shadows
            Transform holder = ResolveHolderPlayerTransform();
            if (holder != null)
            {
                nightVisionLight.transform.position = holder.position;
            }
            else if (PlayerController.LocalInstance != null && IsHeldByLocalPlayer)
            {
                nightVisionLight.transform.position = PlayerController.LocalInstance.transform.position;
            }
            else
            {
                nightVisionLight.transform.position = transform.position;
            }
        }
    }

    private void RestoreLocalCameraOverridesIfIdle()
    {
        if (appliedLocalCameraOverrides && !HasLocalActiveNightVisionCamera(this))
        {
            if (localCamera != null && cameraSettingsStored)
            {
                localCamera.backgroundColor = originalCameraColor;
                localCamera.orthographicSize = originalOrthoSize;

                if (cameraData != null)
                {
                    cameraData.renderPostProcessing = originalPostProcessing;
                }
            }
            appliedLocalCameraOverrides = false;
        }
    }

    private void UpdateNightVisionVisuals()
    {
        if (localCamera == null)
        {
            localCamera = Camera.main;
            if (localCamera != null && !cameraSettingsStored)
            {
                originalCameraColor = localCamera.backgroundColor;
                originalCullingMask = localCamera.cullingMask;
                originalOrthoSize = localCamera.orthographicSize;
                
                cameraData = localCamera.GetComponent<UniversalAdditionalCameraData>();
                if (cameraData != null)
                {
                    originalPostProcessing = cameraData.renderPostProcessing;
                }
                
                cameraSettingsStored = true;
            }
        }

        bool localAlive = PlayerController.LocalInstance == null || PlayerController.LocalInstance.IsAlive;
        bool nightVisionActive = localAlive && !IsHeldByDeadPlayer() && IsLocallyHeldForNightVision() && isPoweredOn.Value;

        if (localCamera != null)
        {
            if (nightVisionActive)
            {
                localCamera.backgroundColor = nightVisionColor;
                if (cameraSettingsStored)
                {
                    localCamera.orthographicSize = originalOrthoSize - 0.1f;
                    if (cameraData != null)
                    {
                        cameraData.renderPostProcessing = true;
                    }
                }
                appliedLocalCameraOverrides = true;
            }
            else
            {
                RestoreLocalCameraOverridesIfIdle();
                if (GhostSpectatorVisuals.Instance != null && GhostSpectatorVisuals.Instance.IsActive && cameraData != null)
                {
                    cameraData.renderPostProcessing = true;
                }
            }
        }

        SyncMainCameraGhostOrbsMask();

        if (nightVisionVolume != null)
        {
            nightVisionVolume.weight = nightVisionActive ? 1f : 0f;
        }

        // Only the local holder enables the light (client-sided)
        if (nightVisionLight != null)
        {
            nightVisionLight.enabled = nightVisionActive;
        }

        if (nightVisionActive)
        {
            DimPlayerAmbientLight();
        }
        else
        {
            RestorePlayerAmbientLight();
        }
    }

    private void DimPlayerAmbientLight()
    {
        var localPlayer = PlayerController.LocalInstance;
        if (localPlayer == null) return;

        Light2D targetLight = localPlayer.PlayerAmbientLight != null
            ? localPlayer.PlayerAmbientLight
            : localPlayer.GetComponent<Light2D>();

        if (targetLight == null) return;

        if (playerAmbientLight != targetLight)
        {
            RestorePlayerAmbientLight();
            playerAmbientLight = targetLight;
            originalPlayerLightIntensity = playerAmbientLight.intensity;
            playerLightStored = true;
        }
        else if (!playerLightStored)
        {
            originalPlayerLightIntensity = playerAmbientLight.intensity;
            playerLightStored = true;
        }

        playerAmbientLight.intensity = Mathf.Min(originalPlayerLightIntensity, 0.25f);
    }

    private void RestorePlayerAmbientLight()
    {
        if (playerAmbientLight != null && playerLightStored)
        {
            playerAmbientLight.intensity = originalPlayerLightIntensity;
        }

        playerLightStored = false;
        playerAmbientLight = null;
    }

    public override void OnDestroy()
    {
        RestorePlayerAmbientLight();
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        UnregisterFromDashboard();

        if (nightVisionVolume != null)
        {
            if (nightVisionVolume.sharedProfile != null) Destroy(nightVisionVolume.sharedProfile);
            Destroy(nightVisionVolume.gameObject);
        }
        if (nightVisionLight != null) Destroy(nightVisionLight.gameObject);

        RestoreLocalCameraOverridesIfIdle();
        SyncMainCameraGhostOrbsMask(this);
    }
}
