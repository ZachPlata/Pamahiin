using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
    private Label cctvNvBadge;
    private Label cctvTimestamp;
    private Button btnToggleNv;
    private ScrollView sanityList;
    private VisualElement activityBar;
    private Label activityLevelText;

    private float updateTimer = 0f;
    private bool isDashboardOpen = false;
    private int closedFrame = -1;

    // CCTV Night Vision
    private bool isCCTVNightVisionOn = false;
    private Light2D cctvNightVisionLight;
    private Light2D cctvGlobalLight;
    private Volume cctvVolume;
    private VisualElement cctvNvColorFilter;
    private float cctvRecBlinkTimer = 0f;
    private bool cctvRecBlinkState = true;

    public static TruckDashboardController Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.isDashboardOpen;
    public static bool WasOpenThisFrame => IsOpen || (Instance != null && Instance.closedFrame == Time.frameCount);
    public bool IsDashboardOpen => isDashboardOpen;

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
            allCameras.AddRange(houseCameras);
        }

        // Collect all sorting layer IDs so 2D lights illuminate Floors, Walls, Items, etc.
        int[] allSortingLayerIds = System.Array.ConvertAll(SortingLayer.layers, layer => layer.id);

        // 1. Setup unscaled dynamic CCTV IR Point Light for night vision
        GameObject lightObj = new GameObject("CCTV_IR_Light");
        lightObj.hideFlags = HideFlags.HideInHierarchy;
        lightObj.transform.SetParent(null);
        lightObj.transform.localScale = Vector3.one;
        cctvNightVisionLight = lightObj.AddComponent<Light2D>();
        cctvNightVisionLight.lightType = Light2D.LightType.Point;
        cctvNightVisionLight.color = Color.white;
        cctvNightVisionLight.intensity = 2.5f;
        cctvNightVisionLight.pointLightOuterRadius = 45f;
        cctvNightVisionLight.pointLightInnerRadius = 0f;
        cctvNightVisionLight.pointLightInnerAngle = 360f;
        cctvNightVisionLight.pointLightOuterAngle = 360f;
        cctvNightVisionLight.shadowsEnabled = false;
        cctvNightVisionLight.targetSortingLayers = allSortingLayerIds;
        cctvNightVisionLight.enabled = false;

        // 2. Setup unscaled dynamic CCTV Global IR Light for unlit rooms
        GameObject globalLightObj = new GameObject("CCTV_IR_GlobalLight");
        globalLightObj.hideFlags = HideFlags.HideInHierarchy;
        globalLightObj.transform.SetParent(null);
        globalLightObj.transform.localScale = Vector3.one;
        cctvGlobalLight = globalLightObj.AddComponent<Light2D>();
        cctvGlobalLight.lightType = Light2D.LightType.Global;
        cctvGlobalLight.color = Color.white;
        cctvGlobalLight.intensity = 1.2f;
        cctvGlobalLight.targetSortingLayers = allSortingLayerIds;
        cctvGlobalLight.enabled = false;

        // 3. Setup CCTV B&W / Night Vision Volume
        GameObject volumeObj = new GameObject("CCTV_NV_Volume");
        volumeObj.hideFlags = HideFlags.HideInHierarchy;
        volumeObj.transform.SetParent(null);
        volumeObj.transform.localScale = Vector3.one;
        cctvVolume = volumeObj.AddComponent<Volume>();
        cctvVolume.isGlobal = true;
        cctvVolume.priority = 350;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        if (profile.Add<ColorAdjustments>(true))
        {
            if (profile.TryGet(out ColorAdjustments colorAdj))
            {
                colorAdj.saturation.Override(-100f);
                colorAdj.postExposure.Override(1.6f);
                colorAdj.contrast.Override(25f);
            }
        }
        cctvVolume.sharedProfile = profile;
        cctvVolume.weight = 0f;
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        if (cctvNightVisionLight != null) Destroy(cctvNightVisionLight.gameObject);
        if (cctvGlobalLight != null) Destroy(cctvGlobalLight.gameObject);
        if (cctvVolume != null) Destroy(cctvVolume.gameObject);
        if (Instance == this) Instance = null;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        if (cctvNightVisionLight != null) cctvNightVisionLight.enabled = false;
        if (cctvGlobalLight != null) cctvGlobalLight.enabled = false;
        if (cctvVolume != null) cctvVolume.weight = 0f;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (allCameras.Count > 0 && currentCameraIndex < allCameras.Count && cam == allCameras[currentCameraIndex])
        {
            if (isCCTVNightVisionOn)
            {
                if (cctvVolume != null) cctvVolume.weight = 1f;
                if (cctvGlobalLight != null) cctvGlobalLight.enabled = true;
                if (cctvNightVisionLight != null)
                {
                    cctvNightVisionLight.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
                    cctvNightVisionLight.enabled = true;
                }
                cam.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            }
        }
    }

    private void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
    {
        if (allCameras.Count > 0 && currentCameraIndex < allCameras.Count && cam == allCameras[currentCameraIndex])
        {
            if (cctvVolume != null) cctvVolume.weight = 0f;
            if (cctvGlobalLight != null) cctvGlobalLight.enabled = false;
            if (cctvNightVisionLight != null) cctvNightVisionLight.enabled = false;
            cam.backgroundColor = Color.black;
        }
    }

    public void RegisterDynamicCamera(Camera cam)
    {
        if (cam != null && !allCameras.Contains(cam))
        {
            allCameras.Add(cam);
            var camData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null) camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.volumeLayerMask = ~0;
            UpdateCameraSource();
            UpdateNoSignalState();
        }
    }

    public void UnregisterDynamicCamera(Camera cam)
    {
        if (cam != null && allCameras.Contains(cam))
        {
            allCameras.Remove(cam);
            if (currentCameraIndex >= allCameras.Count) currentCameraIndex = 0;
            UpdateCameraSource();
            UpdateNoSignalState();
        }
    }

    private void UpdateNoSignalState()
    {
        if (noSignalLabel == null || cctvContainer == null) return;
        
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
        cctvNvBadge = root.Q<Label>("cctv-nv-badge");
        cctvTimestamp = root.Q<Label>("cctv-timestamp");
        btnToggleNv = root.Q<Button>("btn-toggle-nv");
        cctvNvColorFilter = root.Q<VisualElement>("cctv-nv-color-filter");
        
        // Auto-create a RenderTexture if one isn't assigned in the Inspector
        if (cctvRenderTexture == null)
        {
            cctvRenderTexture = new RenderTexture(640, 480, 16);
            cctvRenderTexture.name = "CCTV_AutoRT";
        }

        // Enable post-processing on all cameras so volume effects apply
        for (int i = 0; i < allCameras.Count; i++)
        {
            if (allCameras[i] != null)
            {
                var camData = allCameras[i].GetComponent<UniversalAdditionalCameraData>();
                if (camData == null) camData = allCameras[i].gameObject.AddComponent<UniversalAdditionalCameraData>();
                camData.renderPostProcessing = true;
                camData.volumeLayerMask = ~0;
            }
        }
        
        root.Q<Button>("btn-next-camera").clicked += SwitchToNextCamera;
        
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
        
        UpdateCameraSource();
        UpdateNoSignalState();
        ApplyNightVisionState();
        
        // Hide on start
        root.style.display = DisplayStyle.None;
        isDashboardOpen = false;
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
    }

    private void SwitchToNextCamera()
    {
        if (allCameras.Count == 0) return;
        
        currentCameraIndex = (currentCameraIndex + 1) % allCameras.Count;
        UpdateCameraSource();
    }

    private void UpdateCameraSource()
    {
        if (allCameras.Count == 0) return;

        for (int i = 0; i < allCameras.Count; i++)
        {
            if (allCameras[i] != null)
            {
                allCameras[i].targetTexture = (i == currentCameraIndex) ? cctvRenderTexture : null;
                allCameras[i].gameObject.SetActive(i == currentCameraIndex);
            }
        }

        if (currentCameraIndex < allCameras.Count && allCameras[currentCameraIndex] != null)
        {
            string camName = allCameras[currentCameraIndex].name.ToUpper();
            string formattedTitle = $"CAM 0{currentCameraIndex + 1}: {camName}";
            if (cctvScreenTitle != null) cctvScreenTitle.text = formattedTitle;
            if (cctvCamIdLabel != null) cctvCamIdLabel.text = formattedTitle;
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

        // 3. Update HUD Badge
        if (cctvNvBadge != null)
        {
            if (isCCTVNightVisionOn)
            {
                cctvNvBadge.text = "● IR NIGHT VISION: ON (B&W)";
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

        // 4. Update camera feeds (both handheld cameras and house cameras)
        if (allCameras.Count > 0 && currentCameraIndex < allCameras.Count)
        {
            Camera currentCam = allCameras[currentCameraIndex];
            if (currentCam != null)
            {
                var camData = currentCam.GetComponent<UniversalAdditionalCameraData>();
                if (camData == null) camData = currentCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                camData.renderPostProcessing = true;
                camData.volumeLayerMask = ~0;

                // If this is a dynamic VideoCameraItem, notify it
                VideoCameraItem videoCam = currentCam.GetComponentInParent<VideoCameraItem>();
                if (videoCam != null)
                {
                    videoCam.SetCCTVNightVision(isCCTVNightVisionOn);
                }

                // Ensure Ghost Orbs layer is culled during night vision on ALL cameras
                int ghostOrbLayer = LayerMask.NameToLayer("GhostOrbs");
                if (ghostOrbLayer != -1)
                {
                    if (isCCTVNightVisionOn)
                        currentCam.cullingMask |= (1 << ghostOrbLayer);
                    else
                        currentCam.cullingMask &= ~(1 << ghostOrbLayer);
                }
            }
        }
    }

    public void Interact()
    {
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
        }
        else
        {
            closedFrame = Time.frameCount;
            uiDocument.rootVisualElement.style.display = DisplayStyle.None;
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

            // CCTV session timer
            if (cctvTimer != null)
            {
                int totalSec = Mathf.FloorToInt(Time.timeSinceLevelLoad);
                int h = totalSec / 3600;
                int m = (totalSec % 3600) / 60;
                int s = totalSec % 60;
                cctvTimer.text = $"{h:00}:{m:00}:{s:00}";
            }

            // Live timestamp
            if (cctvTimestamp != null)
            {
                cctvTimestamp.text = System.DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
            }
        }

        updateTimer += Time.deltaTime;
        if (updateTimer >= 0.5f) // Update stats every 0.5 seconds
        {
            updateTimer = 0f;
            UpdateSanityMonitor();
            UpdateActivityMonitor();
        }
    }

    private void UpdateSanityMonitor()
    {
        sanityList.Clear();

        foreach (var player in PlayerController.AllPlayers)
        {
            if (player == null) continue;

            var row = new VisualElement();
            row.AddToClassList("sanity-row");

            var nameLabel = new Label($"Player {player.OwnerClientId}");
            nameLabel.AddToClassList("sanity-name");

            var sanityValueLabel = new Label($"{Mathf.RoundToInt(player.Sanity)}%");
            sanityValueLabel.AddToClassList("sanity-value");

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
