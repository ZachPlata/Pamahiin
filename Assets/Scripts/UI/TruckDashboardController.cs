using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

[RequireComponent(typeof(UIDocument))]
public class TruckDashboardController : MonoBehaviour, IInteractable
{
    [Header("CCTV Settings")]
    [SerializeField] private RenderTexture cctvRenderTexture;
    [SerializeField] private Camera[] houseCameras;
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
    private ScrollView sanityList;
    private VisualElement activityBar;
    private Label activityLevelText;

    private float updateTimer = 0f;
    private bool isDashboardOpen = false;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
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

        // CCTV
        cctvContainer = root.Q<VisualElement>("cctv-image-container");
        if (cctvRenderTexture != null)
        {
            cctvContainer.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(cctvRenderTexture));
            root.Q<Label>(className: "no-signal-text").style.display = DisplayStyle.None;
        }
        
        root.Q<Button>("btn-next-camera").clicked += SwitchToNextCamera;
        
        root.Q<Button>("btn-close").clicked += ToggleDashboard;

        // Sanity & Activity
        sanityList = root.Q<ScrollView>("sanity-list-container");
        activityBar = root.Q<VisualElement>("activity-bar");
        activityLevelText = root.Q<Label>("lbl-activity-level");

        // Default view
        ShowView(viewCctv, tabCctv);
        
        UpdateCameraSource();
        
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
        if (houseCameras == null || houseCameras.Length == 0) return;
        
        currentCameraIndex = (currentCameraIndex + 1) % houseCameras.Length;
        UpdateCameraSource();
    }

    private void UpdateCameraSource()
    {
        if (houseCameras == null || houseCameras.Length == 0) return;

        for (int i = 0; i < houseCameras.Length; i++)
        {
            if (houseCameras[i] != null)
            {
                houseCameras[i].targetTexture = (i == currentCameraIndex) ? cctvRenderTexture : null;
                houseCameras[i].gameObject.SetActive(i == currentCameraIndex);
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
            ToggleDashboard();
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
