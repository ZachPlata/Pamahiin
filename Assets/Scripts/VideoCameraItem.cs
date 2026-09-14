using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VideoCameraItem : EquipmentItem
{
    [Header("Video Camera Settings")]
    [SerializeField] private Color nightVisionColor = new Color(0.2f, 0.2f, 0.2f);
    [SerializeField] private float nightVisionRadius = 15f;
    
    private NetworkVariable<bool> isPoweredOn = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override bool IsPoweredOn => isPoweredOn.Value;

    private Camera localCamera;
    private Color originalCameraColor;
    private int originalCullingMask;
    private float originalOrthoSize;
    private bool originalPostProcessing;
    private UniversalAdditionalCameraData cameraData;
    private bool cameraSettingsStored = false;

    private Volume nightVisionVolume;
    private Light2D nightVisionLight;
    
    private Camera cctvCamera;
    private Volume cctvBWVolume;
    private Light2D cctvNightVisionLight;
    private bool cctvNightVisionEnabled = false;

    /// <summary>
    /// Whether CCTV night vision is currently enabled (toggled from the dashboard).
    /// </summary>
    public bool IsCCTVNightVisionOn => cctvNightVisionEnabled;

    protected override void Awake()
    {
        base.Awake();
        itemName = "Video Camera";
        SetupNightVisionEffects();
    }

    private void SetupNightVisionEffects()
    {
        // 1. Setup Volume for Black and White (player night vision)
        GameObject volumeObj = new GameObject("NightVisionVolume");
        volumeObj.transform.SetParent(transform);
        nightVisionVolume = volumeObj.AddComponent<Volume>();
        nightVisionVolume.isGlobal = true;
        nightVisionVolume.priority = 100;
        
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        if (profile.Add<ColorAdjustments>(true))
        {
            if (profile.TryGet(out ColorAdjustments colorAdj))
            {
                colorAdj.saturation.Override(-100f);
            }
        }
        nightVisionVolume.sharedProfile = profile;
        nightVisionVolume.weight = 0f;

        // 2. Setup Point Light2D to see in the dark (player hand-held night vision)
        // Uses shadow casting so the light is blocked by walls (requires ShadowCaster2D on walls).
        // Only the local holder enables it (client-sided), so other players won't see it.
        GameObject lightObj = new GameObject("NightVisionLight");
        lightObj.transform.SetParent(transform);
        nightVisionLight = lightObj.AddComponent<Light2D>();
        nightVisionLight.lightType = Light2D.LightType.Point;
        nightVisionLight.color = Color.white;
        nightVisionLight.intensity = 1.0f;
        nightVisionLight.pointLightOuterRadius = nightVisionRadius;
        nightVisionLight.pointLightInnerRadius = 0f;
        nightVisionLight.pointLightInnerAngle = 360f;
        nightVisionLight.pointLightOuterAngle = 360f;
        nightVisionLight.shadowsEnabled = true;
        nightVisionLight.shadowIntensity = 1f;
        nightVisionLight.enabled = false;
        
        // 3. Setup CCTV Camera for the Truck Dashboard feed
        GameObject cctvObj = new GameObject("CCTV_POV");
        cctvObj.transform.SetParent(transform, false);
        cctvObj.transform.localPosition = new Vector3(0, 0, -10f);
        cctvCamera = cctvObj.AddComponent<Camera>();
        cctvCamera.orthographic = true;
        cctvCamera.orthographicSize = 5f;
        cctvCamera.backgroundColor = Color.black;
        cctvCamera.clearFlags = CameraClearFlags.SolidColor;
        cctvCamera.depth = -10; // Lower depth so it doesn't interfere with main camera
        cctvCamera.enabled = false; // Dashboard will manage enabling/disabling
        
        // Ensure CCTV can see Ghost Orbs layer
        int ghostOrbLayer = LayerMask.NameToLayer("GhostOrbs");
        if (ghostOrbLayer != -1)
        {
            cctvCamera.cullingMask |= (1 << ghostOrbLayer);
        }
        
        // Enable post-processing on the CCTV camera
        var cctvCamData = cctvObj.AddComponent<UniversalAdditionalCameraData>();
        cctvCamData.renderPostProcessing = true;
        
        // 4. Setup B&W Volume for CCTV feed
        // Toggled via camera rendering events so it only applies during CCTV camera rendering.
        // Only active when CCTV night vision is enabled from the dashboard.
        GameObject cctvVolumeObj = new GameObject("CCTV_BW_Volume");
        cctvVolumeObj.transform.SetParent(cctvObj.transform);
        
        cctvBWVolume = cctvVolumeObj.AddComponent<Volume>();
        cctvBWVolume.isGlobal = true;
        cctvBWVolume.priority = 200;
        cctvBWVolume.weight = 0f;
        
        VolumeProfile cctvProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        if (cctvProfile.Add<ColorAdjustments>(true))
        {
            if (cctvProfile.TryGet(out ColorAdjustments cctvColorAdj))
            {
                cctvColorAdj.saturation.Override(-100f);
            }
        }
        cctvBWVolume.sharedProfile = cctvProfile;

        // 5. Setup CCTV Night Vision Light
        // This light is toggled via rendering events (begin/end camera rendering)
        // so it only illuminates during the CCTV camera's render — invisible to main camera.
        GameObject cctvLightObj = new GameObject("CCTV_NightVisionLight");
        cctvLightObj.transform.SetParent(cctvObj.transform);
        cctvLightObj.transform.localPosition = Vector3.zero;
        cctvNightVisionLight = cctvLightObj.AddComponent<Light2D>();
        cctvNightVisionLight.lightType = Light2D.LightType.Point;
        cctvNightVisionLight.color = Color.white;
        cctvNightVisionLight.intensity = 1.0f;
        cctvNightVisionLight.pointLightOuterRadius = nightVisionRadius;
        cctvNightVisionLight.pointLightInnerRadius = 0f;
        cctvNightVisionLight.pointLightInnerAngle = 360f;
        cctvNightVisionLight.pointLightOuterAngle = 360f;
        cctvNightVisionLight.shadowsEnabled = true;
        cctvNightVisionLight.shadowIntensity = 1f;
        cctvNightVisionLight.enabled = false;
    }

    private void OnEnable()
    {
        // Subscribe to camera rendering events for CCTV night vision toggle
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        
        // Ensure everything is off when disabled
        if (cctvBWVolume != null) cctvBWVolume.weight = 0f;
        if (cctvNightVisionLight != null) cctvNightVisionLight.enabled = false;
    }

    /// <summary>
    /// Right before the CCTV camera renders, enable NV effects if toggled on.
    /// This ensures B&W + light only apply to the CCTV feed, not the main camera.
    /// </summary>
    private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam == cctvCamera && cctvNightVisionEnabled)
        {
            if (cctvBWVolume != null) cctvBWVolume.weight = 1f;
            if (cctvNightVisionLight != null) cctvNightVisionLight.enabled = true;
        }
    }

    /// <summary>
    /// Right after the CCTV camera finishes, disable NV effects
    /// so the main camera renders normally.
    /// </summary>
    private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam == cctvCamera)
        {
            if (cctvBWVolume != null) cctvBWVolume.weight = 0f;
            if (cctvNightVisionLight != null) cctvNightVisionLight.enabled = false;
        }
    }

    /// <summary>
    /// Toggle CCTV night vision on/off. Called from the TruckDashboardController.
    /// </summary>
    public void SetCCTVNightVision(bool enabled)
    {
        cctvNightVisionEnabled = enabled;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isPoweredOn.OnValueChanged += (oldVal, newVal) => UpdateNightVisionVisuals();
        
        // Register the CCTV camera with the Truck Dashboard
        if (cctvCamera != null)
        {
            var dashboard = FindAnyObjectByType<TruckDashboardController>();
            if (dashboard != null)
            {
                dashboard.RegisterDynamicCamera(cctvCamera);
            }
        }
    }

    public override void UsePrimary()
    {
        TogglePowerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void TogglePowerRpc()
    {
        isPoweredOn.Value = !isPoweredOn.Value;
    }

    protected override void UpdateEquipState(ulong newOwnerId)
    {
        base.UpdateEquipState(newOwnerId);
        UpdateNightVisionVisuals();
    }

    protected override void OnInHandChanged(bool inHand)
    {
        base.OnInHandChanged(inHand);
        UpdateNightVisionVisuals();
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
            
            // Anchor the light to the root (player) position so hand movements don't swing the shadows
            if (transform.root != null)
            {
                nightVisionLight.transform.position = transform.root.position;
            }
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

        bool isHolding = (ownerClientId.Value != ulong.MaxValue && 
                            NetworkManager.Singleton != null && 
                            ownerClientId.Value == NetworkManager.Singleton.LocalClientId && 
                            isInHand.Value);

        bool nightVisionActive = isHolding && isPoweredOn.Value;

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
                
                int ghostOrbLayer = LayerMask.NameToLayer("GhostOrbs");
                if (ghostOrbLayer != -1)
                {
                    localCamera.cullingMask |= (1 << ghostOrbLayer);
                }
            }
            else
            {
                if (cameraSettingsStored)
                {
                    localCamera.backgroundColor = originalCameraColor;
                    localCamera.orthographicSize = originalOrthoSize;
                    
                    if (cameraData != null)
                    {
                        cameraData.renderPostProcessing = originalPostProcessing;
                    }
                    
                    int ghostOrbLayer = LayerMask.NameToLayer("GhostOrbs");
                    if (ghostOrbLayer != -1)
                    {
                        localCamera.cullingMask &= ~(1 << ghostOrbLayer);
                    }
                }
            }
        }

        if (nightVisionVolume != null)
        {
            nightVisionVolume.weight = nightVisionActive ? 1f : 0f;
        }

        // Only the local holder enables the light (client-sided)
        if (nightVisionLight != null)
        {
            nightVisionLight.enabled = nightVisionActive;
        }
    }

    public override void OnDestroy()
    {
        // Unsubscribe from rendering events
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;

        // Unregister from dashboard
        if (cctvCamera != null)
        {
            var dashboard = FindAnyObjectByType<TruckDashboardController>();
            if (dashboard != null)
            {
                dashboard.UnregisterDynamicCamera(cctvCamera);
            }
        }

        if (localCamera != null && cameraSettingsStored)
        {
            localCamera.backgroundColor = originalCameraColor;
            localCamera.orthographicSize = originalOrthoSize;
            
            if (cameraData != null)
            {
                cameraData.renderPostProcessing = originalPostProcessing;
            }
            
            int ghostOrbLayer = LayerMask.NameToLayer("GhostOrbs");
            if (ghostOrbLayer != -1)
            {
                localCamera.cullingMask &= ~(1 << ghostOrbLayer);
            }
        }
    }
}
