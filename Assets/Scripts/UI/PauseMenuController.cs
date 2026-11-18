using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// In-game pause menu with four panels:
///   1. Main            — Resume / Settings / Help &amp; Controls / Players / Quit
///   2. Settings        — Audio sliders, graphics toggles, FPS counter, tutorial hints
///   3. Help &amp; Controls — Keybind reference and quick investigation tips
///   4. Players         — Connected player list with host-kick / vote-kick
///
/// Works in both singleplayer (host-only mode) and multiplayer.
/// ESC toggles the pause overlay (or steps back from sub-panels); the HUD "pause" button also works.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PauseMenuController : MonoBehaviour
{
    private UIDocument uiDocument;

    // Panels
    private VisualElement pauseOverlay;
    private VisualElement panelMain;
    private VisualElement panelSettings;
    private VisualElement panelControls;
    private VisualElement panelPlayers;

    // Main panel tutorial buttons
    private Button btnRestartTutorial;
    private Button btnSkipTutorial;

    // Host Disconnect modal
    private VisualElement hostDisconnectOverlay;
    private Label lblHostDisconnectMsg;
    private Button btnHostDisconnectMenu;

    // Settings controls
    private Slider sliderMaster, sliderMusic, sliderSFX;
    private Label  lblMaster, lblMusic, lblSFX;
    private Toggle toggleFullscreen, toggleFPS;
    private Toggle toggleTutorialHints;
    private Toggle toggleReachIndicator;

    // Players panel
    private ScrollView playerListScroll;

    // FPS tracking
    private Label fpsLabel;
    private float fpsTimer;
    private int   fpsFrameCount;

    private bool isPaused = false;
    private bool isHostDisconnectVisible = false;
    private int closedFrame = -1;

    public static PauseMenuController Instance { get; private set; }
    public static bool IsOpen => Instance != null && (Instance.isPaused || Instance.isHostDisconnectVisible);
    public static bool WasOpenThisFrame => IsOpen || (Instance != null && Instance.closedFrame == Time.frameCount);
    public bool IsPaused => isPaused;
    public bool IsHostDisconnectVisible => isHostDisconnectVisible;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        uiDocument = GetComponent<UIDocument>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable()
    {
        uiDocument.sortingOrder = 6;
        var root = uiDocument.rootVisualElement;

        // ── Panels ─────────────────────────────────────────────────────
        pauseOverlay  = root.Q<VisualElement>("pause-overlay");
        panelMain     = root.Q<VisualElement>("panel-main");
        panelSettings = root.Q<VisualElement>("panel-settings");
        panelControls = root.Q<VisualElement>("panel-controls");
        panelPlayers  = root.Q<VisualElement>("panel-players");
        BindHostDisconnectModal(root);

        // ── Main panel buttons ─────────────────────────────────────────
        root.Q<Button>("btn-resume")  .clicked += TogglePause;
        btnRestartTutorial = root.Q<Button>("btn-restart-tutorial");
        if (btnRestartTutorial != null) btnRestartTutorial.clicked += OnRestartTutorialClicked;
        btnSkipTutorial = root.Q<Button>("btn-skip-tutorial");
        if (btnSkipTutorial != null) btnSkipTutorial.clicked += OnSkipTutorialClicked;
        root.Q<Button>("btn-settings").clicked += ShowSettings;
        var btnControls = root.Q<Button>("btn-controls");
        if (btnControls != null) btnControls.clicked += ShowControls;
        root.Q<Button>("btn-players") .clicked += ShowPlayers;
        root.Q<Button>("btn-quit")    .clicked += OnQuitClicked;

        // ── Settings panel ─────────────────────────────────────────────
        sliderMaster  = root.Q<Slider>("slider-master");
        sliderMusic   = root.Q<Slider>("slider-music");
        sliderSFX     = root.Q<Slider>("slider-sfx");

        lblMaster  = root.Q<Label>("lbl-master");
        lblMusic   = root.Q<Label>("lbl-music");
        lblSFX     = root.Q<Label>("lbl-sfx");

        toggleFullscreen = root.Q<Toggle>("toggle-fullscreen");
        toggleFPS        = root.Q<Toggle>("toggle-fps");
        toggleTutorialHints  = root.Q<Toggle>("toggle-tutorial-hints");
        toggleReachIndicator = root.Q<Toggle>("toggle-reach-indicator");

        // Wire sliders → GameSettingsManager
        sliderMaster .RegisterValueChangedCallback(e => { GameSettingsManager.Instance?.SetMasterVolume(e.newValue); UpdateVolLabel(lblMaster,  e.newValue); });
        sliderMusic  .RegisterValueChangedCallback(e => { GameSettingsManager.Instance?.SetMusicVolume(e.newValue);  UpdateVolLabel(lblMusic,   e.newValue); });
        sliderSFX    .RegisterValueChangedCallback(e => { GameSettingsManager.Instance?.SetSFXVolume(e.newValue);    UpdateVolLabel(lblSFX,     e.newValue); });

        toggleFullscreen.RegisterValueChangedCallback(e => GameSettingsManager.Instance?.SetFullscreen(e.newValue));
        toggleFPS       .RegisterValueChangedCallback(e => OnToggleFPS(e.newValue));
        if (toggleTutorialHints != null)
            toggleTutorialHints.RegisterValueChangedCallback(e => GameSettingsManager.Instance?.SetTutorialHintsEnabled(e.newValue));
        if (toggleReachIndicator != null)
            toggleReachIndicator.RegisterValueChangedCallback(e => GameSettingsManager.Instance?.SetReachIndicatorEnabled(e.newValue));

        root.Q<Button>("btn-settings-back").clicked += ShowMain;

        // ── Help & Controls panel ──────────────────────────────────────
        var btnControlsBack = root.Q<Button>("btn-controls-back");
        if (btnControlsBack != null) btnControlsBack.clicked += ShowMain;

        // ── Players panel ──────────────────────────────────────────────
        playerListScroll = root.Q<ScrollView>("player-list-scroll");
        root.Q<Button>("btn-players-back").clicked += ShowMain;

        // Subscribe to vote-kick state changes
        if (VoteKickManager.Instance != null)
            VoteKickManager.Instance.OnVoteStateChanged += RefreshPlayerList;

        // ── FPS label (injected at runtime into root) ──────────────────
        fpsLabel = new Label { name = "fps-display" };
        fpsLabel.AddToClassList("fps-label");
        fpsLabel.style.display = DisplayStyle.None;
        root.Add(fpsLabel);

        // Seed settings values from GameSettingsManager
        SyncSettingsFromManager();

        ShowMain();
    }

    private void OnDisable()
    {
        isHostDisconnectVisible = false;
        if (btnHostDisconnectMenu != null)
        {
            btnHostDisconnectMenu.clicked -= OnQuitClicked;
        }
        if (VoteKickManager.Instance != null)
            VoteKickManager.Instance.OnVoteStateChanged -= RefreshPlayerList;

        GameSettingsManager.Instance?.SaveAll();
    }

    private void Update()
    {
        // Don't capture ESC on the main menu
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenuScene") return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HandleEscapePressed();
        }

        // FPS counter
        if (fpsLabel != null && fpsLabel.style.display == DisplayStyle.Flex)
        {
            fpsFrameCount++;
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 0.5f)
            {
                int fps = Mathf.RoundToInt(fpsFrameCount / fpsTimer);
                fpsLabel.text = $"{fps} FPS";
                fpsLabel.style.color = fps >= 60
                    ? new StyleColor(new Color(0.7f, 1f, 0.7f))
                    : fps >= 30
                        ? new StyleColor(new Color(1f, 0.85f, 0.4f))
                        : new StyleColor(new Color(1f, 0.4f, 0.4f));
                fpsTimer = 0f;
                fpsFrameCount = 0;
            }
        }
    }

    // ── Panel Navigation ──────────────────────────────────────────────────

    private void HandleEscapePressed()
    {
        if (isHostDisconnectVisible) return;
        if (DeveloperConsoleManager.WasOpenThisFrame) return;
        if (TruckDashboardController.WasOpenThisFrame) return;
        if (JournalController.WasOpenThisFrame) return;
        if (SpiritBoxDialogueUI.WasOpenThisFrame) return;
        if (ExorcismManager.WasOpenThisFrame) return;

        if (isPaused && panelMain != null && panelMain.ClassListContains("hidden"))
        {
            ShowMain();
            return;
        }

        TogglePause();
    }

    public void TogglePause()
    {
        if (isHostDisconnectVisible) return;
        isPaused = !isPaused;
        if (isPaused)
        {
            pauseOverlay.RemoveFromClassList("hidden");
            ShowMain();
        }
        else
        {
            closedFrame = Time.frameCount;
            pauseOverlay.AddToClassList("hidden");
            GameSettingsManager.Instance?.SaveAll();
        }
    }

    private void BindHostDisconnectModal(VisualElement root)
    {
        if (root == null) return;
        hostDisconnectOverlay = root.Q<VisualElement>("host-disconnect-overlay");
        lblHostDisconnectMsg = root.Q<Label>("lbl-host-disconnect-msg");
        btnHostDisconnectMenu = root.Q<Button>("btn-host-disconnect-menu");
        if (btnHostDisconnectMenu != null)
        {
            btnHostDisconnectMenu.clicked -= OnQuitClicked;
            btnHostDisconnectMenu.clicked += OnQuitClicked;
        }
        if (!isHostDisconnectVisible)
        {
            hostDisconnectOverlay?.AddToClassList("hidden");
        }
    }

    public void ShowHostDisconnected(string message = null)
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (hostDisconnectOverlay == null && uiDocument != null && uiDocument.rootVisualElement != null)
        {
            BindHostDisconnectModal(uiDocument.rootVisualElement);
        }

        isHostDisconnectVisible = true;
        isPaused = false;
        pauseOverlay?.AddToClassList("hidden");

        if (JournalController.IsOpen) JournalController.Instance?.ToggleJournal();
        if (TruckDashboardController.IsOpen) TruckDashboardController.Instance?.CloseDashboard();
        if (SpiritBoxDialogueUI.Instance != null && SpiritBoxDialogueUI.Instance.IsOpen)
        {
            SpiritBoxDialogueUI.Instance.CloseDialogue();
        }
        LoadingScreenManager.Instance?.HideIntermission(0.1f);

        if (uiDocument != null)
        {
            uiDocument.sortingOrder = 60;
        }

        if (!string.IsNullOrEmpty(message) && lblHostDisconnectMsg != null)
        {
            lblHostDisconnectMsg.text = message;
        }

        if (hostDisconnectOverlay != null)
        {
            hostDisconnectOverlay.RemoveFromClassList("hidden");
            hostDisconnectOverlay.style.display = DisplayStyle.Flex;
        }

        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;
    }

    private void ShowMain()
    {
        bool inTutorial = ProgressionAndLoadoutManager.IsInWarehouseTutorialScene;
        if (btnRestartTutorial != null)
        {
            if (inTutorial) btnRestartTutorial.RemoveFromClassList("hidden");
            else btnRestartTutorial.AddToClassList("hidden");
        }
        if (btnSkipTutorial != null)
        {
            if (inTutorial) btnSkipTutorial.RemoveFromClassList("hidden");
            else btnSkipTutorial.AddToClassList("hidden");
        }
        SetPanel(panelMain);
    }

    private void OnRestartTutorialClicked()
    {
        if (isPaused) TogglePause();
        if (NetworkController.Instance != null)
        {
            NetworkController.Instance.EnterWarehouseTutorial();
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(ProgressionAndLoadoutManager.WAREHOUSE_TUTORIAL_SCENE_NAME);
        }
    }

    private void OnSkipTutorialClicked()
    {
        if (isPaused) TogglePause();
        ProgressionAndLoadoutManager.MarkTutorialSkipped();
        if (WarehouseTutorialManager.Instance != null)
        {
            WarehouseTutorialManager.Instance.SkipTutorial();
        }
        else
        {
            OnQuitClicked();
        }
    }

    private void ShowSettings()
    {
        SyncSettingsFromManager();
        SetPanel(panelSettings);
    }

    private void ShowControls()
    {
        SetPanel(panelControls);
    }

    private void ShowPlayers()
    {
        RefreshPlayerList();
        SetPanel(panelPlayers);
    }

    private void SetPanel(VisualElement active)
    {
        panelMain    ?.AddToClassList("hidden");
        panelSettings?.AddToClassList("hidden");
        panelControls?.AddToClassList("hidden");
        panelPlayers ?.AddToClassList("hidden");
        active       ?.RemoveFromClassList("hidden");
    }

    // ── Settings Helpers ──────────────────────────────────────────────────

    private void SyncSettingsFromManager()
    {
        var s = GameSettingsManager.Instance;
        if (s == null) return;

        sliderMaster .value = s.MasterVolume;
        sliderMusic  .value = s.MusicVolume;
        sliderSFX    .value = s.SFXVolume;

        toggleFullscreen.value = s.IsFullscreen;
        toggleFPS       .value = s.ShowFPS;
        if (toggleTutorialHints != null)  toggleTutorialHints.value = s.TutorialHintsEnabled;
        if (toggleReachIndicator != null) toggleReachIndicator.value = s.ReachIndicatorEnabled;

        UpdateVolLabel(lblMaster,  s.MasterVolume);
        UpdateVolLabel(lblMusic,   s.MusicVolume);
        UpdateVolLabel(lblSFX,     s.SFXVolume);
        OnToggleFPS(s.ShowFPS);
    }

    private static void UpdateVolLabel(Label label, float linear)
    {
        if (label != null)
            label.text = $"{Mathf.RoundToInt(linear * 100f)}%";
    }

    private void OnToggleFPS(bool value)
    {
        GameSettingsManager.Instance?.SetShowFPS(value);
        if (fpsLabel != null)
            fpsLabel.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ── Player List ───────────────────────────────────────────────────────

    private void RefreshPlayerList()
    {
        if (playerListScroll == null) return;
        playerListScroll.Clear();

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            // Singleplayer — just show local player, no kick options
            var row = BuildPlayerRow(0, isSelf: true, isHost: true, isMPHost: false);
            playerListScroll.Add(row);
            return;
        }

        bool iAmHost = NetworkManager.Singleton.IsServer;
        ulong localId = NetworkManager.Singleton.LocalClientId;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            bool isSelf      = clientId == localId;
            bool isHostEntry = clientId == NetworkManager.ServerClientId;

            var row = BuildPlayerRow(clientId, isSelf, isHostEntry, isMPHost: iAmHost);
            playerListScroll.Add(row);
        }
    }

    private VisualElement BuildPlayerRow(ulong clientId, bool isSelf, bool isHost, bool isMPHost)
    {
        var row = new VisualElement();
        row.AddToClassList("player-row");

        // Name
        var nameLabel = new Label($"Player {clientId}");
        nameLabel.AddToClassList("player-name");
        row.Add(nameLabel);

        // HOST badge
        if (isHost)
        {
            var badge = new Label("HOST");
            badge.AddToClassList("player-badge");
            row.Add(badge);
        }

        // Vote count label
        if (!isHost && !isSelf && VoteKickManager.Instance != null)
        {
            int votes  = VoteKickManager.Instance.GetVoteCount(clientId);
            int needed = VoteKickManager.Instance.GetEligiblePlayerCount();
            var voteInfo = new Label($"({votes}/{needed})");
            voteInfo.AddToClassList("slider-value");
            row.Add(voteInfo);
        }

        // Kick buttons — don't show for self or host-entry
        if (!isHost && !isSelf)
        {
            ulong capturedId = clientId; // capture for lambda

            if (isMPHost)
            {
                // Host gets direct kick
                var kickBtn = new Button(() => VoteKickManager.Instance?.HostKick(capturedId))
                    { text = "Kick" };
                kickBtn.AddToClassList("btn-kick");
                row.Add(kickBtn);
            }
            else
            {
                // Non-host clients get vote-kick
                var voteBtn = new Button(() => VoteKickManager.Instance?.RequestVoteKickServerRpc(capturedId))
                    { text = "Vote Kick" };
                voteBtn.AddToClassList("btn-vote");
                row.Add(voteBtn);
            }
        }

        return row;
    }

    // ── Quit ──────────────────────────────────────────────────────────────

    private void OnQuitClicked()
    {
        isHostDisconnectVisible = false;
        GameSettingsManager.Instance?.SaveAll();

        if (NetworkController.Instance != null)
            NetworkController.Instance.DisconnectAndReturnToMenu();
        else if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
        }
    }
}
