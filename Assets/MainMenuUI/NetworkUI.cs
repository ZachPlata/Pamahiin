using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class NetworkUI : MonoBehaviour
{
    private UIDocument uiDocument;
    
    // Views
    private VisualElement mainMenuView, hostView, joinView, helpView;
    
    // Buttons
    private Button startBtn, joinMenuBtn, helpBtn;
    private Button hostBackBtn, launchGameBtn;
    private Button joinBackBtn, discoveredServerBtn;
    private Button helpBackBtn;

    private Label titleLabel;
    private float timeElapsed;

    void Start()
    {
        uiDocument = GetComponent<UIDocument>();
        VisualElement root = uiDocument.rootVisualElement;

        // Cache Views
        mainMenuView = root.Q<VisualElement>("main-menu-view");
        hostView = root.Q<VisualElement>("host-view");
        joinView = root.Q<VisualElement>("join-view");
        helpView = root.Q<VisualElement>("help-view");

        // Cache Buttons
        startBtn = root.Q<Button>("start-btn");
        joinMenuBtn = root.Q<Button>("join-menu-btn");
        helpBtn = root.Q<Button>("help-btn");

        hostBackBtn = root.Q<Button>("host-back-btn");
        launchGameBtn = root.Q<Button>("launch-game-btn");

        joinBackBtn = root.Q<Button>("join-back-btn");
        discoveredServerBtn = root.Q<Button>("discovered-server-btn");

        helpBackBtn = root.Q<Button>("help-back-btn");
        titleLabel = root.Q<Label>("title");

        // Register Listeners
        if (startBtn != null) startBtn.clicked += OnHostClicked;
        if (joinMenuBtn != null) joinMenuBtn.clicked += OnJoinMenuClicked;
        if (helpBtn != null) helpBtn.clicked += OnHelpClicked;

        if (hostBackBtn != null) hostBackBtn.clicked += ShowMainMenu;
        if (launchGameBtn != null) launchGameBtn.clicked += StartInvestigation;

        if (joinBackBtn != null) joinBackBtn.clicked += ShowMainMenu;
        if (discoveredServerBtn != null) discoveredServerBtn.clicked += StartClientGame;

        if (helpBackBtn != null) helpBackBtn.clicked += ShowMainMenu;

        ShowMainMenu();
    }

    void OnDisable()
    {
        if (startBtn != null) startBtn.clicked -= OnHostClicked;
        if (joinMenuBtn != null) joinMenuBtn.clicked -= OnJoinMenuClicked;
        if (helpBtn != null) helpBtn.clicked -= OnHelpClicked;
        if (launchGameBtn != null) launchGameBtn.clicked -= StartInvestigation;
    }

    void Update()
    {
        timeElapsed += Time.deltaTime;

        // Title flicker effect
        if (titleLabel != null && mainMenuView != null && !mainMenuView.ClassListContains("hidden"))
        {
            float flicker = 1f;
            float wave = timeElapsed % 6f;
            if (wave > 4.5f && wave < 4.7f) flicker = 0.4f;
            else if (wave > 5.1f && wave < 5.25f) flicker = 0.7f;
            titleLabel.style.color = new Color(230f / 255f, 50f / 255f, 60f / 255f, flicker);
        }
    }

    private void HideAllViews()
    {
        if (mainMenuView != null) mainMenuView.AddToClassList("hidden");
        if (hostView != null) hostView.AddToClassList("hidden");
        if (joinView != null) joinView.AddToClassList("hidden");
        if (helpView != null) helpView.AddToClassList("hidden");
    }

    private void ShowMainMenu()
    {
        HideAllViews();
        if (mainMenuView != null) mainMenuView.RemoveFromClassList("hidden");
    }

    private void OnHostClicked()
    {
        HideAllViews();
        if (hostView != null) hostView.RemoveFromClassList("hidden");
    }

    private void OnJoinMenuClicked()
    {
        HideAllViews();
        if (joinView != null) joinView.RemoveFromClassList("hidden");

        bool serverFound = NetworkManager.Singleton != null;
        if (discoveredServerBtn != null)
        {
            discoveredServerBtn.style.display = DisplayStyle.Flex;
            discoveredServerBtn.text = serverFound ? "LOCAL LOBBY [READY]" : "SEARCHING...";
        }
    }

    private void OnHelpClicked()
    {
        HideAllViews();
        if (helpView != null) helpView.RemoveFromClassList("hidden");
    }

    private void StartInvestigation()
    {
        // Ensure a NetworkManager exists in the scene before starting
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[NetworkUI] No NetworkManager found in the scene! Please create one.");
            return;
        }

        if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.StartHost();
        }

        // Force hide the entire UI container directly from the root element
        if (uiDocument != null && uiDocument.rootVisualElement != null)
        {
            uiDocument.rootVisualElement.style.display = DisplayStyle.None;
        }
    }

    private void StartClientGame()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[NetworkUI] No NetworkManager found in the scene! Please create one.");
            return;
        }

        NetworkManager.Singleton.StartClient();

        if (uiDocument != null && uiDocument.rootVisualElement != null)
        {
            uiDocument.rootVisualElement.style.display = DisplayStyle.None;
        }
    }
}