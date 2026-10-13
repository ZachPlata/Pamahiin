using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// In-game pause menu with three panels:
///   1. Main  — Resume / Settings / Players / Quit
///   2. Settings — Audio sliders, graphics toggles, FPS counter
///   3. Players  — Connected player list with host-kick / vote-kick
///
/// Works in both singleplayer (host-only mode) and multiplayer.
/// ESC toggles the pause overlay; the HUD "pause" button also works.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PauseMenuController : MonoBehaviour
{
    private UIDocument uiDocument;

    // Panels
    private VisualElement pauseOverlay;
    private VisualElement panelMain;
    private VisualElement panelSettings;
    private VisualElement panelPlayers;

    // Settings controls
    private Slider sliderMaster, sliderMusic, sliderSFX, sliderVoice;
    private Label  lblMaster, lblMusic, lblSFX, lblVoice;
    private Toggle toggleFullscreen, toggleFPS;
    private Toggle toggleTutorialHints;

    // Players panel
    private ScrollView playerListScroll;

    // FPS tracking
    private Label fpsLabel;
    private float fpsTimer;
    private int   fpsFrameCount;

    private bool isPaused = false;
    private int closedFrame = -1;

    public static PauseMenuController Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.isPaused;
    public static bool WasOpenThisFrame => IsOpen || (Instance != null && Instance.closedFrame == Time.frameCount);
    public bool IsPaused => isPaused;

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
        panelPlayers  = root.Q<VisualElement>("panel-players");

        // ── Main panel buttons ─────────────────────────────────────────
        root.Q<Button>("btn-resume")  .clicked += TogglePause;
        root.Q<Button>("btn-settings").clicked += ShowSettings;
        root.Q<Button>("btn-players") .clicked += ShowPlayers;
        root.Q<Button>("btn-quit")    .clicked += OnQuitClicked;

        // ── Settings panel ─────────────────────────────────────────────
        sliderMaster  = root.Q<Slider>("slider-master");
        sliderMusic   = root.Q<Slider>("slider-music");
        sliderSFX     = root.Q<Slider>("slider-sfx");
        sliderVoice   = root.Q<Slider>("slider-voice");

        lblMaster  = root.Q<Label>("lbl-master");
        lblMusic   = root.Q<Label>("lbl-music");
        lblSFX     = root.Q<Label>("lbl-sfx");
        lblVoice   = root.Q<Label>("lbl-voice");

        toggleFullscreen = root.Q<Toggle>("toggle-fullscreen");
        toggleFPS        = root.Q<Toggle>("toggle-fps");
        toggleTutorialHints  = root.Q<Toggle>("toggle-tutorial-hints");

        // Wire sliders → GameSettingsManager
        sliderMaster .RegisterValueChangedCallback(e => { GameSettingsManager.Instance?.SetMasterVolume(e.newValue); UpdateVolLabel(lblMaster,  e.newValue); });
        sliderMusic  .RegisterValueChangedCallback(e => { GameSettingsManager.Instance?.SetMusicVolume(e.newValue);  UpdateVolLabel(lblMusic,   e.newValue); });
        sliderSFX    .RegisterValueChangedCallback(e => { GameSettingsManager.Instance?.SetSFXVolume(e.newValue);    UpdateVolLabel(lblSFX,     e.newValue); });
        sliderVoice  .RegisterValueChangedCallback(e => { GameSettingsManager.Instance?.SetVoiceVolume(e.newValue);  UpdateVolLabel(lblVoice,   e.newValue); });

        toggleFullscreen.RegisterValueChangedCallback(e => GameSettingsManager.Instance?.SetFullscreen(e.newValue));
        toggleFPS       .RegisterValueChangedCallback(e => OnToggleFPS(e.newValue));
        if (toggleTutorialHints != null)
            toggleTutorialHints.RegisterValueChangedCallback(e => GameSettingsManager.Instance?.SetTutorialHintsEnabled(e.newValue));

        root.Q<Button>("btn-settings-back").clicked += ShowMain;

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
            if (DeveloperConsoleManager.WasOpenThisFrame) return;
            if (TruckDashboardController.WasOpenThisFrame) return;
            if (JournalController.WasOpenThisFrame) return;
            if (SpiritBoxDialogueUI.WasOpenThisFrame) return;
            TogglePause();
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

    public void TogglePause()
    {
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

    private void ShowMain()
    {
        SetPanel(panelMain);
    }

    private void ShowSettings()
    {
        SyncSettingsFromManager();
        SetPanel(panelSettings);
    }

    private void ShowPlayers()
    {
        RefreshPlayerList();
        SetPanel(panelPlayers);
    }

    private void SetPanel(VisualElement active)
    {
        panelMain    .AddToClassList("hidden");
        panelSettings.AddToClassList("hidden");
        panelPlayers .AddToClassList("hidden");
        active.RemoveFromClassList("hidden");
    }

    // ── Settings Helpers ──────────────────────────────────────────────────

    private void SyncSettingsFromManager()
    {
        var s = GameSettingsManager.Instance;
        if (s == null) return;

        sliderMaster .value = s.MasterVolume;
        sliderMusic  .value = s.MusicVolume;
        sliderSFX    .value = s.SFXVolume;
        sliderVoice  .value = s.VoiceVolume;

        toggleFullscreen.value = s.IsFullscreen;
        toggleFPS       .value = s.ShowFPS;
        if (toggleTutorialHints != null)  toggleTutorialHints.value = s.TutorialHintsEnabled;

        UpdateVolLabel(lblMaster,  s.MasterVolume);
        UpdateVolLabel(lblMusic,   s.MusicVolume);
        UpdateVolLabel(lblSFX,     s.SFXVolume);
        UpdateVolLabel(lblVoice,   s.VoiceVolume);
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
        GameSettingsManager.Instance?.SaveAll();

        if (NetworkController.Instance != null)
            NetworkController.Instance.DisconnectAndReturnToMenu();
        else if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenuScene");
        }
    }
}
