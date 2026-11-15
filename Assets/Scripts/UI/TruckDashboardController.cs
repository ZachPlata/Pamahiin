using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DefaultExecutionOrder(-10)]
[RequireComponent(typeof(UIDocument))]
public class TruckDashboardController : MonoBehaviour, IInteractable
{
    [Header("CCTV Settings")]
    [SerializeField] private RenderTexture cctvRenderTexture;
    [SerializeField] private Camera[] houseCameras;
    private List<Camera> allCameras = new List<Camera>();
    private int currentCameraIndex = 0;

    private UIDocument uiDocument;

    // Views
    private VisualElement viewCctv;
    private VisualElement viewSanity;
    private VisualElement viewActivity;

    // Tabs
    private Button tabCctv;
    private Button tabSanity;
    private Button tabActivity;

    // UI Elements
    private VisualElement cctvContainer;
    private Label noSignalLabel;
    private Label cctvScreenTitle;
    private VisualElement cctvHudOverlay;
    private VisualElement cctvRecDot;
    private Label cctvTimer;
    private Label cctvCamIdLabel;
    private VisualElement cctvNvDot;
    private Label cctvNvBadge;
    private Label cctvTimestamp;
    private Label cctvHudSub;
    private Button btnPrevCamera;
    private Button btnNextCamera;
    private Button btnToggleNv;
    private ScrollView sanityList;
    private VisualElement activityBar;
    private Label activityLevelText;

    private float updateTimer = 0f;
    private bool isDashboardOpen = false;
    private int closedFrame = -1;
    private bool createdRuntimeRenderTexture = false;

    // CCTV Night Vision
    private bool isCCTVNightVisionOn = false;
    private Light2D cctvNightVisionLight;
    private Volume cctvVolume;
    private VisualElement cctvNvColorFilter;
    private float cctvRecBlinkTimer = 0f;
    private bool cctvRecBlinkState = true;

    public static TruckDashboardController Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.isDashboardOpen;
    public static bool WasOpenThisFrame => IsOpen || (Instance != null && Instance.closedFrame == Time.frameCount);
    public bool IsDashboardOpen => isDashboardOpen;
    public bool IsCCTVNightVisionOn => isCCTVNightVisionOn;
    public Camera ActiveCamera => (allCameras.Count > 0 && currentCameraIndex >= 0 && currentCameraIndex < allCameras.Count) ? allCameras[currentCameraIndex] : null;

    private RoofFader[] cachedRoofFaders;
    private List<RoofFader> activeHiddenFaders = new List<RoofFader>();
    private readonly List<PlayerController> sortedSanityPlayers = new List<PlayerController>(4);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        uiDocument = GetComponent<UIDocument>();
        if (houseCameras != null)
        {
            for (int i = 0; i < houseCameras.Length; i++)
            {
                if (houseCameras[i] != null && !allCameras.Contains(houseCameras[i]))
                {
                    allCameras.Add(houseCameras[i]);
                }
            }
        }
        EnsureRenderTexture();

        // Collect all sorting layer IDs so 2D lights illuminate Floors, Walls, Items, etc.
        int[] allSortingLayerIds = System.Array.ConvertAll(SortingLayer.layers, layer => layer.id);
        int cctvLayer = VideoCameraItem.ResolveCCTVLayer();

        // 1. Setup unscaled dynamic CCTV IR Point Light isolated to the CCTVOnly layer
        GameObject lightObj = new GameObject("CCTV_IR_Light");
        lightObj.layer = cctvLayer;
        lightObj.hideFlags = HideFlags.HideInHierarchy;
        lightObj.transform.SetParent(null);
        lightObj.transform.localScale = Vector3.one;
        cctvNightVisionLight = lightObj.AddComponent<Light2D>();
        VideoCameraItem.ApplyNightVisionLightSettings(
            cctvNightVisionLight,
            VideoCameraItem.NightVisionLightOuterRadius,
            allSortingLayerIds);
        cctvNightVisionLight.enabled = false;

        // 2. Setup CCTV B&W / Night Vision Volume isolated to the CCTVOnly layer
        GameObject volumeObj = new GameObject("CCTV_NV_Volume");
        volumeObj.layer = cctvLayer;
        volumeObj.hideFlags = HideFlags.HideInHierarchy;
        volumeObj.transform.SetParent(null);
        volumeObj.transform.localScale = Vector3.one;
        cctvVolume = volumeObj.AddComponent<Volume>();
        cctvVolume.isGlobal = true;
        cctvVolume.priority = 350;
        cctvVolume.sharedProfile = VideoCameraItem.CreateNightVisionVolumeProfile();
        cctvVolume.weight = 0f;
        cctvVolume.enabled = false;

        IsolateNonCCTVCamera(Camera.main);
    }

    private void Start()
    {
        IsolateNonCCTVCamera(Camera.main);
        VideoCameraItem.SyncMainCameraGhostOrbsMask();
    }

    private void IsolateNonCCTVCamera(Camera cam)
    {
        if (cam == null) return;
        int cctvLayer = VideoCameraItem.ResolveCCTVLayer();
        cam.cullingMask &= ~(1 << cctvLayer);

        var camData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (camData != null)
        {
            camData.volumeLayerMask &= ~(1 << cctvLayer);
        }
    }

    private void EnsureRenderTexture()
    {
        if (cctvRenderTexture == null)
        {
            cctvRenderTexture = new RenderTexture(640, 480, 24);
            cctvRenderTexture.name = "CCTV_AutoRT";
            cctvRenderTexture.filterMode = FilterMode.Point;
            cctvRenderTexture.Create();
            createdRuntimeRenderTexture = true;
        }
        else if (!cctvRenderTexture.IsCreated())
        {
            cctvRenderTexture.Create();
        }
    }

    private void EnsureCameraPostProcessing(Camera cam)
    {
        if (cam == null) return;
        int cctvLayer = VideoCameraItem.ResolveCCTVLayer();
        cam.cullingMask |= (1 << cctvLayer);

        var camData = cam.GetComponent<UniversalAdditionalCameraData>();
        if (camData == null) camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;
        camData.volumeLayerMask = (1 << cctvLayer);
        cam.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);
    }

    private void PruneNullCameras()
    {
        for (int i = allCameras.Count - 1; i >= 0; i--)
        {
            if (allCameras[i] == null)
            {
                allCameras.RemoveAt(i);
                if (i < currentCameraIndex) currentCameraIndex--;
            }
        }
        if (allCameras.Count == 0)
        {
            currentCameraIndex = 0;
        }
        else
        {
            currentCameraIndex = Mathf.Clamp(currentCameraIndex, 0, allCameras.Count - 1);
        }
    }

    private void DisableAllCCTVCameras()
    {
        for (int i = 0; i < allCameras.Count; i++)
        {
            if (allCameras[i] != null)
            {
                allCameras[i].enabled = false;
                allCameras[i].targetTexture = null;
            }
        }
    }

    private Vector3 GetActiveCCTVLightPosition(Camera cam, VideoCameraItem videoCam = null)
    {
        if (videoCam == null && cam != null)
        {
            videoCam = cam.GetComponentInParent<VideoCameraItem>();
        }
        if (videoCam != null)
        {
            return videoCam.GetCCTVNightVisionLightPosition();
        }
        if (cam != null)
        {
            return new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
        }
        return Vector3.zero;
    }

    private void SyncNightVisionRenderState()
    {
        Camera activeCam = ActiveCamera;
        bool nvRenderActive = isDashboardOpen && isCCTVNightVisionOn && activeCam != null;

        if (nvRenderActive)
        {
            EnsureCameraPostProcessing(activeCam);

            var videoCam = activeCam.GetComponentInParent<VideoCameraItem>();
            if (videoCam != null)
            {
                videoCam.SyncCCTVCameraWorldPose();
            }

            if (cctvNightVisionLight != null)
            {
                cctvNightVisionLight.transform.SetPositionAndRotation(
                    GetActiveCCTVLightPosition(activeCam, videoCam),
                    Quaternion.identity);
                cctvNightVisionLight.enabled = true;
            }

            if (cctvVolume != null)
            {
                cctvVolume.enabled = true;
                cctvVolume.weight = 1f;
            }
        }
        else
        {
            DisableCCTVNightVisionPass();
        }
    }

    private void DisableCCTVNightVisionPass()
    {
        if (cctvVolume != null)
        {
            cctvVolume.weight = 0f;
            cctvVolume.enabled = false;
        }
        if (cctvNightVisionLight != null) cctvNightVisionLight.enabled = false;
    }

    private void OnDestroy()
    {
        PlayerController.OnAnyPlayerSanityStateChanged -= HandlePlayerSanityStateChanged;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        RestoreActiveHiddenFaders();
        DisableAllCCTVCameras();
        DisableCCTVNightVisionPass();
        if (cctvNightVisionLight != null) Destroy(cctvNightVisionLight.gameObject);
        if (cctvVolume != null)
        {
            if (cctvVolume.sharedProfile != null) Destroy(cctvVolume.sharedProfile);
            Destroy(cctvVolume.gameObject);
        }
        if (createdRuntimeRenderTexture && cctvRenderTexture != null)
        {
            cctvRenderTexture.Release();
            Destroy(cctvRenderTexture);
            cctvRenderTexture = null;
        }
        if (Instance == this) Instance = null;
    }

    private void OnDisable()
    {
        PlayerController.OnAnyPlayerSanityStateChanged -= HandlePlayerSanityStateChanged;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        RestoreActiveHiddenFaders();
        DisableAllCCTVCameras();
        DisableCCTVNightVisionPass();
    }

    private void EnsureRoofFaders()
    {
        if (cachedRoofFaders == null || cachedRoofFaders.Length == 0)
        {
            cachedRoofFaders = Object.FindObjectsByType<RoofFader>(FindObjectsInactive.Exclude);
        }
    }

    private void ApplyRoofFaderForCamera(Camera cam, bool hide)
    {
        if (cam == null)
        {
            RestoreActiveHiddenFaders();
            return;
        }

        EnsureRoofFaders();
        if (cachedRoofFaders == null || cachedRoofFaders.Length == 0) return;

        if (hide)
        {
            RestoreActiveHiddenFaders();
            Vector2 camPos = cam.transform.position;
            foreach (var fader in cachedRoofFaders)
            {
                if (fader != null && fader.IsPositionInside(camPos))
                {
                    fader.SetCameraRenderHide(true);
                    activeHiddenFaders.Add(fader);
                }
            }
        }
        else
        {
            RestoreActiveHiddenFaders();
        }
    }

    private void RestoreActiveHiddenFaders()
    {
        if (activeHiddenFaders.Count > 0)
        {
            foreach (var fader in activeHiddenFaders)
            {
                if (fader != null)
                {
                    fader.SetCameraRenderHide(false);
                }
            }
            activeHiddenFaders.Clear();
        }
    }

    private void LateUpdate()
    {
        Camera activeCam = ActiveCamera;
        bool nvRenderActive = isDashboardOpen && isCCTVNightVisionOn && activeCam != null;

        if (isDashboardOpen && activeCam != null)
        {
            var videoCam = activeCam.GetComponentInParent<VideoCameraItem>();
            if (videoCam != null)
            {
                videoCam.SyncCCTVCameraWorldPose();
            }

            if (nvRenderActive && cctvNightVisionLight != null)
            {
                cctvNightVisionLight.transform.SetPositionAndRotation(
                    GetActiveCCTVLightPosition(activeCam, videoCam),
                    Quaternion.identity);
                cctvNightVisionLight.enabled = true;
            }
        }

        if (!nvRenderActive)
        {
            DisableCCTVNightVisionPass();
        }
    }

    private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        Camera activeCam = ActiveCamera;
        if (!isDashboardOpen || activeCam == null || cam != activeCam)
        {
            // Guarantee roof hiding & CCTV layers NEVER bleed onto Main Camera or other Game cameras
            RestoreActiveHiddenFaders();
            if (cam != null && cam.cameraType == CameraType.Game && !allCameras.Contains(cam))
            {
                IsolateNonCCTVCamera(cam);
            }
            if (!isDashboardOpen || !isCCTVNightVisionOn || activeCam == null)
            {
                DisableCCTVNightVisionPass();
            }
            return;
        }

        EnsureCameraPostProcessing(cam);

        var videoCam = cam.GetComponentInParent<VideoCameraItem>();
        if (videoCam != null)
        {
            videoCam.SyncCCTVCameraWorldPose();
        }

        // Temporarily hide the roof for this camera's render view if the camera is inside the house
        ApplyRoofFaderForCamera(cam, true);

        if (isCCTVNightVisionOn)
        {
            if (cctvVolume != null)
            {
                cctvVolume.enabled = true;
                cctvVolume.weight = 1f;
            }
            if (cctvNightVisionLight != null)
            {
                cctvNightVisionLight.transform.SetPositionAndRotation(
                    GetActiveCCTVLightPosition(cam, videoCam),
                    Quaternion.identity);
                cctvNightVisionLight.enabled = true;
            }
            cam.backgroundColor = VideoCameraItem.NightVisionBackgroundColor;
        }
        else
        {
            DisableCCTVNightVisionPass();
            cam.backgroundColor = Color.black;
        }
    }

    private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        Camera activeCam = ActiveCamera;
        if (activeCam != null && cam == activeCam)
        {
            // Restore roof renderers & camera background immediately after CCTV pass.
            // Keep CCTV_IR_Light & CCTV_NV_Volume active across LateUpdate() while CCTV NV stays enabled.
            ApplyRoofFaderForCamera(cam, false);
            cam.backgroundColor = Color.black;
            if (!isDashboardOpen || !isCCTVNightVisionOn)
            {
                DisableCCTVNightVisionPass();
            }
        }
    }

    public void RegisterDynamicCamera(Camera cam)
    {
        if (cam == null) return;
        var videoCam = cam.GetComponentInParent<VideoCameraItem>();
        if (videoCam != null && !videoCam.IsAvailableForCCTV) return;

        PruneNullCameras();
        EnsureRenderTexture();

        if (!allCameras.Contains(cam))
        {
            allCameras.Add(cam);
            EnsureCameraPostProcessing(cam);
            UpdateCameraSource();
            UpdateNoSignalState();
        }
    }

    public void UnregisterDynamicCamera(Camera cam)
    {
        if (cam == null) return;
        int removedIndex = allCameras.IndexOf(cam);
        if (removedIndex >= 0)
        {
            allCameras.RemoveAt(removedIndex);
            cam.enabled = false;
            cam.targetTexture = null;
            if (removedIndex < currentCameraIndex) currentCameraIndex--;
            PruneNullCameras();
            UpdateCameraSource();
            UpdateNoSignalState();
        }
    }

    private void RefreshDynamicCameras()
    {
        PruneNullCameras();
        var videoItems = Object.FindObjectsByType<VideoCameraItem>(FindObjectsInactive.Exclude);
        for (int i = 0; i < videoItems.Length; i++)
        {
            var item = videoItems[i];
            if (item == null || item.CctvCamera == null) continue;

            if (item.IsAvailableForCCTV)
            {
                item.SyncCCTVCameraWorldPose();
                if (!allCameras.Contains(item.CctvCamera))
                {
                    allCameras.Add(item.CctvCamera);
                    EnsureCameraPostProcessing(item.CctvCamera);
                }
            }
            else if (allCameras.Contains(item.CctvCamera))
            {
                allCameras.Remove(item.CctvCamera);
                item.CctvCamera.enabled = false;
                item.CctvCamera.targetTexture = null;
            }
        }
        PruneNullCameras();
    }

    private void UpdateNoSignalState()
    {
        if (noSignalLabel == null || cctvContainer == null) return;

        PruneNullCameras();
        EnsureRenderTexture();

        if (allCameras.Count > 0 && cctvRenderTexture != null)
        {
            noSignalLabel.style.display = DisplayStyle.None;
            if (cctvHudOverlay != null) cctvHudOverlay.style.display = DisplayStyle.Flex;
            cctvContainer.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(cctvRenderTexture));
        }
        else
        {
            noSignalLabel.style.display = DisplayStyle.Flex;
            if (cctvHudOverlay != null) cctvHudOverlay.style.display = DisplayStyle.None;
            cctvContainer.style.backgroundImage = StyleKeyword.None;
        }
    }

    private void OnEnable()
    {
        PlayerController.OnAnyPlayerSanityStateChanged -= HandlePlayerSanityStateChanged;
        PlayerController.OnAnyPlayerSanityStateChanged += HandlePlayerSanityStateChanged;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;

        uiDocument.sortingOrder = 2; // Explicitly put Dashboard above HUD
        var root = uiDocument.rootVisualElement;

        // Tabs
        tabCctv = root.Q<Button>("tab-cctv");
        tabSanity = root.Q<Button>("tab-sanity");
        tabActivity = root.Q<Button>("tab-activity");

        tabCctv.clicked += () => ShowView(viewCctv, tabCctv);
        tabSanity.clicked += () => ShowView(viewSanity, tabSanity);
        tabActivity.clicked += () => ShowView(viewActivity, tabActivity);

        // Views
        viewCctv = root.Q<VisualElement>("view-cctv");
        viewSanity = root.Q<VisualElement>("view-sanity");
        viewActivity = root.Q<VisualElement>("view-activity");

        // CCTV Elements
        cctvScreenTitle = root.Q<Label>("cctv-screen-title");
        cctvContainer = root.Q<VisualElement>("cctv-image-container");
        noSignalLabel = root.Q<Label>(className: "no-signal-text");
        cctvHudOverlay = root.Q<VisualElement>("cctv-hud-overlay");
        cctvRecDot = root.Q<VisualElement>("cctv-rec-dot");
        cctvTimer = root.Q<Label>("cctv-timer");
        cctvCamIdLabel = root.Q<Label>("cctv-hud-cam-id");
        cctvNvDot = root.Q<VisualElement>("cctv-nv-dot");
        cctvNvBadge = root.Q<Label>("cctv-nv-badge");
        cctvTimestamp = root.Q<Label>("cctv-timestamp");
        cctvHudSub = root.Q<Label>("cctv-hud-sub");
        btnPrevCamera = root.Q<Button>("btn-prev-camera");
        btnNextCamera = root.Q<Button>("btn-next-camera");
        btnToggleNv = root.Q<Button>("btn-toggle-nv");
        cctvNvColorFilter = root.Q<VisualElement>("cctv-nv-color-filter");
        
        EnsureRenderTexture();

        // Enable post-processing on all pre-configured cameras so volume effects apply
        PruneNullCameras();
        for (int i = 0; i < allCameras.Count; i++)
        {
            EnsureCameraPostProcessing(allCameras[i]);
        }

        if (btnPrevCamera != null) btnPrevCamera.clicked += SwitchToPreviousCamera;
        if (btnNextCamera != null) btnNextCamera.clicked += SwitchToNextCamera;
        
        if (btnToggleNv != null)
        {
            btnToggleNv.clicked += ToggleCCTVNightVision;
        }
        
        root.Q<Button>("btn-close").clicked += ToggleDashboard;

        // Sanity & Activity
        sanityList = root.Q<ScrollView>("sanity-list-container");
        activityBar = root.Q<VisualElement>("activity-bar");
        activityLevelText = root.Q<Label>("lbl-activity-level");

        // Default view
        ShowView(viewCctv, tabCctv);

        // Hide on start before disabling cameras
        root.style.display = DisplayStyle.None;
        isDashboardOpen = false;

        RefreshDynamicCameras();
        UpdateCameraSource();
        UpdateNoSignalState();
        ApplyNightVisionState();
        IsolateNonCCTVCamera(Camera.main);
        VideoCameraItem.SyncMainCameraGhostOrbsMask();
    }

    private void ShowView(VisualElement view, Button tab)
    {
        viewCctv.RemoveFromClassList("active-view");
        viewCctv.AddToClassList("hidden-view");
        viewSanity.RemoveFromClassList("active-view");
        viewSanity.AddToClassList("hidden-view");
        viewActivity.RemoveFromClassList("active-view");
        viewActivity.AddToClassList("hidden-view");

        tabCctv.RemoveFromClassList("active-tab");
        tabSanity.RemoveFromClassList("active-tab");
        tabActivity.RemoveFromClassList("active-tab");

        view.RemoveFromClassList("hidden-view");
        view.AddToClassList("active-view");
        tab.AddToClassList("active-tab");

        if (view == viewSanity)
        {
            UpdateSanityMonitor();
        }
    }

    private void HandlePlayerSanityStateChanged()
    {
        if (isDashboardOpen && sanityList != null)
        {
            UpdateSanityMonitor();
        }
    }

    private void SwitchToPreviousCamera()
    {
        PruneNullCameras();
        if (allCameras.Count == 0) return;

        currentCameraIndex = (currentCameraIndex - 1 + allCameras.Count) % allCameras.Count;
        UpdateCameraSource();
        UpdateNoSignalState();
    }

    private void SwitchToNextCamera()
    {
        PruneNullCameras();
        if (allCameras.Count == 0) return;

        currentCameraIndex = (currentCameraIndex + 1) % allCameras.Count;
        UpdateCameraSource();
        UpdateNoSignalState();
    }

    private string GetCameraDisplayName(Camera cam)
    {
        if (cam == null) return "UNKNOWN";
        var videoCam = cam.GetComponentInParent<VideoCameraItem>();
        if (videoCam != null)
        {
            if (videoCam.IsPlaced) return "VIDEO CAMERA (PLACED)";
            if (videoCam.CurrentHolderClientId != ulong.MaxValue) return $"VIDEO CAMERA (P{videoCam.CurrentHolderClientId})";
            return "VIDEO CAMERA";
        }
        return cam.name.ToUpper();
    }

    private void UpdateCameraNavButtons()
    {
        bool canSwitch = allCameras.Count > 1;
        btnPrevCamera?.SetEnabled(canSwitch);
        btnNextCamera?.SetEnabled(canSwitch);
    }

    private void UpdateCameraSource()
    {
        PruneNullCameras();
        EnsureRenderTexture();
        UpdateCameraNavButtons();

        if (allCameras.Count == 0)
        {
            if (cctvScreenTitle != null) cctvScreenTitle.text = "NO CAMERA SIGNAL";
            if (cctvCamIdLabel != null) cctvCamIdLabel.text = "NO SIGNAL";
            if (cctvHudSub != null) cctvHudSub.text = "NO SIGNAL";
            ApplyNightVisionState();
            return;
        }

        for (int i = 0; i < allCameras.Count; i++)
        {
            Camera cam = allCameras[i];
            if (cam == null) continue;

            bool isActive = isDashboardOpen && i == currentCameraIndex;
            if (!cam.gameObject.activeSelf) cam.gameObject.SetActive(true);
            cam.targetTexture = isActive ? cctvRenderTexture : null;
            cam.enabled = isActive;
        }

        Camera activeCam = ActiveCamera;
        if (activeCam != null)
        {
            string camName = GetCameraDisplayName(activeCam);
            string formattedTitle = $"CAM {currentCameraIndex + 1:00} / {allCameras.Count:00} - {camName}";
            if (cctvScreenTitle != null) cctvScreenTitle.text = formattedTitle;
            if (cctvCamIdLabel != null) cctvCamIdLabel.text = formattedTitle;
            if (cctvHudSub != null) cctvHudSub.text = $"CH-{currentCameraIndex + 1:00} FEED  |  FPS: 30  |  SIGNAL: OPTIMAL";
        }

        ApplyNightVisionState();
    }

    private void ToggleCCTVNightVision()
    {
        isCCTVNightVisionOn = !isCCTVNightVisionOn;
        Debug.Log($"[TruckDashboard] CCTV Night Vision toggled to: {isCCTVNightVisionOn}");
        ApplyNightVisionState();
    }

    private void ApplyNightVisionState()
    {
        // 1. Update Button Visuals
        if (btnToggleNv != null)
        {
            if (isCCTVNightVisionOn)
            {
                btnToggleNv.text = "Night Vision: [ON]";
                btnToggleNv.AddToClassList("btn-nv-active");
            }
            else
            {
                btnToggleNv.text = "Night Vision: [OFF]";
                btnToggleNv.RemoveFromClassList("btn-nv-active");
            }
        }

        // 2. Update CCTV screen tint & borders (pure monochrome black & white!)
        if (cctvContainer != null)
        {
            cctvContainer.style.unityBackgroundImageTintColor = new StyleColor(Color.white);
            if (isCCTVNightVisionOn)
            {
                cctvContainer.AddToClassList("cctv-screen-nv");
            }
            else
            {
                cctvContainer.RemoveFromClassList("cctv-screen-nv");
            }
        }

        // 2b. Hide any legacy color filter
        if (cctvNvColorFilter != null)
        {
            cctvNvColorFilter.style.display = DisplayStyle.None;
        }

        // 3. Update HUD Badge & Square Pixel LED
        if (cctvNvBadge != null)
        {
            if (isCCTVNightVisionOn)
            {
                cctvNvBadge.text = "IR NIGHT VISION: ON (B&W)";
                cctvNvBadge.RemoveFromClassList("cctv-nv-off");
                cctvNvBadge.AddToClassList("cctv-nv-on");
            }
            else
            {
                cctvNvBadge.text = "IR NIGHT VISION: OFF";
                cctvNvBadge.RemoveFromClassList("cctv-nv-on");
                cctvNvBadge.AddToClassList("cctv-nv-off");
            }
        }
        if (cctvNvDot != null)
        {
            if (isCCTVNightVisionOn)
            {
                cctvNvDot.RemoveFromClassList("cctv-nv-dot-off");
                cctvNvDot.AddToClassList("cctv-nv-dot-on");
            }
            else
            {
                cctvNvDot.RemoveFromClassList("cctv-nv-dot-on");
                cctvNvDot.AddToClassList("cctv-nv-dot-off");
            }
        }

        // 4. Update active camera feed (both dynamic VideoCameraItem feeds and any scene house cameras)
        Camera currentCam = ActiveCamera;
        if (currentCam != null)
        {
            EnsureCameraPostProcessing(currentCam);

            // Ensure Ghost Orbs layer is only rendered on CCTV when Night Vision is ON
            int ghostOrbLayer = LayerMask.NameToLayer("GhostOrbs");
            if (ghostOrbLayer != -1)
            {
                if (isCCTVNightVisionOn)
                    currentCam.cullingMask |= (1 << ghostOrbLayer);
                else
                    currentCam.cullingMask &= ~(1 << ghostOrbLayer);
            }
        }

        // 5. Sync Light2D & Volume state so Light2D.LateUpdate() generates mesh & boundingSphere cleanly
        SyncNightVisionRenderState();
    }

    public void Interact()
    {
        if (PlayerController.LocalInstance != null && !PlayerController.LocalInstance.IsAlive) return;
        ToggleDashboard();
    }
    
    public string GetInteractText() => "Access Dashboard";
    
    public bool CanDrag() => false;
    public void OnDragBegin(ulong clientId) {}
    public void OnDragUpdate(Vector2 targetPosition) {}
    public void OnDragEnd(ulong clientId) {}

    private void ToggleDashboard()
    {
        isDashboardOpen = !isDashboardOpen;

        if (isDashboardOpen)
        {
            uiDocument.rootVisualElement.style.display = DisplayStyle.Flex;
            IsolateNonCCTVCamera(Camera.main);
            VideoCameraItem.SyncMainCameraGhostOrbsMask();
            RefreshDynamicCameras();
            UpdateCameraSource();
            UpdateNoSignalState();
            UpdateSanityMonitor();
            UpdateActivityMonitor();
            updateTimer = 0f;
        }
        else
        {
            closedFrame = Time.frameCount;
            uiDocument.rootVisualElement.style.display = DisplayStyle.None;
            UpdateCameraSource();
            UpdateNoSignalState();
            RestoreActiveHiddenFaders();
            SyncNightVisionRenderState();
        }

        // Disable collider while UI is open to prevent accidental clicks passing through
        var col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.enabled = !isDashboardOpen;
        }
    }

    private void Update()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene") return;

        if (isDashboardOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            if (DeveloperConsoleManager.IsConsoleOpen) return;
            ToggleDashboard();
        }

        if (isDashboardOpen)
        {
            // Blink CCTV REC dot
            cctvRecBlinkTimer += Time.deltaTime;
            if (cctvRecBlinkTimer >= 0.5f)
            {
                cctvRecBlinkTimer = 0f;
                cctvRecBlinkState = !cctvRecBlinkState;
                if (cctvRecDot != null)
                {
                    if (cctvRecBlinkState) cctvRecDot.RemoveFromClassList("cctv-rec-dot-dim");
                    else cctvRecDot.AddToClassList("cctv-rec-dot-dim");
                }
            }

            // Live timestamp & CCTV clock
            int totalSec = Mathf.FloorToInt(Time.timeSinceLevelLoad);
            if (cctvTimer != null)
            {
                int h = totalSec / 3600;
                int m = (totalSec % 3600) / 60;
                int s = totalSec % 60;
                cctvTimer.text = $"{h:00}:{m:00}:{s:00}";
            }

            if (cctvTimestamp != null)
            {
                cctvTimestamp.text = System.DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
            }

            updateTimer += Time.deltaTime;
            if (updateTimer >= 0.5f) // Rebuild dashboard lists only while dashboard is open
            {
                updateTimer = 0f;
                UpdateSanityMonitor();
                UpdateActivityMonitor();
            }
        }
    }

    private void UpdateSanityMonitor()
    {
        if (sanityList == null) return;

        sanityList.Clear();
        sortedSanityPlayers.Clear();

        var allPlayers = PlayerController.AllPlayers;
        for (int i = 0; i < allPlayers.Count; i++)
        {
            if (allPlayers[i] != null)
            {
                sortedSanityPlayers.Add(allPlayers[i]);
            }
        }

        sortedSanityPlayers.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        for (int i = 0; i < sortedSanityPlayers.Count; i++)
        {
            var player = sortedSanityPlayers[i];
            int displayIndex = i + 1;

            var row = new VisualElement();
            row.AddToClassList("sanity-row");

            string playerLabel = $"Player {displayIndex}" + (player.IsOwner ? " (YOU)" : "");
            var nameLabel = new Label(playerLabel);
            nameLabel.AddToClassList("sanity-name");

            bool isDead = !player.IsAlive;
            int roundedSanity = Mathf.Clamp(Mathf.RoundToInt(player.Sanity), 0, 100);
            var sanityValueLabel = new Label(isDead ? "DEAD" : $"{roundedSanity}%");
            sanityValueLabel.AddToClassList("sanity-value");

            if (isDead)
            {
                sanityValueLabel.style.color = new StyleColor(new Color(0.55f, 0.77f, 1f, 0.95f));
            }
            else if (roundedSanity >= 60)
            {
                sanityValueLabel.style.color = new StyleColor(new Color(0.78f, 1f, 0.78f, 1f));
            }
            else if (roundedSanity >= 25)
            {
                sanityValueLabel.style.color = new StyleColor(new Color(1f, 0.78f, 0.28f, 1f));
            }
            else
            {
                sanityValueLabel.style.color = new StyleColor(new Color(1f, 0.32f, 0.32f, 1f));
            }

            row.Add(nameLabel);
            row.Add(sanityValueLabel);
            sanityList.Add(row);
        }
    }

    private void UpdateActivityMonitor()
    {
        if (ParanormalManager.Instance != null)
        {
            int level = ParanormalManager.Instance.GetGlobalActivityLevel();
            
            // Random noise if level > 0 to simulate EMF fluctuations
            if (level > 0 && level < 10)
            {
                level = Mathf.Clamp(level + Random.Range(-1, 2), 1, 9);
            }

            activityBar.style.height = new Length(level * 10, LengthUnit.Percent);
            activityLevelText.text = $"Activity Level: {level}";
        }
    }
}
